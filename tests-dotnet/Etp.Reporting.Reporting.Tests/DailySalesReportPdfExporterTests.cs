using Etp.Reporting.Reporting;
using PdfSharp.Pdf.IO;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>
/// 1.9.8, lane EXPORT-TESTS (RA-EXPORT-19): the DSR PDF exporter with sample data. 1.12 lane U4 (decision 45 #4): page 1 is
/// the A4 landscape summary, page 2 the evening matrix when the document has evening sheets, the last page the portrait
/// phone page; the title in the document info, and the exporter's own layout validation.
/// </summary>
public sealed class DailySalesReportPdfExporterTests
{
    [Fact]
    public void Sample_document_exports_the_summary_page_and_the_phone_page_with_the_title()
    {
        var path = OutputPath("dsr.pdf");
        try
        {
            var report = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), SalesFacts(), ServiceFacts(),
                new Dictionary<string, decimal?> { ["WLMHW"] = 2_900_000m, ["HEMW"] = 1_500_000m });
            Assert.Equal(2, report.Stores.Count);
            Assert.NotNull(report.CombinedFtd);

            new DailySalesReportPdfExporter().Export(path, report);

            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
            DailySalesReportPdfExporter.ValidateLayout(path);
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.Equal(2, pdf.PageCount); // no evening sheets: summary + phone page
            Assert.Equal(report.Title, pdf.Info.Title);
            Assert.Equal("ETP governed Daily Sales Report", pdf.Info.Subject);
            Assert.True(pdf.Pages[0].Contents.Elements.Count > 0);
            Assert.True(pdf.Pages[0].Width.Point > pdf.Pages[0].Height.Point);
            Assert.Equal(DailySalesReportPdfExporter.PhonePageWidth, pdf.Pages[1].Width.Point, 1);
            Assert.True(pdf.Pages[1].Height.Point >= DailySalesReportPdfExporter.PhonePageMinimumHeight);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Evening_sheets_add_the_matrix_as_page_two_after_the_designed_summary()
    {
        var path = OutputPath("dsr-evening.pdf");
        try
        {
            var report = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), SalesFacts(), ServiceFacts(), new Dictionary<string, decimal?>()) with
            {
                EveningSheets =
                [
                    new("WLMHW", "Titan World", 1_600_000m, 1_600_000m / 31, 661_803m, 661_803m / 7,
                        [new("VALUE", 34_215m, null, null, 938_197m, 2_143_453.75m, null, "currency"), new("VOL", 5m, null, null, 196m, 431m, null, "number")]),
                    new("HEMW", "Helios", 1_000_000m, 1_000_000m / 31, null, null,
                        [new("VALUE", 76_090m, 46_797m, 62.6m, 821_668m, 3_551_016m, 2_216_848m, "currency")])
                ]
            };

            new DailySalesReportPdfExporter().Export(path, report);

