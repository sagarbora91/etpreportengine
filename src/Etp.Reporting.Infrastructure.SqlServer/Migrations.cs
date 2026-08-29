using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record MigrationScript(string Id, string Checksum, string Sql, string Source);
public sealed record AppliedMigration(string Id, string Checksum, DateTimeOffset AppliedUtc);

public interface IMigrationSource
{
    Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default);
}

public interface IMigrationStore
{
    Task<IAsyncDisposable> AcquireMigrationLockAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken cancellationToken = default);
    Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default);
}

public sealed class MigrationIntegrityException(string message) : InvalidOperationException(message);

public static class MigrationChecksum
{
    public static string Compute(string sql) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
}

public sealed class DirectoryMigrationSource(string directory) : IMigrationSource
{
    public async Task<IReadOnlyList<MigrationScript>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) return [];
        var migrations = new List<MigrationScript>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.sql", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(id)) continue;
            var sql = await File.ReadAllTextAsync(path, cancellationToken);
            migrations.Add(new(id, MigrationChecksum.Compute(sql), sql, path));
        }
        return migrations;
    }
}

public static class MigrationPlanner
{
    public static IReadOnlyList<MigrationScript> Plan(IReadOnlyList<MigrationScript> discovered, IReadOnlyList<AppliedMigration> applied)
    {
        var duplicate = discovered.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new MigrationIntegrityException($"Duplicate migration id '{duplicate.Key}'.");

        var known = discovered.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var item in applied)
        {
            if (!known.TryGetValue(item.Id, out var script))
                throw new MigrationIntegrityException($"Applied migration '{item.Id}' is missing from the migration source.");
            if (!string.Equals(script.Checksum, item.Checksum, StringComparison.OrdinalIgnoreCase))
                throw new MigrationIntegrityException($"Checksum mismatch for applied migration '{item.Id}'.");
        }
        var appliedIds = applied.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return discovered.Where(x => !appliedIds.Contains(x.Id)).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }
}

public sealed class MigrationRunner(IMigrationSource source, IMigrationStore store)
{
    public async Task<IReadOnlyList<string>> RunAsync(CancellationToken cancellationToken = default)
    {
        var migrationLock = await store.AcquireMigrationLockAsync(cancellationToken);
        IReadOnlyList<string>? result = null;
        Exception? migrationException = null;
        try
        {
            var discovered = await source.DiscoverAsync(cancellationToken);
            var applied = await store.GetAppliedAsync(cancellationToken);
            var plan = MigrationPlanner.Plan(discovered, applied);
            foreach (var migration in plan) await store.ApplyAsync(migration, cancellationToken);
            result = plan.Select(x => x.Id).ToArray();
        }
        catch (Exception exception)
        {
            migrationException = exception;
        }

        Exception? releaseException = null;
        try
        {
            await migrationLock.DisposeAsync();
        }
        catch (Exception exception)
        {
            releaseException = exception;
        }

        if (migrationException is not null && releaseException is not null)
            throw new AggregateException("Migration execution and explicit SQL migration-lock release both failed.", migrationException, releaseException);
        if (migrationException is not null) ExceptionDispatchInfo.Capture(migrationException).Throw();
        if (releaseException is not null) ExceptionDispatchInfo.Capture(releaseException).Throw();
        return result ?? [];
    }
}

public sealed class SqlServerMigrationStore(string connectionString) : IMigrationStore
{
    internal const string MigrationLockResource = "ETP_SCHEMA_MIGRATION";
    internal const int MigrationLockTimeoutMilliseconds = 60_000;
    internal const string AcquireMigrationLockSql = """
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock
            @Resource = N'ETP_SCHEMA_MIGRATION',
            @LockMode = N'Exclusive',
            @LockOwner = N'Session',
            @LockTimeout = @lockTimeout,
            @DbPrincipal = N'dbo';
        SELECT @result;
        """;
    internal const string ReleaseMigrationLockSql = """
        DECLARE @result int;
        EXEC @result = sys.sp_releaseapplock
            @Resource = N'ETP_SCHEMA_MIGRATION',
            @LockOwner = N'Session',
            @DbPrincipal = N'dbo';
        SELECT @result;
        """;

