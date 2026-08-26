using System.Globalization;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Reporting;

public sealed record ReportPackTable(string Name, string Status, string Message, ExcelReportData Data);

public sealed record ReportPackDocument(
    string Title,
    DateOnly DateFrom,
    DateOnly DateTo,
    string OverallStatus,
    string RuleVersion,
    string Message,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<ReportPackTable> Tables);

public static class ReportPackArchiveCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(ReportPackDocument pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return JsonSerializer.Serialize(pack, Options);
    }

    public static ReportPackDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("An archived report document is required.", nameof(json));
        var pack = JsonSerializer.Deserialize<ReportPackDocument>(json, Options)
            ?? throw new InvalidDataException("The archived report document is invalid.");
        return pack with
        {
            Tables = pack.Tables.Select(table => table with
            {
                Data = table.Data with
                {
                    Rows = table.Data.Rows.Select(row => (IReadOnlyList<object?>)row.Select(Normalize).ToArray()).ToArray(),
                    Totals = table.Data.Totals?.Select(Normalize).ToArray()
                }
            }).ToArray()
        };
    }

    private static object? Normalize(object? value)
    {
        if (value is not JsonElement element) return value;
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };
    }
}

public sealed class OpenXmlReportPackExporter
{
    public void Export(string filePath, ReportPackDocument pack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(pack);
        if (pack.Tables.Count == 0) throw new ArgumentException("A report pack requires at least one table.", nameof(pack));
        foreach (var table in pack.Tables) Validate(table);

        using var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = Styles();
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint sheetId = 1;
        foreach (var table in pack.Tables)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = Worksheet(pack, table);
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId++,
                Name = SheetName(table.Name, usedNames)
            });
        }
        workbookPart.Workbook.Save();
    }

    private static Worksheet Worksheet(ReportPackDocument pack, ReportPackTable table)
    {
        var data = table.Data;
        var sheetData = new SheetData();
        var worksheet = new Worksheet(sheetData);
        var lastColumn = ColumnName(data.Columns.Count);
        sheetData.Append(Row(1, [Text($"{pack.Title} — {table.Name}", 1)]));
        var merges = new MergeCells(new MergeCell { Reference = $"A1:{lastColumn}1" });
        if (data.Columns.Count > 1) merges.Append(new MergeCell { Reference = $"B6:{lastColumn}6" });
        worksheet.Append(merges);
        sheetData.Append(Row(3, [Text("Period", 2), Text($"{pack.DateFrom:yyyy-MM-dd} to {pack.DateTo:yyyy-MM-dd}", 0)]));
        sheetData.Append(Row(4, [Text("Generated UTC", 2), Text(pack.GeneratedUtc.ToString("u", CultureInfo.InvariantCulture), 0)]));
        sheetData.Append(Row(5, [Text("Status", 2), Text(table.Status, 0), Text("Rule version", 2), Text(pack.RuleVersion, 0)]));
        sheetData.Append(Row(6, [Text("Control", 2), Text(table.Message, 0)]));
        const uint headerRow = 8;
        sheetData.Append(Row(headerRow, data.Columns.Select(x => Text(x.Header, 3))));
        uint rowIndex = headerRow + 1;
        foreach (var values in data.Rows)
            sheetData.Append(Row(rowIndex++, values.Select((value, index) => Value(value, FormatStyle(data.Columns[index].NumberFormat)))));
        if (data.Totals is not null)
            sheetData.Append(Row(rowIndex++, data.Totals.Select((value, index) => Value(value, index == 0 ? 5u : FormatStyle(data.Columns[index].NumberFormat, true)))));
        worksheet.InsertAt(new SheetViews(new SheetView(new Pane { VerticalSplit = 8, TopLeftCell = "A9", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }), 0);
        worksheet.InsertAt(new Columns(data.Columns.Select((x, index) => new Column
        {
            Min = (uint)index + 1,
            Max = (uint)index + 1,
            Width = Math.Clamp(Math.Max(x.Header.Length + 3, x.NumberFormat == "#,##0.00" ? 16 : 12), 12, 42),
            CustomWidth = true
        })), 1);
        worksheet.InsertBefore(new AutoFilter { Reference = $"A{headerRow}:{lastColumn}{Math.Max(headerRow, rowIndex - 1)}" }, worksheet.GetFirstChild<MergeCells>());
        return worksheet;
    }

    private static void Validate(ReportPackTable table)
    {
        if (string.IsNullOrWhiteSpace(table.Name) || table.Data.Columns.Count == 0)
            throw new ArgumentException("Every pack table requires a name and at least one column.", nameof(table));
        var invalidRow = table.Data.Rows.Select((row, index) => (row, index)).FirstOrDefault(x => x.row.Count != table.Data.Columns.Count);
        if (invalidRow.row is not null)
            throw new ArgumentException($"Pack table '{table.Name}' row {invalidRow.index + 1} has {invalidRow.row.Count} values for {table.Data.Columns.Count} columns.", nameof(table));
        if (table.Data.Totals is { } totals && totals.Count != table.Data.Columns.Count)
            throw new ArgumentException($"Pack table '{table.Name}' totals have {totals.Count} values for {table.Data.Columns.Count} columns.", nameof(table));
    }

    private static string SheetName(string value, ISet<string> used)
    {
        var invalid = new HashSet<char>(['[', ']', ':', '*', '?', '/', '\\']);
        var baseName = new string(value.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (baseName.Length == 0) baseName = "Report";
        if (baseName.Length > 31) baseName = baseName[..31];
        var candidate = baseName;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            var tail = $" {suffix++}";
            candidate = baseName[..Math.Min(baseName.Length, 31 - tail.Length)] + tail;
        }
        return candidate;
    }

    private static Stylesheet Styles() => new(
        new Fonts(new Font(), new Font(new Bold(), new FontSize { Val = 16 }, new Color { Rgb = "FFFFFFFF" }), new Font(new Bold()), new Font(new Bold(), new Color { Rgb = "FFFFFFFF" })),
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 }), new Fill(new PatternFill(new ForegroundColor { Rgb = "FF176B87" }) { PatternType = PatternValues.Solid }), new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE8EEF5" }) { PatternType = PatternValues.Solid })),
        new Borders(new Border(), new Border(new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFDCE2E8" } })),
        new CellStyleFormats(new CellFormat()),
        new CellFormats(
            new CellFormat(),
            new CellFormat { FontId = 1, FillId = 2, Alignment = new Alignment { Vertical = VerticalAlignmentValues.Center }, ApplyFont = true, ApplyFill = true },
            new CellFormat { FontId = 2, ApplyFont = true },
            new CellFormat { FontId = 3, FillId = 2, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true },
            new CellFormat { NumberFormatId = 4, ApplyNumberFormat = true },
            new CellFormat { FontId = 2, FillId = 3, ApplyFont = true, ApplyFill = true },
            new CellFormat { FontId = 2, FillId = 3, NumberFormatId = 4, ApplyFont = true, ApplyFill = true, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = 3, ApplyNumberFormat = true },
            new CellFormat { FontId = 2, FillId = 3, NumberFormatId = 3, ApplyFont = true, ApplyFill = true, ApplyNumberFormat = true }),
        new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));

    private static Row Row(uint index, IEnumerable<Cell> cells)
    {
        var row = new Row { RowIndex = index };
        var column = 1;
        foreach (var cell in cells) { cell.CellReference = $"{ColumnName(column++)}{index}"; row.Append(cell); }
        return row;
    }

    private static Cell Text(string value, uint style) => new() { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value ?? string.Empty)), StyleIndex = style };
    private static Cell Value(object? value, uint style) => value switch
    {
        null => Text(string.Empty, style),
        decimal d => Number(d, style), double d => Number((decimal)d, style), float f => Number((decimal)f, style),
        int i => Number(i, style), long l => Number(l, style),
        DateOnly date => Text(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), style),
        _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, style)
    };
    private static Cell Number(decimal value, uint style) => new() { CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)), DataType = CellValues.Number, StyleIndex = style };
    private static uint FormatStyle(string numberFormat, bool total = false) => numberFormat switch
    {
        "#,##0.00" => total ? 6u : 4u,
        "#,##0" => total ? 8u : 7u,
        _ => total ? 5u : 0u
    };
    private static string ColumnName(int count) { var value = count; var result = string.Empty; while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; } return result; }
}

