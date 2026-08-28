namespace Etp.Reporting.Desktop;

/// <summary>
/// Declares which shell module owns each navigable workspace route.
/// This registry is intentionally framework-neutral and has no navigation behavior.
/// </summary>
public sealed record WorkspaceModuleOwnership(
    string Destination,
    string? FeatureCode,
    string ModuleId)
{
    public WorkspaceRoute Route => new(Destination, FeatureCode);
    public bool IsReportRoute => FeatureCode is not null;
}

public static class WorkspaceModuleOwnershipRegistry
{
    private static readonly IReadOnlyList<WorkspaceModuleOwnership> DestinationEntries =
    [
        Destination("Home", "home"),
        Destination("Dashboard", "dashboard"),
        Destination("Daily Workflow", "dashboard"),
        Destination("Manual Entry", "dashboard"),
        Destination("Import ETP", "imports"),
        Destination("Sales Reports", "reports"),
        Destination("Stock Reports", "reports"),
        Destination("Registers", "registers"),
        Destination("Accounting", "accounting"),
        Destination("Operations Center", "exceptions"),
        Destination("Report Archive", "archive"),
        Destination("Masters", "settings"),
        Destination("Settings", "settings"),
        Destination("Admin / Settings", "settings")
    ];

    private static readonly IReadOnlyList<WorkspaceModuleOwnership> ReportEntries =
    [
        Report("dsr"),
        Report("sales-titan"),
        Report("sales-helios"),
        Report("sales-combined"),
        Report("invoice"),
        Report("sales-returns"),
        Report("sales-brand"),
        Report("sales-segment"),
        Report("sales-item"),
        Report("stock-closing"),
        Report("stock-physical"),
        Report("stock-variance"),
        Report("stock-movement"),
        Report("stock-group"),
        Report("stock-brand"),
        Report("stock-slow"),
        Report("staff"),
        Report("tender"),
        Report("cash"),
        Report("tender-diagnostic"),
        Report("service"),
        Report("exceptions"),
        Report("exception-source"),
        Report("exception-unmapped"),
        Report("exception-stock"),
        Report("exception-staff"),
        Report("exception-tender"),
        Report("management-trend"),
        Report("invoice-lineage")
    ];

    private static readonly IReadOnlyDictionary<WorkspaceRoute, WorkspaceModuleOwnership> ByRoute =
        DestinationEntries.Concat(ReportEntries).ToDictionary(entry => entry.Route);

    public static IReadOnlyList<WorkspaceModuleOwnership> Destinations { get; } = DestinationEntries;
    public static IReadOnlyList<WorkspaceModuleOwnership> ReportRoutes { get; } = ReportEntries;
    public static IReadOnlyList<WorkspaceModuleOwnership> All { get; } = DestinationEntries.Concat(ReportEntries).ToArray();

    public static WorkspaceModuleOwnership? Find(WorkspaceRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        return ByRoute.GetValueOrDefault(route);
    }

    private static WorkspaceModuleOwnership Destination(string destination, string moduleId) =>
        new(destination, null, moduleId);

    private static WorkspaceModuleOwnership Report(string featureCode) =>
        new("Sales Reports", featureCode, "reports");
}
