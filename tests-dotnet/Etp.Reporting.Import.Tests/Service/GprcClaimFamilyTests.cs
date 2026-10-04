using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Conversion;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.TestSupport.Service;

namespace Etp.Reporting.Import.Tests.Service;

/// <summary>
/// S041 GPRC CLAIM (decision 16, 4 Oct 2026, Q6: a new family, not a second layout of S023; lane L10). ETP's raw export:
/// sheet "GPRC Claims Report", header row 1, 34 columns, no totals, Transaction Date with a time of day. Its columns are
/// S023 GPRC_Report's in readable form, one to one, except that Value moves from position 22 to 33. Synthetic fixture only.
/// </summary>
public sealed class GprcClaimFamilyTests
{
    // S023 technical header -> S041 readable header, as SERVICE-OPEN-QUESTIONS-ANSWERS.md Q3 maps them.
    private static readonly Dictionary<string, string> S023ToS041 = new(StringComparer.Ordinal)
    {
        ["JONumber"] = "Job Order Number", ["ItemID"] = "Item ID", ["Quantity"] = "Quantity", ["SCName"] = "Store Name",
        ["SCNumber"] = "Store Code", ["Location"] = "Location", ["CustName"] = "Customer Name", ["DocumentType"] = "Document Type",
        ["GSTINNo"] = "GST Number", ["ToGSTIN"] = "To GSTIN", ["ToLocation"] = "To Location", ["HSNCode"] = "HSN Code",
        ["ClusterID"] = "Cluster ID", ["BrandID"] = "Brand ID", ["VarriantID"] = "Variant ID", ["AccountNum"] = "Account Number",
        ["TransDate"] = "Transaction Date", ["DocumentNum"] = "Document Number", ["Price"] = "Price", ["NetAmount"] = "Net Amount",
        ["TaxAmount"] = "Tax Amount", ["Value"] = "Value", ["GrossAmount"] = "Gross Amount", ["Taxable"] = "Taxable",
        ["CGSTRate"] = "CGST Rate", ["CGSTValue"] = "CGST Value", ["SGSTRate"] = "SGST Rate", ["SGSTValue"] = "SGST Value",
        ["TotalTax"] = "Total Tax", ["IGSTRate"] = "IGST Rate", ["IGSTValue"] = "IGST Value", ["NetAmountINCTax"] = "Net Amount INC Tax",
        ["IRNNumber"] = "IRN Number", ["UCPValue"] = "UCP Value",
    };

    [Fact]
    public void S041_is_its_own_importable_family_with_its_own_landing_table()
    {
        var family = EtpReportFamilyRegistry.Resolve("S041");
        Assert.Equal("GPRC_Claim", family.Name);
        Assert.Equal("etp_landing_s041", family.TableName);
        Assert.Equal(BusinessUnit.Service, family.BusinessUnit);
        Assert.False(family.Derived);
        Assert.Equal(["GPRC CLAIM"], family.RawNamePatterns);
        Assert.Equal(34, family.Headers.Count);
        // Dated like every Service family in the interim: one snapshot per file (folder date or window end).
        Assert.Null(family.PrimaryDateHeader);
        Assert.Equal(DocumentScope.Snapshot, family.Identity!.Scope);
        Assert.Contains("S041", ServiceInterimFamilies.Importable);
        Assert.Null(ServiceInterimFamilies.NotImportedCode("S041"));
        var rule = ServiceInterimFamilies.ReadRules["S041"];
        Assert.Equal(ServiceReadRuleKind.DateLog, rule.Kind);
        Assert.Equal("transaction_date", rule.DateColumn!.CanonicalField);
        Assert.Equal(CanonicalDataType.Date, Assert.Single(family.Columns, column => column.CanonicalField == "transaction_date").DataType);
        Assert.Equal(ColumnRole.Fact, Assert.Single(family.Columns, column => column.CanonicalField == "transaction_date").Role);
        Assert.Equal(ColumnRole.Fact, Assert.Single(family.Columns, column => column.CanonicalField == "value").Role);
        Assert.Equal(ColumnRole.Fact, Assert.Single(family.Columns, column => column.CanonicalField == "ucp_value").Role);
    }

