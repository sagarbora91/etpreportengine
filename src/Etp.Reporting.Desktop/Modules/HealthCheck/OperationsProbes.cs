extern alias EtpApplication;

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using EtpApplication::Etp.Reporting.Application.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Modules.HealthCheck;

// OPS-01..09 of the 1.11.0 self-diagnostics (design 5.2, lane L7). Read-only: SQL goes through ReadOnlySqlGuard, files and
// Task Scheduler are only read. Every reader sits behind a small interface so tests can describe a whole machine.
// Rule for all nine: something this account cannot read is Skipped, never Fail (design 13: unelevated Owners cannot see
// the automation account's tasks, the ProgramData folders or their ACLs; the nightly run checks those).

/// <summary>Everything the operations probes take from the machine, with the rules' thresholds (design 5.2).</summary>
public sealed record OperationsProbeSettings(
    string BackupFolder,
    string OperationsConfigFile,
    string HealthExportsFolder,
    string InstallFolder,
    string? AutomationAccount)
{
    /// <summary>
    /// Event log provider names of the SQL Server instance (default instance for Express). They are also its service names,
    /// whose service account setup lets write the Backups folder (OPS-05, 1.11.4).
    /// </summary>
    public IReadOnlyList<string> SqlEventProviders { get; init; } = ["MSSQL$SQLEXPRESS"];
    public TimeZoneInfo LocalZone { get; init; } = TimeZoneInfo.Local;
    public TimeSpan BackupWarningAge { get; init; } = TimeSpan.FromHours(36);
    public TimeSpan BackupFailAge { get; init; } = TimeSpan.FromHours(60);
    public TimeSpan DrillMaxAge { get; init; } = TimeSpan.FromDays(45);
    public TimeSpan BackupTaskMaxAge { get; init; } = TimeSpan.FromHours(26);
    public TimeSpan AutomationTaskMaxAge { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan DrillTaskMaxAge { get; init; } = TimeSpan.FromDays(32);
    public decimal DiskWarningGb { get; init; } = DatabaseOperationalHealthThresholds.Default.BackupFreeSpaceWarningGb;
    public decimal DiskCriticalGb { get; init; } = DatabaseOperationalHealthThresholds.Default.BackupFreeSpaceCriticalGb;
    public int FailureLogDays { get; init; } = 7;
    public TimeSpan EventLogWindow { get; init; } = TimeSpan.FromHours(24);

    /// <summary>The standard layout of this PC: %ProgramData%\EtpReporting\{Operations,Backups,HealthExports} and the install folder.</summary>
    public static OperationsProbeSettings ForThisMachine(string? automationAccount)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting");
        return new(Path.Combine(root, "Backups"), Path.Combine(root, "Operations", "operations.json"), Path.Combine(root, "HealthExports"),
            AppContext.BaseDirectory.TrimEnd('\\', '/'), automationAccount);
    }
}

public interface IEtpScheduledTaskSource
{
    Task<EtpScheduledTaskReadResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The three ETP tasks through <see cref="AutomaticImportTaskReader.ReadEtpTasksAsync"/>.</summary>
public sealed class EtpScheduledTaskSource : IEtpScheduledTaskSource
{
    public Task<EtpScheduledTaskReadResult> ReadAsync(CancellationToken cancellationToken) =>
        AutomaticImportTaskReader.ReadEtpTasksAsync(EtpScheduledTaskNames.All, cancellationToken);
}

/// <summary><c>dbo.load_database_operational_health</c> and the drill result, as the Settings screen reads them (OPS-01, OPS-02, OPS-08).</summary>
public interface IOperationsHealthSource
{
    Task<DatabaseOperationalHealth> LoadAsync(CancellationToken cancellationToken);
}

public sealed class DatabaseOperationsHealthSource(string connectionString) : IOperationsHealthSource
{
    public Task<DatabaseOperationalHealth> LoadAsync(CancellationToken cancellationToken) =>
        new DatabaseOperationalHealthRepository(connectionString).LoadAsync(cancellationToken);
}

public enum FileListStatus { Ok, NotFound, Denied }

/// <summary>Names (not paths) of the files found.</summary>
public sealed record FileListing(FileListStatus Status, IReadOnlyList<string> Names);

/// <summary>Line count and last line of a failure log; the last line starts with its ISO timestamp.</summary>
public sealed record LogFileSummary(int Lines, string? LastLine);

public interface IOperationsFileSystem
{
    FileListing ListFiles(string directory, string searchPattern);
    bool DirectoryExists(string path);
    /// <summary>Null when the file cannot be read.</summary>
    LogFileSummary? Summarise(string path);
    /// <summary>Free bytes on the volume of <paramref name="path"/>; null when it cannot be read.</summary>
    long? FreeBytes(string path);
}

public sealed class WindowsOperationsFileSystem : IOperationsFileSystem
{
    public FileListing ListFiles(string directory, string searchPattern)
    {
        try
        {
            if (!Directory.Exists(directory)) return new(FileListStatus.NotFound, []);
            return new(FileListStatus.Ok, Directory.EnumerateFiles(directory, searchPattern).Select(Path.GetFileName).OfType<string>().ToArray());
        }
        catch (DirectoryNotFoundException) { return new(FileListStatus.NotFound, []); }
        catch (UnauthorizedAccessException) { return new(FileListStatus.Denied, []); }
        catch (IOException) { return new(FileListStatus.Denied, []); }
    }

