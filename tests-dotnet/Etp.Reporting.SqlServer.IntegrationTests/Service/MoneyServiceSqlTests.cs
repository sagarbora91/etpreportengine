using System.Text;
using Etp.Reporting.Application.Service;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// 1.12.1 lane M4 (money test gaps #14 and #15, decision 68). Synthetic stores and amounts only, every expected value worked
/// out by hand in the comments. Gap #14: the DSR Service total adds the Service-money shop's WDC, and the WDC tile follows that
/// shop only (decision 68 answer 6). Gap #15: the S004 daily view's UPI tender is UPI + BharatPe + PhonePe text, and an
/// all-blank day stays blank, not zero. No centre filter is used anywhere (lane P8 adds per-centre views); the S004 asserts are
/// per tender. Needs the migrated schema and views; runs only in the elevated gate, compiled and not run on the lane branch.
/// </summary>
public sealed class MoneyServiceSqlTests
{
    private static readonly DateOnly Day = new(2026, 8, 25);

    private static async Task SeedServiceMoney(SqlDatabaseFixture database)
    {
        // WLMHW is the Service-money shop (the manual-money view marks it, see ServiceGprcClaimsSqlTests). HEMW keys WDC only.
        await database.ExecuteAsync("""
            INSERT dbo.manual_operational_inputs(store_code,business_date,field_code,numeric_value,entered_by,modified_by,change_reason) VALUES
            ('WLMHW','20260825','SERVICE_CASH',700,'test','test','Synthetic service money'),
            ('WLMHW','20260825','SERVICE_CARD',1807,'test','test','Synthetic service money'),
            ('WLMHW','20260825','SERVICE_UPI',577,'test','test','Synthetic service money'),
            ('WLMHW','20260825','SERVICE_WDC',250,'test','test','Synthetic service money'),
            ('HEMW','20260825','SERVICE_WDC',100,'test','test','Synthetic WDC at another shop');
            """);
    }

    [SqlFact]
    public async Task Service_total_adds_the_wdc_of_the_service_money_shop()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedServiceMoney(database);
            var reports = new OperationalReportRepository(database.ConnectionString);

            var rows = await reports.LoadServiceSalesAsync(Day, ["WLMHW"]);
            var titan = rows.Single(x => x.Period == "FTD");
            // 700 cash + 1,807 card + 577 UPI + 250 WDC = 3,334.
            Assert.Equal(700m, titan.Cash);
            Assert.Equal(1807m, titan.Card);
            Assert.Equal(577m, titan.Upi);
            Assert.Equal(250m, titan.Wdc);
            Assert.Equal(3334m, titan.Total);

