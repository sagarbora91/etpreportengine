namespace Etp.Reporting.Desktop.Modules.Reports;

public static class ReportTaskScope
{
    public static bool RequiresSingleStore(string? code) => code is "stock-physical" or "cash" or "exceptions";
    public static bool IsSnapshot(string? code) => code is "stock-physical" or "exceptions" || code is "stock-closing" or "stock-brand" or "stock-slow";
}
