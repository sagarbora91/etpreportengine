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

    public static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Your Windows account does not have permission for this action.",
        FileNotFoundException => "The selected file is no longer available. Select it again.",
        IOException => "The file could not be read. Close it in other applications and try again.",
        SqlException { Number: 2601 or 2627 } => "This item already exists.",
        SqlException { Number: 51210 } => "This business day is finalised. Reopen it before making changes.",
        SqlException sql when sql.Number >= 51000 => sql.Message,
        ImportSourceException => exception.Message,
        InvalidOperationException or ArgumentException => exception.Message,
        _ => "The action could not be completed. Technical details are available in the support package."
    };
}
