namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public sealed record OperationsAdministrationWorkspaceAccess(
    bool CanView,
    bool CanImport,
    bool CanAdminister);

public sealed record MaintenanceOperationResult(bool Succeeded, string Message);

internal static class OperationsAdministrationWorkspaceErrors
{
    public static string Friendly(Exception exception) => DesktopFriendlyError.Describe(exception);
}
