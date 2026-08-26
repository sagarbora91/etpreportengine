using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

if (args.Length == 2 && args[0] == "--drop-validation-databases")
{
    await DropValidationDatabasesAsync(args[1]);
    return 0;
}
if (args.Length == 3 && args[0] == "--verify-existing")
{
    await VerifyExistingAsync(args[1], args[2]);
    return 0;
}
if (args.Length == 3 && args[0] == "--migrate-existing")
{
    var result = await new SqlServerDatabaseBootstrapper(args[1], new DirectoryMigrationSource(args[2])).BootstrapAsync();
    Console.WriteLine($"Existing database migrated; applied={string.Join(',', result.AppliedMigrations)}.");
    return 0;
}

if (args.Length != 3 || !Directory.Exists(args[0]) || !Directory.Exists(args[2]))
{
    Console.Error.WriteLine("Usage: Etp.Reporting.LiveSmoke <source-data-directory> <connection-string> <migration-directory>");
    return 2;
}

var sourceRoot = args[0];
var connectionString = args[1];
var migrationDirectory = args[2];
var bootstrap = await new SqlServerDatabaseBootstrapper(connectionString, new DirectoryMigrationSource(migrationDirectory)).BootstrapAsync();
Console.WriteLine($"Database ready; created={bootstrap.DatabaseCreated}; migrations={bootstrap.AppliedMigrations.Count}.");

var expected = new[] { "SDB-VariantwiseSales", "Revenue Report", "CRO Wise Sales", "All Discount Type", "Variant Stock ledger", "Closing Stock" };
var files = Directory.EnumerateFiles(sourceRoot, "*.xlsx", SearchOption.AllDirectories)
    .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
    .Where(path => expected.Any(name => Path.GetFileName(path).Contains(name, StringComparison.OrdinalIgnoreCase)))
    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
var store = new SqlServerTransactionalImportStore(connectionString);
var reader = new OpenXmlWorkbookReader();
var importedRows = 0;
foreach (var file in files)
{
    var workbook = await reader.ReadAsync(file);
    var preflight = new ImportPreflight().Inspect(workbook, RetailSalesProfiles.FirstSalesSlice.Concat(StockImportProfiles.All));
    if (!preflight.CanImport) throw new InvalidOperationException($"Preflight blocked {Path.GetFileName(file)}: {string.Join(',', preflight.Diagnostics.Select(x => x.Code))}");
    switch (preflight.Profile!.ReportCode)
    {
        case "R025":
            importedRows += (await new R025SqlImportOrchestrator(store).PersistAsync(workbook)).PersistedRows;
            break;
        case "R022":
            var staged = new ImportRowStager().Stage(preflight.Sheet!, preflight.Profile);
            if (!staged.CanPersist) throw new InvalidOperationException($"Staging blocked {Path.GetFileName(file)}.");
            var projection = new R022PersistenceProjector().Project(staged.Rows);
            await new R022SqlImportOrchestrator(store).PersistAsync(workbook, preflight.Sheet!, projection);
            importedRows += projection.InvoiceControls.Count + projection.ClassifiedTenders.Count + projection.QuarantinedTenders.Count;
            break;
        case "STOCK_LEDGER" or "CLOSING_STOCK":
            importedRows += (await new StockSqlImportOrchestrator(store).PersistAsync(workbook)).PersistedRows;
            break;
        case "R003" or "R013":
            importedRows += (await new RetailEnrichmentSqlImportOrchestrator(connectionString)
                .PersistAsync(workbook, preflight.Profile.ReportCode)).PersistedRows;
            break;
        default: throw new InvalidOperationException("Unexpected approved profile.");
    }
    Console.WriteLine($"Imported {preflight.Profile.ReportCode}: {Path.GetFileName(file)}");
}
var fileRepository = new SqlServerImportFileRepository(connectionString);
foreach (var file in files)
{
    var workbook = await reader.ReadAsync(file);
    if (!await fileRepository.ExistsByHashAsync(workbook.Sha256)) throw new InvalidOperationException("Live duplicate-file identity lookup failed.");
}

