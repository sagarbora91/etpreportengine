using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>The folder import keeps why each file failed and records each attempt at once (IF-017).</summary>
public sealed class FolderImportDiagnosticsTests
{
    [Fact]
    public async Task Each_attempt_is_recorded_when_its_file_finishes()
    {
        var persistence = new RecordingPersistence();
        persistence.OnPersist = request =>
        {
            // The second file is persisted only after the first one's attempt was recorded.
            if (request.AcceptedImport.Workbook.FileName == "two.xlsx")
                Assert.Equal(new[] { "one.xlsx" }, persistence.Recorded.Select(result => result.FileName));
        };
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["one.xlsx", "two.xlsx"], new("tester"));
        Assert.Equal(2, summary.Imported);
        Assert.Equal(new[] { "one.xlsx", "two.xlsx" }, persistence.Recorded.Select(result => result.FileName));
        Assert.All(persistence.Recorded, result => Assert.Null(result.Failure));
    }

    [Fact]
    public async Task A_failed_recording_does_not_stop_the_other_files_and_is_reported_when_the_run_ends()
    {
        var persistence = new RecordingPersistence { FailRecordingOf = "one.xlsx" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FolderImportService(persistence,
            new Reader(path => Sales(path, [20260825]))).RunFilesAsync(["one.xlsx", "two.xlsx"], new("tester")));
        Assert.Equal(2, persistence.Requests);
        Assert.Equal(new[] { "two.xlsx" }, persistence.Recorded.Select(result => result.FileName));
    }

    [Fact]
    public async Task Read_failure_keeps_code_and_read_stage()
    {
        var persistence = new RecordingPersistence();
        var summary = await new FolderImportService(persistence, new Reader(_ => throw new IOException("C:\\private\\Synthetic Customer.xlsx is locked")))
            .RunFilesAsync(["broken.xlsx"], new("tester"));
        var result = Assert.Single(summary.Files);
        Assert.Equal("Failed", result.Status);
        Assert.Equal("IMPORT_IO_FAILURE", result.Failure!.Code);
        Assert.Equal(FailureStage.Read, result.Failure.Stage);
        Assert.Equal(nameof(IOException), result.Failure.ExceptionType);
        Assert.Equal(result.Failure.SafeMessage, result.Message);
        Assert.DoesNotContain("private", result.Message);
        Assert.Same(result, Assert.Single(persistence.Recorded));
    }

    [Fact]
    public async Task Unknown_layout_keeps_its_first_blocker_at_the_match_stage()
    {
        var result = Assert.Single((await new FolderImportService(new RecordingPersistence(), new Reader(path =>
        {
            var workbook = Sales(path, [20260825]);
            return workbook with { Sheets = [workbook.Sheets[0] with { Headers = ["SYNTHETIC CUSTOMER NAME"] }] };
        })).RunFilesAsync(["unknown.xlsx"], new("tester"))).Files);
        Assert.Equal("Unknown layout", result.Status);
        Assert.Equal(FailureStage.Match, result.Failure!.Stage);
        Assert.Equal(ImportDiagnosticCatalogue.Template(result.Failure.Code), result.Failure.SafeMessage);
    }

    [Theory]
    [InlineData(true, FailureStage.Plan, null)]
    [InlineData(false, FailureStage.Apply, CommitState.RolledBack)]
    public async Task Persistence_failure_keeps_code_stage_and_commit_state(bool refusal, FailureStage stage, CommitState? commit)
    {
        Exception thrown = refusal
            ? new ImportSourceException("IMPORT_PERIOD_ALREADY_PRESENT", "Already imported. Use Restate.") { Stage = FailureStage.Plan }
            : new InvalidOperationException("Synthetic Customer secret");
        var persistence = new RecordingPersistence { OnPersist = _ => throw thrown };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(refusal ? "IMPORT_PERIOD_ALREADY_PRESENT" : "IMPORT_PROCESSING_FAILED", result.Failure!.Code);
        Assert.Equal(stage, result.Failure.Stage);
        Assert.Equal(commit, result.CommitState);
        Assert.DoesNotContain("Synthetic", result.Message);
        Assert.Equal(new string('b', 64), Assert.Single(persistence.Recorded).SourceSha256);
    }

    [Fact]
    public async Task Conflict_keeps_its_samples()
    {
        var samples = new[] { new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.ImportConflict,
            "Invoice date differs. Review and request a controlled restatement.", 2, DocumentRef: "R025 HEMW|2027|100000001 2026-08-25") };
        var persistence = new RecordingPersistence { OnPersist = _ => throw new ImportConflictException(1, samples) };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal(ImportCodes.ImportConflict, result.Failure!.Code);
        Assert.Equal(FailureStage.Apply, result.Failure.Stage);
        Assert.Equal(samples, result.Failure.Issues);
        Assert.Equal(CommitState.RolledBack, result.CommitState);
    }

    [Fact]
    public async Task Imported_file_keeps_its_batch_and_commit_state()
    {
        var batch = Guid.NewGuid();
        var result = Assert.Single((await new FolderImportService(new RecordingPersistence { BatchId = batch },
            new Reader(path => Sales(path, [20260825]))).RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Imported", result.Status);
        Assert.Equal(batch, result.BatchId);
        Assert.Equal(CommitState.Committed, result.CommitState);
        Assert.Null(result.Failure);
    }

    private static WorkbookSnapshot Sales(string path, int[] dates)
    {
        var rows = dates.Select((date, index) =>
        {
            var values = new Dictionary<string, object?>
            {
                ["TRANS_TYPE"] = "INV", ["STORE CODE"] = "HEMW", ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = (100000001 + index).ToString(),
                ["INVDATE"] = date, ["QTY"] = 1m, ["NETAMOUNT"] = 118m, ["NETVALUE"] = 100m, ["TAX"] = 18m
            };
            return new WorkbookRow(index + 2, RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToArray());
        }).ToArray();
        return new(path, 1, new string('b', 64), [new("SDB VariantwiseSales", 1, RetailSalesProfiles.R025Headers, rows)]);
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }

    private sealed class RecordingPersistence : IImportPersistenceUseCase<MatchedImportEnvelope>, IImportAttemptRecorder
    {
        public Action<ImportPersistenceRequest<MatchedImportEnvelope>>? OnPersist { get; set; }
        public string? FailRecordingOf { get; init; }
        public Guid? BatchId { get; init; }
        public int Requests { get; private set; }
        public List<FolderImportFileResult> Recorded { get; } = [];

        public Task RecordAttemptAsync(FolderImportFileResult result, CancellationToken cancellationToken = default)
        {
            if (result.FileName == FailRecordingOf) throw new InvalidOperationException("The attempt could not be recorded.");
            Recorded.Add(result);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Requests++;
            OnPersist?.Invoke(request);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, request.AcceptedImport.Staging.Rows.Count)
                { Status = "Imported", BatchId = BatchId ?? Guid.NewGuid() });
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
    }
}
