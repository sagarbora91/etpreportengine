namespace Etp.Reporting.Desktop;

public sealed class ShellNavigationService : IShellNavigationService
{
    private readonly List<WorkspaceRoute> history = [WorkspaceRoute.Home];
    private int index;

    public WorkspaceRoute Current => history[index];
    public bool CanGoBack => index > 0;
    public bool CanGoForward => index < history.Count - 1;
    public IReadOnlyList<WorkspaceRoute> History => history;

    public NavigationDecision Navigate(WorkspaceRoute route, ShellAccess access)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(access);

        var decision = Decide(route, access);
        if (!decision.IsAllowed || Current == route) return decision;

        if (CanGoForward) history.RemoveRange(index + 1, history.Count - index - 1);
        history.Add(route);
        index = history.Count - 1;
        return decision;
    }

    public NavigationDecision GoBack(ShellAccess access) =>
        MoveTo(index - 1, access);

    public NavigationDecision GoForward(ShellAccess access) =>
        MoveTo(index + 1, access);

    private NavigationDecision MoveTo(int targetIndex, ShellAccess access)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (targetIndex < 0 || targetIndex >= history.Count)
            return NavigationDecision.Denied(Current, ShellRouteRegistry.Find(Current.Destination), null);

        var route = history[targetIndex];
        var decision = Decide(route, access);
        if (decision.IsAllowed) index = targetIndex;
        return decision;
    }

    private static NavigationDecision Decide(WorkspaceRoute route, ShellAccess access)
    {
        var descriptor = ShellRouteRegistry.Find(route.Destination);
        if (descriptor is null) return NavigationDecision.Denied(route, null, null);
        if (route == WorkspaceRoute.Home) return NavigationDecision.Allowed(route, descriptor);

        if (route.Destination is "Masters" or "Admin / Settings" ||
            route.Destination == "Settings" && access.HasAssignedRole)
        {
            return access.CanAdminister
                ? NavigationDecision.Allowed(route, descriptor)
                : NavigationDecision.Denied(route, descriptor, "Owner permission is required to open administration and database settings.");
        }

        if (route.Destination == "Import ETP" && !access.CanImport)
            return NavigationDecision.Denied(route, descriptor, "Owner or Store Manager permission is required to import ETP reports.");

        if (route.Destination != "Settings" && !access.CanView)
            return NavigationDecision.Denied(route, descriptor, "This Windows account has not been granted application access by an Owner.");

        return NavigationDecision.Allowed(route, descriptor);
    }
}