var executor = new SqlBackedReportingExecutor(new SqlServerReportingQueryRepository(connectionString),
    RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
var scope = new ReportingQueryScope(new(2026, 7, 1), new(2026, 8, 25));
var daily = await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Daily);
var brandSegment = await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.BrandSegment);
var tenders = await executor.ExecuteTenderReconciliationAsync(scope);
var stock = await executor.ExecuteStockReconciliationAsync(scope);
var operationalReports = new OperationalReportRepository(connectionString);
var invoiceSummary = await operationalReports.LoadInvoiceSummaryAsync(scope);
var dsr = await operationalReports.LoadDsrAsync(new(2026, 8, 25));
var staff = await operationalReports.LoadStaffPerformanceAsync(scope);
var dailyRepository = new DailyReportingWorkflowRepository(connectionString);
var testDate = new DateOnly(2026, 8, 25);
foreach (var input in new Dictionary<string, decimal>
{
    ["WALK_INS"] = 0m, ["OPENING_CASH"] = 1_000m, ["CASH_DEPOSIT"] = 0m, ["EXPENSES"] = 0m,
    ["SERVICE_CASH"] = 0m, ["SERVICE_CARD"] = 0m, ["SERVICE_UPI"] = 0m, ["CASH_ADJUSTMENT"] = 0m
})
    await dailyRepository.SaveManualInputAsync("WLMHW", testDate, input.Key, input.Value, null, "live-smoke", "validation");
var preliminaryCash = await operationalReports.LoadCashReconciliationAsync("WLMHW", testDate);
await dailyRepository.SaveManualInputAsync("WLMHW", testDate, "CLOSING_CASH_COUNTED", 1_000m + preliminaryCash.RetailCash, null, "live-smoke", "validation");
var serviceSales = await operationalReports.LoadServiceSalesAsync(testDate, ["WLMHW"]);
var cashControl = await operationalReports.LoadCashReconciliationAsync("WLMHW", testDate);
var completion = new OperationalCompletionRepository(connectionString);
await completion.SaveManualStockCountAsync("WLMHW", testDate, "GAUTO", 0m, 0m, 0m, 0m, 0m, "Validation count", "live-smoke", "validation");
var physicalStock = await operationalReports.LoadPhysicalStockAsync("WLMHW", testDate);
var targetCro = staff.Rows.First(x => x.StoreCode == "WLMHW").CroNumber;
await completion.SaveStaffTargetAsync("WLMHW", targetCro, scope.DateFrom, scope.DateTo, 100_000m, "live-smoke", "validation");
var staffWithTarget = await operationalReports.LoadStaffPerformanceAsync(scope);
var dailyExceptions = await operationalReports.LoadDailyExceptionsAsync("WLMHW", testDate);
var workflow = await dailyRepository.LoadAsync("WLMHW", testDate);
var pack = await new DailyReportingPackService(connectionString).GenerateAsync("WLMHW", new(2026, 8, 25), "live-smoke");
var combinedPack = await new DailyReportingPackService(connectionString).GenerateCombinedAsync(new(2026, 8, 25), "live-smoke");
await VerifyOverlapAwareImportAsync(connectionString);
await VerifyPhase2OperationsAsync(connectionString, files[0]);
if (daily.Status != ReconciliationStatus.Passed || daily.Rows.Count == 0) throw new InvalidOperationException("Daily sales report did not pass live SQL execution.");
if (tenders.Status == ReconciliationStatus.Blocked) throw new InvalidOperationException("Tender reconciliation was blocked in live SQL execution.");
if (stock.Status == ReconciliationStatus.Blocked || stock.Items.Count == 0) throw new InvalidOperationException("Stock reconciliation was blocked or empty in live SQL execution.");
if (invoiceSummary.Count == 0 || dsr.Count != 9 || staff.Rows.Count == 0)
    throw new InvalidOperationException("Operational invoice, DSR or staff reporting did not return the expected live result shape.");
