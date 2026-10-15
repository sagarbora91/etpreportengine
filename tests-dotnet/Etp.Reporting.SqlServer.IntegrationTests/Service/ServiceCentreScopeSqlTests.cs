using Etp.Reporting.Application.Stores;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// 1.12.1 lane P1 (cross-layer C# -> SQL): the Service screens filter per Service Centre. Against a fixture database built by
/// every migration and the object files, with a second synthetic centre SC999 added through C# and a third, SC_97, a
/// SERVICE-unit store with no saved prefix and a code that is not plain A-Z0-9 (review m-1). One synthetic S009 holds a job
/// of each centre and one that matches no prefix. The scoped query reads exactly the jobs v_service_job gives that centre,
/// the C# catalogue places every job where the view does, and a '_' in a fallback prefix is not a wildcard.
/// All values are synthetic (stores SC999 / SC_97, job numbers ...SYN...).
/// </summary>
public sealed class ServiceCentreScopeSqlTests
{
    private const string SecondCode = "SC999";
    private const string SecondPrefix = "JOSC999";
    private const string ThirdCode = "SC_97";
    private const string DatedFolder = "Service Centre till 28 sep 2026";

    [SqlFact]
    public async Task A_scoped_query_reads_only_its_centres_jobs_and_the_catalogue_places_jobs_as_the_view_does()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var catalogue = new SqlServerServiceCentreCatalog(new EtpConnectionFactory(database.ConnectionString));
            var seeded = Assert.Single(await catalogue.LoadAsync());
            await new StoreCatalogRepository(database.ConnectionString)
                .SaveServiceCentreAsync(SecondCode, "Synthetic Service Centre", SecondPrefix, "Second Service Centre (synthetic)");
            // Review m-1: a Service Centre store with no saved prefix whose code 0067 would not have prefixed (it has a '_').
            await database.ExecuteAsync($"""
                INSERT dbo.stores(store_code,store_name,business_unit_id,is_active,service_job_prefix,modified_by,modified_utc,change_reason)
                SELECT '{ThirdCode}',N'Synthetic Service Centre 97',business_unit_id,0,NULL,SUSER_SNAME(),SYSUTCDATETIME(),N'Synthetic centre without a prefix'
                FROM dbo.business_units WHERE business_unit_code='SERVICE';
                """);

            // The catalogue gives SC_97 the view's fallback prefix: JO and the code, upper case.
            var centres = await catalogue.LoadAsync();
            Assert.Equal(3, centres.Count);
            Assert.Equal("JOSC_97", Assert.Single(centres, centre => centre.StoreCode == ThirdCode).JobNumberPrefix);

            var seededJob = seeded.JobNumberPrefix + "SYN0002";
            var secondJob = SecondPrefix + "SYN0001";
            var thirdJob = "JOSC_97SYN0004";
            var wildcardJob = "JOSCX97SYN0005"; // would match 'JOSC_97%' under LIKE; matches no prefix as a plain starts-with
            var unmatchedJob = "ZZSYN0003";
            string[] jobs = [seededJob, secondJob, thirdJob, wildcardJob, unmatchedJob];
            var path = Path.Combine(DatedFolder, "S009_PendingRepair.xlsx");
            var service = new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString),
                new Reader(_ => S009(path, SecondCode, jobs)));
            Assert.Equal("Imported", Assert.Single((await service.RunFilesAsync([path], new("Synthetic Owner"))).Files).Status);

            // The view's centre of each job, and the C# catalogue's (ServiceCentreRules.CentreForJob) agree.
            foreach (var job in jobs)
            {
                var viewCentre = await database.ExecuteAsync($"SELECT service_store_code FROM dbo.v_service_job WHERE job_order_number=N'{job}'");
                var csharpCentre = ServiceCentreRules.CentreForJob(centres, job)?.StoreCode;
                Assert.Equal(csharpCentre, viewCentre is DBNull or null ? null : (string)viewCentre);
            }
            Assert.Equal(ThirdCode, await database.ExecuteAsync($"SELECT service_store_code FROM dbo.v_service_job WHERE job_order_number=N'{thirdJob}'"));
            Assert.Equal(ThirdCode, await database.ExecuteAsync($"SELECT pending_at FROM dbo.v_service_job WHERE job_order_number=N'{thirdJob}'"));
            Assert.IsType<DBNull>(await database.ExecuteAsync($"SELECT service_store_code FROM dbo.v_service_job WHERE job_order_number=N'{wildcardJob}'"));
            Assert.IsType<DBNull>(await database.ExecuteAsync($"SELECT service_store_code FROM dbo.v_service_job WHERE job_order_number=N'{unmatchedJob}'"));

            // Unscoped: every job. Scoped: exactly that centre's jobs, on the Jobs list and the Pending board.
            SqlServerServiceReportQuery.ForgetJobRows();
            var all = new SqlServerServiceReportQuery(database.ConnectionString);
            Assert.Null(all.ServiceStoreCode);
            Assert.Equal(jobs.Order(StringComparer.Ordinal), (await all.LoadJobListAsync()).Select(job => job.JobOrderNumber).Order(StringComparer.Ordinal));
            foreach (var (code, expected) in new[] { (seeded.StoreCode, seededJob), (SecondCode, secondJob), (ThirdCode, thirdJob) })
            {
                var scoped = all.ForServiceCentre(code);
                Assert.Equal(code, scoped.ServiceStoreCode);
                Assert.Equal([expected], (await scoped.LoadJobListAsync()).Select(job => job.JobOrderNumber));
                Assert.Equal(Convert.ToInt32(await database.ExecuteAsync(
                        $"SELECT COUNT(*) FROM dbo.v_service_job WHERE is_open=1 AND service_store_code='{code}'")),
                    (await scoped.LoadPendingBoardAsync()).OpenJobs);
            }
            // The unscoped rows are still the full set after the scoped reads (the cache keys differ).
            Assert.Equal(jobs.Length, (await all.LoadJobListAsync()).Count);
            Assert.Equal(jobs.Length, (await all.ForServiceCentre("  ").LoadJobListAsync()).Count);
            // A store code that is not a Service Centre reads nothing.
            Assert.Empty(await all.ForServiceCentre("SYNNONE").LoadJobListAsync());
            // The claims of a scoped query are the claim lines of its jobs (none in this fixture).
            Assert.Empty((await all.ForServiceCentre(SecondCode).LoadClaimsAsync(null, null)).Lines);
        }
        finally
        {
            SqlServerServiceReportQuery.ForgetJobRows();
            await database.DisposeAsync();
        }
    }

    private static WorkbookSnapshot S009(string path, string storeCode, IReadOnlyList<string> jobNumbers)
    {
        var family = EtpReportFamilyRegistry.Resolve("S009");
        var rows = jobNumbers.Select((job, index) => new WorkbookRow(index + 2, family.Headers.Select(header => new WorkbookCell(header switch
        {
            "JONumber" => job,
            "StoreCode" => storeCode,
            "Brand" => "Sample Brand",
            "JOStatus" => "Pending_Repair",
            _ => null
        })).ToArray())).ToArray();
        var data = new WorkbookSheet("Data", 1, family.Headers, rows);
        var info = new WorkbookSheet("Info", 1, ["Key", "Value"], []);
        return new(Path.GetFileName(path), 1, new string('d', 64), [data, info], path);
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }
}
