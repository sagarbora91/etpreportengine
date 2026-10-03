using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class FolderImportServiceTests
{
    [Fact]
    public async Task Empty_export_for_a_new_store_uses_the_active_catalogue_instead_of_seeded_store_names()
    {
        const string path = "STORE3_R025_20260924.xlsx";
        var unknownPersistence = new CapturePersistence();
        var unknown = await new FolderImportService(unknownPersistence, new Reader(file => Sales(file, "STORE3", [])), knownStores: [])
            .RunFilesAsync([path], new("automation-test"));
        Assert.Equal(1, unknown.Failed);
        Assert.Empty(unknownPersistence.Requests);

        var knownPersistence = new CapturePersistence();
        var known = await new FolderImportService(knownPersistence, new Reader(file => Sales(file, "STORE3", [])), knownStores: ["STORE3"])
            .RunFilesAsync([path], new("automation-test"));
        var result = Assert.Single(known.Files);
        Assert.Equal("empty export", result.Status);
        Assert.Equal("STORE3", result.StoreCode);
        Assert.Equal(new DateOnly(2026, 9, 24), result.PeriodEnd);
        Assert.Equal("STORE3", Assert.Single(knownPersistence.Requests).ExpectedStoreCode);
    }

    [Fact]
    public async Task Full_zip_imports_supported_files_and_reports_unsupported_ETP_workbooks_as_not_needed()
    {
        var folder = Path.Combine(Path.GetTempPath(), "EtpZipTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var zip = Path.Combine(folder, "full-etp.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
                foreach (var name in new[] { "R025_VariantwiseSales.xlsx", "R099_UnsupportedReport.xlsx" })
                    using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write("synthetic workbook read by test adapter");
            var persistence = new CapturePersistence();
            var service = new FolderImportService(persistence, new Reader(path =>
            {
                var snapshot = Sales(path, "HEMW", [20260825]);
                return Path.GetFileName(path).StartsWith("R099", StringComparison.Ordinal)
                    ? snapshot with { Sheets = [snapshot.Sheets[0] with { Headers = ["UNSUPPORTED_COLUMN"] }] } : snapshot;
            }));
            var result = await service.RunAsync(zip, new("tester"));
            Assert.Equal(1, result.Imported);
            Assert.Equal(0, result.Failed);
            Assert.Equal(0, result.UnknownLayouts);
            Assert.Equal("Not needed", Assert.Single(result.Files, file => file.FileName.StartsWith("R099")).Status);
            Assert.Single(persistence.Requests);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Retry_reads_only_failed_files_and_preserves_successful_sibling_scope()
    {
        var repaired = false;
        var reads = new List<string>();
        var persistence = new CapturePersistence();
        var service = new FolderImportService(persistence, new Reader(path =>
        {
            reads.Add(path);
            if (path == "empty.xlsx" && !repaired) throw new InvalidDataException("Corrupted workbook");
            return Sales(path, "HEMW", path == "empty.xlsx" ? [] : [20260825]);
        }));
        var first = await service.RunFilesAsync(["sales.xlsx", "empty.xlsx"], new("tester"));
        Assert.Equal(1, first.Imported);
        Assert.Equal(1, first.Failed);
        Assert.Equal(new[] { "empty.xlsx" }, service.FailedPaths);
        repaired = true;
        reads.Clear();
        var retry = await service.RetryFailedAsync(new("tester"));
        Assert.Equal(new[] { "empty.xlsx" }, reads);
        var result = Assert.Single(retry.Files);
        Assert.Equal("empty export", result.Status);
        Assert.Equal("HEMW", result.StoreCode);
        Assert.Equal(new DateOnly(2026, 8, 25), result.PeriodEnd);
        Assert.Equal(2, persistence.Requests.Count);
        Assert.Empty(service.FailedPaths);
        Assert.Empty((await service.RetryFailedAsync(new("tester"))).Files);
    }

    [Fact]
    public async Task Conflict_outcome_is_retryable_even_when_persistence_did_not_throw()
    {
        var persistence = new CapturePersistence { Status = "Conflict", Conflicts = 1 };
        var service = new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825])));
        Assert.Equal(1, (await service.RunFilesAsync(["sales.xlsx"], new("tester"))).Failed);
        Assert.Equal(new[] { "sales.xlsx" }, service.FailedPaths);
        Assert.Equal(1, (await service.RetryFailedAsync(new("tester"))).Failed);
        Assert.Equal(2, persistence.Requests.Count);
    }

    [Fact]
    public async Task One_folder_detects_each_store_and_full_period_without_user_scope()
    {
        var persistence = new CapturePersistence();
        var reader = new Reader(path => Sales(path, path.StartsWith("titan") ? "WLMHW" : "HEMW", [20260701, 20260825]));
        var service = new FolderImportService(persistence, reader);
        var summary = await service.RunFilesAsync(["titan.xlsx", "helios.xlsx"], new("tester"));
        Assert.Equal(2, summary.Imported);
        Assert.Equal(["WLMHW", "HEMW"], persistence.Requests.Select(request => request.ExpectedStoreCode));
        Assert.All(summary.Files, file => { Assert.Equal(new DateOnly(2026, 7, 1), file.PeriodStart); Assert.Equal(new DateOnly(2026, 8, 25), file.PeriodEnd); });
    }

    [Fact]
    public async Task Corrupted_layout_reports_closest_family_and_does_not_stop_other_files()
    {
        var persistence = new CapturePersistence();
        var reader = new Reader(path =>
        {
            var snapshot = Sales(path, "HEMW", [20260825]);
            return path == "bad.xlsx" ? snapshot with { Sheets = [snapshot.Sheets[0] with { Headers = snapshot.Sheets[0].Headers.Select(header => header == "QTY" ? "BROKEN_QUANTITY" : header).ToArray() }] } : snapshot;
        });
        var summary = await new FolderImportService(persistence, reader).RunFilesAsync(["bad.xlsx", "good.xlsx"], new("tester"));
        Assert.Equal(0, summary.Failed);
        Assert.Equal(1, summary.Imported);
        var bad = Assert.Single(summary.Files, file => file.Status == "Unknown layout");
        Assert.Contains(bad.Diagnostics!, issue => issue.Code == "REQUIRED_COLUMN_MISSING" && issue.Message.Contains("R025"));
        Assert.Single(persistence.Requests);
    }

    [Theory]
    [InlineData("Duplicate content")]
    [InlineData("Already present")]
    public async Task Persistence_content_and_subset_statuses_are_visible_with_zero_new_rows(string status)
    {
        var persistence = new CapturePersistence { Status = status };
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"));
        var file = Assert.Single(summary.Files);
        Assert.Equal(status, file.Status);
        Assert.Equal(0, file.NewRows);
        Assert.Equal(1, file.AlreadyPresentRows);
        Assert.Equal(1, summary.Duplicates);
    }

    [Fact]
    public async Task Exact_hash_duplicate_does_not_call_persistence()
    {
        var persistence = new CapturePersistence { Exists = true };
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"));
        Assert.Equal("Duplicate", Assert.Single(summary.Files).Status);
        Assert.Empty(persistence.Requests);
        Assert.Equal(0, summary.NewRows);
    }

    [Theory]
    [InlineData("WLMHW", 20260825)]
    [InlineData("HEMW", 20260826)]
    public async Task Identical_bytes_in_another_store_or_period_are_not_an_exact_duplicate(string store, int date)
    {
        var persistence = new CapturePersistence { Exists = true, ExactScope = ("HEMW", new(2026, 8, 25), new(2026, 8, 25)) };
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, store, [date])))
            .RunFilesAsync(["sales.xlsx"], new("tester"));
        Assert.Equal("Imported", Assert.Single(summary.Files).Status);
        Assert.Single(persistence.Requests);
        Assert.Equal(1, summary.NewRows);
    }

    [Fact]
    public async Task Restatement_cannot_replace_detected_store_or_date_even_for_duplicate_bytes()
    {
        foreach (var options in new[] { new FolderImportOptions("tester", true, "Correction", "WLMHW"),
                     new FolderImportOptions("tester", true, "Correction", OverrideBusinessDate: new(2026, 8, 26)) })
        {
            var persistence = new CapturePersistence { Exists = true };
            var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825])))
                .RunFilesAsync(["sales.xlsx"], options);
            var file = Assert.Single(summary.Files);
            Assert.Equal("Failed", file.Status);
            Assert.Contains("override does not match", file.Message);
            Assert.Empty(persistence.Requests);
        }
    }

    [Fact]
    public async Task Header_only_file_inherits_its_store_folder_scope_and_succeeds_as_empty_export()
    {
        var persistence = new CapturePersistence();
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", path == "empty.xlsx" ? [] : [20260825])))
            .RunFilesAsync(["empty.xlsx", "sales.xlsx"], new("tester"));
        var empty = Assert.Single(summary.Files, file => file.FileName == "empty.xlsx");
        Assert.Equal("empty export", empty.Status);
        Assert.Equal("HEMW", empty.StoreCode);
        Assert.Equal(new DateOnly(2026, 8, 25), empty.PeriodEnd);
    }

    [Theory]
    [InlineData("Imported", EvidenceState.Retained)]
    [InlineData("Duplicate content", EvidenceState.AlreadyHeld)]
    [InlineData("Already present", EvidenceState.AlreadyHeld)]
    public async Task Imported_and_content_duplicate_files_record_the_evidence_of_their_import_transaction(string status,
        EvidenceState evidence)
    {
        var persistence = new CapturePersistence { Status = status, Evidence = evidence };
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"));
        var file = Assert.Single(summary.Files);
        Assert.Equal(status, file.Status);
        Assert.Equal(evidence, file.Evidence);
        // The import transaction kept the bytes; nothing is retained after it (IF-023).
        Assert.Empty(persistence.Retained);
        Assert.DoesNotContain(file.Diagnostics ?? [], issue => issue.Code == ImportCodes.EvidenceNotRetained);
    }

    [Fact]
    public async Task Evidence_not_retained_by_the_import_is_a_recorded_warning()
    {
        var summary = await new FolderImportService(new CapturePersistence { Evidence = EvidenceState.NotRetained },
            new Reader(path => Sales(path, "HEMW", [20260825]))).RunFilesAsync(["sales.xlsx"], new("tester"));
        var file = Assert.Single(summary.Files);
        Assert.Equal("Imported", file.Status);
        Assert.Equal(EvidenceState.NotRetained, file.Evidence);
        var issue = Assert.Single(file.Diagnostics!, issue => issue.Code == ImportCodes.EvidenceNotRetained);
        Assert.Equal(ImportIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public async Task Duplicate_retains_missing_bytes_in_its_own_transaction_and_records_a_failure()
    {
        var reader = new Reader(path => Sales(path, "HEMW", [20260825]) with { Content = new(SourceBytes, Sha) });
        var failing = new CapturePersistence { Exists = true, RetainFailure = new IOException("Synthetic database failure.") };
        var first = Assert.Single((await new FolderImportService(failing, reader).RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Duplicate", first.Status);
        Assert.Equal(EvidenceState.NotRetained, first.Evidence);
        Assert.Contains(first.Diagnostics!, issue => issue.Code == ImportCodes.EvidenceNotRetained);

        var duplicate = new CapturePersistence { Exists = true };
        var second = await new FolderImportService(duplicate, reader).RunFilesAsync(["sales.xlsx"], new("tester"));
        var file = Assert.Single(second.Files);
        Assert.Equal("Duplicate", file.Status);
        Assert.Equal(EvidenceState.Retained, file.Evidence);
        Assert.DoesNotContain(file.Diagnostics ?? [], issue => issue.Code == ImportCodes.EvidenceNotRetained);
        var retained = Assert.Single(duplicate.Retained);
        Assert.Equal(Sha, retained.Sha256);
        Assert.Equal(SourceBytes, retained.Content);
        Assert.Empty(duplicate.Requests);
        Assert.Equal(0, second.NewRows);
    }

    [Fact]
    public async Task Cancellation_stops_before_the_next_file_and_preserves_finished_results()
    {
        using var cancellation = new CancellationTokenSource();
        var persistence = new CapturePersistence();
        var progress = new InlineProgress(value => { if (value.Completed == 1) cancellation.Cancel(); });
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825])))
            .RunFilesAsync(["one.xlsx", "two.xlsx"], new("tester"), progress, cancellation.Token);
        Assert.Single(persistence.Requests);
        Assert.Equal("Imported", summary.Files[0].Status);
        Assert.Equal("Cancelled", summary.Files[1].Status);
    }

    // IF-016 interim (planner 1): the target is chosen by overlap with the replacement's declared period.
    private static readonly FolderImportOptions Restate = new("tester", true, "Corrected source");
    private static readonly RestatementCandidate First = new(11, "R025_HEMW_25Aug.xlsx", new(2026, 8, 25), new(2026, 8, 25), 40);
    private static readonly RestatementCandidate Second = new(12, "R025_HEMW_26Aug.xlsx", new(2026, 8, 26), new(2026, 8, 26), 35);

    [Fact]
    public async Task Restatement_with_no_overlapping_target_is_rejected()
    {
        var persistence = new CapturePersistence();
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825, 20260826])))
            .RunFilesAsync(["sales.xlsx"], Restate);
        var file = Assert.Single(summary.Files);
        Assert.Equal("Failed", file.Status);
        Assert.Contains("nothing can be restated", file.Message);
        Assert.Equal(ImportCodes.RestatementMatchesNothing, Assert.Single(file.Diagnostics!, issue => issue.Severity == ImportIssueSeverity.Blocker).Code);
        Assert.Equal((new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26)), Assert.Single(persistence.CandidateLookups));
        Assert.Empty(persistence.Prepared);
        Assert.Empty(persistence.Requests);
    }

    [Fact]
    public async Task Restatement_with_one_overlapping_target_uses_it_without_asking()
    {
        var persistence = new CapturePersistence { Candidates = [Second] };
        var asked = 0;
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825, 20260826])))
            .RunFilesAsync(["sales.xlsx"], Restate with { ChooseRestatementTarget = (_, _) => { asked++; return Task.FromResult<RestatementCandidate?>(null); } });
        Assert.Equal("Imported", Assert.Single(summary.Files).Status);
        Assert.Equal(0, asked);
        Assert.Equal(12, Assert.Single(persistence.Prepared).Restatement!.PreviousImportFileId);
        Assert.Equal(12, Assert.Single(persistence.Requests).Restatement!.PreviousImportFileId);
    }

    [Fact]
    public async Task Restatement_with_two_targets_uses_the_picked_one()
    {
        var persistence = new CapturePersistence { Candidates = [First, Second] };
        var choices = new List<RestatementTargetChoice>();
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825, 20260826])))
            .RunFilesAsync(["sales.xlsx"], Restate with { ChooseRestatementTarget = (choice, _) =>
            {
                choices.Add(choice);
                // A copy proves the pick is matched by file id, not by reference.
                return Task.FromResult<RestatementCandidate?>(choice.Candidates[1] with { });
            } });
        Assert.Equal("Imported", Assert.Single(summary.Files).Status);
        var choice = Assert.Single(choices);
        Assert.Equal(("sales.xlsx", "R025", "HEMW"), (choice.FileName, choice.ReportCode, choice.StoreCode));
        Assert.Equal((new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 26)), (choice.PeriodStart, choice.PeriodEnd));
        Assert.Equal([11L, 12L], choice.Candidates.Select(candidate => candidate.ImportFileId));
        var request = Assert.Single(persistence.Requests);
        Assert.Equal(12, request.Restatement!.PreviousImportFileId);
        Assert.Equal("Corrected source", request.Restatement.Reason);
        Assert.Same(request, Assert.Single(persistence.Prepared));
    }

    [Fact]
    public async Task Restatement_with_two_targets_and_no_pick_is_refused_with_candidates()
    {
        // Automation passes no picker; the Owner may also close the dialog, and a pick outside the list counts as none.
        foreach (var options in new[] { Restate, Restate with { ChooseRestatementTarget = (_, _) => Task.FromResult<RestatementCandidate?>(null) },
                     Restate with { ChooseRestatementTarget = (_, _) => Task.FromResult<RestatementCandidate?>(First with { ImportFileId = 99 }) } })
        {
            var persistence = new CapturePersistence { Candidates = [First, Second] };
            var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825, 20260826])))
                .RunFilesAsync(["sales.xlsx"], options);
            var file = Assert.Single(summary.Files);
            Assert.Equal("Failed", file.Status);
            Assert.Contains("files 11, 12", file.Message);
            var issues = file.Diagnostics!.Where(issue => issue.Code == ImportCodes.RestatementTargetAmbiguous).ToArray();
            Assert.Equal(2, issues.Length);
            Assert.All(issues, issue => Assert.Equal(ImportIssueSeverity.Blocker, issue.Severity));
            Assert.Contains("11: R025_HEMW_25Aug.xlsx, 25 Aug 2026, 40 rows", issues[0].Message);
            Assert.Contains("12: R025_HEMW_26Aug.xlsx, 26 Aug 2026, 35 rows", issues[1].Message);
            Assert.Empty(persistence.Prepared);
            Assert.Empty(persistence.Requests);
        }
    }

    [Fact]
    public async Task Missing_approval_reports_RESTATEMENT_APPROVAL_REQUIRED()
    {
        // SqlServerImportPersistenceUseCase raises this from PersistAsync's exact-approval check.
        var refusal = SqlServerImportPersistenceUseCase.RestatementApprovalRequired();
        Assert.Equal(ImportCodes.RestatementApprovalRequired, refusal.Code);
        var persistence = new CapturePersistence { Candidates = [First], PersistFailure = refusal };
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825])))
            .RunFilesAsync(["sales.xlsx"], Restate);
        var file = Assert.Single(summary.Files);
        Assert.Equal("Failed", file.Status);
        Assert.Equal(11, Assert.Single(persistence.Prepared).Restatement!.PreviousImportFileId);
        Assert.Single(persistence.Requests);
        // The result carries the approval message, not the generic access or processing text.
        Assert.Equal(refusal.Message, file.Message);
        Assert.DoesNotContain("could not be accessed", file.Message);
        Assert.DoesNotContain("could not be imported", file.Message);
        // The failure record (IF-017) is set by the diagnostics lane (p1-a); once it is, it must carry this code.
        if (file.Failure is not null) Assert.Equal(ImportCodes.RestatementApprovalRequired, file.Failure.Code);
    }

    [Fact]
    public async Task Restatement_over_a_partly_overlapped_import_is_refused_before_prepare()
    {
        // 1-31 Aug is the only overlap of a 25-26 Aug replacement; SQL would refuse it with 51555, so no auto-pick.
        var month = new RestatementCandidate(13, "R025_HEMW_Aug.xlsx", new(2026, 8, 1), new(2026, 8, 31), 900);
        foreach (var candidates in new[] { new[] { month }, new[] { First, month } })
        {
            var asked = 0;
            var persistence = new CapturePersistence { Candidates = candidates };
            var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825, 20260826])))
                .RunFilesAsync(["sales.xlsx"], Restate with { ChooseRestatementTarget = (choice, _) => { asked++; return Task.FromResult<RestatementCandidate?>(choice.Candidates[0]); } });
            var file = Assert.Single(summary.Files);
            Assert.Equal("Failed", file.Status);
            Assert.Contains("only partly overlaps", file.Message);
            var issue = Assert.Single(file.Diagnostics!, issue => issue.Code == ImportCodes.RestatementTargetNotCovered);
            Assert.Contains("13: R025_HEMW_Aug.xlsx", issue.Message);
            Assert.Equal(0, asked);
            Assert.Empty(persistence.Prepared);
            Assert.Empty(persistence.Requests);
        }
    }

    [Fact]
    public async Task Restatement_that_changes_an_unpicked_import_is_refused_before_prepare_with_a_restatement_code()
    {
        // A corrected 25-26 Aug file over two current files; it changes rows of both, and the Owner picks the first.
        var persistence = new CapturePersistence { Candidates = [First, Second], Changed = [11, 12] };
        var asked = 0;
        var summary = await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825, 20260826])))
            .RunFilesAsync(["sales.xlsx"], Restate with { ChooseRestatementTarget = (choice, _) => { asked++; return Task.FromResult<RestatementCandidate?>(choice.Candidates[0]); } });

        var file = Assert.Single(summary.Files);
        Assert.Equal("Failed", file.Status);
        Assert.Equal(ImportCodes.RestatementOtherImportChanged, file.Failure!.Code);
        Assert.DoesNotContain("Use Restate", file.Message);
        Assert.Contains("restates only one import", file.Message);
        var issues = file.Diagnostics!.Where(issue => issue.Code == ImportCodes.RestatementOtherImportChanged).ToArray();
        Assert.Equal(["Current import 11: R025_HEMW_25Aug.xlsx", "Current import 12: R025_HEMW_26Aug.xlsx"],
            issues.Select(issue => issue.Message.Split(',')[0]));
        // No pick can succeed, so nothing is asked, and no approval is requested.
        Assert.Equal(0, asked);
        Assert.Equal([11L, 12L], Assert.Single(persistence.ChangedLookups));
        Assert.Empty(persistence.Prepared);
        Assert.Empty(persistence.Requests);
    }

    [Fact]
    public async Task Restatement_must_pick_the_one_import_it_changes_when_the_others_are_taken_over()
    {
        // Only file 12 holds rows this file changes; file 11's rows are all in it, so promotion takes 11 over.
        var wrongPick = new CapturePersistence { Candidates = [First, Second], Changed = [12] };
        var refused = await new FolderImportService(wrongPick, new Reader(path => Sales(path, "HEMW", [20260825, 20260826])))
            .RunFilesAsync(["sales.xlsx"], Restate with { ChooseRestatementTarget = (choice, _) => Task.FromResult<RestatementCandidate?>(choice.Candidates[0]) });
        var file = Assert.Single(refused.Files);
        Assert.Equal(ImportCodes.RestatementOtherImportChanged, file.Failure!.Code);
        Assert.StartsWith("Current import 12: R025_HEMW_26Aug.xlsx", Assert.Single(file.Diagnostics!, issue => issue.Code == ImportCodes.RestatementOtherImportChanged).Message);
        Assert.Empty(wrongPick.Prepared);

        var rightPick = new CapturePersistence { Candidates = [First, Second], Changed = [12] };
        var imported = await new FolderImportService(rightPick, new Reader(path => Sales(path, "HEMW", [20260825, 20260826])))
            .RunFilesAsync(["sales.xlsx"], Restate with { ChooseRestatementTarget = (choice, _) => Task.FromResult<RestatementCandidate?>(choice.Candidates[1]) });
        Assert.Equal("Imported", Assert.Single(imported.Files).Status);
        Assert.Equal(12, Assert.Single(rightPick.Prepared).Restatement!.PreviousImportFileId);
    }

    // Review 1.9.3 finding 2 (IF-023): every attempt records an evidence state, never NULL.
    [Fact]
    public async Task A_file_that_was_never_read_or_never_reached_records_evidence_not_attempted()
    {
        var unreadable = await new FolderImportService(new CapturePersistence(),
            new Reader(_ => throw new IOException("Synthetic unreadable workbook."))).RunFilesAsync(["broken.xlsx"], new("tester"));
        var failed = Assert.Single(unreadable.Files);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal(EvidenceState.NotAttempted, failed.Evidence);

        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(value => { if (value.Completed == 1) cancellation.Cancel(); });
        var summary = await new FolderImportService(new CapturePersistence(), new Reader(path => Sales(path, "HEMW", [20260825])))
            .RunFilesAsync(["one.xlsx", "two.xlsx"], new("tester"), progress, cancellation.Token);
        var cancelled = Assert.Single(summary.Files, file => file.Status == "Cancelled");
        Assert.Equal(EvidenceState.NotAttempted, cancelled.Evidence);
    }

    [Fact]
    public async Task A_committed_import_whose_later_step_failed_records_its_evidence_state()
    {
        // The import kept its bytes and committed; reading its result back failed afterwards.
        var persistence = new CapturePersistence { Evidence = EvidenceState.Retained, OutcomeFailure = new InvalidOperationException("Synthetic read-back failure.") };
        var file = Assert.Single((await new FolderImportService(persistence, new Reader(path => Sales(path, "HEMW", [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal(CommitState.Committed, file.CommitState);
        Assert.Equal(EvidenceState.Retained, file.Evidence);

        // A failure after the commit inside the store reports no evidence; the state is unknown, not missing.
        var committed = new CapturePersistence { PersistFailure = new ImportCommittedException(Guid.NewGuid(), new TimeoutException("Synthetic.")) };
        var unknown = Assert.Single((await new FolderImportService(committed, new Reader(path => Sales(path, "HEMW", [20260825])))
            .RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal(CommitState.Committed, unknown.CommitState);
        Assert.Equal(EvidenceState.Unknown, unknown.Evidence);
        Assert.Equal("UNKNOWN", EvidenceState.Unknown.ToDatabaseCode());
    }

    [Fact]
    public async Task A_duplicate_without_an_evidence_retainer_records_evidence_not_attempted()
    {
        var file = Assert.Single((await new FolderImportService(new PlainPersistence(),
            new Reader(path => Sales(path, "HEMW", [20260825]))).RunFilesAsync(["sales.xlsx"], new("tester"))).Files);
        Assert.Equal("Duplicate", file.Status);
        Assert.Equal(EvidenceState.NotAttempted, file.Evidence);
    }

    // A persistence that keeps no evidence: every file is already imported.
    private sealed class PlainPersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task<IReadOnlyList<RestatementCandidate>> FindRestatementCandidatesAsync(string report, string store, DateOnly start, DateOnly end,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RestatementCandidate>>([]);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A duplicate is never persisted.");
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
    }

    private static WorkbookSnapshot Sales(string path, string store, int[] dates)
    {
        var rows = dates.Select((date, index) =>
        {
            var values = new Dictionary<string, object?>
            {
                ["TRANS_TYPE"] = "INV", ["STORE CODE"] = store, ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = (100000001 + index).ToString(),
                ["INVDATE"] = date, ["QTY"] = 1m, ["NETAMOUNT"] = 118m, ["NETVALUE"] = 100m, ["TAX"] = 18m
            };
            return new WorkbookRow(index + 2, RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToArray());
        }).ToArray();
        return new(path, 1, new string(path.StartsWith("titan") ? 'a' : 'b', 64), [new("SDB VariantwiseSales", 1, RetailSalesProfiles.R025Headers, rows)]);
    }
    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }
    private sealed class InlineProgress(Action<FolderImportProgress> report) : IProgress<FolderImportProgress>
    { public void Report(FolderImportProgress value) => report(value); }
    private static readonly byte[] SourceBytes = [7, 8, 9];
    private static readonly string Sha = new('b', 64); // Sales() hashes every non-titan file name as b…b.

    private sealed class CapturePersistence : IImportPersistenceUseCase<MatchedImportEnvelope>, IImportEvidenceRetainer
    {
        public string Status { get; init; } = "Imported";
        public EvidenceState? Evidence { get; init; }
        public Exception? RetainFailure { get; init; }
        public List<(string Sha256, byte[] Content)> Retained { get; } = [];
        public Task<EvidenceState> RetainImportedSourceAsync(string sourceSha256, ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            if (RetainFailure is not null) return Task.FromException<EvidenceState>(RetainFailure);
            Retained.Add((sourceSha256, content.ToArray()));
            return Task.FromResult(EvidenceState.Retained);
        }
        public bool Exists { get; init; }
        public int Conflicts { get; init; }
        public (string Store, DateOnly Start, DateOnly End)? ExactScope { get; init; }
        public List<ImportPersistenceRequest<MatchedImportEnvelope>> Requests { get; } = [];
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(Exists);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(Exists && (ExactScope is null || ExactScope == (store, start, end)));
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public IReadOnlyList<RestatementCandidate> Candidates { get; init; } = [];
        public List<(DateOnly Start, DateOnly End)> CandidateLookups { get; } = [];
        public Exception? PrepareFailure { get; init; }
        public Exception? PersistFailure { get; init; }
        public List<ImportPersistenceRequest<MatchedImportEnvelope>> Prepared { get; } = [];
        public Task<IReadOnlyList<RestatementCandidate>> FindRestatementCandidatesAsync(string report, string store, DateOnly start, DateOnly end,
            CancellationToken cancellationToken = default)
        {
            CandidateLookups.Add((start, end));
            return Task.FromResult(Candidates);
        }
        public IReadOnlyList<long> Changed { get; init; } = [];
        public List<long[]> ChangedLookups { get; } = [];
        public Task<IReadOnlyList<long>> FindImportsChangedByAsync(MatchedImportEnvelope accepted, IReadOnlyList<long> importFileIds,
            DateOnly? businessDate = null, CancellationToken cancellationToken = default)
        {
            ChangedLookups.Add(importFileIds.ToArray());
            return Task.FromResult<IReadOnlyList<long>>(importFileIds.Where(Changed.Contains).ToArray());
        }
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Prepared.Add(request);
            return PrepareFailure is null ? Task.CompletedTask : Task.FromException(PrepareFailure);
        }
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (PersistFailure is not null) return Task.FromException<ImportPersistenceResult>(PersistFailure);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, Status == "Imported" ? request.AcceptedImport.Staging.Rows.Count : 0)
            { Status = Status, AlreadyPresentRows = Status == "Imported" ? 0 : request.AcceptedImport.Staging.Rows.Count, ConflictRows = Conflicts,
              Evidence = Evidence });
        }
        public Exception? OutcomeFailure { get; init; }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            OutcomeFailure is null ? Task.FromResult(new ImportRowOutcome(0, 0, 0, 0)) : Task.FromException<ImportRowOutcome>(OutcomeFailure);
    }
}
