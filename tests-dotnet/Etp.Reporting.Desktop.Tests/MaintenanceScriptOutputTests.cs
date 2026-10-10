using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.9 (IE-CODE-01, IE-CODE-02, IE-CODE-10, IE-CODE-19). Backup now, Recovery drill now and
/// Create support package threw away what the script printed and said "Check its prerequisites
/// and protected operations log" - a log that did not exist; the scheduled backup and drill
/// left nothing but "last result 0x1"; the support package had no diagnostics log and the
/// application never said where it was saved. The script tests run the shipped scripts with no
/// SQL Server: every target below is refused before any connection is attempted.
/// </summary>
public sealed class MaintenanceScriptOutputTests
{
    private const string Backup = "backup-etp-database.ps1";
    private const string Drill = "invoke-etp-recovery-drill.ps1";
    private const string Support = "new-etp-support-package.ps1";

    // ------------------------------------------------------------ reading the output

    [Fact]
    public void A_failure_shows_the_scripts_reason_and_where_it_was_logged()
    {
        var outcome = MaintenanceScriptOutput.Interpret(Backup, 1, "",
            "ETP_FAILURE:Backup storage is below the required free-space limit. Free space on this drive.\r\n" +
            @"ETP_LOG:C:\ProgramData\EtpReporting\Backups\Logs\backup-20261010.log" + "\r\n" +
            "Backup storage is below the required free-space limit. Free space on this \r\ndrive.\r\nAt C:\\x\\backup-etp-database.ps1:152 char:5\r\n+     throw $failure\r\n");

        Assert.False(outcome.Result.Succeeded);
        Assert.Equal("LOW_DISK_SPACE", outcome.ReasonCode);
        Assert.Equal(
            @"Backup storage is below the required free-space limit. Free space on this drive. The failure was recorded in C:\ProgramData\EtpReporting\Backups\Logs\backup-20261010.log.",
            outcome.Result.Message);
        Assert.DoesNotContain("protected operations log", outcome.Result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_script_that_died_before_its_own_catch_is_read_from_the_wrapped_error_record()
    {
        // Captured from Windows PowerShell 5.1 with redirected output: the message is broken
        // at 120 characters with nothing added at the break.
        const string stderr =
            "This SQL Server edition encrypts backups, and no exported recovery keys were found. In the application, open Settings \r\n" +
            "> Database > Encrypted backup recovery keys and select \"Create and export recovery keys\", then run this again.\r\n" +
            "At C:\\Temp\\t1.ps1:3 char:7\r\n" +
            "+ try { throw 'This SQL Server edition encrypts backups, and no exporte ...\r\n" +
            "+       ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\r\n" +
            "    + CategoryInfo          : OperationStopped: (This SQL Server...run this again.:String) [], RuntimeException\r\n" +
            "    + FullyQualifiedErrorId : This SQL Server edition encrypts backups, and no exported recovery keys were found. In t \r\n";

        var outcome = MaintenanceScriptOutput.Interpret(Backup, 1, "", stderr);

        Assert.Equal("RECOVERY_KEYS_MISSING", outcome.ReasonCode);
        // No ETP_LOG line: no log is mentioned, because none is known to exist.
        Assert.Equal(
            "This SQL Server edition encrypts backups, and no exported recovery keys were found. In the application, open Settings > Database > Encrypted backup recovery keys and select \"Create and export recovery keys\", then run this again.",
            outcome.Result.Message);
    }

    [Fact]
    public void The_support_packages_own_refusal_line_is_shown_whole()
    {
        var outcome = MaintenanceScriptOutput.Interpret(Support, 1, "", "Support health query failed. Check database access and try again.\r\n");
        Assert.Equal("SUPPORT_HEALTH_QUERY_FAILED", outcome.ReasonCode);
        Assert.Equal("Support health query failed. Check database access and try again.", outcome.Result.Message);
    }

    [Fact]
    public void A_failure_with_no_output_says_so_with_its_exit_code()
    {
        var outcome = MaintenanceScriptOutput.Interpret(Support, 5, null, "   \r\n");
        Assert.False(outcome.Result.Succeeded);
        Assert.Equal(MaintenanceScriptOutput.NoReason, outcome.ReasonCode);
        Assert.Equal("The script stopped (exit code 5) without giving a reason.", outcome.Result.Message);
    }

    [Fact]
    public void A_support_package_success_says_where_the_zip_was_saved()
    {
        var outcome = MaintenanceScriptOutput.Interpret(Support, 0,
            "Aggregate support package created.\r\nETP_SAVED:C:\\Users\\Owner\\Documents\\EtpReportingSupport\\EtpReporting-Support-1.zip\r\n", "");
        Assert.True(outcome.Result.Succeeded);
        Assert.Equal(@"Saved to C:\Users\Owner\Documents\EtpReportingSupport\EtpReporting-Support-1.zip.", outcome.Result.Message);
    }

    [Fact]
    public void A_backup_success_keeps_its_result_line_and_notices_but_not_wrapped_warnings()
    {
        var outcome = MaintenanceScriptOutput.Interpret(Backup, 0,
            "WARNING: This SQL Server edition cannot encrypt backups. The backup file is unencrypted; protect the backup folder at \r\nrest.\r\n" +
            "ETP_NOTICE:Row counts were not recorded in this backup's receipt (CHANGED_DURING_BACKUP); the recovery drill of this backup will say so.\r\n" +
            "WARNING: Row counts were not recorded in this backup's receipt\r\n" +
            "Backup and verification completed. The file is NOT encrypted on this SQL Server edition; protect the backup folder at rest.\r\n", "");
        Assert.True(outcome.Result.Succeeded);
        Assert.Equal(
            "Backup and verification completed. The file is NOT encrypted on this SQL Server edition; protect the backup folder at rest. Row counts were not recorded in this backup's receipt (CHANGED_DURING_BACKUP); the recovery drill of this backup will say so.",
            outcome.Result.Message);
    }

    [Fact]
    public void A_success_with_no_output_keeps_the_generic_sentence()
        => Assert.Equal("The maintenance operation completed successfully.", MaintenanceScriptOutput.Interpret(Drill, 0, "", "").Result.Message);

    [Theory]
    [InlineData("File C:\\x\\backup-etp-database.ps1 cannot be loaded because running scripts is disabled on this system.", "SCRIPT_BLOCKED")]
    [InlineData("The isolated restore passed, but its result could not be recorded. The database operation failed. Check SQL permissions and operation prerequisites.", "DRILL_RESULT_NOT_RECORDED")]
    [InlineData("The automation account PC\\EtpAutomation is not an active Store Manager of EtpReporting, so ...", "AUTOMATION_NOT_STORE_MANAGER")]
    [InlineData("The automation account PC\\EtpAutomation is an active Store Manager but does not yet have the operations module's rights (missing: x).", "AUTOMATION_RIGHTS_MISSING")]
    [InlineData("Configure a dedicated local automation account.", "AUTOMATION_ACCOUNT_INVALID")]
    [InlineData("This SQL Server edition encrypts backups, and no exported recovery keys were found.", "RECOVERY_KEYS_MISSING")]
    [InlineData("Export the current certificate to immutable recovery custody before backup.", "CERTIFICATE_CUSTODY")]
    [InlineData("The SQL Server edition could not be determined; refusing to take a backup whose protection is unknown.", "SQL_EDITION_UNKNOWN")]
    [InlineData("Backup storage is below the required free-space limit.", "LOW_DISK_SPACE")]
    [InlineData("Complete protected backup-folder setup first.", "BACKUP_FOLDER_MISSING")]
    [InlineData("Linked operation paths are not allowed.", "LINKED_PATH")]
    [InlineData("Install Microsoft Sqlcmd in a protected Program Files folder.", "SQLCMD_MISSING")]
    [InlineData("Could not reach the SQL Server instance with the installed command-line client.", "SQL_UNREACHABLE")]
    [InlineData("Choose a SQL Server instance on this computer.", "SQL_TARGET_NOT_LOCAL")]
    [InlineData("Backup metadata does not match the verification receipt.", "DRILL_METADATA_MISMATCH")]
    [InlineData("The backup changed during the recovery drill.", "DRILL_BACKUP_CHANGED")]
    [InlineData("Recovery drill failed: the restored copy does not match its backup receipt. sales_lines: receipt 5401, restored copy 5400.", "DRILL_ROW_COUNTS")]
    [InlineData("Backup verification failed: the receipt and file differ.", "RECEIPT_INVALID")]
    [InlineData("The backup receipt was not found.", "RECEIPT_INVALID")]
    [InlineData("Support health query failed. Check database access and try again.", "SUPPORT_HEALTH_QUERY_FAILED")]
    [InlineData("The database operation failed. Check SQL permissions and operation prerequisites.", "SQL_OPERATION_FAILED")]
    [InlineData("Something nobody planned for.", "SCRIPT_FAILED")]
    [InlineData("", "NO_REASON")]
    public void Known_script_reasons_map_to_a_code(string reason, string code)
        => Assert.Equal(code, MaintenanceScriptOutput.ReasonCode(reason));

    [Theory]
    [InlineData(Backup, "LOW_DISK_SPACE", "BACKUP_RUN_FAILED:LOW_DISK_SPACE")]
    [InlineData(Drill, "DRILL_ROW_COUNTS", "RECOVERY_DRILL_RUN_FAILED:DRILL_ROW_COUNTS")]
    [InlineData(Support, "NO_REASON", "SUPPORT_PACKAGE_RUN_FAILED:NO_REASON")]
    public void The_diagnostics_event_names_the_operation_and_the_reason(string script, string reason, string eventId)
        => Assert.Equal(eventId, MaintenanceScriptOutput.FailureEventId(script, reason));

    [Fact]
    public void The_diagnostics_entry_holds_the_reason_code_and_exit_code_and_no_script_text()
    {
        var directory = NewTemporaryDirectory();
        try
        {
            var exception = new MaintenanceScriptFailedException(Backup, 1, "AUTOMATION_NOT_STORE_MANAGER");
            DesktopDiagnostics.Record(exception, "OperationsAdministration.Maintenance",
                MaintenanceScriptOutput.FailureEventId(Backup, exception.ReasonCode), logDirectory: directory);
            var line = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "diagnostics-*.jsonl")));
            Assert.Contains("\"EventId\":\"BACKUP_RUN_FAILED:AUTOMATION_NOT_STORE_MANAGER\"", line, StringComparison.Ordinal);
            Assert.Contains("\"ExceptionType\":\"Etp.Reporting.Desktop.MaintenanceScriptFailedException\"", line, StringComparison.Ordinal);
            Assert.Contains("\"HResult\":1", line, StringComparison.Ordinal);
            Assert.Equal("Maintenance script backup-etp-database.ps1 failed with exit code 1 (AUTOMATION_NOT_STORE_MANAGER).", exception.Message);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // ------------------------------------------------------------ the shipped scripts

    [Fact]
    public async Task A_refused_backup_logs_a_dated_line_beside_the_backups_and_tells_the_application()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var backups = Directory.CreateDirectory(Path.Combine(root, "Backups")).FullName;
            var run = await RunScriptAsync(Backup, "-ServerInstance", @"RemoteServerForThisTest\SQLEXPRESS", "-Database", "DisposableDatabase", "-BackupDirectory", backups);

            Assert.NotEqual(0, run.ExitCode);
            var log = Assert.Single(Directory.GetFiles(Path.Combine(backups, "Logs")));
            Assert.Matches(@"\\backup-\d{8}\.log$", log);
            var text = File.ReadAllText(log);
            Assert.Contains("FAILED backup as ", text, StringComparison.Ordinal);
            Assert.Contains("(purpose Scheduled, database DisposableDatabase): Choose a SQL Server instance on this computer.", text, StringComparison.Ordinal);
            // Where it was thrown: here the target check in the shared operations script.
            Assert.Matches(@" at etp-operations-common\.ps1:\d+", text);
            Assert.Contains("ETP_FAILURE:Choose a SQL Server instance on this computer.", run.Error, StringComparison.Ordinal);
            Assert.Contains("ETP_LOG:" + log, run.Error, StringComparison.Ordinal);

            var outcome = MaintenanceScriptOutput.Interpret(Backup, run.ExitCode, run.Output, run.Error);
            Assert.Equal("SQL_TARGET_NOT_LOCAL", outcome.ReasonCode);
            Assert.Equal($"Choose a SQL Server instance on this computer. The failure was recorded in {log}.", outcome.Result.Message);

            // A second failure the same day goes into the same file.
            await RunScriptAsync(Backup, "-ServerInstance", @"RemoteServerForThisTest\SQLEXPRESS", "-Database", "DisposableDatabase", "-BackupDirectory", backups);
            Assert.Equal(2, File.ReadAllLines(log).Count(line => line.Contains("FAILED backup", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_backup_whose_folder_does_not_exist_creates_no_folder_and_still_gives_its_reason()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var backups = Path.Combine(root, "NotCreated");
            var run = await RunScriptAsync(Backup, "-ServerInstance", @"RemoteServerForThisTest\SQLEXPRESS", "-Database", "DisposableDatabase", "-BackupDirectory", backups);
            Assert.NotEqual(0, run.ExitCode);
            Assert.False(Directory.Exists(backups));
            Assert.Contains("ETP_FAILURE:Choose a SQL Server instance on this computer.", run.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("ETP_LOG:", run.Error, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_failed_recovery_drill_logs_a_dated_line_and_tells_the_application()
    {
        // The drill reads the protected operations configuration first; whether that or the
        // refused target stops it depends on the machine, so only the logging is asserted.
        var root = NewTemporaryDirectory();
        try
        {
            var backups = Directory.CreateDirectory(Path.Combine(root, "Backups")).FullName;
            var run = await RunScriptAsync(Drill, "-ServerInstance", @"RemoteServerForThisTest\SQLEXPRESS", "-Database", "DisposableDatabase", "-BackupDirectory", backups);
            Assert.NotEqual(0, run.ExitCode);
            var log = Assert.Single(Directory.GetFiles(Path.Combine(backups, "Logs")));
            Assert.Matches(@"\\recovery-drill-\d{8}\.log$", log);
            Assert.Contains("FAILED recovery-drill as ", File.ReadAllText(log), StringComparison.Ordinal);
            Assert.Contains("ETP_FAILURE:", run.Error, StringComparison.Ordinal);
            var outcome = MaintenanceScriptOutput.Interpret(Drill, run.ExitCode, run.Output, run.Error);
            Assert.NotEqual(MaintenanceScriptOutput.NoReason, outcome.ReasonCode);
            Assert.EndsWith($"The failure was recorded in {log}.", outcome.Result.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task The_failure_log_keeps_what_sql_server_reported_and_prunes_only_its_own_old_files()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var backups = Directory.CreateDirectory(Path.Combine(root, "Backups")).FullName;
            var logs = Directory.CreateDirectory(Path.Combine(backups, "Logs")).FullName;
            var old = Path.Combine(logs, "backup-20250101.log");
            File.WriteAllText(old, "old");
            File.SetLastWriteTime(old, new DateTime(2025, 1, 1));
            var oldDrill = Path.Combine(logs, "recovery-drill-20250101.log");
            File.WriteAllText(oldDrill, "old");
            File.SetLastWriteTime(oldDrill, new DateTime(2025, 1, 1));
            var unrelated = Path.Combine(logs, "notes.txt");
            File.WriteAllText(unrelated, "kept");
            File.SetLastWriteTime(unrelated, new DateTime(2025, 1, 1));
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{ScriptPath("etp-operations-common.ps1")}}'
                $failure = [Management.Automation.RuntimeException]::new($EtpMaskedSqlFailure)
                $failure.Data['EtpSqlDetail'] = 'Msg 15247, Level 16: User does not have permission.'
                try { throw $failure } catch { $record = $_ }
                $path = Write-EtpOperationFailureLog -Operation backup -BackupDirectory '{{Quote(backups)}}' -ErrorRecord $record -Explained "Shown`r`nto the owner" -Context 'purpose Scheduled' -Now ([datetime]'2026-10-10T22:00:05')
                if ($path -cne '{{Quote(Path.Combine(logs, "backup-20261010.log"))}}') { throw "Path: $path" }
                $missing = Write-EtpOperationFailureLog -Operation backup -BackupDirectory '{{Quote(Path.Combine(root, "Missing"))}}' -ErrorRecord $record
                if ($null -ne $missing -or (Test-Path -LiteralPath '{{Quote(Path.Combine(root, "Missing"))}}')) { throw 'A log was written into a backup folder that does not exist.' }
                $none = Write-EtpOperationFailureLog -Operation recovery-drill -BackupDirectory '{{Quote(backups)}}' -ErrorRecord $null
                if ($null -eq $none) { throw 'A failure with no error record was not logged.' }
                Write-Output 'Failure log passed.'
                """;
            var run = await RunCommandAsync(command);
            Assert.True(run.ExitCode == 0, run.Output + run.Error);
            Assert.Contains("Failure log passed.", run.Output, StringComparison.Ordinal);
            var line = Assert.Single(File.ReadAllLines(Path.Combine(logs, "backup-20261010.log")));
            Assert.StartsWith("2026-10-10T22:00:05", line, StringComparison.Ordinal);
            Assert.Contains("(purpose Scheduled): The database operation failed. Check SQL permissions and operation prerequisites. (SQL Server reported: Msg 15247, Level 16: User does not have permission.)", line, StringComparison.Ordinal);
            Assert.Contains("| Shown as: Shown to the owner", line, StringComparison.Ordinal);
            Assert.Contains("System.Management.Automation.RuntimeException", line, StringComparison.Ordinal);
            Assert.False(File.Exists(old), "A backup failure log older than the retention was kept.");
            Assert.False(File.Exists(oldDrill), "A drill failure log older than the retention was kept.");
            Assert.True(File.Exists(unrelated), "A file the log did not write was deleted.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task The_support_package_copies_the_last_30_days_of_the_diagnostics_log_only()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "Logs")).FullName;
            void Write(string name, DateTime written) { var path = Path.Combine(source, name); File.WriteAllText(path, "{}"); File.SetLastWriteTime(path, written); }
            Write("diagnostics-202610.jsonl", new DateTime(2026, 10, 10));
            Write("diagnostics-202609-20260930T101010000-abc.jsonl", new DateTime(2026, 9, 30));
            Write("diagnostics-202608.jsonl", new DateTime(2026, 8, 31));
            Write("other.jsonl", new DateTime(2026, 10, 10));
            Write("diagnostics-202610.txt", new DateTime(2026, 10, 10));
            var destination = Path.Combine(root, "Staging", "diagnostics");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{ScriptPath(Support)}}'
                $count = Copy-EtpSupportDiagnostics -SourceDirectory '{{Quote(source)}}' -Destination '{{Quote(destination)}}' -Days 30 -Now ([datetime]'2026-10-10T12:00:00')
                if ($count -ne 2) { throw "Copied $count" }
                $none = Copy-EtpSupportDiagnostics -SourceDirectory '{{Quote(Path.Combine(root, "NoLogs"))}}' -Destination '{{Quote(Path.Combine(root, "Unused"))}}'
                if ($none -ne 0 -or (Test-Path -LiteralPath '{{Quote(Path.Combine(root, "Unused"))}}')) { throw 'A missing log folder produced files.' }
                $r = Resolve-EtpSupportDiagnosticsDirectory -Configured '' -Environment '' -LocalApplicationData 'C:\Users\Owner\AppData\Local'
                if ($r -cne 'C:\Users\Owner\AppData\Local\EtpReporting\Logs') { throw "Default: $r" }
                $r = Resolve-EtpSupportDiagnosticsDirectory -Configured '' -Environment 'relative\Logs' -LocalApplicationData 'C:\L'
                if ($r -cne 'C:\L\EtpReporting\Logs') { throw "Relative variable was used: $r" }
                $r = Resolve-EtpSupportDiagnosticsDirectory -Configured '' -Environment 'D:\TestLogs' -LocalApplicationData 'C:\L'
                if ($r -cne 'D:\TestLogs') { throw "Full variable was ignored: $r" }
                $r = Resolve-EtpSupportDiagnosticsDirectory -Configured 'E:\Chosen' -Environment 'D:\TestLogs' -LocalApplicationData 'C:\L'
                if ($r -cne 'E:\Chosen') { throw "Parameter was ignored: $r" }
                Write-Output 'Support diagnostics passed.'
                """;
            var run = await RunCommandAsync(command);
            Assert.True(run.ExitCode == 0, run.Output + run.Error);
            Assert.Contains("Support diagnostics passed.", run.Output, StringComparison.Ordinal);
            Assert.Equal(
                new[] { "diagnostics-202609-20260930T101010000-abc.jsonl", "diagnostics-202610.jsonl" },
                Directory.GetFiles(destination).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task The_monthly_runner_fails_with_exit_1_when_the_drill_throws_and_passes_when_it_does_not()
    {
        var day = DateTime.Now.Day;
        if (day > 28) return; // The runner accepts days 1-28 only; nothing to run on the 29th-31st.
        var root = NewTemporaryDirectory();
        try
        {
            File.Copy(ScriptPathRaw("invoke-monthly-recovery-drill-runner.ps1"), Path.Combine(root, "invoke-monthly-recovery-drill-runner.ps1"));
            var fake = Path.Combine(root, "invoke-etp-recovery-drill.ps1");

            File.WriteAllText(fake, "$ErrorActionPreference = 'Stop'\r\nthrow 'Backup metadata does not match the verification receipt.'\r\n");
            var failed = await RunFileAsync(Path.Combine(root, "invoke-monthly-recovery-drill-runner.ps1"), "-DayOfMonth", day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(1, failed.ExitCode);
            Assert.Contains("Monthly recovery drill failed: Backup metadata does not match the verification receipt.", failed.Error, StringComparison.Ordinal);

            // A drill that passes after a native command left a non-zero exit code: the old
            // $LASTEXITCODE check read that and failed a drill that had passed.
            File.WriteAllText(fake, "cmd.exe /c exit 3\r\nWrite-Output 'Receipt-verified recovery drill completed.'\r\n");
            var passed = await RunFileAsync(Path.Combine(root, "invoke-monthly-recovery-drill-runner.ps1"), "-DayOfMonth", day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(passed.ExitCode == 0, passed.Output + passed.Error);
            Assert.Contains("Receipt-verified recovery drill completed.", passed.Output, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ------------------------------------------------------------ plumbing

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    private static string ScriptPathRaw(string name) => Path.Combine(RepositoryRoot(), "scripts", name);
    private static string ScriptPath(string name) => Quote(ScriptPathRaw(name));
    private static string Quote(string value) => value.Replace("'", "''");

    private static string NewTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "EtpMaintenanceOutput-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static Task<(int ExitCode, string Output, string Error)> RunScriptAsync(string script, params string[] arguments)
        => RunFileAsync(ScriptPathRaw(script), arguments);

    private static Task<(int ExitCode, string Output, string Error)> RunFileAsync(string path, params string[] arguments)
        => RunPowerShellAsync(["-File", path, .. arguments]);

    private static Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string command)
        => RunPowerShellAsync(["-Command", command]);

    private static async Task<(int ExitCode, string Output, string Error)> RunPowerShellAsync(string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }
}
