using System.Text;
using Etp.Reporting.Application.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 (B1): every gate (decision 39 (2): shop hours allowed, never during an import or a health run), a non-Owner gets
/// nothing, the attempt record round trip, the next-start outcome (no restore ever), the setup-result file the installer
/// writes, and the pre-update backup receipt check.
/// </summary>
public sealed class UpdateGateAndJournalTests
{
    private static readonly UpdateGateState Clear = new(
        IsOwner: true, KeysConfigured: true, UpdateCheckEnabled: true, ImportRunning: false, HealthRunRunning: false,
        BackupRunning: false, RecoveryDrillRunning: false, EntryDraftOpen: false, OtherEtpSessionOpen: false);

    // ------------------------------------------------------------ gate

    [Fact]
    public void An_owner_with_nothing_running_may_update_at_any_time_of_day()
    {
        var result = UpdateGate.Evaluate(Clear);
        Assert.True(result.Allowed);
        Assert.True(result.ShowAnything);
        // Shop hours are allowed: the gate has no clock to consult.
        Assert.DoesNotContain(typeof(UpdateGateState).GetProperties(), p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(TimeOnly));
    }

    public static TheoryData<string, UpdateGateBlock> Blocks => new()
    {
        { nameof(UpdateGateState.KeysConfigured), UpdateGateBlock.NotSetUp },
        { nameof(UpdateGateState.UpdateCheckEnabled), UpdateGateBlock.TurnedOff },
        { nameof(UpdateGateState.ImportRunning), UpdateGateBlock.ImportRunning },
        { nameof(UpdateGateState.HealthRunRunning), UpdateGateBlock.HealthRunRunning },
        { nameof(UpdateGateState.BackupRunning), UpdateGateBlock.BackupRunning },
        { nameof(UpdateGateState.RecoveryDrillRunning), UpdateGateBlock.RecoveryDrillRunning },
        { nameof(UpdateGateState.EntryDraftOpen), UpdateGateBlock.EntryDraftOpen },
        { nameof(UpdateGateState.OtherEtpSessionOpen), UpdateGateBlock.OtherEtpSessionOpen },
    };

    [Theory]
    [MemberData(nameof(Blocks))]
    public void Each_condition_blocks_on_its_own_with_plain_words(string condition, UpdateGateBlock block)
    {
        var state = condition switch
        {
            nameof(UpdateGateState.KeysConfigured) => Clear with { KeysConfigured = false },
            nameof(UpdateGateState.UpdateCheckEnabled) => Clear with { UpdateCheckEnabled = false },
            nameof(UpdateGateState.ImportRunning) => Clear with { ImportRunning = true },
            nameof(UpdateGateState.HealthRunRunning) => Clear with { HealthRunRunning = true },
            nameof(UpdateGateState.BackupRunning) => Clear with { BackupRunning = true },
            nameof(UpdateGateState.RecoveryDrillRunning) => Clear with { RecoveryDrillRunning = true },
            nameof(UpdateGateState.EntryDraftOpen) => Clear with { EntryDraftOpen = true },
            _ => Clear with { OtherEtpSessionOpen = true },
        };
        var result = UpdateGate.Evaluate(state);
        Assert.False(result.Allowed);
        Assert.Equal(new[] { block }, result.Blocks);
        Assert.False(string.IsNullOrWhiteSpace(UpdateGate.Describe(block)));
    }

    [Fact]
    public void Every_block_that_applies_is_listed_at_once()
    {
        var result = UpdateGate.Evaluate(Clear with { ImportRunning = true, EntryDraftOpen = true, UpdateCheckEnabled = false });
        Assert.Equal(new[] { UpdateGateBlock.TurnedOff, UpdateGateBlock.ImportRunning, UpdateGateBlock.EntryDraftOpen }, result.Blocks);
    }

    [Fact]
    public void A_non_owner_gets_no_banner_and_no_reasons()
    {
        var result = UpdateGate.Evaluate(Clear with { IsOwner = false, ImportRunning = true, KeysConfigured = false });
        Assert.Equal(new[] { UpdateGateBlock.NotOwner }, result.Blocks);
        Assert.False(result.ShowAnything);
    }

    // ------------------------------------------------------------ attempt record

