using System.Diagnostics;
using System.Globalization;
using Etp.Reporting.Application.HealthCheck;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

// 1.11.2 lane L9: DB-06, SEC-04 and OPS-10 are HealthCheckId constants in the catalogue (54 ids) and the composition
// root registers these probes (integration, 12 Oct 2026).

/// <summary>
/// The computer name as a <c>diag.is_token</c> value (<c>diag.health_check_runs.machine_name</c>: 1 to 64 characters
/// of A-Z a-z 0-9 . _ : &lt; &gt; + -). A Windows computer name may hold an apostrophe, an ampersand, spaces, Unicode
/// letters and more; each such character becomes an underscore, so a run is recorded whatever the PC is called.
/// </summary>
public static class HealthMachineName
{
    public const int MaxLength = 64;

    public static string ToToken(string? machineName)
    {
        if (string.IsNullOrWhiteSpace(machineName)) return "unknown";
        var chars = machineName.Trim().Select(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                or '.' or '_' or ':' or '<' or '>' or '+' or '-' ? character : '_').ToArray();
        var token = new string(chars);
        return token.Length <= MaxLength ? token : token[..MaxLength];
    }
}

/// <summary>
/// SEC-04 (Security audit S-08): a Warning for every other database on the instance that holds <c>etp_landing_*</c>
/// tables, because each is a second copy of customer data outside the 0057 rules. Read-only: one list of databases
/// and one count per database; a database that cannot be read is a Skipped sub-result, never a Fail. Sub-result keys
/// are numbers (DATABASE_1 ...), so no database name leaves the PC.
/// </summary>
public sealed class LandingCopyProbe(string connectionString) : IHealthProbe
{
    public IReadOnlyList<string> CheckIds { get; } = [HealthCheckId.Sec04];

    internal const string OtherDatabasesSql =
        "SELECT d.name FROM sys.databases AS d WHERE d.database_id > 4 AND d.database_id <> DB_ID() AND d.state = 0 ORDER BY d.name;";

    /// <summary>The count query of one database; <paramref name="name"/> is quoted for use as an identifier.</summary>
    internal static string LandingCountSql(string name) =>
        "SELECT COUNT(*) FROM " + QuoteName(name) + ".sys.tables AS t WHERE t.name LIKE 'etp[_]landing[_]%';";

    internal static string QuoteName(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var result = await ImportDataProbeSupport.GuardAsync(HealthCheckId.Sec04, async () =>
        {
            var names = new List<string>();
            var counts = new List<int?>();
            await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
            await using (var command = ImportDataProbeSupport.ReadOnlyCommand(connection, OtherDatabasesSql))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) names.Add(reader.GetString(0));
            foreach (var name in names)
            {
                try
                {
                    await using var command = ImportDataProbeSupport.ReadOnlyCommand(connection, LandingCountSql(name));
                    counts.Add(Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture));
                }
                catch (SqlException)
                {
                    counts.Add(null); // not permitted or not accessible: Skipped, not Fail
                }
            }
            return Evaluate(counts, timer.Elapsed);
        }, cancellationToken).ConfigureAwait(false);
        return [result];
    }

    /// <summary>One entry per other database: the number of <c>etp_landing_*</c> tables, or null when it could not be read.</summary>
    internal static HealthCheckResult Evaluate(IReadOnlyList<int?> landingTableCounts, TimeSpan duration)
    {
        var subs = new List<HealthCheckSubResult>();
        for (var index = 0; index < landingTableCounts.Count; index++)
        {
            var key = "DATABASE_" + (index + 1).ToString(CultureInfo.InvariantCulture);
            var count = landingTableCounts[index];
            subs.Add(count is null ? new(key, HealthOutcome.Skipped, "NOT_READABLE")
                : count > 0 ? new(key, HealthOutcome.Warning, "LANDING_COPY", count)
                : new(key, HealthOutcome.Pass));
        }
        var copies = subs.Count(sub => sub.Outcome == HealthOutcome.Warning);
        var unread = subs.Count(sub => sub.Outcome == HealthOutcome.Skipped);
        var detail = copies > 0 || unread > 0 ? subs.Where(sub => sub.Outcome != HealthOutcome.Pass).ToList() : null;
        if (copies > 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Sec04, HealthOutcome.Warning, "LANDING_COPY_ELSEWHERE",
                ImportDataProbeSupport.Text($"{copies} other database(s) on this SQL Server hold a copy of imported customer data (etp_landing tables). Take an encrypted backup if still needed, then drop them."),
                null, duration, copies, detail);
        if (unread > 0 && unread == subs.Count)
            return ImportDataProbeSupport.Make(HealthCheckId.Sec04, HealthOutcome.Skipped, "NOT_READABLE",
                "Other databases on this SQL Server could not be read with this account.", null, duration, null, detail);
        return ImportDataProbeSupport.Make(HealthCheckId.Sec04, HealthOutcome.Pass, HealthCheckTexts.Ok,
            "No other database on this SQL Server holds imported customer data.", null, duration, 0, detail);
    }
}

