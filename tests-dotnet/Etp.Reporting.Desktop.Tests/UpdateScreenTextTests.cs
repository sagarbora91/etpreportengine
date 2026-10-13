using Etp.Reporting.Application.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave 2 (B2). The words of the update screens and the rules in front of the settings: the banner is shown only to an
/// Owner and only for a verified newer release, every gate block reaches the confirm sheet with its sentence, the last
/// check says what happened (including "files ETP will not trust"), and a failed attempt names the backup and never offers
/// a restore. No window is needed: the sentences live in the Application project.
/// </summary>
public sealed class UpdateScreenTextTests
{
    private static ReleaseManifest Offer(string notes = "Synthetic release notes.", bool schemaChange = true)
    {
        var key = new ReleaseSigningKey("0123456789abcdef", Convert.ToBase64String(new byte[65]));
        var bytes = Manifest("1.12.1", key.KeyId, change: fields => { fields["notes"] = notes; fields["hasSchemaChange"] = schemaChange; });
        return ReleaseManifestParser.Parse(bytes).Manifest!;
    }

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly ReleaseVersion Running = new(1, 12, 0);

    // ------------------------------------------------------------ banner

    [Theory]
    [InlineData(true, UpdateFeedStatus.UpdateAvailable, true)]
    [InlineData(true, UpdateFeedStatus.UpToDate, false)]
    [InlineData(true, UpdateFeedStatus.NotSetUp, false)]
    [InlineData(true, UpdateFeedStatus.FeedUnavailable, false)]
    [InlineData(false, UpdateFeedStatus.UpdateAvailable, false)]
    [InlineData(false, UpdateFeedStatus.UpToDate, false)]
    public void The_banner_is_shown_only_to_an_owner_and_only_for_an_available_update(bool owner, UpdateFeedStatus status, bool shown) =>
        Assert.Equal(shown, UpdateBannerRules.ShouldShow(owner, status));

    [Fact]
    public void The_banner_says_the_version_and_the_first_line_of_the_notes()
    {
        Assert.Equal("ETP 1.12.1 is available", UpdateScreenText.BannerTitle(new ReleaseVersion(1, 12, 1)));
        Assert.Equal("First line.", UpdateScreenText.BannerBody(Offer("First line.\nSecond line.")));
        Assert.EndsWith("...", UpdateScreenText.BannerBody(Offer(new string('x', 400))));
        Assert.True(UpdateScreenText.BannerBody(Offer(new string('x', 400))).Length <= 160);
        Assert.Contains("Install it when nobody is in the middle of work", UpdateScreenText.BannerBody(Offer("   ")));
        Assert.Equal("Install now", UpdateScreenText.BannerInstallButton);
    }

    // ------------------------------------------------------------ confirm sheet

