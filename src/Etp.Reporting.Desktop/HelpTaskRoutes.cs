namespace Etp.Reporting.Desktop;

public static class HelpTaskRoutes
{
    public static TaskDestination? Find(string topic) => TaskNavigation.Find(topic switch
    {
        "dashboard" or "getting-started" => "report-dsr", "business-day" => "readiness", "import-etp" => "import-files",
        "import-history" => "import-history",
        "daily-sales-report" => "report-dsr", "sales-reports" => "report-sales-combined", "stock-reports" => "report-stock-closing",
        "tender-cash-service" => "report-tender", "service-centre" => "service-today", "staff-cro" => "report-staff", "management" => "report-management-trend",
        "exception-centre" => "open-items", "investigation" => "investigation", "digital-registers" => "register-inward",
        "accounting" => "prepare-batch", "operations-support" or "troubleshooting" => "support-package", "administration" => "users",
        "report-archive" => "generations", "backup-recovery" => "backups", "health-check" => "health-check",
        // 1.12 wave 2: an id that the shell does not register yet resolves to null (no Open button) until U1 adds its row. CHECK AT INTEGRATION.
        "morning" => "morning", "no-sale-day" => "readiness", "import-fixes" => "conflicts", "month-start" => "masters",
        "tally-screens" => "prepare-batch", "collections" => "collections-statement", "software-update" => "update-settings", _ => ""
    });
}
