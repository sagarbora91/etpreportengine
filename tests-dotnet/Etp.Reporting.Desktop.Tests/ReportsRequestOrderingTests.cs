using System.Threading;
using System.Windows.Controls;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop.Modules.Reports;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportsRequestOrderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Older_report_completion_cannot_replace_new_route_preview_or_export(bool failOlderRequest)
    {
        RunSta(async () =>
        {
            var query = new DeferredTrendQuery();
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(query, previews);
            var older = view.RunReportAsync("management-trend");
            await view.RunReportAsync("sales-titan");
            Assert.Single(previews);
            Assert.Equal("sales-titan", previews[0].ReportCode);
            Assert.Equal(42m, previews[0].ExportData!.Rows[0][2]);
            var currentStatus = ((TextBlock)view.FindName("ReportResult")).Text;
            if (failOlderRequest) query.Completion.SetException(new InvalidOperationException("Old query failed"));
            else query.Completion.SetResult([new(new(2026, 9, 10), "WLMHW", 999m, 1m, 1, 0m, 0)]);
            await older;
            Assert.Single(previews);
            Assert.Equal(currentStatus, ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.Equal("sales-titan", view.CurrentReportCode);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
        });
    }

    [Fact]
    public void Date_change_invalidates_export_and_pending_results_until_report_is_rerun()
    {
        RunSta(async () =>
        {
            var query = new DeferredTrendQuery();
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(query, previews);
            await view.RunReportAsync("sales-titan");
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            var older = view.RunReportAsync("management-trend");
            view.SetBusinessDate(new(2026, 9, 9));
            query.Completion.SetResult([new(new(2026, 9, 10), "WLMHW", 999m, 1m, 1, 0m, 0)]);
            await older;
            Assert.Single(previews);
            Assert.False(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            Assert.Contains("Filters changed", ((TextBlock)view.FindName("ReportResult")).Text);
            await view.RunReportAsync("sales-titan");
            Assert.Equal(new DateOnly(2026, 9, 9), previews[^1].ExportMetadata!.DateTo);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
        });
    }

    [Fact]
    public void Snapshot_refresh_does_not_discard_the_retained_range_start()
    {
        RunSta(async () =>
        {
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(new DeferredTrendQuery(), previews);
            view.ApplyTaskScope("stock-physical", new(2026, 9, 10), new(2026, 9, 10), "Titan");
            await view.RunReportAsync("sales-titan");
            Assert.Equal(new DateOnly(2026, 9, 1), previews[^1].ExportMetadata!.DateFrom);
            Assert.Equal(new DateOnly(2026, 9, 10), previews[^1].ExportMetadata!.DateTo);
            view.ApplyTaskScope("stock-variance", new(2026, 9, 5), new(2026, 9, 10), "Titan");
            await view.RunReportAsync("sales-titan");
            Assert.Equal(new DateOnly(2026, 9, 5), previews[^1].ExportMetadata!.DateFrom);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Export_completion_cannot_overwrite_a_new_report_and_reentry_is_ignored(bool pdf, bool fail)
    {
        RunSta(async () =>
        {
            var exporter = new DeferredExport();
            var previews = new List<ReportPresentationSnapshot>();
            var view = CreateView(new DeferredTrendQuery(), previews, exporter);
            await view.RunReportAsync("sales-titan");
            var saving = view.ExportReportToPathAsync("synthetic-output", pdf);
            Assert.True(view.IsExportInProgress);
            await view.ExportReportToPathAsync("duplicate-output", pdf);
            Assert.Equal(1, exporter.Calls);
            await view.RunReportAsync("sales-helios");
            var status = ((TextBlock)view.FindName("ReportResult")).Text;
            if (fail) exporter.Completion.SetException(new IOException("Synthetic disk failure"));
            else exporter.Completion.SetResult();
            await saving;
            Assert.False(view.IsExportInProgress);
            Assert.Equal(status, ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.Equal("sales-helios", view.CurrentReportCode);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
        });
    }

    [Fact]
    public void Saved_file_is_not_reported_as_failed_when_activity_history_fails()
    {
        RunSta(async () =>
        {
            var exporter = new DeferredExport();
            var view = CreateView(new DeferredTrendQuery(), [], exporter);
            view.AttachHost(_ => true, (kind, _, _) => kind.StartsWith("Export") ? Task.FromException(new IOException("Audit unavailable")) : Task.CompletedTask,
                (_, _, _) => { }, _ => { }, _ => { });
            await view.RunReportAsync("sales-titan");
            var saving = view.ExportReportToPathAsync("saved-output.xlsx", false);
            exporter.Completion.SetResult();
            await saving;
            var status = ((TextBlock)view.FindName("ReportResult")).Text;
            Assert.Contains("report saved to saved-output.xlsx", status);
            Assert.Contains("Activity history could not be updated", status);
            Assert.DoesNotContain("export failed", status);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_or_cancelled_export_keeps_current_result_and_allows_retry(bool cancelled)
    {
        RunSta(async () =>
        {
            var exporter = new DeferredExport();
            var view = CreateView(new DeferredTrendQuery(), [], exporter);
            await view.RunReportAsync("sales-titan");
            var saving = view.ExportReportToPathAsync("synthetic-output", false);
            if (cancelled) exporter.Completion.SetCanceled();
            else exporter.Completion.SetException(new IOException("Synthetic disk failure"));
            await saving;
            Assert.Contains("Excel export failed", ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.Equal("sales-titan", view.CurrentReportCode);
            Assert.True(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            await view.ExportReportToPathAsync("synthetic-retry", false);
            Assert.Equal(2, exporter.Calls);
        });
    }

    private sealed class DeferredExport : IReportExportCoordinator
    {
        public TaskCompletionSource Completion { get; } = new();
        public int Calls { get; private set; }
        public Task ExportReportExcelAsync(string path, Etp.Reporting.Reporting.ExcelReportMetadata metadata, Etp.Reporting.Reporting.ExcelReportData data, Etp.Reporting.Reporting.VisualReportModel? visual, CancellationToken token = default) { Calls++; return Completion.Task; }
        public Task ExportReportPdfAsync(string path, Etp.Reporting.Reporting.ExcelReportMetadata metadata, Etp.Reporting.Reporting.ExcelReportData data, Etp.Reporting.Reporting.VisualReportModel? visual, Etp.Reporting.Reporting.DailySalesReportDocument? dsr, CancellationToken token = default) { Calls++; return Completion.Task; }
        public Task ExportPackExcelAsync(string path, Etp.Reporting.Reporting.ReportPackDocument document, CancellationToken token = default) => throw new NotSupportedException();
        public Task ExportPackPdfAsync(string path, Etp.Reporting.Reporting.ReportPackDocument document, CancellationToken token = default) => throw new NotSupportedException();
        public Task ExportManagementSummaryPdfAsync(string path, Etp.Reporting.Reporting.ExcelReportMetadata metadata, Etp.Reporting.Reporting.ExcelReportData data, CancellationToken token = default) => throw new NotSupportedException();
    }

    private static ReportsWorkspaceView CreateView(DeferredTrendQuery query, List<ReportPresentationSnapshot> previews, IReportExportCoordinator? exporter = null)
    {
        var view = new ReportsWorkspaceView(() => "synthetic", _ => new SalesQuery(),
            _ => throw new InvalidOperationException("Unexpected operational query"), _ => query,
            exporter ?? new ReportExportCoordinator(), new UnusedDiagnostic());
        view.ApplyScope(new(2026, 9, 1), new(2026, 9, 10), "Titan");
        view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask,
            (snapshot, _, _) => previews.Add(snapshot), _ => { }, _ => { });
        return view;
    }

    private sealed class DeferredTrendQuery : IManagementTrendQuery
    {
        public TaskCompletionSource<IReadOnlyList<ManagementTrendRecord>> Completion { get; } = new();
        public Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default) => Completion.Task;
    }

    private sealed class SalesQuery : IControlledReportQuery
    {
        public Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesSummaryReport(dimension, ReportStatus.Passed, [new("Synthetic", 1m, 42m, 1)], "test", "Synthetic sales"));
        public Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedDiagnostic : ITenderVarianceDiagnostic
    {
        public TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance) => throw new NotSupportedException();
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA test failed.", failure);
    }
}
