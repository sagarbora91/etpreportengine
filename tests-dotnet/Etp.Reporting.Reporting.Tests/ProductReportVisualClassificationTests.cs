using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class ProductReportVisualClassificationTests
{
    [Fact]
    public void Production_catalogue_and_visual_classifications_match_bidirectionally()
    {
        var productionCodes = ProductReportCatalogue.All
            .Select(report => report.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var classifiedCodes = ProductReportVisualClassificationRegistry.All
            .Select(entry => entry.ReportCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Empty(productionCodes.Except(classifiedCodes, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(classifiedCodes.Except(productionCodes, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(ProductReportCatalogue.All.Count, ProductReportVisualClassificationRegistry.All.Count);
        Assert.Equal(
            ProductReportVisualClassificationRegistry.All.Count,
            ProductReportVisualClassificationRegistry.All.Select(entry => entry.ReportCode).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData("dsr", ProductReportVisualClass.ExecutiveVisual)]
    [InlineData("sales-titan", ProductReportVisualClass.KpiTable)]
    [InlineData("sales-brand", ProductReportVisualClass.KpiChartTable)]
    [InlineData("tender-diagnostic", ProductReportVisualClass.ExceptionDiagnostic)]
    [InlineData("invoice-lineage", ProductReportVisualClass.TableOnly)]
    public void Registry_returns_the_approved_classification_case_insensitively(
        string reportCode,
        ProductReportVisualClass expected)
    {
        Assert.Equal(expected, ProductReportVisualClassificationRegistry.ForReport(reportCode.ToUpperInvariant()).Classification);
    }

    [Fact]
    public void Unknown_report_codes_fail_closed()
    {
        Assert.Null(ProductReportVisualClassificationRegistry.Find("not-a-production-report"));
        Assert.Throws<KeyNotFoundException>(() => ProductReportVisualClassificationRegistry.ForReport("not-a-production-report"));
    }

    [Fact]
    public void Representative_visual_definition_set_remains_the_existing_seven()
    {
        Assert.Equal(7, VisualReportRegistry.All.Count);
        Assert.Equal(
            ["RPT-SALES-001", "RPT-SALES-002", "RPT-STOCK-001", "RPT-STAFF-001", "RPT-TENDER-001", "RPT-MGMT-001", "RPT-EXCEPTION-001"],
            VisualReportRegistry.All.Select(definition => definition.ReportId).ToArray());
    }
}
