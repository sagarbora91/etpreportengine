using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// Lane L9 (Service interim, decision 15): the synthetic raw Service pack (tests-dotnet/fixtures/service-interim/raw) imports
/// through the real folder import into the same landing tables as the weekly consolidated workbooks (L0's week1/week2),
/// and the read views (L4) take the raw rows for the dates the raw window holds and week2's rows for older dates.
/// Needs L0 (fixtures, 0048 section A), L1 (catalogue), L2 (tables), L3 (routing) and L4 (views) merged. Runs only in the
/// elevated gate; compiled, not run, on the lane branch. View column names follow the L0 contract records in snake case.
/// </summary>
public sealed class ServiceRawImportSqlTests
{
    private static readonly string[] RawImported =
    [
        "JOB REPORT 06.10.2026 TO 09.10.2026.csv", "TENDER COLLECTION 05.10.2026 TO 09.10.2026.csv",
        "PENDING REPORT 09.10.2026.csv", "PENDING DELIVERY 09.10.2026.csv", "R R DELIVERD 06.10.2026 TO 09.10.2026.csv",
        "R R PENDING DELIVERY 06.10.2026 TO 09.10.2026.csv", "RENVENUE REPORT 06.10.2026 TO 09.10.2026.csv",
        "EMPOWERMENT REPORT 06.10.2026 TO 09.10.2026.xlsx", "GPRC CLAIM 06.10.2026 TO 09.10.2026.xlsx"
    ];

    private static readonly string[] RawNotNeeded =
        ["TENDER COLLECTIN SUMMARY 06.10.2026 TO 09.10.2026.csv", "TECHNICIAN PRODUCIVITY REPORT 06.10.2026 TO 09.10.2026.xlsx"];

    private static string Fixtures(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, "tests-dotnet", "fixtures", "service-interim", .. parts]);
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("tests-dotnet/fixtures/service-interim/" + string.Join("/", parts));
    }

    private static Task<FolderImportSummary> Import(SqlDatabaseFixture database, string folder) =>
        new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
            .RunAsync(folder, new("Synthetic Owner"));

    private static async Task<decimal> Scalar(SqlDatabaseFixture database, string sql)
    {
        var value = await database.ExecuteAsync(sql);
        return value is null or DBNull ? 0m : Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Raw_pack_imports_beside_the_weekly_readings_and_the_views_combine_them()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var week1 = await Import(database, Fixtures("week1", "Service Centre till 28 sep 2026"));
            var week2 = await Import(database, Fixtures("week2", "Service Centre till 05 oct 2026"));
            Assert.Equal(0, week1.Failed);
            Assert.Equal(0, week2.Failed);

            var raw = await Import(database, Fixtures("raw"));

            Assert.Equal(0, raw.Failed);
            foreach (var name in RawImported)
            {
                var file = Assert.Single(raw.Files, file => file.FileName == name);
                Assert.True(file.Status == "Imported", $"{name}: {file.Status} {file.Message}");
                Assert.Equal(new DateOnly(2026, 10, 9), file.PeriodEnd);
                Assert.Equal("AW330", file.StoreCode);
            }
            foreach (var name in RawNotNeeded)
                Assert.Equal("Not needed", Assert.Single(raw.Files, file => file.FileName == name).Status);

            // Each raw file is one more reading, dated by its window end; nothing older is superseded.
            Assert.Equal((decimal)RawImported.Length, await Scalar(database,
                "SELECT COUNT(*) FROM dbo.import_files WHERE period_end='2026-10-09' AND is_superseded=0"));
            Assert.Equal(0m, await Scalar(database,
                "SELECT COUNT(*) FROM dbo.import_files WHERE report_code LIKE 'S%' AND is_superseded=1"));
            Assert.Equal(4m, await Scalar(database, """
                SELECT COUNT(*) FROM dbo.etp_landing_s002 AS l JOIN dbo.import_files AS f ON f.import_file_id=l.import_file_id
                WHERE f.period_end='2026-10-09'
                """));

            // The read views: the latest S004 reading is the raw one; its dates come from it, older dates from week2.
            Assert.Equal(new DateTime(2026, 10, 9), Convert.ToDateTime(await database.ExecuteAsync(
                "SELECT snapshot_date FROM dbo.v_service_readings WHERE report_code='S004' AND is_latest=1")));
            Assert.Equal(625m, await Scalar(database,
                "SELECT SUM(amount) FROM dbo.v_service_s004_daily WHERE business_date='2026-10-06'"));
            var olderFromWeek2 = await Scalar(database, """
                SELECT SUM(l.totalamount) FROM dbo.etp_landing_s004 AS l JOIN dbo.import_files AS f ON f.import_file_id=l.import_file_id
                WHERE f.period_end='2026-10-05' AND l.billingdate<'2026-10-05'
                """);
            Assert.True(olderFromWeek2 > 0, "week2 holds S004 billing dates before 2026-10-05");
            Assert.Equal(olderFromWeek2, await Scalar(database,
                "SELECT SUM(amount) FROM dbo.v_service_s004_daily WHERE business_date<'2026-10-05' AND business_date>=" +
                "(SELECT MIN(l.billingdate) FROM dbo.etp_landing_s004 AS l JOIN dbo.import_files AS f ON f.import_file_id=l.import_file_id WHERE f.period_end='2026-10-05')"));

            // 05 Oct is in week2 and in the raw window: the raw amount wins and the change is listed for the Owner.
            var week2On05 = await Scalar(database, """
                SELECT SUM(l.totalamount) FROM dbo.etp_landing_s004 AS l JOIN dbo.import_files AS f ON f.import_file_id=l.import_file_id
                WHERE f.period_end='2026-10-05' AND l.billingdate='2026-10-05'
                """);
            // L0's week2 holds three tender rows on 05 Oct (400 + 450 + 125); the raw window holds two (250 + 275).
            Assert.Equal(975m, week2On05);
            Assert.Equal(525m, await Scalar(database,
                "SELECT SUM(amount) FROM dbo.v_service_s004_daily WHERE business_date='2026-10-05'"));
            Assert.Equal(1m, await Scalar(database,
                "SELECT COUNT(*) FROM dbo.v_service_money_changes WHERE report_code='S004' AND business_date='2026-10-05'"));
            Assert.Equal(-450m, await Scalar(database,
                "SELECT difference FROM dbo.v_service_money_changes WHERE report_code='S004' AND business_date='2026-10-05'"));

            // A second import of the same raw pack changes nothing.
            var rows = await Scalar(database, "SELECT COUNT(*) FROM dbo.etp_import_content");
            var again = await Import(database, Fixtures("raw"));
            Assert.Equal(0, again.Failed);
            Assert.Equal(0, again.Imported);
            Assert.All(RawImported, name => Assert.Equal("Duplicate", Assert.Single(again.Files, file => file.FileName == name).Status));
            Assert.Equal(rows, await Scalar(database, "SELECT COUNT(*) FROM dbo.etp_import_content"));
        }
        finally { await database.DisposeAsync(); }
    }
}