/// <summary>
/// DB-06 (Architecture audit 7, F5/F7; 1.12 A4 warm timing): how long the Service screens' views take to count their rows.
/// Each view is counted twice on one connection. The first (cold) count after a SQL Server start or a view change pays for
/// compiling the plan (the VM's first <c>v_service_job</c> count took 2.7 s, Workpc 1.2 s), so it is reported as a number
/// only; the Warning is judged on the second (warm) count, above one second, which is what a screen pays on every open
/// after the first. A materialised Service table is planned only if the warm count stays slow. Each sub-result carries
/// <c>cold_ms</c> and <c>warm_ms</c>. Read-only SELECT COUNT(*); a missing view or a refusal is Skipped.
/// <para>
/// 1.12.1 (VM acceptance 16 Oct 2026, finding 2): on the 4 GB VM the whole check was Skipped TIME_BUDGET, because the
/// twelve counts did not fit in what DB-01..05 and DB-07 left of the Database area's 10 s (DB-02 alone took 7.6 s reading
/// the migration and object files cold). The Database cap is now 20 s (<see cref="HealthCheckBudget.Default"/>), and the
/// probe keeps its own budget (<see cref="OwnBudget"/>, never more than the runner's allowance less
/// <see cref="AllowanceMargin"/>): a view it has no time left for is a Skipped TIME_BUDGET sub-result, a view whose repeat
/// count did not fit is judged on its first count (only <c>cold_ms</c>), and the views already counted still give a verdict
/// instead of the runner discarding the whole check.
/// </para>
/// </summary>
public sealed class ServiceScreenTimingProbe(string connectionString) : IHealthProbe
{
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(1);

    /// <summary>At most this long for all counts (VM: about 7 s for the six views cold and warm, 3,158 jobs).</summary>
    public static readonly TimeSpan OwnBudget = TimeSpan.FromSeconds(10);

    /// <summary>Kept back from the runner's allowance, so the probe returns its verdict before the runner gives up on it.</summary>
    public static readonly TimeSpan AllowanceMargin = TimeSpan.FromMilliseconds(750);

    /// <summary>The views behind the Service screens (key, view). Job history reads the list events (1.12 A4: sixth view).</summary>
    internal static readonly IReadOnlyList<(string Key, string View)> Views =
    [
        ("JOBS", "dbo.v_service_job"),
        ("PENDING", "dbo.v_service_pending_current"),
        ("STATUS", "dbo.v_service_status_view_rows"),
        ("CLAIMS", "dbo.v_service_claims"),
        ("PARTS", "dbo.v_service_parts"),
        ("JOB_HISTORY", "dbo.v_service_job_list_events")
    ];

    /// <summary>The sub-result field holding the first (compile) count in milliseconds.</summary>
    public const string ColdField = "cold_ms";

    /// <summary>The sub-result field holding the repeat count in milliseconds (the one judged).</summary>
    public const string WarmField = "warm_ms";

    /// <summary>The sub-result finding code of a view the probe had no time left to count.</summary>
    public const string TimeBudgetCode = "TIME_BUDGET";

    public IReadOnlyList<string> CheckIds { get; } = [HealthCheckId.Db06];

    internal static string CountSql(string view) => "SELECT COUNT(*) FROM " + view + ";";

