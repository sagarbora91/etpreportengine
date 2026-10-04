using System.Globalization;
using System.Text;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Stock Variance and Stock Movement through SQL (report audit 3 Oct 2026, ETP 1.9.4): HEMW FIX-01 (an R010 reading of an
// R011 day is never added), HEMW FIX-02 / WLMHW FIX-05 (the opening comes from the day's chain, whatever the stored row
// order), WLMHW FIX-04 (sold-out items are checked against a closing of 0) and WLMHW FIX-13 (a ledger that ends before
// the To date blocks the check and says so). Each test uses its own store, so the shared database never mixes them.
public sealed class StockVarianceSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private static readonly DateOnly Aug25 = new(2026, 8, 25);

    [Fact]
    public async Task Sold_out_items_are_checked_against_a_zero_closing_and_moved_items_all_show()
    {
        const string store = "SV-SOLD";
        await Seed(store,
        [
            new("SOLD-OUT", new(2026, 8, 10), "Purchase Receipt", 0m, 1m),
            new("SOLD-OUT", new(2026, 8, 20), "INV", 1m, 0m),
            new("MISSING", new(2026, 8, 10), "Purchase Receipt", 0m, 1m),
            new("ON-HAND", new(2026, 8, 10), "Purchase Receipt", 0m, 1m)
        ],
        [new("ON-HAND", Aug25, 1m)], ledgerCoversTo: Aug25);

        var result = await Executor().ExecuteStockReconciliationAsync(new(new(2026, 8, 1), Aug25, [store]));

        Assert.Equal(ReconciliationStatus.Failed, result.Status);
        Assert.Equal(["MISSING", "ON-HAND", "SOLD-OUT"], result.Items.Select(x => x.ItemCode));
        var soldOut = result.Items.Single(x => x.ItemCode == "SOLD-OUT");
        Assert.Equal(((decimal?)0m, (decimal?)0m, ReconciliationStatus.Passed), (soldOut.ReportedClosing, soldOut.Variance, soldOut.Status));
        var missing = result.Items.Single(x => x.ItemCode == "MISSING");
        Assert.Equal(((decimal?)0m, (decimal?)1m, ReconciliationStatus.Failed), (missing.ReportedClosing, missing.Variance, missing.Status));

        var movements = await Repository().LoadStockMovementsAsync(new(new(2026, 8, 1), Aug25, [store]));
        Assert.Equal(4, movements.Count);
        Assert.Contains(movements, x => x.ItemCode == "SOLD-OUT" && x.SourceMovementType == "INV" && x.SourceSignedQuantity == -1m);
        Assert.Equal(2m, movements.Sum(x => x.SourceSignedQuantity));
    }

    [Fact]
    public async Task Store_without_a_snapshot_on_the_to_date_is_blocked_and_its_items_are_marked_not_hidden()
    {
        // Owner answer Q9 (decision 13): movements and items of a day with no snapshot are listed, marked "no snapshot".
        const string store = "SV-NOSNAP";
        await Seed(store, [new("ITEM", new(2026, 8, 10), "Purchase Receipt", 0m, 1m)], [new("ITEM", new(2026, 8, 24), 1m)], ledgerCoversTo: Aug25);

        var result = await Executor().ExecuteStockReconciliationAsync(new(new(2026, 8, 1), Aug25, [store]));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.StartsWith("Closing stock missing for 25 Aug 2026 (SV-NOSNAP); items are listed without a closing figure (no snapshot).", result.Message);
        var item = Assert.Single(result.Items);
        Assert.Equal(("ITEM", 0m, 1m, 1m), (item.ItemCode, item.Opening, item.SourceSignedMovements, item.ExpectedClosing));
        Assert.Null(item.ReportedClosing);
        Assert.Null(item.Variance);
        Assert.Equal(ReconciliationStatus.Blocked, item.Status);
        var movement = Assert.Single(await Repository().LoadStockMovementsAsync(new(new(2026, 8, 1), Aug25, [store])));
        Assert.Equal(("ITEM", 1m, false), (movement.ItemCode, movement.SourceSignedQuantity, movement.HasSnapshot));
    }

    [Fact]
    public async Task Store_with_a_snapshot_marks_its_movements_as_having_one()
    {
        const string store = "SV-HASSNAP";
        await Seed(store, [new("ITEM", new(2026, 8, 10), "Purchase Receipt", 0m, 1m)], [new("OTHER", Aug25, 1m)], ledgerCoversTo: Aug25);

        var movement = Assert.Single(await Repository().LoadStockMovementsAsync(new(new(2026, 8, 1), Aug25, [store])));

        // ITEM is in no snapshot row (sold out or missing), but the store has a snapshot that day: judged per store.
        Assert.True(movement.HasSnapshot);
    }

    [Fact]
    public async Task Opening_comes_from_the_chain_of_the_day_whatever_order_the_rows_were_stored_in()
    {
        const string store = "SV-ORDER";
        await Seed(store,
        [
            // ME3219I: received earlier; on 11 Aug the SR (later in the day) is stored before the INV.
            new("RETURNED", new(2026, 8, 1), "Purchase Receipt", 0m, 1m),
            new("RETURNED", new(2026, 8, 11), "SR", 0m, 1m),
            new("RETURNED", new(2026, 8, 11), "INV", 1m, 0m),
            // CECRA26801: two receipts on 5 Aug, the second (1 -> 2) stored first.
            new("TWO-IN", new(2026, 8, 5), "STM Receipt", 1m, 2m),
            new("TWO-IN", new(2026, 8, 5), "STM Receipt", 0m, 1m),
            // 77196SL03: the BC is stored before the two INV rows of other documents; the true opening is 1.
            new("REBILLED", new(2026, 8, 12), "BC", 0m, 1m),
            new("REBILLED", new(2026, 8, 12), "INV", 1m, 0m),
            new("REBILLED", new(2026, 8, 12), "INV", 1m, 0m),
            new("REBILLED", new(2026, 8, 2), "Purchase Receipt", 0m, 1m)
        ],
        [new("RETURNED", Aug25, 1m), new("TWO-IN", Aug25, 2m)], ledgerCoversTo: Aug25);

        var fromDay = await Executor().ExecuteStockReconciliationAsync(new(new(2026, 8, 11), Aug25, [store]));
        var fromMonth = await Executor().ExecuteStockReconciliationAsync(new(new(2026, 8, 1), Aug25, [store]));

        Assert.Equal(ReconciliationStatus.Passed, fromDay.Status);
        Assert.Equal(1m, fromDay.Items.Single(x => x.ItemCode == "RETURNED").Opening);
        Assert.Equal(1m, fromDay.Items.Single(x => x.ItemCode == "REBILLED").Opening);
        Assert.Equal(ReconciliationStatus.Passed, fromMonth.Status);
        Assert.Equal(0m, fromMonth.Items.Single(x => x.ItemCode == "TWO-IN").Opening);
        Assert.All(fromMonth.Items, item => Assert.Equal(0m, item.Variance));
    }

    [Fact]
    public async Task Ledger_ending_before_the_to_date_blocks_with_its_last_date()
    {
        const string store = "SV-SHORT";
        var sep29 = new DateOnly(2026, 9, 29);
        // A sale on 3 Sep (after the ledger, before the To date) shows the ledger is short.
        await Seed(store, [new("ITEM", new(2026, 8, 20), "Purchase Receipt", 0m, 1m)], [new("ITEM", sep29, 1m)], ledgerCoversTo: Aug25,
            saleDates: [new(2026, 8, 20), new(2026, 9, 3), new(2026, 9, 30)]);

        var result = await Executor().ExecuteStockReconciliationAsync(new(new(2026, 8, 1), sep29, [store]));

        Assert.Equal(ReconciliationStatus.Blocked, result.Status);
        Assert.StartsWith("Ledger covers to 25 Aug 2026 for SV-SHORT (sales on 03 Sep 2026 are not in it), before the To date 29 Sep 2026.", result.Message);
        Assert.Equal(ReconciliationStatus.Passed, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task Ledger_whose_last_days_had_no_movement_and_no_sale_is_not_blocked()
    {
        // The import stores a ledger's last row date as its period end. A ledger exported to 25 Aug from a store that
        // had no stock movement on 25 Aug (closed or quiet) ends on 24 Aug; with no sale after 24 Aug it is complete.
        const string store = "SV-QUIET";
        var aug24 = new DateOnly(2026, 8, 24);
        await Seed(store, [new("ITEM", new(2026, 8, 20), "Purchase Receipt", 0m, 1m), new("ITEM", aug24, "INV", 1m, 0m)],
            [new("ITEM", Aug25, 0m)], ledgerCoversTo: aug24, saleDates: [aug24]);

        var result = await Executor().ExecuteStockReconciliationAsync(new(new(2026, 8, 1), Aug25, [store]));

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.DoesNotContain("Ledger covers", result.Message);
        Assert.EndsWith("Last ledger movement 24 Aug 2026 for SV-QUIET; no sales after it to 25 Aug 2026, so those days are taken as days without stock movement.", result.Message);
    }

    [Fact]
    public async Task BinWise_reading_of_a_Closing_Stock_day_is_never_added_to_any_stock_report()
    {
        // HEMW FIX-01: on 29 Sep 2026 the R010 BinWise file (really 7 Sep) was stored beside the R011 Closing Stock.
        // FIX-01 step 2 names six reports: Closing, Brand, Slow, Physical, Variance and Movement. Brand and Slow stock are
        // built in the Reports workspace from the Closing Stock rows (grouped by brand; filtered to non-zero, not ACTIVE),
        // so they are checked here the same way, with the Brand Stock Entry window's own query beside them.
        const string store = "SV-R010";
        await Seed(store,
        [
            new("BOTH-A", new(2026, 8, 10), "Purchase Receipt", 0m, 1m),
            new("BOTH-B", new(2026, 8, 10), "Purchase Receipt", 0m, 1m)
        ],
        [
            new("BOTH-A", Aug25, 1m), new("BOTH-B", Aug25, 1m),
            new("BOTH-A", Aug25, 1m, BinWise: true), new("BOTH-B", Aug25, 1m, BinWise: true), new("R010-ONLY", Aug25, -4m, BinWise: true)
        ], ledgerCoversTo: Aug25);
        var scope = new ReportingQueryScope(new(2026, 8, 1), Aug25, [store]);

        var operational = new OperationalReportRepository(database.ConnectionString);
        var closing = await operational.LoadStockInventoryAsync(new(Aug25, Aug25, [store]));
        var brandEntry = await operational.LoadBrandStockEntryAsync(store, Aug25);
        var physical = await operational.LoadPhysicalStockAsync(store, Aug25);
        var variance = await Executor().ExecuteStockReconciliationAsync(scope);
        var movements = await Repository().LoadStockMovementsAsync(scope);

        Assert.Equal([("BOTH-A", 1m), ("BOTH-B", 1m)], closing.OrderBy(x => x.ProductCode).Select(x => (x.ProductCode, x.Quantity)));
        Assert.All(closing, x => Assert.Equal("Closing Stock", x.SnapshotSource));
        // Brand stock: one brand, 2 units (not 4 doubled, not -2 with the R010-only row).
        Assert.Equal([("BRAND-X", 2m)], closing.GroupBy(x => x.Brand ?? "Unmapped").Select(x => (x.Key, x.Sum(y => y.Quantity))));
        Assert.Equal([("BRAND-X", 2m)], brandEntry.Select(x => (x.Brand, x.System)));
        // Slow stock: never-sold, non-zero items only, and no R010-only -4 row.
        Assert.Equal(["BOTH-A", "BOTH-B"], closing.Where(x => x.Quantity != 0 && x.MovementStatus != "ACTIVE").Select(x => x.ProductCode).Order());
        // Physical stock: the system quantity of the brand is 2.
        Assert.Equal([("BRAND-X", 2m)], physical.Select(x => (x.InventoryGroupCode, x.SystemQuantity)));
        Assert.Equal(ReconciliationStatus.Passed, variance.Status);
        Assert.Equal([("BOTH-A", (decimal?)1m), ("BOTH-B", (decimal?)1m)], variance.Items.Select(x => (x.ItemCode, x.ReportedClosing)));
        Assert.Equal(["BOTH-A", "BOTH-B"], movements.Select(x => x.ItemCode));
    }

    [Fact]
    public async Task Gift_cards_are_left_out_of_every_stock_report()
    {
        // Owner answer Q2 (decision 13), Helios report audit R-10: the GIFT CARD item sits at -4 in the ledger and the snapshot;
        // it is not stock, so Closing, Brand, Brand Stock Entry, Physical, Slow, Variance and Movement all leave it out.
        const string store = "SV-GIFT";
        await Seed(store,
        [
            new("WATCH-1", new(2026, 8, 10), "Purchase Receipt", 0m, 1m),
            new("GIFT CARD", new(2026, 8, 12), "INV", -3m, -4m)
        ],
        [new("WATCH-1", Aug25, 1m), new("GIFT CARD", Aug25, -4m)], ledgerCoversTo: Aug25);
        var scope = new ReportingQueryScope(new(2026, 8, 1), Aug25, [store]);

        var operational = new OperationalReportRepository(database.ConnectionString);
        var closing = await operational.LoadStockInventoryAsync(new(Aug25, Aug25, [store]));
        var brandEntry = await operational.LoadBrandStockEntryAsync(store, Aug25);
        var physical = await operational.LoadPhysicalStockAsync(store, Aug25);
        var brandPhysical = await operational.LoadBrandPhysicalStockAsync(store, Aug25);
        var variance = await Executor().ExecuteStockReconciliationAsync(scope);
        var movements = await Repository().LoadStockMovementsAsync(scope);

        Assert.Equal(["WATCH-1"], closing.Select(x => x.ProductCode));
        Assert.Equal([("BRAND-X", 1m)], brandEntry.Select(x => (x.Brand, x.System)));
        Assert.Equal([("BRAND-X", 1m)], physical.Select(x => (x.InventoryGroupCode, x.SystemQuantity)));
        Assert.Equal([("BRAND-X", 1m)], brandPhysical.Select(x => (x.InventoryGroupCode, x.SystemQuantity)));
        Assert.Equal(ReconciliationStatus.Passed, variance.Status);
        Assert.Equal(["WATCH-1"], variance.Items.Select(x => x.ItemCode));
        Assert.Equal(["WATCH-1"], movements.Select(x => x.ItemCode));
    }

    [Fact]
    public async Task A_snapshot_row_with_brand_code_GC_is_a_gift_card_and_a_blank_brand_code_is_kept()
    {
        const string store = "SV-GC";
        await Seed(store, [new("WATCH-2", new(2026, 8, 10), "Purchase Receipt", 0m, 1m)], [new("WATCH-2", Aug25, 1m)], ledgerCoversTo: Aug25);
        await database.ExecuteAsync($"""
            DECLARE @file bigint=(SELECT TOP 1 l.import_file_id FROM dbo.source_lineage l JOIN dbo.stock_snapshots s ON s.source_lineage_id=l.source_lineage_id WHERE s.store_code='{store}');
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Snapshot',99,'CLOSING_STOCK');
            DECLARE @lineage bigint=SCOPE_IDENTITY();
            INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,unit_cost,total_cost,source_lineage_id,source_report_code,brand_code,brand_name)
             VALUES('{store}','20260825',N'GCV-500',N'LOT-2',2,0,0,@lineage,'CLOSING_STOCK',N'GC',N'GIFT VOUCHER');
            """);

        var closing = await new OperationalReportRepository(database.ConnectionString).LoadStockInventoryAsync(new(Aug25, Aug25, [store]));

        // WATCH-2 has no brand_code (NULL): NOT (gift card) must still keep it.
        Assert.Equal(["WATCH-2"], closing.Select(x => x.ProductCode));
    }

    [Fact]
    public async Task Slow_stock_ages_by_receipt_date_and_shows_recent_receipts_as_new()
    {
        // Owner answer Q8 (decision 14): an item received 5 days ago and not sold is NEW; one received 120 days ago is NEVER
        // SOLD aged 120 days; an item with no ledger receipt stays NEVER SOLD with no age.
        const string store = "SV-AGE";
        await Seed(store,
        [
            new("RECEIVED-OLD", Aug25.AddDays(-120), "Purchase Receipt", 0m, 1m),
            new("RECEIVED-NEW", Aug25.AddDays(-5), "STM Receipt", 0m, 1m),
            // An outward movement is not a receipt.
            new("RECEIVED-NEW", Aug25.AddDays(-2), "STM Issue", 1m, 1m)
        ],
        [new("RECEIVED-NEW", Aug25, 1m), new("RECEIVED-OLD", Aug25, 1m), new("NO-LEDGER", Aug25, 1m)], ledgerCoversTo: Aug25);

        var rows = (await new OperationalReportRepository(database.ConnectionString).LoadStockInventoryAsync(new(Aug25, Aug25, [store])))
            .ToDictionary(x => x.ProductCode);

        Assert.Equal((StockAgeing.New, (DateOnly?)Aug25.AddDays(-5), (int?)5), (rows["RECEIVED-NEW"].MovementStatus, rows["RECEIVED-NEW"].LastReceiptDate, rows["RECEIVED-NEW"].DaysSinceReceipt));
        Assert.Equal((StockAgeing.NeverSold, (int?)120), (rows["RECEIVED-OLD"].MovementStatus, rows["RECEIVED-OLD"].DaysSinceReceipt));
        Assert.Equal((StockAgeing.NeverSold, (DateOnly?)null, (int?)null), (rows["NO-LEDGER"].MovementStatus, rows["NO-LEDGER"].LastReceiptDate, rows["NO-LEDGER"].DaysSinceReceipt));
    }

    private SqlServerReportingQueryRepository Repository() => new(database.ConnectionString);

    private SqlBackedReportingExecutor Executor() => new(Repository(), RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales,
        RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);

    private sealed record Move(string Item, DateOnly Date, string Type, decimal Opening, decimal Closing);
    private sealed record Snap(string Item, DateOnly Date, decimal Quantity, bool BinWise = false);

    // Rows are inserted in the order given, so stock_movement_id follows that order, as an import in file order does.
    private async Task Seed(string store, IReadOnlyList<Move> moves, IReadOnlyList<Snap> snapshots, DateOnly ledgerCoversTo,
        IReadOnlyList<DateOnly>? saleDates = null)
    {
        static string D(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        static string N(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        var first = moves.Min(x => x.Date);
        var sql = new StringBuilder($"""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@lineage bigint;
            INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count)
             VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),{moves.Count + snapshots.Count});
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
             VALUES(@batch,'variance-ledger.xlsx',LEFT(REPLACE(CONCAT(NEWID(),NEWID()),'-',''),64),2048,'STOCK_LEDGER','{store}','{D(ledgerCoversTo)}','{D(first)}','{D(ledgerCoversTo)}',1);
            SET @file=SCOPE_IDENTITY();

            """);
        var row = 2;
        foreach (var move in moves)
        {
            sql.AppendLine($"INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet0',{row},'STOCK_LEDGER'); SET @lineage=SCOPE_IDENTITY();");
            sql.AppendLine($"""
                INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id)
                 VALUES('{store}','DOC-{row}',2027,'{D(move.Date)}',N'{move.Item}',N'{move.Type}',{N(move.Opening)},{N(move.Closing - move.Opening)},{N(move.Closing)},@lineage);
                """);
            row++;
        }
        foreach (var snapshot in snapshots)
        {
            var (record, source) = snapshot.BinWise ? ("R010_SNAPSHOT", "R010") : ("CLOSING_STOCK", "CLOSING_STOCK");
            sql.AppendLine($"INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Snapshot',{row},'{record}'); SET @lineage=SCOPE_IDENTITY();");
            sql.AppendLine($"""
                INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,batch_number,quantity,unit_cost,total_cost,source_lineage_id,source_report_code,brand_name)
                 VALUES('{store}','{D(snapshot.Date)}',N'{snapshot.Item}',N'LOT-1',{N(snapshot.Quantity)},10,{N(snapshot.Quantity * 10m)},@lineage,'{source}',N'BRAND-X');
                """);
            row++;
        }
        // Invoice headers only: the ledger coverage check looks for a sale of the store after the ledger's last day.
        var invoice = 1;
        foreach (var sale in saleDates ?? [])
            sql.AppendLine($"INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('{store}',N'INV-{invoice++}',2027,'{D(sale)}');");
        await database.ExecuteAsync(sql.ToString());
    }
}
