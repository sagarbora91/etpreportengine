namespace Etp.Reporting.Desktop;

/// <summary>Existing catalogue labels continue to find the same canonical reports.</summary>
public static class ReportTaskAliases
{
    private static readonly IReadOnlyDictionary<string, string[]> aliases = new Dictionary<string, string[]>
    {
        ["cash"] = ["Daily Cash Reconciliation"],
        // RA-UI-23 (1.9.9): Ctrl+K "evening" found nothing; the Evening DSR lives inside the DSR.
        ["dsr"] = ["Daily Sales / DSR", "LY / TY Comparison", "Evening DSR", "Evening report"],
        ["exceptions"] = ["Daily Exception Report"],
        ["invoice"] = ["Invoice Summary"],
        ["invoice-lineage"] = ["Invoice Source Drill-down"],
        ["management-trend"] = ["Management Trend"],
        ["sales-brand"] = ["Brand-wise Sales"],
        ["sales-combined"] = ["Combined Sales Summary"],
        ["sales-item"] = ["Item-wise Sales"],
        ["sales-returns"] = ["Returns"],
        ["sales-segment"] = ["Brand-Segment Sales"],
        ["sales-store"] = ["Store Sales Summary"],
        ["service"] = ["Service Sales"],
        ["staff"] = ["Staff Performance", "Targets & Achievement", "Ranking", "LY Comparison", "Contribution"],
        ["stock-brand"] = ["Brand Stock"],
        ["stock-closing"] = ["Closing Stock"],
        ["stock-movement"] = ["Stock Movement"],
        ["stock-physical"] = ["Physical Stock"],
        ["stock-slow"] = ["Slow / Exception Stock"],
        ["stock-variance"] = ["Stock Variance"],
        ["tender"] = ["Tender Reconciliation"],
        ["tender-diagnostic"] = ["Tender Diagnostics"],
    };
    public static IReadOnlyList<string> For(string? code) => code is not null && aliases.TryGetValue(code, out var labels) ? labels : [];
}
