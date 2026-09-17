using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Etp.Reporting.Reporting;

// Uses the same embedded Windows font resolver as DSR. Values are never clipped to fit.
internal sealed class VisualReportPdfDocument : IDisposable
{
    private const double Width = 841.89, Height = 595.28, Margin = 36, ContentWidth = Width - 2 * Margin;
    private readonly PdfDocument document = new() { Version = 14 };
    private VisualReportModel model;
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
        ExportMany(path,[model]);
    }

    public static void ExportMany(string path,IReadOnlyList<VisualReportModel> models)
    {
        if(models.Count==0 || models.Any(x=>x.Detail.Columns.Count==0)) throw new ArgumentException("At least one report with columns is required.");
        DsrPdfFontResolver.EnsureRegistered();
        using var renderer=new VisualReportPdfDocument(models[0]);
        foreach(var model in models){renderer.model=model;renderer.Details();}
        renderer.graphics.Dispose();
        for(var i=0;i<renderer.document.PageCount;i++)
        { using var footer=XGraphics.FromPdfPage(renderer.document.Pages[i],XGraphicsPdfPageOptions.Append);footer.DrawString($"Page {i+1} of {renderer.document.PageCount}",renderer.normal,XBrushes.Gray,new XPoint(Margin,Height-20)); }
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
        if (!string.IsNullOrWhiteSpace(model.Metadata.AppliedScope))
            foreach (var line in Wrap(model.Metadata.AppliedScope, normal, ContentWidth)) { Line(line, normal, navy, Margin, y); y += 13; }
        foreach(var line in Wrap(string.Join("; ",model.Controls.Select(c=>$"{c.Status}: {c.Message}"))+ $" · Rule {model.Metadata.RuleVersion}",normal,ContentWidth)){Line(line,normal,navy,Margin,y);y+=13;} y+=8;
    }

    private void Details()
    {
        var count = model.Detail.Columns.Count;
        graphics?.Dispose();
        graphics=XGraphics.CreateMeasureContext(new XSize(Width,Height),XGraphicsUnit.Point,XPageDirection.Downwards);
        var numeric=Enumerable.Range(0,count).Select(c=>model.Detail.Rows.Any(r=>c<r.Count && r[c] is decimal or int or long or double)).ToArray();
        var desired=Enumerable.Range(0,count).Select(c=>Math.Max(54,Math.Max(
            graphics.MeasureString(model.Detail.Columns[c].Header,bold).Width+12,
            model.Detail.Rows.Concat(model.Detail.Totals is null?[]:new[]{model.Detail.Totals}).Select(r=>c<r.Count?graphics.MeasureString(Format(r[c],model.Detail.Columns[c].NumberFormat),normal).Width+12:0).DefaultIfEmpty(0).Max()))).ToArray();
        for(var c=0;c<count;c++)if(!numeric[c])desired[c]=Math.Min(145,desired[c]);
        var bandList=new List<int[]>();var pending=new List<int>();var used=0d;
        foreach(var c in Enumerable.Range(0,count))
        {
            if(pending.Count>1 && used+desired[c]>ContentWidth-36){bandList.Add(pending.ToArray());pending=[0];used=desired[0];}
            pending.Add(c);used+=desired[c];
        }
        if(pending.Count>0)bandList.Add(pending.ToArray());
        var bands=bandList.ToArray();
        for (var band = 0; band < bands.Length; band++)
        {
            var columns = bands[band]; var extra=Math.Max(0,ContentWidth-36-columns.Sum(c=>desired[c]))/columns.Length;
            var widths=columns.Select(c=>desired[c]+extra).ToArray();
            double Left(int column)=>Margin+42+widths.Take(column).Sum();
            var section = $"Detailed Data - section {band + 1} of {bands.Length}";
            void Header()
            {
                NewPage(section);
                var labels = columns.Select((c,i) => Wrap(model.Detail.Columns[c].Header, bold, widths[i] - 12)).ToArray();
                var height = labels.Max(lines => lines.Count) * 13 + 12;
                graphics.DrawRectangle(navy, Margin, y, ContentWidth, height);
                Line("Row", bold, XBrushes.White, Margin + 4, y + 6);
                for (var c = 0; c < columns.Length; c++)
                    for (var l = 0; l < labels[c].Count; l++) Line(labels[c][l], bold, XBrushes.White, Left(c), y + 6 + l * 13);
                y += height;
            }
            Header();
            var rows = model.Detail.Rows.Select((row, index) => (row, label: (index + 1).ToString(CultureInfo.InvariantCulture), total: false)).ToList();
            if (model.Detail.Totals is { } totals) rows.Add((totals, "Total", true));
            if (rows.Count == 0) Paragraph("No rows for the selected scope.", normal);
            foreach (var (row, label, total) in rows)
            {
                var font = total ? bold : normal;
                var cells = columns.Select((c,i) => Wrap(Format(c < row.Count ? row[c] : null, model.Detail.Columns[c].NumberFormat), font, widths[i] - 12)).ToArray();
                var lineCount = cells.Max(lines => lines.Count); var offset = 0;
                while (offset < lineCount)
                {
                    var available = (int)((Height - 45 - y - 12) / 13);
                    if (available < 1 || (offset == 0 && lineCount <= 25 && lineCount > available)) { Header(); continue; }
                    var lines = Math.Min(lineCount - offset, available); var height = lines * 13 + 12;
                    if (total) graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(235, 242, 247)), Margin, y, ContentWidth, height);
                    Line(label + (offset > 0 ? "+" : ""), font, navy, Margin + 4, y + 6);
                    for (var c = 0; c < columns.Length; c++)
                        for (var l = 0; l < lines && offset + l < cells[c].Count; l++) Line(cells[c][offset + l], font, navy, Left(c), y + 6 + l * 13);
                    y += height; graphics.DrawLine(new XPen(XColors.LightGray, .5), Margin, y, Width - Margin, y);
                    offset += lines;
                }
            }
        }
    }

    private static string Format(object? value, string format) => value switch
    {
        null => "—", decimal n => format.Contains('%') ? n.ToString("0.00",CultureInfo.GetCultureInfo("en-IN"))+"%" : n.ToString("N2",CultureInfo.GetCultureInfo("en-IN")),
        DateOnly date => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
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
