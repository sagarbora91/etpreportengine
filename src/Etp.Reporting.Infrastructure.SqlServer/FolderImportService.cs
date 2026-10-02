using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
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
                var message = new SafeImportFailureClassifier().Describe(exception).SafeMessage;
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
                    var candidates = await persistence.FindRestatementCandidatesAsync(accepted.ProfileIdentity.ReportCode,
                        persistedStore, periodStart, periodEnd, cancellationToken).ConfigureAwait(false);
                    var target = await ChooseRestatementTargetAsync(new(result.FileName, accepted.ProfileIdentity.ReportCode,
                        persistedStore, periodStart, periodEnd, candidates), options, cancellationToken).ConfigureAwait(false);
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
                var saved = await persistence.PersistAsync(request, cancellationToken).ConfigureAwait(false);
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
                result = result with { Status = "Failed", Message = new SafeImportFailureClassifier().Describe(exception).SafeMessage };
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

    // IF-016 interim (planner 1): a restatement replaces one current file whose declared period overlaps its own.
    // None is refused; one is used; several are put to whoever imports, and the others go through promotion as
    // before. Several with no picker (automation) or no pick are refused with the candidates listed.
    private static async Task<RestatementTarget> ChooseRestatementTargetAsync(RestatementTargetChoice choice,
        FolderImportOptions options, CancellationToken cancellationToken)
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
        var picker = options.ChooseRestatementTarget;
        var picked = picker is null ? null : await picker(choice, cancellationToken).ConfigureAwait(false);
        if (candidates.FirstOrDefault(candidate => candidate.ImportFileId == picked?.ImportFileId) is { } target) return new(target);
        var files = string.Join(", ", candidates.Select(candidate => candidate.ImportFileId));
        return new(null, new(ImportCodes.RestatementTargetAmbiguous, picker is null
                ? $"This file's period overlaps {candidates.Count} current imports (files {files}). Restate it from the Import screen, which asks which one it replaces."
                : $"No import to restate was chosen among the {candidates.Count} current imports this file's period overlaps (files {files}). Nothing was changed."),
            candidates.Select(candidate => new ImportIssue(ImportIssueSeverity.Blocker, ImportCodes.RestatementTargetAmbiguous,
                $"Overlapping current import {candidate.ImportFileId}: {candidate.FileName}, {candidate.Period}, {candidate.Rows:N0} rows.")).ToArray());
    }

    private sealed record RestatementTarget(RestatementCandidate? Candidate, ImportSourceException? Refusal = null,
        IReadOnlyList<ImportIssue>? Issues = null);

    private static int DependencyOrder(string? code) => code switch
    {
        "R025" => 0, "R020" => 1, "R022" => 2, "R024" => 3, "R013" => 4, "R003" => 5,
        "STOCK_LEDGER" or "R030" => 6, "R011" or "CLOSING_STOCK" => 7, _ => 10
    };
}
