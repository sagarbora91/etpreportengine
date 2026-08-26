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

var expected = new[] { "SDB-VariantwiseSales", "Revenue Report", "Variant Stock ledger", "Closing Stock" };
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
if (daily.Status != ReconciliationStatus.Passed || daily.Rows.Count == 0) throw new InvalidOperationException("Daily sales report did not pass live SQL execution.");
if (tenders.Status == ReconciliationStatus.Blocked) throw new InvalidOperationException("Tender reconciliation was blocked in live SQL execution.");
if (stock.Status == ReconciliationStatus.Blocked || stock.Items.Count == 0) throw new InvalidOperationException("Stock reconciliation was blocked or empty in live SQL execution.");
await VerifyBackupRestoreAsync(connectionString);
Console.WriteLine($"Live SQL passed: files={files.Length}; persisted evidence rows={importedRows}; daily groups={daily.Rows.Count}; brand-segment={brandSegment.Status}; tender={tenders.Status}; stock={stock.Status} ({stock.Items.Count} matched items).");
return files.Length == 8 ? 0 : 1;

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
