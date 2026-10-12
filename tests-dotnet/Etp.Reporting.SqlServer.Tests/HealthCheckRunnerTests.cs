using Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.11.0 L4: the health check runner (design 5.1-5.3). Order, the 120 s budget and per-area caps, Skipped lines,
/// one line per check id, DB-01 gating, Known per line and per sub-finding (never over a Fail), "unchanged since".
/// </summary>
public sealed class HealthCheckRunnerTests
{
    [Fact]
    public async Task Every_one_of_the_54_ids_gets_exactly_one_line_in_catalogue_order()
    {
        var runner = new HealthCheckRunner([new FakeProbe([HealthCheckId.Db01, HealthCheckId.Db04])], Options());

        var run = await runner.RunAsync(HealthCheckTrigger.Manual);

        Assert.Equal(HealthCheckId.All, run.Results.Select(line => line.CheckId));
        Assert.Equal(54, run.Results.Count);
        Assert.All(run.Results.Where(line => line.CheckId is not (HealthCheckId.Db01 or HealthCheckId.Db04)),
            line => Assert.Equal((HealthOutcome.Skipped, HealthCheckTexts.NotAvailable), (line.Outcome, line.FindingCode)));
        Assert.All(run.Results, line => Assert.Equal(HealthCheckId.AreaOf(line.CheckId), line.Area));
        Assert.Equal(new HealthCheckCounts(2, 0, 0, 0, 52), run.Counts);
        Assert.Equal(HealthOutcome.Pass, run.Overall);
    }

    [Fact]
    public async Task Probes_run_in_the_design_area_order_whatever_order_they_are_registered_in()
    {
        var order = new List<string>();
        FakeProbe Probe(string id) => new([id], onRun: () => order.Add(id));
        var runner = new HealthCheckRunner(
            [Probe(HealthCheckId.Rpt01), Probe(HealthCheckId.Imp01), Probe(HealthCheckId.Data01), Probe(HealthCheckId.Data02),
             Probe(HealthCheckId.Sec01), Probe(HealthCheckId.Ops01), Probe(HealthCheckId.Err01), Probe(HealthCheckId.Db01)],
            Options());

        await runner.RunAsync(HealthCheckTrigger.Nightly);

        Assert.Equal(
            new[] { HealthCheckId.Db01, HealthCheckId.Err01, HealthCheckId.Ops01, HealthCheckId.Sec01, HealthCheckId.Data01,
                HealthCheckId.Data02, HealthCheckId.Imp01, HealthCheckId.Rpt01 },
            order);
    }

