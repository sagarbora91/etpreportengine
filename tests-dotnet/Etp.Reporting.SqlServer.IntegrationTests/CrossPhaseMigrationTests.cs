using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class CrossPhaseMigrationTests
{
    [Theory]
    [InlineData("fresh")]
    [InlineData("phase3")]
    [InlineData("phase4")]
    public async Task Merged_migrations_preserve_history_and_retire_extraction_for_each_upgrade_path(string path)
    {
        await using var database = new MigrationDatabase();
        var all = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        var initial = new SelectedSource(all, path);
        await new SqlServerDatabaseBootstrapper(database.ConnectionString, initial).BootstrapAsync();
        var store = new SqlServerMigrationStore(database.ConnectionString);
        var before = await store.GetAppliedAsync();
        await new MigrationRunner(all, store).RunAsync();
        var after = await store.GetAppliedAsync();
        foreach (var original in before) Assert.Equal(original, Assert.Single(after, x => x.Id == original.Id));
        foreach (var migration in await all.DiscoverAsync())
            Assert.Equal(migration.Checksum, Assert.Single(after, x => x.Id == migration.Id).Checksum);
        Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM sys.objects WHERE object_id=OBJECT_ID(N'dbo.document_extractions')"));
        Assert.Empty(await new MigrationRunner(all, store).RunAsync());
        Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM sys.database_permissions WHERE major_id=OBJECT_ID(N'dbo.document_extractions')"));
    }

    [Fact]
    public async Task Failed_0022_rolls_back_compatibility_object_and_journal_then_can_retry()
    {
        await using var database = new MigrationDatabase();
        var all = new DirectoryMigrationSource(Path.Combine(AppContext.BaseDirectory, "database", "migrations"));
        await new SqlServerDatabaseBootstrapper(database.ConnectionString, new SelectedSource(all, "before22")).BootstrapAsync();
        await database.ExecuteAsync("""
            CREATE TRIGGER dbo.reject_test_migration ON dbo.schema_migrations AFTER INSERT AS
            BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE migration_id='0022_least_privilege_audit')
              THROW 51990,'Synthetic journal failure',1; END;
            """);
        var store = new SqlServerMigrationStore(database.ConnectionString);
        var error = await Assert.ThrowsAsync<SqlException>(() => new MigrationRunner(all, store).RunAsync());
        Assert.Equal(51990, error.Number);
        Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM sys.objects WHERE object_id=OBJECT_ID(N'dbo.document_extractions')"));
        Assert.DoesNotContain(await store.GetAppliedAsync(), x => x.Id == "0022_least_privilege_audit");
        await database.ExecuteAsync("DROP TRIGGER dbo.reject_test_migration");
        Assert.Contains("0022_least_privilege_audit", await new MigrationRunner(all, store).RunAsync());
        Assert.Empty(await new MigrationRunner(all, store).RunAsync());
    }

    private sealed class SelectedSource(IMigrationSource source, string path) : IMigrationSource
    {
        public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            (await source.DiscoverAsync(cancellationToken)).Where(x => path switch
            {
                "fresh" => false,
                "phase3" => string.CompareOrdinal(x.Id, "0021") < 0 || x.Id.StartsWith("0024_", StringComparison.Ordinal),
                "phase4" => string.CompareOrdinal(x.Id, "0017") < 0 || string.CompareOrdinal(x.Id, "0021") >= 0 && string.CompareOrdinal(x.Id, "0024") < 0,
                "before22" => string.CompareOrdinal(x.Id, "0022") < 0,
                _ => throw new ArgumentOutOfRangeException(nameof(path))
            }).ToArray();
    }

    private sealed class MigrationDatabase : IAsyncDisposable
    {
        private readonly string name = "EtpCrossPhaseMigration_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }
        public MigrationDatabase() => ConnectionString = TestSqlConnections.ForDatabase(name, pooling: false);
        public async Task<object?> ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
            return await command.ExecuteScalarAsync();
        }
        public async ValueTask DisposeAsync()
        {
            var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
            await using var connection = new SqlConnection(master.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
