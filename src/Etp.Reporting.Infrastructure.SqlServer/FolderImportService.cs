using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// An unexpected file failure for diagnostics. It carries the real exception, which the
/// import result replaces with a safe message, and never any workbook row data.
/// </summary>
public sealed record FolderImportFailure(string FileName, FailureStage Stage, string? ReportCode, string? StoreCode,
    DateOnly? PeriodEnd, Guid? BatchId, int? SqlErrorNumber, Exception Exception)
{
    /// <summary>What became of the import transaction, when the failure ended one (IF-014).</summary>
    public CommitState? CommitState { get; init; }

    /// <summary>
    /// The failure of one file as diagnostics record it. <paramref name="stage"/> is where the caller
    /// was; a failed COMMIT, or a failure after the work committed, is reported at the COMMIT stage.
    /// </summary>
    public static FolderImportFailure Create(string fileName, FailureStage stage, string? reportCode, string? storeCode,
        DateOnly? periodEnd, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(fileName, SqlTransactionGuard.StageOf(exception, stage), reportCode, storeCode, periodEnd,
            SqlTransactionGuard.BatchIdOf(exception), SqlImportFailureClassifier.SqlErrorNumber(exception), exception)
            { CommitState = SqlTransactionGuard.CommitStateOf(exception) };
    }
}

