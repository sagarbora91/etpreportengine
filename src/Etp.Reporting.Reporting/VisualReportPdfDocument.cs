using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Etp.Reporting.Reporting;

// Uses the same embedded Windows font resolver as DSR. Values are never clipped to fit.
internal sealed class VisualReportPdfDocument : IDisposable
{
    private const double Width = 841.89, Height = 595.28, Margin = 36, ContentWidth = Width - 2 * Margin;
    private readonly PdfDocument document = new() { Version = 14 };
    private readonly VisualReportModel model;
    private readonly XFont normal = new("Segoe UI", 9);
    private readonly XFont bold = new("Segoe UI", 9, XFontStyleEx.Bold);
    private readonly XFont title = new("Segoe UI", 18, XFontStyleEx.Bold);
    private readonly XSolidBrush navy = new(XColor.FromArgb(23, 50, 77));
    private readonly XSolidBrush blue = new(XColor.FromArgb(36, 123, 160));
    private XGraphics graphics = null!;
    private double y;

    private VisualReportPdfDocument(VisualReportModel model) { this.model = model; document.Info.Title = model.Metadata.ReportName; }

    public static void Export(string path, VisualReportModel model)
    {
        if (model.Detail.Columns.Count == 0) throw new ArgumentException("At least one report column is required.", nameof(model));
        DsrPdfFontResolver.EnsureRegistered();
        using var renderer = new VisualReportPdfDocument(model);
        renderer.Summary();
        renderer.Details();
        renderer.graphics.Dispose();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        renderer.document.Save(path);
    }

    private void NewPage(string section)
    {
        graphics?.Dispose();
        var page = document.AddPage(); page.Width = XUnit.FromPoint(Width); page.Height = XUnit.FromPoint(Height);
        graphics = XGraphics.FromPdfPage(page); y = Margin;
        foreach (var line in Wrap(model.Metadata.ReportName, title, ContentWidth)) { Line(line, title, navy, Margin, y); y += 24; }
        Line($"{model.Metadata.DateFrom:yyyy-MM-dd} to {model.Metadata.DateTo:yyyy-MM-dd}  |  {section}", normal, navy, Margin, y); y += 23;
        Line($"ETP Reporting Engine  |  Page {document.PageCount}", normal, navy, Margin, Height - 25);
    }

