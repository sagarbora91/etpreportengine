using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// The core Retail families with the roles and identities of spec 7.3, laid over the shipped catalogue's columns.
/// Every column's role is set here, so these tests do not depend on the roles the shipped catalogue carries.
/// All values are synthetic.
/// </summary>
internal static class ProjectorTestCatalogue
{
    public const string Store = "TST01";
    public const string Sheet = "Sheet1";

    public static EtpReportFamily R025 { get; } = Describe("R025",
        new() { Scope = DocumentScope.Document, DocumentKey = ["invoice_number"], YearRule = YearRule.FinancialYearOfPrimaryDate, Route = FamilyRoute.Sales },
        key: ["invoice_number"],
        attribute: ["hsn_code", "source_brand_code", "source_brand_name", "brand_segment_code", "gender_code", "reference_invoice_number", "reference_invoice_date"],
        descriptive: ["storename", "storetype", "channel", "region", "city", "customer_name", "customer_phone", "ulpnumber"],
        ignored: ["source_store_timestamp"]);

    public static EtpReportFamily R022 { get; } = Describe("R022",
        new() { Scope = DocumentScope.Document, DocumentKey = ["invoice_number"], YearRule = YearRule.FinancialYearOfPrimaryDate, RowRule = RowRule.SingleRowPerDocument, Route = FamilyRoute.Revenue },
        key: ["invoice_number"],
        attribute: ["reference_invoice_number"],
        descriptive: ["store_name", "store_type", "channel", "region", "state", "city", "customer_name", "customer_phone", "encircle"],
        label: ["invoice_year", "referenceyear"],
        ignored: ["source_store_timestamp"]);

    public static EtpReportFamily R013 { get; } = Describe("R013",
        new() { Scope = DocumentScope.Document, DocumentKey = ["invoice_number"], YearRule = YearRule.FinancialYearOfPrimaryDate, Route = FamilyRoute.Enrichment },
        key: ["invoice_number"],
        attribute: ["brand", "brandname", "cluster", "gender", "invrefno", "invrefdate"],
        descriptive: ["store_name", "store_type", "channel", "region", "city", "cro_name", "customer_name", "customer_phone"]);

    public static EtpReportFamily R003 { get; } = Describe("R003",
        new() { Scope = DocumentScope.Document, DocumentKey = ["invoice_number"], YearRule = YearRule.FinancialYearOfPrimaryDate, Route = FamilyRoute.Enrichment },
        key: ["invoice_number"],
        attribute: ["brand", "brand_name", "cluster", "gender", "activation_details", "user_discount_details", "invoice_ref_no", "invoice_ref_date"],
        descriptive: ["store_name", "store_type", "channel", "region", "city", "customernumber", "customer_name", "customer_phone", "ulp_no"],
        ignored: ["eastimestamp", "storetimestamp"]);

    public static EtpReportFamily StockLedger { get; } = Describe("STOCK_LEDGER",
        new() { Scope = DocumentScope.Document, DocumentKey = ["document_number"], YearRule = YearRule.FinancialYearOfPrimaryDate, RowRule = RowRule.StockUnitChain, Route = FamilyRoute.StockMovement },
        key: ["document_number"],
        attribute: ["hsn_code", "brand", "brandname", "cluster", "gender", "ref_documentnumber", "ref_documentdate"],
        descriptive: ["store_name", "city", "state", "location"]);

    public static EtpReportFamily ClosingStock { get; } = Describe("CLOSING_STOCK",
        new()
        {
            Scope = DocumentScope.Snapshot, SnapshotDate = "Column:snapshot_date", RowRule = RowRule.SnapshotItems,
            RowKey = ["product_code", "COALESCE(source_uid,batch_number,ean)"], ChangePolicy = ChangePolicy.LatestReadingWins, Route = FamilyRoute.StockSnapshot
        },
        attribute: ["hsn_code", "brand_code", "cluster", "gender"],
        descriptive: ["store_name", "store_type", "channel", "region", "state", "city", "itemdescription"]);

    public static EtpReportFamily R010 { get; } = Describe("R010",
        new()
        {
            Scope = DocumentScope.Snapshot, SnapshotDate = "Block", RowRule = RowRule.SnapshotItems,
            RowKey = ["itemnumber", "COALESCE(uid,lotnumber)"], ChangePolicy = ChangePolicy.LatestReadingWins, Route = FamilyRoute.StockSnapshot
        },
        attribute: ["hsn_code", "brand", "brandname", "cluster", "gender", "ean_category"],
        descriptive: ["store_name", "store_type", "channel", "region", "city"]);

    /// <summary>
    /// A landing-only family (R001 columns) with the given scope; Date scope is the default of spec 7.4. Without
    /// <paramref name="numberIsFact"/> the invoice number is an attribute, as in a family whose facts hold no number.
    /// </summary>
    public static EtpReportFamily Landing(DocumentScope scope = DocumentScope.Date, string? snapshotDate = null,
        string[]? rowKey = null, bool numberIsFact = true) => Describe("R001",
        new() { Scope = scope, SnapshotDate = snapshotDate, RowRule = scope == DocumentScope.Date ? RowRule.Multiset : RowRule.SnapshotItems, RowKey = rowKey ?? [] },
        attribute: numberIsFact ? [] : ["invnumber"],
        descriptive: ["store_name", "store_type", "channel", "region", "state", "city", "customer_name", "customer_phone", "encircle"],
        label: ["invoice_year", "referenceyear"],
        ignored: ["storetimestamp"]);

