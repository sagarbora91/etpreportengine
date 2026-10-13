using System.Security.Cryptography;
using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Updates;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 (B1, Appendix C 3 second half): the installer is copied out of the feed, the LOCAL copy is re-hashed against the
/// signed manifest, a tampered or resized installer is refused after the copy, a OneDrive placeholder is "not downloaded
/// yet", and the staged file cannot be swapped while it is held. Temporary folders only; no real ProgramData ACL is used
/// (the protected-folder rule is ProtectedOperationPath's, tested in its own tests).
/// </summary>
public sealed class UpdateStagingTests
{
    private static readonly Action<string> NoAclCheck = _ => { };

    private static ReleaseManifest ManifestFor(byte[] installer, string version = "1.12.0", Action<Dictionary<string, object?>>? change = null)
    {
        var bytes = Manifest(version, "0123456789abcdef", change: fields =>
        {
            fields["installerBytes"] = (long)installer.Length;
            fields["sha256"] = Convert.ToHexString(SHA256.HashData(installer));
            change?.Invoke(fields);
        });
        return ReleaseManifestParser.Parse(bytes).Manifest!;
    }

    private static (string Feed, string Updates) Folders(string root)
    {
        var feed = Directory.CreateDirectory(Path.Combine(root, "feed", "1.12.0")).FullName;
        var updates = Directory.CreateDirectory(Path.Combine(root, "Updates")).FullName;
        return (feed, updates);
    }

    [Fact]
    public async Task A_good_installer_is_staged_hashed_from_the_local_copy_and_held_against_changes()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var (feed, updates) = Folders(root);
            var installer = RandomNumberGenerator.GetBytes(300_000);
            var manifest = ManifestFor(installer);
            await File.WriteAllBytesAsync(Path.Combine(feed, manifest.InstallerFileName), installer);

            var result = await UpdateInstallerStager.StageAsync(feed, manifest, updates, CancellationToken.None, NoAclCheck, _ => "1.12.0");

