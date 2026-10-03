using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// The real 1.9.3 upgrade of the shop database: from 0037 (1.9.2) through Tally 0038-0040, then the
// import engine's 0041 and 0042, in one runner call. Review 1.9.3 findings 2 and 3.
public sealed class Migration0041UpgradePathTests
{
    private const string Released = "0037_accounting_invariants";

    [Fact]
    public async Task A_0037_database_with_data_upgrades_through_Tally_then_the_import_engine_in_one_run()
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before(source, "0038")).BootstrapAsync();
        var store = new SqlServerMigrationStore(db.ConnectionString);
        Assert.Equal(Released, (await store.GetAppliedAsync()).Select(migration => migration.Id).Max(StringComparer.Ordinal));
        await db.ExecuteAsync(Seed);
        await db.ExecuteAsync("INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('UPGRADE','20260825','LOCKED',SUSER_SNAME(),SYSUTCDATETIME())");
        var facts = await db.ExecuteAsync(FactCounts);

        var applied = await new MigrationRunner(source, store).RunAsync();

        var pending = (await source.DiscoverAsync()).Select(migration => migration.Id).Where(id => string.CompareOrdinal(id, "0038") >= 0).ToArray();
        Assert.Equal(pending, applied);
        Assert.Equal(["0038_tally_transfer_foundation", "0039_tally_findings_readbacks_reconciliation", "0040_tally_review_fixes", "0041_import_engine_fixes"], applied.Take(4));
        Assert.Equal(pending, (await store.GetAppliedAsync()).Select(migration => migration.Id).Where(id => string.CompareOrdinal(id, "0038") >= 0).Order(StringComparer.Ordinal));

        // Every fact is still there, and 0041 numbered and indexed what it found.
        Assert.Equal(facts, await db.ExecuteAsync(FactCounts));
        Assert.Equal(1, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.tables WHERE name='tally_profiles'"));
        Assert.Equal("1,2", await db.ExecuteAsync("SELECT STRING_AGG(line_seq,',') WITHIN GROUP(ORDER BY line_seq) FROM dbo.stock_movements WHERE store_code='UPGRADE'"));
        Assert.Equal("CLOSING_STOCK", await db.ExecuteAsync("SELECT STRING_AGG(source_report_code,',') FROM dbo.stock_snapshots WHERE store_code='UPGRADE'"));
        Assert.Equal(3, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.indexes WHERE name IN('UX_sales_invoice_controls_invoice','UX_sales_tenders_invoice_type','UX_stock_movements_identity')"));
        Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.triggers WHERE parent_id IN(OBJECT_ID(N'dbo.stock_movements'),OBJECT_ID(N'dbo.stock_snapshots')) AND is_disabled=1"));
        Assert.Empty(await new MigrationRunner(source, store).RunAsync());

        // An older release now says the database is newer than it, and changes nothing.
        var older = await Assert.ThrowsAsync<MigrationIntegrityException>(() => new MigrationRunner(new Before(source, "0038"), store).RunAsync());
        Assert.Contains("upgraded by a newer release of ETP", older.Message, StringComparison.Ordinal);
        Assert.Contains($"'{pending[^1]}'", older.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('UPGRADE',N'100000777',2026,'20260401')",
        ImportCodes.SqlErrors.FinancialYearPrecheck)]
    [InlineData("INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id) SELECT i.sales_invoice_id,'INV',1,100,'INR',l.source_lineage_id FROM dbo.sales_invoices i CROSS JOIN dbo.source_lineage l WHERE i.store_code='UPGRADE' AND l.source_row_number=2",
        ImportCodes.SqlErrors.DuplicateControlsPrecheck)]
    [InlineData("INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id) SELECT i.sales_invoice_id,'Cash',50,'INR',l.source_lineage_id FROM dbo.sales_invoices i CROSS JOIN dbo.source_lineage l WHERE i.store_code='UPGRADE' AND l.source_row_number=2",
        ImportCodes.SqlErrors.DuplicateTendersPrecheck)]
    public async Task A_0041_precheck_refusal_leaves_a_0037_database_at_0037_without_Tally(string blocking, int number)
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        var released = new Before(source, "0038");
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, released).BootstrapAsync();
        await db.ExecuteAsync(Seed);
        await db.ExecuteAsync(blocking);
        var store = new SqlServerMigrationStore(db.ConnectionString);
        var before = await store.GetAppliedAsync();

        var refusal = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(source, store).RunAsync());

        // Before the pre-flight, Tally 0038-0040 committed here and the database was left at 0040:
        // 1.9.2 refused it ("Applied migration '0038_tally_transfer_foundation' is missing") and 1.9.3 refused it again.
        Assert.Equal(number, refusal.Number);
        Assert.Equal(before, await store.GetAppliedAsync());
        Assert.Equal(Released, (await store.GetAppliedAsync()).Select(migration => migration.Id).Max(StringComparer.Ordinal));
        Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.tables WHERE name LIKE 'tally[_]%'"));
        Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.stock_movements') AND name='line_seq'"));
        // So the release that made it (here: every migration up to 0037) still opens it.
        Assert.Empty(await new MigrationRunner(released, store).RunAsync());
    }

    private const string FactCounts = """
        SELECT CONCAT((SELECT COUNT(*) FROM dbo.sales_invoices),'|',(SELECT COUNT(*) FROM dbo.sales_invoice_controls),'|',
          (SELECT COUNT(*) FROM dbo.sales_tenders),'|',(SELECT COUNT(*) FROM dbo.stock_movements),'|',(SELECT COUNT(*) FROM dbo.stock_snapshots),'|',
          (SELECT SUM(quantity) FROM dbo.stock_snapshots),'|',(SELECT COUNT(*) FROM dbo.daily_reporting_days))
        """;

    // One invoice with its control and tender, a movement repeated on one identity (1.9.2 stored per-unit
    // repeats as identical rows), and a closing-stock row: what 0041 renumbers, indexes and backfills.
    private const string Seed = """
        DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@invoice bigint;
        INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'synthetic.xlsx',REPLICATE('e',64),1);
        SET @file=SCOPE_IDENTITY();
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
        VALUES(@file,'Sheet',1,'fact'),(@file,'Sheet',2,'fact'),(@file,'Sheet',3,'fact'),(@file,'Sheet',4,'CLOSING_STOCK');
        INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('UPGRADE','I1',2027,'20260825');
        SET @invoice=SCOPE_IDENTITY();
        INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id)
        SELECT @invoice,'INV',1,100,'INR',source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND source_row_number=1;
        INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
        SELECT @invoice,'CASH',100,'INR',source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND source_row_number=1;
        INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,
          from_location,to_location,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id)
        SELECT 'UPGRADE','D1',2027,'20260825','ITEM','STM Receipt',N'',N'UPGRADE',0,1,1,source_lineage_id
        FROM dbo.source_lineage WHERE import_file_id=@file AND source_row_number IN(2,3);
        INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,quantity,source_lineage_id)
        SELECT 'UPGRADE','20260825',N'ITEM',2,source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND source_row_number=4;
        """;

    private sealed class Before(IMigrationSource source, string firstExcluded) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, firstExcluded) < 0).ToArray();
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase0Test_UpgradePath_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }
        public UpgradeDatabase() => ConnectionString = TestSqlConnections.ForDatabase(name, pooling: false);

        public async Task<object?> ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (!name.StartsWith("EtpPhase0Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture name.");
            var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