            // The DSR Service card sums only the Service-money shop, so Helios (WDC 100 only) adds nothing to its total.
            var document = await reports.LoadDailySalesReportDocumentAsync(Day);
            Assert.Equal(3334m, document.Service.Total);
            Assert.Equal(700m, document.Service.Cash);
            Assert.Equal(1807m, document.Service.Card);
            Assert.Equal(577m, document.Service.Upi);
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact(Skip = "Fails until F1 (gap #14, decision 68)")]
    public async Task Wdc_tile_sums_only_the_service_money_shop()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedServiceMoney(database);

            var document = await new OperationalReportRepository(database.ConnectionString).LoadDailySalesReportDocumentAsync(Day);

            // Decision 68 (6): 250 at the Service-money shop only. Today the tile adds every active shop: 250 + 100 = 350.
            Assert.Equal(250m, document.Service.Wdc);
            // The tile and the total now read the same shop: total minus tile equals cash + card + UPI = 3,084.
            Assert.Equal(700m + 1807m + 577m, document.Service.Total - document.Service.Wdc);
        }
        finally { await database.DisposeAsync(); }
    }

    private sealed record Landing(string Date, string? Upi, string? BharatPe, string? PhonePe, string? Cash, string? Card);

    // One completed S004 file (store AW330, window 6-9 Oct 2026) with one landing row per billing date.
    private static async Task SeedS004(SqlDatabaseFixture database, params Landing[] rows)
    {
        var sql = new StringBuilder($$"""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@lineage bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),{{rows.Length}});
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'s004-money.csv',LEFT(REPLACE(CONCAT(NEWID(),NEWID()),'-',''),64),2048,'S004','AW330','20261009','20261006','20261009',1);
            SET @file=SCOPE_IDENTITY();

            """);
        var number = 2;
        foreach (var row in rows)
        {
            var phonepe = row.PhonePe is null ? "NULL" : "N'" + row.PhonePe + "'";
            sql.AppendLine($"INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',{number},'S004'); SET @lineage=SCOPE_IDENTITY();");
            sql.AppendLine($"INSERT dbo.etp_landing_s004(import_file_id,source_lineage_id,content_key,billingnumber,billingdate,store_code,cashamount,cardamount,bharatpe,phonepe,upi) " +
                $"VALUES(@file,@lineage,'m4-s004-{number}',N'M4-{number}','{row.Date}',N'AW330',{row.Cash ?? "NULL"},{row.Card ?? "NULL"},{row.BharatPe ?? "NULL"},{phonepe},{row.Upi ?? "NULL"});");
            number++;
        }
        await database.ExecuteAsync(sql.ToString());
    }

    private static async Task<List<ServiceS004TenderAmount>> ReadDaily(SqlDatabaseFixture database)
    {
        var rows = new List<ServiceS004TenderAmount>();
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT business_date,tender,amount FROM dbo.v_service_s004_daily ORDER BY business_date,tender", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetDecimal(2)));
        return rows;
    }

    [SqlFact]
    public async Task S004_upi_adds_bharatpe_and_phonepe_text_and_all_blank_stays_blank()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedS004(database,
                new("20261006", Upi: "1000", BharatPe: "250", PhonePe: "1,250.50", Cash: "400", Card: "300"),
                new("20261007", Upi: null, BharatPe: null, PhonePe: null, Cash: "0", Card: null),
                new("20261008", Upi: null, BharatPe: "0", PhonePe: null, Cash: null, Card: null),
                new("20261009", Upi: "100", BharatPe: null, PhonePe: "n/a", Cash: null, Card: null));

            var daily = await ReadDaily(database);
            decimal? Amount(int day, string tender) =>
                daily.Single(x => x.BusinessDate == new DateOnly(2026, 10, day) && x.Tender == tender).Amount;

            // Day 6: UPI 1,000 + BharatPe 250 + PhonePe text "1,250.50" (comma stripped) = 2,500.50. Other tenders are read as they are.
            Assert.Equal(2500.50m, Amount(6, "UPI"));
            Assert.Equal(400m, Amount(6, "CASH"));
            Assert.Equal(300m, Amount(6, "CARD"));
            // Day 7: all three UPI sources blank -> blank, never 0. Cash 0 stays 0; card blank stays blank.
            Assert.Null(Amount(7, "UPI"));
            Assert.Equal(0m, Amount(7, "CASH"));
            Assert.Null(Amount(7, "CARD"));
            // Day 8: one source holds a real zero -> 0.
            Assert.Equal(0m, Amount(8, "UPI"));
            // Day 9: PhonePe text that is not a number counts as blank; UPI 100 + 0 + 0 = 100.
            Assert.Equal(100m, Amount(9, "UPI"));

            // The check against the manual entry: the Service-money shop typed UPI 2,500 for day 6, S004 says 2,500.50.
            var manual = new[]
            {
                new ServiceManualMoneyEntry(new DateOnly(2026, 10, 6), "WLMHW", "Titan World", "SERVICE_UPI", 2500m, true),
                new ServiceManualMoneyEntry(new DateOnly(2026, 10, 6), "WLMHW", "Titan World", "SERVICE_CASH", 400m, true)
            };
            var days = ServiceMoneyCheck.Compare(daily, manual);
            Assert.Equal(0.50m, days.Single(x => x.BusinessDate == new DateOnly(2026, 10, 6) && x.Tender == "UPI").Difference);
            Assert.Equal(0m, days.Single(x => x.BusinessDate == new DateOnly(2026, 10, 6) && x.Tender == "CASH").Difference);
            // A blank S004 day with nothing typed has no difference (blank is never read as zero).
            Assert.Null(days.Single(x => x.BusinessDate == new DateOnly(2026, 10, 7) && x.Tender == "UPI").Difference);
        }
        finally { await database.DisposeAsync(); }
    }
}
