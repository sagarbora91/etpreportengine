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
    decimal SourceSignedNetAmount,
    int? InvoiceYear = null,
    string? BrandRow = null);

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

/// <summary>
/// One Sales Summary group. Owner decision 13 Q6: <see cref="Invoices"/> counts distinct documents with a sale (INV)
/// line, as the Daily Sales Report does; <see cref="Returns"/> counts distinct documents with a return (SR) or bill
/// cancellation (BC) line, so a cancelled bill (INV + BC) is 1 invoice and 1 return.
/// </summary>
public sealed record SalesSummaryRow(
    string Key,
    decimal SourceSignedQuantity,
    decimal SourceSignedNetAmount,
    int Invoices,
    int Returns);

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
                DistinctDocuments(group.Where(x => x.TransactionType == ReportingTransactionType.Sale)),
                DistinctDocuments(group.Where(x => x.TransactionType is ReportingTransactionType.Return or ReportingTransactionType.Cancellation))))
            .ToArray();

        return new(dimension, ReconciliationStatus.Passed, rows, policy.Version,
            "Aggregated source-signed values without sign transformation." + UnmappedNote(selected, dimension));
    }

    /// <summary>Prefix of a Brand-wise / Brand-Segment row for lines no brand row of the store claims.</summary>
    public const string UnmappedPrefix = "Unmapped: ";
    public const string BrandMasterLocation = "Settings > Stores & masters > Brands and targets";

    /// <summary>
    /// 1.9.8 (RA-SALES-03/04, RA-OPS-05): the Brand dimension is the owner's brand row (the DSR's rule), so Helios lines
    /// read SEIKO / CITIZEN / FOSSIL rather than one HELIOS row. A line no row claims is not folded into one "Other":
    /// it keeps its own "Unmapped: brand" row, split by cluster when the cluster is not the brand itself (for Helios the
    /// cluster is the watch brand: "Unmapped: HELIOS / CTZNL"), so the money stays visible by brand until the owner maps it.
    /// </summary>
    public static string BrandKey(SalesReportingLine line) => line.BrandRow is { Length: > 0 } row ? row : UnmappedKey(line);

    private static string UnmappedKey(SalesReportingLine line)
    {
        var brand = line.Brand.Trim(); var segment = line.BrandSegment.Trim();
        return segment.Length == 0 || string.Equals(segment, brand, StringComparison.OrdinalIgnoreCase)
            ? UnmappedPrefix + brand : $"{UnmappedPrefix}{brand} / {segment}";
    }

    /// <summary>
    /// Names every unmapped brand (per store, as the master is) with its value in the period and says where to map it,
    /// so the status line carries what the DSR's "Assign source brands in Settings" note only hints at.
    /// </summary>
    private static string UnmappedNote(IEnumerable<SalesReportingLine> selected, SalesSummaryDimension dimension)
    {
        if (dimension is not (SalesSummaryDimension.Brand or SalesSummaryDimension.BrandSegment)) return string.Empty;
        var unmapped = selected.Where(x => x.BrandRow is not { Length: > 0 })
            .GroupBy(x => (Store: x.StoreCode, Key: UnmappedKey(x)))
            .Select(x => (x.Key.Store, x.Key.Key, Value: x.Sum(l => l.SourceSignedNetAmount)))
            .OrderBy(x => x.Store, StringComparer.OrdinalIgnoreCase).ThenByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray();
        if (unmapped.Length == 0) return string.Empty;
        var listed = string.Join("; ", unmapped.Select(x => $"{x.Store} {x.Key[UnmappedPrefix.Length..]} {x.Value:N2}"));
        return $" Not in the brand-row master, shown as Unmapped rows ({unmapped.Length:N0}, {unmapped.Sum(x => x.Value):N2} in this period): {listed}. Map them in {BrandMasterLocation}.";
    }

    private static int DistinctDocuments(IEnumerable<SalesReportingLine> lines) =>
        lines.Select(x => $"{x.StoreCode}\u001f{x.InvoiceYear ?? (x.TransactionDate.Month >= 4 ? x.TransactionDate.Year + 1 : x.TransactionDate.Year)}\u001f{x.DocumentNumber}")
            .Distinct(StringComparer.Ordinal).Count();

    private static bool HasMissingRequiredDimension(SalesReportingLine line, SalesSummaryDimension dimension) =>
        string.IsNullOrWhiteSpace(line.StoreCode) || string.IsNullOrWhiteSpace(line.DocumentNumber) ||
        string.IsNullOrWhiteSpace(line.LineIdentifier) ||
        (dimension == SalesSummaryDimension.Brand && string.IsNullOrWhiteSpace(line.Brand) && string.IsNullOrWhiteSpace(line.BrandRow)) ||
        (dimension == SalesSummaryDimension.BrandSegment &&
            ((string.IsNullOrWhiteSpace(line.Brand) && string.IsNullOrWhiteSpace(line.BrandRow)) || string.IsNullOrWhiteSpace(line.BrandSegment))) ||
        (dimension == SalesSummaryDimension.Item && string.IsNullOrWhiteSpace(line.ItemCode));

    private static string DimensionKey(SalesReportingLine line, SalesSummaryDimension dimension) => dimension switch
    {
        SalesSummaryDimension.Daily => line.TransactionDate.ToString("yyyy-MM-dd"),
        SalesSummaryDimension.Store => line.StoreCode,
        SalesSummaryDimension.Brand => BrandKey(line),
        // An unmapped key already carries the cluster, so it is not repeated.
        SalesSummaryDimension.BrandSegment => line.BrandRow is { Length: > 0 } row ? $"{row} / {line.BrandSegment}" : UnmappedKey(line),
        SalesSummaryDimension.Item => line.ItemCode,
        SalesSummaryDimension.Returns => line.StoreCode,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    private static SalesSummaryResult Blocked(SalesSummaryDimension dimension, string version, string message) =>
        new(dimension, ReconciliationStatus.Blocked, [], version, message);
}
