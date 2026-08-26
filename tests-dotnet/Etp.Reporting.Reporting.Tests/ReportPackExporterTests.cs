using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class ReportPackExporterTests
{
    private static ReportPackDocument Pack() => new(
        "ETP Daily Reporting Pack",
        new(2026, 8, 25),
        new(2026, 8, 25),
        "Failed",
        "v-test",
        "Visible source variance retained.",
        DateTimeOffset.UtcNow,
        [
            new("Invoice Summary", "Passed", "Canonical R025 totals.",
                new([new("Store"),new("Net Value","#,##0.00")], [["WLMHW",69_880m]], ["Total",69_880m])),
            new("Daily Exceptions", "Failed", "Exact tender variance.",
                new([new("Code"),new("Variance","#,##0.00")], [["TENDER_VARIANCE",2m]]))
        ]);

    [Fact]
    public void Excel_pack_contains_one_filterable_formula_free_sheet_per_report()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-pack-{Guid.NewGuid():N}.xlsx");
        try
        {
            new OpenXmlReportPackExporter().Export(path, Pack());
            using var document = SpreadsheetDocument.Open(path, false);
            Assert.Equal(["Invoice Summary", "Daily Exceptions"], document.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().Select(x => x.Name!.Value));
            Assert.All(document.WorkbookPart.WorksheetParts, part =>
            {
                Assert.NotNull(part.Worksheet.GetFirstChild<AutoFilter>());
                Assert.Empty(part.Worksheet.Descendants<CellFormula>());
            });
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Pdf_pack_contains_all_report_sections_and_global_pagination()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-pack-{Guid.NewGuid():N}.pdf");
        try
        {
            new SimplePdfReportPackExporter().Export(path, Pack());
            var text = Encoding.ASCII.GetString(File.ReadAllBytes(path));
            Assert.Contains("Invoice Summary", text, StringComparison.Ordinal);
            Assert.Contains("Daily Exceptions", text, StringComparison.Ordinal);
            Assert.Contains("Page 1 of 2", text, StringComparison.Ordinal);
            Assert.Contains("Page 2 of 2", text, StringComparison.Ordinal);
            Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Archived_pack_round_trip_preserves_controls_and_exportable_values()
    {
        var original = Pack();
        var json = ReportPackArchiveCodec.Serialize(original);
        var restored = ReportPackArchiveCodec.Deserialize(json);

        Assert.Equal(original.Title, restored.Title);
        Assert.Equal(original.DateFrom, restored.DateFrom);
        Assert.Equal(original.Tables.Select(x => x.Name), restored.Tables.Select(x => x.Name));
        Assert.Equal(69_880L, restored.Tables[0].Data.Rows[0][1]);
        Assert.Equal(2L, restored.Tables[1].Data.Rows[0][1]);
        Assert.Throws<ArgumentException>(() => ReportPackArchiveCodec.Deserialize(" "));
    }
}
