using System.Globalization;
using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.DailyInputs;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.ImportSourceLocations;
using Etp.Reporting.Infrastructure.SqlServer.ShopResults;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 U2 cross-layer (112-W2-PLAN Appendix B item 5; wave plan C-c): C# meets the real 0069 table, the real day lock and the
/// real audit CHECK. A finalised day refuses a daily-inputs sheet; "Reopen day and re-apply" reopens it through the Owner's
/// service and imports the file again from where ETP keeps it, writing a Hand location row and an ImportFixAction audit row.
/// A Viewer is denied the location table (229; impersonation with REVERT in the batch, on a non-pooled connection); nobody may
/// change a location (DENY, then the append-only trigger 51990). The unattended run on a shop zip writes the location rows
/// and the shop's result file in one pass. Synthetic data only: a seeded store code, figures, no names.
/// </summary>
public sealed class ImportFixReapplySqlTests
{
    private const string Store = "HEMW";

    [SqlFact]
    public async Task A_finalised_day_refuses_a_sheet_then_reopen_and_reapply_imports_it_with_a_location_row_and_an_audit_row()
    {
        var database = new SqlDatabaseFixture();
        var root = TempRoot();
        try
        {
            await database.InitializeAsync();
            var factory = new EtpConnectionFactory(database.ConnectionString);
            var day = DateOnly.FromDateTime(DateTime.Today).AddDays(-2);
            var service = new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString));

            // The first sheet is taken, then the Owner finalises the day (as the existing lock tests do).
            var first = await WriteSheetAsync(Path.Combine(root, "first"), day, walkIns: 3);
            Assert.Equal("Imported", Assert.Single((await service.RunAsync(first, new("TEST\\fix"))).Files).Status);
            await database.ExecuteAsync($"UPDATE dbo.daily_reporting_days SET status='LOCKED',finalised_by=SUSER_SNAME(),finalised_utc=SYSUTCDATETIME() WHERE store_code='{Store}' AND business_date='{day:yyyyMMdd}'");

            // A corrected sheet arrives for the finalised day: refused, nothing written.
            var second = await WriteSheetAsync(Path.Combine(root, "failed"), day, walkIns: 7);
            var refused = Assert.Single((await service.RunAsync(second, new("TEST\\fix"))).Files);
            Assert.Equal(("Failed", "DAILY_INPUTS_DAY_FINALISED"), (refused.Status, refused.Failure?.Code));
            Assert.Equal(3m, await WalkIns(database, day));
            Assert.True(ImportFixAdvisor.IsDayFinalised(refused.Failure!.Code, refused.Failure.SqlNumber));

            // Where the run left it (the unattended run writes this row when it moves the source to Failed).
            await new SqlServerImportSourceLocationStore(factory).RecordAsync(
                [new NewImportSourceLocation(refused.FileName, refused.SourceSha256, second, ImportSourceRoute.Failed)]);

            var fixes = new SqlServerImportFixService(factory);
            var target = new ImportFixTarget(refused.FileName, refused.SourceSha256, Store, day);
            var location = await fixes.LocateAsync(target);
            Assert.NotNull(location);
            Assert.True(location!.Exists);
            Assert.Equal((ImportSourceRoute.Failed, second), (location.Location.Route, location.Location.RoutedPath));
            // The advisor offers the Owner exactly this fix for the row.
            Assert.Contains(ImportFixAdvisor.For(refused.Failure.Code, new(true, true, true, location.UnderConfiguredRoot), refused.Failure.SqlNumber),
                action => action.Kind == ImportFixKind.ReopenDayAndReapply);

            var outcome = await fixes.ReopenAndReapplyAsync(location, target, Store, day, "Synthetic: corrected sheet from the shop");

