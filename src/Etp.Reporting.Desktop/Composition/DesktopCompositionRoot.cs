extern alias EtpApplication;

using System.IO;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.Dashboard;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Desktop.Modules.Accounting;
using Etp.Reporting.Desktop.Modules.Archive;
using Etp.Reporting.Desktop.Modules.Registers;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.SourceInbox;
using Etp.Reporting.Desktop.Modules.DailyWorkflow;
using Etp.Reporting.Desktop.Modules.HealthCheck;
// 1.11.0 self-diagnostics: the fingerprint lane (L2) owns Etp.Reporting.Desktop.Diagnostics; the stores (L3),
// DB/SEC probes and KnownSqlBuilds (L4) and import/data probes (L6) live in Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
// the runner (L4) in the Application; report (L5) and operations (L7) probes in Desktop.Modules.HealthCheck.
using Etp.Reporting.Desktop.Diagnostics;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;
using EtpApplication::Etp.Reporting.Application.HealthCheck;
using DashboardQuery = EtpApplication::Etp.Reporting.Application.Dashboard.IDashboardQuery;
using AccessSessionQuery = EtpApplication::Etp.Reporting.Application.Access.IAccessSessionQuery;
using ReportArchiveQuery = EtpApplication::Etp.Reporting.Application.Archive.IReportArchiveQuery<Etp.Reporting.Reporting.ReportPackDocument>;
using DigitalRegisterService = EtpApplication::Etp.Reporting.Application.Registers.IDigitalRegisterService;
using SharingContactsService = EtpApplication::Etp.Reporting.Application.Sharing.ISharingContactsService;
using DailyWorkflowQuery = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyWorkflowQuery;
using DailyWorkflowCommands = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyWorkflowCommands;
using DailyReportPackGenerator = EtpApplication::Etp.Reporting.Application.DailyWorkflow.IDailyReportPackGenerator<Etp.Reporting.Reporting.ReportPackDocument>;
using SourceInboxService = EtpApplication::Etp.Reporting.Application.SourceInbox.ISourceInboxService;
using ControlledReportQuery = EtpApplication::Etp.Reporting.Application.Reports.IControlledReportQuery;
using OperationalReportQuery = EtpApplication::Etp.Reporting.Application.Reports.IOperationalReportQuery<Etp.Reporting.Reporting.DailySalesReportDocument>;
using ManagementTrendQuery = EtpApplication::Etp.Reporting.Application.Reports.IManagementTrendQuery;
using AccountingService = EtpApplication::Etp.Reporting.Application.Accounting.IAccountingService;
using OperationsAdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IOperationsAdministrationService;
using AdministrationService = EtpApplication::Etp.Reporting.Application.OperationsAdministration.IAdministrationService;
using ImportPersistenceUseCase = EtpApplication::Etp.Reporting.Application.Imports.IImportPersistenceUseCase<Etp.Reporting.Import.Preflight.MatchedImportEnvelope>;
using DatabaseLifecycleService = EtpApplication::Etp.Reporting.Application.DatabaseLifecycle.IDatabaseLifecycleService;
using TenderVarianceDiagnostic = EtpApplication::Etp.Reporting.Application.Reports.ITenderVarianceDiagnostic;
using InvestigationQuery = EtpApplication::Etp.Reporting.Application.Distribution.IInvestigationQuery;
using ReportDistributionService = EtpApplication::Etp.Reporting.Application.Distribution.IReportDistributionService<Etp.Reporting.Reporting.ReportPackDocument>;

namespace Etp.Reporting.Desktop.Composition;

