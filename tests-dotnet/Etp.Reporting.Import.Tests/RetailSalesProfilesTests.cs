using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class RetailSalesProfilesTests
{
    [Fact]
    public void Real_R025_logical_headers_match_the_first_sales_profile()
    {
        var workbook = Workbook("SDB-VariantwiseSales.xlsx", RetailSalesProfiles.R025Headers);

        var result = new ImportPreflight().Inspect(workbook, RetailSalesProfiles.FirstSalesSlice);

        Assert.True(result.CanImport);
        Assert.Equal("R025", result.Profile!.ReportCode);
    }

    [Fact]
    public void Horizontally_duplicated_R022_export_is_collapsed_and_matched()
    {
        var headers = RetailSalesProfiles.R022Headers;
        var cells = headers.Select(_ => new WorkbookCell(null)).ToArray();
        var sheet = new WorkbookSheet("Sheet0", 1, [.. headers, .. headers],
            [new WorkbookRow(2, [.. cells, .. cells])]);
        var workbook = new WorkbookSnapshot("Revenue Report.xlsx", 100, new string('a', 64), [sheet]);

        var result = new ImportPreflight().Inspect(workbook, RetailSalesProfiles.FirstSalesSlice);

        Assert.True(result.CanImport);
        Assert.Equal("R022", result.Profile!.ReportCode);
        Assert.Equal(46, result.Sheet!.Headers.Count);
        Assert.Contains(result.Diagnostics, x => x.Code == "REPEATED_LAYOUT_COLLAPSED");
    }

    [Fact]
    public void Restricted_customer_columns_are_not_mapped_to_canonical_fields()
    {
        Assert.DoesNotContain(RetailSalesProfiles.R025.Fields,
            field => field.SourceHeader is "CUSTOMERNAME" or "CONTACTNO");
        Assert.DoesNotContain(RetailSalesProfiles.R022.Fields,
            field => field.SourceHeader.Equals("CUSTOMERNAME", StringComparison.OrdinalIgnoreCase)
                || field.SourceHeader.Equals("ContactNo", StringComparison.OrdinalIgnoreCase));
    }

    private static WorkbookSnapshot Workbook(string fileName, IReadOnlyList<string> headers) =>
        new(fileName, 100, new string('a', 64), [new WorkbookSheet("Sheet0", 1, headers, [])]);
}
