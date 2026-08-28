extern alias EtpApplication;

using System.Text.Json;
using System.IO;
using Etp.Reporting.Reporting;
using AccessRole = EtpApplication::Etp.Reporting.Application.Access.AccessRole;

namespace Etp.Reporting.Desktop;

public enum UiDensity { Comfortable, Compact }

public sealed record ModuleDefinition(
    string Id, string DisplayName, string IconKey, string Description, string Destination, int Order,
    AccessRole MinimumRole, bool DefaultVisibility = true, bool PinAllowed = false,
    string StatusText = "Open workspace", string StatusDetail = "Ready")
{
    public bool IsVisibleTo(AccessRole role) => role >= MinimumRole;
}

public sealed record NavigationItemDefinition(
    string Id, string Label, string Destination, string? FeatureCode = null,
    AccessRole MinimumRole = AccessRole.Viewer, bool IsAvailable = true,
    string? UnavailableReason = null, bool IsFavouriteEligible = false)
{
    public bool IsVisibleTo(AccessRole role) => role >= MinimumRole;
}

public sealed record NavigationGroupDefinition(
    string Id, string Label, int Order, IReadOnlyList<NavigationItemDefinition> Items,
    bool IsCollapsible = true, AccessRole MinimumRole = AccessRole.Viewer)
{
    public bool IsVisibleTo(AccessRole role) => role >= MinimumRole;
}

public static class UiNavigationRegistry
{
    public static IReadOnlyList<ModuleDefinition> Modules { get; } =
    [
        new("dashboard", "Dashboard", "IconDashboard", "Business-day overview, trends and control status.", "Dashboard", 10, AccessRole.Viewer, StatusText: "Business day cockpit"),
        new("reports", "Reports", "IconReports", "Generate, review, package and share management reports.", "Sales Reports", 20, AccessRole.Viewer, StatusText: "29 reports live"),
        new("accounting", "Accounting", "IconAccounting", "Prepare balanced batches and controlled Tally exports.", "Accounting", 30, AccessRole.Viewer, StatusText: "Controlled Tally preparation"),
        new("imports", "Imports", "IconImport", "ETP files, Source Inbox, bulk history, OCR and registers.", "Import ETP", 40, AccessRole.StoreManager, StatusText: "Duplicate-safe intake"),
        new("archive", "Archive", "IconArchive", "Finalised generations, restatements and historical packs.", "Report Archive", 50, AccessRole.Viewer, StatusText: "Immutable history"),
        new("exceptions", "Exceptions", "IconAlert", "A plain-language inbox for items requiring attention.", "Operations Center", 60, AccessRole.Viewer, StatusText: "Controls and approvals"),
        new("registers", "Registers", "IconRegister", "Document-linked operational registers.", "Registers", 70, AccessRole.StoreManager, false, true, "Owner pinned module"),
        new("approvals", "Approvals", "IconAlert", "Restatement, mapping and adjustment approvals.", "Operations Center", 80, AccessRole.Owner, false, true, "Owner only"),
        new("health", "System Health", "IconSettings", "SQL, backup, OCR, scheduler and integration status.", "Admin / Settings", 90, AccessRole.Owner, false, true, "Owner only")
    ];

    public static IReadOnlyList<NavigationGroupDefinition> ForModule(string moduleId) => moduleId switch
    {
        "dashboard" => Dashboard,
        "reports" => Reports,
        "imports" => Imports,
        "accounting" => Accounting,
        "archive" => Archive,
        "exceptions" => Exceptions,
        "settings" or "health" => Settings,
        "registers" => Imports,
        "approvals" => Exceptions,
        _ => []
    };

    public static IReadOnlyList<NavigationItemDefinition> AllItems =>
        Dashboard.Concat(Reports).Concat(Imports).Concat(Accounting).Concat(Archive).Concat(Exceptions).Concat(Settings)
            .SelectMany(x => x.Items).DistinctBy(x => x.Id).ToArray();

    private static readonly IReadOnlyList<NavigationGroupDefinition> Dashboard =
    [
        Group("dashboard-overview", "OVERVIEW", 10, Item("dashboard", "Overview", "Dashboard")),
        Group("dashboard-day", "BUSINESS DAY", 20, Item("daily-health", "Daily Health", "Dashboard"), Item("manual-entry", "Manual Entry", "Manual Entry", minimumRole: AccessRole.StoreManager), Item("readiness", "Readiness", "Daily Workflow"), Item("finalisation", "Finalisation Status", "Daily Workflow")),
        Group("dashboard-performance", "PERFORMANCE", 30, Item("trends", "Trends", "Operations Center"), Item("store-comparison", "Store Comparison", "Sales Reports", "sales-combined"), Item("target-progress", "Target Progress", "Sales Reports", "staff")),
        Group("dashboard-controls", "CONTROLS", 40, Item("control-summary", "Control Summary", "Operations Center"), Item("recent-activity", "Recent Activity", "Dashboard"))
    ];

