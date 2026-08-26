using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class ReportCatalogueTests
{
    [Fact]
    public void Catalogue_Contains_Three_Unique_Fixed_Reports()
    {
        Assert.Equal(3, InitialReportCatalogue.All.Count);
        Assert.Equal(3, InitialReportCatalogue.All.Select(x => x.ReportId).Distinct().Count());
    }

    [Fact]
    public void Every_Report_Uses_Explicit_Date_Range_Parameters()
    {
        foreach (var report in InitialReportCatalogue.All)
        {
            Assert.Contains(report.Parameters, x => x.Id == ReportParameterIds.DateFrom && x.IsRequired);
            Assert.Contains(report.Parameters, x => x.Id == ReportParameterIds.DateTo && x.IsRequired);
        }
    }

    [Fact]
    public void Report_Measures_Are_Stable_Identifiers_Not_Formulas()
    {
        var measures = InitialReportCatalogue.All.SelectMany(x => x.MeasureIds).ToArray();
        Assert.Contains(SalesMeasureIds.NetSales, measures);
        Assert.All(measures, x => { Assert.StartsWith("sales.", x); Assert.DoesNotContain("=", x); Assert.DoesNotContain(" ", x); });
    }

    [Fact]
    public void Classification_Reports_Include_Contribution_And_Partition_Control()
    {
        foreach (var report in new[] { InitialReportCatalogue.BrandSales, InitialReportCatalogue.BrandSegmentSales })
        {
            Assert.Contains(report.Columns, x => x.MeasureId == SalesMeasureIds.ContributionPercent);
            Assert.Contains("partition-total", report.ReconciliationControlId);
        }
    }

    [Fact]
    public void Result_Contracts_Carry_Rows_Totals_And_Reconciliation_Evidence()
    {
        var table = new ReportTable(InitialReportCatalogue.DailySales.Columns,
            [new ReportRow([new ReportCell("net-sales", 125m)])],
            [new ReportTotal("net-sales", 125m, ReportAggregation.Sum)]);
        var result = new ReconciliationResult("sales.canonical-total", ReconciliationStatus.Passed, "Matched",
            new Dictionary<string, decimal> { ["difference"] = 0m });
        Assert.Single(table.Rows); Assert.Single(table.Totals); Assert.Equal(0m, result.Evidence!["difference"]);
    }
}
