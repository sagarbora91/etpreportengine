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

    private static ReportsWorkspaceView CreateView(DeferredTrendQuery query, List<ReportPresentationSnapshot> previews)
    {
        var view = new ReportsWorkspaceView(() => "synthetic", _ => new SalesQuery(),
            _ => throw new InvalidOperationException("Unexpected operational query"), _ => query,
            new ReportExportCoordinator(), new UnusedDiagnostic());
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
