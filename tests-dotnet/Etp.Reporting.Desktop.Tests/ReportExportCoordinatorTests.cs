using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class ReportExportCoordinatorTests
{
    [Fact]
    public void Format_selection_preserves_visual_and_dsr_precedence()
    {
        var metadata = Metadata();
        var data = Data();
        var visual = VisualReportComposer.Compose(metadata, data);
        var dsr = DailySalesReportBuilder.Build(new(2026, 8, 25), [], [], new Dictionary<string, decimal?>());

        Assert.Equal(ReportExcelExportRoute.Tabular, ReportExportCoordinator.SelectExcelRoute(null));
        Assert.Equal(ReportExcelExportRoute.Visual, ReportExportCoordinator.SelectExcelRoute(visual));
        Assert.Equal(ReportPdfExportRoute.Tabular, ReportExportCoordinator.SelectPdfRoute(null, null));
        Assert.Equal(ReportPdfExportRoute.Visual, ReportExportCoordinator.SelectPdfRoute(null, visual));
        Assert.Equal(ReportPdfExportRoute.DailySalesReport, ReportExportCoordinator.SelectPdfRoute(dsr, visual));
    }

    [Fact]
    public void Management_summary_uses_the_production_pdf_export_path()
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-management-{Guid.NewGuid():N}.pdf");
        try
        {
            new ReportExportCoordinator().ExportManagementSummaryPdf(path, Metadata(), Data());

            Assert.True(File.Exists(path));
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static ExcelReportMetadata Metadata() => new(
        "ETP Management Summary",
        new(2026, 8, 1),
        new(2026, 8, 25),
        "Operational",
        "v1",
        "Aggregate operational evidence only.",
        new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    private static ExcelReportData Data() => new(
        [new("Report"), new("Files", "#,##0"), new("Rows", "#,##0")],
        [["R025", 2, 190]],
        ["Total", 2, 190]);
}
