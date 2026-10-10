using System.Globalization;

namespace Etp.Reporting.Application.Accounting;

/// <summary>A prepared, unsaved set of Tally Sales vouchers for one store and business day, with the validation findings
/// of plan task 7. A voucher with a FAIL finding is already BLOCKED in <see cref="Plan"/>.</summary>
public sealed record SalesVoucherPreview(
    long ReportGenerationId,
    TallyProfile Profile,
    string StoreCode,
    DateOnly BusinessDate,
    SalesVoucherPlan Plan,
    IReadOnlyList<ValidationFinding> Findings);

/// <summary>A validation finding as saved with a batch; a WARN can be accepted once with a reason.</summary>
public sealed record SavedValidationFinding(
    long Id,
    int? VoucherSequence,
    string RuleId,
    string Severity,
    string? Subject,
    string Explanation,
    string? CorrectiveAction,
    string? WaivedBy,
    string? WaiverReason);

/// <summary>Plan tasks 6 and 7 for the Owner: prepare one day as Sales vouchers, save it, and accept its warnings.</summary>
public interface ITallySalesBatchService
{
    Task<SalesVoucherPreview> PreviewAsync(int tallyProfileId, string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default);

    /// <summary>Saves the preview with its findings after preparing it again; refused when anything changed. Returns the batch id.</summary>
    Task<long> SaveAsync(SalesVoucherPreview preview, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedValidationFinding>> LoadFindingsAsync(long batchId, CancellationToken cancellationToken = default);

    /// <summary>Accepts one warning with a reason. Failures cannot be accepted.</summary>
    Task AcceptWarningAsync(long findingId, string reason, CancellationToken cancellationToken = default);
}

/// <summary>One version of an approved ledger mapping, as the Tally ledgers screen lists it.</summary>
public sealed record TallyLedgerMappingRow(
    string BusinessEvent,
    string? StoreCode,
    string DebitLedger,
    string CreditLedger,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int Version,
    bool IsActive,
    string ChangedBy,
    DateTime ChangedUtc);

/// <summary>Plan task 13 for the Owner: the ledger names Tally vouchers use, per business event and store, versioned.</summary>
public interface ITallyLedgerMappingService
{
    Task<IReadOnlyList<TallyLedgerMappingRow>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>The business events a store's invoices can need: one per active payment mode, round-off, sales revenue and
    /// one per GST component and rate seen in the store's GST detail imports.</summary>
    Task<IReadOnlyList<string>> LoadNeededEventsAsync(string storeCode, CancellationToken cancellationToken = default);

    /// <summary>Approves a new mapping version for one event and store from a date, with a reason. Earlier versions stay listed.</summary>
    Task SaveAsync(string storeCode, string businessEvent, string ledgerName, DateOnly effectiveFrom, string reason, CancellationToken cancellationToken = default);
}

/// <summary>Runs the plan task 7 rules over the vouchers the composer planned. A voucher with a FAIL finding becomes BLOCKED
/// with <c>VALIDATION_FAILED</c>, which stops the day like any other fault; a WARN leaves it planned but must be accepted
/// before approval. Vouchers the composer already blocked are not judged again.</summary>
public static class TallySalesVoucherValidation
{
    public static (SalesVoucherPlan Plan, IReadOnlyList<ValidationFinding> Findings) Apply(
        SalesVoucherPlan plan, IReadOnlyList<InvoiceAccountingSource> invoices, TallyProfile profile, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(invoices);
        ArgumentNullException.ThrowIfNull(profile);
        var bySequence = new Dictionary<int, InvoiceAccountingSource>();
        foreach (var voucher in plan.Vouchers)
            if (invoices.FirstOrDefault(invoice => invoice.SalesInvoiceId == voucher.SalesInvoiceId && invoice.DocumentNumber == voucher.DocumentNumber) is { } invoice)
                bySequence[voucher.Sequence] = invoice;

        var planned = plan.Vouchers.Where(voucher => voucher.Status == TallyVoucherStatus.Planned && bySequence.ContainsKey(voucher.Sequence))
            .Select(voucher => ToValidation(voucher, bySequence[voucher.Sequence])).ToArray();
        var findings = AccountingValidationRules.Evaluate(new ValidationContext(profile, today, HasFinalGeneration: true), planned);

        var vouchers = plan.Vouchers.Select(voucher =>
            voucher.Status == TallyVoucherStatus.Planned && AccountingValidationRules.BlockedReason(findings, voucher.Sequence) is { } rules
                ? voucher with
                {
                    Status = TallyVoucherStatus.Blocked, BlockedReason = $"VALIDATION_FAILED: {rules}.", Entries = Array.Empty<PlannedEntry>(),
                    PlanSha256 = TallySalesVoucherComposer.PlanHash(voucher.CorrespondenceKey, voucher.VoucherDate, Array.Empty<PlannedEntry>())
                }
                : voucher).ToArray();
        return (TallySalesVoucherComposer.Summarise(vouchers, plan.MappingsUsed), findings);
    }

