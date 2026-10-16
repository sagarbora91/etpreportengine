using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12.1, VM acceptance finding 2 (16 Oct 2026): DB-06 was Skipped TIME_BUDGET on the 4 GB VM (3,158 jobs) because its
/// twelve counts did not fit in what the other Database checks left of the 10 s area cap. The cap is 20 s, the runner tells
/// each probe its allowance, and DB-06 keeps its own budget: it stops counting in time and still reports cold and warm
/// numbers for the views it counted. A fake clock stands in for SQL Server.
/// </summary>
public sealed class ServiceScreenTimingBudgetTests
{
    private static readonly IReadOnlyList<(string Key, string View)> Views = ServiceScreenTimingProbe.Views;

    /// <summary>A count that advances the fake clock by the time given for each view (first call cold, second warm).</summary>
    private sealed class FakeCounts(Dictionary<string, (double ColdSeconds, double WarmSeconds)> times)
    {
        private readonly HashSet<string> counted = new(StringComparer.Ordinal);

        public TimeSpan Now { get; private set; }

        public List<int> Timeouts { get; } = [];

        public Task<TimeSpan> CountAsync(string view, int timeoutSeconds, CancellationToken cancellationToken)
        {
            Timeouts.Add(timeoutSeconds);
            var (cold, warm) = times[view];
            var took = TimeSpan.FromSeconds(counted.Add(view) ? cold : warm);
            if (took > TimeSpan.FromSeconds(timeoutSeconds))
            {
                Now += TimeSpan.FromSeconds(timeoutSeconds);
                throw new TimeoutException();
            }
            Now += took;
            return Task.FromResult(took);
        }
    }

    /// <summary>Roughly the VM's numbers: v_service_job 2.7 s cold, list events 1.0 s warm.</summary>
    private static Dictionary<string, (double, double)> VmTimes() => new(StringComparer.Ordinal)
    {
        ["dbo.v_service_job"] = (2.7, 0.6),
        ["dbo.v_service_pending_current"] = (0.4, 0.2),
        ["dbo.v_service_status_view_rows"] = (0.3, 0.2),
        ["dbo.v_service_claims"] = (0.1, 0.02),
        ["dbo.v_service_parts"] = (0.1, 0.05),
        ["dbo.v_service_job_list_events"] = (1.4, 1.0)
    };

    [Fact]
    public async Task With_its_own_budget_all_six_views_are_counted_cold_and_warm_on_the_VM_numbers()
    {
        var fake = new FakeCounts(VmTimes());
        var (timings, outOfTime) = await ServiceScreenTimingProbe.MeasureAsync(Views, ServiceScreenTimingProbe.OwnBudget, () => fake.Now, fake.CountAsync, default);

        Assert.Empty(outOfTime);
        Assert.All(timings, item => { Assert.NotNull(item.Cold); Assert.NotNull(item.Warm); });
        Assert.True(fake.Now < ServiceScreenTimingProbe.OwnBudget, $"took {fake.Now}");
        var result = ServiceScreenTimingProbe.Evaluate(timings, fake.Now, outOfTime);
        Assert.Equal(HealthOutcome.Pass, result.Outcome);
        Assert.Contains("first read after start took 2700 ms (compile), repeat read 1000 ms", result.Explanation, StringComparison.Ordinal);
        Assert.All(result.SubResults!, sub =>
        {
            Assert.True(sub.Fields!.ContainsKey(ServiceScreenTimingProbe.ColdField));
            Assert.True(sub.Fields.ContainsKey(ServiceScreenTimingProbe.WarmField));
        });
    }

    [Fact]
    public async Task When_the_budget_runs_out_the_counted_views_keep_their_numbers_and_the_rest_are_Skipped_TIME_BUDGET()
    {
        var fake = new FakeCounts(VmTimes());
        // 4 s: JOBS cold and warm (3.3 s), PENDING cold (3.7 s) and warm (3.9 s), STATUS cold (4.2 s, over), then nothing.
        var (timings, outOfTime) = await ServiceScreenTimingProbe.MeasureAsync(Views, TimeSpan.FromSeconds(4), () => fake.Now, fake.CountAsync, default);
        var result = ServiceScreenTimingProbe.Evaluate(timings, fake.Now, outOfTime);

        Assert.Equal(HealthOutcome.Pass, result.Outcome);
        Assert.Equal(new[] { "CLAIMS", "JOB_HISTORY", "PARTS" }, outOfTime.Order(StringComparer.Ordinal));
        Assert.EndsWith("3 view(s) not timed: the time budget ran out.", result.Explanation, StringComparison.Ordinal);
        var jobs = Assert.Single(result.SubResults!, sub => sub.Key == "JOBS");
        Assert.Equal(("2700", "600"), (jobs.Fields![ServiceScreenTimingProbe.ColdField], jobs.Fields[ServiceScreenTimingProbe.WarmField]));
        // STATUS got its first count but no time for the repeat: judged on the first count, which is all it reports.
        var status = Assert.Single(result.SubResults!, sub => sub.Key == "STATUS");
        Assert.Equal(HealthOutcome.Pass, status.Outcome);
        Assert.Equal(300m, status.MetricValue);
        Assert.False(status.Fields!.ContainsKey(ServiceScreenTimingProbe.WarmField));
        Assert.All(result.SubResults!.Where(sub => outOfTime.Contains(sub.Key)), sub =>
            Assert.Equal((HealthOutcome.Skipped, ServiceScreenTimingProbe.TimeBudgetCode), (sub.Outcome, sub.FindingCode)));
        // No count was allowed past the budget: each command timeout is what was left, rounded up.
        Assert.True(fake.Now <= TimeSpan.FromSeconds(4.3), $"took {fake.Now}");
        Assert.Equal(4, fake.Timeouts[0]);
    }

