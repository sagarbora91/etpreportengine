using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class TenderVarianceDiagnosticServiceTests
{
    [Fact]
    public void Diagnose_classifies_variances_without_changing_control_status_or_values()
    {
        var source = new InvoiceTenderReconciliation(ReconciliationStatus.Failed,
            [new("A", "1", 100, 0, 100, ReconciliationStatus.Failed), new("A", "2", 100, 80, 20, ReconciliationStatus.Failed), new("A", "3", 0, 20, -20, ReconciliationStatus.Failed), new("A", "4", 100, 100, 0, ReconciliationStatus.Passed)],
            300, 200, 100, "v1", "control");
        var result = new TenderVarianceDiagnosticService().Diagnose(source, 0.01m);
        Assert.Equal(source.Status, result.Status);
        Assert.Equal(3, result.FailedDocuments);
        Assert.Contains(result.Rows, x => x.LikelyCause == TenderVarianceCause.MissingTender);
        Assert.Contains(result.Rows, x => x.LikelyCause == TenderVarianceCause.PartialTender);
        Assert.Contains(result.Rows, x => x.LikelyCause == TenderVarianceCause.TenderWithoutInvoice);
        Assert.Equal(140, result.AbsoluteVariance);
    }

    [Fact]
    public void Blocked_by_an_r022_gap_keeps_the_reconciled_documents_and_says_why()
    {
        // RA-TENDER-03: the covered store's documents are still classified; the invoices listed without a tender are not
        // (nothing to classify); the result stays Blocked and carries the reconciliation's gap message.
        var source = new InvoiceTenderReconciliation(ReconciliationStatus.Blocked,
            [new("A", "1", 100, 80, 20, ReconciliationStatus.Failed), new("A", "2", 100, 100, 0, ReconciliationStatus.Passed),
             new("B", "9", 500, null, null, ReconciliationStatus.Blocked)],
            700, 180, 20, "v1", "R022 missing / not imported for 1 store-day(s) with sales (B: 01 Sep 2026).");
        var result = new TenderVarianceDiagnosticService().Diagnose(source, 0m);
        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Equal(["1", "2"], result.Rows.Select(x => x.DocumentNumber));
        Assert.Equal(1, result.FailedDocuments);
        Assert.Equal(20, result.AbsoluteVariance);
        Assert.StartsWith("R022 missing / not imported for 1 store-day(s) with sales (B: 01 Sep 2026).", result.Message);
        Assert.EndsWith("approved tender controls and totals are unchanged.", result.Message);
    }

    [Fact]
    public void Blocked_without_any_reconciled_document_has_no_rows_and_keeps_the_message()
    {
        var source = new InvoiceTenderReconciliation(ReconciliationStatus.Blocked,
            [new("B", "9", 500, null, null, ReconciliationStatus.Blocked)], 500, 0, 0, "v1", "R022 missing.");
        var result = new TenderVarianceDiagnosticService().Diagnose(source, 0m);
        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.Empty(result.Rows);
        Assert.Equal("R022 missing.", result.Message);
    }
}
