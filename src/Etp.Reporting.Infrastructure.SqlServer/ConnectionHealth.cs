using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public enum DatabaseHealthStatus { Healthy, Unreachable, InvalidConfiguration }

public sealed record DatabaseHealth(
    DatabaseHealthStatus Status,
    string Message,
    string? ServerVersion = null,
    TimeSpan? Elapsed = null,
    int? SqlErrorNumber = null,
    DatabaseFailureKind? FailureKind = null);

/// <summary>What stopped ETP using its database, from the SQL error numbers (IE-CODE-05..08).</summary>
public enum DatabaseFailureKind { ServerUnreachable, Timeout, LoginRefused, DatabaseMissing, PermissionDenied, InvalidConfiguration, Other }

/// <summary>
/// One classification of connection-class SQL failures for the startup screen, the connection
/// test, the access reload and the headless stderr line (1.9.9, IE-CODE-05..08). A login failure,
/// a missing database and a refused permission are the usual cases on a new PC or after a restore,
/// and each needs a different fix, so none of them may read as "SQL Server could not be reached".
/// </summary>
public static class DatabaseConnectionFailure
{
    public const string ServerUnreachableMessage =
        "SQL Server could not be reached. Check that the SQL Server service is running and the instance name is correct.";
    public const string TimeoutMessage =
        "SQL Server took too long to answer. It may still be starting or be busy; wait a minute and try again.";
    public const string LoginRefusedMessage =
        "SQL Server refused the Windows login for this account. Ask the Owner to add this account in Settings > Users.";
    public const string DatabaseMissingMessage =
        "SQL Server is running but the ETP database could not be opened. Check the database name in the connection, that the database exists (restore it or run setup), and that this account was added in Settings > Users.";
    public const string PermissionDeniedMessage =
        "SQL Server permission denied. Ask the Owner to grant this account access in Settings > Users.";
    public const string InvalidConfigurationMessage = "The SQL Server connection settings are invalid.";

    /// <summary>Every distinct SQL error number in the exception and its inner exceptions, in order.</summary>
    public static IReadOnlyList<int> SqlNumbers(Exception? exception)
    {
        var numbers = new List<int>();
        var pending = new Stack<Exception>();
        if (exception is not null) pending.Push(exception);
        var visited = 0;
        while (pending.Count > 0 && visited++ < 32)
        {
            var current = pending.Pop();
            if (current is SqlException sql)
            {
                foreach (SqlError error in sql.Errors)
                    if (!numbers.Contains(error.Number)) numbers.Add(error.Number);
                if (sql.Errors.Count == 0 && !numbers.Contains(sql.Number)) numbers.Add(sql.Number);
            }
            if (current is AggregateException aggregate)
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--) pending.Push(aggregate.InnerExceptions[index]);
            else if (current.InnerException is not null) pending.Push(current.InnerException);
        }
        return numbers;
    }

    public static DatabaseFailureKind Classify(int number) => number switch
    {
        // 4060 "Cannot open database requested by the login"; 911 "Database does not exist".
        4060 or 911 => DatabaseFailureKind.DatabaseMissing,
        229 or 230 or 262 or 297 or 300 or 916 => DatabaseFailureKind.PermissionDenied,
        18456 or 18452 or 18470 or 18486 or 18487 or 18488 => DatabaseFailureKind.LoginRefused,
        // SqlClient's own client-side timeout.
        -2 => DatabaseFailureKind.Timeout,
        -1 or 2 or 26 or 40 or 53 or 64 or 233 or 258 or 1225 or 10053 or 10054 or 10060 or 10061 or 11001 =>
            DatabaseFailureKind.ServerUnreachable,
        _ => DatabaseFailureKind.Other
    };

    /// <summary>
    /// The kind for an exception. When a login fails because its database is missing SQL Server raises
    /// 4060 and 18456 together, so the most specific kind wins: database, permission, login, timeout,
    /// unreachable. A connection string the validator refused is <see cref="DatabaseFailureKind.InvalidConfiguration"/>.
    /// </summary>
    public static DatabaseFailureKind Classify(Exception? exception)
    {
        if (exception is null) return DatabaseFailureKind.Other;
        var kinds = SqlNumbers(exception).Select(Classify).ToHashSet();
        foreach (var preferred in Precedence)
            if (kinds.Contains(preferred)) return preferred;
        return exception is ArgumentException ? DatabaseFailureKind.InvalidConfiguration : DatabaseFailureKind.Other;
    }

    private static readonly DatabaseFailureKind[] Precedence =
    [
        DatabaseFailureKind.DatabaseMissing, DatabaseFailureKind.PermissionDenied, DatabaseFailureKind.LoginRefused,
        DatabaseFailureKind.Timeout, DatabaseFailureKind.ServerUnreachable
    ];

    /// <summary>The first SQL number of <paramref name="kind"/>, else the first number at all.</summary>
    public static int? DecidingNumber(Exception? exception, DatabaseFailureKind kind)
    {
        var numbers = SqlNumbers(exception);
        foreach (var number in numbers) if (Classify(number) == kind) return number;
        return numbers.Count > 0 ? numbers[0] : null;
    }

    /// <summary>True for the kinds that mean this account cannot use the database at all.</summary>
    public static bool IsConnectionClass(DatabaseFailureKind kind) =>
        kind is not (DatabaseFailureKind.Other or DatabaseFailureKind.InvalidConfiguration);

    public static string? Describe(DatabaseFailureKind kind) => kind switch
    {
        DatabaseFailureKind.ServerUnreachable => ServerUnreachableMessage,
        DatabaseFailureKind.Timeout => TimeoutMessage,
        DatabaseFailureKind.LoginRefused => LoginRefusedMessage,
        DatabaseFailureKind.DatabaseMissing => DatabaseMissingMessage,
        DatabaseFailureKind.PermissionDenied => PermissionDeniedMessage,
        DatabaseFailureKind.InvalidConfiguration => InvalidConfigurationMessage,
        _ => null
    };
}

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
            await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
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
            return Failed(exception, started.Elapsed);
        }
    }

    /// <summary>
    /// IE-CODE-08. The health of a failed open: a login failure (18456), a missing database (4060) and a
    /// refused permission each say so and carry their SQL number, which the connection test writes to
    /// diagnostics. Only a server that did not answer (or an unclassified failure) reads "could not be reached".
    /// </summary>
    internal static DatabaseHealth Failed(Exception exception, TimeSpan? elapsed)
    {
        var kind = DatabaseConnectionFailure.Classify(exception);
        if (kind is DatabaseFailureKind.Other or DatabaseFailureKind.InvalidConfiguration) kind = DatabaseFailureKind.ServerUnreachable;
        var number = DatabaseConnectionFailure.DecidingNumber(exception, kind);
        var message = DatabaseConnectionFailure.Describe(kind)!;
        if (number is { } value) message += $" (SQL error {value.ToString(CultureInfo.InvariantCulture)})";
        return new(DatabaseHealthStatus.Unreachable, message, Elapsed: elapsed, SqlErrorNumber: number, FailureKind: kind);
    }
}
