using Etp.Reporting.Infrastructure.SqlServer.Audit;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

// The SQL reads of the Desktop's operations probes (OPS-01..09, 1.11.0 lane L7). Moved from
// Desktop\Modules\HealthCheck\OperationsProbes.cs in 1.12 (A1): the Desktop keeps the rules and the presentation.

public sealed record SqlServerBuildInfo(string ProductVersion, string? UpdateLevel, string? Edition);

public sealed record AutomationRunCount(string RunType, string Outcome, int Count);

/// <summary>Last 24 hours of <c>dbo.automation_runs</c> and the number of enabled schedules whose last run failed in that time.</summary>
public sealed record AutomationRunsSummary(IReadOnlyList<AutomationRunCount> Counts, int FailedScheduleCount);

/// <summary>The three SQL reads of the operations probes. Each throws on failure; the probe turns that into Skipped.</summary>
public interface IOperationsSqlSource
{
    Task<SqlServerBuildInfo> ReadBuildAsync(CancellationToken cancellationToken);
    /// <summary>Physical path of the database's data file (<c>sys.database_files</c>); null when none is returned.</summary>
    Task<string?> ReadDataFilePathAsync(CancellationToken cancellationToken);
    Task<AutomationRunsSummary> ReadAutomationRunsAsync(CancellationToken cancellationToken);
}

/// <summary><c>dbo.load_database_operational_health</c> for OPS-01, OPS-02 and OPS-08.</summary>
public interface IOperationsHealthSource
{
    Task<DatabaseOperationalHealth> LoadAsync(CancellationToken cancellationToken);
}

public sealed class DatabaseOperationsHealthSource(IEtpConnectionFactory connections) : IOperationsHealthSource
{
    public Task<DatabaseOperationalHealth> LoadAsync(CancellationToken cancellationToken) =>
        new DatabaseOperationalHealthRepository(connections.LegacyConnectionString()).LoadAsync(cancellationToken);
}

public sealed class SqlOperationsSource(IEtpConnectionFactory connections) : IOperationsSqlSource
{
    public const string BuildSql = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)), CAST(SERVERPROPERTY('ProductUpdateLevel') AS nvarchar(128)), CAST(SERVERPROPERTY('Edition') AS nvarchar(128));";
    public const string DataFileSql = "SELECT TOP (1) physical_name FROM sys.database_files WHERE type = 0 ORDER BY file_id;";
    public const string AutomationSql =
        "SELECT run_type, outcome, COUNT(*) FROM dbo.automation_runs WHERE completed_utc >= DATEADD(hour, -24, SYSUTCDATETIME()) GROUP BY run_type, outcome; " +
        "SELECT COUNT(*) FROM dbo.report_pack_schedules WHERE is_enabled = 1 AND last_status = 'Failed' AND last_run_utc >= DATEADD(hour, -24, SYSUTCDATETIME());";

    private static SqlCommand Command(SqlConnection connection, string sql)
    {
        ReadOnlySqlGuard.Require(sql);
        return new SqlCommand(sql, connection) { CommandTimeout = 10 };
    }

    public async Task<SqlServerBuildInfo> ReadBuildAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(EtpConnectionPurpose.Default, cancellationToken);
        await using var command = Command(connection, BuildSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) throw new InvalidOperationException("SQL Server returned no version.");
        return new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    public async Task<string?> ReadDataFilePathAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(EtpConnectionPurpose.Default, cancellationToken);
        await using var command = Command(connection, DataFileSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0) ? reader.GetString(0) : null;
    }

    public async Task<AutomationRunsSummary> ReadAutomationRunsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(EtpConnectionPurpose.Default, cancellationToken);
        await using var command = Command(connection, AutomationSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var counts = new List<AutomationRunCount>();
        while (await reader.ReadAsync(cancellationToken)) counts.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        var failedSchedules = await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken) ? reader.GetInt32(0) : 0;
        return new(counts, failedSchedules);
    }
}

/// <summary>How the Desktop's probes read a failure without touching SqlClient themselves (1.12 A1).</summary>
public static class DesktopProbeFailures
{
    /// <summary>A SqlException whose numbers mean "permission denied" (ERR-01..04 are then Skipped, not failed).</summary>
    public static bool IsPermissionDenied(Exception exception) =>
        exception is SqlException && DatabaseConnectionFailure.Classify(exception) == DatabaseFailureKind.PermissionDenied;

    /// <summary>SqlClient's own client-side timeout (-2).</summary>
    public static bool IsSqlTimeout(Exception exception) =>
        exception is SqlException sql && (sql.Number == -2 || sql.Errors.Cast<SqlError>().Any(x => x.Number == -2));

    /// <summary>Exception type name plus the SQL error number when there is one: a token, never the message.</summary>
    public static string ExceptionToken(Exception exception) =>
        exception is SqlException sql ? $"{sql.GetType().Name} (SQL {sql.Number})" : exception.GetType().Name;
}
