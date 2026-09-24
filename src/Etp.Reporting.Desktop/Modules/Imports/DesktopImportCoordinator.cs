extern alias EtpApplication;

using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using FolderImportOptions = EtpApplication::Etp.Reporting.Application.Imports.FolderImportOptions;
using FolderImportProgress = EtpApplication::Etp.Reporting.Application.Imports.FolderImportProgress;
using FolderImportSummary = EtpApplication::Etp.Reporting.Application.Imports.FolderImportSummary;
using ImportPersistenceRequest = EtpApplication::Etp.Reporting.Application.Imports.ImportPersistenceRequest<Etp.Reporting.Import.Preflight.MatchedImportEnvelope>;
using ImportPersistenceResult = EtpApplication::Etp.Reporting.Application.Imports.ImportPersistenceResult;
using ImportPersistenceUseCase = EtpApplication::Etp.Reporting.Application.Imports.IImportPersistenceUseCase<Etp.Reporting.Import.Preflight.MatchedImportEnvelope>;
using ImportRestatement = EtpApplication::Etp.Reporting.Application.Imports.ImportRestatement;

namespace Etp.Reporting.Desktop.Modules.Imports;

public delegate Task RetainEtpEvidence(
    string connectionString,
    string workbookPath,
    string sourceSha256,
    string reportCode,
    string storeCode,
    DateOnly businessDate,
    CancellationToken cancellationToken);

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
    private readonly RetainEtpEvidence retainEvidence;
    private readonly IWorkbookReader workbookReader;
    private MatchedImportEnvelopeFactory envelopeFactory;
    private IReadOnlyList<string> knownStores = [];
    public void SetKnownStores(IReadOnlyList<string> stores) { knownStores=stores; envelopeFactory=new(stores); }
    private readonly IImportFailureClassifier failureClassifier;
    private BatchImportSource? activeBatchSource;
    private CancellationTokenSource? batchCancellation;
    private ValidatedImport? validatedImport;
    private FolderImportService? folderImportService;
    private FolderImportOptions? folderImportOptions;

    public DesktopImportCoordinator(
        Func<string, ImportPersistenceUseCase> persistenceFactory,
        RetainEtpEvidence retainEvidence,
        IWorkbookReader? workbookReader = null)
    {
        this.persistenceFactory = persistenceFactory ?? throw new ArgumentNullException(nameof(persistenceFactory));
        this.retainEvidence = retainEvidence ?? throw new ArgumentNullException(nameof(retainEvidence));
        this.workbookReader = workbookReader ?? new OpenXmlWorkbookReader();
        envelopeFactory = new MatchedImportEnvelopeFactory();
        failureClassifier = new SafeImportFailureClassifier();
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
            return new(reportCode, new ImportPersistenceResult(reportCode, 0), false, true);
        }
        var restatement = await ResolveRestatementAsync(
            persistence,
            current.Envelope.ProfileIdentity.ReportCode,
            context,
            cancellationToken).ConfigureAwait(false);
        var result = await persistence.PersistAsync(
            new ImportPersistenceRequest(
                current.Envelope,
                context.BusinessDate,
                context.StoreCode,
                context.ImportedBy,
                restatement),
            cancellationToken).ConfigureAwait(false);
        return new(current.Envelope.ProfileIdentity.ReportCode, result, restatement is not null);
    }

    public Task RetainValidatedEvidenceAsync(
        string connectionString,
        DesktopImportRunContext context,
        CancellationToken cancellationToken = default)
    {
        var current = validatedImport;
        return current is null
            ? Task.CompletedTask
            : retainEvidence(
                connectionString,
                current.WorkbookPath,
                current.Envelope.Workbook.Sha256,
                current.Envelope.ProfileIdentity.ReportCode,
                context.StoreCode,
                context.BusinessDate,
                cancellationToken);
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
        folderImportService = new FolderImportService(persistenceFactory(connectionString), workbookReader,
            (path, envelope, store, end, token) => retainEvidence(connectionString, path, envelope.Workbook.Sha256,
                envelope.ProfileIdentity.ReportCode, store, end, token), knownStores);
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

    private async Task<WorkbookImportOutcome> ProcessWorkbookAsync(
        string workbookPath,
        string connectionString,
        Func<bool> restatementEnabled,
        Func<DesktopImportRunContext> contextFactory,
        Func<CancellationToken, Task> recordRestatementAudit,
        CancellationToken cancellationToken)
    {
        var snapshot = await workbookReader.ReadAsync(workbookPath, cancellationToken).ConfigureAwait(false);
        var persistence = persistenceFactory(connectionString);
        var accepted = envelopeFactory.RequireAccepted(snapshot);
        var context = contextFactory();
        if (await persistence.ExistsInScopeAsync(snapshot.Sha256, accepted.ProfileIdentity.ReportCode,
            accepted.Scope.StoreCode ?? context.StoreCode, accepted.Scope.PeriodStart ?? context.BusinessDate,
            accepted.Scope.PeriodEnd ?? context.BusinessDate, cancellationToken).ConfigureAwait(false))
        {
            if (restatementEnabled())
                throw new ImportSourceException(
                    "RESTATEMENT_DUPLICATE_FILE",
                    "A restatement must use a corrected source file with a new hash.");
            await retainEvidence(connectionString, workbookPath, snapshot.Sha256, accepted.ProfileIdentity.ReportCode,
                accepted.Scope.StoreCode ?? context.StoreCode, accepted.Scope.PeriodEnd ?? context.BusinessDate,
                cancellationToken).ConfigureAwait(false);
            return new(0, 0, 0, 0, true);
        }

        var restatement = await ResolveRestatementAsync(
            persistence,
            accepted.ProfileIdentity.ReportCode,
            context,
            cancellationToken).ConfigureAwait(false);
        await persistence.PersistAsync(
            new ImportPersistenceRequest(
                accepted,
                context.BusinessDate,
                context.StoreCode,
                context.ImportedBy,
                restatement),
            cancellationToken).ConfigureAwait(false);
        if (restatement is not null) await recordRestatementAudit(cancellationToken).ConfigureAwait(false);
        await retainEvidence(
            connectionString,
            workbookPath,
            snapshot.Sha256,
            accepted.ProfileIdentity.ReportCode,
            context.StoreCode,
            context.BusinessDate,
            cancellationToken).ConfigureAwait(false);
        var outcome = await persistence.LoadOutcomeInScopeAsync(snapshot.Sha256, accepted.ProfileIdentity.ReportCode,
            accepted.Scope.StoreCode ?? context.StoreCode, accepted.Scope.PeriodStart ?? context.BusinessDate,
            accepted.Scope.PeriodEnd ?? context.BusinessDate, cancellationToken).ConfigureAwait(false);
        return new(
            outcome.RowsProcessed,
            outcome.NewRows,
            outcome.AlreadyPresentRows,
            outcome.ConflictRows,
            outcome.ExactDuplicate);
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