            Assert.True(result.Succeeded, result.Problem.ToString());
            using var staged = result.Staged!;
            Assert.Equal(Path.Combine(updates, "1.12.0", manifest.InstallerFileName), staged.Path);
            Assert.Equal(installer, await File.ReadAllBytesAsync(staged.Path));
            // Held: nobody (not even this user) can replace or delete it until setup has started.
            Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(staged.Path, [1, 2, 3]));
            Assert.ThrowsAny<IOException>(() => File.Delete(staged.Path));
            Assert.Empty(Directory.EnumerateFiles(staged.Folder, "*.partial"));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task A_tampered_installer_is_refused_after_the_local_copy_and_the_copy_is_removed()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var (feed, updates) = Folders(root);
            var installer = RandomNumberGenerator.GetBytes(200_000);
            var manifest = ManifestFor(installer);
            // Same size, one byte different: what someone with the shared OneDrive account could drop beside a real release.
            var tampered = installer.ToArray();
            tampered[100_000] ^= 0xFF;
            await File.WriteAllBytesAsync(Path.Combine(feed, manifest.InstallerFileName), tampered);

            var result = await UpdateInstallerStager.StageAsync(feed, manifest, updates, CancellationToken.None, NoAclCheck, _ => "1.12.0");

            Assert.Equal(StageProblem.HashMismatch, result.Problem);
            Assert.Null(result.Staged);
            Assert.False(File.Exists(Path.Combine(updates, "1.12.0", manifest.InstallerFileName)));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task Size_version_and_folder_problems_are_each_refused()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var (feed, updates) = Folders(root);
            var installer = RandomNumberGenerator.GetBytes(50_000);
            var manifest = ManifestFor(installer);
            var source = Path.Combine(feed, manifest.InstallerFileName);

            Assert.Equal(StageProblem.SourceMissing, (await UpdateInstallerStager.StageAsync(feed, manifest, updates, CancellationToken.None, NoAclCheck, _ => "1.12.0")).Problem);

            await File.WriteAllBytesAsync(source, installer[..40_000]);
            Assert.Equal(StageProblem.SizeMismatch, (await UpdateInstallerStager.StageAsync(feed, manifest, updates, CancellationToken.None, NoAclCheck, _ => "1.12.0")).Problem);

            // A renamed older setup with the signed bytes' hash cannot exist, but its own version is still checked.
            await File.WriteAllBytesAsync(source, installer);
            Assert.Equal(StageProblem.ProductVersionMismatch, (await UpdateInstallerStager.StageAsync(feed, manifest, updates, CancellationToken.None, NoAclCheck, _ => "1.11.3")).Problem);
            Assert.Equal(StageProblem.ProductVersionMismatch, (await UpdateInstallerStager.StageAsync(feed, manifest, updates, CancellationToken.None, NoAclCheck, _ => null)).Problem);
            var accepted = await UpdateInstallerStager.StageAsync(feed, manifest, updates, CancellationToken.None, NoAclCheck, _ => "1.12.0.0");
            Assert.True(accepted.Succeeded);
            accepted.Staged!.Dispose();

            Assert.Equal(StageProblem.UpdatesFolderMissing,
                (await UpdateInstallerStager.StageAsync(feed, manifest, Path.Combine(root, "NoSuchUpdates"), CancellationToken.None, NoAclCheck, _ => "1.12.0")).Problem);
            Assert.Equal(StageProblem.UpdatesFolderNotProtected,
                (await UpdateInstallerStager.StageAsync(feed, manifest, updates, CancellationToken.None, _ => throw new InvalidOperationException("open to Users"), _ => "1.12.0")).Problem);
            Assert.All(Enum.GetValues<StageProblem>().Where(p => p != StageProblem.None), p => Assert.False(string.IsNullOrWhiteSpace(UpdateInstallerStager.Describe(p))));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public void A_onedrive_placeholder_is_not_downloaded_yet_and_a_downloaded_cloud_file_is_ready()
    {
        const FileAttributes Offline = (FileAttributes)0x1000, RecallOnOpen = (FileAttributes)0x40000, RecallOnData = (FileAttributes)0x400000;
        // OneDrive marks every synced file as a reparse point (the cloud-files tag); only a link has a target.
        Assert.Equal(UpdateFileState.NotDownloadedYet, UpdateFileAvailability.Classify(FileAttributes.Archive | FileAttributes.ReparsePoint | RecallOnData, false, 10, 10));
        Assert.Equal(UpdateFileState.NotDownloadedYet, UpdateFileAvailability.Classify(FileAttributes.Archive | RecallOnOpen, false, 10, 10));
        Assert.Equal(UpdateFileState.NotDownloadedYet, UpdateFileAvailability.Classify(FileAttributes.Archive | Offline, false, 10, 10));
        Assert.Equal(UpdateFileState.Ready, UpdateFileAvailability.Classify(FileAttributes.Archive | FileAttributes.ReparsePoint, false, 10, 10));
        Assert.Equal(UpdateFileState.SizeMismatch, UpdateFileAvailability.Classify(FileAttributes.Archive, false, 9, 10));
        Assert.Equal(UpdateFileState.Linked, UpdateFileAvailability.Classify(FileAttributes.Archive | FileAttributes.ReparsePoint, true, 10, 10));
        Assert.Equal(UpdateFileState.Missing, UpdateFileAvailability.Classify(null, false, 0, 10));
        Assert.Equal(UpdateFileState.Missing, UpdateFileAvailability.Classify(FileAttributes.Directory, false, 0, 10));
    }

    [Fact]
    public void The_feed_reader_reads_each_version_folder_and_tolerates_a_missing_feed()
    {
        var root = NewTemporaryDirectory();
        try
        {
            Assert.Null(UpdateFeedReader.Read(Path.Combine(root, "missing")));
            Assert.Null(UpdateFeedReader.Read(null));
            var full = Directory.CreateDirectory(Path.Combine(root, "1.12.0")).FullName;
            File.WriteAllText(Path.Combine(full, UpdateFeedReader.ManifestFileName), "{}");
            File.WriteAllText(Path.Combine(full, UpdateFeedReader.SignatureFileName), "{}");
            Directory.CreateDirectory(Path.Combine(root, "1.12.1"));
            File.WriteAllBytes(Path.Combine(root, "1.12.1", UpdateFeedReader.ManifestFileName), new byte[ReleaseManifest.MaximumBytes + 1]);

            var entries = UpdateFeedReader.Read(root)!.OrderBy(e => e.FolderName, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "1.12.0", "1.12.1" }, entries.Select(e => e.FolderName));
            Assert.Equal("{}"u8.ToArray(), entries[0].ManifestBytes);
            Assert.Equal("{}"u8.ToArray(), entries[0].SignatureBytes);
            Assert.Null(entries[1].ManifestBytes);   // too large to be ours
            Assert.Null(entries[1].SignatureBytes);
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public void Two_earlier_installers_are_kept_and_older_ones_removed()
    {
        var (move, delete) = UpdateRetention.Plan(
            stagedFolderNames: ["1.12.0", "1.11.3", "PreviousReleases", "junk"],
            previousFolderNames: ["1.11.2", "1.11.1", "1.10.0", "1.12.0"],
            keep: new ReleaseVersion(1, 12, 0));
        Assert.Equal(new[] { "1.11.3" }, move);
        Assert.Equal(new[] { "1.11.1", "1.10.0" }, delete);
    }

    [Fact]
    public void The_feed_path_is_the_configured_full_path_or_onedrive_etp_updates()
    {
        Assert.Equal(@"D:\Shared\ETP-Updates", UpdatePaths.ResolveFeed(@" D:\Shared\ETP-Updates ", () => @"C:\Users\Owner\OneDrive"));
        Assert.Equal(@"C:\Users\Owner\OneDrive\ETP-Updates", UpdatePaths.ResolveFeed(null, () => @"C:\Users\Owner\OneDrive"));
        Assert.Null(UpdatePaths.ResolveFeed(null, () => null));
        Assert.Null(UpdatePaths.ResolveFeed(@"ETP-Updates", () => @"C:\Users\Owner\OneDrive"));
        Assert.EndsWith(@"EtpReporting\Updates", UpdatePaths.UpdatesRoot, StringComparison.OrdinalIgnoreCase);
    }
}
