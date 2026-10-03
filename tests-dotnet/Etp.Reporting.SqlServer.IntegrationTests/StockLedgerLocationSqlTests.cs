using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// 1.9.4, WLMHW report audit FIX-14 (migration 0047): the stock ledger's LOCATION (bin) is stored on the movement, kept
// out of its identity, and the Stock Variance opening is chained per bin. Every test uses its own store, dates and
// document numbers, so the shared database never lets one test's file overlap another's period.
public sealed class StockLedgerLocationSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Bin_is_stored_and_the_opening_comes_from_each_bins_own_chain()
    {
        // 1 Sep: a defective unit is received into DEFECTIVEBIN (0 -> 1). 2 Sep: one RETAILBIN unit is sold (5 -> 4).
        // The item opened the period with 5 units (RETAILBIN 5 + DEFECTIVEBIN 0) and closes with 5 (4 + 1).
        // 1.9.3 took the first movement's opening over all bins (0, the DEFECTIVEBIN chain) and reported a variance of -5.
        await Import([
            Unit("Stock Receipt", "WLMHW", "LOC-ITEM", "LOC-DOC-1", Day(1), 0m, 1m, "DEFECTIVEBIN"),
            Unit("INV", "WLMHW", "LOC-ITEM", "LOC-DOC-2", Day(2), 5m, -1m, "RETAILBIN")]);
        await new StockSqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString)).PersistAsync(Book(
            "closing.xlsx", StockImportProfiles.ClosingStockHeaders, [Closing("WLMHW", "LOC-ITEM", Day(2), 5m)]));

        Assert.Equal("DEFECTIVEBIN", await database.ExecuteAsync("SELECT location FROM dbo.stock_movements WHERE document_number='LOC-DOC-1'"));
        Assert.Equal("RETAILBIN", await database.ExecuteAsync("SELECT location FROM dbo.stock_movements WHERE document_number='LOC-DOC-2'"));

        var stock = await new SqlServerReportingQueryRepository(database.ConnectionString)
            .LoadStockAsync(new ReportingQueryScope(Day(1), Day(2), ["WLMHW"], ItemCodes: ["LOC-ITEM"]));
        var position = Assert.Single(stock.Positions);
        Assert.Equal(5m, position.SourceOpeningQuantity);
        Assert.Equal(5m, position.SourceClosingQuantity);
        // The Stock Movement report keeps one row per bin; the item total still adds the bins together.
        Assert.Equal("DEFECTIVEBIN|RETAILBIN", string.Join("|", stock.Movements.Select(movement => movement.Location).Order(StringComparer.Ordinal)));
        Assert.Equal(0m, stock.Movements.Sum(movement => movement.SourceSignedQuantity));
    }

    [Fact]
    public async Task A_movement_with_no_bin_is_not_a_chain_of_its_own_so_its_opening_is_not_counted_twice()
    {
        // 1 Sep: a RETAILBIN sale (5 -> 4) whose bin stayed unknown (no typed row, an ambiguous backfill or a locked day).
        // 2 Sep: the next RETAILBIN sale (4 -> 3) with its bin. Summing a NULL chain (5) and a RETAILBIN chain (4) would open
        // at 9 and report a false variance of -4; the item opened with 5.
        await Import([
            Unit("INV", "WLMHW", "NUL-ITEM", "NUL-DOC-1", Day(8), 5m, -1m, "RETAILBIN"),
            Unit("INV", "WLMHW", "NUL-ITEM", "NUL-DOC-2", Day(9), 4m, -1m, "RETAILBIN")]);
        await database.ExecuteAsync("UPDATE dbo.stock_movements SET location=NULL WHERE document_number='NUL-DOC-1'");
        await new StockSqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString)).PersistAsync(Book(
            "closing.xlsx", StockImportProfiles.ClosingStockHeaders, [Closing("WLMHW", "NUL-ITEM", Day(9), 3m)]));

        var stock = await new SqlServerReportingQueryRepository(database.ConnectionString)
            .LoadStockAsync(new ReportingQueryScope(Day(8), Day(9), ["WLMHW"], ItemCodes: ["NUL-ITEM"]));
        var position = Assert.Single(stock.Positions);
        Assert.Equal(5m, position.SourceOpeningQuantity);
        Assert.Equal(-2m, stock.Movements.Sum(movement => movement.SourceSignedQuantity));
    }

    [Fact]
    public async Task Migration_backfills_the_bin_of_existing_rows_from_the_typed_ledger_and_is_idempotent()
    {
        await Import([
            Unit("Stock Receipt", "HEMW", "BF-ITEM", "BF-DOC", Day(4), 0m, 1m, "DEFECTIVEBIN"),
            Unit("Purchase Receipt", "HEMW", "BF-ITEM", "BF-DOC-R", Day(4), 2m, 1m, "RETAILBIN")]);
        // A movement stored before 0047 had no bin.
        await database.ExecuteAsync("UPDATE dbo.stock_movements SET location=NULL WHERE document_number IN ('BF-DOC','BF-DOC-R')");

        var migration = await File.ReadAllTextAsync(Path.Combine(database.MigrationDirectory, "0047_stock_ledger_location.sql"));
        await database.ExecuteAsync(migration);
        Assert.Equal("DEFECTIVEBIN", await database.ExecuteAsync("SELECT location FROM dbo.stock_movements WHERE document_number='BF-DOC'"));
        Assert.Equal("RETAILBIN", await database.ExecuteAsync("SELECT location FROM dbo.stock_movements WHERE document_number='BF-DOC-R'"));

        await database.ExecuteAsync(migration);
        Assert.Equal(2, Convert.ToInt32(await database.ExecuteAsync("""
            SELECT COUNT(*) FROM dbo.stock_movements WHERE document_number IN ('BF-DOC','BF-DOC-R') AND location IS NOT NULL
            """)));
        Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("""
            SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.stock_movements') AND name='location' AND is_nullable=1
            """)));
    }

    [Fact]
    public async Task Later_export_fills_a_missing_bin_on_the_already_present_row_without_a_conflict()
    {
        await Import([Unit("Purchase Receipt", "WLMHW", "RF-ITEM", "RF-DOC", Day(6), 0m, 1m, "RETAILBIN")]);
        await database.ExecuteAsync("UPDATE dbo.stock_movements SET location=NULL WHERE document_number='RF-DOC'");

        var later = await Import([
            Unit("Purchase Receipt", "WLMHW", "RF-ITEM", "RF-DOC", Day(6), 0m, 1m, "RETAILBIN"),
            Unit("Purchase Receipt", "WLMHW", "RF-OTHER", "RF-DOC", Day(6), 0m, 1m, "DEFECTIVEBIN")]);

        Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.import_conflicts WHERE import_file_id={later.ImportFileId}")));
        Assert.Equal("ALREADY_PRESENT:1,NEW:1", await database.ExecuteAsync($"""
            SELECT STRING_AGG(CONCAT(outcome,':',n),',') WITHIN GROUP(ORDER BY outcome)
            FROM (SELECT outcome,COUNT(*) n FROM dbo.import_row_outcomes WHERE import_file_id={later.ImportFileId} GROUP BY outcome) x
            """));
        Assert.Equal("RETAILBIN", await database.ExecuteAsync("SELECT location FROM dbo.stock_movements WHERE document_number='RF-DOC' AND product_code='RF-ITEM'"));
        Assert.Equal("DEFECTIVEBIN", await database.ExecuteAsync("SELECT location FROM dbo.stock_movements WHERE document_number='RF-DOC' AND product_code='RF-OTHER'"));
    }

    private static DateOnly Day(int day) => new(2026, 9, day);

    private async Task<StockImportPersistenceOutcome> Import(IReadOnlyList<object?[]> rows) =>
        await new StockSqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString))
            .PersistAsync(Book("ledger.xlsx", StockImportProfiles.VariantStockLedgerHeaders, rows));

    private static object?[] Unit(string type, string store, string item, string document, DateOnly date, decimal opening, decimal transaction, string bin) =>
        [type, store, "Store", item, "HSN", "BR", "Brand", "Cluster", "U", document, date.ToDateTime(TimeOnly.MinValue), null, null,
         null, null, opening, transaction, opening + transaction, "City", "State", bin];

    private static object?[] Closing(string store, string item, DateOnly date, decimal quantity) =>
        [store, "Store", "Retail", "Store", "Region", "State", "City", date.ToDateTime(TimeOnly.MinValue), item, "HSN",
         "Description", "EAN", "BR", "Cluster", "U", quantity, 10m, quantity * 10m, null, null];

    private static WorkbookSnapshot Book(string name, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows) =>
        new(name, 10, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            [new("Sheet0", 1, headers, rows.Select((values, index) => new WorkbookRow(index + 2, values.Select(value => new WorkbookCell(value)).ToArray())).ToArray())]);
}
