using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Service;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.TestSupport.Service;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests.Service;

/// <summary>
/// The Service folder import against SQL (lane L3, decision 15) on L0's synthetic fixtures: 35 landed families, the Not
/// needed list, duplicates, a second week beside the first, a changed file for an imported date, the import roles, no
/// day row for AW330 and no automatic pack. Needs 0048 (L0, L2) and L1's catalogue; it runs only in the elevated gate.
/// </summary>
public sealed class ServiceImportSqlTests
{
    private static readonly string[] NotNeededCodes = ["S001", "S005", "S027", "S028", "S038"];

    [Fact]
    public async Task Week1_imports_35_families_then_duplicates_then_week2_lands_beside_it()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var service = new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString));

            var week1 = await service.RunAsync(ServiceFixtures.Week1Folder, new("Synthetic Owner"));
            Assert.Equal(35, week1.Imported + week1.Files.Count(file => file.Status == "empty export"));
            Assert.Equal(0, week1.Failed);
            Assert.Equal(0, week1.UnknownLayouts);
            var notNeeded = week1.Files.Where(file => file.Status == "Not needed").ToArray();
            Assert.Equal(NotNeededCodes, notNeeded.Where(file => file.ReportCode is not null).Select(file => file.ReportCode!).Order());
            Assert.Single(notNeeded, file => file.FileName.StartsWith("00_", StringComparison.Ordinal));
            Assert.All(week1.Files.Where(file => file.Status is "Imported" or "empty export"), file =>
            {
                Assert.Equal(ServiceInterimFamilies.ServiceStoreCode, file.StoreCode);
                Assert.Equal(new DateOnly(2026, 9, 28), file.PeriodEnd);
            });
            Assert.Equal(35, await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.import_files WHERE report_code LIKE 'S[0-9][0-9][0-9]' AND store_code='AW330' AND period_end='20260928'"));
            Assert.Empty(AutomatedOperationsService.ImportedDates(week1));

            var again = await service.RunAsync(ServiceFixtures.Week1Folder, new("Synthetic Owner"));
            Assert.Equal(35, again.Duplicates);
            Assert.Equal(0, again.Imported);
            Assert.Equal(35, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files WHERE report_code LIKE 'S[0-9][0-9][0-9]'"));

            var week2 = await service.RunAsync(ServiceFixtures.Week2Folder, new("Synthetic Owner"));
            Assert.Equal(35, week2.Imported + week2.Files.Count(file => file.Status == "empty export"));
            Assert.Equal(0, week2.Failed);
            Assert.Equal(70, await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.import_files WHERE report_code LIKE 'S[0-9][0-9][0-9]' AND is_superseded=0"));
            Assert.Equal(35, await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.import_files WHERE report_code LIKE 'S[0-9][0-9][0-9]' AND period_end='20261005'"));

            // No Service day locking: AW330 never gets a reporting day, and the landing triggers stay inert for it.
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.daily_reporting_days WHERE store_code='AW330'"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task A_changed_file_for_an_imported_date_is_refused_for_S009_only()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var service = new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString));
            await service.RunAsync(ServiceFixtures.Week1Folder, new("Synthetic Owner"));

            var changed = await service.RunAsync(ServiceFixtures.SameDateChangedFolder, new("Synthetic Owner"));
            var file = Assert.Single(changed.Files);
            Assert.Equal("S009", file.ReportCode);
            Assert.Equal("Failed", file.Status);
            Assert.Equal("IMPORT_PERIOD_ALREADY_PRESENT", file.Failure!.Code);
            Assert.Equal(1, await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.import_files WHERE report_code='S009' AND period_end='20260928'"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task A_store_manager_imports_service_files_and_a_viewer_is_refused()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await SeedRoles(database);
            using (var manager = new RestrictedConnections(database.Name, "service_manager", "etp_store_manager"))
            {
                var summary = await new FolderImportService(new SqlServerImportPersistenceUseCase(manager.ConnectionString))
                    .RunAsync(ServiceFixtures.Week1Folder, new("Synthetic manager"));
                Assert.Equal(35, summary.Imported + summary.Files.Count(file => file.Status == "empty export"));
                Assert.Equal(0, summary.Failed);
                manager.AssertCoverage(35);
            }
            using (var viewer = new RestrictedConnections(database.Name, "service_viewer", "etp_viewer"))
            {
                // Recording the viewer's attempts may itself be refused, which the run rethrows at its end; the file
                // results are read from its progress either way.
                IReadOnlyList<FolderImportFileResult> seen = [];
                var progress = new InlineProgress(value => seen = value.Files);
                await Record.ExceptionAsync(() => new FolderImportService(new SqlServerImportPersistenceUseCase(viewer.ConnectionString))
                    .RunAsync(ServiceFixtures.Week2Folder, new("Synthetic viewer"), progress));
                Assert.DoesNotContain(seen, file => file.Status is "Imported" or "empty export");
                var failed = seen.Where(file => file.Status == "Failed").ToArray();
                Assert.Equal(35, failed.Length);
                Assert.All(failed, file => Assert.Equal("IMPORT_ACCESS_DENIED", file.Failure!.Code));
            }
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files WHERE period_end='20261005'"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task The_watch_folder_imports_a_service_zip_and_queues_no_report_pack()
    {
        var database = new SqlDatabaseFixture();
        var root = Path.Combine(Path.GetTempPath(), "EtpServiceWatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await database.InitializeAsync();
            var repository = new Phase2OperationsRepository(database.ConnectionString);
            await repository.SaveWatchFolderSettingsAsync(new WatchFolderSettings(Path.Combine(root, "In"), Path.Combine(root, "Done"),
                Path.Combine(root, "Failed"), Path.Combine(root, "Reports"), true, DateTime.MinValue, "test"), "Synthetic Service watch-folder test");
            Directory.CreateDirectory(Path.Combine(root, "In"));
            // The ZIP's name dates its root entries (tier 6), as the dated folder does for a folder import.
            var zip = Path.Combine(root, "In", "Service Centre till 28 sep 2026.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(ServiceFixtures.Week1Folder, zip, System.IO.Compression.CompressionLevel.Fastest, includeBaseDirectory: false);
            File.SetLastWriteTimeUtc(zip, DateTime.UtcNow.AddMinutes(-5)); // past the watch folder's stability wait

            var run = await new AutomatedOperationsService(database.ConnectionString).RunOnceAsync();

            Assert.Equal(1, run.SourcesProcessed);
            Assert.Equal(0, run.SourcesFailed);
            Assert.Equal(0, run.PacksGenerated);
            Assert.Equal(35, await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.import_files WHERE report_code LIKE 'S[0-9][0-9][0-9]' AND store_code='AW330' AND period_end='20260928'"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.automation_runs WHERE run_type='WATCH_IMPORT' AND outcome='Succeeded'"));
            Assert.Equal(0, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.automation_runs WHERE run_type='AUTO_REPORT_PACK'"));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "Reports"), "*", SearchOption.AllDirectories));
        }
        finally
        {
            await database.DisposeAsync();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static Task<object?> SeedRoles(SqlDatabaseFixture database) => database.ExecuteAsync("""
        CREATE USER service_manager WITHOUT LOGIN;
        ALTER ROLE etp_store_manager ADD MEMBER service_manager;
        CREATE USER service_viewer WITHOUT LOGIN;
        ALTER ROLE etp_viewer ADD MEMBER service_viewer;
        DECLARE @manager nvarchar(200),@viewer nvarchar(200);
        EXECUTE AS USER='service_manager'; SET @manager=SUSER_SNAME(); REVERT;
        EXECUTE AS USER='service_viewer'; SET @viewer=SUSER_SNAME(); REVERT;
        INSERT dbo.application_users(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
        VALUES(@manager,N'Synthetic manager','STORE_MANAGER',1,SUSER_SNAME(),N'Synthetic test role'),
              (@viewer,N'Synthetic viewer','VIEWER',1,SUSER_SNAME(),N'Synthetic test role');
        """);

    private sealed class InlineProgress(Action<FolderImportProgress> report) : IProgress<FolderImportProgress>
    {
        public void Report(FolderImportProgress value) => report(value);
    }

    // A copy of CrossPhaseStoreManagerImportTests.RestrictedConnections: every command of the repository runs as the
    // restricted test user, never as the fixture's owner.
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
