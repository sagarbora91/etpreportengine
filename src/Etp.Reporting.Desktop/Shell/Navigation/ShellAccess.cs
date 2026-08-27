namespace Etp.Reporting.Desktop;

public sealed record ShellAccess(
    bool HasAssignedRole,
    bool CanView,
    bool CanImport,
    bool CanAdminister)
{
    public static ShellAccess DatabaseSetup { get; } = new(false, false, false, false);
    public static ShellAccess Viewer { get; } = new(true, true, false, false);
    public static ShellAccess StoreManager { get; } = new(true, true, true, false);
    public static ShellAccess Owner { get; } = new(true, true, true, true);
}
