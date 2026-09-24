extern alias EtpApplication;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.Dashboard;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Desktop.Modules.Archive;
using Etp.Reporting.Desktop.Modules.Registers;
using Etp.Reporting.Desktop.Modules.SourceInbox;

namespace Etp.Reporting.Desktop;

using DashboardQuery = EtpApplication::Etp.Reporting.Application.Dashboard.IDashboardQuery;
using AccessSession = EtpApplication::Etp.Reporting.Application.Access.AccessSession;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;
using AccessSessionQuery = EtpApplication::Etp.Reporting.Application.Access.IAccessSessionQuery;
using RecordOperationalAudit = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.RecordOperationalAudit;
using DatabaseLifecycleService = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.IDatabaseLifecycleService;

public partial class MainWindow : Window
{
    internal readonly ShellViewModel shell;
    internal readonly DashboardView dashboardView;
    private readonly Func<string, DashboardQuery> dashboardQueryFactory;
    internal readonly SettingsWorkspaceView settingsWorkspace;
    private readonly DesktopConnectionState connectionState;
    private readonly Func<string, AccessSessionQuery> accessSessionQueryFactory;
    internal readonly ArchiveWorkspaceView archiveWorkspaceView;
    internal readonly RegistersWorkspaceView registersWorkspaceView;
    internal readonly DailyWorkflowWorkspaceView dailyWorkflowWorkspace;
    internal readonly SourceInboxWorkspaceView sourceInboxWorkspaceView;
    internal readonly ReportsWorkspaceView reportsWorkspaceView;
    internal readonly AccountingWorkspaceView accountingWorkspaceView;
    internal readonly OperationsWorkspaceView operationsWorkspaceView;
    internal readonly InvestigationApprovalsWorkspaceView investigationWorkspaceView;
    internal readonly AdministrationWorkspaceView administrationWorkspaceView;
    private readonly Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory;
    internal readonly ImportWorkspaceView importWorkspaceView;
    internal ImportHistoryView? importHistoryView;
    private AccessSession currentAccess = new("unknown", "Unknown user", AccessRole.None, false);