    public async Task<IAsyncDisposable> AcquireMigrationLockAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(AcquireMigrationLockSql, connection) { CommandTimeout = 75 };
            command.Parameters.AddWithValue("@lockTimeout", MigrationLockTimeoutMilliseconds);
            var rawResult = await command.ExecuteScalarAsync(cancellationToken);
            if (rawResult is null or DBNull)
                throw new MigrationIntegrityException("SQL Server did not return a migration-lock result; migration was not started.");
            EnsureMigrationLockAcquired(Convert.ToInt32(rawResult, System.Globalization.CultureInfo.InvariantCulture));
            return new SqlServerMigrationLock(connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal static void EnsureMigrationLockAcquired(int result)
    {
        if (result < 0)
            throw new MigrationIntegrityException($"Could not acquire the exclusive SQL migration lock (sp_getapplock result {result}); migration was not started.");
    }

    internal static void EnsureMigrationLockReleased(int result)
    {
        if (result < 0)
            throw new MigrationIntegrityException($"Could not explicitly release the SQL migration lock (sp_releaseapplock result {result}); the connection pool was invalidated.");
    }

    public async Task<IReadOnlyList<AppliedMigration>> GetAppliedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureJournalAsync(connection, cancellationToken);
        await using var command = new SqlCommand("SELECT migration_id, checksum, applied_utc FROM dbo.schema_migrations ORDER BY migration_id", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetString(0), reader.GetString(1), new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc))));
        return result;
    }

    public async Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureJournalAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var script = new SqlCommand(migration.Sql, connection, transaction) { CommandTimeout = 0 };
            await script.ExecuteNonQueryAsync(cancellationToken);
            await using var journal = new SqlCommand("IF NOT EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE migration_id=@id) INSERT dbo.schema_migrations(migration_id, checksum) VALUES(@id,@checksum)", connection, transaction);
            journal.Parameters.AddWithValue("@id", migration.Id);
            journal.Parameters.AddWithValue("@checksum", migration.Checksum);
            await journal.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private static async Task EnsureJournalAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "IF OBJECT_ID(N'dbo.schema_migrations',N'U') IS NULL CREATE TABLE dbo.schema_migrations(migration_id varchar(100) NOT NULL PRIMARY KEY, checksum char(64) NOT NULL, applied_utc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME())";
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class SqlServerMigrationLock(SqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Exception? releaseException = null;
            try
            {
                if (connection.State != System.Data.ConnectionState.Open)
                    throw new MigrationIntegrityException("The SQL migration-lock connection closed before explicit release; the connection pool was invalidated.");

                await using var command = new SqlCommand(ReleaseMigrationLockSql, connection) { CommandTimeout = 15 };
                var rawResult = await command.ExecuteScalarAsync(CancellationToken.None);
                if (rawResult is null or DBNull)
                    throw new MigrationIntegrityException("SQL Server did not return a migration-lock release result; the connection pool was invalidated.");
                EnsureMigrationLockReleased(Convert.ToInt32(rawResult, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception exception)
            {
                releaseException = exception;
                try
                {
                    SqlConnection.ClearPool(connection);
                }
                catch (Exception poolException)
                {
                    releaseException = new AggregateException(
                        "Explicit SQL migration-lock release and connection-pool invalidation both failed.",
                        exception,
                        poolException);
                }
            }
            finally
            {
                try
                {
                    await connection.DisposeAsync();
                }
                catch (Exception disposeException)
                {
                    if (releaseException is null) throw;
                    throw new AggregateException(
                        "SQL migration-lock release and connection disposal both failed.",
                        releaseException,
                        disposeException);
                }
            }

            if (releaseException is not null) ExceptionDispatchInfo.Capture(releaseException).Throw();
        }
    }
}
