using Etp.Reporting.Domain.Imports;

namespace Etp.Reporting.Import.Profiles;

public static class StockImportProfiles
{
    public static readonly IReadOnlyList<string> VariantStockLedgerHeaders =
    [
        "TRANS_TYPE", "STORE CODE", "STORE NAME", "ITEMNUMBER", "HSN CODE", "BRAND", "BRANDNAME",
        "CLUSTER", "GENDER", "DOCUMENTNUMBER", "DOCUMENTDATE", "FROM LOCATION", "TO LOCATION",
        "REF_DOCUMENTNUMBER", "REF_DOCUMENTDATE", "OPENING_QTY", "TRANS_QTY", "CLOSING_QTY",
        "CITY", "STATE", "LOCATION"
    ];

    public static readonly IReadOnlyList<string> ClosingStockHeaders =
    [
        "STORE CODE", "STORE NAME", "STORE TYPE", "CHANNEL", "REGION", "STATE", "CITY", "Date",
        "ITEMNUMBER", "HSN CODE", "ITEMDESCRIPTION", "EAN NO", "BRAND", "CLUSTER", "GENDER", "QTY",
        "UCP", "TOTALUCP", "BATCHNUMBER", "UID_ITEM"
    ];

    public static ImportProfile VariantStockLedger { get; } = Create("STOCK_LEDGER", VariantStockLedgerHeaders,
        new("TRANS_TYPE", "source_transaction_type", CanonicalDataType.Text, true),
        new("STORE CODE", "store_code", CanonicalDataType.Identifier, true),
        new("ITEMNUMBER", "product_code", CanonicalDataType.Identifier, true),
        new("DOCUMENTNUMBER", "document_number", CanonicalDataType.Identifier, true),
        new("DOCUMENTDATE", "document_date", CanonicalDataType.Date, true),
        new("FROM LOCATION", "from_location", CanonicalDataType.Identifier, false),
        new("TO LOCATION", "to_location", CanonicalDataType.Identifier, false),
        new("OPENING_QTY", "opening_quantity", CanonicalDataType.Decimal, true),
        new("TRANS_QTY", "transaction_quantity", CanonicalDataType.Decimal, true),
        new("CLOSING_QTY", "closing_quantity", CanonicalDataType.Decimal, true));

    public static ImportProfile ClosingStock { get; } = Create("CLOSING_STOCK", ClosingStockHeaders,
        new("STORE CODE", "store_code", CanonicalDataType.Identifier, true),
        new("Date", "snapshot_date", CanonicalDataType.Date, true),
        new("ITEMNUMBER", "product_code", CanonicalDataType.Identifier, true),
        new("EAN NO", "ean", CanonicalDataType.Identifier, false),
        new("BRAND", "brand_code", CanonicalDataType.Identifier, false),
        new("CLUSTER", "cluster", CanonicalDataType.Identifier, false),
        new("GENDER", "gender", CanonicalDataType.Text, false),
        new("QTY", "quantity", CanonicalDataType.Decimal, true),
        new("UCP", "unit_cost", CanonicalDataType.Decimal, false),
        new("TOTALUCP", "total_cost", CanonicalDataType.Decimal, false),
        new("BATCHNUMBER", "batch_number", CanonicalDataType.Identifier, false),
        new("UID_ITEM", "source_uid", CanonicalDataType.Identifier, false));

    public static IReadOnlyList<ImportProfile> All { get; } = [VariantStockLedger, ClosingStock];

    private static ImportProfile Create(string code, IReadOnlyList<string> headers, params ImportFieldMapping[] fields) =>
        new(code, "ETP_2026_08", "1", ImportProfileMatcher.CreateHeaderSignature(headers), fields);
}
