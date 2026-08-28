using System.Windows.Input;

namespace Etp.Reporting.Desktop;

public enum HelpTopicAvailability
{
    Available,
    Overview,
    ComingSoon
}

public sealed record HelpTopicDefinition(
    string Id,
    string Title,
    string Description,
    string IconKey,
    int Order,
    HelpTopicAvailability Availability,
    IReadOnlyList<string> Keywords,
    string Overview,
    string? Destination = null,
    string? FeatureCode = null);

public sealed record ShortcutDefinition(
    string Category,
    string Keys,
    string Action,
    string Scope = "Everywhere",
    bool RequiresPermission = false,
    ShellCommand? Command = null);

public static class HelpCommands
{
    public static RoutedUICommand OpenHelpCentre { get; } = new(
        "Open Help Centre",
        nameof(OpenHelpCentre),
        typeof(HelpCommands),
        [new KeyGesture(Key.F1)]);

    public static RoutedUICommand OpenKeyboardShortcuts { get; } = new(
        "Open Keyboard Shortcuts",
        nameof(OpenKeyboardShortcuts),
        typeof(HelpCommands),
        [new KeyGesture(Key.Oem2, ModifierKeys.Control)]);
}

public static class HelpCentreRegistry
{
    public const string HomeTopicId = "help-home";
    public const string KeyboardShortcutsTopicId = "keyboard-shortcuts";

    public static IReadOnlyList<HelpTopicDefinition> Topics { get; } =
    [
        Topic("getting-started", "Getting Started", "Set up the application and learn the main navigation.", "IconDashboard", 10,
            "Choose a module from the main navigation, select the correct business date and store, then follow the readiness messages shown on each workspace.",
            ["setup", "sign in", "sidebar", "store", "business date"]),
        Topic("dashboard", "Dashboard", "Understand daily status, warnings and quick actions.", "IconDashboard", 20,
            "The Dashboard brings together the current business-day status, important warnings and shortcuts to the next required action.",
            ["status", "warning", "quick action", "daily health"], "Dashboard"),
        Topic("business-day", "Business Day", "Complete daily readiness, manual entry and finalisation.", "IconRegister", 30,
            "Use Business Day to check source readiness, enter approved information that ETP does not provide and finalise the day after controls pass.",
            ["readiness", "manual entry", "walking", "walk-ins", "lock", "finalise"], "Daily Workflow"),
        Topic("import-etp", "Import ETP", "Import files, folders and ZIP packages safely.", "IconImport", 40,
            "Import ETP accepts supported source reports, checks for duplicates and explains missing or rejected files before data reaches reporting.",
            ["file", "folder", "zip", "duplicate", "failure", "retry"], "Import ETP"),
        Topic("daily-sales-report", "Daily Sales Report", "Preview and export the governed DSR.", "IconReports", 50,
            "Select the business date, refresh the preview, resolve any unavailable inputs and export the final DSR from its dedicated workspace.",
            ["dsr", "ftd", "mtd", "ytd", "pdf", "excel", "preview"], "Sales Reports", "dsr"),
        Topic("sales-reports", "Sales Reports", "Run sales, return, brand and comparison reports.", "IconReports", 60,
            "Choose a sales report from the workspace menu, apply its available filters and use the fixed actions to refresh, export or investigate a result.",
            ["sales", "invoice", "return", "brand", "item", "ly", "ty"], "Sales Reports"),
        Topic("stock-reports", "Stock Reports", "Review closing, physical, movement and variance stock.", "IconArchive", 70,
            "Stock reporting keeps the snapshot date and store visible while you review quantities, variances and source exceptions.",
            ["closing", "physical", "variance", "movement", "inventory", "slow stock"], "Stock Reports"),
        Topic("tender-cash-service", "Tender, Cash & Service", "Review reconciliation, diagnostics and service results.", "IconAccounting", 80,
            "Control totals remain authoritative. Diagnostics explain differences without changing the approved reconciliation result.",
            ["tender", "cash", "service", "upi", "card", "reconciliation"], "Sales Reports", "tender"),
        Topic("staff-cro", "Staff / CRO", "Review performance, targets and attribution exceptions.", "IconUser", 90,
            "Use Staff / CRO reporting to review attributed sales, units, invoices, targets, rankings and any unassigned transactions.",
            ["staff", "cro", "target", "ranking", "unassigned", "performance"], "Sales Reports", "staff"),
        Topic("exception-centre", "Exception Centre", "Find blockers, warnings and the evidence needed to resolve them.", "IconAlert", 100,
            "The Exception Centre groups open control findings by severity and source. Select an item to review its evidence and recommended action.",
            ["exception", "blocker", "warning", "missing source", "unmapped", "approval"], "Operations Center"),
        Topic("management", "Management", "Review trends, targets and management report packs.", "IconDashboard", 110,
            "Management workspaces provide high-level trends and comparisons while retaining drill-down access to governed source results.",
            ["management", "trend", "target", "report pack", "summary"], "Sales Reports", "management-trend"),
        Topic("investigation", "Investigation", "Trace an invoice to its source evidence.", "IconSearch", 120,
            "Search for an invoice or document, review its sales, return and tender details, then inspect workbook, worksheet and source-row lineage.",
            ["invoice", "document", "lineage", "source", "evidence", "drill-down"], "Investigation"),
        Topic("digital-registers", "Digital Registers", "Create and review document-linked operational registers.", "IconRegister", 130,
            "Registers retain linked documents and audit information. Registers whose approved schema is unavailable are clearly marked as unavailable.",
            ["inward", "outward", "credit note", "service receipt", "courier", "stock transfer", "expense", "vendor"], "Registers"),
        Topic("accounting", "Accounting", "Prepare governed accounting batches and controlled exports.", "IconAccounting", 140,
            "Accounting workspaces validate mappings and balanced batches before an authorised export. They never post inferred entries or bypass approval controls.",
            ["accounting", "ledger", "mapping", "batch", "tally", "export", "reconciliation"], "Accounting"),
        Topic("operations-support", "Operations & Support", "Review system health, schedules and support information.", "IconSettings", 150,
            "Operations & Support brings together database health, failed imports, watch folders, schedules, printer status and safe support-package generation.",
            ["operations", "support", "system health", "scheduler", "watch folder", "printer", "database growth"], "Admin / Settings"),
        Topic("administration", "Administration", "Manage authorised users, stores, rules and master data.", "IconSettings", 160,
            "Administration is restricted by role. Changes to mappings and control rules remain governed and auditable.",
            ["user", "role", "store", "master", "profile", "kpi", "rule", "audit"], "Admin / Settings"),
        Topic("backup-recovery", "Backup & Recovery", "Check backup health and recovery readiness.", "IconArchive", 170,
            "Review SQL Server status, latest backup time, available space and restore-drill history before taking an authorised recovery action.",
            ["backup", "restore", "sql", "disk space", "health", "recovery"], "Operations Center"),
        Topic("troubleshooting", "Troubleshooting", "Resolve common import, database and export problems.", "IconHelp", 180,
            "Start with the message shown by the affected workspace. System Health and the offline support package provide safe diagnostic details without confidential rows.",
            ["problem", "error", "sql", "pdf", "printer", "import", "support package"]),
        new(KeyboardShortcutsTopicId, "Keyboard Shortcuts", "Work faster using familiar Windows keyboard commands.", "IconHelp", 190,
            HelpTopicAvailability.Available,
            ["keyboard", "shortcut", "hotkey", "accessibility", "back", "forward", "refresh", "export"],
            "Search or browse all supported keyboard shortcuts. Shortcuts respect the current user role, enabled actions and unsaved changes.")
    ];

