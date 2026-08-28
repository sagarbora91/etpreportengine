extern alias EtpApplication;

using System.IO;
using System.Globalization;
using System.Security.Principal;
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
using Etp.Reporting.Desktop.Modules.Settings;
using Microsoft.Win32;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop;

using DashboardSnapshot = EtpApplication::Etp.Reporting.Application.Dashboard.DashboardSnapshot;
using DashboardQuery = EtpApplication::Etp.Reporting.Application.Dashboard.IDashboardQuery;
using AccessSession = EtpApplication::Etp.Reporting.Application.Access.AccessSession;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;
using AccessSessionQuery = EtpApplication::Etp.Reporting.Application.Access.IAccessSessionQuery;

public partial class MainWindow : Window
{
    private WorkbookSnapshot? validatedWorkbook;
    private ImportPreflightResult? validatedPreflight;
    private ImportStagingResult? validatedStaging;
    private ExcelReportMetadata? currentExportMetadata;
    private ExcelReportData? currentExportData;
    private VisualReportModel? currentVisualReport;
    private DailySalesReportDocument? currentDsrReport;
    private string? currentReportCode;
    private ReportPackDocument? currentDailyPackDocument;
    private ReportPackDocument? currentArchivedDocument;
    private DashboardSnapshot? latestDashboardSnapshot;
    private readonly ShellViewModel shell;
    private readonly DashboardView dashboardView;
    private readonly Func<string, DashboardQuery> dashboardQueryFactory;
    private readonly DesktopSettingsStore settingsStore;
    private readonly DesktopConnectionState connectionState;
    private readonly Func<string, AccessSessionQuery> accessSessionQueryFactory;
    private DailyWorkflowSnapshot? currentDailySnapshot;
    private BatchImportSource? activeBatchSource;
    private CancellationTokenSource? batchCancellation;
    private IReadOnlyList<string> failedBatchPaths = [];
    private AccessSession currentAccess = new("unknown", "Unknown user", AccessRole.None, false);

