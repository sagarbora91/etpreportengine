namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record ImportBatchRegistration(Guid BatchId, int? StoreId, DateOnly? PeriodStart, DateOnly? PeriodEnd, DateTimeOffset StartedUtc);
public sealed record ImportFileRegistration(Guid BatchId, int? ImportProfileId, string OriginalFileName, string SourceSha256, long SizeBytes);

public sealed record SourceRowRegistration(string SheetName, int SourceRowNumber, string? SourceRecordType = null);

public sealed record SalesLinePersistence(
    string StoreCode, string DocumentNumber, int InvoiceYear, DateOnly TransactionDate,
    string LineIdentifier, string ProductCode, string? SourceTransactionType,
    decimal SourceQuantity, decimal? SourceGrossAmount, decimal? SourceNetAmount,
    string? SourceBrandCode, string? SourceBrandName, string? BrandSegment,
    string CurrencyCode, SourceRowRegistration Lineage);

public sealed record TenderPersistence(
    string StoreCode, string DocumentNumber, int InvoiceYear, DateOnly TransactionDate,
    string TenderType, decimal SourceAmount, string CurrencyCode, SourceRowRegistration Lineage,
    bool IsReportingEligible = true, string? ExclusionReason = null);

public sealed record SalesInvoiceControlPersistence(
    string StoreCode, string DocumentNumber, int InvoiceYear, DateOnly TransactionDate,
    string? SourceTransactionType, decimal SourceInvoiceQuantity, decimal SourceNetValue,
    string CurrencyCode, SourceRowRegistration Lineage);

public sealed record StockMovementPersistence(
    string StoreCode, string DocumentNumber, int InvoiceYear, DateOnly DocumentDate,
    string ProductCode, string SourceTransactionType, string? FromLocation, string? ToLocation,
    decimal OpeningQuantity, decimal TransactionQuantity, decimal ClosingQuantity,
    SourceRowRegistration Lineage);

public sealed record StockSnapshotPersistence(
    string StoreCode, DateOnly SnapshotDate, string ProductCode, string? Ean,
    string? BrandCode, string? BrandName, string? Cluster, string? Gender,
    string? BatchNumber, string? SourceUid, decimal Quantity, decimal? UnitCost,
    decimal? TotalCost, SourceRowRegistration Lineage);

public sealed record ImportPersistencePackage(
    ImportBatchRegistration Batch,
    ImportFileRegistration File,
    IReadOnlyList<SalesLinePersistence> SalesLines,
    IReadOnlyList<TenderPersistence> Tenders,
    IReadOnlyList<StockMovementPersistence> StockMovements,
    IReadOnlyList<StockSnapshotPersistence> StockSnapshots)
{
    public IReadOnlyList<SalesInvoiceControlPersistence> InvoiceControls { get; init; } = [];
}

public interface IImportBatchRepository
{
    Task CreateAsync(ImportBatchRegistration batch, CancellationToken cancellationToken = default);
    Task CompleteAsync(Guid batchId, int sourceRowCount, CancellationToken cancellationToken = default);
    Task FailAsync(Guid batchId, string reason, CancellationToken cancellationToken = default);
}

public interface IImportFileRepository
{
    Task<bool> ExistsByHashAsync(string sourceSha256, CancellationToken cancellationToken = default);
    Task<long> RegisterAsync(ImportFileRegistration file, CancellationToken cancellationToken = default);
}

public interface IReportingUnitOfWork
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default);
}

public interface ITransactionalImportStore
{
    Task<long> PersistAsync(ImportPersistencePackage package, CancellationToken cancellationToken = default);
}

public static class PersistenceValidation
{
    public static void Validate(ImportPersistencePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Batch.BatchId == Guid.Empty) throw new ArgumentException("Batch id is required.", nameof(package));
        if (package.File.BatchId != package.Batch.BatchId) throw new ArgumentException("The file must belong to the package batch.", nameof(package));
        if (package.File.SizeBytes < 0) throw new ArgumentException("File size cannot be negative.", nameof(package));
        SqlServerImportFileRepository.NormalizeHash(package.File.SourceSha256);
        if (package.Tenders.Any(x => string.Equals(x.TenderType, "PAYMENTTYPE25", StringComparison.OrdinalIgnoreCase) && x.IsReportingEligible))
            throw new ArgumentException("PAYMENTTYPE25 must be quarantined from reporting.", nameof(package));
        if (package.Tenders.Any(x => !x.IsReportingEligible && string.IsNullOrWhiteSpace(x.ExclusionReason)))
            throw new ArgumentException("A quarantined tender requires an exclusion reason.", nameof(package));
        foreach (var lineage in package.SalesLines.Select(x => x.Lineage)
                     .Concat(package.InvoiceControls.Select(x => x.Lineage))
                     .Concat(package.Tenders.Select(x => x.Lineage))
                     .Concat(package.StockMovements.Select(x => x.Lineage))
                     .Concat(package.StockSnapshots.Select(x => x.Lineage)))
        {
            if (string.IsNullOrWhiteSpace(lineage.SheetName) || lineage.SourceRowNumber <= 0)
                throw new ArgumentException("Every persisted row requires a sheet name and positive source row.", nameof(package));
        }
    }
}
