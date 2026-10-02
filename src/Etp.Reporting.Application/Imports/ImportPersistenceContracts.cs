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
    /// <summary>Whether the import transaction stored the source bytes (IF-023); null when it did not say.</summary>
    public EvidenceState? Evidence { get; init; }
    public Guid? BatchId { get; init; }
    /// <summary>Warnings the import raised while persisting, such as <c>STOCK_ROW_REPEATED</c>.</summary>
    public IReadOnlyList<ImportIssue> Issues { get; init; } = [];
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
    /// <summary>
    /// The current imports of this report and store whose declared period overlaps the replacement's (IF-016);
    /// a planner-1 restatement replaces one of them. The default keeps the older lookup by the last day.
    /// </summary>
    async Task<IReadOnlyList<RestatementCandidate>> FindRestatementCandidatesAsync(string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default) =>
        await FindCurrentImportFileIdAsync(reportCode, storeCode, periodEnd, cancellationToken).ConfigureAwait(false) is { } id
            ? [new(id, string.Empty, periodEnd, periodEnd, 0)]
            : [];
    /// <summary>
    /// Of <paramref name="importFileIds"/>, current imports a restatement's period covers but that it does not replace,
    /// those whose stored rows the replacement changes or drops. Planner 1 takes such an import over only when the
    /// replacement holds every row it holds; one it does not hold makes the restatement fail whichever import was
    /// picked, so the importer refuses before any approval is requested. The default finds none.
    /// </summary>
    Task<IReadOnlyList<long>> FindImportsChangedByAsync(TAcceptedImport accepted, IReadOnlyList<long> importFileIds,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<long>>([]);
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