    public bool DirectoryExists(string path)
    {
        try { return Directory.Exists(path); } catch { return false; }
    }

    public LogFileSummary? Summarise(string path)
    {
        try
        {
            var lines = 0; string? last = null;
            // FileShare.ReadWrite: the backup or drill may be appending while the check reads.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                lines++; last = line;
            }
            return new(lines, last);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public long? FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
}

public enum PathProtection { Protected, Finding, Unreadable, Missing }

/// <summary><paramref name="Code"/> is a fixed token (RIGHTS, OWNER, LINK, VOLUME); the finding text itself (with SIDs and paths) is never kept.</summary>
public sealed record PathProtectionResult(PathProtection State, string Code);

public interface IProtectedPathChecker
{
    PathProtectionResult Check(string path);
}

/// <summary>
/// The application's protected-path rule (<see cref="ProtectedOperationPath"/>), as setup and the operations scripts apply it.
/// <paramref name="modifiablePath"/> (HealthExports) may also be modified by individual accounts (setup gives the automation
/// account and the active Owners Modify there, L9), but its permissions may still be changed only by administrators.
/// <paramref name="writablePath"/> (Backups, 1.11.4) may be written by <paramref name="writers"/> only: SQL Server's service
/// account and the automation account, which setup gives Modify there (<see cref="ProtectedOperationPath.BackupWriters"/>).
/// </summary>
public sealed class ProtectedOperationPathChecker(string? modifiablePath = null, string? writablePath = null, Func<string, bool>? writers = null)
    : IProtectedPathChecker
{
    public PathProtectionResult Check(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return new(PathProtection.Missing, "MISSING");
            ProtectedOperationPath.Validate(path, TargetModifier(path));
            return new(PathProtection.Protected, "OK");
        }
        catch (InvalidOperationException ex)
        {
            var message = ex.Message;
            var code = message.Contains("Linked", StringComparison.Ordinal) ? "LINK"
                : message.Contains("owned by", StringComparison.Ordinal) ? "OWNER"
                : message.Contains("local drive", StringComparison.Ordinal) ? "VOLUME"
                : "RIGHTS";
            return new(PathProtection.Finding, code);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException
                                       or System.Security.AccessControl.PrivilegeNotHeldException)
        {
            // Reading an ACL needs READ_CONTROL; an unelevated Owner does not have it under %ProgramData%\EtpReporting.
            return new(PathProtection.Unreadable, "UNREADABLE");
        }
    }

    /// <summary>Who besides SYSTEM, Administrators and TrustedInstaller may write <paramref name="path"/> itself; null: nobody.</summary>
    internal Func<string, bool>? TargetModifier(string path)
    {
        if (Same(path, modifiablePath)) return ProtectedOperationPath.IsIndividualAccount;
        if (Same(path, writablePath)) return writers ?? (_ => false);
        return null;
    }

    private static bool Same(string path, string? other) =>
        other is not null && string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.GetFullPath(other).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

public sealed record SqlServerBuildInfo(string ProductVersion, string? UpdateLevel, string? Edition);

public sealed record AutomationRunCount(string RunType, string Outcome, int Count);

/// <summary>Last 24 hours of <c>dbo.automation_runs</c> and the number of enabled schedules whose last run failed in that time.</summary>
public sealed record AutomationRunsSummary(IReadOnlyList<AutomationRunCount> Counts, int FailedScheduleCount);

/// <summary>The three SQL reads of the operations probes. Each throws on failure; the probe turns that into Skipped.</summary>
public interface IOperationsSqlSource
{
    Task<SqlServerBuildInfo> ReadBuildAsync(CancellationToken cancellationToken);
    /// <summary>Physical path of the database's data file (<c>sys.database_files</c>); null when none is returned.</summary>
    Task<string?> ReadDataFilePathAsync(CancellationToken cancellationToken);
    Task<AutomationRunsSummary> ReadAutomationRunsAsync(CancellationToken cancellationToken);
}

public sealed class SqlOperationsSource(string connectionString) : IOperationsSqlSource
{
    internal const string BuildSql = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), CAST(SERVERPROPERTY('ProductUpdateLevel') AS nvarchar(128)), CAST(SERVERPROPERTY('Edition') AS nvarchar(128));";
    internal const string DataFileSql = "SELECT TOP (1) physical_name FROM sys.database_files WHERE type = 0 ORDER BY file_id;";
    internal const string AutomationSql =
        "SELECT run_type, outcome, COUNT(*) FROM dbo.automation_runs WHERE completed_utc >= DATEADD(hour, -24, SYSUTCDATETIME()) GROUP BY run_type, outcome; " +
        "SELECT COUNT(*) FROM dbo.report_pack_schedules WHERE is_enabled = 1 AND last_status = 'Failed' AND last_run_utc >= DATEADD(hour, -24, SYSUTCDATETIME());";

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static SqlCommand Command(SqlConnection connection, string sql)
    {
        ReadOnlySqlGuard.Require(sql);
        return new SqlCommand(sql, connection) { CommandTimeout = 10 };
    }

