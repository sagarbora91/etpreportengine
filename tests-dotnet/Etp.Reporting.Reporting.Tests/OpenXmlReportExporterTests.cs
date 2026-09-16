using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class OpenXmlReportExporterTests
{
    [Fact]
    public void Export_contains_fixed_metadata_grid_totals_filter_and_no_formulas()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-report-{Guid.NewGuid():N}.xlsx");
        try
        {
            new OpenXmlReportExporter().Export(path,
                new("Daily Sales", new(2026, 7, 1), new(2026, 8, 25), "Passed", "v1", "Control passed.", DateTimeOffset.UtcNow),
                new([new("Date"), new("Units", "#,##0.00"), new("Net Sales", "#,##0.00")],
                    [["2026-07-01", 2m, 236m], ["2026-07-02", -1m, -118m]], ["Total", 1m, 118m]));
            using var document = SpreadsheetDocument.Open(path, false);
            var worksheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet;
            Assert.Contains(worksheet.Descendants<Cell>(), x=>x.CellReference=="A1" && x.InnerText=="Daily Sales");
            Assert.Equal("A8:C10", worksheet.GetFirstChild<AutoFilter>()!.Reference!.Value);
            Assert.Equal(PaneStateValues.Frozen, worksheet.SheetViews!.Elements<SheetView>().Single().Pane!.State!.Value);
            Assert.Empty(worksheet.Descendants<CellFormula>());
            Assert.Contains(worksheet.Descendants<InlineString>(), x => x.InnerText == "Control passed.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
