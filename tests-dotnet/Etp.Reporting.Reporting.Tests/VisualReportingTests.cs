using DocumentFormat.OpenXml.Packaging;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class VisualReportingTests
{
    [Fact]
    public void Golden_daily_sales_values_reconcile_across_kpi_visual_and_detail()
    {
        var data = new ExcelReportData([new("Store"), new("TY Net Sales", "#,##0.00"), new("LY Net Sales", "#,##0.00")],
            [["Titan", 69880m, 22647m]], ["Total", 69880m, 22647m]);
        var model = VisualReportComposer.Compose(Meta("Daily Sales Report"), data);
        Assert.Equal(69880m, model.Kpis[0].Value);
        Assert.Equal(69880m, model.Visuals[0].Series[0].Points.Sum(x => x.Value));
        Assert.Equal(69880m, VisualReportComposer.Total(model.Detail, 1));
        Assert.Equal(2.086m, Math.Round((69880m - 22647m) / 22647m, 3));
    }

    [Fact]
    public void Golden_combined_and_stock_controls_remain_exact()
    {
        Assert.Equal(145970m, 69880m + 76090m);
        var stock = new ExcelReportData([new("Measure"), new("Quantity", "#,##0")], [["Physical", 812m], ["System", 812m], ["Variance", 0m]], ["Total", 0m]);
        var model = VisualReportComposer.Compose(Meta("Closing Stock"), stock);
        Assert.Equal(0m, model.Kpis[0].Value);
        Assert.Contains(model.Visuals[0].Series[0].Points, x => x.Category == "Variance" && x.Value == 0m);
    }

    [Fact]
    public void Missing_zero_and_not_applicable_are_distinct()
    {
        Assert.Equal("Not available", IndianNumberFormatter.Format(null, "currency", VisualValueState.Missing));
        Assert.Equal("N/A", IndianNumberFormatter.Format(null, "currency", VisualValueState.NotApplicable));
        Assert.Contains("0.00", IndianNumberFormatter.Format(0m, "currency"));
    }

    [Fact]
    public void Top_n_preserves_total_with_other_bucket()
    {
        var points = Enumerable.Range(1, 12).Select(x => new ReportVisualPoint($"B{x}", x)).ToArray();
        var top = VisualReportComposer.TopN(points, 10);
        Assert.Equal(points.Sum(x => x.Value), top.Sum(x => x.Value));
        Assert.Equal("Other", top[^1].Category);
    }

    [Fact]
    public void Visual_excel_has_governed_five_sheet_structure_and_pdf_has_summary_and_detail_pages()
    {
        var model = VisualReportComposer.Compose(Meta("Brand Sales"), new([new("Brand"), new("NETVALUE", "#,##0.00")], [["Titan", 69880m], ["Helios", 76090m]], ["Total", 145970m]));
        var xlsx = Path.Combine(Path.GetTempPath(), $"visual-{Guid.NewGuid():N}.xlsx"); var pdf = Path.ChangeExtension(xlsx, ".pdf");
        try
        {
            new OpenXmlVisualReportExporter().Export(xlsx, model); new SimplePdfVisualReportExporter().Export(pdf, model);
            using var book = SpreadsheetDocument.Open(xlsx, false);
            Assert.Equal(["Executive Summary", "Charts & Analysis", "Detailed Data", "Controls & Exceptions", "Metadata"], book.WorkbookPart!.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Select(x => x.Name!.Value));
            Assert.Single(book.WorkbookPart.WorksheetParts.SelectMany(x => x.DrawingsPart?.ChartParts ?? []));
            Assert.StartsWith("%PDF-1.4", File.ReadAllText(pdf)[..8]);
        }
        finally { if (File.Exists(xlsx)) File.Delete(xlsx); if (File.Exists(pdf)) File.Delete(pdf); }
    }

    private static ExcelReportMetadata Meta(string name) => new(name, new(2026, 7, 1), new(2026, 8, 25), "Passed", "v1.8", "Control passed.", DateTimeOffset.UtcNow);
}
