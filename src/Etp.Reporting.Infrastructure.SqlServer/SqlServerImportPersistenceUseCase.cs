using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

public enum ImportPersistenceRoute
{
    Revenue,
    Sales,
    Enrichment,
    Stock
}

public sealed class SqlServerImportPersistenceUseCase : IImportPersistenceUseCase<WorkbookSnapshot>
{
    private readonly ITransactionalImportStore store;
    private readonly SqlServerImportFileRepository files;
    private readonly OperationalCompletionRepository completion;
    private readonly string connectionString;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    public SqlServerImportPersistenceUseCase(string connectionString) : this(connectionString, null)
    {
    }

    internal SqlServerImportPersistenceUseCase(
        string connectionString,
        Func<CancellationToken, Task<ApplicationAccess>>? loadAccess)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        this.connectionString = validated;
        store = new SqlServerTransactionalImportStore(validated);
        files = new SqlServerImportFileRepository(validated);
        completion = new OperationalCompletionRepository(validated);
        this.loadAccess = loadAccess ?? new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
    }

    public async Task<bool> ExistsByHashAsync(string sourceSha256, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(false, cancellationToken).ConfigureAwait(false);
        return await files.ExistsByHashAsync(sourceSha256, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long?> FindCurrentImportFileIdAsync(
        string reportCode,
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(false, cancellationToken).ConfigureAwait(false);
        return (await completion.FindCurrentImportAsync(reportCode, storeCode, businessDate, cancellationToken).ConfigureAwait(false))?.ImportFileId;
    }

    public async Task<ImportPersistenceResult> PersistAsync(
        ImportPersistenceRequest<WorkbookSnapshot> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Workbook);
        await RequireImportAsync(request.Restatement is not null, cancellationToken).ConfigureAwait(false);
        var restatement = Map(request.Restatement);
        return SelectRoute(request.ReportCode) switch
        {
            ImportPersistenceRoute.Revenue => await PersistRevenueAsync(request, restatement, cancellationToken).ConfigureAwait(false),
            ImportPersistenceRoute.Stock => await PersistStockAsync(request, restatement, cancellationToken).ConfigureAwait(false),
            ImportPersistenceRoute.Enrichment => await PersistEnrichmentAsync(request, restatement, cancellationToken).ConfigureAwait(false),
            _ => await PersistSalesAsync(request, restatement, cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<ImportRowOutcome> LoadOutcomeByHashAsync(string sourceSha256, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(false, cancellationToken).ConfigureAwait(false);
        return Map(await files.LoadOutcomeByHashAsync(sourceSha256, cancellationToken).ConfigureAwait(false));
    }

    public static ImportPersistenceRoute SelectRoute(string reportCode) => reportCode?.Trim().ToUpperInvariant() switch
    {
        "R022" => ImportPersistenceRoute.Revenue,
        "STOCK_LEDGER" or "CLOSING_STOCK" => ImportPersistenceRoute.Stock,
        "R003" or "R013" => ImportPersistenceRoute.Enrichment,
        _ => ImportPersistenceRoute.Sales
    };

    public static ImportRowOutcome Map(WorkbookImportOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new(outcome.RowsProcessed, outcome.NewRows, outcome.AlreadyPresentRows, outcome.ConflictRows, outcome.ExactDuplicate);
    }

    private async Task<ImportPersistenceResult> PersistRevenueAsync(
        ImportPersistenceRequest<WorkbookSnapshot> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var preflight = new ImportPreflight().Inspect(request.Workbook, [RetailSalesProfiles.R022]);
        if (!preflight.CanImport)
            throw new ImportSourceException("IMPORT_LAYOUT_BLOCKED", "The workbook layout is not an approved ETP layout.");
        var staging = new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile!);
        if (!staging.CanPersist)
            throw new ImportSourceException("IMPORT_STAGING_BLOCKED", "Workbook rows failed validation.");
        var projection = new R022PersistenceProjector().Project(staging.Rows);
        await new R022SqlImportOrchestrator(store).PersistAsync(
            request.Workbook,
            preflight.Sheet!,
            projection,
            cancellationToken: cancellationToken,
            expectedBusinessDate: request.ExpectedBusinessDate,
            expectedStoreCode: request.ExpectedStoreCode,
            importedBy: request.ImportedBy,
            restatement: restatement).ConfigureAwait(false);
        return new(
            "R022",
            projection.InvoiceControls.Count + projection.ClassifiedTenders.Count + projection.QuarantinedTenders.Count,
            projection.InvoiceControls.Count,
            projection.ClassifiedTenders.Count,
            projection.QuarantinedTenders.Count);
    }

    private async Task<ImportPersistenceResult> PersistSalesAsync(
        ImportPersistenceRequest<WorkbookSnapshot> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new R025SqlImportOrchestrator(store).PersistAsync(
            request.Workbook,
            cancellationToken: cancellationToken,
            expectedBusinessDate: request.ExpectedBusinessDate,
            expectedStoreCode: request.ExpectedStoreCode,
            importedBy: request.ImportedBy,
            restatement: restatement).ConfigureAwait(false);
        return new("R025", outcome.PersistedRows);
    }

    private async Task<ImportPersistenceResult> PersistStockAsync(
        ImportPersistenceRequest<WorkbookSnapshot> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new StockSqlImportOrchestrator(store).PersistAsync(
            request.Workbook,
            cancellationToken: cancellationToken,
            expectedBusinessDate: request.ExpectedBusinessDate,
            expectedStoreCode: request.ExpectedStoreCode,
            importedBy: request.ImportedBy,
            restatement: restatement).ConfigureAwait(false);
        return new(outcome.ReportCode, outcome.PersistedRows);
    }

    private async Task<ImportPersistenceResult> PersistEnrichmentAsync(
        ImportPersistenceRequest<WorkbookSnapshot> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new RetailEnrichmentSqlImportOrchestrator(connectionString).PersistAsync(
            request.Workbook,
            request.ReportCode,
            request.ExpectedBusinessDate,
            request.ExpectedStoreCode,
            request.ImportedBy,
            cancellationToken,
            restatement).ConfigureAwait(false);
        return new(
            outcome.ReportCode,
            outcome.PersistedRows,
            MatchedRows: outcome.MatchedRows,
            MissingMatches: outcome.MissingMatches,
            AmbiguousMatches: outcome.AmbiguousMatches);
    }

    public static ImportRestatementRequest? Map(ImportRestatement? source)
    {
        if (source is null) return null;
        if (source.PreviousImportFileId <= 0 || string.IsNullOrWhiteSpace(source.RequestedBy) || string.IsNullOrWhiteSpace(source.Reason))
            throw new ArgumentException("A restatement requires the previous file, requesting user and reason.", nameof(source));
        return new(source.PreviousImportFileId, source.RequestedBy, source.Reason);
    }

    private async Task RequireImportAsync(bool ownerRequired, CancellationToken cancellationToken)
    {
        var access = await loadAccess(cancellationToken).ConfigureAwait(false);
        if (!access.CanImport)
            throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
        if (ownerRequired && !access.CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required for a controlled restatement.");
    }
}
