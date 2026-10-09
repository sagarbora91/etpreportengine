using System.Text;
using Etp.Reporting.Reporting;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>
/// 1.9.8, lane EXPORT-TESTS (RA-EXPORT-19): the tabular PDF paginates a wide grid into column sections and a long grid
/// across pages without exception, every page carries the report title and the date window, blanks print as "—",
/// money in Indian grouping, dates as dd MMM yyyy.
/// </summary>
public sealed class VisualReportPdfDocumentTests
{
    [Fact]
    public void Wide_grid_splits_into_column_sections_each_with_the_title_and_window()
    {
        var path = OutputPath("wide.pdf");
        try
        {
            var columns = Enumerable.Range(1, 40).Select(i => new ExcelReportColumn($"Measure {i}", i % 2 == 0 ? "#,##0.00" : "General")).ToArray();
            var rows = Enumerable.Range(1, 5).Select(r => (IReadOnlyList<object?>)Enumerable.Range(1, 40)
                .Select(c => c % 2 == 0 ? (object?)(r * c * 1000.25m) : $"Text {r}-{c}").ToArray()).ToArray();
            var model = VisualReportComposer.Compose(
                new("Wide grid report", new(2026, 7, 1), new(2026, 8, 25), "Passed", "test", "Synthetic only", DateTimeOffset.UtcNow, "Stores: WLMHW, HEMW"),
                new(columns, rows, Enumerable.Range(1, 40).Select(c => c == 1 ? "Total" : c % 2 == 0 ? (object?)15m * c * 1000.25m : "").ToArray()));

            new SimplePdfVisualReportExporter().Export(path, model);

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.Equal("Wide grid report", pdf.Info.Title);
            Assert.True(pdf.PageCount >= 3, $"Forty measured columns need several column sections; got {pdf.PageCount} page(s).");
            var sections = new HashSet<string>();
            for (var i = 0; i < pdf.PageCount; i++)
            {
                var page = pdf.Pages[i];
                Assert.InRange(page.Width.Point, 841, 843);
                Assert.InRange(page.Height.Point, 594, 596);
                var text = PageText(page);
                Assert.Contains("Wide grid report", text, StringComparison.Ordinal);
                Assert.Contains("2026-07-01 to 2026-08-25", text, StringComparison.Ordinal);
                Assert.Contains("Stores: WLMHW, HEMW", text, StringComparison.Ordinal);
                Assert.Contains($"Page {i + 1} of {pdf.PageCount}", text, StringComparison.Ordinal);
                var start = text.IndexOf("Detailed Data - section", StringComparison.Ordinal);
                Assert.True(start >= 0, $"Page {i + 1} names no column section.");
                sections.Add(text.Substring(start, 32));
            }
            Assert.Equal(pdf.PageCount, sections.Count);
            Assert.Contains("Measure 40", PageText(pdf.Pages[pdf.PageCount - 1]), StringComparison.Ordinal);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Long_grid_paginates_with_the_header_repeated_and_the_total_last()
    {
        var path = OutputPath("long.pdf");
        try
        {
            var rows = Enumerable.Range(1, 400).Select(i => (IReadOnlyList<object?>)[new DateOnly(2026, 1, 1).AddDays(i % 365), $"Item {i}", i, i * 118m]).ToArray();
            var model = VisualReportComposer.Compose(
                new("Long grid report", new(2026, 1, 1), new(2026, 12, 31), "Passed", "test", "Synthetic only", DateTimeOffset.UtcNow),
                new([new("Date"), new("Item"), new("Units", "#,##0"), new("Net Sales", "#,##0.00")], rows, ["Total", "", 80200, 9463600m]));

            new SimplePdfVisualReportExporter().Export(path, model);

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.True(pdf.PageCount >= 8, $"Four hundred rows need many pages; got {pdf.PageCount}.");
            for (var i = 0; i < pdf.PageCount; i++)
            {
                var text = PageText(pdf.Pages[i]);
                Assert.Contains("Long grid report", text, StringComparison.Ordinal);
                Assert.Contains("2026-01-01 to 2026-12-31", text, StringComparison.Ordinal);
                Assert.Contains("Net Sales", text, StringComparison.Ordinal);
                Assert.Contains("Detailed Data - section 1 of 1", text, StringComparison.Ordinal);
            }
            var last = PageText(pdf.Pages[pdf.PageCount - 1]);
            Assert.Contains("Total", last, StringComparison.Ordinal);
            Assert.Contains("94,63,600.00", last, StringComparison.Ordinal);
            Assert.Contains("Item 400", last, StringComparison.Ordinal);
            Assert.Contains("Item 1", PageText(pdf.Pages[0]), StringComparison.Ordinal);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Cells_print_blanks_as_a_dash_money_in_indian_grouping_and_dates_as_day_month_year()
    {
        var path = OutputPath("cells.pdf");
        try
        {
            var model = VisualReportComposer.Compose(
                new("Cell formats", new(2026, 7, 1), new(2026, 7, 1), "Passed", "test", "Synthetic only", DateTimeOffset.UtcNow),
                new([new("Date"), new("Net Sales", "#,##0.00"), new("Growth %", "0.00%"), new("Note")],
                    [[new DateOnly(2026, 7, 1), 1234567.891m, 12.5m, null], [null, null, null, "Blank row"]]));

            new SimplePdfVisualReportExporter().Export(path, model);

            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var text = PageText(pdf.Pages[0]);
            Assert.Contains("01 Jul 2026", text, StringComparison.Ordinal);
            Assert.Contains("12,34,567.89", text, StringComparison.Ordinal);
            Assert.Contains("12.50%", text, StringComparison.Ordinal);
            Assert.Contains("Blank row", text, StringComparison.Ordinal);
            Assert.Contains("—", text, StringComparison.Ordinal);
            Assert.Contains("Passed: Synthetic only", text, StringComparison.Ordinal);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void A_report_without_rows_still_exports_a_page_that_says_so()
    {
        var path = OutputPath("empty.pdf");
        try
        {
            new SimplePdfReportExporter().Export(path,
                new("Empty report", new(2026, 7, 1), new(2026, 7, 1), "Blocked", "test", "No data", DateTimeOffset.UtcNow),
                new([new("Store"), new("Units")], []));
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.Equal(1, pdf.PageCount);
            Assert.Contains("No rows for the selected scope.", PageText(pdf.Pages[0]), StringComparison.Ordinal);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Models_without_columns_are_rejected_before_any_file_is_written()
    {
        var path = OutputPath("rejected.pdf");
        var metadata = new ExcelReportMetadata("No columns", new(2026, 7, 1), new(2026, 7, 1), "Passed", "test", "x", DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => new SimplePdfReportExporter().Export(path, metadata, new([], [])));
        Assert.Throws<ArgumentException>(() => new SimplePdfReportPackExporter().Export(path,
            new("Pack", new(2026, 7, 1), new(2026, 7, 1), "Passed", "test", "x", DateTimeOffset.UtcNow, [])));
        Assert.False(File.Exists(path));
    }

    /// <summary>The literal text of a page's content streams: PDFsharp writes text operands as (…) strings.</summary>
    internal static string PageText(PdfPage page)
    {
        var text = new StringBuilder();
        for (var i = 0; i < page.Contents.Elements.Count; i++)
        {
            var stream = page.Contents.Elements.GetDictionary(i)!.Stream!;
            text.Append(Encoding.Latin1.GetString(stream.UnfilteredValue));
        }
        return PdfLiterals(text.ToString());
    }

    private static string PdfLiterals(string content)
    {
        // Concatenate every (literal) operand, unescaping \( \) \\ and \ddd, so assertions read like the page.
        var result = new StringBuilder();
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] != '(') continue;
            var depth = 1; i++;
            for (; i < content.Length && depth > 0; i++)
            {
                var c = content[i];
                if (c == '\\' && i + 1 < content.Length)
                {
                    var next = content[++i];
                    if (char.IsDigit(next))
                    {
                        var code = 0; var digits = 0;
                        while (digits < 3 && i < content.Length && char.IsDigit(content[i])) { code = code * 8 + (content[i] - '0'); i++; digits++; }
                        i--; result.Append(WinAnsi((byte)code));
                    }
                    else result.Append(next == 'n' ? '\n' : next == 'r' ? '\r' : next == 't' ? '\t' : next);
                }
                else if (c == '(') { depth++; result.Append(c); }
                else if (c == ')') { if (--depth > 0) result.Append(c); }
                else result.Append(WinAnsi((byte)c));
            }
            i--; result.Append(' ');
        }
        return result.ToString();
    }

    private static char WinAnsi(byte value) => value == 0x97 ? '—' : (char)value;

    private static string OutputPath(string name) =>
        Path.Combine(Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "visual-pdf-tests")).FullName, $"{Guid.NewGuid():N}-{name}");
}
