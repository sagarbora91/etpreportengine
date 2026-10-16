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

public sealed partial class SqlServerImportPersistenceUseCase : IImportPersistenceUseCase<MatchedImportEnvelope>, IImportAttemptRecorder
{
    private readonly ITransactionalImportStore store;
    private readonly SqlServerImportFileRepository files;
    private readonly OperationalCompletionRepository completion;
    private readonly string connectionString;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    // Every connection this use case opens goes through the local SQL policy at the call, as the restatement
    // request does (Phase 5 re-audit, 26 Sep 2026: the approval query opened the stored string directly). The
    // constructor already stores the policy's own output, so this is the same string, checked where it is used.
    private SqlConnection LocalConnection() => new(LocalSqlConnectionPolicy.Validate(connectionString));

    /// <summary>The validated connection string, for the folder import's daily-inputs applier (family D001).</summary>
    internal string ConnectionString => connectionString;

    public Task RecordAttemptAsync(FolderImportFileResult result, CancellationToken cancellationToken = default) =>
        new SqlServerImportHistoryQuery(connectionString).RecordAttemptAsync(result, cancellationToken);

    public SqlServerImportPersistenceUseCase(string connectionString) : this(connectionString, null)
    {
    }

    internal SqlServerImportPersistenceUseCase(
        string connectionString,
        Func<CancellationToken, Task<ApplicationAccess>>? loadAccess,
        Func<SqlTransaction, CancellationToken, Task>? commit = null,
        string? commitCheckConnectionString = null)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        this.connectionString = validated;
        // Only tests replace the COMMIT, to reproduce a COMMIT whose reply never arrives, and the
        // connection of the check that follows it, to make that check fail.
        var transactional = new SqlServerTransactionalImportStore(validated) { CommitCheckConnectionString = commitCheckConnectionString };
        store = commit is null ? transactional : new SqlServerTransactionalImportStore(validated)
            { Commit = commit, CommitCheckConnectionString = commitCheckConnectionString };
        files = new SqlServerImportFileRepository(validated);
        completion = new OperationalCompletionRepository(validated);
        this.loadAccess = loadAccess ?? new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
    }

    public async Task<bool> ExistsByHashAsync(string sourceSha256, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        return await files.ExistsByHashAsync(sourceSha256, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsInScopeAsync(string sourceSha256, string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        return await files.ExistsInScopeAsync(sourceSha256, reportCode, storeCode, periodStart, periodEnd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long?> FindCurrentImportFileIdAsync(
        string reportCode,
        string storeCode,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        return (await completion.FindCurrentImportAsync(reportCode, storeCode, businessDate, cancellationToken).ConfigureAwait(false))?.ImportFileId;
    }

    public async Task<ImportPersistenceResult> PersistAsync(
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AcceptedImport);
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        _ = ApprovedImportProfileRegistry.Resolve(request.AcceptedImport.ProfileIdentity);
        var restatement = Map(request.Restatement);
        var accepted = request.AcceptedImport;
        var scope = R025SqlImportOrchestrator.ValidateScope(accepted.Scope.StoreCode, accepted.Scope.PeriodEnd,
            request.ExpectedStoreCode, request.ExpectedBusinessDate);
        var periodStart = accepted.Scope.PeriodStart ?? scope.BusinessDate!.Value;
        var periodEnd = scope.BusinessDate!.Value;
        if (restatement is not null)
            await RequireApprovedRestatementAsync(restatement, accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
                scope.StoreCode!, periodStart, periodEnd, cancellationToken).ConfigureAwait(false);
        if (await files.ExistsInScopeAsync(accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
            scope.StoreCode!, periodStart, periodEnd, cancellationToken).ConfigureAwait(false))
            return new(accepted.ProfileIdentity.ReportCode, 0) { Status = "Duplicate", AlreadyPresentRows = accepted.Staging.Rows.Count,
                Evidence = await DuplicateEvidenceAsync(accepted, cancellationToken).ConfigureAwait(false) };
        var attempt = new AttemptStore(store);
        Guid? ownBatch = null;
        ImportPersistenceResult duplicate;
        try
        {
            var result = SelectRoute(request.AcceptedImport.ProfileIdentity.ReportCode) switch
            {
                ImportPersistenceRoute.Revenue => await PersistRevenueAsync(attempt, request, restatement, cancellationToken).ConfigureAwait(false),
                ImportPersistenceRoute.Stock => await PersistStockAsync(attempt, request, restatement, cancellationToken).ConfigureAwait(false),
                ImportPersistenceRoute.Enrichment => await PersistEnrichmentAsync(attempt, request, restatement, cancellationToken).ConfigureAwait(false),
                ImportPersistenceRoute.Family => await PersistFamilyAsync(attempt, request, restatement, cancellationToken).ConfigureAwait(false),
                _ => await PersistSalesAsync(attempt, request, restatement, cancellationToken).ConfigureAwait(false)
            };
            // IF-028 / review M-3: a wrongly dated copy this import retired is said in the result and History, not only
            // "Duplicate" or "Imported", with any day that lost its stock reading by it.
            var retired = await RedatedRetirementIssuesAsync(attempt.Retired, cancellationToken).ConfigureAwait(false);
            if (retired.Count > 0) result = result with { Issues = [.. result.Issues, .. retired] };
            if (result.Status != "Duplicate")
            {
                ownBatch = result.BatchId;
                var outcome = await files.LoadOutcomeInScopeAsync(accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
                    scope.StoreCode!, periodStart, periodEnd, cancellationToken);
                var imported = result with { PersistedRows=outcome.NewRows,
                    Status=outcome.NewRows==0 && outcome.AlreadyPresentRows>0 ? "Duplicate content" : "Imported",
                    AlreadyPresentRows=outcome.AlreadyPresentRows,ConflictRows=outcome.ConflictRows,
                    Evidence=await ImportEvidenceAsync(accepted, scope.StoreCode!, periodStart, periodEnd, cancellationToken).ConfigureAwait(false) };
                await RequestDataQualitySyncAsync().ConfigureAwait(false);
                return imported;
            }
            duplicate = result;
        }
        // IF-014/IF-017: once the import transaction has committed, a failure checking which batch owns the
        // file or reading its result back is not a failed import, and never a rollback. The batch id goes
        // with it only once it is known to be ours; until then the attempt's outcome is unknown.
        catch (Exception failure) when (attempt.Committed && failure is not ImportCommittedException)
        {
            await RequestDataQualitySyncAsync().ConfigureAwait(false);
            throw new ImportCommittedException(ownBatch, failure);
        }
        // A failed import can leave a Failed batch, which the FAILED_IMPORT_BATCH check counts.
        catch (Exception)
        {
            await RequestDataQualitySyncAsync().ConfigureAwait(false);
            throw;
        }
        // A duplicate found under the import lock committed nothing of its own (IF-014); its missing
        // source bytes are kept in a transaction of their own (IF-023).
        return duplicate with { Evidence = await DuplicateEvidenceAsync(accepted, cancellationToken).ConfigureAwait(false) };
    }

    public async Task<ImportRowOutcome> LoadOutcomeByHashAsync(string sourceSha256, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        return Map(await files.LoadOutcomeByHashAsync(sourceSha256, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ImportRowOutcome> LoadOutcomeInScopeAsync(string sourceSha256, string reportCode, string storeCode,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default)
    {
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task<ImportPersistenceResult> PersistFamilyAsync(AttemptStore attempt,ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,CancellationToken token)
    {
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

    private async Task<ImportPersistenceResult> PersistRevenueAsync(AttemptStore attempt,
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
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

    private async Task<ImportPersistenceResult> PersistSalesAsync(AttemptStore attempt,
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new R025SqlImportOrchestrator(attempt).PersistAsync(
            request.AcceptedImport,
            cancellationToken: cancellationToken,
            expectedBusinessDate: request.ExpectedBusinessDate,
            expectedStoreCode: request.ExpectedStoreCode,
            importedBy: request.ImportedBy,
            restatement: restatement).ConfigureAwait(false);
        return await ClassifyAttemptAsync(new("R025", outcome.PersistedRows), outcome.BatchId, outcome.ImportFileId,
            request.AcceptedImport.Staging.Rows.Count, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImportPersistenceResult> PersistStockAsync(AttemptStore attempt,
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new StockSqlImportOrchestrator(attempt).PersistAsync(
            request.AcceptedImport,
            cancellationToken: cancellationToken,
            expectedBusinessDate: request.ExpectedBusinessDate,
            expectedStoreCode: request.ExpectedStoreCode,
            importedBy: request.ImportedBy,
            restatement: restatement).ConfigureAwait(false);
        var result = await ClassifyAttemptAsync(new(outcome.ReportCode, outcome.PersistedRows), outcome.BatchId, outcome.ImportFileId,
            request.AcceptedImport.Staging.Rows.Count, cancellationToken).ConfigureAwait(false);
        return result with { Issues = outcome.Warnings.Select(warning => warning.ToImportIssue()).ToArray() };
    }

    private async Task<ImportPersistenceResult> PersistEnrichmentAsync(AttemptStore attempt,
        ImportPersistenceRequest<MatchedImportEnvelope> request,
        ImportRestatementRequest? restatement,
        CancellationToken cancellationToken)
    {
        var outcome = await new RetailEnrichmentSqlImportOrchestrator(connectionString, attempt).PersistAsync(
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
        await using var connection = LocalConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        await using var command = new SqlCommand("SELECT import_batch_id FROM dbo.import_files WHERE import_file_id=@file", connection);
        command.Parameters.AddWithValue("@file", importFileId);
        var savedBatch = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        if (savedBatch is not Guid batchId) throw new InvalidOperationException("The committed import file could not be found.");
        return batchId == attemptedBatchId ? result with { BatchId = batchId }
            : new(result.ReportCode, 0) { Status = "Duplicate", AlreadyPresentRows = sourceRows };
    }

    /// <summary>
    /// One import's view of the store: the batch it sent, and whether its transaction committed,
    /// directly or as confirmed by the check after a failed COMMIT (IF-014).
    /// </summary>
    private sealed class AttemptStore(ITransactionalImportStore inner) : ITransactionalImportStore
    {
        public Guid BatchId { get; private set; }
        public bool Committed { get; private set; }
        /// <summary>The wrongly dated copies the committed import retired (IF-028); read only once it committed.</summary>
        public List<RedatedRetirement> Retired { get; } = [];
        public async Task<long> PersistAsync(ImportPersistencePackage package, CancellationToken cancellationToken = default)
        {
            BatchId = package.Batch.BatchId;
            Retired.Clear();
            var fileId = await inner.PersistAsync(package with { RedatedRetirements = Retired }, cancellationToken).ConfigureAwait(false);
            Committed = true;
            return fileId;
        }
    }

    /// <summary>
    /// One <see cref="ImportCodes.ImportRedatedRetired"/> issue per retired copy, read after the commit: the old and new
    /// dates, the days the old import had a reading for that now show none (<c>v_stock_snapshots_effective</c>), and its
    /// archived Closing Stock stand-in rows (<c>StockSnapshotFallback</c>). Warning when a day lost a reading.
    /// </summary>
    private async Task<IReadOnlyList<ImportIssue>> RedatedRetirementIssuesAsync(IReadOnlyList<RedatedRetirement> retired, CancellationToken token)
    {
        if (retired.Count == 0) return [];
        var issues = new List<ImportIssue>();
        await using var connection = LocalConnection();
        await connection.OpenAsync(token).ConfigureAwait(false);
        foreach (var retirement in retired)
        {
            await using var command = new SqlCommand(RedatedRetirementSql, connection);
            command.Parameters.AddWithValue("@previous", retirement.PreviousImportFileId);
            command.Parameters.AddWithValue("@replacement", retirement.ReplacementImportFileId);
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false)) continue;
            static IReadOnlyList<DateOnly> Days(string? list) => string.IsNullOrEmpty(list) ? []
                : list.Split(',').Select(day => DateOnly.ParseExact(day.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var lost = Days(reader.IsDBNull(4) ? null : reader.GetString(4));
            var fallbackRows = reader.GetInt32(5);
            var fallbackDays = Days(reader.IsDBNull(6) ? null : reader.GetString(6));
            var message = ImportDiagnosticCatalogue.RedatedRetiredMessage(retirement.PreviousImportFileId,
                reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1), reader.GetFieldValue<DateOnly>(2), reader.GetFieldValue<DateOnly>(3),
                lost, fallbackRows, fallbackDays);
            issues.Add(new(lost.Count > 0 || fallbackRows > 0 ? ImportIssueSeverity.Warning : ImportIssueSeverity.Information,
                ImportCodes.ImportRedatedRetired, message));
        }
        return issues;
    }

    private const string RedatedRetirementSql = """
        SELECT CONVERT(date,COALESCE(o.period_start,o.business_date)),CONVERT(date,COALESCE(o.period_end,o.business_date)),
               CONVERT(date,COALESCE(n.period_start,n.business_date)),CONVERT(date,COALESCE(n.period_end,n.business_date)),
               (SELECT STRING_AGG(CONVERT(char(10),d.snapshot_date,23),',') WITHIN GROUP(ORDER BY d.snapshot_date)
                FROM (SELECT DISTINCT TRY_CONVERT(date,JSON_VALUE(a.fact_json,'$.snapshot_date')) snapshot_date
                      FROM dbo.restatement_fact_archive a
                      WHERE a.import_restatement_id=r.import_restatement_id AND a.fact_type='StockSnapshot') d
                WHERE d.snapshot_date IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.v_stock_snapshots_effective e
                      WHERE e.store_code=o.store_code AND e.snapshot_date=d.snapshot_date)) lost_days,
               (SELECT COUNT(*) FROM dbo.restatement_fact_archive a
                WHERE a.import_restatement_id=r.import_restatement_id AND a.fact_type='StockSnapshotFallback') fallback_rows,
               (SELECT STRING_AGG(CONVERT(char(10),d.snapshot_date,23),',') WITHIN GROUP(ORDER BY d.snapshot_date)
                FROM (SELECT DISTINCT TRY_CONVERT(date,JSON_VALUE(a.fact_json,'$.snapshot_date')) snapshot_date
                      FROM dbo.restatement_fact_archive a
                      WHERE a.import_restatement_id=r.import_restatement_id AND a.fact_type='StockSnapshotFallback') d
                WHERE d.snapshot_date IS NOT NULL) fallback_days
        FROM dbo.import_restatements r
        JOIN dbo.import_files o ON o.import_file_id=r.previous_import_file_id
        JOIN dbo.import_files n ON n.import_file_id=r.replacement_import_file_id
        WHERE r.previous_import_file_id=@previous AND r.replacement_import_file_id=@replacement AND r.reason LIKE N're-dated:%';
        """;

    public static ImportRestatementRequest? Map(ImportRestatement? source)
    {
        if (source is null) return null;
        if (source.PreviousImportFileId <= 0 || string.IsNullOrWhiteSpace(source.RequestedBy) || string.IsNullOrWhiteSpace(source.Reason))
            throw new ArgumentException("A restatement requires the previous file, requesting user and reason.", nameof(source));
        return new(source.PreviousImportFileId, source.RequestedBy, source.Reason);
    }

    /// <summary>
    /// Titan store report audit item R-10 (3 Oct 2026): Open items read the saved data-quality issues, which were synced
    /// only when an Owner or Store Manager opened that screen, so a Viewer, or anyone before the next open, saw
    /// counts from before the latest imports. Only an Owner or Store Manager imports, and both may sync.
    /// A failed import syncs too: the FAILED_IMPORT_BATCH check counts failed batches.
    /// A failed sync never turns an import into a failed one, and the screen still syncs when an Owner or
    /// Store Manager opens it. Its own token, so a cancel after the commit does not leave the issues half-way.
    /// Inside a run opened by <see cref="DeferDataQualitySync"/> (a folder or batch import) the sync waits
    /// for the end of the run and happens once (review of FIX-10: the live checks scan every import, and a
    /// folder of a few hundred files ran them a few hundred times on a PC that powers off under heavy load).
    /// </summary>
    private Task RequestDataQualitySyncAsync()
    {
        if (deferredSync.Value is { } run)
        {
            run.Request(connectionString);
            return Task.CompletedTask;
        }
        return SyncDataQualityIssuesAsync(connectionString);
    }

    private static async Task SyncDataQualityIssuesAsync(string connectionString)
    {
        try
        {
            await ProductisationRepository.SyncDataQualityIssuesFromLiveChecksAsync(connectionString, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException or TimeoutException)
        {
            // Best effort; see above.
        }
    }

    private static readonly AsyncLocal<DataQualitySyncRun?> deferredSync = new();

    /// <summary>
    /// Opens an import run: imports made inside it (in this async flow, by any instance of this use case)
    /// sync the saved data-quality issues once, when the run is disposed, instead of after every file.
    /// Runs nest; only the outermost one syncs.
    /// </summary>
    public static DataQualitySyncRun DeferDataQualitySync() => DeferDataQualitySync(SyncDataQualityIssuesAsync);

    // Tests replace the sync itself, to count the syncs a run makes without a database.
    internal static DataQualitySyncRun DeferDataQualitySync(Func<string, Task> sync)
    {
        var run = new DataQualitySyncRun(deferredSync.Value, sync);
        deferredSync.Value = run;
        return run;
    }

    /// <summary>The run the current async flow is in, if any.</summary>
    internal static DataQualitySyncRun? CurrentDataQualitySyncRun => deferredSync.Value;

    public sealed class DataQualitySyncRun : IAsyncDisposable
    {
        private readonly DataQualitySyncRun? outer;
        private readonly Func<string, Task> sync;
        private string? pendingConnectionString;
        private bool disposed;

        internal DataQualitySyncRun(DataQualitySyncRun? outer, Func<string, Task> sync)
        {
            this.outer = outer;
            this.sync = sync;
        }

        /// <summary>Whether an import inside this run asked for a sync that has not happened yet.</summary>
        public bool SyncPending => Volatile.Read(ref pendingConnectionString) is not null;

        internal void Request(string connectionString) => Volatile.Write(ref pendingConnectionString, connectionString);

        // Not async: the restore of the AsyncLocal must reach the caller's flow.
        public ValueTask DisposeAsync()
        {
            if (disposed) return ValueTask.CompletedTask;
            disposed = true;
            deferredSync.Value = outer;
            var pending = Interlocked.Exchange(ref pendingConnectionString, null);
            if (pending is null) return ValueTask.CompletedTask;
            if (outer is not null) { outer.Request(pending); return ValueTask.CompletedTask; }
            return new ValueTask(sync(pending));
        }
    }

    private async Task RequireImportAsync(CancellationToken cancellationToken)
    {
        var access = await loadAccess(cancellationToken).ConfigureAwait(false);
        if (!access.CanImport)
            throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }
}