    /// <summary>The probe's budget: <see cref="OwnBudget"/>, or less when the runner allows less (<see cref="HealthCheckRunner.ProbeAllowanceItem"/>).</summary>
    internal static TimeSpan BudgetFor(HealthCheckContext context)
    {
        var budget = OwnBudget;
        if (context.Items.TryGetValue(HealthCheckRunner.ProbeAllowanceItem, out var value) && value is TimeSpan allowance && allowance - AllowanceMargin < budget)
            budget = allowance - AllowanceMargin;
        return budget;
    }

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var budget = BudgetFor(context);
        var result = await ImportDataProbeSupport.GuardAsync(HealthCheckId.Db06, async () =>
        {
            await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
            var measured = await MeasureAsync(Views, budget, () => timer.Elapsed,
                (view, timeoutSeconds, token) => CountAsync(connection, view, timeoutSeconds, token), cancellationToken).ConfigureAwait(false);
            return Evaluate(measured.Timings, timer.Elapsed, measured.OutOfTime);
        }, cancellationToken).ConfigureAwait(false);
        return [result];
    }

    /// <summary>
    /// Counts each view cold then warm while <paramref name="budget"/> lasts. <paramref name="count"/> gets the seconds left
    /// as its command timeout and throws <see cref="TimeoutException"/> when a count runs out of them; any other
    /// <see cref="SqlException"/> means the view is missing or not permitted (both counts null, not out of time). Once the
    /// budget is gone no further count starts.
    /// </summary>
    internal static async Task<(IReadOnlyList<(string Key, TimeSpan? Cold, TimeSpan? Warm)> Timings, IReadOnlySet<string> OutOfTime)> MeasureAsync(
        IReadOnlyList<(string Key, string View)> views, TimeSpan budget, Func<TimeSpan> elapsed,
        Func<string, int, CancellationToken, Task<TimeSpan>> count, CancellationToken cancellationToken)
    {
        var timings = new List<(string Key, TimeSpan? Cold, TimeSpan? Warm)>();
        var outOfTime = new HashSet<string>(StringComparer.Ordinal);
        var stopped = false;
        foreach (var (key, view) in views)
        {
            TimeSpan? cold = null;
            TimeSpan? warm = null;
            try
            {
                if (stopped || budget - elapsed() <= TimeSpan.Zero) throw new TimeoutException();
                cold = await count(view, TimeoutSeconds(budget - elapsed()), cancellationToken).ConfigureAwait(false);
                if (budget - elapsed() <= TimeSpan.Zero) throw new TimeoutException();
                warm = await count(view, TimeoutSeconds(budget - elapsed()), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                stopped = true;
                if (cold is null) outOfTime.Add(key);
            }
            catch (SqlException)
            {
                cold = null; // view missing (older database) or not permitted
                warm = null;
            }
            timings.Add((key, cold, warm));
        }
        return (timings, outOfTime);
    }

    /// <summary>The command timeout for what is left of the budget: at least 1 s, at most the probes' usual 15 s.</summary>
    internal static int TimeoutSeconds(TimeSpan left) =>
        (int)Math.Clamp(Math.Ceiling(left.TotalSeconds), 1, DatabaseProbeSql.CommandTimeoutSeconds);

    private static async Task<TimeSpan> CountAsync(SqlConnection connection, string view, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var one = Stopwatch.StartNew();
        await using var command = ImportDataProbeSupport.ReadOnlyCommand(connection, CountSql(view));
        command.CommandTimeout = timeoutSeconds;
        try
        {
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            throw new TimeoutException("The count ran out of the DB-06 time budget.", exception);
        }
        return one.Elapsed;
    }

    /// <summary>One count per view (1.11.2 form): each time is judged as a repeat read, with no first-read number.</summary>
    internal static HealthCheckResult Evaluate(IReadOnlyList<(string Key, TimeSpan? Elapsed)> timings, TimeSpan duration) =>
        Evaluate([.. timings.Select(item => (item.Key, (TimeSpan?)null, item.Elapsed))], duration);

    /// <summary>
    /// The verdict from each view's first (cold) and repeat (warm) count. Warning only when a warm count is above
    /// <see cref="SlowAfter"/>; a slow cold count with a fast warm one is a Pass whose text still gives both numbers. A view
    /// with a cold count only (no time left for the repeat) is judged on the cold count. Views in <paramref name="outOfTime"/>
    /// are Skipped TIME_BUDGET sub-results. The metric is the slowest judged count in milliseconds.
    /// </summary>
    internal static HealthCheckResult Evaluate(IReadOnlyList<(string Key, TimeSpan? Cold, TimeSpan? Warm)> timings, TimeSpan duration,
        IReadOnlySet<string>? outOfTime = null)
    {
        outOfTime ??= new HashSet<string>(StringComparer.Ordinal);
        var subs = timings.Select(item => Judged(item) is not { } judged
                ? new HealthCheckSubResult(item.Key, HealthOutcome.Skipped, outOfTime.Contains(item.Key) ? TimeBudgetCode : "NOT_READABLE")
                : new HealthCheckSubResult(item.Key, judged > SlowAfter ? HealthOutcome.Warning : HealthOutcome.Pass,
                    judged > SlowAfter ? "SERVICE_VIEW_SLOW" : null, Ms(judged), Fields(item.Cold, item.Warm)))
            .ToList();
        var read = timings.Where(item => Judged(item) is not null).ToList();
        var untimed = timings.Count(item => Judged(item) is null && outOfTime.Contains(item.Key));
        if (read.Count == 0)
            return untimed > 0
                ? ImportDataProbeSupport.Make(HealthCheckId.Db06, HealthOutcome.Skipped, TimeBudgetCode,
                    "The Service views could not be counted within the time budget of this check.", null, duration, null, subs)
                : ImportDataProbeSupport.Make(HealthCheckId.Db06, HealthOutcome.Skipped, "NOT_READABLE",
                    "The Service views could not be read here (not installed or not permitted).", null, duration, null, subs);
        var slowestWarm = read.Max(item => Ms(Judged(item)!.Value));
        var slowestCold = read.Where(item => item.Cold is not null).Select(item => Ms(item.Cold!.Value)).DefaultIfEmpty(-1m).Max();
        var both = Describe(slowestCold, slowestWarm);
        var suffix = untimed > 0 ? ImportDataProbeSupport.Text($" {untimed} view(s) not timed: the time budget ran out.") : "";
        var slow = subs.Count(sub => sub.Outcome == HealthOutcome.Warning);
        if (slow > 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Db06, HealthOutcome.Warning, "SERVICE_VIEW_SLOW",
                ImportDataProbeSupport.Text($"{slow} Service view(s) took over 1 second to count on a repeat read ({both}). Time to plan a faster Service job index.") + suffix,
                null, duration, slowestWarm, subs);
        return ImportDataProbeSupport.Make(HealthCheckId.Db06, HealthOutcome.Pass, HealthCheckTexts.Ok,
            ImportDataProbeSupport.Text($"The Service views answer quickly ({both}).") + suffix, null, duration, slowestWarm, subs);
    }

    /// <summary>The count a view is judged on: the repeat (warm) count, or the first one when no repeat count fitted.</summary>
    private static TimeSpan? Judged((string Key, TimeSpan? Cold, TimeSpan? Warm) item) => item.Warm ?? item.Cold;

    /// <summary>"first read after start took N ms (compile), repeat read M ms"; only the repeat read when no first read was timed.</summary>
    internal static string Describe(decimal coldMs, decimal warmMs) => coldMs < 0
        ? ImportDataProbeSupport.Text($"slowest repeat read {warmMs:0} ms")
        : ImportDataProbeSupport.Text($"first read after start took {coldMs:0} ms (compile), repeat read {warmMs:0} ms");

    private static decimal Ms(TimeSpan elapsed) => (decimal)Math.Round(elapsed.TotalMilliseconds);

    private static Dictionary<string, string> Fields(TimeSpan? cold, TimeSpan? warm)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (warm is { } repeat) fields[WarmField] = Ms(repeat).ToString("0", CultureInfo.InvariantCulture);
        if (cold is { } first) fields[ColdField] = Ms(first).ToString("0", CultureInfo.InvariantCulture);
        return fields;
    }
}

