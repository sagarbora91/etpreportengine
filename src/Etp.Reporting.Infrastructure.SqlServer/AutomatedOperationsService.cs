using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record AutomatedOperationsSummary(int SourcesProcessed, int SourcesFailed, int DuplicateWorkbooks, int PacksGenerated, string Message);
public sealed record AutomatedWorkbookOutcome(string ReportCode, string? StoreCode, DateOnly? BusinessDate, bool Duplicate, int ConflictRows = 0)
{
    public IReadOnlyList<ImportIssue> Issues { get; init; } = [];
}

internal enum AutomationSourceRoute { Processed, Duplicate, Failed, Inbound }

public sealed class AutomatedOperationsService(string connectionString, Action<FolderImportFailure>? reportImportFailure = null)
{
    public async Task<AutomatedOperationsSummary> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AutomationSessionLease.TryAcquireAsync(connectionString, cancellationToken);
        if (lease is null) return new(0, 0, 0, 0, "Another unattended run is already active.");
        var repository = new Phase2OperationsRepository(connectionString);
        var configured = await repository.LoadWatchFolderSettingsAsync(cancellationToken);
        if (!configured.IsEnabled) return new(0, 0, 0, 0, "Watch-folder automation is disabled.");
        var knownStores = await new StoreCatalogRepository(connectionString).ActiveCodesAsync(cancellationToken);
        var paths = AutomationPathPolicy.Validate(configured.InboundPath, configured.ProcessedPath, configured.FailedPath, configured.ReportOutputPath,
            configured.IsEnabled);
        var duplicatePath = Path.Combine(paths.ProcessedPath, "Duplicate");
        foreach (var directory in new[] { paths.InboundPath, paths.ProcessedPath, paths.FailedPath, paths.ReportOutputPath, duplicatePath })
        {
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Automation cannot use a linked folder.");
        }

