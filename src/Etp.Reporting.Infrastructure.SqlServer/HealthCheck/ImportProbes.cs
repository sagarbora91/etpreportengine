using System.Data;
using System.Diagnostics;
using System.Globalization;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Import.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>An unresolved failed import: a report code ("UNKNOWN" when the file was never recognised), the outcome and a count.</summary>
internal sealed record ImportFailureRow(string ReportCode, string Outcome, int Count);

/// <summary>The folder reads IMP-03 needs, behind an interface so the rule is testable without a disk.</summary>
internal interface IAutomaticImportFolders
{
    bool Exists(string path);

    /// <summary>Last-write times (UTC) of the files directly in the folder. Counts and ages only; names are never read out.</summary>
    IReadOnlyList<DateTime> FileTimesUtc(string path);
}

internal sealed class DiskAutomaticImportFolders : IAutomaticImportFolders
{
    public bool Exists(string path) => Directory.Exists(path);

    public IReadOnlyList<DateTime> FileTimesUtc(string path) =>
        new DirectoryInfo(path).EnumerateFiles()
            .Where(file => !file.Name.StartsWith("~$", StringComparison.Ordinal))
            .Select(file => file.LastWriteTimeUtc).ToArray();
}

/// <summary>
/// Health checks IMP-01 to IMP-03 (design 5.2). Read-only: IMP-01 is in memory only (<see cref="SyntheticFamilySamples"/>),
/// IMP-02 is one guarded SELECT over <c>dbo.import_attempts</c>, IMP-03 reads the watch-folder settings and lists the
/// Inbound and Failed folders (counts and ages, never file names).
/// </summary>
public sealed class ImportProbes : IHealthProbe
{
    public const string FixConflicts = "conflicts", FixWatchFolder = "watch-folder";

    /// <summary>The Inbound file age after which the automatic import is considered stuck.</summary>
    internal static readonly TimeSpan InboundStuckAfter = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Failed attempts of the last 24 hours that no later clean attempt of the same file name, store and report cleared.
    /// This is the rule of the Import &gt; Problems screen (<c>ImportProblems.FromHistory</c>): a store or report the failed
    /// attempt never learned does not stop the match; a failure with no store is cleared only by a clean row with no store
    /// (the screen's extra same-bytes rule needs the source hash, which this check does not read).
    /// </summary>
    internal const string FailuresSql = """
        SET NOCOUNT ON;
        SELECT COALESCE(a.report_code,'UNKNOWN') report_code,a.outcome,COUNT_BIG(*) failures
        FROM dbo.import_attempts a
        WHERE a.recorded_utc>=DATEADD(hour,-24,SYSUTCDATETIME())
          AND a.outcome IN('Failed','Unknown layout')
          AND NOT EXISTS(
            SELECT 1 FROM dbo.import_attempts c
            WHERE c.import_attempt_id<>a.import_attempt_id AND c.recorded_utc>=a.recorded_utc
              AND c.file_name=a.file_name
              AND c.outcome IN('Imported','empty export','Duplicate','Duplicate content','Already present')
              AND ((a.store_code IS NULL AND c.store_code IS NULL) OR (a.store_code IS NOT NULL AND (c.store_code IS NULL OR c.store_code=a.store_code)))
              AND (a.report_code IS NULL OR c.report_code IS NULL OR c.report_code=a.report_code))
        GROUP BY COALESCE(a.report_code,'UNKNOWN'),a.outcome
        ORDER BY COALESCE(a.report_code,'UNKNOWN'),a.outcome;
        """;

    private readonly Func<IReadOnlyList<SyntheticFamilyCheck>> checkFamilies;
    private readonly Func<bool> checkDailyInputs;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ImportFailureRow>>> loadFailures;
    private readonly Func<CancellationToken, Task<WatchFolderSettings>> loadWatchFolders;
    private readonly IAutomaticImportFolders folders;
    private readonly Func<DateTimeOffset> clock;

