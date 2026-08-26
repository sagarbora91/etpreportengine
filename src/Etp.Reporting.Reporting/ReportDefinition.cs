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
