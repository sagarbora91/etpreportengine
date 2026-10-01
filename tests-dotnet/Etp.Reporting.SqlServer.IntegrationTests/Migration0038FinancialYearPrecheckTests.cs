using Etp.Reporting.Application.Imports;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class Migration0038FinancialYearPrecheckTests
{
    private const string OutcomeParameter =
        "SELECT COUNT(*) FROM sys.parameters WHERE object_id=OBJECT_ID(N'dbo.persist_phase_one_enrichment') AND name=N'@outcome' AND is_output=1";

    [Fact]
    public async Task Invoice_keyed_by_a_year_other_than_its_financial_year_refuses_0038_with_51700_and_changes_nothing()
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before0038(source)).BootstrapAsync();
        // A 1-April return keyed by ETP's INVOICEYEAR label, the year before its date, as R022 stored it before OD-1.
        await db.ExecuteAsync("INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES('FYCHECK',N'100000777',2026,'20260401')");
        var store = new SqlServerMigrationStore(db.ConnectionString);
        var before = await store.GetAppliedAsync();

        var refusal = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(source, store).RunAsync());

        Assert.Equal(ImportCodes.SqlErrors.FinancialYearPrecheck, refusal.Number);
        Assert.StartsWith("Some invoices carry a year other than the financial year of their date.", refusal.Message);
        Assert.Equal(before, await store.GetAppliedAsync());
        Assert.DoesNotContain(before, migration => migration.Id.StartsWith("0038", StringComparison.Ordinal));
        Assert.Equal(0, await db.ExecuteAsync(OutcomeParameter));
        Assert.Equal(2026, await db.ExecuteAsync("SELECT invoice_year FROM dbo.sales_invoices WHERE store_code='FYCHECK'"));

        // Keyed by the financial year of its date, the same invoice lets the upgrade apply.
        await db.ExecuteAsync("UPDATE dbo.sales_invoices SET invoice_year=2027 WHERE store_code='FYCHECK'");
        await new MigrationRunner(source, store).RunAsync();

        Assert.Contains(await store.GetAppliedAsync(), migration => migration.Id.StartsWith("0038", StringComparison.Ordinal));
        Assert.Equal(1, await db.ExecuteAsync(OutcomeParameter));
    }

    private sealed class Before0038(IMigrationSource source) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, "0038") < 0).ToArray();
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