    private static readonly IReadOnlyList<NavigationGroupDefinition> Reports = BuildReports();

    private static IReadOnlyList<NavigationGroupDefinition> BuildReports()
    {
        var groups = ProductReportCatalogue.All.GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .Select((category, index) => new NavigationGroupDefinition($"reports-{Slug(category.Key)}", category.Key.ToUpperInvariant(), 20 + index,
                category.Select(report => new NavigationItemDefinition($"report-{report.Code}", report.Name, "Sales Reports", report.Code,
                    AccessRole.Viewer, true, null, true)).ToArray())).ToList();
        groups.Insert(0, Group("reports-overview", "OVERVIEW", 10,
            Item("reports-home", "Reports Overview", "Sales Reports"),
            Item("reports-dsr-favourite", "Daily Sales / DSR", "Sales Reports", "dsr", favourite: true),
            Item("reports-closing-favourite", "Closing Stock", "Sales Reports", "stock-closing", favourite: true),
            Item("reports-staff-favourite", "Staff/CRO Performance", "Sales Reports", "staff", favourite: true)));
        groups.Add(Group("report-packs", "REPORT PACKS", 90,
            Item("store-daily-pack", "Store Daily Pack", "Daily Workflow"), Item("combined-pack", "Combined Pack", "Daily Workflow"), Item("historical-packs", "Historical Packs", "Report Archive")));
        groups.Add(Group("reports-future", "DATA-DEPENDENT FUTURE", 100,
            Future("category-sales", "Category-wise Sales", "Requires an approved product-category master."),
            Future("sell-through", "Sell-through", "Requires an approved purchase/receipt source."),
            Future("stock-turn", "Stock Turn", "Requires an approved purchase/receipt source."),
            Future("days-cover", "Days of Cover", "Requires an approved replenishment policy.")));
        return groups.OrderBy(x => x.Order).ToArray();
    }

    private static readonly IReadOnlyList<NavigationGroupDefinition> Imports =
    [
        Group("imports-overview", "OVERVIEW", 10, Item("import-overview", "Import Overview", "Import ETP")),
        Group("imports-intake", "INTAKE", 20, Item("import-files", "Import Files", "Import ETP"), Item("source-inbox", "Source Inbox", "Import ETP"), Item("bulk-import", "Bulk Historical Import", "Import ETP"), Item("watch-folder", "Watch Folder", "Operations Center")),
        Group("imports-quality", "QUALITY", 30, Item("quarantine", "Quarantine", "Import ETP"), Item("duplicates", "Exact Duplicates", "Import ETP"), Item("already-present", "Already-Present Facts", "Import ETP"), Item("conflicts", "Conflict Review", "Import ETP"), Item("import-failures", "Import Failures", "Operations Center"), Item("import-history", "Import History", "Dashboard")),
        Group("imports-documents", "DOCUMENTS & OCR", 40, Item("documents", "Document Repository", "Import ETP"), Item("native-pdf", "Native PDF Extraction", "Import ETP"), Item("ocr-review", "OCR Review Queue", "Import ETP"), Item("extraction-history", "Extraction History", "Import ETP")),
        Group("imports-registers", "DIGITAL REGISTERS", 50,
            Item("register-inward", "Inward Register", "Registers"), Item("register-outward", "Outward Register", "Registers"), Item("register-credit", "Credit Note Register", "Registers"), Item("register-service", "Service Receipt Register", "Registers"),
            Item("register-courier", "Courier Register", "Registers", available: false, unavailable: "Register schema is not yet configured."), Item("register-transfer", "Stock Transfer Register", "Registers"), Item("register-expense", "Expense Register", "Registers"), Item("register-vendor", "Vendor Invoice Register", "Registers"))
    ];

    private static readonly IReadOnlyList<NavigationGroupDefinition> Accounting =
    [
        Group("accounting-workflow", "ACCOUNTING WORKFLOW", 10, Item("accounting-overview", "Overview", "Accounting"), Item("prepare-batch", "Prepare Batch", "Accounting"), Item("ledger-mapping", "Ledger Mapping", "Accounting"), Item("mapping-review", "Mapping Review", "Accounting"), Item("validation", "Validation", "Accounting"), Item("tally-export", "Tally Export", "Accounting"), Item("export-history", "Export History", "Accounting"), Item("accounting-reconciliation", "Accounting Reconciliation", "Accounting")),
        Group("accounting-future", "FUTURE EXTENSION", 20, Future("direct-posting", "Approved Direct Posting", "No direct-posting authority exists."), Future("gst-assist", "GST Return Assist", "Requires an approved tax policy and source contract."))
    ];

