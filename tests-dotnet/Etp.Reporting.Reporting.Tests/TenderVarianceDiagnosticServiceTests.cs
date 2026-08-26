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
}
