using System.Text.Json;
using System.Text.Json.Serialization;

namespace Etp.Reporting.Application.HealthCheck;

/// <summary>Progress of a run, for the screen's "Reports 7 of 22..." line.</summary>
public sealed record HealthCheckProgress(HealthCheckArea Area, int CompletedChecks, int TotalChecks);

/// <summary>Everything the runner needs besides the probes. Only <see cref="Store"/> needs the database.</summary>
public sealed class HealthCheckRunnerOptions
{
    /// <summary>Previous runs, Known entries and run history. Null: no comparison, no Known (tests, or no store yet).</summary>
    public IHealthCheckStore? Store { get; init; }

    /// <summary>Active Retail store codes (Service unit excluded). Loaded after the Database area when the database answers.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<string>>>? LoadRetailStoreCodes { get; init; }

    public HealthCheckBudget Budget { get; init; } = HealthCheckBudget.Default;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public string AppVersion { get; init; } = "0.0.0.0";

    public string MachineName { get; init; } = Environment.MachineName;

    /// <summary>Owner, Automation, StoreManager, Viewer or Unknown (the procedures' caller role words).</summary>
    public string RunByRole { get; init; } = "Unknown";

    /// <summary>
    /// Called when a probe throws (the line becomes Fail <c>PROBE_FAILED</c>). The composition root records a diagnostics
    /// entry here (event <c>HEALTH_PROBE_FAILED</c>), so the failure also reaches the error register.
    /// </summary>
    public Action<string, Exception>? ProbeFailed { get; init; }

    public IProgress<HealthCheckProgress>? Progress { get; init; }

    /// <summary>How many earlier Nightly runs "unchanged since" may look back over.</summary>
    public int UnchangedSinceLookback { get; init; } = 30;

    /// <summary>Time allowed for loading history, Known entries and stores (design 5.3 "record + export" share).</summary>
    public TimeSpan HistoryTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Runs the health check (design 5.1-5.3): probes in area order Database, Errors, Operations, Security, Data, Imports,
/// Reports; a hard time budget for the run and per area, after which the remaining checks are Skipped (time budget);
/// every other SQL check Skipped when DB-01 fails; exactly one line per check id (51); Known applied per line and per
/// sub-finding, never over a Fail; "unchanged since" from the earlier Nightly runs. The runner records nothing: the
/// caller (nightly schedule or screen) writes the export, then calls <see cref="IHealthCheckStore.RecordRunAsync"/>.
/// <para>
/// DATA-02 and RPT-16 (Step 0 watch-point): the order stays as designed, so the cheap and important areas always run
/// within the budget. DATA-02 does not wait for RPT-16: the Data probe (L6) runs its own tender reconciliation for D-1
/// and reads DATA-01's gaps from <see cref="HealthCheckContext.Items"/> (<c>"DATA-01.missing"</c>), so DATA-01 must be
/// answered before DATA-02 (same probe, or the DATA-01 probe registered first: probes of one area run in registration
/// order). RPT-16 still runs its own report call, because it must time the report path.
/// </para>
/// </summary>
public sealed class HealthCheckRunner
{
    /// <summary>Design 5.3: the cheapest and most important areas first.</summary>
    public static IReadOnlyList<HealthCheckArea> AreaOrder { get; } =
    [
        HealthCheckArea.Database, HealthCheckArea.Errors, HealthCheckArea.Operations, HealthCheckArea.Security,
        HealthCheckArea.Data, HealthCheckArea.Imports, HealthCheckArea.Reports
    ];

    /// <summary>Checks that need no SQL (memory, files, Windows APIs); they still run when DB-01 fails.</summary>
    public static IReadOnlySet<string> RunsWithoutDatabase { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        HealthCheckId.Imp01, HealthCheckId.Ops03, HealthCheckId.Ops05, HealthCheckId.Ops08, HealthCheckId.Ops09
    };

