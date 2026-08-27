using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using C = DocumentFormat.OpenXml.Drawing.Charts;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace Etp.Reporting.Reporting;

public sealed class OpenXmlVisualReportExporter
{
    public void Export(string path, VisualReportModel model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(model);
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart(); workbook.Workbook = new Workbook();
        var styles = workbook.AddNewPart<WorkbookStylesPart>(); styles.Stylesheet = Stylesheet();
        var sheets = workbook.Workbook.AppendChild(new Sheets()); uint id = 1;
        AddSheet(workbook, sheets, ref id, "Executive Summary", Summary(model));
        var analysis = AddSheet(workbook, sheets, ref id, "Charts & Analysis", Analysis(model));
        if (model.Visuals.FirstOrDefault() is { } primary) AddNativeChart(analysis, primary);
        AddSheet(workbook, sheets, ref id, "Detailed Data", Detail(model.Detail));
        AddSheet(workbook, sheets, ref id, "Controls & Exceptions", Controls(model));
        AddSheet(workbook, sheets, ref id, "Metadata", Metadata(model));
        workbook.Workbook.Save();
    }

    private static SheetData Summary(VisualReportModel model)
    {
        var d = new SheetData(); uint r = 1;
        d.Append(Row(r++, [Cell(model.Metadata.ReportName, 1)]));
        d.Append(Row(r++, [Cell($"{model.Metadata.DateFrom:dd MMM yyyy} to {model.Metadata.DateTo:dd MMM yyyy}", 0)])); r++;
        d.Append(Row(r++, [Cell("Key performance indicator", 2), Cell("Value", 2), Cell("Context", 2)]));
        foreach (var kpi in model.Kpis) d.Append(Row(r++, [Cell(kpi.Label, 0), Cell(IndianNumberFormatter.Format(kpi.Value, kpi.Format, kpi.State), 0), Cell(kpi.Context ?? string.Empty, 0)]));
        r++; d.Append(Row(r++, [Cell("Control status", 2), Cell(model.Controls.FirstOrDefault()?.Status ?? "Not available", 0)]));
        return d;
    }

    private static SheetData Analysis(VisualReportModel model)
    {
        var d = new SheetData(); uint r = 1;
        d.Append(Row(r++, [Cell("Charts & Analysis", 1)]));
        foreach (var visual in model.Visuals)
        {
            d.Append(Row(r++, [Cell(visual.Title, 2), Cell(visual.Type.ToString(), 0)]));
            d.Append(Row(r++, [Cell("Category", 2), .. visual.Series.Select(x => Cell(x.Name, 2))]));
            var categories = visual.Series.SelectMany(x => x.Points.Select(p => p.Category)).Distinct().ToArray();
            foreach (var category in categories)
                d.Append(Row(r++, [Cell(category, 0), .. visual.Series.Select(s => Number(s.Points.FirstOrDefault(p => p.Category == category)?.Value))]));
            if (!string.IsNullOrWhiteSpace(visual.Footnote)) d.Append(Row(r++, [Cell(visual.Footnote!, 0)]));
            r++;
        }
        return d;
    }

    private static SheetData Detail(ExcelReportData data)
    {
        var d = new SheetData(); uint r = 1;
        d.Append(Row(r++, data.Columns.Select(x => Cell(x.Header, 2))));
        foreach (var row in data.Rows) d.Append(Row(r++, row.Select(value => Value(value))));
        if (data.Totals is not null) d.Append(Row(r, data.Totals.Select(x => Value(x, 3))));
        return d;
    }

    private static SheetData Controls(VisualReportModel model)
    {
        var d = new SheetData(); uint r = 1; d.Append(Row(r++, [Cell("Control", 2), Cell("Status", 2), Cell("Evidence / message", 2)]));
        foreach (var control in model.Controls) d.Append(Row(r++, [Cell(control.Name, 0), Cell(control.Status, 0), Cell(control.Message, 0)]));
        return d;
    }

    private static SheetData Metadata(VisualReportModel model)
    {
        var d = new SheetData(); uint r = 1;
        foreach (var pair in new[] { ("Report ID", model.Metadata.ReportId), ("Report", model.Metadata.ReportName), ("Date from", model.Metadata.DateFrom.ToString("yyyy-MM-dd")), ("Date to", model.Metadata.DateTo.ToString("yyyy-MM-dd")), ("Rule version", model.Metadata.RuleVersion), ("Generated UTC", model.Metadata.GeneratedUtc.ToString("u")) })
            d.Append(Row(r++, [Cell(pair.Item1, 2), Cell(pair.Item2, 0)]));
        r++; foreach (var note in model.Footnotes) d.Append(Row(r++, [Cell("Footnote", 2), Cell(note, 0)]));
        return d;
    }

