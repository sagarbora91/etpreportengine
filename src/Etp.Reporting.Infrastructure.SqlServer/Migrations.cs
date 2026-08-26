using System.Security.Cryptography;
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
        var plan = MigrationPlanner.Plan(await source.DiscoverAsync(cancellationToken), await store.GetAppliedAsync(cancellationToken));
        foreach (var migration in plan) await store.ApplyAsync(migration, cancellationToken);
        return plan.Select(x => x.Id).ToArray();
    }
}

public sealed class SqlServerMigrationStore(string connectionString) : IMigrationStore
{
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
}