/// <summary>What <see cref="OffPcCopyProbe"/> needs from the PC; tests substitute it.</summary>
public interface IOffPcCopyEnvironment
{
    /// <summary>The folder OneDrive syncs for this user; null when OneDrive is not set up.</summary>
    string? OneDriveRoot();

    /// <summary>The backup encryption key file (<c>keyFile</c> of the OneDrive backup config).</summary>
    string KeyFilePath();

    bool OneDriveIsRunning();
}

public sealed class WindowsOffPcCopyEnvironment : IOffPcCopyEnvironment
{
    public string? OneDriveRoot()
    {
        foreach (var name in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) return value;
        }
        return null;
    }

    public string KeyFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Keys", "onedrive-backup.key");

    public bool OneDriveIsRunning()
    {
        var processes = Process.GetProcessesByName("OneDrive");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}

/// <summary>
/// OPS-10 (Disaster recovery G11): on the PC that copies backups to OneDrive, (1) the newest file in
/// <c>ETP-Backups</c> is under 36 hours old, (2) the encryption key file exists and is NOT inside the OneDrive
/// folder, (3) OneDrive is running. A PC without OneDrive or without an <c>ETP-Backups</c> folder (a shop PC) is
/// Skipped; anything that cannot be read is Skipped, only a measured problem is Warning or Fail. Read-only.
/// </summary>
public sealed class OffPcCopyProbe(IOffPcCopyEnvironment environment, TimeProvider? timeProvider = null) : IHealthProbe
{
    public const int MaximumCopyAgeHours = 36;
    public const string BackupFolderName = "ETP-Backups";

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public IReadOnlyList<string> CheckIds { get; } = [HealthCheckId.Ops10];