    private static WorksheetPart AddSheet(WorkbookPart workbook, Sheets sheets, ref uint id, string name, SheetData data)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(new SheetViews(new SheetView { WorkbookViewId = 0 }), new Columns(new Column { Min = 1, Max = 20, Width = 22, CustomWidth = true }), data);
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id++, Name = name });
        return part;
    }

    private static void AddNativeChart(WorksheetPart sheet, ReportVisual visual)
    {
        var categories = visual.Series.SelectMany(x => x.Points.Select(p => p.Category)).Distinct().Count();
        if (categories == 0) return;
        var drawings = sheet.AddNewPart<DrawingsPart>();
        sheet.Worksheet.Append(new Drawing { Id = sheet.GetIdOfPart(drawings) });
        var chartPart = drawings.AddNewPart<ChartPart>();
        var chartSpace = new C.ChartSpace(new C.EditingLanguage { Val = "en-US" });
        var chart = chartSpace.AppendChild(new C.Chart());
        var plot = chart.AppendChild(new C.PlotArea()); plot.Append(new C.Layout());
        const uint categoryAxisId = 48650112, valueAxisId = 48672768;
        if (visual.Type is ReportVisualType.Line or ReportVisualType.Sparkline)
        {
            var lines = plot.AppendChild(new C.LineChart(new C.Grouping { Val = C.GroupingValues.Standard }, new C.VaryColors { Val = false }));
            for (var index = 0; index < visual.Series.Count; index++)
            {
                var column = Column(index + 2);
                lines.Append(new C.LineChartSeries(new C.Index { Val = (uint)index }, new C.Order { Val = (uint)index },
                    new C.SeriesText(new C.StringReference(new C.Formula($"'Charts & Analysis'!${column}$3"))),
                    new C.Marker(new C.Symbol { Val = C.MarkerStyleValues.Circle }, new C.Size { Val = 5 }),
                    new C.CategoryAxisData(new C.StringReference(new C.Formula($"'Charts & Analysis'!$A$4:$A${3 + categories}"))),
                    new C.Values(new C.NumberReference(new C.Formula($"'Charts & Analysis'!${column}$4:${column}${3 + categories}")))));
            }
            lines.Append(new C.AxisId { Val = categoryAxisId }, new C.AxisId { Val = valueAxisId });
        }
        else
        {
            var bars = plot.AppendChild(new C.BarChart(new C.BarDirection { Val = visual.Type == ReportVisualType.Bar ? C.BarDirectionValues.Bar : C.BarDirectionValues.Column }, new C.BarGrouping { Val = C.BarGroupingValues.Clustered }, new C.VaryColors { Val = false }));
            for (var index = 0; index < visual.Series.Count; index++)
            {
                var column = Column(index + 2);
                bars.Append(new C.BarChartSeries(new C.Index { Val = (uint)index }, new C.Order { Val = (uint)index },
                    new C.SeriesText(new C.StringReference(new C.Formula($"'Charts & Analysis'!${column}$3"))),
                    new C.CategoryAxisData(new C.StringReference(new C.Formula($"'Charts & Analysis'!$A$4:$A${3 + categories}"))),
                    new C.Values(new C.NumberReference(new C.Formula($"'Charts & Analysis'!${column}$4:${column}${3 + categories}")))));
            }
            bars.Append(new C.AxisId { Val = categoryAxisId }, new C.AxisId { Val = valueAxisId });
        }
        plot.Append(new C.CategoryAxis(new C.AxisId { Val = categoryAxisId }, new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }), new C.AxisPosition { Val = C.AxisPositionValues.Bottom }, new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo }, new C.CrossingAxis { Val = valueAxisId }, new C.Crosses { Val = C.CrossesValues.AutoZero }, new C.AutoLabeled { Val = true }, new C.LabelAlignment { Val = C.LabelAlignmentValues.Center }, new C.LabelOffset { Val = 100 }));
        plot.Append(new C.ValueAxis(new C.AxisId { Val = valueAxisId }, new C.Scaling(new C.Orientation { Val = C.OrientationValues.MinMax }), new C.AxisPosition { Val = C.AxisPositionValues.Left }, new C.MajorGridlines(), new C.NumberingFormat { FormatCode = "#,##0.00", SourceLinked = true }, new C.TickLabelPosition { Val = C.TickLabelPositionValues.NextTo }, new C.CrossingAxis { Val = categoryAxisId }, new C.Crosses { Val = C.CrossesValues.AutoZero }, new C.CrossBetween { Val = C.CrossBetweenValues.Between }));
        chart.Append(new C.PlotVisibleOnly { Val = true }); chartPart.ChartSpace = chartSpace;
        var frame = new Xdr.GraphicFrame(new Xdr.NonVisualGraphicFrameProperties(new Xdr.NonVisualDrawingProperties { Id = 2, Name = visual.Title }, new Xdr.NonVisualGraphicFrameDrawingProperties()), new Xdr.Transform(), new A.Graphic(new A.GraphicData(new C.ChartReference { Id = drawings.GetIdOfPart(chartPart) }) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/chart" }));
        drawings.WorksheetDrawing = new Xdr.WorksheetDrawing(new Xdr.TwoCellAnchor(new Xdr.FromMarker(new Xdr.ColumnId("6"), new Xdr.ColumnOffset("0"), new Xdr.RowId("1"), new Xdr.RowOffset("0")), new Xdr.ToMarker(new Xdr.ColumnId("16"), new Xdr.ColumnOffset("0"), new Xdr.RowId("20"), new Xdr.RowOffset("0")), frame, new Xdr.ClientData()));
        drawings.WorksheetDrawing.Save(); sheet.Worksheet.Save();
    }
    private static Row Row(uint index, IEnumerable<Cell> cells) { var row = new Row { RowIndex = index }; var col = 1; foreach (var cell in cells) { cell.CellReference = $"{Column(col++)}{index}"; row.Append(cell); } return row; }
    private static Cell Value(object? value, uint style = 0) => value is decimal or double or float or int or long ? Number(Convert.ToDecimal(value, CultureInfo.InvariantCulture), style) : Cell(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, style);
    private static Cell Number(decimal? value, uint style = 0) => value is null ? Cell(string.Empty, style) : new Cell { CellValue = new CellValue(value.Value.ToString(CultureInfo.InvariantCulture)), DataType = CellValues.Number, StyleIndex = style };
    private static Cell Cell(string value, uint style) => new() { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)), StyleIndex = style };
    private static string Column(int count) { var result = string.Empty; while (count > 0) { count--; result = (char)('A' + count % 26) + result; count /= 26; } return result; }
    private static Stylesheet Stylesheet() => new(new Fonts(new Font(), new Font(new Bold(), new FontSize { Val = 16 }, new Color { Rgb = "FF17324D" }), new Font(new Bold()), new Font(new Bold())), new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })), new Borders(new Border()), new CellStyleFormats(new CellFormat()), new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true }, new CellFormat { FontId = 2, ApplyFont = true }, new CellFormat { FontId = 3, ApplyFont = true }), new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
}

