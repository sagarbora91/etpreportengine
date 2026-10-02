extern alias EtpApplication;

using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using FailureStage = EtpApplication::Etp.Reporting.Application.Imports.FailureStage;
using FolderImportOptions = EtpApplication::Etp.Reporting.Application.Imports.FolderImportOptions;
using FolderImportProgress = EtpApplication::Etp.Reporting.Application.Imports.FolderImportProgress;
using FolderImportSummary = EtpApplication::Etp.Reporting.Application.Imports.FolderImportSummary;
using ImportPersistenceRequest = EtpApplication::Etp.Reporting.Application.Imports.ImportPersistenceRequest<Etp.Reporting.Import.Preflight.MatchedImportEnvelope>;
using ImportPersistenceResult = EtpApplication::Etp.Reporting.Application.Imports.ImportPersistenceResult;
using ImportPersistenceUseCase = EtpApplication::Etp.Reporting.Application.Imports.IImportPersistenceUseCase<Etp.Reporting.Import.Preflight.MatchedImportEnvelope>;
using ImportRestatement = EtpApplication::Etp.Reporting.Application.Imports.ImportRestatement;
using EvidenceState = EtpApplication::Etp.Reporting.Application.Imports.EvidenceState;
using ImportEvidenceRetainer = EtpApplication::Etp.Reporting.Application.Imports.IImportEvidenceRetainer;

namespace Etp.Reporting.Desktop.Modules.Imports;

public sealed record DesktopImportRunContext(
    string StoreCode,
    DateOnly BusinessDate,
    string ImportedBy,
    bool RestatementEnabled,
    string RestatementReason);

public sealed record DesktopImportValidationOutcome(
    bool Accepted,
    string? ReportCode,
    int StagedRows,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public string? StoreCode { get; init; }
    public DateOnly? PeriodStart { get; init; }
    public DateOnly? PeriodEnd { get; init; }
}

public sealed record DesktopImportPersistenceOutcome(
    string ReportCode,
    ImportPersistenceResult Result,
    bool RestatementApplied,
    bool ExactDuplicate = false);

public sealed class DesktopImportCoordinator : IAsyncDisposable
{
    private readonly Func<string, ImportPersistenceUseCase> persistenceFactory;
    private readonly IWorkbookReader workbookReader;
    private MatchedImportEnvelopeFactory envelopeFactory;
    private IReadOnlyList<string> knownStores = [];
    public void SetKnownStores(IReadOnlyList<string> stores) { knownStores=stores; envelopeFactory=new(stores); }
    private readonly IImportFailureClassifier failureClassifier;
    private readonly Action<FolderImportFailure>? reportImportFailure;
    private BatchImportSource? activeBatchSource;
    private CancellationTokenSource? batchCancellation;
    private ValidatedImport? validatedImport;
    private FolderImportService? folderImportService;
    private FolderImportOptions? folderImportOptions;

    public DesktopImportCoordinator(
        Func<string, ImportPersistenceUseCase> persistenceFactory,
        IWorkbookReader? workbookReader = null,
        Action<FolderImportFailure>? reportImportFailure = null)
    {
        this.persistenceFactory = persistenceFactory ?? throw new ArgumentNullException(nameof(persistenceFactory));
        this.workbookReader = workbookReader ?? new OpenXmlWorkbookReader();
        this.reportImportFailure = reportImportFailure;
        envelopeFactory = new MatchedImportEnvelopeFactory();
        failureClassifier = new SqlImportFailureClassifier();
    }

    public bool HasValidatedImport => validatedImport is not null;
    public IReadOnlyList<string> FailedBatchPaths { get; private set; } = [];

    public async Task<DesktopImportValidationOutcome> ValidateAsync(
        string workbookPath,
        CancellationToken cancellationToken = default)
    {
        validatedImport = null;
        var snapshot = await workbookReader.ReadAsync(workbookPath, cancellationToken).ConfigureAwait(false);
        var inspection = envelopeFactory.Inspect(snapshot);
        validatedImport = inspection.AcceptedImport is null
            ? null
            : new(workbookPath, inspection.AcceptedImport);
        return new(
            inspection.Accepted,
            inspection.MatchedProfile?.ReportCode,
            inspection.StagedRows,
            inspection.Diagnostics)
        {
            StoreCode = inspection.AcceptedImport?.Scope.StoreCode,
            PeriodStart = inspection.AcceptedImport?.Scope.PeriodStart,
            PeriodEnd = inspection.AcceptedImport?.Scope.PeriodEnd
        };
    }

