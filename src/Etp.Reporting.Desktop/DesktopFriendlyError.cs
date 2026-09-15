using System.IO;
using Etp.Reporting.Import.Batch;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop;

public static class DesktopFriendlyError
{
    public static bool IsDatabaseAvailabilityFailure(Exception exception) =>
        exception is SqlException or InvalidOperationException;

    public static bool IsAuditFailure(Exception exception) =>
        exception is SqlException or InvalidOperationException or ArgumentException;

    public static string Describe(Exception exception, string safeUnauthorizedMessage) =>
        exception is UnauthorizedAccessException ? safeUnauthorizedMessage : Describe(exception);

    internal static string? DescribeConnectionFailure(int number) => number switch
    {
        18456 or 18452 => "SQL Server login failed. Check your Windows account access.",
        229 or 230 or 262 or 916 or 4060 => "SQL Server permission denied. Ask the Owner to grant access to this database.",
        0 or -2 or -1 or 2 or 26 or 40 or 53 or 64 or 233 or 258 or 10060 or 10061 or 11001 =>
            "SQL Server is unreachable. Check that the SQL Server service is running and the instance name is correct.",
        _ => null
    };

    public static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Your Windows account does not have permission for this action.",
        FileNotFoundException => "The selected file is no longer available. Select it again.",
        IOException => "The file could not be read. Close it in other applications and try again.",
        SqlException sql when DescribeConnectionFailure(sql.Number) is { } message => message,
        SqlException { Number: 2601 or 2627 } => "This item already exists.",
        SqlException { Number: 51210 } => "This business day is finalised. Reopen it before making changes.",
        SqlException sql when sql.Number >= 51000 => sql.Message,
        ImportSourceException => exception.Message,
        InvalidOperationException or ArgumentException => exception.Message,
        _ => "The action could not be completed. Technical details are available in the support package."
    };
}