    public MainWindow(
        ShellViewModel shell,
        DashboardView dashboardView,
        Func<string, DashboardQuery> dashboardQueryFactory,
        SettingsWorkspaceView settingsWorkspace,
        DesktopConnectionState connectionState,
        Func<string, AccessSessionQuery> accessSessionQueryFactory,
        ArchiveWorkspaceView archiveWorkspaceView,
        RegistersWorkspaceView registersWorkspaceView,
        DailyWorkflowWorkspaceView dailyWorkflowWorkspace,
        SourceInboxWorkspaceView sourceInboxWorkspaceView,
        ReportsWorkspaceView reportsWorkspaceView,
        AccountingWorkspaceView accountingWorkspaceView,
        OperationsWorkspaceView operationsWorkspaceView,
        InvestigationApprovalsWorkspaceView investigationWorkspaceView,
        AdministrationWorkspaceView administrationWorkspaceView,
        Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory,
        ImportWorkspaceView importWorkspaceView)
    {
        this.shell = shell ?? throw new ArgumentNullException(nameof(shell));
        this.dashboardView = dashboardView ?? throw new ArgumentNullException(nameof(dashboardView));
        this.dashboardQueryFactory = dashboardQueryFactory ?? throw new ArgumentNullException(nameof(dashboardQueryFactory));
        this.settingsWorkspace = settingsWorkspace ?? throw new ArgumentNullException(nameof(settingsWorkspace));
        this.connectionState = connectionState ?? throw new ArgumentNullException(nameof(connectionState));
        this.accessSessionQueryFactory = accessSessionQueryFactory ?? throw new ArgumentNullException(nameof(accessSessionQueryFactory));
        this.archiveWorkspaceView = archiveWorkspaceView ?? throw new ArgumentNullException(nameof(archiveWorkspaceView));
        this.registersWorkspaceView = registersWorkspaceView ?? throw new ArgumentNullException(nameof(registersWorkspaceView));
        this.dailyWorkflowWorkspace = dailyWorkflowWorkspace ?? throw new ArgumentNullException(nameof(dailyWorkflowWorkspace));
        this.sourceInboxWorkspaceView = sourceInboxWorkspaceView ?? throw new ArgumentNullException(nameof(sourceInboxWorkspaceView));
        this.reportsWorkspaceView = reportsWorkspaceView ?? throw new ArgumentNullException(nameof(reportsWorkspaceView));
        this.accountingWorkspaceView = accountingWorkspaceView ?? throw new ArgumentNullException(nameof(accountingWorkspaceView));
        this.operationsWorkspaceView = operationsWorkspaceView ?? throw new ArgumentNullException(nameof(operationsWorkspaceView));
        this.investigationWorkspaceView = investigationWorkspaceView ?? throw new ArgumentNullException(nameof(investigationWorkspaceView));
        this.administrationWorkspaceView = administrationWorkspaceView ?? throw new ArgumentNullException(nameof(administrationWorkspaceView));
        this.databaseLifecycleServiceFactory = databaseLifecycleServiceFactory ?? throw new ArgumentNullException(nameof(databaseLifecycleServiceFactory));
        this.importWorkspaceView = importWorkspaceView ?? throw new ArgumentNullException(nameof(importWorkspaceView));
        if (Application.Current is null) Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Etp.Reporting.Desktop;component/Themes/Theme.xaml", UriKind.Relative) });
        InitializeComponent();
        // Set after InitializeComponent: the XAML Title is the design-time name.
        Title = DesktopProductVersion.WindowTitle;
        dailyWorkflowWorkspace.AttachHost(
            () => new(currentAccess.CanView, currentAccess.CanImport, currentAccess.CanAdminister),
            RecordAuditAsync,
            RefreshDashboardAsync);
        dailyWorkflowWorkspace.NotificationRequested += (_, args) => ApplicationStatus.Text = args.Message;
        settingsWorkspace.ConnectionPresentationChanged += SettingsWorkspace_ConnectionPresentationChanged;
        settingsWorkspace.OperationCompletedAsync = SettingsWorkspace_OperationCompletedAsync;


        archiveWorkspaceView.AttachHost(() => currentAccess, RecordAuditAsync, DesktopFriendlyError.Describe);
        archiveWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;

        registersWorkspaceView.AttachHost(() => currentAccess, DesktopFriendlyError.Describe);
        registersWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;

        sourceInboxWorkspaceView.AttachHost(() => currentAccess, DesktopFriendlyError.Describe);
        sourceInboxWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        sourceInboxWorkspaceView.SelectedDocumentIdChanged += (_, documentId) => registersWorkspaceView.LinkedSourceDocumentId = documentId;

        reportsWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        reportsWorkspaceView.AttachHost(
            ShowFocusedReportWorkspace,
            RecordAuditAsync,
            (snapshot, rows, status) =>
            {
                if (focusedWorkspaceKind == "report") { reportWorkspaceSession.UpdatePreview(snapshot, rows, status, reportsWorkspaceView.ShowRowDetails); ApplicationStatus.Text = status; }
            },
            message => reportWorkspaceSession.ShowDailySalesFailure(message),
            row => ShowReportDetails("Report row details", "Details for the selected report row.", row));

        importWorkspaceView.AttachHost(
            () => new(currentAccess.CanImport, currentAccess.CanAdminister),
            RecordAuditAsync,
            RefreshDashboardAsync);
        importWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        importWorkspaceView.ReadinessChanged += (_, status) =>
        {
            ApplicationStatus.Text = status;
        };

        accountingWorkspaceView.AttachHost(() => currentAccess, DesktopFriendlyError.Describe);
        accountingWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;

        operationsWorkspaceView.DashboardRefreshRequestedAsync = RefreshDashboardAsync;
        operationsWorkspaceView.AuditRequestedAsync = RecordAuditAsync;


        administrationWorkspaceView.AccessChangedAsync = () => RefreshAccessAsync();
        administrationWorkspaceView.StoresChangedAsync = RefreshStoresAsync;
        dashboardView.RefreshRequested += async (_, _) => await RefreshDashboardAsync();
        dashboardView.ExportDateFrom = () => reportsWorkspaceView.DateFrom is { } from ? DateOnly.FromDateTime(from) : DateOnly.FromDateTime(DateTime.Today);
        dashboardView.ExportDateTo = () => reportsWorkspaceView.DateTo is { } to ? DateOnly.FromDateTime(to) : DateOnly.FromDateTime(DateTime.Today);
        dashboardView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        InitializeShell();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeWorkspaceAsync();
    }

    private bool startupFailed;

