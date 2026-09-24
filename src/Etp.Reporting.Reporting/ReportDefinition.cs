namespace Etp.Reporting.Reporting;

public enum ReportValueType { Text, Date, Integer, Decimal, Money, Percentage }
public enum ReportAggregation { None, Sum, CountDistinct }

public sealed record ReportParameterDefinition(string Id, string Label, ReportValueType ValueType, bool IsRequired = true);
public sealed record ReportColumnDefinition(string Id, string Label, ReportValueType ValueType,
    ReportAggregation Aggregation = ReportAggregation.None, string? MeasureId = null);

public sealed record ReportDefinition(string ReportId, string Name,
    IReadOnlyList<ReportParameterDefinition> Parameters, IReadOnlyList<ReportColumnDefinition> Columns,
    string ReconciliationControlId)
{
    public IReadOnlyList<string> MeasureIds => Columns.Where(x => x.MeasureId is not null)
        .Select(x => x.MeasureId!).Distinct(StringComparer.Ordinal).ToArray();
}

public static class ReportParameterIds
{
    public const string DateFrom = "date-from";
    public const string DateTo = "date-to";
    public const string StoreIds = "store-ids";
}

public static class SalesMeasureIds
{
    // Query-contract identifiers only; financial definitions are deliberately external.
    public const string NetSales = "sales.net-sales";
    public const string Units = "sales.units";
    public const string Bills = "sales.distinct-bills";
    public const string ContributionPercent = "sales.contribution-percent";
}


public sealed record ProductReportEntry(string Code,string Category,string Name,string Description);

public static class ProductReportCatalogue
{
    public static IReadOnlyList<ProductReportEntry> All { get; } =
    [
        new("dsr","Sales","Daily Sales / DSR","FTD, MTD and Indian-financial-year YTD with LY comparison."),
        new("sales-store","Sales","Store Sales Summary","Selected store, GST-inclusive daily sales."),
        new("sales-combined","Sales","Combined Sales Summary","Store comparison and combined scope."),
        new("invoice","Sales","Customer-wise Invoices","Customer names, invoice quantities and GST-inclusive values."),
        new("sales-returns","Sales","Returns","Source-signed sales returns."),
        new("sales-brand","Sales","Brand-wise Sales","Brand partition of canonical sales."),
        new("sales-segment","Sales","Brand-Segment Sales","CLUSTER/brand-segment partition."),
        new("sales-item","Sales","Item-wise Sales","Product-level canonical sales."),
        new("stock-closing","Stock","Closing Stock","Selected-date ETP closing snapshot."),
        new("stock-physical","Stock","Physical Stock","Independent physical-count evidence."),
        new("stock-variance","Stock","Stock Variance","Opening plus movements versus reported closing."),
        new("stock-movement","Stock","Stock Movement","Source transaction types and signed movement quantities."),
        new("stock-brand","Stock","Brand Stock","Closing quantity and cost by brand and segment."),
        new("stock-slow","Stock","Slow / Exception Stock","60-day watch and 90-day exception view."),
        new("staff","Staff","Staff/CRO Performance","Sales, targets, achievement, rank, LY growth and contribution."),
        new("tender","Tender / Cash","Tender Reconciliation","Revenue-control versus eligible tender totals."),
        new("cash","Tender / Cash","Cash Book","Daily Dr/Cr entries, tender modes and opening carry-forward."),
        new("tender-diagnostic","Tender / Cash","Tender Diagnostics","Non-destructive variance classification."),
        new("service","Service","Service Sales","Cash, card, UPI, FTD/MTD/YTD and LY growth."),
        new("exceptions","Exceptions","Daily Exception Report","All source, input and reconciliation findings."),
        new("management-trend","Management","Management Trend","Daily sales, units, invoices and control trends."),
        new("invoice-lineage","Investigation","Invoice Source Drill-down","Workbook, sheet and row lineage.")
    ];
}
