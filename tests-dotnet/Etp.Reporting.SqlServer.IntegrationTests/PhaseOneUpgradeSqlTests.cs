using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseOneUpgradeSqlTests
{
    [Fact]
    public async Task Existing_calendar_year_invoices_upgrade_without_rewriting_old_migrations_and_backfill_signed_tax()
    {
        await using var database = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(database.ConnectionString, new ThroughPhaseZeroSource(source)).BootstrapAsync();
        var store = new SqlServerMigrationStore(database.ConnectionString);
        var before = await store.GetAppliedAsync();
        Assert.Contains(before, x => x.Id == "0016_reporting_indexes");
        Assert.DoesNotContain(before, x => string.CompareOrdinal(x.Id, "0017") >= 0);

        await database.ExecuteAsync("""
            DECLARE @batch uniqueidentifier=NEWID(),@file bigint;
            INSERT dbo.import_batches(import_batch_id,status,period_start,period_end,started_utc)
            VALUES(@batch,'Completed','20250101','20260701',SYSUTCDATETIME());
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date)
            VALUES(@batch,'synthetic-legacy.xlsx',REPLICATE('c',64),1,'R025','UPGRADE','20260701');
            SET @file=SCOPE_IDENTITY();
            INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
            VALUES(@file,'Sales',1,'sale'),(@file,'Sales',2,'sale'),(@file,'Sales',3,'sale'),(@file,'Tender',1,'tender');
            INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date)
            VALUES('UPGRADE','100000068',2025,'20250101'),('UPGRADE','100000068',2026,'20260701'),('UPGRADE','MISSING_GROSS',2026,'20260701');
            INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,currency_code,source_lineage_id)
            SELECT i.sales_invoice_id,CONVERT(nvarchar(80),s.source_row_number),'ITEM',CASE WHEN s.source_row_number=2 THEN 'SR' ELSE 'INV' END,
                CASE WHEN s.source_row_number=2 THEN -1 ELSE 1 END,
                CASE s.source_row_number WHEN 1 THEN 236 WHEN 2 THEN -118 ELSE NULL END,
                CASE s.source_row_number WHEN 1 THEN 200 WHEN 2 THEN -100 ELSE 50 END,'INR',s.source_lineage_id
            FROM dbo.sales_invoices i JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Sales'
              AND s.source_row_number=CASE WHEN i.document_number='MISSING_GROSS' THEN 3 WHEN i.invoice_year=2025 THEN 1 ELSE 2 END
            WHERE i.store_code='UPGRADE';
            INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id,is_reporting_eligible,exclusion_reason)
            SELECT i.sales_invoice_id,'PAYMENTTYPE25',236,'INR',s.source_lineage_id,0,'UNRESOLVED_PAYMENTTYPE25'
            FROM dbo.sales_invoices i JOIN dbo.source_lineage s ON s.import_file_id=@file AND s.sheet_name='Tender'
            WHERE i.store_code='UPGRADE' AND i.invoice_year=2025;
            """);

        var applied = await new MigrationRunner(source, store).RunAsync();
        Assert.Contains("0017_sales_value_columns", applied);
        Assert.DoesNotContain(applied, id => string.CompareOrdinal(id, "0017") < 0);
        Assert.Equal("2025,2027", await database.ExecuteAsync("SELECT STRING_AGG(CONVERT(varchar(4),invoice_year),',') WITHIN GROUP(ORDER BY invoice_year) FROM dbo.sales_invoices WHERE store_code='UPGRADE' AND document_number='100000068'"));
        Assert.Equal(3, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
        Assert.Equal(236m, await database.ExecuteAsync("SELECT source_gross_amount FROM dbo.sales_lines WHERE line_identifier='1'"));
        Assert.Equal(200m, await database.ExecuteAsync("SELECT source_net_amount FROM dbo.sales_lines WHERE line_identifier='1'"));
        Assert.Equal(36m, await database.ExecuteAsync("SELECT source_tax_amount FROM dbo.sales_lines WHERE line_identifier='1'"));
        Assert.Equal(-18m, await database.ExecuteAsync("SELECT source_tax_amount FROM dbo.sales_lines WHERE line_identifier='2'"));
        Assert.Equal(DBNull.Value, await database.ExecuteAsync("SELECT source_tax_amount FROM dbo.sales_lines WHERE line_identifier='3'"));
        Assert.Equal(236m, await database.ExecuteAsync("SELECT SUM(source_amount) FROM dbo.reporting_sales_tenders WHERE tender_type='PAYMENTTYPE25'"));
        Assert.Equal("2025-01-01/2026-07-01", await database.ExecuteAsync("SELECT CONCAT(CONVERT(char(10),period_start,23),'/',CONVERT(char(10),period_end,23)) FROM dbo.import_files WHERE store_code='UPGRADE'"));
        var after = await store.GetAppliedAsync();
        foreach (var old in before)
            Assert.Equal(old, Assert.Single(after, x => x.Id == old.Id));
        Assert.Empty(await new MigrationRunner(source, store).RunAsync());
    }

    private sealed class ThroughPhaseZeroSource(IMigrationSource all) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            (await all.DiscoverAsync(cancellationToken)).Where(x => string.CompareOrdinal(x.Id, "0017") < 0).ToArray();
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase1Upgrade_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }
        public UpgradeDatabase()
        {
            var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ETP_TEST_SQL_CONNECTION")
                ?? @"Server=.\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5") { InitialCatalog = name };
            ConnectionString = builder.ConnectionString;
        }
        public async Task<object?> ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            return await command.ExecuteScalarAsync();
        }
        public async ValueTask DisposeAsync()
        {
            if (!name.StartsWith("EtpPhase1Upgrade_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test database name.");
            SqlConnection.ClearAllPools();
            var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
