using System.IO.Compression;

namespace Etp.Reporting.Import.Batch;

public sealed record ImportPathPolicyOptions(
    int MaximumArchiveEntries = 256,
    long MaximumEntryBytes = 100 * 1024 * 1024,
    long MaximumExpandedBytes = 500 * 1024 * 1024,
    double MaximumCompressionRatio = 200d,
    int MaximumFolderDepth = 8)
{
    public static ImportPathPolicyOptions Default { get; } = new();
}

public sealed class ImportPathPolicy
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".xlsx" };

    private readonly ImportPathPolicyOptions _options;
    internal int MaximumFolderDepth => _options.MaximumFolderDepth;

    public ImportPathPolicy(ImportPathPolicyOptions? options = null)
    {
        _options = options ?? ImportPathPolicyOptions.Default;
        if (_options.MaximumArchiveEntries <= 0 || _options.MaximumEntryBytes <= 0 ||
            _options.MaximumExpandedBytes <= 0 || _options.MaximumCompressionRatio <= 0 ||
            _options.MaximumFolderDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Import safety limits must be positive.");
        }
    }

    public string ValidateExistingSource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ImportSourceException("IMPORT_PATH_REQUIRED", "Select an import folder, workbook, or ZIP archive.");

        string fullPath;
        try { fullPath = Path.GetFullPath(sourcePath.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ImportSourceException("IMPORT_PATH_INVALID", "The selected import path is invalid.", ex);
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new ImportSourceException("IMPORT_SOURCE_NOT_FOUND", "The selected import source no longer exists.");

        RejectReparsePoint(fullPath);
        if (File.Exists(fullPath))
        {
            var extension = Path.GetExtension(fullPath);
            if (!SupportedExtensions.Contains(extension) && !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                throw new ImportSourceException("IMPORT_TYPE_UNSUPPORTED", "Only .xlsx workbooks and .zip archives are supported.");
        }

        return fullPath;
    }

    public void ValidateWorkbook(string path)
    {
        RejectReparsePoint(path);
        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
            throw new ImportSourceException("IMPORT_TYPE_UNSUPPORTED", "Only .xlsx workbooks are supported.");
        var size = new FileInfo(path).Length;
        if (size <= 0 || size > _options.MaximumEntryBytes)
            throw new ImportSourceException("IMPORT_FILE_SIZE_INVALID", "A workbook is empty or exceeds the configured safety limit.");
    }

    public void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > _options.MaximumArchiveEntries)
            throw new ImportSourceException("IMPORT_ARCHIVE_ENTRY_LIMIT", "The archive contains too many entries.");

        long expandedTotal = 0;
        foreach (var entry in archive.Entries)
        {
            if (IsLink(entry))
                throw new ImportSourceException("IMPORT_ARCHIVE_LINK", "Archive links are not allowed.");
            if (entry.Length > _options.MaximumEntryBytes)
                throw new ImportSourceException("IMPORT_ARCHIVE_ENTRY_SIZE", "An archive entry exceeds the configured safety limit.");
            expandedTotal = checked(expandedTotal + entry.Length);
            if (expandedTotal > _options.MaximumExpandedBytes)
                throw new ImportSourceException("IMPORT_ARCHIVE_SIZE_LIMIT", "The expanded archive exceeds the configured safety limit.");
            if (entry.Length > 0 && entry.CompressedLength == 0)
                throw new ImportSourceException("IMPORT_ARCHIVE_RATIO", "The archive has an unsafe compression ratio.");
            if (entry.CompressedLength > 0 && (double)entry.Length / entry.CompressedLength > _options.MaximumCompressionRatio)
                throw new ImportSourceException("IMPORT_ARCHIVE_RATIO", "The archive has an unsafe compression ratio.");
        }
    }

    public void ValidateRelativeArchivePath(string destinationRoot, ZipArchiveEntry entry)
    {
        var normalizedName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedName))
            throw new ImportSourceException("IMPORT_ARCHIVE_TRAVERSAL", "The archive contains an unsafe path.");

        var root = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(destinationRoot, normalizedName));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ImportSourceException("IMPORT_ARCHIVE_TRAVERSAL", "The archive contains an unsafe path.");

        var depth = normalizedName.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length - 1;
        if (depth > _options.MaximumFolderDepth)
            throw new ImportSourceException("IMPORT_ARCHIVE_DEPTH", "The archive folder layout is too deeply nested.");
    }

    public static void RejectReparsePoint(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ImportSourceException("IMPORT_LINK_REJECTED", "Linked files and folders are not allowed for imports.");
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool IsLink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        return unixMode == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }
}

public sealed class ImportSourceException : Exception
{
    public ImportSourceException(string code, string safeMessage, Exception? innerException = null)
        : base(safeMessage, innerException) => Code = code;

    public string Code { get; }
}
