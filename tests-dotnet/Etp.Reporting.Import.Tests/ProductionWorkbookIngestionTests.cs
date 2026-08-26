using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class ProductionWorkbookIngestionTests
{
    [Fact]
    public async Task Reader_ignores_false_worksheet_dimension_and_preflight_collapses_duplicate_layout()
    {
        var headers = RetailSalesProfiles.R022Headers.Concat(RetailSalesProfiles.R022Headers).ToArray();
        var left = R022Values();
        var path = CreateWorkbook(headers, left.Concat(left).ToArray(), "A1");
        try
        {
            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
            Assert.Equal(92, snapshot.Sheets.Single().Headers.Count);
            var result = new ImportPreflight().Inspect(snapshot, RetailSalesProfiles.FirstSalesSlice);
            Assert.True(result.CanImport);
            Assert.Equal("R022", result.Profile!.ReportCode);
            Assert.Equal(46, result.Sheet!.Headers.Count);
            Assert.Contains(result.Diagnostics, x => x.Code == "REPEATED_LAYOUT_COLLAPSED");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Staging_maps_typed_R025_fields_and_excludes_PII()
    {
        var path = CreateWorkbook(RetailSalesProfiles.R025Headers, R025Values(), "A1:A1");
        try
        {
            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
            var preflight = new ImportPreflight().Inspect(snapshot, RetailSalesProfiles.FirstSalesSlice);
            var staged = new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile!);
            Assert.True(staged.CanPersist);
            var values = Assert.Single(staged.Rows).Values;
            Assert.Equal("SKU-1", values["product_code"]);
            Assert.IsType<decimal>(values["source_quantity"]);
            Assert.IsType<DateOnly>(values["transaction_date"]);
            Assert.DoesNotContain(values.Keys, x => x.Contains("customer", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(values.Keys, x => x.Contains("contact", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(values.Keys, x => x.Contains("ulp", StringComparison.OrdinalIgnoreCase));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("R003")]
    [InlineData("R013")]
    public void Legacy_enrichment_profiles_are_exact_and_never_stage_customer_pii(string reportCode)
    {
        var profile = reportCode == "R003" ? RetailSalesProfiles.R003 : RetailSalesProfiles.R013;

        Assert.Equal(reportCode, profile.ReportCode);
        Assert.Contains(profile.Fields, x => x.CanonicalField == "source_net_value" && x.IsRequired);
        Assert.DoesNotContain(profile.Fields, x => x.SourceHeader.Contains("CUSTOMER", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(profile.Fields, x => x.CanonicalField is "activation_details" or "user_discount_details");
        if (reportCode == "R013") Assert.Contains(profile.Fields, x => x.CanonicalField == "cro_number");
    }

    [Fact]
    public async Task Preflight_blocks_repeated_layout_when_halves_differ()
    {
        var headers = RetailSalesProfiles.R022Headers.Concat(RetailSalesProfiles.R022Headers).ToArray();
        var left = R022Values(); var right = R022Values(); right[8] = "DIFFERENT";
        var path = CreateWorkbook(headers, left.Concat(right).ToArray(), "A1");
        try
        {
            var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
            var result = new ImportPreflight().Inspect(snapshot, RetailSalesProfiles.FirstSalesSlice);
            Assert.False(result.CanImport);
            Assert.Contains(result.Diagnostics, x => x.Code == "REPEATED_LAYOUT_MISMATCH");
        }
        finally { File.Delete(path); }
    }

    private static string CreateWorkbook(IReadOnlyList<string> headers, IReadOnlyList<string> values, string dimension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"etp-{Guid.NewGuid():N}.xlsx");
        using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var wb = doc.AddWorkbookPart(); wb.Workbook = new Workbook();
        var ws = wb.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData(Row(1, headers), Row(2, values));
        ws.Worksheet = new Worksheet(new SheetDimension { Reference = dimension }, sheetData);
        wb.Workbook.AppendChild(new Sheets(new Sheet { Id = wb.GetIdOfPart(ws), SheetId = 1, Name = "Sheet0" }));
        wb.Workbook.Save();
        return path;
    }

    private static Row Row(uint number, IEnumerable<string> values)
    {
        var row = new Row { RowIndex = number }; var i = 0;
        foreach (var value in values) row.Append(new Cell { CellReference = Column(++i) + number, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) });
        return row;
    }

    private static string Column(int index) { var s = ""; while (index > 0) { index--; s = (char)('A' + index % 26) + s; index /= 26; } return s; }
    private static string[] R022Values() => RetailSalesProfiles.R022Headers.Select(h => h switch { "INVNUMBER" => "INV-1", "InvoiceQuantity" => "1", "INVOICEDATE" => "2026-08-25", "NetValue" => "100.00", "CUSTOMERNAME" => "REDACTED", "ContactNo" => "REDACTED", _ => "0" }).ToArray();
    private static string[] R025Values() => RetailSalesProfiles.R025Headers.Select(h => h switch { "ITEMNUMBER" => "SKU-1", "INVNUMBER" => "INV-1", "INVDATE" => "2026-08-25", "INVREFDATE" => "", "QTY" => "1", "NETAMOUNT" => "100.00", "NETVALUE" => "100.00", "CUSTOMERNAME" or "CONTACTNO" or "ULPNUMBER" => "REDACTED", _ => "0" }).ToArray();
}
