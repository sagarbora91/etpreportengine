using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFourRecoveryTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Recovery_uses_backup_metadata_despite_later_live_changes_and_rejects_path_escape()
    {
        var root = Path.Combine(Path.GetTempPath(), "EtpRecoveryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var server = new SqlConnectionStringBuilder(database.ConnectionString).DataSource;
        if (!server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
        {
            var service = server.Contains('\\') ? "NT SERVICE\\MSSQL$" + server.Split('\\')[1] : "NT SERVICE\\MSSQLSERVER";
            var acl = new DirectoryInfo(root).GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(service, FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(acl);
        }
        var path = Path.Combine(root, database.Name + "-synthetic.bak");
        try
        {
            await database.ExecuteAsync($"BACKUP DATABASE [{database.Name}] TO DISK=N'{Escape(path)}' WITH COPY_ONLY,CHECKSUM;");
            var template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "sql", "etp-operations-broker.sql"));
            var sql = template.Replace("__PROCEDURE__", "test_recovery_broker").Replace("__DATABASE_LITERAL__", database.Name)
                .Replace("__DATABASE_IDENTIFIER__", database.Name).Replace("__BACKUP_DIRECTORY__", Escape(root)).Replace("__RESTORE_DIRECTORY__", Escape(root));
            await database.ExecuteAsync(sql);
            var file = Path.GetFileName(path);
            var before = (string)(await database.ExecuteAsync($"EXEC dbo.test_recovery_broker 'METADATA',N'{file}'"))!;
            Assert.StartsWith("ETP_METADATA:", before);
            using var metadata = JsonDocument.Parse(before[13..]);
            Assert.True(metadata.RootElement.GetArrayLength() >= 2);
            // Exercise the same Windows PowerShell/sqlcmd boundary as installed
            // operations, using only the broker inside this disposable database.
            var cliMetadata = await ReadMetadataThroughPowerShellAsync(root, server, file);
            using var fromCli = JsonDocument.Parse(cliMetadata);
            Assert.Equal(metadata.RootElement.GetArrayLength(), fromCli.RootElement.GetArrayLength());
            for (var index = 0; index < metadata.RootElement.GetArrayLength(); index++)
                Assert.Equal(metadata.RootElement[index].GetRawText(), fromCli.RootElement[index].GetRawText());
            await database.ExecuteAsync("INSERT dbo.import_batches(import_batch_id,status,started_utc) VALUES(NEWID(),'Processing',SYSUTCDATETIME());");
            var restored = (string)(await database.ExecuteAsync($"EXEC dbo.test_recovery_broker 'DRILL',N'{file}'"))!;
            Assert.Equal(before, restored);
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.import_batches")));
            foreach (var badFile in new[] { "..\\other.bak", database.Name + "-..bak", "OtherDatabase-file.bak", database.Name + "-x';DROP DATABASE master;--.bak" })
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"EXEC dbo.test_recovery_broker 'DRILL',N'{Escape(badFile)}'"));
                Assert.Equal(51330, error.Number);
            }
        }
        finally
        {
            // Only this generated test root is removed; no production backup paths are used.
            if (!Path.GetFileName(root).StartsWith("EtpRecoveryTest_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe cleanup path.");
            // SQL attention/cancellation can escape TRY/CATCH. Remove only test
            // recovery databases whose physical files resolve inside this test root.
            await using var cleanup = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
            await cleanup.OpenAsync();
            await using var query = new SqlCommand("SELECT DISTINCT d.name FROM sys.databases d JOIN sys.master_files f ON f.database_id=d.database_id WHERE d.name LIKE 'EtpRecovery[_]%' AND f.physical_name LIKE @root", cleanup);
            query.Parameters.AddWithValue("@root", root + Path.DirectorySeparatorChar + "%");
            var leftovers = new List<string>();
            await using (var reader = await query.ExecuteReaderAsync()) while (await reader.ReadAsync()) leftovers.Add(reader.GetString(0));
            foreach (var leftover in leftovers)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(leftover, "^EtpRecovery_[A-Fa-f0-9]{32}$")) throw new InvalidOperationException("Unsafe test recovery name.");
                await using var drop = new SqlCommand($"ALTER DATABASE [{leftover}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{leftover}];", cleanup);
                await drop.ExecuteNonQueryAsync();
            }
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private async Task<string> ReadMetadataThroughPowerShellAsync(string directory, string server, string backupFile)
    {
        var script = Path.Combine(directory, "read-disposable-backup-metadata.ps1");
        var result = Path.Combine(directory, "backup-metadata.json");
        await File.WriteAllTextAsync(script, """
            param([string]$HelperPath,[string]$Server,[string]$Database,[string]$BackupFile,[string]$ResultPath)
            $ErrorActionPreference='Stop'
            . $HelperPath
            $sqlcmd=Resolve-EtpSqlCmd
            $output=@(Invoke-EtpSql -SqlCmd $sqlcmd -Server $Server -Database $Database -Query "EXEC dbo.test_recovery_broker 'METADATA',N'$BackupFile';")
            $metadata=@($output | Where-Object { $_.StartsWith('ETP_METADATA:') })
            if ($metadata.Count -ne 1) { throw 'A single backup metadata response is required.' }
            [IO.File]::WriteAllText($ResultPath,$metadata[0].Substring(13))
            """);
        var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(powerShell)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-HelperPath", Path.Combine(AppContext.BaseDirectory, "scripts", "etp-operations-common.ps1"), "-Server", server,
            "-Database", database.Name, "-BackupFile", backupFile, "-ResultPath", result })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
        return await File.ReadAllTextAsync(result);
    }
}
