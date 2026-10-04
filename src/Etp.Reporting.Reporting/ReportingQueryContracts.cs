namespace Etp.Reporting.Reporting;

public sealed record ReportingQueryScope(
    DateOnly DateFrom,
    DateOnly DateTo,
    IReadOnlyList<string>? StoreCodes = null,
    IReadOnlyList<string>? BrandSegments = null,
    IReadOnlyList<string>? TransactionTypes = null,
    IReadOnlyList<string>? ItemCodes = null)
{
    public void Validate()
    {
        if (DateTo < DateFrom) throw new ArgumentException("The end date cannot precede the start date.");
        if (StoreCodes?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Store filters cannot contain blank values.");
        if (BrandSegments?.Any(string.IsNullOrWhiteSpace) == true || TransactionTypes?.Any(string.IsNullOrWhiteSpace) == true || ItemCodes?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Report filters cannot contain blank values.");
    }
}

public sealed record SalesQueryRow(
    DateOnly TransactionDate, string StoreCode, string DocumentNumber, string LineIdentifier,
    string ProductCode, string? Brand, string? BrandSegment, string? SourceTransactionType,
    decimal SourceQuantity, decimal? SourceGrossAmount, decimal? SourceNetAmount, int? InvoiceYear = null);
public sealed record TenderQueryRow(
    string StoreCode, string DocumentNumber, string TenderType, decimal SourceAmount, int? InvoiceYear = null);
public sealed record InvoiceControlQueryRow(
    string StoreCode, string DocumentNumber, decimal SourceNetValue, int? InvoiceYear = null);
/// <summary>
/// One stock key: every item with a ledger movement in the period. The closing is null when the store has no closing-stock
/// snapshot on the To date at all; it is 0 when the store has one and the item is not in it (sold out).
/// </summary>
public sealed record StockPositionQueryRow(
    string StoreCode, string ItemCode, decimal? SourceOpeningQuantity, decimal? SourceClosingQuantity);
// Location: the ledger bin the movements belong to (migration 0047); null for movements stored without one.
// HasSnapshot: whether the store has a closing-stock snapshot on the To date (owner answer Q9: movements of a store
// without one are listed and marked, not hidden).
public sealed record StockMovementQueryRow(
    string StoreCode, string ItemCode, string SourceMovementType, decimal SourceSignedQuantity, string? Location = null, bool HasSnapshot = true);
/// <summary>
/// The last date the store's stock ledger covers (the current ledger imports' period end, or its last movement; null when
/// none is stored), and the store's first sale after that date up to the To date (null when there is none). The import
/// stores a ledger's last row date as its period end, so a ledger exported to the To date "ends" on its last movement;
/// only a sale after that end shows the ledger is really short (Titan report audit R-13).
/// </summary>
public sealed record StockLedgerCoverageRow(string StoreCode, DateOnly? LedgerCoversTo, DateOnly? FirstSaleAfterLedger = null);
public sealed record StockQueryData(
    IReadOnlyList<StockPositionQueryRow> Positions, IReadOnlyList<StockMovementQueryRow> Movements,
    IReadOnlyList<StockLedgerCoverageRow>? LedgerCoverage = null);
/// <summary>A store-day with sales but no current R022 (Revenue Report) file covering it.</summary>
public sealed record TenderCoverageGapRow(string StoreCode, DateOnly BusinessDate);

public interface IReportingQueryRepository
{
    Task<IReadOnlyList<SalesQueryRow>> LoadSalesAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceControlQueryRow>> LoadInvoiceControlsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenderQueryRow>> LoadTendersAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default);
    Task<StockQueryData> LoadStockAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store-days in scope that have sales but no current R022 file. Tender controls and tenders both come from R022,
    /// so on these days the reconciliation would compare 0 with 0. The default reports no gaps for sources that have no import files.
    /// </summary>
    Task<IReadOnlyList<TenderCoverageGapRow>> LoadTenderCoverageGapsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenderCoverageGapRow>>([]);
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
