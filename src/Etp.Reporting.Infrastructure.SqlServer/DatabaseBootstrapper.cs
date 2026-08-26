using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record DatabaseBootstrapResult(bool DatabaseCreated, IReadOnlyList<string> AppliedMigrations);

public sealed partial class SqlServerDatabaseBootstrapper(string connectionString, IMigrationSource migrationSource)
{
    public async Task<DatabaseBootstrapResult> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        var databaseName = ValidateDatabaseName(target.InitialCatalog);
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
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
        var applied = await new MigrationRunner(migrationSource, new SqlServerMigrationStore(connectionString)).RunAsync(cancellationToken);
        return new(created, applied);
    }

    public static string ValidateDatabaseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !DatabaseNamePattern().IsMatch(value))
            throw new ArgumentException("A database name containing only letters, numbers, underscores, or hyphens is required.", nameof(value));
        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseNamePattern();
}
