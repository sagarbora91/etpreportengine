extern alias EtpApplication;

using System.IO;
using System.Reflection;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

/// <summary>
/// 1.12 (B1). Where the update engine reads and writes.
/// - The feed: <c>product_settings.update_feed_path</c> (migration 0065), or, when that is NULL, <c>&lt;OneDrive&gt;\ETP-Updates</c>.
/// - The local working folder: <c>%ProgramData%\EtpReporting\Updates</c>. Setup creates it like HealthExports (SYSTEM and
///   Administrators full control, the active Owners Modify, nobody else) - see the lane NOTE; the engine refuses to stage
///   anything while it is missing or open to anyone else.
///   - <c>&lt;version&gt;\</c>: the staged installer, its setup log and the pre-update backup receipt;
///   - <c>PreviousReleases\&lt;version&gt;\</c>: the last two earlier staged installers (design section 4 rule 6);
///   - <c>attempts.jsonl</c> and <c>setup-result.json</c>.
/// </summary>
internal static class UpdatePaths
{
    public const string FeedFolderName = "ETP-Updates";
    public const string PreviousReleasesFolderName = "PreviousReleases";

    public static string UpdatesRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Updates");

    public static string PreviousReleasesRoot(string updatesRoot) => Path.Combine(updatesRoot, PreviousReleasesFolderName);

    public static string AttemptsFile(string updatesRoot) => Path.Combine(updatesRoot, UpdateAttemptJournal.FileName);

    public static string SetupResultFile(string updatesRoot) => Path.Combine(updatesRoot, UpdateSetupResult.FileName);

    public static string StagingFolder(string updatesRoot, ReleaseVersion version) => Path.Combine(updatesRoot, version.ToString());

    /// <summary>The feed folder: the configured path when it is a full path, otherwise OneDrive's ETP-Updates; null when neither exists.</summary>
    public static string? ResolveFeed(string? configured) => ResolveFeed(configured, OneDriveRoot);

    internal static string? ResolveFeed(string? configured, Func<string?> oneDriveRoot)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var text = configured.Trim();
            // A relative path would depend on the working folder; refuse it rather than guess.
            return Path.IsPathFullyQualified(text) ? Path.GetFullPath(text) : null;
        }
        var root = oneDriveRoot();
        return root is null ? null : Path.Combine(root, FeedFolderName);
    }

    /// <summary>The folder OneDrive syncs for this user (the same order as WindowsOffPcCopyEnvironment); null when none.</summary>
    internal static string? OneDriveRoot()
    {
        foreach (var name in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value)) return value;
        }
        return null;
    }

    /// <summary>This build's release number, or null when the informational version is not a plain release (a dev build).</summary>
    public static ReleaseVersion? RunningVersion()
    {
        var informational = typeof(UpdatePaths).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ReleaseVersion.TryParseRunning(informational, out var version) ? version : null;
    }
}
