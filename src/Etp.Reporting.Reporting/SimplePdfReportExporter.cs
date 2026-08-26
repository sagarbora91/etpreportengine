using System.Globalization;
using System.Text;

namespace Etp.Reporting.Reporting;

public sealed class SimplePdfReportExporter
{
    private const double PageWidth = 841.89;
    private const double PageHeight = 595.28;
    private const double Margin = 36;
    private const int RowsPerPage = 24;

    public void Export(string path, ExcelReportMetadata metadata, ExcelReportData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Columns.Count == 0) throw new ArgumentException("At least one report column is required.", nameof(data));

        var pageRows = data.Rows.Chunk(RowsPerPage).ToList();
        if (pageRows.Count == 0) pageRows.Add(Array.Empty<IReadOnlyList<object?>>());
        var objects = new List<byte[]> { Array.Empty<byte>() };
        var fontId = Add(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var boldFontId = Add(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
        var pageIds = new List<int>();
        var pagePayloads = new List<(int PageId, int ContentId)>();

        foreach (var (rows, index) in pageRows.Select((rows, index) => (rows, index)))
        {
            var content = BuildPage(metadata, data, rows, index + 1, pageRows.Count);
            var contentBytes = Encoding.ASCII.GetBytes(content);
            var contentId = Add(objects, $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream");
            var pageId = Add(objects, string.Empty);
            pageIds.Add(pageId);
            pagePayloads.Add((pageId, contentId));
        }

        var pagesId = Add(objects, string.Empty);
        foreach (var page in pagePayloads)
            objects[page.PageId] = Bytes($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {PageWidth:F2} {PageHeight:F2}] /Resources << /Font << /F1 {fontId} 0 R /F2 {boldFontId} 0 R >> >> /Contents {page.ContentId} 0 R >>");
        objects[pagesId] = Bytes($"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(' ', pageIds.Select(x => $"{x} 0 R"))}] >>");
        var catalogId = Add(objects, $"<< /Type /Catalog /Pages {pagesId} 0 R >>");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        Write(stream, "%PDF-1.4\n%ETPR\n");
        var offsets = new List<long> { 0 };
        for (var i = 1; i < objects.Count; i++)
        {
            offsets.Add(stream.Position);
            Write(stream, $"{i} 0 obj\n"); stream.Write(objects[i]); Write(stream, "\nendobj\n");
        }
        var xref = stream.Position;
        Write(stream, $"xref\n0 {objects.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(stream, $"{offset:0000000000} 00000 n \n");
        Write(stream, $"trailer\n<< /Size {objects.Count} /Root {catalogId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static string BuildPage(ExcelReportMetadata metadata, ExcelReportData data,
        IReadOnlyList<IReadOnlyList<object?>> rows, int page, int pageCount)
    {
        var b = new StringBuilder();
        Text(b, "F2", 18, Margin, PageHeight - 42, metadata.ReportName);
        Text(b, "F1", 9, Margin, PageHeight - 60, $"Period: {metadata.DateFrom:dd MMM yyyy} to {metadata.DateTo:dd MMM yyyy} | Status: {metadata.Status} | Rule: {metadata.RuleVersion}");
        Text(b, "F1", 8, Margin, PageHeight - 75, Clip(metadata.Message, 150));
        var tableTop = PageHeight - 98;
        var usable = PageWidth - 2 * Margin;
        var columnWidth = usable / data.Columns.Count;
        b.AppendLine("0.10 0.22 0.32 rg");
        b.AppendLine($"{Margin:F2} {tableTop - 20:F2} {usable:F2} 20 re f");
        for (var i = 0; i < data.Columns.Count; i++)
            Text(b, "F2", 7, Margin + i * columnWidth + 4, tableTop - 14, Clip(data.Columns[i].Header, Math.Max(8, (int)(columnWidth / 4.8))));
        var y = tableTop - 38;
        foreach (var row in rows)
        {
            b.AppendLine("0.82 0.85 0.88 RG 0.4 w");
            b.AppendLine($"{Margin:F2} {y - 5:F2} {usable:F2} 20 re S");
            for (var i = 0; i < data.Columns.Count; i++)
                Text(b, "F1", 7, Margin + i * columnWidth + 4, y + 1,
                    Clip(Format(i < row.Count ? row[i] : null), Math.Max(8, (int)(columnWidth / 4.8))));
            y -= 20;
        }
        if (page == pageCount && data.Totals is { Count: > 0 } totals)
        {
            b.AppendLine("0.92 0.94 0.96 rg");
            b.AppendLine($"{Margin:F2} {y - 5:F2} {usable:F2} 20 re f");
            for (var i = 0; i < data.Columns.Count; i++)
                Text(b, "F2", 7, Margin + i * columnWidth + 4, y + 1,
                    Clip(Format(i < totals.Count ? totals[i] : null), Math.Max(8, (int)(columnWidth / 4.8))));
        }
        Text(b, "F1", 7, Margin, 20, $"Generated {metadata.GeneratedUtc:dd MMM yyyy HH:mm} UTC");
        Text(b, "F1", 7, PageWidth - 90, 20, $"Page {page} of {pageCount}");
        return b.ToString();
    }

    private static void Text(StringBuilder b, string font, int size, double x, double y, string value) =>
        b.AppendLine($"BT /{font} {size} Tf 0 g 1 0 0 1 {x:F2} {y:F2} Tm ({Escape(value)}) Tj ET");
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Select(c => c is >= ' ' and <= '~' ? c : '?').Aggregate(new StringBuilder(), (b, c) => b.Append(c)).ToString();
    private static string Clip(string value, int limit) => value.Length <= limit ? value : value[..Math.Max(1, limit - 3)] + "...";
    private static string Format(object? value) => value switch { null => string.Empty, decimal d => d.ToString("N2", CultureInfo.InvariantCulture), DateOnly d => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty };
    private static int Add(List<byte[]> objects, string value) { objects.Add(Bytes(value)); return objects.Count - 1; }
    private static byte[] Bytes(string value) => Encoding.ASCII.GetBytes(value);
    private static void Write(Stream stream, string value) => stream.Write(Bytes(value));
}