    public static IReadOnlyList<ShortcutDefinition> Shortcuts { get; } =
    [
        Executable("Navigation", ShellCommand.Back, "Go to the previous screen"),
        Executable("Navigation", ShellCommand.Forward, "Go forward after returning to a previous screen"),
        Executable("Navigation", ShellCommand.Home, "Open Dashboard"),
        new("Navigation", "Ctrl + Tab", "Move to the next section in the current workspace", "Workspaces with sections"),
        new("Navigation", "Ctrl + Shift + Tab", "Move to the previous section in the current workspace", "Workspaces with sections"),
        Executable("Navigation", ShellCommand.CycleRegion, "Move focus between the sidebar, filters, preview and details"),
        Executable("Navigation", ShellCommand.CloseOrCancel, "Close the current dialog, drawer or menu"),
        Executable("Help", ShellCommand.Help, "Open help for the current screen"),
        Executable("Help", ShellCommand.ShortcutGuide, "Open Keyboard Shortcuts"),
        Executable("Reports", ShellCommand.Refresh, "Refresh the current report or workspace"),
        Executable("Reports", ShellCommand.Run, "Run the selected report", "Report workspaces"),
        Executable("Reports", ShellCommand.Search, "Focus search in the current workspace"),
        Executable("Reports", ShellCommand.ExportPdf, "Open PDF and print options", "Report workspaces"),
        Executable("Reports", ShellCommand.ExportExcel, "Export the current report to Excel", "Report workspaces", true),
        Executable("Reports", ShellCommand.GenerateReportPack, "Open Report Pack generation", "Management and report workspaces", true),
        Executable("Reports", ShellCommand.OpenExportFolder, "Open the generated export folder", "Report workspaces", true),
        Executable("Reports", ShellCommand.FocusPeriod, "Focus the business-date or period control", "Report workspaces"),
        Executable("Reports", ShellCommand.GoToReport, "Open Go to Report search", "Report workspaces"),
        Executable("Data entry", ShellCommand.Save, "Save the current entry", "Manual Entry and registers", true),
        new("Data entry", "Enter", "Activate the primary action or open the selected record"),
        Executable("Import", ShellCommand.ImportFiles, "Select ETP files or a ZIP package", "Import ETP", true),
        Executable("Import", ShellCommand.ImportFolder, "Select an import folder", "Import ETP", true),
        Executable("Import", ShellCommand.RetryImport, "Retry the selected failed import", "Import ETP", true),
        new("Tables", "Arrow keys", "Move between rows and cells", "Result tables"),
        new("Tables", "Home / End", "Move to the first or last column", "Result tables"),
        new("Tables", "Ctrl + Home / Ctrl + End", "Move to the first or last result", "Result tables"),
        new("Tables", "Page Up / Page Down", "Move through result pages", "Result tables"),
        new("Tables", "Shift + F10", "Open the selected row's context menu", "Result tables"),
        new("Tables", "Ctrl + C", "Copy permitted selected values", "Result tables"),
        new("Accessibility", "Tab / Shift + Tab", "Move to the next or previous interactive control"),
        new("Accessibility", "Space", "Toggle or activate the focused control"),
        new("Application", "Alt + F4", "Close the application using normal exit checks")
    ];

