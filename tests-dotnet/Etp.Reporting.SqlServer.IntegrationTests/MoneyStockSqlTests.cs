using System.Globalization;
using System.Text;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12.1 lane M4 (money test gaps #17 and #18). Closing-stock value and the physical-count FAIL path, on synthetic stock
/// only (store WLMHW, brand SAMPLE, no customer data). Every expected value is worked out by hand in the comments.
/// Needs the migrated schema; runs only in the elevated gate, compiled and not run on the lane branch.
/// </summary>
public sealed class MoneyStockSqlTests
{
    private const string Store = "WLMHW";
    private static readonly DateOnly Day = new(2030, 7, 10);

    private sealed record Line(string Product, string Batch, decimal Quantity, decimal? UnitCost, decimal? TotalCost);

    private static string Num(decimal? value) => value is null ? "NULL" : value.Value.ToString(CultureInfo.InvariantCulture);

    // One CLOSING_STOCK file for the store and day, one snapshot row per line, all in brand SAMPLE.
    private static async Task SeedSnapshot(SqlDatabaseFixture database, params Line[] lines)
    {
        var day = Day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var sql = new StringBuilder($"""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@lineage bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),{lines.Length});
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'m4-closing.xlsx',LEFT(REPLACE(CONCAT(NEWID(),NEWID()),'-',''),64),2048,'CLOSING_STOCK','{Store}','{day}','{day}','{day}',1);
            SET @file=SCOPE_IDENTITY();

            """);
        var row = 2;
        foreach (var line in lines)
        {
            sql.AppendLine($"INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',{row++},'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();");
            sql.AppendLine($"INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,unit_cost,total_cost,source_lineage_id,source_report_code,brand_name,cluster) " +
                $"VALUES('{Store}','{day}',N'{line.Product}',N'{line.Batch}',{Num(line.Quantity)},{Num(line.UnitCost)},{Num(line.TotalCost)},@lineage,'CLOSING_STOCK',N'SAMPLE',N'SMPLG');");
        }
        await database.ExecuteAsync(sql.ToString());
    }

    [SqlFact]
    public async Task Stock_value_with_an_uncosted_row_and_a_negative_quantity()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedSnapshot(database,
                new("ITEM-A", "LOT-1", 2m, 1000m, 2000m),
                new("ITEM-A", "LOT-2", 1m, null, null),
                new("ITEM-B", "LOT-1", -1m, 500m, -500m),
                new("ITEM-C", "LOT-1", 5m, null, null));
            var reports = new OperationalReportRepository(database.ConnectionString);

            var closing = await reports.LoadStockInventoryAsync(new(Day, Day, [Store]));

            // Item A: lots are merged, quantity 2 + 1 = 3. Only the costed lot has a value, so TotalCost = 2,000. The one
            // uncosted unit is not flagged (PIN: today's behaviour; the value is silently short by that unit).
            var a = closing.Single(x => x.ProductCode == "ITEM-A");
            Assert.Equal(3m, a.Quantity);
            Assert.Equal(2000m, a.TotalCost);
            // Item B: a negative quantity and a negative cost are summed as they come: -1 and -500.
            var b = closing.Single(x => x.ProductCode == "ITEM-B");
            Assert.Equal(-1m, b.Quantity);
            Assert.Equal(-500m, b.TotalCost);
            // Item C: no lot has a cost, so the value is blank, not 0.
            var c = closing.Single(x => x.ProductCode == "ITEM-C");
            Assert.Equal(5m, c.Quantity);
            Assert.Null(c.TotalCost);

            // Brand SAMPLE: quantity 3 - 1 + 5 = 7; the costed rows add up to 2,000 - 500 = 1,500 (item C's 5 units add nothing).
            Assert.Equal(7m, closing.Sum(x => x.Quantity));
            Assert.Equal(1500m, closing.Sum(x => x.TotalCost ?? 0m));

            var physical = await reports.LoadBrandPhysicalStockAsync(Store, Day);
            Assert.Equal(7m, Assert.Single(physical).SystemQuantity);
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task Physical_count_short_by_one_fails_with_minus_one_and_reaches_the_daily_exceptions()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedSnapshot(database, new("ITEM-S", "LOT-1", 10m, 100m, 1000m));
            var counts = new OperationalCompletionRepository(database.ConnectionString);
            var reports = new OperationalReportRepository(database.ConnectionString);

            // Display 6 + Backstock 2 + Defective 0 + Y location 1 = 9 counted against 10 in the system: short by one.
            await counts.SaveManualStockCountAsync(Store, Day, "SAMPLE", 6, 2, 0, 1, null, "Short by one", "test", "Synthetic count");
            var failed = Assert.Single(await reports.LoadBrandPhysicalStockAsync(Store, Day));
            Assert.Equal("SAMPLE", failed.InventoryGroupCode);
            Assert.Equal(10m, failed.SystemQuantity);
            Assert.Equal(9m, failed.ComponentTotal);
            Assert.Equal(-1m, failed.SystemVariance);
            Assert.Equal("FAIL", failed.Status);

            var exceptions = await reports.LoadDailyExceptionsAsync(Store, Day);
            var variance = Assert.Single(exceptions, x => x.Code == "PHYSICAL_SYSTEM_VARIANCE");
            Assert.Equal("FAIL", variance.Severity);
            Assert.Equal(-1m, variance.Variance);
            Assert.Equal("SAMPLE", variance.ItemCode);

            // Counting the missing unit (Y location 2: 6 + 2 + 0 + 2 = 10) clears the FAIL and the exception.
            await counts.SaveManualStockCountAsync(Store, Day, "SAMPLE", 6, 2, 0, 2, null, "Recount", "test", "Synthetic recount");
            var passed = Assert.Single(await reports.LoadBrandPhysicalStockAsync(Store, Day));
            Assert.Equal(0m, passed.SystemVariance);
            Assert.Equal("PASS", passed.Status);
            Assert.DoesNotContain(await reports.LoadDailyExceptionsAsync(Store, Day), x => x.Code.StartsWith("PHYSICAL_", StringComparison.Ordinal));
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task Physical_count_with_a_blank_component_is_manual_input_missing_not_a_variance()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedSnapshot(database, new("ITEM-S", "LOT-1", 10m, 100m, 1000m));
            // Backstock left blank: the total is unknown, so no variance is computed (blank is never read as zero).
            await new OperationalCompletionRepository(database.ConnectionString)
                .SaveManualStockCountAsync(Store, Day, "SAMPLE", 6, null, 0, 1, null, "Backstock not counted", "test", "Synthetic count");
            var reports = new OperationalReportRepository(database.ConnectionString);

            var row = Assert.Single(await reports.LoadBrandPhysicalStockAsync(Store, Day));

            Assert.Equal("MANUAL INPUT MISSING", row.Status);
            Assert.Null(row.ComponentTotal);
            Assert.Null(row.SystemVariance);
            var missing = Assert.Single(await reports.LoadDailyExceptionsAsync(Store, Day), x => x.Code == "PHYSICAL_COUNT_MISSING");
            Assert.Equal("WARNING", missing.Severity);
            Assert.Null(missing.Variance);
        }
        finally { await database.DisposeAsync(); }
    }
}
