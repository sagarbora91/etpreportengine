namespace Etp.Reporting.Desktop.Modules.Reports;

public static class ReportTaskScope
{
    public static int StoreIndexForReport(string? code, int currentIndex, int storeCount = 0) =>
        code == "cash" && storeCount > 0 && (currentIndex < 0 || currentIndex >= storeCount) ? 0 : currentIndex;
    public static bool RequiresSingleStore(string? code) => code is "stock-physical" or "cash" or "exceptions" or "sales-store";
    public static bool IsSnapshot(string? code) => code is "stock-physical" or "exceptions" || code is "stock-closing" or "stock-brand" or "stock-slow";
}
