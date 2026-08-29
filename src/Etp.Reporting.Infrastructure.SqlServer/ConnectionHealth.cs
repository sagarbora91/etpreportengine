using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public enum DatabaseHealthStatus { Healthy, Unreachable, InvalidConfiguration }

public sealed record DatabaseHealth(
    DatabaseHealthStatus Status,
    string Message,
    string? ServerVersion = null,
    TimeSpan? Elapsed = null);

public interface IDatabaseHealthCheck
{
    Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class SqlServerHealthCheck(string connectionString) : IDatabaseHealthCheck
{
    public async Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return new(DatabaseHealthStatus.InvalidConfiguration, "A SQL Server connection string is required.");

        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return new(DatabaseHealthStatus.Healthy, "SQL Server connection succeeded.", connection.ServerVersion, started.Elapsed);
        }
        catch (ArgumentException)
        {
            return new(DatabaseHealthStatus.InvalidConfiguration,
                "The SQL Server connection settings are invalid.", Elapsed: started.Elapsed);
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            return new(DatabaseHealthStatus.Unreachable,
                "SQL Server could not be reached. Verify the configured instance is running and try again.",
                Elapsed: started.Elapsed);
        }
    }
}
