using System.Windows.Controls;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.4, report audit of 3 October 2026 (HEMW FIX-04/FIX-05, WLMHW FIX-08/FIX-11/FIX-12). Wording only:
/// stock UCP/TOTALUCP are MRP, not cost; sales are GST-inclusive NETAMOUNT, not NETVALUE. Owner decision 13 Q6
/// (lane L9): every screen counts INV documents only as "Invoices", like the DSR, plus a separate "Returns" count.
/// </summary>
[Collection(WpfViewCollection.Name)]
public sealed class ReportLabelCorrectionTests
{
    [Theory]
    [InlineData("stock-closing", "Closing Stock")]
    [InlineData("stock-slow", "Slow / Exception Stock")]
    public void Closing_and_slow_stock_say_MRP_not_cost_on_screen_and_in_the_export(string code, string reportName)
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync(code);
            var export = latest();
            Assert.Equal(reportName, export.ExportMetadata!.ReportName);
            var headers = export.ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Contains("Unit MRP", headers);
            Assert.Contains("MRP value (GST incl.)", headers);
            Assert.DoesNotContain(headers, header => header.Contains("Cost", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("UCP × quantity", export.ExportMetadata.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("cost", export.ExportMetadata.Message.Replace("not cost", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
            // The figures are the snapshot's UCP and TOTALUCP, unchanged.
            var row = export.ExportData.Rows.Single();
            Assert.Equal(1250m, row[Array.IndexOf(headers, "Unit MRP")]);
            Assert.Equal(2500m, row[Array.IndexOf(headers, "MRP value (GST incl.)")]);
            Assert.Equal(2500m, export.ExportData.Totals![Array.IndexOf(headers, "MRP value (GST incl.)")]);

            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            var gridHeaders = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.Contains("Unit MRP", gridHeaders);
            Assert.Contains("MRP value (GST incl.) ₹", gridHeaders);
            Assert.DoesNotContain(gridHeaders, header => header.Contains("Cost", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Brand_stock_says_MRP_value_not_total_cost_on_screen_and_in_the_export()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync("stock-brand");
            var export = latest();
            var headers = export.ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Contains("MRP value (GST incl.)", headers);
            Assert.DoesNotContain(headers, header => header.Contains("Cost", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("UCP × quantity", export.ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("quantity and cost are never inferred", export.ExportMetadata.Message, StringComparison.Ordinal);
            Assert.Equal(2500m, export.ExportData.Rows.Single()[Array.IndexOf(headers, "MRP value (GST incl.)")]);

            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            var gridHeaders = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.Contains("MRP value (GST incl.) ₹", gridHeaders);
            Assert.DoesNotContain(gridHeaders, header => header.Contains("Cost", StringComparison.OrdinalIgnoreCase));
        });
    }

    // Owner decision 13 Q6: every screen counts INV documents only as "Invoices" and shows SR/BC documents as "Returns".
    [Theory]
    [InlineData("sales-brand")]
    [InlineData("sales-returns")]
    public void Sales_summary_shows_invoices_and_returns_and_says_what_each_counts(string code)
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync(code);
            var export = latest();
            var headers = export.ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Equal(["Group", "Units", "Net Sales", "Invoices", "Returns"], headers);
            Assert.Contains("Invoices counts INV documents only, like the Daily Sales Report", export.ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.Contains("Returns counts sales returns (SR) and bill cancellations (BC)", export.ExportMetadata.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Documents", export.ExportMetadata.Message, StringComparison.Ordinal);
            // The counts are the service's own: 2 INV documents and 1 SR document.
            Assert.Equal(2, export.ExportData.Totals![3]);
            Assert.Equal(1, export.ExportData.Totals[4]);

            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            var gridHeaders = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.Contains("Invoices", gridHeaders);
            Assert.Contains("Returns", gridHeaders);
            Assert.DoesNotContain(gridHeaders, header => header.Contains("Documents", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Sales_summary_filtered_to_INV_keeps_the_same_columns()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            ((TextBox)view.FindName("TransactionTypeFilterInput")).Text = "INV";
            await view.RunReportAsync("sales-brand");
            var headers = latest().ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Equal(["Group", "Units", "Net Sales", "Invoices", "Returns"], headers);
        });
    }

    [Fact]
    public void Management_trend_shows_invoices_and_returns_and_names_its_sales_value()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync("management-trend");
            var export = latest();
            var headers = export.ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Equal(["Date", "Store", "Net Sales", "Units", "Invoices", "Returns", "Tender Variance", "Tender Source", "Unmatched Staff Rows"], headers);
            Assert.Contains("GST-inclusive sales (R025 NETAMOUNT)", export.ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.Contains("bill cancellations (BC)", export.ExportMetadata.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Documents", export.ExportMetadata.Message, StringComparison.Ordinal);
            var row = export.ExportData.Rows.Single();
            Assert.Equal(2, row[Array.IndexOf(headers, "Invoices")]);
            Assert.Equal(1, row[Array.IndexOf(headers, "Returns")]);
            Assert.Equal(1, export.ExportData.Totals![Array.IndexOf(headers, "Returns")]);

            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            var gridHeaders = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.Contains("Invoices", gridHeaders);
            Assert.Contains("Returns", gridHeaders);
            Assert.DoesNotContain(gridHeaders, header => header.Contains("Documents", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Operations_sales_and_control_trend_shows_invoices_and_returns()
    {
        // Operations > Sales and control trend binds ManagementTrendPoint, from the same INV-only and SR/BC counts
        // as the Management Trend report.
        Assert.Null(TablePresentation.ReportHeader(typeof(ManagementTrendPoint), "Invoices"));
        RunSta(() =>
        {
            var grid = new DataGrid { AutoGenerateColumns = true, ItemsSource = new List<ManagementTrendPoint> { new(new(2026, 8, 25), "HEMW", 236m, 2m, 47, 1, 0m, 0) } };
            TablePresentation.Configure(grid);
            var headers = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.Contains("Invoices", headers);
            Assert.Contains("Returns", headers);
            Assert.Equal(Array.IndexOf(headers, "Invoices") + 1, Array.IndexOf(headers, "Returns"));
            Assert.DoesNotContain(headers, header => header.Contains("Documents", StringComparison.Ordinal));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Customer_wise_counts_a_cancelled_bill_as_an_invoice_and_a_return()
    {
        var text = ReportsWorkspaceView.CustomerWiseCounts(
        [
            new(new(2026, 8, 25), "WLMHW", "A", "INV", 1m, 118m, 1, "Name"),
            new(new(2026, 8, 25), "WLMHW", "B", "INV+BC", 0m, 0m, 2, null),
            new(new(2026, 8, 25), "WLMHW", "C", "SR", -1m, -118m, 1, "Name"),
            new(new(2026, 8, 25), "WLMHW", "D", "inv", 1m, 50m, 1, "Name")
        ]);
        Assert.Equal("3 invoices, 2 returns; 1 missing customer names.", text);
    }

    [Fact]
    public void Customer_wise_and_invoice_history_say_documents_and_GST_inclusive_value()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync("invoice");
            Assert.StartsWith("1 invoices, 1 returns;", ((TextBlock)view.FindName("ReportResult")).Text, StringComparison.Ordinal);
            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            Assert.Contains("Value incl. GST ₹", grid.Columns.Select(column => (string)column.Header));
            Assert.DoesNotContain("Net Value ₹", grid.Columns.Select(column => (string)column.Header));

            await view.RunReportAsync("invoice-lineage");
            var headers = latest().ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Contains("Value incl. GST", headers);
            Assert.DoesNotContain("Net Value", headers);
            TablePresentation.Configure(grid);
            Assert.Contains("Value incl. GST ₹", grid.Columns.Select(column => (string)column.Header));
        });
    }

    [Fact]
    public void Dsr_availability_says_GST_inclusive_NETAMOUNT_not_NETVALUE()
    {
        var report = DailySalesReportBuilder.Build(new(2026, 8, 25), [], [], new Dictionary<string, decimal?>()) with { CombinedFtd = 118m };
        var sales = DailySalesReportWorkspace.DefaultAvailability(report).Single(item => item.Label == "Sales");
        Assert.True(sales.IsAvailable);
        Assert.Equal("recorded GST-inclusive sales (R025 NETAMOUNT) are available.", sales.Detail);
        Assert.DoesNotContain("NETVALUE", sales.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_named_report_columns_are_renamed()
    {
        Assert.Null(TablePresentation.ReportHeader(typeof(StockMovementRecord), "TotalCost"));
        Assert.Null(TablePresentation.ReportHeader(typeof(StaffPerformanceRecord), "Invoices"));
        Assert.Null(TablePresentation.ReportHeader(typeof(SalesSummaryRecord), "SourceSignedNetAmount"));
        Assert.Null(TablePresentation.ReportHeader(typeof(SalesSummaryRecord), "Invoices"));
        Assert.Null(TablePresentation.ReportHeader(typeof(ManagementTrendRecord), "Returns"));
        Assert.Equal("Unit MRP", TablePresentation.ReportHeader(typeof(StockInventoryRecord), "UnitCost"));
    }

    private static ReportsWorkspaceView CreateView(out Func<ReportPresentationSnapshot> latest)
    {
        ReportPresentationSnapshot? current = null;
        var operational = new OperationalQuery();
        var view = new ReportsWorkspaceView(() => "synthetic", _ => new ControlledQuery(), _ => operational,
            _ => new TrendQuery(), new ReportExportCoordinator(), new UnusedDiagnostic(),
            (_, _, _, _) => Task.FromResult<IReadOnlyList<CashBookDay>>([]));
        view.SetStores(TestStoreCatalog.Create());
        view.ApplyScope(new(2026, 8, 25), new(2026, 8, 25), "Titan World");
        view.AttachHost(_ => true, (_, _, _) => Task.CompletedTask, (snapshot, _, _) => current = snapshot, _ => { }, _ => { });
        latest = () => current ?? throw new InvalidOperationException("No report was presented.");
        return view;
    }

    private sealed class ControlledQuery : IControlledReportQuery
    {
        public Task<SalesSummaryReport> RunSalesSummaryAsync(ReportScope scope, ReportSalesDimension dimension, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesSummaryReport(dimension, ReportStatus.Passed, [new("TITAN", 2m, 236m, 2, 1)], "test", "Aggregated source-signed values without sign transformation."));
        public Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TrendQuery : IManagementTrendQuery
    {
        public Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagementTrendRecord>>([new(new(2026, 8, 25), "WLMHW", 236m, 2m, 2, 1, 0m, 0)]);
    }

    private sealed class OperationalQuery : IOperationalReportQuery<DailySalesReportDocument>
    {
        public Task<IReadOnlyList<StockInventoryRecord>> LoadStockInventoryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockInventoryRecord>>([new(new(2026, 8, 25), "WLMHW", "ITEM-1", "TITAN", "WATCHES", 2m, 1250m, 2500m, new(2026, 3, 1), 177, "EXCEPTION", "R011")]);
        public Task<IReadOnlyList<InvoiceSummaryRecord>> LoadInvoiceSummaryAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceSummaryRecord>>([new(new(2026, 8, 25), "WLMHW", "FIRST", "INV", 1m, 118m, 1, "First customer"), new(new(2026, 8, 25), "WLMHW", "SECOND", "SR", -1m, -118m, 1, "First customer")]);
        public Task<IReadOnlyList<InvoiceLineageRecord>> LoadInvoiceLineageAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceLineageRecord>>([new(new(2026, 8, 25), "WLMHW", "FIRST", "1", "ITEM-1", "TITAN", "WATCHES", "INV", 1m, 118m, null, "R025.xlsx", "Sheet1", 2)]);
        public Task<IReadOnlyList<DsrManagementRecord>> LoadDsrAsync(DateOnly businessDate, IReadOnlyList<string> storeCodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DailySalesReportDocument> ComposeDsrDocumentAsync(DateOnly businessDate, IReadOnlyList<DsrManagementRecord> rows, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StaffPerformanceReport> LoadStaffPerformanceAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceSalesRecord>> LoadServiceSalesAsync(DateOnly businessDate, IReadOnlyList<string>? storeCodes = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CashReconciliationReport> LoadCashReconciliationAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhysicalStockRecord>> LoadPhysicalStockAsync(string storeCode, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
        if (failure is not null) throw new InvalidOperationException("Report label test failed", failure);
    }
}
