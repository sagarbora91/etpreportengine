namespace Etp.Reporting.Application.Imports;

public sealed record ImportRestatement(
    long PreviousImportFileId,
    string RequestedBy,
    string Reason);

public sealed record ImportPersistenceRequest<TWorkbook>(
    TWorkbook Workbook,
    string ReportCode,
    DateOnly ExpectedBusinessDate,
    string ExpectedStoreCode,
    string ImportedBy,
    ImportRestatement? Restatement = null)
    where TWorkbook : notnull;

public sealed record ImportPersistenceResult(
    string ReportCode,
    int PersistedRows,
    int InvoiceControls = 0,
    int ReportableTenderRows = 0,
    int QuarantinedTenderRows = 0,
    int MatchedRows = 0,
    int MissingMatches = 0,
    int AmbiguousMatches = 0);

public sealed record ImportRowOutcome(
    int RowsProcessed,
    int NewRows,
    int AlreadyPresentRows,
    int ConflictRows,
    bool ExactDuplicate = false);

public interface IImportPersistenceUseCase<TWorkbook> where TWorkbook : notnull
{
    Task<bool> ExistsByHashAsync(string sourceSha256, CancellationToken cancellationToken = default);
    Task<long?> FindCurrentImportFileIdAsync(
        string reportCode,
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
    Task<ImportPersistenceResult> PersistAsync(
        ImportPersistenceRequest<TWorkbook> request,
        CancellationToken cancellationToken = default);
    Task<ImportRowOutcome> LoadOutcomeByHashAsync(string sourceSha256, CancellationToken cancellationToken = default);
}
