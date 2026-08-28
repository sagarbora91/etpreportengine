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
    private readonly ShellViewModel shell;
    private readonly DashboardView dashboardView;
    private readonly Func<string, DashboardQuery> dashboardQueryFactory;
    private readonly SettingsWorkspaceView settingsWorkspace;
    private readonly DesktopConnectionState connectionState;
    private readonly Func<string, AccessSessionQuery> accessSessionQueryFactory;
    private readonly ArchiveWorkspaceView archiveWorkspaceView;
    private readonly RegistersWorkspaceView registersWorkspaceView;
    private readonly DailyWorkflowWorkspaceView dailyWorkflowWorkspace;
    private readonly SourceInboxWorkspaceView sourceInboxWorkspaceView;
    private readonly ReportsWorkspaceView reportsWorkspaceView;
    private readonly AccountingWorkspaceView accountingWorkspaceView;
    private readonly OperationsWorkspaceView operationsWorkspaceView;
    private readonly InvestigationApprovalsWorkspaceView investigationWorkspaceView;
    private readonly AdministrationWorkspaceView administrationWorkspaceView;
    private readonly Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory;
    private readonly ImportWorkspaceView importWorkspaceView;
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
        InitializeComponent();
        DailyWorkflowPanel.Content = dailyWorkflowWorkspace;
        dailyWorkflowWorkspace.AttachHost(
            () => new(currentAccess.CanView, currentAccess.CanImport, currentAccess.CanAdminister),
            RecordAuditAsync,
            RefreshDashboardAsync);
        dailyWorkflowWorkspace.NotificationRequested += (_, args) => ApplicationStatus.Text = args.Message;
        SettingsPanel.Content = settingsWorkspace;
        settingsWorkspace.ConnectionPresentationChanged += SettingsWorkspace_ConnectionPresentationChanged;
        settingsWorkspace.OperationCompletedAsync = SettingsWorkspace_OperationCompletedAsync;
        DashboardHost.Content = dashboardView;
        ReportArchiveHost.Content = archiveWorkspaceView;
        archiveWorkspaceView.AttachHost(() => currentAccess, RecordAuditAsync, DesktopFriendlyError.Describe);
        archiveWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        RegistersHost.Content = registersWorkspaceView;
        registersWorkspaceView.AttachHost(() => currentAccess, DesktopFriendlyError.Describe);
        registersWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        SourceInboxHost.Content = sourceInboxWorkspaceView;
        sourceInboxWorkspaceView.AttachHost(() => currentAccess, DesktopFriendlyError.Describe);
        sourceInboxWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        sourceInboxWorkspaceView.SelectedDocumentIdChanged += (_, documentId) => registersWorkspaceView.LinkedSourceDocumentId = documentId;
        ReportsHost.Content = reportsWorkspaceView;
        reportsWorkspaceView.AttachHost(
            ShowFocusedReportWorkspace,
            RecordAuditAsync,
            (snapshot, rows, status) =>
            {
                if (focusedWorkspaceKind == "report") reportWorkspaceSession.UpdatePreview(snapshot, rows, status);
            },
            message => reportWorkspaceSession.ShowDailySalesFailure(message),
            row => OpenDrawer("Report row details", "Source evidence and technical lineage remain available without leaving the report workspace.", row));
        ImportHost.Content = importWorkspaceView;
        importWorkspaceView.AttachHost(
            () => new(currentAccess.CanImport, currentAccess.CanAdminister),
            RecordAuditAsync,
            RefreshDashboardAsync);
        importWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        importWorkspaceView.ReadinessChanged += (_, status) =>
        {
            ImportStatus.Text = status;
            AutomationProperties.SetName(ImportStatus, $"Import readiness status: {status}");
        };
        AccountingHost.Content = accountingWorkspaceView;
        accountingWorkspaceView.AttachHost(() => currentAccess, DesktopFriendlyError.Describe);
        accountingWorkspaceView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        OperationsHost.Content = operationsWorkspaceView;
        operationsWorkspaceView.DashboardRefreshRequestedAsync = RefreshDashboardAsync;
        operationsWorkspaceView.AuditRequestedAsync = RecordAuditAsync;
        InvestigationHost.Content = investigationWorkspaceView;
        AdministrationHost.Content = administrationWorkspaceView;
        administrationWorkspaceView.AccessChangedAsync = RefreshAccessAsync;
        dashboardView.RefreshRequested += async (_, _) => await RefreshDashboardAsync();
        dashboardView.ExportDateFrom = () => reportsWorkspaceView.DateFrom is { } from ? DateOnly.FromDateTime(from) : DateOnly.FromDateTime(DateTime.Today);
        dashboardView.ExportDateTo = () => reportsWorkspaceView.DateTo is { } to ? DateOnly.FromDateTime(to) : DateOnly.FromDateTime(DateTime.Today);
        dashboardView.NotificationRequested += (_, message) => ApplicationStatus.Text = message;
        InitializeShell();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        settingsWorkspace.Initialize();
        await RefreshAccessAsync();
        CompleteWelcomeState();
        await settingsWorkspace.CheckConnectionAsync(false);
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
        if (destination is "Settings" or "Admin / Settings")
            _ = settingsWorkspace.PrepareForDisplayAsync(destination == "Admin / Settings");
        DailyWorkflowPanel.Visibility = destination is "Daily Workflow" or "Manual Entry" ? Visibility.Visible : Visibility.Collapsed;
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
        if (destination == "Dashboard") _ = RefreshDashboardAsync();
        if (destination is "Daily Workflow" or "Manual Entry") _ = dailyWorkflowWorkspace.RefreshAsync();
        if (destination == "Import ETP") _ = sourceInboxWorkspaceView.RefreshAsync();
        if (destination == "Registers") _ = registersWorkspaceView.RefreshAsync();
        if (destination == "Accounting") _ = accountingWorkspaceView.RefreshAsync();
        if (destination == "Operations Center") { _ = operationsWorkspaceView.RefreshAsync(); _ = investigationWorkspaceView.RefreshApprovalsAsync(); }
        if (destination == "Report Archive") _ = archiveWorkspaceView.RefreshAsync();
        if (destination is "Masters" or "Admin / Settings") _ = administrationWorkspaceView.RefreshAsync();
        if (destination is "Daily Workflow" or "Manual Entry")
            dailyWorkflowWorkspace.PrepareForDisplay(destination == "Manual Entry");
    }

    private async Task RefreshAccessAsync()
    {
        try
        {
            currentAccess = await accessSessionQueryFactory(connectionState.ConnectionString).LoadCurrentAsync();
            settingsWorkspace.UpdateAccess(new(currentAccess.Role != AccessRole.None, currentAccess.CanAdminister));
            UpdateOperationsAdministrationAccess();
            dailyWorkflowWorkspace.RefreshAccessState();
            AccessStatus.Text = $"{currentAccess.DisplayName} — {RoleLabel(currentAccess.Role)}";
            AccessStatus.Foreground = currentAccess.CanView ? Brushes.SeaGreen : Brushes.Firebrick;
            if (PageTitle.Text is "Dashboard" or "Home") DashboardPanel.Visibility = currentAccess.CanView ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) when (DesktopFriendlyError.IsDatabaseAvailabilityFailure(ex))
        {
            currentAccess = new("unknown", "Access not initialized", AccessRole.None, false);
            settingsWorkspace.UpdateAccess(new(currentAccess.Role != AccessRole.None, currentAccess.CanAdminister));
            UpdateOperationsAdministrationAccess();
            dailyWorkflowWorkspace.RefreshAccessState();
            AccessStatus.Text = "Access: initialize database";
            AccessStatus.Foreground = Brushes.DarkOrange;
            if (PageTitle.Text is "Dashboard" or "Home") DashboardPanel.Visibility = Visibility.Collapsed;
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
        ConnectionStatus.Text = state.ConnectionStatus;
        ConnectionStatus.Foreground = state.IsConnected ? System.Windows.Media.Brushes.SeaGreen : System.Windows.Media.Brushes.DarkOrange;
        ImportStatus.Text = state.ImportStatus;
        ApplicationStatus.Text = state.ApplicationStatus;
        AutomationProperties.SetName(ConnectionStatus, $"Database connection status: {ConnectionStatus.Text}");
        AutomationProperties.SetName(ImportStatus, $"Import readiness status: {ImportStatus.Text}");
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
                    if (currentAccess.CanView) await RefreshDashboardAsync();
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

    private async Task RefreshDashboardAsync()
    {
        try
        {
            var snapshot = await dashboardQueryFactory(connectionState.ConnectionString).LoadAsync();
            dashboardView.Show(snapshot);
        }
        catch (Exception ex)
        {
            dashboardView.ShowError(ex.Message);
            ApplicationStatus.Text = $"Dashboard refresh failed: {ex.Message}";
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
        catch (Exception ex) when (DesktopFriendlyError.IsAuditFailure(ex)) { }
    }

}