    public async Task<SqlServerBuildInfo> ReadBuildAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, BuildSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) throw new InvalidOperationException("SQL Server returned no version.");
        return new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    public async Task<string?> ReadDataFilePathAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, DataFileSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0) ? reader.GetString(0) : null;
    }

    public async Task<AutomationRunsSummary> ReadAutomationRunsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(connection, AutomationSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var counts = new List<AutomationRunCount>();
        while (await reader.ReadAsync(cancellationToken)) counts.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        var failedSchedules = await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken) ? reader.GetInt32(0) : 0;
        return new(counts, failedSchedules);
    }
}

public enum SqlBuildVerdict { AtOrAboveRecommended, BelowRecommended, Rtm, UnsupportedMajor }

/// <summary>
/// OPS-06 compares the build with L4's <c>KnownSqlBuilds</c> table (minimum recommended build per major version). The probe owns no
/// build numbers: the composition root adapts <c>KnownSqlBuilds</c> to this interface (see <see cref="DelegateSqlBuildAssessor"/>).
/// An RTM build (no cumulative update) is <see cref="SqlBuildVerdict.Rtm"/>.
/// </summary>
public interface ISqlBuildAssessor
{
    SqlBuildVerdict Assess(SqlServerBuildInfo build);
}

public sealed class DelegateSqlBuildAssessor(Func<SqlServerBuildInfo, SqlBuildVerdict> assess) : ISqlBuildAssessor
{
    public SqlBuildVerdict Assess(SqlServerBuildInfo build) => assess(build);
}

