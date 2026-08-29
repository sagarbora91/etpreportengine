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
            Guide("Confirm SQL Server and the reporting database show ready on the Dashboard; ask an administrator to resolve a connection warning.", "Use the sidebar to open the required module, then choose the business date and store shown by that workspace.", "Follow readiness and control messages before importing, entering data or exporting; unavailable data must remain unavailable rather than being entered as zero.", "Press F1 from a workspace for contextual help, or Ctrl + / for the implemented keyboard shortcuts."),
            ["setup", "sign in", "sidebar", "store", "business date"]),
        Topic("dashboard", "Dashboard", "Understand daily status, warnings and quick actions.", "IconDashboard", 20,
            Guide("Open Dashboard and confirm the displayed business date, store and signed-in role.", "Review database, backup, import and control warnings; warnings do not alter source values.", "Use the Business Day and control sections to open the next required workspace.", "Refresh after an import or manual-entry change before relying on the displayed status."),
            ["status", "warning", "quick action", "daily health"], "Dashboard"),
        Topic("business-day", "Business Day", "Complete daily readiness, manual entry and finalisation.", "IconRegister", 30,
            Guide("Open Daily Workflow for the required store and ETP business date, then review source readiness.", "Open Manual Entry and record only approved values that ETP does not supply; enter zero only when zero is confirmed and include the required reason.", "Generate and review the selected-store or combined pack, including blocking controls and unavailable-source messages.", "Finalise only after blocking sections are resolved; an authorised Owner must reopen a locked date with a recorded reason before correction."),
            ["readiness", "manual entry", "walking", "walk-ins", "lock", "finalise"], "Daily Workflow"),
        Topic("import-etp", "Import ETP", "Import files, folders and ZIP packages safely.", "IconImport", 40,
            Guide("Open Import ETP and choose approved XLSX files, a folder or an ETP ZIP package.", "Review discovered files and the detected report, store and business date before starting the batch.", "Start the import and monitor completed, failed and duplicate counts; cancellation occurs safely between files.", "Read the failure summary, correct the source problem and retry only failed files; never rename a file to bypass duplicate protection."),
            ["file", "folder", "zip", "duplicate", "failure", "retry"], "Import ETP"),
        Topic("daily-sales-report", "Daily Sales Report", "Preview and export the governed DSR.", "IconReports", 50,
            Guide("Open Sales Reports and choose Daily Sales / DSR, then select the required business date and store scope.", "Refresh the preview and review FTD, MTD, YTD, TY/LY, Service, target and control states.", "Treat an unavailable LY MTD as unavailable; do not replace the displayed source-required state with zero.", "Export the governed PDF or Excel output only after the scope and controls are correct."),
            ["dsr", "ftd", "mtd", "ytd", "pdf", "excel", "preview"], "Sales Reports", "dsr"),
        Topic("sales-reports", "Sales Reports", "Run sales, return, brand and comparison reports.", "IconReports", 60,
            Guide("Open Sales Reports and select a live report from the category menu.", "Set the period, store and supported brand-segment, transaction-type or item filters, then run or refresh the report.", "Use search, sorting, variance-only view and row drill-down where the selected report provides them.", "Verify scope, source lineage and control status before exporting PDF or Excel."),
            ["sales", "invoice", "return", "brand", "item", "ly", "ty"], "Sales Reports"),
        Topic("stock-reports", "Stock Reports", "Review closing, physical, movement and variance stock.", "IconArchive", 70,
            Guide("Open Sales Reports and choose the required closing, physical, movement, variance or slow-stock report.", "Set the end date and store; stock reports use the selected end date as their snapshot.", "Review quantities, source signs, missing components and variances without inventing a physical-stock composition.", "Open supporting detail where available, then export only after the snapshot scope is confirmed."),
            ["closing", "physical", "variance", "movement", "inventory", "slow stock"], "Stock Reports"),
        Topic("tender-cash-service", "Tender, Cash & Service", "Review reconciliation, diagnostics and service results.", "IconAccounting", 80,
            Guide("Open the tender, cash or service report for one store and business date.", "Compare R022 invoice/tender controls with the displayed diagnostic and review manual service or cash inputs separately.", "Investigate unknown tender codes and variances; never force a diagnostic difference to zero or approve an unmapped code.", "Correct the authoritative source or approved manual entry, refresh, and retain unresolved findings in the exported result."),
            ["tender", "cash", "service", "upi", "card", "reconciliation"], "Sales Reports", "tender"),
        Topic("staff-cro", "Staff / CRO", "Review performance, targets and attribution exceptions.", "IconUser", 90,
            Guide("Open Staff / CRO reporting and select the required period and store.", "Review attributed sales, units, invoices, targets, ranking and unassigned transactions.", "Keep the staff-attributed denominator separate from the canonical invoice denominator and inspect any displayed variance.", "Drill into supporting rows where available before exporting the reviewed result."),
            ["staff", "cro", "target", "ranking", "unassigned", "performance"], "Sales Reports", "staff"),
        Topic("exception-centre", "Exception Centre", "Find blockers, warnings and the evidence needed to resolve them.", "IconAlert", 100,
            Guide("Open Operations Center and choose Open Items, Data Quality or Approval Centre as permitted by your role.", "Filter to the affected store/date and select the highest-severity unresolved item.", "Read its source, safe diagnostic and recommended action; use the linked import, report or accounting workspace to correct the cause.", "Refresh the item and retain it as open when source data or Owner approval is still required."),
            ["exception", "blocker", "warning", "missing source", "unmapped", "approval"], "Operations Center"),
        Topic("management", "Management", "Review trends, targets and management report packs.", "IconDashboard", 110,
            Guide("Open Sales Reports and choose the management trend or required management report.", "Set the period and store scope, then refresh the governed summary.", "Review targets, trends, comparison states and any missing-source warning; use drill-down to inspect supporting results.", "Generate a report pack or export only after confirming the displayed scope and controls."),
            ["management", "trend", "target", "report pack", "summary"], "Sales Reports", "management-trend"),
        Topic("investigation", "Investigation", "Trace an invoice to its source evidence.", "IconSearch", 120,
            Guide("Open Investigation and enter the invoice or document identifier supplied by the governed result.", "Review sales, returns and tender details without copying restricted customer information into support material.", "Follow workbook, worksheet and source-row lineage to identify the authoritative source.", "Record the finding and return to the owning report or import workflow for any authorised correction."),
            ["invoice", "document", "lineage", "source", "evidence", "drill-down"], "Operations Center", "invoice-drilldown"),
        Topic("digital-registers", "Digital Registers", "Create and review document-linked operational registers.", "IconRegister", 130,
            Guide("Open Registers and select an available register; unavailable schemas remain visibly unavailable.", "Choose the correct store/date and link the retained Source Inbox document when the workflow requires evidence.", "Enter the approved operational fields, review them and save with the current Windows identity recorded.", "Search or review the audit history; reopen a locked date through the authorised workflow before correcting an entry."),
            ["inward", "outward", "credit note", "service receipt", "courier", "stock transfer", "expense", "vendor"], "Registers"),
        Topic("accounting", "Accounting", "Prepare governed accounting batches and controlled exports.", "IconAccounting", 140,
            Guide("Open Accounting and prepare a batch from a final immutable report generation.", "Review the proposed ledger mapping, tax treatment, balance and exceptions without inferring an unapproved mapping.", "A Store Manager may save a review batch; an Owner must approve mapping changes and controlled export.", "Back up the Tally company, export the balanced XML, then review Tally import exceptions and the ETP export history."),
            ["accounting", "ledger", "mapping", "batch", "tally", "export", "reconciliation"], "Accounting"),
        Topic("operations-support", "Operations & Support", "Review system health, schedules and support information.", "IconSettings", 150,
            Guide("Open Admin / Settings for system configuration or Operations Center for health, schedules and support actions.", "Review SQL, backup age, disk capacity, import, OCR, scheduler and integration states.", "Use only Owner-authorised local folders and schedules; run the privacy-safe support package when diagnostics are needed.", "Share the generated support ZIP without source workbooks, database backups, credentials or confidential screenshots."),
            ["operations", "support", "system health", "scheduler", "watch folder", "printer", "database growth"], "Admin / Settings"),
        Topic("administration", "Administration", "Manage authorised users, stores, rules and master data.", "IconSettings", 160,
            Guide("Open Admin / Settings while signed in as an Owner; other roles cannot change governed configuration.", "Select Users & Roles, Stores, Master Data, Import Profiles, KPI, tender, OCR or sharing settings.", "Review the current value and enter a meaningful reason before an authorised change; do not approve unknown mappings or business rules.", "Save, refresh the affected workspace and confirm the audit trail records the Windows identity and change."),
            ["user", "role", "store", "master", "profile", "kpi", "rule", "audit"], "Admin / Settings"),
        Topic("report-archive", "Report Archive", "Verify, compare, re-export and package immutable report generations.", "IconArchive", 170,
            Guide("Open Report Archive and select the required store, period or report generation.", "Open a generation to verify its document hash and review its recorded scope before using it.", "Select two compatible generations to compare them, or re-export the currently opened generation without changing its stored result.", "Create the governed ZIP manifest or initiate an approved email/WhatsApp share; the application records initiation unless delivery is independently verifiable."),
            ["archive", "generation", "hash", "compare", "re-export", "zip", "share"], "Report Archive"),
        Topic("backup-recovery", "Backup & Recovery", "Check backup health and recovery readiness.", "IconArchive", 180,
            Guide("Open Operations Center and review SQL service state, latest backup time, free capacity and recovery-drill history.", "Create a checksum backup to the approved destination and verify it before relying on it.", "For a drill, restore into a separate recovery database, run integrity and lineage comparisons, and never overwrite production.", "Record the backup identity, restore result, comparisons, operator and time; retain database backups indefinitely."),
            ["backup", "restore", "sql", "disk space", "health", "recovery"], "Operations Center"),
        Topic("troubleshooting", "Troubleshooting", "Resolve common import, database and export problems.", "IconHelp", 190,
            Guide("Start with the plain-language message in the affected workspace and record its safe error code and event time.", "For database or backup warnings open System Health; for imports review the batch summary; for reports confirm date/store and required sources.", "Retry only after correcting the reported cause, and never bypass an unknown layout, missing mapping or control failure.", "Generate the privacy-safe support ZIP and send only that package through the authorised support process."),
            ["problem", "error", "sql", "pdf", "printer", "import", "support package"]),
        new(KeyboardShortcutsTopicId, "Keyboard Shortcuts", "Work faster using familiar Windows keyboard commands.", "IconHelp", 200,
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
        new(id, title, description, iconKey, order, HelpTopicAvailability.Available, keywords, overview, destination, featureCode);

    private static string Guide(params string[] steps) => string.Join(
        Environment.NewLine + Environment.NewLine,
        steps.Select((step, index) => $"{index + 1}. {step}"));
}

public static class ContextHelpRouter
{
    private static readonly IReadOnlyDictionary<string, string> DestinationTopics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Home"] = "getting-started",
        ["Dashboard"] = "dashboard",
        ["Daily Workflow"] = "business-day",
        ["Manual Entry"] = "business-day",
        ["Import ETP"] = "import-etp",
        ["Sales Reports"] = "sales-reports",
        ["Stock Reports"] = "stock-reports",
        ["Report Archive"] = "report-archive",
        ["Registers"] = "digital-registers",
        ["Accounting"] = "accounting",
        ["Operations Center"] = "exception-centre",
        ["Investigation"] = "investigation",
        ["Admin / Settings"] = "administration",
        ["Masters"] = "administration",
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
