using System.Diagnostics;
using System.Text.Json;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Desktop;
using Etp.Reporting.Infrastructure.SqlServer;
using Etp.Reporting.TestSupport;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class DurableImportHistoryTests
{
    [Fact]
    public async Task Imported_and_duplicate_outcomes_survive_complete_application_process_restarts()
    {
        var database = new SqlDatabaseFixture();
        var scratch = Path.Combine(Path.GetTempPath(), "EtpHistoryRestart_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            await database.InitializeAsync();
            var input = Directory.CreateDirectory(Path.Combine(scratch, "inputs")).FullName;
            foreach (var report in new[] { "R025", "R020", "R024" })
            {
                var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), report + "_*.xlsx").Single();
                File.Copy(path, Path.Combine(input, Path.GetFileName(path)));
            }
            var imported = await Launch(input, "first");
            Assert.Equal(3, imported.Count);
            Assert.All(imported, row => Assert.Equal("Imported", row.Result.Status));
            var reopened = await Launch("read", "reopened");
            Assert.Equal(imported.Select(x => x.Key).Order(), reopened.Select(x => x.Key).Order());
            Assert.Equal(Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files")), reopened.Count);
            Assert.Equal(Convert.ToInt32(await database.ExecuteAsync("SELECT SUM(source_row_count) FROM dbo.import_batches")), reopened.Sum(x => x.Result.RowsProcessed));
            var factCount = await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines");
            var repeated = await Launch(input, "duplicate");
            Assert.Equal(6, repeated.Count);
            Assert.Equal(3, repeated.Count(row => row.Result.Status == "Duplicate"));
            Assert.Equal(factCount, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
            var final = await Launch("read", "final");
            Assert.Equal(repeated.Select(x => x.Key).Order(), final.Select(x => x.Key).Order());
            Assert.All(final.Where(x => x.Result.Status == "Duplicate"), row =>
            { Assert.Equal(0, row.Result.NewRows); Assert.Equal(row.Result.RowsProcessed, row.Result.AlreadyPresentRows); });
            foreach (var row in final.Where(x => x.Key.StartsWith("file:", StringComparison.Ordinal)))
            {
                Assert.Equal(Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.import_row_outcomes WHERE import_file_id={row.ImportFileId} AND outcome='NEW'")), row.Result.NewRows);
            }
        }
        finally
        {
            await database.DisposeAsync();
            if (!Path.GetFullPath(scratch).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(scratch).StartsWith("EtpHistoryRestart_")) throw new InvalidOperationException("Unsafe fixture directory.");
            Directory.Delete(scratch, recursive: true);
        }

        async Task<List<ImportHistoryEntry>> Launch(string source, string name)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
            Assert.NotNull(root);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var host = Path.Combine(root.FullName, "tests-dotnet", "Etp.Reporting.HistoryRestartHost", "bin", configuration, "net10.0-windows", "Etp.Reporting.HistoryRestartHost.dll");
            var output = Path.Combine(scratch, name + ".json");
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.Environment[DesktopDiagnostics.DirectoryVariable] = DiagnosticsIsolation.LogDirectory;
            foreach (var arg in new[] { host, database.ConnectionString, Path.Combine(scratch, "settings"), output, source }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(90)).Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            Assert.Contains("saved outcomes", json.RootElement.GetProperty("StatusText").GetString());
            Assert.DoesNotContain("Select an import", json.RootElement.GetProperty("DetailText").GetString());
            return JsonSerializer.Deserialize<List<ImportHistoryEntry>>(json.RootElement.GetProperty("Entries"))!;
        }
    }

    [Fact]
    public async Task History_filters_durable_safe_diagnostics_and_sql_write_permissions_are_enforced()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var history = new SqlServerImportHistoryQuery(database.ConnectionString);
            await history.RecordAttemptAsync(new("failed.xlsx", "R025", "WLMHW", new(2026, 8, 25), new(2026, 8, 25), "Failed",
                Diagnostics: [new(ImportIssueSeverity.Blocker, "REQUIRED_COLUMN_MISSING", "Customer Secret C:\\private\\source.xlsx SQL SELECT", 4, "ITEMNUMBER"),
                    new(ImportIssueSeverity.Warning, "UNEXPECTED_COLUMN", "Secret", 1, "Customer Secret C:\\private\\source.xlsx")]));
            var reopened = new SqlServerImportHistoryQuery(database.ConnectionString);
            var rows = await reopened.LoadAsync(new(new(2026, 8, 25), new(2026, 8, 25), "WLMHW"));
            var row = Assert.Single(rows);
            Assert.Equal("Failed", row.Result.Status);
            var issue = Assert.Single(row.Result.Diagnostics!, diagnostic => diagnostic.Code == "REQUIRED_COLUMN_MISSING");
            Assert.Equal("REQUIRED_COLUMN_MISSING", issue.Code); Assert.Equal(4, issue.SourceRow);
            Assert.Equal("ITEMNUMBER", issue.SourceColumn);
            Assert.Null(Assert.Single(row.Result.Diagnostics!, diagnostic => diagnostic.Code == "UNEXPECTED_COLUMN").SourceColumn);
            Assert.DoesNotContain("Secret", issue.Message); Assert.DoesNotContain("private", issue.Message);
            var savedDiagnostics = (string)(await database.ExecuteAsync("SELECT diagnostics_json FROM dbo.import_attempts"))!;
            Assert.DoesNotContain("Secret", savedDiagnostics); Assert.DoesNotContain("private", savedDiagnostics);
            // Older or externally recorded receipts also pass the display sanitizer.
            await database.ExecuteAsync("""
                UPDATE dbo.import_attempts SET diagnostics_json=N'[{"Severity":1,"Code":"UNEXPECTED_COLUMN","Message":"Secret","SourceRow":1,"SourceColumn":"Secret path"}]';
                """);
            var oldIssue = Assert.Single(Assert.Single(await reopened.LoadAsync(new(new(2026, 8, 25), new(2026, 8, 25)))).Result.Diagnostics!);
            Assert.Null(oldIssue.SourceColumn); Assert.DoesNotContain("Secret", oldIssue.Message);
            Assert.Empty(await reopened.LoadAsync(new(new(2026, 8, 24), new(2026, 8, 24))));
            Assert.Empty(await reopened.LoadAsync(new(new(2026, 8, 25), new(2026, 8, 25), "HEMW")));
            await database.ExecuteAsync("""
                CREATE USER history_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER history_manager;
                CREATE USER history_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER history_viewer;
                EXECUTE AS USER='history_manager';
                EXEC dbo.record_import_attempt @name=N'manager.xlsx',@report='R025',@store='HEMW',@start='20260825',@end='20260825',
                  @outcome='Failed',@rows=0,@new=0,@present=0,@conflicts=0,@diagnostics=N'[]'; REVERT;
                """);
            Assert.Equal(2, await database.ExecuteAsync("EXECUTE AS USER='history_viewer'; SELECT COUNT(*) FROM dbo.import_attempts; REVERT;"));
            var denied = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("""
                EXECUTE AS USER='history_viewer'; EXEC dbo.record_import_attempt @name=N'forbidden.xlsx',
                  @outcome='Failed',@rows=0,@new=0,@present=0,@conflicts=0,@diagnostics=N'[]'; REVERT;
                """));
            Assert.Equal(229, denied.Number);
            var direct = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("EXECUTE AS USER='history_manager'; DELETE dbo.import_attempts; REVERT;"));
            Assert.Equal(229, direct.Number);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Linked_cancelled_failed_and_subsequent_attempts_remain_visible_without_repeating_original_receipt()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var path = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "R025_*.xlsx").Single();
            var imported = Assert.Single((await new FolderImportService(new SqlServerImportPersistenceUseCase(database.ConnectionString))
                .RunAsync(path, new("Synthetic Owner"))).Files);
            Assert.Equal("Imported", imported.Status);
            var history = new SqlServerImportHistoryQuery(database.ConnectionString);
            var scope = new ImportHistoryScope(imported.PeriodStart!.Value, imported.PeriodEnd!.Value, imported.StoreCode);
            var original = Assert.Single(await history.LoadAsync(scope));
            await history.RecordAttemptAsync(imported with { Status = "Cancelled", NewRows = 0 });
            await history.RecordAttemptAsync(imported with { Status = "Failed", NewRows = 0,
                Diagnostics = [new(ImportIssueSeverity.Blocker, "IMPORT_FAILED", "Secret", 1)] });
            // Even historical clients that classified a later attempt as Imported must not erase it.
            await history.RecordAttemptAsync(imported);
            var reopened = await new SqlServerImportHistoryQuery(database.ConnectionString).LoadAsync(scope);
            Assert.Equal(4, reopened.Count);
            var retained = Assert.Single(reopened, entry => entry.Key == original.Key);
            Assert.Equal(original.ImportFileId, retained.ImportFileId);
            Assert.Equal(original.Result.NewRows, retained.Result.NewRows);
            Assert.Equal(original.Result.Status, retained.Result.Status);
            Assert.Single(reopened, entry => entry.Result.Status == "Cancelled");
            var failed = Assert.Single(reopened, entry => entry.Result.Status == "Failed");
            Assert.Equal("IMPORT_FAILED", Assert.Single(failed.Result.Diagnostics!).Code);
            Assert.Equal(2, reopened.Count(entry => entry.Result.Status == "Imported"));
            Assert.Equal(1, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_files"));
            Assert.Equal(imported.NewRows, await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.sales_lines"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void All_assigned_roles_can_reach_read_only_import_history(bool canView, bool canImport, bool owner)
    {
        var history = TaskNavigation.Find("import-history")!;
        Assert.True(new ShellNavigationService().Navigate(history.Route, new(true, canView, canImport, owner)).IsAllowed);
        Assert.Equal(history, TaskNavigation.InSection("Import", new(true, canView, canImport, owner)).First(task => task.Tab == "History"));
        Assert.False(new ShellNavigationService().Navigate(history.Route, new(false, false, false, false)).IsAllowed);
    }
}
