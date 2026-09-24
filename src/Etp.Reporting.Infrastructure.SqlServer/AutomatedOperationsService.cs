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
public sealed record AutomatedWorkbookOutcome(string ReportCode, string? StoreCode, DateOnly? BusinessDate, bool Duplicate, int ConflictRows = 0);

public sealed class AutomatedOperationsService(string connectionString)
{
    public async Task<AutomatedOperationsSummary> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await AutomationSessionLease.TryAcquireAsync(connectionString, cancellationToken);
        if (lease is null) return new(0, 0, 0, 0, "Another unattended run is already active.");
        var repository = new Phase2OperationsRepository(connectionString);
        var configured = await repository.LoadWatchFolderSettingsAsync(cancellationToken);
        if (!configured.IsEnabled) return new(0, 0, 0, 0, "Watch-folder automation is disabled.");
        var paths = AutomationPathPolicy.Validate(configured.InboundPath, configured.ProcessedPath, configured.FailedPath, configured.ReportOutputPath,
            configured.IsEnabled);
        var duplicatePath = Path.Combine(paths.ProcessedPath, "Duplicate");
        foreach (var directory in new[] { paths.InboundPath, paths.ProcessedPath, paths.FailedPath, paths.ReportOutputPath, duplicatePath })
        {
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Automation cannot use a linked folder.");
        }

        var sources = Directory.EnumerateFiles(paths.InboundPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => new[] { ".xlsx", ".zip" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
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
                    retainEvidence: (path,accepted,store,businessDate,token)=>new ProductisationOperationsService(connectionString).IntakeEtpEvidenceAsync(
                        path,accepted.Workbook.Sha256,accepted.ProfileIdentity.ReportCode,store,businessDate,token));
                var batch=await folderService.RunAsync(source,new(AutomationIdentity()),cancellationToken:cancellationToken);
                duplicates+=batch.Duplicates;
                foreach(var file in batch.Files.Where(x=>x.Status=="Imported" && x.PeriodEnd is not null)) importedDates.Add(file.PeriodEnd!.Value);
                if(batch.Failed>0 || batch.UnknownLayouts>0)
                {
                    failed++;
                    MoveCompletedSource(source,paths.FailedPath);
                    await repository.RecordAutomationRunAsync("WATCH_IMPORT",Path.GetFileName(source),null,null,"Failed",
                        $"{batch.Imported} workbook(s) imported; {batch.Failed + batch.UnknownLayouts} file(s) need review. Other files were processed.",started,cancellationToken);
                    continue;
                }
                MoveCompletedSource(source,batch.Imported==0 && batch.Duplicates>0 ? duplicatePath : paths.ProcessedPath);
                processed++;
                var stores=batch.Files.Select(x=>x.StoreCode).Where(x=>x is not null).Distinct().ToArray();
                var dates=batch.Files.Select(x=>x.PeriodEnd).Where(x=>x is not null).Distinct().ToArray();
                await repository.RecordAutomationRunAsync("WATCH_IMPORT",Path.GetFileName(source),stores.Length==1?stores[0]:null,
                    dates.Length==1?dates[0]:null,batch.Imported==0?"Skipped":"Succeeded",
                    $"{batch.Imported} workbook(s) imported; {batch.Duplicates} duplicate(s); {batch.Files.Count(file => file.Status == "Not needed")} not needed.",started,cancellationToken);
            }
            catch (Exception ex)
            {
                failed++;
                var safe = new SafeImportFailureClassifier().Describe(ex).SafeMessage;
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

    public async Task<AutomatedWorkbookOutcome> ProcessWorkbookAsync(string workbookPath, CancellationToken cancellationToken = default)
    {
        var workbook = await new OpenXmlWorkbookReader().ReadAsync(workbookPath, cancellationToken);
        var inspection = new MatchedImportEnvelopeFactory().Inspect(workbook);
        if (inspection.AcceptedImport is null)
        {
            var reason = string.Join(", ", inspection.Diagnostics.Where(x => x.Severity == ImportDiagnosticSeverity.Blocker).Select(x => x.Code).Distinct());
            throw new ImportSourceException("IMPORT_LAYOUT_BLOCKED", $"Workbook validation was blocked: {reason}.");
        }
        var accepted = inspection.AcceptedImport;
        var report = accepted.ProfileIdentity.ReportCode;
        var end = accepted.Scope.PeriodEnd ?? throw new ImportSourceException("SCOPE_NOT_DETECTED","Keep this file beside the other exports for its store.");
        var start = accepted.Scope.PeriodStart ?? end;
        var store = accepted.Scope.StoreCode ?? throw new ImportSourceException("SCOPE_NOT_DETECTED","Store could not be detected.");
        var files = new SqlServerImportFileRepository(connectionString);
        if (await files.ExistsInScopeAsync(workbook.Sha256, report, store, start, end, cancellationToken))
        {
            await new ProductisationOperationsService(connectionString).IntakeEtpEvidenceAsync(workbookPath,workbook.Sha256,report,store,end,cancellationToken);
            return new(report, store, end, true);
        }
        var result = await new SqlServerImportPersistenceUseCase(connectionString).PersistAsync(
            new(accepted, end, store, AutomationIdentity()), cancellationToken);
        await new ProductisationOperationsService(connectionString).IntakeEtpEvidenceAsync(workbookPath,workbook.Sha256,report,store,end,cancellationToken);
        return new(report, store, end, result.Status.StartsWith("Duplicate", StringComparison.Ordinal), result.ConflictRows);
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
