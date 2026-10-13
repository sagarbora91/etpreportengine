using Etp.Reporting.Application.ErrorRegister;

namespace Etp.Reporting.Application.HealthCheck;

/// <summary>Area of a health check line. Stored as the enum name (<c>diag.health_check_results.area</c>).</summary>
public enum HealthCheckArea
{
    Database,
    Security,
    Reports,
    Imports,
    Data,
    Operations,
    Errors
}

/// <summary>Outcome of one line (design 5.1). A Known never hides a Fail.</summary>
public enum HealthOutcome
{
    /// <summary>Works and current.</summary>
    Pass,
    /// <summary>Works, but needs attention soon.</summary>
    Warning,
    /// <summary>Broken or unsafe now.</summary>
    Fail,
    /// <summary>A Warning the Owner accepted until a date (at most 90 days).</summary>
    Known,
    /// <summary>Could not run in this context; the explanation says why.</summary>
    Skipped
}

/// <summary>What started the run. Stored as the enum name (<c>diag.health_check_runs.trigger_kind</c>).</summary>
public enum HealthCheckTrigger
{
    Manual,
    Nightly
}

/// <summary>The 55 check ids (design 5.2; DB-06, SEC-04 and OPS-10 added in 1.11.2, DB-07 in 1.12). The id text is what is stored and exported.</summary>
public static class HealthCheckId
{
    public const string Db01 = "DB-01";
    public const string Db02 = "DB-02";
    public const string Db03 = "DB-03";
    public const string Db04 = "DB-04";
    public const string Db05 = "DB-05";
    public const string Db06 = "DB-06";

    /// <summary>1.12 A3: database objects (views, procedures, functions) match this release's object files.</summary>
    public const string Db07 = "DB-07";

    public const string Sec01 = "SEC-01";
    public const string Sec02 = "SEC-02";
    public const string Sec03 = "SEC-03";
    public const string Sec04 = "SEC-04";

    public const string Rpt01 = "RPT-01";
    public const string Rpt02 = "RPT-02";
    public const string Rpt03 = "RPT-03";
    public const string Rpt04 = "RPT-04";
    public const string Rpt05 = "RPT-05";
    public const string Rpt06 = "RPT-06";
    public const string Rpt07 = "RPT-07";
    public const string Rpt08 = "RPT-08";
    public const string Rpt09 = "RPT-09";
    public const string Rpt10 = "RPT-10";
    public const string Rpt11 = "RPT-11";
    public const string Rpt12 = "RPT-12";
    public const string Rpt13 = "RPT-13";
    public const string Rpt14 = "RPT-14";
    public const string Rpt15 = "RPT-15";
    public const string Rpt16 = "RPT-16";
    public const string Rpt17 = "RPT-17";
    public const string Rpt18 = "RPT-18";
    public const string Rpt19 = "RPT-19";
    public const string Rpt20 = "RPT-20";
    public const string Rpt21 = "RPT-21";
    public const string Rpt22 = "RPT-22";

    public const string Imp01 = "IMP-01";
    public const string Imp02 = "IMP-02";
    public const string Imp03 = "IMP-03";

    public const string Data01 = "DATA-01";
    public const string Data02 = "DATA-02";
    public const string Data03 = "DATA-03";
    public const string Data04 = "DATA-04";
    public const string Data05 = "DATA-05";

    public const string Ops01 = "OPS-01";
    public const string Ops02 = "OPS-02";
    public const string Ops03 = "OPS-03";
    public const string Ops04 = "OPS-04";
    public const string Ops05 = "OPS-05";
    public const string Ops06 = "OPS-06";
    public const string Ops07 = "OPS-07";
    public const string Ops08 = "OPS-08";
    public const string Ops09 = "OPS-09";
    public const string Ops10 = "OPS-10";

    public const string Err01 = "ERR-01";
    public const string Err02 = "ERR-02";
    public const string Err03 = "ERR-03";
    public const string Err04 = "ERR-04";

