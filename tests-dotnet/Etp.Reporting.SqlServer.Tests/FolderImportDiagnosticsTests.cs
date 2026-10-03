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

    [Fact]
    public async Task Inspection_failure_keeps_the_match_stage()
    {
        var result = Assert.Single((await new FolderImportService(new RecordingPersistence(), new Reader(path =>
            Sales(path, [20260825]) with { Sheets = null! })).RunFilesAsync(["broken.xlsx"], new("tester"))).Files);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(FailureStage.Match, result.Failure!.Stage);
    }

    // A refusal thrown inside the import transaction names its own stage, but the transaction it was in
    // rolled back all the same. The import store marks that rollback; a failure before any import
    // transaction existed (access, approval, validation) carries no mark and claims no rollback.
    [Theory]
    [InlineData(true, true, FailureStage.Plan, CommitState.RolledBack)]
    [InlineData(false, true, FailureStage.Apply, CommitState.RolledBack)]
    [InlineData(false, false, FailureStage.Apply, null)]
    public async Task Persistence_failure_keeps_code_stage_and_commit_state(bool refusal, bool inTransaction, FailureStage stage, CommitState? commit)
    {
        Exception thrown = refusal
            ? new ImportSourceException("IMPORT_PERIOD_ALREADY_PRESENT", "Already imported. Use Restate.") { Stage = FailureStage.Plan }
            : new InvalidOperationException("Synthetic Customer secret");
        if (inTransaction) SqlTransactionGuard.MarkRolledBack(thrown);
        var persistence = new RecordingPersistence { OnPersist = _ => throw thrown };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(refusal ? "IMPORT_PERIOD_ALREADY_PRESENT" : "IMPORT_PROCESSING_FAILED", result.Failure!.Code);
        Assert.Equal(stage, result.Failure.Stage);
        Assert.Equal(commit, result.CommitState);
        Assert.Equal(commit, Assert.Single(persistence.Recorded).CommitState);
        Assert.DoesNotContain("Synthetic", result.Message);
        Assert.Equal(new string('b', 64), Assert.Single(persistence.Recorded).SourceSha256);
    }

    [Fact]
    public async Task Conflict_keeps_its_samples()
    {
        var samples = new[] { new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.ImportConflict,
            "Invoice date differs. Review and request a controlled restatement.", 2, DocumentRef: "R025 HEMW|2027|100000001 2026-08-25") };
        // The import store marks a failure inside its transaction as rolled back, as it does for a conflict.
        var conflict = new ImportConflictException(1, samples);
        SqlTransactionGuard.MarkRolledBack(conflict);
        var persistence = new RecordingPersistence { OnPersist = _ => throw conflict };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal(ImportCodes.ImportConflict, result.Failure!.Code);
        Assert.Equal(FailureStage.Apply, result.Failure.Stage);
        Assert.Equal(samples, result.Failure.Issues);
        Assert.Equal(CommitState.RolledBack, result.CommitState);
        Assert.Equal(1, result.ConflictRows);
    }

    [Fact]
    public async Task Conflict_keeps_its_full_count_beside_at_most_twenty_samples()
    {
        var samples = Enumerable.Range(2, 30).Select(row => new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.ImportConflict,
            "Invoice date differs. Review and request a controlled restatement.", row)).ToArray();
        var persistence = new RecordingPersistence { OnPersist = _ => throw new ImportConflictException(35, samples) };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal(35, result.ConflictRows);
        Assert.Equal(ImportConflictException.MaximumSamples, result.Failure!.Issues.Count);
    }

    [Fact]
    public async Task A_failure_before_the_import_transaction_claims_no_rollback()
    {
        // Access revoked between runs: PersistAsync refuses before any import transaction exists.
        var persistence = new RecordingPersistence { OnPersist = _ => throw new UnauthorizedAccessException("Owner or Store Manager permission is required.") };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(FailureStage.Apply, result.Failure!.Stage);
        Assert.Null(result.CommitState);
        Assert.Null(Assert.Single(persistence.Recorded).CommitState);
    }

    [Fact]
    public async Task A_failed_read_back_keeps_the_saved_counts_and_evidence()
    {
        var batch = Guid.NewGuid();
        var persistence = new RecordingPersistence { BatchId = batch, FailOutcome = true, Evidence = EvidenceState.Retained };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(CommitState.Committed, result.CommitState);
        // The save committed the row and the source bytes; the failed read-back must not record zeros and no evidence.
        Assert.Equal(EvidenceState.Retained, result.Evidence);
        Assert.Equal(1, result.RowsProcessed);
        Assert.Equal(1, result.NewRows);
        Assert.Equal(("HEMW", new DateOnly(2026, 8, 25)), (result.StoreCode, result.PeriodEnd!.Value));
        var recorded = Assert.Single(persistence.Recorded);
        Assert.Equal((EvidenceState.Retained, 1, 1), (recorded.Evidence!.Value, recorded.RowsProcessed, recorded.NewRows));
    }

    [Fact]
    public async Task Cancelling_during_the_apply_is_a_cancellation_not_SQL_0()
    {
        using var cancellation = new CancellationTokenSource();
        var reported = new List<FolderImportFailure>();
        var persistence = new RecordingPersistence
        {
            OnPersist = _ =>
            {
                cancellation.Cancel();
                // SqlClient reports a command cancelled by its token as SqlException 0; the store marks the rollback.
                var cancelled = ImportCommitFailureTests.SqlError(0);
                SqlTransactionGuard.MarkRolledBack(cancelled);
                throw cancelled;
            }
        };
        var service = new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])), reportFailure: reported.Add);
        var result = Assert.Single((await service.RunFilesAsync(["sales.xlsx"], new("tester"), cancellationToken: cancellation.Token)).Files);
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal("Import cancelled.", result.Message);
        Assert.Null(result.Failure);
        Assert.Equal(CommitState.RolledBack, result.CommitState);
        Assert.Equal(EvidenceState.NotAttempted, result.Evidence);
        Assert.Empty(reported);
        Assert.Empty(service.FailedPaths);
        Assert.Equal("Cancelled", Assert.Single(persistence.Recorded).Status);

        // Without a cancel, SQL error 0 stays a database failure.
        var failing = new RecordingPersistence { OnPersist = _ => throw ImportCommitFailureTests.SqlError(0) };
        var failed = Assert.Single((await new FolderImportService(failing, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal(ImportCodes.Sql(0), failed.Failure!.Code);
    }

    [Fact]
    public async Task A_timeout_inside_the_transaction_does_not_claim_a_rollback()
    {
        var persistence = new RecordingPersistence { OnPersist = _ => throw new TimeoutException("Synthetic") };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal(ImportCodes.ImportTimeout, result.Failure!.Code);
        Assert.Null(result.CommitState);
    }

    [Fact]
    public async Task A_failure_after_the_commit_is_recorded_as_committed_with_its_batch()
    {
        var batch = Guid.NewGuid();
        var persistence = new RecordingPersistence { BatchId = batch, FailOutcome = true };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Failed", result.Status);
        Assert.Equal(CommitState.Committed, result.CommitState);
        Assert.Equal(batch, result.BatchId);
        Assert.Equal(FailureStage.Commit, result.Failure!.Stage);
        Assert.Equal(CommitState.Committed, Assert.Single(persistence.Recorded).CommitState);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failure_persistence_reports_after_its_commit_is_never_a_rollback(bool batchKnown)
    {
        var batch = Guid.NewGuid();
        var persistence = new RecordingPersistence
        {
            OnPersist = _ => throw new ImportCommittedException(batchKnown ? batch : null, new InvalidOperationException("Synthetic"))
        };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal(batchKnown ? CommitState.Committed : CommitState.Unknown, result.CommitState);
        Assert.Equal(batchKnown ? batch : null, result.BatchId);
        Assert.Equal("IMPORT_PROCESSING_FAILED", result.Failure!.Code);
    }

    [Fact]
    public async Task Cancelling_after_the_commit_keeps_the_committed_batch()
    {
        using var cancellation = new CancellationTokenSource();
        var batch = Guid.NewGuid();
        var persistence = new RecordingPersistence { BatchId = batch, OnOutcome = cancellation.Cancel };
        var result = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"), cancellationToken: cancellation.Token)).Files);
        Assert.Equal("Cancelled", result.Status);
        Assert.Equal(CommitState.Committed, result.CommitState);
        Assert.Equal(batch, result.BatchId);
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
        public bool FailOutcome { get; init; }
        public Action? OnOutcome { get; init; }
        public Guid? BatchId { get; init; }
        public EvidenceState? Evidence { get; init; }
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
                { Status = "Imported", BatchId = BatchId ?? Guid.NewGuid(), Evidence = Evidence });
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default)
        {
            OnOutcome?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (FailOutcome) throw new InvalidOperationException("Synthetic outcome failure");
            return Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
        }
    }
}
