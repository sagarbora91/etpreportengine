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
    private readonly Func<string, string, DateOnly, DateOnly, Task<IReadOnlyList<CashBookDay>>> cashBookLoader;
    private Func<string, bool> focusedWorkspaceRequester = static _ => true;
    private Func<string, string, string, Task> auditRecorder = static (_, _, _) => Task.CompletedTask;
    private Action<ReportPresentationSnapshot, IEnumerable?, string> previewUpdater = static (_, _, _) => { };
    private Action<string> dailySalesFailure = static _ => { };
    private Action<object> detailPresenter = static _ => { };
    private StoreScopeCatalog stores = new();
    public void SetStores(StoreScopeCatalog catalog) => stores = catalog;
    private bool exportInProgress;
    private int reportRevision;

    public ReportsWorkspaceView(
        Func<string> connectionStringProvider,
        Func<string, ControlledReportQuery> controlledReportQueryFactory,
        Func<string, OperationalReportQuery> operationalReportQueryFactory,
        Func<string, ManagementTrendQuery> managementTrendQueryFactory,
        IReportExportCoordinator exportCoordinator,
        TenderVarianceDiagnostic tenderVarianceDiagnostic,
        Func<string, string, DateOnly, DateOnly, Task<IReadOnlyList<CashBookDay>>>? cashBookLoader = null)
    {
        this.connectionStringProvider = connectionStringProvider ?? throw new ArgumentNullException(nameof(connectionStringProvider));
        this.controlledReportQueryFactory = controlledReportQueryFactory ?? throw new ArgumentNullException(nameof(controlledReportQueryFactory));
        this.operationalReportQueryFactory = operationalReportQueryFactory ?? throw new ArgumentNullException(nameof(operationalReportQueryFactory));
        this.managementTrendQueryFactory = managementTrendQueryFactory ?? throw new ArgumentNullException(nameof(managementTrendQueryFactory));
        this.exportCoordinator = exportCoordinator ?? throw new ArgumentNullException(nameof(exportCoordinator));
        this.tenderVarianceDiagnostic = tenderVarianceDiagnostic ?? throw new ArgumentNullException(nameof(tenderVarianceDiagnostic));
        this.cashBookLoader = cashBookLoader ?? ((connection, store, from, to) =>
            new Etp.Reporting.Infrastructure.SqlServer.OperationalReportRepository(connection).LoadCashBookAsync(store, from, to));
        InitializeComponent();
        ReportFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        ReportTo.SelectedDate = DateTime.Today.AddDays(-1);
        ReportFrom.SelectedDateChanged += (_, _) => InvalidateReportScope();
        ReportTo.SelectedDateChanged += (_, _) => InvalidateReportScope();
        foreach (var filter in new[] { StoreFilterInput, BrandSegmentFilterInput, TransactionTypeFilterInput, ItemFilterInput })
            filter.TextChanged += (_, _) => InvalidateReportScope();
        SalesDimensionInput.SelectionChanged += (_, _) => InvalidateReportScope();
    }

    public DateTime? DateFrom => ReportFrom.SelectedDate;
    public DateTime? DateTo => ReportTo.SelectedDate;
    public string? CurrentReportCode => presentation.Current.ReportCode;
    public string StoreScope => stores.Display(StoreFilterInput.Text.Trim());

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
        StoreFilterInput.Text = stores.Resolve(scope) ?? string.Empty;
    }

    public void SetBusinessDate(DateTime date) => ReportTo.SelectedDate = date;
    public void ApplyTaskScope(string report, DateTime? from, DateTime? to, string? scope) => ApplyScope(ReportTaskScope.IsSnapshot(report) ? ReportFrom.SelectedDate : from,to,scope);
    public void FocusSearch() { ReportSearchInput.Focus(); ReportSearchInput.SelectAll(); }
    public void ShowRowDetails(object row) => detailPresenter(row);

    public async Task RunReportAsync(string report)
    {
        ConfigureQueryFilters(report);
        if (report is "sales-combined" or "dsr") StoreFilterInput.Clear();
        if (report == "cash" && Csv(StoreFilterInput.Text) is not { Count: 1 } && stores.Stores.Count == 1)
            StoreFilterInput.Text = stores.Stores[0].Code;
        if (!BeginReportLoad(report)) return;
        if (ReportTaskScope.RequiresSingleStore(report) && Csv(StoreFilterInput.Text) is not { Count: 1 })
        { HandleFailure(new InvalidOperationException("Choose one store in the header."), "REPORT_STORE_REQUIRED", "Select a store"); return; }
        switch (report)
        {
            case "dsr": await RunDsrAsync(); break;
            case "sales-store": SelectSalesDimension("Daily"); await RunSalesReportAsync(); break;
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
            case "stock-physical": await RunPhysicalStockAsync(); break;
            case "stock-closing": await RunStockInventoryAsync("CLOSING"); break;
            case "stock-brand": await RunStockInventoryAsync("BRAND"); break;
            case "stock-slow": await RunStockInventoryAsync("SLOW"); break;
            case "stock-movement": await RunStockMovementAsync(); break;
            case "exceptions": await RunDailyExceptionsAsync(); break;
            case "management-trend": await RunManagementTrendReportAsync(); break;
        }
    }

    public void ExportExcel() => _ = ExportExcelAsync();

    public async Task ExportExcelAsync()
    {
        var report = presentation.Current;
        if (!report.CanExportReport || exportInProgress) return;
        var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"{SafeFileName(report.ExportMetadata!.ReportName)}_{report.ExportMetadata.DateFrom:yyyyMMdd}_{report.ExportMetadata.DateTo:yyyyMMdd}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        if (ReferenceEquals(report, presentation.Current)) await ExportReportToPathAsync(dialog.FileName, pdf: false);
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
        if (ReferenceEquals(report, presentation.Current)) await ExportReportToPathAsync(dialog.FileName, pdf: true);
    }

    private async void RunCatalogueReport_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string report }) await RunReportAsync(report); }
    private async void ExportExcel_Click(object sender, RoutedEventArgs e) => await ExportExcelAsync();
    private async void ExportPdf_Click(object sender, RoutedEventArgs e) => await ExportPdfAsync();

    private bool BeginReportLoad(string reportCode)
    {
        if (!focusedWorkspaceRequester(reportCode)) return false;
        ++reportRevision;
        presentation.BeginReport(reportCode);
        ExceptionFilters.Visibility=reportCode=="exceptions"?Visibility.Visible:Visibility.Collapsed;
        RefreshExportAvailability();
        ReportResult.Text = reportCode == "dsr" ? "Loading the approved Daily Sales Report… Existing context remains visible." : "Loading report… Existing context remains visible.";
        return true;
    }

    private void InvalidateReportScope()
    {
        ++reportRevision;
        filterWorkspace?.InvalidateQueryFilters();
        if (presentation.Current.ReportCode is not { } reportCode) return;
        presentation.BeginReport(reportCode);
        ExceptionFilters.Visibility=reportCode=="exceptions"?Visibility.Visible:Visibility.Collapsed;
        RefreshExportAvailability();
        ReportResult.Text = "Filters changed. Run the report again before exporting. Existing results belong to the previous scope.";
    }

    private ApplicationReportScope ReportScope()
    {
        if (ReportFrom.SelectedDate is null || ReportTo.SelectedDate is null) throw new InvalidOperationException("Select both report dates.");
        return new(DateOnly.FromDateTime(ReportTaskScope.IsSnapshot(presentation.Current.ReportCode) ? ReportTo.SelectedDate.Value : ReportFrom.SelectedDate.Value), DateOnly.FromDateTime(ReportTo.SelectedDate.Value), Csv(StoreFilterInput.Text), Csv(BrandSegmentFilterInput.Text), Csv(TransactionTypeFilterInput.Text), Csv(ItemFilterInput.Text));
    }

    private void SelectSalesDimension(string name) =>
        SalesDimensionInput.SelectedItem = SalesDimensionInput.Items.OfType<ComboBoxItem>().First(x => string.Equals(x.Content?.ToString(), name, StringComparison.Ordinal));

    private async Task RunStockInventoryAsync(string mode)
    {
        var revision = reportRevision;
        try
        {
            var stockScope = ReportScope();
            var rows = await operationalReportQueryFactory(connectionStringProvider()).LoadStockInventoryAsync(stockScope);
            var sources = SnapshotSourceText(rows.Select(x => (x.StoreCode, x.SnapshotDate, x.SnapshotSource))) + MissingSnapshotText(stockScope.StoreCodes, rows.Select(x => x.StoreCode), stockScope.DateTo);
            if (mode == "SLOW") rows = rows.Where(x => x.Quantity != 0 && x.MovementStatus != "ACTIVE").ToArray();
            if (mode == "BRAND")
            {
                var grouped = rows.GroupBy(x => new { x.StoreCode, BrandRow = x.StockGroup, Brand = x.Brand ?? "Unmapped", Group = x.InventoryGroup ?? "Unmapped" }).Select(x => new { x.Key.StoreCode, x.Key.BrandRow, x.Key.Brand, InventoryGroup = x.Key.Group, Quantity = x.Sum(y => y.Quantity), MrpValue = x.Any(y => y.TotalCost is not null) ? (decimal?)x.Sum(y => y.TotalCost ?? 0) : null, Items = x.Select(y => y.ProductCode).Distinct().Count(), SlowItems = x.Count(y => StockAgeing.IsSlow(y.Quantity, y.MovementStatus)) }).OrderBy(x => x.StoreCode).ThenBy(x => x.BrandRow).ThenBy(x => x.InventoryGroup).ThenBy(x => x.Brand).ToArray();
                var status = grouped.Length == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed; if (revision != reportRevision) return; ReportGrid.ItemsSource = grouped; ReportResult.Text = IndianText($"{status}: {grouped.Length:N0} store/brand/inventory-group row(s).{sources}");
                SetExport("Brand Stock", status, RetailReportingPolicy.Version, "Closing stock grouped from the saved ETP snapshot; quantity and MRP are never inferred. " + StockMrpNote + BrandRowNote + sources, [new("Store"),new("Brand row"),new("Brand"),new("Inventory Group"),new("Quantity","#,##0.00"),new(MrpValueHeader,"#,##0.00"),new("Items","#,##0"),new("Slow Items","#,##0")], grouped.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.BrandRow,x.Brand,x.InventoryGroup,x.Quantity,x.MrpValue,x.Items,x.SlowItems]).ToArray(), ["Total","","","",grouped.Sum(x=>x.Quantity),grouped.Sum(x=>x.MrpValue),grouped.Sum(x=>x.Items),grouped.Sum(x=>x.SlowItems)]);
            }
            else
            {
                var status = rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed; var name = mode == "SLOW" ? "Slow / Exception Stock" : "Closing Stock"; if (revision != reportRevision) return; ReportGrid.ItemsSource = rows; ReportResult.Text = IndianText($"{status}: {rows.Count:N0} item(s).{sources} Slow stock uses 60-day watch and 90-day exception bands.{NewStockText(rows)}");
                SetExport(name, status, RetailReportingPolicy.Version, "Closing quantities and MRP come from the selected-date ETP stock snapshot. " + StockMrpNote + " Last sale is the latest positive source-signed sale on or before that date." + ReceiptNote + sources, [new("Date"),new("Store"),new("Item"),new("Brand"),new("Inventory Group"),new("Quantity","#,##0.00"),new(UnitMrpHeader,"#,##0.00"),new(MrpValueHeader,"#,##0.00"),new("Last Sale"),new("Days Since Sale","#,##0"),new("Last Receipt"),new("Days Since Receipt","#,##0"),new("Movement Status"),new("Snapshot Source")], rows.Select(x => (IReadOnlyList<object?>)[x.SnapshotDate,x.StoreCode,x.ProductCode,x.Brand,x.InventoryGroup,x.Quantity,x.UnitCost,x.TotalCost,x.LastSaleDate,x.DaysSinceLastSale,x.LastReceiptDate,x.DaysSinceReceipt,x.MovementStatus,x.SnapshotSource]).ToArray(), ["Total","","","","",rows.Sum(x=>x.Quantity),"",rows.Sum(x=>x.TotalCost),"","","","","",""]);
            }
            ApplyReportFilter(); await auditRecorder("ReportRun", ToAuditOutcome(rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed), mode == "BRAND" ? "Brand stock" : mode == "SLOW" ? "Slow stock" : "Closing stock");
        }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "STOCK_REPORT_FAILED", "Stock report failed"); }
    }

    // Owner answer Q3/Q5: the owner's brand rows (Settings > Evening masters) mapped by cluster.
    internal const string BrandRowNote = " Brand row is the Daily Sales Report brand row mapped to the item's cluster in Settings > Evening masters, else the brand.";

    // Owner answer Q8: recently received items show as NEW, aged by their receipt date.
    internal const string ReceiptNote = " Last receipt is the latest Purchase, STM or Stock Receipt in the stock ledger on or before that date; an item received in the last 60 days and not sold since is NEW, not never sold.";

    internal static string NewStockText(IEnumerable<EtpApplication::Etp.Reporting.Application.Reports.StockInventoryRecord> rows)
    {
        var count = rows.Count(x => x.MovementStatus == StockAgeing.New);
        return count == 0 ? "" : $" {count:N0} NEW (received in the last {StockAgeing.NewWindowDays} days).";
    }

    // Spec 12: the stock report states the snapshot source used per store-day (Closing Stock or BinWise).
    internal static string SnapshotSourceText(IEnumerable<(string StoreCode, DateOnly SnapshotDate, string? Source)> rows)
    {
        var days = rows.GroupBy(x => (x.StoreCode, x.SnapshotDate)).OrderBy(x => x.Key.StoreCode, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key.SnapshotDate)
            .Select(x => $"{x.Key.StoreCode} {x.Key.SnapshotDate.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture)} {string.Join(" + ", x.Select(y => y.Source ?? "Unknown").Distinct(StringComparer.Ordinal))}")
            .ToArray();
        return days.Length == 0 ? "" : " Snapshot source: " + string.Join("; ", days) + ".";
    }

    // Owner answer Q9: a store with no snapshot on the date is named, instead of a bare "Blocked: 0 item(s)".
    internal static string MissingSnapshotText(IReadOnlyList<string>? requestedStores, IEnumerable<string> storesWithRows, DateOnly date)
    {
        var present = storesWithRows.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var day = date.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        if (requestedStores is not { Count: > 0 }) return present.Count == 0 ? $" No closing-stock snapshot for any store on {day}." : "";
        var missing = requestedStores.Where(x => !present.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return missing.Length == 0 ? "" : $" No closing-stock snapshot for {string.Join(", ", missing)} on {day}.";
    }

    private async Task RunStockMovementAsync()
    {
        var revision = reportRevision;
        try { var scope = ReportScope(); var rows = await controlledReportQueryFactory(connectionStringProvider()).LoadStockMovementsAsync(scope); var snapshotNote = StockMovementSnapshotNote(rows, scope.DateTo); var status = rows.Count == 0 || snapshotNote.Length > 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed; if (revision != reportRevision) return; ReportGrid.ItemsSource = rows; ReportResult.Text = IndianText($"{status}: {rows.Count:N0} source movement group(s).") + snapshotNote; SetExport("Stock Movement", status, RetailReportingPolicy.Version, "Movement quantities retain the ETP source transaction type and source-signed quantity." + snapshotNote, [new("Store"),new("Item"),new("Location"),new("Movement Type"),new("Signed Quantity","#,##0.00"),new("Snapshot")], rows.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.ItemCode,x.Location,x.SourceMovementType,x.SourceSignedQuantity,x.Snapshot]).ToArray(), ["Total","","","",rows.Sum(x=>x.SourceSignedQuantity),""]); ApplyReportFilter(); await auditRecorder("ReportRun", ToAuditOutcome(status), "Stock movement"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "STOCK_MOVEMENT_REPORT_FAILED", "Stock movement report failed"); }
    }

    // Owner answer Q9: movements of a store with no closing-stock snapshot on the To date are listed, marked "no snapshot".
    internal static string StockMovementSnapshotNote(IEnumerable<EtpApplication::Etp.Reporting.Application.Reports.StockMovementRecord> rows, DateOnly dateTo)
    {
        var stores = rows.Where(x => !x.HasSnapshot).Select(x => x.StoreCode).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return stores.Length == 0 ? "" : $" No closing-stock snapshot for {string.Join(", ", stores)} on {dateTo.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}; movements are shown, closing stock cannot be checked.";
    }

    private async Task RunManagementTrendReportAsync()
    {
        var revision = reportRevision;
        try { var rows = await managementTrendQueryFactory(connectionStringProvider()).LoadAsync(ReportScope()); var status = rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed; if (revision != reportRevision) return; ReportGrid.ItemsSource=rows; var missingTender = rows.Count(x => x.TenderVariance is null); ReportResult.Text=IndianText($"{status}: {rows.Count:N0} daily management trend row(s).") + (missingTender == 0 ? "" : IndianText($" {missingTender:N0} store-day(s) show R022 missing / not imported; their tender variance is blank, not zero.")); SetExport("Management Trend",status,RetailReportingPolicy.Version,"Daily recorded GST-inclusive sales (R025 NETAMOUNT), units, invoice and return counts and unchanged control variances. " + InvoicesNote,[new("Date"),new("Store"),new("Net Sales","#,##0.00"),new("Units","#,##0.00"),new(InvoicesHeader,"#,##0"),new(ReturnsHeader,"#,##0"),new("Tender Variance","#,##0.00"),new("Tender Source"),new("Unmatched Staff Rows","#,##0")],rows.Select(x=>(IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.NetSales,x.Units,x.Invoices,x.Returns,x.TenderVariance,x.TenderSource,x.UnmatchedEnrichmentRows]).ToArray(),["Total","",rows.Sum(x=>x.NetSales),rows.Sum(x=>x.Units),rows.Sum(x=>x.Invoices),rows.Sum(x=>x.Returns),missingTender == 0 ? rows.Sum(x=>x.TenderVariance) : null,missingTender == 0 ? "" : $"{missingTender:N0} missing",rows.Sum(x=>x.UnmatchedEnrichmentRows)]); ApplyReportFilter(); await auditRecorder("ReportRun",ToAuditOutcome(status),"Management trend"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "MANAGEMENT_TREND_REPORT_FAILED", "Management trend failed"); }
    }

    // Report audit of 3 Oct 2026 (fix lists FIX-04, FIX-05, FIX-08, FIX-11, FIX-12): wording only, no figure changes.
    // Stock UCP/TOTALUCP are MRP (GST-inclusive retail value), not cost. Owner decision 13 Q6: every screen counts INV
    // documents only as "Invoices", like the DSR INVOICE count (G5), and shows SR and BC documents as "Returns".
    internal const string UnitMrpHeader = "Unit MRP";
    internal const string MrpValueHeader = "MRP value (GST incl.)";
    internal const string StockMrpNote = "MRP value is UCP × quantity, the GST-inclusive retail value, not cost.";
    internal const string InvoicesHeader = "Invoices";
    internal const string ReturnsHeader = "Returns";
    internal const string InvoicesNote = "Invoices counts INV documents only, like the Daily Sales Report; Returns counts sales returns (SR) and bill cancellations (BC).";

    private async Task RunSalesReportAsync()
    {
        var revision = reportRevision;
        try
        {
            var name = ((ComboBoxItem)SalesDimensionInput.SelectedItem).Content!.ToString()!;
            var scope = ReportScope();
            var result = await controlledReportQueryFactory(connectionStringProvider()).RunSalesSummaryAsync(scope, Enum.Parse<ApplicationSalesDimension>(name));
            if (revision != reportRevision) return;
            var sales = result.Rows.Sum(row => row.SourceSignedNetAmount);
            var units = result.Rows.Sum(row => row.SourceSignedQuantity);
            ReportGrid.ItemsSource = result.Rows;
            ReportResult.Text = IndianText($"{result.Status}: Sales incl. GST {sales:N2}; units {units:N2}. {result.Message}");
            SetExport($"{name} Sales", ToReportingStatus(result.Status), result.PolicyVersion, result.Message + " " + InvoicesNote,
                [new("Group"), new("Units", "#,##0.00"), new("Net Sales", "#,##0.00"), new(InvoicesHeader, "#,##0"), new(ReturnsHeader, "#,##0")],
                result.Rows.Select(row => (IReadOnlyList<object?>)[row.Key, row.SourceSignedQuantity, row.SourceSignedNetAmount, row.Invoices, row.Returns]).ToArray(),
                ["Total", units, sales, result.Rows.Sum(row => row.Invoices), result.Rows.Sum(row => row.Returns)]);
            ApplyReportFilter();
            await auditRecorder("ReportRun", ToAuditOutcome(result.Status), "Sales report");
        }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "SALES_REPORT_FAILED", "Sales report failed"); }
    }

    /// <summary>
    /// Owner decision 13 Q6: a document counts as an invoice when it has an INV line and as a return when it has an
    /// SR or BC line (the row's transaction types, for example INV+BC for a cancelled bill), like the DSR.
    /// </summary>
    internal static string CustomerWiseCounts(IReadOnlyList<EtpApplication::Etp.Reporting.Application.Reports.InvoiceSummaryRecord> rows)
    {
        static bool Has(EtpApplication::Etp.Reporting.Application.Reports.InvoiceSummaryRecord row, params string[] types) =>
            row.TransactionTypes.Split('+', StringSplitOptions.TrimEntries).Any(type => types.Contains(type, StringComparer.OrdinalIgnoreCase));
        return $"{rows.Count(x => Has(x, "INV")):N0} invoices, {rows.Count(x => Has(x, "SR", "BC")):N0} returns; {rows.Count(x => string.IsNullOrWhiteSpace(x.CustomerName)):N0} missing customer names.";
    }

    private async Task RunInvoiceSummaryAsync()
    {
        var revision=reportRevision;
        try {
            var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadInvoiceSummaryAsync(ReportScope());
            if(revision!=reportRevision)return; ReportGrid.ItemsSource=rows;
            var status=rows.Count==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed;
            ReportResult.Text=CustomerWiseCounts(rows);
            SetExport("Customer-wise Invoices",status,RetailReportingPolicy.Version,ReportResult.Text,
                [new("Date","date"),new("Store"),new("Invoice"),new("Customer name"),new("Invoice quantity","#,##0.00"),new("Value incl. GST","#,##0.00")],
                rows.Select(x=>(IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.DocumentNumber,x.CustomerName??"Name unavailable",x.Quantity,x.NetValue]).ToArray(),
                ["Grand total","","","",rows.Sum(x=>x.Quantity),rows.Sum(x=>x.NetValue)]);
            ApplyReportFilter();
            await auditRecorder("ReportRun",ToAuditOutcome(status),"Customer invoice report");
        }catch(Exception e){if(revision==reportRevision)HandleFailure(e,"INVOICE_SUMMARY_FAILED","Customer report failed");}
    }

    private async Task RunInvoiceLineageAsync()
    {
        var revision = reportRevision;
        try { var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadInvoiceLineageAsync(ReportScope()); var status=rows.Count==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed; const string message="Invoice and item drill-down is traceable to its source workbook, sheet and row. Sales values include GST."; if (revision != reportRevision) return; ReportGrid.ItemsSource=rows; ReportResult.Text=IndianText($"{status}: {rows.Count:N0} recorded line(s)."); SetExport("Invoice Sales source history",status,RetailReportingPolicy.Version,message,[new("Business Date"),new("Store"),new("Document"),new("Line"),new("Item"),new("Brand"),new("Segment"),new("Transaction Type"),new("Quantity","#,##0.00"),new("Value incl. GST","#,##0.00"),new("CRO"),new("Workbook"),new("Sheet"),new("Source Row","#,##0")],rows.Select(x=>(IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.DocumentNumber,x.LineIdentifier,x.ProductCode,x.Brand,x.BrandSegment,x.TransactionType,x.Quantity,x.NetValue,x.CroNumber,x.SourceWorkbook,x.SourceSheet,x.SourceRow]).ToArray(),["Total","","","","","","","",rows.Sum(x=>x.Quantity),rows.Sum(x=>x.NetValue),"","","",rows.Count]); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Blocked","Invoice source history report"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "INVOICE_DRILLDOWN_FAILED", "Invoice drill-down failed"); }
    }

    private async Task RunDsrAsync()
    {
        var revision=reportRevision;
        try {
            var scope=ReportScope();var repo=operationalReportQueryFactory(connectionStringProvider());
            var rows=await repo.LoadDsrAsync(scope.DateTo,[]);var document=await repo.ComposeDsrDocumentAsync(scope.DateTo,rows);
            if(revision!=reportRevision)return;ReportGrid.ItemsSource=rows;
            var status=rows.Any(x=>x.TySales is not null)?ReconciliationStatus.Passed:ReconciliationStatus.Blocked;
            ReportResult.Text="GST-inclusive sales; invoice counts exclude returns. Gift cards are on their own GIFT CARD line, not in VALUE, VOL or INVOICE. Manual totals use available entries. Check Other / unmapped brands in Settings.";
            var data=EveningReportTables.Dsr(document.EveningSheets);
            SetExport("Daily Sales Report",status,RetailReportingPolicy.Version,ReportResult.Text,data.Columns,data.Rows,data.Totals,document,scope.DateTo);
            ApplyReportFilter();
            await auditRecorder("ReportRun",ToAuditOutcome(status),"Daily sales report");
        }catch(Exception e){if(revision==reportRevision)dailySalesFailure(HandleFailure(e,"DSR_REPORT_FAILED","DSR failed"));}
    }

    private async Task RunStaffPerformanceAsync()
    {
        var revision = reportRevision;
        try
        {
            var result = await operationalReportQueryFactory(connectionStringProvider()).LoadStaffPerformanceAsync(ReportScope());
            if (revision != reportRevision) return; ReportGrid.ItemsSource = result.Rows;
            ReportResult.Text = IndianText($"{result.Status}: recorded {result.CanonicalSales:N2}, attributed {result.AttributedSales:N2}, variance {result.Variance:N2}. {result.Message}");
            SetExport("Staff CRO Performance",ToReportingStatus(result.Status), result.MetricPolicy, result.Message,
                [new("Store"),new("CRO"),new("CRO name"),new("Value incl. GST","#,##0.00"),new("LY Sales","#,##0.00"),new("Growth %","0.00%"),new("Growth Status"),new("Net Quantity","#,##0.00"),new("Discount","#,##0.00"),new("Unique invoices","#,##0"),new("AUPT","#,##0.00"),new("ATV","#,##0.00"),new("Contribution %","0.00%"),new("Target","#,##0.00"),new("Achievement %","0.00%"),new("Rank","#,##0")],
                result.Rows.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.CroNumber,x.CroName,x.NetSales,x.LastYearSales,x.GrowthPercent,x.GrowthStatus,x.NetQuantity,x.Discount,x.Transactions,x.Upt,x.Atv,x.ContributionPercent,x.TargetSales,x.TargetAchievementPercent,x.Rank]).ToArray(),
                ["Control","","",result.AttributedSales,"","","","","",result.Rows.Sum(x=>x.Transactions),"","",result.Variance,"","",""]);
            ApplyReportFilter();
            await auditRecorder("ReportRun", ToAuditOutcome(result.Status), "Staff performance");
        }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "STAFF_REPORT_FAILED", "Staff report failed"); }
    }

    private async Task RunServiceSalesAsync()
    {
        var revision = reportRevision;
        try { var scope=ReportScope(); var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadServiceSalesAsync(scope.DateTo,scope.StoreCodes); var status=rows.Any(x=>x.Total is not null)?ReconciliationStatus.Passed:ReconciliationStatus.Blocked; const string message="Service totals sum available entries. Missing days count days without all three service modes."; if (revision != reportRevision) return; ReportGrid.ItemsSource=rows; ReportResult.Text=$"{status}: {message}"; SetExport("Service Sales",status,RetailReportingPolicy.Version,message,[new("Period"),new("Store"),new("From"),new("To"),new("WDC","#,##0.00"),new("Cash","#,##0.00"),new("Card","#,##0.00"),new("UPI","#,##0.00"),new("Total","#,##0.00"),new("LY Total","#,##0.00"),new("Growth %","0.00%"),new("Availability"),new("Missing days","#,##0"),new("LY missing days","#,##0")],rows.Select(x=>(IReadOnlyList<object?>)[x.Period,x.StoreCode,x.PeriodStart,x.PeriodEnd,x.Wdc,x.Cash,x.Card,x.Upi,x.Total,x.LastYearTotal,x.GrowthPercent,x.Availability,x.MissingDays,x.LastYearMissingDays]).ToArray(),null); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Blocked","Service sales"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "SERVICE_REPORT_FAILED", "Service report failed"); }
    }

    private async Task RunCashReconciliationAsync()
    {
        var revision=reportRevision;
        try {
            var scope=ReportScope();if(scope.StoreCodes is not {Count:1})throw new InvalidOperationException("Choose one store for the cash book.");
            var days=await cashBookLoader(connectionStringProvider(),scope.StoreCodes[0],scope.DateFrom,scope.DateTo);
            if(revision!=reportRevision)return;
            var data=CashBookTables.Create(days);
            var table=new System.Data.DataTable();
            foreach(var col in data.Columns)
                table.Columns.Add(col.Header,col.NumberFormat=="date"?typeof(DateOnly):col.NumberFormat=="#,##0.00"?typeof(decimal):typeof(string));
            foreach(var row in data.Rows)table.Rows.Add(row.Select(x=>x??DBNull.Value).ToArray());
            ReportGrid.ItemsSource=table.DefaultView;
            var status=days.All(x=>x.Status=="Complete")?ReconciliationStatus.Passed:ReconciliationStatus.Blocked;
            ReportResult.Text=$"{days.Count} days. Opening carries forward from the previous calculated closing. Enter opening overrides with a reason in Daily inputs.";
            SetExport("Cash Book",status,RetailReportingPolicy.Version,ReportResult.Text,data.Columns,data.Rows,data.Totals);
            ApplyReportFilter();
            await auditRecorder("ReportRun",ToAuditOutcome(status),"Cash book");
        }catch(Exception e){if(revision==reportRevision)HandleFailure(e,"CASH_BOOK_FAILED","Cash book failed");}
    }

    private async Task RunTenderReportAsync()
    {
        var revision = reportRevision;
        try { var r=await controlledReportQueryFactory(connectionStringProvider()).RunTenderReconciliationAsync(ReportScope()); if (revision != reportRevision) return; ReportGrid.ItemsSource=r.Documents; ReportResult.Text=IndianText($"{r.Status}: invoice {r.InvoiceTotal:N2}, tender {r.TenderTotal:N2}, variance {r.Variance:N2}. {r.Message}"); SetExport("Invoice Tender Reconciliation",ToReportingStatus(r.Status),r.RuleVersion,r.Message,[new("Store"),new("Document"),new("Invoice","#,##0.00"),new("Tender","#,##0.00"),new("Variance","#,##0.00"),new("Status")],r.Documents.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.DocumentNumber,x.InvoiceAmount,x.TenderAmount,x.Variance,x.Status.ToString()]).ToArray(),["Total","",r.InvoiceTotal,r.TenderTotal,r.Variance,r.Status.ToString()]); ApplyReportFilter(); await auditRecorder("ReportRun",r.Status==ApplicationReportStatus.Passed?"Succeeded":r.Status.ToString(),"Tender control"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "TENDER_RECONCILIATION_FAILED", "Tender reconciliation failed"); }
    }

    private async Task RunTenderDiagnosticAsync()
    {
        var revision = reportRevision;
        try { var reconciliation=await controlledReportQueryFactory(connectionStringProvider()).RunTenderReconciliationAsync(ReportScope()); var diagnostic=tenderVarianceDiagnostic.Diagnose(reconciliation,RetailReportingPolicy.Tender.AbsoluteTolerance); if (revision != reportRevision) return; ReportGrid.ItemsSource=diagnostic.Rows; ReportResult.Text=IndianText($"{diagnostic.Status}: {diagnostic.FailedDocuments:N0} documents require review; absolute variance {diagnostic.AbsoluteVariance:N2}. Classifications do not change the control result."); SetExport("Tender Variance Diagnostics",ToReportingStatus(diagnostic.Status),diagnostic.RuleVersion,diagnostic.Message,[new("Store"),new("Document"),new("Invoice","#,##0.00"),new("Tender","#,##0.00"),new("Variance","#,##0.00"),new("Likely Cause"),new("Recommended Check")],diagnostic.Rows.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.DocumentNumber,x.InvoiceAmount,x.TenderAmount,x.Variance,x.LikelyCause.ToString(),x.RecommendedCheck]).ToArray(),["Total","",reconciliation.InvoiceTotal,reconciliation.TenderTotal,reconciliation.Variance,diagnostic.Status.ToString(),$"{diagnostic.FailedDocuments:N0} documents"]); ApplyReportFilter(); await auditRecorder("ReportRun",diagnostic.Status==ApplicationReportStatus.Passed?"Succeeded":diagnostic.Status.ToString(),"Tender diagnostic"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "TENDER_DIAGNOSTICS_FAILED", "Tender diagnostics failed"); }
    }

    private async Task RunStockReportAsync()
    {
        var revision = reportRevision;
        try { var r=await controlledReportQueryFactory(connectionStringProvider()).RunStockReconciliationAsync(ReportScope()); if (revision != reportRevision) return; ReportGrid.ItemsSource=r.Items; ReportResult.Text=$"{r.Status}: {r.Message}"; SetExport("Stock Reconciliation",ToReportingStatus(r.Status),r.RuleVersion,r.Message,[new("Store"),new("Item"),new("Opening","#,##0.00"),new("Movements","#,##0.00"),new("Expected Closing","#,##0.00"),new("Reported Closing","#,##0.00"),new("Variance","#,##0.00"),new("Status"),new("Snapshot")],r.Items.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.ItemCode,x.Opening,x.SourceSignedMovements,x.ExpectedClosing,x.ReportedClosing,x.Variance,x.Status.ToString(),x.Snapshot]).ToArray(),["Total","",r.Items.Sum(x=>x.Opening),r.Items.Sum(x=>x.SourceSignedMovements),r.Items.Sum(x=>x.ExpectedClosing),r.Items.Sum(x=>x.ReportedClosing),r.Items.Sum(x=>x.Variance),r.Status.ToString(),""]); ApplyReportFilter(); await auditRecorder("ReportRun",r.Status==ApplicationReportStatus.Passed?"Succeeded":r.Status.ToString(),"Stock control"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "STOCK_RECONCILIATION_FAILED", "Stock reconciliation failed"); }
    }

    private async Task RunPhysicalStockAsync()
    {
        var revision = reportRevision;
        try { var scope=ReportScope(); if(scope.StoreCodes is not {Count:1})throw new InvalidOperationException("Enter exactly one store code for physical stock reporting."); var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadPhysicalStockAsync(scope.StoreCodes[0],scope.DateTo); var status=rows.Any(x=>x.Status=="FAIL")?ReconciliationStatus.Failed:rows.Count==0||rows.Any(x=>x.Status!="PASS")?ReconciliationStatus.Blocked:ReconciliationStatus.Passed; const string message="Physical = Display + Backstock + Defective + Y Location. Difference = Physical − System. Missing counts remain blank."; if (revision != reportRevision) return; ReportGrid.ItemsSource=rows; ReportResult.Text=IndianText($"{status}: {rows.Count:N0} brand(s)."); SetExport("Physical Closing Stock",status,RetailReportingPolicy.Version,message,[new("Store"),new("Date"),new("Brand"),new("Display","#,##0.00"),new("Backstock","#,##0.00"),new("Defective","#,##0.00"),new("Y Location","#,##0.00"),new("Physical","#,##0.00"),new("System","#,##0.00"),new("System Variance","#,##0.00"),new("Remarks"),new("Status")],rows.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.BusinessDate,x.InventoryGroupCode,x.DisplayQuantity,x.BackstockQuantity,x.DefectiveQuantity,x.YLocationQuantity,x.ComponentTotal,x.SystemQuantity,x.SystemVariance,x.Remarks,x.Status]).ToArray(),["Total","","",rows.Sum(x=>x.DisplayQuantity),rows.Sum(x=>x.BackstockQuantity),rows.Sum(x=>x.DefectiveQuantity),rows.Sum(x=>x.YLocationQuantity),rows.All(x=>x.ComponentTotal!=null)?rows.Sum(x=>x.ComponentTotal):null,rows.Sum(x=>x.SystemQuantity),rows.Sum(x=>x.SystemVariance),"",status.ToString()]); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":status.ToString(),"Physical stock report"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "PHYSICAL_STOCK_REPORT_FAILED", "Physical stock report failed"); }
    }

    private async Task RunDailyExceptionsAsync()
    {
        var revision = reportRevision;
        try { var scope=ReportScope(); if(scope.StoreCodes is not {Count:1})throw new InvalidOperationException("Enter exactly one store code for the daily exception report."); if(scope.DateFrom!=scope.DateTo)throw new InvalidOperationException("Select one business date for the daily exception report."); var rows=await operationalReportQueryFactory(connectionStringProvider()).LoadDailyExceptionsAsync(scope.StoreCodes[0],scope.DateTo); var status=rows.Any(x=>x.Severity is "BLOCKER" or "FAIL")?ReconciliationStatus.Failed:ReconciliationStatus.Passed; var message=rows.Count==0?"No daily exceptions were found.":"Every exception retains its exact variance and available source workbook/sheet/row pointer."; if (revision != reportRevision) return; ReportGrid.ItemsSource=rows; ReportResult.Text=IndianText($"{status}: {rows.Count:N0} exception(s)."); SetExport("Daily Exceptions",status,RetailReportingPolicy.Version,message,[new("Severity"),new("Area"),new("Code"),new("Store"),new("Date"),new("Document"),new("Item"),new("Variance","#,##0.00"),new("Workbook"),new("Sheet"),new("Source Row","#,##0"),new("Message"),new("Recommended Action")],rows.Select(x=>(IReadOnlyList<object?>)[x.Severity,x.Area,x.Code,x.StoreCode,x.BusinessDate,x.DocumentNumber,x.ItemCode,x.Variance,x.SourceWorkbook,x.SourceSheet,x.SourceRow,x.Message,x.RecommendedAction]).ToArray(),["Total",rows.Count,"","","","","",rows.Where(x=>x.Variance is not null).Sum(x=>x.Variance),"","","","",""]); ApplyReportFilter(); await auditRecorder("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Failed","Daily exception report"); }
        catch (Exception ex) { if (revision != reportRevision) return; HandleFailure(ex, "DAILY_EXCEPTIONS_REPORT_FAILED", "Daily exceptions failed"); }
    }

    private void SetExport(string name, ReconciliationStatus status, string ruleVersion, string message, IReadOnlyList<ExcelReportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? totals, DailySalesReportDocument? dsrReport = null, DateOnly? businessDate = null)
    {
        var scope=ReportScope(); var snapshot=presentation.SetReport(new(name,businessDate??scope.DateFrom,businessDate??scope.DateTo,status.ToString(),ruleVersion,message,DateTimeOffset.UtcNow,AppliedQueryScope()),new(columns,rows,totals),dsrReport); var renderFailure=ReportPresentationHost.Show(snapshot); if(renderFailure is not null)_=auditRecorder("VisualRender","Failed","Visual summary could not be rendered; detailed report remained available"); previewUpdater(snapshot,ReportGrid.ItemsSource,ReportResult.Text); RefreshExportAvailability();
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
        previewUpdater(presentation.Current,ReportGrid.ItemsSource,message);
        return message;
    }

    private string exceptionFocus="All";
    private void ExceptionFilter_Click(object sender,RoutedEventArgs e)
    {
        if(sender is Button { Tag:string focus }) { exceptionFocus=focus;ApplyReportFilter(); }
    }
    private bool MatchesException(object item)
    {
        var area=item.GetType().GetProperty("Area")?.GetValue(item)?.ToString()??"";
        var code=item.GetType().GetProperty("Code")?.GetValue(item)?.ToString()??"";
        return exceptionFocus switch { "All"=>true,"Unmapped"=>code.Contains("MISSING")||code.Contains("AMBIGUOUS")||code.Contains("UNMAPPED"),_=>area.Contains(exceptionFocus,StringComparison.OrdinalIgnoreCase) };
    }
    private void ReportSearch_TextChanged(object sender, RoutedEventArgs e) => ApplyReportFilter();
    private void ViewDetails_Click(object sender, RoutedEventArgs e) => ShowSelectedDetails();
    private void ApplyReportFilter()
    {
        if (ReportGrid.ItemsSource is null) return;
        var search = ReportSearchInput.Text.Trim();
        var varianceOnly = VarianceOnlyInput.IsChecked == true;
        var view = CollectionViewSource.GetDefaultView(ReportGrid.ItemsSource);
        // DataView's default view cannot accept predicates. A separate list view
        // preserves its typed column schema while keeping all rows available to search.
        if (!view.CanFilter)
        {
            view = new ListCollectionView(ReportGrid.ItemsSource as IList ?? ReportGrid.ItemsSource.Cast<object>().ToList());
            ReportGrid.ItemsSource = view;
        }
        view.Filter = item => item is not null
            && (search.Length == 0 || PageSearch.Matches(item, search))
            && (presentation.Current.ReportCode != "exceptions" || MatchesException(item))
            && (!varianceOnly || ReportDetailFilter.HasVariance(item));
        view.Refresh();
    }
    private void ReportGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ShowSelectedDetails();
    private void ShowSelectedDetails() { if (ReportGrid.SelectedItem is not null) detailPresenter(ReportGrid.SelectedItem); else ReportResult.Text = "Select a report row to view its details and source source history."; }

    internal static string ToAuditOutcome(ApplicationReportStatus status) => ToAuditOutcome(status.ToString());
    internal static string ToAuditOutcome(ReconciliationStatus status) => ToAuditOutcome(status.ToString());
    internal static string ToAuditOutcome(string? status) => status switch
    {
        "Passed" or "Succeeded" => "Succeeded",
        "Failed" => "Failed",
        "Blocked" or "NotRun" => "Blocked",
        _ => "Blocked"
    };
    // Status lines use the app's Indian digit grouping (10,15,259.10), like the grids and tiles, whatever the thread culture.
    internal static string IndianText(FormattableString text) => text.ToString(PresentationCulture.Indian);
    private static ReconciliationStatus ToReportingStatus(ApplicationReportStatus status) => status switch { ApplicationReportStatus.Passed=>ReconciliationStatus.Passed,ApplicationReportStatus.Failed=>ReconciliationStatus.Failed,ApplicationReportStatus.Blocked=>ReconciliationStatus.Blocked,_=>ReconciliationStatus.NotRun };
    private static IReadOnlyList<string>? Csv(string value) { var values=value.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();return values.Length==0?null:values; }
    // The proposed export file name for a report name (RA-EXPORT-03): "Slow / Exception Stock" holds a '/', which Windows
    // rejects in a file name, so every invalid character and every space becomes '_'. Excel and PDF exports share it.
    internal static string SafeFileName(string value) => string.Concat(value.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)).Replace(' ','_');
}
