extern alias EtpApplication;

using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Etp.Reporting.Reporting;
using Microsoft.Win32;

namespace Etp.Reporting.Desktop.Modules.Reports;

using ControlledReportQuery = EtpApplication::Etp.Reporting.Application.Reports.IControlledReportQuery;
using OperationalReportQuery = EtpApplication::Etp.Reporting.Application.Reports.IOperationalReportQuery<Etp.Reporting.Reporting.DailySalesReportDocument>;
using ManagementTrendQuery = EtpApplication::Etp.Reporting.Application.Reports.IManagementTrendQuery;
using ApplicationReportScope = EtpApplication::Etp.Reporting.Application.Reports.ReportScope;
using ApplicationReportStatus = EtpApplication::Etp.Reporting.Application.Reports.ReportStatus;
using ApplicationSalesDimension = EtpApplication::Etp.Reporting.Application.Reports.ReportSalesDimension;
using TenderVarianceDiagnostic = EtpApplication::Etp.Reporting.Application.Reports.ITenderVarianceDiagnostic;

public partial class ReportsWorkspaceView : UserControl
{
    private readonly ReportPresentationSession presentation = new();
    private readonly Func<string> connectionStringProvider;
    private readonly Func<string, ControlledReportQuery> controlledReportQueryFactory;
    private readonly Func<string, OperationalReportQuery> operationalReportQueryFactory;
    private readonly Func<string, ManagementTrendQuery> managementTrendQueryFactory;
    private readonly IReportExportCoordinator exportCoordinator;
    private readonly TenderVarianceDiagnostic tenderVarianceDiagnostic;
    private Func<string, bool> focusedWorkspaceRequester = static _ => true;
    private Func<string, string, string, Task> auditRecorder = static (_, _, _) => Task.CompletedTask;
    private Action<ReportPresentationSnapshot, IEnumerable?, string> previewUpdater = static (_, _, _) => { };
    private Action<string> dailySalesFailure = static _ => { };
    private Action<object> detailPresenter = static _ => { };
    private bool exportInProgress;

    public ReportsWorkspaceView(
        Func<string> connectionStringProvider,
        Func<string, ControlledReportQuery> controlledReportQueryFactory,
        Func<string, OperationalReportQuery> operationalReportQueryFactory,
        Func<string, ManagementTrendQuery> managementTrendQueryFactory,
        IReportExportCoordinator exportCoordinator,
        TenderVarianceDiagnostic tenderVarianceDiagnostic)
    {
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        this.controlledReportQueryFactory = controlledReportQueryFactory ?? throw new ArgumentNullException(nameof(controlledReportQueryFactory));
        this.operationalReportQueryFactory = operationalReportQueryFactory ?? throw new ArgumentNullException(nameof(operationalReportQueryFactory));
        this.managementTrendQueryFactory = managementTrendQueryFactory ?? throw new ArgumentNullException(nameof(managementTrendQueryFactory));
        this.exportCoordinator = exportCoordinator ?? throw new ArgumentNullException(nameof(exportCoordinator));
        this.tenderVarianceDiagnostic = tenderVarianceDiagnostic ?? throw new ArgumentNullException(nameof(tenderVarianceDiagnostic));
        InitializeComponent();
        ReportFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        ReportTo.SelectedDate = DateTime.Today.AddDays(-1);
    }

    public DateTime? DateFrom => ReportFrom.SelectedDate;
    public DateTime? DateTo => ReportTo.SelectedDate;
    public string? CurrentReportCode => presentation.Current.ReportCode;

    public void AttachHost(
        Func<string, bool> focusedWorkspaceRequester,
        Func<string, string, string, Task> auditRecorder,
        Action<ReportPresentationSnapshot, IEnumerable?, string> previewUpdater,
        Action<string> dailySalesFailure,
        Action<object> detailPresenter)
    {
        this.focusedWorkspaceRequester = focusedWorkspaceRequester ?? throw new ArgumentNullException(nameof(focusedWorkspaceRequester));
        ArgumentNullException.ThrowIfNull(auditRecorder);
        this.auditRecorder = (eventType, outcome, detail) =>
            auditRecorder(eventType, eventType == "ReportRun" ? ToAuditOutcome(outcome) : outcome, detail);
        this.previewUpdater = previewUpdater ?? throw new ArgumentNullException(nameof(previewUpdater));
        this.dailySalesFailure = dailySalesFailure ?? throw new ArgumentNullException(nameof(dailySalesFailure));
        this.detailPresenter = detailPresenter ?? throw new ArgumentNullException(nameof(detailPresenter));
    }

    public void ApplyScope(DateTime? from, DateTime? to, string? scope)
    {
        ReportFrom.SelectedDate = from ?? DateTime.Today;
        ReportTo.SelectedDate = to ?? from ?? DateTime.Today;
        StoreFilterInput.Text = scope switch { "Titan" => "WLMHW", "Helios" => "HEMW", _ => string.Empty };
    }

    public void SetBusinessDate(DateTime date) => ReportTo.SelectedDate = date;
    public void FocusSearch() { ReportSearchInput.Focus(); ReportSearchInput.SelectAll(); }

