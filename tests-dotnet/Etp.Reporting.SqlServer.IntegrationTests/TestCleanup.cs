using System.Runtime.ExceptionServices;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// Runs a test body and then every cleanup step, in order, whatever happened before.
/// A plain finally block loses the test's own failure when a cleanup step throws, and
/// skips the remaining steps; here every step runs and nothing is hidden.
/// </summary>
internal static class TestCleanup
{
    public static (string Step, Func<Task> Action) Step(string step, Action action) =>
        (step, () => { action(); return Task.CompletedTask; });

    public static (string Step, Func<Task> Action) StepAsync(string step, Func<Task> action) => (step, action);

    public static async Task RunAsync(Func<Task> body, params (string Step, Func<Task> Action)[] cleanup)
    {
        Exception? primary = null;
        try { await body(); }
        catch (Exception exception) { primary = exception; }
        var failures = new List<Exception>();
        foreach (var (step, action) in cleanup)
        {
            try { await action(); }
            catch (Exception exception) { failures.Add(new InvalidOperationException($"Cleanup step failed: {step}.", exception)); }
        }
        if (failures.Count == 0)
        {
            if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
            return;
        }
        if (primary is null)
            throw failures.Count == 1 ? failures[0] : new AggregateException("Test cleanup failed.", failures);
        throw new AggregateException(
            "The test failed and its cleanup failed too. The first inner exception is the test's own failure.",
            [primary, .. failures]);
    }
}

public sealed class TestCleanupTests
{
    [Fact]
    public async Task A_failing_cleanup_step_keeps_the_test_failure_first_and_still_runs_later_steps()
    {
        var later = false;
        var error = await Assert.ThrowsAsync<AggregateException>(() => TestCleanup.RunAsync(
            () => throw new InvalidOperationException("the walk failed"),
            TestCleanup.Step("drop database", () => throw new IOException("drop failed")),
            TestCleanup.Step("delete folder", () => later = true)));
        Assert.Equal("the walk failed", error.InnerExceptions[0].Message);
        Assert.Equal("drop failed", Assert.IsType<IOException>(error.InnerExceptions[1].InnerException).Message);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.True(later);
    }

    [Fact]
    public async Task A_test_failure_alone_is_rethrown_unchanged()
    {
        var error = await Assert.ThrowsAsync<TimeoutException>(() => TestCleanup.RunAsync(
            () => throw new TimeoutException("child did not exit"), TestCleanup.Step("delete folder", () => { })));
        Assert.Equal("child did not exit", error.Message);
    }

    [Fact]
    public async Task A_cleanup_failure_after_a_passing_body_still_fails()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => TestCleanup.RunAsync(
            () => Task.CompletedTask, TestCleanup.Step("settings unchanged", () => throw new InvalidDataException("changed"))));
        Assert.IsType<InvalidDataException>(error.InnerException);
    }
}
