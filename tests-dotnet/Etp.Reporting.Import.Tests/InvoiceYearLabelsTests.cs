using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class InvoiceYearLabelsTests
{
    [Fact]
    public void R022_rows_whose_INVOICEYEAR_is_not_the_financial_year_of_their_date_get_one_information_diagnostic()
    {
        var workbook = R022(
            Row(2, "SR", "SR-0001", new(2026, 4, 1), 2026),
            Row(3, "SR", "SR-0001", new(2026, 3, 31), 2026),
            Row(4, "INV", "INV-0002", new(2026, 8, 25), 2027),
            Row(5, "SR", "SR-0003", new(2026, 4, 2), 2026),
            Row(6, "INV", "INV-0004", new(2026, 8, 25), null));

        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);

        Assert.NotNull(inspection.AcceptedImport);
        var notice = Assert.Single(inspection.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InvoiceYearDiffers);
        Assert.Equal(ImportDiagnosticSeverity.Information, notice.Severity);
        Assert.Equal(ImportIssueSeverity.Information, ImportCodes.DefaultSeverity(notice.Code));
        Assert.Equal(2, notice.Occurrences);
        Assert.Equal(2, notice.RowNumber);
        Assert.Equal("INVOICEYEAR", notice.ColumnName);
        Assert.Equal("Revenue", notice.SheetName);
        Assert.Contains("2 rows (rows 2, 5)", notice.Message);
        // Messages are written by the code: never an invoice number, customer or other cell value.
        Assert.DoesNotContain("SR-0001", notice.Message);
        Assert.DoesNotContain("Synthetic Customer", notice.Message);
        Assert.Equal(new[] { 2, 5 }, inspection.AcceptedImport!.Staging.Rows
            .Where(row => InvoiceYearLabels.Differs(row.Values)).Select(row => row.SourceRowNumber));
    }

    [Fact]
    public void Labels_that_match_or_are_missing_and_families_without_an_invoice_key_raise_nothing()
    {
        var workbook = R022(Row(2, "INV", "INV-0002", new(2026, 8, 25), 2027), Row(3, "INV", "INV-0004", new(2027, 3, 31), null));

        Assert.DoesNotContain(new MatchedImportEnvelopeFactory().Inspect(workbook).Diagnostics,
            diagnostic => diagnostic.Code == ImportCodes.InvoiceYearDiffers);
        var differing = new StagedImportRow(2, new Dictionary<string, object?>
        {
            ["invoice_year"] = 2026L,
            ["transaction_date"] = new DateOnly(2026, 4, 1)
        });
        Assert.Single(InvoiceYearLabels.Check(RetailSalesProfiles.R022, "Revenue", [differing]));
        // A landing-only family keeps its label without a notice: it has no invoice identity.
        Assert.Empty(InvoiceYearLabels.Check(EtpReportFamilyRegistry.Resolve("R029").CreateProfile(), "Data", [differing]));
    }

    [Theory]
    [InlineData(2026, 3, 31, 2026L, false)]
    [InlineData(2026, 4, 1, 2027L, false)]
    [InlineData(2026, 4, 1, 2026L, true)]
    [InlineData(2027, 3, 31, 2026L, true)]
    [InlineData(2026, 4, 1, null, false)]
    public void A_label_differs_only_from_the_financial_year_of_the_date(int year, int month, int day, long? label, bool differs)
    {
        var values = new Dictionary<string, object?> { ["transaction_date"] = new DateOnly(year, month, day), ["invoice_year"] = label };

        Assert.Equal(differs, InvoiceYearLabels.Differs(values));
        Assert.Equal((int?)label, InvoiceYearLabels.Label(values));
    }

    private static WorkbookRow Row(int rowNumber, string type, string invoice, DateOnly date, int? invoiceYear) =>
        new(rowNumber, RetailSalesProfiles.R022Headers.Select(header => new WorkbookCell(header switch
        {
            "TRANS_TYPE" => type,
            "STORE CODE" => "SYNTH",
            "INVNUMBER" => invoice,
            "CUSTOMERNAME" => "Synthetic Customer",
            "InvoiceQuantity" => 1m,
            "INVOICEDATE" => date.ToDateTime(TimeOnly.MinValue),
            "INVOICEYEAR" => invoiceYear,
            "CASH" => 118m,
            "NetValue" => 118m,
            _ => null
        })).ToArray());

    private static WorkbookSnapshot R022(params WorkbookRow[] rows) =>
        new("R022_Revenue_Report.xlsx", 1, new string('e', 64), [new("Revenue", 1, RetailSalesProfiles.R022Headers, rows)]);
}
