using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Application.Stores;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using Etp.Reporting.TestSupport.Service;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 wave 2, lane A4 (Appendix B item 2): the Service Centre is data. Against a real fixture database built by every
/// migration (0067 included) and the object files: the catalogue reads the seeded centre with its prefix; the Owner adds a
/// second, synthetic centre SC999 through C#; an SC999 Service file imports through the folder import and reads back
/// through the edited v_service_job under SC999; a Service file that names no store is refused in plain words once two
/// centres exist; the 0067 CHECK and triggers refuse a bad prefix; DB-06 returns six sub-results with cold and warm
/// numbers. All values are synthetic (store SC999, job numbers ...SYN...).
/// </summary>
public sealed class ServiceCentreCatalogSqlTests
{
    private const string SecondCode = "SC999";
    private const string SecondPrefix = "JOSC999";
    private const string SecondJob = "JOSC999SYN0001";
    private static readonly string DatedFolder = "Service Centre till 28 sep 2026";

    [SqlFact]
    public async Task SC999_is_added_imports_through_the_catalogue_and_shows_under_its_own_code_in_v_service_job()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var catalogue = new SqlServerServiceCentreCatalog(new EtpConnectionFactory(database.ConnectionString));

            // 0067: the 0048 unit is SERVICE and its store reads JO + its code; no Retail store is a centre.
            var seeded = Assert.Single(await catalogue.LoadAsync());
            Assert.Equal("JO" + seeded.StoreCode, seeded.JobNumberPrefix);
            Assert.Equal("SERVICE", await database.ExecuteAsync("SELECT unit_kind FROM dbo.business_units WHERE business_unit_code='SERVICE'"));
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.stores WHERE service_job_prefix IS NOT NULL AND business_unit_id IS NULL")));