    [Fact]
    public void S041_maps_one_to_one_onto_S023_except_that_Value_moves_from_22_to_33()
    {
        var s023 = EtpReportFamilyRegistry.Resolve("S023").Headers;
        var s041 = EtpReportFamilyRegistry.Resolve("S041").Headers;
        Assert.Equal(s023.Count, s041.Count);
        Assert.Equal(s023.Order(StringComparer.Ordinal), S023ToS041.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(s041.Order(StringComparer.Ordinal), S023ToS041.Values.Order(StringComparer.Ordinal));
        Assert.Equal(21, s023.ToList().IndexOf("Value"));
        Assert.Equal(32, s041.ToList().IndexOf("Value"));
        var moved = s023.Where(header => header != "Value").Select(header => S023ToS041[header]).ToArray();
        Assert.Equal(moved, s041.Where(header => header != "Value"));
        // Different layouts, so neither family can take the other's file, and no claim layout shares S041's signature.
        var signature = ImportProfileMatcher.CreateHeaderSignature(s041);
        Assert.Single(EtpReportFamilyRegistry.Families, family => ImportProfileMatcher.CreateHeaderSignature(family.Headers) == signature);
    }

    [Fact]
    public async Task The_raw_GPRC_CLAIM_file_routes_to_S041_by_its_header_and_by_its_name()
    {
        var path = Path.Combine(ServiceFixtures.GprcClaimFolder, ServiceFixtures.GprcClaimFileName);
        var snapshot = await new OpenXmlWorkbookReader().ReadAsync(path);
        var sheet = Assert.Single(snapshot.Sheets);
        Assert.Equal(ServiceFixtures.GprcClaimSheet, sheet.Name);
        Assert.Equal(1, sheet.HeaderRowNumber);
        Assert.Equal(EtpReportFamilyRegistry.Resolve("S041").Headers, sheet.Headers);
        Assert.Equal(ServiceFixtures.GprcClaimLines, sheet.Rows.Count);

        var matcher = new ImportProfileMatcher();
        Assert.Equal("S041", matcher.Match(sheet.Headers, ApprovedImportProfileRegistry.All, ServiceFixtures.GprcClaimFileName, sheet.Name)?.ReportCode);
        Assert.Equal("S041", matcher.Match(sheet.Headers, ApprovedImportProfileRegistry.All)?.ReportCode);
        Assert.Equal("S041", EtpReportFamilyRegistry.IdentifyName(ServiceFixtures.GprcClaimFileName)?.FamilyCode);
        Assert.Equal("S041", EtpReportFamilyRegistry.IdentifyName("GPRC CLAIM 30.09.2026 TO 03.10.2026.xlsx",
            [EtpReportFamilyRegistry.Resolve("S041")])?.FamilyCode);
        Assert.Equal("S023", EtpReportFamilyRegistry.IdentifyName("S023_GPRC_Report.xlsx")?.FamilyCode);
    }

    [Fact]
    public async Task Transaction_Date_lands_as_its_date_part()
    {
        var path = Path.Combine(ServiceFixtures.GprcClaimFolder, ServiceFixtures.GprcClaimFileName);
        var sheet = Assert.Single((await new OpenXmlWorkbookReader().ReadAsync(path)).Sheets);
        var column = sheet.Headers.ToList().IndexOf("Transaction Date");
        var converter = new TypedCellConverter();
        var dates = sheet.Rows.Select(row => converter.Convert(row.Cells[column].Value, CanonicalDataType.Date, isRequired: false))
            .Select(result => Assert.IsType<DateOnly>(result.Value)).ToArray();
        Assert.Equal(
            [new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 7)],
            dates);
        // The source cell keeps its time (15:35:43.146 on the 5 Aug lines); only the landed value drops it.
        Assert.Equal(new DateTime(2026, 8, 5, 15, 35, 43, 146), ToDateTime(sheet.Rows[1].Cells[column].Value));
    }

    [Fact]
    public async Task The_GPRC_history_file_is_S023_and_overlaps_the_claim_file_on_two_documents()
    {
        var history = Assert.Single((await new OpenXmlWorkbookReader().ReadAsync(
            Path.Combine(ServiceFixtures.GprcHistoryFolder, "S023_GPRC_Report.xlsx"))).Sheets, sheet => sheet.Name == "Data");
        var claims = Assert.Single((await new OpenXmlWorkbookReader().ReadAsync(
            Path.Combine(ServiceFixtures.GprcClaimFolder, ServiceFixtures.GprcClaimFileName))).Sheets);
        Assert.Equal("S023", new ImportProfileMatcher().Match(history.Headers, ApprovedImportProfileRegistry.All, "S023_GPRC_Report.xlsx", "Data")?.ReportCode);
        Assert.Equal(ServiceFixtures.GprcHistoryLines, history.Rows.Count);

        var historyDocs = Documents(history, "DocumentNum");
        var claimDocs = Documents(claims, "Document Number");
        Assert.Equal(["GPAW330SYN0002", "GPAW330SYN0003"], historyDocs.Distinct().Intersect(claimDocs).Order(StringComparer.Ordinal));
        // The union rule (S041 wins per claim document): every claim line, plus the history lines of documents S041 lacks.
        var fromHistory = historyDocs.Count(doc => !claimDocs.Contains(doc));
        Assert.Equal(ServiceFixtures.GprcUnionLinesFromHistory, fromHistory);
        Assert.Equal(ServiceFixtures.GprcUnionLines, fromHistory + claimDocs.Length);
        Assert.Equal(ServiceFixtures.GprcUnionDocuments, historyDocs.Union(claimDocs).Distinct().Count());
    }

    private static string[] Documents(WorkbookSheet sheet, string header)
    {
        var column = sheet.Headers.ToList().IndexOf(header);
        return [.. sheet.Rows.Select(row => Convert.ToString(row.Cells[column].Value, System.Globalization.CultureInfo.InvariantCulture)!)];
    }

    private static DateTime ToDateTime(object? value) => value switch
    {
        DateTime dateTime => new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Millisecond),
        double serial => Truncate(DateTime.FromOADate(serial)),
        decimal serial => Truncate(DateTime.FromOADate((double)serial)),
        _ => throw new InvalidOperationException($"Unexpected cell value type {value?.GetType().Name ?? "null"}."),
    };

    private static DateTime Truncate(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, (int)Math.Round(value.TimeOfDay.TotalMilliseconds % 1000));
}
