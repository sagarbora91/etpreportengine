using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.9.4, migration 0046. Report audit of 3 October 2026 (HEMW FIX-05, WLMHW FIX-11 and FIX-12): after the
/// upgrade from 0045, Settings > Calculations says NET_SALES is GST-inclusive R025 NETAMOUNT and INVOICE_COUNT
/// counts INV documents only. Wording only; running the script again changes nothing.
/// </summary>
public sealed class ReportLabelCorrectionSqlTests
{
    private const string Catalogue = "SELECT STRING_AGG(CONCAT(kpi_code,'|',version,'|',formula,'|',data_source,'|',approval_status,'|',effective_date,'|',is_active),'#') WITHIN GROUP(ORDER BY kpi_code) FROM dbo.kpi_catalogue";

    [Fact]
    public async Task Upgrading_from_0045_corrects_the_catalogue_text_once_and_keeps_everything_else()
    {
        await using var db = new UpgradeDatabase();
        var source = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(db.ConnectionString, new Before(source, "0046")).BootstrapAsync();
        var store = new SqlServerMigrationStore(db.ConnectionString);
        Assert.Equal("0045_recovery_drill_row_counts", (await store.GetAppliedAsync()).Select(migration => migration.Id).Max(StringComparer.Ordinal));
        Assert.Equal("SUM(R025.NETVALUE)", await db.ExecuteAsync("SELECT formula FROM dbo.kpi_catalogue WHERE kpi_code='NET_SALES'"));
        var untouched = await db.ExecuteAsync(Catalogue.Replace("FROM dbo.kpi_catalogue", "FROM dbo.kpi_catalogue WHERE kpi_code NOT IN('NET_SALES','INVOICE_COUNT')", StringComparison.Ordinal));

        var applied = await new MigrationRunner(source, store).RunAsync();

        Assert.Contains("0046_report_label_corrections", applied);
        Assert.Equal("SUM(R025.NETAMOUNT)|Canonical sales lines from R025 NETAMOUNT (GST-inclusive)|2|APPROVED|1",
            await db.ExecuteAsync("SELECT CONCAT(formula,'|',data_source,'|',version,'|',approval_status,'|',is_active) FROM dbo.kpi_catalogue WHERE kpi_code='NET_SALES'"));
        Assert.Equal("Primary sales value including GST, with sales returns retaining their negative signs.",
            await db.ExecuteAsync("SELECT definition FROM dbo.kpi_catalogue WHERE kpi_code='NET_SALES'"));
        var invoice = (string)(await db.ExecuteAsync("SELECT CONCAT(definition,'|',formula,'|',version) FROM dbo.kpi_catalogue WHERE kpi_code='INVOICE_COUNT'"))!;
        Assert.StartsWith("Distinct INV documents (store + financial year + document number)", invoice, StringComparison.Ordinal);
        Assert.EndsWith("|COUNT(DISTINCT store + financial year + document) over documents with an INV line|2", invoice, StringComparison.Ordinal);
        Assert.Equal(0, await db.ExecuteAsync("SELECT COUNT(*) FROM dbo.kpi_catalogue WHERE CONCAT(formula,data_source,definition) LIKE N'%R025.NETVALUE%' OR CONCAT(formula,data_source,definition) LIKE N'%sourced from R025 NETVALUE%'"));
        Assert.Equal(untouched, await db.ExecuteAsync(Catalogue.Replace("FROM dbo.kpi_catalogue", "FROM dbo.kpi_catalogue WHERE kpi_code NOT IN('NET_SALES','INVOICE_COUNT')", StringComparison.Ordinal)));

        // Running the script again (as a restore of an older journal might) changes nothing: no second version bump.
        var after = await db.ExecuteAsync(Catalogue);
        await db.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "migrations", "0046_report_label_corrections.sql")));
        Assert.Equal(after, await db.ExecuteAsync(Catalogue));
        Assert.Empty(await new MigrationRunner(source, store).RunAsync());
    }

    private sealed class Before(IMigrationSource source, string firstExcluded) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken token = default) =>
            (await source.DiscoverAsync(token)).Where(migration => string.CompareOrdinal(migration.Id, firstExcluded) < 0).ToArray();
    }

    private sealed class UpgradeDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpPhase0Test_ReportLabels_" + Guid.NewGuid().ToString("N");
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
