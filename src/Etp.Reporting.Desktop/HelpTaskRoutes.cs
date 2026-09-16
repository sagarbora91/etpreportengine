namespace Etp.Reporting.Desktop;

public static class HelpTaskRoutes
{
    public static TaskDestination? Find(string topic) => TaskNavigation.Find(topic switch
    {
        "dashboard" or "getting-started" => "report-dsr", "business-day" => "readiness", "import-etp" => "import-files",
        "daily-sales-report" => "report-dsr", "sales-reports" => "report-sales-combined", "stock-reports" => "report-stock-closing",
        "tender-cash-service" => "report-tender", "staff-cro" => "report-staff", "management" => "report-management-trend",
        "exception-centre" => "open-items", "investigation" => "investigation", "digital-registers" => "register-inward",
        "accounting" => "prepare-batch", "operations-support" or "troubleshooting" => "support-package", "administration" => "users",
        "report-archive" => "generations", "backup-recovery" => "backups", _ => ""
    });
}