            Assert.True(outcome.Imported, outcome.Message);
            Assert.Equal(7m, await WalkIns(database, day));
            Assert.Equal("OPEN", await database.ExecuteAsync($"SELECT status FROM dbo.daily_reporting_days WHERE store_code='{Store}' AND business_date='{day:yyyyMMdd}'"));
            Assert.Equal(1, await Count(database, $"dbo.import_source_locations WHERE route='Failed' AND source_sha256='{refused.SourceSha256}'"));
            Assert.Equal(1, await Count(database, $"dbo.import_source_locations WHERE route='Hand' AND source_sha256='{refused.SourceSha256}' AND routed_path=N'{second.Replace("'", "''")}'"));
            Assert.Equal(1, await Count(database, $"dbo.operational_audit WHERE event_type='{OperationalAuditRepository.ImportFixActionType}' AND outcome='Succeeded' AND safe_detail=N'{SqlServerImportFixService.ReapplyAuditDetail}'"));
            Assert.Equal(1, await Count(database, "dbo.operational_audit WHERE event_type='DayReopened'"));
            // The day keeps the reopen with its reason (the same procedure as Today > Close day).
            Assert.Equal(1, await Count(database, $"dbo.daily_reporting_days WHERE store_code='{Store}' AND business_date='{day:yyyyMMdd}' AND reopen_reason=N'Synthetic: corrected sheet from the shop'"));
            // The newest location is now the Hand row, and History's next lookup finds it.
            Assert.Equal(ImportSourceRoute.Hand, (await fixes.LocateAsync(target))!.Location.Route);
        }
        finally
        {
            await database.DisposeAsync();
            DeleteQuietly(root);
        }
    }

    [SqlFact]
    public async Task A_viewer_is_denied_the_location_table_and_nobody_changes_a_location()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await database.ExecuteAsync("""
                CREATE USER fix_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER fix_manager;
                CREATE USER fix_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER fix_viewer;
                """);
            const string insert = "INSERT dbo.import_source_locations(file_name,routed_path,route) VALUES(N'synthetic.xlsx',N'C:\\ETP\\Failed\\synthetic.xlsx','Failed')";

            // The Store Manager (the automation account's role) may read and add a row.
            await NonPooledAsync(database, As("fix_manager", insert));
            Assert.Equal(1, Convert.ToInt32(await NonPooledAsync(database, As("fix_manager", "SELECT COUNT(*) FROM dbo.import_source_locations"))));

