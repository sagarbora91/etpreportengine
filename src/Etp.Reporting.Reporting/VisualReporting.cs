using System.Globalization;

namespace Etp.Reporting.Reporting;

public enum ReportVisualType { Line, Bar }
public enum VisualValueState { Available, Missing, NotApplicable }

public sealed record ReportKpi(string Label, decimal? Value, string Format, VisualValueState State = VisualValueState.Available, string? Context = null);
public sealed record ReportVisualPoint(string Category, decimal? Value, VisualValueState State = VisualValueState.Available);
public sealed record ReportVisualSeries(string Name, IReadOnlyList<ReportVisualPoint> Points, string Colour);
public sealed record ReportVisual(string Title, ReportVisualType Type, IReadOnlyList<ReportVisualSeries> Series, string ValueFormat, string? Footnote = null);
public sealed record ReportControl(string Name, string Status, string Message);
public sealed record VisualReportMetadata(string ReportId, string ReportName, DateOnly DateFrom, DateOnly DateTo, string RuleVersion, DateTimeOffset GeneratedUtc, string? AppliedScope = null);
public sealed record VisualReportModel(VisualReportMetadata Metadata, IReadOnlyList<ReportKpi> Kpis,
    IReadOnlyList<ReportVisual> Visuals, ExcelReportData Detail, IReadOnlyList<ReportControl> Controls,
    IReadOnlyList<string> Footnotes)
{
    /// <summary>True when the summary is more than the "Rows" fallback card, so the PDF gets a summary page.</summary>
    public bool HasSummary => Visuals.Count > 0 || Kpis.Count > 1;
}

public static class VisualReportTheme
{
    public const string Navy = "#17324D";
    public const string Blue = "#247BA0";
    public const string Teal = "#2A9D8F";
    public const string Amber = "#E9A23B";
    public const string Red = "#C94C4C";
    public const string Grey = "#8795A1";
    public static readonly IReadOnlyList<string> SeriesColours = [Blue, Teal, Amber, Red, Grey];
}

public static class IndianNumberFormatter
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");
    public static string Format(decimal? value, string format, VisualValueState state = VisualValueState.Available)
    {
        if (state == VisualValueState.NotApplicable) return "N/A";
        if (state == VisualValueState.Missing || value is null) return "Not available";
        return format switch
        {
            "currency" => value.Value.ToString("₹#,##0.00;−₹#,##0.00;₹0.00", India),
            "percent" => value.Value.ToString("0.0;−0.0;0.0", India) + "%",
            "integer" => value.Value.ToString("#,##0;−#,##0;0", India),
            _ => value.Value.ToString("#,##0.00;−#,##0.00;0.00", India)
        };
    }
}

public static class VisualReportComposer
{
    /// <summary>
    /// Builds the summary model for one report result. The family comes from the classification registry by
    /// <paramref name="reportCode"/> (the workspace knows it) or, for callers that only have the export name (daily
    /// pack, tabular PDF), by that name; an unknown report or a table without the family's columns gets the "Rows" card.
    /// </summary>
    public static VisualReportModel Compose(ExcelReportMetadata metadata, ExcelReportData data, string? reportCode = null)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(data);
        // A report's periods and percentages are not additive. Detail owns its totals; the summary only reads them.
        var summary = ReportSummaryBuilder.Build(ProductReportVisualClassificationRegistry.FamilyFor(reportCode, metadata.ReportName), data);
        var controls = new[] { new ReportControl("Report control", metadata.Status, metadata.Message) };
        return new(new(reportCode ?? metadata.ReportName, metadata.ReportName, metadata.DateFrom, metadata.DateTo, metadata.RuleVersion, metadata.GeneratedUtc, metadata.AppliedScope),
            summary.Kpis, summary.Visuals, data, controls,
            ["All KPIs, visuals and detail rows use the same report result; visuals do not recalculate business values.", "Blank, zero and not-applicable values are displayed differently."]);
    }

    public static decimal? Total(ExcelReportData data, int column)
    {
        if (data.Totals is { } totals && column < totals.Count && TryDecimal(totals[column], out var total)) return total;
        var values = data.Rows.Select(row => column < row.Count && TryDecimal(row[column], out var value) ? value : (decimal?)null).ToArray();
        return values.Any(x => x is not null) ? values.Sum(x => x ?? 0) : null;
    }

    public static IReadOnlyList<ReportVisualPoint> TopN(IReadOnlyList<ReportVisualPoint> points, int count)
    {
        var available = points.Where(x => x.State == VisualValueState.Available && x.Value is not null)
            .GroupBy(x => x.Category).Select(g => new ReportVisualPoint(g.Key, g.Sum(x => x.Value ?? 0)))
            .OrderByDescending(x => Math.Abs(x.Value ?? 0)).ToArray();
        if (available.Length <= count) return available;
        return available.Take(count).Append(new("Other", available.Skip(count).Sum(x => x.Value ?? 0))).ToArray();
    }

    internal static bool TryDecimal(object? value, out decimal number)
    {
        if (value is null) { number = 0; return false; }
        try { number = Convert.ToDecimal(value, CultureInfo.InvariantCulture); return true; }
        catch { number = 0; return false; }
    }
}
