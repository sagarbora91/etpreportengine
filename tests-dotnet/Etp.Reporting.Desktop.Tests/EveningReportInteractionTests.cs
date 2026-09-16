using System.Windows.Controls;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class EveningReportInteractionTests
{
    [Theory]
    [InlineData("dsr", "HEMW", 1)]
    [InlineData("invoice", "Second customer", 1)]
    [InlineData("cash", "Expenses", 2)]
    public void Initial_report_run_applies_search_and_clearing_it_restores_source_rows(string report, string search, int expected)
    {
        RunSta(async () =>
        {
            var view = CreateView();
            ((TextBox)view.FindName("ReportSearchInput")).Text = search;
            await view.RunReportAsync(report);
            var grid = (DataGrid)view.FindName("ReportGrid");
            Assert.Equal(expected, grid.Items.Count);
            Assert.DoesNotContain("failed", ((TextBlock)view.FindName("ReportResult")).Text, StringComparison.OrdinalIgnoreCase);
            ((TextBox)view.FindName("ReportSearchInput")).Clear();
            Assert.True(grid.Items.Count > expected);
        });
    }

    [Theory]
    [InlineData("dsr")]
    [InlineData("invoice")]
    [InlineData("cash")]
    public void Initial_report_run_applies_variance_filter_without_mutating_export_rows(string report)
    {
        RunSta(async () =>
        {
            var view = CreateView();
            ReportPresentationSnapshot? snapshot = null;
            view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask, (value, _, _) => snapshot = value, _ => { }, _ => { });
            ((CheckBox)view.FindName("VarianceOnlyInput")).IsChecked = true;
            await view.RunReportAsync(report);
            var grid = (DataGrid)view.FindName("ReportGrid");
            Assert.Empty(grid.Items.Cast<object>());
            Assert.NotNull(snapshot?.ExportData);
            var exported = snapshot!.ExportData;
            ((CheckBox)view.FindName("VarianceOnlyInput")).IsChecked = false;
            Assert.NotEmpty(grid.Items.Cast<object>());
            Assert.Same(exported, snapshot.ExportData);
        });
    }

    [Theory]
    [InlineData("Both stores", "WLMHW", "Titan World", 2, 0)]
    [InlineData("Titan World", "WLMHW", "Titan World", 0, 0)]
    [InlineData("Helios", "HEMW", "Helios", 1, 1)]
    public void Cash_opens_with_a_usable_store_and_preserves_an_explicit_store(string requested, string expectedCode, string expectedLabel, int headerBefore, int headerAfter)
    {
        RunSta(async () =>
        {
            string? loadedStore = null;
            var view = CreateView((_, store, _, _) => { loadedStore = store; return Task.FromResult<IReadOnlyList<CashBookDay>>(CashDays(store)); });
            view.ApplyScope(new(2026, 8, 24), new(2026, 8, 25), requested);
            await view.RunReportAsync("cash");
            Assert.Equal(expectedCode, loadedStore);
            Assert.DoesNotContain("Select a store", ((TextBlock)view.FindName("ReportResult")).Text);
            var focused = new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("cash"));
            focused.ConfigureTaskScope(requested);
            Assert.Equal(expectedLabel, focused.ScopeSelector.SelectedItem);
            Assert.Equal(headerAfter, ReportTaskScope.StoreIndexForReport("cash", headerBefore));
        });
    }

    [Theory]
    [InlineData("invoice", "Customer-wise Invoices")]
    [InlineData("cash", "Cash Book")]
    public void Navigation_and_report_titles_use_the_evening_report_names(string code, string label)
    {
        var destination = TaskNavigation.Find("report-" + code);
        Assert.NotNull(destination);
        Assert.Equal(label, destination.Title);
        Assert.Equal(label, ReportWorkspaceDefinition.ForReport(code).DisplayName);
        Assert.Contains(TaskNavigation.Search(label, ShellAccess.StoreManager), x => x.ReportCode == code);
    }

    private static ReportsWorkspaceView CreateView(Func<string, string, DateOnly, DateOnly, Task<IReadOnlyList<CashBookDay>>>? cash = null)
    {
        var query = new OperationalQuery();
        var view = new ReportsWorkspaceView(() => "synthetic", _ => throw new NotSupportedException(), _ => query,
            _ => throw new NotSupportedException(), new ReportExportCoordinator(), new UnusedDiagnostic(),
            cash ?? ((_, store, _, _) => Task.FromResult<IReadOnlyList<CashBookDay>>(CashDays(store))));
        view.ApplyScope(new(2026, 8, 24), new(2026, 8, 25), "Titan World");
        return view;
    }

    private static CashBookDay[] CashDays(string store) => new[] { 24, 25 }.Select(day =>
        new CashBookDay(new(2026, 8, day), store, 100m, "Synthetic opening", new Dictionary<string, decimal> { ["Cash"] = 118m },
            20m, 10m, 5m, 5m, 10m, 0m, 223m, null, 0m, 0m, "Complete")).ToArray();

    private sealed class OperationalQuery : IOperationalReportQuery<DailySalesReportDocument>
    {
        public Task<IReadOnlyList<InvoiceSummaryRecord>> LoadInvoiceSummaryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceSummaryRecord>>([new(new(2026, 8, 25), "WLMHW", "FIRST", "INV", 1m, 118m, 1, "First customer"), new(new(2026, 8, 25), "HEMW", "SECOND", "INV", 1m, 236m, 1, "Second customer")]);
        public Task<IReadOnlyList<DsrManagementRecord>> LoadDsrAsync(DateOnly businessDate, IReadOnlyList<string> storeCodes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DsrManagementRecord>>([Dsr("WLMHW"), Dsr("HEMW")]);
        public Task<DailySalesReportDocument> ComposeDsrDocumentAsync(DateOnly businessDate, IReadOnlyList<DsrManagementRecord> rows, CancellationToken cancellationToken = default) =>
            Task.FromResult(DailySalesReportBuilder.Build(businessDate, [], [], new Dictionary<string, decimal?>()));
        private static DsrManagementRecord Dsr(string store) => new("FTD", store, new(2026, 8, 25), new(2026, 8, 25), 118m, null, null, "Missing", 1m, null, 1, null, 1m, 118m, 10m, 10m, "Synthetic");
        public Task<IReadOnlyList<InvoiceLineageRecord>> LoadInvoiceLineageAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffPerformanceReport> LoadStaffPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceSalesRecord>> LoadServiceSalesAsync(DateOnly businessDate, IReadOnlyList<string>? storeCodes = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashReconciliationReport> LoadCashReconciliationAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhysicalStockRecord>> LoadPhysicalStockAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StockInventoryRecord>> LoadStockInventoryAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DailyExceptionRecord>> LoadDailyExceptionsAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedDiagnostic : ITenderVarianceDiagnostic
    {
        public TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance) => throw new NotSupportedException();
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Evening report interaction failed", failure);
    }
}
