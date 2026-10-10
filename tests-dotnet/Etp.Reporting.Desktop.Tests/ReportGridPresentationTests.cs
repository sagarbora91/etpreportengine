using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;
using Etp.Reporting.TestSupport;
using TenderVarianceCause = Etp.Reporting.Application.Reports.TenderVarianceCause;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.7, report audit of 9 Oct 2026, lane REPORTS-UI. RA-EXPORT-06/07/08, RA-SALES-06, RA-UI-14/16/18, RA-OPS-16: the
/// detail grid shows the Excel export's headers in the export's order, stock quantities carry no ₹, enum and status
/// values read as words, years are not grouped, internal columns are hidden. RA-UI-04: a task scope never opens with
/// From after To. RA-UI-19: the store prompt is a status line, and row details use the grid's headers. RA-UI-15/16:
/// a 0-row result says "No data for <window>" and cannot be exported. RA-OPS-03: pointers name the entry screens.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ReportGridPresentationTests
{
    // RA-STOCK-08 (1.9.8): "Counted physical" and "Composition variance" are no longer screen-only; they are hidden like the export.
    private static readonly string[] ScreenOnlyHeaders = ["Transaction types", "Brand row"];

    [Theory]
    [InlineData("sales-brand")]
    [InlineData("invoice")]
    [InlineData("invoice-lineage")]
    [InlineData("staff")]
    [InlineData("service")]
    [InlineData("tender")]
    [InlineData("tender-diagnostic")]
    [InlineData("stock-variance")]
    [InlineData("stock-movement")]
    [InlineData("stock-physical")]
    [InlineData("stock-closing")]
    [InlineData("stock-brand")]
    [InlineData("exceptions")]
    [InlineData("management-trend")]
    public void Grid_headers_are_the_export_headers_in_export_order(string code)
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync(code);
            var export = latest().ExportData!.Columns.Select(column => column.Header).ToArray();
            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            var headers = grid.Columns.Select(column => ((string)column.Header).Replace(" ₹", string.Empty, StringComparison.Ordinal)).ToArray();
            Assert.True(IsSubsequence(export, headers), $"{code}: export [{string.Join(" | ", export)}] is not shown in order by grid [{string.Join(" | ", headers)}]");
            Assert.All(headers.Except(export), header => Assert.Contains(header, ScreenOnlyHeaders));
            Assert.DoesNotContain(headers, header => header.Contains("Source Signed", StringComparison.Ordinal) || header is "Key" or "Cro Number" or "Upi" or "Wdc" or "Invoice Year" or "Source Rows");
        });
    }

    [Theory]
    [InlineData("stock-variance")]
    [InlineData("stock-physical")]
    public void Stock_quantities_never_carry_a_rupee_sign(string code)
    {
        RunSta(async () =>
        {
            var view = CreateView(out _);
            await view.RunReportAsync(code);
            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            Assert.DoesNotContain(grid.Columns, column => ((string)column.Header).Contains('₹'));
        });
    }

    [Fact]
    public void Enum_and_status_values_read_as_words_and_years_are_not_grouped()
    {
        RunSta(() =>
        {
            var diagnostic = new DataGrid { ItemsSource = new[] { new TenderVarianceDiagnosticRecord("WLMHW", "D1", 118m, 0m, 118m, TenderVarianceCause.TenderWithoutInvoice, "Check") } };
            TablePresentation.Configure(diagnostic);
            Assert.Equal("Tender without invoice", Cell(diagnostic, "Likely Cause", TenderVarianceCause.TenderWithoutInvoice));

            var staff = new DataGrid { ItemsSource = new[] { new StaffPerformanceRecord("WLMHW", "001", 200m, null, null, "MissingSource", 3m, 0m, 2, 1.5m, 100m, 1m, null, null, 1, "CRO") } };
            TablePresentation.Configure(staff);
            Assert.Equal("Missing source", Cell(staff, "Growth Status", "MissingSource"));

            var tender = new DataGrid { ItemsSource = new[] { new TenderDocumentRecord("WLMHW", "D1", 118m, 118m, 0m, ReportStatus.Passed, 2027) } };
            TablePresentation.Configure(tender);
            Assert.DoesNotContain(tender.Columns, column => ((string)column.Header).Contains("Year", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Passed", Cell(tender, "Status", ReportStatus.Passed));

            var year = new DataGrid { ItemsSource = new[] { new { FinancialYear = 2027, Rows = 1234 } } };
            TablePresentation.Configure(year);
            Assert.Equal("2027", Cell(year, "Financial Year", 2027));
            Assert.Equal("1,234", Cell(year, "Rows", 1234));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("Passed", "Passed")]
    [InlineData("NotRun", "Not run")]
    [InlineData("MissingTender", "Missing tender")]
    [InlineData("TenderWithoutInvoice", "Tender without invoice")]
    [InlineData("BLOCKER", "BLOCKER")]
    [InlineData("Partial — 28 missing days", "Partial — 28 missing days")]
    [InlineData("R022 imported", "R022 imported")]
    public void Spaced_words_only_split_pascal_case_names(string value, string expected) => Assert.Equal(expected, TablePresentation.SpacedWords(value));

    [Fact]
    public void Row_details_use_the_grid_headers_and_formats_and_hide_internal_columns()
    {
        var staff = TablePresentation.DescribeRow(new StaffPerformanceRecord("WLMHW", "001", 1234567.125m, null, null, "MissingSource", 3m, 0m, 2, 1.5m, 100m, 1m, null, null, 1, "Synthetic CRO"));
        Assert.Equal(["Store", "CRO", "CRO name", "Value incl. GST ₹"], staff.Take(4).Select(item => item.Label).ToArray());
        Assert.Equal("12,34,567.13", staff.Single(item => item.Label == "Value incl. GST ₹").Value);
        Assert.Equal("—", staff.Single(item => item.Label == "LY Sales ₹").Value);
        Assert.Equal("Missing source", staff.Single(item => item.Label == "Growth Status").Value);
        Assert.DoesNotContain(staff, item => item.Label is "Cro Number" or "NetSales" or "StoreCode");

        var tender = TablePresentation.DescribeRow(new TenderDocumentRecord("WLMHW", "D1", 118m, 118m, 0m, ReportStatus.Passed, 2027));
        Assert.DoesNotContain(tender, item => item.Label.Contains("Year", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Passed", tender.Single(item => item.Label == "Status").Value);

        var movement = TablePresentation.DescribeRow(new StockMovementRecord("WLMHW", "ITEM", "SR", -1m, "RETAILBIN", false));
        Assert.Equal(["Store", "Item", "Location", "Movement Type", "Signed Quantity", "Snapshot"], movement.Select(item => item.Label).ToArray());
        Assert.Equal("no snapshot", movement[^1].Value);
    }

    [Fact]
    public void Task_scope_never_opens_with_from_after_to()
    {
        // RA-UI-04: Today > Cash after a back-dated business date applied From = today, To = business date.
        var today = new DateTime(2026, 10, 9);
        var businessDate = new DateTime(2026, 9, 26);
        Assert.Equal((businessDate, businessDate), ReportsWorkspaceView.ClampedRange(today, businessDate));
        Assert.Equal((businessDate, businessDate), ReportsWorkspaceView.ClampedRange(null, businessDate));
        Assert.Equal((new DateTime(2026, 9, 1), businessDate), ReportsWorkspaceView.ClampedRange(new DateTime(2026, 9, 1), businessDate));
        Assert.Equal((today, today), ReportsWorkspaceView.ClampedRange(today, null));
        RunSta(() =>
        {
            var view = CreateView(out _);
            view.ApplyScope(today, businessDate, "Titan World");
            Assert.Equal(businessDate, view.DateFrom);
            Assert.Equal(businessDate, view.DateTo);
            view.ApplyScope(new DateTime(2026, 9, 1), new DateTime(2026, 9, 28), "Titan World");
            view.SetBusinessDate(new DateTime(2026, 8, 20));
            Assert.Equal(new DateTime(2026, 8, 20), view.DateFrom);
            Assert.Equal(new DateTime(2026, 8, 20), view.DateTo);
            view.SetBusinessDate(new DateTime(2026, 8, 25));
            Assert.Equal(new DateTime(2026, 8, 20), view.DateFrom);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("sales-store")]
    [InlineData("stock-physical")]
    [InlineData("exceptions")]
    [InlineData("cash")]
    public void Missing_store_is_a_prompt_not_a_failure(string code)
    {
        RunSta(async () =>
        {
            string? status = null;
            var view = CreateView(out _, (_, _, text) => status = text);
            view.ApplyScope(new DateTime(2026, 8, 25), new DateTime(2026, 8, 25), StoreScopeCatalog.AllStores);
            await view.RunReportAsync(code);
            Assert.Equal(ReportsWorkspaceView.StoreRequiredPrompt, ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.Equal(ReportsWorkspaceView.StoreRequiredPrompt, status);
            Assert.False(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            foreach (var log in Directory.GetFiles(DiagnosticsIsolation.LogDirectory, "diagnostics-*.jsonl"))
                Assert.DoesNotContain("REPORT_STORE_REQUIRED", File.ReadAllText(log), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Empty_window_says_no_data_and_cannot_be_exported()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest, empty: true);
            await view.RunReportAsync("sales-brand");
            var text = ((TextBlock)view.FindName("ReportResult")).Text;
            Assert.Equal("No data for 25 Aug 2026, Titan World (WLMHW).", text);
            Assert.DoesNotContain("Passed", text, StringComparison.Ordinal);
            Assert.False(latest().CanExportReport);
            Assert.Equal("Blocked", latest().ExportMetadata!.Status);
            Assert.False(((Button)view.FindName("ExportExcelButton")).IsEnabled);
            Assert.False(((Button)view.FindName("ExportPdfButton")).IsEnabled);

            await view.RunReportAsync("stock-closing");
            Assert.Equal("No data for 25 Aug 2026, Titan World (WLMHW). No closing-stock snapshot for WLMHW on 25 Aug 2026. Slow stock uses 60-day watch and 90-day exception bands.",
                ((TextBlock)view.FindName("ReportResult")).Text);

            // No exceptions is the finding itself, not missing data.
            await view.RunReportAsync("exceptions");
            Assert.Equal("Passed: 0 exception(s).", ((TextBlock)view.FindName("ReportResult")).Text);
            Assert.False(latest().CanExportReport);
        });
    }

    [Fact]
    public void No_data_status_keeps_a_blocked_explanation_and_drops_zero_counts()
    {
        var from = new DateOnly(2024, 3, 1); var to = new DateOnly(2024, 3, 31);
        Assert.Null(ReportsWorkspaceView.NoDataStatus("sales-brand", 3, from, to, "All stores", "Passed: Sales incl. GST 1.00"));
        Assert.Null(ReportsWorkspaceView.NoDataStatus("tender-diagnostic", 0, from, to, "All stores", "Blocked: 0 documents require review"));
        Assert.Equal("No data for 01 Mar 2024 – 31 Mar 2024, All stores.",
            ReportsWorkspaceView.NoDataStatus("sales-brand", 0, from, to, "All stores", "Passed: Sales incl. GST 0.00; units 0.00. Aggregated source-signed values."));
        Assert.Equal("No data for 01 Mar 2024 – 31 Mar 2024, All stores.",
            ReportsWorkspaceView.NoDataStatus("invoice", 0, from, to, "All stores", "0 invoices, 0 returns; 0 missing customer names."));
        Assert.Equal("No data for 01 Mar 2024 – 31 Mar 2024, All stores.",
            ReportsWorkspaceView.NoDataStatus("management-trend", 0, from, to, "All stores", "Blocked: 0 daily management trend row(s)."));
        Assert.Equal("No data for 01 Mar 2024 – 31 Mar 2024, Titan World (WLMHW). Opening carries forward from the previous calculated closing.",
            ReportsWorkspaceView.NoDataStatus("cash", 0, from, to, "Titan World (WLMHW)", "0 days. Opening carries forward from the previous calculated closing."));
        Assert.Equal("No data for 31 Mar 2024, All stores. Ledger covers to 25 Aug 2026 for WLMHW.",
            ReportsWorkspaceView.NoDataStatus("stock-variance", 0, to, to, "All stores", "Blocked: Ledger covers to 25 Aug 2026 for WLMHW."));
    }

    [Fact]
    public void Focused_report_keeps_export_actions_disabled_for_a_result_without_rows()
    {
        RunSta(() =>
        {
            var workspace = new ReportWorkspaceControl(ReportWorkspaceDefinition.ForReport("sales-brand"));
            workspace.SetPreview(new TextBlock { Text = "none" }, "No data", null, canExport: false);
            var exports = Buttons(workspace).Where(button => button.Content?.ToString() is "Export PDF" or "Export Excel").ToArray();
            Assert.Equal(2, exports.Length);
            Assert.All(exports, button => Assert.False(button.IsEnabled));
            workspace.SetPreview(new TextBlock { Text = "rows" }, "Passed");
            Assert.All(exports, button => Assert.True(button.IsEnabled));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Availability_and_cash_book_point_to_the_entry_screens()
    {
        var report = DailySalesReportBuilder.Build(new(2026, 8, 25), [], [], new Dictionary<string, decimal?>());
        var availability = DailySalesReportWorkspace.DefaultAvailability(report).ToDictionary(item => item.Label, item => item.Detail);
        Assert.Equal("Enter monthly targets in Settings > Stores & masters > Brands and targets > Monthly targets (Owner).", availability["Targets"]);
        Assert.Equal("Enter walk-ins in Today > Walk-ins.", availability["Walk-ins"]);
        Assert.Equal("Enter service cash, card and UPI in Today > Cash > Cash and service entries.", availability["Service"]);
        Assert.DoesNotContain(availability.Values, detail => detail.Contains("Manual Entry", StringComparison.Ordinal));
        RunSta(async () =>
        {
            var view = CreateView(out _);
            await view.RunReportAsync("cash");
            var text = ((TextBlock)view.FindName("ReportResult")).Text;
            Assert.Contains("Today > Cash > Cash and service entries", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Daily inputs", text, StringComparison.Ordinal);
        });
    }

    private static bool IsSubsequence(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var position = 0;
        foreach (var header in expected)
        {
            while (position < actual.Count && actual[position] != header) position++;
            if (position++ >= actual.Count) return false;
        }
        return true;
    }

    private static string Cell(DataGrid grid, string header, object value)
    {
        var column = (DataGridTextColumn)grid.Columns.Single(column => (string)column.Header == header);
        return (string)((Binding)column.Binding).Converter.Convert(value, typeof(string), null!, PresentationCulture.Indian);
    }

    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button button) yield return button;
            foreach (var nested in Buttons(child)) yield return nested;
        }
    }

    internal static ReportsWorkspaceView CreateView(out Func<ReportPresentationSnapshot> latest, Action<ReportPresentationSnapshot, System.Collections.IEnumerable?, string>? preview = null, bool empty = false)
    {
        ReportPresentationSnapshot? current = null;
        var operational = new OperationalQuery(empty);
        var view = new ReportsWorkspaceView(() => "synthetic", _ => new ControlledQuery(empty), _ => operational,
            _ => new TrendQuery(empty), new ReportExportCoordinator(), new Diagnostic(empty),
            (_, store, _, _) => Task.FromResult<IReadOnlyList<CashBookDay>>(empty ? [] : [new(new(2026, 8, 25), store, 100m, "Synthetic opening", new Dictionary<string, decimal> { ["Cash"] = 118m }, 20m, 10m, 5m, 5m, 10m, 0m, 223m, null, 0m, 0m, "Complete")]));
        view.SetStores(TestStoreCatalog.Create());
        view.ApplyScope(new(2026, 8, 25), new(2026, 8, 25), "Titan World");
        view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask, (snapshot, rows, status) => { current = snapshot; preview?.Invoke(snapshot, rows, status); }, _ => { }, _ => { });
        latest = () => current ?? throw new InvalidOperationException("No report was presented.");
        return view;
    }

    internal sealed class ControlledQuery(bool empty) : IControlledReportQuery
    {
        public Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesSummaryReport(dimension, ReportStatus.Passed, empty ? [] : [new("TITAN", 2m, 236m, 2, 1)], "test", "Aggregated source-signed values without sign transformation."));
        public Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenderReconciliationReport(ReportStatus.Failed, empty ? [] : [new("WLMHW", "D1", 118m, 0m, 118m, ReportStatus.Failed, 2027)], 118m, 0m, 118m, "test", "Compared invoice and tender values."));
        public Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StockReconciliationReport(ReportStatus.Passed, empty ? [] : [new("WLMHW", "ITEM-1", 10m, -2m, 8m, 8m, 0m, ReportStatus.Passed)], "test", "Stock reconciles."));
        public Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockMovementRecord>>(empty ? [] : [new("WLMHW", "ITEM-1", "SR", -1m, "RETAILBIN")]);
    }

    internal sealed class TrendQuery(bool empty) : IManagementTrendQuery
    {
        public Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagementTrendRecord>>(empty ? [] : [new(new(2026, 8, 25), "WLMHW", 236m, 2m, 2, 1, 0m, 0)]);
    }

    internal sealed class Diagnostic(bool empty) : ITenderVarianceDiagnostic
    {
        public TenderVarianceDiagnosticReport Diagnose(TenderReconciliationReport reconciliation, decimal tolerance) =>
            new(ReportStatus.Failed, empty ? [] : [new("WLMHW", "D1", 118m, 0m, 118m, TenderVarianceCause.MissingTender, "Import the R022.")], empty ? 0 : 1, 118m, "test", "Classified.");
    }

    internal sealed class OperationalQuery(bool empty) : IOperationalReportQuery<DailySalesReportDocument>
    {
        public Task<IReadOnlyList<StockInventoryRecord>> LoadStockInventoryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockInventoryRecord>>(empty ? [] : [new(new(2026, 8, 25), "WLMHW", "ITEM-1", "TITAN", "WATCHES", 2m, 1250m, 2500m, new(2026, 3, 1), 177, "EXCEPTION", "R011", null, null, "TITAN")]);
        public Task<IReadOnlyList<InvoiceSummaryRecord>> LoadInvoiceSummaryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceSummaryRecord>>(empty ? [] : [new(new(2026, 8, 25), "WLMHW", "FIRST", "INV", 1m, 118m, 1, "First customer")]);
        public Task<IReadOnlyList<InvoiceLineageRecord>> LoadInvoiceLineageAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceLineageRecord>>(empty ? [] : [new(new(2026, 8, 25), "WLMHW", "FIRST", "1", "ITEM-1", "TITAN", "WATCHES", "INV", 1m, 118m, "001", "R025.xlsx", "Sheet1", 2)]);
        public Task<IReadOnlyList<DsrManagementRecord>> LoadDsrAsync(DateOnly businessDate, IReadOnlyList<string> storeCodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DailySalesReportDocument> ComposeDsrDocumentAsync(DateOnly businessDate, IReadOnlyList<DsrManagementRecord> rows, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffPerformanceReport> LoadStaffPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StaffPerformanceReport(empty ? [] : [new("WLMHW", "001", 200m, null, null, "MissingSource", 3m, 0m, 2, 1.5m, 100m, 1m, null, null, 1, "Synthetic CRO")], 200m, 200m, 0m, ReportStatus.Passed, "Reconciled.", "test"));
        public Task<IReadOnlyList<ServiceSalesRecord>> LoadServiceSalesAsync(DateOnly businessDate, IReadOnlyList<string>? storeCodes = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServiceSalesRecord>>(empty ? [] : [new("FTD", "WLMHW", businessDate, businessDate, 10m, 20m, 30m, 60m, null, null, "MissingSource", 0, 1, 0m)]);
        public Task<CashReconciliationReport> LoadCashReconciliationAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhysicalStockRecord>> LoadPhysicalStockAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhysicalStockRecord>>(empty ? [] : [new(storeCode, businessDate, "TITAN", 1m, 2m, 0m, 0m, 3m, 3m, 0m, 3m, 0m, null, "PASS")]);
        public Task<IReadOnlyList<DailyExceptionRecord>> LoadDailyExceptionsAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DailyExceptionRecord>>(empty ? [] : [new("WARN", "Cash", "CASH_RECONCILIATION", storeCode, businessDate, null, null, 5m, "R022.xlsx", "Sheet1", 4, "Variance.", "Check the count.")]);
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action().GetAwaiter().GetResult(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Report grid presentation test failed", failure);
    }
}
