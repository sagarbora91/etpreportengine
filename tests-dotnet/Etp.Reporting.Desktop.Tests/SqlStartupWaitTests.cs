using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.TestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>1.12 startup-sql-wait: the unattended task waits for SQL Server after a boot instead of recording STARTUP_FAILED.</summary>
public sealed class SqlStartupWaitTests
{
    private static Microsoft.Data.SqlClient.SqlException Sql(params int[] numbers) =>
        SqlExceptionFactory.Create(numbers.Select(number => new SqlExceptionFactory.Error(number, $"server text {number}")).ToArray());

    private sealed class Fake
    {
        public TimeSpan Now { get; private set; }
        public List<string> Log { get; } = [];
        public int Probes { get; private set; }
        public Queue<Exception?> Script { get; } = new();

        public Task Probe(CancellationToken _)
        {
            Probes++;
            var next = Script.Count > 0 ? Script.Dequeue() : null;
            return next is null ? Task.CompletedTask : Task.FromException(next);
        }

        public Task Delay(TimeSpan span, CancellationToken _) { Now += span; return Task.CompletedTask; }

        public Task Wait(TimeSpan? budget = null) => SqlStartupWait.WaitAsync(Probe, Delay, () => Now, Log.Add, budget);
    }

    [Fact]
    public async Task Succeeds_after_transient_failures_within_the_budget_and_logs_one_line_per_wait()
    {
        var fake = new Fake();
        fake.Script.Enqueue(Sql(-1));
        fake.Script.Enqueue(Sql(53));
        fake.Script.Enqueue(Sql(4060, 18456));
        await fake.Wait();

        Assert.Equal(4, fake.Probes);
        Assert.Equal(3, fake.Log.Count);
        Assert.All(fake.Log, line => Assert.DoesNotContain('\n', line));
        Assert.True(fake.Now < SqlStartupWait.DefaultBudget);
    }

    [Fact]
    public async Task Gives_up_after_the_budget_with_the_last_sql_error()
    {
        var fake = new Fake();
        for (var i = 0; i < 100; i++) fake.Script.Enqueue(Sql(-1));

        var failure = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => fake.Wait(TimeSpan.FromSeconds(120)));

        Assert.Equal(-1, failure.Number);
        Assert.True(fake.Now <= TimeSpan.FromSeconds(120));
        Assert.True(fake.Now >= TimeSpan.FromSeconds(100));
        Assert.Equal(fake.Probes - 1, fake.Log.Count);
    }

    [Fact]
    public async Task Coordinator_records_one_startup_failure_and_never_runs_the_automation_when_sql_stays_down()
    {
        var fake = new Fake();
        for (var i = 0; i < 100; i++) fake.Script.Enqueue(Sql(-1));
        var automationRuns = 0;
        var coordinator = new DesktopStartupCoordinator(_ => Task.CompletedTask, _ => { automationRuns++; return Task.FromResult(0); }, () => { },
            waitForAutomationSql: _ => fake.Wait(TimeSpan.FromSeconds(120)));

        var outcome = await coordinator.RunAsync(["--automation-once"]);

        Assert.Equal(1, outcome.ExitCode);
        Assert.IsType<Microsoft.Data.SqlClient.SqlException>(outcome.Failure);
        Assert.Equal("UnattendedAutomation", outcome.DiagnosticSource);
        Assert.Equal(0, automationRuns);
    }

    [Fact]
    public async Task Coordinator_runs_the_automation_once_sql_comes_up_and_does_not_wait_for_other_modes()
    {
        var fake = new Fake();
        fake.Script.Enqueue(Sql(-2));
        var automationRuns = 0;
        var coordinator = new DesktopStartupCoordinator(_ => Task.CompletedTask, _ => { automationRuns++; return Task.FromResult(0); }, () => { },
            waitForAutomationSql: fake.Probe);

        Assert.Null((await coordinator.RunAsync(["--initialize-database"])).Failure);
        Assert.Equal(0, fake.Probes);
        var outcome = await new DesktopStartupCoordinator(_ => Task.CompletedTask, _ => { automationRuns++; return Task.FromResult(0); }, () => { },
            waitForAutomationSql: ct => fake.Wait()).RunAsync(["--automation-once"]);
        Assert.Equal(0, outcome.ExitCode);
        Assert.Null(outcome.Failure);
        Assert.Equal(1, automationRuns);
    }

    [Theory]
    [InlineData(18456)]
    [InlineData(229)]
    [InlineData(230)]
    [InlineData(262)]
    [InlineData(297)]
    [InlineData(300)]
    [InlineData(208)]
    public async Task Does_not_retry_permission_or_other_non_transient_errors(int number)
    {
        var fake = new Fake();
        fake.Script.Enqueue(Sql(number));

        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => fake.Wait());

        Assert.Equal(1, fake.Probes);
        Assert.Empty(fake.Log);
        Assert.Equal(TimeSpan.Zero, fake.Now);
    }

    [Fact]
    public async Task Does_not_retry_a_configuration_failure_that_is_not_a_sql_error()
    {
        var fake = new Fake();
        fake.Script.Enqueue(new InvalidOperationException("Configure the protected machine database target."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fake.Wait());

        Assert.Equal(1, fake.Probes);
    }

    [Fact]
    public void Give_up_line_keeps_the_sql_number_and_says_how_long_ETP_waited()
    {
        var line = StartupFailureText.HeadlessLine("automation-once", new SqlStartupWaitExpiredException(Sql(-1), SqlStartupWait.DefaultBudget));

        Assert.StartsWith("ETP-STARTUP-FAILED mode=automation-once kind=ServerUnreachable sql=-1: ", line);
        Assert.EndsWith("(waited 120 s)", line);
        Assert.DoesNotContain('\n', line);
    }

    [Theory]
    [InlineData(53, true)]
    [InlineData(-2, true)]
    [InlineData(4060, true)]
    [InlineData(18456, false)]
    [InlineData(262, false)]
    public void Welcome_overlay_adds_the_still_starting_sentence_only_for_transient_failures(int number, bool expected)
    {
        var presentation = StartupFailureText.Welcome(Sql(number), "checking your access");

        Assert.Equal(expected, presentation.Message.Contains("SQL Server may still be starting. Wait a minute and open ETP again.", StringComparison.Ordinal));
        Assert.EndsWith($"(SQL error {number})", presentation.Message);
    }
}
