using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportsPresentationStateTests
{
    [Theory]
    [InlineData(Etp.Reporting.Application.Reports.ReportStatus.Passed, "Succeeded")]
    [InlineData(Etp.Reporting.Application.Reports.ReportStatus.Failed, "Failed")]
    [InlineData(Etp.Reporting.Application.Reports.ReportStatus.Blocked, "Blocked")]
    [InlineData(Etp.Reporting.Application.Reports.ReportStatus.NotRun, "Blocked")]
    public void Application_report_status_has_a_valid_audit_outcome(
        Etp.Reporting.Application.Reports.ReportStatus status,
        string expected)
    {
        Assert.Equal(expected, ReportsWorkspaceView.ToAuditOutcome(status));
    }

    [Theory]
    [InlineData(ReconciliationStatus.Passed, "Succeeded")]
    [InlineData(ReconciliationStatus.Failed, "Failed")]
    [InlineData(ReconciliationStatus.Blocked, "Blocked")]
    [InlineData(ReconciliationStatus.NotRun, "Blocked")]
    public void Reconciliation_status_has_a_valid_audit_outcome(ReconciliationStatus status, string expected)
    {
        Assert.Equal(expected, ReportsWorkspaceView.ToAuditOutcome(status));
    }

    [Fact]
    public void Unknown_report_status_is_blocked_for_audit()
    {
        Assert.Equal("Blocked", ReportsWorkspaceView.ToAuditOutcome("Unexpected"));
    }

    [Fact]
    public void Session_transitions_keep_daily_pack_but_clear_active_report_state()
    {
        var session = new ReportPresentationSession();
        var pack = Pack();
        session.SetDailyPack(pack);
        session.BeginReport("sales-brand");
        var completed = session.SetReport(Metadata("Brand Sales"), Data());

        Assert.Equal("sales-brand", completed.ReportCode);
        Assert.NotNull(completed.VisualReport);
        Assert.True(completed.CanExportReport);
        Assert.Same(pack, completed.DailyPackDocument);

        var loading = session.BeginReport("stock-closing");
        Assert.Equal("stock-closing", loading.ReportCode);
        Assert.Null(loading.ExportMetadata);
        Assert.Null(loading.ExportData);
        Assert.Null(loading.VisualReport);
        Assert.Null(loading.DailySalesReport);
        Assert.Same(pack, loading.DailyPackDocument);
    }

    [Fact]
    public void Session_owns_dsr_and_preserves_the_dsr_route_when_document_is_available()
    {
        var session = new ReportPresentationSession();
        session.BeginReport("dsr");
        var document = DailySalesReportBuilder.Build(
            new DateOnly(2026, 8, 25),
            [],
            [],
            new Dictionary<string, decimal?>());

        var snapshot = session.SetReport(Metadata("Daily Sales Report"), Data(), document);

        Assert.Equal("dsr", snapshot.ReportCode);
        Assert.Same(document, snapshot.DailySalesReport);
        Assert.NotNull(snapshot.VisualReport);
    }

    [Fact]
    public void Dsr_route_is_not_left_active_when_a_non_dsr_result_is_supplied()
    {
        var session = new ReportPresentationSession();
        session.BeginReport("dsr");

        var snapshot = session.SetReport(Metadata("Fallback Report"), Data());

        Assert.Null(snapshot.ReportCode);
        Assert.Null(snapshot.DailySalesReport);
        Assert.True(snapshot.CanExportReport);
    }

    private static ExcelReportMetadata Metadata(string name) => new(
        name,
        new DateOnly(2026, 8, 25),
        new DateOnly(2026, 8, 25),
        "Passed",
        "TEST",
        "Test report",
        new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    private static ExcelReportData Data() => new(
        [new ExcelReportColumn("Value", "#,##0.00")],
        [(IReadOnlyList<object?>)[125m]],
        [125m]);

    private static ReportPackDocument Pack() => new(
        "Daily pack",
        new DateOnly(2026, 8, 25),
        new DateOnly(2026, 8, 25),
        "Passed",
        "TEST",
        "Ready",
        new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
        []);

}
