using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

// Migration 0038 pre-checks 51701/51702 and the stock movement backfill (sections C and C2), applied over a
// database that holds every earlier migration.
public sealed class Migration0038StockAndTenderTests
{
    [Theory]
    [InlineData(false, ImportCodes.SqlErrors.DuplicateControlsPrecheck, "An invoice has more than one revenue control.")]
    [InlineData(true, ImportCodes.SqlErrors.DuplicateTendersPrecheck, "An invoice has the same tender type twice.")]
    public async Task Duplicate_controls_or_tenders_refuse_the_upgrade_and_leave_the_schema_unchanged(bool tenders, int number, string message)
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before0038(source)).BootstrapAsync();
        await db.ExecuteAsync(SeedInvoice);
        await db.ExecuteAsync(tenders
            ? "INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id) SELECT i.sales_invoice_id,CASE l.source_row_number WHEN 1 THEN 'CASH' ELSE 'Cash' END,50,'INR',l.source_lineage_id FROM dbo.sales_invoices i CROSS JOIN dbo.source_lineage l WHERE i.store_code='UPGRADE'"
            : "INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id) SELECT i.sales_invoice_id,'INV',1,100,'INR',l.source_lineage_id FROM dbo.sales_invoices i CROSS JOIN dbo.source_lineage l WHERE i.store_code='UPGRADE'");
        var store = new SqlServerMigrationStore(db.ConnectionString);
        var before = await store.GetAppliedAsync();

        var refusal = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(source, store).RunAsync());

        Assert.Equal(number, refusal.Number);
        Assert.StartsWith(message, refusal.Message);
        Assert.Equal(before, await store.GetAppliedAsync());
        Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.indexes WHERE name IN('UX_sales_invoice_controls_invoice','UX_sales_tenders_invoice_type','UX_stock_movements_identity')"));
        Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.stock_movements') AND name IN('line_seq','from_key','to_key')"));
        Assert.Equal(2, await db.ExecuteAsync(tenders ? "SELECT COUNT(*) FROM dbo.sales_tenders" : "SELECT COUNT(*) FROM dbo.sales_invoice_controls"));
    }

    [Fact]
    public async Task Existing_repeated_movements_on_a_finalised_day_get_chain_line_seq_and_the_guard_returns()
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before0038(source)).BootstrapAsync();
        await db.ExecuteAsync(SeedInvoice);
        // Inserted against the chain (opening 1 first); one row has no FROM LOCATION and the other a blank one.
        await db.ExecuteAsync("""
            INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,
              from_location,to_location,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id)
            SELECT 'UPGRADE','D1',2027,'20260825','ITEM','STM Receipt',CASE source_row_number WHEN 1 THEN NULL ELSE N'' END,'UPGRADE',
              2-source_row_number,1,3-source_row_number,source_lineage_id
            FROM dbo.source_lineage ORDER BY source_row_number;
            INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('UPGRADE','20260825','LOCKED',SUSER_SNAME(),SYSUTCDATETIME());
            """);
        var store = new SqlServerMigrationStore(db.ConnectionString);

        await new MigrationRunner(source, store).RunAsync();

        Assert.Equal("0:1|1:2", await db.ExecuteAsync("SELECT STRING_AGG(CONCAT(CONVERT(int,opening_quantity),':',line_seq),'|') WITHIN GROUP(ORDER BY opening_quantity) FROM dbo.stock_movements"));
        Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.triggers WHERE name='trg_stock_movements_protect_locked' AND is_disabled=1"));
        Assert.Equal(1, await db.ExecuteAsync("SELECT COUNT(*) FROM sys.indexes WHERE name='UX_stock_movements_identity'"));
        var locked = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync("UPDATE dbo.stock_movements SET line_seq=line_seq+10"));
        Assert.Equal(51033, locked.Number);

        var applied = await store.GetAppliedAsync();
        await new MigrationRunner(source, store).RunAsync();
        Assert.Equal(applied, await store.GetAppliedAsync());
    }

    // An invoice whose year is the financial year of its date, and two lineage rows to hang facts on.
    private const string SeedInvoice = """
        DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
        INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'synthetic.xlsx',REPLICATE('d',64),1);
        SET @file=SCOPE_IDENTITY();
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Sheet',1,'fact'),(@file,'Sheet',2,'fact');
        INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('UPGRADE','I1',2027,'20260825');
        """;

    private sealed class Before0038(IMigrationSource source) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, "0038") < 0).ToArray();
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase0Test_Migration0038_" + Guid.NewGuid().ToString("N");
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
