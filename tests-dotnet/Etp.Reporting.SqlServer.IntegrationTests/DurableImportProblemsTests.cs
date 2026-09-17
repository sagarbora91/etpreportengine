using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// R1, Problems half. The Problems tab derived its rows from the in-session
/// latestResults, so closing the application emptied it and the operator lost every
/// recorded conflict and duplicate. It now reads persisted outcomes for the header
/// scope, which is only meaningful if it survives a real process restart.
/// </summary>
public sealed class DurableImportProblemsTests
{
    [Fact]
    public async Task Problems_survive_a_complete_application_restart_and_match_the_persisted_outcomes()
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

            // First run imports cleanly, so nothing is a problem yet.
            var (firstProblems, _) = await Launch(input, "first");
            Assert.Empty(firstProblems);

            // Second run re-imports the same folder. Every file is now a duplicate,
            // which is a problem the operator has to be able to see afterwards.
            var (secondProblems, _) = await Launch(input, "second");
            Assert.Equal(3, secondProblems.Count);
            Assert.All(secondProblems, problem => Assert.Contains("Duplicate", problem.Status, StringComparison.OrdinalIgnoreCase));

            // The defining assertion: a process that imported nothing at all still
            // shows them, so the list came from the database and not from session state.
            var (afterRestart, message) = await Launch("read", "reopened");
            Assert.Equal(3, afterRestart.Count);
            Assert.Equal(
                secondProblems.Select(x => x.File).Order(),
                afterRestart.Select(x => x.File).Order());
            Assert.Contains("3 problems", message);

            // And they agree with what SQL holds, not merely with each other.
            var duplicateFiles = Convert.ToInt32(await database.ExecuteAsync(
                "SELECT COUNT(*) FROM dbo.import_attempts WHERE outcome='Duplicate'"));
            Assert.Equal(duplicateFiles, afterRestart.Count);

            // Safe diagnostics only: no path, no customer data.
            Assert.All(afterRestart, problem =>
            {
                Assert.DoesNotContain("\\", problem.File);
                Assert.DoesNotContain(":", problem.File);
                Assert.DoesNotContain(scratch, problem.Detail);
            });
        }
        finally
        {
            await database.DisposeAsync();
            if (!Path.GetFullPath(scratch).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(scratch).StartsWith("EtpHistoryRestart_")) throw new InvalidOperationException("Unsafe fixture directory.");
            Directory.Delete(scratch, recursive: true);
        }

        async Task<(List<Problem> Problems, string Message)> Launch(string source, string name)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
            Assert.NotNull(root);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var host = Path.Combine(root!.FullName, "tests-dotnet", "Etp.Reporting.HistoryRestartHost", "bin", configuration, "net10.0-windows", "Etp.Reporting.HistoryRestartHost.dll");
            var output = Path.Combine(scratch, name + ".json");
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { host, database.ConnectionString, Path.Combine(scratch, "settings"), output, source }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(output));
            var problems = json.RootElement.GetProperty("Problems").EnumerateArray()
                .Select(x => new Problem(
                    x.GetProperty("File").GetString() ?? "",
                    x.GetProperty("Status").GetString() ?? "",
                    x.GetProperty("Detail").GetString() ?? ""))
                .ToList();
            return (problems, json.RootElement.GetProperty("ProblemsMessage").GetString() ?? "");
        }
    }

    [Fact]
    public async Task Viewer_may_read_the_outcomes_problems_are_built_from_and_may_not_change_them()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await database.ExecuteAsync("""
                CREATE USER problems_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER problems_manager;
                CREATE USER problems_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER problems_viewer;
                """);

            // Reading is what the Problems tab does, and a Viewer must be able to do it.
            Assert.Equal(0, await database.ExecuteAsync(
                "EXECUTE AS USER='problems_viewer'; SELECT COUNT(*) FROM dbo.import_row_outcomes; REVERT;"));

            // Nobody edits recorded outcomes to make a problem disappear.
            foreach (var principal in new[] { "problems_viewer", "problems_manager" })
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                    $"EXECUTE AS USER='{principal}'; DELETE dbo.import_row_outcomes; REVERT;"));
                Assert.Equal(229, denied.Number);
            }
        }
        finally { await database.DisposeAsync(); }
    }

    private sealed record Problem(string File, string Status, string Detail);
}
