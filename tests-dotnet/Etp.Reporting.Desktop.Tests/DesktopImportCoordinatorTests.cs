using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopImportCoordinatorTests
{
    [Fact]
    public async Task Validation_owns_the_accepted_snapshot_profile_and_staging_state()
    {
        var persistence = new FakePersistence();
        await using var coordinator = Create(persistence, new FakeReader(_ => ValidR025()));

        var result = await coordinator.ValidateAsync("sales.xlsx");

        Assert.True(result.Accepted);
        Assert.True(coordinator.HasValidatedImport);
        Assert.Equal("R025", result.ReportCode);
        Assert.Equal(0, result.StagedRows);
        Assert.DoesNotContain(result.Diagnostics, row => row.Severity == Etp.Reporting.Import.Diagnostics.ImportDiagnosticSeverity.Blocker);
    }

    [Fact]
    public async Task Blocked_validation_clears_a_previous_accepted_import()
    {
        var persistence = new FakePersistence();
        await using var coordinator = Create(
            persistence,
            new FakeReader(path => path == "valid.xlsx" ? ValidR025() : InvalidWorkbook()));

        Assert.True((await coordinator.ValidateAsync("valid.xlsx")).Accepted);
        var blocked = await coordinator.ValidateAsync("invalid.xlsx");

        Assert.False(blocked.Accepted);
        Assert.False(coordinator.HasValidatedImport);
    }

    [Fact]
    public async Task Failed_read_and_new_source_selection_clear_the_previous_validated_file()
    {
        var persistence = new FakePersistence();
        await using var coordinator = Create(
            persistence,
            new FakeReader(path => path == "valid.xlsx" ? ValidR025() : throw new IOException("locked")));

        Assert.True((await coordinator.ValidateAsync("valid.xlsx")).Accepted);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ValidateAsync("new.xlsx"));
        Assert.False(coordinator.HasValidatedImport);

        Assert.True((await coordinator.ValidateAsync("valid.xlsx")).Accepted);
        coordinator.ClearValidatedImport();
        Assert.False(coordinator.HasValidatedImport);
    }

    [Fact]
    public async Task Validated_persistence_preserves_scope_restatement_and_evidence_identity()
    {
        var persistence = new FakePersistence
        {
            CurrentImportFileId = 41,
            PersistenceResult = new("R025", 7)
        };
        var evidence = new List<string>();
        await using var coordinator = Create(
            persistence,
            new FakeReader(_ => ValidR025()),
            (_, path, sha256, report, store, date, _) =>
            {
                evidence.Add($"{path}|{sha256}|{report}|{store}|{date:yyyy-MM-dd}");
                return Task.CompletedTask;
            });
        await coordinator.ValidateAsync("sales.xlsx");
        var context = new DesktopImportRunContext(
            "WLMHW", new(2026, 8, 25), "STORE\\Owner", true, "Corrected source");

        var outcome = await coordinator.PersistValidatedAsync("integrated", context);
        await coordinator.RetainValidatedEvidenceAsync("integrated", context);

        Assert.True(outcome.RestatementApplied);
        Assert.Equal("R025", outcome.ReportCode);
        Assert.Equal(41, persistence.LastRequest!.Restatement!.PreviousImportFileId);
        Assert.Equal("STORE\\Owner", persistence.LastRequest.ImportedBy);
        Assert.Equal("WLMHW", persistence.LastRequest.ExpectedStoreCode);
        Assert.Equal(new DateOnly(2026, 8, 25), persistence.LastRequest.ExpectedBusinessDate);
        Assert.Equal(RetailSalesProfiles.R025.Identity, persistence.LastRequest.AcceptedImport.ProfileIdentity);
        Assert.Equal("Sales", persistence.LastRequest.AcceptedImport.MatchedSheet.Name);
        Assert.Equal([$"sales.xlsx|{new string('a', 64)}|R025|WLMHW|2026-08-25"], evidence);
    }

    [Fact]
    public async Task Batch_retries_transient_reads_and_returns_row_outcomes_for_retry_ui()
    {
        var reads = 0;
        var persistence = new FakePersistence
        {
            RowOutcome = new(10, 7, 2, 1)
        };
        var evidenceCalls = 0;
        await using var coordinator = Create(
            persistence,
            new FakeReader(_ => ++reads == 1 ? throw new IOException("locked") : ValidR025()),
            (_, _, _, _, _, _, _) =>
            {
                evidenceCalls++;
                return Task.CompletedTask;
            });

        var summary = await coordinator.RunBatchAsync(
            ["sales.xlsx"],
            "integrated",
            () => false,
            () => new("WLMHW", new(2026, 8, 25), "manager", false, string.Empty),
            _ => Task.CompletedTask);

        var file = Assert.Single(summary.Files);
        Assert.Equal(BatchImportFileStatus.Succeeded, file.Status);
        Assert.Equal(2, file.Attempts);
        Assert.Equal(10, file.RowsProcessed);
        Assert.Equal(7, file.NewRows);
        Assert.Equal(2, file.AlreadyPresentRows);
        Assert.Equal(1, file.ConflictRows);
        Assert.Equal(1, evidenceCalls);
        Assert.Empty(coordinator.FailedBatchPaths);
    }

    [Fact]
    public async Task Batch_duplicate_in_restatement_mode_keeps_the_exact_block_code_and_never_retains_evidence()
    {
        var persistence = new FakePersistence { Exists = true };
        var evidenceCalls = 0;
        await using var coordinator = Create(
            persistence,
            new FakeReader(_ => ValidR025()),
            (_, _, _, _, _, _, _) =>
            {
                evidenceCalls++;
                return Task.CompletedTask;
            });

        var summary = await coordinator.RunBatchAsync(
            ["sales.xlsx"],
            "integrated",
            () => true,
            () => throw new Xunit.Sdk.XunitException("Scope should not be requested for a duplicate restatement."),
            _ => Task.CompletedTask);

        var file = Assert.Single(summary.Files);
        Assert.Equal(BatchImportFileStatus.Failed, file.Status);
        Assert.Equal("RESTATEMENT_DUPLICATE_FILE", file.ErrorCode);
        Assert.Equal("A restatement must use a corrected source file with a new hash.", file.SafeErrorMessage);
        Assert.Equal(["sales.xlsx"], coordinator.FailedBatchPaths);
        Assert.Equal(0, evidenceCalls);
    }

    private static DesktopImportCoordinator Create(
        FakePersistence persistence,
        IWorkbookReader reader,
        RetainEtpEvidence? evidence = null) =>
        new(
            _ => persistence,
            evidence ?? ((_, _, _, _, _, _, _) => Task.CompletedTask),
            reader);

    private static WorkbookSnapshot ValidR025() =>
        new(
            "sales.xlsx",
            1,
            new string('a', 64),
            [new("Sales", 1, RetailSalesProfiles.R025Headers, [])]);

    private static WorkbookSnapshot InvalidWorkbook() =>
        new("invalid.xlsx", 1, new string('b', 64), []);

    private sealed class FakeReader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(read(filePath));
    }

    private sealed class FakePersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public bool Exists { get; set; }
        public long? CurrentImportFileId { get; set; }
        public ImportPersistenceResult PersistenceResult { get; set; } = new("R025", 0);
        public ImportRowOutcome RowOutcome { get; set; } = new(0, 0, 0, 0);
        public ImportPersistenceRequest<MatchedImportEnvelope>? LastRequest { get; private set; }

        public Task<bool> ExistsByHashAsync(string sourceSha256, CancellationToken cancellationToken = default) =>
            Task.FromResult(Exists);

        public Task<long?> FindCurrentImportFileIdAsync(
            string reportCode,
            string storeCode,
            DateOnly businessDate,
            CancellationToken cancellationToken = default) => Task.FromResult(CurrentImportFileId);

        public Task<ImportPersistenceResult> PersistAsync(
            ImportPersistenceRequest<MatchedImportEnvelope> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(PersistenceResult);
        }

        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(
            string sourceSha256,
            CancellationToken cancellationToken = default) => Task.FromResult(RowOutcome);
    }
}
