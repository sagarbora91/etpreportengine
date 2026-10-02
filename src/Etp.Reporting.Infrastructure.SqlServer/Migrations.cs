using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Runs the pre-check sections (<see cref="MigrationPrechecks"/>) of every migration in
    /// <paramref name="plan"/> against the database as it is now, before any of them applies, and
    /// changes nothing. A refusal throws, so a later migration's refusal cannot leave the earlier
    /// pending ones committed.
    /// </summary>
    Task PrecheckAsync(IReadOnlyList<MigrationScript> plan, CancellationToken cancellationToken = default);

    Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default);
}

public sealed class MigrationIntegrityException(string message) : InvalidOperationException(message);

public static class MigrationChecksum
{
    public static string Compute(string sql) => Raw(sql.Replace("\r\n", "\n").Replace('\r', '\n'));

    internal static bool Matches(string sql, string checksum)
    {
        var lf = sql.Replace("\r\n", "\n").Replace('\r', '\n');
        // Upgrade compatibility: accept only hashes of this exact SQL in legacy LF/CRLF form.
        // Do not rewrite the journal or accept changes to spaces, comments or SQL tokens.
        return new[] { Compute(sql), Raw(sql), Raw(lf.Replace("\n", "\r\n")) }
            .Contains(checksum, StringComparer.OrdinalIgnoreCase);
    }

    private static string Raw(string sql) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
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

/// <summary>
/// A migration's pre-checks are the sections between a line <c>-- &gt;&gt;&gt; PRECHECK&lt;name&gt; begin</c>
/// and its line <c>-- &lt;&lt;&lt; PRECHECK&lt;name&gt; end</c>. They stay part of the script, which runs
/// them again inside its own transaction, and the runner also runs them on their own before any pending
/// migration applies. There they see the schema from before every pending migration, so they must only
/// read, and guard each object they read with OBJECT_ID.
/// </summary>
public static partial class MigrationPrechecks
{
    public static IReadOnlyList<string> Extract(string sql)
    {
        var sections = new List<string>();
        var ends = EndMarker().Matches(sql);
        foreach (Match begin in BeginMarker().Matches(sql))
        {
            var name = begin.Groups["name"].Value;
            var end = ends.FirstOrDefault(match => match.Index > begin.Index && string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal))
                ?? throw new MigrationIntegrityException($"Pre-check section '{name}' has no end marker.");
            sections.Add(sql[(begin.Index + begin.Length)..end.Index].Trim('\r', '\n'));
        }
        return sections;
    }