    /// <summary><see cref="HealthCheckContext.Items"/> key, value <c>true</c>: DB-01 failed, do not open a connection.</summary>
    public const string DatabaseUnreachableItem = "runner:database-unreachable";

    /// <summary>Above this many characters of serialised sub-results the runner trims them (detail_json is 2000).</summary>
    internal const int MaxDetailCharacters = 1800;

    /// <summary>Known may last at most 90 days from the day it was set (design 5.1, Q2).</summary>
    public const int MaxKnownDays = 90;

    private static readonly JsonSerializerOptions DetailJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IReadOnlyList<IHealthProbe> probes;
    private readonly HealthCheckRunnerOptions options;

    public HealthCheckRunner(IEnumerable<IHealthProbe> probes, HealthCheckRunnerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(probes);
        this.probes = probes.ToArray();
        this.options = options ?? new HealthCheckRunnerOptions();
        foreach (var probe in this.probes)
        {
            if (probe.CheckIds is null || probe.CheckIds.Count == 0)
                throw new ArgumentException("Every health probe must name the check ids it answers.", nameof(probes));
            foreach (var id in probe.CheckIds) HealthCheckId.AreaOf(id);
        }
    }

    /// <summary>
    /// Runs every probe once. Throws <see cref="OperationCanceledException"/> only when <paramref name="cancellationToken"/>
    /// is cancelled (the user pressed Cancel); budget overruns become Skipped lines.
    /// </summary>
    public async Task<HealthCheckRun> RunAsync(HealthCheckTrigger trigger, CancellationToken cancellationToken = default)
    {
        var clock = options.TimeProvider;
        var budget = options.Budget;
        var startedUtc = clock.GetUtcNow();
        var runStart = clock.GetTimestamp();
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var items = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new HealthCheckContext(today.AddDays(-1), trigger, startedUtc, [], null, budget, items);

        var results = new Dictionary<string, HealthCheckResult>(StringComparer.Ordinal);
        var catalogue = new HashSet<string>(HealthCheckId.All, StringComparer.Ordinal);
        var databaseUnreachable = false;
        var historyLoaded = false;
        IReadOnlyList<HealthCheckKnownEntry> known = [];
        IReadOnlyList<HealthCheckRunSummary> nightlyRuns = [];

        foreach (var area in AreaOrder)
        {
            if (area != HealthCheckArea.Database && !historyLoaded)
            {
                historyLoaded = true;
                if (!databaseUnreachable)
                {
                    var history = await LoadHistoryAsync(trigger, cancellationToken).ConfigureAwait(false);
                    known = history.Known;
                    nightlyRuns = history.NightlyRuns;
                    context = context with { RetailStoreCodes = history.StoreCodes, PreviousRun = history.PreviousRun };
                }
            }

            var areaStart = clock.GetTimestamp();
            var areaCap = budget.AreaCaps.TryGetValue(area, out var cap) ? cap : budget.Total;
            // The probe answering DB-01 runs first (stable otherwise), so no SQL probe runs before the connection is known.
            foreach (var probe in probes.Where(candidate => HealthCheckId.AreaOf(candidate.CheckIds[0]) == area)
                         .OrderBy(candidate => candidate.CheckIds.Contains(HealthCheckId.Db01) ? 0 : 1))
            {
                var ids = probe.CheckIds.Where(id => catalogue.Contains(id) && !results.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray();
                if (ids.Length == 0) continue;
                options.Progress?.Report(new HealthCheckProgress(area, results.Count, catalogue.Count));

                if (databaseUnreachable && ids.All(id => !RunsWithoutDatabase.Contains(id)))
                {
                    foreach (var id in ids) results[id] = Skipped(id, HealthCheckTexts.DatabaseUnreachable, HealthCheckTexts.SkippedDatabaseUnreachable);
                    continue;
                }

                var allowance = Min(budget.Total - clock.GetElapsedTime(runStart), areaCap - clock.GetElapsedTime(areaStart));
                if (allowance <= TimeSpan.Zero)
                {
                    foreach (var id in ids) results[id] = Skipped(id, HealthCheckTexts.TimeBudget, HealthCheckTexts.SkippedTimeBudget(area));
                    continue;
                }

                foreach (var result in await RunProbeAsync(probe, ids, area, context, allowance, cancellationToken).ConfigureAwait(false))
                {
                    var line = databaseUnreachable && !RunsWithoutDatabase.Contains(result.CheckId)
                        ? Skipped(result.CheckId, HealthCheckTexts.DatabaseUnreachable, HealthCheckTexts.SkippedDatabaseUnreachable)
                        : result;
                    results[line.CheckId] = line;
                }

                if (!databaseUnreachable && results.TryGetValue(HealthCheckId.Db01, out var db01) && db01.Outcome == HealthOutcome.Fail)
                {
                    databaseUnreachable = true;
                    items[DatabaseUnreachableItem] = true;
                }
            }
        }

        var lines = HealthCheckId.All
            .Select(id => results.TryGetValue(id, out var line) ? line : Skipped(id, HealthCheckTexts.NotAvailable, HealthCheckTexts.SkippedNotAvailable))
            .Select(line => ApplyKnown(line, known, today))
            .ToList();
        if (!databaseUnreachable && nightlyRuns.Count > 0)
            lines = (await ApplyUnchangedSinceAsync(lines, nightlyRuns, cancellationToken).ConfigureAwait(false)).ToList();
        var final = lines.Select(Normalise).ToArray();

        options.Progress?.Report(new HealthCheckProgress(HealthCheckArea.Reports, final.Length, final.Length));
        var counts = CountOutcomes(final);
        return new HealthCheckRun(
            trigger, context.BusinessDate, startedUtc, clock.GetUtcNow(), options.AppVersion, options.MachineName,
            options.RunByRole, Overall(counts), counts, null, final);
    }

    /// <summary>Pass, Warning or Fail: Known and Skipped lines do not count (design 5.1).</summary>
    public static HealthOutcome Overall(HealthCheckCounts counts) =>
        counts.Fail > 0 ? HealthOutcome.Fail : counts.Warning > 0 ? HealthOutcome.Warning : HealthOutcome.Pass;

    public static HealthCheckCounts CountOutcomes(IEnumerable<HealthCheckResult> lines)
    {
        int pass = 0, warning = 0, fail = 0, knownCount = 0, skipped = 0;
        foreach (var line in lines)
        {
            switch (line.Outcome)
            {
                case HealthOutcome.Pass: pass++; break;
                case HealthOutcome.Warning: warning++; break;
                case HealthOutcome.Fail: fail++; break;
                case HealthOutcome.Known: knownCount++; break;
                default: skipped++; break;
            }
        }
        return new(pass, warning, fail, knownCount, skipped);
    }

    private async Task<IReadOnlyList<HealthCheckResult>> RunProbeAsync(
        IHealthProbe probe, IReadOnlyList<string> ids, HealthCheckArea area, HealthCheckContext context, TimeSpan allowance,
        CancellationToken cancellationToken)
    {
        var clock = options.TimeProvider;
        using var budgetSource = new CancellationTokenSource(allowance, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budgetSource.Token);
        var started = clock.GetTimestamp();
        IReadOnlyList<HealthCheckResult> answered;
        try
        {
            answered = await probe.RunAsync(context, linked.Token).ConfigureAwait(false) ?? [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (budgetSource.IsCancellationRequested)
        {
            return ids.Select(id => Skipped(id, HealthCheckTexts.TimeBudget, HealthCheckTexts.SkippedTimeBudget(area))).ToArray();
        }
        catch (Exception exception)
        {
            options.ProbeFailed?.Invoke(ids[0], exception);
            var elapsed = clock.GetElapsedTime(started);
            return ids.Select(id => new HealthCheckResult(
                id, HealthCheckId.AreaOf(id), HealthOutcome.Fail, HealthCheckTexts.ProbeFailed,
                HealthCheckTexts.ProbeFailedText(exception.GetType().Name), null, elapsed / ids.Count)).ToArray();
        }

        var duration = clock.GetElapsedTime(started);
        var lines = new List<HealthCheckResult>(ids.Count);
        foreach (var id in ids)
        {
            var result = answered.FirstOrDefault(candidate => candidate is not null && string.Equals(candidate.CheckId, id, StringComparison.Ordinal));
            if (result is null)
            {
                // A probe that stopped at the budget without throwing still owes its lines: they were not checked.
                lines.Add(budgetSource.IsCancellationRequested
                    ? Skipped(id, HealthCheckTexts.TimeBudget, HealthCheckTexts.SkippedTimeBudget(area))
                    : new HealthCheckResult(id, HealthCheckId.AreaOf(id), HealthOutcome.Fail, HealthCheckTexts.ProbeNoResult,
                        HealthCheckTexts.ProbeNoResultText, null, TimeSpan.Zero));
                continue;
            }
            lines.Add(result with
            {
                Area = HealthCheckId.AreaOf(id),
                Duration = result.Duration > TimeSpan.Zero ? result.Duration : duration / ids.Count
            });
        }
        return lines;
    }

    private sealed record History(
        IReadOnlyList<string> StoreCodes,
        HealthCheckPreviousRun? PreviousRun,
        IReadOnlyList<HealthCheckKnownEntry> Known,
        IReadOnlyList<HealthCheckRunSummary> NightlyRuns);

    /// <summary>
    /// Stores, the previous run of the same trigger, Known entries and recent Nightly runs. Each part may fail on its own
    /// (an account without the right, a missing procedure): the run goes on without it. Never throws except for Cancel.
    /// </summary>
    private async Task<History> LoadHistoryAsync(HealthCheckTrigger trigger, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(options.HistoryTimeout, options.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;

        IReadOnlyList<string> stores = [];
        if (options.LoadRetailStoreCodes is { } loadStores)
            stores = await Tolerant(() => loadStores(token), [], cancellationToken).ConfigureAwait(false);

        var store = options.Store;
        if (store is null) return new History(stores, null, [], []);

        var knownEntries = await Tolerant(() => store.LoadKnownAsync(token), [], cancellationToken).ConfigureAwait(false);
        var nightly = await Tolerant(
            () => store.LoadRunsAsync(Math.Max(1, options.UnchangedSinceLookback), HealthCheckTrigger.Nightly, token),
            [], cancellationToken).ConfigureAwait(false);
        var sameTrigger = trigger == HealthCheckTrigger.Nightly
            ? nightly
            : await Tolerant(() => store.LoadRunsAsync(1, trigger, token), [], cancellationToken).ConfigureAwait(false);

        HealthCheckPreviousRun? previous = null;
        if (sameTrigger.Count > 0)
        {
            var latest = sameTrigger.OrderByDescending(run => run.StartedUtc).First();
            var previousResults = await TolerantOrNull(() => store.LoadResultsAsync(latest.HealthCheckRunId, token), cancellationToken).ConfigureAwait(false);
            if (previousResults is not null) previous = new HealthCheckPreviousRun(latest.HealthCheckRunId, latest.StartedUtc, previousResults);
        }
        return new History(stores, previous, knownEntries, nightly);
    }

    private static async Task<T> Tolerant<T>(Func<Task<T>> load, T fallback, CancellationToken cancellationToken) where T : class =>
        await TolerantOrNull(load, cancellationToken).ConfigureAwait(false) ?? fallback;

    private static async Task<T?> TolerantOrNull<T>(Func<Task<T>> load, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await load().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Q2 (decision 33): Known per line and per sub-finding, for Warnings only. An entry counts while
    /// <c>until_date</c> is today or later and at most <see cref="MaxKnownDays"/> days after it was set. A Fail line or a
    /// Fail sub-result is never changed, and a Warning sub-result that is not Known keeps its line a Warning.
    /// </summary>
    public static HealthCheckResult ApplyKnown(HealthCheckResult line, IReadOnlyList<HealthCheckKnownEntry> known, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (known is null || known.Count == 0) return line;
        var active = known
            .Where(entry => string.Equals(entry.CheckId, line.CheckId, StringComparison.Ordinal)
                && entry.UntilDate >= today
                && entry.UntilDate <= DateOnly.FromDateTime(entry.SetUtc.UtcDateTime).AddDays(MaxKnownDays))
            .ToArray();
        if (active.Length == 0) return line;

        HealthCheckKnownEntry? Match(string? findingCode) =>
            findingCode is null ? null : active.FirstOrDefault(entry => string.Equals(entry.FindingCode, findingCode, StringComparison.Ordinal));

        var subs = line.SubResults;
        var knownSubs = 0;
        if (subs is { Count: > 0 })
        {
            subs = subs.Select(sub =>
            {
                if (sub.Outcome != HealthOutcome.Warning || Match(sub.FindingCode) is null) return sub;
                knownSubs++;
                return sub with { Outcome = HealthOutcome.Known };
            }).ToArray();
        }

        if (line.Outcome != HealthOutcome.Warning) return knownSubs > 0 ? line with { SubResults = subs } : line;

        IReadOnlyList<HealthCheckSubResult> current = subs ?? [];
        var remaining = current.Where(sub => sub.Outcome is HealthOutcome.Warning or HealthOutcome.Fail).ToArray();
        if (remaining.Length > 0)
        {
            // Still a Warning. When the line's own finding code is Known, the line takes the code of the first warning
            // that is not, so the comparison with the next run and a later Known follow what is still open.
            if (knownSubs == 0) return line;
            return line with
            {
                SubResults = subs,
                FindingCode = Match(line.FindingCode) is not null ? remaining[0].FindingCode ?? line.FindingCode : line.FindingCode,
                Explanation = HealthCheckTexts.Clip(line.Explanation + HealthCheckTexts.PartlyKnownSuffix(knownSubs))
            };
        }

        var lineEntry = Match(line.FindingCode);
        if (lineEntry is null && knownSubs == 0) return line;
        var until = lineEntry?.UntilDate
            ?? active.Where(entry => current.Any(sub => sub.Outcome == HealthOutcome.Known && sub.FindingCode == entry.FindingCode))
                .Select(entry => entry.UntilDate).DefaultIfEmpty(today).Min();
        return line with
        {
            Outcome = HealthOutcome.Known,
            SubResults = subs,
            Explanation = HealthCheckTexts.Clip(line.Explanation + HealthCheckTexts.KnownSuffix(until))
        };
    }

    /// <summary>
    /// "Unchanged since dd MMM" (design 5.1): for a Warning or Fail line, the local date of the oldest consecutive
    /// earlier Nightly run with the same outcome and finding code. Reads each earlier run's results at most once and
    /// stops at the first run where no open line still matches.
    /// </summary>
    private async Task<IReadOnlyList<HealthCheckResult>> ApplyUnchangedSinceAsync(
        IReadOnlyList<HealthCheckResult> lines, IReadOnlyList<HealthCheckRunSummary> nightlyRuns, CancellationToken cancellationToken)
    {
        var store = options.Store;
        if (store is null) return lines;
        var open = lines.Where(line => line.Outcome is HealthOutcome.Warning or HealthOutcome.Fail)
            .ToDictionary(line => line.CheckId, StringComparer.Ordinal);
        if (open.Count == 0) return lines;

        using var timeout = new CancellationTokenSource(options.HistoryTimeout, options.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var since = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var run in nightlyRuns.OrderByDescending(run => run.StartedUtc))
        {
            if (open.Count == 0) break;
            var earlier = await TolerantOrNull(() => store.LoadResultsAsync(run.HealthCheckRunId, linked.Token), cancellationToken).ConfigureAwait(false);
            if (earlier is null) break;
            var runDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(run.StartedUtc, options.TimeProvider.LocalTimeZone).DateTime);
            foreach (var id in open.Keys.ToArray())
            {
                var current = open[id];
                var match = earlier.FirstOrDefault(result => string.Equals(result.CheckId, id, StringComparison.Ordinal));
                if (match is not null && match.Outcome == current.Outcome && string.Equals(match.FindingCode, current.FindingCode, StringComparison.Ordinal))
                    since[id] = runDate;
                else
                    open.Remove(id);
            }
        }
        return lines.Select(line => since.TryGetValue(line.CheckId, out var date) ? line with { UnchangedSince = date } : line).ToArray();
    }

    /// <summary>Fits a line to the columns of <c>diag.health_check_results</c> (design 6.2).</summary>
    public static HealthCheckResult Normalise(HealthCheckResult line)
    {
        var explanation = HealthCheckTexts.Clip(HealthCheckTexts.SingleLine(line.Explanation).Trim());
        var fix = line.FixTaskId is { Length: > 0 } task && task.Length <= HealthCheckTexts.MaxFixTaskIdLength
            && HealthCheckTexts.SafeToken(task) == task ? task : null;
        var subs = line.SubResults?.Select(sub => sub.FindingCode is null ? sub : sub with { FindingCode = HealthCheckTexts.SafeCode(sub.FindingCode) }).ToArray();
        return line with
        {
            FindingCode = HealthCheckTexts.SafeCode(line.FindingCode),
            Explanation = explanation.Length > 0 ? explanation : line.Outcome.ToString(),
            FixTaskId = fix,
            Duration = line.Duration < TimeSpan.Zero ? TimeSpan.Zero : line.Duration,
            SubResults = FitSubResults(subs)
        };
    }

    /// <summary>
    /// Keeps the serialised sub-results under <see cref="MaxDetailCharacters"/>: Fail first, then Warning, Known, Skipped,
    /// Pass, and one <c>MORE</c> sub-result counting the ones left out.
    /// </summary>
    public static IReadOnlyList<HealthCheckSubResult>? FitSubResults(IReadOnlyList<HealthCheckSubResult>? subs)
    {
        if (subs is null || subs.Count == 0) return subs;
        if (JsonSerializer.Serialize(subs, DetailJson).Length <= MaxDetailCharacters) return subs;
        var ordered = subs.Select((sub, index) => (sub, index))
            .OrderBy(pair => Severity(pair.sub.Outcome)).ThenBy(pair => pair.index)
            .Select(pair => pair.sub).ToList();
        var kept = new List<HealthCheckSubResult>();
        foreach (var sub in ordered)
        {
            var candidate = kept.Append(sub).Append(More(ordered.Count - kept.Count - 1)).ToArray();
            if (JsonSerializer.Serialize(candidate, DetailJson).Length > MaxDetailCharacters) break;
            kept.Add(sub);
        }
        kept.Add(More(ordered.Count - kept.Count));
        return kept;
    }

    private static HealthCheckSubResult More(int count) =>
        new(HealthCheckTexts.MoreSubResultsKey, HealthOutcome.Skipped, null, count);

    private static int Severity(HealthOutcome outcome) => outcome switch
    {
        HealthOutcome.Fail => 0,
        HealthOutcome.Warning => 1,
        HealthOutcome.Known => 2,
        HealthOutcome.Skipped => 3,
        _ => 4
    };

    private static HealthCheckResult Skipped(string id, string findingCode, string explanation) =>
        new(id, HealthCheckId.AreaOf(id), HealthOutcome.Skipped, findingCode, explanation, null, TimeSpan.Zero);

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
}
