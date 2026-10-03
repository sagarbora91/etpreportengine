using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class Migration0041FinancialYearPrecheckTests
{
    private const string OutcomeParameter =
        "SELECT COUNT(*) FROM sys.parameters WHERE object_id=OBJECT_ID(N'dbo.persist_phase_one_enrichment') AND name=N'@outcome' AND is_output=1";

    [Fact]
    public async Task Invoice_keyed_by_a_year_other_than_its_financial_year_refuses_0041_with_51700_and_changes_nothing()
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before0041(source)).BootstrapAsync();
        // A 1-April return keyed by ETP's INVOICEYEAR label, the year before its date, as R022 stored it before OD-1;
        // and one whose R025 twin keyed by the financial year already holds the line (R022 holds control and tender).
        await db.ExecuteAsync(TwinSeed);
        var store = new SqlServerMigrationStore(db.ConnectionString);
        var before = await store.GetAppliedAsync();

        var refusal = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(source, store).RunAsync());

        Assert.Equal(ImportCodes.SqlErrors.FinancialYearPrecheck, refusal.Number);
        Assert.StartsWith("Some invoices carry a year other than the financial year of their date. 2 invoices, 1 of them beside a header already keyed by that financial year: ", refusal.Message);
        Assert.Contains("FYCHECK 100000777 dated 2026-04-01 keyed 2026 not 2027", refusal.Message);
        Assert.Contains("FYCHECK 100000778 dated 2026-04-01 keyed 2026 not 2027", refusal.Message);
        Assert.Contains("scripts/repair-invoice-financial-year.sql", refusal.Message);
        Assert.Equal(before, await store.GetAppliedAsync());
        Assert.DoesNotContain(before, migration => migration.Id.StartsWith("0041", StringComparison.Ordinal));
        Assert.Equal(0, await db.ExecuteAsync(OutcomeParameter));
        Assert.Equal(2, await db.ExecuteAsync(WrongYear));

        // The unique invoice key forbids re-keying the twinned invoice by hand.
        var key = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync(
            "UPDATE dbo.sales_invoices SET invoice_year=2027 WHERE store_code='FYCHECK' AND document_number=N'100000778' AND invoice_year=2026"));
        Assert.Equal(2627, key.Number);

        // The repair script previews without changing anything, then re-keys the lone one and combines the twins.
        var repair = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "repair-invoice-financial-year.sql"));
        var preview = await db.QueryAsync(repair);
        Assert.Equal([("PREVIEW", "COMBINE", "100000778"), ("PREVIEW", "RE_KEY", "100000777")],
            preview.Select(row => ((string)row["run"], (string)row["action"], (string)row["document_number"])).OrderBy(row => row.Item3).Reverse());
        Assert.Equal(2, await db.ExecuteAsync(WrongYear));
        var repairing = repair.Replace("DECLARE @apply bit = 0;", "DECLARE @apply bit = 1;", StringComparison.Ordinal);
        Assert.NotEqual(repair, repairing);
        Assert.All(await db.QueryAsync(repairing), row => Assert.Equal("REPAIRED", row["run"]));

        Assert.Equal(0, await db.ExecuteAsync(WrongYear));
        Assert.Equal(1, await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_invoices WHERE document_number=N'100000778'"));
        Assert.Equal("1|1|1", await db.ExecuteAsync("""
            SELECT CONCAT((SELECT COUNT(*) FROM dbo.sales_lines x WHERE x.sales_invoice_id=i.sales_invoice_id),'|',
              (SELECT COUNT(*) FROM dbo.sales_invoice_controls x WHERE x.sales_invoice_id=i.sales_invoice_id),'|',
              (SELECT COUNT(*) FROM dbo.sales_tenders x WHERE x.sales_invoice_id=i.sales_invoice_id))
            FROM dbo.sales_invoices i WHERE i.document_number=N'100000778' AND i.invoice_year=2027
            """));
        await new MigrationRunner(source, store).RunAsync();

        Assert.Contains(await store.GetAppliedAsync(), migration => migration.Id.StartsWith("0041", StringComparison.Ordinal));
        Assert.Equal(1, await db.ExecuteAsync(OutcomeParameter));

        // A second run of 0041 is a no-op: the pre-check passes and the procedure and its grant stay as they are.
        var applied = await store.GetAppliedAsync();
        var definition = await db.ExecuteAsync(Definition);
        var script = (await source.DiscoverAsync()).Single(migration => migration.Id.StartsWith("0041", StringComparison.Ordinal));
        await db.ExecuteInTransactionAsync(script.Sql);

        Assert.Equal(applied, await store.GetAppliedAsync());
        Assert.Equal(definition, await db.ExecuteAsync(Definition));
        Assert.Equal(1, await db.ExecuteAsync(OutcomeParameter));
        Assert.Equal(2027, await db.ExecuteAsync("SELECT invoice_year FROM dbo.sales_invoices WHERE store_code='FYCHECK' AND document_number=N'100000777'"));
    }

    [Fact]
    public async Task Repair_script_leaves_an_invoice_on_a_finalised_day_and_says_why()
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before0041(source)).BootstrapAsync();
        await db.ExecuteAsync(TwinSeed + """
            INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('FYCHECK','20260401','LOCKED',N'tester',SYSUTCDATETIME());
            """);
        var repair = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "repair-invoice-financial-year.sql")))
            .Replace("DECLARE @apply bit = 0;", "DECLARE @apply bit = 1;", StringComparison.Ordinal);

        var plan = await db.QueryAsync(repair);

        Assert.Equal(2, plan.Count);
        Assert.All(plan, row =>
        {
            Assert.Equal(("NOT_CHANGED", "MANUAL"), ((string)row["run"], (string)row["action"]));
            Assert.StartsWith("LOCKED_DAY:", (string)row["reason"]);
        });
        Assert.Equal(2, await db.ExecuteAsync(WrongYear));
        Assert.Equal(3, await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_invoices WHERE store_code='FYCHECK'"));
    }

    private const string WrongYear =
        "SELECT COUNT(*) FROM dbo.sales_invoices WHERE invoice_year<>YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END";

    // 100000777: alone under the label year. 100000778: an R022 header under the label year with its control and tender,
    // beside the R025 header keyed by the financial year with its line.
    private const string TwinSeed = """
        DECLARE @batch uniqueidentifier=NEWID(),@file bigint,@label bigint,@twin bigint;
        INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Completed',SYSUTCDATETIME());
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'fy-twin.xlsx',REPLICATE('f',64),1);
        SET @file=SCOPE_IDENTITY();
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type)
        VALUES(@file,'Sales',2,'sale'),(@file,'Revenue',2,'control'),(@file,'Tender',2,'tender');
        INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('FYCHECK',N'100000777',2026,'20260401');
        INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('FYCHECK',N'100000778',2026,'20260401');
        SET @label=SCOPE_IDENTITY();
        INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('FYCHECK',N'100000778',2027,'20260401');
        SET @twin=SCOPE_IDENTITY();
        INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,currency_code,source_lineage_id)
        SELECT @twin,'1','ITEM','INV',1,118,100,'INR',source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND sheet_name='Sales';
        INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id)
        SELECT @label,'INV',1,100,'INR',source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND sheet_name='Revenue';
        INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id)
        SELECT @label,N'CASH',118,'INR',source_lineage_id FROM dbo.source_lineage WHERE import_file_id=@file AND sheet_name='Tender';
        """;

    private const string Definition = "SELECT OBJECT_DEFINITION(OBJECT_ID(N'dbo.persist_phase_one_enrichment'))";

    private sealed class Before0041(IMigrationSource source) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, "0041") < 0).ToArray();
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase0Test_FinancialYearPrecheck_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }
        public UpgradeDatabase() => ConnectionString = TestSqlConnections.ForDatabase(name, pooling: false);

        public async Task<object?> ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            return await command.ExecuteScalarAsync();
        }

        // The rows of the first result set of a script.
        public async Task<IReadOnlyList<Dictionary<string, object>>> QueryAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<Dictionary<string, object>>();
            while (await reader.ReadAsync())
                rows.Add(Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, reader.GetValue));
            while (await reader.NextResultAsync()) { }
            return rows;
        }

        // As the migration runner applies a script: the whole body in one transaction.
        public async Task ExecuteInTransactionAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = new SqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
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