    private static readonly IReadOnlyList<NavigationGroupDefinition> Archive =
    [
        Group("archive-main", "ARCHIVE", 10, Item("archive-overview", "Overview", "Report Archive"), Item("generations", "Report Generations", "Report Archive"), Item("final-packs", "Finalised Packs", "Report Archive"), Item("restatements", "Restatements", "Report Archive"), Item("compare", "Compare Generations", "Report Archive"), Item("re-export", "Re-export", "Report Archive"), Item("shared", "Shared Reports", "Report Archive"), Item("source-documents", "Source Documents", "Import ETP"))
    ];

    private static readonly IReadOnlyList<NavigationGroupDefinition> Exceptions =
    [
        Group("exceptions-main", "EXCEPTIONS & APPROVALS", 10, Item("open-items", "Open Items", "Operations Center"), Item("data-quality", "Data Quality", "Operations Center"), Item("missing-sources", "Missing Sources", "Sales Reports", "exception-source"), Item("unknown-layouts", "Unknown Layouts", "Import ETP"), Item("unmapped", "Unmapped Data", "Sales Reports", "exception-unmapped"), Item("import-conflicts", "Import Conflicts", "Import ETP"), Item("tender-exceptions", "Tender", "Sales Reports", "exception-tender"), Item("stock-exceptions", "Stock", "Sales Reports", "exception-stock"), Item("staff-exceptions", "Staff", "Sales Reports", "exception-staff"), Item("ocr-exceptions", "OCR Review", "Import ETP"), Item("accounting-exceptions", "Accounting", "Accounting"), Item("approval-centre", "Approval Centre", "Operations Center", minimumRole: AccessRole.Owner))
    ];

    private static readonly IReadOnlyList<NavigationGroupDefinition> Settings =
    [
        Group("settings-general", "SETTINGS & ADMIN", 10, Item("settings", "General", "Admin / Settings", minimumRole: AccessRole.Owner), Item("users", "Users & Roles", "Admin / Settings", minimumRole: AccessRole.Owner), Item("stores", "Stores", "Admin / Settings", minimumRole: AccessRole.Owner), Item("masters", "Master Data", "Admin / Settings", minimumRole: AccessRole.Owner), Item("profiles", "Import Profiles", "Admin / Settings", minimumRole: AccessRole.Owner), Item("kpi", "KPI Catalogue", "Admin / Settings", minimumRole: AccessRole.Owner), Item("tender-rules", "Tender Rules", "Admin / Settings", minimumRole: AccessRole.Owner), Item("accounting-map", "Accounting Mapping", "Accounting", minimumRole: AccessRole.Owner), Item("watch", "Watch Folders", "Operations Center", minimumRole: AccessRole.Owner), Item("ocr", "OCR", "Admin / Settings", minimumRole: AccessRole.Owner), Item("sharing", "Email & Sharing", "Admin / Settings", minimumRole: AccessRole.Owner), Item("backup", "Backup & Recovery", "Operations Center", minimumRole: AccessRole.Owner), Item("scheduler", "Scheduler", "Operations Center", minimumRole: AccessRole.Owner), Item("health", "System Health", "Admin / Settings", minimumRole: AccessRole.Owner), Item("audit", "Audit Trail", "Dashboard", minimumRole: AccessRole.Owner))
    ];

    private static NavigationGroupDefinition Group(string id, string label, int order, params NavigationItemDefinition[] items) => new(id, label, order, items);
    private static NavigationItemDefinition Item(string id, string label, string destination, string? feature = null, AccessRole minimumRole = AccessRole.Viewer, bool available = true, string? unavailable = null, bool favourite = false) => new(id, label, destination, feature, minimumRole, available, unavailable, favourite);
    private static NavigationItemDefinition Future(string id, string label, string reason) => new(id, label, "", null, AccessRole.Viewer, false, reason);
    private static string Slug(string value) => new string(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
}

public sealed record UiPreferences(UiDensity Density, IReadOnlyList<string> PinnedModuleIds, IReadOnlyList<string> FavouriteReportCodes)
{
    public static UiPreferences Default { get; } = new(UiDensity.Comfortable, [], ["dsr", "stock-closing", "staff"]);
}

public static class UiPreferenceStore
{
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting");
    public static string FilePath => Path.Combine(DirectoryPath, "ui-preferences.json");

    public static UiPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return UiPreferences.Default;
            return JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(FilePath)) ?? UiPreferences.Default;
        }
        catch (Exception) when (File.Exists(FilePath)) { return UiPreferences.Default; }
    }

    public static void Save(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
