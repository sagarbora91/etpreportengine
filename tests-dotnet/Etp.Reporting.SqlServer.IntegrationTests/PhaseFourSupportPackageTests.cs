using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFourSupportPackageTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Customer = "SYNTHETIC_PRIVATE_CUSTOMER_ZETA";
    private const string CustomerName = "Synthetic Sensitive Person Zeta";
    private const string Phone = "+919999000123";
    private const string Workbook = "PRIVATE_CUSTOMER_ZETA.xlsx";
    private const string SourcePath = @"C:\SyntheticPrivateSourceZeta\PRIVATE_CUSTOMER_ZETA.xlsx";
    private const string SqlText = "SELECT private_customer_secret_zeta FROM private_source_zeta";
    private static readonly string[] PrivateSentinels = [Customer, CustomerName, Phone, Workbook, SourcePath, SqlText];

    [Fact]
    public async Task Support_package_contains_only_aggregate_health_and_omits_private_database_content()
    {
        await SeedPrivateDataAsync();
        var output = NewOutputDirectory();
        try
        {
            var process = await RunSupportScriptAsync(output);
            Assert.True(process.ExitCode == 0, process.Output);
            Assert.Contains("Aggregate support package created.", process.Output, StringComparison.Ordinal);
            AssertPrivateContentAbsent(process.Output);
            var archivePath = Assert.Single(Directory.GetFiles(output, "*.zip"));
            Assert.Empty(Directory.GetDirectories(output));
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Equal(new[] { "database-health.txt", "privacy.txt", "scheduled-tasks.txt", "system.txt" },
                archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
            foreach (var entry in archive.Entries)
            {
                AssertPrivateContentAbsent(entry.FullName);
                using var reader = new StreamReader(entry.Open());
                var content = await reader.ReadToEndAsync();
                AssertPrivateContentAbsent(content);
                Assert.DoesNotContain(database.Name, content, StringComparison.OrdinalIgnoreCase);
                if (entry.Name == "database-health.txt")
                {
                    Assert.Contains("FailedImportsLastDay", content, StringComparison.Ordinal);
                    Assert.Contains(new string('A', 64), content, StringComparison.Ordinal);
                    Assert.Contains(new string('B', 64), content, StringComparison.Ordinal);
                }
            }
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.sharing_contacts WHERE display_name=N'{CustomerName}'")));
            Assert.Equal(SourcePath, await database.ExecuteAsync("SELECT TOP(1) managed_file_path FROM dbo.source_documents ORDER BY source_document_id DESC"));
        }
        finally { DeleteOutputDirectory(output); }
    }

    [Fact]
    public async Task Native_sql_failure_omits_submitted_values_and_creates_no_package()
    {
        var original = (string)(await database.ExecuteAsync("SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.load_database_operational_health'));"))!;
        var output = NewOutputDirectory();
        try
        {
            // Emulate a SQL permission or runtime failure containing source data.
            // Only this test's randomly named database is altered, then restored.
            await database.ExecuteAsync($"""
                ALTER PROCEDURE dbo.load_database_operational_health AS
                BEGIN
                  SET NOCOUNT ON;
                  SELECT N'{Customer}' AS detail;
                  THROW 51991,N'{CustomerName}; {Phone}; {SourcePath}; {SqlText}',1;
                END
                """);
            var process = await RunSupportScriptAsync(output);
            Assert.NotEqual(0, process.ExitCode);
            Assert.Equal("Support health query failed. Check database access and try again.", process.Output.Trim());
            AssertPrivateContentAbsent(process.Output);
            Assert.Empty(Directory.GetFileSystemEntries(output));
        }
        finally
        {
            try
            {
                var restore = System.Text.RegularExpressions.Regex.Replace(original, @"\ACREATE(?:\s+OR\s+ALTER)?\s+PROCEDURE", "ALTER PROCEDURE", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                await database.ExecuteAsync(restore);
            }
            finally { DeleteOutputDirectory(output); }
        }
    }

    private async Task SeedPrivateDataAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        const string sql = """
            INSERT dbo.sharing_contacts(display_name,contact_role,phone_e164,modified_by,change_reason)
            VALUES(@name,@customer,@phone,SUSER_SNAME(),N'Disposable privacy test');
            DECLARE @batch uniqueidentifier=NEWID();
            INSERT dbo.import_batches(import_batch_id,status,started_utc,failure_reason)
            VALUES(@batch,'Failed',SYSUTCDATETIME(),@sql);
            INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes)
            VALUES(@batch,@workbook,REPLICATE('d',64),1);
            INSERT dbo.source_documents(original_file_name,managed_file_path,source_sha256,size_bytes,source_type,lifecycle_status,received_by,last_status_by,safe_message)
            VALUES(@workbook,@path,REPLICATE('e',64),1,'ETP_WORKBOOK','RECEIVED',@name,@customer,@sql);
            EXEC dbo.record_verified_operation 'Backup','AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA';
            EXEC dbo.record_verified_operation 'RestoreDrill','BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB';
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", CustomerName);
        command.Parameters.AddWithValue("@customer", Customer);
        command.Parameters.AddWithValue("@phone", Phone);
        command.Parameters.AddWithValue("@sql", SqlText);
        command.Parameters.AddWithValue("@workbook", Workbook);
        command.Parameters.AddWithValue("@path", SourcePath);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(int ExitCode, string Output)> RunSupportScriptAsync(string output)
    {
        Assert.StartsWith("EtpPhase0Test_", database.Name, StringComparison.Ordinal);
        var server = new SqlConnectionStringBuilder(database.ConnectionString).DataSource;
        var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(powerShell)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        // A PowerShell 7 test host can pass an incompatible module path to 5.1.
        start.Environment.Remove("PSModulePath");
        // Only unsigned development scripts use Bypass in this disposable test;
        // the desktop production launcher continues to require AllSigned.
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(AppContext.BaseDirectory, "scripts", "new-etp-support-package.ps1"), "-ServerInstance", server, "-Database", database.Name, "-OutputDirectory", output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }

    private static void AssertPrivateContentAbsent(string content)
    {
        foreach (var sentinel in PrivateSentinels) Assert.DoesNotContain(sentinel, content, StringComparison.OrdinalIgnoreCase);
    }

    private static string NewOutputDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpSupportPrivacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteOutputDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"^EtpSupportPrivacy-[a-f0-9]{32}$"))
            throw new InvalidOperationException("Refusing to remove an unexpected test directory.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Refusing to remove a linked test directory.");
        Directory.Delete(full, recursive: true);
    }
}
