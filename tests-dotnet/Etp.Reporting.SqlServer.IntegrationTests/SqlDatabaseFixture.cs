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
            // On SQL Server 2025 over shared memory (1 October 2026) the first command after
            // the bootstrap received a pooled connection that was already broken, and failed
            // with "transport-level error ... The I/O operation has been aborted"; the same
            // command on a new connection passed. Shared memory cannot detect a dead pooled
            // session before reuse, so the bootstrap's connections are not handed on.
            await using (var pooled = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pooled);
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task<object?> ExecuteAsync(string sql, int timeoutSeconds = 60)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
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
