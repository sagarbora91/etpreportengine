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

public static class InitialReportCatalogue
{
    private static readonly IReadOnlyList<ReportParameterDefinition> SalesParameters =
    [
        new(ReportParameterIds.DateFrom, "From date", ReportValueType.Date),
        new(ReportParameterIds.DateTo, "To date", ReportValueType.Date),
        new(ReportParameterIds.StoreIds, "Stores", ReportValueType.Text, false)
    ];

    public static ReportDefinition DailySales { get; } = new("RPT-SALES-001", "Daily Sales", SalesParameters,
    [
        new("date", "Date", ReportValueType.Date), new("store", "Store", ReportValueType.Text),
        new("net-sales", "Net Sales", ReportValueType.Money, ReportAggregation.Sum, SalesMeasureIds.NetSales),
        new("units", "Units", ReportValueType.Decimal, ReportAggregation.Sum, SalesMeasureIds.Units),
        new("bills", "Bills", ReportValueType.Integer, ReportAggregation.Sum, SalesMeasureIds.Bills)
    ], "sales.canonical-total");

    public static ReportDefinition BrandSales { get; } = CreateClassificationReport(
        "RPT-SALES-002", "Brand-Wise Sales", "brand", "Brand", "sales.brand-partition-total");
    public static ReportDefinition BrandSegmentSales { get; } = CreateClassificationReport(
        "RPT-SALES-003", "Brand-Segment Sales", "brand-segment", "Brand Segment", "sales.brand-segment-partition-total");
    public static IReadOnlyList<ReportDefinition> All { get; } = [DailySales, BrandSales, BrandSegmentSales];

    private static ReportDefinition CreateClassificationReport(string id, string name, string dimensionId,
        string dimensionLabel, string controlId) => new(id, name, SalesParameters,
        [
            new(dimensionId, dimensionLabel, ReportValueType.Text), new("store", "Store", ReportValueType.Text),
            new("net-sales", "Net Sales", ReportValueType.Money, ReportAggregation.Sum, SalesMeasureIds.NetSales),
            new("units", "Units", ReportValueType.Decimal, ReportAggregation.Sum, SalesMeasureIds.Units),
            new("contribution", "Contribution %", ReportValueType.Percentage, ReportAggregation.None, SalesMeasureIds.ContributionPercent)
        ], controlId);
}

public sealed record ProductReportEntry(string Code,string Category,string Name,string Description);

public static class ProductReportCatalogue
{
    public static IReadOnlyList<ProductReportEntry> All { get; } =
    [
        new("dsr","Sales","Daily Sales / DSR","FTD, MTD and Indian-financial-year YTD with LY comparison."),
        new("sales-titan","Sales","Titan Sales Summary","Titan World canonical NETVALUE sales."),
        new("sales-helios","Sales","Helios Sales Summary","Helios canonical NETVALUE sales."),
        new("sales-combined","Sales","Combined Sales Summary","Store comparison and combined scope."),
        new("invoice","Sales","Invoice Summary","Customer-safe invoice totals."),
        new("sales-returns","Sales","Returns","Source-signed sales returns."),
        new("sales-brand","Sales","Brand-wise Sales","Brand partition of canonical sales."),
        new("sales-segment","Sales","Brand-Segment Sales","CLUSTER/brand-segment partition."),
        new("sales-item","Sales","Item-wise Sales","Product-level canonical sales."),
        new("stock-closing","Stock","Closing Stock","Selected-date ETP closing snapshot."),
        new("stock-physical","Stock","Physical Stock","Independent physical-count evidence."),
        new("stock-variance","Stock","Stock Variance","Opening plus movements versus reported closing."),
        new("stock-movement","Stock","Stock Movement","Source transaction types and signed movement quantities."),
        new("stock-group","Stock","Inventory-Group Report","System and physical stock by inventory group."),
        new("stock-brand","Stock","Brand Stock","Closing quantity and cost by brand and segment."),
        new("stock-slow","Stock","Slow / Exception Stock","60-day watch and 90-day exception view."),
        new("staff","Staff","Staff/CRO Performance","Sales, targets, achievement, rank, LY growth and contribution."),
        new("tender","Tender / Cash","Tender Reconciliation","Revenue-control versus eligible tender totals."),
        new("cash","Tender / Cash","Daily Cash Reconciliation","Controlled cash evidence and closing variance."),
        new("tender-diagnostic","Tender / Cash","Tender Diagnostics","Non-destructive variance classification."),
        new("service","Service","Service Sales","Cash, card, UPI, FTD/MTD/YTD and LY growth."),
        new("exceptions","Exceptions","Daily Exception Report","All source, input and reconciliation findings."),
        new("exception-source","Exceptions","Missing Source Report","Required ETP sources not received."),
        new("exception-unmapped","Exceptions","Unmapped Data","Missing or ambiguous source enrichment."),
        new("exception-stock","Exceptions","Stock Exceptions","Physical/system/composition findings."),
        new("exception-staff","Exceptions","Staff Exceptions","Staff attribution findings."),
        new("exception-tender","Exceptions","Tender Exceptions","Invoice-level tender differences."),
        new("management-trend","Management","Management Trend","Daily sales, units, invoices and control trends."),
        new("invoice-lineage","Investigation","Invoice Source Drill-down","Workbook, sheet and row lineage.")
    ];
}
