using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// P4-15 review. The recovery drill now runs as a SQL administrator, and after the restore it
/// records its result by calling procedures in the application database - procedures any
/// application Owner can redefine, because Owners are db_owner there. Called directly, code
/// planted in them would run with the drill's server rights: a route from Owner to sysadmin.
///
/// The drill script therefore records through Invoke-EtpSqlAsAutomationUser. These tests run
/// that exact shipped function, through Windows PowerShell and sqlcmd as installed operations
/// do, and show that the batch keeps none of the caller's server rights while the recording
/// itself still works.
/// </summary>
[Collection(ServerPrincipalTests.Name)]
public sealed class DrillRecordingScopeTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Principal = @"NT AUTHORITY\LOCAL SERVICE";

    [Fact]
    public async Task The_drill_records_its_result_with_the_automation_accounts_database_rights_only()
    {
        var created = await ScalarAsync<int>($"SELECT CASE WHEN SUSER_ID(N'{Principal}') IS NULL THEN 1 ELSE 0 END") == 1;
        try
        {
            await ProvisionAutomationAccountAsync();
            // Stand-in for code an Owner planted in a procedure the drill calls: it reports
            // what rights it runs with. Like record_verified_operation, the automation role
            // may execute it.
            await database.ExecuteAsync("""
                CREATE OR ALTER PROCEDURE dbo.etp_test_planted AS
                SELECT 'ETP_PLANTED:' + CONVERT(varchar(1),COALESCE(IS_SRVROLEMEMBER('sysadmin'),0))
                     + ':' + CONVERT(varchar(1),HAS_PERMS_BY_NAME(NULL,NULL,'CONTROL SERVER'));
                """);
            await database.ExecuteAsync("GRANT EXECUTE ON dbo.etp_test_planted TO etp_automation;");

            // Called the old way, by the drill's administrator session, it has everything.
            Assert.Contains("ETP_PLANTED:1:1", await RunCommonAsync("Invoke-EtpSql -SqlCmd $sqlcmd -Server $Server -Database $Database -Query 'EXEC dbo.etp_test_planted;'"));

            // Through the shipped function it has nothing at server level.
            Assert.Contains("ETP_PLANTED:0:0", await RunScopedAsync("EXEC dbo.etp_test_planted;"));

            // And it cannot switch back: NO REVERT.
            var reverted = await RunScopedAsync("BEGIN TRY REVERT; SELECT 'ETP_REVERTED'; END TRY BEGIN CATCH SELECT 'ETP_REVERT_REFUSED'; END CATCH; EXEC dbo.etp_test_planted;");
            Assert.Contains("ETP_REVERT_REFUSED", reverted);
            Assert.DoesNotContain("ETP_REVERTED", reverted);
            Assert.Contains("ETP_PLANTED:0:0", reverted);

            // The recording itself still works, under the account that records backups.
            var hash = new string('D', 64);
            await RunScopedAsync($"EXEC dbo.record_verified_operation 'RestoreDrill','{hash}';");
            await RunScopedAsync("EXEC dbo.record_operational_audit 'RestoreDrill','Succeeded',N'Isolated restore and backup metadata checks passed; the backup was not encrypted',N'operations';");
            Assert.Equal(Principal, (string?)await database.ExecuteAsync(
                $"SELECT TOP(1) recorded_by FROM dbo.verified_operation_receipts WHERE operation_type='RestoreDrill' AND backup_sha256='{hash}' ORDER BY verified_operation_receipt_id DESC"));
            Assert.Equal(Principal, (string?)await database.ExecuteAsync(
                "SELECT TOP(1) actor_name FROM dbo.operational_audit WHERE event_type='RestoreDrill' ORDER BY operational_audit_id DESC"));
        }
        finally
        {
            await database.ExecuteAsync("IF OBJECT_ID(N'dbo.etp_test_planted',N'P') IS NOT NULL DROP PROCEDURE dbo.etp_test_planted;");
            if (created) await ExecuteMasterAsync($"IF SUSER_ID(N'{Principal}') IS NOT NULL DROP LOGIN [{Principal}];");
        }
    }

    [Fact]
    public async Task Recording_is_refused_rather_than_run_unconfined()
    {
        var created = await ScalarAsync<int>($"SELECT CASE WHEN SUSER_ID(N'{Principal}') IS NULL THEN 1 ELSE 0 END") == 1;
        try
        {
            // No user for the automation account in this database: nothing to confine it to.
            await Assert.ThrowsAsync<InvalidOperationException>(() => RunScopedAsync("SELECT 'ETP_RAN';", @"NT AUTHORITY\NETWORK SERVICE"));

            // TRUSTWORTHY would let an impersonated user reach back out to the server.
            await ProvisionAutomationAccountAsync();
            Assert.Contains("ETP_RAN", await RunScopedAsync("SELECT 'ETP_RAN';"));
            await ExecuteMasterAsync($"ALTER DATABASE [{database.Name}] SET TRUSTWORTHY ON;");
            try { await Assert.ThrowsAsync<InvalidOperationException>(() => RunScopedAsync("SELECT 'ETP_RAN';")); }
            finally { await ExecuteMasterAsync($"ALTER DATABASE [{database.Name}] SET TRUSTWORTHY OFF;"); }
        }
        finally
        {
            if (created) await ExecuteMasterAsync($"IF SUSER_ID(N'{Principal}') IS NOT NULL DROP LOGIN [{Principal}];");
        }
    }

    // ---------------------------------------------------------------- the product path

    private async Task ProvisionAutomationAccountAsync()
    {
        // Settings > Users, then what the operations module install adds.
        await database.ExecuteAsync($"EXEC dbo.configure_application_role N'{Principal}','STORE_MANAGER',1;");
        await database.ExecuteAsync($"""
            MERGE dbo.application_users AS target
            USING(SELECT N'{Principal}' windows_identity) source ON target.windows_identity=source.windows_identity
            WHEN MATCHED THEN UPDATE SET role_code='STORE_MANAGER',is_active=1
            WHEN NOT MATCHED THEN INSERT(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
              VALUES(N'{Principal}',N'Automation (test)','STORE_MANAGER',1,SUSER_SNAME(),N'Drill recording scope test');
            DECLARE @u sysname=(SELECT name FROM sys.database_principals WHERE sid=SUSER_SID(N'{Principal}'));
            DECLARE @s nvarchar(max)=N'ALTER ROLE etp_automation ADD MEMBER '+QUOTENAME(@u)+N';';
            IF IS_ROLEMEMBER(N'etp_automation',@u)<>1 EXEC(@s);
            """);
    }

    private Task<string> RunScopedAsync(string query, string principal = Principal) =>
        RunCommonAsync($"Invoke-EtpSqlAsAutomationUser -SqlCmd $sqlcmd -Server $Server -Database $Database -AutomationPrincipal '{principal}' -Query \"{query}\"");

    // Dot-sources the shipped etp-operations-common.ps1 and runs one call against the fixture
    // database. A failing call surfaces as InvalidOperationException carrying its output.
    private async Task<string> RunCommonAsync(string call)
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpDrillScopeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "run.ps1");
            await File.WriteAllTextAsync(script, $$"""
                param([string]$HelperPath,[string]$Server,[string]$Database)
                $ErrorActionPreference='Stop'
                . $HelperPath
                $sqlcmd=Resolve-EtpSqlCmd
                $Server=Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $Server
                @({{call}}) | ForEach-Object { $_ }
                """);
            var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            var start = new ProcessStartInfo(powerShell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment.Remove("PSModulePath");
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                "-HelperPath", Path.Combine(AppContext.BaseDirectory, "scripts", "etp-operations-common.ps1"),
                "-Server", new SqlConnectionStringBuilder(database.ConnectionString).DataSource, "-Database", database.Name })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            var output = await stdout + await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException(output);
            return output;
        }
        finally
        {
            if (!Path.GetFileName(directory).StartsWith("EtpDrillScopeTest_", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe cleanup path.");
            Directory.Delete(directory, recursive: true);
        }
    }

    // ---------------------------------------------------------------- plumbing

    // Unpooled: a session a severe error kills must not be handed to the next caller.
    private string MasterConnectionString => new SqlConnectionStringBuilder(database.ConnectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString;

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task ExecuteMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
