namespace Etp.Reporting.Reporting;

public sealed record InvoiceControlValue(string StoreCode, string DocumentNumber, decimal SourceSignedNetAmount);
public sealed record TenderControlValue(
    string StoreCode,
    string DocumentNumber,
    string TenderType,
    decimal SourceSignedAmount,
    bool IsRecognizedType);
public sealed record ApprovedControlRule(string Version, decimal AbsoluteTolerance)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version)) throw new ArgumentException("An approved rule version is required.");
        if (AbsoluteTolerance < 0) throw new ArgumentOutOfRangeException(nameof(AbsoluteTolerance));
    }
}
public sealed record DocumentControlResult(
    string StoreCode, string DocumentNumber, decimal InvoiceAmount, decimal TenderAmount,
    decimal Variance, ReconciliationStatus Status);
public sealed record InvoiceTenderReconciliation(
    ReconciliationStatus Status, IReadOnlyList<DocumentControlResult> Documents,
    decimal InvoiceTotal, decimal TenderTotal, decimal Variance, string RuleVersion, string Message);

public sealed class InvoiceTenderReconciliationService
{
    public InvoiceTenderReconciliation Reconcile(
        IEnumerable<InvoiceControlValue> invoiceValues,
        IEnumerable<TenderControlValue> tenderValues,
        ApprovedControlRule rule)
    {
        ArgumentNullException.ThrowIfNull(invoiceValues);
        ArgumentNullException.ThrowIfNull(tenderValues);
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();
        var invoices = invoiceValues.ToArray();
        var tenders = tenderValues.ToArray();
        if (invoices.Any(x => Missing(x.StoreCode) || Missing(x.DocumentNumber)) ||
            tenders.Any(x => Missing(x.StoreCode) || Missing(x.DocumentNumber) || Missing(x.TenderType) || !x.IsRecognizedType))
            return new(ReconciliationStatus.Blocked, [], 0, 0, 0, rule.Version,
                "Missing identifiers or unknown tender types prevent reconciliation.");

        var invoiceMap = invoices.GroupBy(Key).ToDictionary(x => x.Key, x => x.Sum(v => v.SourceSignedNetAmount));
        var tenderMap = tenders.GroupBy(Key).ToDictionary(x => x.Key, x => x.Sum(v => v.SourceSignedAmount));
        var keys = invoiceMap.Keys.Union(tenderMap.Keys).OrderBy(x => x.StoreCode).ThenBy(x => x.DocumentNumber);
        var documents = keys.Select(key =>
        {
            var invoice = invoiceMap.GetValueOrDefault(key);
            var tender = tenderMap.GetValueOrDefault(key);
            var variance = invoice - tender;
            return new DocumentControlResult(key.StoreCode, key.DocumentNumber, invoice, tender, variance,
                Math.Abs(variance) <= rule.AbsoluteTolerance ? ReconciliationStatus.Passed : ReconciliationStatus.Failed);
        }).ToArray();
        var invoiceTotal = invoices.Sum(x => x.SourceSignedNetAmount);
        var tenderTotal = tenders.Sum(x => x.SourceSignedAmount);
        var variance = invoiceTotal - tenderTotal;
        var status = documents.All(x => x.Status == ReconciliationStatus.Passed)
            ? ReconciliationStatus.Passed : ReconciliationStatus.Failed;
        return new(status, documents, invoiceTotal, tenderTotal, variance, rule.Version,
            "Compared source-signed invoice and tender values by store and document.");
    }

    private static (string StoreCode, string DocumentNumber) Key(InvoiceControlValue x) => (x.StoreCode, x.DocumentNumber);
    private static (string StoreCode, string DocumentNumber) Key(TenderControlValue x) => (x.StoreCode, x.DocumentNumber);
    private static bool Missing(string value) => string.IsNullOrWhiteSpace(value);
}