public sealed class SimplePdfReportPackExporter
{
    private const double PageWidth = 841.89;
    private const double PageHeight = 595.28;
    private const double Margin = 36;
    private const int RowsPerPage = 24;

    public void Export(string path, ReportPackDocument pack)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(pack);
        if (pack.Tables.Count == 0) throw new ArgumentException("A report pack requires at least one table.", nameof(pack));
        var pages = pack.Tables.SelectMany(table =>
        {
            var chunks = table.Data.Rows.Chunk(RowsPerPage).ToList();
            if (chunks.Count == 0) chunks.Add([]);
            return chunks.Select((rows, index) => new Page(table, rows, index + 1, chunks.Count));
        }).ToArray();

        var objects = new List<byte[]> { Array.Empty<byte>() };
        var fontId = Add(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var boldFontId = Add(objects, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>");
        var pageIds = new List<int>();
        var payloads = new List<(int PageId, int ContentId)>();
        for (var index = 0; index < pages.Length; index++)
        {
            var content = BuildPage(pack, pages[index], index + 1, pages.Length);
            var contentBytes = Encoding.ASCII.GetBytes(content);
            var contentId = Add(objects, $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream");
            var pageId = Add(objects, string.Empty);
            pageIds.Add(pageId); payloads.Add((pageId, contentId));
        }
        var pagesId = Add(objects, string.Empty);
        foreach (var page in payloads)
            objects[page.PageId] = Bytes($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {PageWidth:F2} {PageHeight:F2}] /Resources << /Font << /F1 {fontId} 0 R /F2 {boldFontId} 0 R >> >> /Contents {page.ContentId} 0 R >>");
        objects[pagesId] = Bytes($"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(' ', pageIds.Select(x => $"{x} 0 R"))}] >>");
        var catalogId = Add(objects, $"<< /Type /Catalog /Pages {pagesId} 0 R >>");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        Write(stream, "%PDF-1.4\n%ETPR\n");
        var offsets = new List<long> { 0 };
        for (var i = 1; i < objects.Count; i++) { offsets.Add(stream.Position); Write(stream, $"{i} 0 obj\n"); stream.Write(objects[i]); Write(stream, "\nendobj\n"); }
        var xref = stream.Position;
        Write(stream, $"xref\n0 {objects.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(stream, $"{offset:0000000000} 00000 n \n");
        Write(stream, $"trailer\n<< /Size {objects.Count} /Root {catalogId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static string BuildPage(ReportPackDocument pack, Page page, int pageNumber, int pageCount)
    {
        var data = page.Table.Data;
        var b = new StringBuilder();
        Text(b, "F2", 16, Margin, PageHeight - 42, $"{pack.Title} - {page.Table.Name}");
        Text(b, "F1", 9, Margin, PageHeight - 60, $"Period: {pack.DateFrom:dd MMM yyyy} to {pack.DateTo:dd MMM yyyy} | Status: {page.Table.Status} | Rule: {pack.RuleVersion}");
        Text(b, "F1", 8, Margin, PageHeight - 75, Clip(page.Table.Message, 150));
        var top = PageHeight - 98;
        var usable = PageWidth - 2 * Margin;
        var columnWidth = usable / data.Columns.Count;
        b.AppendLine("0.10 0.22 0.32 rg"); b.AppendLine($"{Margin:F2} {top - 20:F2} {usable:F2} 20 re f");
        for (var i = 0; i < data.Columns.Count; i++)
            Text(b, "F2", 7, Margin + i * columnWidth + 4, top - 14, Clip(data.Columns[i].Header, Math.Max(8, (int)(columnWidth / 4.8))));
        var y = top - 38;
        foreach (var row in page.Rows)
        {
            b.AppendLine("0.82 0.85 0.88 RG 0.4 w"); b.AppendLine($"{Margin:F2} {y - 5:F2} {usable:F2} 20 re S");
            for (var i = 0; i < data.Columns.Count; i++)
                Text(b, "F1", 7, Margin + i * columnWidth + 4, y + 1, Clip(Format(i < row.Count ? row[i] : null), Math.Max(8, (int)(columnWidth / 4.8))));
            y -= 20;
        }
        if (page.SectionPage == page.SectionPageCount && data.Totals is { Count: > 0 } totals)
        {
            b.AppendLine("0.92 0.94 0.96 rg"); b.AppendLine($"{Margin:F2} {y - 5:F2} {usable:F2} 20 re f");
            for (var i = 0; i < data.Columns.Count; i++)
                Text(b, "F2", 7, Margin + i * columnWidth + 4, y + 1, Clip(Format(i < totals.Count ? totals[i] : null), Math.Max(8, (int)(columnWidth / 4.8))));
        }
        Text(b, "F1", 7, Margin, 20, $"Generated {pack.GeneratedUtc:dd MMM yyyy HH:mm} UTC");
        Text(b, "F1", 7, PageWidth - 120, 20, $"Page {pageNumber} of {pageCount}");
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
    private sealed record Page(ReportPackTable Table, IReadOnlyList<IReadOnlyList<object?>> Rows, int SectionPage, int SectionPageCount);
}