    /// <summary>All 55 ids in catalogue order (DB, SEC, RPT, IMP, DATA, OPS, ERR). Run order is the runner's (design 5.3).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Db01, Db02, Db03, Db04, Db05, Db06, Db07,
        Sec01, Sec02, Sec03, Sec04,
        Rpt01, Rpt02, Rpt03, Rpt04, Rpt05, Rpt06, Rpt07, Rpt08, Rpt09, Rpt10, Rpt11,
        Rpt12, Rpt13, Rpt14, Rpt15, Rpt16, Rpt17, Rpt18, Rpt19, Rpt20, Rpt21, Rpt22,
        Imp01, Imp02, Imp03,
        Data01, Data02, Data03, Data04, Data05,
        Ops01, Ops02, Ops03, Ops04, Ops05, Ops06, Ops07, Ops08, Ops09, Ops10,
        Err01, Err02, Err03, Err04
    ];

    /// <summary>The area an id belongs to, from its prefix.</summary>
    public static HealthCheckArea AreaOf(string checkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkId);
        var dash = checkId.IndexOf('-');
        return (dash > 0 ? checkId[..dash] : checkId) switch
        {
            "DB" => HealthCheckArea.Database,
            "SEC" => HealthCheckArea.Security,
            "RPT" => HealthCheckArea.Reports,
            "IMP" => HealthCheckArea.Imports,
            "DATA" => HealthCheckArea.Data,
            "OPS" => HealthCheckArea.Operations,
            "ERR" => HealthCheckArea.Errors,
            _ => throw new ArgumentOutOfRangeException(nameof(checkId), checkId, "Unknown health check id.")
        };
    }
}

/// <summary>
/// One sub-result (per store, per report, per family). Rolled up under its line; never a line of its own.
/// <see cref="Fields"/> holds tokens and numbers only (exported as <c>detail</c>; the privacy guard applies).
/// </summary>
public sealed record HealthCheckSubResult(
    string Key,
    HealthOutcome Outcome,
    string? FindingCode = null,
    decimal? MetricValue = null,
    IReadOnlyDictionary<string, string>? Fields = null);

/// <summary>
/// One health check line (design 5.1). <see cref="FindingCode"/> is stable (for comparisons and Known), for example
/// <c>AUTO_CLOSE_ON</c>; <c>OK</c> for a Pass. <see cref="Explanation"/> is a <c>HealthCheckTexts</c> template filled with
/// tokens only. <see cref="FixTaskId"/> is a <c>TaskNavigation</c> id or null. <see cref="UnchangedSince"/> is set by the
/// runner when the same Warning was present on the previous nightly runs.
/// </summary>
public sealed record HealthCheckResult(
    string CheckId,
    HealthCheckArea Area,
    HealthOutcome Outcome,
    string FindingCode,
    string Explanation,
    string? FixTaskId,
    TimeSpan Duration,
    decimal? MetricValue = null,
    IReadOnlyList<HealthCheckSubResult>? SubResults = null,
    DateOnly? UnchangedSince = null);

/// <summary>Time limits (design 5.3): 120 s for the run, 30 s per report, a hard cap per area.</summary>
public sealed record HealthCheckBudget(
    TimeSpan Total,
    TimeSpan PerReportTimeout,
    IReadOnlyDictionary<HealthCheckArea, TimeSpan> AreaCaps)
{
    public static HealthCheckBudget Default { get; } = new(
        TimeSpan.FromSeconds(120),
        TimeSpan.FromSeconds(30),
        new Dictionary<HealthCheckArea, TimeSpan>
        {
            [HealthCheckArea.Database] = TimeSpan.FromSeconds(10),
            [HealthCheckArea.Security] = TimeSpan.FromSeconds(5),
            [HealthCheckArea.Reports] = TimeSpan.FromSeconds(60),
            [HealthCheckArea.Imports] = TimeSpan.FromSeconds(10),
            [HealthCheckArea.Data] = TimeSpan.FromSeconds(10),
            [HealthCheckArea.Operations] = TimeSpan.FromSeconds(15),
            [HealthCheckArea.Errors] = TimeSpan.FromSeconds(10)
        });
}

