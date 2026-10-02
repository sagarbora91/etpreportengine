using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Stock movement identity (IF-018, migration 0041 section C). Every test uses its own store, date and document
// numbers, so the shared database never lets one test's file overlap another's period.
public sealed class StockIdentitySqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Per_unit_rows_get_chain_line_seq_in_any_row_order()
    {
        // The package's per-unit groups: openings are distinct and the file order is not monotone.
        var rising = new[] { 0m, 1m, 3m, 4m, 2m };
        var falling = new[] { 3m, 5m, 1m, 4m, 2m };
        foreach (var (store, reverse) in new[] { ("WLMHW", false), ("HEMW", true) })
        {
            var rows = rising.Select(opening => Unit("STM Receipt", store, "CHAIN-ITEM", "CHAIN-UP", Day(10), opening, 1m))
                .Concat(falling.Select(opening => Unit("INV", store, "CHAIN-ITEM", "CHAIN-DOWN", Day(10), opening, -1m))).ToArray();
            await Import(reverse ? rows.Reverse().ToArray() : rows);
        }

        foreach (var store in new[] { "WLMHW", "HEMW" })
        {
            var up = await LineSeqByOpening(store, "CHAIN-UP");
            Assert.Equal([(0m, 1), (1m, 2), (2m, 3), (3m, 4), (4m, 5)], up);
            var down = await LineSeqByOpening(store, "CHAIN-DOWN");
            Assert.Equal([(1m, 5), (2m, 4), (3m, 3), (4m, 2), (5m, 1)], down);
        }
    }

    [Fact]
    public async Task Reimport_of_per_unit_ledger_is_already_present()
    {
        var first = await Import([.. new[] { 0m, 1m, 3m, 4m, 2m }.Select(opening => Unit("STM Receipt", "WLMHW", "RE-ITEM", "RE-DOC", Day(12), opening, 1m))]);
        Assert.Equal(5, first.PersistedRows);

        // A later export of the same day in another row order, with one movement more (present + new, 0 conflicts).
        var later = await Import([
            .. new[] { 2m, 4m, 0m, 3m, 1m }.Select(opening => Unit("STM Receipt", "WLMHW", "RE-ITEM", "RE-DOC", Day(12), opening, 1m)),
            Unit("STM Receipt", "WLMHW", "RE-OTHER", "RE-DOC", Day(12), 0m, 1m)]);

        Assert.Equal(5, await Scalar($"SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id={later.ImportFileId} AND outcome='ALREADY_PRESENT'"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id={later.ImportFileId} AND outcome='NEW'"));
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id={later.ImportFileId} AND outcome='CONFLICT'"));
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM dbo.import_conflicts WHERE import_file_id={later.ImportFileId}"));
        Assert.Equal(6, await Scalar("SELECT COUNT(*) FROM dbo.stock_movements WHERE document_number='RE-DOC'"));
        Assert.Equal(1, await Scalar($"""
            SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id={later.ImportFileId}
              AND business_identity LIKE N'%/RE-ITEM/%/#5' AND outcome='ALREADY_PRESENT'
            """));
    }

    [Fact]
    public async Task Full_group_import_over_a_stored_row_that_is_not_the_chain_start_adds_the_missing_units()
    {
        // Before 0041 the identity kept the first row in file order (opening 1), and 0041 left it on line 1.
        var stored = await Import([Unit("STM Receipt", "WLMHW", "AL-ITEM", "AL-DOC", Day(17), 1m, 1m)]);
        Assert.Equal(1, await Scalar("SELECT line_seq FROM dbo.stock_movements WHERE document_number='AL-DOC'"));

        // The whole group in the package's file order; its chain start (opening 0) belongs on line 1.
        var full = await Import([.. new[] { 1m, 2m, 0m, 3m, 4m }.Select(opening => Unit("STM Receipt", "WLMHW", "AL-ITEM", "AL-DOC", Day(17), opening, 1m))]);

        Assert.Equal("ALREADY_PRESENT:1,NEW:4", await database.ExecuteAsync($"""
            SELECT STRING_AGG(CONCAT(outcome,':',n),',') WITHIN GROUP(ORDER BY outcome)
            FROM (SELECT outcome,COUNT(*) n FROM dbo.import_row_outcomes WHERE import_file_id={full.ImportFileId} GROUP BY outcome) x
            """));
        Assert.Equal(0, await Scalar($"SELECT COUNT(*) FROM dbo.import_conflicts WHERE import_file_id={full.ImportFileId}"));
        // Every unit once: the stored row keeps line 1, the others take lines 2-5 in chain order.
        Assert.Equal([(0m, 2), (1m, 1), (2m, 3), (3m, 4), (4m, 5)], await LineSeqByOpening("WLMHW", "AL-DOC"));
        Assert.NotEqual(stored.ImportFileId, full.ImportFileId);

        // The same file again is all present.
        var again = await Import([.. new[] { 4m, 3m, 2m, 1m, 0m }.Select(opening => Unit("STM Receipt", "WLMHW", "AL-ITEM", "AL-DOC", Day(17), opening, 1m)),
            Unit("STM Receipt", "WLMHW", "AL-OTHER", "AL-DOC", Day(17), 0m, 1m)]);
        Assert.Equal(5, await Scalar($"SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id={again.ImportFileId} AND outcome='ALREADY_PRESENT'"));
        Assert.Equal(6, await Scalar("SELECT COUNT(*) FROM dbo.stock_movements WHERE document_number='AL-DOC'"));
    }

    [Fact]
    public async Task Full_group_import_over_a_stored_row_with_other_values_still_conflicts()
    {
        await Import([Unit("STM Receipt", "HEMW", "AL-ITEM", "AL-DIFF", Day(18), 7m, 1m)]);

        var refused = await Assert.ThrowsAsync<ImportConflictException>(() => Import(
            [.. new[] { 1m, 2m, 0m }.Select(opening => Unit("STM Receipt", "HEMW", "AL-ITEM", "AL-DIFF", Day(18), opening, 1m))]));

        Assert.Equal(1, refused.Count);
        Assert.Equal(1, await Scalar("SELECT COUNT(*) FROM dbo.stock_movements WHERE document_number='AL-DIFF'"));
    }

    [Fact]
    public async Task Procedure_matches_on_line_seq_and_logs_conflicts_under_the_ledger_report_code()
    {
        var outcome = await Import([.. new[] { 0m, 1m }.Select(opening => Unit("STM Receipt", "HEMW", "CF-ITEM", "CF-DOC", Day(16), opening, 1m))]);
        string Persist(int row, decimal opening, decimal transaction, string lineSeq) => $"""
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES({outcome.ImportFileId},'Direct',{row},'STOCK_MOVEMENT');
            DECLARE @lineage bigint=SCOPE_IDENTITY();
            EXEC dbo.persist_stock_movement 'HEMW',N'CF-DOC',2027,'20260816',N'CF-ITEM',N'STM Receipt',NULL,N'HEMW',{opening},{transaction},{opening + transaction},@lineage{lineSeq};
            SELECT CONCAT(outcome,'|',business_identity) FROM dbo.import_row_outcomes WHERE source_lineage_id=@lineage;
            """;

        // A caller that passes no line_seq still means the first unit of the chain.
        var present = (string)(await database.ExecuteAsync(Persist(1, 0m, 1m, "")))!;
        Assert.StartsWith("ALREADY_PRESENT|", present);
        Assert.EndsWith("/HEMW/#1", present);
        Assert.StartsWith("ALREADY_PRESENT|", (string)(await database.ExecuteAsync(Persist(2, 1m, 1m, ",2")))!);
        Assert.StartsWith("CONFLICT|", (string)(await database.ExecuteAsync(Persist(3, 0m, 2m, ",2")))!);

        Assert.Equal("STOCK_LEDGER", await database.ExecuteAsync($"SELECT report_code FROM dbo.import_conflicts WHERE import_file_id={outcome.ImportFileId}"));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) FROM dbo.stock_movements WHERE document_number='CF-DOC'"));
    }

    [Fact]
    public async Task Movement_identity_index_rejects_duplicates()
    {
        var outcome = await Import([Unit("STM Issue", "WLMHW", "IX-ITEM", "IX-DOC", Day(13), 4m, -1m)]);
        Assert.Equal(1, await Scalar("""
            SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.stock_movements')
              AND name='UX_stock_movements_identity' AND is_unique=1
            """));

        // A blank location is the same identity as a missing one, so this copy of the row is refused.
        var duplicate = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(CopyMovement(outcome.ImportFileId, 1, lineSeq: 1)));
        Assert.Equal(2601, duplicate.Number);
        Assert.Contains("UX_stock_movements_identity", duplicate.Message);

        await database.ExecuteAsync(CopyMovement(outcome.ImportFileId, 2, lineSeq: 2));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) FROM dbo.stock_movements WHERE document_number='IX-DOC'"));
    }

    [Fact]
    public async Task Exact_ledger_repeats_are_kept_and_warned()
    {
        var book = Ledger([
            Unit("STM Receipt", "WLMHW", "REP-ITEM", "REP-DOC", Day(14), 1m, 1m),
            Unit("STM Receipt", "WLMHW", "REP-ITEM", "REP-DOC", Day(14), 0m, 1m),
            Unit("STM Receipt", "WLMHW", "REP-ITEM", "REP-DOC", Day(14), 1m, 1m)]);
        var accepted = new MatchedImportEnvelopeFactory(["WLMHW"]).RequireAccepted(book);
        var result = await new SqlServerImportPersistenceUseCase(database.ConnectionString)
            .PersistAsync(new(accepted, Day(14), "WLMHW", "Stock identity SQL test"));

        Assert.Equal("Imported", result.Status);
        var warning = Assert.Single(result.Issues);
        Assert.Equal(ImportCodes.StockRowRepeated, warning.Code);
        Assert.Equal(ImportIssueSeverity.Warning, warning.Severity);
        Assert.Equal(2, warning.Occurrences);
        Assert.Equal(2, warning.SourceRow);
        Assert.DoesNotContain("WLMHW", warning.DocumentRef);

        Assert.Equal([(0m, 1), (1m, 2), (1m, 3)], await LineSeqByOpening("WLMHW", "REP-DOC"));
        Assert.Equal(3, await Scalar("""
            SELECT COUNT(*) FROM dbo.import_row_outcomes o JOIN dbo.stock_movements m ON m.source_lineage_id=o.source_lineage_id
            WHERE m.document_number='REP-DOC' AND o.outcome='NEW'
            """));
    }

    [Fact]
    public async Task First_movement_uses_chain_start_in_any_insert_order()
    {
        // Rows insert in file order, so the lowest stock_movement_id is not the start of the chain.
        await Import([
            .. new[] { 3m, 0m, 4m, 1m, 2m }.Select(opening => Unit("STM Receipt", "HEMW", "FM-ITEM-1", "FM-DOC", Day(15), opening, 1m)),
            .. new[] { 2m, 4m, 1m, 0m, 3m }.Select(opening => Unit("STM Receipt", "HEMW", "FM-ITEM-2", "FM-DOC", Day(15), opening, 1m))]);
        await new StockSqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString)).PersistAsync(Book(
            "closing.xlsx", StockImportProfiles.ClosingStockHeaders,
            [Closing("FM-ITEM-1", Day(15), 5m), Closing("FM-ITEM-2", Day(15), 5m)]));
        Assert.Equal(3m, await database.ExecuteAsync("""
            SELECT TOP(1) opening_quantity FROM dbo.stock_movements WHERE product_code='FM-ITEM-1' ORDER BY stock_movement_id
            """));

        var stock = await new SqlServerReportingQueryRepository(database.ConnectionString)
            .LoadStockAsync(new ReportingQueryScope(Day(15), Day(15), ["HEMW"], ItemCodes: ["FM-ITEM-1", "FM-ITEM-2"]));

        Assert.Equal(2, stock.Positions.Count);
        Assert.All(stock.Positions, position =>
        {
            Assert.Equal(0m, position.SourceOpeningQuantity);
            Assert.Equal(5m, position.SourceClosingQuantity);
        });
    }

    private static DateOnly Day(int day) => new(2026, 8, day);

    private async Task<StockImportPersistenceOutcome> Import(IReadOnlyList<object?[]> rows) =>
        await new StockSqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString)).PersistAsync(Ledger(rows));

    private async Task<int> Scalar(string sql) => Convert.ToInt32(await database.ExecuteAsync(sql));

    private async Task<IReadOnlyList<(decimal Opening, int LineSeq)>> LineSeqByOpening(string store, string document)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT opening_quantity,line_seq FROM dbo.stock_movements WHERE store_code=@store AND document_number=@doc
            ORDER BY opening_quantity,line_seq
            """, connection);
        command.Parameters.AddWithValue("@store", store);
        command.Parameters.AddWithValue("@doc", document);
        var rows = new List<(decimal, int)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add((reader.GetDecimal(0), reader.GetInt32(1)));
        return rows;
    }

    // The imported row has a TO LOCATION and no FROM LOCATION; the copy stores a blank FROM LOCATION instead.
    private static string CopyMovement(long file, int row, int lineSeq) => $"""
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES({file},'Copy',{row},'STOCK_MOVEMENT');
        INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,
          from_location,to_location,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id,line_seq)
        SELECT store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,N'',to_location,
          opening_quantity,transaction_quantity,closing_quantity,SCOPE_IDENTITY(),{lineSeq}
        FROM dbo.stock_movements WHERE document_number='IX-DOC' AND line_seq=1;
        """;

    private static object?[] Unit(string type, string store, string item, string document, DateOnly date, decimal opening, decimal transaction) =>
        [type, store, "Store", item, "HSN", "BR", "Brand", "Cluster", "U", document, date.ToDateTime(TimeOnly.MinValue), null, store,
         null, null, opening, transaction, opening + transaction, "City", "State", "Location"];

    private static object?[] Closing(string item, DateOnly date, decimal quantity) =>
        ["HEMW", "Store", "Retail", "Store", "Region", "State", "City", date.ToDateTime(TimeOnly.MinValue), item, "HSN",
         "Description", "EAN", "BR", "Cluster", "U", quantity, 10m, quantity * 10m, null, null];

    private static WorkbookSnapshot Ledger(IReadOnlyList<object?[]> rows) =>
        Book("ledger.xlsx", StockImportProfiles.VariantStockLedgerHeaders, rows);

    private static WorkbookSnapshot Book(string name, IReadOnlyList<string> headers, IReadOnlyList<object?[]> rows) =>
        new(name, 10, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            [new("Sheet0", 1, headers, rows.Select((values, index) => new WorkbookRow(index + 2, values.Select(value => new WorkbookCell(value)).ToArray())).ToArray())]);
}
