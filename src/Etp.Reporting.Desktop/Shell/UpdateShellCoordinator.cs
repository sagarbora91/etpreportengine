extern alias EtpApplication;

using EtpApplication::Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Updates;

namespace Etp.Reporting.Desktop;

/// <summary>What the "Update available" banner needs: the words, and what its two buttons do.</summary>
internal sealed record UpdateBannerModel(ReleaseVersion Version, string Title, string Body, Func<Task> InstallNow);

/// <summary>What the confirm sheet needs: the text (steps and blocks) and the release's notes.</summary>
internal sealed record UpdateConfirmRequest(UpdateConfirmText Text);

/// <summary>Everything the coordinator touches outside itself, so the order of events is tested without a window, a feed or a database.</summary>
internal sealed record UpdateShellServices(
    IUpdateSettingsStore Settings,
    Func<ReleaseVersion?> Running,
    Func<bool> IsOwner,
    Func<ReleaseVersion, string?, UpdateFeedChoice> Check,
    UpdateGateProbe Gate,
    Func<ReleaseVersion, string, ReleaseManifest, Func<UpdateGateState>, CancellationToken, Task<UpdateInstallResult>> Install,
    Func<ReleaseVersion, (UpdateAttemptRecord Attempt, UpdateAttemptOutcome Outcome)?> ResolvePending,
    // Shows the banner, or removes it when the model is null.
    Action<UpdateBannerModel?> ShowBanner,
    // The confirm sheet; true = the Owner chose Install now.
    Func<UpdateConfirmRequest, Task<bool>> Confirm,
    // The result or refusal dialog.
    Action<UpdateResultText> ShowResult,
    Action<string> SetStatus,
    // Closes ETP so setup can replace it (the window's own close, which keeps its draft guards).
    Action CloseApplication,
    Func<DateTimeOffset> NowUtc);

/// <summary>
/// 1.12 wave 2 (B2, plan section B "UI"). The order of events around an update, with no WPF in it:
/// start-up (an Owner only) -> resolve the pending attempt and show its result screen -> check the feed when the Owner
/// has switched checks on -> the banner. "Check now" in Settings runs the same check. "Install now" -> read the gate,
/// confirm (the sheet lists every block) -> the engine -> close ETP, or say why not. Nothing here restores anything.
/// </summary>
internal sealed class UpdateShellCoordinator(Func<UpdateShellServices> createServices)
{
    private readonly SemaphoreSlim checking = new(1, 1);
    private readonly Lazy<UpdateShellServices> lazyServices = new(createServices, LazyThreadSafetyMode.ExecutionAndPublication);
    private bool installing;
    private bool startedUp;

    public UpdateShellCoordinator(UpdateShellServices services) : this(() => services) { }

    private UpdateShellServices services => lazyServices.Value;

    public UpdateFeedChoice? LastChoice { get; private set; }

    public DateTimeOffset? LastCheckedUtc { get; private set; }

    public ReleaseVersion? Running => services.Running();

    public bool IsOwner => services.IsOwner();

    public event EventHandler? StateChanged;