/// <summary>OPS-01..OPS-09. Run from the nightly runner and the manual run; returns one result per id, Skipped where it cannot read.</summary>
public sealed class OperationsProbes : IHealthProbe
{
    private static readonly Regex FailureLogName = new(@"^(?<kind>backup|recovery-drill)-(?<date>\d{8})\.log$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Task Scheduler "last result" values that are not failures: 0x41301 is "currently running".
    private const int TaskRunning = 0x41301;

    private readonly IEtpScheduledTaskSource tasks;
    private readonly IOperationsHealthSource health;
    private readonly IOperationsFileSystem fileSystem;
    private readonly IProtectedPathChecker paths;
    private readonly IOperationsSqlSource sql;
    private readonly ISqlBuildAssessor sqlBuilds;
    private readonly EventLogProbe eventLog;
    private readonly OperationsProbeSettings settings;

    public OperationsProbes(IEtpScheduledTaskSource tasks, IOperationsHealthSource health, IOperationsFileSystem fileSystem, IProtectedPathChecker paths,
        IOperationsSqlSource sql, ISqlBuildAssessor sqlBuilds, IEventLogSource eventLogSource, OperationsProbeSettings settings)
    {
        this.tasks = tasks; this.health = health; this.fileSystem = fileSystem; this.paths = paths; this.sql = sql; this.sqlBuilds = sqlBuilds;
        this.settings = settings;
        eventLog = new EventLogProbe(eventLogSource, settings);
    }

    /// <summary>The real machine's readers for <paramref name="connectionString"/>; the caller supplies the adapter over L4's KnownSqlBuilds.</summary>
    public static OperationsProbes CreateDefault(string connectionString, OperationsProbeSettings settings, ISqlBuildAssessor sqlBuilds) =>
        new(new EtpScheduledTaskSource(), new DatabaseOperationsHealthSource(connectionString), new WindowsOperationsFileSystem(),
            new ProtectedOperationPathChecker(settings.HealthExportsFolder, settings.BackupFolder,
                ProtectedOperationPath.BackupWriters(settings.SqlEventProviders, ProtectedOperationPath.AccountSid(settings.AutomationAccount))),
            new SqlOperationsSource(connectionString), sqlBuilds, new WindowsEventLogSource(), settings);

    public IReadOnlyList<string> CheckIds { get; } =
    [
        HealthCheckId.Ops01, HealthCheckId.Ops02, HealthCheckId.Ops03, HealthCheckId.Ops04, HealthCheckId.Ops05,
        HealthCheckId.Ops06, HealthCheckId.Ops07, HealthCheckId.Ops08, HealthCheckId.Ops09
    ];

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var now = context.StartedUtc;
        var results = new List<HealthCheckResult>(9);

        // One load of the health procedure serves OPS-01, OPS-02 and OPS-08 (and the backup free space is not used from it: OPS-04 reads the disks).
        DatabaseOperationalHealth? loaded = null;
        try { loaded = await health.LoadAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* Skipped lines below say so; no exception text is kept. */ }

        results.Add(Run(HealthCheckId.Ops01, sw => Backup(loaded, now, sw)));
        results.Add(Run(HealthCheckId.Ops02, sw => Drill(loaded, now, sw)));
        results.Add(await RunAsync(HealthCheckId.Ops03, async sw => Tasks(await tasks.ReadAsync(cancellationToken), now, sw), cancellationToken));
        results.Add(await RunAsync(HealthCheckId.Ops04, sw => DiskAsync(sw, cancellationToken), cancellationToken));
        results.Add(Run(HealthCheckId.Ops05, Acls));
        results.Add(await RunAsync(HealthCheckId.Ops06, sw => SqlBuildAsync(sw, cancellationToken), cancellationToken));
        results.Add(await RunAsync(HealthCheckId.Ops07, sw => UnattendedAsync(sw, cancellationToken), cancellationToken));
        results.Add(Run(HealthCheckId.Ops08, sw => FailureLogs(loaded, now, sw)));
        try { results.Add(await eventLog.RunAsync(now, cancellationToken)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { results.Add(Skipped(HealthCheckId.Ops09, "EVENT_LOG_UNREADABLE", "The Windows Application event log could not be read by this account; the nightly run checks it.", Stopwatch.StartNew())); }
        return results;
    }

    // ---- guards: an unexpected failure is Skipped (no exception text kept), never Fail ----

    private HealthCheckResult Run(string id, Func<Stopwatch, HealthCheckResult> check)
    {
        var stopwatch = Stopwatch.StartNew();
        try { return check(stopwatch); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Skipped(id, "NOT_READABLE", "This check could not read what it needs on this PC; the nightly run checks it.", stopwatch); }
    }

    private async Task<HealthCheckResult> RunAsync(string id, Func<Stopwatch, Task<HealthCheckResult>> check, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try { return await check(stopwatch); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return Skipped(id, "NOT_READABLE", "This check could not read what it needs on this PC; the nightly run checks it.", stopwatch); }
    }

    private static HealthCheckResult Skipped(string id, string code, string text, Stopwatch stopwatch) =>
        Result(id, HealthOutcome.Skipped, code, text, null, stopwatch);

    private static HealthCheckResult Result(string id, HealthOutcome outcome, string code, string text, string? fix, Stopwatch stopwatch,
        decimal? metric = null, IReadOnlyList<HealthCheckSubResult>? subs = null) =>
        new(id, HealthCheckId.AreaOf(id), outcome, code, text, fix, stopwatch.Elapsed, metric, subs);

    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("dd MMM yyyy HH:mm", Etp.Reporting.Reporting.IndianFormat.Culture) + " UTC";
    private static decimal Round(double value, int digits = 1) => Math.Round((decimal)value, digits);

    private static DateTimeOffset? Utc(DateTime? value) =>
        value is { } moment ? new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)) : null;

