using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Application.Stores;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// 1.12.1 lane P8 (cross-layer C# -> SQL): the Service Centre filter reaches the parts invoices and the closing stock, and
/// "Not matched to any centre" holds what names no centre. Against a fixture database built by every migration and the object
/// files, with the week-1 synthetic Service fixture imported, a second synthetic centre SC999 added through C#, and the
/// store_code of every S006 and S007 landing row rewritten to the seeded centre, SC999 or a code that is no centre (by row /
/// invoice, in thirds). The two views must give each bucket exactly the rows the landing tables hold, the buckets must add up
/// to the all-centres views, and (once P8's ScopedSql is wired into the query's ReadAsync, see the P8 NOTE) the scoped query's
/// Parts and Jobs add up the same way: seeded centre + SC999 + Not matched = All Service Centres. Synthetic data only.
/// </summary>
public sealed class ServiceCentreMoreSqlTests
{
    private const string SecondCode = "SC999";
    private const string NoCentreCode = "ZZSYNNONE";

    [SqlFact]
    public async Task Parts_and_stock_are_placed_per_centre_and_the_buckets_add_up_to_all_centres()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var catalogue = new SqlServerServiceCentreCatalog(new EtpConnectionFactory(database.ConnectionString));
            var seeded = Assert.Single(await catalogue.LoadAsync()).StoreCode;
            await new StoreCatalogRepository(database.ConnectionString)
                .SaveServiceCentreAsync(SecondCode, "Synthetic Service Centre", "JOSC999", "Second Service Centre (synthetic)");
            var summary = await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunAsync(ServiceFixtures.Week1Folder, new("Synthetic Owner"));
            Assert.NotEmpty(summary.Files);

            // Thirds: the seeded centre, SC999, and a code that is no Service Centre (so: Not matched).
            await database.ExecuteAsync($"""
                UPDATE dbo.etp_landing_s006 SET store_code=CASE etp_row_id % 3 WHEN 0 THEN N'{seeded}' WHEN 1 THEN N'{SecondCode}' ELSE N'{NoCentreCode}' END;
                UPDATE dbo.etp_landing_s007 SET store_code=CASE ABS(CHECKSUM(invoice_number)) % 3 WHEN 0 THEN N'{seeded}' WHEN 1 THEN N' {SecondCode.ToLowerInvariant()} ' ELSE N'{NoCentreCode}' END;
                """);

            // Stock: each bucket equals the S006 rows of the latest reading that name it; the buckets add up to the all-centres view.
            var file = await IntAsync(database, "SELECT MAX(import_file_id) FROM dbo.v_service_readings WHERE report_code='S006' AND is_latest=1");
            Assert.True(file > 0, "The fixture imports an S006 reading.");
            var inSeeded = await IntAsync(database, $"SELECT COUNT(*) FROM dbo.etp_landing_s006 WHERE import_file_id={file} AND store_code=N'{seeded}'");
            var inSecond = await IntAsync(database, $"SELECT COUNT(*) FROM dbo.etp_landing_s006 WHERE import_file_id={file} AND store_code=N'{SecondCode}'");
            var inNone = await IntAsync(database, $"SELECT COUNT(*) FROM dbo.etp_landing_s006 WHERE import_file_id={file} AND store_code=N'{NoCentreCode}'");
            Assert.True(inSeeded > 0 && inSecond > 0 && inNone > 0, "Every bucket holds stock rows.");
            Assert.Equal(inSeeded, await IntAsync(database, $"SELECT ISNULL(SUM(items),0) FROM dbo.v_service_stock_summary_centre WHERE service_store_code=N'{seeded}'"));
            Assert.Equal(inSecond, await IntAsync(database, $"SELECT ISNULL(SUM(items),0) FROM dbo.v_service_stock_summary_centre WHERE service_store_code=N'{SecondCode}'"));
            Assert.Equal(inNone, await IntAsync(database, "SELECT ISNULL(SUM(items),0) FROM dbo.v_service_stock_summary_centre WHERE service_store_code IS NULL"));
            Assert.Equal(await IntAsync(database, "SELECT ISNULL(SUM(items),0) FROM dbo.v_service_stock_summary"),
                await IntAsync(database, "SELECT ISNULL(SUM(items),0) FROM dbo.v_service_stock_summary_centre"));

            // Parts: one centre per (invoice, item); the buckets add up to the invoice-and-item lines of the created (S007) readings.
            var partsCentreLines = await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_parts_centre");
            Assert.True(partsCentreLines > 0, "The fixture has S007 invoices.");
            Assert.Equal(partsCentreLines, await IntAsync(database, "SELECT COUNT(*) FROM (SELECT DISTINCT invoice_number,item_id FROM dbo.v_service_parts_centre) x"));
            Assert.Equal(partsCentreLines, await IntAsync(database,
                "SELECT COUNT(*) FROM dbo.v_service_parts_centre WHERE service_store_code IN (N'" + seeded + "',N'" + SecondCode + "') OR service_store_code IS NULL"));
            Assert.True(await IntAsync(database, $"SELECT COUNT(*) FROM dbo.v_service_parts_centre WHERE service_store_code=N'{SecondCode}'") > 0,
                "A lower-case, padded store code on a row still names SC999.");
            Assert.True(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_parts_centre WHERE service_store_code IS NULL") > 0);
            // The view adds no line to v_service_parts: every created line has its centre row, no invoice twice.
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM (SELECT invoice_number,item_id FROM dbo.v_service_parts_centre GROUP BY invoice_number,item_id HAVING COUNT(*)>1) d
                """));

            // C# (needs P8's ScopedSql/BindScope wired into ReadAsync): the scoped reads add up to the all-centres read.
            var all = new SqlServerServiceReportQuery(database.ConnectionString);
            var allParts = await all.LoadPartsAsync();
            var buckets = new[] { seeded, SecondCode, ServiceCentreScope.NotMatchedCode };
            var invoices = 0;
            var stockItems = 0;
            foreach (var code in buckets)
            {
                var parts = await all.ForServiceCentre(code).LoadPartsAsync();
                invoices += parts.Invoices.Count;
                stockItems += parts.Stock?.Items ?? 0;
            }
            Assert.Equal(allParts.Invoices.Count, invoices);
            Assert.Equal(allParts.Stock?.Items ?? 0, stockItems);
            Assert.Equal(inSeeded, (await all.ForServiceCentre(seeded).LoadPartsAsync()).Stock?.Items ?? 0);
            Assert.Equal(inNone, (await all.ForServiceCentre(ServiceCentreScope.NotMatchedCode).LoadPartsAsync()).Stock?.Items ?? 0);

            // Jobs: the seeded centre's prefix starts every fixture job; "Not matched" is the jobs of no centre.
            SqlServerServiceReportQuery.ForgetJobRows();
            var jobs = (await all.LoadJobListAsync()).Count;
            var split = 0;
            foreach (var code in buckets) split += (await all.ForServiceCentre(code).LoadJobListAsync()).Count;
            Assert.Equal(jobs, split);
            Assert.Equal(await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_job WHERE service_store_code IS NULL"),
                (await all.ForServiceCentre(ServiceCentreScope.NotMatchedCode).LoadJobListAsync()).Count);
        }
        finally
        {
            SqlServerServiceReportQuery.ForgetJobRows();
            await database.DisposeAsync();
        }
    }

    private static async Task<int> IntAsync(SqlDatabaseFixture database, string sql) => Convert.ToInt32(await database.ExecuteAsync(sql, 300));
}