    public async Task<DesktopImportPersistenceOutcome> PersistValidatedAsync(
        string connectionString,
        DesktopImportRunContext context,
        CancellationToken cancellationToken = default)
    {
        var current = validatedImport ?? throw new InvalidOperationException("Validate an import workbook before persisting it.");
        // A single file has no folder siblings, and the selected business date is not a snapshot date (spec 6.4).
        current.Envelope.Scope.RequireOwnSnapshotDate();
        var persistence = persistenceFactory(connectionString);
        if (await persistence.ExistsInScopeAsync(current.Envelope.Workbook.Sha256, current.Envelope.ProfileIdentity.ReportCode,
            current.Envelope.Scope.StoreCode ?? context.StoreCode, current.Envelope.Scope.PeriodStart ?? context.BusinessDate,
            current.Envelope.Scope.PeriodEnd ?? context.BusinessDate, cancellationToken).ConfigureAwait(false))
        {
            if (context.RestatementEnabled)
                throw new ImportSourceException(
                    "RESTATEMENT_DUPLICATE_FILE",
                    "A restatement must use a corrected source file with a new hash.");
            var reportCode = current.Envelope.ProfileIdentity.ReportCode;
            return new(reportCode, new ImportPersistenceResult(reportCode, 0)
                { Evidence = await RetainDuplicateEvidenceAsync(persistence, current.Envelope.Workbook, cancellationToken).ConfigureAwait(false) },
                false, true);
        }
        var restatement = await ResolveRestatementAsync(
            persistence,
            current.Envelope.ProfileIdentity.ReportCode,
            context,
            cancellationToken).ConfigureAwait(false);
        var request = new ImportPersistenceRequest(current.Envelope, context.BusinessDate,
            context.StoreCode, context.ImportedBy, restatement);
        if (restatement is not null)
            await persistence.PrepareRestatementAsync(request, cancellationToken).ConfigureAwait(false);
        var result = await persistence.PersistAsync(request, cancellationToken).ConfigureAwait(false);
        return new(current.Envelope.ProfileIdentity.ReportCode, result, restatement is not null);
    }

    // IF-023 (spec 11.2): an import keeps the source bytes inside its own transaction. A file whose rows are
    // already stored keeps missing bytes in a small transaction of its own.
    // The rows are already stored, so a failed evidence write is recorded as NOT_RETAINED, never thrown.
    private static async Task<EvidenceState> RetainDuplicateEvidenceAsync(ImportPersistenceUseCase persistence,
        WorkbookSnapshot workbook, CancellationToken cancellationToken)
    {
        if (persistence is not ImportEvidenceRetainer retainer) return EvidenceState.NotAttempted;
        try
        {
            return await retainer.RetainImportedSourceAsync(workbook.Sha256, workbook.EvidenceBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return EvidenceState.NotRetained;
        }
    }

    public void ClearValidatedImport() => validatedImport = null;

    public async Task<FolderImportSummary> ImportFolderAsync(string sourcePath, string connectionString,
        FolderImportOptions options, IProgress<FolderImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        batchCancellation?.Dispose();
        batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var paths = await OpenBatchSourceAsync(sourcePath, batchCancellation.Token).ConfigureAwait(false);
        folderImportOptions = options;
        folderImportService = new FolderImportService(persistenceFactory(connectionString), workbookReader, knownStores, reportImportFailure);
        var result = await folderImportService.RunFilesAsync(paths, options, progress, batchCancellation.Token).ConfigureAwait(false);
        FailedBatchPaths = folderImportService.FailedPaths;
        return result;
    }

    public async Task<FolderImportSummary> RetryFailedFolderAsync(IProgress<FolderImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var service = folderImportService ?? throw new InvalidOperationException("Import a source before retrying failed files.");
        var options = folderImportOptions ?? throw new InvalidOperationException("The original import options are unavailable.");
        batchCancellation?.Dispose();
        batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var result = await service.RetryFailedAsync(options, progress, batchCancellation.Token).ConfigureAwait(false);
        FailedBatchPaths = service.FailedPaths;
        return result;
    }

    public async Task<IReadOnlyList<string>> OpenBatchSourceAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await DisposeBatchSourceAsync().ConfigureAwait(false);
        activeBatchSource = await BatchImportSource.OpenAsync(
            sourcePath,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return activeBatchSource.WorkbookPaths;
    }

    public async Task<BatchImportSummary> RunBatchAsync(
        IReadOnlyList<string> workbookPaths,
        string connectionString,
        Func<bool> restatementEnabled,
        Func<DesktopImportRunContext> contextFactory,
        Func<CancellationToken, Task> recordRestatementAudit,
        IProgress<BatchImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbookPaths);
        ArgumentNullException.ThrowIfNull(restatementEnabled);
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(recordRestatementAudit);
        batchCancellation?.Dispose();
        batchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var processor = new CoordinatorWorkbookImportOutcomeProcessor((path, token) => ProcessWorkbookAsync(
            path,
            connectionString,
            restatementEnabled,
            contextFactory,
            recordRestatementAudit,
            token));
        var coordinator = new BatchImportCoordinator(processor, failureClassifier);
        var summary = await coordinator.RunAsync(workbookPaths, progress, batchCancellation.Token).ConfigureAwait(false);
        FailedBatchPaths = workbookPaths.Zip(summary.Files)
            .Where(pair => pair.Second.Status == BatchImportFileStatus.Failed)
            .Select(pair => pair.First)
            .ToArray();
        return summary;
    }

