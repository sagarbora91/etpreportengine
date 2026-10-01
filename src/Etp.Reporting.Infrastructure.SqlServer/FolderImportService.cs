using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>The same folder workflow is used by the desktop and command-line import.</summary>
public sealed class FolderImportService(
    IImportPersistenceUseCase<MatchedImportEnvelope> persistence,
    IWorkbookReader? workbookReader = null,
    Func<string, MatchedImportEnvelope, string, DateOnly, CancellationToken, Task>? retainEvidence = null,
    IReadOnlyList<string>? knownStores = null) : IFolderImportService
{
    private readonly IWorkbookReader reader = workbookReader ?? new OpenXmlWorkbookReader();
    private readonly MatchedImportEnvelopeFactory envelopes = new(knownStores);
    private readonly Dictionary<string, ImportScope> detectedScopes = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> FailedPaths { get; private set; } = [];

    public async Task<FolderImportSummary> RunAsync(string sourcePath, FolderImportOptions options,
        IProgress<FolderImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var source = await BatchImportSource.OpenAsync(sourcePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await RunFilesAsync(source.WorkbookPaths, options, progress, cancellationToken).ConfigureAwait(false);
    }

    public Task<FolderImportSummary> RunFilesAsync(IReadOnlyList<string> paths, FolderImportOptions options,
        IProgress<FolderImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        detectedScopes.Clear();
        return RunFilesCoreAsync(paths, options, progress, cancellationToken);
    }

    public Task<FolderImportSummary> RetryFailedAsync(FolderImportOptions options,
        IProgress<FolderImportProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunFilesCoreAsync(FailedPaths.ToArray(), options, progress, cancellationToken);

    private async Task<FolderImportSummary> RunFilesCoreAsync(IReadOnlyList<string> paths, FolderImportOptions options,
        IProgress<FolderImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (options.RestatementEnabled && string.IsNullOrWhiteSpace(options.RestatementReason))
            throw new ImportSourceException("RESTATEMENT_REASON_REQUIRED", "Enter the reason for the restatement.");
        if (!options.RestatementEnabled && (options.OverrideStoreCode is not null || options.OverrideBusinessDate is not null))
            throw new ImportSourceException("IMPORT_OVERRIDE_REQUIRES_RESTATEMENT", "Enable restatement before overriding the detected store or date.");

        var results = new List<FolderImportFileResult>();
        var recording = new AttemptRecording(persistence as IImportAttemptRecorder);
        var ready = new List<(string Path, MatchedImportInspection Inspection)>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (cancellationToken.IsCancellationRequested) break;
            progress?.Report(new(0, paths.Count, Path.GetFileName(path), "Reading folder", results.ToArray()));
            var readStage = FailureStage.Read;
            try
            {
                var workbook = await reader.ReadAsync(path, cancellationToken).ConfigureAwait(false);
                readStage = FailureStage.Match;
                var inspection = envelopes.Inspect(workbook);
                ready.Add((path, inspection));
                if (inspection.AcceptedImport is { } accepted) detectedScopes[path] = accepted.Scope;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                var failure = SqlImportFailures.Describe(exception, readStage);
                var failed = new FolderImportFileResult(Path.GetFileName(path), null, null, null, null, "Failed", Message: failure.SafeMessage)
                    { SourcePath = path, Failure = failure };
                results.Add(failed);
                handled.Add(path);
                await recording.RecordAsync(failed).ConfigureAwait(false);
            }
        }
        foreach (var entry in ready.OrderBy(item => DependencyOrder(item.Inspection.MatchedProfile?.ReportCode)))
        {
            if (cancellationToken.IsCancellationRequested) break;
            handled.Add(entry.Path);
            var accepted = entry.Inspection.AcceptedImport;
            var scope = accepted?.Scope;
            var issues = entry.Inspection.Diagnostics.Select(issue => issue.ToImportIssue()).ToArray();
            var result = new FolderImportFileResult(Path.GetFileName(entry.Path), entry.Inspection.MatchedProfile?.ReportCode,
                scope?.StoreCode, scope?.PeriodStart, scope?.PeriodEnd, "Importing", Diagnostics: issues)
                { SourcePath = entry.Path, SourceSha256 = accepted?.Workbook.Sha256 };
            progress?.Report(new(results.Count, paths.Count, result.FileName, "Importing", results.Append(result).ToArray()));
            if (accepted is null)
            {
                var unknown = issues.Any(issue => issue.Code is "LAYOUT_UNKNOWN" or "REQUIRED_COLUMN_MISSING" or "UNEXPECTED_COLUMN");
                var sourceCode = Regex.Match(result.FileName, @"(?:^|[^A-Z0-9])(R\d{3})(?:[^A-Z0-9]|$)", RegexOptions.IgnoreCase);
                var unsupportedFamily = sourceCode.Success && !EtpReportFamilyRegistry.Families.Any(family =>
                    family.FamilyCode.Equals(sourceCode.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                var notNeeded = result.FileName.StartsWith("00_", StringComparison.OrdinalIgnoreCase) || unsupportedFamily;
                result = result with { Status = notNeeded ? "Not needed" : unknown ? "Unknown layout" : "Failed",
                    Message = notNeeded ? unsupportedFamily ? "This ETP report type is not needed by the reporting engine; the other workbooks are processed." : "Consolidation control workbook; report workbooks are imported separately." : string.Join(" ", issues.Select(issue => issue.Message).Distinct()) };
                if (!notNeeded) result = result with { Failure = MatchFailure(issues) };
                results.Add(result);
                await recording.RecordAsync(result).ConfigureAwait(false);
                continue;
            }
            var stage = FailureStage.Scope;
            Guid? committedBatch = null;
            try
            {
                // A retry re-reads only failed files. Preserve the original sibling scope for
                // empty exports that depend on the other successful exports in their folder.
                var siblings = detectedScopes.Where(item => string.Equals(Path.GetDirectoryName(item.Key), Path.GetDirectoryName(entry.Path), StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Value).ToArray();
                if (scope?.StoreCode is { } detectedStore && options.OverrideStoreCode is { } overrideStore &&
                    !string.Equals(detectedStore, overrideStore, StringComparison.OrdinalIgnoreCase))
                    throw new ImportSourceException("STORE_OVERRIDE_MISMATCH", "The store override does not match the file. Use a corrected source file to change its store.");
                if (scope?.PeriodEnd is { } detectedDate && options.OverrideBusinessDate is { } overrideDate && detectedDate != overrideDate)
                    throw new ImportSourceException("DATE_OVERRIDE_MISMATCH", "The date override does not match the file. Use a corrected source file to change its date.");
                var store = scope?.StoreCode ?? options.OverrideStoreCode ?? siblings.Select(item => item!.StoreCode).FirstOrDefault(value => value is not null);
                var end = scope?.PeriodEnd ?? options.OverrideBusinessDate ?? siblings.Select(item => item!.PeriodEnd).Max();
                if (string.IsNullOrWhiteSpace(store) || end is null)
                    throw new ImportSourceException("SCOPE_NOT_DETECTED", "Store or date could not be detected. Keep this file beside the other exports for its store.");
                var persistedStore = scope?.StoreCode ?? store;
                var periodStart = scope?.PeriodStart ?? end.Value;
                var periodEnd = scope?.PeriodEnd ?? end.Value;
                if (await persistence.ExistsInScopeAsync(accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
                    persistedStore, periodStart, periodEnd, cancellationToken).ConfigureAwait(false))
                {
                    result = result with { StoreCode = persistedStore, PeriodStart = periodStart, PeriodEnd = periodEnd,
                        Status = "Duplicate", RowsProcessed = accepted.Staging.Rows.Count,
                        AlreadyPresentRows = accepted.Staging.Rows.Count, Message = "This file was already imported for this store and date range; no new rows." };
                    result = await RetainEvidenceAsync(result, entry.Path, accepted, persistedStore, periodEnd, cancellationToken).ConfigureAwait(false);
                    results.Add(result);
                    await recording.RecordAsync(result).ConfigureAwait(false);
                    continue;
                }
                stage = FailureStage.Plan;
                ImportRestatement? restatement = null;
                if (options.RestatementEnabled)
                {
                    var previous = await persistence.FindCurrentImportFileIdAsync(accepted.ProfileIdentity.ReportCode, store, end.Value, cancellationToken).ConfigureAwait(false);
                    if (previous is not null) restatement = new(previous.Value, options.ImportedBy, options.RestatementReason);
                }
                var request = new ImportPersistenceRequest<MatchedImportEnvelope>(accepted, end.Value, store, options.ImportedBy, restatement);
                if (restatement is not null)
                    await persistence.PrepareRestatementAsync(request, cancellationToken).ConfigureAwait(false);
                stage = FailureStage.Apply;
                var saved = await persistence.PersistAsync(request, cancellationToken).ConfigureAwait(false);
                // From here on the facts are committed: a later failure must not be recorded as a rollback.
                committedBatch = saved.BatchId;
                if (committedBatch is not null) stage = FailureStage.Commit;
                var outcome = saved.Status == "Imported"
                    ? await persistence.LoadOutcomeInScopeAsync(accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
                        persistedStore, periodStart, periodEnd, cancellationToken).ConfigureAwait(false)
                    : new ImportRowOutcome(accepted.Staging.Rows.Count, saved.PersistedRows, saved.AlreadyPresentRows, saved.ConflictRows);
                result = result with { StoreCode = persistedStore, PeriodStart = periodStart, PeriodEnd = periodEnd,
                    Status = accepted.Staging.Rows.Count == 0 && saved.Status == "Imported" ? "empty export" : saved.Status,
                    RowsProcessed = Math.Max(accepted.Staging.Rows.Count, outcome.RowsProcessed), NewRows = Math.Max(saved.PersistedRows, outcome.NewRows),
                    AlreadyPresentRows = outcome.AlreadyPresentRows, ConflictRows = outcome.ConflictRows, BatchId = saved.BatchId,
                    Evidence = saved.Evidence ?? result.Evidence, CommitState = saved.BatchId is null ? null : CommitState.Committed };
                if (outcome.ConflictRows > 0) result = result with { Status = "Failed", Message = $"{outcome.ConflictRows:N0} conflicting rows. Review the source before retrying.",
                    Failure = new(ImportCodes.ImportConflict, FailureStage.Apply, ImportDiagnosticCatalogue.Template(ImportCodes.ImportConflict)) };
                stage = FailureStage.Evidence;
                if (result.Status is "Imported" or "empty export" or "Duplicate" or "Duplicate content" or "Already present")
                    result = await RetainEvidenceAsync(result, entry.Path, accepted, persistedStore, periodEnd, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                result = Failed(result, exception, stage, committedBatch, cancellationToken.IsCancellationRequested);
            }
            results.Add(result);
            await recording.RecordAsync(result).ConfigureAwait(false);
            progress?.Report(new(results.Count, paths.Count, result.FileName, result.Status, results.ToArray()));
        }
        if (cancellationToken.IsCancellationRequested)
            foreach (var path in paths.Where(path => !handled.Contains(path)))
            {
                var cancelled = new FolderImportFileResult(Path.GetFileName(path), null, null, null, null, "Cancelled") { SourcePath = path };
                results.Add(cancelled);
                await recording.RecordAsync(cancelled).ConfigureAwait(false);
            }
        FailedPaths = results.Where(result => result.Failed).Select(result => result.SourcePath!).ToArray();
        recording.ThrowIfFailed();
        progress?.Report(new(results.Count, paths.Count, string.Empty, cancellationToken.IsCancellationRequested ? "Cancelled" : "Completed", results.ToArray()));
        return new(results);
    }

    /// <summary>
    /// The result of a file whose import threw. The commit state follows what is known: committed once
    /// persistence returned a batch (or said so through <see cref="ImportCommittedException"/>), rolled back
    /// for a failure inside the import transaction, and not stated when the failure could have hit the
    /// commit itself (a timeout or a broken connection).
    /// </summary>
    private static FolderImportFileResult Failed(FolderImportFileResult result, Exception exception, FailureStage stage,
        Guid? committedBatch, bool cancelled)
    {
        var committed = exception as ImportCommittedException;
        var cause = committed?.InnerException ?? exception;
        if (committed is not null)
        {
            committedBatch ??= committed.BatchId;
            stage = FailureStage.Commit;
        }
        var afterCommit = committedBatch is not null || committed is not null;
        if (cancelled && cause is OperationCanceledException)
            return afterCommit
                ? result with { Status = "Cancelled", Message = "Import cancelled after its data was committed.", BatchId = committedBatch,
                    CommitState = committedBatch is null ? CommitState.Unknown : CommitState.Committed }
                : result with { Status = "Cancelled", Message = "Import cancelled." };
        var failure = SqlImportFailures.Describe(cause, stage);
        var commitState = afterCommit ? committedBatch is null ? CommitState.Unknown : CommitState.Committed
            : failure.Code == ImportCodes.CommitOutcomeUnknown ? CommitState.Unknown
            : stage == FailureStage.Apply && !MayHaveReachedCommit(cause) ? CommitState.RolledBack
            : (CommitState?)null;
        return result with
        {
            Status = "Failed", Failure = failure, CommitState = commitState, BatchId = committedBatch ?? result.BatchId,
            Message = afterCommit ? $"The data was committed, but the import did not finish. {failure.SafeMessage}" : failure.SafeMessage,
            // A conflict rolls back the whole file; its full count is kept beside the samples (spec 11.1).
            ConflictRows = cause is ImportConflictException conflict ? conflict.Count : result.ConflictRows
        };
    }

    // A client timeout or a broken connection can strike while COMMIT is in flight, so the outcome is unknown
    // until it is checked again; any other failure inside the transaction rolls it back.
    private static bool MayHaveReachedCommit(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is TimeoutException || current is Microsoft.Data.SqlClient.SqlException { Number: SqlImportFailures.ClientTimeout } ||
                current is Microsoft.Data.SqlClient.SqlException { Class: >= 20 })
                return true;
        return false;
    }

    private async Task<FolderImportFileResult> RetainEvidenceAsync(FolderImportFileResult result, string path,
        MatchedImportEnvelope accepted, string store, DateOnly businessDate, CancellationToken cancellationToken)
    {
        if (retainEvidence is null) return result;
        try { await retainEvidence(path, accepted, store, businessDate, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return result with { Message = "Data is present; the original document could not be retained. Keep the source file and import it again to retry evidence retention." };
        }
        return result;
    }

    // A workbook no profile accepted: the first blocker names the failure. Its message comes from the
    // catalogue, because a layout message can quote a header.
    private static ImportFailure MatchFailure(IReadOnlyList<ImportIssue> issues)
    {
        var first = issues.FirstOrDefault(issue => issue.Severity == ImportIssueSeverity.Blocker) ?? issues.FirstOrDefault();
        var code = first?.Code ?? "IMPORT_LAYOUT_BLOCKED";
        return new(code, FailureStage.Match, ImportDiagnosticCatalogue.SafeMessage(code, first?.Message));
    }

    /// <summary>
    /// Records each attempt as soon as its file is finished (IF-017), so a run that stops part way still
    /// leaves the files it handled in History. A failed write does not stop the other files; the first
    /// one is rethrown when the run ends, as before.
    /// </summary>
    private sealed class AttemptRecording(IImportAttemptRecorder? recorder)
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

    private static int DependencyOrder(string? code) => code switch
    {
        "R025" => 0, "R020" => 1, "R022" => 2, "R024" => 3, "R013" => 4, "R003" => 5,
        "STOCK_LEDGER" or "R030" => 6, "R011" or "CLOSING_STOCK" => 7, _ => 10
    };
}
