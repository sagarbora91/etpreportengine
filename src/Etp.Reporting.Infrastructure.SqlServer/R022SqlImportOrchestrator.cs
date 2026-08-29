using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class R022SqlImportOrchestrator(ITransactionalImportStore store)
{
    public Task<long> PersistAsync(
        WorkbookSnapshot workbook,
        int? storeId = null,
        string currencyCode = "INR",
        CancellationToken cancellationToken = default,
        DateOnly? expectedBusinessDate = null,
        string? expectedStoreCode = null,
        string? importedBy = null,
        ImportRestatementRequest? restatement = null)
    {
        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);
        if (inspection.AcceptedImport is null) throw new SalesImportBlockedException(inspection.Diagnostics);
        return PersistAsync(
            inspection.AcceptedImport,
            storeId,
            currencyCode,
            cancellationToken,
            expectedBusinessDate,
            expectedStoreCode,
            importedBy,
            restatement);
    }

    public async Task<long> PersistAsync(
        MatchedImportEnvelope accepted,
        int? storeId = null,
        string currencyCode = "INR",
        CancellationToken cancellationToken = default,
        DateOnly? expectedBusinessDate = null,
        string? expectedStoreCode = null,
        string? importedBy = null,
        ImportRestatementRequest? restatement = null)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        _ = ApprovedImportProfileRegistry.Resolve(accepted.ProfileIdentity);
        if (!string.Equals(accepted.ProfileIdentity.ReportCode, "R022", StringComparison.Ordinal))
            throw new InvalidOperationException("The accepted import is not the approved R022 profile.");
        if (!accepted.Staging.CanPersist) throw new SalesImportBlockedException(accepted.Diagnostics);

        var projection = new R022PersistenceProjector().Project(accepted.Staging.Rows);
        var dates = projection.InvoiceControls.Select(x => x.TransactionDate).ToArray();
        var stores = projection.InvoiceControls.Select(x => x.StoreCode)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (stores.Length > 1) throw new InvalidOperationException("A source workbook cannot contain more than one store.");
        var businessDate = dates.Length == 0 ? (DateOnly?)null : dates.Max();
        var scope = R025SqlImportOrchestrator.ValidateScope(
            stores.SingleOrDefault(), businessDate, expectedStoreCode, expectedBusinessDate);
        var batchId = Guid.NewGuid();
        var batch = new ImportBatchRegistration(
            batchId,
            storeId,
            dates.Length == 0 ? null : dates.Min(),
            dates.Length == 0 ? null : dates.Max(),
            DateTimeOffset.UtcNow);
        var file = new ImportFileRegistration(
            batchId,
            accepted.ProfileIdentity,
            accepted.Workbook.FileName,
            accepted.Workbook.Sha256,
            accepted.Workbook.FileSizeBytes,
            StoreCode: scope.StoreCode,
            BusinessDate: scope.BusinessDate,
            SourceReportDate: scope.BusinessDate,
            ImportedBy: importedBy ?? Environment.UserName);
        var controls = projection.InvoiceControls.Select(x => new SalesInvoiceControlPersistence(
            x.StoreCode,
            x.InvoiceNumber,
            x.TransactionDate.Year,
            x.TransactionDate,
            x.TransactionTypeRaw,
            x.InvoiceQuantity,
            x.NetValue,
            currencyCode,
            new(accepted.MatchedSheet.Name, x.SourceRowNumber, "R022_INVOICE"))).ToArray();
        var tenders = projection.ClassifiedTenders.Concat(projection.QuarantinedTenders).Select(x =>
            new TenderPersistence(
                x.StoreCode,
                x.InvoiceNumber,
                x.TransactionDate.Year,
                x.TransactionDate,
                x.TenderCode,
                x.SourceAmount,
                currencyCode,
                new(accepted.MatchedSheet.Name, x.SourceRowNumber, $"R022_TENDER_{x.TenderCode}"),
                !x.IsQuarantined,
                x.QuarantineReason)).ToArray();
        var package = new ImportPersistencePackage(batch, file, [], tenders, [], [])
        {
            InvoiceControls = controls,
            Restatement = restatement
        };
        return await store.PersistAsync(package, cancellationToken).ConfigureAwait(false);
    }
}
