using Etp.Reporting.Application.Imports;
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

    [Fact]
    public async Task Empty_export_evidence_uses_the_detected_sibling_scope()
    {
        var retained = new List<(string Path, string Store, DateOnly Date)>();
        var service = new FolderImportService(new CapturePersistence(), new Reader(path => Sales(path, "HEMW", path == "empty.xlsx" ? [] : [20260825])),
            (path, _, store, date, _) => { retained.Add((path, store, date)); return Task.CompletedTask; });
        await service.RunFilesAsync(["empty.xlsx", "sales.xlsx"], new("tester"));
        Assert.Contains(retained, value => value.Path == "empty.xlsx" && value.Store == "HEMW" && value.Date == new DateOnly(2026, 8, 25));
    }

    [Fact]
    public async Task Duplicate_retry_repairs_a_failed_original_document_copy_without_new_data()
    {
        var attempts = 0;
        Task Retain(string path, MatchedImportEnvelope envelope, string store, DateOnly date, CancellationToken token)
        {
            if (++attempts == 1) throw new IOException("Temporary copy failure.");
            return Task.CompletedTask;
        }
        var reader = new Reader(path => Sales(path, "HEMW", [20260825]));
        var first = await new FolderImportService(new CapturePersistence(), reader, Retain).RunFilesAsync(["sales.xlsx"], new("tester"));
        Assert.Contains("could not be retained", Assert.Single(first.Files).Message);
        Assert.Equal("Imported", first.Files[0].Status);
        var duplicatePersistence = new CapturePersistence { Exists = true };
        var second = await new FolderImportService(duplicatePersistence, reader, Retain).RunFilesAsync(["sales.xlsx"], new("tester"));
        Assert.Equal(2, attempts);
        Assert.Equal("Duplicate", Assert.Single(second.Files).Status);
        Assert.DoesNotContain("could not be retained", second.Files[0].Message);
        Assert.Empty(duplicatePersistence.Requests);
        Assert.Equal(0, second.NewRows);
    }

    [Theory]
    [InlineData("Duplicate content")]
    [InlineData("Already present")]
    public async Task Content_duplicates_and_subsets_retain_their_original_document(string status)
    {
        var retained = 0;
        var summary = await new FolderImportService(new CapturePersistence { Status = status },
            new Reader(path => Sales(path, "HEMW", [20260825])),
            (_, _, _, _, _) => { retained++; return Task.CompletedTask; }).RunFilesAsync(["sales.xlsx"], new("tester"));
        Assert.Equal(status, Assert.Single(summary.Files).Status);
        Assert.Equal(1, retained);
        Assert.Equal(0, summary.NewRows);
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
    private sealed class CapturePersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public string Status { get; init; } = "Imported";
        public bool Exists { get; init; }
        public int Conflicts { get; init; }
        public (string Store, DateOnly Start, DateOnly End)? ExactScope { get; init; }
        public List<ImportPersistenceRequest<MatchedImportEnvelope>> Requests { get; } = [];
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(Exists);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(Exists && (ExactScope is null || ExactScope == (store, start, end)));
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, Status == "Imported" ? request.AcceptedImport.Staging.Rows.Count : 0)
            { Status = Status, AlreadyPresentRows = Status == "Imported" ? 0 : request.AcceptedImport.Staging.Rows.Count, ConflictRows = Conflicts });
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
    }
}
