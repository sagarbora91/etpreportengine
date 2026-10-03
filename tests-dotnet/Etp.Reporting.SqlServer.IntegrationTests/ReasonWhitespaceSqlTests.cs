using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.9.3, migration 0044. Phase 5 re-audit (26 Sep 2026), defect 3: the SQL reason checks trimmed spaces only,
/// so a reason of tabs or non-breaking spaces passed. Every Phase 5 reason check now refuses what the app's
/// string.IsNullOrWhiteSpace refuses, with its own error number and message, and changes nothing.
/// </summary>
public sealed class ReasonWhitespaceSqlTests
{
    // T-SQL expressions for reasons made only of white space that the 0029/0033/0035 checks let through.
    public static TheoryData<string> BlankReasons => new()
    {
        "NCHAR(160)",                                         // non-breaking space
        "NCHAR(9)+NCHAR(9)",                                  // tabs (0029's constraint and 0033's trigger)
        "NCHAR(13)+NCHAR(10)+NCHAR(160)",                     // line break and non-breaking space
        "NCHAR(12288)+NCHAR(8239)+NCHAR(8199)",               // ideographic, narrow no-break and figure spaces
        "NCHAR(8232)+NCHAR(8233)+NCHAR(133)+NCHAR(11)+NCHAR(12)",
    };

    [Fact]
    public async Task Blank_text_is_white_space_exactly_as_dotnet_counts_it()
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            var blank = (string)(await db.ExecuteAsync("""
                WITH d AS (SELECT v FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15)) x(v)),
                n AS (SELECT a.v*4096+b.v*256+c.v*16+e.v AS cp FROM d a CROSS JOIN d b CROSS JOIN d c CROSS JOIN d e)
                SELECT STUFF((SELECT ','+CONVERT(varchar(5),cp) FROM n
                  WHERE cp NOT BETWEEN 55296 AND 57343 AND dbo.is_blank_text(NCHAR(cp))=1 ORDER BY cp FOR XML PATH('')),1,1,'');
                """))!;
            var expected = Enumerable.Range(0, 0x10000).Where(code => code is < 0xD800 or > 0xDFFF && char.IsWhiteSpace((char)code));
            Assert.Equal(string.Join(',', expected), blank);
            Assert.Equal("1|1|1|0|0|0|0|1", await db.ExecuteAsync("""
                SELECT CONCAT(dbo.is_blank_text(NULL),'|',dbo.is_blank_text(N''),'|',dbo.is_blank_text(REPLICATE(CONVERT(nvarchar(max),NCHAR(160)),5000)),'|',
                  dbo.is_blank_text(NCHAR(160)+N'Checked'),'|',dbo.is_blank_text(N'Checked'+NCHAR(9)),'|',dbo.is_blank_text(NCHAR(8203)),'|',
                  dbo.is_blank_text(NCHAR(65279)),'|',dbo.is_blank_text(N'   '))
                """));
            Assert.Equal(1, await db.ExecuteAsync("SELECT CONVERT(int,OBJECTPROPERTY(OBJECT_ID(N'dbo.is_blank_text'),'IsSchemaBound'))"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [MemberData(nameof(BlankReasons))]
    public async Task Direct_approval_with_a_white_space_reason_is_refused_by_the_constraint_and_the_trigger(string reason)
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            await db.ExecuteAsync(SeedDay);
            var batch = await db.ExecuteAsync(Batch);
            var approve = $"UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason={reason} WHERE accounting_batch_id={batch}";

            var constraint = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync(approve));
            Assert.Equal(547, constraint.Number);
            Assert.Contains("CK_accounting_batches_approval_reason", constraint.Message);
            Assert.Equal("DRAFT|", await db.ExecuteAsync(State(batch)));

            // With the constraint out of the way, 0033's trigger refuses the same approval on its own.
            await db.ExecuteAsync("ALTER TABLE dbo.accounting_batches NOCHECK CONSTRAINT CK_accounting_batches_approval_reason");
            var trigger = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync(approve));
            Assert.Equal(51457, trigger.Number);
            Assert.Equal("DRAFT|", await db.ExecuteAsync(State(batch)));
            await db.ExecuteAsync("ALTER TABLE dbo.accounting_batches WITH CHECK CHECK CONSTRAINT CK_accounting_batches_approval_reason");