            // The Viewer may do neither: the table holds local folder paths (0069 DENY beats 0022's schema-wide SELECT).
            foreach (var sql in new[] { "SELECT COUNT(*) FROM dbo.import_source_locations", insert })
                Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => NonPooledAsync(database, As("fix_viewer", sql)))).Number);

            // Nobody changes a location: the Store Manager is denied, and dbo meets the append-only trigger.
            foreach (var sql in new[] { "UPDATE dbo.import_source_locations SET route='Hand'", "DELETE dbo.import_source_locations" })
            {
                Assert.Equal(229, (await Assert.ThrowsAsync<SqlException>(() => NonPooledAsync(database, As("fix_manager", sql)))).Number);
                Assert.Equal(51990, (await Assert.ThrowsAsync<SqlException>(() => NonPooledAsync(database, sql))).Number);
            }
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_source_locations WHERE route='Failed'")));

            // The C# store, run as the Viewer, finds nothing instead of failing (a Viewer has no Fix action that needs a path).
            Assert.Equal("DENY", await database.ExecuteAsync("""
                SELECT MIN(state_desc) FROM sys.database_permissions
                WHERE major_id=OBJECT_ID('dbo.import_source_locations') AND grantee_principal_id=DATABASE_PRINCIPAL_ID('etp_viewer') AND permission_name='SELECT'
                """));
        }
        finally { await database.DisposeAsync(); }
    }

    [SqlFact]
    public async Task The_unattended_run_on_a_shop_zip_writes_the_location_rows_and_the_result_file_in_one_pass()
    {
        var database = new SqlDatabaseFixture();
        var root = TempRoot();
        try
        {
            await database.InitializeAsync();
            var day = DateOnly.FromDateTime(DateTime.Today).AddDays(-3);
            await new Phase2OperationsRepository(database.ConnectionString).SaveWatchFolderSettingsAsync(new WatchFolderSettings(
                Path.Combine(root, "In"), Path.Combine(root, "Done"), Path.Combine(root, "Failed"), Path.Combine(root, "Reports"), true,
                DateTime.MinValue, "test"), "Synthetic U2 watch-folder test");
            Directory.CreateDirectory(Path.Combine(root, "In"));
            var sheet = await WriteSheetAsync(Path.Combine(root, "sheet"), day, walkIns: 5);
            var zip = Path.Combine(root, "In", $"TITAN-{day:yyyy-MM-dd}.zip");
            ZipFile.CreateFromDirectory(Path.GetDirectoryName(sheet)!, zip, CompressionLevel.Fastest, includeBaseDirectory: false);
            File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(-5)); // past the watch folder's stability wait
            var pending = Path.Combine(root, "ShopResults", "pending");

            var run = await new AutomatedOperationsService(database.ConnectionString, shopResultWriter: new ShopResultWriter(pending)).RunOnceAsync();

            Assert.Equal((1, 0), (run.SourcesProcessed, run.SourcesFailed));
            var moved = Assert.Single(Directory.GetFiles(Path.Combine(root, "Done"), "*.zip"));
            Assert.Equal(1, await Count(database, $"dbo.import_source_locations WHERE route='Processed' AND file_name=N'{Path.GetFileName(sheet)}' AND routed_path=N'{moved.Replace("'", "''")}' AND source_sha256 IS NOT NULL"));
            // The committed import is linked: the file's hash finds its import_files row.
            Assert.Equal(1, await Count(database, "dbo.import_source_locations l WHERE l.import_file_id IS NULL OR EXISTS(SELECT 1 FROM dbo.import_files f WHERE f.import_file_id=l.import_file_id)"));
            var result = Path.Combine(pending, $"TITAN-{day:yyyy-MM-dd}-1.json");
            Assert.True(File.Exists(result));
            Assert.Contains("\"overall\": \"Accepted\"", await File.ReadAllTextAsync(result), StringComparison.Ordinal);
            Assert.DoesNotContain("could not be saved", (string)(await database.ExecuteAsync("SELECT TOP(1) safe_message FROM dbo.automation_runs WHERE run_type='WATCH_IMPORT' ORDER BY automation_run_id DESC"))!);
            Assert.Equal(5m, await WalkIns(database, day));

            // Its location is what a Retry would use: the shop zip behind the routed name is still recognised.
            Assert.True(SqlServerImportFixService.TryParseRoutedShopZip(moved, out var role, out var zipDay));
            Assert.Equal(("TITAN", day), (role, zipDay));
        }
        finally
        {
            await database.DisposeAsync();
            DeleteQuietly(root);
        }
    }

    // ----- helpers -----

    private static string As(string user, string sql) =>
        $"EXECUTE AS USER='{user}'; BEGIN TRY {sql}; REVERT; END TRY BEGIN CATCH REVERT; THROW; END CATCH;";

    /// <summary>A non-pooled connection: an impersonating batch never hands a session back to the pool (SQL 18059).</summary>
    private static async Task<object?> NonPooledAsync(SqlDatabaseFixture database, string sql)
    {
        await using var connection = new SqlConnection(TestSqlConnections.ForDatabase(database.Name, pooling: false));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task<int> Count(SqlDatabaseFixture database, string from) =>
        Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM " + from), CultureInfo.InvariantCulture);

    private static async Task<decimal?> WalkIns(SqlDatabaseFixture database, DateOnly day) =>
        await database.ExecuteAsync($"SELECT numeric_value FROM dbo.manual_operational_inputs WHERE store_code='{Store}' AND business_date='{day:yyyyMMdd}' AND field_code='WALK_INS'") is decimal value ? value : null;

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpImportFix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteQuietly(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The real daily-inputs template (DailyInputsTemplateWriter) for the seeded store and <paramref name="day"/>, with walk-ins
    /// and "Filled by" typed in, saved under the template's own file name in <paramref name="folder"/>.
    /// </summary>
    private static async Task<string> WriteSheetAsync(string folder, DateOnly day, decimal walkIns)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, DailyInputsTemplate.FileName(Store, DailyInputsKind.Retail, day));
        await using var stream = new MemoryStream();
        DailyInputsTemplateWriter.Write(stream, DailyInputsKind.Retail, Store, "Synthetic store", day);
        stream.Position = 0;
        using (var document = SpreadsheetDocument.Open(stream, true))
        {
            var sheet = document.WorkbookPart!.WorksheetParts.Single().Worksheet!;
            SetValue(sheet, DailyInputsTemplate.FilledByKey, "Synthetic staff");
            SetValue(sheet, "WALK_INS", walkIns);
            sheet.Save();
        }
        await File.WriteAllBytesAsync(path, stream.ToArray());
        return path;
    }

    private static void SetValue(Worksheet sheet, string key, object value)
    {
        var row = sheet.Descendants<Row>().Single(r =>
            r.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == $"A{r.RowIndex!.Value}")?.InnerText == key);
        var cell = row.Elements<Cell>().Single(c => c.CellReference?.Value == $"C{row.RowIndex!.Value}");
        cell.InlineString = null; cell.CellValue = null; cell.DataType = null;
        if (value is string text)
        {
            cell.DataType = CellValues.InlineString;
            cell.InlineString = new InlineString(new Text(text));
        }
        else cell.CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!);
    }
}
