using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.11.0 self-diagnostics (lane L9) in the shipped scripts. Setup creates
/// %ProgramData%\EtpReporting\HealthExports with read/write for the automation account and the active Owners only
/// (design 8, decision 33 Q4); the support package carries the newest 14 health exports; the monthly recovery
/// drill runs DBCC CHECKDB WITH PHYSICAL_ONLY after a passed drill (decision 36 Q6) and keeps its behaviour and its
/// day-of-month rule. These run the shipped PowerShell functions with no SQL Server and change no ACL.
/// </summary>
public sealed class HealthDiagnosticsScriptTests
{
    private const string Bootstrap = "bootstrap-etp-prerequisites.ps1";
    private const string Support = "new-etp-support-package.ps1";
    private const string Drill = "invoke-etp-recovery-drill.ps1";
    private const string Owner = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string Automation = "S-1-5-21-1111111111-2222222222-3333333333-1002";

    // ------------------------------------------------------------ setup: HealthExports folder

    [Fact]
    public async Task The_export_folder_grants_full_control_to_system_and_administrators_and_modify_to_automation_and_owners_only()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{ScriptPath(Bootstrap)}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $rules = @(Get-EtpHealthExportFolderRules -AutomationSid '{{Automation}}' -OwnerSids @('{{Owner}}', '{{Owner}}', 'S-1-5-32-545', 'S-1-1-0', 'S-1-5-11', 'S-1-5-19', 'S-1-5-80-123', '', $null, '{{Automation}}'))
            $text = ($rules | ForEach-Object { "$($_.Sid)=$($_.Rights)" }) -join ';'
            if ($text -cne 'S-1-5-18=FullControl;S-1-5-32-544=FullControl;{{Automation}}=Modify;{{Owner}}=Modify') { throw "Rules: $text" }
            # No automation account and no Owner: SYSTEM and Administrators only.
            $none = (@(Get-EtpHealthExportFolderRules -AutomationSid $null -OwnerSids @()) | ForEach-Object { $_.Sid }) -join ';'
            if ($none -cne 'S-1-5-18;S-1-5-32-544') { throw "Empty: $none" }
            # The Owners query only reads.
            $sql = New-EtpActiveOwnersSql
            if ($sql -notmatch '^SET NOCOUNT ON; SELECT windows_identity FROM dbo\.application_users WHERE role_code=''OWNER'' AND is_active=1') { throw "SQL: $sql" }
            if ($sql -match '(?i)\b(INSERT|UPDATE|DELETE|MERGE|EXEC|GRANT|ALTER|DROP)\b' -or $sql.Contains([string][char]34)) { throw "Not read-only: $sql" }
            # A name Windows cannot resolve is skipped, not fatal.
            $sids = @(Get-EtpActiveOwnerSids -Identities @('NO-SUCH-PC-ETP\NoSuchOwner', '', $null))
            if ($sids.Count -ne 0) { throw "Unresolvable names gave $($sids.Count) SID(s)." }
            Write-Output 'Health export folder rules passed.'
            """;
        var result = await RunCommandAsync(command);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("Health export folder rules passed.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Setup_prepares_the_export_folder_after_the_owners_are_final_and_before_the_tasks_without_stopping_setup()
    {
        var text = File.ReadAllText(ScriptPathRaw(Bootstrap));
        var guard = text.IndexOf("if ($MyInvocation.InvocationName -eq '.') { return }", StringComparison.Ordinal);
        var functions = text.IndexOf("function Set-EtpHealthExportFolder", StringComparison.Ordinal);
        var ownerGrant = text.IndexOf("$ownerLoginAdministration = try", StringComparison.Ordinal);
        var call = text.IndexOf("Set-EtpHealthExportFolder -Path (Join-Path $env:ProgramData 'EtpReporting\\HealthExports')", StringComparison.Ordinal);
        var tasks = text.IndexOf("& (Join-Path $scripts 'install-daily-backup-task.ps1')", StringComparison.Ordinal);
        Assert.True(functions > 0 && functions < guard, "The folder functions must be dot-sourceable (above the guard).");
        Assert.True(ownerGrant > guard && call > ownerGrant && call < tasks, "The folder is prepared after the Owner steps and before the tasks.");
        // Best effort: a failure is a WARNING line in the setup log, never the end of setup.
        var block = text[text.LastIndexOf("try {", call, StringComparison.Ordinal)..tasks];
        Assert.Contains("catch {", block, StringComparison.Ordinal);
        Assert.Contains("WARNING: setup could not prepare the health check export folder", block, StringComparison.Ordinal);
        Assert.DoesNotContain("throw $_", block, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ support package

    [Fact]
    public async Task The_support_package_copies_the_newest_14_health_exports_and_nothing_else()
    {
        var root = NewTemporaryDirectory();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "HealthExports")).FullName;
            for (var day = 1; day <= 20; day++)
                File.WriteAllText(Path.Combine(source, $"health-202610{day:00}-023000-Nightly.json"), "{}");
            File.WriteAllText(Path.Combine(source, "health-20261020-101500-Manual.json"), "{}");
            File.WriteAllText(Path.Combine(source, ".health-20261020-101500-Manual-0123456789abcdef0123456789abcdef.tmp"), "{}");
            File.WriteAllText(Path.Combine(source, "notes.json"), "{}");
            File.WriteAllText(Path.Combine(source, "health-old.json"), "{}");
            var destination = Path.Combine(root, "Staging", "health");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{ScriptPath(Support)}}'
                $count = Copy-EtpSupportHealthExports -SourceDirectory '{{Quote(source)}}' -Destination '{{Quote(destination)}}' -Count 14
                if ($count -ne 14) { throw "Copied $count" }
                $none = Copy-EtpSupportHealthExports -SourceDirectory '{{Quote(Path.Combine(root, "Missing"))}}' -Destination '{{Quote(Path.Combine(root, "Unused"))}}'
                if ($none -ne 0 -or (Test-Path -LiteralPath '{{Quote(Path.Combine(root, "Unused"))}}')) { throw 'A missing export folder produced files.' }
                Write-Output 'Support health exports passed.'
                """;
            var run = await RunCommandAsync(command);
            Assert.True(run.ExitCode == 0, run.Output + run.Error);
            Assert.Contains("Support health exports passed.", run.Output, StringComparison.Ordinal);
            var copied = Directory.GetFiles(destination).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(14, copied.Length);
            Assert.Contains("health-20261020-101500-Manual.json", copied);
            Assert.Contains("health-20261020-023000-Nightly.json", copied);
            Assert.Contains("health-20261008-023000-Nightly.json", copied);
            Assert.DoesNotContain("health-20261007-023000-Nightly.json", copied);
            Assert.DoesNotContain(copied, name => !name!.StartsWith("health-202610", StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_support_package_says_when_no_export_could_be_included()
    {
        var text = File.ReadAllText(ScriptPathRaw(Support));
        Assert.Contains("[string]$HealthExportDirectory = \"$env:ProgramData\\EtpReporting\\HealthExports\"", text, StringComparison.Ordinal);
        Assert.Contains("ETP_NOTICE:No health check export was found to include.", text, StringComparison.Ordinal);
        Assert.Contains("ETP_NOTICE:The health check exports could not be read by this account", text, StringComparison.Ordinal);
        Assert.Contains("Join-Path $staging 'health'", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ recovery drill: CHECKDB

    [Fact]
    public async Task The_drill_integrity_check_is_checkdb_physical_only_on_the_configured_database()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{ScriptPath(Drill)}}'
            $sql = New-EtpDrillIntegrityCheckSql -Database 'EtpReporting'
            if ($sql -cne 'SET NOCOUNT ON; DBCC CHECKDB ([EtpReporting]) WITH PHYSICAL_ONLY, NO_INFOMSGS;') { throw "SQL: $sql" }
            foreach ($bad in @('Etp]; DROP DATABASE x; --', 'Etp Reporting', '')) {
                $refused = $false
                try { New-EtpDrillIntegrityCheckSql -Database $bad | Out-Null } catch { $refused = $true }
                if (-not $refused) { throw "Accepted database name: $bad" }
            }
            foreach ($case in @(
                @('SQL Server error 7983 (level 14, state 36): User does not have permission', 'Permission'),
                @('SQL Server error 2571 (level 14, state 3): no permission', 'Permission'),
                @('SQL Server error 916 (level 14, state 2): cannot access', 'Permission'),
                @('SQL Server error 8928 (level 16, state 1): Object ID 1, index ID 1: Page (1:2) could not be processed.', 'Integrity'),
                @('SQL Server error 824 (level 24, state 2): logical consistency-based I/O error', 'Integrity'),
                @($null, 'Integrity'))) {
                $kind = Get-EtpDrillIntegrityFailureKind $case[0]
                if ($kind -cne $case[1]) { throw "Detail '$($case[0])' gave $kind" }
            }
            Write-Output 'Drill integrity check passed.'
            """;
        var result = await RunCommandAsync(command);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Contains("Drill integrity check passed.", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_integrity_check_runs_only_after_a_passed_and_recorded_drill_and_the_completion_line_stays_last()
    {
        var text = File.ReadAllText(ScriptPathRaw(Drill));
        var guard = text.IndexOf("if ($MyInvocation.InvocationName -eq '.') { return }", StringComparison.Ordinal);
        var publish = text.IndexOf("Publish-EtpRecoveryDrillResult -Verdict", StringComparison.Ordinal);
        var failedDrill = text.IndexOf("if (-not $verdict.Succeeded) { throw $verdict.Message }", StringComparison.Ordinal);
        var check = text.IndexOf("New-EtpDrillIntegrityCheckSql -Database $Database", StringComparison.Ordinal);
        var completed = text.IndexOf("Write-Output 'Receipt-verified recovery drill completed.'", StringComparison.Ordinal);
        var log = text.IndexOf("Write-EtpOperationFailureLog -Operation recovery-drill", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < publish, "The functions are dot-sourceable without running a drill.");
        Assert.True(publish < failedDrill && failedDrill < check && check < completed && completed < log,
            "CHECKDB runs after the drill passed and was recorded, before the completion line; failures still reach the drill's log.");
        // The application shows the last plain output line: it is still the drill's own.
        var tail = text[check..completed];
        Assert.DoesNotContain("Receipt-verified", tail, StringComparison.Ordinal);
        Assert.Contains("Write-EtpOperationNotice", tail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_monthly_runner_keeps_its_day_of_month_rule()
    {
        var text = File.ReadAllText(ScriptPathRaw("invoke-monthly-recovery-drill-runner.ps1"));
        Assert.Contains("param([ValidateRange(1,28)][int]$DayOfMonth = 1)", text, StringComparison.Ordinal);
        Assert.Contains("if ((Get-Date).Day -ne $DayOfMonth) { exit 0 }", text, StringComparison.Ordinal);
        Assert.Contains("invoke-etp-recovery-drill.ps1", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Bootstrap)]
    [InlineData(Support)]
    [InlineData(Drill)]
    public void The_changed_scripts_stay_ascii_for_windows_powershell_51(string script)
    {
        var bytes = File.ReadAllBytes(ScriptPathRaw(script));
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        Assert.DoesNotContain(bytes.Skip(start), value => value > 0x7F);
    }

    // ------------------------------------------------------------ plumbing (as MaintenanceScriptOutputTests)

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
        var directory = Path.Combine(Path.GetTempPath(), "EtpHealthScripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string command)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }
}
