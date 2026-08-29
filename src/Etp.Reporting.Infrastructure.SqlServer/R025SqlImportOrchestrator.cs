using System.Globalization;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SalesImportBlockedException(IReadOnlyList<ImportDiagnostic> diagnostics)
    : InvalidOperationException($"Sales import was blocked: {string.Join(", ", diagnostics.Select(x => $"{x.Code}:{x.ColumnName}").Distinct())}")
{
    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; } = diagnostics;
}

public sealed record SalesImportPersistenceOutcome(Guid BatchId, long ImportFileId, int PersistedRows);

public sealed class R025SqlImportOrchestrator(ITransactionalImportStore store)
{
    public async Task<SalesImportPersistenceOutcome> PersistAsync(
        WorkbookSnapshot workbook, int? storeId = null, string currencyCode = "INR",
        CancellationToken cancellationToken = default, DateOnly? expectedBusinessDate = null,
        string? expectedStoreCode = null, string? importedBy = null,
        ImportRestatementRequest? restatement = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);
        if (inspection.AcceptedImport is null) throw new SalesImportBlockedException(inspection.Diagnostics);
        var accepted = inspection.AcceptedImport;
        return await PersistAsync(accepted, storeId, currencyCode, cancellationToken, expectedBusinessDate,
            expectedStoreCode, importedBy, restatement).ConfigureAwait(false);
    }

    public async Task<SalesImportPersistenceOutcome> PersistAsync(
        MatchedImportEnvelope accepted, int? storeId = null, string currencyCode = "INR",
        CancellationToken cancellationToken = default, DateOnly? expectedBusinessDate = null,
        string? expectedStoreCode = null, string? importedBy = null,
        ImportRestatementRequest? restatement = null)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        _ = ApprovedImportProfileRegistry.Resolve(accepted.ProfileIdentity);
        if (!string.Equals(accepted.ProfileIdentity.ReportCode, "R025", StringComparison.Ordinal))
            throw new InvalidOperationException("The accepted import is not the approved R025 profile.");
        if (!accepted.Staging.CanPersist) throw new SalesImportBlockedException(accepted.Diagnostics);

        var lines = accepted.Staging.Rows.Select(row =>
        {
            var values = row.Values;
            var date = Required<DateOnly>(values, "transaction_date");
            return new SalesLinePersistence(
                Required<string>(values, "store_code"), Required<string>(values, "invoice_number"),
                date.Year, date, row.SourceRowNumber.ToString(CultureInfo.InvariantCulture),
                Required<string>(values, "product_code"), Required<string>(values, "source_transaction_type"),
                Required<decimal>(values, "source_quantity"), null, Required<decimal>(values, "source_net_value"),
                Optional<string>(values, "source_brand_code"), Optional<string>(values, "source_brand_name"), Optional<string>(values, "brand_segment_code"),
                currencyCode, new(accepted.MatchedSheet.Name, row.SourceRowNumber, "R025_SALES_LINE"));
        }).ToArray();
        var dates = lines.Select(x => x.TransactionDate).ToArray();
        var storeCode = SingleStore(lines.Select(x => x.StoreCode));
        var businessDate = dates.Length == 0 ? (DateOnly?)null : dates.Max();
        (storeCode, businessDate) = ValidateScope(storeCode, businessDate, expectedStoreCode, expectedBusinessDate);
        var batchId = Guid.NewGuid();
        var package = new ImportPersistencePackage(
            new(batchId, storeId, dates.Length == 0 ? null : dates.Min(), dates.Length == 0 ? null : dates.Max(), DateTimeOffset.UtcNow),
            new(batchId, accepted.ProfileIdentity, accepted.Workbook.FileName, accepted.Workbook.Sha256, accepted.Workbook.FileSizeBytes,
                "R025", storeCode, businessDate, businessDate, importedBy ?? Environment.UserName),
            lines, [], [], []) { Restatement = restatement };
        var fileId = await store.PersistAsync(package, cancellationToken);
        return new(batchId, fileId, lines.Length);
    }

    private static T Required<T>(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is T typed ? typed : throw new InvalidOperationException($"Required staged field '{key}' is missing.");
    private static T? Optional<T>(IReadOnlyDictionary<string, object?> values, string key) where T : class =>
        values.TryGetValue(key, out var value) ? value as T : null;

    private static string? SingleStore(IEnumerable<string> values)
    {
        var stores = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return stores.Length switch
        {
            0 => null,
            1 => stores[0],
            _ => throw new InvalidOperationException("A source workbook cannot contain more than one store.")
        };
    }

    internal static (string? StoreCode, DateOnly? BusinessDate) ValidateScope(
        string? sourceStore, DateOnly? sourceDate, string? expectedStore, DateOnly? expectedDate)
    {
        expectedStore = string.IsNullOrWhiteSpace(expectedStore) ? null : expectedStore.Trim();
        if (sourceStore is not null && expectedStore is not null &&
            !string.Equals(sourceStore, expectedStore, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The workbook store does not match the selected store.");
        if (sourceDate is not null && expectedDate is not null && sourceDate != expectedDate)
            throw new InvalidOperationException("The ETP source report date does not match the selected business date.");
        return (sourceStore ?? expectedStore, sourceDate ?? expectedDate);
    }
}
