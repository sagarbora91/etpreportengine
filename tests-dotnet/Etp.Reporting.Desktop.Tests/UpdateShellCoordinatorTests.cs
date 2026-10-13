using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 wave 2 (B2). The order of events around an update, with fakes for everything outside the coordinator: a non-Owner
/// sees and reads nothing; an Owner with checks on gets the banner at start; the result of the last attempt comes before the
/// check; "Install now" reads the gate, shows the sheet (with every block), and only an unblocked, confirmed install reaches
/// the engine; ExitNow closes ETP; anything else is said in words. Nothing restores anything.
/// </summary>
public sealed class UpdateShellCoordinatorTests
{
    private static readonly ReleaseVersion Running = new(1, 12, 0);

    private static ReleaseManifest Offer(string version = "1.12.1")
    {
        var key = new ReleaseSigningKey("0123456789abcdef", Convert.ToBase64String(new byte[65]));
        return ReleaseManifestParser.Parse(Manifest(version, key.KeyId)).Manifest!;
    }

    private sealed class Harness
    {
        public bool Owner = true;
        public ReleaseVersion? RunningVersion = Running;
        public UpdateSettings Settings = new(@"D:\Feed", true);
        public UpdateFeedChoice Choice = new(UpdateFeedStatus.UpdateAvailable, Offer(), "1.12.1", []);
        public UpdateGateState Gate = new(true, true, true, false, false, false, false, false, false);
        public (UpdateAttemptRecord Attempt, UpdateAttemptOutcome Outcome)? Pending;
        public bool Confirms = true;
        public UpdateInstallResult InstallResult = new(UpdateInstallStatus.ExitNow, "Setup has started. ETP will close now and open again when the update is finished.", [], "abc");
        public Exception? InstallThrows;

        public readonly List<string> Events = [];
        public readonly List<UpdateBannerModel?> Banners = [];
        public readonly List<UpdateResultText> Results = [];
        public readonly List<string> Statuses = [];
        public readonly List<UpdateConfirmRequest> Confirmations = [];
        public (string Feed, ReleaseManifest Offer)? Installed;
        public int SettingsReads;