/// <summary>
/// Owns construction of the executable's concrete adapters and top-level WPF objects.
/// Feature views and view models must receive their dependencies from this boundary.
/// </summary>
public sealed class DesktopCompositionRoot
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

    public static DesktopCompositionRoot CreateForArguments(IReadOnlyList<string> arguments)
    {
        var index = arguments.ToList().FindIndex(value => value.Equals("--connection-string", StringComparison.OrdinalIgnoreCase));
        if (index < 0) return CreateDefault();
        if (index + 1 >= arguments.Count) throw new ArgumentException("Provide a connection string after --connection-string.");
        var validation = ConnectionStringValidation.Validate(arguments[index + 1]);
        if (!validation.IsValid) throw new ArgumentException(validation.Error);
        return new(AppContext.BaseDirectory, validation.ConnectionString!, temporaryConnection: true);
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
        Func<string, DashboardQuery> dashboardQueryFactory = value => new SqlServerDashboardQuery(value);
        Func<string, AccessSessionQuery> accessSessionQueryFactory = value => new SqlServerAccessSessionQuery(value);
        Func<string, ReportArchiveQuery> reportArchiveQueryFactory = value => new SqlServerReportArchiveQuery(value);
        Func<string, DigitalRegisterService> digitalRegisterServiceFactory = value => new SqlServerDigitalRegisterService(value);
        Func<string, SharingContactsService> sharingContactsServiceFactory = value => new SqlServerSharingContactsService(value);
        Func<string, DailyWorkflowQuery> dailyWorkflowQueryFactory = value => new SqlServerDailyWorkflowService(value);
        Func<string, DailyWorkflowCommands> dailyWorkflowCommandsFactory = value => new SqlServerDailyWorkflowService(value);
        Func<string, DailyReportPackGenerator> dailyReportPackGeneratorFactory = value => new SqlServerDailyWorkflowService(value);
        Func<string, SourceInboxService> sourceInboxServiceFactory = value => new SqlServerSourceInboxService(value);
        Func<string, ControlledReportQuery> controlledReportQueryFactory = value => new SqlServerApplicationReportQuery(value);
        Func<string, OperationalReportQuery> operationalReportQueryFactory = value => new SqlServerApplicationReportQuery(value);
        Func<string, ManagementTrendQuery> managementTrendQueryFactory = value => new SqlServerApplicationReportQuery(value);
        Func<string, AccountingService> accountingServiceFactory = value => new SqlServerAccountingService(value);
        Func<string, OperationsAdministrationService> operationsAdministrationServiceFactory = value => new SqlServerOperationsAdministrationService(value, ImportFailureDiagnostics.Record, ReportPackFailureDiagnostics.Record);
        Func<string, AdministrationService> administrationServiceFactory = value => new SqlServerAdministrationService(value);
        Func<string, ImportPersistenceUseCase> importPersistenceUseCaseFactory = value => new SqlServerImportPersistenceUseCase(value);
        Func<string, DatabaseLifecycleService> databaseLifecycleServiceFactory = value => new SqlServerDatabaseLifecycleService(value);
        var settingsWorkspaceView = new SettingsWorkspaceView(
            new DesktopSettingsPresentationSession(settingsStore, connectionState, temporaryConnection),
            databaseLifecycleServiceFactory,
            administrationServiceFactory,
            MigrationDirectory,
            accountingServiceFactory);
        TenderVarianceDiagnostic tenderVarianceDiagnostic = new ReportingTenderVarianceDiagnostic();
        var reportsWorkspaceView = new ReportsWorkspaceView(
            () => connectionState.ConnectionString,
            controlledReportQueryFactory,
            operationalReportQueryFactory,
            managementTrendQueryFactory,
            reportExportCoordinator,
            tenderVarianceDiagnostic);
        var dailyWorkflowWorkspaceView = new DailyWorkflowWorkspaceView(
            new DailyWorkflowPresentationSession(),
            () => connectionState.ConnectionString,
            dailyWorkflowQueryFactory,
            dailyWorkflowCommandsFactory,
            dailyReportPackGeneratorFactory,
            static () => new(false, false, false),
            static (_, _, _) => Task.CompletedTask,
            (path, document) => reportExportCoordinator.ExportPackExcelAsync(path, document),
            (path, document) => reportExportCoordinator.ExportPackPdfAsync(path, document));
        Func<string, InvestigationQuery> investigationQueryFactory = value => new SqlServerInvestigationQuery(value);
        Func<string, ReportDistributionService> reportDistributionServiceFactory = value => new SqlServerReportDistributionService(value);
        var archiveSession = new ArchiveDistributionPresentationSession(
            reportArchiveQueryFactory, sharingContactsServiceFactory, reportDistributionServiceFactory);
        var archiveWorkspaceView = new ArchiveWorkspaceView(
            archiveSession,
            () => connectionState.ConnectionString,
            (path, document) => reportExportCoordinator.ExportPackExcelAsync(path, document),
            (path, document) => reportExportCoordinator.ExportPackPdfAsync(path, document),
            new ArchiveShareLauncher());
        var registersSession = new RegistersPresentationSession(digitalRegisterServiceFactory);
        var registersWorkspaceView = new RegistersWorkspaceView(
            registersSession, () => connectionState.ConnectionString);
        var sourceInboxWorkspaceView = new SourceInboxWorkspaceView(
            sourceInboxServiceFactory,
            () => connectionState.ConnectionString,
            new SourceDocumentLauncher());
        var accountingSession = new AccountingPresentationSession(accountingServiceFactory);
        var accountingWorkspaceView = new AccountingWorkspaceView(
            accountingSession, () => connectionState.ConnectionString);
        var operationsAdministrationSession = new OperationsAdministrationPresentationSession();
        var operationsWorkspaceView = new OperationsWorkspaceView(
            operationsAdministrationSession,
            () => connectionState.ConnectionString,
            operationsAdministrationServiceFactory,
            async (script, cancellationToken) =>
            {
                var result = await PowerShellOperationsService.RunAsync(script, connectionState.ConnectionString, cancellationToken);
                return new MaintenanceOperationResult(result.Succeeded, result.Message);
            });
        var investigationWorkspaceView = new InvestigationApprovalsWorkspaceView(
            () => connectionState.ConnectionString,
            operationsAdministrationServiceFactory,
            investigationQueryFactory);
        var administrationWorkspaceView = new AdministrationWorkspaceView(
            operationsAdministrationSession,
            () => connectionState.ConnectionString,
            administrationServiceFactory);
        var importCoordinator = new DesktopImportCoordinator(importPersistenceUseCaseFactory,
            reportImportFailure: ImportFailureDiagnostics.Record);
        var importWorkspaceView = new ImportWorkspaceView(
            importCoordinator,
            () => connectionState.ConnectionString);
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
            operationsWorkspaceView,
            investigationWorkspaceView,
            administrationWorkspaceView,
            databaseLifecycleServiceFactory,
            importWorkspaceView);
        window.importHistoryView = new ImportHistoryView(scope =>
            new SqlServerImportHistoryQuery(connectionState.ConnectionString).LoadAsync(scope));
        window.serviceExcelExport = (path, metadata, data) =>
            reportExportCoordinator.ExportReportExcelAsync(path, metadata, data, null);
        window.serviceReportQuery = () => new SqlServerServiceReportQuery(connectionState.ConnectionString);
        window.dailyReadinessQuery = () => new SqlServerDailyReadinessQuery(connectionState.ConnectionString);
        // 1.11.0: Settings > Database > Health check and Error register (Owner only; L8 screens).
        DiagnosticsScreens.Services = CreateDiagnosticsScreenServices(() => connectionState.ConnectionString);
        dailyWorkflowWorkspaceView.AttachRegisters((store, date, token) =>
            SqlServerDigitalRegisterService.LoadDayAsync(connectionState.ConnectionString, store, date, token));
        return window;
    }

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

    private async Task<int> RunWatchFolderAutomationAsync(CancellationToken cancellationToken)
    {
        var result = await new SqlServerOperationsAdministrationService(LoadAutomationConnectionString(), ImportFailureDiagnostics.Record, ReportPackFailureDiagnostics.Record)
            .RunAutomationOnceAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.SourcesFailed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ 1.11.0 self-diagnostics (lane L9)
    // Every probe, both diag stores, the runner and the export writer are built here and nowhere else.

    /// <summary>The stderr prefix of a nightly health run that could not complete (199-STDERR-FORMAT.md style).</summary>
    internal const string HealthRunFailedPrefix = "ETP-HEALTH-RUN-FAILED";

    /// <summary>The nightly schedule for the automation account's database target. Nothing is read before 02:30.</summary>
    internal NightlyHealthSchedule CreateNightlyHealthSchedule()
    {
        // Lazy: a tick before 02:30 must not even read the protected configuration.
        var target = new Lazy<string>(LoadAutomationConnectionString);
        return new NightlyHealthSchedule(
            async token => (await new SqlServerHealthCheckStore(target.Value).LoadLatestNightlySummaryAsync(token).ConfigureAwait(false))?.BusinessDate,
            token => NightlyHealthLease.TryAcquireAsync(target.Value, token),
            async token => await RunHealthCheckAsync(target.Value, HealthCheckTrigger.Nightly, null, token).ConfigureAwait(false),
            new FileNightlyHealthAttemptStore(FileNightlyHealthAttemptStore.DefaultPath),
            TimeProvider.System,
            ReportHealthRunFailure);
    }

    /// <summary>
    /// The services of the two Owner-only screens (L8: Settings &gt; Database &gt; Health check and Error register).
    /// The connection is read when used, so a connection changed in Settings is the one the screens use.
    /// </summary>
    internal DiagnosticsScreenServices CreateDiagnosticsScreenServices(Func<string> connection) => new()
    {
        HealthStore = () => new SqlServerHealthCheckStore(connection()),
        ErrorStore = () => new SqlServerErrorRegisterStore(connection()),
        RunHealthCheck = (progress, token) => RunManualHealthCheckAsync(connection(), progress, token),
        IngestOwnLog = token => IngestOwnLogAsync(connection(), token),
        ExportFolder = () => HealthExportWriter.DefaultDirectory
    };

    /// <summary>
    /// The Owner's "Run health check". Takes the automation lease first, so it never overlaps the nightly run or an
    /// import run, and returns the id of the run it recorded.
    /// </summary>
    public async Task<long> RunManualHealthCheckAsync(string targetConnectionString, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var lease = await NightlyHealthLease.TryAcquireAsync(targetConnectionString, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A health check or an automatic import is already running. Try again in a few minutes.");
        await using (lease.ConfigureAwait(false))
            return (await RunHealthCheckAsync(targetConnectionString, HealthCheckTrigger.Manual, progress, cancellationToken).ConfigureAwait(false)).RunId;
    }

    /// <summary>
    /// One health run (design 5 and 8), called with the automation lease held: applies the fix manifest, ingests this
    /// account's diagnostics log into the register, runs every probe through L4's runner, writes the privacy-checked
    /// export, then records the run with the export's file name. The export is written before the run is recorded, so a
    /// run whose recording fails still leaves its file; the recording failure then propagates (HEALTH_RUN_FAILED).
    /// </summary>
    internal async Task<(HealthCheckRun Run, long RunId)> RunHealthCheckAsync(
        string targetConnectionString, HealthCheckTrigger trigger, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var healthStore = new SqlServerHealthCheckStore(targetConnectionString);
        var registerStore = new SqlServerErrorRegisterStore(targetConnectionString);
        var appVersion = HealthApplicationVersion();
        var automationPrincipal = TryLoadAutomationPrincipal();

        // Design 4.4 step 2: the first run of a new version marks the fixes it ships. Never throws (L2).
        await ErrorFixManifest.ApplyAsync(registerStore, appVersion, cancellationToken: cancellationToken).ConfigureAwait(false);
        // Design 4.3: this account's log (nightly: the automation account's, which no one else can read).
        await BestEffortAsync(() => CreateIngest(registerStore).RunAsync(cancellationToken), "HEALTH_INGEST_FAILED").ConfigureAwait(false);

        var runner = new HealthCheckRunner(
            CreateHealthProbes(targetConnectionString, MigrationDirectory, healthStore, automationPrincipal),
            new HealthCheckRunnerOptions
            {
                Store = healthStore,
                LoadRetailStoreCodes = async token => await new StoreCatalogRepository(targetConnectionString).ActiveCodesAsync(token).ConfigureAwait(false),
                AppVersion = appVersion,
                MachineName = Environment.MachineName,
                RunByRole = trigger == HealthCheckTrigger.Nightly ? "Automation" : "Owner",
                ProbeFailed = static (checkId, exception) => DesktopDiagnostics.Record(exception, "HealthCheck.Probe", "HEALTH_PROBE_FAILED"),
                Progress = progress is null ? null : new AreaProgress(progress)
            });
        var run = await runner.RunAsync(trigger, cancellationToken).ConfigureAwait(false);

        // The latest recorded run of this trigger is still the previous one: this run is recorded below.
        var previousRun = await BestEffortAsync(() => LoadPreviousHealthRunAsync(healthStore, trigger, cancellationToken), "HEALTH_PREVIOUS_RUN_FAILED").ConfigureAwait(false);
        var register = await BestEffortAsync(() => registerStore.LoadRegisterAsync(cancellationToken: cancellationToken), "HEALTH_REGISTER_LOAD_FAILED").ConfigureAwait(false) ?? [];
        var nightlyRuns = await BestEffortAsync(() => healthStore.LoadRunsAsync(30, HealthCheckTrigger.Nightly, cancellationToken), "HEALTH_RUNS_LOAD_FAILED").ConfigureAwait(false) ?? [];
        // Occurrence details (sample Refs, contexts) are Owner-only (diag.load_error_entry): manual runs only.
        IReadOnlyDictionary<int, ErrorRegisterEntryDetail>? details = trigger == HealthCheckTrigger.Manual
            ? await BestEffortAsync(() => LoadRegisterDetailsAsync(registerStore, register, cancellationToken), "HEALTH_REGISTER_DETAIL_FAILED").ConfigureAwait(false)
            : null;
        IReadOnlyDictionary<string, int>? warnings = null;
        try { warnings = new DiagnosticsLogReader().CountWarnings(run.StartedUtc.AddDays(-1)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }

        var writer = new HealthExportWriter(HealthExportWriter.DefaultDirectory, []);
        var input = new HealthExportInput(run, previousRun, register, details, nightlyRuns, warnings);
        run = HealthExportWriter.WithRedactionWarning(run, writer.CountRedactions(input));
        var export = writer.Write(input with { Run = run });
        if (export.FailureCode is not null)
            DesktopDiagnostics.Record(null, "HealthCheck.Export", "HEALTH_EXPORT_FAILED:" + export.FailureCode);

        run = run with { ExportFileName = export.FileName };
        var runId = await healthStore.RecordRunAsync(run, cancellationToken).ConfigureAwait(false);
        return (run, runId);
    }

    /// <summary>
    /// The health probes (design 5.2). The run order is the runner's (design 5.3); probes of one area run in this order.
    /// ERR-01..04 have no probe yet (not assigned in section 14): the runner reports them Skipped (NOT_AVAILABLE).
    /// </summary>
    internal static IReadOnlyList<IHealthProbe> CreateHealthProbes(
        string targetConnectionString, string migrationDirectory, IHealthCheckStore healthStore, string? automationPrincipal) =>
    [
        .. DatabaseProbes.Create(targetConnectionString, migrationDirectory),                                   // L4: DB-01..05
        new SecurityProbes(healthStore, automationPrincipal),                                                   // L4: SEC-01..03
        OperationsProbes.CreateDefault(targetConnectionString,                                                  // L7: OPS-01..09
            OperationsProbeSettings.ForThisMachine(automationPrincipal), new DelegateSqlBuildAssessor(AssessSqlBuild)),
        new DataProbes(targetConnectionString),                                                                 // L6: DATA-01..05
        new ImportProbes(targetConnectionString),                                                               // L6: IMP-01..03
        ReportProbes.ForConnection(targetConnectionString)                                                      // L5: RPT-01..22
    ];

    /// <summary>OPS-06 (L7) judged by L4's <c>KnownSqlBuilds</c>, the one table of recommended builds.</summary>
    internal static SqlBuildVerdict AssessSqlBuild(SqlServerBuildInfo build)
    {
        var assessment = KnownSqlBuilds.Assess(build.ProductVersion, build.UpdateLevel);
        return assessment.FindingCode switch
        {
            "SQL_RTM" => SqlBuildVerdict.Rtm,
            "SQL_UNSUPPORTED" => SqlBuildVerdict.UnsupportedMajor,
            _ when assessment.Outcome == HealthOutcome.Pass => SqlBuildVerdict.AtOrAboveRecommended,
            // SQL_BELOW_RECOMMENDED, and a version that is unreadable or newer than ETP was tested on: a Warning.
            _ => SqlBuildVerdict.BelowRecommended
        };
    }

    private ErrorRegisterIngest CreateIngest(IErrorRegisterStore registerStore) =>
        new(registerStore, HealthApplicationVersion(), loadKnownEntries: token => registerStore.LoadRegisterAsync(null, null, token));

    // Error register > Refresh: the Owner's own log. Null when nothing could be sent (failed, or already running).
    private async Task<ErrorIngestResult?> IngestOwnLogAsync(string targetConnectionString, CancellationToken cancellationToken)
    {
        var result = await CreateIngest(new SqlServerErrorRegisterStore(targetConnectionString)).RunAsync(cancellationToken).ConfigureAwait(false);
        return result.Failed || result.AlreadyRunning
            ? null
            : new ErrorIngestResult(result.Accepted, result.Duplicates, result.Rejected, result.NewEntries, result.RecurredEntries);
    }

    private static async Task<HealthCheckPreviousRun?> LoadPreviousHealthRunAsync(IHealthCheckStore store, HealthCheckTrigger trigger, CancellationToken cancellationToken)
    {
        var latest = (await store.LoadRunsAsync(1, trigger, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (latest is null) return null;
        return new(latest.HealthCheckRunId, latest.StartedUtc, await store.LoadResultsAsync(latest.HealthCheckRunId, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IReadOnlyDictionary<int, ErrorRegisterEntryDetail>> LoadRegisterDetailsAsync(
        IErrorRegisterStore store, IReadOnlyList<ErrorRegisterEntry> register, CancellationToken cancellationToken)
    {
        var details = new Dictionary<int, ErrorRegisterEntryDetail>();
        foreach (var entry in HealthExportWriter.SelectRegister(register, DateTimeOffset.UtcNow).Take(50))
            if (await store.LoadEntryAsync(entry.ErrorRegisterId, cancellationToken).ConfigureAwait(false) is { } detail)
                details[entry.ErrorRegisterId] = detail;
        return details;
    }

    // A failed read is logged and the run goes on without it; cancellation is not swallowed.
    private static async Task<T?> BestEffortAsync<T>(Func<Task<T>> read, string eventId)
    {
        try { return await read().ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "HealthCheck.Run", eventId, DesktopDiagnosticSeverity.Warning);
            return default;
        }
    }

    /// <summary>The runner's progress as the screen's line: "Reports 7 of 51".</summary>
    private sealed class AreaProgress(IProgress<string> target) : IProgress<HealthCheckProgress>
    {
        public void Report(HealthCheckProgress value) =>
            target.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{value.Area} {value.CompletedChecks} of {value.TotalChecks}"));
    }

    /// <summary>The four-part version recorded with the run, as the diagnostics log records it (for example 1.11.0.0).</summary>
    internal static string HealthApplicationVersion() =>
        typeof(DesktopCompositionRoot).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>The automation account named in the protected configuration (SEC-02, OPS-09's 18456 filter), or null.</summary>
    internal static string? TryLoadAutomationPrincipal()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Operations", "operations.json");
            if (!File.Exists(path)) return null;
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("automationPrincipal", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }

    /// <summary>
    /// A nightly run that could not complete: one diagnostics entry (<c>HEALTH_RUN_FAILED</c>, ingested into the
    /// register next time) and one stderr line for whoever reads the task's output. Never throws.
    /// </summary>
    internal static void ReportHealthRunFailure(Exception exception)
    {
        DesktopDiagnostics.Record(exception, "HealthCheck.Nightly", "HEALTH_RUN_FAILED");
        try
        {
            Console.Error.WriteLine(HealthRunFailedLine(exception));
            Console.Error.Flush();
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary><c>ETP-HEALTH-RUN-FAILED mode=automation-once kind=&lt;kind&gt; sql=&lt;numbers|none&gt;: &lt;reason&gt;</c>.</summary>
    internal static string HealthRunFailedLine(Exception exception) =>
        HealthRunFailedPrefix + StartupFailureText.HeadlessLine(
            StartupFailureText.ModeToken(DesktopStartupMode.AutomationOnce), exception)[StartupFailureText.HeadlessPrefix.Length..];
}
