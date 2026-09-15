using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>The same folder workflow is used by the desktop and command-line import.</summary>
public sealed class FolderImportService(
    IImportPersistenceUseCase<MatchedImportEnvelope> persistence,
    IWorkbookReader? workbookReader = null,
    Func<string, MatchedImportEnvelope, string, DateOnly, CancellationToken, Task>? retainEvidence = null) : IFolderImportService
{
    private readonly IWorkbookReader reader = workbookReader ?? new OpenXmlWorkbookReader();
    private readonly MatchedImportEnvelopeFactory envelopes = new();
    public IReadOnlyList<string> FailedPaths { get; private set; } = [];

    public async Task<FolderImportSummary> RunAsync(string sourcePath, FolderImportOptions options,
        IProgress<FolderImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await using var source = await BatchImportSource.OpenAsync(sourcePath, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await RunFilesAsync(source.WorkbookPaths, options, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FolderImportSummary> RunFilesAsync(IReadOnlyList<string> paths, FolderImportOptions options,
        IProgress<FolderImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (options.RestatementEnabled && string.IsNullOrWhiteSpace(options.RestatementReason))
            throw new ImportSourceException("RESTATEMENT_REASON_REQUIRED", "Enter the reason for the restatement.");
        if (!options.RestatementEnabled && (options.OverrideStoreCode is not null || options.OverrideBusinessDate is not null))
            throw new ImportSourceException("IMPORT_OVERRIDE_REQUIRES_RESTATEMENT", "Enable restatement before overriding the detected store or date.");

        var results = new List<FolderImportFileResult>();
        var ready = new List<(string Path, MatchedImportInspection Inspection)>();
        var failed = new List<string>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (cancellationToken.IsCancellationRequested) break;
            progress?.Report(new(0, paths.Count, Path.GetFileName(path), "Reading folder", results.ToArray()));
            try
            {
                ready.Add((path, envelopes.Inspect(await reader.ReadAsync(path, cancellationToken).ConfigureAwait(false))));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                var message = new SafeImportFailureClassifier().Describe(exception).SafeMessage;
                results.Add(new(Path.GetFileName(path), null, null, null, null, "Failed", Message: message));
                failed.Add(path);
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
                scope?.StoreCode, scope?.PeriodStart, scope?.PeriodEnd, "Importing", Diagnostics: issues);
            progress?.Report(new(results.Count, paths.Count, result.FileName, "Importing", results.Append(result).ToArray()));
            if (accepted is null)
            {
                var unknown = issues.Any(issue => issue.Code is "LAYOUT_UNKNOWN" or "REQUIRED_COLUMN_MISSING" or "UNEXPECTED_COLUMN");
                var notNeeded = result.FileName.StartsWith("00_", StringComparison.OrdinalIgnoreCase);
                result = result with { Status = notNeeded ? "Not needed" : unknown ? "Unknown layout" : "Failed",
                    Message = notNeeded ? "Consolidation control workbook; report workbooks are imported separately." : string.Join(" ", issues.Select(issue => issue.Message).Distinct()) };
                if (result.Failed) failed.Add(entry.Path);
                results.Add(result);
                continue;
            }
            try
            {
                var siblings = ready.Where(item => string.Equals(Path.GetDirectoryName(item.Path), Path.GetDirectoryName(entry.Path), StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.Inspection.AcceptedImport?.Scope).Where(item => item is not null).ToArray();
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
                var saved = await persistence.PersistAsync(new(accepted, end.Value, store, options.ImportedBy, restatement), cancellationToken).ConfigureAwait(false);
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
                failed.Add(entry.Path);
            }
            results.Add(result);
            progress?.Report(new(results.Count, paths.Count, result.FileName, result.Status, results.ToArray()));
        }
        if (cancellationToken.IsCancellationRequested)
            foreach (var path in paths.Where(path => !handled.Contains(path)))
                results.Add(new(Path.GetFileName(path), null, null, null, null, "Cancelled"));
        FailedPaths = failed;
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

    private static int DependencyOrder(string? code) => code switch
    {
        "R025" => 0, "R020" => 1, "R022" => 2, "R024" => 3, "R013" => 4, "R003" => 5,
        "STOCK_LEDGER" or "R030" => 6, "R011" or "CLOSING_STOCK" => 7, _ => 10
    };
}
