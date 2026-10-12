namespace Etp.Reporting.Reporting;

/// <summary>
/// The one status vocabulary people read (UI/UX audit FW-21, decision 45 #5): OK, Check, Problem, Cannot run, Not checked yet,
/// Known, Skipped. The enum names (Passed, Warning, Failed, Blocked, NotRun) stay in code, storage and audit; screens, PDFs and
/// Excel show the label. Imports and files use their own words (Accepted, Refused, Failed), which this class leaves alone.
/// </summary>
public static class StatusWords
{
    public const string Ok = "OK";
    public const string Check = "Check";
    public const string Problem = "Problem";
    public const string CannotRun = "Cannot run";
    public const string NotChecked = "Not checked yet";

    /// <summary>The label for a check status given by enum or text ("Passed", "PASS", "NotRun"); any other text is returned unchanged.</summary>
    public static string Label(object? status)
    {
        var name = status?.ToString();
        if (string.IsNullOrWhiteSpace(name)) return IndianFormat.Missing;
        return name.Trim().ToUpperInvariant() switch
        {
            "PASSED" or "PASS" or "OK" => Ok,
            "WARNING" or "WARN" or "CHECK" => Check,
            "FAILED" or "FAIL" or "PROBLEM" => Problem,
            "BLOCKED" or "CANNOT RUN" => CannotRun,
            "NOTRUN" or "NOT RUN" or "NOT CHECKED YET" => NotChecked,
            "KNOWN" => "Known",
            "SKIPPED" or "SKIP" => "Skipped",
            _ => name
        };
    }
}