            DailySalesReportPdfExporter.ValidateLayout(path);
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.Equal(3, pdf.PageCount); // OUT-01: the card summary is page 1 even with evening sheets; the matrix is page 2
            Assert.Equal(report.Title, pdf.Info.Title);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Empty_document_exports_without_exception()
    {
        var path = OutputPath("dsr-empty.pdf");
        try
        {
            var report = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), [], [], new Dictionary<string, decimal?>());
            new DailySalesReportPdfExporter().Export(path, report);
            DailySalesReportPdfExporter.ValidateLayout(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Blank_path_and_missing_document_are_rejected()
    {
        var report = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), [], [], new Dictionary<string, decimal?>());
        Assert.Throws<ArgumentException>(() => new DailySalesReportPdfExporter().Export(" ", report));
        Assert.Throws<ArgumentNullException>(() => new DailySalesReportPdfExporter().Export(OutputPath("never.pdf"), null!));
    }

    [Fact]
    public void Validation_rejects_a_file_that_is_not_summary_matrix_and_phone_page()
    {
        var path = OutputPath("not-dsr.pdf");
        try
        {
            new SimplePdfReportExporter().Export(path,
                new("Long report", new(2026, 7, 1), new(2026, 8, 25), "Passed", "test", "x", DateTimeOffset.UtcNow),
                new([new("Item"), new("Units", "#,##0")], Enumerable.Range(1, 120).Select(i => (IReadOnlyList<object?>)[$"Item {i}", i]).ToArray()));
            Assert.Throws<InvalidDataException>(() => DailySalesReportPdfExporter.ValidateLayout(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Phone_page_prints_the_document_figures_unchanged()
    {
        // Same input, same figures: every number on the phone page is the document's own value in the DSR formats.
        var report = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), SalesFacts(), ServiceFacts(),
            new Dictionary<string, decimal?> { ["WLMHW"] = 2_900_000m, ["HEMW"] = 1_500_000m },
            stores: [new("WLMHW", "Titan World"), new("HEMW", "Helios")]);
        var text = string.Join("\n", DsrPhonePage.Lines(report, new DateTimeOffset(2026, 8, 26, 9, 14, 0, TimeSpan.FromHours(5.5))).Select(x => x.Text));

        Assert.Contains("Tuesday 25 Aug 2026", text, StringComparison.Ordinal);
        Assert.Contains("Titan World + Helios", text, StringComparison.Ordinal);
        Assert.Equal("₹1,45,970", DsrDisplay.Currency(report.CombinedFtd));
        Assert.Contains(DsrDisplay.Currency(report.CombinedFtd), text, StringComparison.Ordinal);
        Assert.Contains($"{DsrDisplay.Percent(report.CombinedFtdGrowth)} vs last year", text, StringComparison.Ordinal);
        Assert.Contains($"Bills {DsrDisplay.Number(report.CombinedInvoices, 0)}", text, StringComparison.Ordinal);
        Assert.Contains($"Month so far {DsrDisplay.CompactCurrency(report.MtdSales)}", text, StringComparison.Ordinal);
        Assert.Contains($"Year so far {DsrDisplay.CompactCurrency(report.YtdSales)}", text, StringComparison.Ordinal);
        foreach (var store in report.Stores)
        {
            Assert.Contains(store.DisplayName, text, StringComparison.Ordinal);
            Assert.Contains($"Today {DsrDisplay.Currency(store.Periods[0].TyValue)}", text, StringComparison.Ordinal);
            Assert.Contains($"Month {DsrDisplay.Currency(store.Periods[1].TyValue)}", text, StringComparison.Ordinal);
        }
        Assert.Contains($"Today {DsrDisplay.Currency(report.Service.Total)}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("INR", text, StringComparison.Ordinal);
        Assert.All(DsrPhonePage.Lines(report, DateTimeOffset.Now).Where(x => !x.IsDivider), line => Assert.True(line.Size >= 10, line.Text));
    }

    [Fact]
    public void Evening_matrix_spells_the_labels_and_prints_the_same_figures()
    {
        var value = new EveningMetricRow("VALUE", 34_215.4m, 46_797m, -26.9m, 938_197m, 2_143_453.75m, null, "currency");
        var cells = EveningDsrPdf.Cells(value);
        Assert.Equal(["Sales (₹)", "34,215", "46,797", "-26.9%", "9,38,197", "21,43,454", "—"], cells);
        // Whole rupees are the same figure rounded for print; nothing else changes.
        Assert.Equal(Math.Round(value.Ytd!.Value, MidpointRounding.AwayFromZero), decimal.Parse(cells[5], System.Globalization.NumberStyles.Number, OutputText.India));
        Assert.Equal(["Units", "5", "—", "+12.5%", "196", "431", "—"], EveningDsrPdf.Cells(new("VOL", 5m, null, 12.5m, 196m, 431m, null, "number")));
        Assert.Equal("Units per bill", EveningDsrPdf.Label("AUPT"));
        Assert.Equal("Walk-ins (retail)", EveningDsrPdf.Label("RETAIL WALKIN"));
        Assert.Equal("Titan", EveningDsrPdf.Label("Titan")); // a brand row keeps its name
        Assert.Equal("67.03 L", EveningDsrPdf.Compact(6_703_290m, "currency"));
        Assert.Equal("1.03 Cr", EveningDsrPdf.Compact(10_254_306m, "currency"));
        Assert.Equal(7, EveningDsrPdf.MinimumFontSize);
    }

    [Fact]
    public void The_same_document_exports_the_same_pages_twice()
    {
        var first = OutputPath("dsr-a.pdf"); var second = OutputPath("dsr-b.pdf");
        try
        {
            var report = DailySalesReportBuilder.Build(new DateOnly(2026, 8, 25), SalesFacts(), ServiceFacts(), new Dictionary<string, decimal?>());
            var at = new DateTimeOffset(2026, 8, 26, 9, 14, 0, TimeSpan.FromHours(5.5));
            new DailySalesReportPdfExporter().Export(first, report, at);
            new DailySalesReportPdfExporter().Export(second, report, at);
            using var a = PdfReader.Open(first, PdfDocumentOpenMode.Import);
            using var b = PdfReader.Open(second, PdfDocumentOpenMode.Import);
            Assert.Equal(a.PageCount, b.PageCount);
            Assert.Equal(a.Pages[a.PageCount - 1].Height.Point, b.Pages[b.PageCount - 1].Height.Point);
        }
        finally { foreach (var path in new[] { first, second }) if (File.Exists(path)) File.Delete(path); }
    }

    private static DsrPeriodFact[] SalesFacts() =>
    [
        new("FTD", "WLMHW", 69_880m, 22_647m, 8m, 6m, 8, 6, 1m, 8_735m, 10m, null),
        new("MTD", "WLMHW", 973_860m, 812_400m, 199m, 170m, 184, 160, 1.08m, 5_293m, null, null),
        new("YTD", "WLMHW", 6_703_290m, 4_890_060m, 1_325m, 1_116m, 1_250, 1_053, 1.06m, 5_363m, null, null),
        new("FTD", "HEMW", 76_090m, 46_797m, 3m, 3m, 3, 3, 1m, 25_363m, 4m, null),
        new("MTD", "HEMW", 821_668m, null, 40m, null, 37, null, 1.08m, 22_207m, null, null),
        new("YTD", "HEMW", 3_551_016m, 2_216_848m, 192m, 133m, 181, 125, 1.06m, 19_619m, null, null),
        new("FTD", "COMBINED", 145_970m, 69_444m, 11m, 9m, 11, 9, 1m, 13_270m, 14m, null),
        new("MTD", "COMBINED", 1_795_528m, null, 239m, null, 221, null, 1.08m, 8_124m, null, null),
        new("YTD", "COMBINED", 10_254_306m, 7_106_908m, 1_517m, 1_249m, 1_431, 1_178, 1.06m, 7_166m, null, null)
    ];

    private static DsrServiceFact[] ServiceFacts() =>
    [
        new("FTD", "WLMHW", 1_200m, 800m, 500m, 2_500m, 2_100m),
        new("MTD", "WLMHW", 31_000m, 20_000m, 12_000m, 63_000m, 58_000m),
        new("YTD", "WLMHW", 310_000m, 200_000m, 120_000m, 630_000m, 580_000m)
    ];

    private static string OutputPath(string name) =>
        Path.Combine(Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "dsr-pdf-tests")).FullName, $"{Guid.NewGuid():N}-{name}");
}
