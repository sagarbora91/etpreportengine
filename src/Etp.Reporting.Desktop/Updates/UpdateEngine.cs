extern alias EtpApplication;

using System.Diagnostics;
using System.IO;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

internal enum UpdateInstallStatus
{
    /// <summary>Setup has started. The caller closes ETP now (setup waits for it).</summary>
    ExitNow,
    Blocked,
    /// <summary>The offered release no longer verifies or is no longer the same (the feed changed since the check).</summary>
    ReleaseChanged,
    StagingFailed,
    BackupFailed,
    LaunchFailed,
}

internal sealed record UpdateInstallResult(UpdateInstallStatus Status, string Message, IReadOnlyList<UpdateGateBlock> Blocks, string? AttemptId)
{
    public static UpdateInstallResult Fail(UpdateInstallStatus status, string message) => new(status, message, [], null);
}

/// <summary>
/// 1.12 (B1, plan section B "Engine"). Not wired: wave 2 (B2) adds the banner, the confirm dialog, Settings and the result
/// screen, and supplies the gate state, the feed path (product_settings.update_feed_path) and the connection string.
///
/// Install order, each step refusing on failure and changing nothing after a refusal except its own staging folder:
/// 1. the gate (Owner, key, switched on, no import / health run / backup / drill / draft / other session);
/// 2. the offered release is read and verified again now (the check may be hours old);
/// 3. the installer is copied to %ProgramData%\EtpReporting\Updates\&lt;version&gt;\ and the LOCAL copy re-hashed and
///    version-checked, then held open so it cannot be swapped;
/// 4. older staged installers move to PreviousReleases (two kept);
/// 5. a verified PRE_UPDATE backup through the existing backup script, receipt checked;
/// 6. the gate again (a backup takes minutes; an import may have started);
/// 7. the attempt record, flushed to disk;
/// 8. setup starts; ETP must then exit. No step ever restores anything.
/// </summary>
internal sealed class UpdateEngine(string updatesRoot)
{
    public UpdateEngine() : this(UpdatePaths.UpdatesRoot) { }

    public string UpdatesRoot { get; } = updatesRoot;

    /// <summary>The check: read the feed and choose. Read-only; safe for any role, but only an Owner is ever shown the result.</summary>
    public static UpdateFeedChoice Check(ReleaseVersion running, string? feedPath) =>
        UpdateFeedSelector.Choose(running, UpdateFeedReader.Read(feedPath));

    public async Task<UpdateInstallResult> InstallAsync(
        ReleaseVersion running,
        string feedPath,
        ReleaseManifest offered,
        Func<UpdateGateState> gateNow,
        string connectionString,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(gateNow);

        var gate = UpdateGate.Evaluate(gateNow());
        if (!gate.Allowed) return new(UpdateInstallStatus.Blocked, Describe(gate), gate.Blocks, null);

        // 2. The feed again, now. The release to install is the one the Owner confirmed, and it must still verify.
        var fresh = Check(running, feedPath);
        if (fresh.Offer is not { } current || current.Version != offered.Version || !string.Equals(current.InstallerSha256, offered.InstallerSha256, StringComparison.Ordinal))
            return UpdateInstallResult.Fail(UpdateInstallStatus.ReleaseChanged, "The update in the update folder has changed since ETP checked. Check again.");

        // 3.
        var stage = await UpdateInstallerStager.StageAsync(Path.Combine(feedPath, fresh.OfferFolderName!), current, UpdatesRoot, cancellationToken).ConfigureAwait(false);
        if (!stage.Succeeded) return UpdateInstallResult.Fail(UpdateInstallStatus.StagingFailed, UpdateInstallerStager.Describe(stage.Problem));
        using var staged = stage.Staged!;

        // 4.
        UpdateRetention.Apply(UpdatesRoot, current.Version);

        // 5.
        PreUpdateBackupCheck backup;
        try
        {
            backup = await PreUpdateBackupRunner.RunAsync(connectionString, staged.Folder, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            DesktopDiagnostics.Record(exception, "Updates.PreUpdateBackup", "PRE_UPDATE_BACKUP_NOT_STARTED");
            backup = new PreUpdateBackupCheck(PreUpdateBackupProblem.Unreadable, null, null);
        }
        if (!backup.Passed)
            return UpdateInstallResult.Fail(UpdateInstallStatus.BackupFailed, "The backup before the update did not complete, so nothing was installed. Your data is unchanged. Call Sagar.");

        // 6.
        gate = UpdateGate.Evaluate(gateNow());
        if (!gate.Allowed) return new(UpdateInstallStatus.Blocked, Describe(gate), gate.Blocks, null);

        // 7.
        var attemptId = UpdateAttemptRecord.NewAttemptId();
        var now = DateTimeOffset.UtcNow;
        var store = new UpdateAttemptStore(UpdatesRoot);
        var started = new UpdateAttemptRecord(attemptId, UpdateAttemptEvent.Started, running.ToString(), current.Version.ToString(), current.InstallerSha256,
            backup.BackupPath, backup.VerifiedUtc, now, null, UpdateInstallerLauncher.SetupLogPath(staged, attemptId), now);
        try
        {
            store.Append(started);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DesktopDiagnostics.Record(exception, "Updates.Journal", "UPDATE_ATTEMPT_NOT_RECORDED");
            return UpdateInstallResult.Fail(UpdateInstallStatus.LaunchFailed, "ETP could not record the update attempt, so it did not start setup. Nothing was installed.");
        }

        // 8.
        try
        {
            UpdateInstallerLauncher.Launch(staged, attemptId);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            DesktopDiagnostics.Record(exception, "Updates.Launch", "UPDATE_SETUP_NOT_STARTED");
            try { store.Append(UpdateOutcomeResolver.OutcomeRecord(started, UpdateAttemptOutcome.NotStarted, DateTimeOffset.UtcNow)); }
            catch (Exception journal) when (journal is IOException or UnauthorizedAccessException) { }
            return UpdateInstallResult.Fail(UpdateInstallStatus.LaunchFailed, "Setup did not start (the Windows permission prompt may have been declined). Nothing was installed.");
        }
        return new(UpdateInstallStatus.ExitNow, "Setup has started. ETP will close now and open again when the update is finished.", [], attemptId);
    }

    private static string Describe(UpdateGateResult gate) => string.Join(Environment.NewLine, gate.Blocks.Select(UpdateGate.Describe));

    /// <summary>
    /// For the gate's OtherEtpSessionOpen: another Etp.Reporting.Desktop process on this PC (another Windows session, or a
    /// second window). Setup would refuse anyway while one runs (the mutex), but the Owner should be told before the backup.
    /// </summary>
    public static bool OtherEtpProcessRunning()
    {
        var current = Environment.ProcessId;
        var processes = Process.GetProcessesByName("Etp.Reporting.Desktop");
        try { return processes.Any(p => p.Id != current); }
        finally { foreach (var p in processes) p.Dispose(); }
    }
}
