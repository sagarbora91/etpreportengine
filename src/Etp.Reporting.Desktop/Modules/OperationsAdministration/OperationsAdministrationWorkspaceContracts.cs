using System.IO;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public sealed record OperationsAdministrationWorkspaceAccess(
    bool CanView,
    bool CanImport,
    bool CanAdminister);

public sealed record MaintenanceOperationResult(bool Succeeded, string Message);

internal static class OperationsAdministrationWorkspaceErrors
{
    public static string Friendly(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Your Windows account does not have permission for this action.",
        FileNotFoundException => "The selected file is no longer available. Select it again.",
        IOException => "The file could not be read. Close it in other applications and try again.",
        SqlException { Number: 2601 or 2627 } => "This item already exists.",
        SqlException { Number: 51210 } => "This business day is finalised. Reopen it before making changes.",
        SqlException sql when sql.Number >= 51000 => sql.Message,
        InvalidOperationException or ArgumentException => exception.Message,
        _ => "The action could not be completed. Technical details are available in the support package."
    };
}
