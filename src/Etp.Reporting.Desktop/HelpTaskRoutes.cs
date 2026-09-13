namespace Etp.Reporting.Desktop;

public static class HelpTaskRoutes
{
    public static TaskDestination? Find(string topic) => topic switch
    {
        "sales-reports" => Category("Reports","Sales Reports","Sales",1),
        "stock-reports" => Category("Reports","Stock Reports","Stock",1),
        "digital-registers" => Category("Registers","Registers","Registers",2),
        _ => FindTask(topic)
    };
    private static TaskDestination Category(string module,string destination,string category,int role) => new($"category:{module}:{category}",category,module,category,destination,"category",role);
    private static TaskDestination? FindTask(string topic) => TaskNavigation.Find(topic switch
    {
        "dashboard" or "getting-started" => "dashboard", "business-day" => "readiness", "import-etp" => "import-files",
        "daily-sales-report" => "report-dsr", "sales-reports" => "reports-home", "stock-reports" => "report-stock-closing",
        "tender-cash-service" => "report-tender", "staff-cro" => "report-staff", "management" => "report-management-trend",
        "exception-centre" => "open-items", "investigation" => "investigation", "digital-registers" => "register-inward",
        "accounting" => "prepare-batch", "operations-support" or "troubleshooting" => "support-package", "administration" => "settings",
        "report-archive" => "generations", "backup-recovery" => "backups", _ => ""
    });
}
