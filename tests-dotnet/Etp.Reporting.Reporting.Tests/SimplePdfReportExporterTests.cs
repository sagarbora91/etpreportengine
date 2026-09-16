using PdfSharp.Pdf.IO;
using System.Text;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class SimplePdfReportExporterTests
{
    [Fact]
    public void Export_creates_paginated_pdf_with_metadata_headers_and_totals()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-report-{Guid.NewGuid():N}.pdf");
        try
        {
            var rows = Enumerable.Range(1, 30)
                .Select(i => (IReadOnlyList<object?>)[new DateOnly(2026, 7, Math.Min(i, 28)), i, i * 100m])
                .ToArray();
            new SimplePdfReportExporter().Export(path,
                new("Daily Sales", new(2026, 7, 1), new(2026, 8, 25), "Passed", "v1", "Control passed.", DateTimeOffset.UtcNow),
                new([new("Date"), new("Units"), new("Net Sales")], rows, ["Total", 465m, 46_500m]));

            var bytes = File.ReadAllBytes(path);
            var text = Encoding.ASCII.GetString(bytes);
            Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
            using var document=PdfReader.Open(path,PdfDocumentOpenMode.Import);
            Assert.Equal(2,document.PageCount);
            Assert.All(document.Pages.Cast<PdfSharp.Pdf.PdfPage>(),page=>Assert.True(page.Width.Point>page.Height.Point));
            Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