if (workflow.MissingReports.Count != 0 || pack.Sections.Count != 11 || pack.Document.Tables.Count < 10 || pack.GenerationNumber < 1 ||
    !combinedPack.Tables.Any(x => x.Name == "Titan Helios Combined DSR"))
    throw new InvalidOperationException("Daily completeness or reporting-pack generation failed live verification.");
if (workflow.MissingRequiredInputs.Count != 0 || serviceSales.Single(x => x.Period == "FTD").Total != 0 || cashControl.Status != ReconciliationStatus.Passed)
    throw new InvalidOperationException("Zero-safe manual input, service sales or cash reconciliation failed live verification.");
if (!physicalStock.Any(x => x.InventoryGroupCode == "GAUTO" && x.CountedPhysicalQuantity == 0m) ||
    !staffWithTarget.Rows.Any(x => x.StoreCode == "WLMHW" && x.CroNumber == targetCro && x.TargetSales == 100_000m) ||
    dailyExceptions.Count == 0)
    throw new InvalidOperationException("Physical stock, staff target or traceable exception reporting failed live verification.");
var packExcel = Path.Combine(Path.GetTempPath(), $"etp-live-pack-{Guid.NewGuid():N}.xlsx");
var packPdf = Path.Combine(Path.GetTempPath(), $"etp-live-pack-{Guid.NewGuid():N}.pdf");
try
{
    new OpenXmlReportPackExporter().Export(packExcel, combinedPack);
    new SimplePdfReportPackExporter().Export(packPdf, combinedPack);
    if (new FileInfo(packExcel).Length == 0 || new FileInfo(packPdf).Length == 0)
        throw new InvalidOperationException("Complete report-pack exports were empty.");
}
finally
{
    if (File.Exists(packExcel)) File.Delete(packExcel);
    if (File.Exists(packPdf)) File.Delete(packPdf);
}
await VerifyBackupRestoreAsync(connectionString);
Console.WriteLine($"Live SQL passed: files={files.Length}; persisted evidence rows={importedRows}; daily groups={daily.Rows.Count}; invoices={invoiceSummary.Count}; dsr={dsr.Count}; staff={staff.Rows.Count}/{staff.Status}; workflow={workflow.Status}; pack={pack.Status}; brand-segment={brandSegment.Status}; tender={tenders.Status}; stock={stock.Status} ({stock.Items.Count} matched items).");
return files.Length == 12 ? 0 : 1;

