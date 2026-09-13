using System.Globalization;
using Etp.Reporting.Reporting;
using PdfSharp.Pdf.IO;

namespace Etp.Reporting.Reporting.Tests;

public sealed class VisualPdfExportRegressionTests
{
    [Theory]
    [InlineData("en-IN")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public void Wide_reports_paginate_as_readable_column_sections_in_all_supported_numeric_cultures(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var path = Path.Combine(Path.GetTempPath(), $"etp-wide-{Guid.NewGuid():N}.pdf");
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var columns = Enumerable.Range(1, 13).Select(i => new ExcelReportColumn($"Column {i}")).ToArray();
            var rows = Enumerable.Range(1, 60).Select(i => (IReadOnlyList<object?>)Enumerable.Range(1, 13)
                .Select(c => c == 1 ? (object)$"Invoice {i}" : c == 2 ? -12345678901234.5678m : c == 3 ? null : (object)$"Complete value {i}/{c}").ToArray()).ToArray();
            var model = VisualReportComposer.Compose(new("Wide report", new(2026, 8, 25), new(2026, 8, 25), "Passed", "test", "Synthetic only", DateTimeOffset.UtcNow),
                new(columns, rows, ["Total", -12345678901234.5678m]));
            new SimplePdfVisualReportExporter().Export(path, model);
            using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            Assert.True(pdf.PageCount >= 9, "Thirteen columns and sixty rows must span readable column sections and vertical pages.");
            foreach (var page in pdf.Pages)
            {
                Assert.InRange(page.Width.Point, 841, 843);
                Assert.InRange(page.Height.Point, 594, 596);
                Assert.True(page.Contents.Elements.Count > 0);
            }
        }
        finally { CultureInfo.CurrentCulture = previous; if (File.Exists(path)) File.Delete(path); }
    }
}