    [Fact]
    public async Task A_count_that_times_out_stops_the_probe_and_its_view_is_Skipped_TIME_BUDGET()
    {
        var times = VmTimes();
        times["dbo.v_service_job"] = (30, 30);
        var fake = new FakeCounts(times);
        var (timings, outOfTime) = await ServiceScreenTimingProbe.MeasureAsync(Views, TimeSpan.FromSeconds(5), () => fake.Now, fake.CountAsync, default);
        var result = ServiceScreenTimingProbe.Evaluate(timings, fake.Now, outOfTime);

        Assert.Single(fake.Timeouts);
        Assert.Equal(Views.Count, outOfTime.Count);
        Assert.Equal((HealthOutcome.Skipped, ServiceScreenTimingProbe.TimeBudgetCode), (result.Outcome, result.FindingCode));
    }

    [Fact]
    public void A_missing_view_is_still_NOT_READABLE_and_not_a_time_budget_skip()
    {
        var result = ServiceScreenTimingProbe.Evaluate([("JOBS", TimeSpan.FromMilliseconds(900), TimeSpan.FromMilliseconds(100)), ("PARTS", null, null)],
            TimeSpan.Zero, new HashSet<string>(StringComparer.Ordinal));
        Assert.Equal(HealthOutcome.Pass, result.Outcome);
        Assert.Equal("NOT_READABLE", Assert.Single(result.SubResults!, sub => sub.Key == "PARTS").FindingCode);
        Assert.DoesNotContain("not timed", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void The_probe_budget_is_its_own_or_the_runner_allowance_less_the_margin()
    {
        Assert.Equal(ServiceScreenTimingProbe.OwnBudget, ServiceScreenTimingProbe.BudgetFor(Context(null)));
        Assert.Equal(ServiceScreenTimingProbe.OwnBudget, ServiceScreenTimingProbe.BudgetFor(Context(TimeSpan.FromSeconds(60))));
        Assert.Equal(TimeSpan.FromSeconds(3) - ServiceScreenTimingProbe.AllowanceMargin, ServiceScreenTimingProbe.BudgetFor(Context(TimeSpan.FromSeconds(3))));
        Assert.Equal(1, ServiceScreenTimingProbe.TimeoutSeconds(TimeSpan.FromMilliseconds(10)));
        Assert.Equal(15, ServiceScreenTimingProbe.TimeoutSeconds(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void The_Database_area_cap_leaves_DB06_its_budget_after_the_VM_s_other_database_checks()
    {
        var cap = HealthCheckBudget.Default.AreaCaps[HealthCheckArea.Database];
        Assert.Equal(TimeSpan.FromSeconds(20), cap);
        Assert.Equal(TimeSpan.FromSeconds(120), HealthCheckBudget.Default.Total);
        // VM night of 16 Oct 2026: DB-01..05 and DB-07 took 8.2 s together.
        Assert.True(cap - TimeSpan.FromSeconds(8.2) - ServiceScreenTimingProbe.AllowanceMargin >= ServiceScreenTimingProbe.OwnBudget);
    }

    [Fact]
    public async Task The_runner_tells_each_probe_its_allowance()
    {
        var seen = new List<TimeSpan>();
        var runner = new HealthCheckRunner([new AllowanceProbe(HealthCheckId.Db01, seen), new AllowanceProbe(HealthCheckId.Db06, seen)],
            new HealthCheckRunnerOptions { AppVersion = "1.12.1.0", MachineName = "TESTPC", RunByRole = "Automation" });
        await runner.RunAsync(HealthCheckTrigger.Nightly);
        Assert.Equal(2, seen.Count);
        Assert.All(seen, allowance => Assert.True(allowance > TimeSpan.Zero && allowance <= TimeSpan.FromSeconds(20), allowance.ToString()));
    }

    private static HealthCheckContext Context(TimeSpan? allowance)
    {
        var items = new Dictionary<string, object>(StringComparer.Ordinal);
        if (allowance is { } value) items[HealthCheckRunner.ProbeAllowanceItem] = value;
        return new HealthCheckContext(new DateOnly(2026, 10, 15), HealthCheckTrigger.Nightly, DateTimeOffset.UtcNow, [], null, HealthCheckBudget.Default, items);
    }

    private sealed class AllowanceProbe(string id, List<TimeSpan> seen) : IHealthProbe
    {
        public IReadOnlyList<string> CheckIds { get; } = [id];

        public Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            seen.Add((TimeSpan)context.Items[HealthCheckRunner.ProbeAllowanceItem]);
            return Task.FromResult<IReadOnlyList<HealthCheckResult>>(
                [new HealthCheckResult(id, HealthCheckId.AreaOf(id), HealthOutcome.Pass, "OK", "Fixed text.", null, TimeSpan.FromMilliseconds(1))]);
        }
    }
}
