using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using static Etp.Reporting.TestSupport.StackedBinWiseWorkbooks;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Review 1.9.3, dating-restatement-fy finding 1. A stacked R010's period is min..max of its blocks (2 Jul..29 Sep), but
// it holds only those three snapshots. Each row's snapshot date is part of its content key, so planner 1 matches a
// single-date R010 only against the block of its own date, in either import order. Each test uses its own store.
public sealed class StackedSnapshotSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Single_date_R010_inside_a_stacked_range_is_stored_not_called_a_duplicate()
    {
        const string store = "STACKA";
        var importer = new EtpFamilySqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString));
        var stacked = await importer.PersistAsync(Accept(Stacked(store, "a1")));

        // 15 Sep lies inside the stacked period, and each of its rows repeats a block row unchanged. Before the fix it
        // was superseded as "Duplicate content" and no 15 Sep snapshot was stored.
        var september = await importer.PersistAsync(Accept(Single(store, "202609151200", "a2", Units)));

        Assert.Equal("2026-07-02:2,2026-08-07:2,2026-09-15:2,2026-09-29:3", await SnapshotCounts(store));
        Assert.False(await IsSuperseded(september));
        Assert.False(await IsSuperseded(stacked));
        Assert.Equal(3m, await database.ExecuteAsync($"SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='{store}' AND snapshot_date='20260915'"));

        // A 7 Aug export equal to the 7 Aug block is still a duplicate of the stacked file, and stores nothing twice.
        var duplicate = await importer.PersistAsync(Accept(Single(store, "202608071900", "a3", Units)));
        Assert.Equal(stacked, await SupersededBy(duplicate));
        // A 7 Aug export that changes a unit is another reading of a stored snapshot: it must be restated, not added.
        var changed = await Assert.ThrowsAsync<ImportSourceException>(() =>
            importer.PersistAsync(Accept(Single(store, "202608072000", "a4", ("UNIT-A", 5m), ("UNIT-B", 2m)))));
        Assert.Equal("IMPORT_PERIOD_ALREADY_PRESENT", changed.Code);

        Assert.Equal("2026-07-02:2,2026-08-07:2,2026-09-15:2,2026-09-29:3", await SnapshotCounts(store));
        Assert.Equal(3m, await database.ExecuteAsync($"SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='{store}' AND snapshot_date='20260807'"));
    }

    [Fact]
    public async Task Stacked_R010_promotes_current_single_date_files_onto_rows_of_their_own_dates()
    {
        const string store = "STACKB";
        var importer = new EtpFamilySqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString));
        // Two current single-date files whose rows are the same units: before the fix both were promoted onto the
        // stacked file's first (2 Jul) rows, and the import failed with SQL 2627 on a reused row reference.
        var july = await importer.PersistAsync(Accept(Single(store, "202607021446", "b1", Units)));
        var august = await importer.PersistAsync(Accept(Single(store, "202608071848", "b2", Units)));

        var stacked = await importer.PersistAsync(Accept(Stacked(store, "b3")));

        Assert.Equal(stacked, await SupersededBy(july));
        Assert.Equal(stacked, await SupersededBy(august));
        Assert.False(await IsSuperseded(stacked));
        // No snapshot is doubled, and every kept fact now points at the stacked file's row in the block of its own date.
        Assert.Equal("2026-07-02:2,2026-08-07:2,2026-09-29:3", await SnapshotCounts(store));
        Assert.Equal("2026-07-02:2-3,2026-08-07:4-5,2026-09-29:6-8", await database.ExecuteAsync($"""
            SELECT STRING_AGG(CONCAT(CONVERT(char(10),snapshot_date,23),':',first_row,'-',last_row),',') WITHIN GROUP(ORDER BY snapshot_date)
            FROM (SELECT s.snapshot_date,MIN(l.source_row_number) first_row,MAX(l.source_row_number) last_row
                  FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id
                  WHERE s.store_code='{store}' AND l.import_file_id={stacked} GROUP BY s.snapshot_date) x
            """));
        Assert.Equal(0, await database.ExecuteAsync($"""
            SELECT COUNT(*) FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id
            WHERE s.store_code='{store}' AND l.import_file_id<>{stacked}
            """));
        Assert.Equal(3m, await database.ExecuteAsync($"SELECT SUM(quantity) FROM dbo.v_stock_snapshots_effective WHERE store_code='{store}' AND snapshot_date='20260807'"));
    }

    [Fact]
    public async Task Stacked_R010_after_a_changed_single_date_file_must_be_restated()
    {
        const string store = "STACKC";
        var importer = new EtpFamilySqlImportOrchestrator(new SqlServerTransactionalImportStore(database.ConnectionString));
        // The current 7 Aug reading differs from the stacked file's 7 Aug block: no automatic promotion may replace it.
        var august = await importer.PersistAsync(Accept(Single(store, "202608071848", "c1", ("UNIT-A", 4m), ("UNIT-B", 2m))));

        var refused = await Assert.ThrowsAsync<ImportSourceException>(() => importer.PersistAsync(Accept(Stacked(store, "c2"))));

        Assert.Equal("IMPORT_PERIOD_ALREADY_PRESENT", refused.Code);
        Assert.False(await IsSuperseded(august));
        Assert.Equal("2026-08-07:2", await SnapshotCounts(store));
    }

    private static MatchedImportEnvelope Accept(WorkbookSnapshot workbook) => new MatchedImportEnvelopeFactory().RequireAccepted(workbook);

    private async Task<string?> SnapshotCounts(string store) => await database.ExecuteAsync($"""
        SELECT STRING_AGG(CONCAT(CONVERT(char(10),snapshot_date,23),':',n),',') WITHIN GROUP(ORDER BY snapshot_date)
        FROM (SELECT snapshot_date,COUNT(*) n FROM dbo.stock_snapshots WHERE store_code='{store}' GROUP BY snapshot_date) x
        """) as string;

    private async Task<bool> IsSuperseded(long file) =>
        Convert.ToBoolean(await database.ExecuteAsync($"SELECT is_superseded FROM dbo.import_files WHERE import_file_id={file}"));

    private async Task<long?> SupersededBy(long file) =>
        await database.ExecuteAsync($"SELECT superseded_by_import_file_id FROM dbo.import_files WHERE import_file_id={file}") is long id ? id : null;
}
