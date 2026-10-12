extern alias EtpApplication;

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

internal enum StageProblem
{
    None,
    /// <summary>%ProgramData%\EtpReporting\Updates does not exist: setup has not prepared this PC for updates.</summary>
    UpdatesFolderMissing,
    /// <summary>The Updates folder can be changed by someone other than SYSTEM, Administrators and the Owners.</summary>
    UpdatesFolderNotProtected,
    SourceMissing,
    NotDownloadedYet,
    SourceLinked,
    SourceUnreadable,
    SizeMismatch,
    /// <summary>The local copy's SHA-256 is not the one in the signed release.json: the installer was changed or swapped.</summary>
    HashMismatch,
    /// <summary>The installer's own ProductVersion is not the signed version (a renamed older or newer setup).</summary>
    ProductVersionMismatch,
    CopyFailed,
}

/// <summary>
/// A staged installer, held open for reading with no write or delete sharing from the moment it is hashed until setup has
/// started, so nothing can swap it between the check and the launch. Dispose after <see cref="UpdateInstallerLauncher"/>.
/// </summary>
internal sealed class StagedInstaller(string path, FileStream hold, ReleaseManifest manifest) : IDisposable
{
    public string Path { get; } = path;
    public ReleaseManifest Manifest { get; } = manifest;
    public string Folder => System.IO.Path.GetDirectoryName(Path)!;
    public void Dispose() => hold.Dispose();
}

internal sealed record StageResult(StageProblem Problem, StagedInstaller? Staged)
{
    public bool Succeeded => Problem == StageProblem.None && Staged is not null;
}

/// <summary>
/// 1.12 (B1, design section 4 rules 1 and 6, appendix "In-app check"). The installer is never run from the OneDrive folder,
/// which can change mid-run. It is copied into <c>%ProgramData%\EtpReporting\Updates\&lt;version&gt;\</c>, and the LOCAL copy is
/// re-hashed against the signed manifest and its <c>FileVersionInfo.ProductVersion</c> compared with the signed version.
/// </summary>
internal static class UpdateInstallerStager
{
    private const int CopyBufferBytes = 1024 * 1024;

    /// <param name="validateUpdatesRoot">Throws InvalidOperationException when the folder is not protected. Default:
    /// <see cref="ProtectedOperationPath.Validate(string, Func{string, bool}?)"/> with the Owners as permitted modifiers
    /// (the HealthExports rule).</param>
    /// <param name="productVersionOf">Reads the ProductVersion string of a file. Default: FileVersionInfo.</param>
    public static async Task<StageResult> StageAsync(string feedVersionFolder, ReleaseManifest manifest, string updatesRoot,
        CancellationToken cancellationToken, Action<string>? validateUpdatesRoot = null, Func<string, string?>? productVersionOf = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!Directory.Exists(updatesRoot)) return Fail(StageProblem.UpdatesFolderMissing);
        try
        {
            (validateUpdatesRoot ?? ValidateProtected)(updatesRoot);
        }
        catch (InvalidOperationException)
        {
            return Fail(StageProblem.UpdatesFolderNotProtected);
        }

        var source = Path.Combine(feedVersionFolder, manifest.InstallerFileName);
        switch (UpdateFileAvailability.Check(source, manifest.InstallerBytes))
        {
            case UpdateFileState.Missing: return Fail(StageProblem.SourceMissing);
            case UpdateFileState.NotDownloadedYet: return Fail(StageProblem.NotDownloadedYet);
            case UpdateFileState.Linked: return Fail(StageProblem.SourceLinked);
            case UpdateFileState.Unreadable: return Fail(StageProblem.SourceUnreadable);
            case UpdateFileState.SizeMismatch: return Fail(StageProblem.SizeMismatch);
        }

        var folder = UpdatePaths.StagingFolder(updatesRoot, manifest.Version);
        var target = Path.Combine(folder, manifest.InstallerFileName);
        var partial = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            Directory.CreateDirectory(folder);
            var copied = await CopyAsync(source, partial, manifest.InstallerBytes, cancellationToken).ConfigureAwait(false);
            if (copied != manifest.InstallerBytes)
            {
                TryDelete(partial);
                return Fail(StageProblem.SizeMismatch);
            }
            File.Move(partial, target, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            return Fail(StageProblem.CopyFailed);
        }

