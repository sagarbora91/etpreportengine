using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
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
    Func<string, MatchedImportEnvelope, string, DateOnly, CancellationToken, Task>? retainEvidence = null,
    IReadOnlyList<string>? knownStores = null,
    Action<FolderImportFailure>? reportFailure = null) : IFolderImportService
{
    private readonly IImportFailureClassifier classifier = new SqlImportFailureClassifier();
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
        var ready = new List<(string Path, MatchedImportInspection Inspection)>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (cancellationToken.IsCancellationRequested) break;
            progress?.Report(new(0, paths.Count, Path.GetFileName(path), "Reading folder", results.ToArray()));
            try
            {
                var inspection = envelopes.Inspect(await reader.ReadAsync(path, cancellationToken).ConfigureAwait(false));
                ready.Add((path, inspection));
                if (inspection.AcceptedImport is { } accepted) detectedScopes[path] = accepted.Scope;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                var message = classifier.Describe(exception).SafeMessage;
                Report(Path.GetFileName(path), FailureStage.Read, null, null, exception);
                results.Add(new(Path.GetFileName(path), null, null, null, null, "Failed", Message: message) { SourcePath = path });
                handled.Add(path);
            }
        }
        foreach (var entry in ready.OrderBy(item => DependencyOrder(item.Inspection.MatchedProfile?.ReportCode)))
        {
            if (cancellationToken.IsCancellationRequested) break;
            handled.Add(entry.Path);
            var accepted = entry.Inspection.AcceptedImport;
            var scope = accepted?.Scope;
            var issues = entry.Inspection.Diagnostics.Select(issue => new ImportIssue(
                (ImportIssueSeverity)(int)issue.Severity, issue.Code, issue.Message, issue.RowNumber, issue.ColumnName)).ToArray();
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
                results.Add(result);
                continue;
            }
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
                    continue;
                }
                ImportRestatement? restatement = null;
                if (options.RestatementEnabled)
                {
                    var previous = await persistence.FindCurrentImportFileIdAsync(accepted.ProfileIdentity.ReportCode, store, end.Value, cancellationToken).ConfigureAwait(false);
                    if (previous is not null) restatement = new(previous.Value, options.ImportedBy, options.RestatementReason);
                }
                var request = new ImportPersistenceRequest<MatchedImportEnvelope>(accepted, end.Value, store, options.ImportedBy, restatement);
                if (restatement is not null)
                    await persistence.PrepareRestatementAsync(request, cancellationToken).ConfigureAwait(false);
                var saved = await persistence.PersistAsync(request, cancellationToken).ConfigureAwait(false);
                // Only a duplicate found before or under the import lock commits nothing of its own (IF-014).
                // Anything after this point reads back a saved import, so a failure there is not a failed import.
                if (saved.Status != "Duplicate") result = result with { CommitState = CommitState.Committed, BatchId = saved.BatchId };
                var outcome = saved.Status == "Imported"
                    ? await persistence.LoadOutcomeInScopeAsync(accepted.Workbook.Sha256, accepted.ProfileIdentity.ReportCode,
                        persistedStore, periodStart, periodEnd, cancellationToken).ConfigureAwait(false)
                    : new ImportRowOutcome(accepted.Staging.Rows.Count, saved.PersistedRows, saved.AlreadyPresentRows, saved.ConflictRows);
                result = result with { StoreCode = persistedStore, PeriodStart = periodStart, PeriodEnd = periodEnd,
                    Status = accepted.Staging.Rows.Count == 0 && saved.Status == "Imported" ? "empty export" : saved.Status,
                    RowsProcessed = Math.Max(accepted.Staging.Rows.Count, outcome.RowsProcessed), NewRows = Math.Max(saved.PersistedRows, outcome.NewRows),
                    AlreadyPresentRows = outcome.AlreadyPresentRows, ConflictRows = outcome.ConflictRows };
                if (outcome.ConflictRows > 0) result = result with { Status = "Failed", Message = $"{outcome.ConflictRows:N0} conflicting rows. Review the source before retrying." };
                if (result.Status is "Imported" or "empty export" or "Duplicate" or "Duplicate content" or "Already present")
                    result = await RetainEvidenceAsync(result, entry.Path, accepted, persistedStore, periodEnd, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { result = result with { Status = "Cancelled", Message = "Import cancelled." }; }
            catch (Exception exception)
            {
                if (result.CommitState == CommitState.Committed) SqlTransactionGuard.MarkCommitted(exception, result.BatchId);
                result = result with { Status = "Failed", Message = classifier.Describe(exception).SafeMessage,
                    CommitState = SqlTransactionGuard.CommitStateOf(exception), BatchId = SqlTransactionGuard.BatchIdOf(exception) };
                Report(result.FileName, FailureStage.Apply, result.ReportCode, scope, exception);
            }
            results.Add(result);
            progress?.Report(new(results.Count, paths.Count, result.FileName, result.Status, results.ToArray()));
        }
        if (cancellationToken.IsCancellationRequested)
            foreach (var path in paths.Where(path => !handled.Contains(path)))
                results.Add(new(Path.GetFileName(path), null, null, null, null, "Cancelled") { SourcePath = path });
        FailedPaths = results.Where(result => result.Failed).Select(result => result.SourcePath!).ToArray();
        if (persistence is IImportAttemptRecorder recorder)
            foreach (var result in results)
                await recorder.RecordAttemptAsync(result, CancellationToken.None).ConfigureAwait(false);
        progress?.Report(new(results.Count, paths.Count, string.Empty, cancellationToken.IsCancellationRequested ? "Cancelled" : "Completed", results.ToArray()));
        return new(results);
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

    private static int DependencyOrder(string? code) => code switch
    {
        "R025" => 0, "R020" => 1, "R022" => 2, "R024" => 3, "R013" => 4, "R003" => 5,
        "STOCK_LEDGER" or "R030" => 6, "R011" or "CLOSING_STOCK" => 7, _ => 10
    };
}
