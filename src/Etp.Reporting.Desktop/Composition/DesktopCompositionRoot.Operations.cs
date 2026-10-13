extern alias EtpApplication;

using System.IO;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.HealthCheck;
// 1.11.0 self-diagnostics: the fingerprint lane (L2) owns Etp.Reporting.Desktop.Diagnostics; the stores (L3),
// DB/SEC probes and KnownSqlBuilds (L4) and import/data probes (L6) live in Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
// the runner (L4) in the Application; report (L5) and operations (L7) probes in Desktop.Modules.HealthCheck. 1.12 (A1)
// moved the probes' own SQL (the lease, the operations reads) into Infrastructure.SqlServer.HealthCheck as well.
using Etp.Reporting.Desktop.Diagnostics;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;
using EtpApplication::Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.Desktop.Composition;

// Operations: the Operations Centre, Investigation and approvals, Settings administration, the maintenance scripts,
// and the health check (nightly and manual).
public sealed partial class DesktopCompositionRoot
{
    private sealed record OperationsViews(
        OperationsWorkspaceView Operations,
        InvestigationApprovalsWorkspaceView Investigation,
        AdministrationWorkspaceView Administration);

    private static OperationsViews CreateOperationsViews(DesktopConnectionState connectionState)
    {
        string Current() => connectionState.ConnectionString;
        var operationsAdministrationSession = new OperationsAdministrationPresentationSession();
        var operationsWorkspaceView = new OperationsWorkspaceView(
            operationsAdministrationSession,
            () => new SqlServerOperationsAdministrationService(Current(), ImportFailureDiagnostics.Record, ReportPackFailureDiagnostics.Record),
            async (script, cancellationToken) =>
            {
                var result = await PowerShellOperationsService.RunAsync(script, MaintenanceTarget.For(connectionState.Factory), cancellationToken);
                return new MaintenanceOperationResult(result.Succeeded, result.Message);
            });
        var investigationWorkspaceView = new InvestigationApprovalsWorkspaceView(
            () => new SqlServerOperationsAdministrationService(Current(), ImportFailureDiagnostics.Record, ReportPackFailureDiagnostics.Record),
            () => new SqlServerInvestigationQuery(Current()));
        var administrationWorkspaceView = new AdministrationWorkspaceView(
            operationsAdministrationSession,
            () => new SqlServerAdministrationService(Current()),
            () => new DatabaseOperationalHealthRepository(Current()).LoadAsync(default));
        return new(operationsWorkspaceView, investigationWorkspaceView, administrationWorkspaceView);
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
        var connections = new Lazy<IEtpConnectionFactory>(() => new EtpConnectionFactory(target.Value));
        return new NightlyHealthSchedule(
            async token => (await new SqlServerHealthCheckStore(target.Value).LoadLatestNightlySummaryAsync(token).ConfigureAwait(false))?.BusinessDate,
            token => NightlyHealthLease.TryAcquireAsync(connections.Value, token),
            async token => await RunHealthCheckAsync(target.Value, HealthCheckTrigger.Nightly, null, token).ConfigureAwait(false),
            new FileNightlyHealthAttemptStore(FileNightlyHealthAttemptStore.DefaultPath),
            TimeProvider.System,
            ReportHealthRunFailure,
            token => NightlyHealthLease.ImportIsOpenAsync(connections.Value, token));
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
        var lease = await NightlyHealthLease.TryAcquireAsync(new EtpConnectionFactory(targetConnectionString), cancellationToken).ConfigureAwait(false)
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
        // ERR-01 reports this ingest (null: it threw, which the line shows as a Fail).
        var ingest = await BestEffortAsync(() => CreateIngest(registerStore).RunAsync(cancellationToken), "HEALTH_INGEST_FAILED").ConfigureAwait(false);

        var runner = new HealthCheckRunner(
            CreateHealthProbes(targetConnectionString, MigrationDirectory, healthStore, automationPrincipal,
                new ErrorProbes(token => registerStore.LoadRegisterAsync(cancellationToken: token), () => ingest)),
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
        var runId = await RecordHealthRunAsync(healthStore, run, cancellationToken).ConfigureAwait(false);
        return (run, runId);
    }

    /// <summary>
    /// Records the run. <c>diag.record_health_check_run</c> refuses the whole run (51820) for one invalid line; the
    /// runner normalises codes and texts, but not other lanes' sub-result details, so a refused run is recorded once
    /// more without any details (L4 note, watch-point 4) rather than losing the night. Any other failure propagates.
    /// </summary>
    internal static async Task<long> RecordHealthRunAsync(IHealthCheckStore store, HealthCheckRun run, CancellationToken cancellationToken)
    {
        try { return await store.RecordRunAsync(run, cancellationToken).ConfigureAwait(false); }
        catch (Microsoft.Data.SqlClient.SqlException refused) when (refused.Number == 51820)
        {
            DesktopDiagnostics.Record(refused, "HealthCheck.Record", "HEALTH_RUN_RECORD_REFUSED", DesktopDiagnosticSeverity.Warning);
            var withoutDetail = run with { Results = run.Results.Select(result => result with { SubResults = null }).ToArray() };
            return await store.RecordRunAsync(withoutDetail, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The health probes (design 5.2). The run order is the runner's (design 5.3); probes of one area run in this order.
    /// ERR-01..04 come from <paramref name="errorProbes"/> (it needs this run's ingest result); without it the runner
    /// reports them Skipped (NOT_AVAILABLE).
    /// </summary>
    internal static IReadOnlyList<IHealthProbe> CreateHealthProbes(
        string targetConnectionString, string migrationDirectory, IHealthCheckStore healthStore, string? automationPrincipal,
        ErrorProbes? errorProbes = null) =>
    [
        .. DatabaseProbes.Create(targetConnectionString, migrationDirectory),                                   // L4: DB-01..05
        new ServiceScreenTimingProbe(targetConnectionString),                                                   // 1.11.2 L9: DB-06
        new SecurityProbes(healthStore, automationPrincipal),                                                   // L4: SEC-01..03
        new LandingCopyProbe(targetConnectionString),                                                           // 1.11.2 L9: SEC-04
        OperationsProbes.CreateDefault(new EtpConnectionFactory(targetConnectionString),                       // L7: OPS-01..09
            OperationsProbeSettings.ForThisMachine(automationPrincipal), new DelegateSqlBuildAssessor(AssessSqlBuild)),
        new OffPcCopyProbe(new WindowsOffPcCopyEnvironment()),                                                  // 1.11.2 L9: OPS-10
        new DataProbes(targetConnectionString),                                                                 // L6: DATA-01..05
        new ImportProbes(targetConnectionString),                                                               // L6: IMP-01..03
        CreateReportProbes(targetConnectionString),                                                             // L5: RPT-01..22
        .. (errorProbes is null ? Array.Empty<IHealthProbe>() : new IHealthProbe[] { errorProbes })                               // ERR-01..04
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
