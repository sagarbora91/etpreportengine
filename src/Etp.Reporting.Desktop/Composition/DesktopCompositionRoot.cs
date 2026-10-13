extern alias EtpApplication;

using System.IO;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Desktop.Modules.Dashboard;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.Archive;
using Etp.Reporting.Desktop.Modules.Registers;
using Etp.Reporting.Desktop.Modules.SourceInbox;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.HealthCheck;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using DashboardQuery = EtpApplication::Etp.Reporting.Application.Dashboard.IDashboardQuery;
using AccessSessionQuery = EtpApplication::Etp.Reporting.Application.Access.IAccessSessionQuery;
using AdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IAdministrationService;
using AccountingService = EtpApplication::Etp.Reporting.Application.Accounting.IAccountingService;
using DatabaseLifecycleService = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.IDatabaseLifecycleService;

namespace Etp.Reporting.Desktop.Composition;

/// <summary>
/// Owns construction of the executable's concrete adapters and top-level WPF objects.
/// Feature views and view models must receive their dependencies from this boundary.
/// <para>
/// 1.12 (A1): this file is the core part. Each area builds its screens in its own partial file
/// (<c>.Imports</c>, <c>.Reports</c>, <c>.Accounting</c>, <c>.Operations</c>), and every wave-2 lane adds one partial file
/// that implements its <c>Register*</c> hook below. Screens receive services (built here from the connection factory),
/// never a connection string; <c>ArchitectureGuardTests</c> keeps it that way.
/// </para>
/// </summary>
public sealed partial class DesktopCompositionRoot
{
    public const string DefaultConnectionString =
        @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;Encrypt=Optional;Connect Timeout=5";

    private readonly string baseDirectory;
    private readonly string connectionString;
    private readonly string settingsDirectory;
    private readonly bool temporaryConnection;

    public DesktopCompositionRoot(
        string baseDirectory,
        string connectionString,
        string? settingsDirectory = null,
        bool temporaryConnection = false)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentException("The application base directory is required.", nameof(baseDirectory));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
        if (settingsDirectory is not null && string.IsNullOrWhiteSpace(settingsDirectory))
            throw new ArgumentException("The settings directory cannot be blank.", nameof(settingsDirectory));

        this.baseDirectory = Path.GetFullPath(baseDirectory);
        this.connectionString = connectionString;
        this.temporaryConnection = temporaryConnection;
        this.settingsDirectory = Path.GetFullPath(settingsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EtpReporting"));
    }

    public string MigrationDirectory => Path.Combine(baseDirectory, "database", "migrations");
    public string SettingsDirectory => settingsDirectory;

    public static DesktopCompositionRoot CreateDefault() =>
        new(AppContext.BaseDirectory, DefaultConnectionString);

    /// <summary>
    /// The start of every process (App.OnStartup). The endpoint policy is applied first, so a refused
    /// <c>database.endpointPolicy</c> stops the start with STARTUP_CONFIGURATION_REJECTED before anything connects.
    /// </summary>
    public static DesktopCompositionRoot CreateForArguments(IReadOnlyList<string> arguments)
    {
        var root = CreateForArgumentsCore(arguments);
        root.ApplyEndpointPolicy();
        return root;
    }

