using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.8, report audit of 9 Oct 2026, lane REPORTS-MISC. RA-UI-16 / RA-EXPORT-09: "Variance only" applies only to a row
/// type with a variance column and is otherwise disabled with a hint. RA-SALES-08: the Returns report is keyed by store
/// and brand and counts return documents. RA-STOCK-08: Physical Stock hides its two internal columns. RA-UI-21: Favourites
/// uses the catalogue's tiles. RA-UI-26: the footer status is replaced on navigation. RA-UI-17: an empty Management Trend
/// window is a clean "No data", and a To date before From (the ArgumentException logged at 20:18:18) is a prompt.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ReportsMiscFixTests
{
    [Theory]
    [InlineData(typeof(TenderDocumentRecord), "Variance")]
    [InlineData(typeof(TenderVarianceDiagnosticRecord), "Variance")]
    [InlineData(typeof(StockControlRecord), "Variance")]
    [InlineData(typeof(DailyExceptionRecord), "Variance")]
    [InlineData(typeof(ManagementTrendRecord), "TenderVariance")]
    [InlineData(typeof(PhysicalStockRecord), "SystemVariance")]
    [InlineData(typeof(SalesSummaryRecord), null)]
    [InlineData(typeof(InvoiceSummaryRecord), null)]
    [InlineData(typeof(InvoiceLineageRecord), null)]
    [InlineData(typeof(StaffPerformanceRecord), null)]
    [InlineData(typeof(ServiceSalesRecord), null)]
    [InlineData(typeof(StockInventoryRecord), null)]
    [InlineData(typeof(StockMovementRecord), null)]
    [InlineData(typeof(DataRowView), null)]
    [InlineData(null, null)]
    public void Variance_column_is_the_row_types_variance_property_or_none(Type? type, string? expected)
        => Assert.Equal(expected, ReportGridColumns.VarianceProperty(type));

    [Fact]
    public void Variance_only_is_disabled_with_a_hint_for_a_report_without_a_variance_column()
    {
        RunSta(() =>
        {
            var rows = new[] { new SalesSummaryRecord("TITAN", 2m, 236m, 2, 1), new SalesSummaryRecord("HELIOS", 1m, 118m, 1, 0) };
            var grid = new DataGrid();
            var filter = new ReportDetailFilter(grid, rows);
            Assert.False(filter.VarianceOnly.IsEnabled);
            Assert.NotEqual(true, filter.VarianceOnly.IsChecked);
            Assert.Equal(ReportDetailFilter.NoVarianceHint, filter.VarianceOnly.ToolTip);
            Assert.True(ToolTipService.GetShowOnDisabled(filter.VarianceOnly));
            Assert.Equal(2, grid.Items.Count);

            // A typed empty list still identifies its row type.
            var empty = new ReportDetailFilter(new DataGrid(), Array.Empty<InvoiceSummaryRecord>());
            Assert.False(empty.VarianceOnly.IsEnabled);
            Assert.Null(ReportDetailFilter.RowType(null));
            Assert.Equal(typeof(TenderDocumentRecord), ReportDetailFilter.RowType(Array.Empty<TenderDocumentRecord>()));
        });
    }

    [Fact]
    public void Variance_only_filters_management_trend_on_its_tender_variance()
    {
        RunSta(() =>
        {
            var rows = new[]
            {
                new ManagementTrendRecord(new(2026, 8, 24), "WLMHW", 100m, 1m, 1, 0, 0m, 0),
                new ManagementTrendRecord(new(2026, 8, 25), "WLMHW", 200m, 2m, 2, 0, 5m, 0),
                new ManagementTrendRecord(new(2026, 8, 26), "HEMW", 300m, 3m, 3, 0, null, 0)
            };
            var grid = new DataGrid();
            var filter = new ReportDetailFilter(grid, rows);
            Assert.True(filter.VarianceOnly.IsEnabled);
            Assert.Null(filter.VarianceOnly.ToolTip);
            filter.VarianceOnly.IsChecked = true;
            Assert.Same(rows[1], Assert.Single(grid.Items.Cast<object>()));
            filter.VarianceOnly.IsChecked = false;
            Assert.Equal(3, grid.Items.Count);
        });
    }

    [Fact]
    public void Legacy_variance_box_follows_the_report_row_type()
    {
        RunSta(async () =>
        {
            var view = ReportGridPresentationTests.CreateView(out _);
            var box = (CheckBox)view.FindName("VarianceOnlyInput");
            var grid = (DataGrid)view.FindName("ReportGrid");
            box.IsChecked = true;
            await view.RunReportAsync("sales-brand");
            Assert.False(box.IsEnabled);
            Assert.NotEqual(true, box.IsChecked);
            Assert.Equal(ReportDetailFilter.NoVarianceHint, box.ToolTip);
            Assert.NotEmpty(grid.Items.Cast<object>());

            // The fake stock reconciliation row has a zero variance, so the filter empties the grid and nothing else.
            await view.RunReportAsync("stock-variance");
            Assert.True(box.IsEnabled);
            Assert.Null(box.ToolTip);
            Assert.NotEmpty(grid.Items.Cast<object>());
            box.IsChecked = true;
            Assert.Empty(grid.Items.Cast<object>());
            box.IsChecked = false;
            Assert.NotEmpty(grid.Items.Cast<object>());
        });
    }

    [Fact]
    public void Physical_stock_hides_counted_physical_and_composition_variance_on_screen_and_in_row_details()
    {
        RunSta(async () =>
        {
            var view = ReportGridPresentationTests.CreateView(out var latest);
            await view.RunReportAsync("stock-physical");
            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            var headers = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.DoesNotContain("Counted physical", headers);
            Assert.DoesNotContain("Composition variance", headers);
            Assert.Equal(latest().ExportData!.Columns.Select(column => column.Header).ToArray(), headers);

            var details = TablePresentation.DescribeRow(new PhysicalStockRecord("WLMHW", new(2026, 8, 25), "TITAN", 1m, 2m, 0m, 0m, 3m, 3m, null, 3m, 0m, null, "PASS"));
            Assert.DoesNotContain(details, item => item.Label is "Counted physical" or "Composition variance" or "Counted Physical Quantity");
            Assert.Equal("0.00", details.Single(item => item.Label == "System Variance").Value);
        });
    }

    [Fact]
    public void Returns_report_export_explains_its_store_and_brand_rows()
    {
        RunSta(async () =>
        {
            var view = ReportGridPresentationTests.CreateView(out var latest);
            await view.RunReportAsync("sales-returns");
            Assert.Contains(ReportsWorkspaceView.ReturnsNote, latest().ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(ReportsWorkspaceView.InvoicesNote, latest().ExportMetadata!.Message, StringComparison.Ordinal);
            await view.RunReportAsync("sales-brand");
            Assert.Contains(ReportsWorkspaceView.InvoicesNote, latest().ExportMetadata!.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Favourites_show_every_favourite_as_a_catalogue_tile_in_the_same_columns()
    {
        RunSta(() =>
        {
            var access = new ShellAccess(true, true, true, true);
            var favourites = new FavouriteReportsView(UiPreferences.Default, access, _ => { });
            var catalogue = new ReportListView(access, _ => { });
            Layout(favourites, 1100); Layout(catalogue, 1100);
            var tiles = Descendants(favourites).OfType<UniformGrid>().Single();
            var reference = Descendants(catalogue).OfType<UniformGrid>().Single();
            var buttons = tiles.Children.OfType<Button>().ToArray();
            Assert.Equal(UiPreferences.Default.FavouriteReportCodes.Count, buttons.Length);
            Assert.Equal(UiPreferences.Default.FavouriteReportCodes.Select(code => TaskNavigation.Find("report-" + code)!.Title), buttons.Select(button => (string)button.Content));
            var sample = reference.Children.OfType<Button>().First();
            Assert.All(buttons, button =>
            {
                Assert.Equal(sample.MinHeight, button.MinHeight);
                Assert.Equal(sample.Padding, button.Padding);
                Assert.Equal(sample.Margin, button.Margin);
                Assert.Equal(sample.HorizontalContentAlignment, button.HorizontalContentAlignment);
                Assert.True(button.ActualHeight < 90, $"{button.Content} tile is {button.ActualHeight} DIP tall");
            });
            Assert.Equal(reference.Columns, tiles.Columns);
            Assert.Equal(3, tiles.Columns);
            Layout(favourites, 700);
            Assert.Equal(ReportListView.ColumnsFor(favourites.ActualWidth), tiles.Columns);
            Assert.Equal(2, tiles.Columns);

            var selector = Descendants(favourites).OfType<ComboBox>().Single();
            Assert.Equal(FavouriteReportsView.AllCategories, selector.SelectedItem);
            selector.SelectedItem = TaskNavigation.Find("report-stock-closing")!.Category;
            Assert.Single(tiles.Children.OfType<Button>(), button => (string)button.Content == TaskNavigation.Find("report-stock-closing")!.Title);
        });
    }

    [Theory]
    [InlineData("favourite-reports")]
    [InlineData("reports-list")]
    public void Footer_status_is_replaced_when_the_screen_changes(string taskId)
    {
        RunSta(() =>
        {
            var window = Phase3ShellTests.CreateWindow();
            try
            {
                window.ApplicationStatus.Text = "6 saved pack(s) found for the archive.";
                var navigator = new TaskNavigator(window);
                var task = TaskNavigation.Find(taskId)!;
                Assert.True(navigator.DisplayTaskRoute(task.Route));
                Assert.Equal(task.Path, window.ApplicationStatus.Text);
            }
            finally { window.importWorkspaceView.DisposeAsync().AsTask().GetAwaiter().GetResult(); window.Close(); }
        });
    }

    [Fact]
    public void Management_trend_chart_accepts_an_empty_window()
    {
        RunSta(() =>
        {
            var columns = new ExcelReportColumn[] { new("Date"), new("Store"), new("Net Sales", "#,##0.00") };
            var panel = ManagementTrendChart.Create(new ExcelReportData(columns, []));
            Assert.Empty(panel.Children);
            var one = ManagementTrendChart.Create(new ExcelReportData(columns, [[new DateOnly(2026, 8, 25), "WLMHW", 236m]]));
            Assert.Single(one.Children);
        });
    }

    [Fact]
    public void Empty_management_trend_window_is_no_data_without_a_diagnostics_error()
    {
        RunSta(async () =>
        {
            FrameworkElement? preview = null;
            var view = ReportGridPresentationTests.CreateView(out var latest,
                (snapshot, rows, _) => { if (snapshot.VisualReport is not null) preview = Layout((FrameworkElement)ReportVisualPresenter.BuildFocusedPreview(snapshot.VisualReport, rows), 1300); },
                empty: true);
            await view.RunReportAsync("management-trend");
            Assert.Equal("No data for 25 Aug 2026, Titan World (WLMHW).", ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.NotNull(preview);
            Assert.Empty(Descendants(preview!).OfType<DataGrid>().Single().Items.Cast<object>());
            Assert.False(latest().CanExportReport);
            AssertNoTrendFailureLogged();
        });
    }

    [Fact]
    public void A_to_date_before_from_or_an_over_long_trend_window_is_a_prompt_not_a_failure()
    {
        RunSta(async () =>
        {
            string? status = null;
            var view = ReportGridPresentationTests.CreateView(out _, (_, _, text) => status = text);
            // ApplyScope clamps From to To, so the slip is made the way the audit made it: in the To picker.
            ((DatePicker)view.FindName("ReportTo")).SelectedDate = new DateTime(2026, 8, 20);
            await view.RunReportAsync("management-trend");
            Assert.Equal(ReportsWorkspaceView.DateOrderPrompt, ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.Equal(ReportsWorkspaceView.DateOrderPrompt, status);
            Assert.False(((Button)view.FindName("ExportExcelButton")).IsEnabled);

            view.ApplyScope(new DateTime(2025, 7, 1), new DateTime(2026, 8, 25), "Titan World");
            await view.RunReportAsync("management-trend");
            Assert.Equal(ReportsWorkspaceView.TrendPeriodPrompt, ((TextBlock)view.FindName("ReportResult")).Text);

            // Other reports accept a long window; a snapshot report reads only its To date.
            await view.RunReportAsync("sales-brand");
            Assert.StartsWith("Passed", ((TextBlock)view.FindName("ReportResult")).Text, StringComparison.Ordinal);
            AssertNoTrendFailureLogged();
        });
    }

    [Theory]
    [InlineData("management-trend", "2026-08-25", "2026-08-20", ReportsWorkspaceView.DateOrderPrompt)]
    [InlineData("sales-brand", "2026-08-25", "2026-08-20", ReportsWorkspaceView.DateOrderPrompt)]
    [InlineData("management-trend", "2025-07-01", "2026-08-25", ReportsWorkspaceView.TrendPeriodPrompt)]
    [InlineData("management-trend", "2025-08-24", "2026-08-25", null)]
    [InlineData("sales-brand", "2025-07-01", "2026-08-25", null)]
    [InlineData("stock-closing", "2026-08-25", "2026-08-20", null)]
    [InlineData("management-trend", "2026-08-25", "2026-08-25", null)]
    public void Date_prompt_covers_order_and_the_trend_limit(string report, string from, string to, string? expected)
        => Assert.Equal(expected, ReportsWorkspaceView.DatePrompt(report, DateTime.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateTime.Parse(to, System.Globalization.CultureInfo.InvariantCulture)));

    private static void AssertNoTrendFailureLogged()
    {
        foreach (var log in Directory.GetFiles(DiagnosticsIsolation.LogDirectory, "diagnostics-*.jsonl"))
            Assert.DoesNotContain("MANAGEMENT_TREND_REPORT_FAILED", File.ReadAllText(log), StringComparison.Ordinal);
    }

    private static FrameworkElement Layout(FrameworkElement root, double width)
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            root.Measure(new Size(width, 650));
            root.Arrange(new Rect(0, 0, width, 650));
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
        return root;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Reports misc fix test failed", failure);
    }

    private static void RunSta(Action action) => RunSta(() => { action(); return Task.CompletedTask; });
}
