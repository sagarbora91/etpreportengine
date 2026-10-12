namespace Etp.Reporting.Application.Updates;

/// <summary>
/// What is true on this PC at the moment the Owner asks to install (or ETP decides whether to offer) an update. The wave-2
/// wiring (B2) fills it from the running app: the import coordinator, the nightly health schedule, the backup and drill
/// runners, the open-draft guards and the session list.
/// </summary>
public sealed record UpdateGateState(
    bool IsOwner,
    bool KeysConfigured,
    bool UpdateCheckEnabled,
    bool ImportRunning,
    bool HealthRunRunning,
    bool BackupRunning,
    bool RecoveryDrillRunning,
    bool EntryDraftOpen,
    bool OtherEtpSessionOpen);

public enum UpdateGateBlock
{
    NotOwner,
    NotSetUp,
    TurnedOff,
    ImportRunning,
    HealthRunRunning,
    BackupRunning,
    RecoveryDrillRunning,
    EntryDraftOpen,
    OtherEtpSessionOpen,
}

public sealed record UpdateGateResult(IReadOnlyList<UpdateGateBlock> Blocks)
{
    public bool Allowed => Blocks.Count == 0;

    /// <summary>A non-Owner sees nothing at all: no banner, no reason (plan section B, UI).</summary>
    public bool ShowAnything => !Blocks.Contains(UpdateGateBlock.NotOwner);
}

/// <summary>
/// 1.12 (B1). Whether an update may start now (design section 4 rule 3, decision 39 (2), decision 44 #1).
/// - Shop hours are allowed: there is deliberately no time-of-day rule.
/// - Never while an import, a nightly health run, a backup or the recovery drill is running, or while an entry draft or a
///   second ETP session is open.
/// - Only an Owner, only once the release key is compiled in, and only once the Owner has switched update checks on in
///   Settings (<c>product_settings.update_check_enabled</c>, default off until a real Workpc upgrade has worked unattended).
/// Every block that applies is listed, so the confirm dialog can show the whole list at once.
/// </summary>
public static class UpdateGate
{
    public static UpdateGateResult Evaluate(UpdateGateState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        // A non-Owner is told nothing else: the other reasons would describe the Owner's work.
        if (!state.IsOwner) return new([UpdateGateBlock.NotOwner]);
        var blocks = new List<UpdateGateBlock>();
        if (!state.KeysConfigured) blocks.Add(UpdateGateBlock.NotSetUp);
        if (!state.UpdateCheckEnabled) blocks.Add(UpdateGateBlock.TurnedOff);
        if (state.ImportRunning) blocks.Add(UpdateGateBlock.ImportRunning);
        if (state.HealthRunRunning) blocks.Add(UpdateGateBlock.HealthRunRunning);
        if (state.BackupRunning) blocks.Add(UpdateGateBlock.BackupRunning);
        if (state.RecoveryDrillRunning) blocks.Add(UpdateGateBlock.RecoveryDrillRunning);
        if (state.EntryDraftOpen) blocks.Add(UpdateGateBlock.EntryDraftOpen);
        if (state.OtherEtpSessionOpen) blocks.Add(UpdateGateBlock.OtherEtpSessionOpen);
        return new(blocks);
    }

    /// <summary>Plain words for the confirm dialog (B2 shows them; the design system's voice: what, then what to do).</summary>
    public static string Describe(UpdateGateBlock block) => block switch
    {
        UpdateGateBlock.NotOwner => "Only an Owner can install updates.",
        UpdateGateBlock.NotSetUp => "Updates are not set up on this PC. Install new versions with the setup file.",
        UpdateGateBlock.TurnedOff => "Update checks are turned off. An Owner can turn them on in Settings > Software update.",
        UpdateGateBlock.ImportRunning => "An import is running. Wait for it to finish.",
        UpdateGateBlock.HealthRunRunning => "The health check is running. Wait for it to finish.",
        UpdateGateBlock.BackupRunning => "A backup is running. Wait for it to finish.",
        UpdateGateBlock.RecoveryDrillRunning => "The recovery drill is running. Wait for it to finish.",
        UpdateGateBlock.EntryDraftOpen => "An entry has not been saved. Save or discard it first.",
        UpdateGateBlock.OtherEtpSessionOpen => "ETP is open in another Windows session. Close it there first.",
        _ => throw new ArgumentOutOfRangeException(nameof(block)),
    };
}

public enum UpdateVersionVerdict
{
    /// <summary>Newer than the running build, and the running build is at or above the release's minimum.</summary>
    Offer,
    /// <summary>Same as the running build, or older: never offered (no downgrade through this path).</summary>
    NotNewer,
    /// <summary>Newer, but needs an earlier release installed first (minimumFromVersion is above the running build).</summary>
    NeedsEarlierRelease,
}

/// <summary>1.12 (B1, design section 4 rule 7). Never older; respect <c>minimumFromVersion</c> so no required step is skipped.</summary>
public static class UpdateVersionRules
{
    public static UpdateVersionVerdict Evaluate(ReleaseVersion running, ReleaseManifest candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Version <= running) return UpdateVersionVerdict.NotNewer;
        return running < candidate.MinimumFromVersion ? UpdateVersionVerdict.NeedsEarlierRelease : UpdateVersionVerdict.Offer;
    }
}