    private static DesktopCompositionRoot CreateForArgumentsCore(IReadOnlyList<string> arguments)
    {
        var index = arguments.ToList().FindIndex(value => value.Equals("--connection-string", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return CreateDefault();
        if (index + 1 >= arguments.Count) throw new ArgumentException("Provide a connection string after --connection-string.");
        var validation = ConnectionStringValidation.Validate(arguments[index + 1]);
        if (!validation.IsValid) throw new ArgumentException(validation.Error);
        return new(AppContext.BaseDirectory, validation.ConnectionString!, temporaryConnection: true);
    }

    /// <summary>
    /// <c>settings.json</c> → <c>database.endpointPolicy</c> (1.12 A1). LocalOnly (or no value) keeps the process default;
    /// any other value is refused: "This ETP build allows only this PC's SQL Server." An unreadable settings file is
    /// LocalOnly, as it always was. Safe to call more than once in a process (tests build several roots).
    /// </summary>
    internal void ApplyEndpointPolicy()
    {
        string? configured;
        try { configured = new DesktopSettingsStore(settingsDirectory).LoadEndpointPolicyName(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException) { configured = null; }
        var policy = EtpSqlEndpoints.FromSetting(configured);
        if (!string.Equals(EtpSqlEndpoints.Policy.Name, policy.Name, StringComparison.Ordinal)) EtpSqlEndpoints.Configure(policy);
    }

    public MainWindow CreateMainWindow()
    {
        var shell = new ShellViewModel(new ShellNavigationService());
        IReportExportCoordinator reportExportCoordinator = new ReportExportCoordinator();
        var dashboardView = new DashboardView(
            new DashboardPresentationSession(),
            (path, metadata, data) => reportExportCoordinator.ExportManagementSummaryPdfAsync(path, metadata, data));
        var settingsStore = new DesktopSettingsStore(settingsDirectory);
        var connectionState = new DesktopConnectionState(connectionString);
        // The connection Settings may change while the window is open: every service below reads it when it is built.
        string Current() => connectionState.ConnectionString;
        Func<string, DashboardQuery> dashboardQueryFactory = value => new SqlServerDashboardQuery(value);
        Func<string, AccessSessionQuery> accessSessionQueryFactory = value => new SqlServerAccessSessionQuery(value);
        Func<string, AccountingService> accountingServiceFactory = value => new SqlServerAccountingService(value);
        Func<string, AdministrationService> administrationServiceFactory = value => new SqlServerAdministrationService(value);
        Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory = value => new SqlServerDatabaseLifecycleService(value);
        // Settings is the connection editor, so it keeps its string-keyed factories (Settings and MainWindow are on the
        // ArchitectureGuardTests allowlist until their owning lanes move them).
        var settingsWorkspaceView = new SettingsWorkspaceView(
            new DesktopSettingsPresentationSession(settingsStore, connectionState, temporaryConnection),
            databaseLifecycleServiceFactory,
            administrationServiceFactory,
            MigrationDirectory,
            accountingServiceFactory);
        // 1.11.2 (AUDIT-2 S-07): the Owner-only "Show customer names in shared packs" box, default off.
        settingsWorkspaceView.SharePackCustomerNames = new(
            (connection, token) => new DailyReportingPackService(connection).LoadSharePackCustomerNamesAsync(token),
            (connection, include, reason, token) => new DailyReportingPackService(connection).SetSharePackCustomerNamesAsync(include, reason, token));
        var reportsWorkspaceView = CreateReportsWorkspaceView(Current, reportExportCoordinator);
        var dailyWorkflowWorkspaceView = new DailyWorkflowWorkspaceView(
            new DailyWorkflowPresentationSession(),
            () => new SqlServerDailyWorkflowService(Current()),
            () => new SqlServerDailyWorkflowService(Current()),
            () => new SqlServerDailyWorkflowService(Current()),
            static () => new(false, false, false),
            static (_, _, _) => Task.CompletedTask,
            (path, document) => reportExportCoordinator.ExportPackExcelAsync(path, document),
            (path, document) => reportExportCoordinator.ExportPackPdfAsync(path, document));
        dailyWorkflowWorkspaceView.BrandStockEntryWindowFactory = (store, date) => new BrandStockEntryWindow(Current(), store, date);
        var archiveSession = new ArchiveDistributionPresentationSession(
            () => new SqlServerReportArchiveQuery(Current()),
            () => new SqlServerSharingContactsService(Current()),
            () => new SqlServerReportDistributionService(Current()));
        var archiveWorkspaceView = new ArchiveWorkspaceView(
            archiveSession,
            (path, document) => reportExportCoordinator.ExportPackExcelAsync(path, document),
            (path, document) => reportExportCoordinator.ExportPackPdfAsync(path, document),
            new ArchiveShareLauncher());
        var registersWorkspaceView = new RegistersWorkspaceView(
            new RegistersPresentationSession(() => new SqlServerDigitalRegisterService(Current())));
        var sourceInboxWorkspaceView = new SourceInboxWorkspaceView(
            () => new SqlServerSourceInboxService(Current()),
            new SourceDocumentLauncher());
        var accountingWorkspaceView = CreateAccountingWorkspaceView(Current);
        var operations = CreateOperationsViews(connectionState);
        var importWorkspaceView = CreateImportWorkspaceView(Current);
        var window = new MainWindow(
            shell,
            dashboardView,
            dashboardQueryFactory,
            settingsWorkspaceView,
            connectionState,
            accessSessionQueryFactory,
            archiveWorkspaceView,
            registersWorkspaceView,
            dailyWorkflowWorkspaceView,
            sourceInboxWorkspaceView,
            reportsWorkspaceView,
            accountingWorkspaceView,
            operations.Operations,
            operations.Investigation,
            operations.Administration,
            databaseLifecycleServiceFactory,
            importWorkspaceView);
        AttachImportHistory(window, Current);
        window.serviceExcelExport = (path, metadata, data) =>
            reportExportCoordinator.ExportReportExcelAsync(path, metadata, data, null);
        window.serviceReportQuery = () => new SqlServerServiceReportQuery(connectionState.ConnectionString);
        window.dailyReadinessQuery = () => new SqlServerDailyReadinessQuery(connectionState.ConnectionString);
        // 1.11.0: Settings > Database > Health check and Error register (Owner only; L8 screens).
        DiagnosticsScreens.Services = CreateDiagnosticsScreenServices(Current);
        // Design 4.3: one ingest after a successful start. The fix manifest is not applied here (Owner/automation only).
        window.startupErrorIngest = token => IngestOwnLogAsync(Current(), token);
        dailyWorkflowWorkspaceView.AttachRegisters((store, date, token) =>
            SqlServerDigitalRegisterService.LoadDayAsync(Current(), store, date, token));
        RegisterScreens(window.Screens, connectionState);
        return window;
    }

    // ------------------------------------------------------------------ 1.12 seams for wave 2
    // Each wave-2 lane implements one hook in its own partial file (DesktopCompositionRoot.<Area>.cs) and registers its
    // screens by task id; U1 adds the task rows. A hook nobody implements is removed by the compiler.

    partial void RegisterMorning(ScreenRegistry registry);
    partial void RegisterUpdates(ScreenRegistry registry);
    partial void RegisterCollections(ScreenRegistry registry);
    partial void RegisterImportFixes(ScreenRegistry registry);
    partial void RegisterService(ScreenRegistry registry);

    /// <summary>
    /// The connection factory for the running window's current connection. Wave-2 registrations read it when a
    /// screen opens, so a connection changed in Settings is the one they use.
    /// </summary>
    private Func<IEtpConnectionFactory> currentConnections = static () => throw new InvalidOperationException("No main window was created.");

    private void RegisterScreens(ScreenRegistry registry, DesktopConnectionState connectionState)
    {
        currentConnections = () => connectionState.Factory;
        RegisterMorning(registry);
        RegisterUpdates(registry);
        RegisterCollections(registry);
        RegisterImportFixes(registry);
        RegisterService(registry);
    }

    /// <summary>The factory of the window's current connection (for the wave-2 <c>Register*</c> hooks).</summary>
    private IEtpConnectionFactory CurrentConnections() => currentConnections();

    public string LoadConnectionString() =>
        new DesktopConnectionState(temporaryConnection ? connectionString : new DesktopSettingsStore(settingsDirectory).Load()?.ConnectionString ?? connectionString)
            .ConnectionString;

    public Task InitializeDatabaseAsync(CancellationToken cancellationToken = default) =>
        InitializeDatabaseTargetAsync(LoadConnectionString(), cancellationToken);

    public Task InitializeConfiguredDatabaseAsync(CancellationToken cancellationToken = default) =>
        InitializeDatabaseTargetAsync(LoadAutomationConnectionString(), cancellationToken);

    private async Task InitializeDatabaseTargetAsync(string targetConnectionString, CancellationToken cancellationToken)
    {
        var migrations = new DirectoryMigrationSource(MigrationDirectory);
        var bootstrapper = new SqlServerDatabaseBootstrapper(targetConnectionString, migrations);
        await bootstrapper.BootstrapAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string LoadAutomationConnectionString()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Operations", "operations.json");
        return LoadOperationsConnectionString(path);
    }

    internal static string LoadOperationsConnectionString(string path)
    {
        ProtectedOperationPath.Validate(path);
        return ParseOperationsConnectionString(File.ReadAllText(path));
    }

    internal static string ParseOperationsConnectionString(string configuration)
    {
        using var document = System.Text.Json.JsonDocument.Parse(configuration);
        if (!document.RootElement.TryGetProperty("serverInstance", out var serverValue) || serverValue.ValueKind != System.Text.Json.JsonValueKind.String ||
            !document.RootElement.TryGetProperty("database", out var databaseValue) || databaseValue.ValueKind != System.Text.Json.JsonValueKind.String)
            throw new InvalidOperationException("Configure the protected machine database target before running unattended operations.");
        var server = serverValue.GetString();
        var database = databaseValue.GetString();
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder { DataSource = server, InitialCatalog = database, IntegratedSecurity = true };
        return LocalSqlConnectionPolicy.Validate(builder.ConnectionString);
    }

    /// <summary>
    /// The five-minute task (<c>--automation-once</c>): the watch-folder import step, then the nightly health check
    /// when it is due (1.11.0, design section 7). The health step runs also when watch-folder import is disabled or
    /// the import step threw, and never changes the exit code (import sources failed = 1).
    /// </summary>
    public Task<int> RunAutomationOnceAsync(CancellationToken cancellationToken = default) =>
        NightlyHealthSchedule.RunAfterAutomationAsync(RunWatchFolderAutomationAsync, CreateNightlyHealthSchedule().RunIfDueAsync, cancellationToken);

    /// <summary>Waits (bounded, transient errors only) for SQL Server before the unattended task starts; one Information line per wait.</summary>
    public Task WaitForAutomationSqlAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = LoadAutomationConnectionString();
        return SqlStartupWait.WaitAsync(
            token => SqlStartupWait.ProbeAsync(connectionString, token),
            Task.Delay,
            () => TimeSpan.FromMilliseconds(Environment.TickCount64),
            line =>
            {
                DesktopDiagnostics.Record(null, "UnattendedAutomation", "STARTUP_SQL_WAIT", DesktopDiagnosticSeverity.Information);
                try { Console.Error.WriteLine("ETP-STARTUP-WAIT mode=automation-once: " + line); } catch (IOException) { } catch (ObjectDisposedException) { }
            },
            cancellationToken: cancellationToken);
    }

    private async Task<int> RunWatchFolderAutomationAsync(CancellationToken cancellationToken)
    {
        var result = await new SqlServerOperationsAdministrationService(LoadAutomationConnectionString(), ImportFailureDiagnostics.Record, ReportPackFailureDiagnostics.Record)
            .RunAutomationOnceAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.SourcesFailed == 0 ? 0 : 1;
    }
}
