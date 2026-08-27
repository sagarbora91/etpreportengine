namespace Etp.Reporting.Desktop;

public interface IShellNavigationService
{
    WorkspaceRoute Current { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    IReadOnlyList<WorkspaceRoute> History { get; }

    NavigationDecision Navigate(WorkspaceRoute route, ShellAccess access);
    NavigationDecision GoBack(ShellAccess access);
    NavigationDecision GoForward(ShellAccess access);
}