        public UpdateShellCoordinator Create() => new(new UpdateShellServices(
            Settings: new FakeStore(this),
            Running: () => RunningVersion,
            IsOwner: () => Owner,
            Check: (running, feed) => { Events.Add("check:" + feed); return Choice; },
            Gate: new UpdateGateProbe(new UpdateGateSources(
                IsOwner: () => Gate.IsOwner,
                ImportRunningInThisWindow: () => Gate.ImportRunning,
                ImportOpenInDatabase: _ => Task.FromResult(false),
                AutomationLeaseHeld: _ => Task.FromResult(Gate.HealthRunRunning),
                OperationsRunnerBusy: () => Gate.BackupRunning,
                ScheduledBackupRunning: () => false,
                ScheduledDrillRunning: () => Gate.RecoveryDrillRunning,
                EntryDraftOpen: () => Gate.EntryDraftOpen,
                OtherEtpSessionOpen: () => Gate.OtherEtpSessionOpen,
                KeysConfigured: () => Gate.KeysConfigured,
                UpdateCheckEnabled: _ => Task.FromResult(Gate.UpdateCheckEnabled))),
            Install: (running, feed, offer, gateNow, token) =>
            {
                Events.Add("install");
                if (InstallThrows is not null) throw InstallThrows;
                Installed = (feed, offer);
                // The engine reads the gate itself; prove the delegate it was given works off the UI thread.
                _ = gateNow();
                return Task.FromResult(InstallResult);
            },
            ResolvePending: running => { Events.Add("resolve"); return Pending; },
            ShowBanner: model => { Events.Add(model is null ? "banner:none" : "banner:" + model.Version); Banners.Add(model); },
            Confirm: request => { Events.Add("confirm"); Confirmations.Add(request); return Task.FromResult(Confirms); },
            ShowResult: text => { Events.Add("result:" + text.Title); Results.Add(text); },
            SetStatus: text => { Events.Add("status"); Statuses.Add(text); },
            CloseApplication: () => Events.Add("close"),
            NowUtc: () => new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero)));
    }

    private sealed class FakeStore(Harness harness) : IUpdateSettingsStore
    {
        public Task<UpdateSettings> LoadAsync(CancellationToken cancellationToken = default) { harness.SettingsReads++; return Task.FromResult(harness.Settings); }
        public Task SaveAsync(UpdateSettings settings, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static UpdateAttemptRecord Attempt() => new(
        UpdateAttemptRecord.NewAttemptId(), UpdateAttemptEvent.Started, "1.12.0", "1.12.1", Sha, @"C:\ProgramData\EtpReporting\Backups\x.bak",
        new DateTimeOffset(2026, 10, 14, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 14, 8, 1, 0, TimeSpan.Zero), null, null, DateTimeOffset.UtcNow);

    // ------------------------------------------------------------ start-up

    [Fact]
    public async Task A_non_owner_sees_nothing_and_nothing_is_read()
    {
        var h = new Harness { Owner = false, Pending = (Attempt(), UpdateAttemptOutcome.Failed) };
        await h.Create().StartUpAsync();
        Assert.Empty(h.Events);
        Assert.Equal(0, h.SettingsReads);
    }

    [Fact]
    public async Task A_non_owner_check_clears_the_banner_and_reads_no_feed()
    {
        var h = new Harness { Owner = false };
        Assert.Null(await h.Create().CheckAsync());
        Assert.Equal(new[] { "banner:none" }, h.Events);
    }

    [Fact]
    public async Task A_development_build_offers_nothing()
    {
        var h = new Harness { RunningVersion = null };
        await h.Create().StartUpAsync();
        Assert.Empty(h.Events);
    }

    [Fact]
    public async Task An_owner_with_checks_on_gets_the_banner_for_the_saved_folder_at_start()
    {
        var h = new Harness();
        var coordinator = h.Create();
        await coordinator.StartUpAsync();
        Assert.Equal(new[] { "resolve", @"check:D:\Feed", "banner:1.12.1" }, h.Events);
        var banner = Assert.Single(h.Banners)!;
        Assert.Equal("ETP 1.12.1 is available", banner.Title);
        Assert.Equal(UpdateFeedStatus.UpdateAvailable, coordinator.LastChoice!.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), coordinator.LastCheckedUtc);
    }

    [Fact]
    public async Task With_checks_off_nothing_is_checked_at_start_but_Check_now_still_works_and_shows_the_banner()
    {
        var h = new Harness { Settings = new UpdateSettings(null, false) };
        var coordinator = h.Create();
        await coordinator.StartUpAsync();
        Assert.Equal(new[] { "resolve" }, h.Events);

        var raised = 0;
        coordinator.StateChanged += (_, _) => raised++;
        var choice = await coordinator.CheckAsync();
        Assert.Equal(UpdateFeedStatus.UpdateAvailable, choice!.Status);
        Assert.Equal(1, raised);
        // No saved folder: the engine's folder resolution falls back to OneDrive (null when this PC has none).
        Assert.Contains(h.Events, e => e.StartsWith("check:", StringComparison.Ordinal));
        Assert.Contains("banner:1.12.1", h.Events);
    }

    [Fact]
    public async Task When_the_feed_has_nothing_newer_the_banner_is_removed()
    {
        var h = new Harness { Choice = new UpdateFeedChoice(UpdateFeedStatus.UpToDate, null, null, []) };
        await h.Create().StartUpAsync();
        Assert.Equal("banner:none", h.Events[^1]);
        Assert.Null(Assert.Single(h.Banners));
    }

    [Fact]
    public async Task A_failed_attempt_is_shown_before_any_check_and_a_successful_one_only_in_the_status_line()
    {
        var failed = new Harness { Pending = (Attempt(), UpdateAttemptOutcome.Failed) };
        await failed.Create().StartUpAsync();
        Assert.Equal("resolve", failed.Events[0]);
        Assert.Equal("result:The update did not finish", failed.Events[1]);
        Assert.Contains(failed.Events, e => e.StartsWith("check:", StringComparison.Ordinal));
        Assert.True(failed.Results[0].NeedsAttention);

        var succeeded = new Harness { Pending = (Attempt(), UpdateAttemptOutcome.Succeeded) };
        await succeeded.Create().StartUpAsync();
        Assert.Empty(succeeded.Results);
        Assert.Contains("ETP is now version 1.12.0", Assert.Single(succeeded.Statuses), StringComparison.Ordinal);

        var notStarted = new Harness { Pending = (Attempt(), UpdateAttemptOutcome.NotStarted) };
        await notStarted.Create().StartUpAsync();
        Assert.Equal("The update did not start", Assert.Single(notStarted.Results).Title);
    }

    [Fact]
    public async Task Start_up_runs_once_per_start()
    {
        var h = new Harness();
        var coordinator = h.Create();
        await coordinator.StartUpAsync();
        var events = h.Events.Count;
        await coordinator.StartUpAsync();
        Assert.Equal(events, h.Events.Count);
    }

    // ------------------------------------------------------------ install

    [Fact]
    public async Task Install_now_with_a_clear_gate_confirms_runs_the_engine_with_the_saved_folder_and_closes_ETP()
    {
        var h = new Harness();
        var offer = Offer();
        await h.Create().InstallAsync(offer);
        Assert.Equal(new[] { "confirm", "install", "status", "close" }, h.Events);
        Assert.Equal((@"D:\Feed", offer), h.Installed);
        var sheet = Assert.Single(h.Confirmations).Text;
        Assert.True(sheet.CanInstall);
        Assert.Equal("Install ETP 1.12.1?", sheet.Title);
        Assert.Contains("Setup has started", Assert.Single(h.Statuses), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_blocked_gate_shows_every_block_on_the_sheet_and_the_engine_is_never_called()
    {
        var h = new Harness { Gate = new UpdateGateState(true, true, true, ImportRunning: true, HealthRunRunning: false, BackupRunning: false, RecoveryDrillRunning: false, EntryDraftOpen: true, OtherEtpSessionOpen: false) };
        await h.Create().InstallAsync(Offer());
        var sheet = Assert.Single(h.Confirmations).Text;
        Assert.False(sheet.CanInstall);
        Assert.Equal(2, sheet.Blocks.Count);
        Assert.DoesNotContain("install", h.Events);
        Assert.DoesNotContain("close", h.Events);
    }

    [Fact]
    public async Task Keep_working_installs_nothing()
    {
        var h = new Harness { Confirms = false };
        await h.Create().InstallAsync(Offer());
        Assert.Equal(new[] { "confirm" }, h.Events);
    }

    [Fact]
    public async Task Even_if_a_sheet_reports_confirmed_while_blocks_are_listed_the_engine_is_not_called()
    {
        var h = new Harness { Confirms = true, Gate = new UpdateGateState(true, true, true, false, false, BackupRunning: true, false, false, false) };
        await h.Create().InstallAsync(Offer());
        Assert.DoesNotContain("install", h.Events);
    }

    [Fact]
    public async Task A_non_owner_cannot_start_an_install()
    {
        var h = new Harness { Owner = false };
        await h.Create().InstallAsync(Offer());
        Assert.Empty(h.Events);
    }

    [Fact]
    public async Task An_engine_refusal_is_said_in_words_and_ETP_stays_open()
    {
        var h = new Harness
        {
            InstallResult = new UpdateInstallResult(UpdateInstallStatus.Blocked, "x", [UpdateGateBlock.ImportRunning], null),
        };
        await h.Create().InstallAsync(Offer());
        var result = Assert.Single(h.Results);
        Assert.Equal("ETP cannot start the update yet", result.Title);
        Assert.Contains("An import is running.", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("close", h.Events);

        var backup = new Harness { InstallResult = UpdateInstallResult.Fail(UpdateInstallStatus.BackupFailed, "The backup before the update did not complete, so nothing was installed. Your data is unchanged. Call Sagar.") };
        await backup.Create().InstallAsync(Offer());
        Assert.Equal("The backup did not complete", Assert.Single(backup.Results).Title);
        Assert.Contains("Your data is unchanged", backup.Results[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("close", backup.Events);
    }

    [Fact]
    public async Task An_unexpected_failure_says_nothing_was_installed()
    {
        var h = new Harness { InstallThrows = new InvalidOperationException("Synthetic failure") };
        await h.Create().InstallAsync(Offer());
        var result = Assert.Single(h.Results);
        Assert.Equal(UpdateTextTone.Critical, result.Tone);
        Assert.StartsWith("Nothing was installed.", result.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("close", h.Events);
    }

    [Fact]
    public async Task The_banners_install_button_runs_the_same_flow()
    {
        var h = new Harness();
        var coordinator = h.Create();
        await coordinator.CheckAsync();
        await h.Banners.Single()!.InstallNow();
        Assert.Contains("install", h.Events);
        Assert.Contains("close", h.Events);
    }

    [Fact]
    public async Task A_second_install_while_one_is_running_is_ignored()
    {
        var h = new Harness();
        var gate = new TaskCompletionSource<bool>();
        var services = new UpdateShellCoordinatorTestsFacade(h, gate);
        var coordinator = services.Create();
        var first = coordinator.InstallAsync(Offer());
        await coordinator.InstallAsync(Offer());
        Assert.Single(h.Confirmations);
        gate.SetResult(true);
        await first;
        Assert.Single(h.Confirmations);
    }

    /// <summary>A coordinator whose confirm sheet waits, so a second click can arrive while the first install is open.</summary>
    private sealed class UpdateShellCoordinatorTestsFacade(Harness h, TaskCompletionSource<bool> release)
    {
        public UpdateShellCoordinator Create()
        {
            return new UpdateShellCoordinator(() => new UpdateShellServices(
                Settings: new FakeStore(h), Running: () => Running, IsOwner: () => true,
                Check: (_, _) => h.Choice,
                Gate: new UpdateGateProbe(new UpdateGateSources(() => true, () => false, _ => Task.FromResult(false), _ => Task.FromResult(false), () => false, () => false, () => false, () => false, () => false, () => true, _ => Task.FromResult(true))),
                Install: (_, _, _, _, _) => Task.FromResult(h.InstallResult),
                ResolvePending: _ => null,
                ShowBanner: _ => { },
                Confirm: async request => { h.Confirmations.Add(request); return await release.Task; },
                ShowResult: _ => { },
                SetStatus: _ => { },
                CloseApplication: () => { },
                NowUtc: () => DateTimeOffset.UtcNow));
        }
    }
}