    private void Summary()
    {
        NewPage("Summary");
        foreach (var kpi in model.Kpis)
            Paragraph($"{kpi.Label}: {IndianNumberFormatter.Format(kpi.Value, kpi.Format, kpi.State)}" + (string.IsNullOrWhiteSpace(kpi.Context) ? "" : $"  |  {kpi.Context}"), bold);
        y += 10;
        foreach (var visual in model.Visuals)
        foreach (var series in visual.Series)
        {
            Paragraph($"{visual.Title} - {series.Name}", bold);
            var points = series.Points;
            var min = Math.Min(0, points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).DefaultIfEmpty(0).Min());
            var max = Math.Max(0, points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).DefaultIfEmpty(0).Max());
            var range = max - min; if (range == 0) range = 1;
            const double plotX = 330, plotWidth = 280;
            var zero = plotX + (double)(-min / range) * plotWidth;
            foreach (var point in points)
            {
                var labels = Wrap(point.Category, normal, 280); var rowHeight = Math.Max(22, labels.Count * 13 + 6);
                Ensure(rowHeight);
                for (var i = 0; i < labels.Count; i++) Line(labels[i], normal, navy, Margin, y + i * 13);
                graphics.DrawLine(new XPen(XColors.Gray, .5), zero, y, zero, y + rowHeight - 3);
                if (point.Value is { } value)
                {
                    var end = plotX + (double)((value - min) / range) * plotWidth;
                    graphics.DrawRectangle(blue, Math.Min(zero, end), y + 2, Math.Abs(end - zero), 10);
                }
                Line(point.Value?.ToString(CultureInfo.InvariantCulture) ?? "Not available", normal, navy, plotX + plotWidth + 12, y);
                y += rowHeight;
            }
            if (visual.Footnote is { } footnote) Paragraph(footnote, normal);
            y += 10;
        }
        foreach (var control in model.Controls) Paragraph($"{control.Name}: {control.Status}. {control.Message}", normal);
        foreach (var note in model.Footnotes) Paragraph(note, normal);
        Paragraph($"Rule: {model.Metadata.RuleVersion}. Generated UTC: {model.Metadata.GeneratedUtc:u}", normal);
    }

    private void Details()
    {
        // Repeat the identifying first column and row number across horizontal sections.
        var count = model.Detail.Columns.Count;
        var bands = count <= 4 ? new[] { Enumerable.Range(0, count).ToArray() }
            : Enumerable.Range(1, count - 1).Chunk(3).Select(rest => new[] { 0 }.Concat(rest).ToArray()).ToArray();
        for (var band = 0; band < bands.Length; band++)
        {
            var columns = bands[band]; var width = (ContentWidth - 36) / columns.Length;
            var section = $"Detailed Data - section {band + 1} of {bands.Length}";
            void Header()
            {
                NewPage(section);
                var labels = columns.Select(c => Wrap(model.Detail.Columns[c].Header, bold, width - 12)).ToArray();
                var height = labels.Max(lines => lines.Count) * 13 + 12;
                graphics.DrawRectangle(navy, Margin, y, ContentWidth, height);
                Line("Row", bold, XBrushes.White, Margin + 4, y + 6);
                for (var c = 0; c < columns.Length; c++)
                    for (var l = 0; l < labels[c].Count; l++) Line(labels[c][l], bold, XBrushes.White, Margin + 42 + c * width, y + 6 + l * 13);
                y += height;
            }
            Header();
            var rows = model.Detail.Rows.Select((row, index) => (row, label: (index + 1).ToString(CultureInfo.InvariantCulture), total: false)).ToList();
            if (model.Detail.Totals is { } totals) rows.Add((totals, "Total", true));
            if (rows.Count == 0) Paragraph("No rows for the selected scope.", normal);
            foreach (var (row, label, total) in rows)
            {
                var font = total ? bold : normal;
                var cells = columns.Select(c => Wrap(Format(c < row.Count ? row[c] : null), font, width - 12)).ToArray();
                var lineCount = cells.Max(lines => lines.Count); var offset = 0;
                while (offset < lineCount)
                {
                    var available = (int)((Height - 45 - y - 12) / 13);
                    if (available < 1 || (offset == 0 && lineCount <= 25 && lineCount > available)) { Header(); continue; }
                    var lines = Math.Min(lineCount - offset, available); var height = lines * 13 + 12;
                    if (total) graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(235, 242, 247)), Margin, y, ContentWidth, height);
                    Line(label + (offset > 0 ? "+" : ""), font, navy, Margin + 4, y + 6);
                    for (var c = 0; c < columns.Length; c++)
                        for (var l = 0; l < lines && offset + l < cells[c].Count; l++) Line(cells[c][offset + l], font, navy, Margin + 42 + c * width, y + 6 + l * 13);
                    y += height; graphics.DrawLine(new XPen(XColors.LightGray, .5), Margin, y, Width - Margin, y);
                    offset += lines;
                }
            }
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "Not available", DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };
    private void Ensure(double height) { if (y + height > Height - 45) NewPage("Summary continued"); }
    private void Paragraph(string value, XFont font)
    {
        foreach (var line in Wrap(value, font, ContentWidth)) { Ensure(15); Line(line, font, navy, Margin, y); y += 15; }
        y += 4;
    }
    private List<string> Wrap(string value, XFont font, double width)
    {
        var lines = new List<string>();
        foreach (var paragraph in value.Replace("\r", "").Split('\n'))
        {
            var rest = paragraph;
            if (rest.Length == 0) { lines.Add(""); continue; }
            while (rest.Length > 0)
            {
                var length = rest.Length;
                while (length > 1 && graphics.MeasureString(rest[..length], font).Width > width) length--;
                if (length < rest.Length && rest.LastIndexOf(' ', length - 1, length) is var space && space > 0) length = space + 1;
                lines.Add(rest[..length].TrimEnd()); rest = rest[length..];
            }
        }
        return lines;
    }
    private void Line(string value, XFont font, XBrush brush, double x, double top) => graphics.DrawString(value, font, brush, new XPoint(x, top + font.Size));
    public void Dispose() { graphics?.Dispose(); document.Dispose(); }
}