    private static ValidationVoucher ToValidation(PlannedVoucher voucher, InvoiceAccountingSource invoice)
    {
        // One validation line per product: GST rows are matched to products, not to individual sales lines.
        var lines = invoice.Lines.GroupBy(line => line.ProductCode, StringComparer.Ordinal).Select(group =>
        {
            var rows = invoice.TaxRows.Where(row => string.Equals(row.ItemNumber, group.Key, StringComparison.Ordinal)).ToArray();
            var components = rows.Length == 0 ? null : rows
                .SelectMany(row => new[] { ("CGST", row.CgstRate, row.CgstAmount), ("SGST", row.SgstRate, row.SgstAmount), ("IGST", row.IgstRate, row.IgstAmount) })
                .Where(part => part.Item3 is { } amount && amount != 0 && part.Item2 is not null)
                .GroupBy(part => (part.Item1, Rate: part.Item2!.Value))
                .Select(part => new ValidationTaxComponent(part.Key.Item1, part.Key.Rate, part.Sum(item => item.Item3!.Value)))
                .ToArray();
            return new ValidationLine(string.Join("+", group.Select(line => line.LineIdentifier)), group.Sum(line => line.Gross ?? 0m),
                group.Sum(line => line.Net ?? 0m), group.Sum(line => line.Tax ?? 0m), components);
        }).ToArray();
        var tenders = invoice.Tenders.Where(tender => tender.Amount != 0).Select(tender => new ValidationTender(tender.SourceTenderCode, tender.Mode,
            string.Equals(tender.SourceTenderCode, TallySalesVoucherComposer.RoundOffCode, StringComparison.OrdinalIgnoreCase), tender.Amount)).ToArray();
        return new ValidationVoucher(voucher.Sequence, voucher.StoreCode, voucher.DocumentNumber, "INV", voucher.VoucherDate,
            lines.Sum(line => line.Gross), lines.Sum(line => line.Net), lines.Sum(line => line.Tax), lines, tenders,
            voucher.Entries.Select(entry => new ValidationEntry(entry.BusinessEvent, entry.LedgerName, entry.Debit, entry.Credit)).ToArray());
    }

    /// <summary>One plain sentence for the screen, such as "3 invoices: 2 ready, 1 left out, 0 need fixing".</summary>
    public static string Describe(SalesVoucherPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ready = plan.Vouchers.Count(voucher => voucher.Status == TallyVoucherStatus.Planned);
        var left = plan.Vouchers.Count(voucher => voucher.LeftOutByScope);
        var fix = plan.Vouchers.Count - ready - left;
        return string.Create(CultureInfo.InvariantCulture,
            $"{plan.Vouchers.Count} invoice(s): {ready} ready, {left} left out for a later step, {fix} need fixing. Vouchers total {plan.DebitTotal:0.00}.");
    }
}