    /// <summary>Once per start, after the Owner is known: the result of the previous attempt, then the automatic check.</summary>
    public async Task StartUpAsync(CancellationToken cancellationToken = default)
    {
        if (startedUp) return;
        startedUp = true;
        if (!services.IsOwner()) return; // a non-Owner sees nothing, and the pending attempt waits for the Owner's own start
        if (services.Running() is not { } running) return;

        try
        {
            var pending = await Task.Run(() => services.ResolvePending(running), cancellationToken).ConfigureAwait(true);
            if (pending is { } resolved)
            {
                var text = UpdateScreenText.Result(resolved.Attempt, resolved.Outcome, running, TimeZoneInfo.Local);
                if (text.NeedsAttention) services.ShowResult(text);
                else services.SetStatus(text.Title + ". " + text.Body);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DesktopDiagnostics.Record(exception, "Updates.Startup", "UPDATE_RESULT_NOT_SHOWN", DesktopDiagnosticSeverity.Warning);
        }

        try
        {
            var settings = await services.Settings.LoadAsync(cancellationToken).ConfigureAwait(true);
            if (settings.CheckEnabled) await CheckAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The automatic check is a convenience; the Owner sees the reason on Settings > Software update.
            DesktopDiagnostics.Record(exception, "Updates.Startup", "UPDATE_CHECK_AT_START_FAILED", DesktopDiagnosticSeverity.Warning);
        }
    }

    /// <summary>Reads the feed and shows or removes the banner. Safe to call twice at once (the second waits).</summary>
    public async Task<UpdateFeedChoice?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!services.IsOwner() || services.Running() is not { } running) { services.ShowBanner(null); return null; }
        await checking.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var settings = await services.Settings.LoadAsync(cancellationToken).ConfigureAwait(true);
            var feed = UpdatePaths.ResolveFeed(settings.FeedPath);
            var choice = await Task.Run(() => services.Check(running, feed), cancellationToken).ConfigureAwait(true);
            LastChoice = choice;
            LastCheckedUtc = services.NowUtc();
            if (UpdateBannerRules.ShouldShow(services.IsOwner(), choice.Status) && choice.Offer is { } offer)
                services.ShowBanner(new UpdateBannerModel(offer.Version, UpdateScreenText.BannerTitle(offer.Version), UpdateScreenText.BannerBody(offer), () => InstallAsync(offer)));
            else
                services.ShowBanner(null);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return choice;
        }
        finally { checking.Release(); }
    }

    /// <summary>The banner's "Install now": gate, confirm, engine, close.</summary>
    public async Task InstallAsync(ReleaseManifest offer)
    {
        if (installing || !services.IsOwner() || services.Running() is not { } running) return;
        installing = true;
        try
        {
            var gate = UpdateGate.Evaluate(await services.Gate.ReadAsync(CancellationToken.None).ConfigureAwait(true));
            var text = UpdateScreenText.Confirm(offer, gate);
            if (!await services.Confirm(new UpdateConfirmRequest(text)).ConfigureAwait(true)) return;
            if (!text.CanInstall) return; // the sheet cannot confirm while blocks are listed; this is the belt to that brace

            var settings = await services.Settings.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            var feed = UpdatePaths.ResolveFeed(settings.FeedPath);
            if (feed is null)
            {
                services.ShowResult(new("The update folder was not found", UpdateTextTone.Warning,
                    "ETP cannot find the update folder. Check that OneDrive is signed in, or set the folder in Settings > Software update.", true, null));
                return;
            }

            // The whole engine runs off the UI thread, so its gate reads (SQL and window state) never wait on the UI thread.
            var result = await Task.Run(() => services.Install(running, feed, offer, services.Gate.ReadBlocking, CancellationToken.None)).ConfigureAwait(true);
            if (result.Status == UpdateInstallStatus.ExitNow)
            {
                services.SetStatus(result.Message);
                services.CloseApplication();
                return;
            }
            var title = result.Status switch
            {
                UpdateInstallStatus.Blocked => "ETP cannot start the update yet",
                UpdateInstallStatus.ReleaseChanged => "The update has changed",
                UpdateInstallStatus.BackupFailed => "The backup did not complete",
                _ => "The update did not start",
            };
            services.ShowResult(new(title, UpdateTextTone.Warning, UpdateScreenText.InstallRefusal(result.Message, result.Blocks), true, null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var reference = DesktopDiagnostics.Record(exception, "Updates.Install", "UPDATE_INSTALL_FAILED");
            services.ShowResult(new("The update did not start", UpdateTextTone.Critical,
                DesktopDiagnostics.WithReference("Nothing was installed. " + DesktopFriendlyError.Describe(exception), reference), true, null));
        }
        finally { installing = false; }
    }
}
