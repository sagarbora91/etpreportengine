namespace Etp.Reporting.Desktop.Modules.Reports;

public static class ReportTaskScope
{
    public static bool RequiresSingleStore(string? code) => code is "stock-physical" or "stock-group" or "cash" or "exceptions" || code?.StartsWith("exception-",StringComparison.Ordinal)==true;
    public static bool IsSnapshot(string? code) => RequiresSingleStore(code) || code is "stock-closing" or "stock-brand" or "stock-slow";
}