            await db.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason=NCHAR(160)+N'Checked source' WHERE accounting_batch_id={batch}");
            Assert.Equal("APPROVED_READY|" + '\u00A0' + "Checked source", await db.ExecuteAsync(State(batch)));
            Assert.Equal(0, await db.ExecuteAsync("SELECT CONVERT(int,is_disabled)+CONVERT(int,is_not_trusted) FROM sys.check_constraints WHERE name='CK_accounting_batches_approval_reason'"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [MemberData(nameof(BlankReasons))]
    public async Task Rejection_with_a_white_space_reason_is_refused_and_the_batch_is_unchanged(string reason)
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            await db.ExecuteAsync(SeedDay);
            var batch = await db.ExecuteAsync(Batch);

            var refusal = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync($"DECLARE @r nvarchar(max)={reason}; EXEC dbo.reject_accounting_batch {batch},@r;"));

            Assert.Equal(51431, refusal.Number);
            Assert.Equal("Enter a rejection reason of at most 1000 characters.", refusal.Message);
            Assert.Equal("DRAFT|", await db.ExecuteAsync(State(batch)));
            Assert.Equal(DBNull.Value, await db.ExecuteAsync($"SELECT rejection_reason FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
            await db.ExecuteAsync($"EXEC dbo.reject_accounting_batch {batch},N'Prepare a corrected batch';");
            Assert.Equal("REJECTED", await db.ExecuteAsync($"SELECT status FROM dbo.accounting_batches WHERE accounting_batch_id={batch}"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [MemberData(nameof(BlankReasons))]
    public async Task Adjustment_and_decision_with_a_white_space_reason_are_refused(string reason)
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();
            await db.ExecuteAsync(SeedDay);

            var submit = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync(
                $"DECLARE @r nvarchar(max)={reason}; EXEC dbo.submit_controlled_adjustment 'WHITESPACE','20260825','CORRECTION',25,@r;"));
            Assert.Equal(51314, submit.Number);
            Assert.Equal("Enter an adjustment reason.", submit.Message);
            Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.controlled_adjustments WHERE store_code='WHITESPACE'"));
            Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.approval_requests WHERE store_code='WHITESPACE'"));

            var adjustment = await db.ExecuteAsync("EXEC dbo.submit_controlled_adjustment 'WHITESPACE','20260825','CORRECTION',25,N'Synthetic adjustment';");
            var request = await db.ExecuteAsync($"SELECT approval_request_id FROM dbo.controlled_adjustments WHERE controlled_adjustment_id={adjustment}");
            var decide = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync($"DECLARE @r nvarchar(max)={reason}; EXEC dbo.decide_approval_request {request},1,@r;"));
            Assert.Equal(51314, decide.Number);
            Assert.Equal("Choose a decision and enter its reason.", decide.Message);
            Assert.Equal("PENDING|PENDING", await db.ExecuteAsync(
                $"SELECT CONCAT(a.status,'|',c.status) FROM dbo.approval_requests a JOIN dbo.controlled_adjustments c ON c.approval_request_id=a.approval_request_id WHERE a.approval_request_id={request}"));
        }
        finally { await db.DisposeAsync(); }
    }

    [Theory]
    [MemberData(nameof(BlankReasons))]
    public async Task Register_entry_and_restatement_with_a_white_space_reason_are_refused(string reason)
    {
        var db = new SqlDatabaseFixture();
        try
        {
            await db.InitializeAsync();

            var register = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync(
                $"DECLARE @r nvarchar(500)={reason}; EXEC dbo.save_register_entry @type='INWARD',@store='WHITESPACE',@date='20260825',@number='WS-1',@verification='DRAFT',@reason=@r;"));
            Assert.Equal(51551, register.Number);
            Assert.Equal("Enter a register change reason.", register.Message);
            Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.register_entries WHERE store_code='WHITESPACE'"));

            var request = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync(
                $"DECLARE @r nvarchar(500)={reason}; EXEC dbo.request_import_restatement 1,'{new string('b', 64)}','R025','WHITESPACE','20260825','20260825',@r;"));
            Assert.Equal(51039, request.Number);
            Assert.Equal("A restatement reason is required.", request.Message);
            Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.approval_requests WHERE store_code='WHITESPACE'"));

            // The internal step refuses too, so a request approved before 0044 with such a reason cannot be applied.
            var apply = await Assert.ThrowsAsync<SqlException>(() => db.ExecuteAsync($"""
                DECLARE @r nvarchar(500)={reason};
                BEGIN TRANSACTION;
                BEGIN TRY EXEC dbo.replace_import_facts_internal 1,2,N'Owner',@r; END TRY
                BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
                ROLLBACK TRANSACTION;
                """));
            Assert.Equal(51039, apply.Number);
            Assert.Equal("A restatement reason is required.", apply.Message);
        }
        finally { await db.DisposeAsync(); }
    }

    [Fact]
    public async Task Upgrade_refuses_an_existing_white_space_approval_reason_with_51562_and_changes_nothing()
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before(source, "0044")).BootstrapAsync();
        await db.ExecuteAsync(SeedDay);
        var batch = await db.ExecuteAsync(Batch);
        // 0029's constraint and 0033's trigger let a non-breaking space through.
        await db.ExecuteAsync($"UPDATE dbo.accounting_batches SET status='APPROVED_READY',approval_reason=NCHAR(160)+NCHAR(9) WHERE accounting_batch_id={batch}");
        var store = new SqlServerMigrationStore(db.ConnectionString);
        var before = await store.GetAppliedAsync();
        const string history = "SELECT * FROM dbo.accounting_batches ORDER BY accounting_batch_id FOR JSON PATH";
        const string constraint = "SELECT definition FROM sys.check_constraints WHERE name='CK_accounting_batches_approval_reason'";
        var batchesBefore = await db.ExecuteAsync(history);
        var constraintBefore = await db.ExecuteAsync(constraint);

        var refusal = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(source, store).RunAsync());

        Assert.Equal(51562, refusal.Number);
        Assert.StartsWith($"Some accounting batches have an approval reason made only of spaces, tabs, line breaks or other white space: 1 batch, the first is accounting batch {batch}. Nothing was changed.", refusal.Message);
        Assert.Equal(before, await store.GetAppliedAsync());
        Assert.Equal(batchesBefore, await db.ExecuteAsync(history));
        Assert.Equal(constraintBefore, await db.ExecuteAsync(constraint));
        Assert.Equal(DBNull.Value, await db.ExecuteAsync("SELECT OBJECT_ID(N'dbo.is_blank_text')"));

        // Once the real reason is recorded, the upgrade applies.
        await db.ExecuteAsync($"UPDATE dbo.accounting_batches SET approval_reason=N'Checked synthetic source' WHERE accounting_batch_id={batch}");
        await new MigrationRunner(source, store).RunAsync();
        Assert.Contains(await store.GetAppliedAsync(), migration => migration.Id.StartsWith("0044_", StringComparison.Ordinal));
        Assert.Contains("is_blank_text", (string)(await db.ExecuteAsync(constraint))!);
    }

    [Fact]
    public async Task The_precheck_compiles_and_passes_on_a_database_from_before_the_approval_reason_column()
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before(source, "0029")).BootstrapAsync();
        Assert.NotEqual(DBNull.Value, await db.ExecuteAsync("SELECT OBJECT_ID(N'dbo.accounting_batches',N'U')"));
        Assert.Equal(DBNull.Value, await db.ExecuteAsync("SELECT COL_LENGTH(N'dbo.accounting_batches',N'approval_reason')"));
        var migration = (await source.DiscoverAsync()).Single(script => script.Id.StartsWith("0044_", StringComparison.Ordinal));

        await new SqlServerMigrationStore(db.ConnectionString).PrecheckAsync([migration]);
    }

    private const string SeedDay = """
        INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by,is_final)
        VALUES('WHITESPACE','20260825',1,REPLICATE('a',64),N'{}',SUSER_SNAME(),1);
        """;

    private const string Batch = """
        INSERT dbo.accounting_batches(store_code,business_date,daily_report_generation_id,accounting_generation,debit_total,credit_total,status,created_by)
        SELECT store_code,business_date,daily_report_generation_id,1,25,25,'DRAFT',SUSER_SNAME()
        FROM dbo.daily_report_generations WHERE store_code='WHITESPACE' AND business_date='20260825';
        SELECT CONVERT(bigint,SCOPE_IDENTITY());
        """;

    private static string State(object? batch) =>
        $"SELECT CONCAT(status,'|',approval_reason) FROM dbo.accounting_batches WHERE accounting_batch_id={batch}";

    private sealed class Before(IMigrationSource source, string first) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, first) < 0).ToArray();
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase0Test_ReasonWhitespace_" + Guid.NewGuid().ToString("N");
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
