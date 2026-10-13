using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed record AutomatedOperationsSummary(int SourcesProcessed, int SourcesFailed, int DuplicateWorkbooks, int PacksGenerated, string Message);
public sealed record AutomatedWorkbookOutcome(string ReportCode, string? StoreCode, DateOnly? BusinessDate, bool Duplicate, int ConflictRows = 0)
{
    public IReadOnlyList<ImportIssue> Issues { get; init; } = [];
}

internal enum AutomationSourceRoute { Processed, Duplicate, Failed, Inbound }

/// <summary>A report pack the unattended run could not generate (IE-CODE-09), for the diagnostics log.</summary>
public sealed record AutomatedReportPackFailure(string RunType, DateOnly BusinessDate, int? SqlErrorNumber, Exception Exception);

public sealed class AutomatedOperationsService(string connectionString, Action<FolderImportFailure>? reportImportFailure = null,
    Action<AutomatedReportPackFailure>? reportPackFailure = null, ShopResultWriter? shopResultWriter = null,
    IImportSourceLocationStore? sourceLocationStore = null)
{
    private readonly ShopResultWriter shopResults = shopResultWriter ?? new ShopResultWriter();
    // 1.12 U2 (0069): where each routed file went, for the Problems and History Fix buttons. Built on first use, so a
    // connection string the factory refuses fails here (as a note on the run line), never the import.
    private IImportSourceLocationStore? sourceLocations = sourceLocationStore;

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
                var shopResultNote=route!=AutomationSourceRoute.Inbound ? WriteShopResult(source,batch.Files) : "";
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
                    var failedAt=MoveCompletedSource(source,paths.FailedPath);
                    var failedNote=shopResultNote+await RecordLocationsAsync(source,failedAt,ImportSourceRoute.Failed,batch.Files,cancellationToken);
                    await repository.RecordAutomationRunAsync("WATCH_IMPORT",Path.GetFileName(source),null,null,"Failed",
                        WithNote($"{imported} workbook(s) imported; {NeedReview(batch)} file(s) need review. Other files were processed.",failedNote),started,cancellationToken);
                    continue;
                }
                var routedAt=MoveCompletedSource(source,route==AutomationSourceRoute.Duplicate ? duplicatePath : paths.ProcessedPath);
                shopResultNote+=await RecordLocationsAsync(source,routedAt,
                    route==AutomationSourceRoute.Duplicate ? ImportSourceRoute.Duplicate : ImportSourceRoute.Processed,batch.Files,cancellationToken);
                processed++;
                var stores=batch.Files.Select(x=>x.StoreCode).Where(x=>x is not null).Distinct().ToArray();
                var dates=batch.Files.Select(x=>x.PeriodEnd).Where(x=>x is not null).Distinct().ToArray();
                // Save warnings (e.g. STOCK_ROW_REPEATED) would otherwise stay in the in-memory file results.
                var warnings=batch.Files.Sum(x=>x.Diagnostics?.Count(issue=>issue.Severity==ImportIssueSeverity.Warning) ?? 0);
                var unread=batch.Files.Count(IsSavedDespiteFailure);
                await repository.RecordAutomationRunAsync("WATCH_IMPORT",Path.GetFileName(source),stores.Length==1?stores[0]:null,
                    dates.Length==1?dates[0]:null,imported==0?"Skipped":"Succeeded",
                    WithNote($"{imported} workbook(s) imported; {batch.Duplicates} duplicate(s); {batch.Files.Count(file => file.Status == "Not needed")} not needed."+(warnings>0?$" {warnings} warning(s); review the file results.":"")+
                    (unread>0?$" {unread} saved but not read back; see Import History.":""),shopResultNote),started,cancellationToken);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                failed++;
                var shopResultNote = WriteShopResult(source, null);
                var safe = new SqlImportFailureClassifier().Describe(ex).SafeMessage;
                try
                {
                    var failedAt = MoveCompletedSource(source, paths.FailedPath);
                    shopResultNote += await RecordLocationsAsync(source, failedAt, ImportSourceRoute.Failed, null, cancellationToken);
                }
                catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException)
                { safe = $"{safe} The source could not be moved to Failed and remains available for the next run."; }
                await repository.RecordAutomationRunAsync("WATCH_IMPORT", Path.GetFileName(source), null, null, "Failed", WithNote(safe, shopResultNote), started, cancellationToken);
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
    /// Review IR-6: the run message is cut at <see cref="Phase2OperationsRepository.AutomationMessageLimit"/> characters. The B-1
    /// sentence (the shop's result was not written) must survive the cut, so the base message is shortened first, ending in "...".
    /// </summary>
    internal static string WithNote(string message, string note)
    {
        const int limit = Phase2OperationsRepository.AutomationMessageLimit;
        message ??= "";
        if (string.IsNullOrEmpty(note)) return message;
        if (message.Length + note.Length <= limit) return message + note;
        if (note.Length >= limit) return note[..limit];
        var room = limit - note.Length;
        return (room > 3 ? message[..(room - 3)].TrimEnd() + "..." : message[..room]) + note;
    }

    /// <summary>
    /// Item C: a verdict file for the shop whose zip this is (<c>ROLE-yyyy-MM-dd[-rN].zip</c>); any other source is not a shop's.
    /// Files is null when the import threw before any file result existed. Never changes the import outcome; a write that fails
    /// returns a sentence for the run's automation line (review B-1), otherwise "".
    /// </summary>
    private string WriteShopResult(string source, IReadOnlyList<FolderImportFileResult>? files)
    {
        if (!ShopResultBuilder.TryParseShopZip(source, out var role, out var day, out var sequence)) return "";
        var version = typeof(AutomatedOperationsService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        var result = files is null
            ? ShopResultBuilder.BuildNotCompleted(role, day, sequence, version, DateTime.UtcNow, Path.GetFileName(source))
            : ShopResultBuilder.Build(role, day, sequence, version, DateTime.UtcNow, files);
        if (result.Files.Count == 0) return "";
        shopResults.TryWrite(result, out var failure);
        return ShopResultWriter.FailureSentence(failure);
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
        catch (Exception exception) when (!(exception is OperationCanceledException && token.IsCancellationRequested))
        {
            // IE-CODE-09. The fixed text used to point at diagnostics nobody wrote. The history row now
            // carries the classified reason (as the import loop's does) and the exception goes to the
            // diagnostics sink. Recorded with None: a pack that failed late must still leave its row.
            Report(new(runType, date, SqlImportFailureClassifier.SqlErrorNumber(exception), exception));
            await repository.RecordAutomationRunAsync(runType, null, "COMBINED", date, "Failed",
                PackFailureMessage(exception), started, CancellationToken.None);
            return false;
        }
    }

    /// <summary>The history row's text for a failed pack: what kind of failure, never exception text.</summary>
    internal static string PackFailureMessage(Exception exception)
    {
        var kind = DatabaseConnectionFailure.Classify(exception);
        var number = SqlImportFailureClassifier.SqlErrorNumber(exception);
        var reason = DatabaseConnectionFailure.IsConnectionClass(kind) ? DatabaseConnectionFailure.Describe(kind)!
            : exception switch
            {
                UnauthorizedAccessException => "The report output folder could not be written. Check its permissions.",
                IOException => "The report file could not be written. Check the report output folder has space and the file is not open elsewhere.",
                _ when number is not null => "The database refused the report query.",
                _ => $"An unexpected {exception.GetType().Name} occurred."
            };
        var sql = number is { } value ? $" (SQL error {value.ToString(System.Globalization.CultureInfo.InvariantCulture)})" : "";
        return $"Report generation failed: {reason}{sql} Details are in the application diagnostics log.";
    }

    private void Report(AutomatedReportPackFailure failure)
    {
        if (reportPackFailure is null) return;
        // A failing sink must never stop the run recording what happened.
        try { reportPackFailure(failure); }
        catch (Exception sinkFailure) when (sinkFailure is not OutOfMemoryException) { }
    }

    /// <summary>Moves the source into the route's folder and returns where it now is.</summary>
    private static string MoveCompletedSource(string source, string destinationRoot)
    {
        var destination = Path.Combine(destinationRoot, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Path.GetFileName(source)}");
        if (File.Exists(destination)) destination = Path.Combine(destinationRoot, $"{Guid.NewGuid():N}-{Path.GetFileName(source)}");
        File.Move(source, destination);
        return destination;
    }

    /// <summary>The sentence the run line gets when the locations could not be saved; empty when they were.</summary>
    internal const string LocationNotSavedNote = " Where the file went could not be saved, so its Fix buttons cannot re-apply it; it is in the folder named above.";

    /// <summary>
    /// 1.12 U2 (0069): one location row per file the source held (the source itself when the run threw before any file
    /// result existed), all pointing at the routed source. Never changes the run's outcome: a failure returns
    /// <see cref="LocationNotSavedNote"/> for the automation line. A cancelled run does not record (the source stays in Inbound).
    /// </summary>
    private async Task<string> RecordLocationsAsync(string source, string routedPath, ImportSourceRoute route,
        IReadOnlyList<FolderImportFileResult>? files, CancellationToken cancellationToken)
    {
        try
        {
            var store = sourceLocations ??= new SqlServerImportSourceLocationStore(new EtpConnectionFactory(connectionString));
            await store.RecordAsync(LocationsFor(source, routedPath, route, files), CancellationToken.None).ConfigureAwait(false);
            return "";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            return LocationNotSavedNote;
        }
    }

    /// <summary>The rows <see cref="RecordLocationsAsync"/> writes: one per decided file, or one for the source itself.</summary>
    internal static IReadOnlyList<NewImportSourceLocation> LocationsFor(string source, string routedPath, ImportSourceRoute route,
        IReadOnlyList<FolderImportFileResult>? files)
    {
        var decided = (files ?? []).Where(file => file.Status != "Cancelled" && !string.IsNullOrWhiteSpace(file.FileName)).ToArray();
        return decided.Length == 0
            ? [new NewImportSourceLocation(Path.GetFileName(source), null, routedPath, route)]
            : decided.Select(file => new NewImportSourceLocation(Path.GetFileName(file.FileName), file.SourceSha256, routedPath, route)).ToArray();
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
