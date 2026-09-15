using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportExportCoordinatorTests
{
    [Fact]
    public void Format_selection_preserves_visual_and_dsr_precedence()
    {
        var metadata = Metadata();
        var data = Data();
        var visual = VisualReportComposer.Compose(metadata, data);
        var dsr = DailySalesReportBuilder.Build(new(2026, 8, 25), [], [], new Dictionary<string, decimal?>());

        Assert.Equal(ReportExcelExportRoute.Tabular, ReportExportCoordinator.SelectExcelRoute(null));
        Assert.Equal(ReportExcelExportRoute.Visual, ReportExportCoordinator.SelectExcelRoute(visual));
        Assert.Equal(ReportPdfExportRoute.Tabular, ReportExportCoordinator.SelectPdfRoute(null, null));
        Assert.Equal(ReportPdfExportRoute.Visual, ReportExportCoordinator.SelectPdfRoute(null, visual));
        Assert.Equal(ReportPdfExportRoute.DailySalesReport, ReportExportCoordinator.SelectPdfRoute(dsr, visual));
    }

    [Fact]
    public async Task Management_summary_uses_the_production_pdf_export_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-management-{Guid.NewGuid():N}.pdf");
        try
        {
            await new ReportExportCoordinator().ExportManagementSummaryPdfAsync(path, Metadata(), Data());

            Assert.True(File.Exists(path));
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Every_synchronous_exporter_is_scheduled_away_from_the_caller_thread()
    {
        var exporterThread = 0;
        void Capture() => exporterThread = Environment.CurrentManagedThreadId;
        var coordinator = new ReportExportCoordinator(
            (_, _) => Capture(),
            (_, _) => Capture(),
            (_, _, _) => Capture(),
            (_, _) => Capture(),
            (_, _, _) => Capture(),
            (_, _) => Capture(),
            (_, _) => Capture());
        var metadata = Metadata();
        var data = Data();
        var visual = VisualReportComposer.Compose(metadata, data);
        var dsr = DailySalesReportBuilder.Build(new(2026, 8, 25), [], [], new Dictionary<string, decimal?>());
        var pack = Pack();

        await AssertOffloadedAsync(() => coordinator.ExportPackExcelAsync("pack.xlsx", pack));
        await AssertOffloadedAsync(() => coordinator.ExportPackPdfAsync("pack.pdf", pack));
        await AssertOffloadedAsync(() => coordinator.ExportReportExcelAsync("report.xlsx", metadata, data, null));
        await AssertOffloadedAsync(() => coordinator.ExportReportExcelAsync("visual.xlsx", metadata, data, visual));
        await AssertOffloadedAsync(() => coordinator.ExportReportPdfAsync("report.pdf", metadata, data, null, null));
        await AssertOffloadedAsync(() => coordinator.ExportReportPdfAsync("visual.pdf", metadata, data, visual, null));
        await AssertOffloadedAsync(() => coordinator.ExportReportPdfAsync("dsr.pdf", metadata, data, visual, dsr));
        await AssertOffloadedAsync(() => coordinator.ExportManagementSummaryPdfAsync("management.pdf", metadata, data));

        async Task AssertOffloadedAsync(Func<Task> export)
        {
            exporterThread = 0;
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var callerThread = 0;
            // A dedicated caller models WPF. Observe completion without blocking the exporter
            // or requiring the worker pool to schedule it within a wall-clock deadline.
            var caller = new Thread(() =>
            {
                callerThread = Environment.CurrentManagedThreadId;
                try { export().GetAwaiter().GetResult(); completed.SetResult(); }
                catch (Exception exception) { completed.SetException(exception); }
            });
            caller.Start();
            await completed.Task;
            caller.Join();
            Assert.NotEqual(0, exporterThread);
            Assert.NotEqual(callerThread, exporterThread);
        }
    }

    private static ExcelReportMetadata Metadata() => new(
        "ETP Management Summary",
        new(2026, 8, 1),
        new(2026, 8, 25),
        "Operational",
        "v1",
        "Aggregate operational evidence only.",
        new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    private static ExcelReportData Data() => new(
        [new("Report"), new("Files", "#,##0"), new("Rows", "#,##0")],
        [["R025", 2, 190]],
        ["Total", 2, 190]);

    private static ReportPackDocument Pack() => new(
        "Daily pack",
        new(2026, 8, 25),
        new(2026, 8, 25),
        "Passed",
        "TEST",
        "Ready",
        new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
        []);
}
