using System.Windows.Input;

namespace Etp.Reporting.Desktop;

public enum HelpTopicAvailability
{
    Available
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
    public static string Category(string topicId) => topicId switch
    {
        "getting-started" or "dashboard" or "keyboard-shortcuts" => "Getting Started",
        "business-day" or "import-etp" or "import-history" or "digital-registers" => "Daily Work",
        "daily-sales-report" or "sales-reports" or "stock-reports" or "tender-cash-service" or "staff-cro" or "management" => "Reports",
        "exception-centre" or "investigation" or "accounting" or "report-archive" => "Review & Records",
        _ => "Settings & Support"
    };

    public static IReadOnlyList<HelpTopicDefinition> Topics { get; } =
    [
        Topic("getting-started", "Getting Started", "Set up the application and learn the main navigation.", "IconDashboard", 10,
            Guide("Choose Today, Import, Reports, Stock or Settings on the left. The header shows the business date and store.", "Use Today for daily sales, cash, walk-ins and Close day. Import reads a folder or ZIP and keeps a durable history.", "Use Reports for filters, exports, investigations and the archive; Stock contains stock reports, physical counts and registers. Settings contains Owner configuration and approvals.", "Press Ctrl+K to find a task or F1 for its guide. Saving and approving depend on your Windows role."),
            ["setup", "sign in", "sidebar", "store", "business date"]),
        Topic("dashboard", "Today", "Sales, cash, walk-ins and Close day.", "IconDashboard", 20,
            Guide("Today opens the daily sales report for the header date. Choose the store before entering data.", "Use Cash for the cash book and expense entries; use Walk-ins for daily inputs.", "Close day shows readiness and register entries. Generate and review the pack before finalising.", "For database size, verified backups and recovery evidence, an Owner opens Settings → Database → Database health."),
            ["status", "warning", "quick action", "daily health"], "Dashboard"),
        Topic("business-day", "Business Day", "Complete daily readiness, manual entry and finalisation.", "IconRegister", 30,
            Guide("Choose Today → Close day and a store/date. Review required imports and missing inputs.", "Enter confirmed walk-ins and cash/service inputs; stock counts are under Stock. Register entries for the day are shown in Close day.", "Generate the pack, review all blockers, then finalise. Unavailable source figures remain unavailable.", "A finalised day is protected. Corrections need the authorised reopen or restatement workflow and a reason."),
            ["readiness", "manual entry", "walking", "walk-ins", "lock", "finalise"], "Daily Workflow"),
        Topic("import-etp", "Import ETP", "Import files, folders and ZIP packages safely.", "IconImport", 40,
            Guide("Open Import → Import folder and select the folder or ZIP received from ETP. Stores and dates are detected from workbook data.", "Start once. Review imported, duplicate, not needed, empty and failed outcomes. Originals are retained.", "Open History after restarting to see persisted imports and Received files. Problems shows safe diagnostics and Retry failed retries only failed entries.", "A replacement import creates a restatement request and waits for Owner approval. Retry the same file and scope after the Owner decides."),
            ["file", "folder", "zip", "duplicate", "failure", "retry"], "Import ETP"),
        Topic("import-history", "Import History", "Review saved outcomes and repeat import attempts.", "IconImport", 45,
            Guide("Open Import → History → Imports and set the header scope.", "Select an import to review its persisted outcome and safe diagnostics. History survives app restarts.", "Received files lists retained originals with integrity checks. Opening a file verifies its stored hash.", "Use Problems to investigate failures. Owner and Store Manager can retry; Viewer access is read-only."),
            ["import", "history", "duplicate", "diagnostics", "received files"], "Import History"),
        Topic("daily-sales-report", "Daily Sales Report", "Preview and export the approved DSR.", "IconReports", 50,
            Guide("Search DSR with Ctrl+K, or open Reports → Sales → Daily Sales Report, then select the required business date. DSR compares Titan and Helios with combined totals.", "Refresh the preview and review FTD, MTD, YTD, TY/LY, Service, target and control states. Other reports have Summary and Detail rows tabs; select a row to open its source details.", "Treat an unavailable LY MTD as unavailable; do not replace the displayed source-required state with zero. Ctrl+F finds page text or filters the active detail table; it does not change exported totals.", "Use Actions to export PDF or Excel after refreshing the displayed scope, or generate a report pack for that date and store."),
            ["dsr", "ftd", "mtd", "ytd", "pdf", "excel", "preview"], "Sales Reports", "dsr"),
        Topic("sales-reports", "Sales Reports", "Run sales, return, brand and comparison reports.", "IconReports", 60,
            Guide("Open Reports and choose a report. Select dates and an active store from the database catalogue.", "Filters apply to store, brand segment, transaction type and item where supported. The applied scope is printed on exports.", "Run or refresh, then inspect totals and source rows. Clearing filters restores the broader report.", "Export Excel or PDF from the current successful result. A changed scope requires a refresh before export."),
            ["sales", "invoice", "return", "brand", "item", "ly", "ty"], "Sales Reports"),
        Topic("stock-reports", "Stock Reports", "Review closing, physical, movement and variance stock.", "IconArchive", 70,
            Guide("Open Stock and select Closing stock, Physical count, Stock ledger or a stock register.", "Choose the store/date. ETP Closing Stock supplies system stock; enter Display, Backstock, Defective and Y Loc as actual counts.", "Review the difference and source availability. Prior counts are suggestions to review, not confirmation for today.", "Owner and Store Manager can enter registers; only an Owner verifies them with a reason."),
            ["closing", "physical", "variance", "movement", "inventory", "slow stock"], "Sales Reports"),
        Topic("tender-cash-service", "Tender, Cash & Service", "Review reconciliation, diagnostics and service results.", "IconAccounting", 80,
            Guide("Open the tender, cash or service report for one store and business date.", "Compare R022 invoice/tender controls with the displayed diagnostic and review manual service or cash inputs separately.", "Investigate unknown tender codes and variances; never force a diagnostic difference to zero or approve an unmapped code.", "Correct the authoritative source or approved manual entry, refresh, and retain unresolved findings in the exported result."),
            ["tender", "cash", "service", "upi", "card", "reconciliation"], "Sales Reports", "tender"),
        Topic("staff-cro", "Staff / CRO", "Review performance, targets and attribution exceptions.", "IconUser", 90,
            Guide("Open the Staff / CRO report and choose the date range and store.", "Review net sales, net quantity and unique invoices attributed to each CRO.", "ATV is CRO net sales divided by CRO unique invoices; AUPT is CRO net quantity divided by CRO unique invoices. Missing or zero denominators remain unavailable.", "Owner-maintained targets appear with the report. Inspect unassigned transactions before exporting."),
            ["staff", "cro", "target", "ranking", "unassigned", "performance"], "Sales Reports", "staff"),
        Topic("exception-centre", "Exception Centre", "Find blockers, warnings and the evidence needed to resolve them.", "IconAlert", 100,
            Guide("Open Reports → Exceptions for report exceptions, Import → Problems for file failures, or the Owner control centre for operational issues.", "Select the affected store/date and inspect the cause and supporting record.", "Correct source data through imports, or raise the authorised adjustment/restatement request. Do not invent a replacement value.", "Refresh after the correction. Unresolved data or approval requirements remain visible."),
            ["exception", "blocker", "warning", "missing source", "unmapped", "approval"], "Operations Center"),
        Topic("management", "Management", "Review trends, targets and management report packs.", "IconDashboard", 110,
            Guide("Open Reports → Management → Management Trend.", "Set the period and store scope, then refresh the approved summary.", "Review targets, trends, comparison states and any missing-source warning; use drill-down to inspect supporting results.", "Generate a report pack or export only after confirming the displayed scope and controls."),
            ["management", "trend", "target", "report pack", "summary"], "Sales Reports", "management-trend"),
        Topic("investigation", "Investigation", "Trace an invoice to its source evidence.", "IconSearch", 120,
            Guide("Open Reports → Investigation as Owner or Store Manager and enter an invoice, product or document reference.", "Select a result and use Open selected (or double-click) to reach the report, import, archive or register record it names.", "Check the destination store, business date and selected reference before taking action.", "Customer information stays in authorised reports and retained sources; exclude it from support diagnostics."),
            ["invoice", "document", "lineage", "source", "evidence", "drill-down"], "Operations Center", "invoice-drilldown"),
        Topic("digital-registers", "Digital Registers", "Create and review document-linked operational registers.", "IconRegister", 130,
            Guide("Open stock registers from Stock or money/document registers from Today → Close day. Courier and Expense are both retained.", "Choose the store/date, enter the register fields and save a draft. Owner and Store Manager may create and edit drafts.", "An Owner verifies the selected draft with a reason. Verified entries and locked business days cannot be silently edited.", "Review today’s register entries in Close day and open the selected entry to inspect its detail."),
            ["inward", "outward", "credit note", "service receipt", "courier", "stock transfer", "expense", "vendor"], "Registers"),
        Topic("accounting", "Accounting", "Prepare approved accounting batches and controlled exports.", "IconAccounting", 140,
            Guide("As Owner, open Settings → Accounting → Prepare → Review → Export. Set the Tally company and TEST environment in Settings first.", "Prepare from a final report generation. Missing mappings or company setup produce a BLOCKED batch with a reason. Each invoice can belong to only one non-rejected batch.", "Review the balanced entries. Approve or reject with a reason; rejecting an unexported batch releases its invoices for a replacement batch.", "Export approved TEST XML and review its permanent receipt: file, SHA-256, company and environment. Exported means awaiting import; actual Tally import/read-back belongs to the later Tally integration phase."),
            ["accounting", "ledger", "mapping", "batch", "tally", "export", "reconciliation"], "Accounting"),
        Topic("operations-support", "Operations & Support", "Review system health, schedules and support information.", "IconSettings", 150,
            Guide("An Owner opens Settings → Database → Database health to see database size, disk space and verified backup/recovery evidence.", "Settings → Automatic import shows the installed Windows task and its last run separately from folder configuration. Missing task evidence is shown as missing.", "Configure the approved folders and schedules, then verify a run through its history. Backup and recovery actions follow the operations runbook.", "Use Support package for privacy-safe diagnostics. Share it only with the intended support recipient."),
            ["operations", "support", "system health", "scheduler", "watch folder", "printer", "database growth"], "Admin / Settings"),
        Topic("administration", "Administration", "Manage authorised users, stores, rules and master data.", "IconSettings", 160,
            Guide("An Owner opens Settings to manage Users, Stores, Brand rows, Tender mapping and monthly/staff targets.", "Store lists come from the active store master. Store Manager can maintain Brand rows but cannot change Owner-only targets or users.", "Enter a reason for audited changes. Tender mapping uses the actual source tender master.", "Save and refresh the affected screen. Review the audit record and verify access using the intended Windows account."),
            ["user", "role", "store", "master", "profile", "kpi", "rule", "audit"], "Admin / Settings"),
        Topic("report-archive", "Report Archive", "Open, export and share saved report packs with delivery history.", "IconArchive", 170,
            Guide("Open Reports → Archive and select a generated pack for the required scope.", "Each row offers Open, Excel, PDF, ZIP and Share. Opening verifies the saved pack integrity.", "For sharing, select a saved contact. Email uses the configured SMTP server; WhatsApp opens a handoff with the PDF path copied for attachment.", "Read delivery history for the pack. SMTP accepted does not prove recipient delivery, and WhatsApp handoff does not prove a message was sent."),
            ["archive", "generation", "hash", "compare", "re-export", "zip", "share"], "Report Archive"),
        Topic("backup-recovery", "Backup & Recovery", "Check backup health and recovery readiness.", "IconArchive", 180,
            Guide("Open Settings → Database → Database health and check the last verified backup and recovery drill.", "Create and verify a backup using the approved operations setup. SQL Express backup encryption follows the configured edition policy.", "A recovery drill restores to a separate database, checks integrity and records evidence. Never overwrite the live database for a drill.", "Retain required recovery keys and independent pre-upgrade backups according to the operations runbook. Missing or stale evidence must be resolved."),
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
            || x.Overview.Contains(query, StringComparison.OrdinalIgnoreCase)
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

    public static string ScreenshotFor(string topicId) => topicId switch
    {
        "getting-started" or "dashboard" or "business-day" or "daily-sales-report" or "tender-cash-service" => "Today",
        "import-etp" or "import-history" => "Import",
        "stock-reports" or "digital-registers" => "Stock",
        "sales-reports" or "staff-cro" or "exception-centre" or "investigation" or "management" or "report-archive" => "Reports",
        _ => "Settings"
    };

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
        ["Import History"] = "import-history",
        ["Sales Reports"] = "sales-reports",
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
