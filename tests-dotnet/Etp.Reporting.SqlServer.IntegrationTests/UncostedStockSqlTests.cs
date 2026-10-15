using System.Globalization;
using System.Text;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12.1 lane F2 (decision 69). Stock value is computed as before (an uncosted line adds nothing); the new count says how many
/// items it left out. Synthetic stores UNC-A and UNC-B, brands SAMPLE and OTHER, no customer data.
/// </summary>
public sealed class UncostedStockSqlTests
{
    private static readonly DateOnly Day = new(2030, 7, 11);

    private sealed record Line(string Store, string Product, string Batch, decimal Quantity, decimal? TotalCost, string Brand = "SAMPLE");

    private static string Num(decimal? value) => value is null ? "NULL" : value.Value.ToString(CultureInfo.InvariantCulture);

    private static async Task Seed(SqlDatabaseFixture database, params Line[] lines)
    {
        var day = Day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var sql = new StringBuilder("DECLARE @batch uniqueidentifier,@file bigint,@lineage bigint;\n");
        foreach (var store in lines.Select(x => x.Store).Distinct())
        {
            sql.AppendLine($"""
                SET @batch=NEWID();
                INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),{lines.Length});
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
                 VALUES(@batch,'f2-closing-{store}.xlsx',LEFT(REPLACE(CONCAT(NEWID(),NEWID()),'-',''),64),2048,'CLOSING_STOCK','{store}','{day}','{day}','{day}',1);
                SET @file=SCOPE_IDENTITY();
                """);
            var row = 2;
            foreach (var line in lines.Where(x => x.Store == store))
            {
                sql.AppendLine($"INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',{row++},'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();");
                sql.AppendLine($"INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,unit_cost,total_cost,source_lineage_id,source_report_code,brand_name,cluster) " +
                    $"VALUES('{store}','{day}',N'{line.Product}',N'{line.Batch}',{Num(line.Quantity)},{Num(line.TotalCost)},{Num(line.TotalCost)},@lineage,'CLOSING_STOCK',N'{line.Brand}',N'SMPLG');");
            }
        }
        await database.ExecuteAsync(sql.ToString());
    }

    [SqlFact]
    public async Task Uncosted_items_are_counted_per_store_and_brand_and_the_value_is_unchanged()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await Seed(database,
                new("UNC-A", "ITEM-1", "L1", 2m, 2000m),                  // costed lot: adds 2,000
                new("UNC-A", "ITEM-1", "L2", 1m, null),                   // same item, one uncosted lot: ITEM-1 counts once
                new("UNC-A", "ITEM-2", "L1", 5m, null),                   // wholly uncosted: counts
                new("UNC-A", "ITEM-3", "L1", 0m, null),                   // no quantity, nothing left out: not counted
                new("UNC-A", "ITEM-4", "L1", 3m, null, "OTHER"),          // another brand of the same store: counts under OTHER
                new("UNC-A", "GIFT CARD", "L1", 9m, null),                // gift cards are not stock: not counted
                new("UNC-B", "ITEM-5", "L1", 1m, 100m));                  // store with everything costed: no row
            var reports = new OperationalReportRepository(database.ConnectionString);
            var scope = new Etp.Reporting.Reporting.ReportingQueryScope(Day, Day, ["UNC-A", "UNC-B"]);

            var uncosted = await reports.LoadUncostedStockAsync(scope, latestOnOrBefore: false);

            Assert.Equal([("UNC-A", "OTHER", 1), ("UNC-A", "SAMPLE", 2)], uncosted.Select(x => (x.StoreCode, x.Brand, x.Items)).ToArray());
            Assert.Equal("3 items have no cost price; stock value leaves them out (UNC-A: OTHER 1, SAMPLE 2).",
                Etp.Reporting.Application.Reports.UncostedStockNote.Sentence(
                    uncosted.Select(x => new Etp.Reporting.Application.Reports.UncostedStockRecord(x.StoreCode, x.Brand, x.Items)).ToArray()));

            // The value is unchanged by this lane: ITEM-1 still shows 2,000 (its uncosted unit adds nothing), ITEM-2 and ITEM-4 are blank.
            var closing = await reports.LoadStockInventoryAsync(scope);
            Assert.Equal(2000m, closing.Single(x => x.ProductCode == "ITEM-1").TotalCost);
            Assert.Null(closing.Single(x => x.ProductCode == "ITEM-2").TotalCost);
            Assert.Equal(2100m, closing.Sum(x => x.TotalCost ?? 0m));

            // A scope that names only the fully costed store has nothing to say.
            Assert.Empty(await reports.LoadUncostedStockAsync(new(Day, Day, ["UNC-B"]), latestOnOrBefore: false));
        }
        finally { await database.DisposeAsync(); }
    }
}
