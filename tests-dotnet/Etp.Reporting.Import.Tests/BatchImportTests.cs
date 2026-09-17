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
    public async Task CoordinatorAggregatesOverlapAwareOutcomes()
    {
        var coordinator = new BatchImportCoordinator(new OutcomeProcessor());
        var result = await coordinator.RunAsync(["historical.xlsx", "renamed-copy.xlsx"]);

        Assert.Equal(120, result.RowsProcessed);
        Assert.Equal(70, result.NewRows);
        Assert.Equal(45, result.AlreadyPresentRows);
        Assert.Equal(5, result.Conflicts);
        Assert.Equal(1, result.ExactDuplicates);
    }

    [Fact]
    public async Task CoordinatorDoesNotStartRetryAfterCancellationDuringTransientFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var processor = new CancelThenFailProcessor(cancellation);
        var coordinator = new BatchImportCoordinator(processor, maximumAttempts: 2);

        var result = await coordinator.RunAsync(["one.xlsx", "two.xlsx"], cancellationToken: cancellation.Token);

        Assert.Equal(1, processor.Calls);
        Assert.Equal(2, result.Cancelled);
        Assert.Equal(1, result.Files[0].Attempts);
        Assert.Equal(0, result.Files[1].Attempts);
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

    [Fact]
    public async Task Folder_and_zip_skip_excel_lock_files_and_zip_temp_data_is_deleted_on_dispose()
    {
        File.WriteAllBytes(Path.Combine(_root, "~$locked.xlsx"), []);
        File.WriteAllBytes(Path.Combine(_root, "valid.xlsx"), [1]);
        await using (var folder = await BatchImportSource.OpenAsync(_root)) Assert.Single(folder.WorkbookPaths);
        var zipPath = Path.Combine(_root, "locks.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("~$locked.xlsx");
            using var stream = archive.CreateEntry("valid.xlsx").Open(); stream.WriteByte(1);
        }
        var zip = await BatchImportSource.OpenAsync(zipPath);
        var extracted = Assert.Single(zip.WorkbookPaths);
        Assert.True(File.Exists(extracted));
        await zip.DisposeAsync();
        Assert.False(Directory.Exists(Path.GetDirectoryName(extracted)));
    }

    [Fact]
    public async Task Zip_inflated_bytes_are_capped_even_when_declared_size_is_forged()
    {
        var zipPath = Path.Combine(_root, "forged.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var stream = archive.CreateEntry("payload.xlsx", CompressionLevel.Optimal).Open();
            stream.Write(new byte[32 * 1024]);
        }
        var bytes = await File.ReadAllBytesAsync(zipPath);
        for (var i = 0; i < bytes.Length - 46; i++)
        {
            if (BitConverter.ToUInt32(bytes, i) == 0x02014b50) BitConverter.GetBytes(16).CopyTo(bytes, i + 24);
            if (BitConverter.ToUInt32(bytes, i) == 0x04034b50) BitConverter.GetBytes(16).CopyTo(bytes, i + 22);
        }
        await File.WriteAllBytesAsync(zipPath, bytes);
        var policy = new ImportPathPolicy(new(MaximumEntryBytes: 1024));
        var error = await Assert.ThrowsAsync<ImportSourceException>(() => BatchImportSource.OpenAsync(zipPath, policy));
        Assert.Contains(error.Code, new[] { "IMPORT_ARCHIVE_ENTRY_SIZE", "IMPORT_ARCHIVE_CORRUPT" });
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

    private sealed class OutcomeProcessor : IWorkbookImportOutcomeProcessor
    {
        public Task ProcessAsync(string workbookPath, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<WorkbookImportOutcome> ProcessWithOutcomeAsync(string workbookPath, CancellationToken cancellationToken) =>
            Task.FromResult(workbookPath.Contains("renamed", StringComparison.Ordinal)
                ? new WorkbookImportOutcome(0, 0, 0, 0, true)
                : new WorkbookImportOutcome(120, 70, 45, 5));
    }

    private sealed class CancelThenFailProcessor(CancellationTokenSource cancellation) : IWorkbookImportProcessor
    {
        public int Calls { get; private set; }

        public Task ProcessAsync(string workbookPath, CancellationToken cancellationToken)
        {
            Calls++;
            cancellation.Cancel();
            throw new IOException("Transient failure concurrent with cancellation.");
        }
    }
}