    public async Task RunReportAsync(string report)
    {
        if (!BeginReportLoad(report)) return;
        switch (report)
        {
            case "dsr": await RunDsrAsync(); break;
            case "sales-titan": StoreFilterInput.Text = "WLMHW"; SelectSalesDimension("Daily"); await RunSalesReportAsync(); break;
            case "sales-helios": StoreFilterInput.Text = "HEMW"; SelectSalesDimension("Daily"); await RunSalesReportAsync(); break;
            case "sales-combined": StoreFilterInput.Clear(); SelectSalesDimension("Store"); await RunSalesReportAsync(); break;
            case "sales-returns": SelectSalesDimension("Returns"); await RunSalesReportAsync(); break;
            case "sales-brand": SelectSalesDimension("Brand"); await RunSalesReportAsync(); break;
            case "sales-segment": SelectSalesDimension("BrandSegment"); await RunSalesReportAsync(); break;
            case "sales-item": SelectSalesDimension("Item"); await RunSalesReportAsync(); break;
            case "invoice": await RunInvoiceSummaryAsync(); break;
            case "invoice-lineage": await RunInvoiceLineageAsync(); break;
            case "staff": await RunStaffPerformanceAsync(); break;
            case "service": await RunServiceSalesAsync(); break;
            case "cash": await RunCashReconciliationAsync(); break;
            case "tender": await RunTenderReportAsync(); break;
            case "tender-diagnostic": await RunTenderDiagnosticAsync(); break;
            case "stock-variance": await RunStockReportAsync(); break;
            case "stock-physical" or "stock-group": await RunPhysicalStockAsync(); break;
            case "stock-closing": await RunStockInventoryAsync("CLOSING"); break;
            case "stock-brand": await RunStockInventoryAsync("BRAND"); break;
            case "stock-slow": await RunStockInventoryAsync("SLOW"); break;
            case "stock-movement": await RunStockMovementAsync(); break;
            case "exceptions": await RunDailyExceptionsAsync(); break;
            case "exception-source": await RunFocusedExceptionAsync("Source"); break;
            case "exception-unmapped": await RunFocusedExceptionAsync("Unmapped"); break;
            case "exception-stock": await RunFocusedExceptionAsync("Stock"); break;
            case "exception-staff": await RunFocusedExceptionAsync("Staff"); break;
            case "exception-tender": await RunFocusedExceptionAsync("Tender"); break;
            case "management-trend": await RunManagementTrendReportAsync(); break;
        }
    }

    public void ExportExcel() => _ = ExportExcelAsync();

    public async Task ExportExcelAsync()
    {
        var report = presentation.Current;
        if (!report.CanExportReport || exportInProgress) return;
        var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"{report.ExportMetadata!.ReportName.Replace(' ', '_')}_{report.ExportMetadata.DateFrom:yyyyMMdd}_{report.ExportMetadata.DateTo:yyyyMMdd}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        exportInProgress = true;
        RefreshExportAvailability();
        try { await exportCoordinator.ExportReportExcelAsync(dialog.FileName, report.ExportMetadata, report.ExportData!, report.VisualReport); ReportResult.Text = $"Excel report saved to {dialog.FileName}"; await auditRecorder("ExportExcel", "Succeeded", "Visual report exported"); }
        catch (Exception ex) { HandleFailure(ex, "REPORT_EXCEL_EXPORT_FAILED", "Excel export failed"); }
        finally { exportInProgress = false; RefreshExportAvailability(); }
    }

    public void ExportPdf() => _ = ExportPdfAsync();

    public async Task ExportPdfAsync()
    {
        var report = presentation.Current;
        if (!report.CanExportReport || exportInProgress) return;
        if (string.Equals(report.ExportMetadata!.ReportName, "Daily Sales Report", StringComparison.Ordinal) && report.DailySalesReport is null)
        { ReportResult.Text = "The DSR document is not ready. Run Daily Sales / DSR again before exporting."; return; }
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"{SafeFileName(report.ExportMetadata.ReportName)}_{report.ExportMetadata.DateFrom:yyyyMMdd}_{report.ExportMetadata.DateTo:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        exportInProgress = true;
        RefreshExportAvailability();
        try { await exportCoordinator.ExportReportPdfAsync(dialog.FileName, report.ExportMetadata, report.ExportData!, report.VisualReport, report.DailySalesReport); ReportResult.Text = $"PDF report saved to {dialog.FileName}"; await auditRecorder("ExportPdf", "Succeeded", report.DailySalesReport is null ? "Visual report exported" : "One-page DSR exported"); }
        catch (Exception ex) { HandleFailure(ex, "REPORT_PDF_EXPORT_FAILED", "PDF export failed"); }
        finally { exportInProgress = false; RefreshExportAvailability(); }
    }