    public ImportProbes(string connectionString)
        : this(
            () => SyntheticFamilySamples.CheckAll(),
            SyntheticFamilySamples.CheckDailyInputs,
            LoadFailuresFrom(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString))),
            new Phase2OperationsRepository(SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString))).LoadWatchFolderSettingsAsync,
            new DiskAutomaticImportFolders(),
            () => DateTimeOffset.UtcNow)
    {
    }

    internal ImportProbes(
        Func<IReadOnlyList<SyntheticFamilyCheck>> checkFamilies,
        Func<bool> checkDailyInputs,
        Func<CancellationToken, Task<IReadOnlyList<ImportFailureRow>>> loadFailures,
        Func<CancellationToken, Task<WatchFolderSettings>> loadWatchFolders,
        IAutomaticImportFolders folders,
        Func<DateTimeOffset> clock)
    {
        this.checkFamilies = checkFamilies;
        this.checkDailyInputs = checkDailyInputs;
        this.loadFailures = loadFailures;
        this.loadWatchFolders = loadWatchFolders;
        this.folders = folders;
        this.clock = clock;
    }

    public IReadOnlyList<string> CheckIds { get; } = [HealthCheckId.Imp01, HealthCheckId.Imp02, HealthCheckId.Imp03];

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return
        [
            await ImportDataProbeSupport.GuardAsync(HealthCheckId.Imp01, () => Task.FromResult(RunFamilies()), cancellationToken),
            await ImportDataProbeSupport.GuardAsync(HealthCheckId.Imp02, () => RunFailuresAsync(cancellationToken), cancellationToken),
            await ImportDataProbeSupport.GuardAsync(HealthCheckId.Imp03, () => RunAutomaticImportAsync(context, cancellationToken), cancellationToken)
        ];
    }

    // ---- IMP-01 ----------------------------------------------------------------------------------------------------

    private HealthCheckResult RunFamilies()
    {
        var timer = Stopwatch.StartNew();
        var checks = checkFamilies();
        var daily = checkDailyInputs();
        timer.Stop();
        return EvaluateFamilies(checks, daily, timer.Elapsed);
    }

    internal static HealthCheckResult EvaluateFamilies(IReadOnlyList<SyntheticFamilyCheck> checks, bool dailyInputsRecognised, TimeSpan duration)
    {
        var failures = checks.Where(check => !check.Recognised).ToArray();
        var subs = failures.Select(check => new HealthCheckSubResult(
            check.FamilyCode, HealthOutcome.Fail,
            check.Blockers.Count > 0 ? "FAMILY_BLOCKED" : check.MatchedReportCode is null ? "FAMILY_UNMATCHED" : "FAMILY_MISMATCH",
            null,
            new Dictionary<string, string>
            {
                ["matched"] = check.MatchedReportCode ?? "none",
                ["blockers"] = check.Blockers.Count == 0 ? "none" : HealthCheckTexts.DetailToken(string.Join(";", check.Blockers))
            })).ToList();
        if (!dailyInputsRecognised)
            subs.Add(new HealthCheckSubResult("D001", HealthOutcome.Fail, "FAMILY_UNMATCHED", null,
                new Dictionary<string, string> { ["matched"] = "none", ["blockers"] = "none" }));

        var total = checks.Count + 1;
        if (subs.Count == 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Imp01, HealthOutcome.Pass, "OK",
                ImportDataProbeSupport.Text($"All {checks.Count} export families and the daily-inputs sheet are recognised."),
                null, duration, total);
        return ImportDataProbeSupport.Make(HealthCheckId.Imp01, HealthOutcome.Fail, "FAMILY_NOT_RECOGNISED",
            ImportDataProbeSupport.Text($"{subs.Count} of {total} families are not recognised as themselves: {string.Join(", ", subs.Select(s => s.Key))}. A layout or profile changed; update ETP to the latest version or tell support."),
            null, duration, subs.Count, subs);
    }

    // ---- IMP-02 ----------------------------------------------------------------------------------------------------

    private async Task<HealthCheckResult> RunFailuresAsync(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var rows = await loadFailures(cancellationToken);
        timer.Stop();
        return EvaluateFailures(rows, timer.Elapsed);
    }

    internal static HealthCheckResult EvaluateFailures(IReadOnlyList<ImportFailureRow> rows, TimeSpan duration)
    {
        if (rows.Count == 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Imp02, HealthOutcome.Pass, "OK",
                "No failed or unrecognised import in the last 24 hours is still unresolved.", null, duration, 0);
        var total = rows.Sum(row => row.Count);
        var subs = rows.GroupBy(row => row.ReportCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => new HealthCheckSubResult(group.Key, HealthOutcome.Warning, "IMPORT_FAILED", group.Sum(row => row.Count),
                group.ToDictionary(row => HealthCheckTexts.DetailToken(row.Outcome, 60), row => row.Count.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal)))
            .OrderBy(sub => sub.Key, StringComparer.Ordinal).ToArray();
        var byReport = string.Join(", ", subs.Select(sub => $"{sub.Key} {sub.MetricValue:0}"));
        return ImportDataProbeSupport.Make(HealthCheckId.Imp02, HealthOutcome.Warning, "IMPORT_FAILURES_OPEN",
            ImportDataProbeSupport.Text($"{total} failed or unrecognised imports in the last 24 hours are not resolved by a later clean import (by report: {byReport}). Open Import > Problems."),
            FixConflicts, duration, total, subs);
    }

    private static Func<CancellationToken, Task<IReadOnlyList<ImportFailureRow>>> LoadFailuresFrom(string connectionString) =>
        async cancellationToken =>
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = ImportDataProbeSupport.ReadOnlyCommand(connection, FailuresSql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<ImportFailureRow>();
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new ImportFailureRow(reader.GetString(0), reader.GetString(1), (int)Math.Min(int.MaxValue, reader.GetInt64(2))));
            return rows;
        };

    // ---- IMP-03 ----------------------------------------------------------------------------------------------------

    private async Task<HealthCheckResult> RunAutomaticImportAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var settings = await loadWatchFolders(cancellationToken);
        var now = clock();
        var result = EvaluateAutomaticImport(settings, folders, now, context.PreviousRun?.StartedUtc ?? now.AddHours(-24), timer.Elapsed);
        timer.Stop();
        return result with { Duration = timer.Elapsed };
    }

    internal static HealthCheckResult EvaluateAutomaticImport(
        WatchFolderSettings settings, IAutomaticImportFolders folders, DateTimeOffset now, DateTimeOffset previousRunUtc, TimeSpan duration)
    {
        if (!settings.IsEnabled)
            return ImportDataProbeSupport.Make(HealthCheckId.Imp03, HealthOutcome.Pass, "DISABLED_BY_CHOICE",
                "Automatic import is switched off (a choice in Settings > Automatic import).", null, duration);

        var bad = new List<HealthCheckSubResult>();
        try
        {
            AutomationPathPolicy.Validate(settings.InboundPath, settings.ProcessedPath, settings.FailedPath, settings.ReportOutputPath);
        }
        catch (ArgumentException)
        {
            bad.Add(new HealthCheckSubResult("Folders", HealthOutcome.Fail, "FOLDER_NOT_ALLOWED"));
        }
        foreach (var (name, path) in new[] { ("Inbound", settings.InboundPath), ("Processed", settings.ProcessedPath), ("Failed", settings.FailedPath) })
            if (!folders.Exists(path))
                bad.Add(new HealthCheckSubResult(name, HealthOutcome.Fail, "FOLDER_MISSING"));
        if (bad.Count > 0)
        {
            var badList = string.Join(", ", bad.Select(sub => $"{sub.Key}: {sub.FindingCode}"));
            return ImportDataProbeSupport.Make(HealthCheckId.Imp03, HealthOutcome.Fail, bad[0].FindingCode!,
                ImportDataProbeSupport.Text($"Automatic import is on but its folders cannot be used ({badList}). A folder is missing, a link, not local or shared with another. Open Settings > Automatic import."),
                FixWatchFolder, duration, bad.Count, bad);
        }

        var issues = new List<HealthCheckSubResult>();
        var inbound = folders.FileTimesUtc(settings.InboundPath);
        var stuck = inbound.Where(time => now - new DateTimeOffset(time, TimeSpan.Zero) > InboundStuckAfter).ToArray();
        if (stuck.Length > 0)
        {
            var oldest = (int)(now - new DateTimeOffset(stuck.Min(), TimeSpan.Zero)).TotalMinutes;
            issues.Add(new HealthCheckSubResult("Inbound", HealthOutcome.Warning, "INBOUND_STUCK", stuck.Length,
                new Dictionary<string, string>
                {
                    ["files"] = stuck.Length.ToString(CultureInfo.InvariantCulture),
                    ["oldestMinutes"] = oldest.ToString(CultureInfo.InvariantCulture)
                }));
        }
        var failed = folders.FileTimesUtc(settings.FailedPath)
            .Where(time => new DateTimeOffset(time, TimeSpan.Zero) > previousRunUtc).ToArray();
        if (failed.Length > 0)
            issues.Add(new HealthCheckSubResult("Failed", HealthOutcome.Warning, "FAILED_NEW", failed.Length,
                new Dictionary<string, string> { ["files"] = failed.Length.ToString(CultureInfo.InvariantCulture) }));
        if (issues.Count == 0)
            return ImportDataProbeSupport.Make(HealthCheckId.Imp03, HealthOutcome.Pass, "OK",
                ImportDataProbeSupport.Text($"Automatic import is on; Inbound holds {inbound.Count} files, none older than {InboundStuckAfter.TotalMinutes:0} minutes, and nothing new is in Failed."),
                null, duration, inbound.Count);
        var attention = string.Join("; ", issues.Select(sub => sub.FindingCode == "INBOUND_STUCK"
            ? $"{sub.MetricValue:0} files in Inbound for over {InboundStuckAfter.TotalMinutes:0} minutes (oldest {sub.Fields!["oldestMinutes"]} minutes; the task is not running or stuck)"
            : $"{sub.MetricValue:0} new files in Failed"));
        return ImportDataProbeSupport.Make(HealthCheckId.Imp03, HealthOutcome.Warning, issues[0].FindingCode!,
            ImportDataProbeSupport.Text($"Automatic import needs attention: {attention}. Open Settings > Automatic import."),
            FixWatchFolder, duration, issues.Sum(sub => sub.MetricValue ?? 0), issues);
    }
}

