using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Reporting;

public sealed record ExcelReportColumn(string Header, string NumberFormat = "General");
public sealed record ExcelReportMetadata(string ReportName, DateOnly DateFrom, DateOnly DateTo,
    string Status, string RuleVersion, string Message, DateTimeOffset GeneratedUtc);
public sealed record ExcelReportData(IReadOnlyList<ExcelReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<object?>? Totals = null);

public sealed class OpenXmlReportExporter
{
    public void Export(string filePath, ExcelReportMetadata metadata, ExcelReportData report)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath); ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(report);
        if (report.Columns.Count == 0 || report.Rows.Any(x => x.Count != report.Columns.Count) || report.Totals?.Count != report.Columns.Count)
            throw new ArgumentException("Report rows and totals must match the declared columns.", nameof(report));
        using var document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart(); workbookPart.Workbook = new Workbook();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>(); stylesPart.Stylesheet = Styles();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData(); var worksheet = new Worksheet(sheetData); worksheetPart.Worksheet = worksheet;
        var lastColumn = ColumnName(report.Columns.Count);
        sheetData.Append(Row(1, [Text(metadata.ReportName, 1)]));
        var merges = new MergeCells(new MergeCell { Reference = $"A1:{lastColumn}1" });
        if (report.Columns.Count > 1) merges.Append(new MergeCell { Reference = $"B6:{lastColumn}6" });
        worksheet.Append(merges);
        sheetData.Append(Row(3, [Text("Period", 2), Text($"{metadata.DateFrom:yyyy-MM-dd} to {metadata.DateTo:yyyy-MM-dd}", 0)]));
        sheetData.Append(Row(4, [Text("Generated UTC", 2), Text(metadata.GeneratedUtc.ToString("u", CultureInfo.InvariantCulture), 0)]));
        sheetData.Append(Row(5, [Text("Status", 2), Text(metadata.Status, 0), Text("Rule version", 2), Text(metadata.RuleVersion, 0)]));
        sheetData.Append(Row(6, [Text("Control", 2), Text(metadata.Message, 0)]));
        const uint headerRow = 8;
        sheetData.Append(Row(headerRow, report.Columns.Select(x => Text(x.Header, 3))));
        uint rowIndex = headerRow + 1;
        foreach (var values in report.Rows) sheetData.Append(Row(rowIndex++, values.Select((value, index) => Value(value, FormatStyle(report.Columns[index].NumberFormat)))));
        if (report.Totals is not null) sheetData.Append(Row(rowIndex++, report.Totals.Select((value, index) => Value(value, index == 0 ? 5u : FormatStyle(report.Columns[index].NumberFormat, true)))));
        worksheet.InsertAt(new SheetViews(new SheetView(new Pane { VerticalSplit = 8, TopLeftCell = "A9", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }), 0);
        worksheet.InsertAt(new Columns(report.Columns.Select((x, index) => new Column { Min = (uint)index + 1, Max = (uint)index + 1, Width = Math.Max(Width(x.Header, x.NumberFormat), index == report.Columns.Count - 1 ? 28 : 0), CustomWidth = true })), 1);
        worksheet.InsertBefore(new AutoFilter { Reference = $"A{headerRow}:{lastColumn}{Math.Max(headerRow, rowIndex - 1)}" }, worksheet.GetFirstChild<MergeCells>());
        workbookPart.Workbook.AppendChild(new Sheets(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Report" }));
        workbookPart.Workbook.Save();
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
        var row = new Row { RowIndex = index }; var column = 1;
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
    private static double Width(string header, string format) => Math.Clamp(Math.Max(header.Length + 3, format == "#,##0.00" ? 16 : 12), 12, 42);
    private static string ColumnName(int count) { var value = count; var result = string.Empty; while (value > 0) { value--; result = (char)('A' + value % 26) + result; value /= 26; } return result; }
}