    public Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            return Task.FromResult<IReadOnlyList<HealthCheckResult>>([Check(timer)]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Task.FromResult<IReadOnlyList<HealthCheckResult>>([Skipped("NOT_READABLE", "The off-PC copy could not be inspected on this PC.", timer.Elapsed)]);
        }
    }

    private HealthCheckResult Check(Stopwatch timer)
    {
        var root = environment.OneDriveRoot();
        if (string.IsNullOrWhiteSpace(root))
            return Skipped("NO_ONEDRIVE", "OneDrive is not set up for this Windows user, so there is no off-PC copy to check here.", timer.Elapsed);
        var folder = Path.Combine(root, BackupFolderName);
        if (!Directory.Exists(folder))
            return Skipped("NOT_A_BACKUP_PC", "This PC does not copy backups to OneDrive (no ETP-Backups folder).", timer.Elapsed);

        var now = clock.GetUtcNow();
        var newest = Directory.EnumerateFiles(folder, "*.bak.enc").Select(path => File.GetLastWriteTimeUtc(path))
            .OrderDescending().Cast<DateTime?>().FirstOrDefault();
        var key = environment.KeyFilePath();
        var subs = new List<HealthCheckSubResult>();

        if (newest is null)
            subs.Add(new("COPY_AGE", HealthOutcome.Warning, "NO_OFF_PC_COPY"));
        else
        {
            var hours = (decimal)Math.Round((now - new DateTimeOffset(newest.Value, TimeSpan.Zero)).TotalHours, 1);
            subs.Add(hours > MaximumCopyAgeHours
                ? new("COPY_AGE", HealthOutcome.Warning, "OFF_PC_COPY_OLD", hours)
                : new("COPY_AGE", HealthOutcome.Pass, null, hours));
        }

        if (string.IsNullOrWhiteSpace(key) || !File.Exists(key))
            subs.Add(new("KEY_FILE", HealthOutcome.Warning, "KEY_MISSING"));
        else if (IsInside(key, root))
            subs.Add(new("KEY_FILE", HealthOutcome.Fail, "KEY_IN_ONEDRIVE"));
        else
            subs.Add(new("KEY_FILE", HealthOutcome.Pass));

        subs.Add(environment.OneDriveIsRunning()
            ? new("ONEDRIVE_RUNNING", HealthOutcome.Pass)
            : new("ONEDRIVE_RUNNING", HealthOutcome.Warning, "ONEDRIVE_NOT_RUNNING"));

        var age = subs[0].MetricValue;
        var bad = subs.Where(sub => sub.Outcome is HealthOutcome.Fail or HealthOutcome.Warning).ToList();
        if (subs.Any(sub => sub.Outcome == HealthOutcome.Fail))
            return Make(HealthOutcome.Fail, "KEY_IN_ONEDRIVE",
                "The backup encryption key file is inside the OneDrive folder, so the key is stored next to the backups. Move it out and make a new key.", timer.Elapsed, age, subs);
        if (bad.Count > 0)
            return Make(HealthOutcome.Warning, bad[0].FindingCode!,
                ImportDataProbeSupport.Text($"The off-PC backup copy needs attention: {string.Join("; ", bad.Select(Phrase))}."), timer.Elapsed, age, subs);
        return Make(HealthOutcome.Pass, HealthCheckTexts.Ok,
            ImportDataProbeSupport.Text($"The newest off-PC backup copy is {age:0.#} hours old, the key is outside OneDrive and OneDrive is running."), timer.Elapsed, age, subs);
    }

    private static string Phrase(HealthCheckSubResult sub) => sub.FindingCode switch
    {
        "NO_OFF_PC_COPY" => "no copy in OneDrive yet",
        "OFF_PC_COPY_OLD" => FormattableString.Invariant($"newest copy is {sub.MetricValue:0} hours old (limit {MaximumCopyAgeHours})"),
        "KEY_MISSING" => "backup key file not found",
        "ONEDRIVE_NOT_RUNNING" => "OneDrive is not running",
        _ => "check"
    };

    internal static bool IsInside(string path, string folder)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static HealthCheckResult Make(HealthOutcome outcome, string code, string text, TimeSpan duration, decimal? metric, IReadOnlyList<HealthCheckSubResult> subs) =>
        ImportDataProbeSupport.Make(HealthCheckId.Ops10, outcome, code, text, null, duration, metric, subs);

    private static HealthCheckResult Skipped(string code, string text, TimeSpan duration) =>
        ImportDataProbeSupport.Skipped(HealthCheckId.Ops10, code, text, duration);
}
