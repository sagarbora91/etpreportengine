using Etp.Reporting.Application.Imports;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.TestSupport.Service;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// Lane L10 (decision 16) against SQL: S041 GPRC CLAIM lands in its own table, and dbo.v_service_gprc_claims reads S023
/// and S041 together without counting the overlap twice (S041 wins per claim document). dbo.v_service_manual_money marks
/// the Service-money shop, so ServiceMoneyCheck compares only that shop's entries and lists the others.
/// <para>
/// Runs only on the integrated branch: it needs lane L3's Service routing (the Service store is inactive), lane L4's
/// section C with S041 in its DateLog list (patch plan L4-1 in the svc-l10 lane notes) and the L10 block of
/// scripts/service-centre/c_service_read_gprc_and_money.sql, which this test applies itself (CREATE OR ALTER) so it also
/// works before the block is generated into section C. Synthetic fixtures only (tests-dotnet/fixtures/service-interim/gprc).
/// </para>
/// </summary>
public sealed class ServiceGprcClaimsSqlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_GPRC_union_counts_each_claim_document_once_whatever_the_import_order(bool claimsFirst)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            string[] folders = claimsFirst
                ? [ServiceFixtures.GprcClaimFolder, ServiceFixtures.GprcHistoryFolder]
                : [ServiceFixtures.GprcHistoryFolder, ServiceFixtures.GprcClaimFolder];
            foreach (var folder in folders)
            {
                var summary = await ImportAsync(database, folder);
                Assert.True(summary.Files.Count(file => file.Status == "Imported") == 1, $"{folder}: {Outcomes(summary)}");
            }
            await ApplyL10BlockAsync(database);

            // Each family landed in its own table.
            Assert.Equal(ServiceFixtures.GprcHistoryLines, await IntAsync(database, "SELECT COUNT(*) FROM dbo.etp_landing_s023"));
            Assert.Equal(ServiceFixtures.GprcClaimLines, await IntAsync(database, "SELECT COUNT(*) FROM dbo.etp_landing_s041"));
            Assert.Equal(1, await IntAsync(database, "SELECT COUNT(*) FROM dbo.import_files WHERE report_code='S041' AND period_end='2026-08-07'"));
            // Transaction Date landed as its date part (the source keeps 15:35:43.146 on the 5 Aug lines).
            Assert.Equal(2, await IntAsync(database, "SELECT COUNT(*) FROM dbo.etp_landing_s041 WHERE transaction_date='2026-08-05'"));

            // The union: 8 lines, 5 documents; 2 lines from S023 (the document S041 does not hold), 6 from S041.
            Assert.Equal(ServiceFixtures.GprcUnionLines, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_gprc_claims"));
            Assert.Equal(ServiceFixtures.GprcUnionDocuments, await IntAsync(database, "SELECT COUNT(DISTINCT document_number) FROM dbo.v_service_gprc_claims"));
            Assert.Equal(ServiceFixtures.GprcUnionLinesFromHistory, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_gprc_claims WHERE report_code='S023'"));
            Assert.Equal(0, await IntAsync(database, """
                SELECT COUNT(*) FROM (SELECT document_number FROM dbo.v_service_gprc_claims GROUP BY document_number
                HAVING COUNT(DISTINCT report_code)>1) d
                """));
            Assert.Equal("S023", await database.ExecuteAsync("SELECT DISTINCT report_code FROM dbo.v_service_gprc_claims WHERE document_number=N'GPAW330SYN0001'"));
            // The overlapping documents come from S041 only; 0003 has the second line that only S041 holds.
            Assert.Equal(1, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_gprc_claims WHERE document_number=N'GPAW330SYN0002'"));
            Assert.Equal(2, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_gprc_claims WHERE document_number=N'GPAW330SYN0003' AND report_code='S041'"));
            // Value: S041 lines 2+10+2+2+10+2 = 28, plus the S023-only document 2+10 = 12. A plain UNION ALL would give 52.
            Assert.Equal(40m, Convert.ToDecimal(await database.ExecuteAsync("SELECT SUM(value) FROM dbo.v_service_gprc_claims")));
            Assert.Equal(2, await IntAsync(database, "SELECT COUNT(*) FROM dbo.v_service_gprc_claims WHERE business_date='2026-08-01'"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task The_manual_money_view_marks_only_the_Titan_World_shop_and_the_check_lists_the_others_apart()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await ApplyL10BlockAsync(database);
            await database.ExecuteAsync("""
                INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason) VALUES
                ('WLMHW','20260825','SERVICE_CASH',700,'test','test','Synthetic service money'),
                ('WLMHW','20260825','SERVICE_CARD',1807,'test','test','Synthetic service money'),
                ('WLMHW','20260825','SERVICE_UPI',577,'test','test','Synthetic service money'),
                ('WLMHW','20260825','SERVICE_WDC',90,'test','test','Synthetic service money'),
                ('WLMHW','20260825','WALK_INS',12,'test','test','Not a Service field'),
                ('HEMW','20260825','SERVICE_CASH',300,'test','test','Entered at the wrong shop');
                """);

            var entries = new List<ServiceManualMoneyEntry>();
            await using (var connection = new SqlConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand(
                    "SELECT business_date,store_code,store_name,field_code,amount,is_service_money_shop,tender FROM dbo.v_service_manual_money ORDER BY store_code,field_code", connection);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    entries.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetString(3), reader.GetDecimal(4), reader.GetBoolean(5)));
                    Assert.Equal(reader.GetString(3) == "SERVICE_WDC", reader.IsDBNull(6));
                }
            }
            Assert.Equal(5, entries.Count);
            Assert.Equal(["HEMW"], entries.Where(entry => !entry.IsServiceMoneyShop).Select(entry => entry.StoreCode).Distinct());

            var day = new DateOnly(2026, 8, 25);
            var days = ServiceMoneyCheck.Compare(
                [new(day, "CASH", 700), new(day, "CARD", 1807), new(day, "UPI", 577)], entries);
            Assert.All(days, row => Assert.Equal(0m, row.Difference));
            var unmatched = Assert.Single(ServiceMoneyCheck.Unmatched(entries));
            Assert.Equal(("HEMW", "SERVICE_CASH", 300m), (unmatched.StoreCode, unmatched.FieldCode, unmatched.Amount));
            Assert.Equal("Service entry at Helios: not matched to AW330.", unmatched.Note);
        }
        finally { await database.DisposeAsync(); }
    }

    // The L10 block of the script, one batch per view (each is a single EXEC).
    private static async Task ApplyL10BlockAsync(SqlDatabaseFixture database)
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "service-centre", "c_service_read_gprc_and_money.sql")).Replace("\r\n", "\n");
        var begin = text.IndexOf("-- >>> L10 block begin\n", StringComparison.Ordinal);
        var end = text.IndexOf("\n-- <<< L10 block end", StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        await database.ExecuteAsync(text[begin..end], 120);
    }

    private static async Task<FolderImportSummary> ImportAsync(SqlDatabaseFixture database, string folder) =>
        await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString)).RunAsync(folder, new("Synthetic Owner"));

    private static string Outcomes(FolderImportSummary summary) =>
        string.Join(", ", summary.Files.Select(file => $"{file.FileName}: {file.Status} {file.Message}"));

    private static async Task<int> IntAsync(SqlDatabaseFixture database, string sql) => Convert.ToInt32(await database.ExecuteAsync(sql));

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root containing Etp.Reporting.slnx.");
    }
}