    private static UpdateAttemptRecord Started(string id, DateTimeOffset at) => new(
        id, UpdateAttemptEvent.Started, "1.11.3", "1.12.0", Sha, @"C:\ProgramData\EtpReporting\Backups\EtpReporting-20261012-083000-abc.bak",
        at.AddMinutes(-2), at, null, @"C:\ProgramData\EtpReporting\Updates\1.12.0\setup-x.log", at);

    [Fact]
    public void The_attempt_record_round_trips_and_a_torn_line_is_skipped()
    {
        var at = new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.Zero);
        var first = Started(UpdateAttemptRecord.NewAttemptId(), at);
        var outcome = UpdateOutcomeResolver.OutcomeRecord(first, UpdateAttemptOutcome.Succeeded, at.AddMinutes(9));
        var lines = new[] { UpdateAttemptJournal.Serialize(first), UpdateAttemptJournal.Serialize(outcome), "{\"attemptId\":\"torn", "" };

        var read = UpdateAttemptJournal.Parse(lines);
        Assert.Equal(new[] { first, outcome }, read);
        Assert.Contains("\"event\":\"STARTED\"", lines[0], StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"SUCCEEDED\"", lines[1], StringComparison.Ordinal);
        Assert.Null(UpdateAttemptJournal.PendingAttempt(read));

