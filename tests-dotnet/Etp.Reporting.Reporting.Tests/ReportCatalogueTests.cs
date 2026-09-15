using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

public sealed class ReportCatalogueTests
{

    [Fact]
    public void Product_catalogue_exposes_named_operational_reports_by_category()
    {
        Assert.True(ProductReportCatalogue.All.Count >= 29);
        Assert.Equal(ProductReportCatalogue.All.Count,ProductReportCatalogue.All.Select(x=>x.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(ProductReportCatalogue.All,x=>x.Name=="Closing Stock"&&x.Category=="Stock");
        Assert.Contains(ProductReportCatalogue.All,x=>x.Name=="Missing Source Report"&&x.Category=="Exceptions");
        Assert.Contains(ProductReportCatalogue.All,x=>x.Name=="Management Trend"&&x.Category=="Management");
    }
}
