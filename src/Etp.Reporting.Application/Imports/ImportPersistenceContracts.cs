namespace Etp.Reporting.Application.Imports;

public sealed record ImportRestatement(
    long PreviousImportFileId,
    string RequestedBy,
    string Reason);

public sealed record ImportPersistenceRequest<TAcceptedImport>(
    TAcceptedImport AcceptedImport,
    DateOnly ExpectedBusinessDate,
    string ExpectedStoreCode,
    string ImportedBy,
    ImportRestatement? Restatement = null)
    where TAcceptedImport : notnull;

public sealed record ImportPersistenceResult(
    string ReportCode,
    int PersistedRows,
    int InvoiceControls = 0,
    int ReportableTenderRows = 0,
    int QuarantinedTenderRows = 0,
    int MatchedRows = 0,
    int MissingMatches = 0,
    int AmbiguousMatches = 0)
{
    public string Status { get; init; } = "Imported";
    public int AlreadyPresentRows { get; init; }
    public int ConflictRows { get; init; }
}

public sealed record ImportRowOutcome(
    int RowsProcessed,
    int NewRows,
    int AlreadyPresentRows,
    int ConflictRows,
    bool ExactDuplicate = false);

public interface IImportPersistenceUseCase<TAcceptedImport> where TAcceptedImport : notnull
{
    Task<bool> ExistsByHashAsync(string sourceSha256, CancellationToken cancellationToken = default);
    Task<bool> ExistsInScopeAsync(string sourceSha256, string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default) =>
        ExistsByHashAsync(sourceSha256, cancellationToken);
    Task<long?> FindCurrentImportFileIdAsync(
        string reportCode,
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
    Task PrepareRestatementAsync(
        ImportPersistenceRequest<TAcceptedImport> request,
        CancellationToken cancellationToken = default);
    Task<ImportPersistenceResult> PersistAsync(
        ImportPersistenceRequest<TAcceptedImport> request,
        CancellationToken cancellationToken = default);
    Task<ImportRowOutcome> LoadOutcomeByHashAsync(string sourceSha256, CancellationToken cancellationToken = default);
    Task<ImportRowOutcome> LoadOutcomeInScopeAsync(string sourceSha256, string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default) =>
        LoadOutcomeByHashAsync(sourceSha256, cancellationToken);
}