    [Fact]
    public void The_confirm_sheet_lists_the_backup_first_step_and_that_ETP_will_close()
    {
        var text = UpdateScreenText.Confirm(Offer(), UpdateGate.Evaluate(Allowed));
        Assert.Equal("Install ETP 1.12.1?", text.Title);
        Assert.True(text.CanInstall);
        Assert.Null(text.BlockedIntro);
        Assert.Contains("backup", text.Steps[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(text.Steps, step => step.Contains("ETP closes", StringComparison.Ordinal));
        Assert.Contains(text.Steps, step => step.Contains("opens again", StringComparison.Ordinal));
        Assert.Contains(text.Steps, step => step.Contains("changes the database", StringComparison.Ordinal));
    }

    [Fact]
    public void A_release_without_a_schema_change_does_not_claim_one()
    {
        var text = UpdateScreenText.Confirm(Offer(schemaChange: false), UpdateGate.Evaluate(Allowed));
        Assert.DoesNotContain(text.Steps, step => step.Contains("changes the database", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(UpdateGateState.ImportRunning), "An import is running. Wait for it to finish.")]
    [InlineData(nameof(UpdateGateState.HealthRunRunning), "The health check is running. Wait for it to finish.")]
    [InlineData(nameof(UpdateGateState.BackupRunning), "A backup is running. Wait for it to finish.")]
    [InlineData(nameof(UpdateGateState.RecoveryDrillRunning), "The recovery drill is running. Wait for it to finish.")]
    [InlineData(nameof(UpdateGateState.EntryDraftOpen), "An entry has not been saved. Save or discard it first.")]
    [InlineData(nameof(UpdateGateState.OtherEtpSessionOpen), "ETP is open in another Windows session. Close it there first.")]
    public void Every_gate_block_reaches_the_confirm_sheet_with_its_sentence_and_stops_the_install(string condition, string sentence)
    {
        var state = condition switch
        {
            nameof(UpdateGateState.ImportRunning) => Allowed with { ImportRunning = true },
            nameof(UpdateGateState.HealthRunRunning) => Allowed with { HealthRunRunning = true },
            nameof(UpdateGateState.BackupRunning) => Allowed with { BackupRunning = true },
            nameof(UpdateGateState.RecoveryDrillRunning) => Allowed with { RecoveryDrillRunning = true },
            nameof(UpdateGateState.EntryDraftOpen) => Allowed with { EntryDraftOpen = true },
            _ => Allowed with { OtherEtpSessionOpen = true },
        };
        var text = UpdateScreenText.Confirm(Offer(), UpdateGate.Evaluate(state));
        Assert.False(text.CanInstall);
        Assert.Equal(sentence, Assert.Single(text.Blocks));
        Assert.Equal("ETP cannot start the update yet:", text.BlockedIntro);
    }

    [Fact]
    public void Several_blocks_are_all_listed_at_once()
    {
        var text = UpdateScreenText.Confirm(Offer(), UpdateGate.Evaluate(Allowed with { ImportRunning = true, EntryDraftOpen = true, UpdateCheckEnabled = false }));
        Assert.Equal(3, text.Blocks.Count);
        Assert.Contains(text.Blocks, block => block.Contains("turned off", StringComparison.Ordinal));
    }

    [Fact]
    public void The_refusal_text_spells_out_the_engines_blocks_or_passes_its_own_message_through()
    {
        Assert.Equal("The update in the update folder has changed since ETP checked. Check again.",
            UpdateScreenText.InstallRefusal("The update in the update folder has changed since ETP checked. Check again.", []));
        var blocked = UpdateScreenText.InstallRefusal("ignored", [UpdateGateBlock.ImportRunning, UpdateGateBlock.BackupRunning]);
        Assert.Contains("An import is running.", blocked, StringComparison.Ordinal);
        Assert.Contains("A backup is running.", blocked, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", blocked, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ settings rules

    [Theory]
    [InlineData(null, "Because", null)]
    [InlineData("", "Because", null)]
    [InlineData("   ", "Because", null)]
    [InlineData(@"D:\Shared\ETP-Updates", "Because", null)]
    [InlineData(@"\\server\share\ETP-Updates", "Because", null)]
    [InlineData(@"D:\Shared\ETP-Updates", "", "Enter a reason for the change.")]
    [InlineData(@"D:\Shared\ETP-Updates", "   ", "Enter a reason for the change.")]
    [InlineData(@"ETP-Updates", "Because", @"Enter the full path of the update folder, for example D:\ETP-Updates, or leave it blank to use OneDrive\ETP-Updates.")]
    [InlineData(@"D:\Bad|Folder", "Because", "The update folder path has a character that Windows does not allow.")]
    public void The_settings_rules_accept_a_full_path_or_blank_and_always_need_a_reason(string? path, string? reason, string? expected) =>
        Assert.Equal(expected, UpdateSettingsRules.Validate(path, reason));

    [Fact]
    public void Too_long_a_path_or_reason_is_refused_before_SQL_sees_it()
    {
        Assert.Equal("The update folder path is too long.", UpdateSettingsRules.Validate(@"D:\" + new string('a', 400), "Because"));
        Assert.Equal("The reason is too long. Use 500 characters or fewer.", UpdateSettingsRules.Validate(null, new string('r', 501)));
        Assert.Null(UpdateSettingsRules.NormalizePath("   "));
        Assert.Equal(@"D:\Feed", UpdateSettingsRules.NormalizePath(@"  D:\Feed  "));
    }

    // ------------------------------------------------------------ last check

    [Fact]
    public void The_last_check_says_what_it_found()
    {
        var at = new DateTimeOffset(2026, 10, 14, 9, 5, 0, TimeSpan.Zero);
        var available = UpdateScreenText.LastCheck(new UpdateFeedChoice(UpdateFeedStatus.UpdateAvailable, Offer(), "1.12.1", []), Running, at, Utc);
        Assert.Equal(("Update available", UpdateTextTone.Information), (available.Word, available.Tone));
        Assert.Equal("ETP 1.12.1 is ready to install. Checked at 09:05 on 14 Oct 2026.", available.Sentence);

        var upToDate = UpdateScreenText.LastCheck(new UpdateFeedChoice(UpdateFeedStatus.UpToDate, null, null, []), Running, at, Utc);
        Assert.Equal(("Up to date", UpdateTextTone.Success), (upToDate.Word, upToDate.Tone));
        Assert.Contains("ETP 1.12.0 is the newest release", upToDate.Sentence, StringComparison.Ordinal);

        var notSetUp = UpdateScreenText.LastCheck(new UpdateFeedChoice(UpdateFeedStatus.NotSetUp, null, null, []), Running, at, Utc);
        Assert.Equal("Updates are not set up on this PC. Install new versions with the setup file. Checked at 09:05 on 14 Oct 2026.", notSetUp.Sentence);

        var missing = UpdateScreenText.LastCheck(new UpdateFeedChoice(UpdateFeedStatus.FeedUnavailable, null, null, []), Running, at, Utc);
        Assert.Equal(UpdateTextTone.Warning, missing.Tone);
        Assert.Contains("OneDrive", missing.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_ETP_will_not_trust_are_named_on_a_second_line()
    {
        var untrusted = new UpdateFeedChoice(UpdateFeedStatus.UpToDate, null, null,
            [new UpdateFeedFinding("1.12.5", UpdateFeedRejection.SignatureNotValid, null, null, null)]);
        var summary = UpdateScreenText.LastCheck(untrusted, Running, null, Utc);
        Assert.Equal("The update folder holds files ETP will not trust. ETP ignored them. Tell Sagar.", summary.Extra);

        var needsEarlier = new UpdateFeedChoice(UpdateFeedStatus.UpToDate, null, null,
            [new UpdateFeedFinding("1.13.0", UpdateFeedRejection.NeedsEarlierRelease, null, null, "1.12.1")]);
        Assert.Equal("Release 1.13.0 needs ETP 1.12.1 installed first.", UpdateScreenText.LastCheck(needsEarlier, Running, null, Utc).Extra);
    }

    [Fact]
    public void A_development_build_and_a_missing_check_are_said_plainly()
    {
        Assert.Equal("Not offered", UpdateScreenText.LastCheck(null, null, null, Utc).Word);
        var never = UpdateScreenText.LastCheck(null, Running, null, Utc);
        Assert.Equal("Not checked", never.Word);
        Assert.Contains("Press Check now", never.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_switch_help_says_it_is_off_and_what_still_works()
    {
        Assert.Contains("Check now still works", UpdateScreenText.CheckSwitchHelp(false), StringComparison.Ordinal);
        Assert.Contains("when it starts", UpdateScreenText.CheckSwitchHelp(true), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ result screen

    private static UpdateAttemptRecord Attempt(bool withBackup = true) => new(
        UpdateAttemptRecord.NewAttemptId(), UpdateAttemptEvent.Started, "1.12.0", "1.12.1", Sha,
        withBackup ? @"C:\ProgramData\EtpReporting\Backups\EtpReporting_PRE_UPDATE_20261014.bak" : null,
        withBackup ? new DateTimeOffset(2026, 10, 14, 9, 5, 0, TimeSpan.Zero) : null,
        new DateTimeOffset(2026, 10, 14, 9, 6, 0, TimeSpan.Zero), null,
        @"C:\ProgramData\EtpReporting\Updates\1.12.1\setup-abc.log", new DateTimeOffset(2026, 10, 14, 9, 6, 0, TimeSpan.Zero));

    [Fact]
    public void A_failed_update_names_the_backup_says_nothing_was_restored_and_offers_the_log_folder()
    {
        var result = UpdateScreenText.Result(Attempt(), UpdateAttemptOutcome.Failed, Running, Utc);
        Assert.True(result.NeedsAttention);
        Assert.Equal(UpdateTextTone.Critical, result.Tone);
        Assert.Equal("The update did not finish", result.Title);
        Assert.Contains("Your data is safe.", result.Body, StringComparison.Ordinal);
        Assert.Contains("Backup taken at 09:05 (EtpReporting_PRE_UPDATE_20261014.bak)", result.Body, StringComparison.Ordinal);
        Assert.Contains("Nothing was restored", result.Body, StringComparison.Ordinal);
        Assert.Contains("Call Sagar", result.Body, StringComparison.Ordinal);
        Assert.Contains("ETP is running version 1.12.0", result.Body, StringComparison.Ordinal);
        Assert.Equal(@"C:\ProgramData\EtpReporting\Updates\1.12.1", result.SetupLogFolder);
        // Never a restore: there is no sentence that tells anyone to restore, and no restore wording in the button.
        Assert.DoesNotContain("restore the", result.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Open setup log folder", UpdateScreenText.OpenSetupLogFolderButton);
    }

    [Fact]
    public void A_failure_without_a_recorded_backup_still_says_the_data_is_safe()
    {
        var result = UpdateScreenText.Result(Attempt(withBackup: false), UpdateAttemptOutcome.Failed, Running, Utc);
        Assert.Contains("Update failed. Your data is safe. Call Sagar.", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void An_update_that_never_started_says_nothing_changed()
    {
        var result = UpdateScreenText.Result(Attempt(), UpdateAttemptOutcome.NotStarted, Running, Utc);
        Assert.True(result.NeedsAttention);
        Assert.Equal(UpdateTextTone.Information, result.Tone);
        Assert.Equal("The update did not start", result.Title);
        Assert.Contains("nothing was installed", result.Body, StringComparison.Ordinal);
        Assert.Contains("permission prompt", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_successful_update_needs_no_dialog_only_a_status_line()
    {
        var result = UpdateScreenText.Result(Attempt(), UpdateAttemptOutcome.Succeeded, new ReleaseVersion(1, 12, 1), Utc);
        Assert.False(result.NeedsAttention);
        Assert.Equal("ETP is now version 1.12.1", result.Title);
        Assert.Equal(UpdateTextTone.Success, result.Tone);
    }

    private static readonly UpdateGateState Allowed = new(
        IsOwner: true, KeysConfigured: true, UpdateCheckEnabled: true, ImportRunning: false, HealthRunRunning: false,
        BackupRunning: false, RecoveryDrillRunning: false, EntryDraftOpen: false, OtherEtpSessionOpen: false);
}
