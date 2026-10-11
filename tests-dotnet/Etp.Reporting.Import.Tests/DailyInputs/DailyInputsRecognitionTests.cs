using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using static Etp.Reporting.Import.Tests.DailyInputs.DailyInputsTestSheets;

namespace Etp.Reporting.Import.Tests.DailyInputs;

/// <summary>D001 is recognised by sheet content, never by file name (design section 2).</summary>
public sealed class DailyInputsRecognitionTests
{
    [Fact]
    public void The_template_is_recognised_whatever_its_file_name()
    {
        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(fields: [("WALK_INS", 3m)])));
        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(fileName: "anything.XLSX")));
        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(sheetName: "daily inputs")));
        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(headers: ["field code", " Input ", "VALUE", "Note", "", ""])));
    }

    [Fact]
    public void An_etp_report_export_is_not_recognised()
    {
        var headers = RetailSalesProfiles.R025Headers;
        var export = new WorkbookSnapshot("R025_synthetic.xlsx", 1, new string('c', 64),
            [new WorkbookSheet("SDB VariantwiseSales", 1, headers, [new WorkbookRow(2, headers.Select(_ => new WorkbookCell(null)).ToArray())])]);

        Assert.False(DailyInputsTemplate.IsDailyInputsWorkbook(export));
    }

    [Fact]
    public void A_csv_is_not_recognised_even_with_template_content()
    {
        Assert.False(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(fileName: "ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11.csv")));
    }

    [Fact]
    public void A_renamed_sheet_is_not_recognised()
    {
        Assert.False(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(sheetName: "Sheet1")));
        Assert.False(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(sheetName: "Daily Inputs (2)")));
    }

    [Theory]
    [InlineData("Field code", "Input", "Value")]
    [InlineData("Field code", "Input", "Amount", "Note")]
    [InlineData("Field code", "Input", "Value", "Note", "Extra")]
    public void A_wrong_header_is_not_recognised(params string[] headers)
    {
        Assert.False(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(headers: headers)));
    }

    [Fact]
    public void A_first_row_that_is_not_the_template_marker_is_not_recognised()
    {
        Assert.False(DailyInputsTemplate.IsDailyInputsWorkbook(Sheet(version: "SOMETHING-ELSE")));

        var workbook = Sheet();
        var sheet = workbook.Sheets[0];
        var withoutMarker = workbook with { Sheets = [sheet with { Rows = sheet.Rows.Skip(1).ToArray() }] };
        Assert.False(DailyInputsTemplate.IsDailyInputsWorkbook(withoutMarker));
    }

    [Fact]
    public void A_later_template_version_is_recognised_so_the_parser_can_reject_it_by_name()
    {
        var later = Sheet(version: "ETP-DAILY-INPUTS-2");

        Assert.True(DailyInputsTemplate.IsDailyInputsWorkbook(later));
        Assert.Equal([DailyInputsCodes.TemplateVersion], DailyInputsParser.Parse(later, Today).Errors.Select(e => e.Code));
    }

    [Fact]
    public void D001_stays_out_of_the_report_family_catalogue()
    {
        Assert.Equal("D001", DailyInputsTemplate.FamilyCode);
        Assert.DoesNotContain(EtpReportFamilyRegistry.Families, family =>
            family.FamilyCode == DailyInputsTemplate.FamilyCode || family.ReportCode == DailyInputsTemplate.FamilyCode);
    }

    [Fact]
    public void Retail_and_service_fields_are_disjoint_and_follow_the_design_order()
    {
        Assert.Equal(["WALK_INS", "OPENING_CASH", "EXPENSES", "CASH_DEPOSIT", "CASH_ADJUSTMENT", "CLOSING_CASH_COUNTED", "OPERATIONAL_REMARK"],
            DailyInputsTemplate.FieldsFor(DailyInputsKind.Retail).Select(f => f.FieldCode));
        Assert.Equal(["SERVICE_CASH", "SERVICE_CARD", "SERVICE_UPI", "SERVICE_WDC", "WCC_WALKIN", "WCC_SALES", "WDC_BILLS"],
            DailyInputsTemplate.FieldsFor(DailyInputsKind.Service).Select(f => f.FieldCode));
        Assert.Empty(DailyInputsTemplate.FieldsFor(DailyInputsKind.Retail).Select(f => f.FieldCode)
            .Intersect(DailyInputsTemplate.FieldsFor(DailyInputsKind.Service).Select(f => f.FieldCode)));
        Assert.Equal(["CASH_ADJUSTMENT"], DailyInputsTemplate.AllFields.Where(f => f.AllowNegative).Select(f => f.FieldCode));
    }

    [Fact]
    public void The_daily_file_name_carries_store_kind_and_date()
    {
        Assert.Equal("ETP-Daily-Inputs-HEMW-RETAIL-2026-10-11.xlsx",
            DailyInputsTemplate.FileName("hemw", DailyInputsKind.Retail, new DateOnly(2026, 10, 11)));
        Assert.Equal("ETP-Daily-Inputs-WLMHW-SERVICE-2026-10-01.xlsx",
            DailyInputsTemplate.FileName("WLMHW", DailyInputsKind.Service, new DateOnly(2026, 10, 1)));
        Assert.StartsWith(DailyInputsTemplate.FileNamePrefix, DailyInputsTemplate.FileName("HEMW", DailyInputsKind.Retail, Today));
    }
}
