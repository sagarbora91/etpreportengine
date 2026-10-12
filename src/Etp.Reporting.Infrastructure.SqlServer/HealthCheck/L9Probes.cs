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
/// DB-06 (Architecture audit 7, F5/F7): how long the main Service screens' views take to count their rows. A Warning
/// above one second, so the day to materialise the Service views is measured. Read-only SELECT COUNT(*); a missing
/// view or a refusal is Skipped.
/// </summary>
public sealed class ServiceScreenTimingProbe(string connectionString) : IHealthProbe
{
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(1);

    /// <summary>The views behind the Service screens (key, view).</summary>
    internal static readonly IReadOnlyList<(string Key, string View)> Views =
    [
        ("JOBS", "dbo.v_service_job"),
        ("PENDING", "dbo.v_service_pending_current"),
        ("STATUS", "dbo.v_service_status_view_rows"),
        ("CLAIMS", "dbo.v_service_claims"),
        ("PARTS", "dbo.v_service_parts")
    ];

    public IReadOnlyList<string> CheckIds { get; } = [HealthCheckId.Db06];

    internal static string CountSql(string view) => "SELECT COUNT(*) FROM " + view + ";";

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var result = await ImportDataProbeSupport.GuardAsync(HealthCheckId.Db06, async () =>
        {
            var timings = new List<(string Key, TimeSpan? Elapsed)>();
            await using var connection = await DatabaseProbeSql.OpenAsync(connectionString, cancellationToken).ConfigureAwait(false);
            foreach (var (key, view) in Views)
            {
                var one = Stopwatch.StartNew();
                try
                {
                    await using var command = ImportDataProbeSupport.ReadOnlyCommand(connection, CountSql(view));
                    await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    timings.Add((key, one.Elapsed));
                }
                catch (SqlException)
                {
                    timings.Add((key, null)); // view missing (older database) or not permitted
                }
            }
            return Evaluate(timings, timer.Elapsed);
        }, cancellationToken).ConfigureAwait(false);
        return [result];
    }

    internal static HealthCheckResult Evaluate(IReadOnlyList<(string Key, TimeSpan? Elapsed)> timings, TimeSpan duration)
    {
        var subs = timings.Select(item => item.Elapsed is not { } elapsed
                ? new HealthCheckSubResult(item.Key, HealthOutcome.Skipped, "NOT_READABLE")
                : new HealthCheckSubResult(item.Key, elapsed > SlowAfter ? HealthOutcome.Warning : HealthOutcome.Pass,
                    elapsed > SlowAfter ? "SERVICE_VIEW_SLOW" : null, (decimal)Math.Round(elapsed.TotalMilliseconds)))
            .ToList();
        var read = subs.Where(sub => sub.Outcome != HealthOutcome.Skipped).ToList();
        if (read.Count == 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Db06, HealthOutcome.Skipped, "NOT_READABLE",
                "The Service views could not be read here (not installed or not permitted).", null, duration, null, subs);
        var slowest = read.Max(sub => sub.MetricValue ?? 0);
        var slow = read.Count(sub => sub.Outcome == HealthOutcome.Warning);
        if (slow > 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Db06, HealthOutcome.Warning, "SERVICE_VIEW_SLOW",
                ImportDataProbeSupport.Text($"{slow} Service view(s) took over 1 second to count (slowest {slowest:0} ms). Time to plan a faster Service job index."),
                null, duration, slowest, subs);
        return ImportDataProbeSupport.Make(HealthCheckId.Db06, HealthOutcome.Pass, HealthCheckTexts.Ok,
            ImportDataProbeSupport.Text($"The Service views answer quickly (slowest {slowest:0} ms)."), null, duration, slowest, subs);
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