/// <summary>The same folder workflow is used by the desktop and command-line import.</summary>
public sealed class FolderImportService(
    IImportPersistenceUseCase<MatchedImportEnvelope> persistence,
    IWorkbookReader? workbookReader = null,
    IReadOnlyList<string>? knownStores = null,
    Action<FolderImportFailure>? reportFailure = null) : IFolderImportService
{
    private static readonly SqlImportFailureClassifier Classifier = new();
    private readonly IWorkbookReader reader = workbookReader ?? new SourceFileReader();
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
                var failure = Classifier.DescribeDetailed(exception, readStage);
                Report(Path.GetFileName(path), readStage, null, null, exception);
                var failed = new FolderImportFileResult(Path.GetFileName(path), null, null, null, null, "Failed", Message: failure.SafeMessage)
                    { SourcePath = path, Failure = failure, Evidence = EvidenceState.NotAttempted };
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
                result = result with { Evidence = EvidenceState.NotAttempted, Status = notNeeded ? "Not needed" : unknown ? "Unknown layout" : "Failed",
                    Message = notNeeded ? unsupportedFamily ? "This ETP report type is not needed by the reporting engine; the other workbooks are processed." : "Consolidation control workbook; report workbooks are imported separately." : string.Join(" ", issues.Select(issue => issue.Message).Distinct()) };
                if (!notNeeded) result = result with { Failure = MatchFailure(issues) };
                results.Add(result);
                await recording.RecordAsync(result).ConfigureAwait(false);
                continue;
            }
            var stage = FailureStage.Scope;
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
                // An undated snapshot takes its siblings' end date only when they agree on one (spec 6.4 tier 7); never a maximum.
                var undated = EtpReportFamilyRegistry.Resolve(accepted.ProfileIdentity.ReportCode).PrimaryDateHeader is null;
                var siblingEnds = siblings.Select(item => item!.PeriodEnd).OfType<DateOnly>().Distinct().ToArray();
                var siblingEnd = !undated ? siblings.Select(item => item!.PeriodEnd).Max() : siblingEnds.Length == 1 ? siblingEnds[0] : (DateOnly?)null;
                var end = scope?.PeriodEnd ?? options.OverrideBusinessDate ?? siblingEnd;
                if (undated && end is null)
                    throw siblingEnds.Length > 1
                        ? new ImportSourceException(ImportCodes.SnapshotDateAmbiguous, "The snapshot date could not be found, and the other exports in this folder end on different dates. Import the ETP file under its original name.")
                        : new ImportSourceException(ImportCodes.SnapshotDateUnknown, "The snapshot date could not be found. Import the ETP file under its original name, or keep it beside the other exports of its date.");
                // Tier 7 has its own basis (SIBLING), so the audit can tell it from a folder-name date. The override is not a
                // spec 6.4 tier; it stays an explicit Owner choice and is recorded as such.
                if (undated && scope?.PeriodEnd is null)
                    result = result with { Diagnostics = [.. result.Diagnostics ?? [], options.OverrideBusinessDate is not null
                        ? new ImportIssue(ImportIssueSeverity.Warning, ImportCodes.SnapshotDateFromOverride,
                            $"The snapshot date {end:yyyy-MM-dd} was taken from the date override; the file states no date of its own.")
                        : new ImportIssue(ImportIssueSeverity.Warning, ImportCodes.SnapshotDateFromSiblings,
                            $"The snapshot date {end:yyyy-MM-dd} was taken from the other exports in this folder.")] };
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
                    result = await RetainDuplicateEvidenceAsync(result, accepted, cancellationToken).ConfigureAwait(false);
                    results.Add(result);
                    await recording.RecordAsync(result).ConfigureAwait(false);
                    continue;
                }
                stage = FailureStage.Plan;
                ImportRestatement? restatement = null;
                if (options.RestatementEnabled)
                {
                    var candidates = await persistence.FindRestatementCandidatesAsync(accepted.ProfileIdentity.ReportCode,
                        persistedStore, periodStart, periodEnd, cancellationToken).ConfigureAwait(false);
                    var target = await ChooseRestatementTargetAsync(new(result.FileName, accepted.ProfileIdentity.ReportCode,
                        persistedStore, periodStart, periodEnd, candidates), options,
                        ids => persistence.FindImportsChangedByAsync(accepted, ids, end.Value, cancellationToken), cancellationToken).ConfigureAwait(false);
                    if (target.Refusal is { } refusal)
                    {
                        // The file fails below with the refusal's code; its issues keep the candidates for the Owner.
                        result = result with { Diagnostics = [.. issues, .. target.Issues ?? []] };
                        throw refusal;
                    }
                    restatement = new(target.Candidate!.ImportFileId, options.ImportedBy, options.RestatementReason);
                }
                var request = new ImportPersistenceRequest<MatchedImportEnvelope>(accepted, end.Value, store, options.ImportedBy, restatement);
                if (restatement is not null)
                    await persistence.PrepareRestatementAsync(request, cancellationToken).ConfigureAwait(false);
                stage = FailureStage.Apply;
                var saved = await persistence.PersistAsync(request, cancellationToken).ConfigureAwait(false);
                // Only a duplicate found before or under the import lock commits nothing of its own (IF-014).
                // Anything after this point reads back a saved import, so a failure there is not a failed import.
                if (saved.Status != "Duplicate")
                {
                    result = result with { CommitState = CommitState.Committed, BatchId = saved.BatchId };
                    stage = FailureStage.Commit;
                }
                // What the save itself reported is kept before the result is read back, so a failed read-back still
                // records the saved import's scope, counts and evidence (IF-017, IF-023), not zeros and no evidence.
                result = result with { StoreCode = persistedStore, PeriodStart = periodStart, PeriodEnd = periodEnd,
                    RowsProcessed = accepted.Staging.Rows.Count, NewRows = saved.PersistedRows,
                    AlreadyPresentRows = saved.AlreadyPresentRows, ConflictRows = saved.ConflictRows, Evidence = saved.Evidence };
                var outcome = saved.Status == "Imported"
                    ? await persistence.LoadOutcomeInScopeAsync(accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
                        persistedStore, periodStart, periodEnd, cancellationToken).ConfigureAwait(false)
                    : new ImportRowOutcome(accepted.Staging.Rows.Count, saved.PersistedRows, saved.AlreadyPresentRows, saved.ConflictRows);
                result = SavedAttempt(result, saved, accepted.Staging.Rows.Count, outcome);
            }
            catch (Exception exception)
            {
                if (result.CommitState == CommitState.Committed) SqlTransactionGuard.MarkCommitted(exception, result.BatchId);
                result = FailedAttempt(result, exception, stage, cancellationToken.IsCancellationRequested);
                if (result.Status == "Failed") Report(result.FileName, stage, result.ReportCode, scope, exception);
            }
            results.Add(result);
            await recording.RecordAsync(result).ConfigureAwait(false);
            progress?.Report(new(results.Count, paths.Count, result.FileName, result.Status, results.ToArray()));
        }
        if (cancellationToken.IsCancellationRequested)
            foreach (var path in paths.Where(path => !handled.Contains(path)))
            {
                var cancelled = new FolderImportFileResult(Path.GetFileName(path), null, null, null, null, "Cancelled")
                    { SourcePath = path, Evidence = EvidenceState.NotAttempted };
                results.Add(cancelled);
                await recording.RecordAsync(cancelled).ConfigureAwait(false);
            }
        FailedPaths = results.Where(result => result.Failed).Select(result => result.SourcePath!).ToArray();
        recording.ThrowIfFailed();
        progress?.Report(new(results.Count, paths.Count, string.Empty, cancellationToken.IsCancellationRequested ? "Cancelled" : "Completed", results.ToArray()));
        return new(results);
    }

    /// <summary>
    /// The result of a file whose import threw. The commit state is only the one the failure carries (IF-014:
    /// the import store marks a failure inside its transaction as rolled back, a checked COMMIT, a failure
    /// after the commit, or <see cref="ImportCommittedException"/>); a failure before any import transaction
    /// existed (access, approval, validation, opening the connection) states none. Source bytes are kept only
    /// inside a committed import (IF-023), so a file that did not commit records its evidence as not attempted.
    /// Used by the folder import and by the desktop single-file and batch routes, so every route records the
    /// same attempt for the same failure (IF-017).
    /// </summary>
    public static FolderImportFileResult FailedAttempt(FolderImportFileResult result, Exception exception, FailureStage stage, bool cancelled)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(exception);
        var known = SqlTransactionGuard.CommitStateOf(exception);
        var afterCommit = known is CommitState.Committed or CommitState.Unknown;
        var batchId = SqlTransactionGuard.BatchIdOf(exception);
        var cause = exception is ImportCommittedException { InnerException: { } inner } ? inner : exception;
        if (cancelled && IsCancellation(cause))
            return afterCommit
                ? result with { Status = "Cancelled", Message = "Import cancelled after its data was committed.", BatchId = batchId, CommitState = known,
                    Evidence = result.Evidence ?? EvidenceState.Unknown }
                : result with { Evidence = EvidenceState.NotAttempted, Status = "Cancelled", Message = "Import cancelled.", CommitState = known };
        var failure = Classifier.DescribeDetailed(exception, stage);
        return result with
        {
            Status = "Failed", Failure = failure, Message = failure.SafeMessage, CommitState = known, BatchId = batchId,
            // A committed import kept its bytes in its own transaction; when what it kept was never read back, say so.
            Evidence = afterCommit ? result.Evidence ?? EvidenceState.Unknown : EvidenceState.NotAttempted,
            // A conflict rolls back the whole file; its full count is kept beside the samples (spec 11.1).
            ConflictRows = cause is ImportConflictException conflict ? conflict.Count : result.ConflictRows
        };
    }

    /// <summary>
    /// The result of a file the import saved, or found already held under the import lock: its status, the
    /// counts read back in <paramref name="outcome"/>, the save's warnings, and its evidence (IF-023). Rows that
    /// conflict make it a failure. The caller sets the commit state and batch. Used by the folder import and by
    /// the desktop single-file and batch routes (IF-017).
    /// </summary>
    public static FolderImportFileResult SavedAttempt(FolderImportFileResult result, ImportPersistenceResult saved, int stagedRows,
        ImportRowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(outcome);
        result = result with { Status = stagedRows == 0 && saved.Status == "Imported" ? "empty export" : saved.Status,
            RowsProcessed = Math.Max(stagedRows, outcome.RowsProcessed), NewRows = Math.Max(saved.PersistedRows, outcome.NewRows),
            AlreadyPresentRows = outcome.AlreadyPresentRows, ConflictRows = outcome.ConflictRows };
        if (saved.Issues.Count > 0) result = result with { Diagnostics = [.. result.Diagnostics ?? [], .. saved.Issues] };
        if (outcome.ConflictRows > 0) result = result with { Status = "Failed", Message = $"{outcome.ConflictRows:N0} conflicting rows. Review the source before retrying.",
            Failure = new(ImportCodes.ImportConflict, FailureStage.Apply, ImportDiagnosticCatalogue.Template(ImportCodes.ImportConflict)) };
        return WithEvidence(result, saved.Evidence);
    }

    // SqlClient reports a token cancelled while a command runs as SqlException number 0 ("Operation cancelled
    // by user"), not as OperationCanceledException; both are the Owner's cancel when the run was cancelled.
    private static bool IsCancellation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is OperationCanceledException || current is Microsoft.Data.SqlClient.SqlException { Number: 0 })
                return true;
        return false;
    }

    // IF-023 (spec 11.2): a new import keeps the source bytes inside its own transaction and reports it in
    // ImportPersistenceResult.Evidence. A file whose rows are already stored keeps missing bytes in a small
    // transaction of its own. Either way a failure is recorded on the attempt, never swallowed.
    private async Task<FolderImportFileResult> RetainDuplicateEvidenceAsync(FolderImportFileResult result,
        MatchedImportEnvelope accepted, CancellationToken cancellationToken)
    {
        if (persistence is not IImportEvidenceRetainer retainer) return result with { Evidence = EvidenceState.NotAttempted };
        EvidenceState evidence;
        try
        {
            evidence = await retainer.RetainImportedSourceAsync(accepted.Workbook.Sha256, accepted.Workbook.EvidenceBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { evidence = EvidenceState.NotRetained; }
        return WithEvidence(result, evidence);
    }

    private static FolderImportFileResult WithEvidence(FolderImportFileResult result, EvidenceState? evidence)
    {
        if (evidence != EvidenceState.NotRetained) return result with { Evidence = evidence };
        const string message = "The rows are stored, but the source file could not be kept in the database. Import the same file again to keep it.";
        return result with
        {
            Evidence = evidence, Message = string.IsNullOrEmpty(result.Message) ? message : result.Message + " " + message,
            Diagnostics = [.. result.Diagnostics ?? [], new(ImportIssueSeverity.Warning, ImportCodes.EvidenceNotRetained, message)]
        };
    }

    // Refusals with their own code already explain themselves; anything else reaches the
    // operator only as a generic message, so the real exception goes to diagnostics.
    private void Report(string fileName, FailureStage stage, string? reportCode, ImportScope? scope, Exception exception)
    {
        if (reportFailure is null || exception is ImportSourceException) return;
        try
        {
            reportFailure(FolderImportFailure.Create(fileName, stage, reportCode, scope?.StoreCode, scope?.PeriodEnd, exception));
        }
        catch (Exception sinkFailure) when (sinkFailure is not OperationCanceledException)
        {
            // Diagnostics are best effort; a failing log must not change the import outcome.
        }
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

    // IF-016 interim (planner 1): a restatement replaces one current file whose declared period overlaps its own.
    // None is refused; one is used; several are put to whoever imports, and the others go through promotion as
    // before. Several with no picker (automation) or no pick are refused with the candidates listed. Promotion takes
    // an unpicked file over only when this file holds every row it holds, so one this file changes is refused here,
    // before any approval is requested, instead of failing in the planner with "Use Restate".
    private static async Task<RestatementTarget> ChooseRestatementTargetAsync(RestatementTargetChoice choice,
        FolderImportOptions options, Func<IReadOnlyList<long>, Task<IReadOnlyList<long>>> findChanged,
        CancellationToken cancellationToken)
    {
        var candidates = choice.Candidates;
        if (candidates.Count == 0)
        {
            var message = $"No current {choice.ReportCode} import for this store overlaps {choice.Period}, so nothing can be restated. Import the file without restatement.";
            return new(null, new(ImportCodes.RestatementMatchesNothing, message),
                [new(ImportIssueSeverity.Blocker, ImportCodes.RestatementMatchesNothing, message)]);
        }
        // The database restates (and the planner promotes over) only files wholly inside the replacement's period;
        // any partly overlapped current file makes every pick fail, so refuse before asking.
        var uncovered = candidates.Where(candidate => !candidate.IsCoveredBy(choice.PeriodStart, choice.PeriodEnd)).ToArray();
        if (uncovered.Length > 0)
        {
            var message = $"This file's period {choice.Period} only partly overlaps current {choice.ReportCode} import(s) "
                + $"{string.Join(", ", uncovered.Select(candidate => candidate.ImportFileId))}, so it cannot replace them. "
                + "Import a file whose period covers them fully. Nothing was changed.";
            return new(null, new(ImportCodes.RestatementTargetNotCovered, message),
                uncovered.Select(candidate => new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.RestatementTargetNotCovered,
                    $"Partly overlapped current import {candidate.ImportFileId}: {candidate.FileName}, {candidate.Period}, {candidate.Rows:N0} rows.")).ToArray());
        }
        if (candidates.Count == 1) return new(candidates[0]);
        var changedIds = (await findChanged(candidates.Select(candidate => candidate.ImportFileId).ToArray()).ConfigureAwait(false)).ToHashSet();
        var changed = candidates.Where(candidate => changedIds.Contains(candidate.ImportFileId)).ToArray();
        // Two or more changed files: whichever is picked, another blocks the run, so nothing is asked.
        if (changed.Length > 1) return OtherImportChanged(choice, null, changed);
        var picker = options.ChooseRestatementTarget;
        var picked = picker is null ? null : await picker(choice, cancellationToken).ConfigureAwait(false);
        if (candidates.FirstOrDefault(candidate => candidate.ImportFileId == picked?.ImportFileId) is { } target)
            return changed.Length == 1 && changed[0].ImportFileId != target.ImportFileId ? OtherImportChanged(choice, target, changed) : new(target);
        var files = string.Join(", ", candidates.Select(candidate => candidate.ImportFileId));
        return new(null, new(ImportCodes.RestatementTargetAmbiguous, picker is null
                ? $"This file's period overlaps {candidates.Count} current imports (files {files}). Restate it from the Import screen, which asks which one it replaces."
                : $"No import to restate was chosen among the {candidates.Count} current imports this file's period overlaps (files {files}). Nothing was changed."),
            candidates.Select(candidate => new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.RestatementTargetAmbiguous,
                $"Overlapping current import {candidate.ImportFileId}: {candidate.FileName}, {candidate.Period}, {candidate.Rows:N0} rows.")).ToArray());
    }

    private static RestatementTarget OtherImportChanged(RestatementTargetChoice choice, RestatementCandidate? picked,
        IReadOnlyList<RestatementCandidate> changed)
    {
        var blocking = changed.Where(candidate => candidate.ImportFileId != picked?.ImportFileId).ToArray();
        var message = $"This file's period {choice.Period} covers {choice.Candidates.Count} current {choice.ReportCode} imports, and it changes "
            + $"or drops rows of {(blocking.Length == 1 ? "import" : "imports")} {string.Join(", ", blocking.Select(candidate => candidate.ImportFileId))}"
            + (picked is null ? "" : $" besides import {picked.ImportFileId}, the one chosen") + ". A run restates only one import, and "
            + "the others are taken over only when this file holds all their rows. Restate each import with a corrected file for its own period. Nothing was changed.";
        return new(null, new(ImportCodes.RestatementOtherImportChanged, message),
            blocking.Select(candidate => new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.RestatementOtherImportChanged,
                $"Current import {candidate.ImportFileId}: {candidate.FileName}, {candidate.Period}, {candidate.Rows:N0} rows; this file changes it, and it is not the one restated.")).ToArray());
    }

    private sealed record RestatementTarget(RestatementCandidate? Candidate, ImportSourceException? Refusal = null,
        IReadOnlyList<ImportIssue>? Issues = null);

    private static int DependencyOrder(string? code) => code switch
    {
        "R025" => 0, "R020" => 1, "R022" => 2, "R024" => 3, "R013" => 4, "R003" => 5,
        "STOCK_LEDGER" or "R030" => 6, "R011" or "CLOSING_STOCK" => 7, _ => 10
    };
}
