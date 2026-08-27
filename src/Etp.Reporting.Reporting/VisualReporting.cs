using System.Globalization;
using System.Text;

namespace Etp.Reporting.Reporting;

public enum ReportVisualType { Line, Column, Bar, ClusteredBar, StackedBar, Donut, Progress, Comparison, Sparkline }
public enum VisualValueState { Available, Missing, NotApplicable }
public enum VisualReportTemplate { ExecutiveSummary, Trend, Comparison, Ranking, Composition, Exception, Stock }

public sealed record ReportKpi(string Label, decimal? Value, string Format, VisualValueState State = VisualValueState.Available, string? Context = null);
public sealed record ReportVisualPoint(string Category, decimal? Value, VisualValueState State = VisualValueState.Available);
public sealed record ReportVisualSeries(string Name, IReadOnlyList<ReportVisualPoint> Points, string Colour);
public sealed record ReportVisual(string Title, ReportVisualType Type, IReadOnlyList<ReportVisualSeries> Series, string ValueFormat, string? Footnote = null);
public sealed record ReportControl(string Name, string Status, string Message);
public sealed record VisualReportMetadata(string ReportId, string ReportName, DateOnly DateFrom, DateOnly DateTo, string RuleVersion, DateTimeOffset GeneratedUtc);
public sealed record VisualReportModel(VisualReportMetadata Metadata, IReadOnlyList<ReportKpi> Kpis,
    IReadOnlyList<ReportVisual> Visuals, ExcelReportData Detail, IReadOnlyList<ReportControl> Controls,
    IReadOnlyList<string> Footnotes);

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
            "percent" => value.Value.ToString("0.0%;−0.0%;0.0%", India),
            "integer" => value.Value.ToString("#,##0;−#,##0;0", India),
            _ => value.Value.ToString("#,##0.00;−#,##0.00;0.00", India)
        };
    }
}

public static class VisualReportComposer
{
    public static bool IsRepresentative(string reportName) => VisualReportRegistry.Find(reportName) is not null;

    public static VisualReportModel Compose(ExcelReportMetadata metadata, ExcelReportData data)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(data);
        var numeric = data.Columns.Select((column, index) => (column, index))
            .Where(x => x.column.NumberFormat is "#,##0.00" or "#,##0").ToArray();
        var labelIndex = data.Columns.Select((column, index) => (column, index))
            .FirstOrDefault(x => x.column.NumberFormat == "General").index;
        var kpis = numeric.Take(4).Select(x => new ReportKpi(x.column.Header,
            Total(data, x.index), DisplayFormat(x.column),
            Total(data, x.index) is null ? VisualValueState.Missing : VisualValueState.Available)).ToList();
        if (kpis.Count == 0) kpis.Add(new("Rows", data.Rows.Count, "integer"));

        var visuals = new List<ReportVisual>();
        var definition = VisualReportRegistry.Find(metadata.ReportName);
        if (numeric.Length > 0 && definition is not null)
        {
            var primary = numeric[0];
            var points = TopN(data.Rows.Select(row => new ReportVisualPoint(Label(row, labelIndex), Number(row, primary.index))).ToArray(), 10);
            var type = definition.PrimaryVisualType;
            var series = new List<ReportVisualSeries> { new(primary.column.Header, points, VisualReportTheme.Blue) };
            if (type == ReportVisualType.ClusteredBar && numeric.Length > 1)
                series.Add(new(numeric[1].column.Header, TopN(data.Rows.Select(row => new ReportVisualPoint(Label(row, labelIndex), Number(row, numeric[1].index))).ToArray(), 10), VisualReportTheme.Teal));
            visuals.Add(new($"{metadata.ReportName} analysis", type, series,
                DisplayFormat(primary.column), "Top 10 categories are shown; remaining categories are combined as Other."));
        }

