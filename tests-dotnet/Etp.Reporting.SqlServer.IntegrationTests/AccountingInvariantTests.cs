using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class AccountingInvariantTests
{
    [Fact]
    public async Task Database_refuses_second_active_batch_for_an_adjustment_only_day()
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            await db.ExecuteAsync(SeedDay);
            var adjustment = await db.ExecuteAsync("EXEC dbo.submit_controlled_adjustment 'INVARIANT','20260825','CORRECTION',25,N'Synthetic adjustment';");
            var request = await db.ExecuteAsync($"SELECT approval_request_id FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}");
            await db.ExecuteAsync($"EXEC dbo.decide_approval_request {request},1,N'Checked synthetic adjustment';");
            var first = await db.ExecuteAsync(Batch(1));
            Assert.Equal(0,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_invoices WHERE store_code='INVARIANT'"));
            Assert.Equal(0,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batch_invoices"));

            var duplicate = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync(Batch(2)));
            Assert.Equal(2601,duplicate.Number);
            Assert.Contains("UX_accounting_batches_active_day",duplicate.Message);
            Assert.Equal(1,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batches"));

            await db.ExecuteAsync($"EXEC dbo.reject_accounting_batch {first},N'Prepare corrected batch';");
            var replacement = await db.ExecuteAsync(Batch(2));
            Assert.NotEqual(first,replacement);
            Assert.Equal(1,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batches WHERE status<>'REJECTED'"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Database_refuses_a_second_export_receipt_for_one_batch()
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            await db.ExecuteAsync(SeedDay);
            var batch = await db.ExecuteAsync(Batch(1));
            await db.ExecuteAsync(Receipt(batch!));
            var duplicate = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync(Receipt(batch!)));
            Assert.Equal(2601,duplicate.Number);
            Assert.Contains("UX_accounting_export_receipts_batch",duplicate.Message);
            Assert.Equal(1,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_export_receipts"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("APPROVED_READY")]
    [InlineData("EXPORTED_AWAITING_IMPORT")]
    public async Task Invoice_reservation_refusal_names_the_earlier_batch_and_a_feasible_action(string status)
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            await db.ExecuteAsync(SeedDay);
            await db.ExecuteAsync("""
                INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
                VALUES('INVARIANT','20260826',1,REPLICATE('b',64),N'{}',SUSER_SNAME(),1);
                INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('INVARIANT','INV-1',2027,'20260825');
                """);
            var first = await db.ExecuteAsync(Batch(1,status));
            await db.ExecuteAsync($"INSERT dbo.accounting_batch_invoices VALUES({first},'INVARIANT',2027,N'INV-1',1)");
            var later = await db.ExecuteAsync(Batch(1,date:"20260826"));
            var duplicate = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync($"INSERT dbo.accounting_batch_invoices VALUES({later},'INVARIANT',2027,N'INV-1',1)"));
            Assert.Equal(51452,duplicate.Number);
            Assert.Contains($"batch {first}",duplicate.Message);
            Assert.Equal(status == "EXPORTED_AWAITING_IMPORT"
                ? $"Invoice INV-1 is already in exported batch {first}. An exported batch is final; it cannot be replaced."
                : $"Invoice INV-1 is already in batch {first}. Reject that unexported batch before preparing another.", duplicate.Message);
            Assert.Equal(1,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batch_invoices"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Status_constraint_permits_exactly_the_five_honest_accounting_states()
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            await db.ExecuteAsync(SeedDay);
            var batch = await db.ExecuteAsync(Batch(1));
            var definition = Assert.IsType<string>(await db.ExecuteAsync("SELECT definition FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.accounting_batches') AND name='CK_accounting_batches_status'"));
            var states = Regex.Matches(definition,"'([^']+)'").Select(match => match.Groups[1].Value).Order().ToArray();
            Assert.Equal(new[] { "APPROVED_READY", "BLOCKED", "DRAFT", "EXPORTED_AWAITING_IMPORT", "REJECTED" },states);
            Assert.Equal(0,await db.ExecuteAsync("SELECT CONVERT(int,is_disabled)+CONVERT(int,is_not_trusted) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.accounting_batches') AND name='CK_accounting_batches_status'"));
            var invalid = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='EXPORTED_TO_TALLY' WHERE accounting_batch_id={batch}"));
            Assert.Equal(547,invalid.Number);
            Assert.Contains("CK_accounting_batches_status",invalid.Message);
            Assert.Equal("DRAFT",await db.ExecuteAsync($"SELECT status FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData("   ",547,"CK_accounting_batches_approval_reason")]
    [InlineData(null,51457,"approval reason")]
    public async Task Direct_approval_with_blank_reason_is_refused_without_changing_the_draft(string? reason,int number,string message)
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            await db.ExecuteAsync(SeedDay);
            var batch = await db.ExecuteAsync(Batch(1));
            var value = reason is null ? "NULL" : "'   '";
            var blank = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason={value} WHERE accounting_batch_id={batch}"));
            Assert.Equal(number,blank.Number);
            Assert.Contains(message,blank.Message);
            Assert.Equal(DBNull.Value,await db.ExecuteAsync($"SELECT approval_reason FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
            Assert.Equal("DRAFT",await db.ExecuteAsync($"SELECT status FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [InlineData(false,51560,"Existing accounting batches cover the same store and business date.")]
    [InlineData(true,51561,"Existing accounting batches have more than one export receipt.")]
    public async Task Upgrade_refuses_existing_duplicate_days_or_receipts_without_rewriting_history(bool receipts,int number,string message)
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory,"database","migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString,new BeforeInvariants(source)).BootstrapAsync();
        await db.ExecuteAsync(SeedDay);
        var batch = await db.ExecuteAsync(Batch(1));
        if (receipts)
        {
            await db.ExecuteAsync(Receipt(batch!));
            await db.ExecuteAsync(Receipt(batch!));
        }
        else await db.ExecuteAsync(Batch(2));
        var store = new SqlServerMigrationStore(db.ConnectionString);
        var before = await store.GetAppliedAsync();
        const string batchHistory = "SELECT * FROM dbo.accounting_batches ORDER BY accounting_batch_id FOR JSON PATH";
        const string receiptHistory = "SELECT * FROM dbo.accounting_export_receipts ORDER BY accounting_export_receipt_id FOR JSON PATH";
        var batchesBefore = await db.ExecuteAsync(batchHistory);
        var receiptsBefore = await db.ExecuteAsync(receiptHistory);

        var refusal = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(source,store).RunAsync());

        Assert.Equal(number,refusal.Number);
        Assert.StartsWith(message,refusal.Message);
        Assert.Equal(before,await store.GetAppliedAsync());
        Assert.Equal(batchesBefore,await db.ExecuteAsync(batchHistory));
        Assert.Equal(receiptsBefore,await db.ExecuteAsync(receiptHistory));
        Assert.Equal(receipts ? 1 : 2,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_batches"));
        Assert.Equal(receipts ? 2 : 0,await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.accounting_export_receipts"));
        Assert.Equal(0,await db.ExecuteAsync("SELECT COUNT(*) FROM sys.indexes WHERE name IN('UX_accounting_batches_active_day','UX_accounting_export_receipts_batch')"));
    }

    private const string SeedDay = """
        INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
        VALUES('INVARIANT','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1);
        """;
    private static string Batch(int generation,string status="DRAFT",string date="20260825") => $"""
        INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by,approval_reason)
        SELECT store_code,business_date,daily_report_generation_id,{generation},25,25,'{status}',SUSER_SNAME(),{(status=="DRAFT" ? "NULL" : "N'Checked synthetic source'")}
        FROM dbo.daily_report_generations WHERE store_code='INVARIANT' AND business_date='{date}';
        SELECT CONVERT(bigint,SCOPE_IDENTITY());
        """;
    private static string Receipt(object batch) => $"""
        INSERT dbo.accounting_export_receipts(accounting_batch_id,output_path,sha256,tally_company_name,environment_label,exported_by)
        VALUES({batch},N'synthetic.xml',REPLICATE('a',64),N'TEST Fixture','TEST',SUSER_SNAME());
        """;

    private sealed class BeforeInvariants(IMigrationSource source) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token=default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id,"0037")<0).ToArray();
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase0Test_AccountingInvariant_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }
        public UpgradeDatabase() => ConnectionString = TestSqlConnections.ForDatabase(name,pooling:false);
        public async Task<object?> ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql,connection);
            return await command.ExecuteScalarAsync();
        }
        public async ValueTask DisposeAsync()
        {
            if (!name.StartsWith("EtpPhase0Test_",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture name.");
            var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog="master" };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END",connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