    [Fact]
    public async Task Business_date_is_yesterday_and_items_are_shared_between_probes()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 10, 2, 31, 0, TimeSpan.Zero));
        HealthCheckContext? seen = null;
        var writer = new FakeProbe([HealthCheckId.Data01], onContext: context => context.Items["DATA-01.missing"] = "x");
        var reader = new FakeProbe([HealthCheckId.Data02], onContext: context => seen = context);

        // Probes of one area run in registration order, so DATA-01 (registered first) runs before DATA-02.
        var run = await new HealthCheckRunner([writer, reader], Options(clock)).RunAsync(HealthCheckTrigger.Nightly);

        Assert.Equal(new DateOnly(2026, 10, 9), run.BusinessDate);
        Assert.NotNull(seen);
        Assert.Equal(new DateOnly(2026, 10, 9), seen!.BusinessDate);
        Assert.Equal(HealthCheckTrigger.Nightly, seen.Trigger);
        Assert.Equal(new[] { "AX1", "AX2" }, seen.RetailStoreCodes);
        Assert.Equal("x", seen.Items["DATA-01.missing"]);
    }

    [Fact]
    public async Task When_the_run_budget_is_used_up_the_remaining_checks_are_skipped_and_the_run_still_completes()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var slow = new FakeProbe([HealthCheckId.Db01], onRun: () => clock.Advance(TimeSpan.FromSeconds(121)));
        var later = new FakeProbe([HealthCheckId.Err02, HealthCheckId.Err03]);
        var reports = new FakeProbe([HealthCheckId.Rpt01]);
        var options = Options(clock, budget: HealthCheckBudget.Default with
        {
            AreaCaps = HealthCheckArea.Database.CapsWith(TimeSpan.FromSeconds(500))
        });

        var run = await new HealthCheckRunner([reports, later, slow], options).RunAsync(HealthCheckTrigger.Nightly);

        Assert.Equal(HealthOutcome.Pass, Line(run, HealthCheckId.Db01).Outcome);
        foreach (var id in new[] { HealthCheckId.Err02, HealthCheckId.Err03, HealthCheckId.Rpt01 })
        {
            Assert.Equal(HealthOutcome.Skipped, Line(run, id).Outcome);
            Assert.Equal(HealthCheckTexts.TimeBudget, Line(run, id).FindingCode);
        }
    }

    [Fact]
    public async Task An_area_cap_skips_the_rest_of_that_area_but_the_next_area_still_runs()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var first = new FakeProbe([HealthCheckId.Rpt01], onRun: () => clock.Advance(TimeSpan.FromSeconds(61)));
        var second = new FakeProbe([HealthCheckId.Rpt02]);
        var data = new FakeProbe([HealthCheckId.Data03], onRun: () => clock.Advance(TimeSpan.FromSeconds(11)));
        var data2 = new FakeProbe([HealthCheckId.Data04]);
        var imports = new FakeProbe([HealthCheckId.Imp01]);

        var run = await new HealthCheckRunner([first, second, data, data2, imports], Options(clock)).RunAsync(HealthCheckTrigger.Manual);

        Assert.Equal(HealthOutcome.Pass, Line(run, HealthCheckId.Data03).Outcome);
        Assert.Equal(HealthCheckTexts.TimeBudget, Line(run, HealthCheckId.Data04).FindingCode);
        Assert.Equal(HealthOutcome.Pass, Line(run, HealthCheckId.Imp01).Outcome);
        Assert.Equal(HealthOutcome.Pass, Line(run, HealthCheckId.Rpt01).Outcome);
        Assert.Equal(HealthCheckTexts.TimeBudget, Line(run, HealthCheckId.Rpt02).FindingCode);
    }

    [Fact]
    public async Task A_probe_that_overruns_its_allowance_is_cancelled_and_its_lines_are_skipped()
    {
        var hanging = new FakeProbe([HealthCheckId.Ops01, HealthCheckId.Ops02], hang: true);
        var options = Options(TimeProvider.System, budget: HealthCheckBudget.Default with
        {
            AreaCaps = HealthCheckArea.Operations.CapsWith(TimeSpan.FromMilliseconds(200))
        });

        var run = await new HealthCheckRunner([hanging], options).RunAsync(HealthCheckTrigger.Manual);

        Assert.Equal(HealthOutcome.Skipped, Line(run, HealthCheckId.Ops01).Outcome);
        Assert.Equal(HealthCheckTexts.TimeBudget, Line(run, HealthCheckId.Ops02).FindingCode);
    }

    [Fact]
    public async Task Cancel_by_the_user_is_not_turned_into_skipped_lines()
    {
        using var cancel = new CancellationTokenSource();
        var probe = new FakeProbe([HealthCheckId.Db01], onRun: cancel.Cancel, hang: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new HealthCheckRunner([probe], Options()).RunAsync(HealthCheckTrigger.Manual, cancel.Token));
    }

    [Fact]
    public async Task A_probe_that_throws_fails_its_lines_and_is_reported_without_its_message()
    {
        var failures = new List<(string Id, Exception Error)>();
        var probe = new FakeProbe([HealthCheckId.Sec01, HealthCheckId.Sec02], throws: new InvalidOperationException("customer Mr X invoice 123"));
        var runner = new HealthCheckRunner([probe], new HealthCheckRunnerOptions
        {
            TimeProvider = new ManualClock(new DateTimeOffset(2026, 10, 11, 2, 31, 0, TimeSpan.Zero)),
            ProbeFailed = (id, error) => failures.Add((id, error))
        });

        var run = await runner.RunAsync(HealthCheckTrigger.Manual);

        Assert.Equal(HealthOutcome.Fail, Line(run, HealthCheckId.Sec01).Outcome);
        Assert.Equal(HealthCheckTexts.ProbeFailed, Line(run, HealthCheckId.Sec02).FindingCode);
        Assert.DoesNotContain("Mr X", Line(run, HealthCheckId.Sec01).Explanation);
        Assert.Contains("InvalidOperationException", Line(run, HealthCheckId.Sec01).Explanation);
        Assert.Single(failures);
        Assert.Equal(HealthOutcome.Fail, run.Overall);
    }

    [Fact]
    public async Task A_probe_that_forgets_a_line_fails_that_line()
    {
        var probe = new FakeProbe([HealthCheckId.Data01, HealthCheckId.Data02], answer: [HealthCheckId.Data01]);

        var run = await new HealthCheckRunner([probe], Options()).RunAsync(HealthCheckTrigger.Manual);

        Assert.Equal(HealthOutcome.Pass, Line(run, HealthCheckId.Data01).Outcome);
        Assert.Equal((HealthOutcome.Fail, HealthCheckTexts.ProbeNoResult), (Line(run, HealthCheckId.Data02).Outcome, Line(run, HealthCheckId.Data02).FindingCode));
    }

    [Fact]
    public async Task When_DB01_fails_every_other_sql_check_is_skipped_but_file_and_memory_checks_run()
    {
        var called = new List<string>();
        var db01 = new FakeProbe([HealthCheckId.Db01], outcome: HealthOutcome.Fail);
        var db02 = new FakeProbe([HealthCheckId.Db02], onRun: () => called.Add(HealthCheckId.Db02));
        var mixed = new FakeProbe([HealthCheckId.Ops01, HealthCheckId.Ops03], onRun: () => called.Add("OPS"));
        var imp01 = new FakeProbe([HealthCheckId.Imp01], onRun: () => called.Add(HealthCheckId.Imp01));
        var store = new FakeStore();

        var run = await new HealthCheckRunner([imp01, mixed, db02, db01], Options(store: store)).RunAsync(HealthCheckTrigger.Nightly);

        Assert.DoesNotContain(HealthCheckId.Db02, called);
        Assert.Contains("OPS", called);
        Assert.Contains(HealthCheckId.Imp01, called);
        Assert.Equal(HealthCheckTexts.DatabaseUnreachable, Line(run, HealthCheckId.Db02).FindingCode);
        Assert.Equal(HealthCheckTexts.DatabaseUnreachable, Line(run, HealthCheckId.Ops01).FindingCode);
        Assert.Equal(HealthOutcome.Pass, Line(run, HealthCheckId.Ops03).Outcome);
        Assert.Equal(HealthOutcome.Pass, Line(run, HealthCheckId.Imp01).Outcome);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Known_turns_a_warning_into_known_but_never_hides_a_fail()
    {
        var store = new FakeStore
        {
            Known =
            [
                Known(HealthCheckId.Db04, "AUTO_CLOSE_ON"),
                Known(HealthCheckId.Sec01, "EXTRA_GRANT")
            ]
        };
        var probes = new IHealthProbe[]
        {
            new FakeProbe([HealthCheckId.Db04], outcome: HealthOutcome.Warning, findingCode: "AUTO_CLOSE_ON"),
            new FakeProbe([HealthCheckId.Sec01], outcome: HealthOutcome.Fail, findingCode: "EXTRA_GRANT")
        };

        var run = await new HealthCheckRunner(probes, Options(store: store)).RunAsync(HealthCheckTrigger.Manual);

        Assert.Equal(HealthOutcome.Known, Line(run, HealthCheckId.Db04).Outcome);
        Assert.Contains("Known until", Line(run, HealthCheckId.Db04).Explanation);
        Assert.Equal(HealthOutcome.Fail, Line(run, HealthCheckId.Sec01).Outcome);
        Assert.Equal(HealthOutcome.Fail, run.Overall);
        Assert.Equal(1, run.Counts.Known);
    }

    [Fact]
    public void Known_applies_per_sub_finding_and_the_line_stays_a_warning_while_one_sub_finding_is_open()
    {
        var today = new DateOnly(2026, 10, 11);
        var line = new HealthCheckResult(HealthCheckId.Data01, HealthCheckArea.Data, HealthOutcome.Warning, "DAY_INCOMPLETE",
            "Yesterday is incomplete.", "import-files", TimeSpan.Zero, null,
            [
                new HealthCheckSubResult("EXPORTS", HealthOutcome.Warning, "EXPORTS_MISSING"),
                new HealthCheckSubResult("INPUTS", HealthOutcome.Warning, "INPUTS_MISSING"),
                new HealthCheckSubResult("TARGETS", HealthOutcome.Warning, "STAFF_TARGETS_MISSING")
            ]);
        IReadOnlyList<HealthCheckKnownEntry> known = [Known(HealthCheckId.Data01, "INPUTS_MISSING"), Known(HealthCheckId.Data01, "STAFF_TARGETS_MISSING")];

        var partly = HealthCheckRunner.ApplyKnown(line, known, today);

        Assert.Equal(HealthOutcome.Warning, partly.Outcome);
        Assert.Equal("DAY_INCOMPLETE", partly.FindingCode);
        Assert.Equal(new[] { HealthOutcome.Warning, HealthOutcome.Known, HealthOutcome.Known }, partly.SubResults!.Select(sub => sub.Outcome));

        var all = HealthCheckRunner.ApplyKnown(line, [.. known, Known(HealthCheckId.Data01, "EXPORTS_MISSING")], today);
        Assert.Equal(HealthOutcome.Known, all.Outcome);
    }

    [Fact]
    public void Known_on_the_line_code_moves_the_code_to_the_first_open_sub_finding()
    {
        var line = new HealthCheckResult(HealthCheckId.Data01, HealthCheckArea.Data, HealthOutcome.Warning, "INPUTS_MISSING",
            "x", null, TimeSpan.Zero, null,
            [new HealthCheckSubResult("I", HealthOutcome.Warning, "INPUTS_MISSING"), new HealthCheckSubResult("E", HealthOutcome.Warning, "EXPORTS_MISSING")]);

        var result = HealthCheckRunner.ApplyKnown(line, [Known(HealthCheckId.Data01, "INPUTS_MISSING")], new DateOnly(2026, 10, 11));

        Assert.Equal((HealthOutcome.Warning, "EXPORTS_MISSING"), (result.Outcome, result.FindingCode));
    }

    [Fact]
    public void Known_does_not_change_a_fail_sub_result_or_count_after_its_date_or_beyond_90_days()
    {
        var today = new DateOnly(2026, 10, 11);
        var line = new HealthCheckResult(HealthCheckId.Data02, HealthCheckArea.Data, HealthOutcome.Warning, "TENDER_BLOCKED", "x", null, TimeSpan.Zero, null,
            [new HealthCheckSubResult("AX1", HealthOutcome.Fail, "TENDER_BLOCKED")]);
        Assert.Equal(HealthOutcome.Warning, HealthCheckRunner.ApplyKnown(line, [Known(HealthCheckId.Data02, "TENDER_BLOCKED")], today).Outcome);

        var plain = line with { SubResults = null };
        var expired = Known(HealthCheckId.Data02, "TENDER_BLOCKED") with { UntilDate = today.AddDays(-1) };
        var tooLong = Known(HealthCheckId.Data02, "TENDER_BLOCKED") with { UntilDate = today.AddDays(91), SetUtc = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero) };
        Assert.Equal(HealthOutcome.Warning, HealthCheckRunner.ApplyKnown(plain, [expired], today).Outcome);
        Assert.Equal(HealthOutcome.Warning, HealthCheckRunner.ApplyKnown(plain, [tooLong], today).Outcome);
        Assert.Equal(HealthOutcome.Known, HealthCheckRunner.ApplyKnown(plain, [Known(HealthCheckId.Data02, "TENDER_BLOCKED")], today).Outcome);
    }

    [Fact]
    public async Task Unchanged_since_is_the_oldest_consecutive_nightly_run_with_the_same_outcome_and_code()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 10, 11, 2, 31, 0, TimeSpan.Zero));
        var store = new FakeStore();
        store.AddRun(1, new DateTimeOffset(2026, 10, 7, 2, 31, 0, TimeSpan.Zero), (HealthCheckId.Db04, HealthOutcome.Pass, "OK"));
        store.AddRun(2, new DateTimeOffset(2026, 10, 8, 2, 31, 0, TimeSpan.Zero), (HealthCheckId.Db04, HealthOutcome.Warning, "AUTO_CLOSE_ON"));
        store.AddRun(3, new DateTimeOffset(2026, 10, 9, 2, 31, 0, TimeSpan.Zero), (HealthCheckId.Db04, HealthOutcome.Warning, "AUTO_CLOSE_ON"));
        store.AddRun(4, new DateTimeOffset(2026, 10, 10, 2, 31, 0, TimeSpan.Zero),
            (HealthCheckId.Db04, HealthOutcome.Warning, "AUTO_CLOSE_ON"), (HealthCheckId.Db03, HealthOutcome.Warning, "CHECKDB_STALE"));
        var probes = new IHealthProbe[]
        {
            new FakeProbe([HealthCheckId.Db04], outcome: HealthOutcome.Warning, findingCode: "AUTO_CLOSE_ON"),
            new FakeProbe([HealthCheckId.Db03], outcome: HealthOutcome.Fail, findingCode: "SUSPECT_PAGES"),
            new FakeProbe([HealthCheckId.Db05])
        };

        var run = await new HealthCheckRunner(probes, Options(clock, store: store)).RunAsync(HealthCheckTrigger.Nightly);

        Assert.Equal(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(2026, 10, 8, 2, 31, 0, TimeSpan.Zero), clock.LocalTimeZone).DateTime),
            Line(run, HealthCheckId.Db04).UnchangedSince);
        Assert.Null(Line(run, HealthCheckId.Db03).UnchangedSince);
        Assert.Null(Line(run, HealthCheckId.Db05).UnchangedSince);
    }

    [Fact]
    public async Task The_previous_run_of_the_same_trigger_reaches_the_probes()
    {
        var store = new FakeStore();
        store.AddRun(7, new DateTimeOffset(2026, 10, 10, 2, 31, 0, TimeSpan.Zero), (HealthCheckId.Rpt01, HealthOutcome.Pass, "OK"));
        HealthCheckContext? seen = null;
        var probe = new FakeProbe([HealthCheckId.Rpt01], onContext: context => seen = context);

        await new HealthCheckRunner([probe], Options(store: store)).RunAsync(HealthCheckTrigger.Nightly);

        Assert.Equal(7, seen!.PreviousRun!.HealthCheckRunId);
        Assert.Single(seen.PreviousRun.Results);
    }

    [Fact]
    public async Task A_store_that_fails_does_not_stop_the_run()
    {
        var store = new FakeStore { Throw = true };
        var run = await new HealthCheckRunner([new FakeProbe([HealthCheckId.Db04], outcome: HealthOutcome.Warning, findingCode: "AUTO_CLOSE_ON")],
            Options(store: store)).RunAsync(HealthCheckTrigger.Nightly);

        Assert.Equal(HealthOutcome.Warning, Line(run, HealthCheckId.Db04).Outcome);
        Assert.Equal(HealthOutcome.Warning, run.Overall);
    }

    [Fact]
    public void Lines_are_fitted_to_the_result_columns()
    {
        var subs = Enumerable.Range(0, 200).Select(index => new HealthCheckSubResult(
            "STORE" + index, index == 150 ? HealthOutcome.Fail : HealthOutcome.Pass, index == 150 ? "BROKEN" : null, index,
            new Dictionary<string, string> { ["report"] = "R022" })).ToArray();
        var line = new HealthCheckResult(HealthCheckId.Rpt02, HealthCheckArea.Reports, HealthOutcome.Fail, new string('X', 80),
            new string('e', 900), new string('t', 50), TimeSpan.FromSeconds(-1), null, subs);

        var fitted = HealthCheckRunner.Normalise(line);

        Assert.Equal(HealthCheckTexts.MaxFindingCodeLength, fitted.FindingCode.Length);
        Assert.Equal(HealthCheckTexts.MaxExplanationLength, fitted.Explanation.Length);
        Assert.Null(fitted.FixTaskId);
        Assert.Equal(TimeSpan.Zero, fitted.Duration);
        Assert.InRange(fitted.SubResults!.Count, 2, 60);
        Assert.Equal("STORE150", fitted.SubResults![0].Key);
        var more = fitted.SubResults[^1];
        Assert.Equal(HealthCheckTexts.MoreSubResultsKey, more.Key);
        Assert.Equal(200 - (fitted.SubResults.Count - 1), more.MetricValue);
    }

    [Fact]
    public void Normalise_makes_codes_and_explanations_acceptable_to_the_run_procedure()
    {
        var line = new HealthCheckResult(HealthCheckId.Ops09, HealthCheckArea.Operations, HealthOutcome.Warning, "app hang-1002",
            "First line\r\nsecond\tline", "bad task id!", TimeSpan.Zero, null,
            [new HealthCheckSubResult("K", HealthOutcome.Warning, "event 1002")]);

        var fitted = HealthCheckRunner.Normalise(line);

        Assert.Equal("APP_HANG_1002", fitted.FindingCode);
        Assert.Equal("First line  second line", fitted.Explanation);
        Assert.Null(fitted.FixTaskId);
        Assert.Equal("EVENT_1002", fitted.SubResults![0].FindingCode);
        Assert.Equal("OK", HealthCheckRunner.Normalise(line with { FindingCode = " " }).FindingCode);
    }

    [Fact]
    public void Overall_ignores_known_and_skipped()
    {
        Assert.Equal(HealthOutcome.Pass, HealthCheckRunner.Overall(new HealthCheckCounts(10, 0, 0, 5, 36)));
        Assert.Equal(HealthOutcome.Warning, HealthCheckRunner.Overall(new HealthCheckCounts(10, 1, 0, 5, 35)));
        Assert.Equal(HealthOutcome.Fail, HealthCheckRunner.Overall(new HealthCheckCounts(10, 1, 1, 5, 34)));
    }

    [Fact]
    public void The_area_order_is_the_design_order_and_every_area_has_a_cap()
    {
        Assert.Equal(
            new[] { HealthCheckArea.Database, HealthCheckArea.Errors, HealthCheckArea.Operations, HealthCheckArea.Security,
                HealthCheckArea.Data, HealthCheckArea.Imports, HealthCheckArea.Reports },
            HealthCheckRunner.AreaOrder);
        Assert.Equal(Enum.GetValues<HealthCheckArea>().Order(), HealthCheckRunner.AreaOrder.Order());
        Assert.All(HealthCheckRunner.AreaOrder, area => Assert.True(HealthCheckBudget.Default.AreaCaps.ContainsKey(area)));
        Assert.Equal(TimeSpan.FromSeconds(120), HealthCheckBudget.Default.Total);
    }

    [Fact]
    public void A_probe_without_ids_or_with_an_unknown_id_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new HealthCheckRunner([new FakeProbe([])]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HealthCheckRunner([new FakeProbe(["XYZ-01"])]));
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static HealthCheckResult Line(HealthCheckRun run, string id) => run.Results.Single(line => line.CheckId == id);

    private static HealthCheckKnownEntry Known(string checkId, string code) =>
        new(checkId, code, new DateOnly(2026, 10, 31), "Starting in November", "owner", new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));

    private static HealthCheckRunnerOptions Options(TimeProvider? clock = null, IHealthCheckStore? store = null, HealthCheckBudget? budget = null) => new()
    {
        TimeProvider = clock ?? new ManualClock(new DateTimeOffset(2026, 10, 11, 2, 31, 0, TimeSpan.Zero)),
        Store = store,
        Budget = budget ?? HealthCheckBudget.Default,
        LoadRetailStoreCodes = _ => Task.FromResult<IReadOnlyList<string>>(["AX1", "AX2"]),
        AppVersion = "1.11.0.0",
        MachineName = "TESTPC",
        RunByRole = "Owner"
    };

    private sealed class FakeProbe(
        IReadOnlyList<string> ids,
        HealthOutcome outcome = HealthOutcome.Pass,
        string findingCode = "OK",
        Action? onRun = null,
        Action<HealthCheckContext>? onContext = null,
        bool hang = false,
        Exception? throws = null,
        IReadOnlyList<string>? answer = null) : IHealthProbe
    {
        public IReadOnlyList<string> CheckIds => ids;

        public async Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            onRun?.Invoke();
            onContext?.Invoke(context);
            if (throws is not null) throw throws;
            if (hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return (answer ?? ids).Select(id => new HealthCheckResult(
                id, HealthCheckId.AreaOf(id), outcome, outcome == HealthOutcome.Pass ? "OK" : findingCode, "Fixed text.", null,
                TimeSpan.FromMilliseconds(1))).ToArray();
        }
    }

    private sealed class FakeStore : IHealthCheckStore
    {
        private readonly List<HealthCheckRunSummary> runs = [];
        private readonly Dictionary<long, IReadOnlyList<HealthCheckResult>> results = new();

        public IReadOnlyList<HealthCheckKnownEntry> Known { get; init; } = [];
        public bool Throw { get; init; }
        public int Calls { get; private set; }

        public void AddRun(long id, DateTimeOffset started, params (string Id, HealthOutcome Outcome, string Code)[] lines)
        {
            runs.Add(new(id, HealthCheckTrigger.Nightly, DateOnly.FromDateTime(started.UtcDateTime).AddDays(-1), started, started.AddSeconds(40),
                40000, "1.11.0.0", "TESTPC", "Automation", HealthOutcome.Warning, new(0, 0, 0, 0, 0), null));
            results[id] = lines.Select(line => new HealthCheckResult(line.Id, HealthCheckId.AreaOf(line.Id), line.Outcome, line.Code, "x", null, TimeSpan.Zero)).ToArray();
        }

        private void Touch()
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("store down");
        }

        public Task<long> RecordRunAsync(HealthCheckRun run, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<HealthCheckRunSummary>> LoadRunsAsync(int top = 30, HealthCheckTrigger? trigger = null, CancellationToken cancellationToken = default)
        {
            Touch();
            return Task.FromResult<IReadOnlyList<HealthCheckRunSummary>>(
                runs.Where(run => trigger is null || run.Trigger == trigger).OrderByDescending(run => run.StartedUtc).Take(top).ToArray());
        }

        public Task<IReadOnlyList<HealthCheckResult>> LoadResultsAsync(long healthCheckRunId, CancellationToken cancellationToken = default)
        {
            Touch();
            return Task.FromResult(results[healthCheckRunId]);
        }

        public Task<HealthCheckNightlySummary?> LoadLatestNightlySummaryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<HealthCheckKnownEntry>> LoadKnownAsync(CancellationToken cancellationToken = default)
        {
            Touch();
            return Task.FromResult(Known);
        }

        public Task SetKnownAsync(string checkId, string findingCode, DateOnly untilDate, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ClearKnownAsync(string checkId, string findingCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RolePermissionSnapshot> LoadRolePermissionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

/// <summary>A clock the test moves by hand. Timers (budget cancellation) still use real time, far longer than a test.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;
    private long ticks;

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => ticks;

    public void Advance(TimeSpan by)
    {
        now += by;
        ticks += by.Ticks;
    }
}

internal static class HealthCheckAreaCapExtensions
{
    /// <summary>The default caps with one area changed.</summary>
    public static IReadOnlyDictionary<HealthCheckArea, TimeSpan> CapsWith(this HealthCheckArea area, TimeSpan cap)
    {
        var caps = HealthCheckBudget.Default.AreaCaps.ToDictionary(pair => pair.Key, pair => pair.Value);
        caps[area] = cap;
        return caps;
    }
}
