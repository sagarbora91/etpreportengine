namespace Etp.Reporting.Desktop;

public sealed record ShellRouteDescriptor(string Destination, string ModuleId);

/// <summary>Legacy service routes only. Screen names, sections and access live in TaskNavigation.</summary>
public static class ShellRouteRegistry
{
    public static IReadOnlyCollection<ShellRouteDescriptor> All { get; } =
    [
        new("Home", "home"),
        new("Dashboard", "dashboard"),
        new("Daily Workflow", "dashboard"),
        new("Manual Entry", "dashboard"),
        new("Import ETP", "imports"),
        new("Sales Reports", "reports"),
        new("Registers", "registers"),
        new("Accounting", "accounting"),
        new("Operations Center", "exceptions"),
        new("Report Archive", "archive"),
        new("Settings", "settings"),
        new("Admin / Settings", "settings")
    ];

    private static readonly IReadOnlyDictionary<string, ShellRouteDescriptor> Routes =
        All.ToDictionary(route => route.Destination, StringComparer.Ordinal);

    public static ShellRouteDescriptor? Find(string destination) => Routes.GetValueOrDefault(destination);
}
