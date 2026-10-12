extern alias EtpApplication;

using System.Diagnostics;
using System.Globalization;
using Etp.Reporting.Desktop.Diagnostics;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;
using EtpApplication::Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.Desktop.Modules.HealthCheck;

/// <summary>
/// ERR-01..04 of the 1.11.0 self-diagnostics (design 5.2, Errors). ERR-01 reports the ingest that the health run did just
/// before the runner (design 4.3); ERR-02..04 read the register once (<c>diag.load_error_register</c>, Owner and automation
/// only) and look at what changed since the previous run of the same trigger (or the last 24 hours when there is none).
/// Read-only: the register read is a procedure. Lines carry ER ids, event codes and counts only.
/// A permission refusal of the register read makes ERR-02..04 Skipped, never Fail.
/// </summary>
public sealed class ErrorProbes : IHealthProbe
{
    public const string IngestFailed = "INGEST_FAILED";
    public const string IngestBusy = "INGEST_BUSY";
    public const string IngestNotRun = "INGEST_NOT_RUN";
    public const string EntriesRejected = "ENTRIES_REJECTED";
    public const string NewErrors = "NEW_ERRORS";
    public const string NewCritical = "NEW_CRITICAL";
    public const string Recurred = "RECURRED";
    public const string CrashLike = "CRASH_LIKE";
    public const string RegisterNotReadable = "REGISTER_NOT_READABLE";

    /// <summary>Event bases that mean the app (or a background task) fell over (design 5.2, ERR-04), plus any <c>*_UNHANDLED</c>.</summary>
    public static IReadOnlySet<string> CrashLikeEvents { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "DISPATCHER_UNHANDLED", "APPDOMAIN_UNHANDLED", "TASK_UNOBSERVED", "STARTUP_FAILED", "WINDOW_CLOSE_FAILED"
    };

    /// <summary>At most this many sub-results per line (the runner trims further if needed).</summary>
    public const int MaxSubResults = 10;

    private readonly Func<CancellationToken, Task<IReadOnlyList<ErrorRegisterEntry>>>? loadRegister;
    private readonly Func<ErrorRegisterIngestRun?>? ingestResult;
    private readonly bool ingestAttempted;

    /// <param name="loadRegister">Reads the register (all statuses, merged entries excluded). Null: ERR-02..04 Skipped.</param>
    /// <param name="ingestResult">The ingest this run did; returns null when the ingest threw. Null delegate: ERR-01 Skipped (no ingest in this run).</param>
    public ErrorProbes(
        Func<CancellationToken, Task<IReadOnlyList<ErrorRegisterEntry>>>? loadRegister,
        Func<ErrorRegisterIngestRun?>? ingestResult)
    {
        this.loadRegister = loadRegister;
        this.ingestResult = ingestResult;
        ingestAttempted = ingestResult is not null;
    }

    public IReadOnlyList<string> CheckIds { get; } = [HealthCheckId.Err01, HealthCheckId.Err02, HealthCheckId.Err03, HealthCheckId.Err04];

    public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var results = new List<HealthCheckResult>(4);
        var clock = Stopwatch.StartNew();
        results.Add(EvaluateIngest(ingestAttempted, ingestResult?.Invoke(), clock.Elapsed));