static async Task VerifyPhase2OperationsAsync(string connectionString, string duplicateWorkbook)
{
    var repository = new Phase2OperationsRepository(connectionString);
    var access = await repository.LoadCurrentAccessAsync();
    if (!access.CanAdminister) throw new InvalidOperationException("The bootstrap Windows identity was not assigned the Owner role.");
    await repository.UpsertUserAsync(@"NT AUTHORITY\SYSTEM", "ETP Automated Operations", "Store Manager", true, "Live validation of role and SQL access administration");
    await repository.UpsertMasterValueAsync("Brand Segment", "PHASE2_TEST", "Phase 2 validation", "Observed", true, "Live validation of master administration");
    var users = await repository.LoadUsersAsync();
    var masters = await repository.LoadMasterValuesAsync("Brand Segment");
    if (!users.Any(x => x.WindowsIdentity == @"NT AUTHORITY\SYSTEM" && x.RoleCode == "STORE_MANAGER") ||
        !masters.Any(x => x.Code == "GAUTO" && x.ApprovalStatus == "APPROVED") || !masters.Any(x => x.Code == "PHASE2_TEST"))
        throw new InvalidOperationException("Owner-controlled user or master-data administration failed.");

    var archive = await repository.LoadReportGenerationsAsync(businessDate: new(2026, 8, 25));
    var combined = archive.FirstOrDefault(x => x.StoreCode == "COMBINED" && x.CanReExport)
        ?? throw new InvalidOperationException("The combined report archive was not persisted.");
    var archivedDocument = await repository.LoadArchivedReportAsync(combined.Id);
    var wlGenerations = archive.Where(x => x.StoreCode == "WLMHW" && x.CanReExport).Take(2).ToArray();
    if (archivedDocument.Tables.All(x => x.Name != "Titan Helios Combined DSR") || wlGenerations.Length != 2 ||
        (await repository.CompareReportGenerationsAsync(wlGenerations[0].Id, wlGenerations[1].Id)).Count == 0)
        throw new InvalidOperationException("Archived report integrity or comparison failed.");
    if ((await repository.LoadManagementTrendAsync(new(2026, 7, 1), new(2026, 8, 25))).Count == 0)
        throw new InvalidOperationException("Management trend reporting returned no results.");
    _ = await repository.LoadDataQualitySummaryAsync();

    var root = Path.Combine(Path.GetTempPath(), $"EtpLiveAutomation-{Guid.NewGuid():N}");
    var inbound = Path.Combine(root, "Inbound"); var processed = Path.Combine(root, "Processed");
    var failed = Path.Combine(root, "Failed"); var reports = Path.Combine(root, "Reports");
    try
    {
        await repository.SaveWatchFolderSettingsAsync(new(inbound, processed, failed, reports, 5, true, DateTime.MinValue, access.WindowsIdentity),
            "Live validation of unattended duplicate protection");
        Directory.CreateDirectory(inbound);
        var queued = Path.Combine(inbound, Path.GetFileName(duplicateWorkbook));
        File.Copy(duplicateWorkbook, queued);
        File.SetLastWriteTimeUtc(queued, DateTime.UtcNow.AddMinutes(-1));
        var result = await new AutomatedOperationsService(connectionString).RunOnceAsync();
        if (result.SourcesProcessed != 1 || result.DuplicateWorkbooks != 1 || Directory.EnumerateFiles(processed,"*",SearchOption.AllDirectories).Count() != 1 || Directory.EnumerateFiles(inbound).Any())
            throw new InvalidOperationException("Unattended watch-folder duplicate handling failed.");
        if (!(await repository.LoadAutomationRunsAsync()).Any(x => x.RunType == "WATCH_IMPORT" && x.Outcome == "Skipped"))
            throw new InvalidOperationException("Unattended automation history was not recorded.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

static async Task VerifyOverlapAwareImportAsync(string connectionString)
{
    const string sql = """
        SET XACT_ABORT ON; BEGIN TRANSACTION;
        DECLARE @invoice bigint,@store varchar(30),@doc nvarchar(80),@year int,@date date,@line nvarchar(80),@product nvarchar(80),@type nvarchar(80),
                @qty decimal(19,4),@gross decimal(19,4),@net decimal(19,4),@brand nvarchar(80),@brandName nvarchar(200),@segment nvarchar(100),@currency char(3);
        SELECT TOP(1) @invoice=i.sales_invoice_id,@store=i.store_code,@doc=i.document_number,@year=i.invoice_year,@date=i.transaction_date,@line=l.line_identifier,
          @product=l.product_code,@type=l.source_transaction_type,@qty=l.source_quantity,@gross=l.source_gross_amount,@net=l.source_net_amount,
          @brand=l.source_brand_code,@brandName=l.source_brand_name,@segment=l.brand_segment,@currency=l.currency_code
        FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id ORDER BY l.sales_line_id;
        DECLARE @batch uniqueidentifier=NEWID(); INSERT dbo.import_batches(import_batch_id,status,period_start,period_end,started_utc) VALUES(@batch,'Processing',@date,@date,SYSUTCDATETIME());
        INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,source_report_date,imported_by)
        VALUES(@batch,N'overlap-validation.xlsx,',REPLICATE('a',64),1,'R025',@store,@date,@date,N'live-smoke'); DECLARE @file bigint=SCOPE_IDENTITY();
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,N'Overlap',2,'R025_LINE'); DECLARE @same bigint=SCOPE_IDENTITY();
        EXEC dbo.persist_sales_line @store,@doc,@year,@date,@line,@product,@type,@qty,@gross,@net,@brand,@brandName,@segment,@currency,@same;
        INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,N'Overlap',3,'R025_LINE'); DECLARE @different bigint=SCOPE_IDENTITY();
        DECLARE @changedNet decimal(19,4)=@net+1; EXEC dbo.persist_sales_line @store,@doc,@year,@date,@line,@product,@type,@qty,@gross,@changedNet,@brand,@brandName,@segment,@currency,@different;
        IF (SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id=@file AND outcome='ALREADY_PRESENT')<>1 THROW 51290,'Identical overlap was not classified.',1;
        IF (SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id=@file AND outcome='CONFLICT')<>1 THROW 51291,'Changed overlap was not classified as conflict.',1;
        IF (SELECT COUNT(*) FROM dbo.import_conflicts WHERE import_file_id=@file AND status='OPEN')<>1 THROW 51292,'Conflict review item was not created.',1;
        ROLLBACK TRANSACTION;
        """;
    await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
    await using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 }; await command.ExecuteNonQueryAsync();
    Console.WriteLine("Overlap-aware row duplicate and conflict routing passed.");
}

static async Task VerifyBackupRestoreAsync(string connectionString)
{
    var target = new SqlConnectionStringBuilder(connectionString);
    var database = SqlServerDatabaseBootstrapper.ValidateDatabaseName(target.InitialCatalog);
    var restoreDatabase = SqlServerDatabaseBootstrapper.ValidateDatabaseName($"{database}_Restore_{Guid.NewGuid():N}");
    var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
    string backupDirectory;
    string dataDirectory;
    await using (var connection = new SqlConnection(master.ConnectionString))
    {
        await connection.OpenAsync();
        await using var paths = new SqlCommand("SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath')),CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath'))", connection);
        await using var pathReader = await paths.ExecuteReaderAsync();
        await pathReader.ReadAsync();
        backupDirectory = pathReader.GetString(0); dataDirectory = pathReader.GetString(1);
    }
    var backupPath = Path.Combine(backupDirectory, $"{database}_{Guid.NewGuid():N}.bak");
    var restoredFiles = new List<string>();
    try
    {
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync();
        await using (var backup = new SqlCommand($"BACKUP DATABASE [{database}] TO DISK=@path WITH COPY_ONLY,INIT,CHECKSUM", connection) { CommandTimeout = 0 })
        { backup.Parameters.AddWithValue("@path", backupPath); await backup.ExecuteNonQueryAsync(); }
        await using (var verify = new SqlCommand("RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM", connection) { CommandTimeout = 0 })
        { verify.Parameters.AddWithValue("@path", backupPath); await verify.ExecuteNonQueryAsync(); }

        var logicalFiles = new List<(string Name, string Type)>();
        await using (var list = new SqlCommand("RESTORE FILELISTONLY FROM DISK=@path", connection))
        {
            list.Parameters.AddWithValue("@path", backupPath);
            await using var fileReader = await list.ExecuteReaderAsync();
            while (await fileReader.ReadAsync()) logicalFiles.Add((fileReader.GetString(0), fileReader.GetString(2)));
        }
        var moves = new List<string>();
        for (var index = 0; index < logicalFiles.Count; index++)
        {
            var extension = logicalFiles[index].Type == "L" ? ".ldf" : index == 0 ? ".mdf" : $"_{index}.ndf";
            var physical = Path.Combine(dataDirectory, restoreDatabase + extension);
            restoredFiles.Add(physical);
            moves.Add($"MOVE @logical{index} TO @physical{index}");
        }
        await using (var restore = new SqlCommand($"RESTORE DATABASE [{restoreDatabase}] FROM DISK=@path WITH {string.Join(',', moves)},RECOVERY", connection) { CommandTimeout = 0 })
        {
            restore.Parameters.AddWithValue("@path", backupPath);
            for (var index = 0; index < logicalFiles.Count; index++) { restore.Parameters.AddWithValue($"@logical{index}", logicalFiles[index].Name); restore.Parameters.AddWithValue($"@physical{index}", restoredFiles[index]); }
            await restore.ExecuteNonQueryAsync();
        }
        await using (var counts = new SqlCommand($"SELECT (SELECT COUNT_BIG(*) FROM [{database}].dbo.source_lineage),(SELECT COUNT_BIG(*) FROM [{restoreDatabase}].dbo.source_lineage)", connection))
        await using (var countReader = await counts.ExecuteReaderAsync())
        { await countReader.ReadAsync(); if (countReader.GetInt64(0) != countReader.GetInt64(1)) throw new InvalidOperationException("Restored lineage count differs from source database."); }
        await using (var drop = new SqlCommand($"ALTER DATABASE [{restoreDatabase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{restoreDatabase}]", connection) { CommandTimeout = 0 }) await drop.ExecuteNonQueryAsync();
        Console.WriteLine("Backup CHECKSUM, RESTORE VERIFYONLY, full restore and lineage comparison passed.");
    }
    finally
    {
        if (File.Exists(backupPath)) File.Delete(backupPath);
    }
}

static async Task DropValidationDatabasesAsync(string connectionString)
{
    var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
    await using var connection = new SqlConnection(master.ConnectionString);
    await connection.OpenAsync();
    var names = new List<string>();
    await using (var query = new SqlCommand("SELECT name FROM sys.databases WHERE name LIKE N'EtpReportingSprintValidation%'", connection))
    await using (var reader = await query.ExecuteReaderAsync()) while (await reader.ReadAsync()) names.Add(reader.GetString(0));
    foreach (var name in names)
    {
        if (!Regex.IsMatch(name, "^EtpReportingSprintValidation[1-6]?$", RegexOptions.CultureInvariant))
            throw new InvalidOperationException($"Refusing to drop unexpected database '{name}'.");
        await using var drop = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", connection) { CommandTimeout = 0 };
        await drop.ExecuteNonQueryAsync();
        Console.WriteLine($"Dropped temporary validation database {name}.");
    }
}

static async Task VerifyExistingAsync(string connectionString, string migrationDirectory)
{
    var migration = await new SqlServerDatabaseBootstrapper(connectionString, new DirectoryMigrationSource(migrationDirectory)).BootstrapAsync();
    if (migration.AppliedMigrations.Count != 0) throw new InvalidOperationException("Existing database unexpectedly required migrations during final verification.");
    var repository = new SqlServerReportingQueryRepository(connectionString);
    var executor = new SqlBackedReportingExecutor(repository, RetailReportingPolicy.Mapping, RetailReportingPolicy.Sales, RetailReportingPolicy.Tender, RetailReportingPolicy.Stock);
    var scope = new ReportingQueryScope(new(2026, 7, 1), new(2026, 8, 25));
    var daily = await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.Daily);
    var brand = await executor.ExecuteSalesSummaryAsync(scope, SalesSummaryDimension.BrandSegment);
    var tender = await executor.ExecuteTenderReconciliationAsync(scope);
    var stock = await executor.ExecuteStockReconciliationAsync(scope);
    var storeFiltered = await executor.ExecuteSalesSummaryAsync(scope with { StoreCodes = ["WLMHW"] }, SalesSummaryDimension.Daily);
    var diagnostic = new TenderVarianceDiagnosticService().Diagnose(tender, RetailReportingPolicy.Tender.AbsoluteTolerance);
    var operationalHealth = await new DatabaseOperationalHealthRepository(connectionString).LoadAsync();
    await using var connection = new SqlConnection(connectionString); await connection.OpenAsync();
    await using var command = new SqlCommand("SELECT (SELECT COUNT_BIG(*) FROM dbo.import_files),(SELECT COUNT_BIG(*) FROM dbo.source_lineage)", connection);
    await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
    if (reader.GetInt64(0) != 8 || daily.Status != ReconciliationStatus.Passed || brand.Status != ReconciliationStatus.Passed || stock.Status != ReconciliationStatus.Passed || storeFiltered.Rows.Count == 0 || diagnostic.Status != tender.Status)
        throw new InvalidOperationException("Existing application database failed final acceptance checks.");
    Console.WriteLine($"Existing database verified: files={reader.GetInt64(0)}; lineage={reader.GetInt64(1)}; daily={daily.Status}; filtered-sales={storeFiltered.Status}; brand-segment={brand.Status}; tender-control={tender.Status}; tender-diagnostic={diagnostic.Status}; stock={stock.Status}; database-health={operationalHealth.Severity}.");
}
