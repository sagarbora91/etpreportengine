using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class SqlDatabaseFixture : IAsyncLifetime
{
    private const string Prefix = "EtpPhase0Test_";
    private static int liveFixtures;
    private int counted;

    public SqlDatabaseFixture() : this(NewName()) { }

    // The role walk's parent process chooses the name its child process will use, so that
    // it can drop the database itself if it has to kill the child (which then cannot).
    // Internal, not public: xunit refuses a class fixture with more than one public
    // constructor ("may only define a single public constructor"), and every
    // IClassFixture<SqlDatabaseFixture> test class then fails before it runs.
    internal SqlDatabaseFixture(string name) => Name = RequireTestName(name);

    public string Name { get; }
    public string ConnectionString { get; private set; } = "";
    public string MigrationDirectory => Path.Combine(AppContext.BaseDirectory, "database", "migrations");

    /// <summary>Fixtures initialised in this process and not yet disposed (F-22 scheduling proof).</summary>
    public static int LiveFixtureCount => Volatile.Read(ref liveFixtures);

    public static string NewName() => Prefix + Guid.NewGuid().ToString("N");

    public static async Task DropIfPresentAsync(string name)
    {
        var fixture = new SqlDatabaseFixture(name) { ConnectionString = TestSqlConnections.ForDatabase(name) };
        await fixture.DropAsync();
    }

    private static string RequireTestName(string name)
    {
        // Only a generated name is ever accepted: never configuration, never a live database.
        if (name is null || name.Length != Prefix.Length + 32 || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || !name[Prefix.Length..].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidOperationException("Unsafe test database name.");
        return name;
    }

    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref counted, 1) == 0) Interlocked.Increment(ref liveFixtures);
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
        try { await DropAsync(); }
        finally { if (Interlocked.Exchange(ref counted, 0) == 1) Interlocked.Decrement(ref liveFixtures); }
    }

    private async Task DropAsync()
    {
        if (ConnectionString.Length == 0) return;
        RequireTestName(Name);
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END", connection);
        await command.ExecuteNonQueryAsync();
    }
}
