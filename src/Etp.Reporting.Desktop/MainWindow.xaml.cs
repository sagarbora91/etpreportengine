using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Win32;

namespace Etp.Reporting.Desktop;

public partial class MainWindow : Window
{
    private WorkbookSnapshot? validatedWorkbook;
    private ImportPreflightResult? validatedPreflight;
    private ImportStagingResult? validatedStaging;
    private ExcelReportMetadata? currentExportMetadata;
    private ExcelReportData? currentExportData;
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "settings.json");
    private static readonly IReadOnlyDictionary<string, PageDetails> Pages = new Dictionary<string, PageDetails>(StringComparer.Ordinal)
    {
        ["Dashboard"] = new("Application and import readiness at a glance.", "Getting started", "Open Settings to configure the database, then use Import ETP to select and validate a workbook.", "Open Settings", "Settings"),
        ["Import ETP"] = new("Select, validate and review ETP workbooks before import.", "Import workspace", "Workbook selection and validation will become available when the import service is connected.", "Import unavailable", "Import ETP"),
        ["Sales Reports"] = new("View approved sales reports from canonical data.", "Sales reporting", "Run daily, store, brand, brand-segment, item and return reports after importing sales and closing-stock data.", "Run reports below", "Sales Reports"),
        ["Stock Reports"] = new("View approved stock movement and balance reports.", "Stock reporting", "Reconcile the stock ledger to the closing-stock snapshot using source-signed quantities.", "Run reports below", "Stock Reports"),
        ["Masters"] = new("Maintain reporting reference data.", "Master data", "Store, business unit, brand, category and product administration will appear here.", "Masters unavailable", "Masters"),
        ["Settings"] = new("Configure the application and database connection.", "Connection settings", "SQL Server connection controls will appear here when the infrastructure service is connected.", "Configuration unavailable", "Settings")
    };

    public MainWindow()
    {
        InitializeComponent();
        ReportFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        ReportTo.SelectedDate = DateTime.Today.AddDays(-1);
        BrandDictionaryGrid.ItemsSource = new[] { new BrandSegmentEntry("GAUTO", "Titan Automatic", "Confirmed") };
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var saved = LoadSettings();
        if (!string.IsNullOrWhiteSpace(saved?.ConnectionString)) ConnectionStringInput.Text = saved.ConnectionString;
        await CheckConnectionAndRefreshAsync(false);
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination } || !Pages.TryGetValue(destination, out var page)) return;
        PageTitle.Text = destination;
        PageDescription.Text = page.Description;
        WorkspaceHeading.Text = page.Heading;
        WorkspaceMessage.Text = page.Message;
        PrimaryAction.Content = page.ActionLabel;
        PrimaryAction.Tag = page.ActionDestination;
        PrimaryAction.IsEnabled = destination == "Dashboard";
        SettingsPanel.Visibility = destination == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        ImportPanel.Visibility = destination == "Import ETP" ? Visibility.Visible : Visibility.Collapsed;
        ReportsPanel.Visibility = destination is "Sales Reports" or "Stock Reports" ? Visibility.Visible : Visibility.Collapsed;
        DashboardPanel.Visibility = destination == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        MastersPanel.Visibility = destination == "Masters" ? Visibility.Visible : Visibility.Collapsed;
        ApplicationStatus.Text = $"{destination} selected. {page.Message}";
        if (destination == "Dashboard") _ = RefreshDashboardAsync();
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        await CheckConnectionAndRefreshAsync(true);
    }

    private async void BootstrapDatabase_Click(object sender, RoutedEventArgs e)
    {
        ConnectionResult.Text = "Creating/updating database…";
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "database", "migrations");
            var result = await new SqlServerDatabaseBootstrapper(ConnectionStringInput.Text, new DirectoryMigrationSource(path)).BootstrapAsync();
            ConnectionResult.Text = $"Database ready. Applied migrations: {(result.AppliedMigrations.Count == 0 ? "none" : string.Join(", ", result.AppliedMigrations))}.";
            SetConnectionState(true, "Ready to import");
            SaveSettings();
            await RefreshDashboardAsync();
        }
        catch (Exception ex) { ConnectionResult.Text = $"Database setup failed: {ex.Message}"; }
    }

    private void BrowseWorkbook_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Excel workbooks (*.xlsx)|*.xlsx", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) WorkbookPathInput.Text = dialog.FileName;
    }

    private async void ValidateWorkbook_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WorkbookPathInput.Text))
        {
            ValidationResult.Text = "Select an XLSX workbook first.";
            return;
        }

        ValidateButton.IsEnabled = false;
        ValidationResult.Text = "Reading and validating workbook…";
        try
        {
            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(WorkbookPathInput.Text, CancellationToken.None);
            var profiles = RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All);
            var preflight = new ImportPreflight().Inspect(snapshot, profiles);
            var diagnostics = preflight.Diagnostics.ToList();
            var stagedRows = 0;
            if (preflight.CanImport)
            {
                var staged = new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile!);
                diagnostics.AddRange(staged.Diagnostics);
                stagedRows = staged.Rows.Count;
            }
            DiagnosticsGrid.ItemsSource = diagnostics;
            var accepted = preflight.CanImport && diagnostics.All(x => x.Severity != Etp.Reporting.Import.Diagnostics.ImportDiagnosticSeverity.Blocker);
            validatedWorkbook = accepted ? snapshot : null;
            validatedPreflight = accepted ? preflight : null;
            validatedStaging = accepted ? new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile!) : null;
            PersistButton.IsEnabled = accepted;
            ValidationResult.Text = accepted
                ? $"Validated as {preflight.Profile!.ReportCode}. {stagedRows:N0} rows are ready for persistence."
                : "Validation blocked. Review the diagnostics below.";
            ImportStatus.Text = accepted ? "Workbook validated" : "Validation blocked";
            ApplicationStatus.Text = ValidationResult.Text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            ValidationResult.Text = $"Could not read workbook: {ex.Message}";
        }
        finally { ValidateButton.IsEnabled = true; }
    }

    private async void PersistWorkbook_Click(object sender, RoutedEventArgs e)
    {
        if (validatedWorkbook is null || validatedPreflight?.Sheet is null || validatedStaging is null) return;
        PersistButton.IsEnabled = false;
        try
        {
            var store = new SqlServerTransactionalImportStore(ConnectionStringInput.Text);
            if (validatedPreflight.Profile!.ReportCode == "R022")
            {
                var projection = new R022PersistenceProjector().Project(validatedStaging.Rows);
                await new R022SqlImportOrchestrator(store).PersistAsync(validatedWorkbook, validatedPreflight.Sheet, projection);
                ValidationResult.Text = $"Imported {projection.InvoiceControls.Count:N0} invoice controls and {projection.ClassifiedTenders.Count:N0} reportable tender rows. {projection.QuarantinedTenders.Count:N0} unresolved tender rows were quarantined.";
                ImportStatus.Text = "Import completed";
                await RefreshDashboardAsync();
                return;
            }
            if (validatedPreflight.Profile.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK")
            {
                var outcome = await new StockSqlImportOrchestrator(store).PersistAsync(validatedWorkbook);
                ValidationResult.Text = $"Imported {outcome.PersistedRows:N0} {outcome.ReportCode} rows successfully.";
                ImportStatus.Text = "Import completed";
                await RefreshDashboardAsync();
                return;
            }

            var salesOutcome = await new R025SqlImportOrchestrator(store).PersistAsync(validatedWorkbook);
            ValidationResult.Text = $"Imported {salesOutcome.PersistedRows:N0} sales rows successfully.";
            ImportStatus.Text = "Import completed";
            await RefreshDashboardAsync();
        }
        catch (Exception ex) { ValidationResult.Text = $"Import failed: {ex.Message}"; }
        finally { PersistButton.IsEnabled = true; }
    }

    private SqlBackedReportingExecutor CreateReportExecutor() => new(
        new SqlServerReportingQueryRepository(ConnectionStringInput.Text), RetailReportingPolicy.Mapping,
        RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);

    private ReportingQueryScope ReportScope()
    {
        if (ReportFrom.SelectedDate is null || ReportTo.SelectedDate is null) throw new InvalidOperationException("Select both report dates.");
        return new(DateOnly.FromDateTime(ReportFrom.SelectedDate.Value), DateOnly.FromDateTime(ReportTo.SelectedDate.Value));
    }

    private async void RunSalesReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = ((ComboBoxItem)SalesDimensionInput.SelectedItem).Content!.ToString()!;
            var result = await CreateReportExecutor().ExecuteSalesSummaryAsync(ReportScope(), Enum.Parse<SalesSummaryDimension>(name));
            ReportGrid.ItemsSource = result.Rows; ReportResult.Text = $"{result.Status}: {result.Message}";
            SetExport($"{name} Sales", result.Status, result.PolicyVersion, result.Message,
                [new("Group"), new("Units", "#,##0.00"), new("Net Sales", "#,##0.00"), new("Bills", "#,##0")],
                result.Rows.Select(x => (IReadOnlyList<object?>)[x.Key, x.SourceSignedQuantity, x.SourceSignedNetAmount, x.DistinctInvoices]).ToArray(),
                ["Total", result.Rows.Sum(x => x.SourceSignedQuantity), result.Rows.Sum(x => x.SourceSignedNetAmount), result.Rows.Sum(x => x.DistinctInvoices)]);
        }
        catch (Exception ex) { ReportResult.Text = $"Report failed: {ex.Message}"; }
    }

    private async void RunTenderReport_Click(object sender, RoutedEventArgs e)
    {
        try { var r = await CreateReportExecutor().ExecuteTenderReconciliationAsync(ReportScope()); ReportGrid.ItemsSource = r.Documents; ReportResult.Text = $"{r.Status}: invoice {r.InvoiceTotal:N2}, tender {r.TenderTotal:N2}, variance {r.Variance:N2}."; SetExport("Invoice Tender Reconciliation", r.Status, r.RuleVersion, r.Message, [new("Store"),new("Document"),new("Invoice", "#,##0.00"),new("Tender", "#,##0.00"),new("Variance", "#,##0.00"),new("Status")], r.Documents.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.DocumentNumber,x.InvoiceAmount,x.TenderAmount,x.Variance,x.Status.ToString()]).ToArray(), ["Total","",r.InvoiceTotal,r.TenderTotal,r.Variance,r.Status.ToString()]); }
        catch (Exception ex) { ReportResult.Text = $"Reconciliation failed: {ex.Message}"; }
    }

    private async void RunStockReport_Click(object sender, RoutedEventArgs e)
    {
        try { var r = await CreateReportExecutor().ExecuteStockReconciliationAsync(ReportScope()); ReportGrid.ItemsSource = r.Items; ReportResult.Text = $"{r.Status}: {r.Message}"; SetExport("Stock Reconciliation", r.Status, r.RuleVersion, r.Message, [new("Store"),new("Item"),new("Opening", "#,##0.00"),new("Movements", "#,##0.00"),new("Expected Closing", "#,##0.00"),new("Reported Closing", "#,##0.00"),new("Variance", "#,##0.00"),new("Status")], r.Items.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.ItemCode,x.Opening,x.SourceSignedMovements,x.ExpectedClosing,x.ReportedClosing,x.Variance,x.Status.ToString()]).ToArray(), ["Total","",r.Items.Sum(x=>x.Opening),r.Items.Sum(x=>x.SourceSignedMovements),r.Items.Sum(x=>x.ExpectedClosing),r.Items.Sum(x=>x.ReportedClosing),r.Items.Sum(x=>x.Variance),r.Status.ToString()]); }
        catch (Exception ex) { ReportResult.Text = $"Reconciliation failed: {ex.Message}"; }
    }

    private void SetExport(string name, ReconciliationStatus status, string ruleVersion, string message,
        IReadOnlyList<ExcelReportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?> totals)
    {
        var scope = ReportScope();
        currentExportMetadata = new(name, scope.DateFrom, scope.DateTo, status.ToString(), ruleVersion, message, DateTimeOffset.UtcNow);
        currentExportData = new(columns, rows, totals); ExportExcelButton.IsEnabled = true; ExportPdfButton.IsEnabled = true;
    }

    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (currentExportMetadata is null || currentExportData is null) return;
        var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"{currentExportMetadata.ReportName.Replace(' ', '_')}_{currentExportMetadata.DateFrom:yyyyMMdd}_{currentExportMetadata.DateTo:yyyyMMdd}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { new OpenXmlReportExporter().Export(dialog.FileName, currentExportMetadata, currentExportData); ReportResult.Text = $"Excel report saved to {dialog.FileName}"; }
        catch (Exception ex) { ReportResult.Text = $"Excel export failed: {ex.Message}"; }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (currentExportMetadata is null || currentExportData is null) return;
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"{SafeFileName(currentExportMetadata.ReportName)}_{currentExportMetadata.DateFrom:yyyyMMdd}_{currentExportMetadata.DateTo:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { new SimplePdfReportExporter().Export(dialog.FileName, currentExportMetadata, currentExportData); ReportResult.Text = $"PDF report saved to {dialog.FileName}"; }
        catch (Exception ex) { ReportResult.Text = $"PDF export failed: {ex.Message}"; }
    }

    private async void RefreshDashboard_Click(object sender, RoutedEventArgs e) => await RefreshDashboardAsync();

    private async Task CheckConnectionAndRefreshAsync(bool showProgress)
    {
        if (showProgress) ConnectionResult.Text = "Testing…";
        var health = await new SqlServerHealthCheck(ConnectionStringInput.Text).CheckAsync();
        var connected = health.Status == DatabaseHealthStatus.Healthy;
        ConnectionResult.Text = health.Message;
        SetConnectionState(connected, connected ? "Ready to validate or report" : "Waiting for connection");
        ApplicationStatus.Text = connected ? $"Connected to SQL Server {health.ServerVersion}." : health.Message;
        if (connected) { SaveSettings(); await RefreshDashboardAsync(); }
    }

    private void SetConnectionState(bool connected, string importState)
    {
        ConnectionStatus.Text = connected ? "Connected" : "Connection failed";
        ConnectionStatus.Foreground = connected ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.DarkOrange;
        ImportStatus.Text = importState;
        AutomationProperties.SetName(ConnectionStatus, $"Database connection status: {ConnectionStatus.Text}");
        AutomationProperties.SetName(ImportStatus, $"Import readiness status: {ImportStatus.Text}");
    }

    private async Task RefreshDashboardAsync()
    {
        try
        {
            var summary = await new OperationalStatusRepository(ConnectionStringInput.Text).LoadAsync();
            ImportedFilesMetric.Text = summary.ImportedFiles.ToString("N0");
            CompletedBatchesMetric.Text = summary.CompletedBatches.ToString("N0");
            SourceRowsMetric.Text = summary.SourceRows.ToString("N0");
            LatestImportMetric.Text = summary.LatestImportUtc?.ToString("dd MMM yyyy HH:mm") ?? "None";
            ImportHistoryGrid.ItemsSource = summary.RecentImports;
        }
        catch (Exception ex)
        {
            ImportedFilesMetric.Text = CompletedBatchesMetric.Text = SourceRowsMetric.Text = "-";
            LatestImportMetric.Text = "Unavailable";
            ApplicationStatus.Text = $"Dashboard refresh failed: {ex.Message}";
        }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new DesktopSettings(ConnectionStringInput.Text)));
    }

    private static DesktopSettings? LoadSettings()
    {
        try { return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(SettingsPath)) : null; }
        catch (JsonException) { return null; }
    }

    private static string SafeFileName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Replace(' ', '_');

    private sealed record PageDetails(string Description, string Heading, string Message, string ActionLabel, string ActionDestination);
    private sealed record DesktopSettings(string ConnectionString);
    private sealed record BrandSegmentEntry(string Code, string Description, string Status);
}
