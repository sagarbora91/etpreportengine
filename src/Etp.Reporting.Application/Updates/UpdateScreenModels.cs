using System.Globalization;

namespace Etp.Reporting.Application.Updates;

/// <summary>
/// 1.12 wave 2 (B2). The two Owner settings of migration 0065: the update folder (NULL = the signed-in user's
/// <c>OneDrive\ETP-Updates</c>) and the switch that lets ETP check it and offer "Install now". The switch stays off
/// (design rule 9) until a real unattended Workpc upgrade has worked.
/// </summary>
public sealed record UpdateSettings(string? FeedPath, bool CheckEnabled);

/// <summary>Reads and writes the two update settings. The Desktop composition root implements it over SQL; SQL itself refuses a non-Owner write.</summary>
public interface IUpdateSettingsStore
{
    Task<UpdateSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Owner only. Writes both values with modified_by, modified_utc and the reason, and records the configuration change in the audit trail.</summary>
    Task SaveAsync(UpdateSettings settings, string reason, CancellationToken cancellationToken = default);
}

/// <summary>What a person may type into the update settings, checked before SQL sees it (SQL has the same rule as a CHECK).</summary>
public static class UpdateSettingsRules
{
    public const int MaximumPathLength = 400;
    public const int MaximumReasonLength = 500;

    /// <summary>Blank means "use OneDrive\ETP-Updates", which is stored as NULL.</summary>
    public static string? NormalizePath(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The sentence to show when the input cannot be saved, or null when it can.</summary>
    public static string? Validate(string? feedPathText, string? reasonText)
    {
        var path = NormalizePath(feedPathText);
        if (path is not null)
        {
            if (path.Length > MaximumPathLength) return "The update folder path is too long.";
            if (!path.Contains(':') && !path.StartsWith(@"\\", StringComparison.Ordinal)) return @"Enter the full path of the update folder, for example D:\ETP-Updates, or leave it blank to use OneDrive\ETP-Updates.";
            if (path.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0) return "The update folder path has a character that Windows does not allow.";
        }
        if (string.IsNullOrWhiteSpace(reasonText)) return "Enter a reason for the change.";
        if (reasonText.Trim().Length > MaximumReasonLength) return "The reason is too long. Use 500 characters or fewer.";
        return null;
    }
}

/// <summary>When the "Update available" banner is shown (plan section B, UI): only to an Owner, only for a verified newer release.</summary>
public static class UpdateBannerRules
{
    public static bool ShouldShow(bool isOwner, UpdateFeedStatus status)
    {
        // The gate decides who sees anything at all; the other fields do not matter for that answer.
        var gate = UpdateGate.Evaluate(new UpdateGateState(isOwner, true, true, false, false, false, false, false, false));
        return gate.ShowAnything && status == UpdateFeedStatus.UpdateAvailable;
    }
}

public enum UpdateTextTone
{
    Neutral,
    Success,
    Warning,
    Critical,
    Information,
}

/// <summary>"Last check" as Settings shows it: a chip word, a sentence, and an optional second line.</summary>
public sealed record UpdateCheckSummary(string Word, UpdateTextTone Tone, string Sentence, string? Extra);

/// <summary>What the confirm sheet shows: the headline, the steps ETP will take, and every reason it cannot start yet.</summary>
public sealed record UpdateConfirmText(string Title, IReadOnlyList<string> Steps, string? BlockedIntro, IReadOnlyList<string> Blocks, string Notes)
{
    public bool CanInstall => Blocks.Count == 0;
}

/// <summary>What the start-up result screen shows after an update attempt.</summary>
public sealed record UpdateResultText(string Title, UpdateTextTone Tone, string Body, bool NeedsAttention, string? SetupLogFolder);

/// <summary>
/// 1.12 wave 2 (B2). Every sentence of the update screens, in one pure place so the wording is tested without a window
/// (design voice: what happened, then what to do; no codes; the app's own words for the release key and the folder).
/// </summary>
public static class UpdateScreenText
{
    public const string BannerInstallButton = "Install now";
    public const string BannerLaterButton = "Not now";
    public const string OpenSetupLogFolderButton = "Open setup log folder";

    public static string BannerTitle(ReleaseVersion version) => string.Create(CultureInfo.InvariantCulture, $"ETP {version} is available");

