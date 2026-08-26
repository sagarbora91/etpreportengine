namespace Etp.Reporting.Reporting;

public sealed record ReportingQueryScope(DateOnly DateFrom, DateOnly DateTo, IReadOnlyList<string>? StoreCodes = null)
{
    public void Validate()
    {
        if (DateTo < DateFrom) throw new ArgumentException("The end date cannot precede the start date.");
        if (StoreCodes?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Store filters cannot contain blank values.");
    }
}

public sealed record SalesQueryRow(
    DateOnly TransactionDate, string StoreCode, string DocumentNumber, string LineIdentifier,
    string ProductCode, string? Brand, string? BrandSegment, string? SourceTransactionType,
    decimal SourceQuantity, decimal? SourceGrossAmount, decimal? SourceNetAmount);
public sealed record TenderQueryRow(
    string StoreCode, string DocumentNumber, string TenderType, decimal SourceAmount);
public sealed record InvoiceControlQueryRow(
    string StoreCode, string DocumentNumber, decimal SourceNetValue);
public sealed record StockPositionQueryRow(
    string StoreCode, string ItemCode, decimal? SourceOpeningQuantity, decimal? SourceClosingQuantity);
public sealed record StockMovementQueryRow(
    string StoreCode, string ItemCode, string SourceMovementType, decimal SourceSignedQuantity);
public sealed record StockQueryData(
    IReadOnlyList<StockPositionQueryRow> Positions, IReadOnlyList<StockMovementQueryRow> Movements);

public interface IReportingQueryRepository
{
    Task<IReadOnlyList<SalesQueryRow>> LoadSalesAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceControlQueryRow>> LoadInvoiceControlsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenderQueryRow>> LoadTendersAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default);
    Task<StockQueryData> LoadStockAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default);
}

public enum ApprovedSalesAmountSource { Gross, Net }

public sealed record ApprovedReportingMapping(
    string Version,
    ApprovedSalesAmountSource SalesAmountSource,
    IReadOnlyDictionary<string, ReportingTransactionType> SalesTransactionTypes,
    IReadOnlySet<string> TenderTypes,
    IReadOnlySet<string> StockMovementTypes)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version)) throw new ArgumentException("An approved mapping version is required.");
        if (SalesTransactionTypes.Count == 0 || SalesTransactionTypes.Values.Any(x => x == ReportingTransactionType.Unknown))
            throw new ArgumentException("Approved sales transaction mappings cannot be empty or map to Unknown.");
        if (TenderTypes.Count == 0) throw new ArgumentException("Approved tender types are required.");
        if (StockMovementTypes.Count == 0) throw new ArgumentException("Approved stock movement types are required.");
    }
}
