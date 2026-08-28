using Etp.Reporting.Application.Reports;

namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

public sealed class ReportingTenderVarianceDiagnosticTests
{
    [Fact]
    public void Application_adapter_preserves_control_status_totals_and_diagnostic_classifications()
    {
        var source = new TenderReconciliationReport(
            ReportStatus.Failed,
            [
                new("A", "1", 100, 0, 100, ReportStatus.Failed),
                new("A", "2", 100, 80, 20, ReportStatus.Failed),
                new("A", "3", 0, 20, -20, ReportStatus.Failed),
                new("A", "4", 100, 100, 0, ReportStatus.Passed)
            ],
            300, 200, 100, "v1", "control");

        var result = new ReportingTenderVarianceDiagnostic().Diagnose(source, 0.01m);

        Assert.Equal(source.Status, result.Status);
        Assert.Equal(3, result.FailedDocuments);
        Assert.Equal(140, result.AbsoluteVariance);
        Assert.Contains(result.Rows, row => row.LikelyCause == TenderVarianceCause.MissingTender);
        Assert.Contains(result.Rows, row => row.LikelyCause == TenderVarianceCause.PartialTender);
        Assert.Contains(result.Rows, row => row.LikelyCause == TenderVarianceCause.TenderWithoutInvoice);
        Assert.Equal(100, result.Rows.Single(row => row.DocumentNumber == "1").Variance);
    }

    [Fact]
    public void Blocked_control_remains_blocked_and_has_no_diagnostic_rows()
    {
        var source = new TenderReconciliationReport(ReportStatus.Blocked, [], 0, 0, 0, "v1", "Missing identifiers.");

        var result = new ReportingTenderVarianceDiagnostic().Diagnose(source, 0.01m);

        Assert.Equal(ReportStatus.Blocked, result.Status);
        Assert.Empty(result.Rows);
        Assert.Equal("Missing identifiers.", result.Message);
    }
}
