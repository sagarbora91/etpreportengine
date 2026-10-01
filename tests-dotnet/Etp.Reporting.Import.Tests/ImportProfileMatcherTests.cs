using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Import.Tests;

public sealed class ImportProfileMatcherTests
{
    [Fact]
    public void Matches_Known_Headers_Without_Depending_On_Display_Casing()
    {
        var signature = ImportProfileMatcher.CreateHeaderSignature(["Bill Date", "Article", "Net Amount"]);
        var profile = new ImportProfile("R025", "1", "1", signature,
        [
            new("Bill Date", "transaction_date", CanonicalDataType.Date, true),
            new("Article", "product_code", CanonicalDataType.Identifier, true),
            new("Net Amount", "net_sales_amount", CanonicalDataType.Decimal, true)
        ]);

        var match = new ImportProfileMatcher().Match([" bill date ", "ARTICLE", "Net  Amount"], [profile]);

        Assert.Same(profile, match);
    }

    [Fact]
    public void Unknown_Layout_Does_Not_Guess()
    {
        var signature = ImportProfileMatcher.CreateHeaderSignature(["Bill Date"]);
        var profile = new ImportProfile("R025", "1", "1", signature,
            [new("Bill Date", "transaction_date", CanonicalDataType.Date, true)]);

        Assert.Null(new ImportProfileMatcher().Match(["Different Header"], [profile]));
    }

    [Theory]
    [InlineData("S001", "S001_RepairRegister.xlsx", "S001")]
    [InlineData("S001", "S014_RepairRegister_DC.xlsx", "S014")]
    [InlineData("S001", "RepairRegister_SRNINV_20260929.xlsx", "S035")]
    [InlineData("S001", "RepairRegister_SRN_20260929.xlsx", "S033")]
    [InlineData("S001", "RepairRegister_20260929.xlsx", "S001")]
    [InlineData("S007", "PurchaseRegister_RECCIVED_20260929.xlsx", "S008")]
    [InlineData("S024", "GPRC_WDC_20260929.xlsx", "S025")]
    [InlineData("S023", "GPRC_Report_20260929.xlsx", "S023")]
    [InlineData("S039", "WD_Claim_20260929.xlsx", "S039")]
    [InlineData("S039", "WRA_Claim_20260929.xlsx", "S040")]
    [InlineData("R022", "R022_Revenue_Report.xlsx", "R022")]
    [InlineData("R022", "Revenue Report.xlsx", "R022")]
    public void Shared_service_centre_and_retail_signatures_are_named_only_among_the_matching_families(
        string headersOf, string fileName, string expected)
    {
        var headers = EtpReportFamilyRegistry.Resolve(headersOf).Headers;

        var match = new ImportProfileMatcher().Match(headers, ApprovedImportProfileRegistry.All, fileName, "Data");

        Assert.Equal(expected, match?.ReportCode);
    }

    [Fact]
    public void A_shared_repair_register_layout_without_a_name_is_not_guessed()
    {
        var headers = EtpReportFamilyRegistry.Resolve("S001").Headers;

        Assert.Null(new ImportProfileMatcher().Match(headers, ApprovedImportProfileRegistry.All, "export.xlsx", "Data"));
    }

    [Fact]
    public void Retail_and_service_names_that_normalise_alike_resolve_to_the_family_whose_headers_matched()
    {
        // S003 RevenueReport and S006 ClosingStock normalise exactly like Retail Revenue Report and Closing Stock.
        Assert.Equal("R022", EtpReportFamilyRegistry.IdentifyName("Revenue Report.xlsx")!.FamilyCode);
        Assert.Equal("R011", EtpReportFamilyRegistry.IdentifyName("Closing Stock.xlsx")!.FamilyCode);
        Assert.Equal("S003", EtpReportFamilyRegistry.IdentifyName("Revenue Report.xlsx", [EtpReportFamilyRegistry.Resolve("S003")])!.FamilyCode);
        Assert.Equal("S006", EtpReportFamilyRegistry.IdentifyName("ClosingStock.xlsx", [EtpReportFamilyRegistry.Resolve("S006")])!.FamilyCode);
        Assert.Equal("S003", new ImportProfileMatcher().Match(EtpReportFamilyRegistry.Resolve("S003").Headers,
            ApprovedImportProfileRegistry.All, "Revenue Report.xlsx")?.ReportCode);
    }

    [Theory]
    [InlineData("Sales S123 report.xlsx", "S123")]
    [InlineData("S014_RepairRegister_DC.xlsx", "S014")]
    [InlineData("R025_SDB_VariantwiseSales.xlsx", "R025")]
    [InlineData("RS123 report.xlsx", null)]
    [InlineData("S1234 report.xlsx", null)]
    public void Family_codes_in_names_need_a_boundary_on_both_sides(string name, string? expected)
    {
        var match = EtpReportFamilyRegistry.FamilyCodePattern.Match(name);

        Assert.Equal(expected, match.Success ? match.Groups[1].Value : null);
    }

    [Fact]
    public void A_code_in_the_name_that_is_not_among_the_matching_families_names_none()
    {
        var candidates = new[] { EtpReportFamilyRegistry.Resolve("S001"), EtpReportFamilyRegistry.Resolve("S014") };

        Assert.Null(EtpReportFamilyRegistry.IdentifyName("S002_RepairRegister.xlsx", candidates));
    }
}
