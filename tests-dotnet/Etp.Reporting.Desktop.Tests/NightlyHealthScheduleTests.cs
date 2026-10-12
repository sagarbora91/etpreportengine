using Etp.Reporting.Desktop.Modules.HealthCheck;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.0 self-diagnostics, design section 7, decision 33 (Q1): the nightly health check runs at the first
/// five-minute tick at or after 02:30 local, catches up at the first tick after the PC is back on (shop hours too),
/// runs whether or not watch-folder import is enabled, holds the automation lease, and never changes the task's
/// exit code. Fakes only: no SQL Server, no clock, no files outside a temporary folder.
/// </summary>
public sealed class NightlyHealthScheduleTests
{
    private static readonly TimeZoneInfo India = TimeZoneInfo.CreateCustomTimeZone("ETP-Test-IST", TimeSpan.FromMinutes(330), "IST", "IST");

    // ------------------------------------------------------------ IsDue

    [Theory]
    [InlineData("2026-10-11 02:29:59", null, false)]          // before 02:30: never
    [InlineData("2026-10-11 02:30:00", null, true)]           // first run ever
    [InlineData("2026-10-11 02:30:00", "2026-10-10", false)]  // D-1 already recorded
    [InlineData("2026-10-11 02:35:00", "2026-10-09", true)]   // last night's slot
    [InlineData("2026-10-11 14:05:00", "2026-10-07", true)]   // catch-up in shop hours (Q1)
    [InlineData("2026-10-11 23:55:00", "2026-10-10", false)]
    [InlineData("2026-10-11 01:00:00", "2026-10-05", false)]  // days behind, but before 02:30: waits for 02:30
    public void Due_from_0230_local_when_no_nightly_run_exists_for_yesterday(string nowLocal, string? last, bool due)
    {
        var now = DateTime.Parse(nowLocal, System.Globalization.CultureInfo.InvariantCulture);
        DateOnly? lastDate = last is null ? null : DateOnly.Parse(last, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(due, NightlyHealthSchedule.IsDue(now, lastDate));
        Assert.Equal(new DateOnly(2026, 10, 10), NightlyHealthSchedule.BusinessDateFor(now));
    }

    // ------------------------------------------------------------ one tick

    [Fact]
    public async Task Before_0230_nothing_is_read_not_even_the_database()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 29));
        Assert.Equal(NightlyHealthOutcome.NotDue, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(0, harness.LastDateReads);
        Assert.Equal(0, harness.LeaseRequests);
        Assert.Equal(0, harness.Runs);
    }

    [Fact]
    public async Task The_first_tick_after_0230_runs_once_under_the_lease_and_releases_it()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 31)) { LastNightly = new DateOnly(2026, 10, 9) };
        harness.OnRun = () => Assert.True(harness.LeaseHeld, "The run must hold the automation lease.");

        Assert.Equal(NightlyHealthOutcome.Completed, await harness.Schedule.RunIfDueAsync());

        Assert.Equal(1, harness.Runs);
        Assert.False(harness.LeaseHeld);
        Assert.Equal(1, harness.LeasesDisposed);
        Assert.Equal(new NightlyHealthAttempt(new DateOnly(2026, 10, 10), 1, harness.NowUtc), harness.Attempts.Saved);
        Assert.Empty(harness.Failures);
    }

    [Fact]
    public async Task A_catch_up_run_starts_in_shop_hours_when_the_pc_was_off_overnight()
    {
        var harness = new Harness(Local(2026, 10, 11, 11, 15)) { LastNightly = new DateOnly(2026, 10, 8) };
        Assert.Equal(NightlyHealthOutcome.Completed, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(1, harness.Runs);
    }

    [Fact]
    public async Task A_night_already_recorded_is_not_run_again()
    {
        var harness = new Harness(Local(2026, 10, 11, 3, 0)) { LastNightly = new DateOnly(2026, 10, 10) };
        Assert.Equal(NightlyHealthOutcome.NotDue, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(0, harness.LeaseRequests);
        Assert.Equal(0, harness.Runs);
    }

    [Fact]
    public async Task A_busy_lease_skips_this_tick_without_running_or_counting_an_attempt()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 35)) { LeaseFree = false };
        Assert.Equal(NightlyHealthOutcome.Busy, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(0, harness.Runs);
        Assert.Null(harness.Attempts.Saved);
    }

    [Fact]
    public async Task An_open_import_batch_skips_the_tick_as_busy_without_running_or_counting_an_attempt()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 35)) { ImportOpen = true };
        Assert.Equal(NightlyHealthOutcome.Busy, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(0, harness.Runs);
        Assert.Null(harness.Attempts.Saved);
        Assert.Equal(1, harness.LeasesDisposed);   // the lease is released again

        harness.ImportOpen = false;                // the import finished: the next tick runs
        Assert.Equal(NightlyHealthOutcome.Completed, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(1, harness.Runs);
    }

    [Fact]
    public async Task An_unreadable_import_check_does_not_block_the_run()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 35)) { ImportCheckFails = true };
        Assert.Equal(NightlyHealthOutcome.Completed, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(1, harness.Runs);
    }

    [Fact]
    public async Task A_run_finished_by_another_process_while_waiting_for_the_lease_is_not_repeated()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 35));
        harness.OnLease = () => harness.LastNightly = new DateOnly(2026, 10, 10);
        Assert.Equal(NightlyHealthOutcome.NotDue, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(0, harness.Runs);
        Assert.Equal(1, harness.LeasesDisposed);
    }

    [Fact]
    public async Task A_failed_run_is_reported_once_and_retried_at_most_three_times_30_minutes_apart()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 30)) { RunFails = true };

        Assert.Equal(NightlyHealthOutcome.Failed, await harness.Schedule.RunIfDueAsync());
        Assert.Single(harness.Failures);
        Assert.False(harness.LeaseHeld);

        harness.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(NightlyHealthOutcome.Waiting, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(1, harness.Runs);

        harness.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(NightlyHealthOutcome.Failed, await harness.Schedule.RunIfDueAsync());
        harness.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(NightlyHealthOutcome.Failed, await harness.Schedule.RunIfDueAsync());
        harness.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(NightlyHealthOutcome.AttemptsUsedUp, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(3, harness.Runs);
        Assert.Equal(3, harness.Failures.Count);

        // The next night starts fresh.
        harness.Advance(TimeSpan.FromDays(1));
        harness.RunFails = false;
        Assert.Equal(NightlyHealthOutcome.Completed, await harness.Schedule.RunIfDueAsync());
    }

    [Fact]
    public async Task A_database_that_cannot_be_read_counts_as_an_attempt_so_the_log_is_not_flooded()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 30)) { LastDateFails = true };
        Assert.Equal(NightlyHealthOutcome.Failed, await harness.Schedule.RunIfDueAsync());
        Assert.Equal(1, harness.Attempts.Saved!.Attempts);
        harness.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(NightlyHealthOutcome.Waiting, await harness.Schedule.RunIfDueAsync());
        Assert.Single(harness.Failures);
    }

    [Fact]
    public async Task Cancellation_is_not_reported_as_a_failed_run()
    {
        var harness = new Harness(Local(2026, 10, 11, 2, 30));
        using var cancel = new CancellationTokenSource();
        harness.OnRun = () => { cancel.Cancel(); cancel.Token.ThrowIfCancellationRequested(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Schedule.RunIfDueAsync(cancel.Token));
        Assert.Empty(harness.Failures);
        Assert.False(harness.LeaseHeld);
    }

    // ------------------------------------------------------------ after the automation step

    [Fact]
    public async Task The_health_step_runs_when_watch_folder_import_is_disabled_and_keeps_the_exit_code()
    {
        var healthRan = 0;
        // A disabled watch folder returns at once with no sources: exit 0.
        var exit = await NightlyHealthSchedule.RunAfterAutomationAsync(_ => Task.FromResult(0), _ => { healthRan++; return Task.FromResult(NightlyHealthOutcome.Completed); });
        Assert.Equal(0, exit);
        Assert.Equal(1, healthRan);

        exit = await NightlyHealthSchedule.RunAfterAutomationAsync(_ => Task.FromResult(1), _ => { healthRan++; return Task.FromResult(NightlyHealthOutcome.Failed); });
        Assert.Equal(1, exit);
        Assert.Equal(2, healthRan);
    }

    [Fact]
    public async Task The_health_step_still_runs_when_the_import_step_throws_and_the_import_failure_is_rethrown()
    {
        var healthRan = false;
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => NightlyHealthSchedule.RunAfterAutomationAsync(
            _ => throw new InvalidOperationException("Configure the protected machine database target before running unattended operations."),
            _ => { healthRan = true; return Task.FromResult(NightlyHealthOutcome.Completed); }));
        Assert.True(healthRan);
        Assert.StartsWith("Configure the protected machine database target", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_health_step_that_throws_never_changes_the_task_result()
    {
        var exit = await NightlyHealthSchedule.RunAfterAutomationAsync(_ => Task.FromResult(0), _ => throw new InvalidOperationException("boom"));
        Assert.Equal(0, exit);
    }

    // ------------------------------------------------------------ attempt file

    [Fact]
    public void The_attempt_file_round_trips_and_a_damaged_file_counts_as_no_attempt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpNightlyHealth-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(directory, "Logs", "nightly-health-attempt.json");
            var store = new FileNightlyHealthAttemptStore(path);
            Assert.Null(store.Load());
            var attempt = new NightlyHealthAttempt(new DateOnly(2026, 10, 10), 2, new DateTimeOffset(2026, 10, 10, 21, 5, 0, TimeSpan.Zero));
            store.Save(attempt);
            Assert.Equal(attempt, store.Load());
            File.WriteAllText(path, "{ not json");
            Assert.Null(store.Load());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void The_lease_is_the_import_runs_application_lock()
    {
        // AutomationSessionLease.Resource in Etp.Reporting.Infrastructure.SqlServer (internal there).
        Assert.Equal("ETP_PHASE2_AUTOMATION", NightlyHealthLease.Resource);
    }

    // ------------------------------------------------------------ recording (L4 watch-point 4)

    [Fact]
    public async Task A_run_refused_for_its_detail_is_recorded_once_more_without_details()
    {
        var store = new RecordingStore { Refusals = 1 };
        var run = RunWithDetail();

        var id = await Etp.Reporting.Desktop.Composition.DesktopCompositionRoot.RecordHealthRunAsync(store, run, CancellationToken.None);

        Assert.Equal(77, id);
        Assert.Equal(2, store.Recorded.Count);
        Assert.NotNull(store.Recorded[0].Results[0].SubResults);
        Assert.All(store.Recorded[1].Results, result => Assert.Null(result.SubResults));
        Assert.Equal(run.Counts, store.Recorded[1].Counts);
    }

    [Fact]
    public async Task A_second_refusal_or_another_sql_error_is_not_swallowed()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Etp.Reporting.Desktop.Composition.DesktopCompositionRoot.RecordHealthRunAsync(new RecordingStore { Refusals = 2 }, RunWithDetail(), CancellationToken.None));
        var other = new RecordingStore { Refusals = 1, Number = 51824 };
        await Assert.ThrowsAnyAsync<Exception>(() =>
            Etp.Reporting.Desktop.Composition.DesktopCompositionRoot.RecordHealthRunAsync(other, RunWithDetail(), CancellationToken.None));
        Assert.Single(other.Recorded);
    }

    private static Etp.Reporting.Application.HealthCheck.HealthCheckRun RunWithDetail()
    {
        Etp.Reporting.Application.HealthCheck.HealthCheckResult[] results =
        [
            new("DATA-01", Etp.Reporting.Application.HealthCheck.HealthCheckArea.Data, Etp.Reporting.Application.HealthCheck.HealthOutcome.Warning,
                "DAY_INCOMPLETE", "Yesterday is not complete.", "import-files", TimeSpan.FromMilliseconds(400),
                SubResults: [new("HEMW", Etp.Reporting.Application.HealthCheck.HealthOutcome.Warning, "EXPORTS_MISSING")])
        ];
        return new(Etp.Reporting.Application.HealthCheck.HealthCheckTrigger.Nightly, new DateOnly(2026, 10, 10),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(40), "1.11.0.0", "WORKPC", "Automation",
            Etp.Reporting.Application.HealthCheck.HealthOutcome.Warning, new(0, 1, 0, 0, 0), null, results);
    }

    private sealed class RecordingStore : Etp.Reporting.Application.HealthCheck.IHealthCheckStore
    {
        public int Refusals { get; init; }
        public int Number { get; init; } = 51820;
        public List<Etp.Reporting.Application.HealthCheck.HealthCheckRun> Recorded { get; } = [];

        public Task<long> RecordRunAsync(Etp.Reporting.Application.HealthCheck.HealthCheckRun run, CancellationToken cancellationToken = default)
        {
            Recorded.Add(run);
            if (Recorded.Count <= Refusals)
                throw Etp.Reporting.TestSupport.SqlExceptionFactory.Create(new Etp.Reporting.TestSupport.SqlExceptionFactory.Error(Number, "Malformed run JSON."));
            return Task.FromResult(77L);
        }

        public Task<IReadOnlyList<Etp.Reporting.Application.HealthCheck.HealthCheckRunSummary>> LoadRunsAsync(int top = 30, Etp.Reporting.Application.HealthCheck.HealthCheckTrigger? trigger = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Etp.Reporting.Application.HealthCheck.HealthCheckResult>> LoadResultsAsync(long healthCheckRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Etp.Reporting.Application.HealthCheck.HealthCheckNightlySummary?> LoadLatestNightlySummaryAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Etp.Reporting.Application.HealthCheck.HealthCheckKnownEntry>> LoadKnownAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetKnownAsync(string checkId, string findingCode, DateOnly untilDate, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearKnownAsync(string checkId, string findingCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Etp.Reporting.Application.HealthCheck.RolePermissionSnapshot> LoadRolePermissionsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // ------------------------------------------------------------ plumbing

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, India.BaseUtcOffset).ToUniversalTime();

    private sealed class FixedClock(DateTimeOffset utc) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = utc;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => India;
    }

    private sealed class MemoryAttempts : INightlyHealthAttemptStore
    {
        public NightlyHealthAttempt? Saved { get; private set; }
        public NightlyHealthAttempt? Load() => Saved;
        public void Save(NightlyHealthAttempt attempt) => Saved = attempt;
    }

    private sealed class Harness
    {
        private readonly FixedClock clock;

        public Harness(DateTimeOffset nowUtc)
        {
            clock = new FixedClock(nowUtc);
            Schedule = new NightlyHealthSchedule(
                _ =>
                {
                    LastDateReads++;
                    if (LastDateFails) throw new InvalidOperationException("The database could not be reached.");
                    return Task.FromResult(LastNightly);
                },
                _ =>
                {
                    LeaseRequests++;
                    if (!LeaseFree) return Task.FromResult<IAsyncDisposable?>(null);
                    LeaseHeld = true;
                    OnLease?.Invoke();
                    return Task.FromResult<IAsyncDisposable?>(new Lease(this));
                },
                _ =>
                {
                    Runs++;
                    OnRun?.Invoke();
                    if (RunFails) throw new InvalidOperationException("The run failed.");
                    LastNightly = NightlyHealthSchedule.BusinessDateFor(TimeZoneInfo.ConvertTime(clock.Now, India).DateTime);
                    return Task.CompletedTask;
                },
                Attempts,
                clock,
                Failures.Add,
                _ => ImportCheckFails ? throw new InvalidOperationException("unreadable") : Task.FromResult(ImportOpen));
        }

        public NightlyHealthSchedule Schedule { get; }
        public MemoryAttempts Attempts { get; } = new();
        public List<Exception> Failures { get; } = [];
        public DateTimeOffset NowUtc => clock.Now;
        public DateOnly? LastNightly { get; set; }
        public bool LeaseFree { get; set; } = true;
        public bool ImportOpen { get; set; }
        public bool ImportCheckFails { get; set; }
        public bool RunFails { get; set; }
        public bool LastDateFails { get; set; }
        public bool LeaseHeld { get; set; }
        public int LeaseRequests { get; private set; }
        public int LeasesDisposed { get; set; }
        public int LastDateReads { get; private set; }
        public int Runs { get; private set; }
        public Action? OnRun { get; set; }
        public Action? OnLease { get; set; }

        public void Advance(TimeSpan by) => clock.Now += by;
    }

    private sealed class Lease(Harness harness) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            harness.LeaseHeld = false;
            harness.LeasesDisposed++;
            return ValueTask.CompletedTask;
        }
    }
}
