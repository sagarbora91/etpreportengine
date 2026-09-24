using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class CrossPhaseStoreManagerImportTests
{
    [Theory]
    [InlineData("R018")]
    [InlineData("R019")]
    public async Task Numeric_state_code_formatting_preserves_valid_superset_promotion(string report)
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedRoles(database);
            using var session = new RestrictedConnections(database.Name, "crossphase_manager", "etp_store_manager");
            var service = new SqlServerImportPersistenceUseCase(session.ConnectionString);
            var sample = await new OpenXmlWorkbookReader().ReadAsync(Directory.GetFiles(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), report + "_*.xlsx").Single());
            var columns = EtpReportFamilyRegistry.Resolve(report).Columns;
            WorkbookRow SourceRow(int rowNumber, string state, string invoice)
            {
                var cells = sample.Sheets[0].Rows[0].Cells.ToArray();
                foreach (var (field, value) in new[] { ("issue_state_code", state), ("recipient_state_code", state), ("doc_invoice_no", invoice) })
                {
                    var header = columns.Single(c => c.CanonicalField == field).SourceHeader;
                    cells[sample.Sheets[0].Headers.ToList().IndexOf(header)] = new(value);
                }
                return new(rowNumber, cells);
            }
            await Save(service, Workbook(sample, [SourceRow(2, "07", "SYNTHETIC-OLD")]));
            await Save(service, Workbook(sample, [SourceRow(2, "7", "SYNTHETIC-OLD"), SourceRow(3, "7", "SYNTHETIC-ADDED")]));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_restatements"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files WHERE is_superseded=0"));
            Assert.Equal(2, await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.{EtpReportFamilyRegistry.Resolve(report).TableName} r JOIN dbo.import_files f ON f.import_file_id=r.import_file_id WHERE f.is_superseded=0"));
            session.AssertCoverage(6);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Real_folder_and_retained_links_work_as_manager_on_every_repository_connection()
    {
        var database = new SqlDatabaseFixture();
        var folder = Path.Combine(Path.GetTempPath(), "EtpRestrictedFolder_" + Guid.NewGuid().ToString("N"));
        try
        {
            await database.InitializeAsync();
            await SeedRoles(database);
            using var session = new RestrictedConnections(database.Name, "crossphase_manager", "etp_store_manager");
            var persistence = new SqlServerImportPersistenceUseCase(session.ConnectionString);
            var documents = new ProductisationRepository(session.ConnectionString);
            var service = new FolderImportService(persistence, retainEvidence: async (path, accepted, store, date, token) =>
            {
                var document = await documents.RegisterDocumentAsync(accepted.Workbook.FileName, path,
                    accepted.Workbook.Sha256, accepted.Workbook.FileSizeBytes, "ETP_WORKBOOK",
                    accepted.ProfileIdentity.ReportCode, store, date, "VALIDATED", "Synthetic source", token);
                await documents.LinkDocumentToImportAsync(document.Id, accepted.Workbook.Sha256,
                    accepted.ProfileIdentity.ReportCode, store, date, token);
            });
            Directory.CreateDirectory(folder);
            foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "*.xlsx"))
                File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
            // The independent R010 and R011 fixtures describe conflicting snapshots of the
            // same item. Give the R010 sample its own item so this tests all family permissions.
            var binPath = Directory.GetFiles(folder, "R010_*.xlsx").Single();
            var bin = await new OpenXmlWorkbookReader().ReadAsync(binPath);
            var itemColumn = bin.Sheets[0].Headers.ToList().IndexOf("ITEMNUMBER");
            var columnName = "";
            for (var number = itemColumn + 1; number > 0; number = (number - 1) / 26)
                columnName = (char)('A' + (number - 1) % 26) + columnName;
            using (var workbook = SpreadsheetDocument.Open(binPath, true))
            {
                var sheet = workbook.WorkbookPart!.WorksheetParts.First().Worksheet;
                foreach (var row in bin.Sheets[0].Rows)
                {
                    var cell = sheet.Descendants<Cell>().Single(c => c.CellReference?.Value == columnName + row.RowNumber);
                    cell.DataType = CellValues.InlineString;
                    cell.CellValue = null;
                    cell.InlineString = new InlineString(new Text("R010-SYNTHETIC-" + row.RowNumber));
                }
                sheet.Save();
            }
            var first = await service.RunAsync(folder, new("Restricted folder test"));
            Assert.Equal(32, first.Files.Count);
            Assert.All(first.Files, f => Assert.True(f.Status is "Imported" or "empty export", $"{f.FileName}: {f.Status}; {f.Message}"));
            Assert.Equal(32, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            Assert.Equal(32, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.source_document_import_links"));
            Assert.True(Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines")) > 0);
            Assert.True(Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_line_enrichments WHERE content_key IS NOT NULL AND source_gross_value IS NOT NULL")) > 0);
            Assert.True(Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.staff")) > 0);
            var repeat = await service.RunAsync(folder, new("Restricted folder test"));
            Assert.All(repeat.Files, f => Assert.Equal("Duplicate", f.Status));
            Assert.Equal(0, repeat.NewRows);
            session.AssertCoverage(minimumConnections: 32 * 4);
        }
        finally
        {
            await database.DisposeAsync();
            if (!Path.GetFileName(folder).StartsWith("EtpRestrictedFolder_", StringComparison.Ordinal) ||
                !Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe synthetic folder cleanup path.");
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Manager_duplicates_supersets_and_brands_preserve_fact_denials_and_locked_dates()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedRoles(database);
            using var session = new RestrictedConnections(database.Name, "crossphase_manager", "etp_store_manager");
            var service = new SqlServerImportPersistenceUseCase(session.ConnectionString);
            var sample = await Sample();
            var a = Row(sample, 2, "100000001", new(2026, 8, 1));
            var b = Row(sample, 3, "100000002", new(2026, 8, 25));
            await Save(service, Workbook(sample, [a]));
            var duplicate = await Save(service, Workbook(sample, [a]));
            Assert.Equal("Duplicate content", duplicate.Status);
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
            await Save(service, Workbook(sample, [a, b]));
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files WHERE is_superseded=0"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_restatements"));
            Assert.Equal(await database.ExecuteAsync("SELECT windows_identity FROM dbo.application_users WHERE display_name=N'Synthetic manager'"), await database.ExecuteAsync("SELECT requested_by FROM dbo.import_restatements"));
            foreach (var sql in new[]
            {
                "UPDATE dbo.import_files SET is_superseded=1",
                "UPDATE dbo.sales_lines SET source_net_amount=0",
                "DELETE dbo.sales_line_enrichments",
                "UPDATE dbo.staff SET staff_name=N'Forged'",
                "DELETE dbo.etp_import_content",
                "DELETE dbo.etp_r025",
                "DELETE dbo.source_document_import_links",
                "UPDATE dbo.brand_rows SET row_label=N'Forged'",
                "EXEC dbo.prepare_import_restatement 1,2,N'Forged',N'Forged correction'",
                "EXEC dbo.replace_import_facts_internal 1,2,N'Forged',N'Forged correction'"
            })
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => Execute(session.ConnectionString, sql));
                Assert.Equal(sql.StartsWith("EXEC dbo.prepare_import_restatement", StringComparison.Ordinal) ? 51422 : 229, denied.Number);
            }

            var brands = new EveningMasterRepository(session.ConnectionString);
            await brands.SaveBrandAsync(new(0, "HEMW", "Manager brand", 99, "SYNTHETIC"));
            var brand = Assert.Single(await brands.LoadBrandsAsync(), x => x.Label == "Manager brand");
            await brands.SaveBrandAsync(brand with { Label = "Manager revised brand", SourceCodes = "SYNTHETIC,SECOND" });
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.brand_row_codes WHERE source_brand IN('SYNTHETIC','SECOND')"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DataTruthMasterRepository(session.ConnectionString)
                .SaveStaffAsync(new("HEMW", "A", "Forbidden", true)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DataTruthMasterRepository(session.ConnectionString)
                .SaveMonthlyTargetAsync(new("HEMW", new(2026, 8, 1), 1)));

            // An incomplete staged replacement cannot exploit the promotion procedure directly.
            var invalidPromotion = await Assert.ThrowsAsync<SqlException>(() => Execute(session.ConnectionString, """
                BEGIN TRANSACTION;
                DECLARE @previous bigint=(SELECT import_file_id FROM dbo.import_files WHERE is_superseded=0),@batch uniqueidentifier=NEWID(),@fresh bigint;
                INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Processing',SYSUTCDATETIME());
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
                VALUES(@batch,'synthetic.xlsx',REPLICATE('a',64),1,'R025','HEMW','20260825','20260801','20260825',1);
                SET @fresh=SCOPE_IDENTITY();
                EXEC dbo.promote_import_superset @previous,@fresh;
                COMMIT TRANSACTION;
                """));
            Assert.Equal(51424, invalidPromotion.Number);
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
            Assert.Equal(3, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            var copiedKeyAttack = await Assert.ThrowsAsync<SqlException>(() => CopySourceAndPromote(session.ConnectionString, changeValues: true));
            Assert.Equal(51424, copiedKeyAttack.Number);
            var factIds = await database.ExecuteAsync("SELECT STRING_AGG(CONVERT(varchar(20),sales_line_id),',') WITHIN GROUP(ORDER BY sales_line_id) FROM dbo.sales_lines");
            var factValues = await database.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines");
            // Stage valid typed source rows, promote and COMMIT without invoking any canonical
            // persistence procedure. Historical facts must survive this direct SQL route.
            await CopySourceAndPromote(session.ConnectionString, changeValues: false);
            Assert.Equal(factIds, await database.ExecuteAsync("SELECT STRING_AGG(CONVERT(varchar(20),sales_line_id),',') WITHIN GROUP(ORDER BY sales_line_id) FROM dbo.sales_lines"));
            Assert.Equal(factValues, await database.ExecuteAsync("SELECT SUM(source_gross_amount) FROM dbo.sales_lines"));
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines l JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=s.import_file_id WHERE f.is_superseded=0"));
            await database.ExecuteAsync("INSERT dbo.daily_reporting_days(store_code,business_date,status,finalised_by,finalised_utc) VALUES('HEMW','20260801','LOCKED',SUSER_SNAME(),SYSUTCDATETIME())");
            var locked = await Assert.ThrowsAsync<SqlException>(() => Save(service, Workbook(sample, [a, b, Row(sample, 4, "100000003", new(2026, 8, 26))])));
            Assert.Equal(51021, locked.Number);
            Assert.Equal(2, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
            Assert.Equal(4, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            session.AssertCoverage(20);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Viewer_cannot_edit_brands_even_with_accidental_execute_grant()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedRoles(database);
            await database.ExecuteAsync("GRANT EXECUTE ON dbo.save_evening_brand TO crossphase_viewer");
            using var session = new RestrictedConnections(database.Name, "crossphase_viewer", "etp_viewer");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new EveningMasterRepository(session.ConnectionString)
                .SaveBrandAsync(new(0, "HEMW", "Forbidden", 1, "FORBIDDEN")));
            var denied = await Assert.ThrowsAsync<SqlException>(() => Execute(session.ConnectionString,
                "EXEC dbo.save_evening_brand 0,'HEMW',N'Forbidden',1,N'[\"FORBIDDEN\"]'"));
            Assert.Equal(51420, denied.Number);
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.brand_rows WHERE row_label='Forbidden'"));
            session.AssertCoverage(2);
        }
        finally { await database.DisposeAsync(); }
    }

    private static Task<object?> SeedRoles(SqlDatabaseFixture database) => database.ExecuteAsync("""
        CREATE USER crossphase_manager WITHOUT LOGIN;
        ALTER ROLE etp_store_manager ADD MEMBER crossphase_manager;
        CREATE USER crossphase_viewer WITHOUT LOGIN;
        ALTER ROLE etp_viewer ADD MEMBER crossphase_viewer;
        DECLARE @manager nvarchar(200),@viewer nvarchar(200);
        EXECUTE AS USER='crossphase_manager'; SET @manager=SUSER_SNAME(); REVERT;
        EXECUTE AS USER='crossphase_viewer'; SET @viewer=SUSER_SNAME(); REVERT;
        INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
        VALUES(@manager,N'Synthetic manager','STORE_MANAGER',1,SUSER_SNAME(),N'Synthetic test role'),
              (@viewer,N'Synthetic viewer','VIEWER',1,SUSER_SNAME(),N'Synthetic test role');
        """);

    private static async Task<object?> Execute(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private static async Task CopySourceAndPromote(string connectionString, bool changeValues)
    {
        var family = EtpReportFamilyRegistry.Resolve("R025");
        var columns = string.Join(',', family.Columns.Select(c => "r.[" + c.CanonicalField + "]"));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            long previous;
            var rows = new List<object[]>();
            await using (var query = new SqlCommand("SELECT import_file_id FROM dbo.import_files WHERE report_code='R025' AND is_superseded=0", connection, transaction))
                previous = Convert.ToInt64(await query.ExecuteScalarAsync());
            await using (var query = new SqlCommand($"SELECT l.source_row_number,r.content_key,{columns} FROM dbo.etp_r025 r JOIN dbo.source_lineage l ON l.source_lineage_id=r.source_lineage_id WHERE r.import_file_id=@previous", connection, transaction))
            {
                query.Parameters.AddWithValue("@previous", previous);
                await using var reader = await query.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var values = new object[reader.FieldCount]; reader.GetValues(values); rows.Add(values);
                }
            }
            long file;
            await using (var create = new SqlCommand("""
                DECLARE @batch uniqueidentifier=NEWID();
                INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(@batch,'Processing',SYSUTCDATETIME());
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes,report_code,store_code,business_date,period_start,period_end,data_truth_version)
                SELECT @batch,'synthetic-copy.xlsx',@hash,1,report_code,store_code,business_date,period_start,period_end,1 FROM dbo.import_files WHERE import_file_id=@previous;
                SELECT CONVERT(bigint,SCOPE_IDENTITY());
                """, connection, transaction))
            {
                create.Parameters.AddWithValue("@previous", previous);
                create.Parameters.AddWithValue("@hash", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
                file = Convert.ToInt64(await create.ExecuteScalarAsync());
            }
            foreach (var row in rows)
            {
                long lineage;
                await using (var create = new SqlCommand("INSERT dbo.source_lineage(import_file_id,sheet_name,source_row_number,source_record_type) VALUES(@file,'Data',@row,'R025_SOURCE'); SELECT CONVERT(bigint,SCOPE_IDENTITY())", connection, transaction))
                {
                    create.Parameters.AddWithValue("@file", file); create.Parameters.AddWithValue("@row", row[0]);
                    lineage = Convert.ToInt64(await create.ExecuteScalarAsync());
                }
                var parameters = string.Join(',', family.Columns.Select((_, i) => "@v" + i));
                await using var append = new SqlCommand("EXEC dbo.append_etp_r025 @file,@lineage,@key," + parameters, connection, transaction);
                append.Parameters.AddWithValue("@file", file); append.Parameters.AddWithValue("@lineage", lineage); append.Parameters.AddWithValue("@key", row[1]);
                for (var i = 0; i < family.Columns.Count; i++)
                    append.Parameters.AddWithValue("@v" + i, changeValues && family.Columns[i].CanonicalField == "customer_name" ? "Changed source with copied key" : row[i + 2]);
                await append.ExecuteNonQueryAsync();
            }
            await using (var promote = new SqlCommand("EXEC dbo.promote_import_superset @previous,@file", connection, transaction))
            {
                promote.Parameters.AddWithValue("@previous", previous); promote.Parameters.AddWithValue("@file", file);
                await promote.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        catch
        {
            if (transaction.Connection is not null) await transaction.RollbackAsync();
            throw;
        }
    }

    private static Task<WorkbookSnapshot> Sample() => new OpenXmlWorkbookReader().ReadAsync(
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single());
    private static WorkbookRow Row(WorkbookSnapshot sample, int number, string invoice, DateOnly date)
    {
        var cells = sample.Sheets[0].Rows[0].Cells.ToArray();
        cells[sample.Sheets[0].Headers.ToList().IndexOf("INVNUMBER")] = new(invoice);
        cells[sample.Sheets[0].Headers.ToList().IndexOf("INVDATE")] = new(date);
        return new(number, cells);
    }
    private static WorkbookSnapshot Workbook(WorkbookSnapshot sample, WorkbookRow[] rows) => sample with
    { Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), Sheets = [sample.Sheets[0] with { Rows = rows }] };
    private static Task<Etp.Reporting.Application.Imports.ImportPersistenceResult> Save(SqlServerImportPersistenceUseCase service, WorkbookSnapshot workbook)
    {
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(workbook);
        return service.PersistAsync(new(accepted, accepted.Scope.PeriodEnd!.Value, accepted.Scope.StoreCode!, "Synthetic importer"));
    }

    /// <summary>Test-only observer: all production-created connections for this exact test target
    /// are observed at open. Every actual command is impersonated and checked by SQL before its payload runs, using a verified
    /// connection. No production connection factory or access check is replaced.</summary>
    private sealed class RestrictedConnections : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string database;
        private readonly string user;
        private readonly string role;
        private readonly string application = "CrossPhaseRoles_" + Guid.NewGuid().ToString("N");
        private readonly ConcurrentDictionary<SqlConnection, byte> verified = new();
        private readonly ConcurrentQueue<Exception> failures = new();
        private readonly List<IDisposable> subscriptions = [];
        private readonly IDisposable all;
        private int commands;
        private readonly ConcurrentDictionary<string, string> events = new();
        public string ConnectionString { get; }

        public RestrictedConnections(string database, string user, string role)
        {
            this.database = database; this.user = user; this.role = role;
            var builder = new DbConnectionStringBuilder { ConnectionString = TestSqlConnections.ForDatabase(database, pooling: false) };
            builder["Application Name"] = application;
            ConnectionString = builder.ConnectionString;
            all = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name.Contains("SqlClient", StringComparison.Ordinal))
                subscriptions.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> item)
        {
            var payload = item.Value;
            events.TryAdd(item.Key, string.Join(",", payload?.GetType().GetProperties().Select(x => x.Name) ?? []));
            var connection = payload?.GetType().GetProperty("Connection")?.GetValue(payload) as SqlConnection
                ?? (payload?.GetType().GetProperty("Command")?.GetValue(payload) as SqlCommand)?.Connection;
            if (connection is null) return;
            var target = new SqlConnectionStringBuilder(connection.ConnectionString);
            if (target.InitialCatalog != database || target.ApplicationName != application) return;
            try
            {
                if (item.Key.EndsWith("WriteConnectionOpenBefore", StringComparison.Ordinal))
                {
                    Assert.False(target.Pooling);
                    Assert.True(verified.TryAdd(connection, 0), "The same restricted connection was unexpectedly reopened.");

                }
                else if (item.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal))
                {
                    Assert.True(verified.ContainsKey(connection), "A repository command used an unverified privileged connection. " + string.Join("; ", events.Select(x => x.Key + ":" + x.Value)));
                    Assert.Equal(System.Data.ConnectionState.Open, connection.State);
                    var command = payload?.GetType().GetProperty("Command")?.GetValue(payload) as SqlCommand;
                    Assert.NotNull(command);
                    Assert.Equal(System.Data.CommandType.Text, command.CommandType);
                    // Prefix the actual command instead of issuing a reentrant command inside
                    // OpenAfter (SqlClient has not completed its asynchronous open yet).
                    command.CommandText = $"IF USER_NAME()=N'dbo' EXECUTE AS USER='{user}'; " +
                        $"IF USER_NAME()<>N'{user}' OR SUSER_SNAME() IS NULL OR COALESCE(IS_ROLEMEMBER('{role}'),0)<>1 " +
                        "OR COALESCE(IS_ROLEMEMBER('db_owner'),0)<>0 OR COALESCE(IS_ROLEMEMBER('db_datawriter'),0)<>0 " +
                        "OR COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>0 BEGIN DECLARE @testAccess nvarchar(400)=CONCAT(USER_NAME(),'|',SUSER_SNAME(),'|',IS_ROLEMEMBER('etp_store_manager'),'|',IS_ROLEMEMBER('etp_viewer'),'|',IS_ROLEMEMBER('db_owner'),'|',IS_ROLEMEMBER('db_datawriter'),'|',IS_SRVROLEMEMBER('sysadmin')); THROW 51999,@testAccess,1; END; " + command.CommandText;
                    Interlocked.Increment(ref commands);
                }
            }
            catch (Exception exception) { failures.Enqueue(exception); throw; }
        }
        public void AssertCoverage(int minimumConnections)
        {
            Assert.Empty(failures);
            Assert.True(verified.Count >= minimumConnections, $"Only {verified.Count} connections were observed; expected at least {minimumConnections}.");
            Assert.True(commands >= verified.Count, "Commands were not observed on every restricted connection.");
        }
        public void OnError(Exception error) => failures.Enqueue(error);
        public void OnCompleted() { }
        public void Dispose()
        {
            all.Dispose();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }
}