    public MainWindow(
        ShellViewModel shell,
        DashboardView dashboardView,
        Func<string, DashboardQuery> dashboardQueryFactory,
        DesktopSettingsStore settingsStore,
        DesktopConnectionState connectionState,
        Func<string, AccessSessionQuery> accessSessionQueryFactory)
    {
        this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
        this.dashboardView = dashboardView ?? throw new ArgumentNullException(nameof(dashboardView));
        this.dashboardQueryFactory = dashboardQueryFactory ?? throw new ArgumentNullException(nameof(dashboardQueryFactory));
        this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        this.connectionState = connectionState ?? throw new ArgumentNullException(nameof(connectionState));
        this.accessSessionQueryFactory = accessSessionQueryFactory ?? throw new ArgumentNullException(nameof(accessSessionQueryFactory));
        InitializeComponent();
        DashboardHost.Content = dashboardView;
        dashboardView.RefreshRequested += async (_, _) => await RefreshDashboardAsync();
        dashboardView.ExportPdfRequested += (_, _) => ExportDashboardPdf_Click(dashboardView, new RoutedEventArgs());
        InitializeShell();
        ReportFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        ReportTo.SelectedDate = DateTime.Today.AddDays(-1);
        DailyBusinessDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        ImportBusinessDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        OperationsFromInput.SelectedDate = DateTime.Today.AddDays(-30);
        OperationsToInput.SelectedDate = DateTime.Today;
        ArchiveDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        SourceDocumentDateInput.SelectedDate = DateTime.Today;
        RegisterBusinessDateInput.SelectedDate = DateTime.Today;
        AccountingDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        AdjustmentDateInput.SelectedDate = DateTime.Today.AddDays(-1);
        StaffTargetFromInput.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        StaffTargetToInput.SelectedDate = DateTime.Today.AddDays(-1);
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var saved = settingsStore.Load();
        if (saved is not null) connectionState.TryUpdate(saved.ConnectionString, out _);
        ConnectionStringInput.Text = connectionState.ConnectionString;
        await RefreshAccessAsync();
        CompleteWelcomeState();
        await CheckConnectionAndRefreshAsync(false);
        await RecordAuditAsync("ApplicationStart", "Succeeded", "Desktop application started");
        await RecordAuditAsync("SessionStart", "Succeeded", "Windows integrated user session started");
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination }) return;
        NavigateToDestination(destination);
    }

    private void ApplyNavigationDecision(NavigationDecision decision)
    {
        if (!decision.IsAllowed)
        {
            if (!string.IsNullOrWhiteSpace(decision.DenialReason)) ApplicationStatus.Text = decision.DenialReason;
            return;
        }
        if (decision.RequestedRoute == WorkspaceRoute.Home)
        {
            DisplayModuleHome();
            return;
        }
        if (decision.Descriptor is not { } page) return;
        var destination = page.Destination;
        HideFocusedWorkspace();
        PageTitle.Text = destination switch { "Dashboard" => "Home", "Sales Reports" or "Stock Reports" => "Reports", "Operations Center" => "Control Centre", "Report Archive" => "Archive", _ => destination };
        PageDescription.Text = page.Description;
        WorkspaceHeading.Text = page.Heading;
        WorkspaceMessage.Text = page.Message;
        PrimaryAction.Content = page.ActionLabel;
        PrimaryAction.Tag = page.ActionDestination;
        PrimaryAction.IsEnabled = destination == "Dashboard";
        SettingsPanel.Visibility = destination is "Settings" or "Admin / Settings" ? Visibility.Visible : Visibility.Collapsed;
        DailyWorkflowPanel.Visibility = destination is "Daily Workflow" or "Manual Entry" or "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        ImportPanel.Visibility = destination == "Import ETP" ? Visibility.Visible : Visibility.Collapsed;
        SourceInboxPanel.Visibility = destination == "Import ETP" ? Visibility.Visible : Visibility.Collapsed;
        ReportsPanel.Visibility = destination is "Sales Reports" or "Stock Reports" ? Visibility.Visible : Visibility.Collapsed;
        DashboardPanel.Visibility = destination == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        OperationsPanel.Visibility = destination == "Operations Center" ? Visibility.Visible : Visibility.Collapsed;
        InvestigationPanel.Visibility = destination == "Operations Center" ? Visibility.Visible : Visibility.Collapsed;
        ReportArchivePanel.Visibility = destination == "Report Archive" ? Visibility.Visible : Visibility.Collapsed;
        RegistersPanel.Visibility = destination == "Registers" ? Visibility.Visible : Visibility.Collapsed;
        AccountingPanel.Visibility = destination == "Accounting" ? Visibility.Visible : Visibility.Collapsed;
        MastersPanel.Visibility = destination is "Masters" or "Admin / Settings" ? Visibility.Visible : Visibility.Collapsed;
        UpdateShellForDestination(page);
        ApplicationStatus.Text = $"{destination} selected. {page.Message}";
        if (destination == "Dashboard") { _ = RefreshDashboardAsync(); _ = RefreshDailyWorkflowAsync(); }
        if (destination is "Daily Workflow" or "Manual Entry") _ = RefreshDailyWorkflowAsync();
        if (destination == "Import ETP") _ = RefreshSourceInboxAsync();
        if (destination == "Registers") _ = RefreshRegistersAsync();
        if (destination == "Accounting") _ = RefreshAccountingAsync();
        if (destination == "Operations Center") { _ = RefreshOperationsAsync(); _ = RefreshApprovalsAsync(); }
        if (destination == "Report Archive") { _ = RefreshReportArchiveAsync(); _ = RefreshSharingContactsAsync(); }
        if (destination is "Masters" or "Admin / Settings") _ = RefreshMasterAdministrationAsync();
        if (destination == "Manual Entry") Dispatcher.BeginInvoke(() => { ManualEntrySection.BringIntoView(); ManualFieldInput.Focus(); });
    }

    private async Task RefreshAccessAsync()
    {
        try
        {
            currentAccess = await accessSessionQueryFactory(connectionState.ConnectionString).LoadCurrentAsync();
            AccessStatus.Text = $"{currentAccess.DisplayName} — {RoleLabel(currentAccess.Role)}";
            AccessStatus.Foreground = currentAccess.CanView ? Brushes.SeaGreen : Brushes.Firebrick;
            if (PageTitle.Text is "Dashboard" or "Home") { DashboardPanel.Visibility = currentAccess.CanView ? Visibility.Visible : Visibility.Collapsed; DailyWorkflowPanel.Visibility = currentAccess.CanView ? Visibility.Visible : Visibility.Collapsed; }
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            currentAccess = new("unknown", "Access not initialized", AccessRole.None, false);
            AccessStatus.Text = "Access: initialize database";
            AccessStatus.Foreground = Brushes.DarkOrange;
            if (PageTitle.Text is "Dashboard" or "Home") { DashboardPanel.Visibility = Visibility.Collapsed; DailyWorkflowPanel.Visibility = Visibility.Collapsed; }
        }
    }

    private void RequireViewAccess()
    {
        if (!currentAccess.CanView) throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private void RequireImportAccess()
    {
        if (!currentAccess.CanImport) throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private void RequireOwnerAccess()
    {
        if (!currentAccess.CanAdminister) throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private async void RefreshDailyWorkflow_Click(object sender, RoutedEventArgs e) => await RefreshDailyWorkflowAsync();

    private async Task RefreshDailyWorkflowAsync()
    {
        try
        {
            var (store, date) = DailyScope();
            currentDailySnapshot = await new DailyReportingWorkflowRepository(connectionState.ConnectionString).LoadAsync(store, date);
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
            ManualFieldInput.ItemsSource = currentDailySnapshot.ManualInputs;
            if (ManualFieldInput.SelectedIndex < 0 && currentDailySnapshot.ManualInputs.Count > 0) ManualFieldInput.SelectedIndex = 0;
            DailyStockCountsGrid.ItemsSource = await new OperationalCompletionRepository(connectionState.ConnectionString).LoadManualStockCountsAsync(store, date);
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
            RequireImportAccess();
            var (store, date) = DailyScope();
            var field = ManualFieldInput.SelectedValue as string ?? throw new InvalidOperationException("Select a manual-entry field.");
            var reason = ManualReasonInput.Text.Trim();
            decimal? numeric = null;
            string? text = null;
            if (field == "OPERATIONAL_REMARK") text = string.IsNullOrWhiteSpace(ManualValueInput.Text) ? null : ManualValueInput.Text.Trim();
            else
            {
                if (!decimal.TryParse(ManualValueInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed))
                    throw new InvalidOperationException("Enter a valid numeric value.");
                if (field == "WALK_INS" && (parsed < 0 || decimal.Truncate(parsed) != parsed))
                    throw new InvalidOperationException("Walk-ins must be a whole number of zero or more.");
                numeric = parsed;
            }
            await new DailyReportingWorkflowRepository(connectionState.ConnectionString).SaveManualInputAsync(
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
            RequireImportAccess();
            var (store, date) = DailyScope();
            await new OperationalCompletionRepository(connectionState.ConnectionString).SaveManualStockCountAsync(
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
            RequireImportAccess();
            var (store, _) = DailyScope();
            if (StaffTargetFromInput.SelectedDate is null || StaffTargetToInput.SelectedDate is null)
                throw new InvalidOperationException("Select the target start and end dates.");
            if (!decimal.TryParse(StaffTargetValueInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var target))
                throw new InvalidOperationException("Enter a valid target sales value.");
            await new OperationalCompletionRepository(connectionState.ConnectionString).SaveStaffTargetAsync(
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
            RequireImportAccess();
            var (store, date) = DailyScope();
            var pack = await new DailyReportingPackService(connectionState.ConnectionString).GenerateAsync(store, date, Environment.UserName);
            DailyPackGrid.ItemsSource = pack.Sections;
            var hasBlockers = pack.Sections.Any(x => x.Status is ReconciliationStatus.Blocked or ReconciliationStatus.Failed);
            await new DailyReportingWorkflowRepository(connectionState.ConnectionString).FinaliseAsync(
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
            RequireOwnerAccess();
            var (store, date) = DailyScope();
            using var identity = WindowsIdentity.GetCurrent();
            var isAdministrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            await new DailyReportingWorkflowRepository(connectionState.ConnectionString).ReopenAsync(
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
            RequireViewAccess();
            var (store, date) = DailyScope();
            var pack = await new DailyReportingPackService(connectionState.ConnectionString).GenerateAsync(store, date, Environment.UserName);
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
            RequireViewAccess();
            if (DailyBusinessDateInput.SelectedDate is null) throw new InvalidOperationException("Select the ETP business date.");
            var date = DateOnly.FromDateTime(DailyBusinessDateInput.SelectedDate.Value);
            currentDailyPackDocument = await new DailyReportingPackService(connectionState.ConnectionString)
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
            if (currentAccess.Role != AccessRole.None) RequireOwnerAccess();
            var validation = ConnectionStringValidation.Validate(ConnectionStringInput.Text);
            if (!validation.IsValid) throw new InvalidOperationException(validation.Error);
            var path = Path.Combine(AppContext.BaseDirectory, "database", "migrations");
            var result = await new SqlServerDatabaseBootstrapper(validation.ConnectionString!, new DirectoryMigrationSource(path)).BootstrapAsync();
            connectionState.TryUpdate(validation.ConnectionString, out _);
            ConnectionResult.Text = $"Database ready. Applied migrations: {(result.AppliedMigrations.Count == 0 ? "none" : string.Join(", ", result.AppliedMigrations))}.";
            SetConnectionState(true, "Ready to import");
            TrySaveSettings();
            await RefreshDashboardAsync();
            await RefreshAccessAsync();
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
            RequireImportAccess();
            var persistenceStore = new SqlServerTransactionalImportStore(connectionState.ConnectionString);
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
                await RetainValidatedEtpEvidenceAsync(selectedStore,selectedDate);
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
                await RetainValidatedEtpEvidenceAsync(selectedStore,selectedDate);
                await RefreshDashboardAsync();
                return;
            }

            if (validatedPreflight.Profile.ReportCode is "R003" or "R013")
            {
                var outcome = await new RetailEnrichmentSqlImportOrchestrator(connectionState.ConnectionString).PersistAsync(
                    validatedWorkbook, validatedPreflight.Profile.ReportCode, selectedDate, selectedStore, Environment.UserName, restatement: restatement);
                ValidationResult.Text = $"Imported {outcome.PersistedRows:N0} {outcome.ReportCode} enrichment rows: {outcome.MatchedRows:N0} matched, {outcome.MissingMatches:N0} missing, {outcome.AmbiguousMatches:N0} ambiguous. Revenue totals were not changed.";
                ImportStatus.Text = "Import completed";
                if (restatement is not null) await RecordAuditAsync("Restatement", "Succeeded", "Controlled source restatement applied");
                await RetainValidatedEtpEvidenceAsync(selectedStore,selectedDate);
                await RefreshDashboardAsync();
                return;
            }

            var salesOutcome = await new R025SqlImportOrchestrator(persistenceStore).PersistAsync(validatedWorkbook,
                expectedBusinessDate: selectedDate, expectedStoreCode: selectedStore, importedBy: Environment.UserName, restatement: restatement);
            ValidationResult.Text = $"Imported {salesOutcome.PersistedRows:N0} sales rows successfully.";
            ImportStatus.Text = "Import completed";
            if (restatement is not null) await RecordAuditAsync("Restatement", "Succeeded", "Controlled source restatement applied");
            await RetainValidatedEtpEvidenceAsync(selectedStore,selectedDate);
            await RefreshDashboardAsync();
        }
        catch (Exception ex) { ValidationResult.Text = $"Import failed: {ex.Message}"; }
        finally { PersistButton.IsEnabled = true; }
    }

    private Task RetainValidatedEtpEvidenceAsync(string store,DateOnly date) =>
        validatedWorkbook is null || validatedPreflight?.Profile is null
            ? Task.CompletedTask
            : new ProductisationOperationsService(connectionState.ConnectionString).IntakeEtpEvidenceAsync(WorkbookPathInput.Text,validatedWorkbook.Sha256,validatedPreflight.Profile.ReportCode,store,date);

    private async void StartBatchImport_Click(object sender, RoutedEventArgs e)
    {
        try { RequireImportAccess(); }
        catch (UnauthorizedAccessException ex) { ValidationResult.Text = ex.Message; return; }
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
            var coordinator = new BatchImportCoordinator(new DelegateWorkbookImportOutcomeProcessor(ProcessWorkbookAsync));
            var summary = await coordinator.RunAsync(paths, progress, batchCancellation.Token);
            BatchResultsGrid.ItemsSource = summary.Files;
            failedBatchPaths = paths.Zip(summary.Files).Where(x => x.Second.Status == BatchImportFileStatus.Failed).Select(x => x.First).ToArray();
            ValidationResult.Text = $"Batch completed: {summary.Succeeded:N0} processed, {summary.ExactDuplicates:N0} exact duplicate files, " +
                $"{summary.NewRows:N0} new rows, {summary.AlreadyPresentRows:N0} rows already present, {summary.Conflicts:N0} conflicts, " +
                $"{summary.Failed:N0} failed, {summary.Cancelled:N0} cancelled.";
            RetryBatchButton.IsEnabled = failedBatchPaths.Count > 0;
            await RefreshDashboardAsync();
            await RecordAuditAsync("ImportBatch", summary.Failed > 0 ? "Failed" : summary.Cancelled > 0 ? "Cancelled" : "Succeeded", "Batch import completed");
        }
        finally { StartBatchButton.IsEnabled = true; CancelBatchButton.IsEnabled = false; }
    }

    private async Task<WorkbookImportOutcome> ProcessWorkbookAsync(string workbookPath, CancellationToken cancellationToken)
    {
        var snapshot = await new OpenXmlWorkbookReader().ReadAsync(workbookPath, cancellationToken);
        var restatementMode = RestatementModeInput.IsChecked == true;
        if (await new SqlServerImportFileRepository(connectionState.ConnectionString).ExistsByHashAsync(snapshot.Sha256, cancellationToken))
        {
            if (restatementMode) throw new ImportSourceException("RESTATEMENT_DUPLICATE_FILE", "A restatement must use a corrected source file with a new hash.");
            return new(0, 0, 0, 0, true);
        }
        var preflight = new ImportPreflight().Inspect(snapshot, RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All));
        if (!preflight.CanImport) throw new ImportSourceException("IMPORT_LAYOUT_BLOCKED", "The workbook layout is not an approved ETP layout.");
        var persistenceStore = new SqlServerTransactionalImportStore(connectionState.ConnectionString);
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
            await new RetailEnrichmentSqlImportOrchestrator(connectionState.ConnectionString).PersistAsync(snapshot, preflight.Profile.ReportCode,
                selectedDate, selectedStore, Environment.UserName, cancellationToken, restatement);
        else
            await new R025SqlImportOrchestrator(persistenceStore).PersistAsync(snapshot, cancellationToken: cancellationToken,
                expectedBusinessDate: selectedDate, expectedStoreCode: selectedStore, importedBy: Environment.UserName, restatement: restatement);
        if (restatement is not null) await RecordAuditAsync("Restatement", "Succeeded", "Controlled source restatement applied");
        await new ProductisationOperationsService(connectionState.ConnectionString).IntakeEtpEvidenceAsync(workbookPath,snapshot.Sha256,preflight.Profile.ReportCode,selectedStore,selectedDate,cancellationToken);
        return await new SqlServerImportFileRepository(connectionState.ConnectionString).LoadOutcomeByHashAsync(snapshot.Sha256, cancellationToken);
    }

    private async Task<ImportRestatementRequest?> ResolveRestatementAsync(
        string reportCode,
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        if (RestatementModeInput.IsChecked != true) return null;
        RequireOwnerAccess();
        var reason = RestatementReasonInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason)) throw new ImportSourceException("RESTATEMENT_REASON_REQUIRED", "Enter the reason for the controlled restatement.");
        var previous = await new OperationalCompletionRepository(connectionState.ConnectionString).FindCurrentImportAsync(
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
        new SqlServerReportingQueryRepository(connectionState.ConnectionString), RetailReportingPolicy.Mapping,
        RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);

    private ReportingQueryScope ReportScope()
    {
        if (ReportFrom.SelectedDate is null || ReportTo.SelectedDate is null) throw new InvalidOperationException("Select both report dates.");
        return new(DateOnly.FromDateTime(ReportFrom.SelectedDate.Value), DateOnly.FromDateTime(ReportTo.SelectedDate.Value),
            Csv(StoreFilterInput.Text), Csv(BrandSegmentFilterInput.Text), Csv(TransactionTypeFilterInput.Text), Csv(ItemFilterInput.Text));
    }

    private async void RunCatalogueReport_Click(object sender,RoutedEventArgs e)
    {
        if(sender is not Button { Tag:string report })return;
        await RunCatalogueReportAsync(report);
    }

    private async Task RunCatalogueReportAsync(string report)
    {
        if (!BeginReportLoad(report)) return;
        var e = new RoutedEventArgs();
        switch(report)
        {
            case "dsr": RunDsr_Click(this,e); break;
            case "sales-titan": StoreFilterInput.Text="WLMHW";SelectSalesDimension("Daily");RunSalesReport_Click(this,e);break;
            case "sales-helios": StoreFilterInput.Text="HEMW";SelectSalesDimension("Daily");RunSalesReport_Click(this,e);break;
            case "sales-combined": StoreFilterInput.Clear();SelectSalesDimension("Store");RunSalesReport_Click(this,e);break;
            case "sales-returns": SelectSalesDimension("Returns");RunSalesReport_Click(this,e);break;
            case "sales-brand": SelectSalesDimension("Brand");RunSalesReport_Click(this,e);break;
            case "sales-segment": SelectSalesDimension("BrandSegment");RunSalesReport_Click(this,e);break;
            case "sales-item": SelectSalesDimension("Item");RunSalesReport_Click(this,e);break;
            case "invoice": RunInvoiceSummary_Click(this,e);break;
            case "invoice-lineage": RunInvoiceLineage_Click(this,e);break;
            case "staff": RunStaffPerformance_Click(this,e);break;
            case "service": RunServiceSales_Click(this,e);break;
            case "cash": RunCashReconciliation_Click(this,e);break;
            case "tender": RunTenderReport_Click(this,e);break;
            case "tender-diagnostic": RunTenderDiagnostic_Click(this,e);break;
            case "stock-variance": RunStockReport_Click(this,e);break;
            case "stock-physical" or "stock-group": RunPhysicalStock_Click(this,e);break;
            case "stock-closing": await RunStockInventoryAsync("CLOSING");break;
            case "stock-brand": await RunStockInventoryAsync("BRAND");break;
            case "stock-slow": await RunStockInventoryAsync("SLOW");break;
            case "stock-movement": await RunStockMovementAsync();break;
            case "exceptions": RunDailyExceptions_Click(this,e);break;
            case "exception-source": await RunFocusedExceptionAsync("Source");break;
            case "exception-unmapped": await RunFocusedExceptionAsync("Unmapped");break;
            case "exception-stock": await RunFocusedExceptionAsync("Stock");break;
            case "exception-staff": await RunFocusedExceptionAsync("Staff");break;
            case "exception-tender": await RunFocusedExceptionAsync("Tender");break;
            case "management-trend": await RunManagementTrendReportAsync();break;
        }
    }

    private bool BeginReportLoad(string reportCode)
    {
        if (!ShowFocusedReportWorkspace(reportCode)) return false;
        currentReportCode = reportCode;
        currentExportMetadata = null;
        currentExportData = null;
        currentVisualReport = null;
        currentDsrReport = null;
        ExportExcelButton.IsEnabled = false;
        ExportPdfButton.IsEnabled = false;
        VisualReportPanel.Children.Clear();
        ReportResult.Text = reportCode == "dsr" ? "Loading the governed Daily Sales Report…" : "Loading report…";
        return true;
    }

    private void SelectSalesDimension(string name)
    {
        SalesDimensionInput.SelectedItem=SalesDimensionInput.Items.OfType<ComboBoxItem>().First(x=>string.Equals(x.Content?.ToString(),name,StringComparison.Ordinal));
    }

    private async Task RunStockInventoryAsync(string mode)
    {
        try
        {
            var rows=await new OperationalReportRepository(connectionState.ConnectionString).LoadStockInventoryAsync(ReportScope());
            if(mode=="SLOW")rows=rows.Where(x=>x.Quantity!=0&&x.MovementStatus!="ACTIVE").ToArray();
            if(mode=="BRAND")
            {
                var grouped=rows.GroupBy(x=>new{x.StoreCode,Brand=x.Brand??"Unmapped",Group=x.InventoryGroup??"Unmapped"}).Select(x=>new{x.Key.StoreCode,x.Key.Brand,InventoryGroup=x.Key.Group,Quantity=x.Sum(y=>y.Quantity),TotalCost=x.Any(y=>y.TotalCost is not null)?(decimal?)x.Sum(y=>y.TotalCost??0):null,Items=x.Select(y=>y.ProductCode).Distinct().Count(),SlowItems=x.Count(y=>y.Quantity!=0&&y.MovementStatus!="ACTIVE")}).OrderBy(x=>x.StoreCode).ThenBy(x=>x.InventoryGroup).ThenBy(x=>x.Brand).ToArray();
                var status=grouped.Length==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed;ReportGrid.ItemsSource=grouped;ReportResult.Text=$"{status}: {grouped.Length:N0} store/brand/inventory-group row(s).";
                SetExport("Brand Stock",status,RetailReportingPolicy.Version,"Closing stock grouped from the immutable ETP snapshot; quantity and cost are never inferred.",[new("Store"),new("Brand"),new("Inventory Group"),new("Quantity","#,##0.00"),new("Total Cost","#,##0.00"),new("Items","#,##0"),new("Slow Items","#,##0")],grouped.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.Brand,x.InventoryGroup,x.Quantity,x.TotalCost,x.Items,x.SlowItems]).ToArray(),["Total","","",grouped.Sum(x=>x.Quantity),grouped.Sum(x=>x.TotalCost),grouped.Sum(x=>x.Items),grouped.Sum(x=>x.SlowItems)]);
            }
            else
            {
                var status=rows.Count==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed;var name=mode=="SLOW"?"Slow / Exception Stock":"Closing Stock";ReportGrid.ItemsSource=rows;ReportResult.Text=$"{status}: {rows.Count:N0} item(s). Slow stock uses 60-day watch and 90-day exception bands.";
                SetExport(name,status,RetailReportingPolicy.Version,"Closing quantities and costs come from the selected-date ETP stock snapshot. Last sale is the latest positive source-signed sale on or before that date.",[new("Date"),new("Store"),new("Item"),new("Brand"),new("Inventory Group"),new("Quantity","#,##0.00"),new("Unit Cost","#,##0.00"),new("Total Cost","#,##0.00"),new("Last Sale"),new("Days Since Sale","#,##0"),new("Movement Status")],rows.Select(x=>(IReadOnlyList<object?>)[x.SnapshotDate,x.StoreCode,x.ProductCode,x.Brand,x.InventoryGroup,x.Quantity,x.UnitCost,x.TotalCost,x.LastSaleDate,x.DaysSinceLastSale,x.MovementStatus]).ToArray(),["Total","","","","",rows.Sum(x=>x.Quantity),"",rows.Sum(x=>x.TotalCost),"","",""]);
            }
            ApplyReportFilter();await RecordAuditAsync("ReportRun",rows.Count==0?"Blocked":"Succeeded",mode=="BRAND"?"Brand stock":mode=="SLOW"?"Slow stock":"Closing stock");
        }
        catch(Exception ex){ReportResult.Text=$"Stock report failed: {ex.Message}";}
    }

    private async Task RunStockMovementAsync()
    {
        try
        {
            var scope=ReportScope();var data=await new SqlServerReportingQueryRepository(connectionState.ConnectionString).LoadStockAsync(scope);var rows=data.Movements;var status=rows.Count==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed;
            ReportGrid.ItemsSource=rows;ReportResult.Text=$"{status}: {rows.Count:N0} source movement group(s).";SetExport("Stock Movement",status,RetailReportingPolicy.Version,"Movement quantities retain the ETP source transaction type and source-signed quantity.",[new("Store"),new("Item"),new("Movement Type"),new("Signed Quantity","#,##0.00")],rows.Select(x=>(IReadOnlyList<object?>)[x.StoreCode,x.ItemCode,x.SourceMovementType,x.SourceSignedQuantity]).ToArray(),["Total","","",rows.Sum(x=>x.SourceSignedQuantity)]);ApplyReportFilter();await RecordAuditAsync("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Blocked","Stock movement");
        }
        catch(Exception ex){ReportResult.Text=$"Stock movement report failed: {ex.Message}";}
    }

    private async Task RunFocusedExceptionAsync(string focus)
    {
        try
        {
            var scope=ReportScope();if(scope.StoreCodes is not {Count:1}||scope.DateFrom!=scope.DateTo)throw new InvalidOperationException("Select one store and one business date for an exception report.");var all=await new OperationalReportRepository(connectionState.ConnectionString).LoadDailyExceptionsAsync(scope.StoreCodes[0],scope.DateTo);
            var rows=focus switch{"Source"=>all.Where(x=>x.Area=="Source"),"Unmapped"=>all.Where(x=>x.Area.Contains("Staff",StringComparison.OrdinalIgnoreCase)||x.Code.Contains("MISSING",StringComparison.OrdinalIgnoreCase)||x.Code.Contains("AMBIGUOUS",StringComparison.OrdinalIgnoreCase)),"Stock"=>all.Where(x=>x.Area.Contains("stock",StringComparison.OrdinalIgnoreCase)),"Staff"=>all.Where(x=>x.Area.Contains("Staff",StringComparison.OrdinalIgnoreCase)),"Tender"=>all.Where(x=>x.Area=="Tender"),_=>all};var result=rows.ToArray();var status=result.Any(x=>x.Severity is "BLOCKER" or "FAIL")?ReconciliationStatus.Failed:ReconciliationStatus.Passed;
            ReportGrid.ItemsSource=result;ReportResult.Text=$"{status}: {result.Length:N0} {focus.ToLowerInvariant()} exception(s).";SetExport($"{focus} Exceptions",status,RetailReportingPolicy.Version,"Focused view of the same immutable daily exception evidence; filtering never changes technical control status.",[new("Severity"),new("Area"),new("Code"),new("Store"),new("Date"),new("Document"),new("Item"),new("Variance","#,##0.00"),new("Workbook"),new("Sheet"),new("Source Row","#,##0"),new("Message"),new("Recommended Action")],result.Select(x=>(IReadOnlyList<object?>)[x.Severity,x.Area,x.Code,x.StoreCode,x.BusinessDate,x.DocumentNumber,x.ItemCode,x.Variance,x.SourceWorkbook,x.SourceSheet,x.SourceRow,x.Message,x.RecommendedAction]).ToArray(),["Total",result.Length,"","","","","",result.Where(x=>x.Variance is not null).Sum(x=>x.Variance),"","","","",""]);ApplyReportFilter();await RecordAuditAsync("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Failed",$"{focus} exceptions");
        }
        catch(Exception ex){ReportResult.Text=$"Exception report failed: {ex.Message}";}
    }

    private async Task RunManagementTrendReportAsync()
    {
        try
        {
            var scope=ReportScope();var rows=await new Phase2OperationsRepository(connectionState.ConnectionString).LoadManagementTrendAsync(scope.DateFrom,scope.DateTo);if(scope.StoreCodes is {Count:>0})rows=rows.Where(x=>scope.StoreCodes.Contains(x.StoreCode,StringComparer.OrdinalIgnoreCase)).ToArray();var status=rows.Count==0?ReconciliationStatus.Blocked:ReconciliationStatus.Passed;
            ReportGrid.ItemsSource=rows;ReportResult.Text=$"{status}: {rows.Count:N0} daily management trend row(s).";SetExport("Management Trend",status,RetailReportingPolicy.Version,"Daily canonical sales, units, invoices and unchanged control variances.",[new("Date"),new("Store"),new("Net Sales","#,##0.00"),new("Units","#,##0.00"),new("Invoices","#,##0"),new("Tender Variance","#,##0.00"),new("Unmatched Staff Rows","#,##0")],rows.Select(x=>(IReadOnlyList<object?>)[x.BusinessDate,x.StoreCode,x.NetSales,x.Units,x.Invoices,x.TenderVariance,x.UnmatchedEnrichmentRows]).ToArray(),["Total","",rows.Sum(x=>x.NetSales),rows.Sum(x=>x.Units),rows.Sum(x=>x.Invoices),rows.Sum(x=>x.TenderVariance),rows.Sum(x=>x.UnmatchedEnrichmentRows)]);ApplyReportFilter();await RecordAuditAsync("ReportRun",status==ReconciliationStatus.Passed?"Succeeded":"Blocked","Management trend");
        }
        catch(Exception ex){ReportResult.Text=$"Management trend failed: {ex.Message}";}
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
            var rows = await new OperationalReportRepository(connectionState.ConnectionString).LoadInvoiceSummaryAsync(ReportScope());
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
            var rows = await new OperationalReportRepository(connectionState.ConnectionString).LoadInvoiceLineageAsync(ReportScope());
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
            var repository = new OperationalReportRepository(connectionState.ConnectionString);
            var rows = await repository.LoadDsrAsync(scope.DateTo, ["WLMHW", "HEMW"]);
            var dsrDocument = await repository.ComposeDailySalesReportDocumentAsync(scope.DateTo, rows);
            var hasSales = rows.Any(x => x.TySales is not null);
            var status = hasSales ? ReconciliationStatus.Passed : ReconciliationStatus.Blocked;
            var unavailable = rows.Count(x => x.GrowthStatus != MetricAvailability.Available.ToString());
            var message = $"FTD, MTD and Indian-financial-year YTD use business date {scope.DateTo:dd-MMM-yyyy}; {unavailable:N0} row(s) have unavailable LY growth rather than a misleading percentage.";
            ReportGrid.ItemsSource = rows; ReportResult.Text = $"{status}: {message}";
            SetExport("Daily Sales Report", status, OperationalReportRepository.DsrMetricPolicy, message,
                [new("Period"),new("Store"),new("From"),new("To"),new("TY Sales","#,##0.00"),new("LY Sales","#,##0.00"),new("Growth %","#,##0.00"),new("Growth Status"),new("TY Units","#,##0.00"),new("LY Units","#,##0.00"),new("TY Invoices","#,##0"),new("LY Invoices","#,##0"),new("UPT","#,##0.00"),new("ATV","#,##0.00"),new("Walk-ins","#,##0.00"),new("Conversion %","#,##0.00")],
                rows.Select(x => (IReadOnlyList<object?>)[x.Period,x.Store,x.PeriodStart,x.PeriodEnd,x.TySales,x.LySales,x.GrowthPercent,x.GrowthStatus,x.TyUnits,x.LyUnits,x.TyInvoices,x.LyInvoices,x.Upt,x.Atv,x.WalkIns,x.ConversionPercent]).ToArray(),
                ["Independent periods","","","","","","","","","","","","","","",""], dsrDocument, scope.DateTo);
            ApplyReportFilter();
            await RecordAuditAsync("ReportRun", status == ReconciliationStatus.Passed ? "Succeeded" : "Blocked", "Daily sales report");
        }
        catch (Exception ex)
        {
            ReportResult.Text = $"DSR failed: {ex.Message}";
            if (focusedWorkspaceKind == "report") dsrWorkspace?.ShowFailure(ex.Message);
        }
    }

    private async void RunStaffPerformance_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await new OperationalReportRepository(connectionState.ConnectionString).LoadStaffPerformanceAsync(ReportScope());
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
            var rows = await new OperationalReportRepository(connectionState.ConnectionString).LoadServiceSalesAsync(scope.DateTo, scope.StoreCodes);
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
            var result = await new OperationalReportRepository(connectionState.ConnectionString).LoadCashReconciliationAsync(scope.StoreCodes[0], scope.DateTo);
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
            var rows = await new OperationalReportRepository(connectionState.ConnectionString).LoadPhysicalStockAsync(scope.StoreCodes[0], scope.DateTo);
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
            var rows = await new OperationalReportRepository(connectionState.ConnectionString).LoadDailyExceptionsAsync(scope.StoreCodes[0], scope.DateTo);
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
        IReadOnlyList<ExcelReportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?> totals,
        DailySalesReportDocument? dsrReport = null, DateOnly? businessDate = null)
    {
        var scope = ReportScope();
        currentExportMetadata = new(name, businessDate ?? scope.DateFrom, businessDate ?? scope.DateTo, status.ToString(), ruleVersion, message, DateTimeOffset.UtcNow);
        currentExportData = new(columns, rows, totals);
        currentDsrReport = dsrReport;
        currentReportCode = dsrReport is null && currentReportCode == "dsr" ? null : currentReportCode;
        currentVisualReport = VisualReportComposer.Compose(currentExportMetadata, currentExportData);
        if (currentDsrReport is not null) RenderDsrReport(currentDsrReport); else RenderVisualReport(currentVisualReport);
        UpdateFocusedReportPreview();
        ExportExcelButton.IsEnabled = true; ExportPdfButton.IsEnabled = true;
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
        try { if (currentVisualReport is not null) new OpenXmlVisualReportExporter().Export(dialog.FileName, currentVisualReport); else new OpenXmlReportExporter().Export(dialog.FileName, currentExportMetadata, currentExportData); ReportResult.Text = $"Excel report saved to {dialog.FileName}"; _ = RecordAuditAsync("ExportExcel", "Succeeded", "Visual report exported"); }
        catch (Exception ex) { ReportResult.Text = $"Excel export failed: {ex.Message}"; }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (currentExportMetadata is null || currentExportData is null) return;
        if (string.Equals(currentExportMetadata.ReportName, "Daily Sales Report", StringComparison.Ordinal) && currentDsrReport is null)
        {
            ReportResult.Text = "The DSR document is not ready. Run Daily Sales / DSR again before exporting.";
            return;
        }
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"{SafeFileName(currentExportMetadata.ReportName)}_{currentExportMetadata.DateFrom:yyyyMMdd}_{currentExportMetadata.DateTo:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { if (currentDsrReport is not null) new DailySalesReportPdfExporter().Export(dialog.FileName, currentDsrReport); else if (currentVisualReport is not null) new SimplePdfVisualReportExporter().Export(dialog.FileName, currentVisualReport); else new SimplePdfReportExporter().Export(dialog.FileName, currentExportMetadata, currentExportData); ReportResult.Text = $"PDF report saved to {dialog.FileName}"; _ = RecordAuditAsync("ExportPdf", "Succeeded", currentDsrReport is null ? "Visual report exported" : "One-page DSR exported"); }
        catch (Exception ex) { ReportResult.Text = $"PDF export failed: {ex.Message}"; }
    }

    private async Task CheckConnectionAndRefreshAsync(bool showProgress)
    {
        if (showProgress) ConnectionResult.Text = "Testing…";
        var validation = ConnectionStringValidation.Validate(ConnectionStringInput.Text);
        if (!validation.IsValid)
        {
            ConnectionResult.Text = validation.Error;
            SetConnectionState(false, "Waiting for a valid Windows-integrated connection");
            ApplicationStatus.Text = validation.Error;
            return;
        }
        var health = await new SqlServerHealthCheck(validation.ConnectionString!).CheckAsync();
        var connected = health.Status == DatabaseHealthStatus.Healthy;
        ConnectionResult.Text = health.Message;
        SetConnectionState(connected, connected ? "Ready to validate or report" : "Waiting for connection");
        ApplicationStatus.Text = connected ? $"Connected to SQL Server {health.ServerVersion}." : health.Message;
        if (connected)
        {
            connectionState.TryUpdate(validation.ConnectionString, out _);
            ConnectionStringInput.Text = connectionState.ConnectionString;
            await RefreshAccessAsync();
            TrySaveSettings();
            await RecordAuditAsync("ConfigurationChange", "Succeeded", "Windows integrated database configuration saved");
            if (currentAccess.CanView) await RefreshDashboardAsync();
        }
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
            latestDashboardSnapshot = await dashboardQueryFactory(connectionState.ConnectionString).LoadAsync();
            dashboardView.Show(DashboardViewState.FromSnapshot(latestDashboardSnapshot));
        }
        catch (Exception ex)
        {
            dashboardView.ShowError(ex.Message);
            ApplicationStatus.Text = $"Dashboard refresh failed: {ex.Message}";
        }
    }

    private async void RefreshOperations_Click(object sender, RoutedEventArgs e) => await RefreshOperationsAsync();

    private async Task RefreshOperationsAsync()
    {
        try
        {
            RequireViewAccess();
            if (OperationsFromInput.SelectedDate is null || OperationsToInput.SelectedDate is null) throw new InvalidOperationException("Select the management trend dates.");
            var from = DateOnly.FromDateTime(OperationsFromInput.SelectedDate.Value);
            var to = DateOnly.FromDateTime(OperationsToInput.SelectedDate.Value);
            var repository = new Phase2OperationsRepository(connectionState.ConnectionString);
            var settingsTask = repository.LoadWatchFolderSettingsAsync();
            var trendTask = repository.LoadManagementTrendAsync(from, to);
            var qualityTask = repository.LoadDataQualitySummaryAsync();
            var schedulesTask = repository.LoadSchedulesAsync();
            var runsTask = repository.LoadAutomationRunsAsync(100);
            await Task.WhenAll(settingsTask, trendTask, qualityTask, schedulesTask, runsTask);
            var settings = await settingsTask; var trend = await trendTask; var quality = await qualityTask;
            var productRepository = new ProductisationRepository(connectionState.ConnectionString);
            await productRepository.SyncDataQualityIssuesAsync(quality);
            var issueRows = await productRepository.LoadDataQualityIssuesAsync();
            WatchInboundInput.Text = settings.InboundPath; WatchProcessedInput.Text = settings.ProcessedPath;
            WatchFailedInput.Text = settings.FailedPath; WatchReportOutputInput.Text = settings.ReportOutputPath; WatchEnabledInput.IsChecked = settings.IsEnabled;
            ManagementTrendGrid.ItemsSource = trend; DataQualityGrid.ItemsSource = issueRows; ReportSchedulesGrid.ItemsSource = await schedulesTask; AutomationRunsGrid.ItemsSource = await runsTask;
            RenderManagementTrendChart(trend);
            OperationsStatus.Text = $"Loaded {trend.Count:N0} daily store result(s), {issueRows.Count:N0} governed quality issue(s), and {(await runsTask).Count:N0} recent unattended run(s).";
        }
        catch (Exception ex) { OperationsStatus.Text = $"Operations center could not be refreshed: {ex.Message}"; }
    }

    private async void SaveAutomationSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            var repository = new Phase2OperationsRepository(connectionState.ConnectionString);
            await repository.SaveWatchFolderSettingsAsync(new(WatchInboundInput.Text, WatchProcessedInput.Text, WatchFailedInput.Text,
                WatchReportOutputInput.Text, 5, WatchEnabledInput.IsChecked == true, DateTime.MinValue, currentAccess.WindowsIdentity), WatchChangeReasonInput.Text);
            WatchChangeReasonInput.Clear(); OperationsStatus.Text = "Automatic import and report-output folders were saved and audited.";
            await RefreshOperationsAsync();
        }
        catch (Exception ex) { OperationsStatus.Text = $"Automation settings were not saved: {ex.Message}"; }
    }

    private async void RunAutomationNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireImportAccess();
            OperationsStatus.Text = "Running controlled watch-folder import and due report schedules…";
            var result = await new AutomatedOperationsService(connectionState.ConnectionString).RunOnceAsync();
            OperationsStatus.Text = result.Message;
            await RefreshOperationsAsync(); await RefreshDashboardAsync();
        }
        catch (Exception ex) { OperationsStatus.Text = $"Unattended processing failed: {ex.Message}"; }
    }

    private void ReportSchedule_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReportSchedulesGrid.SelectedItem is not ReportPackSchedule schedule) return;
        ScheduleTimeInput.Text = schedule.LocalRunTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        ScheduleEnabledInput.IsChecked = schedule.IsEnabled; ScheduleExcelInput.IsChecked = schedule.ExportExcel; SchedulePdfInput.IsChecked = schedule.ExportPdf;
    }

    private async void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            if (ReportSchedulesGrid.SelectedItem is not ReportPackSchedule schedule) throw new InvalidOperationException("Select the morning or evening schedule first.");
            if (!TimeOnly.TryParseExact(ScheduleTimeInput.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new InvalidOperationException("Enter schedule time in 24-hour HH:mm format.");
            await new Phase2OperationsRepository(connectionState.ConnectionString).SaveScheduleAsync(schedule.Id, time, ScheduleEnabledInput.IsChecked == true,
                ScheduleExcelInput.IsChecked == true, SchedulePdfInput.IsChecked == true, ScheduleReasonInput.Text);
            ScheduleReasonInput.Clear(); OperationsStatus.Text = "The selected report schedule was updated and audited."; await RefreshOperationsAsync();
        }
        catch (Exception ex) { OperationsStatus.Text = $"Schedule was not saved: {ex.Message}"; }
    }

    private async void RunBackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess(); MaintenanceStatus.Text = "Creating and verifying a checksum database backup…";
            var result = await PowerShellOperationsService.RunAsync("backup-etp-database.ps1");
            MaintenanceStatus.Text = result.Succeeded ? $"Backup passed. {result.Message}" : $"Backup failed. {result.Message}";
            await RefreshDashboardAsync();
        }
        catch (Exception ex) { MaintenanceStatus.Text = $"Backup could not run: {ex.Message}"; }
    }

    private async void RunRecoveryDrillNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess(); MaintenanceStatus.Text = "Running an isolated restore, integrity check and lineage comparison…";
            var result = await PowerShellOperationsService.RunAsync("invoke-etp-recovery-drill.ps1");
            MaintenanceStatus.Text = result.Succeeded ? "Recovery drill passed and the temporary database was removed." : $"Recovery drill failed. {result.Message}";
            await RefreshDashboardAsync();
        }
        catch (Exception ex) { MaintenanceStatus.Text = $"Recovery drill could not run: {ex.Message}"; }
    }

    private async void CreateSupportPackage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess(); MaintenanceStatus.Text = "Creating an aggregate-only support package without source rows or confidential identifiers…";
            var result = await PowerShellOperationsService.RunAsync("new-etp-support-package.ps1");
            MaintenanceStatus.Text = result.Succeeded ? $"Support package created. {result.Message}" : $"Support package failed. {result.Message}";
            await RecordAuditAsync("SupportPackage", result.Succeeded ? "Succeeded" : "Failed", "Privacy-safe support package operation completed");
        }
        catch (Exception ex) { MaintenanceStatus.Text = $"Support package could not be created: {ex.Message}"; }
    }

    private async void RefreshReportArchive_Click(object sender, RoutedEventArgs e) => await RefreshReportArchiveAsync();

    private async Task RefreshReportArchiveAsync()
    {
        try
        {
            RequireViewAccess();
            var store = ArchiveStoreInput.SelectedItem is ComboBoxItem item && !string.Equals(item.Content?.ToString(), "All", StringComparison.OrdinalIgnoreCase) ? item.Content?.ToString() : null;
            var date = ArchiveAllDatesInput.IsChecked == true || ArchiveDateInput.SelectedDate is null ? (DateOnly?)null : DateOnly.FromDateTime(ArchiveDateInput.SelectedDate.Value);
            var rows = await new Phase2OperationsRepository(connectionState.ConnectionString).LoadReportGenerationsAsync(store, date);
            ReportGenerationGrid.ItemsSource = rows; ReportArchiveDetailGrid.ItemsSource = null; currentArchivedDocument = null; currentShareFile = null;
            ReportArchiveStatus.Text = $"{rows.Count:N0} immutable generation(s) found. Select one to open or exactly two to compare.";
        }
        catch (Exception ex) { ReportArchiveStatus.Text = $"Report archive could not be loaded: {ex.Message}"; }
    }

    private async void OpenArchivedGeneration_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ReportGenerationGrid.SelectedItem is not ArchivedReportGeneration generation) throw new InvalidOperationException("Select one report generation.");
            currentArchivedDocument = await new Phase2OperationsRepository(connectionState.ConnectionString).LoadArchivedReportAsync(generation.Id);
            currentShareFile = null;
            ReportArchiveDetailGrid.ItemsSource = currentArchivedDocument.Tables.Select(table => new { table.Name, table.Status, Rows = table.Data.Rows.Count, table.Message });
            ReportArchiveStatus.Text = $"Generation {generation.GenerationNumber} passed its document SHA-256 check and is ready to re-export.";
            await RecordAuditAsync("ReportArchive", "Succeeded", "Archived report opened");
        }
        catch (Exception ex) { ReportArchiveStatus.Text = $"Archived generation could not be opened: {ex.Message}"; }
    }

    private async void CompareArchivedGenerations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = ReportGenerationGrid.SelectedItems.OfType<ArchivedReportGeneration>().ToArray();
            if (selected.Length != 2) throw new InvalidOperationException("Select exactly two report generations.");
            var rows = await new Phase2OperationsRepository(connectionState.ConnectionString).CompareReportGenerationsAsync(selected[0].Id, selected[1].Id);
            ReportArchiveDetailGrid.ItemsSource = rows; currentArchivedDocument = null;
            ReportArchiveStatus.Text = $"Compared generations {selected[0].GenerationNumber} and {selected[1].GenerationNumber}: {rows.Count(x => x.Changed):N0} report section(s) changed.";
            await RecordAuditAsync("ReportArchive", "Succeeded", "Archived generations compared");
        }
        catch (Exception ex) { ReportArchiveStatus.Text = $"Generation comparison failed: {ex.Message}"; }
    }

    private void ExportArchivedExcel_Click(object sender, RoutedEventArgs e)
    {
        if (currentArchivedDocument is null) { ReportArchiveStatus.Text = "Open one archived generation before exporting."; return; }
        var dialog = new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx", FileName = $"ETP_Archived_Pack_{currentArchivedDocument.DateTo:yyyyMMdd}.xlsx", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { new OpenXmlReportPackExporter().Export(dialog.FileName, currentArchivedDocument); ReportArchiveStatus.Text = $"Archived Excel pack saved to {dialog.FileName}"; _ = RecordAuditAsync("ExportExcel", "Succeeded", "Archived report pack exported"); }
        catch (Exception ex) { ReportArchiveStatus.Text = $"Archived Excel export failed: {ex.Message}"; }
    }

    private void ExportArchivedPdf_Click(object sender, RoutedEventArgs e)
    {
        if (currentArchivedDocument is null) { ReportArchiveStatus.Text = "Open one archived generation before exporting."; return; }
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"ETP_Archived_Pack_{currentArchivedDocument.DateTo:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { new SimplePdfReportPackExporter().Export(dialog.FileName, currentArchivedDocument); ReportArchiveStatus.Text = $"Archived PDF pack saved to {dialog.FileName}"; _ = RecordAuditAsync("ExportPdf", "Succeeded", "Archived report pack exported"); }
        catch (Exception ex) { ReportArchiveStatus.Text = $"Archived PDF export failed: {ex.Message}"; }
    }

    private async void MasterType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !currentAccess.CanAdminister) return;
        await RefreshMasterAdministrationAsync();
    }

    private async Task RefreshMasterAdministrationAsync()
    {
        try
        {
            RequireOwnerAccess();
            var repository = new Phase2OperationsRepository(connectionState.ConnectionString);
            var mastersTask = repository.LoadMasterValuesAsync(SelectedContent(MasterTypeInput));
            var usersTask = repository.LoadUsersAsync();
            var productRepository = new ProductisationRepository(connectionState.ConnectionString);
            var kpiTask = productRepository.LoadKpiCatalogueAsync();
            var healthTask = productRepository.LoadProductHealthAsync();
            var productSettingsTask = productRepository.LoadSettingsAsync();
            await Task.WhenAll(mastersTask, usersTask, kpiTask, healthTask, productSettingsTask);
            ControlledMastersGrid.ItemsSource = await mastersTask; ApplicationUsersGrid.ItemsSource = await usersTask;
            KpiCatalogueGrid.ItemsSource = await kpiTask; ProductHealthGrid.ItemsSource = await healthTask;
            var productSettings = await productSettingsTask;
            DocumentRepositoryInput.Text=productSettings.DocumentRepositoryPath;ShareFolderInput.Text=productSettings.ShareFolderPath;
            OcrHelperInput.Text=productSettings.OcrHelperPath??string.Empty;OcrModelInput.Text=productSettings.OcrModelPath??string.Empty;
            SmtpHostInput.Text=productSettings.SmtpHost??string.Empty;SmtpPortInput.Text=productSettings.SmtpPort?.ToString(CultureInfo.InvariantCulture)??string.Empty;
            SmtpFromInput.Text=productSettings.SmtpFromAddress??string.Empty;MaximumAttachmentInput.Text=productSettings.MaximumAttachmentMb.ToString(CultureInfo.InvariantCulture);
            ApplicationStatus.Text = "Controlled masters and Windows-integrated access are ready for Owner administration.";
        }
        catch (Exception ex) { ApplicationStatus.Text = $"Master administration could not be loaded: {ex.Message}"; }
    }

    private async void SaveMaster_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            await new Phase2OperationsRepository(connectionState.ConnectionString).UpsertMasterValueAsync(SelectedContent(MasterTypeInput), MasterCodeInput.Text,
                MasterNameInput.Text, SelectedContent(MasterApprovalInput), MasterActiveInput.IsChecked == true, MasterReasonInput.Text);
            MasterCodeInput.Clear(); MasterNameInput.Clear(); MasterReasonInput.Clear(); await RefreshMasterAdministrationAsync();
        }
        catch (Exception ex) { ApplicationStatus.Text = $"Master value was not saved: {ex.Message}"; }
    }

    private async void SaveUserAccess_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RequireOwnerAccess();
            await new Phase2OperationsRepository(connectionState.ConnectionString).UpsertUserAsync(UserIdentityInput.Text, UserDisplayNameInput.Text,
                SelectedContent(UserRoleInput), UserActiveInput.IsChecked == true, UserReasonInput.Text);
            UserIdentityInput.Clear(); UserDisplayNameInput.Clear(); UserReasonInput.Clear(); await RefreshMasterAdministrationAsync(); await RefreshAccessAsync();
        }
        catch (Exception ex) { ApplicationStatus.Text = $"User access was not saved: {ex.Message}"; }
    }

    private void RenderManagementTrendChart(IReadOnlyList<ManagementTrendRow> rows)
    {
        ManagementTrendChartPanel.Children.Clear();
        var points = rows.GroupBy(x => x.BusinessDate).Select(group => new { Date = group.Key, Sales = group.Sum(x => x.NetSales) }).OrderBy(x => x.Date).TakeLast(31).ToArray();
        var maximum = Math.Max(1m, points.Select(x => Math.Abs(x.Sales)).DefaultIfEmpty(1m).Max());
        foreach (var point in points)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(95) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(120) });
            var label = new TextBlock { Text = point.Date.ToString("dd MMM"), VerticalAlignment = VerticalAlignment.Center };
            var bar = new Border { Background = point.Sales < 0 ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(23, 107, 135)), Height = 14, HorizontalAlignment = HorizontalAlignment.Left, Width = 480d * (double)(Math.Abs(point.Sales) / maximum) };
            var value = new TextBlock { Text = point.Sales.ToString("N2"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(label, 0); Grid.SetColumn(bar, 1); Grid.SetColumn(value, 2); row.Children.Add(label); row.Children.Add(bar); row.Children.Add(value); ManagementTrendChartPanel.Children.Add(row);
        }
    }

    private void SaveSettings()
    {
        settingsStore.Save(connectionState.ConnectionString);
    }

    private void TrySaveSettings()
    {
        try { SaveSettings(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { ApplicationStatus.Text = "Connection succeeded, but the local settings file could not be updated safely."; }
    }

    private static string SafeFileName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Replace(' ', '_');

    private static string SelectedContent(ComboBox comboBox) =>
        comboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Content?.ToString())
            ? item.Content!.ToString()!
            : throw new InvalidOperationException("Select a value from the list.");

    private static string RoleLabel(AccessRole role) => role switch
    {
        AccessRole.Owner => "Owner",
        AccessRole.StoreManager => "Store Manager",
        AccessRole.Viewer => "Viewer",
        _ => "No access"
    };

    private static decimal? OptionalDecimal(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
            ? parsed : throw new InvalidOperationException($"'{value}' is not a valid number.");
    }

    private async Task RecordAuditAsync(string eventType, string outcome, string detail)
    {
        try { await new OperationalAuditRepository(connectionState.ConnectionString).RecordAsync(eventType, outcome, detail); }
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
        OpenDrawer("Report row details", "Source evidence and technical lineage remain available without leaving the report workspace.", ReportGrid.SelectedItem);
    }

    private void ExportDashboardPdf_Click(object sender, RoutedEventArgs e)
    {
        if (latestDashboardSnapshot is null) { ApplicationStatus.Text = "Refresh the dashboard before exporting a management summary."; return; }
        var dialog = new SaveFileDialog { Filter = "PDF report (*.pdf)|*.pdf", FileName = $"ETP_Management_Summary_{DateTime.Today:yyyyMMdd}.pdf", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        var groups = latestDashboardSnapshot.RecentImports.GroupBy(x => x.ReportCode).OrderBy(x => x.Key).ToArray();
        var metadata = new ExcelReportMetadata("ETP Management Summary", ReportFrom.SelectedDate is { } from ? DateOnly.FromDateTime(from) : DateOnly.FromDateTime(DateTime.Today), ReportTo.SelectedDate is { } to ? DateOnly.FromDateTime(to) : DateOnly.FromDateTime(DateTime.Today), "Operational", "v1", "Aggregate operational evidence only; confidential source rows are excluded.", DateTimeOffset.UtcNow);
        var data = new ExcelReportData([new("Report"), new("Files", "#,##0"), new("Rows", "#,##0")], groups.Select(x => (IReadOnlyList<object?>)[x.Key, x.Count(), x.Sum(v => v.SourceRows)]).ToArray(), ["Total", latestDashboardSnapshot.ImportedFiles, latestDashboardSnapshot.SourceRows]);
        try { new SimplePdfReportExporter().Export(dialog.FileName, metadata, data); ApplicationStatus.Text = $"Management summary saved to {dialog.FileName}"; }
        catch (Exception ex) { ApplicationStatus.Text = $"Management summary export failed: {ex.Message}"; }
    }

    private sealed record BrandSegmentEntry(string Code, string Description, string Status);
    private sealed class DelegateWorkbookImportOutcomeProcessor(Func<string, CancellationToken, Task<WorkbookImportOutcome>> process) : IWorkbookImportOutcomeProcessor
    {
        public async Task ProcessAsync(string workbookPath, CancellationToken cancellationToken) =>
            _ = await process(workbookPath, cancellationToken);
        public Task<WorkbookImportOutcome> ProcessWithOutcomeAsync(string workbookPath, CancellationToken cancellationToken) =>
            process(workbookPath, cancellationToken);
    }
}
