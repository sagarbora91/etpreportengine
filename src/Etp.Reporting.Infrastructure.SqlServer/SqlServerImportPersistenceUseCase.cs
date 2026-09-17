using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public enum ImportPersistenceRoute
{
    Revenue,
    Sales,
    Enrichment,
    Stock,
    Family
}

public sealed class SqlServerImportPersistenceUseCase : IImportPersistenceUseCase<MatchedImportEnvelope>, IImportAttemptRecorder
{
    private readonly ITransactionalImportStore store;
    private readonly SqlServerImportFileRepository files;
    private readonly OperationalCompletionRepository completion;
    private readonly string connectionString;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    public Task RecordAttemptAsync(FolderImportFileResult result, CancellationToken cancellationToken = default) =>
        new SqlServerImportHistoryQuery(connectionString).RecordAttemptAsync(result, cancellationToken);

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

    public async Task<bool> ExistsInScopeAsync(string sourceSha256, string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(false, cancellationToken).ConfigureAwait(false);
        return await files.ExistsInScopeAsync(sourceSha256, reportCode, storeCode, periodStart, periodEnd, cancellationToken).ConfigureAwait(false);
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
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AcceptedImport);
        await RequireImportAsync(request.Restatement is not null, cancellationToken).ConfigureAwait(false);
        _ = ApprovedImportProfileRegistry.Resolve(request.AcceptedImport.ProfileIdentity);
        var restatement = Map(request.Restatement);
        var accepted = request.AcceptedImport;
        var scope = R025SqlImportOrchestrator.ValidateScope(accepted.Scope.StoreCode, accepted.Scope.PeriodEnd,
            request.ExpectedStoreCode, request.ExpectedBusinessDate);
        var periodStart = accepted.Scope.PeriodStart ?? scope.BusinessDate!.Value;
        var periodEnd = scope.BusinessDate!.Value;
        if (await files.ExistsInScopeAsync(accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
            scope.StoreCode!, periodStart, periodEnd, cancellationToken).ConfigureAwait(false))
            return new(accepted.ProfileIdentity.ReportCode, 0) { Status = "Duplicate", AlreadyPresentRows = accepted.Staging.Rows.Count };
        var result = SelectRoute(request.AcceptedImport.ProfileIdentity.ReportCode) switch
        {
            ImportPersistenceRoute.Revenue => await PersistRevenueAsync(request, restatement, cancellationToken).ConfigureAwait(false),
            ImportPersistenceRoute.Stock => await PersistStockAsync(request, restatement, cancellationToken).ConfigureAwait(false),
            ImportPersistenceRoute.Enrichment => await PersistEnrichmentAsync(request, restatement, cancellationToken).ConfigureAwait(false),
            ImportPersistenceRoute.Family => await PersistFamilyAsync(request, restatement, cancellationToken).ConfigureAwait(false),
            _ => await PersistSalesAsync(request, restatement, cancellationToken).ConfigureAwait(false)
        };
        if (result.Status == "Duplicate") return result;
        var outcome = await files.LoadOutcomeInScopeAsync(accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
            scope.StoreCode!, periodStart, periodEnd, cancellationToken);
        return result with { PersistedRows=outcome.NewRows,
            Status=outcome.NewRows==0 && outcome.AlreadyPresentRows>0 ? "Duplicate content" : "Imported",
            AlreadyPresentRows=outcome.AlreadyPresentRows,ConflictRows=outcome.ConflictRows };
    }

    public async Task<ImportRowOutcome> LoadOutcomeByHashAsync(string sourceSha256, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(false, cancellationToken).ConfigureAwait(false);
        return Map(await files.LoadOutcomeByHashAsync(sourceSha256, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ImportRowOutcome> LoadOutcomeInScopeAsync(string sourceSha256, string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(false, cancellationToken).ConfigureAwait(false);
        return Map(await files.LoadOutcomeInScopeAsync(sourceSha256, reportCode, storeCode, periodStart, periodEnd, cancellationToken).ConfigureAwait(false));
    }

    public static ImportPersistenceRoute SelectRoute(string reportCode) => reportCode?.Trim().ToUpperInvariant() switch
    {
        "R022" => ImportPersistenceRoute.Revenue,
        "STOCK_LEDGER" or "CLOSING_STOCK" => ImportPersistenceRoute.Stock,
        "R003" or "R013" => ImportPersistenceRoute.Enrichment,
        "R025" => ImportPersistenceRoute.Sales,
        _ => ImportPersistenceRoute.Family
    };

    private async Task<ImportPersistenceResult> PersistFamilyAsync(ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,CancellationToken token)
    {
        var attempt = new AttemptStore(store);
        var fileId = await new EtpFamilySqlImportOrchestrator(attempt).PersistAsync(request.AcceptedImport,request.ExpectedBusinessDate,
            request.ExpectedStoreCode,request.ImportedBy,restatement,token);
        return await ClassifyAttemptAsync(new(request.AcceptedImport.ProfileIdentity.ReportCode,request.AcceptedImport.Staging.Rows.Count),
            attempt.BatchId, fileId, request.AcceptedImport.Staging.Rows.Count, token).ConfigureAwait(false);
    }

    public static ImportRowOutcome Map(WorkbookImportOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return new(outcome.RowsProcessed, outcome.NewRows, outcome.AlreadyPresentRows, outcome.ConflictRows, outcome.ExactDuplicate);
    }

    private async Task<ImportPersistenceResult> PersistRevenueAsync(
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var attempt = new AttemptStore(store);
        var fileId = await new R022SqlImportOrchestrator(attempt).PersistAsync(
            request.AcceptedImport,
            cancellationToken: cancellationToken,
            expectedBusinessDate: request.ExpectedBusinessDate,
            expectedStoreCode: request.ExpectedStoreCode,
            importedBy: request.ImportedBy,
            restatement: restatement).ConfigureAwait(false);
        var projection = new Etp.Reporting.Import.Staging.R022PersistenceProjector()
            .Project(request.AcceptedImport.Staging.Rows);
        return await ClassifyAttemptAsync(new(
            "R022",
            projection.InvoiceControls.Count + projection.ClassifiedTenders.Count + projection.QuarantinedTenders.Count,
            projection.InvoiceControls.Count,
            projection.ClassifiedTenders.Count,
            projection.QuarantinedTenders.Count), attempt.BatchId, fileId, request.AcceptedImport.Staging.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportPersistenceResult> PersistSalesAsync(
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new R025SqlImportOrchestrator(store).PersistAsync(
            request.AcceptedImport,
            cancellationToken: cancellationToken,
            expectedBusinessDate: request.ExpectedBusinessDate,
            expectedStoreCode: request.ExpectedStoreCode,
            importedBy: request.ImportedBy,
            restatement: restatement).ConfigureAwait(false);
        return await ClassifyAttemptAsync(new("R025", outcome.PersistedRows), outcome.BatchId, outcome.ImportFileId,
            request.AcceptedImport.Staging.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportPersistenceResult> PersistStockAsync(
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new StockSqlImportOrchestrator(store).PersistAsync(
            request.AcceptedImport,
            cancellationToken: cancellationToken,
            expectedBusinessDate: request.ExpectedBusinessDate,
            expectedStoreCode: request.ExpectedStoreCode,
            importedBy: request.ImportedBy,
            restatement: restatement).ConfigureAwait(false);
        return await ClassifyAttemptAsync(new(outcome.ReportCode, outcome.PersistedRows), outcome.BatchId, outcome.ImportFileId,
            request.AcceptedImport.Staging.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportPersistenceResult> PersistEnrichmentAsync(
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new RetailEnrichmentSqlImportOrchestrator(connectionString).PersistAsync(
            request.AcceptedImport,
            request.ExpectedBusinessDate,
            request.ExpectedStoreCode,
            request.ImportedBy,
            cancellationToken,
            restatement).ConfigureAwait(false);
        return await ClassifyAttemptAsync(new(
            outcome.ReportCode,
            outcome.PersistedRows,
            MatchedRows: outcome.MatchedRows,
            MissingMatches: outcome.MissingMatches,
            AmbiguousMatches: outcome.AmbiguousMatches), outcome.BatchId, outcome.ImportFileId,
            request.AcceptedImport.Staging.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportPersistenceResult> ClassifyAttemptAsync(ImportPersistenceResult result,
        Guid attemptedBatchId, long importFileId, int sourceRows, CancellationToken token)
    {
        // The SQL import lock can return a file created by another concurrent attempt.
        // Its immutable batch identity distinguishes that no-op from our own committed import.
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new SqlCommand("SELECT import_batch_id FROM dbo.import_files WHERE import_file_id=@file", connection);
        command.Parameters.AddWithValue("@file", importFileId);
        var savedBatch = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        if (savedBatch is not Guid batchId) throw new InvalidOperationException("The committed import file could not be found.");
        return batchId == attemptedBatchId ? result
            : new(result.ReportCode, 0) { Status = "Duplicate", AlreadyPresentRows = sourceRows };
    }

    private sealed class AttemptStore(ITransactionalImportStore inner) : ITransactionalImportStore
    {
        public Guid BatchId { get; private set; }
        public Task<long> PersistAsync(ImportPersistencePackage package, CancellationToken cancellationToken = default)
        {
            BatchId = package.Batch.BatchId;
            return inner.PersistAsync(package, cancellationToken);
        }
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
