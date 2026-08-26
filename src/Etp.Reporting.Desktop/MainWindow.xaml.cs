using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Win32;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop;

public partial class MainWindow : Window
{
    private WorkbookSnapshot? validatedWorkbook;
    private ImportPreflightResult? validatedPreflight;
    private ImportStagingResult? validatedStaging;
    private ExcelReportMetadata? currentExportMetadata;
    private ExcelReportData? currentExportData;
    private OperationalSummary? latestOperationalSummary;
    private BatchImportSource? activeBatchSource;
    private CancellationTokenSource? batchCancellation;
    private IReadOnlyList<string> failedBatchPaths = [];
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "settings.json");
    private static readonly IReadOnlyDictionary<string, PageDetails> Pages = new Dictionary<string, PageDetails>(StringComparer.Ordinal)
    {
        ["Dashboard"] = new("Application and import readiness at a glance.", "Operational overview", "Review database health, backup status and recent imports before running reports.", "Open Settings", "Settings"),
        ["Import ETP"] = new("Select, validate and review ETP workbooks before import.", "Import workspace", "Import one approved workbook, a folder batch, or a safe ZIP package with progress, cancellation and retry.", "Import ready", "Import ETP"),
        ["Sales Reports"] = new("View approved sales reports from canonical data.", "Sales reporting", "Run daily, store, brand, brand-segment, item and return reports after importing sales and closing-stock data.", "Run reports below", "Sales Reports"),
        ["Stock Reports"] = new("View approved stock movement and balance reports.", "Stock reporting", "Reconcile the stock ledger to the closing-stock snapshot using source-signed quantities.", "Run reports below", "Stock Reports"),
        ["Masters"] = new("Maintain reporting reference data.", "Master data", "Review confirmed Brand Segment descriptions while unresolved mappings remain fail-closed.", "Review dictionary", "Masters"),
        ["Settings"] = new("Configure the application and database connection.", "Connection settings", "Test the saved Windows-integrated SQL Server connection or safely create/update the database.", "Configuration ready", "Settings")
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
        await RecordAuditAsync("ApplicationStart", "Succeeded", "Desktop application started");
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
            TrySaveSettings();
            await RefreshDashboardAsync();
            await RecordAuditAsync("DatabaseSetup", "Succeeded", "Database migrations verified");
        }
        catch (Exception ex) { ConnectionResult.Text = $"Database setup failed: {ex.Message}"; }
    }

    private void BrowseWorkbook_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ETP import sources (*.xlsx;*.zip)|*.xlsx;*.zip|Excel workbooks (*.xlsx)|*.xlsx|ZIP archives (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) WorkbookPathInput.Text = dialog.FileName;
    }

    private void BrowseImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select folder containing ETP workbooks", Multiselect = false };
        if (dialog.ShowDialog(this) == true) WorkbookPathInput.Text = dialog.FolderName;
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

    private async void StartBatchImport_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WorkbookPathInput.Text)) { ValidationResult.Text = "Select a folder, XLSX workbook, or ZIP archive first."; return; }
        await DisposeBatchSourceAsync();
        try
        {
            activeBatchSource = await BatchImportSource.OpenAsync(WorkbookPathInput.Text);
            await RunBatchAsync(activeBatchSource.WorkbookPaths);
        }
        catch (ImportSourceException ex) { ValidationResult.Text = $"Batch blocked ({ex.Code}): {ex.Message}"; }
        catch (Exception ex) { ValidationResult.Text = $"Batch could not start: {new SafeImportFailureClassifier().Describe(ex).SafeMessage}"; }
    }

    private void CancelBatchImport_Click(object sender, RoutedEventArgs e) => batchCancellation?.Cancel();

    private async void RetryBatchImport_Click(object sender, RoutedEventArgs e)
    {
        if (failedBatchPaths.Count > 0) await RunBatchAsync(failedBatchPaths);
    }

    private async Task RunBatchAsync(IReadOnlyList<string> paths)
    {
        batchCancellation?.Dispose(); batchCancellation = new CancellationTokenSource();
        StartBatchButton.IsEnabled = RetryBatchButton.IsEnabled = false; CancelBatchButton.IsEnabled = true;
        ImportProgressBar.Maximum = Math.Max(1, paths.Count); ImportProgressBar.Value = 0;
        var progress = new Progress<BatchImportProgress>(x => { ImportProgressBar.Maximum = Math.Max(1, x.Total); ImportProgressBar.Value = x.Completed; ValidationResult.Text = $"{x.Stage}: {x.SafeFileName}"; });
        try
        {
            var coordinator = new BatchImportCoordinator(new DelegateWorkbookImportProcessor(ProcessWorkbookAsync));
            var summary = await coordinator.RunAsync(paths, progress, batchCancellation.Token);
            BatchResultsGrid.ItemsSource = summary.Files;
            failedBatchPaths = paths.Zip(summary.Files).Where(x => x.Second.Status == BatchImportFileStatus.Failed).Select(x => x.First).ToArray();
            ValidationResult.Text = $"Batch completed: {summary.Succeeded:N0} succeeded, {summary.Failed:N0} failed, {summary.Cancelled:N0} cancelled.";
            RetryBatchButton.IsEnabled = failedBatchPaths.Count > 0;
            await RefreshDashboardAsync();
            await RecordAuditAsync("ImportBatch", summary.Failed > 0 ? "Failed" : summary.Cancelled > 0 ? "Cancelled" : "Succeeded", "Batch import completed");
        }
        finally { StartBatchButton.IsEnabled = true; CancelBatchButton.IsEnabled = false; }
    }

    private async Task ProcessWorkbookAsync(string workbookPath, CancellationToken cancellationToken)
    {
        var snapshot = await new OpenXmlWorkbookReader().ReadAsync(workbookPath, cancellationToken);
        if (await new SqlServerImportFileRepository(ConnectionStringInput.Text).ExistsByHashAsync(snapshot.Sha256, cancellationToken)) return;
        var preflight = new ImportPreflight().Inspect(snapshot, RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All));
        if (!preflight.CanImport) throw new ImportSourceException("IMPORT_LAYOUT_BLOCKED", "The workbook layout is not an approved ETP layout.");
        var store = new SqlServerTransactionalImportStore(ConnectionStringInput.Text);
        if (preflight.Profile!.ReportCode == "R022")
        {
            var staged = new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile);
            if (!staged.CanPersist) throw new ImportSourceException("IMPORT_STAGING_BLOCKED", "Workbook rows failed validation.");
            await new R022SqlImportOrchestrator(store).PersistAsync(snapshot, preflight.Sheet!, new R022PersistenceProjector().Project(staged.Rows), cancellationToken: cancellationToken);
        }
        else if (preflight.Profile.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK")
            await new StockSqlImportOrchestrator(store).PersistAsync(snapshot, cancellationToken: cancellationToken);
        else
            await new R025SqlImportOrchestrator(store).PersistAsync(snapshot, cancellationToken: cancellationToken);
    }

    private async Task DisposeBatchSourceAsync()
    {
        if (activeBatchSource is not null) await activeBatchSource.DisposeAsync();
        activeBatchSource = null; failedBatchPaths = [];
    }

    protected override async void OnClosed(EventArgs e)
    {
        batchCancellation?.Cancel(); batchCancellation?.Dispose();
        await DisposeBatchSourceAsync();
        base.OnClosed(e);
    }

    private SqlBackedReportingExecutor CreateReportExecutor() => new(
        new SqlServerReportingQueryRepository(ConnectionStringInput.Text), RetailReportingPolicy.Mapping,
        RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);

    private ReportingQueryScope ReportScope()
    {
        if (ReportFrom.SelectedDate is null || ReportTo.SelectedDate is null) throw new InvalidOperationException("Select both report dates.");
        return new(DateOnly.FromDateTime(ReportFrom.SelectedDate.Value), DateOnly.FromDateTime(ReportTo.SelectedDate.Value),
            Csv(StoreFilterInput.Text), Csv(BrandSegmentFilterInput.Text), Csv(TransactionTypeFilterInput.Text), Csv(ItemFilterInput.Text));
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
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", result.Status == ReconciliationStatus.Passed ? "Succeeded" : result.Status.ToString(), "Sales report");
        }
        catch (Exception ex) { ReportResult.Text = $"Report failed: {ex.Message}"; }
    }

    private async void RunTenderReport_Click(object sender, RoutedEventArgs e)
    {
        try { var r = await CreateReportExecutor().ExecuteTenderReconciliationAsync(ReportScope()); ReportGrid.ItemsSource = r.Documents; ReportResult.Text = $"{r.Status}: invoice {r.InvoiceTotal:N2}, tender {r.TenderTotal:N2}, variance {r.Variance:N2}."; SetExport("Invoice Tender Reconciliation", r.Status, r.RuleVersion, r.Message, [new("Store"),new("Document"),new("Invoice", "#,##0.00"),new("Tender", "#,##0.00"),new("Variance", "#,##0.00"),new("Status")], r.Documents.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.DocumentNumber,x.InvoiceAmount,x.TenderAmount,x.Variance,x.Status.ToString()]).ToArray(), ["Total","",r.InvoiceTotal,r.TenderTotal,r.Variance,r.Status.ToString()]); ApplyReportFilter(); await RecordAuditAsync("ReportRun", r.Status == ReconciliationStatus.Passed ? "Succeeded" : r.Status.ToString(), "Tender control"); }
        catch (Exception ex) { ReportResult.Text = $"Reconciliation failed: {ex.Message}"; }
    }

    private async void RunTenderDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var reconciliation = await CreateReportExecutor().ExecuteTenderReconciliationAsync(ReportScope());
            var diagnostic = new TenderVarianceDiagnosticService().Diagnose(reconciliation, RetailReportingPolicy.Tender.AbsoluteTolerance);
            ReportGrid.ItemsSource = diagnostic.Rows;
            ReportResult.Text = $"{diagnostic.Status}: {diagnostic.FailedDocuments:N0} documents require review; absolute variance {diagnostic.AbsoluteVariance:N2}. Classifications do not change the control result.";
            SetExport("Tender Variance Diagnostics", diagnostic.Status, diagnostic.RuleVersion, diagnostic.Message,
                [new("Store"), new("Document"), new("Invoice", "#,##0.00"), new("Tender", "#,##0.00"), new("Variance", "#,##0.00"), new("Likely Cause"), new("Recommended Check")],
                diagnostic.Rows.Select(x => (IReadOnlyList<object?>)[x.StoreCode, x.DocumentNumber, x.InvoiceAmount, x.TenderAmount, x.Variance, x.LikelyCause.ToString(), x.RecommendedCheck]).ToArray(),
                ["Total", "", reconciliation.InvoiceTotal, reconciliation.TenderTotal, reconciliation.Variance, diagnostic.Status.ToString(), $"{diagnostic.FailedDocuments:N0} documents"]);
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", diagnostic.Status == ReconciliationStatus.Passed ? "Succeeded" : diagnostic.Status.ToString(), "Tender diagnostic");
        }
        catch (Exception ex) { ReportResult.Text = $"Tender diagnostics failed: {ex.Message}"; }
    }

    private async void RunStockReport_Click(object sender, RoutedEventArgs e)
    {
        try { var r = await CreateReportExecutor().ExecuteStockReconciliationAsync(ReportScope()); ReportGrid.ItemsSource = r.Items; ReportResult.Text = $"{r.Status}: {r.Message}"; SetExport("Stock Reconciliation", r.Status, r.RuleVersion, r.Message, [new("Store"),new("Item"),new("Opening", "#,##0.00"),new("Movements", "#,##0.00"),new("Expected Closing", "#,##0.00"),new("Reported Closing", "#,##0.00"),new("Variance", "#,##0.00"),new("Status")], r.Items.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.ItemCode,x.Opening,x.SourceSignedMovements,x.ExpectedClosing,x.ReportedClosing,x.Variance,x.Status.ToString()]).ToArray(), ["Total","",r.Items.Sum(x=>x.Opening),r.Items.Sum(x=>x.SourceSignedMovements),r.Items.Sum(x=>x.ExpectedClosing),r.Items.Sum(x=>x.ReportedClosing),r.Items.Sum(x=>x.Variance),r.Status.ToString()]); ApplyReportFilter(); await RecordAuditAsync("ReportRun", r.Status == ReconciliationStatus.Passed ? "Succeeded" : r.Status.ToString(), "Stock control"); }
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
        try { new OpenXmlReportExporter().Export(dialog.FileName, currentExportMetadata, currentExportData); ReportResult.Text = $"Excel report saved to {dialog.FileName}"; _ = RecordAuditAsync("ExportExcel", "Succeeded", "Report exported"); }
        catch (Exception ex) { ReportResult.Text = $"Excel export failed: {ex.Message}"; }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (currentExportMetadata is null || currentExportData is null) return;
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"{SafeFileName(currentExportMetadata.ReportName)}_{currentExportMetadata.DateFrom:yyyyMMdd}_{currentExportMetadata.DateTo:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { new SimplePdfReportExporter().Export(dialog.FileName, currentExportMetadata, currentExportData); ReportResult.Text = $"PDF report saved to {dialog.FileName}"; _ = RecordAuditAsync("ExportPdf", "Succeeded", "Report exported"); }
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
        if (connected) { TrySaveSettings(); await RefreshDashboardAsync(); }
        await RecordAuditAsync("ConnectionTest", connected ? "Succeeded" : "Failed", "Database connection tested");
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
            var summaryTask = new OperationalStatusRepository(ConnectionStringInput.Text).LoadAsync();
            var healthTask = new DatabaseOperationalHealthRepository(ConnectionStringInput.Text).LoadAsync();
            var auditTask = new OperationalAuditRepository(ConnectionStringInput.Text).LoadRecentAsync(25);
            await Task.WhenAll(summaryTask, healthTask, auditTask);
            var summary = await summaryTask;
            var health = await healthTask;
            var audit = await auditTask;
            latestOperationalSummary = summary;
            ImportedFilesMetric.Text = summary.ImportedFiles.ToString("N0");
            CompletedBatchesMetric.Text = summary.CompletedBatches.ToString("N0");
            SourceRowsMetric.Text = summary.SourceRows.ToString("N0");
            LatestImportMetric.Text = summary.LatestImportUtc?.ToString("dd MMM yyyy HH:mm") ?? "None";
            ImportHistoryGrid.ItemsSource = summary.RecentImports;
            RenderDashboardChart(summary);
            DatabaseHealthMetric.Text = health.Severity.ToString();
            DatabaseHealthMetric.Foreground = health.Severity == OperationalHealthSeverity.Healthy ? Brushes.SeaGreen : health.Severity == OperationalHealthSeverity.Warning ? Brushes.DarkOrange : Brushes.Firebrick;
            DatabaseSizeMetric.Text = $"{health.DatabaseSizeMb:N2} MB";
            BackupAgeMetric.Text = health.LastSuccessfulBackupUtc?.ToString("dd MMM yyyy HH:mm") ?? "Missing";
            BackupSpaceMetric.Text = health.BackupFreeSpaceGb is { } freeGb ? $"{freeGb:N2} GB" : "Unavailable";
            FailedImportsMetric.Text = health.FailedImportsLast24Hours.ToString("N0");
            HealthWarningsList.ItemsSource = health.Warnings.Select(x => $"{x.Code}: {x.Message}").ToArray();
            OperationalAuditGrid.ItemsSource = audit;
        }
        catch (Exception ex)
        {
            ImportedFilesMetric.Text = CompletedBatchesMetric.Text = SourceRowsMetric.Text = "-";
            LatestImportMetric.Text = "Unavailable";
            DatabaseHealthMetric.Text = DatabaseSizeMetric.Text = BackupAgeMetric.Text = BackupSpaceMetric.Text = FailedImportsMetric.Text = "Unavailable";
            HealthWarningsList.ItemsSource = null;
            OperationalAuditGrid.ItemsSource = null;
            ApplicationStatus.Text = $"Dashboard refresh failed: {ex.Message}";
        }
    }

    private void SaveSettings()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionStringInput.Text);
        if (!builder.IntegratedSecurity || !string.IsNullOrWhiteSpace(builder.Password))
        {
            ApplicationStatus.Text = "Connection succeeded. For security, connections containing SQL credentials are not saved.";
            return;
        }
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The settings directory cannot be a linked path.");
        var temporary = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(new DesktopSettings(builder.ConnectionString))); File.Move(temporary, SettingsPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void TrySaveSettings()
    {
        try { SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { ApplicationStatus.Text = "Connection succeeded, but the local settings file could not be updated safely."; }
    }

    private static DesktopSettings? LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath) || (File.GetAttributes(SettingsPath) & FileAttributes.ReparsePoint) != 0) return null;
            return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(SettingsPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static string SafeFileName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Replace(' ', '_');

    private async Task RecordAuditAsync(string eventType, string outcome, string detail)
    {
        try { await new OperationalAuditRepository(ConnectionStringInput.Text).RecordAsync(eventType, outcome, detail); }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException) { }
    }

    private static IReadOnlyList<string>? Csv(string value)
    {
        var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0 ? null : values;
    }

    private void ReportSearch_TextChanged(object sender, RoutedEventArgs e) => ApplyReportFilter();

    private void ApplyReportFilter()
    {
        if (ReportGrid.ItemsSource is null) return;
        var search = ReportSearchInput.Text.Trim();
        var varianceOnly = VarianceOnlyInput.IsChecked == true;
        var view = CollectionViewSource.GetDefaultView(ReportGrid.ItemsSource);
        view.Filter = item =>
        {
            if (item is null) return false;
            if (search.Length > 0 && !item.ToString()!.Contains(search, StringComparison.OrdinalIgnoreCase)) return false;
            if (!varianceOnly) return true;
            var property = item.GetType().GetProperty("Variance");
            return property?.GetValue(item) is decimal variance && variance != 0;
        };
        view.Refresh();
    }

    private void ReportGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ReportGrid.SelectedItem is null) return;
        MessageBox.Show(ReportGrid.SelectedItem.ToString() ?? "No details available.", "Report row details", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RenderDashboardChart(OperationalSummary summary)
    {
        DashboardChartPanel.Children.Clear();
        var groups = summary.RecentImports.GroupBy(x => x.ReportCode).Select(x => new { Code = x.Key, Rows = x.Sum(v => v.SourceRows) }).OrderByDescending(x => x.Rows).ToArray();
        var maximum = Math.Max(1, groups.Select(x => x.Rows).DefaultIfEmpty(1).Max());
        foreach (var group in groups)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(120) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(80) });
            var label = new TextBlock { Text = group.Code, VerticalAlignment = VerticalAlignment.Center };
            var bar = new Border { Background = new SolidColorBrush(Color.FromRgb(23, 107, 135)), Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Width = 420d * group.Rows / maximum };
            var value = new TextBlock { Text = group.Rows.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(label, 0); Grid.SetColumn(bar, 1); Grid.SetColumn(value, 2); row.Children.Add(label); row.Children.Add(bar); row.Children.Add(value); DashboardChartPanel.Children.Add(row);
        }
    }

    private void ExportDashboardPdf_Click(object sender, RoutedEventArgs e)
    {
        if (latestOperationalSummary is null) { ApplicationStatus.Text = "Refresh the dashboard before exporting a management summary."; return; }
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"ETP_Management_Summary_{DateTime.Today:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        var groups = latestOperationalSummary.RecentImports.GroupBy(x => x.ReportCode).OrderBy(x => x.Key).ToArray();
        var metadata = new ExcelReportMetadata("ETP Management Summary", ReportFrom.SelectedDate is { } from ? DateOnly.FromDateTime(from) : DateOnly.FromDateTime(DateTime.Today), ReportTo.SelectedDate is { } to ? DateOnly.FromDateTime(to) : DateOnly.FromDateTime(DateTime.Today), "Operational", "v1", "Aggregate operational evidence only; confidential source rows are excluded.", DateTimeOffset.UtcNow);
        var data = new ExcelReportData([new("Report"), new("Files", "#,##0"), new("Rows", "#,##0")], groups.Select(x => (IReadOnlyList<object?>)[x.Key, x.Count(), x.Sum(v => v.SourceRows)]).ToArray(), ["Total", latestOperationalSummary.ImportedFiles, latestOperationalSummary.SourceRows]);
        try { new SimplePdfReportExporter().Export(dialog.FileName, metadata, data); ApplicationStatus.Text = $"Management summary saved to {dialog.FileName}"; }
        catch (Exception ex) { ApplicationStatus.Text = $"Management summary export failed: {ex.Message}"; }
    }

    private sealed record PageDetails(string Description, string Heading, string Message, string ActionLabel, string ActionDestination);
    private sealed record DesktopSettings(string ConnectionString);
    private sealed record BrandSegmentEntry(string Code, string Description, string Status);
    private sealed class DelegateWorkbookImportProcessor(Func<string, CancellationToken, Task> process) : IWorkbookImportProcessor
    {
        public Task ProcessAsync(string workbookPath, CancellationToken cancellationToken) => process(workbookPath, cancellationToken);
    }
}
