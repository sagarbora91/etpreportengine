using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record DatabaseBootstrapResult(bool DatabaseCreated, IReadOnlyList<string> AppliedMigrations)
{
    /// <summary>What the objects step did after the migrations (1.12 A3); none when there are no object files.</summary>
    public SchemaObjectRunResult Objects { get; init; } = SchemaObjectRunResult.None;
}

/// <summary>
/// Creates the database when it is missing, applies the pending migrations, then the objects step (1.12 A3):
/// <see cref="SchemaObjectRunner"/> over <c>database/objects</c>. Without an explicit <paramref name="objectSource"/>, a
/// <see cref="DirectoryMigrationSource"/> brings its sibling <c>objects</c> folder (none when the folder is missing); any
/// other migration source brings no objects. Pass <see cref="SchemaObjectSource.None"/> for migrations only. A failed
/// objects step throws <see cref="SchemaObjectsNotUpToDateException"/> after rolling every object back; the migrations
/// already applied stay applied, and running setup again retries the objects.
/// </summary>
public sealed partial class SqlServerDatabaseBootstrapper(string connectionString, IMigrationSource migrationSource, ISchemaObjectSource? objectSource = null)
{
    public async Task<DatabaseBootstrapResult> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var validated = LocalSqlConnectionPolicy.Validate(connectionString);
        var target = new SqlConnectionStringBuilder(validated);
        var databaseName = ValidateDatabaseName(target.InitialCatalog);
        var master = new SqlConnectionStringBuilder(validated) { InitialCatalog = "master" };
        var created = false;
        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var exists = new SqlCommand("SELECT CONVERT(bit,CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END)", connection);
            exists.Parameters.AddWithValue("@name", databaseName);
            if (!(bool)(await exists.ExecuteScalarAsync(cancellationToken))!)
            {
                await using var create = new SqlCommand($"CREATE DATABASE [{databaseName}]", connection) { CommandTimeout = 0 };
                await create.ExecuteNonQueryAsync(cancellationToken);
                created = true;
            }
        }
        var applied = await new MigrationRunner(migrationSource, new SqlServerMigrationStore(validated)).RunAsync(cancellationToken);
        var objects = await new SchemaObjectRunner(ObjectSourceFor(migrationSource, objectSource), new SqlServerSchemaObjectStore(validated))
            .RunAsync(cancellationToken);
        return new(created, applied) { Objects = objects };
    }

    internal static ISchemaObjectSource ObjectSourceFor(IMigrationSource migrations, ISchemaObjectSource? explicitSource) =>
        explicitSource
        ?? (migrations is DirectoryMigrationSource directory
            ? new DirectorySchemaObjectSource(SchemaObjectFiles.ObjectsDirectoryFor(directory.DirectoryPath))
            : SchemaObjectSource.None);

    public static string ValidateDatabaseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !DatabaseNamePattern().IsMatch(value))
            throw new ArgumentException("A database name containing only letters, numbers, underscores, or hyphens is required.", nameof(value));
        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseNamePattern();
}
