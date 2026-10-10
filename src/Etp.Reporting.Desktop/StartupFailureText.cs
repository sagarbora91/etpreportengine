using System.Globalization;
using System.Text;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop;

/// <summary>The welcome overlay's title and message after a failed start.</summary>
public sealed record StartupFailurePresentation(string Title, string Message, DatabaseFailureKind Kind);

/// <summary>What the shell does when access cannot be reloaded (IE-CODE-06).</summary>
public sealed record AccessRefreshFailure(bool RemoveAccess, string Status, DatabaseFailureKind Kind);

/// <summary>
/// 1.9.9, lane startup-connection (IE-CODE-05..08). Text for a failed start, a failed access
/// reload, a failed connection test and the headless stderr line, all from one classification
/// (<see cref="DatabaseConnectionFailure"/>) so that a login failure, a missing database and a
/// refused permission never read as "Cannot reach SQL Server".
/// </summary>
public static class StartupFailureText
{
    /// <summary>The prefix setup matches in the captured stderr (199-STDERR-FORMAT.md).</summary>
    public const string HeadlessPrefix = "ETP-STARTUP-FAILED";
    internal const int MaxReasonLength = 500;

    public static string ModeToken(DesktopStartupMode mode) => mode switch
    {
        DesktopStartupMode.InitializeDatabase => "initialize-database",
        DesktopStartupMode.InitializeConfiguredDatabase => "initialize-configured-database",
        DesktopStartupMode.AutomationOnce => "automation-once",
        _ => "interactive"
    };

    public const string ConfigurationModeToken = "configuration";
    public const string UnhandledModeToken = "unhandled";

    /// <summary>
    /// IE-CODE-05. One line for stderr, so the setup log names the reason a headless step failed:
    /// <c>ETP-STARTUP-FAILED mode=&lt;mode&gt; kind=&lt;kind&gt; sql=&lt;numbers|none&gt;: &lt;reason&gt;</c>.
    /// Never the connection string: the reason is fixed text, ETP's own SQL refusal (50000 and above)
    /// or the friendly description.
    /// </summary>
    public static string HeadlessLine(string modeToken, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeToken);
        ArgumentNullException.ThrowIfNull(exception);
        var kind = DatabaseConnectionFailure.Classify(exception);
        if (kind == DatabaseFailureKind.Other && modeToken == ConfigurationModeToken) kind = DatabaseFailureKind.InvalidConfiguration;
        var numbers = DatabaseConnectionFailure.SqlNumbers(exception);
        var sql = numbers.Count == 0 ? "none" : string.Join(",", numbers.Select(number => number.ToString(CultureInfo.InvariantCulture)));
        return $"{HeadlessPrefix} mode={modeToken} kind={kind} sql={sql}: {SingleLine(HeadlessReason(exception, kind))}";
    }

    private static string HeadlessReason(Exception exception, DatabaseFailureKind kind)
    {
        if (ProductSqlMessage(exception) is { } product) return product;
        if (DatabaseConnectionFailure.IsConnectionClass(kind)) return DatabaseConnectionFailure.Describe(kind)!;
        return DesktopFriendlyError.Describe(exception);
    }

    // ETP's own RAISERROR/THROW text (migration prechecks 51240, 51260, 51560 and the like) is reviewed
    // product text and says what to do; SQL Server's own messages can quote values, so they never pass.
    private static string? ProductSqlMessage(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is not SqlException sql) continue;
            foreach (SqlError error in sql.Errors)
                if (error.Number >= 50000 && !string.IsNullOrWhiteSpace(error.Message))
                    return $"{error.Message.Trim()} (SQL error {error.Number.ToString(CultureInfo.InvariantCulture)})";
        }
        return null;
    }

    internal static string SingleLine(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text) builder.Append(char.IsControl(character) ? ' ' : character);
        var line = builder.ToString().Trim();
        return line.Length <= MaxReasonLength ? line : line[..(MaxReasonLength - 3)] + "...";
    }

    /// <summary>
    /// IE-CODE-07. The welcome overlay after a failed start. "Cannot reach SQL Server" only when SQL
    /// Server did not answer; a refused login, a missing database and a refused permission each say
    /// so; anything else says ETP could not open and names the step that failed.
    /// </summary>
    public static StartupFailurePresentation Welcome(Exception exception, string step)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var kind = DatabaseConnectionFailure.Classify(exception);
        var title = kind switch
        {
            DatabaseFailureKind.ServerUnreachable => "Cannot reach SQL Server",
            DatabaseFailureKind.Timeout => "SQL Server is not answering",
            DatabaseFailureKind.LoginRefused => "Login refused",
            DatabaseFailureKind.DatabaseMissing => "Database not available",
            DatabaseFailureKind.PermissionDenied => "Permission denied",
            DatabaseFailureKind.InvalidConfiguration => "Connection settings invalid",
            _ => "ETP could not open"
        };
        if (DatabaseConnectionFailure.IsConnectionClass(kind))
            return new(title, DatabaseConnectionFailure.Describe(kind)! + NumberSuffix(exception, kind), kind);
        var reason = DesktopFriendlyError.Describe(exception);
        var where = string.IsNullOrWhiteSpace(step) ? "" : $" while {step}";
        return new(title, $"ETP could not open{where}: {reason}", kind);
    }

    /// <summary>
    /// IE-CODE-06. An access reload that failed. Only a connection-class failure (the database cannot be
    /// used from this account) removes access; anything else keeps the access already loaded. Either way
    /// the status bar says so, instead of every workspace quietly behaving as "No access".
    /// </summary>
    public static AccessRefreshFailure AccessRefresh(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var kind = DatabaseConnectionFailure.Classify(exception);
        if (DatabaseConnectionFailure.IsConnectionClass(kind))
            return new(true, "Access could not be refreshed: " + DatabaseConnectionFailure.Describe(kind) + NumberSuffix(exception, kind), kind);
        return new(false, "Access could not be refreshed: " + DesktopFriendlyError.Describe(exception) +
            " Your current access is kept; reopen ETP if it looks wrong.", kind);
    }

    /// <summary>IE-CODE-08. The connection test's diagnostics event, with the SQL number when there is one.</summary>
    public static string HealthCheckEventId(int? sqlErrorNumber) =>
        sqlErrorNumber is { } number
            ? "DATABASE_HEALTH_CHECK_FAILED.SQL" + number.ToString(CultureInfo.InvariantCulture)
            : "DATABASE_HEALTH_CHECK_FAILED";

    private static string NumberSuffix(Exception exception, DatabaseFailureKind kind) =>
        DatabaseConnectionFailure.DecidingNumber(exception, kind) is { } number
            ? $" (SQL error {number.ToString(CultureInfo.InvariantCulture)})"
            : "";
}