    [GeneratedRegex(@"^--[ \t]*>>>[ \t]*(?<name>PRECHECK[A-Za-z0-9_]*)[ \t]+begin[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BeginMarker();

    [GeneratedRegex(@"^--[ \t]*<<<[ \t]*(?<name>PRECHECK[A-Za-z0-9_]*)[ \t]+end[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex EndMarker();
}

public static class MigrationPlanner
{
    public static IReadOnlyList<MigrationScript> Plan(IReadOnlyList<MigrationScript> discovered, IReadOnlyList<AppliedMigration> applied)
    {
        var duplicate = discovered.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null) throw new MigrationIntegrityException($"Duplicate migration id '{duplicate.Key}'.");

        var known = discovered.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var newestKnown = discovered.Select(x => x.Id).Max(StringComparer.Ordinal);
        var newer = applied.Select(x => x.Id).Where(id => !known.ContainsKey(id) && (newestKnown is null || string.CompareOrdinal(id, newestKnown) > 0))
            .Order(StringComparer.Ordinal).ToArray();
        if (newer.Length > 0)
            throw new MigrationIntegrityException(
                $"This database was upgraded by a newer release of ETP: it holds migration '{newer[^1]}', and this release knows migrations "
                + $"only up to '{newestKnown ?? "(none)"}'. Nothing was changed. Install the release that upgraded it (or a later one), "
                + "or restore the backup taken before that upgrade.");
        foreach (var item in applied)
        {
            if (!known.TryGetValue(item.Id, out var script))
                throw new MigrationIntegrityException($"Applied migration '{item.Id}' is missing from the migration source.");
            if (!string.Equals(script.Checksum, item.Checksum, StringComparison.OrdinalIgnoreCase) && !MigrationChecksum.Matches(script.Sql, item.Checksum))
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
            // Every pending migration's pre-checks run before the first of them applies: each script
            // commits on its own, so a refusal found only when its script ran would leave the
            // database between releases (1.9.3: Tally 0038-0040 committed, then 0041 refused).
            if (plan.Count > 0) await store.PrecheckAsync(plan, cancellationToken);
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
        var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
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
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(cancellationToken);
        await EnsureJournalAsync(connection, cancellationToken);
        await using var command = new SqlCommand("SELECT migration_id, checksum, applied_utc FROM dbo.schema_migrations ORDER BY migration_id", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetString(0), reader.GetString(1), new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc))));
        return result;
    }

    public async Task PrecheckAsync(IReadOnlyList<MigrationScript> plan, CancellationToken cancellationToken = default)
    {
        var checks = plan.SelectMany(migration => MigrationPrechecks.Extract(migration.Sql)).ToArray();
        if (checks.Length == 0) return;
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(cancellationToken);
        // One transaction that is always rolled back: a pre-check only reads, and if one wrote by
        // mistake, nothing of it would stay.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var check in checks)
            {
                await using var command = new SqlCommand(check, connection, transaction) { CommandTimeout = 0 };
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception failure)
        {
            await SqlTransactionGuard.RollBackAsync(failure, transaction);
            throw;
        }
        await transaction.RollbackAsync(CancellationToken.None);
    }

    public async Task ApplyAsync(MigrationScript migration, CancellationToken cancellationToken = default)
    {
        // The migration transaction has the commit budget as Connect Timeout, which SqlClient also applies to COMMIT.
        await using var connection = await LocalSqlConnectionPolicy.OpenWithCommitBudgetAsync(connectionString, cancellationToken);
        await EnsureJournalAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var bridgedRetiredTable = await PrepareRetiredExtractionGrantAsync(migration, connection, transaction, cancellationToken);
            await using var script = new SqlCommand(migration.Sql, connection, transaction) { CommandTimeout = 0 };
            await script.ExecuteNonQueryAsync(cancellationToken);
            if (bridgedRetiredTable)
            {
                await using var cleanup = new SqlCommand("DROP TABLE dbo.document_extractions;", connection, transaction);
                await cleanup.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var journal = new SqlCommand("IF NOT EXISTS (SELECT 1 FROM dbo.schema_migrations WHERE migration_id=@id) INSERT dbo.schema_migrations(migration_id, checksum) VALUES(@id,@checksum)", connection, transaction);
            journal.Parameters.AddWithValue("@id", migration.Id);
            journal.Parameters.AddWithValue("@checksum", migration.Checksum);
            await journal.ExecuteNonQueryAsync(cancellationToken);
            // A COMMIT whose reply is lost is judged by the journal: the script and its journal
            // row commit together, so a journalled migration is applied.
            await SqlTransactionGuard.CommitOrVerifyAsync(() => Commit(transaction, cancellationToken),
                () => SqlTransactionGuard.ReleaseAsync(connection), () => JournaledAsync(migration));
        }
        catch (Exception failure)
        {
            SqlTransactionGuard.MarkRolledBack(failure);
            await SqlTransactionGuard.RollBackAsync(failure, transaction);
            throw;
        }
    }

    /// <summary>Issues the migration COMMIT. Tests replace it to simulate a COMMIT whose reply is lost.</summary>
    internal Func<SqlTransaction, CancellationToken, Task> Commit { get; init; } = (transaction, token) => transaction.CommitAsync(token);

    /// <summary>Where the journal check after a failed COMMIT connects. Tests point it at a missing database to make the check fail.</summary>
    internal string? CommitCheckConnectionString { get; init; }

    private Task<bool> JournaledAsync(MigrationScript migration) => SqlTransactionGuard.CheckAsync(CommitCheckConnectionString ?? connectionString,
        "SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.schema_migrations WITH(READCOMMITTEDLOCK) WHERE migration_id=@id AND checksum=@checksum) THEN 1 ELSE 0 END)",
        command =>
        {
            command.Parameters.AddWithValue("@id", migration.Id);
            command.Parameters.AddWithValue("@checksum", migration.Checksum);
        });

    private static async Task<bool> PrepareRetiredExtractionGrantAsync(MigrationScript migration,
        SqlConnection connection, SqlTransaction transaction, CancellationToken token)
    {
        if (migration.Id != "0022_least_privilege_audit") return false;
        // The committed Phase 4 script grants on an object retired by Phase 1.
        // Preserve its bytes, checksum and numeric order. This empty compatibility
        // object exists only inside this migration transaction and is dropped before
        // commit. A later migration cannot repair a failure in 0022.
        const string originalChecksum = "e272fa70f48e92731c0b3c78072ac924a530b2f1fb415f2cf7a32903a91611f9";
        if (MigrationChecksum.Compute(migration.Sql) != originalChecksum)
            throw new MigrationIntegrityException("The retired-table compatibility bridge requires the original 0022 migration.");
        const string sql = """
            IF OBJECT_ID(N'dbo.document_extractions') IS NULL
              AND EXISTS(SELECT 1 FROM dbo.schema_migrations WHERE migration_id='0020_remove_document_extraction')
            BEGIN
              CREATE TABLE dbo.document_extractions(
                review_status varchar(30) NULL,reviewed_by nvarchar(256) NULL,
                reviewed_utc datetime2(3) NULL,review_reason nvarchar(1000) NULL);
              SELECT CAST(1 AS bit);
            END
            ELSE SELECT CAST(0 AS bit);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        return (bool)(await command.ExecuteScalarAsync(token))!;
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
