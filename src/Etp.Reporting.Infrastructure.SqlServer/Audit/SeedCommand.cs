using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Audit;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer.Audit;

/// <summary>
/// <c>seed</c>: the old ImportAudit behaviour, the only command that writes (design 3.7). It bootstraps the tool's
/// migrations into a scratch database, drops it first with <c>--rebuild</c>, imports <c>--folder</c>s through
/// <see cref="FolderImportService"/>, and prints aggregate counts. The name is checked again here, so no caller can
/// reach live <c>EtpReporting</c> past the parser.
/// </summary>
internal static class SeedCommand
{
    public static async Task<int> RunAsync(AuditCommand command, string migrationsDirectory, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        var database = command.Database;
        if (!AuditCommandLine.IsScratchDatabase(database))
        {
            await error.WriteLineAsync("seed writes only to a scratch database: EtpReportingHelios, EtpPhase1Test_*, EtpAccept_* or EtpStaging_*.").ConfigureAwait(false);
            return AuditExitCodes.Usage;
        }
        // The same boundary as the application: a SQL Server on this computer, Windows authentication, Encrypt=Optional.
        var connectionString = LocalSqlConnectionPolicy.Validate(new SqlConnectionStringBuilder
            { DataSource = command.Server, InitialCatalog = database, IntegratedSecurity = true, ConnectTimeout = 5 }.ConnectionString);
        if (command.Rebuild)
        {
            var master = LocalSqlConnectionPolicy.Validate(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString);
            await using var connection = new SqlConnection(master);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            // The name matched the scratch pattern (letters, digits, underscores), so it is safe inside brackets.
            await using var drop = new SqlCommand($"IF DB_ID(N'{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END", connection);
            await drop.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await new SqlServerDatabaseBootstrapper(connectionString, new DirectoryMigrationSource(migrationsDirectory)).BootstrapAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteLineAsync($"Audit database ready: {database}").ConfigureAwait(false);
        if (command.MigrateOnly) return AuditExitCodes.Clean;

        var exitCode = AuditExitCodes.Clean;
        if (command.Folders.Count > 0)
        {
            var knownStores = await new StoreCatalogRepository(connectionString).ActiveCodesAsync(cancellationToken).ConfigureAwait(false);
            var service = new FolderImportService(new SqlServerImportPersistenceUseCase(connectionString), knownStores: knownStores);
            var summary = command.Folders.Count == 1
                ? await service.RunAsync(command.Folders[0], new(Environment.UserName), cancellationToken: cancellationToken).ConfigureAwait(false)
                : await service.RunFilesAsync(command.Folders.SelectMany(path => Directory.EnumerateFiles(path, "*.xlsx", SearchOption.AllDirectories))
                    .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal)).ToArray(), new(Environment.UserName),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var file in summary.Files)
                await output.WriteLineAsync($"{file.FileName} | {file.ReportCode} | {file.StoreCode} | {file.PeriodStart:yyyy-MM-dd}..{file.PeriodEnd:yyyy-MM-dd} | {file.Status} | rows={file.RowsProcessed} new={file.NewRows} present={file.AlreadyPresentRows} conflicts={file.ConflictRows} | {file.Message}").ConfigureAwait(false);
            foreach (var file in summary.Files.Where(file => file.Failed))
                foreach (var diagnostic in file.Diagnostics ?? [])
                    await output.WriteLineAsync($"  {diagnostic.Code}: {ImportDiagnosticCatalogue.SafeMessage(diagnostic.Code, diagnostic.Message)}").ConfigureAwait(false);
            if (summary.Files.Any(file => file.Failed)) exitCode = AuditExitCodes.Findings;
        }
        await using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            const string sql = """
              SELECT 'sales_lines' metric,CONVERT(varchar(80),COUNT(*)) value FROM dbo.sales_lines
              UNION ALL SELECT 'sales_invoices',CONVERT(varchar(80),COUNT(*)) FROM dbo.sales_invoices
              UNION ALL SELECT 'conflict_outcomes',CONVERT(varchar(80),COUNT(*)) FROM dbo.import_row_outcomes WHERE outcome='CONFLICT'
              UNION ALL SELECT 'stock_movements',CONVERT(varchar(80),COUNT(*)) FROM dbo.stock_movements;
              SELECT i.store_code,FORMAT(i.transaction_date,'yyyy-MM') month,COUNT(DISTINCT i.sales_invoice_id) documents,
                SUM(CASE WHEN l.source_transaction_type='INV' THEN 0 ELSE 1 END) return_lines,
                SUM(l.source_quantity) qty,SUM(l.source_gross_amount) netamount,SUM(l.source_net_amount) netvalue,SUM(l.source_tax_amount) tax
              FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
              GROUP BY i.store_code,FORMAT(i.transaction_date,'yyyy-MM') ORDER BY i.store_code,month;
              SELECT store_code,document_number,invoice_year,transaction_date FROM dbo.sales_invoices WHERE document_number='100000068' ORDER BY store_code,invoice_year;
              """;
            await using var query = new SqlCommand(sql, connection);
            await using var reader = await query.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            do
            {
                await output.WriteLineAsync(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName))).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    await output.WriteLineAsync(string.Join(" | ", Enumerable.Range(0, reader.FieldCount)
                        .Select(i => Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)))).ConfigureAwait(false);
            } while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));
        }
        return exitCode;
    }
}
