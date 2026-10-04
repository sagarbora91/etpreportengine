using System.Globalization;
using System.Text;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Owner answer Q3/Q5 (decision 14), ETP 1.9.4 lane L9: Brand Stock Entry and Brand Physical Stock group Helios house-brand
// items by the owner's hybrid rows (DSR brand rows mapped by cluster), keep brand grouping where rows are mapped by brand
// name only, and keep the old brand layout on a date whose saved counts use a brand the rows split. Synthetic stores only.
public sealed class HybridStockRowsSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private static readonly DateOnly Day1 = new(2030, 7, 10);

    [Fact]
    public async Task Cluster_mapped_brand_rows_split_the_house_brand_into_the_owner_rows()
    {
        const string store = "HYB-A";
        await SaveRows(store, ("G SHOCK", 10, "CGSHG"), ("CITIZEN", 20, "CTZNG,CTZNL"), ("AMAZEFIT", 30, "AMZTG"), ("KENNETH COLE", 40, "KENNETH COLE"));
        await SeedSnapshot(store, Day1, HouseBrandItems());
        var reports = new OperationalReportRepository(database.ConnectionString);

        var entry = await reports.LoadBrandStockEntryAsync(store, Day1);
        var physical = await reports.LoadBrandPhysicalStockAsync(store, Day1);
        var closing = await reports.LoadStockInventoryAsync(new(Day1, Day1, [store]));

        Expected(entry.Select(x => (x.Brand, x.System)));
        Expected(physical.Select(x => (x.InventoryGroupCode, x.SystemQuantity)));
        Assert.Equal("G SHOCK", closing.Single(x => x.ProductCode == "H-GS").BrandRow);
        // The brand-name row KENNETH COLE is not a cluster, so the item keeps its brand as its group.
        Assert.Null(closing.Single(x => x.ProductCode == "K-1").BrandRow);
        Assert.Equal("KENNETH COLE", closing.Single(x => x.ProductCode == "K-1").StockGroup);

        static void Expected(IEnumerable<(string, decimal)> rows) =>
            Assert.Equal([("AMAZEFIT", 3m), ("CITIZEN", 1m), ("G SHOCK", 2m), ("KENNETH COLE", 4m)], rows.OrderBy(x => x.Item1, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Store_with_brand_name_rows_only_keeps_brand_grouping()
    {
        const string store = "HYB-W";
        await SaveRows(store, ("TITAN", 10, "TITAN"), ("SONATA", 20, "SONATA"));
        await SeedSnapshot(store, Day1, [new("T-1", "TITAN", "TWATG", 2m), new("S-1", "SONATA", "SNTWG", 1m), new("F-1", "FASTRACK", "FTWG", 5m)]);

        var physical = await new OperationalReportRepository(database.ConnectionString).LoadBrandPhysicalStockAsync(store, Day1);

        Assert.Equal([("FASTRACK", 5m), ("SONATA", 1m), ("TITAN", 2m)], physical.Select(x => (x.InventoryGroupCode, x.SystemQuantity)).OrderBy(x => x.InventoryGroupCode, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_date_counted_under_the_old_brand_keeps_the_old_layout_and_the_next_day_starts_the_new_one()
    {
        const string store = "HYB-L";
        var day2 = Day1.AddDays(1);
        await SaveRows(store, ("G SHOCK", 10, "CGSHG"), ("CITIZEN", 20, "CTZNG,CTZNL"), ("AMAZEFIT", 30, "AMZTG"));
        await SeedSnapshot(store, Day1, HouseBrandItems());
        await SeedSnapshot(store, day2, HouseBrandItems());
        var counts = new OperationalCompletionRepository(database.ConnectionString);
        await counts.SaveManualStockCountAsync(store, Day1, "HELIOS", 6, 0, 0, 0, 6, "Counted before the owner rows", "test", "Test fixture");
        var reports = new OperationalReportRepository(database.ConnectionString);

        var legacy = await reports.LoadBrandPhysicalStockAsync(store, Day1);
        var legacyEntry = await reports.LoadBrandStockEntryAsync(store, Day1);
        var next = await reports.LoadBrandStockEntryAsync(store, day2);

        Assert.Equal([("HELIOS", 6m, "PASS"), ("KENNETH COLE", 4m, "MANUAL INPUT MISSING")],
            legacy.Select(x => (x.InventoryGroupCode, x.SystemQuantity, x.Status)).OrderBy(x => x.InventoryGroupCode, StringComparer.Ordinal));
        Assert.Equal(["HELIOS", "KENNETH COLE"], legacyEntry.Select(x => x.Brand).Order(StringComparer.Ordinal));
        // Day 2 has no counts of its own: the owner rows apply, and yesterday's HELIOS count is not offered as a row.
        Assert.Equal(["AMAZEFIT", "CITIZEN", "G SHOCK", "KENNETH COLE"], next.Select(x => x.Brand).Order(StringComparer.Ordinal));
    }

    private static IReadOnlyList<Item> HouseBrandItems() =>
    [
        new("H-GS", "HELIOS", "CGSHG", 2m),
        new("H-CL", "HELIOS", "CTZNL", 1m),
        new("H-AM", "HELIOS", "AMZTG", 3m),
        new("K-1", "KENNETH COLE", "KCOLG", 4m)
    ];

    private sealed record Item(string Product, string Brand, string Cluster, decimal Quantity);

    private async Task SaveRows(string store, params (string Label, int Order, string Codes)[] rows)
    {
        var masters = new EveningMasterRepository(database.ConnectionString);
        foreach (var row in rows) await masters.SaveBrandAsync(new(0, store, row.Label, row.Order, row.Codes));
    }

    private async Task SeedSnapshot(string store, DateOnly date, IReadOnlyList<Item> items)
    {
        var day = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var sql = new StringBuilder($"""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@lineage bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count) VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),{items.Count});
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'hybrid-closing.xlsx',LEFT(REPLACE(CONCAT(NEWID(),NEWID()),'-',''),64),2048,'CLOSING_STOCK','{store}','{day}','{day}','{day}',1);
            SET @file=SCOPE_IDENTITY();

            """);
        var row = 2;
        foreach (var item in items)
        {
            sql.AppendLine($"INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',{row++},'CLOSING_STOCK'); SET @lineage=SCOPE_IDENTITY();");
            sql.AppendLine($"""
                INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,unit_cost,total_cost,source_lineage_id,source_report_code,brand_name,cluster)
                 VALUES('{store}','{day}',N'{item.Product}',N'LOT-1',{item.Quantity.ToString(CultureInfo.InvariantCulture)},10,{(item.Quantity * 10m).ToString(CultureInfo.InvariantCulture)},@lineage,'CLOSING_STOCK',N'{item.Brand}',N'{item.Cluster}');
                """);
        }
        await database.ExecuteAsync(sql.ToString());
    }
}
