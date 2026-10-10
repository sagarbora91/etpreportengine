using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Application.Accounting;

/// <summary>One source line of an invoice (<c>sales_lines</c>). A missing amount stays null, never zero.</summary>
public sealed record InvoiceSourceLine(
    string LineIdentifier,
    string ProductCode,
    string? TransactionType,
    decimal Quantity,
    decimal? Gross,
    decimal? Net,
    decimal? Tax);

/// <summary>One GST row of the invoice from <c>etp_r018</c> (one import file per invoice). Null means the file left it empty.</summary>
public sealed record InvoiceTaxRow(
    string ItemNumber,
    decimal? CgstRate,
    decimal? CgstAmount,
    decimal? SgstRate,
    decimal? SgstAmount,
    decimal? IgstRate,
    decimal? IgstAmount,
    decimal? CessAmount);

/// <summary>One tender row (<c>reporting_sales_tenders</c>). <paramref name="Mode"/> is the tender master's mode, or null when the code is not in it.</summary>
public sealed record InvoiceSourceTender(string SourceTenderCode, string? Mode, decimal Amount);

/// <summary>Everything task 6 reads for one invoice. It never holds a customer phone, number or address (D3).</summary>
public sealed record InvoiceAccountingSource(
    long SalesInvoiceId,
    string StoreCode,
    int InvoiceYear,
    string DocumentNumber,
    DateOnly InvoiceDate,
    IReadOnlyList<InvoiceSourceLine> Lines,
    IReadOnlyList<InvoiceTaxRow> TaxRows,
    IReadOnlyList<InvoiceSourceTender> Tenders);

/// <summary>An approved ledger mapping version for one business event on the voucher date.</summary>
public sealed record TallyLedgerMapping(string BusinessEvent, long MappingId, int Version, string DebitLedger, string CreditLedger);

/// <param name="CostCentre">D12: the store's cost centre in a company shared by several stores; null when the company has one store.</param>
/// <param name="CostCentreRequired">The company holds more than one store, so every voucher must name its store's cost centre.</param>
public sealed record SalesVoucherContext(
    TallyProfile Profile,
    string? CostCentre,
    bool CostCentreRequired,
    IReadOnlyList<TallyLedgerMapping> Mappings);

public sealed record PlannedEntry(int LineNumber, string BusinessEvent, string LedgerName, decimal Debit, decimal Credit, string? CostCentre, decimal? TaxRate);

public sealed record PlannedVoucher(
    int Sequence,
    string ComponentRole,
    string VoucherType,
    string StoreCode,
    int InvoiceYear,
    string DocumentNumber,
    int Revision,
    long SalesInvoiceId,
    DateOnly VoucherDate,
    decimal ExpectedTotal,
    string CorrespondenceKey,
    string Narration,
    string SourceSha256,
    string PlanSha256,
    string Status,
    string? BlockedReason,
    IReadOnlyList<PlannedEntry> Entries)
{
    /// <summary>The block code, such as <c>MAPPING_MISSING</c>; null for a planned voucher.</summary>
    public string? BlockCode => BlockedReason is null ? null : BlockedReason.Split(':', 2)[0];

    /// <summary>Left out by a known limit of this slice (a return, a split payment…), not by a fault to fix.</summary>
    public bool LeftOutByScope => BlockCode is not null && TallySalesVoucherComposer.ScopeBlocks.Contains(BlockCode);
}

/// <param name="BlockingReason">Why the batch cannot be approved: a setup or data fault on at least one invoice, or nothing to send.
/// Invoices left out by scope do not block; they stay visible as BLOCKED with their reason.</param>
public sealed record SalesVoucherPlan(
    IReadOnlyList<PlannedVoucher> Vouchers,
    decimal DebitTotal,
    decimal CreditTotal,
    string? BlockingReason,
    IReadOnlyList<TallyLedgerMapping> MappingsUsed);

