using System.IO;
using System.Globalization;
using System.Security.Principal;
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
    private ReportPackDocument? currentDailyPackDocument;
    private OperationalSummary? latestOperationalSummary;
    private DailyWorkflowSnapshot? currentDailySnapshot;
    private BatchImportSource? activeBatchSource;
    private CancellationTokenSource? batchCancellation;
    private IReadOnlyList<string> failedBatchPaths = [];
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "settings.json");
    private static readonly IReadOnlyDictionary<string, PageDetails> Pages = new Dictionary<string, PageDetails>(StringComparer.Ordinal)
    {
        ["Dashboard"] = new("Application and import readiness at a glance.", "Operational overview", "Review database health, backup status and recent imports before running reports.", "Open Settings", "Settings"),
        ["Daily Workflow"] = new("Complete, reconcile and finalise one ETP business date.", "Daily reporting", "Review source completeness, enter only non-ETP operational values, then finalise the protected day.", "Daily workflow ready", "Daily Workflow"),
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
        DailyBusinessDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        ImportBusinessDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        StaffTargetFromInput.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        StaffTargetToInput.SelectedDate = DateTime.Today.AddDays(-1);
        BrandDictionaryGrid.ItemsSource = new[] { new BrandSegmentEntry("GAUTO", "Titan Automatic", "Confirmed") };
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var saved = LoadSettings();
        if (!string.IsNullOrWhiteSpace(saved?.ConnectionString)) ConnectionStringInput.Text = saved.ConnectionString;
        await CheckConnectionAndRefreshAsync(false);
        await RecordAuditAsync("ApplicationStart", "Succeeded", "Desktop application started");
        await RecordAuditAsync("SessionStart", "Succeeded", "Windows integrated user session started");
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
        DailyWorkflowPanel.Visibility = destination == "Daily Workflow" ? Visibility.Visible : Visibility.Collapsed;
        ImportPanel.Visibility = destination == "Import ETP" ? Visibility.Visible : Visibility.Collapsed;
        ReportsPanel.Visibility = destination is "Sales Reports" or "Stock Reports" ? Visibility.Visible : Visibility.Collapsed;
        DashboardPanel.Visibility = destination == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        MastersPanel.Visibility = destination == "Masters" ? Visibility.Visible : Visibility.Collapsed;
        ApplicationStatus.Text = $"{destination} selected. {page.Message}";
        if (destination == "Dashboard") _ = RefreshDashboardAsync();
        if (destination == "Daily Workflow") _ = RefreshDailyWorkflowAsync();
    }

    private async void RefreshDailyWorkflow_Click(object sender, RoutedEventArgs e) => await RefreshDailyWorkflowAsync();

    private async Task RefreshDailyWorkflowAsync()
    {
        try
        {
            var (store, date) = DailyScope();
            currentDailySnapshot = await new DailyReportingWorkflowRepository(ConnectionStringInput.Text).LoadAsync(store, date);
            DailyWorkflowStatus.Text = currentDailySnapshot.Status.ToString();
            DailyWorkflowStatus.Foreground = currentDailySnapshot.Status switch
            {
                DailyReadinessStatus.Locked or DailyReadinessStatus.Reconciled => Brushes.SeaGreen,
                DailyReadinessStatus.ReadyWithWarnings or DailyReadinessStatus.Partial => Brushes.DarkOrange,
                _ => Brushes.Firebrick
            };
            DailyWorkflowMessage.Text = currentDailySnapshot.StatusMessage;
            DailySourceStatus.Text = currentDailySnapshot.MissingReports.Count == 0
                ? $"ETP sources: complete ({string.Join(", ", currentDailySnapshot.ImportedReports)})"
                : $"Missing ETP sources: {string.Join(", ", currentDailySnapshot.MissingReports)}";
            DailyInputStatus.Text = currentDailySnapshot.MissingRequiredInputs.Count == 0
                ? "Required manual inputs: complete (zero values remain distinct from missing values)."
                : $"Missing manual inputs: {string.Join(", ", currentDailySnapshot.MissingRequiredInputs)}";
            DailyManualInputsGrid.ItemsSource = currentDailySnapshot.ManualInputs;
            DailyStockCountsGrid.ItemsSource = await new OperationalCompletionRepository(ConnectionStringInput.Text).LoadManualStockCountsAsync(store, date);
            FinaliseDayButton.IsEnabled = currentDailySnapshot.CanFinalise;
        }
        catch (Exception ex)
        {
            currentDailySnapshot = null;
            FinaliseDayButton.IsEnabled = false;
            DailyWorkflowStatus.Text = "Unavailable";
            DailyWorkflowMessage.Text = ex.Message;
        }
    }

    private async void SaveManualInput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (store, date) = DailyScope();
            var field = ((ComboBoxItem)ManualFieldInput.SelectedItem).Content!.ToString()!;
            var reason = ManualReasonInput.Text.Trim();
            decimal? numeric = null;
            string? text = null;
            if (field == "OPERATIONAL_REMARK") text = string.IsNullOrWhiteSpace(ManualValueInput.Text) ? null : ManualValueInput.Text.Trim();
            else if (decimal.TryParse(ManualValueInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)) numeric = parsed;
            await new DailyReportingWorkflowRepository(ConnectionStringInput.Text).SaveManualInputAsync(
                store, date, field, numeric, text, Environment.UserName, reason);
            ManualValueInput.Clear(); ManualReasonInput.Clear();
            await RecordAuditAsync("ManualInput", "Succeeded", "Manual input saved");
            await RefreshDailyWorkflowAsync();
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Manual input was not saved: {ex.Message}"; }
    }

    private async void SaveStockCount_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (store, date) = DailyScope();
            await new OperationalCompletionRepository(ConnectionStringInput.Text).SaveManualStockCountAsync(
                store, date, StockGroupInput.Text, OptionalDecimal(StockDisplayInput.Text), OptionalDecimal(StockBackstockInput.Text),
                OptionalDecimal(StockDefectiveInput.Text), OptionalDecimal(StockYLocationInput.Text), OptionalDecimal(StockPhysicalInput.Text),
                string.IsNullOrWhiteSpace(StockRemarksInput.Text) ? null : StockRemarksInput.Text.Trim(), Environment.UserName, StockReasonInput.Text);
            StockGroupInput.Clear(); StockDisplayInput.Clear(); StockBackstockInput.Clear(); StockDefectiveInput.Clear();
            StockYLocationInput.Clear(); StockPhysicalInput.Clear(); StockRemarksInput.Clear(); StockReasonInput.Clear();
            await RecordAuditAsync("StockCount", "Succeeded", "Physical stock count saved");
            await RefreshDailyWorkflowAsync();
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Physical stock count was not saved: {ex.Message}"; }
    }

    private async void SaveStaffTarget_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (store, _) = DailyScope();
            if (StaffTargetFromInput.SelectedDate is null || StaffTargetToInput.SelectedDate is null)
                throw new InvalidOperationException("Select the target start and end dates.");
            if (!decimal.TryParse(StaffTargetValueInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var target))
                throw new InvalidOperationException("Enter a valid target sales value.");
            await new OperationalCompletionRepository(ConnectionStringInput.Text).SaveStaffTargetAsync(
                store, StaffTargetCroInput.Text, DateOnly.FromDateTime(StaffTargetFromInput.SelectedDate.Value),
                DateOnly.FromDateTime(StaffTargetToInput.SelectedDate.Value), target, Environment.UserName, StaffTargetReasonInput.Text);
            StaffTargetCroInput.Clear(); StaffTargetValueInput.Clear(); StaffTargetReasonInput.Clear();
            await RecordAuditAsync("StaffTarget", "Succeeded", "Staff target saved");
            DailyWorkflowMessage.Text = "Staff/CRO target saved. Target achievement and ranking are available in the staff report.";
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Staff target was not saved: {ex.Message}"; }
    }

    private async void FinaliseDay_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (store, date) = DailyScope();
            var pack = await new DailyReportingPackService(ConnectionStringInput.Text).GenerateAsync(store, date, Environment.UserName);
            DailyPackGrid.ItemsSource = pack.Sections;
            var hasBlockers = pack.Sections.Any(x => x.Status is ReconciliationStatus.Blocked or ReconciliationStatus.Failed);
            await new DailyReportingWorkflowRepository(ConnectionStringInput.Text).FinaliseAsync(
                store, date, Environment.UserName, hasBlockers);
            await RecordAuditAsync("DayFinalised", "Succeeded", "Business day finalised");
            await RefreshDailyWorkflowAsync();
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Day was not finalised: {ex.Message}"; }
    }

    private async void ReopenDay_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (store, date) = DailyScope();
            using var identity = WindowsIdentity.GetCurrent();
            var isAdministrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            await new DailyReportingWorkflowRepository(ConnectionStringInput.Text).ReopenAsync(
                store, date, Environment.UserName, ReopenReasonInput.Text.Trim(), isAdministrator);
            ReopenReasonInput.Clear();
            await RecordAuditAsync("DayReopened", "Succeeded", "Business day reopened");
            await RefreshDailyWorkflowAsync();
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Day was not reopened: {ex.Message}"; }
    }

    private async void GenerateDailyPack_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (store, date) = DailyScope();
            var pack = await new DailyReportingPackService(ConnectionStringInput.Text).GenerateAsync(store, date, Environment.UserName);
            DailyPackGrid.ItemsSource = pack.Sections;
            DailyWorkflowMessage.Text = $"{pack.Status}: {pack.Message} Generation {pack.GenerationNumber}, control hash {pack.ContentSha256[..12]}.";
            currentDailyPackDocument = pack.Document;
            currentExportMetadata = new("Daily Reporting Pack", date, date, pack.Status.ToString(), RetailReportingPolicy.Version,
                pack.Message, pack.GeneratedAtUtc);
            currentExportData = new(
                [new("Report"),new("Status"),new("Control Total","#,##0.00"),new("Variance","#,##0.00"),new("Message")],
                pack.Sections.Select(x => (IReadOnlyList<object?>)[x.Report,x.Status.ToString(),x.ControlTotal,x.Variance,x.Message]).ToArray(),
                ["Overall",pack.Status.ToString(),"","",pack.Message]);
            await RecordAuditAsync("ReportPack", pack.Status == ReconciliationStatus.Passed ? "Succeeded" : "Failed", "Daily report pack");
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Daily report pack failed: {ex.Message}"; }
    }

    private async void GenerateCombinedDailyPack_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DailyBusinessDateInput.SelectedDate is null) throw new InvalidOperationException("Select the ETP business date.");
            var date = DateOnly.FromDateTime(DailyBusinessDateInput.SelectedDate.Value);
            currentDailyPackDocument = await new DailyReportingPackService(ConnectionStringInput.Text)
                .GenerateCombinedAsync(date, Environment.UserName);
            DailyPackGrid.ItemsSource = currentDailyPackDocument.Tables.Select(x => new
                { Report = x.Name, Status = x.Status, Rows = x.Data.Rows.Count, x.Message });
            DailyWorkflowMessage.Text = $"{currentDailyPackDocument.OverallStatus}: {currentDailyPackDocument.Message} The export contains {currentDailyPackDocument.Tables.Count:N0} report sections.";
            await RecordAuditAsync("ReportPack", currentDailyPackDocument.OverallStatus == ReconciliationStatus.Passed.ToString() ? "Succeeded" : "Failed", "Combined daily report pack");
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Combined daily report pack failed: {ex.Message}"; }
    }

    private (string Store, DateOnly Date) DailyScope()
    {
        if (DailyBusinessDateInput.SelectedDate is null) throw new InvalidOperationException("Select the ETP business date.");
        if (DailyStoreInput.SelectedItem is not ComboBoxItem storeItem) throw new InvalidOperationException("Select a store.");
        return (storeItem.Content!.ToString()!, DateOnly.FromDateTime(DailyBusinessDateInput.SelectedDate.Value));
    }

    private (string Store, DateOnly Date) ImportScope()
    {
        if (ImportBusinessDateInput.SelectedDate is null) throw new InvalidOperationException("Select the ETP business date before importing.");
        if (ImportStoreInput.SelectedItem is not ComboBoxItem storeItem) throw new InvalidOperationException("Select the ETP store before importing.");
        return (storeItem.Content!.ToString()!, DateOnly.FromDateTime(ImportBusinessDateInput.SelectedDate.Value));
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
            await RecordAuditAsync("ConfigurationChange", "Succeeded", "Windows integrated database configuration saved");
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
            var persistenceStore = new SqlServerTransactionalImportStore(ConnectionStringInput.Text);
            var (selectedStore, selectedDate) = ImportScope();
            var restatement = await ResolveRestatementAsync(validatedPreflight.Profile!.ReportCode, selectedStore, selectedDate, CancellationToken.None);
            if (validatedPreflight.Profile!.ReportCode == "R022")
            {
                var projection = new R022PersistenceProjector().Project(validatedStaging.Rows);
                await new R022SqlImportOrchestrator(persistenceStore).PersistAsync(validatedWorkbook, validatedPreflight.Sheet, projection,
                    expectedBusinessDate: selectedDate, expectedStoreCode: selectedStore, importedBy: Environment.UserName, restatement: restatement);
                ValidationResult.Text = $"Imported {projection.InvoiceControls.Count:N0} invoice controls and {projection.ClassifiedTenders.Count:N0} reportable tender rows. {projection.QuarantinedTenders.Count:N0} unresolved tender rows were quarantined.";
                ImportStatus.Text = "Import completed";
                if (restatement is not null) await RecordAuditAsync("Restatement", "Succeeded", "Controlled source restatement applied");
                await RefreshDashboardAsync();
                return;
            }
            if (validatedPreflight.Profile.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK")
            {
                var outcome = await new StockSqlImportOrchestrator(persistenceStore).PersistAsync(validatedWorkbook,
                    expectedBusinessDate: selectedDate, expectedStoreCode: selectedStore, importedBy: Environment.UserName, restatement: restatement);
                ValidationResult.Text = $"Imported {outcome.PersistedRows:N0} {outcome.ReportCode} rows successfully.";
                ImportStatus.Text = "Import completed";
                if (restatement is not null) await RecordAuditAsync("Restatement", "Succeeded", "Controlled source restatement applied");
                await RefreshDashboardAsync();
                return;
            }

            if (validatedPreflight.Profile.ReportCode is "R003" or "R013")
            {
                var outcome = await new RetailEnrichmentSqlImportOrchestrator(ConnectionStringInput.Text).PersistAsync(
                    validatedWorkbook, validatedPreflight.Profile.ReportCode, selectedDate, selectedStore, Environment.UserName, restatement: restatement);
                ValidationResult.Text = $"Imported {outcome.PersistedRows:N0} {outcome.ReportCode} enrichment rows: {outcome.MatchedRows:N0} matched, {outcome.MissingMatches:N0} missing, {outcome.AmbiguousMatches:N0} ambiguous. Revenue totals were not changed.";
                ImportStatus.Text = "Import completed";
                if (restatement is not null) await RecordAuditAsync("Restatement", "Succeeded", "Controlled source restatement applied");
                await RefreshDashboardAsync();
                return;
            }

            var salesOutcome = await new R025SqlImportOrchestrator(persistenceStore).PersistAsync(validatedWorkbook,
                expectedBusinessDate: selectedDate, expectedStoreCode: selectedStore, importedBy: Environment.UserName, restatement: restatement);
            ValidationResult.Text = $"Imported {salesOutcome.PersistedRows:N0} sales rows successfully.";
            ImportStatus.Text = "Import completed";
            if (restatement is not null) await RecordAuditAsync("Restatement", "Succeeded", "Controlled source restatement applied");
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
        var restatementMode = RestatementModeInput.IsChecked == true;
        if (await new SqlServerImportFileRepository(ConnectionStringInput.Text).ExistsByHashAsync(snapshot.Sha256, cancellationToken))
        {
            if (restatementMode) throw new ImportSourceException("RESTATEMENT_DUPLICATE_FILE", "A restatement must use a corrected source file with a new hash.");
            return;
        }
        var preflight = new ImportPreflight().Inspect(snapshot, RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All));
        if (!preflight.CanImport) throw new ImportSourceException("IMPORT_LAYOUT_BLOCKED", "The workbook layout is not an approved ETP layout.");
        var persistenceStore = new SqlServerTransactionalImportStore(ConnectionStringInput.Text);
        var (selectedStore, selectedDate) = ImportScope();
        var restatement = await ResolveRestatementAsync(preflight.Profile!.ReportCode, selectedStore, selectedDate, cancellationToken);
        if (preflight.Profile!.ReportCode == "R022")
        {
            var staged = new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile);
            if (!staged.CanPersist) throw new ImportSourceException("IMPORT_STAGING_BLOCKED", "Workbook rows failed validation.");
            await new R022SqlImportOrchestrator(persistenceStore).PersistAsync(snapshot, preflight.Sheet!, new R022PersistenceProjector().Project(staged.Rows), cancellationToken: cancellationToken,
                expectedBusinessDate: selectedDate, expectedStoreCode: selectedStore, importedBy: Environment.UserName, restatement: restatement);
        }
        else if (preflight.Profile.ReportCode is "STOCK_LEDGER" or "CLOSING_STOCK")
            await new StockSqlImportOrchestrator(persistenceStore).PersistAsync(snapshot, cancellationToken: cancellationToken,
                expectedBusinessDate: selectedDate, expectedStoreCode: selectedStore, importedBy: Environment.UserName, restatement: restatement);
        else if (preflight.Profile.ReportCode is "R003" or "R013")
            await new RetailEnrichmentSqlImportOrchestrator(ConnectionStringInput.Text).PersistAsync(snapshot, preflight.Profile.ReportCode,
                selectedDate, selectedStore, Environment.UserName, cancellationToken, restatement);
        else
            await new R025SqlImportOrchestrator(persistenceStore).PersistAsync(snapshot, cancellationToken: cancellationToken,
                expectedBusinessDate: selectedDate, expectedStoreCode: selectedStore, importedBy: Environment.UserName, restatement: restatement);
        if (restatement is not null) await RecordAuditAsync("Restatement", "Succeeded", "Controlled source restatement applied");
    }

    private async Task<ImportRestatementRequest?> ResolveRestatementAsync(
        string reportCode,
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        if (RestatementModeInput.IsChecked != true) return null;
        var reason = RestatementReasonInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason)) throw new ImportSourceException("RESTATEMENT_REASON_REQUIRED", "Enter the reason for the controlled restatement.");
        var previous = await new OperationalCompletionRepository(ConnectionStringInput.Text).FindCurrentImportAsync(
            reportCode, storeCode, businessDate, cancellationToken);
        if (previous is null) throw new ImportSourceException("RESTATEMENT_SOURCE_NOT_FOUND", "No current import exists for this report, store and business date. Use a normal import instead.");
        return new(previous.ImportFileId, Environment.UserName, reason);
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

    private async void RunInvoiceSummary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rows = await new OperationalReportRepository(ConnectionStringInput.Text).LoadInvoiceSummaryAsync(ReportScope());
            var status = rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed;
            var message = rows.Count == 0 ? "No canonical invoice lines are available for the selected scope." : "Invoice totals are generated from canonical R025 lines; customer PII is intentionally excluded.";
            ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {rows.Count:N0} invoices.";
            SetExport("Customer-safe Invoice Sales Summary", status, RetailReportingPolicy.Version, message,
                [new("Business Date"),new("Store"),new("Document"),new("Transaction Type"),new("Quantity","#,##0.00"),new("Net Value","#,##0.00"),new("Source Rows","#,##0")],
                rows.Select(x => (IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.DocumentNumber,x.TransactionTypes,x.Quantity,x.NetValue,x.SourceRows]).ToArray(),
                ["Total","","","",rows.Sum(x=>x.Quantity),rows.Sum(x=>x.NetValue),rows.Sum(x=>x.SourceRows)]);
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", status == ReconciliationStatus.Passed ? "Succeeded" : "Blocked", "Invoice summary");
        }
        catch (Exception ex) { ReportResult.Text = $"Invoice summary failed: {ex.Message}"; }
    }

    private async void RunInvoiceLineage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var rows = await new OperationalReportRepository(ConnectionStringInput.Text).LoadInvoiceLineageAsync(ReportScope());
            var status = rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed;
            var message = "Invoice and item drill-down is traceable to its source workbook, sheet and row. Customer PII remains excluded pending owner approval.";
            ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {rows.Count:N0} canonical line(s).";
            SetExport("Invoice Sales Lineage", status, RetailReportingPolicy.Version, message,
                [new("Business Date"),new("Store"),new("Document"),new("Line"),new("Item"),new("Brand"),new("Segment"),new("Transaction Type"),new("Quantity","#,##0.00"),new("Net Value","#,##0.00"),new("CRO"),new("Workbook"),new("Sheet"),new("Source Row","#,##0")],
                rows.Select(x => (IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.DocumentNumber,x.LineIdentifier,x.ProductCode,x.Brand,x.BrandSegment,x.TransactionType,x.Quantity,x.NetValue,x.CroNumber,x.SourceWorkbook,x.SourceSheet,x.SourceRow]).ToArray(),
                ["Total","","","","","","","",rows.Sum(x=>x.Quantity),rows.Sum(x=>x.NetValue),"","","",rows.Count]);
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", status == ReconciliationStatus.Passed ? "Succeeded" : "Blocked", "Invoice lineage report");
        }
        catch (Exception ex) { ReportResult.Text = $"Invoice drill-down failed: {ex.Message}"; }
    }

    private async void RunDsr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scope = ReportScope();
            var rows = await new OperationalReportRepository(ConnectionStringInput.Text).LoadDsrAsync(scope.DateTo, scope.StoreCodes);
            var hasSales = rows.Any(x => x.TySales is not null);
            var status = hasSales ? ReconciliationStatus.Passed : ReconciliationStatus.Blocked;
            var unavailable = rows.Count(x => x.GrowthStatus != MetricAvailability.Available.ToString());
            var message = $"FTD, MTD and Indian-financial-year YTD use business date {scope.DateTo:dd-MMM-yyyy}; {unavailable:N0} row(s) have unavailable LY growth rather than a misleading percentage.";
            ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {message}";
            SetExport("Daily Sales Report", status, OperationalReportRepository.DsrMetricPolicy, message,
                [new("Period"),new("Store"),new("From"),new("To"),new("TY Sales","#,##0.00"),new("LY Sales","#,##0.00"),new("Growth %","#,##0.00"),new("Growth Status"),new("TY Units","#,##0.00"),new("LY Units","#,##0.00"),new("TY Invoices","#,##0"),new("LY Invoices","#,##0"),new("UPT","#,##0.00"),new("ATV","#,##0.00"),new("Walk-ins","#,##0.00"),new("Conversion %","#,##0.00")],
                rows.Select(x => (IReadOnlyList<object?>)[x.Period,x.Store,x.PeriodStart,x.PeriodEnd,x.TySales,x.LySales,x.GrowthPercent,x.GrowthStatus,x.TyUnits,x.LyUnits,x.TyInvoices,x.LyInvoices,x.Upt,x.Atv,x.WalkIns,x.ConversionPercent]).ToArray(),
                ["Independent periods","","","","","","","","","","","","","","",""]);
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", status == ReconciliationStatus.Passed ? "Succeeded" : "Blocked", "Daily sales report");
        }
        catch (Exception ex) { ReportResult.Text = $"DSR failed: {ex.Message}"; }
    }

    private async void RunStaffPerformance_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await new OperationalReportRepository(ConnectionStringInput.Text).LoadStaffPerformanceAsync(ReportScope());
            ReportGrid.ItemsSource = result.Rows;
            ReportResult.Text = $"{result.Status}: canonical {result.CanonicalSales:N2}, attributed {result.AttributedSales:N2}, variance {result.Variance:N2}. {result.Message}";
            SetExport("Staff CRO Performance", result.Status, result.MetricPolicy, result.Message,
                [new("Store"),new("CRO"),new("Net Sales","#,##0.00"),new("LY Sales","#,##0.00"),new("Growth %","#,##0.00"),new("Growth Status"),new("Net Quantity","#,##0.00"),new("Discount","#,##0.00"),new("Transactions","#,##0"),new("UPT","#,##0.00"),new("ATV","#,##0.00"),new("Contribution %","#,##0.00"),new("Target","#,##0.00"),new("Achievement %","#,##0.00"),new("Rank","#,##0")],
                result.Rows.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.CroNumber,x.NetSales,x.LastYearSales,x.GrowthPercent,x.GrowthStatus,x.NetQuantity,x.Discount,x.Transactions,x.Upt,x.Atv,x.ContributionPercent,x.TargetSales,x.TargetAchievementPercent,x.Rank]).ToArray(),
                ["Control","",result.AttributedSales,"","","","","",result.Rows.Sum(x=>x.Transactions),"","",result.Variance,"","",""]);
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", result.Status == ReconciliationStatus.Passed ? "Succeeded" : "Failed", "Staff performance");
        }
        catch (Exception ex) { ReportResult.Text = $"Staff report failed: {ex.Message}"; }
    }

    private async void RunServiceSales_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scope = ReportScope();
            var rows = await new OperationalReportRepository(ConnectionStringInput.Text).LoadServiceSalesAsync(scope.DateTo, scope.StoreCodes);
            var status = rows.Any(x => x.Total is not null) ? ReconciliationStatus.Passed : ReconciliationStatus.Blocked;
            var message = "Service cash, card and UPI are controlled manual operational facts; missing values remain missing and retail sales are never mixed in.";
            ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {message}";
            SetExport("Service Sales", status, RetailReportingPolicy.Version, message,
                [new("Period"),new("Store"),new("From"),new("To"),new("Cash","#,##0.00"),new("Card","#,##0.00"),new("UPI","#,##0.00"),new("Total","#,##0.00"),new("LY Total","#,##0.00"),new("Growth %","#,##0.00"),new("Availability")],
                rows.Select(x => (IReadOnlyList<object?>)[x.Period,x.StoreCode,x.PeriodStart,x.PeriodEnd,x.Cash,x.Card,x.Upi,x.Total,x.LastYearTotal,x.GrowthPercent,x.Availability]).ToArray(),
                ["Independent periods","","","","","","","","","",""]);
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", status == ReconciliationStatus.Passed ? "Succeeded" : "Blocked", "Service sales");
        }
        catch (Exception ex) { ReportResult.Text = $"Service report failed: {ex.Message}"; }
    }

    private async void RunCashReconciliation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scope = ReportScope();
            if (scope.StoreCodes is not { Count: 1 }) throw new InvalidOperationException("Enter exactly one store code for cash reconciliation.");
            var result = await new OperationalReportRepository(ConnectionStringInput.Text).LoadCashReconciliationAsync(scope.StoreCodes[0], scope.DateTo);
            var rows = new[] { result };
            ReportGrid.ItemsSource = rows; ReportResult.Text = $"{result.Status}: {result.Message}";
            SetExport("Daily Cash Reconciliation", result.Status, RetailReportingPolicy.Version, result.Message,
                [new("Store"),new("Business Date"),new("Opening","#,##0.00"),new("Retail Cash","#,##0.00"),new("Service Cash","#,##0.00"),new("Expenses","#,##0.00"),new("Deposit","#,##0.00"),new("Adjustment","#,##0.00"),new("Calculated Closing","#,##0.00"),new("Counted Closing","#,##0.00"),new("Variance","#,##0.00"),new("Status")],
                [(IReadOnlyList<object?>)[result.StoreCode,result.BusinessDate,result.OpeningCash,result.RetailCash,result.ServiceCash,result.Expenses,result.CashDeposit,result.Adjustment,result.CalculatedClosing,result.CountedClosing,result.Variance,result.Status.ToString()]],
                ["Control","","","","","","","","",result.CountedClosing,result.Variance,result.Status.ToString()]);
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", result.Status == ReconciliationStatus.Passed ? "Succeeded" : result.Status.ToString(), "Cash reconciliation");
        }
        catch (Exception ex) { ReportResult.Text = $"Cash reconciliation failed: {ex.Message}"; }
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

    private async void RunPhysicalStock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scope = ReportScope();
            if (scope.StoreCodes is not { Count: 1 }) throw new InvalidOperationException("Enter exactly one store code for physical stock reporting.");
            var rows = await new OperationalReportRepository(ConnectionStringInput.Text).LoadPhysicalStockAsync(scope.StoreCodes[0], scope.DateTo);
            var status = rows.Any(x => x.Status == "FAIL") ? ReconciliationStatus.Failed : rows.Count == 0 ? ReconciliationStatus.Blocked : ReconciliationStatus.Passed;
            var message = "Physical count, component total and ETP system quantity remain separate; neither count overwrites the other.";
            ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {rows.Count:N0} inventory group(s).";
            SetExport("Physical Closing Stock", status, RetailReportingPolicy.Version, message,
                [new("Store"),new("Date"),new("Inventory Group"),new("Display","#,##0.00"),new("Backstock","#,##0.00"),new("Defective","#,##0.00"),new("Y Location","#,##0.00"),new("Component Total","#,##0.00"),new("Counted Physical","#,##0.00"),new("Composition Variance","#,##0.00"),new("System","#,##0.00"),new("System Variance","#,##0.00"),new("Remarks"),new("Status")],
                rows.Select(x => (IReadOnlyList<object?>)[x.StoreCode,x.BusinessDate,x.InventoryGroupCode,x.DisplayQuantity,x.BackstockQuantity,x.DefectiveQuantity,x.YLocationQuantity,x.ComponentTotal,x.CountedPhysicalQuantity,x.CompositionVariance,x.SystemQuantity,x.SystemVariance,x.Remarks,x.Status]).ToArray(),
                ["Total","","","","","","",rows.Sum(x=>x.ComponentTotal),rows.Sum(x=>x.CountedPhysicalQuantity),rows.Sum(x=>x.CompositionVariance),rows.Sum(x=>x.SystemQuantity),rows.Sum(x=>x.SystemVariance),"",status.ToString()]);
            ApplyReportFilter(); await RecordAuditAsync("ReportRun", status == ReconciliationStatus.Passed ? "Succeeded" : status.ToString(), "Physical stock report");
        }
        catch (Exception ex) { ReportResult.Text = $"Physical stock report failed: {ex.Message}"; }
    }

    private async void RunDailyExceptions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var scope = ReportScope();
            if (scope.StoreCodes is not { Count: 1 }) throw new InvalidOperationException("Enter exactly one store code for the daily exception report.");
            if (scope.DateFrom != scope.DateTo) throw new InvalidOperationException("Select one business date for the daily exception report.");
            var rows = await new OperationalReportRepository(ConnectionStringInput.Text).LoadDailyExceptionsAsync(scope.StoreCodes[0], scope.DateTo);
            var status = rows.Any(x => x.Severity is "BLOCKER" or "FAIL") ? ReconciliationStatus.Failed : ReconciliationStatus.Passed;
            var message = rows.Count == 0 ? "No daily exceptions were found." : "Every exception retains its exact variance and available source workbook/sheet/row pointer.";
            ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {rows.Count:N0} exception(s).";
            SetExport("Daily Exceptions", status, RetailReportingPolicy.Version, message,
                [new("Severity"),new("Area"),new("Code"),new("Store"),new("Date"),new("Document"),new("Item"),new("Variance","#,##0.00"),new("Workbook"),new("Sheet"),new("Source Row","#,##0"),new("Message"),new("Recommended Action")],
                rows.Select(x => (IReadOnlyList<object?>)[x.Severity,x.Area,x.Code,x.StoreCode,x.BusinessDate,x.DocumentNumber,x.ItemCode,x.Variance,x.SourceWorkbook,x.SourceSheet,x.SourceRow,x.Message,x.RecommendedAction]).ToArray(),
                ["Total",rows.Count,"","","","","",rows.Where(x=>x.Variance is not null).Sum(x=>x.Variance),"","","","",""]);
            ApplyReportFilter(); await RecordAuditAsync("ReportRun", status == ReconciliationStatus.Passed ? "Succeeded" : "Failed", "Daily exception report");
        }
        catch (Exception ex) { ReportResult.Text = $"Daily exceptions failed: {ex.Message}"; }
    }

    private void SetExport(string name, ReconciliationStatus status, string ruleVersion, string message,
        IReadOnlyList<ExcelReportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?> totals)
    {
        var scope = ReportScope();
        currentExportMetadata = new(name, scope.DateFrom, scope.DateTo, status.ToString(), ruleVersion, message, DateTimeOffset.UtcNow);
        currentExportData = new(columns, rows, totals); ExportExcelButton.IsEnabled = true; ExportPdfButton.IsEnabled = true;
    }

    private void ExportDailyPackExcel_Click(object sender, RoutedEventArgs e)
    {
        if (currentDailyPackDocument is null) { DailyWorkflowMessage.Text = "Generate the complete daily report pack before exporting."; return; }
        var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"ETP_Daily_Report_Pack_{currentDailyPackDocument.DateTo:yyyyMMdd}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            new OpenXmlReportPackExporter().Export(dialog.FileName, currentDailyPackDocument);
            DailyWorkflowMessage.Text = $"Complete multi-sheet report pack saved to {dialog.FileName}";
            _ = RecordAuditAsync("ExportExcel", "Succeeded", "Complete report pack exported");
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Report-pack Excel export failed: {ex.Message}"; }
    }

    private void ExportDailyPackPdf_Click(object sender, RoutedEventArgs e)
    {
        if (currentDailyPackDocument is null) { DailyWorkflowMessage.Text = "Generate the complete daily report pack before exporting."; return; }
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"ETP_Daily_Report_Pack_{currentDailyPackDocument.DateTo:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            new SimplePdfReportPackExporter().Export(dialog.FileName, currentDailyPackDocument);
            DailyWorkflowMessage.Text = $"Complete paginated report pack saved to {dialog.FileName}";
            _ = RecordAuditAsync("ExportPdf", "Succeeded", "Complete report pack exported");
        }
        catch (Exception ex) { DailyWorkflowMessage.Text = $"Report-pack PDF export failed: {ex.Message}"; }
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
        if (connected) { TrySaveSettings(); await RecordAuditAsync("ConfigurationChange", "Succeeded", "Windows integrated database configuration saved"); await RefreshDashboardAsync(); }
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

    private static decimal? OptionalDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
            ? parsed : throw new InvalidOperationException($"'{value}' is not a valid number.");
    }

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
