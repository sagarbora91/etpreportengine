namespace Etp.Reporting.Desktop;

public sealed record NavigationDecision(
    bool IsAllowed,
    WorkspaceRoute RequestedRoute,
    ShellRouteDescriptor? Descriptor,
    string? DenialReason)
{
    public static NavigationDecision Allowed(WorkspaceRoute route, ShellRouteDescriptor descriptor) =>
        new(true, route, descriptor, null);

    public static NavigationDecision Denied(WorkspaceRoute route, ShellRouteDescriptor? descriptor, string? reason) =>
        new(false, route, descriptor, reason);
}
