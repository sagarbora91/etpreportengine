using System.IO.Compression;

namespace Etp.Reporting.Import.Batch;

public sealed class BatchImportSource : IAsyncDisposable
{
    private readonly string? _temporaryDirectory;

    private BatchImportSource(IReadOnlyList<string> workbookPaths, string? temporaryDirectory)
    {
        WorkbookPaths = workbookPaths;
        _temporaryDirectory = temporaryDirectory;
    }

    public IReadOnlyList<string> WorkbookPaths { get; }

    public static async Task<BatchImportSource> OpenAsync(
        string sourcePath,
        ImportPathPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        policy ??= new ImportPathPolicy();
        var source = policy.ValidateExistingSource(sourcePath);
        if (Directory.Exists(source))
        {
            var discovered = DiscoverFolder(source, policy, cancellationToken);
            if (discovered.Count == 0)
                throw new ImportSourceException("IMPORT_NO_WORKBOOKS", "No supported .xlsx workbooks were found.");
            return new BatchImportSource(discovered, null);
        }
        if (!Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            policy.ValidateWorkbook(source);
            return new BatchImportSource([source], null);
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "EtpReporting", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var paths = await ExtractArchiveAsync(source, temporaryDirectory, policy, cancellationToken).ConfigureAwait(false);
            if (paths.Count == 0)
                throw new ImportSourceException("IMPORT_NO_WORKBOOKS", "No supported .xlsx workbooks were found.");
            return new BatchImportSource(paths, temporaryDirectory);
        }
        catch
        {
            TryDeleteDirectory(temporaryDirectory);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_temporaryDirectory is not null)
            TryDeleteDirectory(_temporaryDirectory);
        return ValueTask.CompletedTask;
    }

    private static IReadOnlyList<string> DiscoverFolder(string root, ImportPathPolicy policy, CancellationToken token)
    {
        var workbooks = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var (current, depth) = pending.Pop();
            ImportPathPolicy.RejectReparsePoint(current);
            foreach (var file in Directory.EnumerateFiles(current).Order(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                ImportPathPolicy.RejectReparsePoint(file);
                if (!Path.GetExtension(file).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                    continue;
                policy.ValidateWorkbook(file);
                workbooks.Add(file);
            }
            foreach (var directory in Directory.EnumerateDirectories(current).OrderDescending(StringComparer.OrdinalIgnoreCase))
            {
                ImportPathPolicy.RejectReparsePoint(directory);
                if (depth >= policy.MaximumFolderDepth)
                    throw new ImportSourceException("IMPORT_FOLDER_DEPTH", "The import folder layout is too deeply nested.");
                pending.Push((directory, depth + 1));
            }
        }
        return workbooks;
    }

    private static async Task<IReadOnlyList<string>> ExtractArchiveAsync(
        string zipPath, string destinationRoot, ImportPathPolicy policy, CancellationToken token)
    {
        using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
        policy.ValidateArchive(archive);
        var extracted = new List<string>();
        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            policy.ValidateRelativeArchivePath(destinationRoot, entry);
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            if (!Path.GetExtension(entry.Name).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ImportSourceException("IMPORT_ARCHIVE_LAYOUT", "ZIP archives may contain folders and .xlsx workbooks only.");
            var destination = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
            await input.CopyToAsync(output, token).ConfigureAwait(false);
            policy.ValidateWorkbook(destination);
            extracted.Add(destination);
        }
        return extracted;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