    private static readonly Dictionary<string, object?> Defaults = new(StringComparer.Ordinal)
    {
        ["source_transaction_type"] = "INV", ["store_code"] = Store, ["storename"] = "Synthetic store", ["store_name"] = "Synthetic store",
        ["storetype"] = "COCO", ["store_type"] = "COCO", ["channel"] = "RETAIL", ["region"] = "WEST", ["city"] = "TESTCITY", ["state"] = "TESTSTATE",
        ["product_code"] = "ITEM-1", ["hsn_code"] = "91021100", ["source_brand_code"] = "BR", ["source_brand_name"] = "Brand", ["brand"] = "BR",
        ["brandname"] = "Brand", ["brand_name"] = "Brand", ["brand_code"] = "BR", ["brand_segment_code"] = "SEG", ["cluster"] = "SEG",
        ["gender_code"] = "U", ["gender"] = "U", ["source_quantity"] = 1m, ["source_ucp"] = 1000m, ["source_gross_ucp"] = 1000m, ["ucp"] = 1000m,
        ["grossucp"] = 1000m, ["scheme_discount"] = 0m, ["user_discount"] = 0m, ["pre_discount"] = 0m, ["netgross"] = 1000m,
        ["source_net_amount"] = 1000m, ["source_tax_amount"] = 152.54m, ["source_net_value"] = 847.46m, ["tax"] = 152.54m,
        ["customer_name"] = "Synthetic customer A", ["customer_phone"] = "PHONE-A", ["cro_number"] = "CRO-1", ["cro_name"] = "Synthetic adviser",
        ["source_invoice_quantity"] = 1m, ["tender_cash"] = 1000m, ["encircle"] = "LOYALTY-A", ["source_store_timestamp"] = "2026-08-29 11:00:00",
        ["storetimestamp"] = "2026-08-29 11:00:00", ["eastimestamp"] = "2026-08-29 11:00:00", ["opening_quantity"] = 0m,
        ["transaction_quantity"] = 1m, ["closing_quantity"] = 1m, ["quantity"] = 1m, ["unit_cost"] = 1000m, ["total_cost"] = 1000m,
        ["itemnumber"] = "ITEM-1", ["closingbalance"] = 1m, ["totalucp"] = 1000m,
        ["invoice_number"] = "INV-0001", ["transaction_date"] = new DateOnly(2026, 8, 29), ["document_number"] = "STM-0001",
        ["document_date"] = new DateOnly(2026, 8, 29), ["snapshot_date"] = new DateOnly(2026, 9, 1), ["trans_type"] = "INV",
        ["invnumber"] = "INV-0001", ["invoicedate"] = new DateOnly(2026, 8, 29), ["invoicequantity"] = 1m, ["netvalue"] = 1000m
    };

    public static bool Has(EtpReportFamily family, string field) => family.Columns.Any(column => column.CanonicalField == field);

    /// <summary>A staged row: every column of the family, the defaults above, then the given values.</summary>
    public static SourceRow Row(EtpReportFamily family, int sheetRow, params (string Field, object? Value)[] values) =>
        Row(family, new RowLocator(1, Sheet, sheetRow), values);

    public static SourceRow Row(EtpReportFamily family, RowLocator locator, params (string Field, object? Value)[] values)
    {
        var row = family.Columns.ToDictionary(column => column.CanonicalField, column => Defaults.GetValueOrDefault(column.CanonicalField), StringComparer.Ordinal);
        foreach (var (field, value) in values)
        {
            if (!row.ContainsKey(field)) throw new InvalidOperationException($"{family.ReportCode} has no column {field}.");
            row[field] = value;
        }
        return new(locator, row);
    }

    public static SourceBlock Block(int rowCount, ExportTime? exportTime = null, int blockNo = 1) =>
        new(blockNo, Sheet, 2, rowCount + 1, rowCount, BlockCompleteness.Complete, BlockOrigin.Raw,
            exportTime ?? ExportTime.AtMinute(new DateTime(2026, 9, 29, 14, 49, 0)));

    private static EtpReportFamily Describe(string reportCode, EtpFamilyIdentity identity, string[]? key = null, string[]? attribute = null,
        string[]? descriptive = null, string[]? label = null, string[]? ignored = null)
    {
        var family = EtpReportFamilyRegistry.Resolve(reportCode);
        var roles = new Dictionary<string, ColumnRole>(StringComparer.Ordinal);
        foreach (var (fields, role) in new[] { (key, ColumnRole.Key), (attribute, ColumnRole.Attribute), (descriptive, ColumnRole.Descriptive), (label, ColumnRole.Label), (ignored, ColumnRole.Ignored) })
            foreach (var field in fields ?? [])
            {
                // A renamed catalogue column fails here instead of quietly turning into a fact.
                if (family.Columns.All(column => column.CanonicalField != field))
                    throw new InvalidOperationException($"{reportCode} has no column {field}.");
                roles.Add(field, role);
            }
        return family with
        {
            Columns = family.Columns.Select(column => column with { Role = roles.GetValueOrDefault(column.CanonicalField, ColumnRole.Fact) }).ToArray(),
            Identity = identity
        };
    }
}
