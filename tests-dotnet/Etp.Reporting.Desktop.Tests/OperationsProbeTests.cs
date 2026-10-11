using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop.Modules.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Audit;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.0 health check OPS-01..OPS-09 (lane L7). Every reader is a fake, so no task, folder, event log or database of this PC is touched.
/// The rule under test everywhere: something the account cannot read is Skipped, never Fail.
/// </summary>
public sealed class OperationsProbeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 2, 30, 0, TimeSpan.Zero);
    private const string Backups = @"C:\ProgramData\EtpReporting\Backups";
    private const string Automation = @"TESTPC\EtpAutomation";

    // ---- fakes ----

    private sealed class FakeTasks(EtpScheduledTaskReadResult? result = null, bool throws = false) : IEtpScheduledTaskSource
    {
        public Task<EtpScheduledTaskReadResult> ReadAsync(CancellationToken cancellationToken) =>
            throws ? throw new InvalidOperationException("boom") : Task.FromResult(result ?? GreenTasks());
    }

    private sealed class FakeHealth(DatabaseOperationalHealth? value, bool throws = false) : IOperationsHealthSource
    {
        public Task<DatabaseOperationalHealth> LoadAsync(CancellationToken cancellationToken) =>
            throws || value is null ? throw new InvalidOperationException("login failed") : Task.FromResult(value);
    }

    private sealed class FakeFiles : IOperationsFileSystem
    {
        public FileListing Logs { get; set; } = new(FileListStatus.NotFound, []);
        public bool BackupFolderExists { get; set; } = true;
        public Dictionary<string, LogFileSummary?> Summaries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long?> Free { get; } = new(StringComparer.OrdinalIgnoreCase);
        public FileListing ListFiles(string directory, string searchPattern) => Logs;
        public bool DirectoryExists(string path) => BackupFolderExists;
        public LogFileSummary? Summarise(string path) => Summaries.TryGetValue(Path.GetFileName(path), out var summary) ? summary : null;
        public long? FreeBytes(string path) => Free.TryGetValue(path[..2], out var bytes) ? bytes : null;
    }

    private sealed class FakePaths(Func<string, PathProtectionResult>? check = null) : IProtectedPathChecker
    {
        public PathProtectionResult Check(string path) => check?.Invoke(path) ?? new(PathProtection.Protected, "OK");
    }

    private sealed class FakeSql : IOperationsSqlSource
    {
        public SqlServerBuildInfo? Build { get; set; } = new("17.0.4000.1", "CU1", "Express Edition (64-bit)");
        public string? DataFile { get; set; } = @"E:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\DATA\EtpReporting.mdf";
        public bool DataFileThrows { get; set; }
        public AutomationRunsSummary? Runs { get; set; } = new([new("WATCH_IMPORT", "Succeeded", 20), new("SCHEDULED_REPORT_PACK", "Succeeded", 3)], 0);
        public Task<SqlServerBuildInfo> ReadBuildAsync(CancellationToken cancellationToken) =>
            Build is null ? throw new InvalidOperationException("unreachable") : Task.FromResult(Build);
        public Task<string?> ReadDataFilePathAsync(CancellationToken cancellationToken) =>
            DataFileThrows ? throw new InvalidOperationException("denied") : Task.FromResult(DataFile);
        public Task<AutomationRunsSummary> ReadAutomationRunsAsync(CancellationToken cancellationToken) =>
            Runs is null ? throw new InvalidOperationException("denied") : Task.FromResult(Runs);
    }

    private sealed class FakeEvents(EventLogReadResult? result = null) : IEventLogSource
    {
        public EventLogReadResult Read(string logName, IReadOnlyCollection<int> eventIds, DateTimeOffset sinceUtc, int maxEntries, CancellationToken cancellationToken) =>
            result ?? new(EventLogReadStatus.Ok, []);
    }

    // ---- builders ----

    private static EtpScheduledTaskReadResult GreenTasks(bool elevated = true) => new(true, elevated,
    [
        new(EtpScheduledTaskNames.AutomatedOperations, true, true, true, "Ready", Now.UtcDateTime.AddMinutes(-3), 0, Now.UtcDateTime.AddMinutes(2)),
        new(EtpScheduledTaskNames.DailyBackup, true, true, true, "Ready", Now.UtcDateTime.AddHours(-4), 0, Now.UtcDateTime.AddHours(20)),
        new(EtpScheduledTaskNames.MonthlyRecoveryDrill, true, true, true, "Ready", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), 0, new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc))
    ]);

    private static EtpScheduledTaskReadResult WithTask(EtpScheduledTaskInfo replacement, bool elevated = true)
    {
        var green = GreenTasks(elevated);
        return green with { Tasks = green.Tasks.Select(task => task.Name == replacement.Name ? replacement : task).ToArray() };
    }

    private static DatabaseOperationalHealth GreenHealth(Func<DatabaseOperationalHealth, DatabaseOperationalHealth>? change = null)
    {
        var health = new DatabaseOperationalHealth(OperationalHealthSeverity.Healthy, 464m, null, Now.UtcDateTime.AddHours(-4), 0, 120m, [])
        {
            LastSuccessfulRecoveryDrillUtc = Now.UtcDateTime.AddDays(-2),
            LatestRecoveryDrillResult = new RecoveryDrillResult(Now.UtcDateTime.AddDays(-2), true, "sha", "Matched", null, [])
        };
        return change?.Invoke(health) ?? health;
    }

    private static FakeFiles GreenFiles()
    {
        var files = new FakeFiles();
        files.Free["C:"] = 200L * 1024 * 1024 * 1024;
        files.Free["E:"] = 150L * 1024 * 1024 * 1024;
        return files;
    }

    private sealed record Rig(FakeTasks Tasks, FakeHealth Health, FakeFiles Files, FakePaths Paths, FakeSql Sql, FakeEvents Events, SqlBuildVerdict Verdict = SqlBuildVerdict.AtOrAboveRecommended)
    {
        public OperationsProbes Build() => new(Tasks, Health, Files, Paths, Sql, new DelegateSqlBuildAssessor(_ => Verdict), Events,
            new OperationsProbeSettings(Backups, @"C:\ProgramData\EtpReporting\Operations\operations.json", @"C:\ProgramData\EtpReporting\HealthExports",
                @"C:\Program Files\ETP", Automation) { LocalZone = TimeZoneInfo.Utc });
    }

    private static Rig Green() => new(new FakeTasks(), new FakeHealth(GreenHealth()), GreenFiles(), new FakePaths(), new FakeSql(), new FakeEvents());

    private static HealthCheckContext Context() => new(new DateOnly(2026, 10, 10), HealthCheckTrigger.Nightly, Now, ["MAIN"], null, HealthCheckBudget.Default, new Dictionary<string, object>());

    private static async Task<IReadOnlyDictionary<string, HealthCheckResult>> RunAsync(Rig rig) =>
        (await rig.Build().RunAsync(Context(), CancellationToken.None)).ToDictionary(result => result.CheckId);

    private static EventLogEntryInfo Event(string provider, int id, params string[] data) => new(provider, id, Now.AddHours(-1), data);

    // ---- shape ----

    [Fact]
    public async Task Everything_green_gives_nine_passes_in_catalogue_order()
    {
        var probes = Green().Build();
        var results = await probes.RunAsync(Context(), CancellationToken.None);
        Assert.Equal(probes.CheckIds, results.Select(result => result.CheckId).ToArray());
        Assert.Equal(
            new[] { HealthCheckId.Ops01, HealthCheckId.Ops02, HealthCheckId.Ops03, HealthCheckId.Ops04, HealthCheckId.Ops05, HealthCheckId.Ops06, HealthCheckId.Ops07, HealthCheckId.Ops08, HealthCheckId.Ops09 },
            probes.CheckIds);
        Assert.All(results, result => Assert.Equal(HealthOutcome.Pass, result.Outcome));
        Assert.All(results, result => Assert.Equal(HealthCheckArea.Operations, result.Area));
    }

    [Fact]
    public async Task A_reader_that_throws_is_Skipped_and_never_fails_the_line()
    {
        var rig = Green() with
        {
            Tasks = new FakeTasks(throws: true), Health = new FakeHealth(null, throws: true),
            Sql = new FakeSql { Build = null, Runs = null, DataFileThrows = true }
        };
        var results = await RunAsync(rig);
        foreach (var id in new[] { HealthCheckId.Ops01, HealthCheckId.Ops02, HealthCheckId.Ops03, HealthCheckId.Ops06, HealthCheckId.Ops07 })
            Assert.Equal(HealthOutcome.Skipped, results[id].Outcome);
        Assert.DoesNotContain(results.Values, result => result.Outcome == HealthOutcome.Fail);
        Assert.DoesNotContain(results.Values, result => result.Explanation.Contains("boom") || result.Explanation.Contains("login failed"));
    }

    // ---- OPS-01 / OPS-02 ----

    [Theory]
    [InlineData(10, HealthOutcome.Pass)]
    [InlineData(40, HealthOutcome.Warning)]
    [InlineData(70, HealthOutcome.Fail)]
    public async Task Ops01_backup_age(int hours, HealthOutcome expected)
    {
        var rig = Green() with { Health = new FakeHealth(GreenHealth(h => h with { LastSuccessfulBackupUtc = Now.UtcDateTime.AddHours(-hours) })) };
        Assert.Equal(expected, (await RunAsync(rig))[HealthCheckId.Ops01].Outcome);
    }

    [Fact]
    public async Task Ops01_no_backup_ever_is_a_Fail()
    {
        var rig = Green() with { Health = new FakeHealth(GreenHealth(h => h with { LastSuccessfulBackupUtc = null })) };
        var line = (await RunAsync(rig))[HealthCheckId.Ops01];
        Assert.Equal(HealthOutcome.Fail, line.Outcome);
        Assert.Equal("BACKUP_MISSING", line.FindingCode);
    }

    [Fact]
    public async Task Ops02_failed_missing_stale_and_unrecorded_drills()
    {
        var failed = GreenHealth(h => h with { LatestRecoveryDrillResult = new RecoveryDrillResult(Now.UtcDateTime.AddDays(-1), false, "sha", "Mismatch", null, []) });
        Assert.Equal("RECOVERY_DRILL_FAILED", (await RunAsync(Green() with { Health = new FakeHealth(failed) }))[HealthCheckId.Ops02].FindingCode);

        var never = GreenHealth(h => h with { LatestRecoveryDrillResult = null, LastSuccessfulRecoveryDrillUtc = null });
        var neverLine = (await RunAsync(Green() with { Health = new FakeHealth(never) }))[HealthCheckId.Ops02];
        Assert.Equal(HealthOutcome.Fail, neverLine.Outcome);

        var old = GreenHealth(h => h with { LatestRecoveryDrillResult = new RecoveryDrillResult(Now.UtcDateTime.AddDays(-50), true, "sha", "Matched", null, []) });
        var oldLine = (await RunAsync(Green() with { Health = new FakeHealth(old) }))[HealthCheckId.Ops02];
        Assert.Equal((HealthOutcome.Warning, "RECOVERY_DRILL_STALE"), (oldLine.Outcome, oldLine.FindingCode));

        var unrecorded = GreenHealth(h => h with { LatestRecoveryDrillResult = new RecoveryDrillResult(Now.UtcDateTime.AddDays(-2), true, "sha", "NotRecorded", "RECEIPT_WITHOUT_COUNTS", []) });
        var unrecordedLine = (await RunAsync(Green() with { Health = new FakeHealth(unrecorded) }))[HealthCheckId.Ops02];
        Assert.Equal((HealthOutcome.Warning, "ROW_COUNTS_NOT_RECORDED"), (unrecordedLine.Outcome, unrecordedLine.FindingCode));
    }

    // ---- OPS-03 ----

    [Fact]
    public async Task Ops03_unelevated_account_that_cannot_see_the_other_principals_tasks_is_Skipped_not_Fail()
    {
        var tasks = new EtpScheduledTaskReadResult(true, false,
        [
            EtpScheduledTaskInfo.NotFound(EtpScheduledTaskNames.AutomatedOperations),
            EtpScheduledTaskInfo.NotFound(EtpScheduledTaskNames.DailyBackup),
            GreenTasks().Tasks[2]
        ]);
        var line = (await RunAsync(Green() with { Tasks = new FakeTasks(tasks) }))[HealthCheckId.Ops03];
        Assert.Equal(HealthOutcome.Skipped, line.Outcome);
        Assert.Equal("TASK_NOT_VISIBLE", line.FindingCode);
        Assert.Equal(HealthOutcome.Pass, line.SubResults!.Single(sub => sub.Key == EtpScheduledTaskNames.MonthlyRecoveryDrill).Outcome);
    }

    [Fact]
    public async Task Ops03_a_task_that_exists_but_is_unreadable_is_Skipped()
    {
        var hidden = new EtpScheduledTaskInfo(EtpScheduledTaskNames.DailyBackup, true, false, false, "Unreadable", null, null, null);
        var line = (await RunAsync(Green() with { Tasks = new FakeTasks(WithTask(hidden, elevated: false)) }))[HealthCheckId.Ops03];
        Assert.Equal(HealthOutcome.Skipped, line.Outcome);
    }

    [Fact]
    public async Task Ops03_an_elevated_reader_that_finds_no_task_reports_it_missing()
    {
        var line = (await RunAsync(Green() with { Tasks = new FakeTasks(WithTask(EtpScheduledTaskInfo.NotFound(EtpScheduledTaskNames.DailyBackup))) }))[HealthCheckId.Ops03];
        Assert.Equal((HealthOutcome.Fail, "TASK_MISSING"), (line.Outcome, line.FindingCode));
        Assert.Equal("Run ETP setup as administrator.", line.FixTaskId);
    }

    [Fact]
    public async Task Ops03_a_failure_beats_a_Skipped_task()
    {
        var failed = new EtpScheduledTaskInfo(EtpScheduledTaskNames.MonthlyRecoveryDrill, true, true, true, "Ready", new DateTime(2026, 10, 1, 8, 0, 0), 1, null);
        var tasks = new EtpScheduledTaskReadResult(true, false,
            [EtpScheduledTaskInfo.NotFound(EtpScheduledTaskNames.AutomatedOperations), GreenTasks().Tasks[1], failed]);
        var line = (await RunAsync(Green() with { Tasks = new FakeTasks(tasks) }))[HealthCheckId.Ops03];
        Assert.Equal((HealthOutcome.Fail, "TASK_RESULT"), (line.Outcome, line.FindingCode));
    }

    [Fact]
    public async Task Ops03_disabled_automation_task_is_a_Warning_even_after_failed_sign_ins()
    {
        var disabled = new EtpScheduledTaskInfo(EtpScheduledTaskNames.AutomatedOperations, true, true, false, "Disabled", Now.UtcDateTime.AddDays(-2), unchecked((int)0x8007052E), null);
        var line = (await RunAsync(Green() with { Tasks = new FakeTasks(WithTask(disabled)) }))[HealthCheckId.Ops03];
        Assert.Equal((HealthOutcome.Warning, "TASK_DISABLED"), (line.Outcome, line.FindingCode));
    }

    [Fact]
    public async Task Ops03_non_zero_result_late_run_and_never_run()
    {
        var result = new EtpScheduledTaskInfo(EtpScheduledTaskNames.DailyBackup, true, true, true, "Ready", Now.UtcDateTime.AddHours(-4), unchecked((int)0x80070002), null);
        Assert.Equal((HealthOutcome.Fail, "TASK_RESULT"), Pair((await RunAsync(Green() with { Tasks = new FakeTasks(WithTask(result)) }))[HealthCheckId.Ops03]));

        var late = new EtpScheduledTaskInfo(EtpScheduledTaskNames.DailyBackup, true, true, true, "Ready", Now.UtcDateTime.AddHours(-30), 0, null);
        Assert.Equal((HealthOutcome.Warning, "TASK_LATE"), Pair((await RunAsync(Green() with { Tasks = new FakeTasks(WithTask(late)) }))[HealthCheckId.Ops03]));

        var lateAutomation = new EtpScheduledTaskInfo(EtpScheduledTaskNames.AutomatedOperations, true, true, true, "Ready", Now.UtcDateTime.AddMinutes(-20), 0, null);
        Assert.Equal((HealthOutcome.Warning, "TASK_LATE"), Pair((await RunAsync(Green() with { Tasks = new FakeTasks(WithTask(lateAutomation)) }))[HealthCheckId.Ops03]));

        var never = new EtpScheduledTaskInfo(EtpScheduledTaskNames.MonthlyRecoveryDrill, true, true, true, "Ready", null, null, null);
        Assert.Equal((HealthOutcome.Warning, "TASK_NEVER_RUN"), Pair((await RunAsync(Green() with { Tasks = new FakeTasks(WithTask(never)) }))[HealthCheckId.Ops03]));
    }

    [Fact]
    public async Task Ops03_scheduler_that_cannot_be_opened_is_Skipped()
    {
        var line = (await RunAsync(Green() with { Tasks = new FakeTasks(EtpScheduledTaskReadResult.Unavailable(false)) }))[HealthCheckId.Ops03];
        Assert.Equal(HealthOutcome.Skipped, line.Outcome);
    }

    private static (HealthOutcome, string) Pair(HealthCheckResult line) => (line.Outcome, line.FindingCode);

    // ---- OPS-04 ----

    [Fact]
    public async Task Ops04_free_space_thresholds_use_the_smaller_volume()
    {
        var warning = Green(); warning.Files.Free["E:"] = 10L * 1024 * 1024 * 1024;
        Assert.Equal((HealthOutcome.Warning, "DISK_LOW"), Pair((await RunAsync(warning))[HealthCheckId.Ops04]));
        var critical = Green(); critical.Files.Free["C:"] = 3L * 1024 * 1024 * 1024;
        var line = (await RunAsync(critical))[HealthCheckId.Ops04];
        Assert.Equal((HealthOutcome.Fail, "DISK_CRITICAL"), Pair(line));
        Assert.Equal(3m, line.MetricValue);
    }

    [Fact]
    public async Task Ops04_unreadable_volumes_are_Skipped_and_one_readable_volume_still_counts()
    {
        var none = Green(); none.Files.Free.Clear();
        Assert.Equal(HealthOutcome.Skipped, (await RunAsync(none))[HealthCheckId.Ops04].Outcome);
        var oneVolume = Green() with { Sql = new FakeSql { DataFileThrows = true } };
        oneVolume.Files.Free["C:"] = 2L * 1024 * 1024 * 1024;
        Assert.Equal(HealthOutcome.Fail, (await RunAsync(oneVolume))[HealthCheckId.Ops04].Outcome);
        var oneGreen = Green() with { Sql = new FakeSql { DataFileThrows = true } };
        Assert.Equal(HealthOutcome.Pass, (await RunAsync(oneGreen))[HealthCheckId.Ops04].Outcome);
    }

    // ---- OPS-05 ----

    [Fact]
    public async Task Ops05_a_folder_others_can_write_is_a_Fail_and_names_only_the_folder_key()
    {
        var rig = Green() with { Paths = new FakePaths(path => path.EndsWith("Backups") ? new(PathProtection.Finding, "RIGHTS") : new(PathProtection.Protected, "OK")) };
        var line = (await RunAsync(rig))[HealthCheckId.Ops05];
        Assert.Equal((HealthOutcome.Fail, "ACL_RIGHTS"), Pair(line));
        Assert.Contains("backups", line.Explanation);
        Assert.DoesNotContain("ProgramData", line.Explanation);
    }

    [Fact]
    public async Task Ops05_unreadable_or_missing_folders_are_Skipped_never_Fail()
    {
        var unreadable = Green() with { Paths = new FakePaths(_ => new(PathProtection.Unreadable, "UNREADABLE")) };
        Assert.Equal(HealthOutcome.Skipped, (await RunAsync(unreadable))[HealthCheckId.Ops05].Outcome);
        var missing = Green() with { Paths = new FakePaths(path => path.EndsWith("HealthExports") ? new(PathProtection.Missing, "MISSING") : new(PathProtection.Protected, "OK")) };
        Assert.Equal(HealthOutcome.Skipped, (await RunAsync(missing))[HealthCheckId.Ops05].Outcome);
    }

    [Fact]
    public async Task Ops05_a_finding_is_a_Fail_even_when_other_folders_are_unreadable()
    {
        var rig = Green() with { Paths = new FakePaths(path => path.EndsWith("operations.json") ? new(PathProtection.Finding, "OWNER") : new(PathProtection.Unreadable, "UNREADABLE")) };
        Assert.Equal((HealthOutcome.Fail, "ACL_OWNER"), Pair((await RunAsync(rig))[HealthCheckId.Ops05]));
    }

    // ---- OPS-06 ----

    [Fact]
    public async Task Ops06_rtm_is_a_Warning_not_a_Fail_decision_36_q15()
    {
        var rig = Green() with { Sql = new FakeSql { Build = new("17.0.1000.7", null, "Express Edition (64-bit)") }, Verdict = SqlBuildVerdict.Rtm };
        var line = (await RunAsync(rig))[HealthCheckId.Ops06];
        Assert.Equal((HealthOutcome.Warning, "SQL_RTM"), Pair(line));
        Assert.Contains("17.0.1000.7", line.Explanation);
        Assert.Equal("RTM", line.SubResults!.Single().Fields!["updateLevel"]);
    }

    [Fact]
    public async Task Ops06_below_recommended_unsupported_and_current()
    {
        var below = Green() with { Verdict = SqlBuildVerdict.BelowRecommended };
        Assert.Equal((HealthOutcome.Warning, "SQL_BUILD_BELOW_RECOMMENDED"), Pair((await RunAsync(below))[HealthCheckId.Ops06]));
        var unsupported = Green() with { Verdict = SqlBuildVerdict.UnsupportedMajor };
        Assert.Equal((HealthOutcome.Fail, "SQL_MAJOR_UNSUPPORTED"), Pair((await RunAsync(unsupported))[HealthCheckId.Ops06]));
        Assert.Equal(HealthOutcome.Pass, (await RunAsync(Green()))[HealthCheckId.Ops06].Outcome);
    }

    // ---- OPS-07 ----

    [Fact]
    public async Task Ops07_failed_report_pack_is_a_Fail_failed_watch_import_a_Warning()
    {
        var pack = Green() with { Sql = new FakeSql { Runs = new([new("SCHEDULED_REPORT_PACK", "Failed", 1), new("WATCH_IMPORT", "Succeeded", 5)], 0) } };
        Assert.Equal((HealthOutcome.Fail, "REPORT_PACK_FAILED"), Pair((await RunAsync(pack))[HealthCheckId.Ops07]));
        var schedule = Green() with { Sql = new FakeSql { Runs = new([], 1) } };
        Assert.Equal(HealthOutcome.Fail, (await RunAsync(schedule))[HealthCheckId.Ops07].Outcome);
        var watch = Green() with { Sql = new FakeSql { Runs = new([new("WATCH_IMPORT", "Failed", 2)], 0) } };
        var line = (await RunAsync(watch))[HealthCheckId.Ops07];
        Assert.Equal((HealthOutcome.Warning, "WATCH_IMPORT_FAILED"), Pair(line));
        Assert.Equal("conflicts", line.FixTaskId);
        var blocked = Green() with { Sql = new FakeSql { Runs = new([new("AUTO_REPORT_PACK", "Blocked", 4)], 0) } };
        Assert.Equal(HealthOutcome.Pass, (await RunAsync(blocked))[HealthCheckId.Ops07].Outcome);
    }

    // ---- OPS-08 ----

    private static Rig WithLogs(params (string Name, string Line)[] logs)
    {
        var rig = Green();
        rig.Files.Logs = new(FileListStatus.Ok, logs.Select(log => log.Name).ToArray());
        foreach (var (name, line) in logs) rig.Files.Summaries[name] = new LogFileSummary(1, line);
        return rig;
    }

    [Fact]
    public async Task Ops08_no_logs_folder_but_a_readable_backup_folder_is_a_Pass()
    {
        var line = (await RunAsync(Green()))[HealthCheckId.Ops08];
        Assert.Equal(HealthOutcome.Pass, line.Outcome);
    }

    [Fact]
    public async Task Ops08_unreadable_logs_or_backup_folder_are_Skipped()
    {
        var denied = Green(); denied.Files.Logs = new(FileListStatus.Denied, []);
        Assert.Equal(HealthOutcome.Skipped, (await RunAsync(denied))[HealthCheckId.Ops08].Outcome);
        var hidden = Green(); hidden.Files.BackupFolderExists = false;
        Assert.Equal(HealthOutcome.Skipped, (await RunAsync(hidden))[HealthCheckId.Ops08].Outcome);
        var unreadableFile = Green(); unreadableFile.Files.Logs = new(FileListStatus.Ok, ["backup-20261010.log"]);
        Assert.Equal(HealthOutcome.Skipped, (await RunAsync(unreadableFile))[HealthCheckId.Ops08].Outcome);
    }

    [Fact]
    public async Task Ops08_a_failure_followed_by_a_verified_backup_is_a_Warning()
    {
        // Last verified backup is Now - 4 h = 10 Oct 22:30 UTC; the failure was on 9 Oct.
        var rig = WithLogs(("backup-20261009.log", "2026-10-09T22:00:03.0000000+00:00 FAILED backup as TESTPC\\EtpAutomation: LOW_DISK_SPACE"));
        var line = (await RunAsync(rig))[HealthCheckId.Ops08];
        Assert.Equal((HealthOutcome.Warning, "FAILURE_THEN_SUCCESS"), Pair(line));
        Assert.Equal(1m, line.MetricValue);
    }

    [Fact]
    public async Task Ops08_a_failure_newer_than_the_last_success_is_a_Fail_and_old_logs_are_ignored()
    {
        var rig = WithLogs(
            ("recovery-drill-20261010.log", "2026-10-10T23:15:00.0000000+00:00 FAILED recovery-drill as TESTPC\\Sagar: RECOVERY_KEYS_MISSING"),
            ("backup-20260901.log", "2026-09-01T22:00:03.0000000+00:00 FAILED backup"));
        // The drill last succeeded 2 days ago, so the failure on 10 Oct is newer; the 1 Sep log is outside the 7-day window.
        var line = (await RunAsync(rig))[HealthCheckId.Ops08];
        Assert.Equal((HealthOutcome.Fail, "FAILURE_UNRESOLVED"), Pair(line));
        Assert.Single(line.SubResults!);
        Assert.Equal("recovery-drill", line.SubResults![0].Key);
        Assert.DoesNotContain("RECOVERY_KEYS_MISSING", line.Explanation);
    }

    [Fact]
    public async Task Ops08_a_log_with_no_readable_success_record_is_a_Warning_not_a_Fail()
    {
        var rig = WithLogs(("backup-20261010.log", "2026-10-10T23:00:00.0000000+00:00 FAILED backup")) with { Health = new FakeHealth(null, throws: true) };
        var line = (await RunAsync(rig))[HealthCheckId.Ops08];
        Assert.Equal((HealthOutcome.Warning, "FAILURE_UNVERIFIED"), Pair(line));
    }

    // ---- OPS-09 ----

    private static Rig WithEvents(params EventLogEntryInfo[] events) => Green() with { Events = new FakeEvents(new(EventLogReadStatus.Ok, events)) };

    [Fact]
    public async Task Ops09_etp_crash_is_a_Fail_and_hang_a_Warning()
    {
        var crash = WithEvents(Event("Application Error", 1000, "Etp.Reporting.Desktop.exe", "15.0.0.0"));
        Assert.Equal((HealthOutcome.Fail, "ETP_CRASH"), Pair((await RunAsync(crash))[HealthCheckId.Ops09]));
        var dotNet = WithEvents(Event(".NET Runtime", 1026, "Application: Etp.Reporting.Desktop.exe", "Framework Version: v10.0"));
        Assert.Equal(HealthOutcome.Fail, (await RunAsync(dotNet))[HealthCheckId.Ops09].Outcome);
        var hang = WithEvents(Event("Application Hang", 1002, "Etp.Reporting.Desktop.exe"));
        Assert.Equal((HealthOutcome.Warning, "ETP_HANG"), Pair((await RunAsync(hang))[HealthCheckId.Ops09]));
    }

    [Fact]
    public async Task Ops09_non_etp_dotnet_and_application_events_are_noise()
    {
        var rig = WithEvents(
            Event(".NET Runtime", 1026, "Application: SomeOtherTool.exe"),
            Event("Application Error", 1000, "chrome.exe"),
            Event("Application Hang", 1002, "excel.exe"),
            Event("Application Error", 3449, "MSSQL$SQLEXPRESS lookalike"));
        var line = (await RunAsync(rig))[HealthCheckId.Ops09];
        Assert.Equal(HealthOutcome.Pass, line.Outcome);
        Assert.Equal(0m, line.MetricValue);
    }

    [Fact]
    public async Task Ops09_engine_errors_from_the_sql_provider_are_a_Fail_with_counts_only()
    {
        var rig = WithEvents(Event("MSSQL$SQLEXPRESS", 3449, "Server is shutting down"), Event("MSSQL$SQLEXPRESS", 3449), Event("MSSQL$SQLEXPRESS", 824));
        var line = (await RunAsync(rig))[HealthCheckId.Ops09];
        Assert.Equal((HealthOutcome.Fail, "SQL_ENGINE_ERROR"), Pair(line));
        Assert.Equal(3m, line.MetricValue);
        Assert.Contains("3449 x2", line.Explanation);
        Assert.DoesNotContain("Server is shutting down", line.Explanation);
        // Engine errors from another provider (for example a different instance) do not count.
        var other = WithEvents(Event("MSSQL$OTHER", 3449));
        Assert.Equal(HealthOutcome.Pass, (await RunAsync(other))[HealthCheckId.Ops09].Outcome);
    }

    [Fact]
    public async Task Ops09_18456_counts_only_for_the_automation_account_and_never_for_test_logins()
    {
        var rig = WithEvents(
            Event("MSSQL$SQLEXPRESS", 18456, "EtpPhase0Test_Missing_ab12cd", "Reason: Could not find a login"),
            Event("MSSQL$SQLEXPRESS", 18456, "someone", "Reason: Password did not match"),
            Event("MSSQL$SQLEXPRESS", 18456, "TESTPC\\EtpAutomation", "Reason: Could not find a login"));
        var line = (await RunAsync(rig))[HealthCheckId.Ops09];
        Assert.Equal((HealthOutcome.Warning, "AUTOMATION_LOGIN_FAILED"), Pair(line));
        Assert.Equal(1m, line.MetricValue);
        Assert.DoesNotContain("EtpAutomation", line.Explanation);
        var onlyNoise = WithEvents(Event("MSSQL$SQLEXPRESS", 18456, "EtpPhase0Test_Missing_ab12cd"));
        Assert.Equal(HealthOutcome.Pass, (await RunAsync(onlyNoise))[HealthCheckId.Ops09].Outcome);
    }

    [Theory]
    [InlineData("TESTPC\\EtpAutomation", "TESTPC\\EtpAutomation", true)]
    [InlineData("EtpAutomation", "TESTPC\\EtpAutomation", true)]
    [InlineData("etpautomation", "TESTPC\\EtpAutomation", true)]
    [InlineData("EtpPhase0Test_Missing_1", "TESTPC\\EtpAutomation", false)]
    [InlineData("TESTPC\\EtpPhase0Test_Missing_1", "TESTPC\\EtpPhase0Test_Missing_1", false)]
    [InlineData("sa", "TESTPC\\EtpAutomation", false)]
    [InlineData("EtpAutomation", null, false)]
    [InlineData("", "TESTPC\\EtpAutomation", false)]
    public void Automation_login_matching_rules(string login, string? account, bool expected) =>
        Assert.Equal(expected, EventLogClassifier.IsAutomationLogin(login, account));

    [Fact]
    public async Task Ops09_an_unreadable_event_log_is_Skipped()
    {
        foreach (var status in new[] { EventLogReadStatus.Denied, EventLogReadStatus.Unavailable })
        {
            var rig = Green() with { Events = new FakeEvents(new(status, [])) };
            Assert.Equal(HealthOutcome.Skipped, (await RunAsync(rig))[HealthCheckId.Ops09].Outcome);
        }
    }

    // ---- read-only proof ----

    [Fact]
    public void Every_sql_text_the_operations_probes_own_is_read_only()
    {
        foreach (var sql in new[] { SqlOperationsSource.BuildSql, SqlOperationsSource.DataFileSql, SqlOperationsSource.AutomationSql })
            Assert.Empty(ReadOnlySqlGuard.Violations(sql));
    }

    [Fact]
    public void The_generalised_task_reader_knows_the_three_etp_task_names()
    {
        Assert.Equal(new[] { "ETP Reporting Automated Operations", "ETP Reporting Daily Backup", "ETP Reporting Monthly Recovery Drill" }, EtpScheduledTaskNames.All);
        Assert.Equal("Disabled", AutomaticImportTaskReader.StateName(1));
    }
}
