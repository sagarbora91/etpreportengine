using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Updates;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave 2 (B2). Where each field of the update gate comes from (B1's source list): the window's import and Operations
/// runners, the database's open-import count, the automation lease, the scheduled backup and drill tasks, the draft guards,
/// the second ETP session, the compiled-in key and the 0065 switch. A database source that cannot be read fails the gate closed (1.12.1, m-6).
/// </summary>
public sealed class UpdateGateProbeTests
{
    private sealed class Sources
    {
        public bool Owner = true, ImportInWindow, ImportInDatabase, LeaseHeld, OperationsBusy, BackupTask, DrillTask, Draft, OtherSession, Keys = true, Enabled = true;
        public Exception? Throw;

        public UpdateGateProbe Probe(Func<Func<bool>, bool>? onUi = null)
        {
            var sources = new UpdateGateSources(
                () => Owner,
                () => ImportInWindow,
                _ => Throw is null ? Task.FromResult(ImportInDatabase) : Task.FromException<bool>(Throw),
                _ => Throw is null ? Task.FromResult(LeaseHeld) : Task.FromException<bool>(Throw),
                () => OperationsBusy,
                () => BackupTask,
                () => DrillTask,
                () => Draft,
                () => OtherSession,
                () => Keys,
                _ => Task.FromResult(Enabled));
            return onUi is null ? new UpdateGateProbe(sources) : new UpdateGateProbe(sources, onUi);
        }
    }

    private static async Task<IReadOnlyList<UpdateGateBlock>> BlocksAsync(UpdateGateProbe probe) =>
        UpdateGate.Evaluate(await probe.ReadAsync(CancellationToken.None)).Blocks;

    [Fact]
    public async Task With_nothing_running_the_gate_is_clear()
    {
        var state = await new Sources().Probe().ReadAsync(CancellationToken.None);
        Assert.True(UpdateGate.Evaluate(state).Allowed);
    }

    [Fact]
    public async Task Each_source_maps_to_its_own_block()
    {
        Assert.Equal(new[] { UpdateGateBlock.ImportRunning }, await BlocksAsync(new Sources { ImportInWindow = true }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.ImportRunning }, await BlocksAsync(new Sources { ImportInDatabase = true }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.HealthRunRunning }, await BlocksAsync(new Sources { LeaseHeld = true }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.BackupRunning }, await BlocksAsync(new Sources { OperationsBusy = true }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.BackupRunning }, await BlocksAsync(new Sources { BackupTask = true }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.RecoveryDrillRunning }, await BlocksAsync(new Sources { DrillTask = true }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.EntryDraftOpen }, await BlocksAsync(new Sources { Draft = true }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.OtherEtpSessionOpen }, await BlocksAsync(new Sources { OtherSession = true }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.NotSetUp }, await BlocksAsync(new Sources { Keys = false }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.TurnedOff }, await BlocksAsync(new Sources { Enabled = false }.Probe()));
        Assert.Equal(new[] { UpdateGateBlock.NotOwner }, await BlocksAsync(new Sources { Owner = false }.Probe()));
    }

    [Fact]
    public async Task The_automation_lease_is_also_held_by_an_automatic_import_so_an_open_import_is_the_only_sentence()
    {
        Assert.Equal(new[] { UpdateGateBlock.ImportRunning }, await BlocksAsync(new Sources { ImportInDatabase = true, LeaseHeld = true }.Probe()));
    }

    [Fact]
    public async Task A_database_source_that_cannot_be_read_fails_the_gate_closed_with_one_plain_sentence()
    {
        var sources = new Sources { Throw = new InvalidOperationException("Synthetic: the database could not be read") };
        var result = UpdateGate.Evaluate(await sources.Probe().ReadAsync(CancellationToken.None));
        Assert.False(result.Allowed);
        // Only the unreadable sentence: not "turned off" (the setting read worked here) and not an invented import/backup.
        Assert.Equal(new[] { UpdateGateBlock.DatabaseUnreadable }, result.Blocks);
        var sentence = UpdateGate.Describe(UpdateGateBlock.DatabaseUnreadable);
        Assert.Contains("Try again", sentence);
        Assert.DoesNotContain("Exception", sentence);
    }

    [Fact]
    public async Task An_unreadable_update_setting_says_unreadable_not_turned_off()
    {
        var sources = new UpdateGateSources(() => true, () => false, _ => Task.FromResult(false), _ => Task.FromResult(false),
            () => false, () => false, () => false, () => false, () => false, () => true,
            _ => Task.FromException<bool>(new InvalidOperationException("Synthetic")));
        var result = UpdateGate.Evaluate(await new UpdateGateProbe(sources).ReadAsync(CancellationToken.None));
        Assert.Equal(new[] { UpdateGateBlock.DatabaseUnreadable }, result.Blocks);
    }

    [Fact]
    public async Task The_gate_clears_again_when_the_database_can_be_read()
    {
        var sources = new Sources { Throw = new InvalidOperationException("Synthetic") };
        var probe = sources.Probe();
        Assert.False(UpdateGate.Evaluate(await probe.ReadAsync(CancellationToken.None)).Allowed);
        sources.Throw = null;
        Assert.True(UpdateGate.Evaluate(await probe.ReadAsync(CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task A_machine_side_source_that_throws_still_reads_as_not_running()
    {
        var sources = new UpdateGateSources(() => true, () => false, _ => Task.FromResult(false), _ => Task.FromResult(false),
            () => false, () => throw new InvalidOperationException("Synthetic"), () => throw new InvalidOperationException("Synthetic"),
            () => false, () => throw new InvalidOperationException("Synthetic"), () => true, _ => Task.FromResult(true));
        Assert.True(UpdateGate.Evaluate(await new UpdateGateProbe(sources).ReadAsync(CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task The_window_bound_sources_run_through_the_dispatcher_marshal_and_the_others_do_not()
    {
        var marshalled = 0;
        var probe = new Sources().Probe(read => { marshalled++; return read(); });
        await probe.ReadAsync(CancellationToken.None);
        // IsOwner, the import screen, the Operations runner and the draft guards touch WPF objects.
        Assert.Equal(4, marshalled);
    }

    [Fact]
    public void The_blocking_read_the_engine_uses_agrees_with_the_async_read()
    {
        var probe = new Sources { Draft = true }.Probe();
        Assert.Equal(new[] { UpdateGateBlock.EntryDraftOpen }, UpdateGate.Evaluate(probe.ReadBlocking()).Blocks);
    }

    [Theory]
    [InlineData("\"ETP Reporting Daily Backup\",\"N/A\",\"Running\"", true)]
    [InlineData("\"ETP Reporting Daily Backup\",\"14/10/2026 22:00:00\",\"Ready\"", false)]
    [InlineData("\"ETP Reporting Daily Backup\",\"N/A\",\"Disabled\"", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_scheduled_task_status_column_is_read(string? line, bool running) =>
        Assert.Equal(running, UpdateScheduledTasks.StatusLineMeansRunning(line));

    [Fact]
    public void The_two_task_names_are_the_ones_the_installer_registers()
    {
        var scripts = Path.Combine(UpdateTestSupport.RepositoryRoot(), "scripts");
        Assert.Contains(UpdateScheduledTasks.DailyBackup, File.ReadAllText(Path.Combine(scripts, "install-daily-backup-task.ps1")), StringComparison.Ordinal);
        Assert.Contains(UpdateScheduledTasks.MonthlyRecoveryDrill, File.ReadAllText(Path.Combine(scripts, "install-monthly-recovery-drill-task.ps1")), StringComparison.Ordinal);
    }
}
