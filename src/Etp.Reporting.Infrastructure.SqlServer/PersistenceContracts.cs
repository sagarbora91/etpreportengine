using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record ImportBatchRegistration(Guid BatchId, int? StoreId, DateOnly? PeriodStart, DateOnly? PeriodEnd, DateTimeOffset StartedUtc);
public sealed record ImportFileRegistration(
    Guid BatchId,
    ImportProfileIdentity Profile,
    string OriginalFileName,
    string SourceSha256,
    long SizeBytes,
    string? ReportCode = null,
    string? StoreCode = null,
    DateOnly? BusinessDate = null,
    DateOnly? SourceReportDate = null,
    string? ImportedBy = null,
    DateOnly? PeriodStart = null,
    DateOnly? PeriodEnd = null);

public sealed record SourceRowRegistration(string SheetName, int SourceRowNumber, string? SourceRecordType = null);

public sealed record ImportRestatementRequest(long PreviousImportFileId, string RequestedBy, string Reason);

public sealed record SalesLinePersistence(
    string StoreCode, string DocumentNumber, int InvoiceYear, DateOnly TransactionDate,
    string LineIdentifier, string ProductCode, string? SourceTransactionType,
    decimal SourceQuantity, decimal? SourceGrossAmount, decimal? SourceNetAmount,
    string? SourceBrandCode, string? SourceBrandName, string? BrandSegment,
    string CurrencyCode, SourceRowRegistration Lineage,
    decimal? SourceTaxAmount = null);

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
    public ImportRestatementRequest? Restatement { get; init; }
    public Etp.Reporting.Import.Preflight.MatchedImportEnvelope? AcceptedImport { get; init; }
    public IReadOnlyList<EnrichmentPersistence> Enrichments { get; init; } = [];
}

public sealed record EnrichmentPersistence(string ReportCode, string StoreCode, DateOnly TransactionDate,
    string DocumentNumber, string ProductCode, string TransactionType, decimal Quantity,
    decimal NetValue, decimal GrossValue, string? CroNumber, string? StaffName,
    decimal? SchemeDiscount, decimal? UserDiscount, decimal? PreDiscount, decimal? OtherCharges,
    string? ActivationDetails, string? UserDiscountDetails, string ContentKey, SourceRowRegistration Lineage);

public interface IImportBatchRepository
{
    Task CreateAsync(ImportBatchRegistration batch, CancellationToken cancellationToken = default);
    Task CompleteAsync(Guid batchId, int sourceRowCount, CancellationToken cancellationToken = default);
    Task FailAsync(Guid batchId, string reason, CancellationToken cancellationToken = default);
}

public interface IImportFileRepository
{
    Task<bool> ExistsByHashAsync(string sourceSha256, CancellationToken cancellationToken = default);
    Task<bool> ExistsInScopeAsync(string sourceSha256, string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default) =>
        ExistsByHashAsync(sourceSha256, cancellationToken);
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
        _ = ResolveReportCode(package.File);
        if (package.File.SizeBytes < 0) throw new ArgumentException("File size cannot be negative.", nameof(package));
        SqlServerImportFileRepository.NormalizeHash(package.File.SourceSha256);
        if (package.Tenders.Any(x => !x.IsReportingEligible && string.IsNullOrWhiteSpace(x.ExclusionReason)))
            throw new ArgumentException("A quarantined tender requires an exclusion reason.", nameof(package));
        if (package.Restatement is { } restatement &&
            (restatement.PreviousImportFileId <= 0 || string.IsNullOrWhiteSpace(restatement.RequestedBy) || string.IsNullOrWhiteSpace(restatement.Reason)))
            throw new ArgumentException("A restatement requires the previous file, requesting user and reason.", nameof(package));
        foreach (var lineage in package.SalesLines.Select(x => x.Lineage)
                     .Concat(package.InvoiceControls.Select(x => x.Lineage))
                     .Concat(package.Tenders.Select(x => x.Lineage))
                     .Concat(package.StockMovements.Select(x => x.Lineage))
                     .Concat(package.StockSnapshots.Select(x => x.Lineage))
                     .Concat(package.Enrichments.Select(x => x.Lineage)))
        {
            if (string.IsNullOrWhiteSpace(lineage.SheetName) || lineage.SourceRowNumber <= 0)
                throw new ArgumentException("Every persisted row requires a sheet name and positive source row.", nameof(package));
        }
    }

    internal static string ResolveReportCode(ImportFileRegistration file)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(file.Profile);
        var approved = ApprovedImportProfileRegistry.Resolve(file.Profile);
        if (file.ReportCode is { } reportCode &&
            !string.Equals(reportCode.Trim(), approved.ReportCode, StringComparison.Ordinal))
            throw new ArgumentException(
                "The import file report code must match its exact approved profile identity.",
                nameof(file));
        return approved.ReportCode;
    }
}
