using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.12 S1: the interactive start refuses a database that setup has not updated, and never blocks on a check it cannot judge.</summary>
public sealed class StartupObjectsCheckTests
{
    private static Task<bool> Yes(CancellationToken _) => Task.FromResult(true);
    private static Func<CancellationToken, Task<DatabaseChangePlan>> Plan(DatabaseChangePlan plan) => _ => Task.FromResult(plan);

    [Fact]
    public async Task A_database_with_nothing_pending_starts()
    {
        Assert.Null(await StartupObjectsCheck.RefusalAsync(Yes, Plan(new([], []))));
    }

    [Fact]
    public async Task Pending_objects_or_migrations_refuse_with_A3s_message_and_counts_but_no_names()
    {
        var objects = await StartupObjectsCheck.RefusalAsync(Yes, Plan(new([], ["dbo.v_service_job"])));
        var both = await StartupObjectsCheck.RefusalAsync(Yes, Plan(new(["0067_x", "0068_y"], ["dbo.v_a", "dbo.v_b", "dbo.v_c"])));

        Assert.StartsWith(SchemaObjectsNotUpToDateException.Reason, objects, StringComparison.Ordinal);
        Assert.Contains("1 report or view update waiting", objects, StringComparison.Ordinal);
        Assert.DoesNotContain("v_service_job", objects, StringComparison.Ordinal);
        Assert.Contains("2 database changes and 3 report or view updates waiting", both, StringComparison.Ordinal);
        Assert.DoesNotContain("0067", both, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_login_that_cannot_read_object_definitions_is_not_judged()
    {
        var planned = false;
        var result = await StartupObjectsCheck.RefusalAsync(_ => Task.FromResult(false), _ => { planned = true; return Task.FromResult(new DatabaseChangePlan(["0067_x"], [])); });

        Assert.Null(result);
        Assert.False(planned);
    }

    [Fact]
    public async Task A_failure_to_plan_lets_the_window_open_and_is_reported_once()
    {
        var reported = new List<Exception>();
        var result = await StartupObjectsCheck.RefusalAsync(Yes, _ => throw new InvalidOperationException("server down"), reported.Add);

        Assert.Null(result);
        Assert.Single(reported);
    }

    [Fact]
    public async Task A_check_that_runs_past_its_time_limit_lets_the_window_open()
    {
        var result = await StartupObjectsCheck.RefusalAsync(Yes,
            async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new DatabaseChangePlan([], []); },
            timeout: TimeSpan.FromMilliseconds(50));

        Assert.Null(result);
    }

    [Fact]
    public async Task The_callers_own_cancellation_still_cancels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StartupObjectsCheck.RefusalAsync(
            async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return true; }, Plan(new([], [])), cancellationToken: cancelled.Token));
    }
}