/// <summary>Helpers shared by <see cref="ImportProbes"/> and <see cref="DataProbes"/>: result building, the read-only guard, failure mapping.</summary>
internal static class ImportDataProbeSupport
{
    public static HealthCheckResult Make(string id, HealthOutcome outcome, string code, string text, string? fixTaskId, TimeSpan duration,
        decimal? metric = null, IReadOnlyList<HealthCheckSubResult>? subs = null) =>
        new(id, HealthCheckId.AreaOf(id), outcome, code, text, fixTaskId, duration, metric, subs);

    public static HealthCheckResult Skipped(string id, string code, string text, TimeSpan duration) =>
        Make(id, HealthOutcome.Skipped, code, text, null, duration);

    /// <summary>Invariant-culture text from an interpolated string (counts and dates print the same on every PC).</summary>
    public static string Text(FormattableString text) => FormattableString.Invariant(text);

    public static string Day(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// A command for text that passed <see cref="ReadOnlySqlGuard"/>; a text that is not read-only throws before any command exists (design 5.4).
    /// </summary>
    public static SqlCommand ReadOnlyCommand(SqlConnection connection, string sql)
    {
        var violations = ReadOnlySqlGuard.Violations(sql);
        if (violations.Count > 0)
            throw new InvalidOperationException("A health check refused a SQL text that is not read-only: " + string.Join(", ", violations) + ".");
        return new SqlCommand(sql, connection) { CommandType = CommandType.Text, CommandTimeout = 15 };
    }

    /// <summary>
    /// Runs one check. A database that cannot be read gives a Skipped line (never a Fail: the cause is DB-01's);
    /// any other exception gives a Fail line carrying the exception type only (no message, which could hold data).
    /// </summary>
    public static async Task<HealthCheckResult> GuardAsync(string id, Func<Task<HealthCheckResult>> run, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            return await run();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqlException exception)
        {
            return Skipped(id, "DATABASE_NOT_READABLE",
                Text($"Could not read the database for this check (SQL error {exception.Number}); see DB-01 and the other database checks."), timer.Elapsed);
        }
        catch (Exception exception)
        {
            return Make(id, HealthOutcome.Fail, "PROBE_EXCEPTION",
                Text($"The check itself failed with {exception.GetType().Name}. Tell support."), null, timer.Elapsed);
        }
    }
}
