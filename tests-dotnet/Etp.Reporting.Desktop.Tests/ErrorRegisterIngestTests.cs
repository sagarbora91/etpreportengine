using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Etp.Reporting.Application.ErrorRegister;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Desktop;
using Etp.Reporting.Desktop.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.0 lane L2: reading this account's diagnostics log, the cursor, batching and the ingest client, against a fake
/// <see cref="IErrorRegisterStore"/>. No database, no real log folder: every test uses its own temporary folder.
/// </summary>
public sealed class ErrorRegisterIngestTests : IDisposable
{
    private static readonly JsonSerializerOptions LogOptions = new() { Converters = { new JsonStringEnumConverter() } };
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"etp-register-{Guid.NewGuid():N}");

    public ErrorRegisterIngestTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal sealed class FakeStore : IErrorRegisterStore
    {
        public List<(IReadOnlyList<ErrorOccurrence> Items, string Version)> Batches { get; } = [];
        public Exception? Throw { get; set; }
        public Func<Task>? Gate { get; set; }
        public List<(string Json, string Version)> Manifests { get; } = [];
        public ErrorFixManifestResult ManifestResult { get; set; } = new(5, 5, 0, 0, 0);

        public IEnumerable<ErrorOccurrence> AllItems => Batches.SelectMany(batch => batch.Items);

        public async Task<ErrorIngestResult> IngestAsync(IReadOnlyList<ErrorOccurrence> items, string appVersion, CancellationToken cancellationToken = default)
        {
            if (Gate is not null) await Gate();
            if (Throw is not null) throw Throw;
            Batches.Add((items.ToArray(), appVersion));
            return new ErrorIngestResult(items.Count, 0, 0, items.Count, 0);
        }

        public Task<IReadOnlyList<ErrorRegisterEntry>> LoadRegisterAsync(ErrorStatus? status = null, string? reference = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ErrorRegisterEntryDetail?> LoadEntryAsync(int errorRegisterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetStatusAsync(int errorRegisterId, ErrorStatus status, string? fixedInVersion, string? ticket, string? reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MergeAsync(int fromId, int intoId, string reason, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ErrorFixManifestResult> ApplyFixManifestAsync(string manifestJson, string runningVersion, CancellationToken cancellationToken = default)
        {
            Manifests.Add((manifestJson, runningVersion));
            return Task.FromResult(ManifestResult);
        }
    }

    private void Log(params DesktopDiagnosticEntry[] entries) =>
        File.AppendAllLines(Path.Combine(_directory, "diagnostics-202610.jsonl"),
            entries.Select(entry => JsonSerializer.Serialize(entry, LogOptions)), new UTF8Encoding(false));

    private static DesktopDiagnosticEntry E(int minute, string reference, DesktopDiagnosticSeverity severity = DesktopDiagnosticSeverity.Error,
        string eventId = "REPORT_STORE_REQUIRED", IReadOnlyList<string>? stack = null, string version = "1.9.9.0", int seconds = 0) =>
        ErrorFingerprintTests.Entry(eventId, stack: stack, severity: severity, reference: reference, version: version, at: T0.AddMinutes(minute).AddSeconds(seconds));

    private ErrorRegisterIngest Ingest(FakeStore store, Func<CancellationToken, Task<IReadOnlyList<ErrorRegisterEntry>>>? known = null) =>
        new(store, "1.11.0.0", _directory, known);

    [Fact]
    public async Task Errors_and_criticals_are_sent_oldest_first_and_warnings_and_information_are_not_registered()
    {
        Log(
            E(3, "CCCCCCCCCCCC", DesktopDiagnosticSeverity.Critical, "CASH_BOOK_FAILED"),
            E(1, "AAAAAAAAAAAA"),
            E(2, "BBBBBBBBBBBB", DesktopDiagnosticSeverity.Warning, "USER_ACCESS_SAVE_FAILED"),
            E(4, "DDDDDDDDDDDD", DesktopDiagnosticSeverity.Information, "APP_STARTED"));
        var store = new FakeStore();

        var run = await Ingest(store).RunAsync();

        Assert.Equal(["AAAAAAAAAAAA", "CCCCCCCCCCCC"], store.AllItems.Select(item => item.Reference));
        Assert.Equal([ErrorRegisterSeverity.Error, ErrorRegisterSeverity.Critical], store.AllItems.Select(item => item.Severity));
        Assert.Equal(2, run.Sent);
        Assert.Equal(1, run.Batches);
        Assert.All(store.Batches, batch => Assert.Equal("1.11.0.0", batch.Version));
    }

    [Fact]
    public async Task The_cursor_moves_after_a_batch_and_a_second_run_sends_nothing_new()
    {
        Log(E(1, "AAAAAAAAAAAA"), E(2, "BBBBBBBBBBBB"));
        var store = new FakeStore();
        var ingest = Ingest(store);

        await ingest.RunAsync();
        var second = await ingest.RunAsync();
        Log(E(3, "CCCCCCCCCCCC"));
        await ingest.RunAsync();

        Assert.Equal(0, second.Sent);
        Assert.Equal(["AAAAAAAAAAAA", "BBBBBBBBBBBB", "CCCCCCCCCCCC"], store.AllItems.Select(item => item.Reference));
        var cursor = new DiagnosticsLogReader(_directory).ReadCursor();
        Assert.Equal(new RegisterCursor(T0.AddMinutes(3), "CCCCCCCCCCCC"), cursor);
    }

    [Fact]
    public void Entries_with_the_same_time_are_ordered_by_ref_and_the_cursor_is_strict()
    {
        Log(E(1, "BBBBBBBBBBBB"), E(1, "AAAAAAAAAAAA"));
        var reader = new DiagnosticsLogReader(_directory);

        var all = reader.ReadErrorsAfter(null).Entries.Select(entry => entry.CorrelationId);
        var afterFirst = reader.ReadErrorsAfter(new RegisterCursor(T0.AddMinutes(1), "AAAAAAAAAAAA")).Entries.Select(entry => entry.CorrelationId);
        var afterBoth = reader.ReadErrorsAfter(new RegisterCursor(T0.AddMinutes(1), "BBBBBBBBBBBB")).Entries;

        Assert.Equal(["AAAAAAAAAAAA", "BBBBBBBBBBBB"], all);
        Assert.Equal(["BBBBBBBBBBBB"], afterFirst);
        Assert.Empty(afterBoth);
    }

    [Fact]
    public async Task More_than_five_hundred_entries_go_in_batches_of_five_hundred_oldest_first()
    {
        Log(Enumerable.Range(0, 1203).Select(index => E(0, index.ToString("X12")) with { TimestampUtc = T0.AddSeconds(index) }).ToArray());
        var store = new FakeStore();

        var run = await Ingest(store).RunAsync();

        Assert.Equal([500, 500, 203], store.Batches.Select(batch => batch.Items.Count));
        Assert.Equal(3, run.Batches);
        Assert.Equal(1203, store.AllItems.Select(item => item.Reference).Distinct().Count());
        Assert.Equal(store.AllItems.OrderBy(item => item.TimestampUtc).Select(item => item.Reference), store.AllItems.Select(item => item.Reference));
        Assert.Equal(T0.AddSeconds(1202), new DiagnosticsLogReader(_directory).ReadCursor()!.TimestampUtc);
    }

    [Fact]
    public async Task A_failing_store_leaves_the_cursor_logs_one_failure_entry_and_the_next_run_sends_everything_including_that_entry()
    {
        Log(E(1, "AAAAAAAAAAAA"));
        var store = new FakeStore { Throw = new InvalidOperationException("secret message that must never be stored") };
        var ingest = Ingest(store);

        var failed = await ingest.RunAsync();

        Assert.True(failed.Failed);
        Assert.Null(new DiagnosticsLogReader(_directory).ReadCursor());
        var text = string.Concat(Directory.GetFiles(_directory, "diagnostics-*.jsonl").Select(File.ReadAllText));
        Assert.Contains("ERROR_REGISTER_INGEST_FAILED", text);
        Assert.DoesNotContain("secret message", text);

        store.Throw = null;
        var ok = await ingest.RunAsync();

        Assert.False(ok.Failed);
        Assert.Contains(store.AllItems, item => item.Reference == "AAAAAAAAAAAA");
        Assert.Contains(store.AllItems, item => item.Parts.EventBase == "ERROR_REGISTER_INGEST_FAILED" && item.Parts.Source == "Diagnostics.ErrorRegister");
    }

    [Fact]
    public async Task A_lost_or_corrupt_cursor_resends_and_the_database_ignores_the_duplicates()
    {
        Log(E(1, "AAAAAAAAAAAA"));
        var store = new FakeStore();
        var ingest = Ingest(store);
        await ingest.RunAsync();

        File.WriteAllText(Path.Combine(_directory, DiagnosticsLogReader.CursorFileName), "{ not json");
        var run = await ingest.RunAsync();

        Assert.Equal(1, run.Sent);
        Assert.Equal(2, store.Batches.Count);
    }

    [Fact]
    public async Task Entries_that_cannot_be_registered_are_skipped_and_do_not_block_the_cursor()
    {
        Log(
            E(1, "SHORT"),
            E(2, "BBBBBBBBBBBB", version: "unknown"),
            E(3, "CCCCCCCCCCCC", eventId: "lower_case_event"),
            E(4, "DDDDDDDDDDDD") with { Source = "Has Space" },
            E(5, "EEEEEEEEEEEE") with { BusinessDate = "09/10/2026" },
            E(6, "FFFFFFFFFFFF"));
        var store = new FakeStore();

        var run = await Ingest(store).RunAsync();

        Assert.Equal(["FFFFFFFFFFFF"], store.AllItems.Select(item => item.Reference));
        Assert.Equal(5, run.SkippedInvalid);
        Assert.Equal(T0.AddMinutes(6), new DiagnosticsLogReader(_directory).ReadCursor()!.TimestampUtc);
    }

    [Fact]
    public async Task A_legacy_thirty_two_character_id_is_cut_to_twelve_and_a_half_written_last_line_is_ignored()
    {
        Log(E(1, "0123456789ABCDEF0123456789ABCDEF"));
        File.AppendAllText(Path.Combine(_directory, "diagnostics-202610.jsonl"), "{\"TimestampUtc\":\"2026-10-09T11:00:");
        var store = new FakeStore();

        var run = await Ingest(store).RunAsync();

        Assert.Equal(["0123456789AB"], store.AllItems.Select(item => item.Reference));
        Assert.Equal(1, run.UnreadableLines);
    }

    [Fact]
    public async Task Item_carries_context_and_only_the_etp_frames_normalised_and_collapsed()
    {
        string[] stack =
        [
            "System.Threading.Tasks.Task.Wait",
            "Etp.Reporting.Infrastructure.SqlServer.SqlServerApplicationReportQuery.LoadStaffPerformanceAsync",
            "Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView+<RunAsync>d__4.MoveNext",
            "Etp.Reporting.Desktop.Modules.Reports.ReportsWorkspaceView+<RunAsync>d__4.MoveNext",
            "System.Runtime.CompilerServices.AsyncMethodBuilderCore.Start"
        ];
        Log(E(1, "AAAAAAAAAAAA", stack: stack) with
        {
            ReportCode = "staff",
            StoreCode = "ALL",
            BusinessDate = "2026-10-09",
            DateFrom = "2026-10-01",
            Operation = "Run report",
            SqlErrors = [new DesktopDiagnosticSqlError(51402, 1, 16, "usp_x", 7)]
        });
        var store = new FakeStore();

        await Ingest(store).RunAsync();

        var item = Assert.Single(store.AllItems);
        Assert.Equal(["SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", "ReportsWorkspaceView.RunAsync"], item.Stack);
        Assert.Equal("SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", item.Parts.Site);
        Assert.Equal(51402, item.Parts.SqlNumber);
        Assert.Equal(ErrorFingerprint.Compute(item.Parts), item.Fingerprint);
        Assert.Equal(1, item.FingerprintVersion);
        Assert.Equal("staff", item.ReportCode);
        Assert.Equal("ALL", item.StoreCode);
        Assert.Equal(new DateOnly(2026, 10, 9), item.BusinessDate);
        Assert.Equal(new DateOnly(2026, 10, 1), item.DateFrom);
        Assert.Equal("Run report", item.Operation);
        Assert.Equal(T0.AddMinutes(1), item.TimestampUtc);
    }

    [Fact]
    public async Task An_old_entry_without_a_stack_attaches_to_the_single_matching_known_entry()
    {
        Log(E(1, "AAAAAAAAAAAA"));
        var known = new ErrorRegisterEntry(9, new string('c', 64), 1, "REPORT_STORE_REQUIRED", null, "Reports.Workspace", "ReportsWorkspaceView.RunAsync",
            "System.InvalidOperationException", 0, ErrorRegisterSeverity.Error, ErrorStatus.New, T0, T0, 1, "1.9.9.0", "1.9.9.0", null, null, null, null, null,
            [], null, T0, "x", null);
        var store = new FakeStore();

        await Ingest(store, _ => Task.FromResult<IReadOnlyList<ErrorRegisterEntry>>([known])).RunAsync();

        var item = Assert.Single(store.AllItems);
        Assert.Equal(new string('c', 64), item.Fingerprint);
        Assert.Equal("ReportsWorkspaceView.RunAsync", item.Parts.Site);
    }

    [Fact]
    public async Task The_register_is_not_loaded_when_every_entry_has_a_stack_and_a_refused_load_keeps_the_legacy_fingerprint()
    {
        var calls = 0;
        Log(E(1, "AAAAAAAAAAAA", stack: ["Etp.Reporting.Desktop.Foo.Run"]));
        var withStack = new FakeStore();
        await Ingest(withStack, _ => { calls++; return Task.FromResult<IReadOnlyList<ErrorRegisterEntry>>([]); }).RunAsync();
        Assert.Equal(0, calls);

        Log(E(2, "BBBBBBBBBBBB"));
        var refused = new FakeStore();
        var run = await Ingest(refused, _ => { calls++; throw new UnauthorizedAccessException("not the Owner"); }).RunAsync();

        Assert.Equal(1, calls);
        Assert.False(run.Failed);
        var item = Assert.Single(refused.AllItems);
        Assert.Equal("BBBBBBBBBBBB", item.Reference);
        Assert.Equal(string.Empty, item.Parts.Site);
        Assert.Equal("a46f2ccbec1c1b4f6c1f26ba228ec5527cae851e8443662c7bed03a4172658c4", item.Fingerprint);
    }

    [Fact]
    public async Task A_second_run_for_the_same_folder_while_one_is_in_progress_returns_at_once()
    {
        Log(E(1, "AAAAAAAAAAAA"));
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var store = new FakeStore { Gate = async () => { entered.SetResult(); await release.Task; } };
        var first = Ingest(store).RunAsync();
        await entered.Task;

        var second = await Ingest(new FakeStore()).RunAsync();
        release.SetResult();
        var firstResult = await first;

        Assert.True(second.AlreadyRunning);
        Assert.False(firstResult.AlreadyRunning);
        Assert.Equal(1, firstResult.Sent);
    }

    [Fact]
    public void Warnings_are_counted_per_event_code_and_never_registered()
    {
        Log(
            E(1, "AAAAAAAAAAAA", DesktopDiagnosticSeverity.Warning, "USER_ACCESS_SAVE_FAILED"),
            E(2, "BBBBBBBBBBBB", DesktopDiagnosticSeverity.Warning, "USER_ACCESS_SAVE_FAILED"),
            E(3, "CCCCCCCCCCCC", DesktopDiagnosticSeverity.Warning, "IMPORT_ROW_REJECTED.SQL547"),
            E(4, "DDDDDDDDDDDD"));
        var reader = new DiagnosticsLogReader(_directory);

        var counts = reader.CountWarnings();
        var recent = reader.CountWarnings(T0.AddMinutes(2));

        Assert.Equal(2, counts["USER_ACCESS_SAVE_FAILED"]);
        Assert.Equal(1, counts["IMPORT_ROW_REJECTED"]);
        Assert.Equal(2, counts.Count);
        Assert.Equal(1, recent["USER_ACCESS_SAVE_FAILED"]);
    }

    [Fact]
    public void A_missing_log_folder_reads_as_empty_and_a_cursor_is_written_and_read_back()
    {
        var reader = new DiagnosticsLogReader(Path.Combine(_directory, "nowhere"));

        Assert.Empty(reader.ReadErrorsAfter(null).Entries);
        Assert.Empty(reader.CountWarnings());
        Assert.Null(reader.ReadCursor());
        Assert.True(reader.WriteCursor(new RegisterCursor(T0, "AAAAAAAAAAAA")));
        Assert.Equal(new RegisterCursor(T0, "AAAAAAAAAAAA"), reader.ReadCursor());
    }

    [Fact]
    public void Old_months_are_not_read_when_a_cursor_is_newer_than_their_last_write()
    {
        var old = Path.Combine(_directory, "diagnostics-202609.jsonl");
        File.WriteAllText(old, JsonSerializer.Serialize(E(-60 * 24 * 20, "AAAAAAAAAAAA"), LogOptions) + Environment.NewLine);
        File.SetLastWriteTimeUtc(old, T0.UtcDateTime.AddDays(-20));
        Log(E(1, "BBBBBBBBBBBB"));
        var reader = new DiagnosticsLogReader(_directory);

        var fromStart = reader.ReadErrorsAfter(null).Entries.Select(entry => entry.CorrelationId);
        var afterCursor = reader.ReadErrorsAfter(new RegisterCursor(T0, "")).Entries.Select(entry => entry.CorrelationId);

        Assert.Equal(["AAAAAAAAAAAA", "BBBBBBBBBBBB"], fromStart);
        Assert.Equal(["BBBBBBBBBBBB"], afterCursor);
    }
}
