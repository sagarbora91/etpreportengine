namespace Etp.Reporting.Reporting;

public enum TenderVarianceCause { Matched, MissingTender, PartialTender, ExcessTender, TenderWithoutInvoice }

public sealed record TenderVarianceDiagnosticRow(
    string StoreCode,
    string DocumentNumber,
    decimal InvoiceAmount,
    decimal TenderAmount,
    decimal Variance,
    TenderVarianceCause LikelyCause,
    string RecommendedCheck);

public sealed record TenderVarianceDiagnosticResult(
    ReconciliationStatus Status,
    IReadOnlyList<TenderVarianceDiagnosticRow> Rows,
    int FailedDocuments,
    decimal AbsoluteVariance,
    string RuleVersion,
    string Message);

public sealed class TenderVarianceDiagnosticService
{
    public TenderVarianceDiagnosticResult Diagnose(InvoiceTenderReconciliation reconciliation, decimal tolerance)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        // RA-TENDER-03: a reconciliation Blocked by an R022 gap still carries the documents of the covered days; those are
        // classified as usual. Documents listed without a tender (no R022) have nothing to classify and are left out.
        // The result stays Blocked and keeps the reconciliation's message, which says why.
        var reconciled = reconciliation.Documents.Where(x => x.TenderAmount is not null && x.Variance is not null).ToArray();
        if (reconciliation.Status == ReconciliationStatus.Blocked && reconciled.Length == 0)
            return new(ReconciliationStatus.Blocked, [], 0, 0, reconciliation.RuleVersion, reconciliation.Message);

        var rows = reconciled
            .Select(x => ToDiagnostic(x, tolerance))
            .OrderByDescending(x => Math.Abs(x.Variance))
            .ThenBy(x => x.StoreCode, StringComparer.Ordinal)
            .ThenBy(x => x.DocumentNumber, StringComparer.Ordinal)
            .ToArray();
        var failed = rows.Count(x => x.LikelyCause != TenderVarianceCause.Matched);
        const string note = "Diagnostic classifications are evidence-led prompts only; approved tender controls and totals are unchanged.";
        var message = reconciliation.Status == ReconciliationStatus.Blocked ? $"{reconciliation.Message} {note}" : note;
        return new(reconciliation.Status, rows, failed, rows.Sum(x => Math.Abs(x.Variance)), reconciliation.RuleVersion, message);
    }

    private static TenderVarianceDiagnosticRow ToDiagnostic(DocumentControlResult row, decimal tolerance)
    {
        TenderVarianceCause cause;
        string check;
        var tender = row.TenderAmount!.Value;
        var variance = row.Variance!.Value;
        if (Math.Abs(variance) <= tolerance) { cause = TenderVarianceCause.Matched; check = "No action required."; }
        else if (row.InvoiceAmount == 0 && tender != 0) { cause = TenderVarianceCause.TenderWithoutInvoice; check = "Check Revenue Report document linkage."; }
        else if (tender == 0 && row.InvoiceAmount != 0) { cause = TenderVarianceCause.MissingTender; check = "Check whether tender rows are absent or quarantined."; }
        else if (variance > 0) { cause = TenderVarianceCause.PartialTender; check = "Check split tenders, rounding and excluded tender types."; }
        else { cause = TenderVarianceCause.ExcessTender; check = "Check duplicate tender rows or document linkage."; }
        return new(row.StoreCode, row.DocumentNumber, row.InvoiceAmount, tender, variance, cause, check);
    }
}