/// <summary>Plan task 6, Slice 7a: one Sales voucher per invoice (D13), the tender ledger as the debit side with payment inside the
/// voucher (D14 single retail ledger, D16), accounting only (D17), one GST ledger per tax and rate (D15), and the store's cost centre
/// on every line when one company holds several stores (D12). Pure: the caller loads the invoices and mappings.
/// Tax is never derived; the composer never adds a balancing line; anything it cannot plan exactly is BLOCKED with a reason.</summary>
public static class TallySalesVoucherComposer
{
    public const string VoucherType = "Sales";
    public const string RoundOffCode = "ROUND_OFF";

    /// <summary>Known limits of Slice 7a. Such invoices are left out and do not block the rest of the day.</summary>
    public static IReadOnlySet<string> ScopeBlocks { get; } = new HashSet<string>(StringComparer.Ordinal) { "NOT_IN_SCOPE_7A", "CESS_NOT_SUPPORTED", "KEY_UNSAFE" };

    /// <summary>Refund tenders belong to returns (Slice 7b).</summary>
    public static IReadOnlySet<string> RefundTenderCodes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CASH_REFUND", "CHEQUE_RTGS_REFUND", "NO_REFUND", "ISSUED_CREDITNOTE" };

    private static readonly string[] ComponentOrder = ["CGST", "SGST", "IGST"];
    private static readonly Regex UnsafeKeyCharacters = new(@"[:|\s]", RegexOptions.CultureInvariant);

    /// <summary>Null when the company's settings are the ones this slice composes; otherwise the sentence to show.</summary>
    public static string? Unsupported(TallyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var unsupported = new List<string>();
        if (profile.VoucherGranularity != "PER_INVOICE") unsupported.Add("one daily summary voucher");
        if (profile.PartyPolicy != "SINGLE_LEDGER") unsupported.Add("named customer ledgers");
        if (profile.TenderModel != "IN_VOUCHER") unsupported.Add("a clearing ledger for payments");
        if (profile.PostingModel != "ACCOUNTING_ONLY") unsupported.Add("stock items");
        return unsupported.Count == 0 ? null
            : $"This Tally company is set up for {string.Join(", ", unsupported)}, which ETP cannot prepare yet. Only one voucher per invoice, one retail ledger, payment inside the voucher and accounting only are supported.";
    }

    public static string TenderEvent(string mode) => "TENDER_" + mode.Trim().ToUpperInvariant().Replace(' ', '_');

    public static string TaxEvent(string component, decimal rate) =>
        $"OUTPUT_{component}_{Money(rate).ToString("0.##", CultureInfo.InvariantCulture)}";

    public static SalesVoucherPlan Compose(SalesVoucherContext context, IReadOnlyList<InvoiceAccountingSource> invoices)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(invoices);
        if (Unsupported(context.Profile) is { } refusal) throw new InvalidOperationException(refusal);

        var mappings = context.Mappings.GroupBy(mapping => mapping.BusinessEvent, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(mapping => mapping.Version).First(), StringComparer.Ordinal);
        var used = new SortedDictionary<string, TallyLedgerMapping>(StringComparer.Ordinal);
        var vouchers = new List<PlannedVoucher>();
        var sequence = 0;
        foreach (var invoice in invoices.OrderBy(item => item.InvoiceYear).ThenBy(item => item.DocumentNumber, StringComparer.Ordinal))
            vouchers.Add(ComposeOne(++sequence, invoice, context, mappings, used));