    /// <summary>The first line of the release notes, cut to a banner's length; a plain sentence when there are no notes.</summary>
    public static string BannerBody(ReleaseManifest offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var first = offer.Notes.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first)) return "A new version has been published. Install it when nobody is in the middle of work.";
        return first.Length <= 160 ? first : first[..157].TrimEnd() + "...";
    }

    public static UpdateConfirmText Confirm(ReleaseManifest offer, UpdateGateResult gate)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(gate);
        var steps = new List<string>
        {
            "ETP makes a backup of the database and checks it. If the backup fails, nothing is installed.",
            "ETP closes. Windows asks for permission to run the setup: choose Yes.",
            "When the update has finished, ETP opens again by itself.",
        };
        if (offer.HasSchemaChange) steps.Add("This update changes the database. The backup from step 1 is kept in case anything goes wrong.");
        var blocks = gate.Blocks.Select(UpdateGate.Describe).ToArray();
        return new(
            string.Create(CultureInfo.InvariantCulture, $"Install ETP {offer.Version}?"),
            steps,
            blocks.Length == 0 ? null : "ETP cannot start the update yet:",
            blocks,
            offer.Notes.Trim());
    }

    /// <summary>"Last check" in Settings. <paramref name="running"/> is null for a development build.</summary>
    public static UpdateCheckSummary LastCheck(UpdateFeedChoice? choice, ReleaseVersion? running, DateTimeOffset? checkedUtc, TimeZoneInfo zone)
    {
        if (running is null)
            return new("Not offered", UpdateTextTone.Neutral, "This is a development build, so updates are not offered.", null);
        if (choice is null)
            return new("Not checked", UpdateTextTone.Neutral, "ETP has not checked the update folder yet. Press Check now.", null);

        var when = checkedUtc is { } utc
            ? string.Create(CultureInfo.InvariantCulture, $" Checked at {TimeZoneInfo.ConvertTime(utc, zone):HH:mm} on {TimeZoneInfo.ConvertTime(utc, zone):dd MMM yyyy}.")
            : string.Empty;
        var (word, tone, sentence) = choice.Status switch
        {
            UpdateFeedStatus.UpdateAvailable when choice.Offer is { } offer =>
                ("Update available", UpdateTextTone.Information, string.Create(CultureInfo.InvariantCulture, $"ETP {offer.Version} is ready to install.")),
            UpdateFeedStatus.UpToDate =>
                ("Up to date", UpdateTextTone.Success, string.Create(CultureInfo.InvariantCulture, $"ETP {running} is the newest release in the update folder.")),
            UpdateFeedStatus.NotSetUp =>
                ("Not set up", UpdateTextTone.Neutral, "Updates are not set up on this PC. Install new versions with the setup file."),
            UpdateFeedStatus.FeedUnavailable =>
                ("Folder not found", UpdateTextTone.Warning, "ETP cannot read the update folder. Check that OneDrive is signed in and that the folder path below is right."),
            _ => ("Not checked", UpdateTextTone.Neutral, "ETP has not checked the update folder yet. Press Check now."),
        };
        string? extra = null;
        if (choice.HasUntrustedEntries)
            extra = "The update folder holds files ETP will not trust. ETP ignored them. Tell Sagar.";
        else if (choice.Status == UpdateFeedStatus.UpToDate && choice.Findings.FirstOrDefault(f => f.Rejection == UpdateFeedRejection.NeedsEarlierRelease) is { } needs)
            extra = string.Create(CultureInfo.InvariantCulture, $"Release {needs.FolderName} needs ETP {needs.Detail} installed first.");
        return new(word, tone, sentence + when, extra);
    }

    /// <summary>The Settings switch's own line: what it does and what it does not do.</summary>
    public static string CheckSwitchHelp(bool enabled) => enabled
        ? "ETP checks the update folder when it starts and shows the Install now notice to an Owner."
        : "Off. ETP does not check by itself. Check now still works, and Install now stays unavailable until this is on.";

    /// <summary>The words for a failed or refused install (the engine's own message, with the Blocked list spelled out).</summary>
    public static string InstallRefusal(string engineMessage, IReadOnlyList<UpdateGateBlock> blocks)
    {
        if (blocks.Count == 0) return engineMessage;
        return "ETP cannot start the update yet:" + Environment.NewLine + string.Join(Environment.NewLine, blocks.Select(UpdateGate.Describe));
    }

    /// <summary>
    /// The start-up result screen (plan section B, UI). Never a restore: a failure names the backup and says to call Sagar.
    /// </summary>
    public static UpdateResultText Result(UpdateAttemptRecord attempt, UpdateAttemptOutcome outcome, ReleaseVersion running, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var logFolder = string.IsNullOrWhiteSpace(attempt.SetupLogPath) ? null : Path.GetDirectoryName(attempt.SetupLogPath);
        switch (outcome)
        {
            case UpdateAttemptOutcome.Succeeded:
                return new(string.Create(CultureInfo.InvariantCulture, $"ETP is now version {running}"), UpdateTextTone.Success,
                    "The update finished. A backup was taken first.", false, null);
            case UpdateAttemptOutcome.Failed:
                return new("The update did not finish", UpdateTextTone.Critical,
                    UpdateOutcomeResolver.FailureText(attempt, zone) + " Nothing was restored."
                    + string.Create(CultureInfo.InvariantCulture, $" ETP is running version {running}."),
                    true, logFolder);
            default:
                return new("The update did not start", UpdateTextTone.Information,
                    string.Create(CultureInfo.InvariantCulture, $"Setup did not run, so nothing was installed and nothing changed. This happens when the Windows permission prompt is declined. ETP is still version {running}. You can try again from Settings > Software update."),
                    true, logFolder);
        }
    }
}