    private async Task InitializeWorkspaceAsync()
    {
        ContinueButton.IsEnabled = false;
        WelcomeProgress.Visibility = Visibility.Visible;
        try
        {
            settingsWorkspace.Initialize();
            await RefreshAccessAsync(propagateFailure: true);
            await settingsWorkspace.CheckConnectionAsync(false);
            await RecordAuditAsync("ApplicationStart", "Succeeded", "Desktop application started");
            await RecordAuditAsync("SessionStart", "Succeeded", "Windows integrated user session started");
            if (currentAccess.CanView) { await RefreshStoresAsync(); await RefreshDashboardAsync(); }
            startupFailed = false;
            ContinueButton.Content = "Continue";
            CompleteWelcomeState();
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Startup", "STARTUP_FAILED", DesktopDiagnosticSeverity.Error);
            startupFailed = true;
            WelcomeOverlay.Visibility = Visibility.Visible;
            WelcomeRoleText.Text = "Connection unavailable";
            WelcomeMessage.Text = "Cannot reach SQL Server: " + DesktopFriendlyError.Describe(exception);
            WelcomeProgress.Visibility = Visibility.Collapsed;
            ContinueButton.Content = "Retry";
            ContinueButton.IsEnabled = true;
        }
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string destination }) return;
        NavigateToDestination(destination);
    }

    internal void ApplyNavigationDecision(NavigationDecision decision)
    {
        if (!decision.IsAllowed)
        {
            if (!string.IsNullOrWhiteSpace(decision.DenialReason)) ApplicationStatus.Text = decision.DenialReason;
            return;
        }
        if (decision.RequestedRoute.TaskId?.StartsWith("help:", StringComparison.Ordinal) != true) helpWorkspaceSession.Abandon();
        if (decision.RequestedRoute == WorkspaceRoute.Home) { OpenSection("Today"); return; }
        taskNavigator!.DisplayTaskRoute(decision.RequestedRoute);
    }

    private async Task RefreshAccessAsync(bool propagateFailure = false)
    {
        try
        {
            currentAccess = await accessSessionQueryFactory(connectionState.ConnectionString).LoadCurrentAsync();
            settingsWorkspace.UpdateAccess(new(currentAccess.Role != AccessRole.None, currentAccess.CanAdminister));
            UpdateOperationsAdministrationAccess();
            dailyWorkflowWorkspace.RefreshAccessState();
        }
        catch (Exception ex) when (!propagateFailure && DesktopFriendlyError.IsDatabaseAvailabilityFailure(ex))
        {
            currentAccess = new("unknown", "Access not initialized", AccessRole.None, false);
            settingsWorkspace.UpdateAccess(new(currentAccess.Role != AccessRole.None, currentAccess.CanAdminister));
            UpdateOperationsAdministrationAccess();
            dailyWorkflowWorkspace.RefreshAccessState();
        }
    }

    private void UpdateOperationsAdministrationAccess()
    {
        var access = new OperationsAdministrationWorkspaceAccess(
            currentAccess.CanView, currentAccess.CanImport, currentAccess.CanAdminister);
        operationsWorkspaceView.UpdateAccess(access);
        investigationWorkspaceView.UpdateAccess(access);
        administrationWorkspaceView.UpdateAccess(access);
    }

    protected override async void OnClosed(EventArgs e)
    {
        await importWorkspaceView.DisposeAsync();
        base.OnClosed(e);
    }

    private void SettingsWorkspace_ConnectionPresentationChanged(
        object? sender,
        SettingsConnectionPresentationChangedEventArgs e)
    {
        var state = e.State;
        ApplicationStatus.Text = state.ApplicationStatus;
    }

    private async Task SettingsWorkspace_OperationCompletedAsync(
        SettingsWorkspaceOperation operation,
        bool succeeded)
    {
        switch (operation)
        {
            case SettingsWorkspaceOperation.ConnectionTest:
                if (succeeded)
                {
                    await RefreshAccessAsync();
                    await RecordAuditAsync("ConfigurationChange", "Succeeded", "Windows integrated database configuration saved");
                    if (currentAccess.CanView) { await RefreshStoresAsync(); await RefreshDashboardAsync(); }
                }
                await RecordAuditAsync("ConnectionTest", succeeded ? "Succeeded" : "Failed", "Database connection tested");
                break;
            case SettingsWorkspaceOperation.DatabaseBootstrap:
                await RefreshDashboardAsync();
                await RefreshAccessAsync();
                await RecordAuditAsync("ConfigurationChange", "Succeeded", "Windows integrated database configuration saved");
                await RecordAuditAsync("DatabaseSetup", "Succeeded", "Database migrations verified");
                break;
            case SettingsWorkspaceOperation.ProductConfigurationSaved:
                await administrationWorkspaceView.RefreshAsync();
                break;
        }
    }

    internal async Task RefreshDashboardAsync()
    {
        try
        {
            var snapshot = await dashboardQueryFactory(connectionState.ConnectionString).LoadAsync();
            dashboardView.Show(snapshot);
            var state = dashboardView.CurrentState!;
            databaseHealthLine = $"Database: {state.DatabaseHealth} · Last backup: {state.LatestBackup}";
            AttentionBadge.Text = state.DatabaseHealthTone == DashboardHealthTone.Critical ? "Needs attention" : "";
            AttentionBadge.Visibility = state.DatabaseHealthTone == DashboardHealthTone.Critical ? Visibility.Visible : Visibility.Collapsed;
            taskNavigator?.DefaultBusinessDateToLatestData(snapshot.LatestBusinessDate);
        }
        catch (Exception ex)
        {
            DesktopDiagnostics.Record(ex, "Dashboard.Shell", "DASHBOARD_REFRESH_FAILED");
            var message = DesktopFriendlyError.Describe(ex); dashboardView.ShowError(message); ApplicationStatus.Text = $"Dashboard refresh failed: {message}";
        }
    }

    private static string RoleLabel(AccessRole role) => role switch
    {
        AccessRole.Owner => "Owner",
        AccessRole.StoreManager => "Store Manager",
        AccessRole.Viewer => "Viewer",
        _ => "No access"
    };

    private async Task RecordAuditAsync(string eventType, string outcome, string detail)
    {
        try { await databaseLifecycleServiceFactory(connectionState.ConnectionString).RecordAuditAsync(new RecordOperationalAudit(eventType, outcome, detail)); }
        catch (Exception ex) when (DesktopFriendlyError.IsAuditFailure(ex)) { DesktopDiagnostics.Record(ex, "OperationalAudit", "AUDIT_WRITE_FAILED", DesktopDiagnosticSeverity.Warning); }
    }

}
