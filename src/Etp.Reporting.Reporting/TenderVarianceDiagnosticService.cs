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
        if (reconciliation.Status == ReconciliationStatus.Blocked)
            return new(ReconciliationStatus.Blocked, [], 0, 0, reconciliation.RuleVersion, reconciliation.Message);

        var rows = reconciliation.Documents
            .Select(x => ToDiagnostic(x, tolerance))
            .OrderByDescending(x => Math.Abs(x.Variance))
            .ThenBy(x => x.StoreCode, StringComparer.Ordinal)
            .ThenBy(x => x.DocumentNumber, StringComparer.Ordinal)
            .ToArray();
        var failed = rows.Count(x => x.LikelyCause != TenderVarianceCause.Matched);
        return new(reconciliation.Status, rows, failed, rows.Sum(x => Math.Abs(x.Variance)), reconciliation.RuleVersion,
            "Diagnostic classifications are evidence-led prompts only; approved tender controls and totals are unchanged.");
    }

    private static TenderVarianceDiagnosticRow ToDiagnostic(DocumentControlResult row, decimal tolerance)
    {
        TenderVarianceCause cause;
        string check;
        if (Math.Abs(row.Variance) <= tolerance) { cause = TenderVarianceCause.Matched; check = "No action required."; }
        else if (row.InvoiceAmount == 0 && row.TenderAmount != 0) { cause = TenderVarianceCause.TenderWithoutInvoice; check = "Check Revenue Report document linkage."; }
        else if (row.TenderAmount == 0 && row.InvoiceAmount != 0) { cause = TenderVarianceCause.MissingTender; check = "Check whether tender rows are absent or quarantined."; }
        else if (row.Variance > 0) { cause = TenderVarianceCause.PartialTender; check = "Check split tenders, rounding and excluded tender types."; }
        else { cause = TenderVarianceCause.ExcessTender; check = "Check duplicate tender rows or document linkage."; }
        return new(row.StoreCode, row.DocumentNumber, row.InvoiceAmount, row.TenderAmount, row.Variance, cause, check);
    }
}
