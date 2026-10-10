using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>
/// 1.9.8, lane EXPORT-TESTS (RA-EXPORT-14/19): what every Excel export writes. Headers in row 8, every row below, Indian
/// lakh formats for money (2 decimals) and counts (none), percent columns, real dates, "—" for blank cells, the applied
/// scope as a merged row 7, sheet names Excel accepts.
/// </summary>
public sealed class ReportWorksheetWriterTests
{
    // The lakh money format the writer registers as numFmt 164 (ReportWorksheetWriter.IndianMoney).
    private const string IndianMoney = "[>=10000000]##\\,##\\,##\\,##0.00;[>=100000]##\\,##\\,##0.00;##,##0.00";
    private static readonly ExcelReportColumn[] Columns =
    [
        new("Store"), new("Units", "#,##0"), new("Net Sales", "#,##0.00"), new("Quantity", "#,##0.00"), new("Growth %", "0.00%"), new("Date"), new("Stamp")
    ];

    [Fact]
    public void Headers_rows_totals_and_cell_styles_follow_the_column_formats()
    {
        var path = OutputPath("worksheet.xlsx");
        try
        {
            var rows = Enumerable.Range(1, 50).Select(i => (IReadOnlyList<object?>)
                ["WLMHW", i, i * 1234567.891m, i * 0.5m, i == 2 ? null : 12.5m, new DateOnly(2026, 7, Math.Min(i, 28)), new DateTime(2026, 7, 1, 9, 30, 0)]).ToArray();
            rows[0] = ["WLMHW", null, null, null, null, null, null];
            new OpenXmlReportExporter().Export(path,
                new("Brand Sales", new(2026, 7, 1), new(2026, 7, 28), "Passed", "v1", "Control passed.", new DateTimeOffset(2026, 8, 1, 6, 0, 0, TimeSpan.Zero), "Stores: WLMHW"),
                new(Columns, rows, ["Total", 1275, 123m, 637.5m, null, null, null]));

            using var document = SpreadsheetDocument.Open(path, false);
            var book = document.WorkbookPart!;
            var worksheet = book.WorksheetParts.Single().Worksheet;
            var byRow = worksheet.Descendants<Row>().ToDictionary(row => row.RowIndex!.Value, row => row.Elements<Cell>().ToArray());
            var formats = book.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<CellFormat>().ToArray();
            var numbering = book.WorkbookStylesPart.Stylesheet.NumberingFormats!.Elements<NumberingFormat>().ToDictionary(x => x.NumberFormatId!.Value, x => x.FormatCode!.Value);
            string FormatOf(Cell cell) => formats[(int)(cell.StyleIndex?.Value ?? 0)].NumberFormatId?.Value is { } id && numbering.TryGetValue(id, out var code) ? code! : "General";

            Assert.Equal("Brand Sales", byRow[1][0].InnerText);
            Assert.Equal("Stores: WLMHW", byRow[7][0].InnerText);
            Assert.Equal("A7:G7", worksheet.Descendants<MergeCell>().Single().Reference!.Value);
            Assert.Equal(Columns.Select(column => column.Header), byRow[8].Select(cell => cell.InnerText));
            Assert.Equal(Enumerable.Range(9, 51).Select(i => (uint)i), byRow.Keys.Where(index => index > 8).Order());
            Assert.Equal("A8:G58", worksheet.GetFirstChild<AutoFilter>()!.Reference!.Value);

            var second = byRow[10];
            Assert.Equal(CellValues.InlineString, second[0].DataType!.Value); Assert.Equal("WLMHW", second[0].InnerText);
            Assert.Equal(CellValues.Number, second[1].DataType!.Value); Assert.Equal("2", second[1].CellValue!.Text);
            Assert.Equal("[>=10000000]##\\,##\\,##\\,##0;[>=100000]##\\,##\\,##0;##,##0", FormatOf(second[1]));
            Assert.Equal("2469135.782", second[2].CellValue!.Text);
            Assert.Equal(IndianMoney, FormatOf(second[2]));
            Assert.Equal(IndianMoney, FormatOf(second[3]));
            Assert.Equal("—", second[4].InnerText);
            Assert.Equal(new DateTime(2026, 7, 2).ToOADate().ToString(System.Globalization.CultureInfo.InvariantCulture), second[5].CellValue!.Text);
            Assert.Equal("dd mmm yyyy", FormatOf(second[5]));
            Assert.Equal(CellValues.Number, second[6].DataType!.Value);

            var third = byRow[11];
            Assert.Equal("12.5", third[4].CellValue!.Text);
            Assert.Equal("0.00\"%\"", FormatOf(third[4]));

            var blank = byRow[9];
            Assert.All(blank.Skip(1), cell => Assert.Equal("—", cell.InnerText));
            Assert.All(blank.Skip(1), cell => Assert.Equal(CellValues.InlineString, cell.DataType!.Value));

            var totals = byRow[59];
            Assert.Equal("Total", totals[0].InnerText);
            Assert.Equal("1275", totals[1].CellValue!.Text);
            Assert.Equal(IndianMoney, FormatOf(totals[2]));
            Assert.Equal("—", totals[4].InnerText);
            Assert.Empty(worksheet.Descendants<CellFormula>());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Money_uses_two_decimal_lakh_grouping_and_counts_use_none()
    {
        var path = OutputPath("formats.xlsx");
        try
        {
            new OpenXmlReportExporter().Export(path,
                new("Formats", new(2026, 7, 1), new(2026, 7, 1), "Passed", "v1", "x", DateTimeOffset.UtcNow),
                new([new("Items", "#,##0"), new("Value incl. GST", "#,##0.00"), new("Plain")], [[1234567, 1234567.5m, 1234567.5m]]));
            using var document = SpreadsheetDocument.Open(path, false);
            var book = document.WorkbookPart!;
            var cells = book.WorksheetParts.Single().Worksheet.Descendants<Row>().Single(row => row.RowIndex!.Value == 9).Elements<Cell>().ToArray();
            var formats = book.WorkbookStylesPart!.Stylesheet.CellFormats!.Elements<CellFormat>().ToArray();
            uint NumberFormat(Cell cell) => formats[(int)cell.StyleIndex!.Value].NumberFormatId!.Value;
            Assert.Equal(167u, NumberFormat(cells[0]));
            Assert.Equal(164u, NumberFormat(cells[1]));
            // A number under a "General" column is still written as a number in the money style, never as text.
            Assert.Equal(CellValues.Number, cells[2].DataType!.Value);
            Assert.Equal(164u, NumberFormat(cells[2]));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("Slow / Exception Stock", "Slow  Exception Stock")]
    [InlineData("Invoice Sales source history and lineage", "Invoice Sales source hist")]
    [InlineData("[]:*?/\\", "Report")]
    public void Sheet_name_drops_characters_excel_rejects_and_fits_31_characters(string reportName, string expected)
    {
        var path = OutputPath("sheet.xlsx");
        try
        {
            new OpenXmlReportExporter().Export(path,
                new(reportName, new(2026, 7, 1), new(2026, 7, 1), "Passed", "v1", "x", DateTimeOffset.UtcNow),
                new([new("Store")], [["WLMHW"]]));
            using var document = SpreadsheetDocument.Open(path, false);
            Assert.Equal(expected, document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Single().Name!.Value);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Rows_and_totals_must_match_the_columns()
    {
        var path = OutputPath("mismatch.xlsx");
        var metadata = new ExcelReportMetadata("Mismatch", new(2026, 7, 1), new(2026, 7, 1), "Passed", "v1", "x", DateTimeOffset.UtcNow);
        try
        {
            Assert.Throws<ArgumentException>(() => new OpenXmlReportExporter().Export(path, metadata, new([new("Store"), new("Units")], [["WLMHW"]])));
            Assert.Throws<ArgumentException>(() => new OpenXmlReportExporter().Export(path, metadata, new([new("Store")], [["WLMHW"]], ["Total", 1])));
            Assert.Throws<ArgumentException>(() => new OpenXmlReportExporter().Export(path, metadata, new([], [])));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static string OutputPath(string name) =>
        Path.Combine(Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "worksheet-writer-tests")).FullName, $"{Guid.NewGuid():N}-{name}");
}