/// <summary>The previous run of the same trigger kind, for "unchanged since" and changed-line comparisons.</summary>
public sealed record HealthCheckPreviousRun(
    long HealthCheckRunId,
    DateTimeOffset StartedUtc,
    IReadOnlyList<HealthCheckResult> Results);

/// <summary>
/// What every probe gets. <see cref="BusinessDate"/> is D-1 (yesterday's local date) in manual and nightly runs.
/// <see cref="RetailStoreCodes"/> are the active Retail stores (Service unit excluded).
/// <see cref="Items"/> is a per-run bag the runner shares between probes (for example RPT-16's tender result reused by
/// DATA-02, DATA-01's missing kinds used to skip duplicates); keys are check ids or documented names.
/// </summary>
public sealed record HealthCheckContext(
    DateOnly BusinessDate,
    HealthCheckTrigger Trigger,
    DateTimeOffset StartedUtc,
    IReadOnlyList<string> RetailStoreCodes,
    HealthCheckPreviousRun? PreviousRun,
    HealthCheckBudget Budget,
    IDictionary<string, object> Items);

/// <summary>
/// A group of checks. Read-only by contract (design 5.4): no writes outside <c>diag.*</c>, inline SQL through
/// <c>ReadOnlySqlGuard</c>. Returns one result per id in <see cref="CheckIds"/>; a probe that cannot run returns
/// Skipped lines, never throws for an expected condition.
/// </summary>
public interface IHealthProbe
{
    /// <summary>The ids this probe answers, so the runner can mark them Skipped when the budget is used up.</summary>
    IReadOnlyList<string> CheckIds { get; }

    Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken);
}

/// <summary>Line counts of a run.</summary>
public sealed record HealthCheckCounts(int Pass, int Warning, int Fail, int Known, int Skipped);

/// <summary>A finished run, as recorded by <c>diag.record_health_check_run</c>. <see cref="Overall"/> is Pass, Warning or Fail.</summary>
public sealed record HealthCheckRun(
    HealthCheckTrigger Trigger,
    DateOnly BusinessDate,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string AppVersion,
    string MachineName,
    string RunByRole,
    HealthOutcome Overall,
    HealthCheckCounts Counts,
    string? ExportFileName,
    IReadOnlyList<HealthCheckResult> Results);

/// <summary>One row of <c>diag.load_health_check_runs</c> (no results).</summary>
public sealed record HealthCheckRunSummary(
    long HealthCheckRunId,
    HealthCheckTrigger Trigger,
    DateOnly BusinessDate,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    int DurationMs,
    string AppVersion,
    string MachineName,
    string RunByRole,
    HealthOutcome Overall,
    HealthCheckCounts Counts,
    string? ExportFileName);

/// <summary>The latest Nightly run for the dashboard tile (<c>diag.load_health_check_summary</c>, all roles).</summary>
public sealed record HealthCheckNightlySummary(
    DateOnly BusinessDate,
    DateTimeOffset CompletedUtc,
    HealthOutcome Overall,
    HealthCheckCounts Counts);

/// <summary>A Warning the Owner accepted (<c>diag.health_check_known</c>).</summary>
public sealed record HealthCheckKnownEntry(
    string CheckId,
    string FindingCode,
    DateOnly UntilDate,
    string Reason,
    string SetBy,
    DateTimeOffset SetUtc);

/// <summary>One permission of an etp role (SEC-01). <see cref="State"/> is GRANT, DENY or GRANT_WITH_GRANT_OPTION.</summary>
public sealed record RolePermission(
    string RoleName,
    string ClassDesc,
    string? SchemaName,
    string? ObjectName,
    string Permission,
    string State);

