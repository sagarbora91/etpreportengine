using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Stock;

namespace Etp.Reporting.Import.Identity;

/// <summary>
/// The rows a typed fact table stores for one source row (spec 7.1 <c>canonical_sha256</c>), named by the fact
/// table's own columns, so the upgrade can hash a typed fact row read from SQL with no mapping and get the same
/// hash as an import. Only identity and fact columns take part: Attribute columns (brand, segment, activation and
/// discount details), Descriptive ones (<c>staff_name</c>), derived labels (<c>line_identifier</c>,
/// <c>content_key</c>, <c>line_seq</c>), the financial year (part of the document key) and constants (currency,
/// match status) do not. Each row carries <see cref="FactTable"/>. The values are the ones today's orchestrators
/// persist: R025 <c>source_gross_amount</c> is NETAMOUNT and <c>source_net_amount</c> NETVALUE; enrichments are
/// signed by type; R022 gives one control and one tender per non-zero tender column.
/// </summary>
public static class CanonicalFactProjection
{
    /// <summary>The key naming the fact table of a canonical row.</summary>
    public const string FactTable = "fact_table";

    /// <summary>The canonical rows of one source row; empty for a landing-only family.</summary>
    /// <param name="storeCode">The import's store: R010 rows carry none of their own (planner 1 stamps the scope store).</param>
    /// <param name="snapshotDate">The snapshot date of a snapshot row (from its column or its block).</param>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(
        EtpReportFamily family, string storeCode, DateOnly? snapshotDate, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(values);
        object? V(string field) => values.GetValueOrDefault(field);
        object? Store() => V("store_code") ?? storeCode;
        return family.Identity?.Route switch
        {
            FamilyRoute.Sales =>
            [
                Row("sales_lines", ("store_code", Store()), ("document_number", V("invoice_number")),
                    ("transaction_date", V("transaction_date")), ("product_code", V("product_code")),
                    ("source_transaction_type", V("source_transaction_type")), ("source_quantity", V("source_quantity")),
                    ("source_gross_amount", V("source_net_amount")), ("source_net_amount", V("source_net_value")),
                    ("source_tax_amount", V("source_tax_amount")))
            ],
            FamilyRoute.Revenue => Revenue(values),
            FamilyRoute.Enrichment => [Enrichment(family.ReportCode, values, Store())],
            FamilyRoute.StockMovement =>
            [
                Row("stock_movements", ("store_code", Store()), ("document_number", V("document_number")),
                    ("document_date", V("document_date")), ("product_code", V("product_code")),
                    ("source_transaction_type", V("source_transaction_type")), ("from_location", V("from_location")),
                    ("to_location", V("to_location")), ("opening_quantity", V("opening_quantity")),
                    ("transaction_quantity", V("transaction_quantity")), ("closing_quantity", V("closing_quantity")))
            ],
            FamilyRoute.StockSnapshot when StockSnapshotFields.For(family.ReportCode) is { } fields =>
            [
                Row("stock_snapshots", ("store_code", fields.RowStore ? Store() : storeCode), ("snapshot_date", snapshotDate),
                    ("source_report_code", fields.SourceReportCode), ("product_code", V(fields.Product)),
                    ("ean", fields.Ean is null ? null : V(fields.Ean)), ("batch_number", V(fields.Batch)),
                    ("source_uid", V(fields.Uid)), ("quantity", V(fields.Quantity) ?? (fields.QuantityDefaultsToZero ? 0m : null)),
                    ("unit_cost", V(fields.UnitCost)), ("total_cost", V(fields.TotalCost)))
            ],
            _ => []
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Revenue(IReadOnlyDictionary<string, object?> values)
    {
        // The R022 projection planner 1 persists, so tender codes and the zero rule cannot drift.
        var projection = new R022PersistenceProjector().Project([new StagedImportRow(0, values)]);
        return projection.InvoiceControls.Select(control => Row("sales_invoice_controls", ("store_code", control.StoreCode),
                ("document_number", control.InvoiceNumber), ("transaction_date", control.TransactionDate),
                ("source_transaction_type", control.TransactionTypeRaw), ("source_invoice_quantity", control.InvoiceQuantity),
                ("source_net_value", control.NetValue)))
            .Concat(projection.ClassifiedTenders.Concat(projection.QuarantinedTenders).Select(tender => Row("sales_tenders",
                ("store_code", tender.StoreCode), ("document_number", tender.InvoiceNumber),
                ("transaction_date", tender.TransactionDate), ("tender_type", tender.TenderCode), ("source_amount", tender.SourceAmount))))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> Enrichment(string reportCode, IReadOnlyDictionary<string, object?> values, object? store)
    {
        // As RetailEnrichmentSqlImportOrchestrator persists: returns and buy-backs negative, whatever the export's sign.
        object? V(string field) => values.GetValueOrDefault(field);
        var sign = V("source_transaction_type") is "SR" or "BC" ? -1m : 1m;
        object? Signed(string field) => V(field) is decimal amount ? sign * Math.Abs(amount) : V(field);
        return Row("sales_line_enrichments", ("enrichment_type", reportCode), ("store_code", store),
            ("document_number", V("invoice_number")), ("transaction_date", V("transaction_date")), ("product_code", V("product_code")),
            ("source_transaction_type", V("source_transaction_type")), ("source_quantity", Signed("source_quantity")),
            ("source_net_value", Signed("source_net_value")), ("source_gross_value", Signed("source_net_amount")),
            ("source_cro_number", V("cro_number")), ("scheme_discount", V("scheme_discount")), ("user_discount", V("user_discount")),
            ("pre_discount", V("pre_discount")), ("other_charges", V("other_charges")));
    }

    private static IReadOnlyDictionary<string, object?> Row(string table, params (string Column, object? Value)[] columns)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal) { [FactTable] = table };
        foreach (var (column, value) in columns) row[column] = value;
        return row;
    }
}

/// <summary>
/// Where a stock-snapshot family keeps the fields <c>stock_snapshots</c> stores: R011 Closing Stock as
/// <c>StockWorkbookParser</c> reads it, R010 BinWise as <c>EtpFamilySqlImportOrchestrator</c> maps it.
/// </summary>
internal sealed record StockSnapshotFields(
    string SourceReportCode, bool RowStore, string Product, string? Ean, string Batch, string Uid, string Quantity,
    bool QuantityDefaultsToZero, string UnitCost, string TotalCost)
{
    private static readonly StockSnapshotFields ClosingStock = new(StockSnapshotSources.ClosingStock, true, "product_code", "ean",
        "batch_number", "source_uid", "quantity", false, "unit_cost", "total_cost");
    private static readonly StockSnapshotFields BinWise = new(StockSnapshotSources.BinWise, false, "itemnumber", null,
        "lotnumber", "uid", "closingbalance", true, "ucp", "totalucp");

    public static StockSnapshotFields? For(string reportCode) => reportCode switch
    {
        StockSnapshotSources.ClosingStock => ClosingStock,
        StockSnapshotSources.BinWise => BinWise,
        _ => null
    };
}
