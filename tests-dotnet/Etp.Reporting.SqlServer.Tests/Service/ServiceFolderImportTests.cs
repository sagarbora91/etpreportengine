using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.TestSupport.Service;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// The Service folder import end to end on L0's synthetic fixtures (tests-dotnet/fixtures/service-interim, through
/// TestSupport/Service/ServiceFixtures), with a fake persistence and the real Open XML reader and L1's S catalogue.
/// </summary>
public sealed class ServiceFolderImportTests
{
    private static readonly DateOnly Week1 = ServiceFixtures.Week1SnapshotDate;

    [Fact]
    public async Task Week1_lands_35_families_under_AW330_dated_by_the_folder_and_reports_the_rest_not_needed()
    {
        var persistence = new ServicePersistence();
        var summary = await new FolderImportService(persistence).RunAsync(ServiceFixtures.Week1Folder, new("tester"));

        var imported = summary.Files.Where(file => file.Status == "Imported").ToArray();
        Assert.Equal(35, imported.Length);
        Assert.Equal(ServiceFixtures.ExpectedImported, imported.Select(file => file.ReportCode!).Order(StringComparer.Ordinal));
        Assert.Equal(ServiceInterimFamilies.Importable.Except(["S041"]).Order(StringComparer.Ordinal), ServiceFixtures.ExpectedImported); // S041 GPRC CLAIM is raw only
        Assert.All(imported, file =>
        {
            Assert.Equal("AW330", file.StoreCode);
            Assert.Equal(Week1, file.PeriodEnd);
            Assert.Contains(file.Diagnostics!, issue => issue.Code == ImportCodes.SnapshotDateFromFolder);
            Assert.DoesNotContain(file.Diagnostics!, issue => issue.Code == ServiceInterimFamilies.Codes.ServiceSnapshotDateDiffersFromHistory);
        });
        Assert.Equal(35, persistence.Requests.Count);
        Assert.All(persistence.Requests, request => Assert.Equal("AW330", request.ExpectedStoreCode));

        var notNeeded = summary.Files.Where(file => file.Status == "Not needed").ToArray();
        Assert.Equal(ServiceFixtures.ExpectedNotNeeded.Select(item => item.File).Order(StringComparer.Ordinal),
            notNeeded.Select(file => file.FileName).Order(StringComparer.Ordinal));
        foreach (var (name, code) in ServiceFixtures.ExpectedNotNeeded.Where(item => item.Code is not null))
            Assert.Equal(code, Assert.Single(Assert.Single(notNeeded, file => file.FileName == name).Diagnostics!).Code);
        Assert.Equal(ServiceFixtures.ControlFileName, Assert.Single(notNeeded, file => file.FileName.StartsWith("00_", StringComparison.Ordinal)).FileName);
        Assert.Equal(ImportCodes.FamilyDerived, Reason(notNeeded, "S001"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyNotNeeded, Reason(notNeeded, "S005"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyNotNeeded, Reason(notNeeded, "S038"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyDeferred, Reason(notNeeded, "S027"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyDeferred, Reason(notNeeded, "S028"));
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.UnknownLayouts);
        Assert.Empty(AutomatedOperationsService.ImportedDates(summary));
    }

    [Fact]
    public async Task Week1_as_a_watch_folder_zip_has_no_unknown_layout_and_routes_as_processed()
    {
        // Review item 6: AutomatedOperationsService counts Unknown layouts as failures, so the week1 ZIP (S038 header
        // only, the 00_ control file) must come out Not needed by name, never Unknown layout.
        var root = Path.Combine(Path.GetTempPath(), "EtpServiceZip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var zip = Path.Combine(root, ServiceFixtures.Week1FolderName + ".zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(ServiceFixtures.Week1Folder, zip, System.IO.Compression.CompressionLevel.Fastest, includeBaseDirectory: false);
            var persistence = new ServicePersistence();
            var summary = await new FolderImportService(persistence).RunAsync(zip, new("tester"));

            Assert.Equal(0, summary.UnknownLayouts);
            Assert.Equal(0, summary.Failed);
            Assert.Equal(35, summary.Imported);
            Assert.Equal(ServiceFixtures.ExpectedNotNeeded.Count, summary.Files.Count(file => file.Status == "Not needed"));
            Assert.All(summary.Files.Where(file => file.Status == "Imported"), file => Assert.Equal(Week1, file.PeriodEnd));
            Assert.Equal(AutomationSourceRoute.Processed, AutomatedOperationsService.RouteOf(summary));
            Assert.Empty(AutomatedOperationsService.ImportedDates(summary));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task S011_and_S013_take_AW330_from_their_siblings()
    {
        var summary = await new FolderImportService(new ServicePersistence()).RunAsync(ServiceFixtures.Week1Folder, new("tester"));
        foreach (var code in new[] { "S011", "S013" })
        {
            var file = Assert.Single(summary.Files, file => file.ReportCode == code);
            Assert.Equal("Imported", file.Status);
            Assert.Equal("AW330", file.StoreCode);
            Assert.DoesNotContain(file.Diagnostics!, issue => issue.Code == ServiceInterimFamilies.Codes.ServiceStoreDefaulted);
        }
    }

    [Fact]
    public async Task A_lone_S011_defaults_to_AW330_with_a_note()
    {
        var path = Directory.GetFiles(ServiceFixtures.Week1Folder, "S011_*.xlsx").Single();
        var file = Assert.Single((await new FolderImportService(new ServicePersistence()).RunFilesAsync([path], new("tester"))).Files);
        Assert.Equal("Imported", file.Status);
        Assert.Equal("AW330", file.StoreCode);
        var note = Assert.Single(file.Diagnostics!, issue => issue.Code == ServiceInterimFamilies.Codes.ServiceStoreDefaulted);
        Assert.Equal(ImportIssueSeverity.Information, note.Severity);
    }

    [Fact]
    public async Task An_undated_service_folder_says_how_to_date_it()
    {
        var persistence = new ServicePersistence();
        var summary = await new FolderImportService(persistence).RunAsync(ServiceFixtures.UndatedFolder, new("tester"));
        Assert.Equal(2, summary.Files.Count);
        Assert.All(summary.Files, file =>
        {
            Assert.Equal("Failed", file.Status);
            Assert.Equal(ServiceInterimFamilies.Codes.ServiceSnapshotDateNeeded, file.Failure!.Code);
            Assert.Equal(ServiceRouting.DateNeededMessage, file.Message);
        });
        Assert.Empty(persistence.Requests);
    }

    [Fact]
    public async Task A_service_file_refused_by_its_own_coverage_keeps_the_real_reason()
    {
        // S009 in a dated folder whose Info sheet states two Coverage dates: tier 5 refuses before the folder is read, so
        // neither a dated folder nor the date override can help, and the Coverage conflict must stay the message.
        var path = Path.Combine("Service Centre till 28 sep 2026", "S009_PendingRepair.xlsx");
        var persistence = new ServicePersistence();
        var file = Assert.Single((await new FolderImportService(persistence, new Reader(_ => S009(path, coverage: ["21-Sep-2026", "28-Sep-2026"])))
            .RunFilesAsync([path], new("tester"))).Files);
        Assert.Equal("Failed", file.Status);
        Assert.NotEqual(ServiceInterimFamilies.Codes.ServiceSnapshotDateNeeded, file.Failure!.Code);
        Assert.DoesNotContain("folder whose name ends with the date", file.Message);
        Assert.Contains(file.Diagnostics!, issue => issue.Code == ImportCodes.SnapshotDateAmbiguous);
        Assert.Empty(persistence.Requests);
    }

    [Fact]
    public async Task An_undated_service_file_never_takes_a_retail_siblings_date()
    {
        var folder = "Mixed exports";
        var retail = Path.Combine(folder, "HEMW_R025_20260825.xlsx");
        var service = Path.Combine(folder, "S009_PendingRepair.xlsx");
        var persistence = new ServicePersistence();
        var summary = await new FolderImportService(persistence, new Reader(path => path == retail ? Sales(path) : S009(path, coverage: [])))
            .RunFilesAsync([retail, service], new("tester"));

        Assert.Equal("Imported", Assert.Single(summary.Files, file => file.ReportCode == "R025").Status);
        var undated = Assert.Single(summary.Files, file => file.FileName == Path.GetFileName(service));
        Assert.Equal("Failed", undated.Status);
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceSnapshotDateNeeded, undated.Failure!.Code);
        Assert.DoesNotContain(undated.Diagnostics ?? [], issue => issue.Code == ImportCodes.SnapshotDateFromSiblings);
        Assert.Single(persistence.Requests);
    }

    [Fact]
    public void Automation_packs_only_the_retail_dates_of_a_mixed_batch()
    {
        var retail = new DateOnly(2026, 9, 27);
        var service = Week1;
        var batch = new FolderImportSummary([new("R025.xlsx", "R025", "HEMW", retail, retail, "Imported"),
            new("S009_PendingRepair.xlsx", "S009", "AW330", service, service, "Imported"),
            new("S002_JobReportBooking.xlsx", "S002", "AW330", service, service, "Imported")]);
        Assert.Equal(new[] { retail }, AutomatedOperationsService.ImportedDates(batch));
        var serviceOnly = new FolderImportSummary(batch.Files.Skip(1).ToArray());
        Assert.Empty(AutomatedOperationsService.ImportedDates(serviceOnly));
    }

    private static WorkbookSnapshot S009(string path, string[] coverage)
    {
        var family = EtpReportFamilyRegistry.Resolve("S009");
        var data = new WorkbookSheet("Data", 1, family.Headers,
            [new(2, family.Headers.Select((_, index) => new WorkbookCell(index == 0 ? "JOAW330SYN0001" : null)).ToArray())]);
        var info = new WorkbookSheet("Info", 1, ["Key", "Value"],
            coverage.Select((value, index) => new WorkbookRow(index + 2, [new WorkbookCell("Coverage"), new WorkbookCell(value)])).ToArray());
        return new(Path.GetFileName(path), 1, new string('e', 64), [data, info], path);
    }

    private static WorkbookSnapshot Sales(string path)
    {
        var values = new Dictionary<string, object?>
        {
            ["TRANS_TYPE"] = "INV", ["STORE CODE"] = "HEMW", ["ITEMNUMBER"] = "TEST-001", ["INVNUMBER"] = "100000001",
            ["INVDATE"] = 20260825, ["QTY"] = 1m, ["NETAMOUNT"] = 118m, ["NETVALUE"] = 100m, ["TAX"] = 18m
        };
        return new(Path.GetFileName(path), 1, new string('f', 64), [new("SDB VariantwiseSales", 1, RetailSalesProfiles.R025Headers,
            [new(2, RetailSalesProfiles.R025Headers.Select(header => new WorkbookCell(values.GetValueOrDefault(header))).ToArray())])], path);
    }

    [Fact]
    public async Task The_raw_pack_imports_its_nine_families_and_reports_S005_and_S028_not_needed()
    {
        // Lane L9: the synthetic raw exports (CSV and xlsx, dated by the window end in each name) through the real folder
        // import. The raw S005 and S028 are recognised by header, so L3's gate reports them Not needed, not Unknown layout.
        // The GPRC CLAIM file has the 34 real headers, so it imports as S041 (lane L10, decision 16).
        var persistence = new ServicePersistence();
        var summary = await new FolderImportService(persistence).RunAsync(RawFolder(), new("tester"));

        var imported = summary.Files.Where(file => file.Status == "Imported").ToArray();
        Assert.Equal(["S002", "S003", "S004", "S009", "S010", "S018", "S022", "S031", "S041"],
            imported.Select(file => file.ReportCode!).Order(StringComparer.Ordinal));
        Assert.All(imported, file =>
        {
            Assert.Equal("AW330", file.StoreCode);
            Assert.Equal(new DateOnly(2026, 10, 9), file.PeriodEnd);
        });
        var notNeeded = summary.Files.Where(file => file.Status == "Not needed").ToArray();
        Assert.Equal(["S005", "S028"], notNeeded.Select(file => file.ReportCode!).Order(StringComparer.Ordinal));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyNotNeeded, Reason(notNeeded, "S005"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyDeferred, Reason(notNeeded, "S028"));
        Assert.Equal("Imported", Assert.Single(summary.Files, file => file.FileName.StartsWith("GPRC CLAIM", StringComparison.Ordinal)).Status);
        Assert.Equal(11, summary.Files.Count);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(9, persistence.Requests.Count);
    }

    private static string RawFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests-dotnet", "fixtures", "service-interim", "raw");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests-dotnet/fixtures/service-interim/raw was not found above the test output folder.");
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }

    private static string? Reason(IEnumerable<FolderImportFileResult> files, string code) =>
        Assert.Single(Assert.Single(files, file => file.ReportCode == code).Diagnostics!).Code;

    private sealed class ServicePersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public List<ImportPersistenceRequest<MatchedImportEnvelope>> Requests { get; } = [];
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsInScopeAsync(string hash, string report, string store, DateOnly start, DateOnly end, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task<IReadOnlyList<RestatementCandidate>> FindRestatementCandidatesAsync(string report, string store, DateOnly start, DateOnly end,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RestatementCandidate>>([]);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, request.AcceptedImport.Staging.Rows.Count));
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
    }
}
