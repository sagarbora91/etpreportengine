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
using ImportAttemptRecorder = EtpApplication::Etp.Reporting.Application.Imports.IImportAttemptRecorder;
using FolderImportFileResult = EtpApplication::Etp.Reporting.Application.Imports.FolderImportFileResult;
using ImportRowOutcome = EtpApplication::Etp.Reporting.Application.Imports.ImportRowOutcome;
using CommitState = EtpApplication::Etp.Reporting.Application.Imports.CommitState;
using System.Runtime.ExceptionServices;

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
        this.workbookReader = workbookReader ?? new SourceFileReader();
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

    // IF-017: the single-file route records its attempt in import_attempts like the folder import, through
    // the same result builders, so History shows it with its code, stage, commit state and evidence.
    public async Task<DesktopImportPersistenceOutcome> PersistValidatedAsync(
        string connectionString,
        DesktopImportRunContext context,
        CancellationToken cancellationToken = default)
    {
        var current = validatedImport ?? throw new InvalidOperationException("Validate an import workbook before persisting it.");
        var envelope = current.Envelope;
        var persistence = persistenceFactory(connectionString);
        var attempt = Attempt(current.WorkbookPath, envelope, context);
        var stage = FailureStage.Scope;
        DesktopImportPersistenceOutcome outcome;
        FolderImportFileResult recorded;
        try
        {
            // A single file has no folder siblings, and the selected business date is not a snapshot date (spec 6.4).
            envelope.Scope.RequireOwnSnapshotDate();
            var reportCode = envelope.ProfileIdentity.ReportCode;
            if (await persistence.ExistsInScopeAsync(envelope.Workbook.Sha256, reportCode, attempt.StoreCode!, attempt.PeriodStart!.Value,
                attempt.PeriodEnd!.Value, cancellationToken).ConfigureAwait(false))
            {
                if (context.RestatementEnabled)
                    throw new ImportSourceException(
                        "RESTATEMENT_DUPLICATE_FILE",
                        "A restatement must use a corrected source file with a new hash.");
                var evidence = await RetainDuplicateEvidenceAsync(persistence, envelope.Workbook, cancellationToken).ConfigureAwait(false);
                outcome = new(reportCode, new ImportPersistenceResult(reportCode, 0) { Evidence = evidence }, false, true);
                recorded = DuplicateAttempt(attempt, envelope, evidence);
            }
            else
            {
                stage = FailureStage.Plan;
                var restatement = await ResolveRestatementAsync(
                    persistence,
                    reportCode,
                    context,
                    cancellationToken).ConfigureAwait(false);
                var request = new ImportPersistenceRequest(envelope, context.BusinessDate,
                    context.StoreCode, context.ImportedBy, restatement);
                if (restatement is not null)
                    await persistence.PrepareRestatementAsync(request, cancellationToken).ConfigureAwait(false);
                stage = FailureStage.Apply;
                var result = await persistence.PersistAsync(request, cancellationToken).ConfigureAwait(false);
                outcome = new(reportCode, result, restatement is not null);
                var rows = envelope.Staging.Rows.Count;
                recorded = FolderImportService.SavedAttempt(Committed(attempt, result), result, rows,
                    new ImportRowOutcome(rows, result.PersistedRows, result.AlreadyPresentRows, result.ConflictRows));
            }
        }
        catch (Exception exception)
        {
            var failed = FolderImportService.FailedAttempt(attempt, exception, stage, cancellationToken.IsCancellationRequested);
            await RecordAfterFailureAsync(persistence, failed, exception).ConfigureAwait(false);
            throw;
        }
        // As in the folder import, a failed write is reported once the import itself has finished.
        if (persistence is ImportAttemptRecorder recorder)
            await recorder.RecordAttemptAsync(recorded, CancellationToken.None).ConfigureAwait(false);
        return outcome;
    }

    // The attempt as it starts: the file, its report, and the scope the import uses (the file's own, else the run's).
    private static FolderImportFileResult Attempt(string workbookPath, MatchedImportEnvelope envelope, DesktopImportRunContext context) =>
        new(System.IO.Path.GetFileName(workbookPath), envelope.ProfileIdentity.ReportCode,
            envelope.Scope.StoreCode ?? context.StoreCode, envelope.Scope.PeriodStart ?? context.BusinessDate,
            envelope.Scope.PeriodEnd ?? context.BusinessDate, "Importing",
            Diagnostics: envelope.Diagnostics.Select(diagnostic => diagnostic.ToImportIssue()).ToArray())
        { SourcePath = workbookPath, SourceSha256 = envelope.Workbook.Sha256 };

    private static FolderImportFileResult DuplicateAttempt(FolderImportFileResult attempt, MatchedImportEnvelope envelope, EvidenceState evidence) =>
        attempt with
        {
            Status = "Duplicate", RowsProcessed = envelope.Staging.Rows.Count, AlreadyPresentRows = envelope.Staging.Rows.Count,
            Message = "This file was already imported for this store and date range; no new rows.", Evidence = evidence
        };

    // Only a duplicate found under the import lock commits nothing of its own (IF-014).
    private static FolderImportFileResult Committed(FolderImportFileResult attempt, ImportPersistenceResult saved) =>
        saved.Status == "Duplicate" ? attempt : attempt with { CommitState = CommitState.Committed, BatchId = saved.BatchId };

    // The import's own failure is what the caller sees; a failed write of its attempt goes with it as data.
    private static async Task RecordAfterFailureAsync(ImportPersistenceUseCase persistence, FolderImportFileResult failed, Exception exception)
    {
        if (persistence is not ImportAttemptRecorder recorder) return;
        try { await recorder.RecordAttemptAsync(failed, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception recordFailure)
        {
            exception.Data[AttemptRecordFailureKey] = $"{recordFailure.GetType().FullName}: {recordFailure.Message}";
        }
    }

    /// <summary>Exception.Data key holding a failure to record the attempt of an import that itself failed.</summary>
    public const string AttemptRecordFailureKey = "EtpAttemptRecordFailure";

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
        // Open items sync once for the whole batch, after its last file (review of FIX-10).
        await using var qualitySync = SqlServerImportPersistenceUseCase.DeferDataQualitySync();
        var recording = new BatchAttemptRecording(persistenceFactory(connectionString) as ImportAttemptRecorder);
        var processor = new CoordinatorWorkbookImportOutcomeProcessor((path, token) => ProcessWorkbookAsync(
            path,
            connectionString,
            restatementEnabled,
            contextFactory,
            recordRestatementAudit,
            recording,
            token));
        var coordinator = new BatchImportCoordinator(processor, failureClassifier);
        var summary = await coordinator.RunAsync(workbookPaths, progress, batchCancellation.Token).ConfigureAwait(false);
        // A file the cancel reached before it started is recorded as cancelled, as the folder import does.
        foreach (var (path, _) in workbookPaths.Zip(summary.Files).Where(pair => pair.Second is { Status: BatchImportFileStatus.Cancelled, Attempts: 0 }))
            await recording.RecordAsync(new FolderImportFileResult(System.IO.Path.GetFileName(path), null, null, null, null, "Cancelled")
                { SourcePath = path }).ConfigureAwait(false);
        FailedBatchPaths = workbookPaths.Zip(summary.Files)
            .Where(pair => pair.Second.Status == BatchImportFileStatus.Failed)
            .Select(pair => pair.First)
            .ToArray();
        recording.ThrowIfFailed();
        return summary;
    }

    /// <summary>
    /// Records each batch attempt as soon as it finishes (IF-017), retries included. A failed write does not
    /// stop the batch; the first one is rethrown when the batch ends, as the folder import does.
    /// </summary>
    private sealed class BatchAttemptRecording(ImportAttemptRecorder? recorder)
    {
        private ExceptionDispatchInfo? failure;

        public async Task RecordAsync(FolderImportFileResult result)
        {
            if (recorder is null) return;
            try { await recorder.RecordAttemptAsync(result, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { failure ??= ExceptionDispatchInfo.Capture(exception); }
        }

        public void ThrowIfFailed() => failure?.Throw();
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
        BatchAttemptRecording recording,
        CancellationToken cancellationToken)
    {
        var attempt = new BatchAttempt(workbookPath);
        WorkbookImportOutcome outcome;
        try
        {
            outcome = await ProcessWorkbookCoreAsync(workbookPath, connectionString, restatementEnabled, contextFactory,
                recordRestatementAudit, attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (attempt.Saved is { Status: not "Duplicate" } saved) SqlImportFailureClassifier.MarkCommitted(exception, saved.BatchId);
            // The same attempt the folder import records for this failure (IF-017).
            var failed = FolderImportService.FailedAttempt(attempt.Result, exception, attempt.Stage, cancellationToken.IsCancellationRequested);
            if (failed.Status == "Failed" && reportImportFailure is not null && exception is not ImportSourceException)
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
            await recording.RecordAsync(failed).ConfigureAwait(false);
            // SqlClient reports a cancelled command as SqlException 0; the batch then reports a cancel, as it is recorded.
            if (failed.Status == "Cancelled" && exception is not OperationCanceledException)
                throw new OperationCanceledException("Import cancelled.", exception, cancellationToken);
            throw;
        }
        await recording.RecordAsync(attempt.Completed!).ConfigureAwait(false);
        return outcome;
    }

    private sealed class BatchAttempt(string workbookPath)
    {
        public FailureStage Stage { get; set; } = FailureStage.Read;
        public string? ReportCode { get; set; }
        public string? StoreCode { get; set; }
        public DateOnly? PeriodEnd { get; set; }
        public ImportPersistenceResult? Saved { get; set; }
        /// <summary>The attempt as History records it; only the file name until the workbook is accepted.</summary>
        public FolderImportFileResult Result { get; set; } =
            new(System.IO.Path.GetFileName(workbookPath), null, null, null, null, "Importing") { SourcePath = workbookPath };
        /// <summary>The finished attempt of a file that did not throw.</summary>
        public FolderImportFileResult? Completed { get; set; }
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
        attempt.Stage = FailureStage.Match;
        var persistence = persistenceFactory(connectionString);
        var accepted = envelopeFactory.RequireAccepted(snapshot);
        var context = contextFactory();
        attempt.Result = Attempt(workbookPath, accepted, context);
        attempt.Stage = FailureStage.Scope;
        accepted.Scope.RequireOwnSnapshotDate();
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
            var evidence = await RetainDuplicateEvidenceAsync(persistence, snapshot, cancellationToken).ConfigureAwait(false);
            attempt.Completed = DuplicateAttempt(attempt.Result, accepted, evidence);
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
        // A failure from here on follows the save: the attempt keeps what the save reported (IF-014, IF-023).
        var rows = accepted.Staging.Rows.Count;
        attempt.Result = Committed(attempt.Result, saved) with { RowsProcessed = rows, NewRows = saved.PersistedRows,
            AlreadyPresentRows = saved.AlreadyPresentRows, ConflictRows = saved.ConflictRows, Evidence = saved.Evidence };
        if (restatement is not null) await recordRestatementAudit(cancellationToken).ConfigureAwait(false);
        var outcome = await persistence.LoadOutcomeInScopeAsync(snapshot.Sha256, accepted.ProfileIdentity.ReportCode,
            accepted.Scope.StoreCode ?? context.StoreCode, accepted.Scope.PeriodStart ?? context.BusinessDate,
            accepted.Scope.PeriodEnd ?? context.BusinessDate, cancellationToken).ConfigureAwait(false);
        attempt.Completed = FolderImportService.SavedAttempt(attempt.Result, saved, rows, outcome);
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
