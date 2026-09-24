using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class EveningReportInteractionTests
{
    [Theory]
    [InlineData("Expenses")]
    [InlineData("No matching cash entry")]
    public void Cash_grids_preserve_table_headers_and_render_formatted_cells_through_filtering(string initialSearch)
    {
        RunSta(async () =>
        {
            var view = CreateView((_, store, _, _) => Task.FromResult<IReadOnlyList<CashBookDay>>(
                CashDays(store).Select(day => day with { Expenses = 1234567.125m }).ToArray()));
            FrameworkElement? preview = null;
            view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask,
                (snapshot, rows, _) => preview = (FrameworkElement)ReportVisualPresenter.BuildFocusedPreview(snapshot.VisualReport!, rows),
                _ => { }, _ => { });
            ((TextBox)view.FindName("ReportSearchInput")).Text = initialSearch;
            await view.RunReportAsync("cash");
            var reportGrid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(reportGrid);
            var headers = CashBookTables.Create([]).Columns.Select(column => column.Header).ToArray();
            Assert.Equal(headers, reportGrid.Columns.Select(column => column.Header));
            Assert.NotNull(preview);
            Layout(preview);
            var grid = Visuals(preview).OfType<DataGrid>().Single();
            var filter = Visuals(preview).OfType<ReportDetailFilter>().Single();
            filter.Search.Text = "Expenses";
            Layout(preview);
            Assert.Equal(headers, Visuals(grid).OfType<DataGridColumnHeader>()
                .Where(header => header.Content is string).OrderBy(header => header.DisplayIndex).Select(header => header.Content));
            var row = grid.Items[0];
            Assert.Equal("24 Aug 2026", Assert.IsType<TextBlock>(grid.Columns[0].GetCellContent(row)).Text);
            Assert.Equal("WLMHW", Assert.IsType<TextBlock>(grid.Columns[1].GetCellContent(row)).Text);
            Assert.Equal("Expenses", Assert.IsType<TextBlock>(grid.Columns[2].GetCellContent(row)).Text);
            var amount = Assert.IsType<TextBlock>(grid.Columns[3].GetCellContent(row));
            Assert.Equal("12,34,567.13", amount.Text);
            Assert.Equal(TextAlignment.Right, amount.TextAlignment);
            Assert.Equal("Opening balance", Assert.IsType<TextBlock>(grid.Columns[4].GetCellContent(row)).Text);
            Assert.Equal("100.00", Assert.IsType<TextBlock>(grid.Columns[5].GetCellContent(row)).Text);
            filter.Search.Text = "Bank cash deposit";
            Layout(preview);
            Assert.Equal("—", Assert.IsType<TextBlock>(grid.Columns[5].GetCellContent(grid.Items[0])).Text);
            filter.Search.Text = "No matching cash entry";
            Layout(preview);
            Assert.Empty(grid.Items.Cast<object>());
            Assert.Equal(headers, grid.Columns.Select(column => column.Header));
            filter.Search.Clear();
            Layout(preview);
            Assert.NotEmpty(grid.Items.Cast<object>());
            Assert.Equal("24 Aug 2026", Assert.IsType<TextBlock>(grid.Columns[0].GetCellContent(grid.Items[0])).Text);
        });
    }

    [Fact]
    public void Cash_grid_keeps_its_table_schema_when_the_source_contains_no_days()
    {
        RunSta(async () =>
        {
            var view = CreateView((_, _, _, _) => Task.FromResult<IReadOnlyList<CashBookDay>>([]));
            FrameworkElement? preview = null;
            view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask,
                (snapshot, rows, _) => preview = (FrameworkElement)ReportVisualPresenter.BuildFocusedPreview(snapshot.VisualReport!, rows),
                _ => { }, _ => { });
            await view.RunReportAsync("cash");
            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            Assert.Empty(grid.Items.Cast<object>());
            Assert.Equal(CashBookTables.Create([]).Columns.Select(column => column.Header), grid.Columns.Select(column => column.Header));
            Assert.NotNull(preview);
            Layout(preview);
            var previewGrid = Visuals(preview).OfType<DataGrid>().Single();
            Assert.Empty(previewGrid.Items.Cast<object>());
            Assert.Equal(CashBookTables.Create([]).Columns.Select(column => column.Header), previewGrid.Columns.Select(column => column.Header));
        });
    }

    [Fact]
    public void Staff_performance_headers_name_the_agreed_invoice_metrics_without_renaming_other_reports()
    {
        RunSta(() =>
        {
            var grid = new DataGrid { ItemsSource = new StaffPerformanceRecord[]
                { new("WLMHW", "001", 200m, null, null, "Missing", 3m, 0m, 2, 1.5m, 100m, 1m, null, null, 1, "Synthetic CRO") } };
            TablePresentation.Configure(grid);
            Assert.Equal("Unique invoices", grid.Columns[8].Header);
            Assert.Equal("AUPT", grid.Columns[9].Header);
            Assert.Equal("ATV", grid.Columns[10].Header);
            var otherReport = new DataGrid { ItemsSource = new[] { new { Transactions = 2, Upt = 1.5m, Atv = 100m } } };
            TablePresentation.Configure(otherReport);
            Assert.Equal(new[] { "Transactions", "Upt", "Atv" }, otherReport.Columns.Select(column => column.Header));
            return Task.CompletedTask;
        });
    }

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
            view.SetStores(TestStoreCatalog.Create());
        view.ApplyScope(new(2026, 8, 24), new(2026, 8, 25), requested);
            await view.RunReportAsync("cash");
            Assert.Equal(expectedCode, loadedStore);
            Assert.DoesNotContain("Select a store", ((TextBlock)view.FindName("ReportResult")).Text);
            var focused = new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("cash"));
            focused.SetStores(TestStoreCatalog.Create(),requested);
            Assert.Equal($"{expectedLabel} ({expectedCode})", focused.ScopeSelector.SelectedItem);
            Assert.Equal(headerAfter, ReportTaskScope.StoreIndexForReport("cash", headerBefore,2));
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
        view.SetStores(TestStoreCatalog.Create());
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

    private static void Layout(FrameworkElement root)
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            root.Measure(new Size(1300, 650));
            root.Arrange(new Rect(0, 0, 1300, 650));
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Visuals(child)) yield return descendant;
        }
    }
}