    private static ShortcutDefinition Executable(
        string category,
        ShellCommand command,
        string action,
        string scope = "Everywhere",
        bool requiresPermission = false)
    {
        var shortcut = ShellShortcutRegistry.All.Single(item => item.Command == command);
        return new(category, shortcut.Display, action, scope, requiresPermission, command);
    }

    public static HelpTopicDefinition? Find(string topicId) =>
        Topics.FirstOrDefault(x => string.Equals(x.Id, topicId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<HelpTopicDefinition> Search(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Topics.OrderBy(x => x.Order).ToArray();
        var query = text.Trim();
        return Topics.Where(x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || x.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || x.Keywords.Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Order).ToArray();
    }

    public static IReadOnlyList<ShortcutDefinition> SearchShortcuts(string? text, string? category = null)
    {
        var query = text?.Trim();
        return Shortcuts.Where(x => (string.IsNullOrWhiteSpace(category) || category == "All" || string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(query) || x.Keys.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.Action.Contains(query, StringComparison.OrdinalIgnoreCase)
                || x.Scope.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static HelpTopicDefinition Topic(string id, string title, string description, string iconKey, int order,
        string overview, IReadOnlyList<string> keywords, string? destination = null, string? featureCode = null) =>
        new(id, title, description, iconKey, order, HelpTopicAvailability.Overview, keywords, overview, destination, featureCode);
}

public static class ContextHelpRouter
{
    private static readonly IReadOnlyDictionary<string, string> DestinationTopics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Dashboard"] = "dashboard",
        ["Daily Workflow"] = "business-day",
        ["Manual Entry"] = "business-day",
        ["Import ETP"] = "import-etp",
        ["Sales Reports"] = "sales-reports",
        ["Stock Reports"] = "stock-reports",
        ["Registers"] = "digital-registers",
        ["Accounting"] = "accounting",
        ["Operations Center"] = "exception-centre",
        ["Investigation"] = "investigation",
        ["Admin / Settings"] = "administration",
        ["Settings"] = "administration"
    };

    private static readonly IReadOnlyDictionary<string, string> FeatureTopics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["dsr"] = "daily-sales-report",
        ["staff"] = "staff-cro",
        ["management-trend"] = "management",
        ["invoice-drilldown"] = "investigation",
        ["stock-closing"] = "stock-reports",
        ["stock-physical"] = "stock-reports",
        ["stock-variance"] = "stock-reports",
        ["tender"] = "tender-cash-service",
        ["tender-diagnostic"] = "tender-cash-service",
        ["service-sales"] = "tender-cash-service"
    };

    public static string ResolveTopicId(string? destination, string? featureCode = null)
    {
        if (!string.IsNullOrWhiteSpace(featureCode) && FeatureTopics.TryGetValue(featureCode, out var featureTopic)) return featureTopic;
        if (!string.IsNullOrWhiteSpace(destination) && DestinationTopics.TryGetValue(destination, out var destinationTopic)) return destinationTopic;
        return HelpCentreRegistry.HomeTopicId;
    }
}
