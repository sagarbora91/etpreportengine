using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop.Modules.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using System.IO.Compression;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DesktopImportCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Restatement_handler_rechecks_revoked_import_role_before_start_and_retry(bool retry)
    {
        RunSta(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), "EtpRestatementRole_" + Guid.NewGuid().ToString("N") + ".xlsx");
            File.WriteAllText(path, "Synthetic workbook supplied by the fixture reader");
            var persistence = new FakePersistence { CurrentImportFileId = 42 };
            var coordinator = Create(persistence, new FakeReader(_ => ValidR025()));
            var options = new FolderImportOptions("manager", true, "Corrected source", "WLMHW", new(2026, 8, 25));
            try
            {
                if (retry)
                {
                    persistence.PersistenceFailure = new InvalidOperationException("Synthetic first failure");
                    Await(coordinator.ImportFolderAsync(path, "synthetic", options));
                    Assert.Single(coordinator.FailedBatchPaths);
                    persistence.PersistenceFailure = null;
                }
                var view = new ImportWorkspaceView(coordinator, () => "synthetic");
                var checks = 0;
                view.AttachHost(() => new(++checks <= (retry ? 2 : 1), false),
                    (_, _, _) => Task.CompletedTask, () => Task.CompletedTask);
                ((TextBox)view.FindName("WorkbookPathInput")).Text = path;
                ((CheckBox)view.FindName("RestatementModeInput")).IsChecked = true;
                if (retry)
                    typeof(ImportWorkspaceView).GetField("lastImportOptions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, options);
                var writes = persistence.PersistenceCalls;
                var preparations = persistence.PrepareCalls;

                Await(retry ? view.RetryFailedBatchAsync() : view.ImportSelectedSourceAsync());

                Assert.Equal(writes, persistence.PersistenceCalls);
                Assert.Equal(preparations, persistence.PrepareCalls);
                Assert.Contains("does not have permission", ((TextBlock)view.FindName("ValidationResult")).Text);
            }
            finally { Await(coordinator.DisposeAsync().AsTask()); File.Delete(path); }
        });
    }

    [Fact]
    public async Task Restatement_preflight_refusal_stops_validated_persistence()
    {
        var persistence = new FakePersistence
        {
            CurrentImportFileId = 42,
            PrepareFailure = new UnauthorizedAccessException("Exact approval is absent.")
        };
        await using var coordinator = Create(persistence, new FakeReader(_ => ValidR025()));
        await coordinator.ValidateAsync("sales.xlsx");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => coordinator.PersistValidatedAsync("synthetic",
            new("WLMHW", new(2026, 8, 25), "manager", true, "Corrected source")));
        Assert.Equal(1, persistence.PrepareCalls);
        Assert.Equal(0, persistence.PersistenceCalls);
        Assert.Null(persistence.LastRequest);
    }

    [Fact]
    public async Task Folder_retry_keeps_zip_sources_until_disposal_and_reads_only_the_failed_entry()
    {
        var archivePath = Path.Combine(Path.GetTempPath(), "EtpRetry_" + Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            foreach (var name in new[] { "WLMHW_good_20260825.xlsx", "WLMHW_bad_20260825.xlsx" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write("Synthetic workbook read by the fixture reader");
            }
        var reads = new List<string>();
        var repaired = false;
        var coordinator = Create(new FakePersistence(), new FakeReader(path =>
        {
            Assert.True(File.Exists(path));
            reads.Add(path);
            if (path.Contains("bad") && !repaired) throw new InvalidDataException("Synthetic corrupt workbook");
            return ValidR025() with { FileName = path };
        }));
        coordinator.SetKnownStores(["WLMHW","HEMW"]);
        string? failedPath = null;
        try
        {
            var first = await coordinator.ImportFolderAsync(archivePath, "test", new("tester"));
            Assert.Equal(1, first.Imported);
            Assert.Equal(1, first.Failed);
            failedPath = Assert.Single(coordinator.FailedBatchPaths);
            Assert.True(File.Exists(failedPath));
            repaired = true;
            reads.Clear();
            var retried = await coordinator.RetryFailedFolderAsync();
            Assert.Equal(new[] { failedPath }, reads);
            Assert.Equal("empty export", Assert.Single(retried.Files).Status);
            Assert.Empty(coordinator.FailedBatchPaths);
        }
        finally
        {
            await coordinator.DisposeAsync();
            File.Delete(archivePath);
        }
        Assert.False(File.Exists(failedPath));
    }

    [Fact]
    public async Task Single_workbook_duplicate_is_a_no_op_without_a_second_persistence_request()
    {
        var persistence = new FakePersistence { Exists = true };
        await using var coordinator = Create(persistence, new FakeReader(_ => ValidR025()));
        await coordinator.ValidateAsync("sales.xlsx");

        var result = await coordinator.PersistValidatedAsync(
            "test", new("WLMHW", new(2026, 8, 25), "tester", false, ""));

        Assert.True(result.ExactDuplicate);
        Assert.Equal(0, result.Result.PersistedRows);
        Assert.False(result.RestatementApplied);
        Assert.Null(persistence.LastRequest);
    }

    [Fact]
    public async Task Single_workbook_restatement_rejects_an_identical_source_before_persistence()
    {
        var persistence = new FakePersistence { Exists = true, CurrentImportFileId = 41 };
        await using var coordinator = Create(persistence, new FakeReader(_ => ValidR025()));
        await coordinator.ValidateAsync("sales.xlsx");

        var error = await Assert.ThrowsAsync<ImportSourceException>(() => coordinator.PersistValidatedAsync(
            "test", new("WLMHW", new(2026, 8, 25), "tester", true, "Correction")));

        Assert.Equal("RESTATEMENT_DUPLICATE_FILE", error.Code);
        Assert.Null(persistence.LastRequest);
    }

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
        Assert.Same(persistence.PreparedRequest, persistence.LastRequest);
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
            () => new("WLMHW", new(2026, 8, 25), "tester", true, "Correction"),
            _ => Task.CompletedTask);

        var file = Assert.Single(summary.Files);
        Assert.Equal(BatchImportFileStatus.Failed, file.Status);
        Assert.Equal("RESTATEMENT_DUPLICATE_FILE", file.ErrorCode);
        Assert.Equal("A restatement must use a corrected source file with a new hash.", file.SafeErrorMessage);
        Assert.Equal(["sales.xlsx"], coordinator.FailedBatchPaths);
        Assert.Equal(0, evidenceCalls);
    }

    [Fact]
    public async Task Batch_duplicate_retains_the_original_without_persisting_new_data()
    {
        var persistence = new FakePersistence { Exists = true };
        var retained = new List<(string Path, string Report, string Store, DateOnly Date)>();
        await using var coordinator = Create(persistence, new FakeReader(_ => ValidR025()),
            (_, path, _, report, store, date, _) =>
            {
                retained.Add((path, report, store, date));
                return Task.CompletedTask;
            });
        var summary = await coordinator.RunBatchAsync(["sales.xlsx"], "integrated", () => false,
            () => new("WLMHW", new(2026, 8, 25), "tester", false, ""), _ => Task.CompletedTask);
        Assert.Equal(BatchImportFileStatus.Succeeded, Assert.Single(summary.Files).Status);
        Assert.True(summary.Files[0].ExactDuplicate);
        Assert.Equal(0, summary.Files[0].NewRows);
        Assert.Equal(("sales.xlsx", "R025", "WLMHW", new DateOnly(2026, 8, 25)), Assert.Single(retained));
        Assert.Null(persistence.LastRequest);
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

    private static void Await(Task task)
    {
        var elapsed = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(30), "Restatement UI action timed out.");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); } catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(1)), "Restatement UI test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class FakePersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public bool Exists { get; set; }
        public long? CurrentImportFileId { get; set; }
        public ImportPersistenceResult PersistenceResult { get; set; } = new("R025", 0);
        public ImportRowOutcome RowOutcome { get; set; } = new(0, 0, 0, 0);
        public ImportPersistenceRequest<MatchedImportEnvelope>? LastRequest { get; private set; }
        public ImportPersistenceRequest<MatchedImportEnvelope>? PreparedRequest { get; private set; }
        public Exception? PrepareFailure { get; set; }
        public Exception? PersistenceFailure { get; set; }
        public int PersistenceCalls { get; private set; }
        public int PrepareCalls { get; private set; }

        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request,
            CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            PreparedRequest = request;
            return PrepareFailure is null ? Task.CompletedTask : Task.FromException(PrepareFailure);
        }

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
            PersistenceCalls++;
            if (PersistenceFailure is not null) return Task.FromException<ImportPersistenceResult>(PersistenceFailure);
            LastRequest = request;
            return Task.FromResult(PersistenceResult);
        }

        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(
            string sourceSha256,
            CancellationToken cancellationToken = default) => Task.FromResult(RowOutcome);
    }
}
