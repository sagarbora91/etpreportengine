using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Infrastructure.SqlServer;
using ScannedSource = Etp.Reporting.Infrastructure.SqlServer.EarlierImportSourceScanner.ScannedSource;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// "Keep source files for earlier imports…" (review 1.9.3 finding 4): database failures are counted apart from
/// unreadable files, a broken connection keeps the counts, the Owner can stop it, and folders below the depth
/// limit are reported.
/// </summary>
public sealed class EarlierImportEvidenceWalkTests
{
    [Fact]
    public async Task Busy_files_and_database_failures_are_counted_apart_from_unreadable_files()
    {
        var sources = new[] { Source(1), Source(2), default, Source(3), Source(4) };
        var store = new FakeStore(sources);
        store.Failures[sources[1].Sha256!] = new ImportEvidenceBusyException("Synthetic import in progress.");
        store.Failures[sources[3].Sha256!] = new SyntheticDbException();

        var result = await EarlierImportEvidenceWalk.RunAsync(Async(sources), store, null, CancellationToken.None);

        Assert.Equal(4, result.FilesHashed);
        Assert.Equal(4, result.Matched);
        Assert.Equal(2, result.Retained);
        Assert.Equal(1, result.Busy);
        Assert.Equal(1, result.DatabaseFailures);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(EarlierImportStop.None, result.Stop);
    }

    [Fact]
    public async Task A_broken_connection_is_opened_again_and_the_walk_continues()
    {
        var sources = new[] { Source(1), Source(2), Source(3) };
        var store = new FakeStore(sources);
        store.Failures[sources[0].Sha256!] = new InvalidOperationException("Synthetic broken connection.");
        store.BreakOn = sources[0].Sha256;

        var result = await EarlierImportEvidenceWalk.RunAsync(Async(sources), store, null, CancellationToken.None);

        Assert.Equal(1, store.Reopens);
        Assert.Equal(1, result.DatabaseFailures);
        Assert.Equal(2, result.Retained);
        Assert.Equal(EarlierImportStop.None, result.Stop);
    }

    [Fact]
    public async Task A_connection_that_cannot_be_opened_again_stops_the_walk_with_its_counts()
    {
        var sources = new[] { Source(1), Source(2), Source(3) };
        var store = new FakeStore(sources) { CanReopen = false };
        store.Failures[sources[1].Sha256!] = new InvalidOperationException("Synthetic broken connection.");
        store.BreakOn = sources[1].Sha256;

        var result = await EarlierImportEvidenceWalk.RunAsync(Async(sources), store, null, CancellationToken.None);

        Assert.Equal(EarlierImportStop.ConnectionLost, result.Stop);
        Assert.Equal(1, result.Retained);
        Assert.Equal(1, result.DatabaseFailures);
        Assert.Equal(2, result.FilesHashed);
        Assert.Equal([sources[0].Sha256!, sources[1].Sha256!], store.Attempts);
    }

    [Fact]
    public async Task Stop_returns_the_counts_so_far_and_keeps_the_files_already_stored()
    {
        var sources = new[] { Source(1), Source(2), Source(3) };
        using var cancellation = new CancellationTokenSource();
        var store = new FakeStore(sources);
        // The Owner stops while the second file is being stored; its command then fails as a database error.
        store.OnRetain = sha =>
        {
            if (sha != sources[1].Sha256) return;
            cancellation.Cancel();
            throw new SyntheticDbException();
        };

        var result = await EarlierImportEvidenceWalk.RunAsync(Async(sources), store, null, cancellation.Token);

        Assert.Equal(EarlierImportStop.Cancelled, result.Stop);
        Assert.Equal(1, result.Retained);
        Assert.Equal(0, result.DatabaseFailures);
        Assert.Equal(2, store.Attempts.Count);
    }

    [Fact]
    public async Task An_import_holding_the_list_of_imports_is_reported_busy_before_anything_is_stored()
    {
        var sources = new[] { Source(1) };
        var store = new FakeStore(sources) { LoadFailure = new ImportEvidenceBusyException("Synthetic import in progress.") };
        await Assert.ThrowsAsync<ImportEvidenceBusyException>(() =>
            EarlierImportEvidenceWalk.RunAsync(Async(sources), store, null, CancellationToken.None));
        Assert.Empty(store.Attempts);
    }

    [Fact]
    public async Task Folders_below_the_depth_limit_are_reported_rather_than_skipped_silently()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpEvidenceDepth_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "store", "deep-one"));
            Directory.CreateDirectory(Path.Combine(root, "store", "deep-two"));
            await File.WriteAllBytesAsync(Path.Combine(root, "top.csv"), [1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(root, "store", "deep-one", "below.csv"), [4, 5, 6]);
            var limits = new ImportPathPolicyOptions(MaximumFolderDepth: 1);

            var result = await EarlierImportEvidenceWalk.RunAsync(
                EarlierImportSourceScanner.ScanAsync([root], limits, CancellationToken.None), new FakeStore([]), null, CancellationToken.None);

            Assert.Equal(1, result.FilesHashed);
            Assert.Equal(2, result.FoldersTooDeep);
            Assert.Equal(0, result.Skipped);
        }
        finally
        {
            if (!Path.GetFileName(root).StartsWith("EtpEvidenceDepth_", StringComparison.Ordinal) ||
                !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe synthetic folder cleanup path.");
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ScannedSource Source(byte value)
    {
        byte[] bytes = [value, (byte)(value + 1)];
        return new(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static async IAsyncEnumerable<ScannedSource> Async(IEnumerable<ScannedSource> sources,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return source;
        }
    }

    private sealed class SyntheticDbException() : DbException("Synthetic database failure.");

    private sealed class FakeStore(IEnumerable<ScannedSource> imported) : IEvidenceWalkStore
    {
        private readonly Dictionary<string, (long, bool)> imports = imported.Where(source => source.Sha256 is not null)
            .Select((source, index) => (source.Sha256!, (FileId: index + 1L, Held: false)))
            .ToDictionary(item => item.Item1, item => item.Item2, StringComparer.Ordinal);
        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);
        public List<string> Attempts { get; } = [];
        public Action<string>? OnRetain { get; set; }
        public Exception? LoadFailure { get; init; }
        public string? BreakOn { get; set; }
        public bool CanReopen { get; init; } = true;
        public int Reopens { get; private set; }
        public bool IsOpen { get; private set; } = true;

        public Task<bool> TryReopenAsync(CancellationToken token)
        {
            Reopens++;
            IsOpen = CanReopen;
            return Task.FromResult(CanReopen);
        }

        public Task<IReadOnlyDictionary<string, (long FileId, bool Held)>> LoadImportedSourcesAsync(CancellationToken token) =>
            LoadFailure is not null
                ? Task.FromException<IReadOnlyDictionary<string, (long FileId, bool Held)>>(LoadFailure)
                : Task.FromResult<IReadOnlyDictionary<string, (long FileId, bool Held)>>(imports);

        public Task<EvidenceState> RetainAsync(string sourceSha256, byte[] content, long importFileId, CancellationToken token)
        {
            Attempts.Add(sourceSha256);
            OnRetain?.Invoke(sourceSha256);
            if (sourceSha256 == BreakOn) IsOpen = false;
            return Failures.TryGetValue(sourceSha256, out var failure)
                ? Task.FromException<EvidenceState>(failure)
                : Task.FromResult(EvidenceState.Retained);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
