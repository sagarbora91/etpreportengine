using System.Globalization;

namespace Etp.Reporting.Application.Accounting;

/// <summary>One result of a validation rule (plan task 7, <c>dbo.accounting_validation_findings</c>).
/// Only WARN and FAIL results are returned; a rule that holds produces no finding.</summary>
public sealed record ValidationFinding(
    string RuleId,
    int RuleVersion,
    string Severity,
    int? VoucherSequence,
    string? Subject,
    string? Observed,
    string? Expected,
    string Explanation,
    string? CorrectiveAction);

public static class ValidationSeverity
{
    public const string Pass = "PASS";
    public const string Warn = "WARN";
    public const string Fail = "FAIL";
}

/// <summary>Facts about the whole batch that the rules need.</summary>
/// <param name="HasFinalGeneration">The business day has a final report generation (RULE-SRC-001).</param>
/// <param name="MappingVersionSetUnchanged">The mapping versions are the ones recorded at approval (RULE-MAP-003); true before approval.</param>
public sealed record ValidationContext(
    TallyProfile Profile,
    DateOnly Today,
    bool HasFinalGeneration,
    bool MappingVersionSetUnchanged = true);

/// <summary>One GST component of a source line, as read from <c>etp_r018</c>.</summary>
public sealed record ValidationTaxComponent(string Component, decimal Rate, decimal Amount);

/// <summary>One source line. <paramref name="TaxComponents"/> is null when no tax row was found for the line.</summary>
public sealed record ValidationLine(string LineIdentifier, decimal Gross, decimal Net, decimal Tax, IReadOnlyList<ValidationTaxComponent>? TaxComponents);

/// <summary>One tender row. <paramref name="Mode"/> is null when the source tender code is not in the tender master.</summary>
public sealed record ValidationTender(string SourceTenderCode, string? Mode, bool IsRoundOff, decimal Amount);

/// <summary>One planned ledger line. <paramref name="LedgerName"/> is null when no approved mapping covers the event on the voucher date.</summary>
public sealed record ValidationEntry(string BusinessEvent, string? LedgerName, decimal Debit, decimal Credit);

public sealed record ValidationVoucher(
    int Sequence,
    string StoreCode,
    string DocumentNumber,
    string TransactionType,
    DateOnly VoucherDate,
    decimal Gross,
    decimal Net,
    decimal Tax,
    IReadOnlyList<ValidationLine> Lines,
    IReadOnlyList<ValidationTender> Tenders,
    IReadOnlyList<ValidationEntry> Entries,
    bool SourceUnchanged = true,
    bool KeyAlreadyReserved = false,
    bool AlreadySent = false);

/// <summary>The decision-independent checks of plan task 7. Pure: the caller loads the facts, these rules judge them.
/// Money is compared at two decimals, rounded away from zero; a missing amount is never treated as zero.
/// RULE-PAY-001 (re-parsed payload equals plan) belongs to the file export and is not here.</summary>
public static class AccountingValidationRules
{
    public const int RuleVersion = 1;
    public const decimal LargeInvoiceThreshold = 500_000m;
    public const int OldVoucherDays = 30;

    public static IReadOnlyList<string> SupportedTransactionTypes { get; } = ["INV"];

    public static IReadOnlyList<ValidationFinding> Evaluate(ValidationContext context, IReadOnlyList<ValidationVoucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(vouchers);
        var findings = new List<ValidationFinding>();
        void Add(string rule, string severity, int? voucher, string? subject, string? observed, string? expected, string explanation, string? action) =>
            findings.Add(new(rule, RuleVersion, severity, voucher, subject, observed, expected, explanation, action));

        // Batch level.
        if (!context.HasFinalGeneration)
            Add("RULE-SRC-001", ValidationSeverity.Fail, null, null, "no final report", "final report generation",
                "This business day has no final report generation, so its figures may still change.", "Finalise the day's reports, then prepare again.");
        var profile = context.Profile;
        if (!profile.IsEnabled)
            Add("RULE-ENV-001", ValidationSeverity.Fail, null, profile.ProfileCode, "not in use", "in use",
                "This Tally company is switched off.", "Turn it on under Settings → Integrations → Tally companies, or choose another company.");
        if (string.IsNullOrWhiteSpace(profile.CompanyName))
            Add("RULE-ENV-001", ValidationSeverity.Fail, null, profile.ProfileCode, "blank", "Tally company name",
                "The Tally company has no name.", "Enter the company name exactly as Tally shows it.");
        if (profile.Environment == "PRODUCTION" && profile.ProductionEnabledUtc is null)
            Add("RULE-ENV-001", ValidationSeverity.Fail, null, profile.ProfileCode, "live books not enabled", "live books enabled",
                "Live books have not been enabled for this company, so nothing can be prepared for it.", "Use the test company until live books are approved.");
        if (!context.MappingVersionSetUnchanged)
            Add("RULE-MAP-003", ValidationSeverity.Fail, null, null, "mappings changed", "mappings at approval",
                "A ledger mapping changed after this batch was approved.", "Prepare and approve the batch again.");

        foreach (var voucher in vouchers)
            EvaluateVoucher(context, voucher, Add);
        return findings;
    }