        var sources = Directory.EnumerateFiles(paths.InboundPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => new[] { ".xlsx", ".csv", ".zip" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .Where(IsStableAndReadable)
            .Order(StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
        var processed = 0; var failed = 0; var duplicates = 0; var packs = 0;
        var importedDates = new HashSet<DateOnly>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = DateTime.UtcNow;
            try
            {
                var folderService=new FolderImportService(new SqlServerImportPersistenceUseCase(connectionString),
                    knownStores: knownStores, reportFailure: reportImportFailure);
                var batch=await folderService.RunAsync(source,new(AutomationIdentity()),cancellationToken:cancellationToken);
                duplicates+=batch.Duplicates;
                foreach(var date in ImportedDates(batch)) importedDates.Add(date);
                var route=RouteOf(batch);
                if(route==AutomationSourceRoute.Inbound)
                {
                    // Files the cancel left unhandled are imported by the next run; the source stays in Inbound.
                    cancellationToken.ThrowIfCancellationRequested();
                    continue;
                }
                var imported=batch.Imported+batch.Files.Count(IsSavedDespiteFailure);
                if(route==AutomationSourceRoute.Failed)
                {
                    failed++;
                    MoveCompletedSource(source,paths.FailedPath);
                    await repository.RecordAutomationRunAsync("WATCH_IMPORT",Path.GetFileName(source),null,null,"Failed",
                        $"{imported} workbook(s) imported; {NeedReview(batch)} file(s) need review. Other files were processed.",started,cancellationToken);
                    continue;
                }
                MoveCompletedSource(source,route==AutomationSourceRoute.Duplicate ? duplicatePath : paths.ProcessedPath);
                processed++;
                var stores=batch.Files.Select(x=>x.StoreCode).Where(x=>x is not null).Distinct().ToArray();
                var dates=batch.Files.Select(x=>x.PeriodEnd).Where(x=>x is not null).Distinct().ToArray();
                // Save warnings (e.g. STOCK_ROW_REPEATED) would otherwise stay in the in-memory file results.
                var warnings=batch.Files.Sum(x=>x.Diagnostics?.Count(issue=>issue.Severity==ImportIssueSeverity.Warning) ?? 0);
                var unread=batch.Files.Count(IsSavedDespiteFailure);
                await repository.RecordAutomationRunAsync("WATCH_IMPORT",Path.GetFileName(source),stores.Length==1?stores[0]:null,
                    dates.Length==1?dates[0]:null,imported==0?"Skipped":"Succeeded",
                    $"{imported} workbook(s) imported; {batch.Duplicates} duplicate(s); {batch.Files.Count(file => file.Status == "Not needed")} not needed."+(warnings>0?$" {warnings} warning(s); review the file results.":"")+
                    (unread>0?$" {unread} saved but not read back; see Import History.":""),started,cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                failed++;
                var safe = new SqlImportFailureClassifier().Describe(ex).SafeMessage;
                try { MoveCompletedSource(source, paths.FailedPath); }
                catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException)
                { safe = $"{safe} The source could not be moved to Failed and remains available for the next run."; }
                await repository.RecordAutomationRunAsync("WATCH_IMPORT", Path.GetFileName(source), null, null, "Failed", safe, started, cancellationToken);
            }
        }

        foreach (var date in importedDates.Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await GenerateAndExportAsync(date, "AutoImport", true, true, paths.ReportOutputPath, repository, "AUTO_REPORT_PACK", cancellationToken)) packs++;
        }

        var latest = await repository.LoadLatestCombinedBusinessDateAsync(cancellationToken);
        if (latest is { } latestDate)
        {
            var due = await repository.LoadDueSchedulesAsync(latestDate, TimeOnly.FromDateTime(DateTime.Now), cancellationToken);
            foreach (var schedule in due)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var succeeded = await GenerateAndExportAsync(latestDate, schedule.Name, schedule.ExportExcel, schedule.ExportPdf, paths.ReportOutputPath,
                    repository, "SCHEDULED_REPORT_PACK", cancellationToken);
                await repository.CompleteScheduleAsync(schedule.Id, latestDate, succeeded ? "Succeeded" : "Failed",
                    succeeded ? "The scheduled management pack was generated." : "The scheduled pack failed; review automation history.", cancellationToken);
                if (succeeded) packs++;
            }
        }
        return new(processed, failed, duplicates, packs, $"Unattended run completed: {processed} source(s) processed, {failed} failed, {packs} report pack(s) generated.");
    }

    /// <summary>
    /// A file whose import committed although the run reports it Failed or Cancelled: only reading its result
    /// back failed, or the cancel came after the COMMIT (IF-014). Its data is live, so automation treats it as
    /// imported. A file with conflicting rows still needs review.
    /// </summary>
    internal static bool IsSavedDespiteFailure(FolderImportFileResult file) =>
        file.Status is "Failed" or "Cancelled" && file.CommitState == CommitState.Committed && file.ConflictRows == 0;

    /// <summary>The business dates whose report pack a source's run makes due: those of its new or saved imports.
    /// A Service Centre import never makes a pack due: the packs are Retail management packs (Service interim, decision 15),
    /// so a Service-only batch queues none and a mixed batch only its Retail dates.</summary>
    /// <param name="families">The family catalogue; the shipped registry when null (a test passes a synthetic one).</param>
    internal static IEnumerable<DateOnly> ImportedDates(FolderImportSummary batch, IEnumerable<EtpReportFamily>? families = null) =>
        batch.Files.Where(file => (file.Status == "Imported" || IsSavedDespiteFailure(file)) && file.PeriodEnd is not null
                && !ServiceRouting.IsService(file.ReportCode, families))
            .Select(file => file.PeriodEnd!.Value).Distinct();

    /// <summary>
    /// Where a watched source goes after its run. A file the cancel left unhandled keeps the source in Inbound,
    /// so the next run imports it; a file that needs review moves it to Failed; a saved import counts as imported.
    /// </summary>
    internal static AutomationSourceRoute RouteOf(FolderImportSummary batch)
    {
        if (batch.Files.Any(file => file.Status == "Cancelled" && !IsSavedDespiteFailure(file))) return AutomationSourceRoute.Inbound;
        if (NeedReview(batch) > 0) return AutomationSourceRoute.Failed;
        return batch.Imported + batch.Files.Count(IsSavedDespiteFailure) == 0 && batch.Duplicates > 0
            ? AutomationSourceRoute.Duplicate : AutomationSourceRoute.Processed;
    }

    private static int NeedReview(FolderImportSummary batch) =>
        batch.Files.Count(file => file.Failed && !IsSavedDespiteFailure(file)) + batch.UnknownLayouts;

    public async Task<AutomatedWorkbookOutcome> ProcessWorkbookAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        var workbook = await new SourceFileReader().ReadAsync(workbookPath, cancellationToken);
        var knownStores = await new StoreCatalogRepository(connectionString).ActiveCodesAsync(cancellationToken);
        var inspection = new MatchedImportEnvelopeFactory(knownStores).Inspect(workbook);
        if (inspection.AcceptedImport is null)
        {
            var reason = string.Join(", ", inspection.Diagnostics.Where(x => x.Severity == ImportDiagnosticSeverity.Blocker).Select(x => x.Code).Distinct());
            throw new ImportSourceException("IMPORT_LAYOUT_BLOCKED", $"Workbook validation was blocked: {reason}.");
        }
        var accepted = inspection.AcceptedImport;
        var report = accepted.ProfileIdentity.ReportCode;
        // A Service family the interim does not land has no table to persist into.
        if (ServiceRouting.NotNeeded(workbook.FileName, report) is { } skipped) throw new ImportSourceException(skipped.Code, skipped.Message);
        accepted.Scope.RequireOwnSnapshotDate();
        var end = accepted.Scope.PeriodEnd ?? throw new ImportSourceException("SCOPE_NOT_DETECTED","Keep this file beside the other exports for its store.");
        var start = accepted.Scope.PeriodStart ?? end;
        var store = accepted.Scope.StoreCode ?? ImportScope.ServiceStoreFallback(report)
            ?? throw new ImportSourceException("SCOPE_NOT_DETECTED","Store could not be detected.");
        var files = new SqlServerImportFileRepository(connectionString);
        var persistence = new SqlServerImportPersistenceUseCase(connectionString);
        // IF-023: the import keeps the source bytes inside its transaction; a duplicate keeps missing bytes.
        if (await files.ExistsInScopeAsync(workbook.Sha256, report, store, start, end, cancellationToken))
        {
            // Rows are already stored: a failed evidence write must not move the file to Failed.
            try { await persistence.RetainImportedSourceAsync(workbook.Sha256, workbook.EvidenceBytes, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException) { }
            return new(report, store, end, true);
        }
        var result = await persistence.PersistAsync(new(accepted, end, store, AutomationIdentity()), cancellationToken);
        return new(report, store, end, result.Status.StartsWith("Duplicate", StringComparison.Ordinal), result.ConflictRows) { Issues = result.Issues };
    }

    private async Task<bool> GenerateAndExportAsync(DateOnly date, string label, bool excel, bool pdf, string outputPath,
        Phase2OperationsRepository repository, string runType, CancellationToken token)
    {
        var started = DateTime.UtcNow;
        try
        {
            var pack = await new DailyReportingPackService(connectionString).GenerateCombinedAsync(date, AutomationIdentity(), token);
            var safeLabel = string.Concat(label.Select(character => char.IsLetterOrDigit(character) ? character : '_')).Trim('_');
            var stem = $"ETP_{safeLabel}_{date:yyyyMMdd}_{DateTime.Now:HHmmss}";
            if (excel) new OpenXmlReportPackExporter().Export(Path.Combine(outputPath, stem + ".xlsx"), pack);
            if (pdf) new SimplePdfReportPackExporter().Export(Path.Combine(outputPath, stem + ".pdf"), pack);
            await repository.RecordAutomationRunAsync(runType, null, "COMBINED", date, "Succeeded", "Combined management pack generated for the configured stores.", started, token);
            return true;
        }
        catch (Exception)
        {
            await repository.RecordAutomationRunAsync(runType, null, "COMBINED", date, "Failed", "Report generation failed; review daily exceptions and application diagnostics.", started, token);
            return false;
        }
    }

    private static void MoveCompletedSource(string source, string destinationRoot)
    {
        var destination = Path.Combine(destinationRoot, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Path.GetFileName(source)}");
        if (File.Exists(destination)) destination = Path.Combine(destinationRoot, $"{Guid.NewGuid():N}-{Path.GetFileName(source)}");
        File.Move(source, destination);
    }

    private static bool IsStableAndReadable(string path)
    {
        if (File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddSeconds(-10)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string AutomationIdentity() => $"{Environment.UserDomainName}\\{Environment.UserName}";
}
