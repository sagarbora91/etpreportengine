using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class SqlDatabaseFixture : IAsyncLifetime
{
    public string Name { get; } = "EtpPhase0Test_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";
    public string MigrationDirectory => Path.Combine(AppContext.BaseDirectory, "database", "migrations");

    public async Task InitializeAsync()
    {
        ConnectionString = TestSqlConnections.ForDatabase(Name);
        try
        {
            await new SqlServerDatabaseBootstrapper(ConnectionString, new DirectoryMigrationSource(MigrationDirectory)).BootstrapAsync();
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task<object?> ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        return await command.ExecuteScalarAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString.Length == 0) return;
        // The generated name is never taken from configuration or a caller.
        if (!Name.StartsWith("EtpPhase0Test_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test database name.");
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END", connection);
        await command.ExecuteNonQueryAsync();
    }
}
