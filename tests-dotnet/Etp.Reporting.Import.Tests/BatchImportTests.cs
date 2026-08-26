using System.IO.Compression;
using Etp.Reporting.Import.Batch;

namespace Etp.Reporting.Import.Tests;

public sealed class BatchImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "EtpImportTests", Guid.NewGuid().ToString("N"));

    public BatchImportTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FolderDiscoveryReturnsOnlyValidatedWorkbooksInStableOrder()
    {
        File.WriteAllBytes(Path.Combine(_root, "b.xlsx"), [1]);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "ignored");
        Directory.CreateDirectory(Path.Combine(_root, "nested"));
        File.WriteAllBytes(Path.Combine(_root, "nested", "a.xlsx"), [1]);

        await using var source = await BatchImportSource.OpenAsync(_root);

        Assert.Equal(2, source.WorkbookPaths.Count);
        Assert.All(source.WorkbookPaths, path => Assert.Equal(".xlsx", Path.GetExtension(path)));
    }

    [Fact]
    public async Task ZipExtractionRejectsTraversal()
    {
        var zipPath = Path.Combine(_root, "unsafe.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../outside.xlsx");
            using var stream = entry.Open();
            stream.WriteByte(1);
        }

        var error = await Assert.ThrowsAsync<ImportSourceException>(() => BatchImportSource.OpenAsync(zipPath));
        Assert.Equal("IMPORT_ARCHIVE_TRAVERSAL", error.Code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "outside.xlsx")));
    }

    [Fact]
    public async Task ZipExtractionRejectsUnsupportedPayloadInsteadOfSilentlyIgnoringIt()
    {
        var zipPath = Path.Combine(_root, "mixed.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("content");
        }

        var error = await Assert.ThrowsAsync<ImportSourceException>(() => BatchImportSource.OpenAsync(zipPath));
        Assert.Equal("IMPORT_ARCHIVE_LAYOUT", error.Code);
    }

    [Fact]
    public async Task CoordinatorRetriesTransientFailureAndReportsSuccess()
    {
        var processor = new StubProcessor([new IOException("private raw detail"), null]);
        var coordinator = new BatchImportCoordinator(processor, maximumAttempts: 2);

        var result = await coordinator.RunAsync([Path.Combine(_root, "sample.xlsx")]);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Files[0].Attempts);
    }

    [Fact]
    public async Task CoordinatorSanitizesUnexpectedFailure()
    {
        var processor = new StubProcessor([new InvalidOperationException("customer and invoice detail")]);
        var coordinator = new BatchImportCoordinator(processor);

        var result = await coordinator.RunAsync([Path.Combine(_root, "sample.xlsx")]);

        Assert.Equal("IMPORT_PROCESSING_FAILED", result.Files[0].ErrorCode);
        Assert.DoesNotContain("customer", result.Files[0].SafeErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CoordinatorStopsCleanlyWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var coordinator = new BatchImportCoordinator(new StubProcessor([]));

        var result = await coordinator.RunAsync(["one.xlsx", "two.xlsx"], cancellationToken: cancellation.Token);

        Assert.Equal(2, result.Cancelled);
        Assert.All(result.Files, item => Assert.Equal(0, item.Attempts));
    }

    [Fact]
    public void SourceValidationRejectsUnsupportedExtension()
    {
        var path = Path.Combine(_root, "legacy.xls");
        File.WriteAllBytes(path, [1]);

        var error = Assert.Throws<ImportSourceException>(() => new ImportPathPolicy().ValidateExistingSource(path));

        Assert.Equal("IMPORT_TYPE_UNSUPPORTED", error.Code);
    }

    [Fact]
    public async Task EmptyFolderReturnsClearFailure()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;

        var error = await Assert.ThrowsAsync<ImportSourceException>(() => BatchImportSource.OpenAsync(empty));

        Assert.Equal("IMPORT_NO_WORKBOOKS", error.Code);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private sealed class StubProcessor(Queue<Exception?> outcomes) : IWorkbookImportProcessor
    {
        public StubProcessor(IEnumerable<Exception?> outcomes) : this(new Queue<Exception?>(outcomes)) { }

        public Task ProcessAsync(string workbookPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (outcomes.Count > 0 && outcomes.Dequeue() is { } error) throw error;
            return Task.CompletedTask;
        }
    }
}