        var since = context.PreviousRun?.StartedUtc ?? context.StartedUtc.AddDays(-1);
        clock.Restart();
        if (loadRegister is null)
        {
            results.AddRange(SkippedRegister(HealthCheckTexts.NotAvailable, HealthCheckTexts.SkippedNotAvailable, clock.Elapsed));
            return results;
        }
        IReadOnlyList<ErrorRegisterEntry> register;
        try
        {
            register = await loadRegister(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (DesktopProbeFailures.IsPermissionDenied(exception))
        {
            results.AddRange(SkippedRegister(HealthCheckTexts.NotPermitted, HealthCheckTexts.SkippedNotPermitted, clock.Elapsed));
            return results;
        }
        var elapsed = clock.Elapsed;
        results.Add(EvaluateNew(register, since, elapsed));
        results.Add(EvaluateRecurred(register, since, elapsed));
        results.Add(EvaluateCrashLike(register, since, elapsed));
        return results;
    }

    /// <summary>ERR-01: Pass = ingested; Warning = entries rejected or skipped as invalid; Fail = the ingest failed.</summary>
    public static HealthCheckResult EvaluateIngest(bool attempted, ErrorRegisterIngestRun? run, TimeSpan duration)
    {
        if (!attempted)
            return Line(HealthCheckId.Err01, HealthOutcome.Skipped, IngestNotRun, "Skipped: this run did not read the diagnostics log into the error register.", null, duration);
        if (run is null || run.Failed)
            return Line(HealthCheckId.Err01, HealthOutcome.Fail, IngestFailed,
                "The diagnostics log could not be read into the error register, so the register is not current. The diagnostics log has the details (ERROR_REGISTER_INGEST_FAILED).",
                "error-register", duration);
        if (run.AlreadyRunning)
            return Line(HealthCheckId.Err01, HealthOutcome.Skipped, IngestBusy, "Skipped: another reading of the diagnostics log was in progress.", null, duration);
        var rejected = run.Rejected + run.SkippedInvalid;
        if (rejected > 0)
            return Line(HealthCheckId.Err01, HealthOutcome.Warning, EntriesRejected,
                $"{Count(run.Accepted)} log entries were added to the error register; {Count(rejected)} could not be registered (invalid entry).",
                "error-register", duration, rejected);
        return Line(HealthCheckId.Err01, HealthOutcome.Pass, HealthCheckTexts.Ok,
            $"The error register is current ({Count(run.Accepted)} new log entries added, {Count(run.Duplicates)} already present).",
            null, duration, run.Accepted);
    }

    /// <summary>ERR-02: New entries first seen since <paramref name="since"/>. Warning for Errors, Fail for any Critical.</summary>
    public static HealthCheckResult EvaluateNew(IReadOnlyList<ErrorRegisterEntry> register, DateTimeOffset since, TimeSpan duration)
    {
        var fresh = register.Where(entry => entry.MergedIntoId is null && entry.Status == ErrorStatus.New && entry.FirstSeenUtc >= since)
            .OrderByDescending(entry => entry.MaxSeverity).ThenBy(entry => entry.ErrorRegisterId).ToArray();
        if (fresh.Length == 0)
            return Line(HealthCheckId.Err02, HealthOutcome.Pass, HealthCheckTexts.Ok, "No new errors since the previous run.", null, duration, 0);
        var critical = fresh.Count(entry => entry.MaxSeverity == ErrorRegisterSeverity.Critical);
        var outcome = critical > 0 ? HealthOutcome.Fail : HealthOutcome.Warning;
        var text = critical > 0
            ? $"{Count(fresh.Length)} new error(s) since the previous run, {Count(critical)} of them critical (first: {fresh[0].DisplayId} {Token(fresh[0].EventCode)})."
            : $"{Count(fresh.Length)} new error(s) since the previous run (first: {fresh[0].DisplayId} {Token(fresh[0].EventCode)}).";
        return Line(HealthCheckId.Err02, outcome, critical > 0 ? NewCritical : NewErrors, text, "error-register", duration, fresh.Length,
            Subs(fresh, entry => entry.MaxSeverity == ErrorRegisterSeverity.Critical ? HealthOutcome.Fail : HealthOutcome.Warning));
    }

    /// <summary>ERR-03: any entry still Recurred (a fix did not hold) is a Fail; the text says how many recurred since the previous run.</summary>
    public static HealthCheckResult EvaluateRecurred(IReadOnlyList<ErrorRegisterEntry> register, DateTimeOffset since, TimeSpan duration)
    {
        var recurred = register.Where(entry => entry.MergedIntoId is null && entry.Status == ErrorStatus.Recurred)
            .OrderByDescending(entry => entry.RecurredUtc).ThenBy(entry => entry.ErrorRegisterId).ToArray();
        if (recurred.Length == 0)
            return Line(HealthCheckId.Err03, HealthOutcome.Pass, HealthCheckTexts.Ok, "No fixed error has come back.", null, duration, 0);
        var sinceLast = recurred.Count(entry => entry.RecurredUtc is { } at && at >= since);
        return Line(HealthCheckId.Err03, HealthOutcome.Fail, Recurred,
            $"{Count(recurred.Length)} error(s) marked fixed came back ({Count(sinceLast)} since the previous run; first: {recurred[0].DisplayId} {Token(recurred[0].EventCode)}, fixed in {Token(recurred[0].FixedInVersion)}).",
            "error-register", duration, recurred.Length, Subs(recurred, _ => HealthOutcome.Fail));
    }

    /// <summary>ERR-04: crash-like events seen since <paramref name="since"/> (Expected entries are left out).</summary>
    public static HealthCheckResult EvaluateCrashLike(IReadOnlyList<ErrorRegisterEntry> register, DateTimeOffset since, TimeSpan duration)
    {
        var crashes = register.Where(entry => entry.MergedIntoId is null && entry.Status != ErrorStatus.Expected
                && entry.LastSeenUtc >= since && IsCrashLike(entry.EventCode))
            .OrderBy(entry => entry.ErrorRegisterId).ToArray();
        if (crashes.Length == 0)
            return Line(HealthCheckId.Err04, HealthOutcome.Pass, HealthCheckTexts.Ok, "No crash-like errors since the previous run.", null, duration, 0);
        return Line(HealthCheckId.Err04, HealthOutcome.Fail, CrashLike,
            $"{Count(crashes.Length)} crash-like error(s) since the previous run (first: {crashes[0].DisplayId} {Token(crashes[0].EventCode)}).",
            "error-register", duration, crashes.Length, Subs(crashes, _ => HealthOutcome.Fail));
    }

    public static bool IsCrashLike(string? eventCode) =>
        !string.IsNullOrEmpty(eventCode)
        && (CrashLikeEvents.Contains(eventCode) || eventCode.EndsWith("_UNHANDLED", StringComparison.Ordinal));

    private static IEnumerable<HealthCheckResult> SkippedRegister(string code, string text, TimeSpan duration) =>
        [
            Line(HealthCheckId.Err02, HealthOutcome.Skipped, code, text, null, duration),
            Line(HealthCheckId.Err03, HealthOutcome.Skipped, code, text, null, TimeSpan.Zero),
            Line(HealthCheckId.Err04, HealthOutcome.Skipped, code, text, null, TimeSpan.Zero)
        ];

    private static IReadOnlyList<HealthCheckSubResult> Subs(IEnumerable<ErrorRegisterEntry> entries, Func<ErrorRegisterEntry, HealthOutcome> outcome) =>
        entries.Take(MaxSubResults).Select(entry => new HealthCheckSubResult(
            entry.DisplayId,
            outcome(entry),
            null,
            entry.OccurrenceCount,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["event"] = HealthCheckTexts.SafeToken(entry.EventCode, 60),
                ["severity"] = entry.MaxSeverity.ToString(),
                ["status"] = entry.Status.ToString()
            })).ToArray();

    private static HealthCheckResult Line(string id, HealthOutcome outcome, string code, string text, string? fix, TimeSpan duration,
        decimal? metric = null, IReadOnlyList<HealthCheckSubResult>? subs = null) =>
        new(id, HealthCheckArea.Errors, outcome, code, text, fix, duration, metric, subs);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Token(string? value) => HealthCheckTexts.SafeToken(value, 60);
}