        return Summarise(vouchers, used.Values.ToArray());
    }

    /// <summary>Totals and the batch blocking reason for a list of vouchers: faults block the day, scope limits do not.</summary>
    public static SalesVoucherPlan Summarise(IReadOnlyList<PlannedVoucher> vouchers, IReadOnlyList<TallyLedgerMapping> mappingsUsed)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        ArgumentNullException.ThrowIfNull(mappingsUsed);
        var planned = vouchers.Where(voucher => voucher.Status == TallyVoucherStatus.Planned).ToArray();
        var faults = vouchers.Where(voucher => voucher.Status == TallyVoucherStatus.Blocked && !voucher.LeftOutByScope).ToArray();
        string? blocking = null;
        if (faults.Length > 0)
            blocking = $"{faults.Length} invoice(s) need fixing before this day can be approved: "
                       + string.Join("; ", faults.Take(10).Select(voucher => $"{voucher.DocumentNumber} {voucher.BlockedReason}"))
                       + (faults.Length > 10 ? $"; and {faults.Length - 10} more." : ".");
        else if (planned.Length == 0)
            blocking = vouchers.Count == 0 ? "No invoices exist for this day." : "No invoice of this day can be sent yet; every invoice is left out.";

        return new(vouchers, planned.SelectMany(voucher => voucher.Entries).Sum(entry => entry.Debit),
            planned.SelectMany(voucher => voucher.Entries).Sum(entry => entry.Credit), blocking, mappingsUsed);
    }

    private static PlannedVoucher ComposeOne(int sequence, InvoiceAccountingSource invoice, SalesVoucherContext context,
        IReadOnlyDictionary<string, TallyLedgerMapping> mappings, IDictionary<string, TallyLedgerMapping> used)
    {
        const int revision = 1;
        var role = TallyComponentRole.Sales;
        var key = $"ETP:{invoice.StoreCode}:{invoice.InvoiceYear}:{invoice.DocumentNumber}:{role}:{revision}";
        var narration = Cut($"{key} | ETP {invoice.StoreCode} invoice {invoice.DocumentNumber} dated {invoice.InvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)}", 500);
        var gross = invoice.Lines.All(line => line.Gross is not null) ? Money(invoice.Lines.Sum(line => line.Gross!.Value)) : 0m;
        var sourceHash = SourceHash(invoice);

        PlannedVoucher Result(string status, string? reason, IReadOnlyList<PlannedEntry> entries) => new(
            sequence, role, VoucherType, invoice.StoreCode, invoice.InvoiceYear, invoice.DocumentNumber, revision, invoice.SalesInvoiceId,
            invoice.InvoiceDate, gross, key, narration, sourceHash, PlanHash(key, invoice.InvoiceDate, entries), status, reason, entries);
        PlannedVoucher Blocked(string code, string detail) => Result(TallyVoucherStatus.Blocked, $"{code}: {detail}", Array.Empty<PlannedEntry>());

        // Known limits first, so an invoice this slice never sends is not reported as a fault.
        if (UnsafeKeyCharacters.IsMatch(invoice.DocumentNumber) || invoice.DocumentNumber.Length is 0 or > 80)
            return Blocked("KEY_UNSAFE", "the invoice number contains ':', '|' or a space and cannot be traced in Tally.");
        if (invoice.Lines.Count == 0)
            return Blocked("NOT_IN_SCOPE_7A", "the invoice has no lines.");
        var types = invoice.Lines.Select(line => line.TransactionType?.Trim().ToUpperInvariant() ?? "").Distinct(StringComparer.Ordinal).ToArray();
        if (types.Length != 1 || types[0] != "INV")
            return Blocked("NOT_IN_SCOPE_7A", $"only sales invoices (INV) are sent in this step; this document is {string.Join("/", types.Select(type => type.Length == 0 ? "unknown" : type))}.");
        var tenders = invoice.Tenders.Where(tender => tender.Amount != 0).ToArray();
        if (tenders.FirstOrDefault(tender => RefundTenderCodes.Contains(tender.SourceTenderCode)) is { } refund)
            return Blocked("NOT_IN_SCOPE_7A", $"refund tender {refund.SourceTenderCode} belongs to returns.");
        var payments = tenders.Where(tender => !IsRoundOff(tender)).ToArray();
        if (payments.Length != 1)
            return Blocked("NOT_IN_SCOPE_7A", payments.Length == 0 ? "the invoice has no payment." : $"split payment ({string.Join(", ", payments.Select(tender => tender.SourceTenderCode))}).");
        if (invoice.TaxRows.Any(row => row.CessAmount is { } cess && cess != 0))
            return Blocked("CESS_NOT_SUPPORTED", "the invoice carries cess, which needs its own ledger.");

        // Faults that someone must fix.
        if (invoice.Lines.Any(line => line.Gross is null || line.Net is null || line.Tax is null))
            return Blocked("SOURCE_AMOUNT_MISSING", "a line has no gross, net or tax amount. Import the sales export again.");
        if (payments[0].Mode is null)
            return Blocked("TENDER_MODE_UNKNOWN", $"payment code {payments[0].SourceTenderCode} is not in the tender master.");
        var missingTax = invoice.Lines.Select(line => line.ProductCode).Distinct(StringComparer.Ordinal)
            .Where(product => !invoice.TaxRows.Any(row => string.Equals(row.ItemNumber, product, StringComparison.Ordinal))).ToArray();
        if (missingTax.Length > 0)
            return Blocked("TAX_ROW_MISSING", $"no GST row for item {string.Join(", ", missingTax)}. Import the GST detail report (R018) for this day.");
        var components = new List<(string Component, decimal Rate, decimal Amount)>();
        foreach (var row in invoice.TaxRows)
            foreach (var (component, rate, amount) in new[] { ("CGST", row.CgstRate, row.CgstAmount), ("SGST", row.SgstRate, row.SgstAmount), ("IGST", row.IgstRate, row.IgstAmount) })
            {
                if (amount is null && rate is null or 0m) continue;
                if (amount is null || rate is null)
                    return Blocked("TAX_AMOUNT_MISSING", $"the {component} rate or amount of item {row.ItemNumber} is empty.");
                if (amount.Value != 0) components.Add((component, Money(rate.Value), amount.Value));
            }
        var rateSets = invoice.TaxRows.Select(row => string.Join(",", new[] { ("CGST", row.CgstRate, row.CgstAmount), ("SGST", row.SgstRate, row.SgstAmount), ("IGST", row.IgstRate, row.IgstAmount) }
                .Where(part => part.Item3 is { } value && value != 0).Select(part => $"{part.Item1}{Money(part.Item2!.Value).ToString("0.##", CultureInfo.InvariantCulture)}")))
            .Distinct(StringComparer.Ordinal).Count();
        if (rateSets > 1)
            return Blocked("NOT_IN_SCOPE_7A", "the invoice has items at different GST rates.");
        var lineTax = Money(invoice.Lines.Sum(line => line.Tax!.Value));
        var componentTax = Money(components.Sum(component => component.Amount));
        if (componentTax != lineTax)
            return Blocked("TAX_SPLIT_MISMATCH", $"the GST rows add up to {Text(componentTax)} but the sales lines carry {Text(lineTax)}.");
        if (context.CostCentreRequired && string.IsNullOrWhiteSpace(context.CostCentre))
            return Blocked("COST_CENTRE_MISSING", $"this Tally company holds several stores; set the cost centre for {invoice.StoreCode} in Settings → Tally companies.");

        // The voucher: tender debit, round-off, revenue credit, one credit per GST component and rate.
        var lines = new List<(string Event, bool Debit, decimal Amount, decimal? Rate)>
        {
            (TenderEvent(payments[0].Mode!), true, Money(payments[0].Amount), null)
        };
        var roundOff = Money(tenders.Where(IsRoundOff).Sum(tender => tender.Amount));
        if (roundOff != 0) lines.Add(("ROUND_OFF", roundOff > 0, Math.Abs(roundOff), null));
        lines.Add(("SALES_REVENUE", false, Money(invoice.Lines.Sum(line => line.Net!.Value)), null));
        foreach (var group in components.GroupBy(component => (component.Component, component.Rate))
                     .OrderBy(group => Array.IndexOf(ComponentOrder, group.Key.Component)).ThenBy(group => group.Key.Rate))
            lines.Add((TaxEvent(group.Key.Component, group.Key.Rate), false, Money(group.Sum(component => component.Amount)), group.Key.Rate));

        if (lines.Any(line => line.Amount <= 0 && line.Event is not "ROUND_OFF"))
            return Blocked("NOT_IN_SCOPE_7A", "the invoice has a zero or negative payment, revenue or tax amount.");
        var debit = lines.Where(line => line.Debit).Sum(line => line.Amount);
        var credit = lines.Where(line => !line.Debit).Sum(line => line.Amount);
        if (debit != credit)
            return Blocked("UNBALANCED", $"payment and round-off ({Text(debit)}) do not equal revenue and GST ({Text(credit)}). Check the sales export for this invoice.");
        var missing = lines.Select(line => line.Event).Where(code => !mappings.ContainsKey(code)).Distinct(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            return Blocked("MAPPING_MISSING", $"no approved ledger for {string.Join(", ", missing)} on {invoice.InvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)}.");

        var entries = new List<PlannedEntry>();
        foreach (var line in lines)
        {
            var mapping = mappings[line.Event];
            used[mapping.BusinessEvent] = mapping;
            entries.Add(new(entries.Count + 1, line.Event, line.Debit ? mapping.DebitLedger : mapping.CreditLedger,
                line.Debit ? line.Amount : 0m, line.Debit ? 0m : line.Amount, string.IsNullOrWhiteSpace(context.CostCentre) ? null : context.CostCentre.Trim(), line.Rate));
        }
        return Result(TallyVoucherStatus.Planned, null, entries);
    }

    private static bool IsRoundOff(InvoiceSourceTender tender) => string.Equals(tender.SourceTenderCode, RoundOffCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>SHA-256 of the invoice facts in canonical JSON: ordinal key order, decimals at four places, lines by identifier.</summary>
    public static string SourceHash(InvoiceAccountingSource invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return Hash(Object(
            ("store", invoice.StoreCode), ("year", invoice.InvoiceYear.ToString(CultureInfo.InvariantCulture)), ("document", invoice.DocumentNumber),
            ("date", invoice.InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("lines", invoice.Lines.OrderBy(line => line.LineIdentifier, StringComparer.Ordinal).Select(line => (object?)Object(
                ("id", line.LineIdentifier), ("product", line.ProductCode), ("type", line.TransactionType), ("quantity", Decimal(line.Quantity)),
                ("gross", Decimal(line.Gross)), ("net", Decimal(line.Net)), ("tax", Decimal(line.Tax)))).ToList()),
            ("tax", invoice.TaxRows.OrderBy(row => row.ItemNumber, StringComparer.Ordinal).ThenBy(row => Decimal(row.CgstAmount) + Decimal(row.SgstAmount) + Decimal(row.IgstAmount), StringComparer.Ordinal)
                .Select(row => (object?)Object(("item", row.ItemNumber), ("cgstRate", Decimal(row.CgstRate)), ("cgst", Decimal(row.CgstAmount)), ("sgstRate", Decimal(row.SgstRate)),
                    ("sgst", Decimal(row.SgstAmount)), ("igstRate", Decimal(row.IgstRate)), ("igst", Decimal(row.IgstAmount)), ("cess", Decimal(row.CessAmount)))).ToList()),
            ("tenders", invoice.Tenders.OrderBy(tender => tender.SourceTenderCode, StringComparer.Ordinal).ThenBy(tender => Decimal(tender.Amount), StringComparer.Ordinal)
                .Select(tender => (object?)Object(("code", tender.SourceTenderCode), ("mode", tender.Mode), ("amount", Decimal(tender.Amount)))).ToList())));
    }

    /// <summary>SHA-256 of the planned entries, in line order.</summary>
    public static string PlanHash(string correspondenceKey, DateOnly voucherDate, IReadOnlyList<PlannedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return Hash(Object(("key", correspondenceKey), ("type", VoucherType), ("date", voucherDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("entries", entries.Select(entry => (object?)Object(("line", entry.LineNumber.ToString(CultureInfo.InvariantCulture)), ("event", entry.BusinessEvent),
                ("ledger", entry.LedgerName), ("debit", Decimal(entry.Debit)), ("credit", Decimal(entry.Credit)), ("costCentre", entry.CostCentre),
                ("rate", Decimal(entry.TaxRate)))).ToList())));
    }

    private static SortedDictionary<string, object?> Object(params (string Key, object? Value)[] properties)
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in properties) result.Add(name, value);
        return result;
    }

    private static string? Decimal(decimal? value) => value?.ToString("F4", CultureInfo.InvariantCulture);

    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Text(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length];
}