    private async void RunCatalogueReport_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string report }) await RunReportAsync(report); }
    private async void ExportExcel_Click(object sender, RoutedEventArgs e) => await ExportExcelAsync();
    private async void ExportPdf_Click(object sender, RoutedEventArgs e) => await ExportPdfAsync();

    private bool BeginReportLoad(string reportCode)
    {
        if (!focusedWorkspaceRequester(reportCode)) return false;
        presentation.BeginReport(reportCode);
        RefreshExportAvailability();
        ReportPresentationHost.Clear();
        ReportResult.Text = reportCode == "dsr" ? "Loading the governed Daily Sales Report…" : "Loading report…";
        return true;
    }

    private ApplicationReportScope ReportScope()
    {
        if (ReportFrom.SelectedDate is null || ReportTo.SelectedDate is null) throw new InvalidOperationException("Select both report dates.");
        return new(DateOnly.FromDateTime(ReportFrom.SelectedDate.Value), DateOnly.FromDateTime(ReportTo.SelectedDate.Value), Csv(StoreFilterInput.Text), Csv(BrandSegmentFilterInput.Text), Csv(TransactionTypeFilterInput.Text), Csv(ItemFilterInput.Text));
    }

    private void SelectSalesDimension(string name) =>
        SalesDimensionInput.SelectedItem = SalesDimensionInput.Items.OfType<ComboBoxItem>().First(x => string.Equals(x.Content?.ToString(), name, StringComparison.Ordinal));

    private async Task RunStockInventoryAsync(string mode)
    {
        try
        {
            var rows = await operationalReportQueryFactory(connectionStringProvider()).LoadStockInventoryAsync(ReportScope());
            if (mode == "SLOW") rows = rows.Where(x => x.Quantity != 0 && x.MovementStatus != "ACTIVE").ToArray();
            if (mode == "BRAND")
            {
                var grouped = rows.GroupBy(x => new { x.StoreCode, Brand = x.Brand ?? "Unmapped", Group = x.InventoryGroup ?? "Unmapped" }).Select(x => new { x.Key.StoreCode, x.Key.Brand, InventoryGroup = x.Key.Group, Quantity = x.Sum(y => y.Quantity), TotalCost = x.Any(y => y.TotalCost is not null) ? (decimal?)x.Sum(y => y.TotalCost ?? 0) : null, Items = x.Select(y => y.ProductCode).Distinct().Count(), SlowItems = x.Count(y => y.Quantity != 0 && y.MovementStatus != "ACTIVE") }).OrderBy(x => x.StoreCode).ThenBy(x => x.InventoryGroup).ThenBy(x => x.Brand).ToArray();
                var status = grouped.Length == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed; ReportGrid.ItemsSource = grouped; ReportResult.Text = $"{status}: {grouped.Length:N0} store/brand/inventory-group row(s).";
                SetExport("Brand Stock", status, RetailReportingPolicy.Version, "Closing stock grouped from the immutable ETP snapshot; quantity and cost are never inferred.", [new("Store"),new("Brand"),new("Inventory Group"),new("Quantity","#,##0.00"),new("Total Cost","#,##0.00"),new("Items","#,##0"),new("Slow Items","#,##0")], grouped.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.Brand,x.InventoryGroup,x.Quantity,x.TotalCost,x.Items,x.SlowItems]).ToArray(), ["Total","","",grouped.Sum(x=>x.Quantity),grouped.Sum(x=>x.TotalCost),grouped.Sum(x=>x.Items),grouped.Sum(x=>x.SlowItems)]);
            }
            else
            {
                var status = rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed; var name = mode == "SLOW" ? "Slow / Exception Stock" : "Closing Stock"; ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {rows.Count:N0} item(s). Slow stock uses 60-day watch and 90-day exception bands.";
                SetExport(name, status, RetailReportingPolicy.Version, "Closing quantities and costs come from the selected-date ETP stock snapshot. Last sale is the latest positive source-signed sale on or before that date.", [new("Date"),new("Store"),new("Item"),new("Brand"),new("Inventory Group"),new("Quantity","#,##0.00"),new("Unit Cost","#,##0.00"),new("Total Cost","#,##0.00"),new("Last Sale"),new("Days Since Sale","#,##0"),new("Movement Status")], rows.Select(x => (IReadOnlyList<object?>)[x.SnapshotDate,x.StoreCode,x.ProductCode,x.Brand,x.InventoryGroup,x.Quantity,x.UnitCost,x.TotalCost,x.LastSaleDate,x.DaysSinceLastSale,x.MovementStatus]).ToArray(), ["Total","","","","",rows.Sum(x=>x.Quantity),"",rows.Sum(x=>x.TotalCost),"","",""]);
            }
            ApplyReportFilter(); await auditRecorder("ReportRun", ToAuditOutcome(rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed), mode == "BRAND" ? "Brand stock" : mode == "SLOW" ? "Slow stock" : "Closing stock");
        }
        catch (Exception ex) { HandleFailure(ex, "STOCK_REPORT_FAILED", "Stock report failed"); }
    }

    private async Task RunStockMovementAsync()
    {
        try { var rows = await controlledReportQueryFactory(connectionStringProvider()).LoadStockMovementsAsync(ReportScope()); var status = rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed; ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {rows.Count:N0} source movement group(s)."; SetExport("Stock Movement", status, RetailReportingPolicy.Version, "Movement quantities retain the ETP source transaction type and source-signed quantity.", [new("Store"),new("Item"),new("Movement Type"),new("Signed Quantity","#,##0.00")], rows.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.ItemCode,x.SourceMovementType,x.SourceSignedQuantity]).ToArray(), ["Total","","",rows.Sum(x=>x.SourceSignedQuantity)]); ApplyReportFilter(); await auditRecorder("ReportRun", ToAuditOutcome(status), "Stock movement"); }
        catch (Exception ex) { HandleFailure(ex, "STOCK_MOVEMENT_REPORT_FAILED", "Stock movement report failed"); }
    }

    private async Task RunFocusedExceptionAsync(string focus)
    {
        try { var scope = ReportScope(); if (scope.StoreCodes is not { Count: 1 } || scope.DateFrom != scope.DateTo) throw new InvalidOperationException("Select one store and one business date for an exception report."); var all = await operationalReportQueryFactory(connectionStringProvider()).LoadDailyExceptionsAsync(scope.StoreCodes[0], scope.DateTo); var rows = focus switch { "Source" => all.Where(x=>x.Area=="Source"), "Unmapped" => all.Where(x=>x.Area.Contains("Staff",StringComparison.OrdinalIgnoreCase)||x.Code.Contains("MISSING",StringComparison.OrdinalIgnoreCase)||x.Code.Contains("AMBIGUOUS",StringComparison.OrdinalIgnoreCase)), "Stock" => all.Where(x=>x.Area.Contains("stock",StringComparison.OrdinalIgnoreCase)), "Staff" => all.Where(x=>x.Area.Contains("Staff",StringComparison.OrdinalIgnoreCase)), "Tender" => all.Where(x=>x.Area=="Tender"), _ => all }; var result = rows.ToArray(); var status = result.Any(x=>x.Severity is "BLOCKER" or "FAIL") ? ReconciliationStatus.Failed : ReconciliationStatus.Passed; ReportGrid.ItemsSource=result; ReportResult.Text=$"{status}: {result.Length:N0} {focus.ToLowerInvariant()} exception(s)."; SetExport($"{focus} Exceptions",status,RetailReportingPolicy.Version,"Focused view of the same immutable daily exception evidence; filtering never changes technical control status.",[new("Severity"),new("Area"),new("Code"),new("Store"),new("Date"),new("Document"),new("Item"),new("Variance","#,##0.00"),new("Workbook"),new("Sheet"),new("Source Row","#,##0"),new("Message"),new("Recommended Action")],result.Select(x=>(IReadOnlyList<object?>)[x.Severity,x.Area,x.Code,x.StoreCode,x.BusinessDate,x.DocumentNumber,x.ItemCode,x.Variance,x.SourceWorkbook,x.SourceSheet,x.SourceRow,x.Message,x.RecommendedAction]).ToArray(),["Total",result.Length,"","","","","",result.Where(x=>x.Variance is not null).Sum(x=>x.Variance),"","","","",""]); ApplyReportFilter(); await auditRecorder("ReportRun",ToAuditOutcome(status),$"{focus} exceptions"); }
        catch (Exception ex) { HandleFailure(ex, "FOCUSED_EXCEPTION_REPORT_FAILED", "Exception report failed"); }
    }

    private async Task RunManagementTrendReportAsync()
    {
        try { var rows = await managementTrendQueryFactory(connectionStringProvider()).LoadAsync(ReportScope()); var status = rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed; ReportGrid.ItemsSource=rows; ReportResult.Text=$"{status}: {rows.Count:N0} daily management trend row(s)."; SetExport("Management Trend",status,RetailReportingPolicy.Version,"Daily canonical sales, units, invoices and unchanged control variances.",[new("Date"),new("Store"),new("Net Sales","#,##0.00"),new("Units","#,##0.00"),new("Invoices","#,##0"),new("Tender Variance","#,##0.00"),new("Unmatched Staff Rows","#,##0")],rows.Select(x=>(IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.NetSales,x.Units,x.Invoices,x.TenderVariance,x.UnmatchedEnrichmentRows]).ToArray(),["Total","",rows.Sum(x=>x.NetSales),rows.Sum(x=>x.Units),rows.Sum(x=>x.Invoices),rows.Sum(x=>x.TenderVariance),rows.Sum(x=>x.UnmatchedEnrichmentRows)]); ApplyReportFilter(); await auditRecorder("ReportRun",ToAuditOutcome(status),"Management trend"); }
        catch (Exception ex) { HandleFailure(ex, "MANAGEMENT_TREND_REPORT_FAILED", "Management trend failed"); }
    }

    private async Task RunSalesReportAsync()
    {
        try { var name=((ComboBoxItem)SalesDimensionInput.SelectedItem).Content!.ToString()!; var result=await controlledReportQueryFactory(connectionStringProvider()).RunSalesSummaryAsync(ReportScope(),Enum.Parse<ApplicationSalesDimension>(name)); ReportGrid.ItemsSource=result.Rows; ReportResult.Text=$"{result.Status}: {result.Message}"; SetExport($"{name} Sales",ToReportingStatus(result.Status),result.PolicyVersion,result.Message,[new("Group"),new("Units","#,##0.00"),new("Net Sales","#,##0.00"),new("Bills","#,##0")],result.Rows.Select(x=>(IReadOnlyList<object?>)[x.Key,x.SourceSignedQuantity,x.SourceSignedNetAmount,x.DistinctInvoices]).ToArray(),["Total",result.Rows.Sum(x=>x.SourceSignedQuantity),result.Rows.Sum(x=>x.SourceSignedNetAmount),result.Rows.Sum(x=>x.DistinctInvoices)]); ApplyReportFilter(); await auditRecorder("ReportRun",ToAuditOutcome(result.Status),"Sales report"); }
        catch (Exception ex) { HandleFailure(ex, "SALES_REPORT_FAILED", "Sales report failed"); }
    }

    private async Task RunInvoiceSummaryAsync()
    {
        try { var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadInvoiceSummaryAsync(ReportScope()); var status=rows.Count==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed; var message=rows.Count==0?"No canonical invoice lines are available for the selected scope.":"Invoice totals are generated from canonical R025 lines; customer PII is intentionally excluded."; ReportGrid.ItemsSource=rows; ReportResult.Text=$"{status}: {rows.Count:N0} invoices."; SetExport("Customer-safe Invoice Sales Summary",status,RetailReportingPolicy.Version,message,[new("Business Date"),new("Store"),new("Document"),new("Transaction Type"),new("Quantity","#,##0.00"),new("Net Value","#,##0.00"),new("Source Rows","#,##0")],rows.Select(x=>(IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.DocumentNumber,x.TransactionTypes,x.Quantity,x.NetValue,x.SourceRows]).ToArray(),["Total","","","",rows.Sum(x=>x.Quantity),rows.Sum(x=>x.NetValue),rows.Sum(x=>x.SourceRows)]); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Blocked","Invoice summary"); }
        catch (Exception ex) { HandleFailure(ex, "INVOICE_SUMMARY_FAILED", "Invoice summary failed"); }
    }

    private async Task RunInvoiceLineageAsync()
    {
        try { var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadInvoiceLineageAsync(ReportScope()); var status=rows.Count==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed; const string message="Invoice and item drill-down is traceable to its source workbook, sheet and row. Customer PII remains excluded pending owner approval."; ReportGrid.ItemsSource=rows; ReportResult.Text=$"{status}: {rows.Count:N0} canonical line(s)."; SetExport("Invoice Sales Lineage",status,RetailReportingPolicy.Version,message,[new("Business Date"),new("Store"),new("Document"),new("Line"),new("Item"),new("Brand"),new("Segment"),new("Transaction Type"),new("Quantity","#,##0.00"),new("Net Value","#,##0.00"),new("CRO"),new("Workbook"),new("Sheet"),new("Source Row","#,##0")],rows.Select(x=>(IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.DocumentNumber,x.LineIdentifier,x.ProductCode,x.Brand,x.BrandSegment,x.TransactionType,x.Quantity,x.NetValue,x.CroNumber,x.SourceWorkbook,x.SourceSheet,x.SourceRow]).ToArray(),["Total","","","","","","","",rows.Sum(x=>x.Quantity),rows.Sum(x=>x.NetValue),"","","",rows.Count]); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Blocked","Invoice lineage report"); }
        catch (Exception ex) { HandleFailure(ex, "INVOICE_DRILLDOWN_FAILED", "Invoice drill-down failed"); }
    }

    private async Task RunDsrAsync()
    {
        try { var scope=ReportScope(); var repository=operationalReportQueryFactory(connectionStringProvider()); var rows=await repository.LoadDsrAsync(scope.DateTo,["WLMHW","HEMW"]); var document=await repository.ComposeDsrDocumentAsync(scope.DateTo,rows); var status=rows.Any(x=>x.TySales is not null)?ReconciliationStatus.Passed:ReconciliationStatus.Blocked; var unavailable=rows.Count(x=>x.GrowthStatus!=MetricAvailability.Available.ToString()); var message=$"FTD, MTD and Indian-financial-year YTD use business date {scope.DateTo:dd-MMM-yyyy}; {unavailable:N0} row(s) have unavailable LY growth rather than a misleading percentage."; ReportGrid.ItemsSource=rows; ReportResult.Text=$"{status}: {message}"; var policy=rows.Select(x=>x.MetricPolicy).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??"DSR_INVOICE_DENOMINATOR_SOURCE_EVIDENCE_V1"; SetExport("Daily Sales Report",status,policy,message,[new("Period"),new("Store"),new("From"),new("To"),new("TY Sales","#,##0.00"),new("LY Sales","#,##0.00"),new("Growth %","#,##0.00"),new("Growth Status"),new("TY Units","#,##0.00"),new("LY Units","#,##0.00"),new("TY Invoices","#,##0"),new("LY Invoices","#,##0"),new("UPT","#,##0.00"),new("ATV","#,##0.00"),new("Walk-ins","#,##0.00"),new("Conversion %","#,##0.00")],rows.Select(x=>(IReadOnlyList<object?>)[x.Period,x.Store,x.PeriodStart,x.PeriodEnd,x.TySales,x.LySales,x.GrowthPercent,x.GrowthStatus,x.TyUnits,x.LyUnits,x.TyInvoices,x.LyInvoices,x.Upt,x.Atv,x.WalkIns,x.ConversionPercent]).ToArray(),["Independent periods","","","","","","","","","","","","","","",""],document,scope.DateTo); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Blocked","Daily sales report"); }
        catch (Exception ex)
        {
            var message = HandleFailure(ex, "DSR_REPORT_FAILED", "DSR failed");
            dailySalesFailure(message);
        }
    }

    private async Task RunStaffPerformanceAsync()
    {
        try
        {
            var result = await operationalReportQueryFactory(connectionStringProvider()).LoadStaffPerformanceAsync(ReportScope());
            ReportGrid.ItemsSource = result.Rows;
            ReportResult.Text = $"{result.Status}: canonical {result.CanonicalSales:N2}, attributed {result.AttributedSales:N2}, variance {result.Variance:N2}. {result.Message}";
            SetExport("Staff CRO Performance",ToReportingStatus(result.Status), result.MetricPolicy, result.Message,
                [new("Store"),new("CRO"),new("Net Sales","#,##0.00"),new("LY Sales","#,##0.00"),new("Growth %","#,##0.00"),new("Growth Status"),new("Net Quantity","#,##0.00"),new("Discount","#,##0.00"),new("Transactions","#,##0"),new("UPT","#,##0.00"),new("ATV","#,##0.00"),new("Contribution %","#,##0.00"),new("Target","#,##0.00"),new("Achievement %","#,##0.00"),new("Rank","#,##0")],
                result.Rows.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.CroNumber,x.NetSales,x.LastYearSales,x.GrowthPercent,x.GrowthStatus,x.NetQuantity,x.Discount,x.Transactions,x.Upt,x.Atv,x.ContributionPercent,x.TargetSales,x.TargetAchievementPercent,x.Rank]).ToArray(),
                ["Control","",result.AttributedSales,"","","","","",result.Rows.Sum(x=>x.Transactions),"","",result.Variance,"","",""]);
            ApplyReportFilter();
            await auditRecorder("ReportRun", ToAuditOutcome(result.Status), "Staff performance");
        }
        catch (Exception ex) { HandleFailure(ex, "STAFF_REPORT_FAILED", "Staff report failed"); }
    }

    private async Task RunServiceSalesAsync()
    {
        try { var scope=ReportScope(); var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadServiceSalesAsync(scope.DateTo,scope.StoreCodes); var status=rows.Any(x=>x.Total is not null)?ReconciliationStatus.Passed:ReconciliationStatus.Blocked; const string message="Service cash, card and UPI are controlled manual operational facts; missing values remain missing and retail sales are never mixed in."; ReportGrid.ItemsSource=rows; ReportResult.Text=$"{status}: {message}"; SetExport("Service Sales",status,RetailReportingPolicy.Version,message,[new("Period"),new("Store"),new("From"),new("To"),new("Cash","#,##0.00"),new("Card","#,##0.00"),new("UPI","#,##0.00"),new("Total","#,##0.00"),new("LY Total","#,##0.00"),new("Growth %","#,##0.00"),new("Availability")],rows.Select(x=>(IReadOnlyList<object?>)[x.Period,x.StoreCode,x.PeriodStart,x.PeriodEnd,x.Cash,x.Card,x.Upi,x.Total,x.LastYearTotal,x.GrowthPercent,x.Availability]).ToArray(),["Independent periods","","","","","","","","","",""]); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Blocked","Service sales"); }
        catch (Exception ex) { HandleFailure(ex, "SERVICE_REPORT_FAILED", "Service report failed"); }
    }

    private async Task RunCashReconciliationAsync()
    {
        try { var scope=ReportScope(); if(scope.StoreCodes is not {Count:1})throw new InvalidOperationException("Enter exactly one store code for cash reconciliation."); var result=await operationalReportQueryFactory(connectionStringProvider()).LoadCashReconciliationAsync(scope.StoreCodes[0],scope.DateTo); ReportGrid.ItemsSource=new[]{result}; ReportResult.Text=$"{result.Status}: {result.Message}"; SetExport("Daily Cash Reconciliation",ToReportingStatus(result.Status),RetailReportingPolicy.Version,result.Message,[new("Store"),new("Business Date"),new("Opening","#,##0.00"),new("Retail Cash","#,##0.00"),new("Service Cash","#,##0.00"),new("Expenses","#,##0.00"),new("Deposit","#,##0.00"),new("Adjustment","#,##0.00"),new("Calculated Closing","#,##0.00"),new("Counted Closing","#,##0.00"),new("Variance","#,##0.00"),new("Status")],[(IReadOnlyList<object?>)[result.StoreCode,result.BusinessDate,result.OpeningCash,result.RetailCash,result.ServiceCash,result.Expenses,result.CashDeposit,result.Adjustment,result.CalculatedClosing,result.CountedClosing,result.Variance,result.Status.ToString()]],["Control","","","","","","","","",result.CountedClosing,result.Variance,result.Status.ToString()]); ApplyReportFilter(); await auditRecorder("ReportRun",result.Status==ApplicationReportStatus.Passed?"Succeeded":result.Status.ToString(),"Cash reconciliation"); }
        catch (Exception ex) { HandleFailure(ex, "CASH_RECONCILIATION_FAILED", "Cash reconciliation failed"); }
    }

    private async Task RunTenderReportAsync()
    {
        try { var r=await controlledReportQueryFactory(connectionStringProvider()).RunTenderReconciliationAsync(ReportScope()); ReportGrid.ItemsSource=r.Documents; ReportResult.Text=$"{r.Status}: invoice {r.InvoiceTotal:N2}, tender {r.TenderTotal:N2}, variance {r.Variance:N2}."; SetExport("Invoice Tender Reconciliation",ToReportingStatus(r.Status),r.RuleVersion,r.Message,[new("Store"),new("Document"),new("Invoice","#,##0.00"),new("Tender","#,##0.00"),new("Variance","#,##0.00"),new("Status")],r.Documents.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.DocumentNumber,x.InvoiceAmount,x.TenderAmount,x.Variance,x.Status.ToString()]).ToArray(),["Total","",r.InvoiceTotal,r.TenderTotal,r.Variance,r.Status.ToString()]); ApplyReportFilter(); await auditRecorder("ReportRun",r.Status==ApplicationReportStatus.Passed?"Succeeded":r.Status.ToString(),"Tender control"); }
        catch (Exception ex) { HandleFailure(ex, "TENDER_RECONCILIATION_FAILED", "Tender reconciliation failed"); }
    }

    private async Task RunTenderDiagnosticAsync()
    {
        try { var reconciliation=await controlledReportQueryFactory(connectionStringProvider()).RunTenderReconciliationAsync(ReportScope()); var diagnostic=tenderVarianceDiagnostic.Diagnose(reconciliation,RetailReportingPolicy.Tender.AbsoluteTolerance); ReportGrid.ItemsSource=diagnostic.Rows; ReportResult.Text=$"{diagnostic.Status}: {diagnostic.FailedDocuments:N0} documents require review; absolute variance {diagnostic.AbsoluteVariance:N2}. Classifications do not change the control result."; SetExport("Tender Variance Diagnostics",ToReportingStatus(diagnostic.Status),diagnostic.RuleVersion,diagnostic.Message,[new("Store"),new("Document"),new("Invoice","#,##0.00"),new("Tender","#,##0.00"),new("Variance","#,##0.00"),new("Likely Cause"),new("Recommended Check")],diagnostic.Rows.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.DocumentNumber,x.InvoiceAmount,x.TenderAmount,x.Variance,x.LikelyCause.ToString(),x.RecommendedCheck]).ToArray(),["Total","",reconciliation.InvoiceTotal,reconciliation.TenderTotal,reconciliation.Variance,diagnostic.Status.ToString(),$"{diagnostic.FailedDocuments:N0} documents"]); ApplyReportFilter(); await auditRecorder("ReportRun",diagnostic.Status==ApplicationReportStatus.Passed?"Succeeded":diagnostic.Status.ToString(),"Tender diagnostic"); }
        catch (Exception ex) { HandleFailure(ex, "TENDER_DIAGNOSTICS_FAILED", "Tender diagnostics failed"); }
    }

    private async Task RunStockReportAsync()
    {
        try { var r=await controlledReportQueryFactory(connectionStringProvider()).RunStockReconciliationAsync(ReportScope()); ReportGrid.ItemsSource=r.Items; ReportResult.Text=$"{r.Status}: {r.Message}"; SetExport("Stock Reconciliation",ToReportingStatus(r.Status),r.RuleVersion,r.Message,[new("Store"),new("Item"),new("Opening","#,##0.00"),new("Movements","#,##0.00"),new("Expected Closing","#,##0.00"),new("Reported Closing","#,##0.00"),new("Variance","#,##0.00"),new("Status")],r.Items.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.ItemCode,x.Opening,x.SourceSignedMovements,x.ExpectedClosing,x.ReportedClosing,x.Variance,x.Status.ToString()]).ToArray(),["Total","",r.Items.Sum(x=>x.Opening),r.Items.Sum(x=>x.SourceSignedMovements),r.Items.Sum(x=>x.ExpectedClosing),r.Items.Sum(x=>x.ReportedClosing),r.Items.Sum(x=>x.Variance),r.Status.ToString()]); ApplyReportFilter(); await auditRecorder("ReportRun",r.Status==ApplicationReportStatus.Passed?"Succeeded":r.Status.ToString(),"Stock control"); }
        catch (Exception ex) { HandleFailure(ex, "STOCK_RECONCILIATION_FAILED", "Stock reconciliation failed"); }
    }

    private async Task RunPhysicalStockAsync()
    {
        try { var scope=ReportScope(); if(scope.StoreCodes is not {Count:1})throw new InvalidOperationException("Enter exactly one store code for physical stock reporting."); var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadPhysicalStockAsync(scope.StoreCodes[0],scope.DateTo); var status=rows.Any(x=>x.Status=="FAIL")?ReconciliationStatus.Failed:rows.Count==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed; const string message="Physical count, component total and ETP system quantity remain separate; neither count overwrites the other."; ReportGrid.ItemsSource=rows; ReportResult.Text=$"{status}: {rows.Count:N0} inventory group(s)."; SetExport("Physical Closing Stock",status,RetailReportingPolicy.Version,message,[new("Store"),new("Date"),new("Inventory Group"),new("Display","#,##0.00"),new("Backstock","#,##0.00"),new("Defective","#,##0.00"),new("Y Location","#,##0.00"),new("Component Total","#,##0.00"),new("Counted Physical","#,##0.00"),new("Composition Variance","#,##0.00"),new("System","#,##0.00"),new("System Variance","#,##0.00"),new("Remarks"),new("Status")],rows.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.BusinessDate,x.InventoryGroupCode,x.DisplayQuantity,x.BackstockQuantity,x.DefectiveQuantity,x.YLocationQuantity,x.ComponentTotal,x.CountedPhysicalQuantity,x.CompositionVariance,x.SystemQuantity,x.SystemVariance,x.Remarks,x.Status]).ToArray(),["Total","","","","","","",rows.Sum(x=>x.ComponentTotal),rows.Sum(x=>x.CountedPhysicalQuantity),rows.Sum(x=>x.CompositionVariance),rows.Sum(x=>x.SystemQuantity),rows.Sum(x=>x.SystemVariance),"",status.ToString()]); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":status.ToString(),"Physical stock report"); }
        catch (Exception ex) { HandleFailure(ex, "PHYSICAL_STOCK_REPORT_FAILED", "Physical stock report failed"); }
    }

    private async Task RunDailyExceptionsAsync()
    {
        try { var scope=ReportScope(); if(scope.StoreCodes is not {Count:1})throw new InvalidOperationException("Enter exactly one store code for the daily exception report."); if(scope.DateFrom!=scope.DateTo)throw new InvalidOperationException("Select one business date for the daily exception report."); var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadDailyExceptionsAsync(scope.StoreCodes[0],scope.DateTo); var status=rows.Any(x=>x.Severity is "BLOCKER" or "FAIL")?ReconciliationStatus.Failed:ReconciliationStatus.Passed; var message=rows.Count==0?"No daily exceptions were found.":"Every exception retains its exact variance and available source workbook/sheet/row pointer."; ReportGrid.ItemsSource=rows; ReportResult.Text=$"{status}: {rows.Count:N0} exception(s)."; SetExport("Daily Exceptions",status,RetailReportingPolicy.Version,message,[new("Severity"),new("Area"),new("Code"),new("Store"),new("Date"),new("Document"),new("Item"),new("Variance","#,##0.00"),new("Workbook"),new("Sheet"),new("Source Row","#,##0"),new("Message"),new("Recommended Action")],rows.Select(x=>(IReadOnlyList<object?>)[x.Severity,x.Area,x.Code,x.StoreCode,x.BusinessDate,x.DocumentNumber,x.ItemCode,x.Variance,x.SourceWorkbook,x.SourceSheet,x.SourceRow,x.Message,x.RecommendedAction]).ToArray(),["Total",rows.Count,"","","","","",rows.Where(x=>x.Variance is not null).Sum(x=>x.Variance),"","","","",""]); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Failed","Daily exception report"); }
        catch (Exception ex) { HandleFailure(ex, "DAILY_EXCEPTIONS_REPORT_FAILED", "Daily exceptions failed"); }
    }

    private void SetExport(string name, ReconciliationStatus status, string ruleVersion, string message, IReadOnlyList<ExcelReportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?> totals, DailySalesReportDocument? dsrReport = null, DateOnly? businessDate = null)
    {
        var scope=ReportScope(); var snapshot=presentation.SetReport(new(name,businessDate??scope.DateFrom,businessDate??scope.DateTo,status.ToString(),ruleVersion,message,DateTimeOffset.UtcNow),new(columns,rows,totals),dsrReport); var renderFailure=ReportPresentationHost.Show(snapshot); if(renderFailure is not null)_=auditRecorder("VisualRender","Failed","Visual summary could not be rendered; detailed report remained available"); previewUpdater(snapshot,ReportGrid.ItemsSource,ReportResult.Text); RefreshExportAvailability();
    }

    private void RefreshExportAvailability()
    {
        var enabled = presentation.Current.CanExportReport && !exportInProgress;
        ExportExcelButton.IsEnabled = ExportPdfButton.IsEnabled = enabled;
    }

    private string HandleFailure(Exception exception, string eventId, string operation)
    {
        DesktopDiagnostics.Record(exception, "Reports.Workspace", eventId);
        var message = $"{operation}: {DesktopFriendlyError.Describe(exception)}";
        ReportResult.Text = message;
        return message;
    }

    private void ReportSearch_TextChanged(object sender, RoutedEventArgs e) => ApplyReportFilter();
    private void ApplyReportFilter()
    {
        if(ReportGrid.ItemsSource is null)return; var search=ReportSearchInput.Text.Trim(); var varianceOnly=VarianceOnlyInput.IsChecked==true; var view=CollectionViewSource.GetDefaultView(ReportGrid.ItemsSource); view.Filter=item=>{if(item is null)return false;if(search.Length>0&&!item.ToString()!.Contains(search,StringComparison.OrdinalIgnoreCase))return false;if(!varianceOnly)return true;var property=item.GetType().GetProperty("Variance");return property?.GetValue(item) is decimal variance&&variance!=0;}; view.Refresh();
    }
    private void ReportGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) { if(ReportGrid.SelectedItem is not null)detailPresenter(ReportGrid.SelectedItem); }

    internal static string ToAuditOutcome(ApplicationReportStatus status) => ToAuditOutcome(status.ToString());
    internal static string ToAuditOutcome(ReconciliationStatus status) => ToAuditOutcome(status.ToString());
    internal static string ToAuditOutcome(string? status) => status switch
    {
        "Passed" or "Succeeded" => "Succeeded",
        "Failed" => "Failed",
        "Blocked" or "NotRun" => "Blocked",
        _ => "Blocked"
    };
    private static ReconciliationStatus ToReportingStatus(ApplicationReportStatus status) => status switch { ApplicationReportStatus.Passed=>ReconciliationStatus.Passed,ApplicationReportStatus.Failed=>ReconciliationStatus.Failed,ApplicationReportStatus.Blocked=>ReconciliationStatus.Blocked,_=>ReconciliationStatus.NotRun };
    private static IReadOnlyList<string>? Csv(string value) { var values=value.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();return values.Length==0?null:values; }
    private static string SafeFileName(string value) => string.Concat(value.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Replace(' ','_');
}
