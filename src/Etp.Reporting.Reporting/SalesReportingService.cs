namespace Etp.Reporting.Reporting;

public enum ReportingTransactionType { Unknown = 0, Sale, Return, Cancellation }
public enum SalesSummaryDimension { Daily, Store, Brand, BrandSegment, Item, Returns }

public sealed record SalesReportingLine(
    DateOnly TransactionDate,
    string StoreCode,
    string DocumentNumber,
    string LineIdentifier,
    string Brand,
    string BrandSegment,
    string ItemCode,
    ReportingTransactionType TransactionType,
    decimal SourceSignedQuantity,
    decimal SourceSignedNetAmount);

public sealed record ApprovedSalesReportingPolicy(
    string Version,
    IReadOnlySet<ReportingTransactionType> IncludedTransactionTypes)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version)) throw new ArgumentException("An approved policy version is required.");
        if (IncludedTransactionTypes.Count == 0 || IncludedTransactionTypes.Contains(ReportingTransactionType.Unknown))
            throw new ArgumentException("Approved transaction types are required and cannot include Unknown.");
    }
}

public sealed record SalesSummaryRow(
    string Key,
    decimal SourceSignedQuantity,
    decimal SourceSignedNetAmount,
    int DistinctInvoices);

public sealed record SalesSummaryResult(
    SalesSummaryDimension Dimension,
    ReconciliationStatus Status,
    IReadOnlyList<SalesSummaryRow> Rows,
    string PolicyVersion,
    string Message);

public sealed class SalesReportingService
{
    public SalesSummaryResult Summarize(
        IEnumerable<SalesReportingLine> source,
        SalesSummaryDimension dimension,
        ApprovedSalesReportingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        var lines = source.ToArray();

        if (lines.Any(x => x.TransactionType == ReportingTransactionType.Unknown))
            return Blocked(dimension, policy.Version, "Unknown transaction types must be classified before reporting.");
        if (lines.Any(x => HasMissingRequiredDimension(x, dimension)))
            return Blocked(dimension, policy.Version, "Required reporting dimensions are missing.");

        var selected = lines.Where(x => policy.IncludedTransactionTypes.Contains(x.TransactionType));
        if (dimension == SalesSummaryDimension.Returns)
            selected = selected.Where(x => x.TransactionType == ReportingTransactionType.Return);

        var rows = selected
            .GroupBy(x => DimensionKey(x, dimension), StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new SalesSummaryRow(
                group.Key,
                group.Sum(x => x.SourceSignedQuantity),
                group.Sum(x => x.SourceSignedNetAmount),
                group.Select(x => $"{x.StoreCode}\u001f{x.DocumentNumber}").Distinct(StringComparer.Ordinal).Count()))
            .ToArray();

        return new(dimension, ReconciliationStatus.Passed, rows, policy.Version,
            "Aggregated source-signed values without sign transformation.");
    }

    private static bool HasMissingRequiredDimension(SalesReportingLine line, SalesSummaryDimension dimension) =>
        string.IsNullOrWhiteSpace(line.StoreCode) || string.IsNullOrWhiteSpace(line.DocumentNumber) ||
        string.IsNullOrWhiteSpace(line.LineIdentifier) ||
        (dimension == SalesSummaryDimension.Brand && string.IsNullOrWhiteSpace(line.Brand)) ||
        (dimension == SalesSummaryDimension.BrandSegment &&
            (string.IsNullOrWhiteSpace(line.Brand) || string.IsNullOrWhiteSpace(line.BrandSegment))) ||
        (dimension == SalesSummaryDimension.Item && string.IsNullOrWhiteSpace(line.ItemCode));

    private static string DimensionKey(SalesReportingLine line, SalesSummaryDimension dimension) => dimension switch
    {
        SalesSummaryDimension.Daily => line.TransactionDate.ToString("yyyy-MM-dd"),
        SalesSummaryDimension.Store => line.StoreCode,
        SalesSummaryDimension.Brand => line.Brand,
        SalesSummaryDimension.BrandSegment => $"{line.Brand} / {line.BrandSegment}",
        SalesSummaryDimension.Item => line.ItemCode,
        SalesSummaryDimension.Returns => line.StoreCode,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    private static SalesSummaryResult Blocked(SalesSummaryDimension dimension, string version, string message) =>
        new(dimension, ReconciliationStatus.Blocked, [], version, message);
}