        FileStream? hold = null;
        try
        {
            // From here on nobody may write, rename or delete the file until setup has started (FileShare.Read only).
            hold = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.SequentialScan);
            if (hold.Length != manifest.InstallerBytes) return Refuse(hold, target, StageProblem.SizeMismatch);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(hold, cancellationToken).ConfigureAwait(false));
            if (!string.Equals(hash, manifest.InstallerSha256, StringComparison.OrdinalIgnoreCase)) return Refuse(hold, target, StageProblem.HashMismatch);
            var productVersion = (productVersionOf ?? ReadProductVersion)(target);
            if (!ReleaseVersion.TryParseRunning(productVersion, out var fileVersion) || fileVersion != manifest.Version)
                return Refuse(hold, target, StageProblem.ProductVersionMismatch);
            return new(StageProblem.None, new StagedInstaller(target, hold, manifest));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hold?.Dispose();
            TryDelete(target);
            return Fail(StageProblem.CopyFailed);
        }
    }

    private static void ValidateProtected(string updatesRoot) =>
        ProtectedOperationPath.Validate(updatesRoot, ProtectedOperationPath.IsIndividualAccount);

    private static string? ReadProductVersion(string path) => FileVersionInfo.GetVersionInfo(path).ProductVersion;

    private static async Task<long> CopyAsync(string source, string destination, long expected, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferBytes, FileOptions.Asynchronous);
        var buffer = new byte[CopyBufferBytes];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            // Stop at one byte past the signed size: a file that keeps growing is not the signed one.
            if (total > expected) return total;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
        return total;
    }

    private static StageResult Refuse(FileStream hold, string target, StageProblem problem)
    {
        hold.Dispose();
        TryDelete(target);
        return Fail(problem);
    }

    private static StageResult Fail(StageProblem problem) => new(problem, null);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public static string Describe(StageProblem problem) => problem switch
    {
        StageProblem.UpdatesFolderMissing => "Updates are not set up on this PC (the protected Updates folder is missing). Run the setup file once by hand.",
        StageProblem.UpdatesFolderNotProtected => "The Updates folder can be changed by other accounts, so ETP will not use it. Call Sagar.",
        StageProblem.SourceMissing => "The installer is not in the update folder. Call Sagar.",
        StageProblem.NotDownloadedYet => "OneDrive has not downloaded the installer to this PC yet. Wait for OneDrive to finish, then try again.",
        StageProblem.SourceLinked => "The installer in the update folder is a link. ETP will not use it. Call Sagar.",
        StageProblem.SourceUnreadable => "The installer could not be read (OneDrive may still be writing it). Try again in a few minutes.",
        StageProblem.SizeMismatch => "The installer is not the size the release says. OneDrive may still be syncing it. Try again later.",
        StageProblem.HashMismatch => "The installer does not match the signed release. ETP will not run it. Call Sagar.",
        StageProblem.ProductVersionMismatch => "The installer is a different version from the signed release. ETP will not run it. Call Sagar.",
        StageProblem.CopyFailed => "The installer could not be copied to this PC. Check free disk space, then try again.",
        _ => "",
    };
}

/// <summary>
/// 1.12 (B1, design section 4 rule 6). Keeps the last two earlier installers. When version N is staged, every other staged
/// version folder moves to <c>PreviousReleases\</c>, and only the two highest versions stay there.
/// </summary>
internal static class UpdateRetention
{
    public const int KeepPrevious = 2;

    /// <summary>The plan, from folder names only (unit-tested): which staged folders move, which previous ones are deleted.</summary>
    internal static (IReadOnlyList<string> Move, IReadOnlyList<string> Delete) Plan(IEnumerable<string> stagedFolderNames, IEnumerable<string> previousFolderNames, ReleaseVersion keep)
    {
        var move = stagedFolderNames
            .Where(n => ReleaseVersion.TryParse(n, out var v) && v != keep)
            .ToList();
        var afterMove = previousFolderNames.Where(n => ReleaseVersion.TryParse(n, out _))
            .Concat(move)
            .Distinct(StringComparer.Ordinal)
            .Select(n => (Name: n, Version: ReleaseVersion.Parse(n)))
            .Where(p => p.Version != keep)
            .OrderByDescending(p => p.Version)
            .ToList();
        var delete = afterMove.Skip(KeepPrevious).Select(p => p.Name).ToList();
        return (move, delete);
    }

    /// <summary>Best effort: a folder that cannot be moved or deleted now is left for the next update.</summary>
    public static void Apply(string updatesRoot, ReleaseVersion keep)
    {
        var previousRoot = UpdatePaths.PreviousReleasesRoot(updatesRoot);
        try
        {
            Directory.CreateDirectory(previousRoot);
            var staged = Directory.EnumerateDirectories(updatesRoot).Select(d => Path.GetFileName(d)).ToList();
            var previous = Directory.EnumerateDirectories(previousRoot).Select(d => Path.GetFileName(d)).ToList();
            var (move, delete) = Plan(staged, previous, keep);
            foreach (var name in move)
            {
                var destination = Path.Combine(previousRoot, name);
                try
                {
                    if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                    Directory.Move(Path.Combine(updatesRoot, name), destination);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            foreach (var name in delete)
            {
                try { Directory.Delete(Path.Combine(previousRoot, name), recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}
