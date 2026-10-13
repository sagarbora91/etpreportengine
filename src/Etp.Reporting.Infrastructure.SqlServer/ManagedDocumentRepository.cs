using System.Security.Cryptography;

namespace Etp.Reporting.Infrastructure.SqlServer;

public static class ManagedDocumentRepository
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".xlsx", ".zip", ".csv" }; // .csv: bank and settlement statements (1.12, D2)

    public static Task<(string ManagedPath, string Sha256, long Size)> StoreAsync(
        string sourcePath, string repositoryRoot, CancellationToken cancellationToken = default) =>
        StoreAsync(sourcePath, repositoryRoot, acceptUnreadableExistingCopy: false, cancellationToken);

    /// <param name="acceptUnreadableExistingCopy">
    /// 1.12 IR-2 (decision 59): in CollectionsEvidence a Store Manager may read only the files they created. A copy named by
    /// this content's SHA-256 that another account already kept is then visible (attributes) but not readable, and cannot be
    /// overwritten. True accepts that copy as kept: the name is the content hash and the account that wrote it hashed it again.
    /// The Owner's integrity check still reads it.
    /// </param>
    public static async Task<(string ManagedPath, string Sha256, long Size)> StoreAsync(
        string sourcePath, string repositoryRoot, bool acceptUnreadableExistingCopy, CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected document no longer exists.", source);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked document paths are not allowed.");
        var extension = Path.GetExtension(source);
        if (!AllowedExtensions.Contains(extension)) throw new InvalidOperationException("Select a supported document, spreadsheet or CSV file.");
        var info = new FileInfo(source);
        if (info.Length is <= 0 or > 100 * 1024 * 1024) throw new InvalidOperationException("The document is empty or exceeds the 100 MB safety limit.");
        var root = Path.GetFullPath(repositoryRoot);
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The document repository cannot be a linked folder.");
        var hash = await ComputeHashAsync(source, cancellationToken).ConfigureAwait(false);
        var folder = Path.Combine(root, DateTime.Today.ToString("yyyy"), DateTime.Today.ToString("MM"));
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, hash + extension.ToLowerInvariant());
        var existed = File.Exists(destination);
        if (!existed)
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        string kept;
        try { kept = await ComputeHashAsync(destination, cancellationToken).ConfigureAwait(false); }
        catch (UnauthorizedAccessException) when (existed && acceptUnreadableExistingCopy
            && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) == 0)
        {
            return (destination, hash, info.Length);
        }
        if (!string.Equals(hash, kept, StringComparison.Ordinal))
            throw new IOException("The managed document failed its integrity check.");
        return (destination, hash, info.Length);
    }

    public static async Task<bool> VerifyIntegrityAsync(string managedPath,string expectedSha256,CancellationToken cancellationToken=default)
    {
        var full=Path.GetFullPath(managedPath);if(!File.Exists(full)||(File.GetAttributes(full)&FileAttributes.ReparsePoint)!=0)return false;
        return string.Equals(await ComputeHashAsync(full,cancellationToken).ConfigureAwait(false),SqlServerImportFileRepository.NormalizeHash(expectedSha256),StringComparison.Ordinal);
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
    }
}
