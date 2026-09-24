using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop;

/// <summary>Shared destinations for tiles, search, history and focused workspace sections.</summary>
public sealed record TaskDestination(string Id, string Title, string Module, string Category,
    string Destination, string Section, int MinimumRole, string? ReportCode = null,
    string[]? Aliases = null, bool Available = true, string? UnavailableReason = null)
{
    public string Rail => Module;
    public string Tab => Category;
    public string Path => $"{Module} → {Category} → {Title}";
    public string Purpose => ReportCode is null ? $"Open {Title.ToLowerInvariant()} in {Module}." : ProductReportCatalogue.All.Single(x => x.Code == ReportCode).Description;
    public WorkspaceRoute Route => new(Destination, ReportCode, Id);
    // The shell binds this record straight into a ComboBox. A record's generated ToString
    // prints every field, so a screen reader announced the whole object - id, destination,
    // role, route - instead of the task name a sighted user sees through DisplayMemberPath.
    public override string ToString() => Title;

    public bool IsAllowed(ShellAccess access) => Available
        && (Destination != "Import ETP" || access.CanImport || Id is "conflicts" or "source-inbox" && access.CanView)
        && (Destination is not ("Settings" or "Admin / Settings") || access.CanAdminister || Id == "masters" && access.CanImport)
        && (MinimumRole >= 3 ? access.CanAdminister : MinimumRole >= 2 ? access.CanImport : access.CanView);
}

public static class TaskNavigation
{
    public static TaskDestination? Find(string? id) => All.FirstOrDefault(x => x.Id == id);
    public static IReadOnlyList<string> Sections { get; } = ["Today", "Import", "Reports", "Stock", "Settings"];
    public static IReadOnlyList<TaskDestination> All { get; } = Build();
    public static IReadOnlyList<TaskDestination> InSection(string section, ShellAccess access) => All.Where(t => t.Rail == section && t.IsAllowed(access)).ToArray();

    public static string CanonicalId(string id) => id switch
    {
        "reports-dsr-favourite" => "report-dsr", "source-documents" => "documents",
        "import-conflicts" => "conflicts",
        "watch" => "watch-folder", "accounting-map" => "ledger-mapping",
        "manual-entry" => "walk-ins", "backup" => "backups", _ => id
    };

    public static IReadOnlyList<TaskDestination> Search(string query, ShellAccess access)
    {
        var value = query.Trim();
        return All.Where(x => x.IsAllowed(access)).Select(x => (Task: x, Rank: Rank(x, value)))
            .Where(x => x.Rank >= 0).OrderBy(x => x.Rank).ThenBy(x => x.Task.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Task).ToArray();
    }

