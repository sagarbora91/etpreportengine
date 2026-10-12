extern alias EtpApplication;

using System.IO;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

internal enum UpdateFileState
{
    Ready,
    Missing,
    /// <summary>A OneDrive "Files On-Demand" placeholder: listed, but not on this PC yet.</summary>
    NotDownloadedYet,
    /// <summary>On disk but not the size release.json says: still syncing, or a different file.</summary>
    SizeMismatch,
    /// <summary>A link or junction: never followed.</summary>
    Linked,
    /// <summary>Could not be opened or read (locked by OneDrive while it writes, or access denied).</summary>
    Unreadable,
}

/// <summary>
/// 1.12 (B1, design section 4 rule 1). Is a file in the OneDrive feed really here? The attributes OneDrive uses for a
/// placeholder are checked first (the same three as Test-EtpOneDrivePlaceholder in scripts\onedrive\OneDriveCommon.ps1),
/// then the size, then a read of the first block. Nothing here hashes: the hash is taken of the local copy only.
/// </summary>
internal static class UpdateFileAvailability
{
    private const FileAttributes Offline = (FileAttributes)0x1000;           // FILE_ATTRIBUTE_OFFLINE
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;     // FILE_ATTRIBUTE_RECALL_ON_OPEN
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000; // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS

    internal static bool IsPlaceholder(FileAttributes attributes) => (attributes & (Offline | RecallOnOpen | RecallOnDataAccess)) != 0;

    /// <summary>
    /// A symbolic link or junction (never followed). Not the reparse point OneDrive itself puts on every synced file (the
    /// cloud-files tag): that has no link target, and such a file is a normal file once it is downloaded.
    /// </summary>
    internal static bool IsLink(FileSystemInfo info)
    {
        try { return info.LinkTarget is not null; }
        catch (IOException) { return true; }
    }

    /// <summary>The decision from what the file system reports, without touching the file (unit-tested).</summary>
    internal static UpdateFileState Classify(FileAttributes? attributes, bool isLink, long length, long expectedLength)
    {
        if (attributes is not { } a) return UpdateFileState.Missing;
        if ((a & FileAttributes.Directory) != 0) return UpdateFileState.Missing;
        if (isLink) return UpdateFileState.Linked;
        if (IsPlaceholder(a)) return UpdateFileState.NotDownloadedYet;
        return length == expectedLength ? UpdateFileState.Ready : UpdateFileState.SizeMismatch;
    }

    public static UpdateFileState Check(string path, long expectedLength)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return UpdateFileState.Missing;
            var state = Classify(info.Attributes, UpdateFileAvailability.IsLink(info), info.Length, expectedLength);
            if (state != UpdateFileState.Ready) return state;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UpdateFileState.Unreadable;
        }

        try
        {
            // Opening for read with FileShare.Read fails while OneDrive still has the file open for writing.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);
            var buffer = new byte[4096];
            _ = stream.Read(buffer, 0, buffer.Length);
            return stream.Length == expectedLength ? UpdateFileState.Ready : UpdateFileState.SizeMismatch;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UpdateFileState.Unreadable;
        }
    }
}

/// <summary>
/// 1.12 (B1). Lists the feed's version folders and reads each one's <c>release.json</c> and <c>release.json.sig</c> for
/// <see cref="UpdateFeedSelector"/>. Read-only. Links are never followed; small files only; at most 200 folders.
/// </summary>
internal static class UpdateFeedReader
{
    public const string ManifestFileName = "release.json";
    public const string SignatureFileName = "release.json.sig";
    private const int MaximumFolders = 200;

    /// <summary>The entries, or null when the feed folder is missing or cannot be listed.</summary>
    public static IReadOnlyList<UpdateFeedEntry>? Read(string? feedPath)
    {
        if (string.IsNullOrWhiteSpace(feedPath)) return null;
        DirectoryInfo feed;
        IEnumerable<DirectoryInfo> folders;
        try
        {
            feed = new DirectoryInfo(feedPath);
            if (!feed.Exists || UpdateFileAvailability.IsLink(feed)) return null;
            folders = feed.EnumerateDirectories("*", new EnumerationOptions { RecurseSubdirectories = false, AttributesToSkip = 0, IgnoreInaccessible = true })
                .Take(MaximumFolders).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }

        var entries = new List<UpdateFeedEntry>();
        foreach (var folder in folders)
        {
            // A junction inside the feed could point anywhere on the PC.
            if (UpdateFileAvailability.IsLink(folder)) continue;
            entries.Add(new UpdateFeedEntry(
                folder.Name,
                ReadSmall(Path.Combine(folder.FullName, ManifestFileName), ReleaseManifest.MaximumBytes),
                ReadSmall(Path.Combine(folder.FullName, SignatureFileName), ReleaseSignatureVerifier.MaximumSignatureFileBytes)));
        }
        return entries;
    }

    /// <summary>
    /// The whole file, or null. A placeholder this small is downloaded by the read itself (OneDrive hydrates on open); when
    /// the PC is offline that read fails and the file counts as missing for this check.
    /// </summary>
    internal static byte[]? ReadSmall(string path, int maximumBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0 || info.Length > maximumBytes) return null;
            if (UpdateFileAvailability.IsLink(info)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maximumBytes) return null;
            var buffer = new byte[stream.Length];
            stream.ReadExactly(buffer);
            return buffer;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