    private DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, settings.LocalZone), TimeSpan.Zero);
    }

    // ---- OPS-01 ----

    private HealthCheckResult Backup(DatabaseOperationalHealth? loaded, DateTimeOffset now, Stopwatch sw)
    {
        const string id = HealthCheckId.Ops01;
        if (loaded is null)
            return Skipped(id, "HEALTH_UNREADABLE", "The backup record could not be read from the database (database not reachable, or this account may not read it).", sw);
        if (loaded.LastSuccessfulBackupUtc is not { } last)
            return Result(id, HealthOutcome.Fail, "BACKUP_MISSING", "No verified backup is recorded.", "backups", sw);
        var lastUtc = new DateTimeOffset(DateTime.SpecifyKind(last, DateTimeKind.Utc));
        var age = now - lastUtc;
        var hours = Round(age.TotalHours);
        if (age > settings.BackupFailAge)
            return Result(id, HealthOutcome.Fail, "BACKUP_VERY_STALE", $"The last verified backup was {Stamp(lastUtc)}, {hours} hours ago.", "backups", sw, hours);
        if (age > settings.BackupWarningAge)
            return Result(id, HealthOutcome.Warning, "BACKUP_STALE", $"The last verified backup was {Stamp(lastUtc)}, {hours} hours ago.", "backups", sw, hours);
        return Result(id, HealthOutcome.Pass, "OK", $"The last verified backup was {Stamp(lastUtc)}.", null, sw, hours);
    }

    // ---- OPS-02 ----

    private HealthCheckResult Drill(DatabaseOperationalHealth? loaded, DateTimeOffset now, Stopwatch sw)
    {
        const string id = HealthCheckId.Ops02;
        if (loaded is null)
            return Skipped(id, "HEALTH_UNREADABLE", "The recovery drill record could not be read from the database (database not reachable, or this account may not read it).", sw);
        var latest = loaded.LatestRecoveryDrillResult;
        if (latest is { Succeeded: false })
        {
            var when = new DateTimeOffset(DateTime.SpecifyKind(latest.CompletedUtc, DateTimeKind.Utc));
            return Result(id, HealthOutcome.Fail, "RECOVERY_DRILL_FAILED", $"The latest recovery drill ({Stamp(when)}) failed; row counts: {latest.RowCountStatus}.", "recovery", sw);
        }
        var completed = latest is not null ? Utc(latest.CompletedUtc) : Utc(loaded.LastSuccessfulRecoveryDrillUtc);
        if (completed is not { } at)
            return Result(id, HealthOutcome.Fail, "RECOVERY_DRILL_MISSING", "No recovery drill has ever been recorded.", "recovery", sw);
        var days = Round((now - at).TotalDays);
        if (now - at > settings.DrillMaxAge)
            return Result(id, HealthOutcome.Warning, "RECOVERY_DRILL_STALE", $"The last successful recovery drill was {Stamp(at)}, {days} days ago.", "recovery", sw, days);
        var counts = latest?.RowCountStatus ?? "NotRecorded";
        if (!counts.Equals("Matched", StringComparison.OrdinalIgnoreCase))
            return Result(id, HealthOutcome.Warning, "ROW_COUNTS_NOT_RECORDED", $"The last recovery drill ({Stamp(at)}) passed but its row counts were not recorded ({counts}).", "recovery", sw, days);
        return Result(id, HealthOutcome.Pass, "OK", $"The last recovery drill ({Stamp(at)}) passed and its row counts matched the backup.", null, sw, days);
    }

    // ---- OPS-03 ----

    private sealed record TaskSpec(string Name, string Label, Func<OperationsProbeSettings, TimeSpan> MaxAge);

    private static readonly TaskSpec[] TaskSpecs =
    [
        new(EtpScheduledTaskNames.AutomatedOperations, "Automated operations task", s => s.AutomationTaskMaxAge),
        new(EtpScheduledTaskNames.DailyBackup, "Daily backup task", s => s.BackupTaskMaxAge),
        new(EtpScheduledTaskNames.MonthlyRecoveryDrill, "Monthly recovery drill task", s => s.DrillTaskMaxAge)
    ];

    private HealthCheckResult Tasks(EtpScheduledTaskReadResult read, DateTimeOffset now, Stopwatch sw)
    {
        const string id = HealthCheckId.Ops03;
        if (!read.Readable)
            return Skipped(id, "TASKS_UNREADABLE", "Windows Task Scheduler could not be read by this account; the nightly run checks the three ETP tasks.", sw);
        var subs = new List<HealthCheckSubResult>();
        var phrases = new List<string>();
        foreach (var spec in TaskSpecs)
        {
            var info = read.Tasks.FirstOrDefault(task => task.Name.Equals(spec.Name, StringComparison.OrdinalIgnoreCase));
            var (outcome, code) = EvaluateTask(spec, info, read.Elevated, now);
            var fields = new Dictionary<string, string> { ["task"] = spec.Name };
            if (info is { Found: true, Readable: true })
            {
                fields["state"] = info.State;
                if (info.LastRun is { } run) fields["lastRun"] = FromLocal(run).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
                if (info.LastResult is { } result) fields["lastResult"] = "0x" + unchecked((uint)result).ToString("X8", CultureInfo.InvariantCulture);
            }
            subs.Add(new(spec.Name, outcome, code, null, fields));
            phrases.Add($"{spec.Label}: {TaskPhrase(code)}");
        }
        var worst = subs.Any(sub => sub.Outcome == HealthOutcome.Fail) ? HealthOutcome.Fail
            : subs.Any(sub => sub.Outcome == HealthOutcome.Warning) ? HealthOutcome.Warning
            : subs.Any(sub => sub.Outcome == HealthOutcome.Skipped) ? HealthOutcome.Skipped
            : HealthOutcome.Pass;
        var lead = worst == HealthOutcome.Pass ? "OK" : subs.First(sub => sub.Outcome == worst).FindingCode!;
        var text = string.Join("; ", phrases) + ".";
        string? fix = worst is HealthOutcome.Pass or HealthOutcome.Skipped ? null
            : lead is "TASK_MISSING" or "TASK_DISABLED" ? "Run ETP setup as administrator." : "backups";
        return Result(id, worst, lead, text, fix, sw, null, subs);
    }

    private (HealthOutcome Outcome, string Code) EvaluateTask(TaskSpec spec, EtpScheduledTaskInfo? info, bool elevated, DateTimeOffset now)
    {
        // An unelevated account cannot see tasks registered under another principal (the backup and automation tasks run as the
        // automation account), and Task Scheduler then answers "not found" as well: that is not evidence the task is missing.
        if (info is null || !info.Found)
            return elevated ? (HealthOutcome.Fail, "TASK_MISSING") : (HealthOutcome.Skipped, "TASK_NOT_VISIBLE");
        if (!info.Readable) return (HealthOutcome.Skipped, "TASK_NOT_VISIBLE");
        if (!info.Enabled) return (HealthOutcome.Warning, "TASK_DISABLED");
        if (info.LastResult is { } result && result != 0 && result != TaskRunning) return (HealthOutcome.Fail, "TASK_RESULT");
        if (info.LastRun is not { } lastRun) return (HealthOutcome.Warning, "TASK_NEVER_RUN");
        if (now - FromLocal(lastRun) > spec.MaxAge(settings)) return (HealthOutcome.Warning, "TASK_LATE");
        return (HealthOutcome.Pass, "OK");
    }

    private static string TaskPhrase(string code) => code switch
    {
        "OK" => "ok",
        "TASK_MISSING" => "not installed",
        "TASK_DISABLED" => "disabled",
        "TASK_RESULT" => "last run failed",
        "TASK_NEVER_RUN" => "has not run yet",
        "TASK_LATE" => "last run is overdue",
        _ => "not visible to this account"
    };

    // ---- OPS-04 ----

    private async Task<HealthCheckResult> DiskAsync(Stopwatch sw, CancellationToken cancellationToken)
    {
        const string id = HealthCheckId.Ops04;
        string? dataFile = null;
        try { dataFile = await sql.ReadDataFilePathAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* the data volume is then reported as not read */ }

        var volumes = new List<(string Key, string? Path)> { ("backup-volume", settings.BackupFolder), ("data-volume", dataFile) };
        var subs = new List<HealthCheckSubResult>();
        var phrases = new List<string>();
        decimal? least = null;
        foreach (var (key, path) in volumes)
        {
            long? free = path is null ? null : fileSystem.FreeBytes(path);
            if (free is not { } bytes)
            {
                subs.Add(new(key, HealthOutcome.Skipped, "VOLUME_NOT_READ"));
                phrases.Add($"{key.Replace('-', ' ')} not read");
                continue;
            }
            var gb = Math.Round(bytes / 1024m / 1024m / 1024m, 1);
            var outcome = gb < settings.DiskCriticalGb ? HealthOutcome.Fail : gb < settings.DiskWarningGb ? HealthOutcome.Warning : HealthOutcome.Pass;
            var code = outcome == HealthOutcome.Fail ? "DISK_CRITICAL" : outcome == HealthOutcome.Warning ? "DISK_LOW" : "OK";
            subs.Add(new(key, outcome, code, gb));
            phrases.Add($"{key.Replace('-', ' ')} {gb.ToString("0.0", CultureInfo.InvariantCulture)} GB free");
            least = least is { } current ? Math.Min(current, gb) : gb;
        }
        if (least is null)
            return Skipped(id, "VOLUME_NOT_READ", "Free disk space could not be read for the backup or the data volume.", sw);
        var worst = subs.Any(sub => sub.Outcome == HealthOutcome.Fail) ? HealthOutcome.Fail : subs.Any(sub => sub.Outcome == HealthOutcome.Warning) ? HealthOutcome.Warning : HealthOutcome.Pass;
        var finding = worst == HealthOutcome.Fail ? "DISK_CRITICAL" : worst == HealthOutcome.Warning ? "DISK_LOW" : "OK";
        return Result(id, worst, finding, string.Join("; ", phrases) + ".", worst == HealthOutcome.Pass ? null : "backups", sw, least, subs);
    }

    // ---- OPS-05 ----

    private HealthCheckResult Acls(Stopwatch sw)
    {
        const string id = HealthCheckId.Ops05;
        var folders = new (string Key, string Path)[]
        {
            ("operations-config", settings.OperationsConfigFile), ("backups", settings.BackupFolder),
            ("health-exports", settings.HealthExportsFolder), ("install", settings.InstallFolder)
        };
        var subs = new List<HealthCheckSubResult>();
        foreach (var (key, path) in folders)
        {
            var check = paths.Check(path);
            var (outcome, code) = check.State switch
            {
                PathProtection.Protected => (HealthOutcome.Pass, "OK"),
                PathProtection.Finding => (HealthOutcome.Fail, "ACL_" + check.Code),
                PathProtection.Missing => (HealthOutcome.Skipped, "FOLDER_MISSING"),
                _ => (HealthOutcome.Skipped, "ACL_UNREADABLE")
            };
            subs.Add(new(key, outcome, code));
        }
        var findings = subs.Where(sub => sub.Outcome == HealthOutcome.Fail).Select(sub => sub.Key).ToArray();
        if (findings.Length > 0)
            return Result(id, HealthOutcome.Fail, subs.First(sub => sub.Outcome == HealthOutcome.Fail).FindingCode!,
                "These ETP folders can be changed by accounts other than administrators and the system: " + string.Join(", ", findings) + ".",
                "Run ETP setup as administrator.", sw, findings.Length, subs);
        var unchecked_ = subs.Where(sub => sub.Outcome == HealthOutcome.Skipped).Select(sub => sub.Key).ToArray();
        if (unchecked_.Length > 0)
            return Result(id, HealthOutcome.Skipped, subs.First(sub => sub.Outcome == HealthOutcome.Skipped).FindingCode!,
                "The permissions of these ETP folders could not be read by this account: " + string.Join(", ", unchecked_) + "; the nightly run checks them.", null, sw, null, subs);
        return Result(id, HealthOutcome.Pass, "OK", "The operations file, the backup folder, the health export folder and the install folder are protected from non-administrator changes.", null, sw, 0, subs);
    }

    // ---- OPS-06 ----

    private async Task<HealthCheckResult> SqlBuildAsync(Stopwatch sw, CancellationToken cancellationToken)
    {
        const string id = HealthCheckId.Ops06;
        SqlServerBuildInfo build;
        try { build = await sql.ReadBuildAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Skipped(id, "BUILD_UNREADABLE", "The SQL Server version could not be read (database not reachable).", sw); }
        var level = string.IsNullOrWhiteSpace(build.UpdateLevel) ? "RTM" : build.UpdateLevel!.Trim();
        var fields = new Dictionary<string, string> { ["version"] = build.ProductVersion, ["updateLevel"] = level };
        if (!string.IsNullOrWhiteSpace(build.Edition)) fields["edition"] = HealthCheckTexts.DetailToken(build.Edition);
        var subs = new[] { new HealthCheckSubResult("sql-server", HealthOutcome.Pass, null, null, fields) };
        var label = $"SQL Server {build.ProductVersion} ({level})";
        // Decision 36 (Q15): an RTM build is a Warning, not a Fail; the fix is the CU runbook.
        var (outcome, code, text, fix) = sqlBuilds.Assess(build) switch
        {
            SqlBuildVerdict.AtOrAboveRecommended => (HealthOutcome.Pass, "OK", $"{label} is at or above the recommended build.", (string?)null),
            SqlBuildVerdict.Rtm => (HealthOutcome.Warning, "SQL_RTM", $"{label} has no cumulative update installed; install the latest cumulative update (see SQL-CU-AND-AUTOCLOSE-RUNBOOK).", "Install the latest SQL Server cumulative update."),
            SqlBuildVerdict.BelowRecommended => (HealthOutcome.Warning, "SQL_BUILD_BELOW_RECOMMENDED", $"{label} is below the recommended build; install the latest cumulative update.", "Install the latest SQL Server cumulative update."),
            _ => (HealthOutcome.Fail, "SQL_MAJOR_UNSUPPORTED", $"{label}: this major version of SQL Server is not supported by ETP.", "Install a supported SQL Server version.")
        };
        var checked_ = subs[0] with { Outcome = outcome, FindingCode = code };
        return Result(id, outcome, code, text, fix, sw, null, [checked_]);
    }

    // ---- OPS-07 ----

    private async Task<HealthCheckResult> UnattendedAsync(Stopwatch sw, CancellationToken cancellationToken)
    {
        const string id = HealthCheckId.Ops07;
        AutomationRunsSummary summary;
        try { summary = await sql.ReadAutomationRunsAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Skipped(id, "AUTOMATION_RUNS_UNREADABLE", "The unattended run record could not be read (database not reachable, or this account may not read it).", sw); }
        int Failed(params string[] types) => summary.Counts.Where(count => count.Outcome == "Failed" && types.Contains(count.RunType)).Sum(count => count.Count);
        var packFailures = Failed("SCHEDULED_REPORT_PACK", "AUTO_REPORT_PACK");
        var importFailures = Failed("WATCH_IMPORT");
        var succeeded = summary.Counts.Where(count => count.Outcome == "Succeeded").Sum(count => count.Count);
        var subs = new List<HealthCheckSubResult>
        {
            new("report-packs", packFailures > 0 || summary.FailedScheduleCount > 0 ? HealthOutcome.Fail : HealthOutcome.Pass, packFailures > 0 || summary.FailedScheduleCount > 0 ? "REPORT_PACK_FAILED" : "OK", packFailures + summary.FailedScheduleCount),
            new("watch-import", importFailures > 0 ? HealthOutcome.Warning : HealthOutcome.Pass, importFailures > 0 ? "WATCH_IMPORT_FAILED" : "OK", importFailures)
        };
        if (packFailures > 0 || summary.FailedScheduleCount > 0)
            return Result(id, HealthOutcome.Fail, "REPORT_PACK_FAILED",
                $"In the last 24 hours {packFailures + summary.FailedScheduleCount} unattended report pack run(s) failed; {importFailures} automatic import run(s) failed.", "generations", sw, packFailures + summary.FailedScheduleCount, subs);
        if (importFailures > 0)
            return Result(id, HealthOutcome.Warning, "WATCH_IMPORT_FAILED", $"In the last 24 hours {importFailures} automatic import run(s) failed.", "conflicts", sw, importFailures, subs);
        return Result(id, HealthOutcome.Pass, "OK", $"No unattended run failed in the last 24 hours ({succeeded} succeeded).", null, sw, 0, subs);
    }

    // ---- OPS-08 ----

    private HealthCheckResult FailureLogs(DatabaseOperationalHealth? loaded, DateTimeOffset now, Stopwatch sw)
    {
        const string id = HealthCheckId.Ops08;
        var directory = Path.Combine(settings.BackupFolder, "Logs");
        var listing = fileSystem.ListFiles(directory, "*.log");
        if (listing.Status == FileListStatus.Denied
            || (listing.Status == FileListStatus.NotFound && !fileSystem.DirectoryExists(settings.BackupFolder)))
            return Skipped(id, "LOGS_UNREADABLE", "The backup and recovery drill failure logs could not be read by this account; the nightly run checks them.", sw);

        var today = TimeZoneInfo.ConvertTime(now, settings.LocalZone).Date;
        var first = today.AddDays(-(settings.FailureLogDays - 1));
        var latest = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var lines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in listing.Names)
        {
            var match = FailureLogName.Match(name);
            if (!match.Success || !DateTime.TryParseExact(match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            if (date < first || date > today) continue;
            var summary = fileSystem.Summarise(Path.Combine(directory, name));
            if (summary is null) return Skipped(id, "LOGS_UNREADABLE", "The backup and recovery drill failure logs could not be read by this account; the nightly run checks them.", sw);
            if (summary.Lines == 0) continue;
            var kind = match.Groups["kind"].Value.ToLowerInvariant();
            var stamp = LineTime(summary.LastLine) ?? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.AddDays(1).AddSeconds(-1), settings.LocalZone), TimeSpan.Zero);
            lines[kind] = lines.GetValueOrDefault(kind) + summary.Lines;
            if (!latest.TryGetValue(kind, out var known) || stamp > known) latest[kind] = stamp;
        }
        if (latest.Count == 0)
            return Result(id, HealthOutcome.Pass, "OK", $"No backup or recovery drill failure was logged in the last {settings.FailureLogDays} days.", null, sw, 0);

        DateTimeOffset? Success(string kind) => kind == "backup" ? Utc(loaded?.LastSuccessfulBackupUtc) : Utc(loaded?.LastSuccessfulRecoveryDrillUtc);
        var subs = new List<HealthCheckSubResult>();
        foreach (var (kind, failedAt) in latest.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var success = Success(kind);
            var (outcome, code) = loaded is null ? (HealthOutcome.Warning, "FAILURE_UNVERIFIED")
                : success is { } at && at >= failedAt ? (HealthOutcome.Warning, "FAILURE_THEN_SUCCESS")
                : (HealthOutcome.Fail, "FAILURE_UNRESOLVED");
            subs.Add(new(kind, outcome, code, lines[kind], new Dictionary<string, string> { ["lastFailure"] = failedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture) }));
        }
        var total = lines.Values.Sum();
        var summaryText = string.Join("; ", subs.Select(sub => $"{sub.Key}: {(int)sub.MetricValue!} failure line(s)"));
        if (subs.Any(sub => sub.Outcome == HealthOutcome.Fail))
            return Result(id, HealthOutcome.Fail, "FAILURE_UNRESOLVED", $"A failure was logged after the last success in the last {settings.FailureLogDays} days ({summaryText}).", "backups", sw, total, subs);
        return Result(id, HealthOutcome.Warning, subs[0].FindingCode!, $"Failures were logged in the last {settings.FailureLogDays} days ({summaryText}); " +
            (loaded is null ? "the later success could not be checked." : "a later success is recorded."), "backups", sw, total, subs);
    }

    private static DateTimeOffset? LineTime(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var token = line.Split(' ', 2)[0];
        return DateTimeOffset.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value.ToUniversalTime() : null;
    }
}