    private static int Rank(TaskDestination task, string query)
    {
        if (query.Length == 0) return task.Id == "report-dsr" ? 0 : 3;
        var terms = new[] { task.Title, task.ReportCode ?? "" }.Concat(task.Aliases ?? []).ToArray();
        if (terms.Any(x => x.Equals(query, StringComparison.OrdinalIgnoreCase))) return 0;
        if (terms.Any(x => x.StartsWith(query, StringComparison.OrdinalIgnoreCase))) return 1;
        var text = task.Path + " " + string.Join(" ", terms);
        return query.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)) ? 2 : -1;
    }

    private static IReadOnlyList<TaskDestination> Build()
    {
        var result = new List<TaskDestination>();
        void Add(string id, string title, string rail, string tab, string destination, string section, int role = 1)
            => result.Add(new(id, title, rail, tab, destination, section, role, Aliases: id == "recovery" ? ["restore", "recovery drill"] : [id.Replace('-', ' ')]));
        Add("walk-ins", "Walk-ins", "Today", "Walk-ins", "Manual Entry", "manual", 2);
        Add("readiness", "Close day", "Today", "Close day", "Daily Workflow", "readiness", 1);
        Add("finalisation", "Finalise day", "Today", "Close day", "Daily Workflow", "finalisation", 2);
        Add("store-daily-pack", "Store pack", "Today", "Close day", "Daily Workflow", "pack", 1);
        Add("combined-pack", "All stores pack", "Today", "Close day", "Daily Workflow", "pack", 1);
        Add("register-expense", "Expense entry", "Today", "Cash", "Registers", "register", 2);
        Add("cash-input", "Cash and service entries", "Today", "Cash", "Manual Entry", "manual", 2);
        Add("import-files", "Import folder", "Import", "Import", "Import ETP", "import", 2);
        Add("conflicts", "Problems", "Import", "Problems", "Import ETP", "import-results", 1);
        Add("import-history", "Imports", "Import", "History", "Import History", "import-history", 1);
        Add("source-inbox", "Received files", "Import", "History", "Import ETP", "inbox", 1);
        Add("stock-count", "Physical count", "Stock", "Physical count", "Manual Entry", "stock-count", 2);
        Add("settings", "Display", "Settings", "Display", "Home", "settings", 1);
        Add("connection", "Connection", "Settings", "Database", "Settings", "connection", 3);
        Add("health", "Database health", "Settings", "Database", "Admin / Settings", "health", 3);
        Add("backups", "Backups", "Settings", "Database", "Operations Center", "backups", 3);
        Add("recovery", "Recovery drill", "Settings", "Database", "Operations Center", "recovery", 3);
        Add("support-package", "Support package", "Settings", "Database", "Operations Center", "support-package", 3);
        Add("audit", "Audit trail", "Settings", "Database", "Dashboard", "audit", 3);
        Add("users", "Users", "Settings", "Users", "Admin / Settings", "users", 3);
        Add("profiles", "Import profiles", "Settings", "Users", "Admin / Settings", "profiles", 3);
        Add("masters", "Brands and targets", "Settings", "Stores & masters", "Admin / Settings", "masters", 2);
        Add("stores", "Stores", "Settings", "Stores & masters", "Admin / Settings", "stores", 3);
        Add("kpi", "Calculations", "Settings", "Stores & masters", "Admin / Settings", "kpi", 3);
        Add("tender-rules", "Tender mapping", "Settings", "Stores & masters", "Admin / Settings", "tender-rules", 3);
        Add("staff-target", "Staff targets", "Settings", "Stores & masters", "Manual Entry", "staff-target", 3);
        Add("watch-folder", "Automatic import", "Settings", "Automatic import", "Operations Center", "watch-folder", 3);
        Add("sharing", "Email and sharing", "Settings", "Integrations", "Settings", "sharing", 3);
        Add("sharing-contacts", "Sharing contacts", "Settings", "Integrations", "Report Archive", "sharing-contacts", 3);
        Add("prepare-batch", "Prepare → Review → Export", "Settings", "Accounting", "Accounting", "prepare-batch", 3);
        Add("open-items", "Open items", "Settings", "Control centre", "Operations Center", "open-items", 3);
        Add("data-quality", "Data quality", "Settings", "Control centre", "Operations Center", "data-quality", 3);
        Add("approval-centre", "Approvals", "Settings", "Control centre", "Operations Center", "approval-centre", 3);
        Add("adjustment", "Adjustment request", "Settings", "Control centre", "Operations Center", "adjustment", 3);
        Add("investigation", "Investigation", "Reports", "Investigation", "Operations Center", "investigation", 2);
        Add("reports-list", "All reports", "Reports", "All reports", "Sales Reports", "report-list");
        Add("generations", "Report archive", "Reports", "Archive", "Report Archive", "generations", 1);
        Add("favourite-reports", "Favourites", "Reports", "Favourites", "Sales Reports", "favourite-reports", 1);
        Add("trends", "Trends", "Reports", "Management", "Operations Center", "trends", 1);
        Add("profile", "Current profile", "Settings", "Help", "Home", "profile", 1);
        Add("register-inward", "Inward", "Stock", "Registers", "Registers", "register", 2);
        Add("register-outward", "Outward", "Stock", "Registers", "Registers", "register", 2);
        Add("register-credit", "Credit notes", "Today", "Close day", "Registers", "register", 2);
        Add("register-service", "Service receipts", "Today", "Close day", "Registers", "register", 2);
        Add("register-transfer", "Stock transfers", "Stock", "Registers", "Registers", "register", 2);
        Add("register-vendor", "Vendor invoices", "Today", "Close day", "Registers", "register", 2);
        Add("register-courier", "Courier", "Today", "Close day", "Registers", "register", 2);
        foreach (var report in ProductReportCatalogue.All)
        {
            var (rail, tab) = report.Code switch
            {
                "dsr" => ("Today", "Sales"), "cash" => ("Today", "Cash"),
                "stock-closing" => ("Stock", "Closing stock"), "stock-brand" => ("Stock", "Brand stock"),
                "stock-physical" => ("Stock", "Physical count"), "stock-variance" => ("Stock", "Variance"),
                "stock-movement" => ("Stock", "Movement"), "stock-slow" => ("Stock", "Slow stock"),
                _ => ("Reports", report.Category switch { "Tender / Cash" => "Tender & service", "Service" => "Tender & service", "Investigation" => "Management", _ => report.Category })
            };
            result.Add(new("report-" + report.Code, report.Code == "dsr" ? "Sales" : report.Name, rail, tab, "Sales Reports", "report", 1, report.Code, ReportTaskAliases.For(report.Code).ToArray()));
        }
        foreach (var topic in HelpCentreRegistry.Topics)
            Add("help:" + topic.Id, topic.Title, "Settings", "Help", "Home", "help");
        var tabOrder = new[] { "All reports", "Sales", "Cash", "Walk-ins", "Close day", "Import", "Problems", "History", "Documents", "Staff", "Tender & service", "Exceptions", "Investigation", "Management", "Archive", "Favourites", "Closing stock", "Brand stock", "Physical count", "Variance", "Movement", "Slow stock", "Display", "Database", "Users", "Stores & masters", "Integrations", "Automatic import", "Accounting", "Control centre", "Registers", "Help" };
        return result.OrderBy(t => Array.IndexOf(Sections.ToArray(), t.Rail)).ThenBy(t => Array.IndexOf(tabOrder, t.Tab)).ThenBy(t => t.ReportCode is null ? 1 : 0).ToArray();
    }
}