    /// <summary>True when nothing FAILs and every WARN has been accepted with a reason (plan task 7 gate).</summary>
    public static bool CanApprove(IReadOnlyList<ValidationFinding> findings, Func<ValidationFinding, bool> isWaived)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(isWaived);
        return findings.All(finding => finding.Severity != ValidationSeverity.Fail) &&
               findings.Where(finding => finding.Severity == ValidationSeverity.Warn).All(isWaived);
    }

    /// <summary>The <c>blocked_reason</c> for a voucher: its FAIL rules in order, or null when it is not blocked.
    /// A batch-level FAIL blocks every voucher.</summary>
    public static string? BlockedReason(IReadOnlyList<ValidationFinding> findings, int voucherSequence)
    {
        var rules = findings.Where(finding => finding.Severity == ValidationSeverity.Fail && (finding.VoucherSequence is null || finding.VoucherSequence == voucherSequence))
            .Select(finding => finding.RuleId).Distinct(StringComparer.Ordinal).ToArray();
        return rules.Length == 0 ? null : string.Join(", ", rules);
    }

    private static void EvaluateVoucher(ValidationContext context, ValidationVoucher voucher,
        Action<string, string, int?, string?, string?, string?, string, string?> add)
    {
        var sequence = voucher.Sequence;
        var subject = $"Invoice {voucher.DocumentNumber}";
        var profile = context.Profile;

        if (!SupportedTransactionTypes.Contains(voucher.TransactionType, StringComparer.Ordinal))
            add("RULE-SRC-002", ValidationSeverity.Fail, sequence, subject, voucher.TransactionType, string.Join("/", SupportedTransactionTypes),
                $"Documents of type {voucher.TransactionType} cannot be sent to Tally yet.", "Leave this document out; returns and cancellations come in a later step.");

        var tenders = voucher.Tenders.Where(tender => !tender.IsRoundOff).ToArray();
        var rateSets = voucher.Lines.Where(line => line.TaxComponents is not null)
            .Select(line => string.Join("|", line.TaxComponents!.OrderBy(c => c.Component, StringComparer.Ordinal).Select(c => $"{c.Component}:{Money(c.Rate)}")))
            .Distinct(StringComparer.Ordinal).Count();
        if (tenders.Length != 1 || rateSets > 1)
            add("RULE-SRC-003", ValidationSeverity.Fail, sequence, subject,
                $"{tenders.Length} payment mode(s), {rateSets} tax rate set(s)", "one payment mode and one tax rate set",
                "The first Tally step handles only invoices paid one way with one GST rate.", "Leave this invoice out for now; split payments and mixed rates come in a later step.");

        if (!voucher.SourceUnchanged)
            add("RULE-SRC-004", ValidationSeverity.Fail, sequence, subject, "changed", "unchanged since preparation",
                "The invoice's source figures changed after the batch was prepared (SOURCE_CHANGED).", "Reject this batch and prepare it again.");

        foreach (var entry in voucher.Entries.Where(item => string.IsNullOrWhiteSpace(item.LedgerName)))
            add(entry.BusinessEvent.StartsWith("TENDER_", StringComparison.Ordinal) ? "RULE-MAP-002" : "RULE-MAP-001", ValidationSeverity.Fail, sequence,
                entry.BusinessEvent, "no approved ledger", "an approved mapping effective " + Date(voucher.VoucherDate),
                $"No approved Tally ledger is mapped for {entry.BusinessEvent} on {Date(voucher.VoucherDate)}.", "Approve a ledger mapping for this event, then prepare again.");
        foreach (var tender in voucher.Tenders.Where(item => !item.IsRoundOff && item.Mode is null))
            add("RULE-MAP-002", ValidationSeverity.Fail, sequence, tender.SourceTenderCode, "unknown payment code", "a payment mode",
                $"Payment code {tender.SourceTenderCode} is not in the tender master (TENDER_MODE_UNKNOWN).", "Add it under Settings → Stores & masters → Tender mapping.");

        var debit = Money(voucher.Entries.Sum(entry => entry.Debit));
        var credit = Money(voucher.Entries.Sum(entry => entry.Credit));
        if (debit != credit || voucher.Entries.Count == 0)
            add("RULE-AMT-001", ValidationSeverity.Fail, sequence, subject, $"debit {Text(debit)}, credit {Text(credit)}", "debits equal credits",
                "The voucher's ledger lines do not balance.", "Check the ledger mappings for this invoice.");

        if (Money(voucher.Gross) != Money(voucher.Net + voucher.Tax))
            add("RULE-AMT-002", ValidationSeverity.Fail, sequence, subject, Text(Money(voucher.Gross)), Text(Money(voucher.Net + voucher.Tax)),
                "The invoice total is not its taxable value plus tax.", "Check the imported sales lines for this invoice.");
        foreach (var line in voucher.Lines.Where(item => Money(item.Gross) != Money(item.Net + item.Tax)))
            add("RULE-AMT-002", ValidationSeverity.Fail, sequence, $"Line {line.LineIdentifier}", Text(Money(line.Gross)), Text(Money(line.Net + line.Tax)),
                "A line's total is not its taxable value plus tax.", "Check the imported sales line.");

        var tendered = Money(voucher.Tenders.Sum(tender => tender.Amount));
        if (tendered != Money(voucher.Gross))
            add("RULE-AMT-003", profile.TenderModel == "IN_VOUCHER" ? ValidationSeverity.Fail : ValidationSeverity.Warn, sequence, subject,
                Text(tendered), Text(Money(voucher.Gross)),
                "Payments plus round-off do not add up to the invoice total.", "Check the invoice's payment rows in the Revenue Report import.");

        if (voucher.Gross > LargeInvoiceThreshold)
            add("RULE-AMT-004", ValidationSeverity.Warn, sequence, subject, Text(Money(voucher.Gross)), "at most " + Text(LargeInvoiceThreshold),
                "This invoice is unusually large.", "Confirm the amount, then accept this warning with a reason.");

        foreach (var line in voucher.Lines)
        {
            if (line.TaxComponents is null)
                add("RULE-TAX-001", ValidationSeverity.Fail, sequence, $"Line {line.LineIdentifier}", "no tax row", "GST components",
                    "No GST breakdown was found for this line (TAX_ROW_MISSING). Tax is never worked out by ETP.", "Import the GST detail report for this day.");
            else if (Money(line.TaxComponents.Sum(component => component.Amount)) != Money(line.Tax))
                add("RULE-TAX-001", ValidationSeverity.Fail, sequence, $"Line {line.LineIdentifier}",
                    Text(Money(line.TaxComponents.Sum(component => component.Amount))), Text(Money(line.Tax)),
                    "The GST components do not add up to the line's tax (TAX_SPLIT_MISMATCH).", "Check the GST detail report for this invoice.");
        }

        if (!TallyCorrespondenceKey.IsDocumentNumberSafe(voucher.DocumentNumber))
            add("RULE-KEY-001", ValidationSeverity.Fail, sequence, subject, "unsafe number", "a number without ':', '|' or spaces",
                "This invoice number cannot be carried into Tally safely (KEY_UNSAFE).", "This invoice has to be entered in Tally by hand.");
        if (voucher.KeyAlreadyReserved)
            add("RULE-KEY-001", ValidationSeverity.Fail, sequence, subject, "already in another batch", "not reserved",
                "This invoice is already in another batch for this Tally company.", "Reject that batch first if it was never sent.");
        if (voucher.AlreadySent)
            add("RULE-KEY-001", ValidationSeverity.Fail, sequence, subject, "already sent", "not sent",
                "This invoice has already been written or sent to this Tally company.", "Do not send it again; check what Tally recorded.");

        if (profile.PostingModel == "INVENTORY" && profile.TenderModel == "IN_VOUCHER" && tenders.Length > 1)
            add("RULE-ENV-002", ValidationSeverity.Fail, sequence, subject, $"{tenders.Length} payment modes", "one payment mode",
                "With stock items, payments inside the voucher allow only one payment mode.", "Leave this invoice out, or choose a clearing ledger per payment mode.");

        if (voucher.VoucherDate > context.Today ||
            profile.PostingFromDate is { } from && voucher.VoucherDate < from ||
            profile.PostingToDate is { } to && voucher.VoucherDate > to)
            add("RULE-DAT-001", ValidationSeverity.Fail, sequence, subject, Date(voucher.VoucherDate), DateWindow(profile, context.Today),
                "The voucher date is outside the dates this Tally company accepts.", "Check the date, or the company's allowed dates.");
        else if (voucher.VoucherDate < context.Today.AddDays(-OldVoucherDays))
            add("RULE-DAT-002", ValidationSeverity.Warn, sequence, subject, Date(voucher.VoucherDate), $"within {OldVoucherDays} days",
                $"This voucher is more than {OldVoucherDays} days old.", "Confirm with the accountant, then accept this warning with a reason.");
    }

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Text(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Date(DateOnly value) => value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

    private static string DateWindow(TallyProfile profile, DateOnly today) =>
        (profile.PostingFromDate, profile.PostingToDate) switch
        {
            ({ } from, { } to) => $"{Date(from)} to {Date(to)}, not after {Date(today)}",
            ({ } from, null) => $"from {Date(from)}, not after {Date(today)}",
            (null, { } to) => $"up to {Date(to)}, not after {Date(today)}",
            _ => $"not after {Date(today)}"
        };
}