            // The Owner adds a second Service Centre in Settings > Stores: inactive, in the SERVICE unit, audited.
            var stores = new StoreCatalogRepository(database.ConnectionString);
            var activeBefore = await stores.ActiveCodesAsync();
            var audits = Convert.ToInt32(await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.operational_audit WHERE safe_detail=N'Service Centre store changed'"));
            await stores.SaveServiceCentreAsync(SecondCode, "Synthetic Service Centre", SecondPrefix, "Second Service Centre (synthetic)");

            var centres = await catalogue.LoadAsync();
            Assert.Equal([seeded.StoreCode, SecondCode], centres.Select(centre => centre.StoreCode).Order(StringComparer.Ordinal));
            Assert.Equal(SecondPrefix, Assert.Single(centres, centre => centre.StoreCode == SecondCode).JobNumberPrefix);
            var entry = Assert.Single(await stores.LoadAsync(), store => store.Code == SecondCode);
            Assert.False(entry.IsActive);
            Assert.True(entry.IsServiceCentre);
            Assert.Equal("SERVICE", entry.UnitKind);
            Assert.Equal(SecondPrefix, entry.ServiceJobPrefix);
            Assert.Equal(activeBefore, await stores.ActiveCodesAsync());
            Assert.Equal(audits + 1, Convert.ToInt32(await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.operational_audit WHERE safe_detail=N'Service Centre store changed'")));

            // An SC999 pending-repair file names its store; it imports under SC999, dated by its folder.
            var path = Path.Combine(DatedFolder, "S009_PendingRepair.xlsx");
            var service = new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString), new Reader(_ => S009(path, SecondJob, SecondCode)));
            var file = Assert.Single((await service.RunFilesAsync([path], new("Synthetic Owner"))).Files);
            Assert.Equal("Imported", file.Status);
            Assert.Equal(SecondCode, file.StoreCode);
            Assert.Equal(new DateOnly(2026, 9, 28), file.PeriodEnd);

            // Read back through the edited view: the job belongs to SC999 by its prefix, and so does its pending place.
            Assert.Equal(SecondCode, await database.ExecuteAsync($"SELECT service_store_code FROM dbo.v_service_job WHERE job_order_number=N'{SecondJob}'"));
            Assert.Equal(SecondCode, await database.ExecuteAsync($"SELECT pending_at FROM dbo.v_service_job WHERE job_order_number=N'{SecondJob}'"));
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.daily_reporting_days WHERE store_code='SC999'")));
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task A_service_file_naming_no_store_goes_to_the_only_centre_and_is_refused_once_there_are_two()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var lone = Directory.GetFiles(ServiceFixtures.Week1Folder, "S011_*.xlsx").Single();
            var seeded = Assert.Single(await new SqlServerServiceCentreCatalog(database.ConnectionString).LoadAsync());

            // One centre: the folder import's own catalogue (from its SQL persistence) gives the file to it, with a note.
            var first = Assert.Single((await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunFilesAsync([lone], new("Synthetic Owner"))).Files);
            Assert.Equal("Imported", first.Status);
            Assert.Equal(seeded.StoreCode, first.StoreCode);
            Assert.Contains(first.Diagnostics!, issue => issue.Code == "SERVICE_STORE_DEFAULTED" && issue.Message == ServiceCentreRules.StoreDefaultedMessage);
            var files = Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));

            // Two centres: the same kind of file is refused in plain words, and nothing is written.
            await new StoreCatalogRepository(database.ConnectionString)
                .SaveServiceCentreAsync(SecondCode, "Synthetic Service Centre", SecondPrefix, "Second Service Centre (synthetic)");
            var refused = Assert.Single((await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunFilesAsync([lone], new("Synthetic Owner"))).Files);
            Assert.Equal("Failed", refused.Status);
            Assert.Equal(ServiceCentreRules.StoreAmbiguousCode, refused.Failure!.Code);
            Assert.Equal(ServiceCentreRules.StoreAmbiguousMessage, refused.Message);
            Assert.Equal(files, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files")));

            // The automation run's single-workbook path refuses the same way.
            var automation = await Assert.ThrowsAsync<Etp.Reporting.Import.Batch.ImportSourceException>(() =>
                ServiceCentreFallback.StoreForAsync(new SqlServerServiceCentreCatalog(database.ConnectionString), "S011"));
            Assert.Equal(ServiceCentreRules.StoreAmbiguousCode, automation.Code);
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task The_0067_check_and_triggers_refuse_a_bad_prefix_and_keep_the_0048_numbers()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var stores = new StoreCatalogRepository(database.ConnectionString);
            var seeded = Assert.Single(await new SqlServerServiceCentreCatalog(database.ConnectionString).LoadAsync());
            var count = Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.stores"));

            // The store editor checks the shape before anything is sent; the CHECK refuses the same shape by hand.
            var shape = await Assert.ThrowsAsync<ArgumentException>(() => stores.SaveServiceCentreAsync("SC998", "Synthetic", "jo-x", "Synthetic"));
            Assert.Equal(ServiceCentreStores.PrefixInvalidMessage, shape.Message);
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                $"UPDATE dbo.stores SET service_job_prefix='JO-X' WHERE store_code='{seeded.StoreCode}'"))).Number);
            Assert.Equal(547, (await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                $"UPDATE dbo.stores SET service_job_prefix='jo{seeded.StoreCode}' WHERE store_code='{seeded.StoreCode}'"))).Number);

            // 51971: a prefix that begins with (or is the start of) another centre's.
            var overlap = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                stores.SaveServiceCentreAsync("SC998", "Synthetic", seeded.JobNumberPrefix[..4], "Synthetic"));
            Assert.Equal(ServiceCentreStores.PrefixOverlapMessage, overlap.Message);
            Assert.Contains(Assert.IsType<SqlException>(overlap.InnerException).Errors.Cast<SqlError>(), error => error.Number == 51971);

            // 51975: a shop store's code cannot become a Service Centre; 51970: a shop store holds no prefix.
            var shop = (await stores.ActiveCodesAsync()).First();
            var shopRefusal = await Assert.ThrowsAsync<InvalidOperationException>(() => stores.SaveServiceCentreAsync(shop, "Synthetic", "JOSYN1", "Synthetic"));
            Assert.Equal(ServiceCentreStores.CodeIsShopStoreMessage, shopRefusal.Message);
            Assert.Equal(51970, (await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                $"UPDATE dbo.stores SET service_job_prefix='JOSYN1' WHERE store_code='{shop}'"))).Number);
            Assert.Equal(count, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.stores")));

            // 51973: a unit with stores keeps its kind.
            Assert.Equal(51973, (await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                "UPDATE dbo.business_units SET unit_kind='RETAIL' WHERE business_unit_code='SERVICE'"))).Number);

            // 0048's refusals keep their numbers beside the 0067 trigger.
            Assert.Equal(51900, (await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                $"UPDATE dbo.stores SET is_active=1 WHERE store_code='{seeded.StoreCode}'"))).Number);
            Assert.Equal(51904, (await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                $"UPDATE dbo.stores SET business_unit_id=NULL WHERE store_code='{seeded.StoreCode}'"))).Number);
            Assert.Equal(seeded.JobNumberPrefix, await database.ExecuteAsync($"SELECT service_job_prefix FROM dbo.stores WHERE store_code='{seeded.StoreCode}'"));
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task DB06_returns_six_sub_results_with_cold_and_warm_numbers()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var context = new HealthCheckContext(new DateOnly(2026, 9, 28), HealthCheckTrigger.Manual, DateTimeOffset.UtcNow, [], null,
                HealthCheckBudget.Default, new Dictionary<string, object>());

            var result = Assert.Single(await new ServiceScreenTimingProbe(database.ConnectionString).RunAsync(context, CancellationToken.None));

            Assert.Equal(HealthCheckId.Db06, result.CheckId);
            Assert.Equal(["JOBS", "PENDING", "STATUS", "CLAIMS", "PARTS", "JOB_HISTORY"], result.SubResults!.Select(sub => sub.Key));
            Assert.All(result.SubResults!, sub =>
            {
                Assert.NotEqual(HealthOutcome.Skipped, sub.Outcome);
                Assert.True(int.Parse(sub.Fields![ServiceScreenTimingProbe.ColdField], System.Globalization.CultureInfo.InvariantCulture) >= 0);
                Assert.True(int.Parse(sub.Fields[ServiceScreenTimingProbe.WarmField], System.Globalization.CultureInfo.InvariantCulture) >= 0);
            });
            // An empty Service read model answers a repeat count well under a second.
            Assert.Equal(HealthOutcome.Pass, result.Outcome);
            Assert.Contains("first read after start took", result.Explanation, StringComparison.Ordinal);
            Assert.Contains("repeat read", result.Explanation, StringComparison.Ordinal);
        }
        finally { await database.DisposeAsync(); }
    }

    private static WorkbookSnapshot S009(string path, string jobNumber, string storeCode)
    {
        var family = EtpReportFamilyRegistry.Resolve("S009");
        var data = new WorkbookSheet("Data", 1, family.Headers,
            [new(2, family.Headers.Select(header => new WorkbookCell(header switch
            {
                "JONumber" => jobNumber,
                "StoreCode" => storeCode,
                "Brand" => "Sample Brand",
                "JOStatus" => "Pending_Repair",
                _ => null
            })).ToArray())]);
        var info = new WorkbookSheet("Info", 1, ["Key", "Value"], []);
        return new(Path.GetFileName(path), 1, new string('c', 64), [data, info], path);
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }
}
