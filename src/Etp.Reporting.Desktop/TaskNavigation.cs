using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop;

/// <summary>Shared destinations for tiles, search, history and focused workspace sections.</summary>
public sealed record TaskDestination(string Id, string Title, string Module, string Category,
    string Destination, string Section, int MinimumRole, string? ReportCode = null,
    string[]? Aliases = null, bool Available = true, string? UnavailableReason = null)
{
    public string Path => $"{Module} → {Category} → {Title}";
    public string Purpose => ReportCode is null ? $"Open {Title.ToLowerInvariant()} in {Module}." : ProductReportCatalogue.All.Single(x => x.Code == ReportCode).Description;
    public WorkspaceRoute Route => new(Destination, ReportCode, Id);
    public bool IsAllowed(ShellAccess access) => Available
        && (Destination != "Import ETP" || access.CanImport)
        && (Destination is not ("Settings" or "Masters" or "Admin / Settings") || access.CanAdminister)
        && (MinimumRole >= 3 ? access.CanAdminister : MinimumRole >= 2 ? access.CanImport : access.CanView);
}

public static class TaskNavigation
{
    public static IReadOnlyList<TaskDestination> All { get; } = Build();
    public static TaskDestination? Find(string? id) => All.FirstOrDefault(x => x.Id == id);
    public static TaskDestination? ForItem(NavigationItemDefinition item) =>
        item.FeatureCode is not null ? Find("report-" + item.FeatureCode) : Find(CanonicalId(item.Id));

    public static string CanonicalId(string id) => id switch
    {
        "reports-dsr-favourite" => "report-dsr", "source-documents" => "documents",
        "import-conflicts" => "conflicts", "ocr-exceptions" => "ocr-review",
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
        var modules = new[] { "dashboard", "reports", "imports", "accounting", "archive", "exceptions", "settings" };
        foreach (var module in modules)
        foreach (var group in UiNavigationRegistry.ForModule(module))
        foreach (var item in group.Items)
        {
            var id = item.FeatureCode is null ? CanonicalId(item.Id) : "report-" + item.FeatureCode;
            if (result.Any(x => x.Id == id)) continue;
            var moduleName = char.ToUpperInvariant(module[0]) + module[1..];
            var title = item.Label;
            var category = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(group.Label.ToLowerInvariant());
            var destination = item.Destination;
            var section = id;
            var role = (int)item.MinimumRole;
            if (item.FeatureCode is { } code)
            {
                var report = ProductReportCatalogue.All.Single(x => x.Code == code);
                moduleName = "Reports"; category = report.Category; title = code == "dsr" ? "Daily Sales Report" : report.Name;
                destination = "Sales Reports"; section = "report";
            }
            else
            {
                if (module == "imports") role = Math.Max(role, 2);
                if (id.StartsWith("register-")) { moduleName = "Registers"; category = "Registers"; role = 2; section = "register"; }
                if (new[] { "source-inbox", "quarantine", "duplicates", "already-present", "conflicts", "documents", "native-pdf", "ocr-review", "extraction-history", "unknown-layouts" }.Contains(id))
                    section = "inbox";
                if (new[] { "quarantine", "duplicates", "already-present", "conflicts", "import-failures", "import-history", "unknown-layouts" }.Contains(id)) category = "Quality & History";
                if (new[] { "watch-folder", "scheduler", "ocr", "sharing" }.Contains(id)) { moduleName = "Settings"; category = "Integrations"; }
                if (module == "settings") category = id switch
                {
                    "users" => "Users & Access", "stores" or "masters" or "profiles" or "kpi" or "tender-rules" => "Stores & Masters",
                    "health" or "backups" => "Database & Recovery", "ocr" or "sharing" or "scheduler" => "Integrations", _ => "General"
                };
                if (id == "tally-export") category = "Export";
                if (id == "compare") category = "Generations";
                if (id is "walk-ins" or "readiness" or "finalisation") category = "Daily Close";
                if (id == "walk-ins") { title = "Walk-ins"; section = "manual"; destination = "Manual Entry"; }
                if (id == "backups") { title = "Backups"; section = "backups"; }
                if (id == "approval-centre") { moduleName = "Approvals"; category = "Review"; }
            }
            if (id is "duplicates" or "already-present" or "conflicts" or "import-failures") { section = "import-results"; destination = "Import ETP"; role = 2; }
            if (id is "reports-home" or "import-overview" or "accounting-overview" or "archive-overview") section = "overview";
            if (id == "dashboard") title = "Today Overview";
            if (id is "ledger-mapping" or "mapping-review") role = 3;
            result.Add(new(id, title, moduleName, category, destination, section, role, item.FeatureCode,
                new[] { item.Label, id.Replace('-', ' ') }.Concat(ReportTaskAliases.For(item.FeatureCode)).Distinct().ToArray(), item.IsAvailable, item.UnavailableReason));
        }
        void Add(string id, string title, string module, string category, string destination, string section, int role, params string[] aliases)
            => result.Add(new(id, title, module, category, destination, section, role, Aliases: aliases));
        Add("accounting-approval", "Approve Accounting Batch", "Approvals", "Accounting", "Accounting", "accounting-approval", 3);
        Add("sharing-contacts", "Sharing Contacts", "Settings", "Integrations", "Report Archive", "sharing-contacts", 3);
        Add("report-filters", "Report Filters", "Reports", "Filters", "Sales Reports", "report-filters", 1, "brand segment", "transaction type", "item filter");
        Add("favourite-reports", "Favourite Reports", "Reports", "Favourites", "Sales Reports", "favourite-reports", 1, "favorites", "favourites", "starred reports");
        Add("support-package", "Support Package", "Settings", "Database & Recovery", "Operations Center", "support-package", 3, "diagnostics package", "support");
        Add("recovery", "Restore & Recovery Drill", "Settings", "Database & Recovery", "Operations Center", "recovery", 3, "restore", "recovery drill");
        Add("connection", "Database Connection", "Settings", "Database & Recovery", "Settings", "connection", 3, "sql", "connection");
        Add("stock-count", "Stock Counts", "Dashboard", "Daily Close", "Manual Entry", "stock-count", 2, "physical count");
        Add("staff-target", "Staff Targets", "Dashboard", "Daily Close", "Manual Entry", "staff-target", 2, "targets");
        Add("adjustment", "Adjustment Request", "Exceptions", "Review", "Operations Center", "adjustment", 2);
        Add("investigation", "Investigation", "Exceptions", "Review", "Operations Center", "investigation", 1, "invoice lookup");
        foreach (var topic in HelpCentreRegistry.Topics.Where(x => x.Availability != HelpTopicAvailability.ComingSoon))
            result.Add(new("help:" + topic.Id, "Help: " + topic.Title, "Help", HelpCentreRegistry.Category(topic.Id), "Home", "help", topic.Destination is "Settings" or "Admin / Settings" ? 3 : 1, Aliases: [topic.Title + " help"]));
        Add("profile", "Profile", "Help", "Your account", "Home", "profile", 1, "role", "identity");
        return result;
    }
}