        var second = Started(UpdateAttemptRecord.NewAttemptId(), at.AddDays(1));
        Assert.Equal(second, UpdateAttemptJournal.PendingAttempt([.. read, second]));
        Assert.Throws<ArgumentException>(() => UpdateAttemptJournal.Serialize(first with { AttemptId = "../x" }));
    }

    [Fact]
    public void The_next_start_works_out_the_outcome_and_never_restores()
    {
        var pending = Started("0123456789abcdef0123456789abcdef", DateTimeOffset.UtcNow);
        var target = new ReleaseVersion(1, 12, 0);
        var old = new ReleaseVersion(1, 11, 3);
        UpdateSetupResult Result(string outcome, string? id = null) => new(id ?? pending.AttemptId, "1.12.0", outcome, "");

        Assert.Equal(UpdateAttemptOutcome.Succeeded, UpdateOutcomeResolver.Resolve(pending, target, Result("SUCCEEDED"), false));
        Assert.Equal(UpdateAttemptOutcome.Failed, UpdateOutcomeResolver.Resolve(pending, target, Result("SUCCEEDED"), true));
        Assert.Equal(UpdateAttemptOutcome.Failed, UpdateOutcomeResolver.Resolve(pending, target, Result("FAILED"), true));
        Assert.Equal(UpdateAttemptOutcome.Failed, UpdateOutcomeResolver.Resolve(pending, old, Result("FAILED"), false));
        Assert.Equal(UpdateAttemptOutcome.NotStarted, UpdateOutcomeResolver.Resolve(pending, old, Result("CANCELLED"), false));
        // No result for this attempt (UAC declined, or power lost before setup started) and the old build still running.
        Assert.Equal(UpdateAttemptOutcome.NotStarted, UpdateOutcomeResolver.Resolve(pending, old, null, false));
        Assert.Equal(UpdateAttemptOutcome.NotStarted, UpdateOutcomeResolver.Resolve(pending, old, Result("SUCCEEDED", "ffffffffffffffffffffffffffffffff"), false));
        // Setup's post-install step failed before it could write a result: the marker says so.
        Assert.Equal(UpdateAttemptOutcome.Failed, UpdateOutcomeResolver.Resolve(pending, target, null, true));
        Assert.Equal(UpdateAttemptOutcome.Succeeded, UpdateOutcomeResolver.Resolve(pending, target, null, false));

        // The resolver only chooses a screen; there is no restore anywhere in the update engine.
        var root = RepositoryRoot();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Etp.Reporting.Application", "Updates"), "*.cs")
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Updates"), "*.cs")))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("restore-etp-database", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("invoke-release-rollback", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_failure_text_names_the_backup_time_and_file()
    {
        var pending = Started("0123456789abcdef0123456789abcdef", new DateTimeOffset(2026, 10, 12, 8, 32, 0, TimeSpan.Zero));
        Assert.Equal("Update failed. Setup stopped before it finished, so some database changes may already be in place. The backup taken before the update is EtpReporting-20261012-083000-abc.bak (taken at 08:30). Call Sagar before using ETP.",
            UpdateOutcomeResolver.FailureText(pending, TimeZoneInfo.Utc));
        Assert.Equal("Update failed. Setup stopped before it finished, so some database changes may already be in place. ETP has no record of a backup for this update. Call Sagar before using ETP.", UpdateOutcomeResolver.FailureText(pending with { BackupPath = null }, TimeZoneInfo.Utc));
    }

    [Fact]
    public void The_setup_result_file_the_installer_writes_is_read_and_anything_else_is_ignored()
    {
        // The exact text installer\EtpReportingEngine.iss builds (RecordEtpUpdateResult), with its constants filled in.
        var iss = File.ReadAllText(Path.Combine(RepositoryRoot(), "installer", "EtpReportingEngine.iss"));
        Assert.Contains("'{\"attemptId\":\"' + EtpUpdateAttemptId() + '\",\"version\":\"{#AppVersion}\",\"outcome\":\"' + Outcome + '\",\"reason\":\"' + Reason + '\"}'", iss, StringComparison.Ordinal);
        var written = "{\"attemptId\":\"0123456789abcdef0123456789abcdef\",\"version\":\"1.12.0\",\"outcome\":\"FAILED\",\"reason\":\"SETUP_STEP_FAILED\"}";
        var result = UpdateSetupResult.TryParse(Encoding.ASCII.GetBytes(written));
        Assert.Equal(new UpdateSetupResult("0123456789abcdef0123456789abcdef", "1.12.0", "FAILED", "SETUP_STEP_FAILED"), result);

        // The attempt id is empty when setup was started without a valid one: never matches an attempt.
        Assert.Null(UpdateSetupResult.TryParse("{\"attemptId\":\"\",\"version\":\"1.12.0\",\"outcome\":\"SUCCEEDED\",\"reason\":\"\"}"u8));
        Assert.Null(UpdateSetupResult.TryParse("{\"attemptId\":\"0123456789abcdef0123456789abcdef\",\"version\":\"1.12.0\",\"outcome\":\"MAYBE\"}"u8));
        Assert.Null(UpdateSetupResult.TryParse("garbage"u8));
    }

    // ------------------------------------------------------------ pre-update backup receipt

    private static byte[] Receipt(string purpose = "PRE_UPDATE", bool verified = true, string database = "EtpReporting", string? verifiedAt = null) =>
        Encoding.UTF8.GetBytes($$"""
            {"schemaVersion":2,"verified":{{(verified ? "true" : "false")}},"serverInstance":".\\SQLEXPRESS","database":"{{database}}",
             "backupPath":"C:\\ProgramData\\EtpReporting\\Backups\\EtpReporting-20261012-083000-abc.bak","sha256":"{{Sha}}","lengthBytes":1,
             "verifiedAtUtc":"{{verifiedAt ?? "2026-10-12T08:30:00.0000000Z"}}","encryption":"NONE","purpose":"{{purpose}}"}
            """);

    [Fact]
    public void Only_a_fresh_verified_pre_update_receipt_for_this_database_lets_the_update_continue()
    {
        var now = new DateTimeOffset(2026, 10, 12, 8, 40, 0, TimeSpan.Zero);
        var ok = PreUpdateBackupReceipt.Check(Receipt(), "EtpReporting", now);
        Assert.True(ok.Passed);
        Assert.EndsWith("EtpReporting-20261012-083000-abc.bak", ok.BackupPath, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 30, 0, TimeSpan.Zero), ok.VerifiedUtc);

        Assert.Equal(PreUpdateBackupProblem.NotVerified, PreUpdateBackupReceipt.Check(Receipt(verified: false), "EtpReporting", now).Problem);
        // A daily backup is not the update's own backup.
        Assert.Equal(PreUpdateBackupProblem.WrongPurpose, PreUpdateBackupReceipt.Check(Receipt("SCHEDULED"), "EtpReporting", now).Problem);
        Assert.Equal(PreUpdateBackupProblem.WrongDatabase, PreUpdateBackupReceipt.Check(Receipt(database: "EtpOther"), "EtpReporting", now).Problem);
        Assert.Equal(PreUpdateBackupProblem.TooOld, PreUpdateBackupReceipt.Check(Receipt(), "EtpReporting", now.AddHours(2)).Problem);
        Assert.Equal(PreUpdateBackupProblem.FromTheFuture, PreUpdateBackupReceipt.Check(Receipt(verifiedAt: "2026-10-12T09:30:00Z"), "EtpReporting", now).Problem);
        Assert.Equal(PreUpdateBackupProblem.Unreadable, PreUpdateBackupReceipt.Check("{"u8, "EtpReporting", now).Problem);
    }
}
