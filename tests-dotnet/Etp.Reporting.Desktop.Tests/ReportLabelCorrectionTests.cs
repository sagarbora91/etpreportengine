using System.Windows.Controls;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Application.Reports;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.4, report audit of 3 October 2026 (HEMW FIX-04/FIX-05, WLMHW FIX-08/FIX-11/FIX-12). Wording only:
/// stock UCP/TOTALUCP are MRP, not cost; sales are GST-inclusive NETAMOUNT, not NETVALUE; the Sales Summary
/// and Management Trend count every document, including returns, while the DSR INVOICE count is INV only.
/// The values themselves must not change.
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

    [Fact]
    public void Sales_summary_names_its_document_count_and_says_what_it_counts()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync("sales-brand");
            var export = latest();
            var headers = export.ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Equal(["Group", "Units", "Net Sales", "Documents (incl. returns)"], headers);
            Assert.Contains("sales return (SR)", export.ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.Contains("INVOICE count includes INV documents only", export.ExportMetadata.Message, StringComparison.Ordinal);
            // The count is the service's own, unchanged: 2 INV + 1 SR documents = 3.
            Assert.Equal(3, export.ExportData.Totals![3]);

            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            var gridHeaders = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.Contains("Documents (incl. returns)", gridHeaders);
            Assert.DoesNotContain(gridHeaders, header => header.Contains("Invoice", StringComparison.OrdinalIgnoreCase) || header.Contains("Bills", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Management_trend_names_its_document_count_and_its_sales_value()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync("management-trend");
            var export = latest();
            var headers = export.ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Contains("Documents (incl. returns)", headers);
            Assert.DoesNotContain("Invoices", headers);
            Assert.Contains("GST-inclusive sales (R025 NETAMOUNT)", export.ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.Contains("bill cancellation (BC)", export.ExportMetadata.Message, StringComparison.Ordinal);
            Assert.Equal(3, export.ExportData.Rows.Single()[Array.IndexOf(headers, "Documents (incl. returns)")]);

            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            var gridHeaders = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.Contains("Documents (incl. returns)", gridHeaders);
            Assert.DoesNotContain("Invoices", gridHeaders);
        });
    }

    [Fact]
    public void Operations_sales_and_control_trend_labels_its_all_document_count_documents()
    {
        // Operations > Sales and control trend binds ManagementTrendPoint, from the same COUNT(DISTINCT sales_invoice_id)
        // over INV, SR and BC lines as the Management Trend report (HEMW Aug 2026: 48 here against DSR INVOICE 47).
        Assert.Equal("Documents (incl. returns)", TablePresentation.ReportHeader(typeof(ManagementTrendPoint), "Invoices"));
        RunSta(() =>
        {
            var grid = new DataGrid { AutoGenerateColumns = true, ItemsSource = new List<ManagementTrendPoint> { new(new(2026, 8, 25), "HEMW", 236m, 2m, 48, 0m, 0) } };
            TablePresentation.Configure(grid);
            var headers = grid.Columns.Select(column => (string)column.Header).ToArray();
            Assert.Contains("Documents (incl. returns)", headers);
            Assert.DoesNotContain("Invoices", headers);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Sales_returns_view_says_it_counts_only_return_and_cancellation_documents()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync("sales-returns");
            var export = latest();
            var headers = export.ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Equal(["Group", "Units", "Net Sales", "Documents (SR, BC)"], headers);
            Assert.Contains("only the sales return (SR) and bill cancellation (BC) documents", export.ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.Contains("INV documents are not counted", export.ExportMetadata.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("every invoice (INV)", export.ExportMetadata.Message, StringComparison.Ordinal);
            Assert.Equal(3, export.ExportData.Totals![3]);

            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            Assert.Contains("Documents (SR, BC)", grid.Columns.Select(column => (string)column.Header));

            // Back to a view that counts all three types: the default header and note return.
            await view.RunReportAsync("sales-brand");
            Assert.Contains("Documents (incl. returns)", latest().ExportData!.Columns.Select(column => column.Header));
            TablePresentation.Configure(grid);
            Assert.Contains("Documents (incl. returns)", grid.Columns.Select(column => (string)column.Header));
        });
    }

    [Fact]
    public void Sales_summary_filtered_to_INV_says_it_counts_only_invoices()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            ((TextBox)view.FindName("TransactionTypeFilterInput")).Text = "INV";
            await view.RunReportAsync("sales-brand");
            var export = latest();
            var headers = export.ExportData!.Columns.Select(column => column.Header).ToArray();
            Assert.Contains("Documents (INV)", headers);
            Assert.DoesNotContain("Documents (incl. returns)", headers);
            Assert.Contains("only the invoice (INV) documents", export.ExportMetadata!.Message, StringComparison.Ordinal);
            Assert.Contains("SR and BC documents are not counted", export.ExportMetadata.Message, StringComparison.Ordinal);

            var grid = (DataGrid)view.FindName("ReportGrid");
            TablePresentation.Configure(grid);
            Assert.Contains("Documents (INV)", grid.Columns.Select(column => (string)column.Header));
        });
    }

    [Theory]
    [InlineData("Brand", null, "Documents (incl. returns)")]
    [InlineData("Daily", "inv,sr,bc", "Documents (incl. returns)")]
    [InlineData("Store", "SR", "Documents (SR)")]
    [InlineData("Returns", "INV,SR", "Documents (SR)")]
    [InlineData("Returns", "INV", "Documents")]
    public void Sales_document_label_names_the_types_counted(string dimension, string? types, string header)
    {
        var label = ReportsWorkspaceView.SalesDocumentsLabel(dimension, types?.Split(','));
        Assert.Equal(header, label.Header);
        Assert.Contains("INVOICE count includes INV documents only", label.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Customer_wise_and_invoice_history_say_documents_and_GST_inclusive_value()
    {
        RunSta(async () =>
        {
            var view = CreateView(out var latest);
            await view.RunReportAsync("invoice");
            Assert.StartsWith("2 documents (invoices, returns and cancellations);", ((TextBlock)view.FindName("ReportResult")).Text, StringComparison.Ordinal);
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
            Task.FromResult(new SalesSummaryReport(dimension, ReportStatus.Passed, [new("TITAN", 2m, 236m, 3)], "test", "Aggregated source-signed values without sign transformation."));
        public Task<TenderReconciliationReport> RunTenderReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StockReconciliationReport> RunStockReconciliationAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<StockMovementRecord>> LoadStockMovementsAsync(ReportScope scope, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TrendQuery : IManagementTrendQuery
    {
        public Task<IReadOnlyList<ManagementTrendRecord>> LoadAsync(ReportScope scope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagementTrendRecord>>([new(new(2026, 8, 25), "WLMHW", 236m, 2m, 3, 0m, 0)]);
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