/// <summary>
/// Role membership (SEC-02). For an Owner caller one row per member with <see cref="MemberName"/>; for anyone else
/// <see cref="MemberName"/> is null and rows are grouped with <see cref="MemberCount"/>.
/// </summary>
public sealed record RoleMembership(
    string RoleName,
    string? MemberName,
    bool IsActiveApplicationUser,
    string? ApplicationRoleCode,
    int MemberCount);

/// <summary>
/// The three result sets of <c>diag.load_role_permissions</c> (SEC-01..03).
/// <see cref="OwnersWithoutGrantOption"/> is empty for a non-Owner caller; the count is always filled.
/// </summary>
public sealed record RolePermissionSnapshot(
    IReadOnlyList<RolePermission> Permissions,
    IReadOnlyList<RoleMembership> Memberships,
    IReadOnlyList<string> OwnersWithoutGrantOption,
    int OwnersWithoutGrantOptionCount);

/// <summary>Health check persistence (<c>diag</c> procedures of migration 0055). See docs\design\SELF-DIAGNOSTICS-PROCEDURES.md.</summary>
public interface IHealthCheckStore
{
    /// <summary>Records a run and its results in one transaction; returns the new run id.</summary>
    Task<long> RecordRunAsync(HealthCheckRun run, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HealthCheckRunSummary>> LoadRunsAsync(int top = 30, HealthCheckTrigger? trigger = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HealthCheckResult>> LoadResultsAsync(long healthCheckRunId, CancellationToken cancellationToken = default);

    /// <summary>Null when no Nightly run has been recorded.</summary>
    Task<HealthCheckNightlySummary?> LoadLatestNightlySummaryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HealthCheckKnownEntry>> LoadKnownAsync(CancellationToken cancellationToken = default);

    /// <summary>Owner only; <paramref name="untilDate"/> at most 90 days ahead.</summary>
    Task SetKnownAsync(string checkId, string findingCode, DateOnly untilDate, string reason, CancellationToken cancellationToken = default);

    /// <summary>Owner only.</summary>
    Task ClearKnownAsync(string checkId, string findingCode, CancellationToken cancellationToken = default);

    Task<RolePermissionSnapshot> LoadRolePermissionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Error register persistence (<c>diag</c> procedures of migration 0054). See docs\design\SELF-DIAGNOSTICS-PROCEDURES.md.</summary>
public interface IErrorRegisterStore
{
    /// <summary>At most 500 items per call, oldest first.</summary>
    Task<ErrorIngestResult> IngestAsync(IReadOnlyList<ErrorOccurrence> items, string appVersion, CancellationToken cancellationToken = default);

    /// <summary>Optional filters: one status, or the entry holding a Ref (12 characters).</summary>
    Task<IReadOnlyList<ErrorRegisterEntry>> LoadRegisterAsync(ErrorStatus? status = null, string? reference = null, CancellationToken cancellationToken = default);

    /// <summary>Owner only. Null when the id does not exist.</summary>
    Task<ErrorRegisterEntryDetail?> LoadEntryAsync(int errorRegisterId, CancellationToken cancellationToken = default);

    /// <summary>Owner only. Allowed targets: Seen, Expected, New (undo Expected), Fixed (manual fallback).</summary>
    Task SetStatusAsync(int errorRegisterId, ErrorStatus status, string? fixedInVersion, string? ticket, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Owner only. Moves occurrences and counts of <paramref name="fromId"/> into <paramref name="intoId"/>.</summary>
    Task MergeAsync(int fromId, int intoId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Applies <c>ErrorFixManifest.json</c> (raw JSON) for the running version. Idempotent.</summary>
    Task<ErrorFixManifestResult> ApplyFixManifestAsync(string manifestJson, string runningVersion, CancellationToken cancellationToken = default);
}
