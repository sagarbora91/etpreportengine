using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests.Service;

/// <summary>
/// The Service folder import end to end on L0's synthetic fixtures (tests-dotnet/fixtures/service-interim), with a fake
/// persistence and the real Open XML reader. These need L0's fixtures (L0b) and L1's S catalogue entries, which are not
/// on this branch yet (SERVICE-LANES.md section 2: L3 "finishes after L1 merges"); remove the Skip when pulling them.
/// </summary>
public sealed class ServiceFolderImportTests
{
    private const string WaitsForL0bAndL1 = "Waits for L0b fixtures and L1 S catalogue on feature/service-interim; remove on pull.";
    private static readonly DateOnly Week1 = new(2026, 9, 28);
    private static readonly string[] NotNeededCodes = ["S001", "S005", "S027", "S028", "S038"];

    [Fact(Skip = WaitsForL0bAndL1)]
    public async Task Week1_lands_35_families_under_AW330_dated_by_the_folder_and_reports_the_rest_not_needed()
    {
        var persistence = new ServicePersistence();
        var summary = await new FolderImportService(persistence).RunAsync(ServiceFixtureFolders.Week1, new("tester"));

        var imported = summary.Files.Where(file => file.Status == "Imported").ToArray();
        Assert.Equal(35, imported.Length);
        Assert.Equal(ServiceInterimFamilies.Importable.Order(), imported.Select(file => file.ReportCode!).Order());
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
        Assert.Equal(6, notNeeded.Length);
        Assert.Equal(NotNeededCodes, notNeeded.Where(file => file.ReportCode is not null).Select(file => file.ReportCode!).Order());
        Assert.Single(notNeeded, file => file.FileName.StartsWith("00_", StringComparison.Ordinal));
        Assert.Equal(ImportCodes.FamilyDerived, Reason(notNeeded, "S001"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyNotNeeded, Reason(notNeeded, "S005"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyNotNeeded, Reason(notNeeded, "S038"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyDeferred, Reason(notNeeded, "S027"));
        Assert.Equal(ServiceInterimFamilies.Codes.ServiceFamilyDeferred, Reason(notNeeded, "S028"));
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.UnknownLayouts);
        Assert.Empty(AutomatedOperationsService.ImportedDates(summary));
    }

    [Fact(Skip = WaitsForL0bAndL1)]
    public async Task S011_and_S013_take_AW330_from_their_siblings()
    {
        var summary = await new FolderImportService(new ServicePersistence()).RunAsync(ServiceFixtureFolders.Week1, new("tester"));
        foreach (var code in new[] { "S011", "S013" })
        {
            var file = Assert.Single(summary.Files, file => file.ReportCode == code);
            Assert.Equal("Imported", file.Status);
            Assert.Equal("AW330", file.StoreCode);
            Assert.DoesNotContain(file.Diagnostics!, issue => issue.Code == ServiceInterimFamilies.Codes.ServiceStoreDefaulted);
        }
    }

    [Fact(Skip = WaitsForL0bAndL1)]
    public async Task A_lone_S011_defaults_to_AW330_with_a_note()
    {
        var path = Directory.GetFiles(ServiceFixtureFolders.Week1, "S011_*.xlsx").Single();
        var file = Assert.Single((await new FolderImportService(new ServicePersistence()).RunFilesAsync([path], new("tester"))).Files);
        Assert.Equal("Imported", file.Status);
        Assert.Equal("AW330", file.StoreCode);
        var note = Assert.Single(file.Diagnostics!, issue => issue.Code == ServiceInterimFamilies.Codes.ServiceStoreDefaulted);
        Assert.Equal(ImportIssueSeverity.Information, note.Severity);
    }

    [Fact(Skip = WaitsForL0bAndL1)]
    public async Task An_undated_service_folder_says_how_to_date_it()
    {
        var persistence = new ServicePersistence();
        var summary = await new FolderImportService(persistence).RunAsync(ServiceFixtureFolders.Undated, new("tester"));
        Assert.Equal(2, summary.Files.Count);
        Assert.All(summary.Files, file =>
        {
            Assert.Equal("Failed", file.Status);
            Assert.Equal(ServiceInterimFamilies.Codes.ServiceSnapshotDateNeeded, file.Failure!.Code);
            Assert.Equal(ServiceRouting.DateNeededMessage, file.Message);
        });
        Assert.Empty(persistence.Requests);
    }

    [Fact(Skip = WaitsForL0bAndL1)]
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

/// <summary>
/// The L0 fixture folders (SERVICE-LANES.md 4.1 step 9), found from the test output folder up to the repository. L0's
/// TestSupport/Service/ServiceFixtures.cs replaces this when it lands.
/// </summary>
internal static class ServiceFixtureFolders
{
    public static string Root => Find(Path.Combine("tests-dotnet", "fixtures", "service-interim"));
    public static string Week1 => Path.Combine(Root, "week1", "Service Centre till 28 sep 2026");
    public static string Week2 => Path.Combine(Root, "week2", "Service Centre till 05 oct 2026");
    public static string Undated => Path.Combine(Root, "undated", "Service Centre");
    public static string SameDateChanged => Path.Combine(Root, "samedate-changed", "Service Centre till 28 sep 2026");

    private static string Find(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException($"The Service fixtures ({relative}) were not found above the test output folder.");
    }
}