        var controls = new[] { new ReportControl("Report control", metadata.Status, metadata.Message) };
        return new(new(definition?.ReportId ?? "RPT-GENERIC", metadata.ReportName, metadata.DateFrom, metadata.DateTo, metadata.RuleVersion, metadata.GeneratedUtc),
            kpis, visuals, data, controls,
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

    private static string Label(IReadOnlyList<object?> row, int index) => index < row.Count ? Convert.ToString(row[index], CultureInfo.InvariantCulture) ?? "Not available" : "Not available";
    private static string DisplayFormat(ExcelReportColumn column)
    {
        var header = column.Header;
        if (header.Contains('%') || header.Contains("Contribution", StringComparison.OrdinalIgnoreCase) || header.Contains("Achievement", StringComparison.OrdinalIgnoreCase) || header.Contains("Growth", StringComparison.OrdinalIgnoreCase)) return "percent";
        if (new[] { "Sales", "Value", "Cost", "Amount", "Tender", "Invoice", "Cash", "Variance", "Revenue" }.Any(x => header.Contains(x, StringComparison.OrdinalIgnoreCase))) return "currency";
        return column.NumberFormat == "#,##0" ? "integer" : "number";
    }
    private static decimal? Number(IReadOnlyList<object?> row, int index) => index < row.Count && TryDecimal(row[index], out var value) ? value : null;
    private static bool TryDecimal(object? value, out decimal number)
    {
        if (value is null) { number = 0; return false; }
        try { number = Convert.ToDecimal(value, CultureInfo.InvariantCulture); return true; }
        catch { number = 0; return false; }
    }
}

public sealed record VisualReportDefinition(string ReportId, string NameMatch, VisualReportTemplate Template, ReportVisualType PrimaryVisualType, int MaximumCategories = 10);

public static class VisualReportRegistry
{
    public static IReadOnlyList<VisualReportDefinition> All { get; } =
    [
        new("RPT-SALES-001", "Daily Sales", VisualReportTemplate.Trend, ReportVisualType.Line),
        new("RPT-SALES-002", "Brand", VisualReportTemplate.Ranking, ReportVisualType.Bar),
        new("RPT-STOCK-001", "Closing Stock", VisualReportTemplate.Stock, ReportVisualType.StackedBar),
        new("RPT-STAFF-001", "Staff", VisualReportTemplate.Ranking, ReportVisualType.Bar),
        new("RPT-TENDER-001", "Tender Reconciliation", VisualReportTemplate.Comparison, ReportVisualType.ClusteredBar),
        new("RPT-MGMT-001", "Management Trend", VisualReportTemplate.ExecutiveSummary, ReportVisualType.Line),
        new("RPT-EXCEPTION-001", "Daily Exceptions", VisualReportTemplate.Exception, ReportVisualType.Bar)
    ];
    public static VisualReportDefinition? Find(string reportName) => All.FirstOrDefault(x => reportName.Contains(x.NameMatch, StringComparison.OrdinalIgnoreCase));
}

public interface IChartRenderer { string RenderSvg(ReportVisual visual, int width = 900, int height = 360); }

public sealed class SvgChartRenderer : IChartRenderer
{
    public string RenderSvg(ReportVisual visual, int width = 900, int height = 360)
    {
        ArgumentNullException.ThrowIfNull(visual);
        var points = visual.Series.SelectMany(x => x.Points).Where(x => x.Value is not null).ToArray();
        var max = Math.Max(1m, points.Select(x => Math.Abs(x.Value ?? 0)).DefaultIfEmpty(1).Max());
        var b = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" role=\"img\"><title>{Xml(visual.Title)}</title><rect width=\"100%\" height=\"100%\" fill=\"white\"/><text x=\"24\" y=\"30\" font-family=\"Segoe UI\" font-size=\"18\" font-weight=\"600\" fill=\"{VisualReportTheme.Navy}\">{Xml(visual.Title)}</text>");
        var first = visual.Series.FirstOrDefault();
        if (first is not null)
        {
            var chartTop = 55; var chartHeight = height - 95; var slot = Math.Max(1d, (width - 80d) / Math.Max(1, first.Points.Count));
            for (var i = 0; i < first.Points.Count; i++)
            {
                var point = first.Points[i]; var value = point.Value ?? 0; var h = (double)(Math.Abs(value) / max) * (chartHeight - 30); var x = 50 + i * slot; var y = chartTop + chartHeight - h;
                b.Append($"<rect x=\"{x:F1}\" y=\"{y:F1}\" width=\"{Math.Max(4, slot - 12):F1}\" height=\"{h:F1}\" fill=\"{first.Colour}\"><title>{Xml(point.Category)}: {Xml(IndianNumberFormatter.Format(value, visual.ValueFormat))}</title></rect>");
                b.Append($"<text x=\"{x:F1}\" y=\"{height - 18}\" font-family=\"Segoe UI\" font-size=\"10\" fill=\"{VisualReportTheme.Navy}\">{Xml(Clip(point.Category, 12))}</text>");
            }
        }
        return b.Append("</svg>").ToString();
    }
    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
    private static string Clip(string value, int length) => value.Length <= length ? value : value[..(length - 1)] + "…";
}
