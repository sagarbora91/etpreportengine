namespace Etp.Reporting.Desktop.Modules.Reports;

public static class ReportTaskScope
{
    public static int StoreIndexForReport(string? code, int currentIndex) => code == "cash" && currentIndex is not (0 or 1) ? 0 : currentIndex;
    public static bool RequiresSingleStore(string? code) => code is "stock-physical" or "cash" or "exceptions";
    public static bool IsSnapshot(string? code) => code is "stock-physical" or "exceptions" || code is "stock-closing" or "stock-brand" or "stock-slow";
}