    public (string Code, string SafeMessage) DescribeFailure(Exception exception) =>
        failureClassifier.Describe(exception);

    public void CancelBatch() => batchCancellation?.Cancel();

    public async Task DisposeBatchSourceAsync()
    {
        if (activeBatchSource is not null) await activeBatchSource.DisposeAsync().ConfigureAwait(false);
        activeBatchSource = null;
        folderImportService = null;
        folderImportOptions = null;
        FailedBatchPaths = [];
    }

    public async ValueTask DisposeAsync()
    {
        batchCancellation?.Cancel();
        batchCancellation?.Dispose();
        batchCancellation = null;
        await DisposeBatchSourceAsync().ConfigureAwait(false);
    }

    // The batch path reports its unexpected failures to diagnostics like the folder import does,
    // and marks a failure after a committed import as saved, so it is neither retried nor
    // described as a failed import (IF-014).
    private async Task<WorkbookImportOutcome> ProcessWorkbookAsync(
        string workbookPath,
        string connectionString,
        Func<bool> restatementEnabled,
        Func<DesktopImportRunContext> contextFactory,
        Func<CancellationToken, Task> recordRestatementAudit,
        CancellationToken cancellationToken)
    {
        var attempt = new BatchAttempt();
        try
        {
            return await ProcessWorkbookCoreAsync(workbookPath, connectionString, restatementEnabled, contextFactory,
                recordRestatementAudit, attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (attempt.Saved is { Status: not "Duplicate" } saved) SqlImportFailureClassifier.MarkCommitted(exception, saved.BatchId);
            if (reportImportFailure is not null && exception is not ImportSourceException)
            {
                try
                {
                    reportImportFailure(FolderImportFailure.Create(System.IO.Path.GetFileName(workbookPath), attempt.Stage, attempt.ReportCode,
                        attempt.StoreCode, attempt.PeriodEnd, exception));
                }
                catch (Exception sinkFailure) when (sinkFailure is not OperationCanceledException)
                {
                    // Diagnostics are best effort; a failing log must not change the import outcome.
                }
            }
            throw;
        }
    }

    private sealed class BatchAttempt
    {
        public FailureStage Stage { get; set; } = FailureStage.Read;
        public string? ReportCode { get; set; }
        public string? StoreCode { get; set; }
        public DateOnly? PeriodEnd { get; set; }
        public ImportPersistenceResult? Saved { get; set; }
    }

    private async Task<WorkbookImportOutcome> ProcessWorkbookCoreAsync(
        string workbookPath,
        string connectionString,
        Func<bool> restatementEnabled,
        Func<DesktopImportRunContext> contextFactory,
        Func<CancellationToken, Task> recordRestatementAudit,
        BatchAttempt attempt,
        CancellationToken cancellationToken)
    {
        var snapshot = await workbookReader.ReadAsync(workbookPath, cancellationToken).ConfigureAwait(false);
        var persistence = persistenceFactory(connectionString);
        var accepted = envelopeFactory.RequireAccepted(snapshot);
        accepted.Scope.RequireOwnSnapshotDate();
        var context = contextFactory();
        attempt.Stage = FailureStage.Apply;
        attempt.ReportCode = accepted.ProfileIdentity.ReportCode;
        attempt.StoreCode = accepted.Scope.StoreCode ?? context.StoreCode;
        attempt.PeriodEnd = accepted.Scope.PeriodEnd ?? context.BusinessDate;
        if (await persistence.ExistsInScopeAsync(snapshot.Sha256, accepted.ProfileIdentity.ReportCode,
            accepted.Scope.StoreCode ?? context.StoreCode, accepted.Scope.PeriodStart ?? context.BusinessDate,
            accepted.Scope.PeriodEnd ?? context.BusinessDate, cancellationToken).ConfigureAwait(false))
        {
            if (restatementEnabled())
                throw new ImportSourceException(
                    "RESTATEMENT_DUPLICATE_FILE",
                    "A restatement must use a corrected source file with a new hash.");
            await RetainDuplicateEvidenceAsync(persistence, snapshot, cancellationToken).ConfigureAwait(false);
            return new(0, 0, 0, 0, true);
        }

        var restatement = await ResolveRestatementAsync(
            persistence,
            accepted.ProfileIdentity.ReportCode,
            context,
            cancellationToken).ConfigureAwait(false);
        var request = new ImportPersistenceRequest(accepted, context.BusinessDate,
            context.StoreCode, context.ImportedBy, restatement);
        if (restatement is not null)
            await persistence.PrepareRestatementAsync(request, cancellationToken).ConfigureAwait(false);
        var saved = attempt.Saved = await persistence.PersistAsync(request, cancellationToken).ConfigureAwait(false);
        if (restatement is not null) await recordRestatementAudit(cancellationToken).ConfigureAwait(false);
        var outcome = await persistence.LoadOutcomeInScopeAsync(snapshot.Sha256, accepted.ProfileIdentity.ReportCode,
            accepted.Scope.StoreCode ?? context.StoreCode, accepted.Scope.PeriodStart ?? context.BusinessDate,
            accepted.Scope.PeriodEnd ?? context.BusinessDate, cancellationToken).ConfigureAwait(false);
        return new(
            outcome.RowsProcessed,
            outcome.NewRows,
            outcome.AlreadyPresentRows,
            outcome.ConflictRows,
            outcome.ExactDuplicate) { Issues = saved.Issues };
    }

    private static async Task<ImportRestatement?> ResolveRestatementAsync(
        ImportPersistenceUseCase persistence,
        string reportCode,
        DesktopImportRunContext context,
        CancellationToken cancellationToken)
    {
        if (!context.RestatementEnabled) return null;
        if (string.IsNullOrWhiteSpace(context.RestatementReason))
            throw new ImportSourceException(
                "RESTATEMENT_REASON_REQUIRED",
                "Enter the reason for the controlled restatement.");
        var previousImportFileId = await persistence.FindCurrentImportFileIdAsync(
            reportCode,
            context.StoreCode,
            context.BusinessDate,
            cancellationToken).ConfigureAwait(false);
        if (previousImportFileId is null)
            throw new ImportSourceException(
                "RESTATEMENT_SOURCE_NOT_FOUND",
                "No current import exists for this report, store and business date. Use a normal import instead.");
        return new(previousImportFileId.Value, context.ImportedBy, context.RestatementReason);
    }

    private sealed record ValidatedImport(
        string WorkbookPath,
        MatchedImportEnvelope Envelope);

    private sealed class CoordinatorWorkbookImportOutcomeProcessor(
        Func<string, CancellationToken, Task<WorkbookImportOutcome>> process) : IWorkbookImportOutcomeProcessor
    {
        public async Task ProcessAsync(string workbookPath, CancellationToken cancellationToken) =>
            _ = await process(workbookPath, cancellationToken).ConfigureAwait(false);

        public Task<WorkbookImportOutcome> ProcessWithOutcomeAsync(
            string workbookPath,
            CancellationToken cancellationToken) => process(workbookPath, cancellationToken);
    }
}
