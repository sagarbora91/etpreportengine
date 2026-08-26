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
public sealed record AutomatedWorkbookOutcome(string ReportCode, string? StoreCode, DateOnly? BusinessDate, bool Duplicate);

public sealed class AutomatedOperationsService(string connectionString)
{
    public async Task<AutomatedOperationsSummary> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var lease = await TryAcquireLeaseAsync(cancellationToken);
        if (lease is null) return new(0, 0, 0, 0, "Another unattended run is already active.");
        var repository = new Phase2OperationsRepository(connectionString);
        var configured = await repository.LoadWatchFolderSettingsAsync(cancellationToken);
        if (!configured.IsEnabled) return new(0, 0, 0, 0, "Watch-folder automation is disabled.");
        var paths = AutomationPathPolicy.Validate(configured.InboundPath, configured.ProcessedPath, configured.FailedPath, configured.ReportOutputPath,
            configured.PollMinutes, configured.IsEnabled);
        foreach (var directory in new[] { paths.InboundPath, paths.ProcessedPath, paths.FailedPath, paths.ReportOutputPath })
        {
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Automation cannot use a linked folder.");
        }

        var sources = Directory.EnumerateFiles(paths.InboundPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
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
                await using var batchSource = await BatchImportSource.OpenAsync(source, cancellationToken: cancellationToken);
                var outcomes = new List<AutomatedWorkbookOutcome>();
                foreach (var workbook in batchSource.WorkbookPaths)
                {
                    var outcome = await ProcessWorkbookAsync(workbook, cancellationToken);
                    outcomes.Add(outcome);
                    if (outcome.Duplicate) duplicates++;
                    if (!outcome.Duplicate && outcome.BusinessDate is { } date) importedDates.Add(date);
                }
                MoveCompletedSource(source, paths.ProcessedPath);
                processed++;
                var imported = outcomes.Count(x => !x.Duplicate);
                var stores = outcomes.Select(x => x.StoreCode).Where(x => x is not null).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var dates = outcomes.Select(x => x.BusinessDate).Where(x => x is not null).Select(x => x!.Value).Distinct().ToArray();
                await repository.RecordAutomationRunAsync("WATCH_IMPORT", Path.GetFileName(source), stores.Length == 1 ? stores[0] : stores.Length > 1 ? "MULTIPLE" : null,
                    dates.Length == 1 ? dates[0] : null,
                    imported == 0 ? "Skipped" : "Succeeded", imported == 0 ? "All workbooks were already imported." : $"{imported} workbook(s) imported; duplicates were skipped.", started, cancellationToken);
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
        if (await new SqlServerImportFileRepository(connectionString).ExistsByHashAsync(workbook.Sha256, cancellationToken))
        {
            var existing = await LoadScopeByHashAsync(workbook.Sha256, cancellationToken);
            return new(existing.ReportCode, existing.StoreCode, existing.BusinessDate, true);
        }
        var preflight = new ImportPreflight().Inspect(workbook, RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All));
        if (!preflight.CanImport)
        {
            var reason = string.Join(", ", preflight.Diagnostics.Where(x => x.Severity == ImportDiagnosticSeverity.Blocker).Select(x => x.Code).Distinct());
            throw new ImportSourceException("IMPORT_LAYOUT_BLOCKED", $"Workbook validation was blocked: {reason}.");
        }
        var report = preflight.Profile!.ReportCode;
        var store = new SqlServerTransactionalImportStore(connectionString);
        if (report == "R022")
        {
            var staged = new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile);
            if (!staged.CanPersist) throw new ImportSourceException("IMPORT_STAGING_BLOCKED", "Workbook rows failed deterministic validation.");
            await new R022SqlImportOrchestrator(store).PersistAsync(workbook, preflight.Sheet!, new R022PersistenceProjector().Project(staged.Rows),
                cancellationToken: cancellationToken, importedBy: AutomationIdentity());
        }
        else if (report is "STOCK_LEDGER" or "CLOSING_STOCK")
            await new StockSqlImportOrchestrator(store).PersistAsync(workbook, cancellationToken: cancellationToken, importedBy: AutomationIdentity());
        else if (report is "R003" or "R013")
            await new RetailEnrichmentSqlImportOrchestrator(connectionString).PersistAsync(workbook, report, importedBy: AutomationIdentity(), cancellationToken: cancellationToken);
        else
            await new R025SqlImportOrchestrator(store).PersistAsync(workbook, cancellationToken: cancellationToken, importedBy: AutomationIdentity());
        var scope = await LoadScopeByHashAsync(workbook.Sha256, cancellationToken);
        return new(scope.ReportCode, scope.StoreCode, scope.BusinessDate, false);
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
            await repository.RecordAutomationRunAsync(runType, null, "COMBINED", date, "Succeeded", "Complete Titan and Helios management pack generated.", started, token);
            return true;
        }
        catch (Exception)
        {
            await repository.RecordAutomationRunAsync(runType, null, "COMBINED", date, "Failed", "Report generation failed; review daily exceptions and application diagnostics.", started, token);
            return false;
        }
    }

    private async Task<(string ReportCode, string? StoreCode, DateOnly? BusinessDate)> LoadScopeByHashAsync(string sha256, CancellationToken token)
    {
        await using var connection = new SqlConnection(connectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("SELECT report_code,store_code,business_date FROM dbo.import_files WHERE source_sha256=@hash", connection);
        command.Parameters.AddWithValue("@hash", SqlServerImportFileRepository.NormalizeHash(sha256));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("The persisted workbook scope was not found.");
        return (reader.IsDBNull(0) ? "UNKNOWN" : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2));
    }

    private async Task<SqlConnection?> TryAcquireLeaseAsync(CancellationToken token)
    {
        var connection = new SqlConnection(connectionString); await connection.OpenAsync(token);
        await using var command = new SqlCommand("DECLARE @result int; EXEC @result=sp_getapplock @Resource=N'ETP_PHASE2_AUTOMATION',@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @result;", connection);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) < 0) { await connection.DisposeAsync(); return null; }
        return connection;
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
