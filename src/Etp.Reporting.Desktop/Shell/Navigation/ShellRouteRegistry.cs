namespace Etp.Reporting.Desktop;

public sealed record ShellRouteDescriptor(
    string Destination,
    string ModuleId,
    string Description,
    string Heading,
    string Message,
    string ActionLabel,
    string ActionDestination);

public static class ShellRouteRegistry
{
    private static readonly IReadOnlyDictionary<string, ShellRouteDescriptor> Routes =
        new Dictionary<string, ShellRouteDescriptor>(StringComparer.Ordinal)
        {
            ["Home"] = Route("Home", "home", "Choose a module. Daily work stays on the surface while governed controls remain underneath.", "Home", "Choose a module to begin.", "", "Home"),
            ["Dashboard"] = Route("Dashboard", "dashboard", "Today’s reporting status, required actions and application health.", "Business day cockpit", "Follow Import → Check → Complete inputs → Generate → Finalise → Share.", "Open daily workflow", "Daily Workflow"),
            ["Daily Workflow"] = Route("Daily Workflow", "dashboard", "Complete, reconcile and finalise one ETP business date.", "Daily reporting", "Review source completeness, enter only non-ETP operational values, then finalise the protected day.", "Daily workflow ready", "Daily Workflow"),
            ["Manual Entry"] = Route("Manual Entry", "dashboard", "Enter governed business-day values that are not supplied by ETP reports.", "Manual Entry", "Start with walk-ins. Additional approved fields will appear here automatically when they are registered in the database.", "Manual Entry ready", "Manual Entry"),
            ["Import ETP"] = Route("Import ETP", "imports", "Import ETP workbooks and manage every received source document.", "Import and Source Inbox", "Import one workbook, a large historical folder, safe ZIP package, PDF or image with progress and review queues.", "Import ready", "Import ETP"),
            ["Sales Reports"] = Route("Sales Reports", "reports", "Preview and export every approved report through one consistent workspace.", "Reports Centre", "Use the same period, store, brand, transaction and item filters across Sales, Stock, Staff, Tender, Service and Exceptions.", "Run reports below", "Sales Reports"),
            ["Stock Reports"] = Route("Stock Reports", "reports", "View approved stock movement and balance reports.", "Stock reporting", "Reconcile the stock ledger to the closing-stock snapshot using source-signed quantities.", "Run reports below", "Stock Reports"),
            ["Registers"] = Route("Registers", "registers", "Create and search audited registers linked to immutable documents.", "Digital registers", "Start with the Inward Register and reuse the same governed structure for future register types.", "Registers ready", "Registers"),
            ["Accounting"] = Route("Accounting", "accounting", "Prepare balanced accounting batches from final report generations.", "Accounting preparation", "Owner-approved ledger mappings remain mandatory before review, approval and Tally XML export.", "Accounting ready", "Accounting"),
            ["Operations Center"] = Route("Operations Center", "exceptions", "Investigate issues, approvals, trends, automation, backup and product health.", "Control Centre", "Business resolution never changes the underlying technical control result.", "Control Centre ready", "Operations Center"),
            ["Report Archive"] = Route("Report Archive", "archive", "Open, compare, package and share immutable report generations.", "Archive and sharing", "Every archived document and ZIP package is integrity checked and remains tied to its generation.", "Archive ready", "Report Archive"),
            ["Masters"] = Route("Masters", "settings", "Maintain reporting reference data.", "Master data", "Review confirmed Brand Segment descriptions while unresolved mappings remain fail-closed.", "Review dictionary", "Masters"),
            ["Settings"] = Route("Settings", "settings", "Configure the application and database connection.", "Connection settings", "Test the saved Windows-integrated SQL Server connection or safely create/update the database.", "Configuration ready", "Settings"),
            ["Admin / Settings"] = Route("Admin / Settings", "settings", "Administer users, masters, KPI definitions, integrations and database settings.", "Admin and Settings", "Owner-only changes remain versioned or audited and cannot rewrite locked history.", "Administration ready", "Admin / Settings")
        };

    public static IReadOnlyCollection<ShellRouteDescriptor> All { get; } = Routes.Values.ToArray();

    public static ShellRouteDescriptor? Find(string destination) =>
        Routes.GetValueOrDefault(destination);

    private static ShellRouteDescriptor Route(
        string destination,
        string moduleId,
        string description,
        string heading,
        string message,
        string actionLabel,
        string actionDestination) =>
        new(destination, moduleId, description, heading, message, actionLabel, actionDestination);
}