public sealed class SimplePdfVisualReportExporter
{
    private const double W = 841.89, H = 595.28, M = 36;
    public void Export(string path, VisualReportModel model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(model);
        var pages = new List<string> { Summary(model) };
        pages.AddRange(model.Detail.Rows.Chunk(22).Select((rows, i) => Detail(model, rows, i + 1)));
        if (model.Detail.Rows.Count == 0) pages.Add(Detail(model, [], 1));
        WritePdf(path, pages);
    }
    private static string Summary(VisualReportModel m)
    {
        var b = new StringBuilder(); Text(b, "F2", 18, M, H - 42, m.Metadata.ReportName); Text(b, "F1", 9, M, H - 60, $"{m.Metadata.DateFrom:dd MMM yyyy} to {m.Metadata.DateTo:dd MMM yyyy} | Rule {m.Metadata.RuleVersion}");
        var x = M; foreach (var k in m.Kpis.Take(4)) { b.AppendLine("0.93 0.95 0.97 rg"); b.AppendLine($"{x:F2} {H - 142:F2} 175 62 re f"); Text(b, "F1", 8, x + 8, H - 99, k.Label); Text(b, "F2", 14, x + 8, H - 124, IndianNumberFormatter.Format(k.Value, k.Format, k.State)); x += 185; }
        var visual = m.Visuals.FirstOrDefault(); if (visual is not null) DrawBars(b, visual, M, H - 420, W - M * 2, 230);
        var control = m.Controls.FirstOrDefault(); Text(b, "F2", 9, M, 74, $"Control: {control?.Status ?? "Not available"}"); Text(b, "F1", 8, M, 58, Clip(control?.Message ?? string.Empty, 150)); Text(b, "F1", 7, M, 20, "Visuals and details use the same governed report result."); return b.ToString();
    }
    private static void DrawBars(StringBuilder b, ReportVisual visual, double x, double y, double w, double h)
    {
        Text(b, "F2", 12, x, y + h + 18, visual.Title); var points = visual.Series.FirstOrDefault()?.Points.Where(p => p.Value is not null).Take(10).ToArray() ?? []; var max = Math.Max(1m, points.Select(p => Math.Abs(p.Value ?? 0)).DefaultIfEmpty(1).Max()); var slot = w / Math.Max(1, points.Length);
        for (var i = 0; i < points.Length; i++) { var bh = (double)(Math.Abs(points[i].Value ?? 0) / max) * (h - 35); b.AppendLine("0.14 0.48 0.63 rg"); b.AppendLine($"{x + i * slot + 5:F2} {y + 20:F2} {Math.Max(5, slot - 12):F2} {bh:F2} re f"); Text(b, "F1", 6, x + i * slot + 3, y + 6, Clip(points[i].Category, 10)); }
    }
    private static string Detail(VisualReportModel m, IReadOnlyList<IReadOnlyList<object?>> rows, int page)
    {
        var b = new StringBuilder(); Text(b, "F2", 15, M, H - 40, $"{m.Metadata.ReportName} - Detailed Data"); var top = H - 68; var cw = (W - M * 2) / m.Detail.Columns.Count; b.AppendLine("0.09 0.20 0.30 rg"); b.AppendLine($"{M} {top - 18:F2} {W - M * 2:F2} 18 re f"); for (var i = 0; i < m.Detail.Columns.Count; i++) Text(b, "F2", 6, M + i * cw + 3, top - 13, Clip(m.Detail.Columns[i].Header, Math.Max(5, (int)(cw / 4.2)))); var y = top - 36; foreach (var row in rows) { for (var i = 0; i < m.Detail.Columns.Count; i++) Text(b, "F1", 6, M + i * cw + 3, y, Clip(i < row.Count ? Convert.ToString(row[i], CultureInfo.InvariantCulture) ?? "" : "", Math.Max(5, (int)(cw / 4.2)))); y -= 20; } Text(b, "F1", 7, W - 90, 20, $"Detail page {page}"); return b.ToString();
    }
    private static void WritePdf(string path, IReadOnlyList<string> pages)
    {
        var objects = new List<byte[]> { Array.Empty<byte>() }; var f1 = Add(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"); var f2 = Add(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"); var ids = new List<int>(); var payload = new List<(int,int)>(); foreach (var p in pages) { var bytes = Encoding.ASCII.GetBytes(p); var c = Add(objects, $"<< /Length {bytes.Length} >>\nstream\n{p}\nendstream"); var id = Add(objects, ""); ids.Add(id); payload.Add((id,c)); } var pagesId = Add(objects, ""); foreach (var p in payload) objects[p.Item1] = Bytes($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {W:F2} {H:F2}] /Resources << /Font << /F1 {f1} 0 R /F2 {f2} 0 R >> >> /Contents {p.Item2} 0 R >>"); objects[pagesId] = Bytes($"<< /Type /Pages /Count {ids.Count} /Kids [{string.Join(' ', ids.Select(x => $"{x} 0 R"))}] >>"); var catalog = Add(objects, $"<< /Type /Catalog /Pages {pagesId} 0 R >>"); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var s = File.Create(path); Write(s, "%PDF-1.4\n%ETPV\n"); var offsets = new List<long>{0}; for(var i=1;i<objects.Count;i++){offsets.Add(s.Position);Write(s,$"{i} 0 obj\n");s.Write(objects[i]);Write(s,"\nendobj\n");}var xr=s.Position;Write(s,$"xref\n0 {objects.Count}\n0000000000 65535 f \n");foreach(var o in offsets.Skip(1))Write(s,$"{o:0000000000} 00000 n \n");Write(s,$"trailer\n<< /Size {objects.Count} /Root {catalog} 0 R >>\nstartxref\n{xr}\n%%EOF\n");
    }
    private static void Text(StringBuilder b,string font,int size,double x,double y,string value)=>b.AppendLine($"BT /{font} {size} Tf 0 g 1 0 0 1 {x:F2} {y:F2} Tm ({Escape(value)}) Tj ET"); private static string Escape(string v)=>new(v.Replace("\\","\\\\").Replace("(","\\(").Replace(")","\\)").Select(c=>c is >= ' ' and <= '~'?c:'?').ToArray()); private static string Clip(string v,int n)=>v.Length<=n?v:v[..Math.Max(1,n-3)]+"..."; private static int Add(List<byte[]>o,string v){o.Add(Bytes(v));return o.Count-1;} private static byte[] Bytes(string v)=>Encoding.ASCII.GetBytes(v); private static void Write(Stream s,string v)=>s.Write(Bytes(v));
}
