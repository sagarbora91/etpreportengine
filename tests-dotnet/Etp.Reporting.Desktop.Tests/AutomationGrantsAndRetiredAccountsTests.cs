using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.3, found moving to Workpc on 2 October 2026.
/// 1. The old PC's accounts could not be deactivated in Settings &gt; Users (migration 0042).
/// 2. Setup installed the operations broker alone; the automation account got its rights only
///    from a separate, unprompted script run, and until then the backup and the drill's
///    recording failed with only "The database operation failed".
/// </summary>
public sealed class AutomationGrantsAndRetiredAccountsTests
{
    // ------------------------------------------------------------ Settings > Users messages

    [Theory]
    [InlineData(51230, "Keep at least one active Owner")]
    [InlineData(51471, "can only be deactivated")]
    [InlineData(15401, "Windows cannot find this account")]
    public void User_access_refusals_say_what_to_do(int number, string expected)
        => Assert.Contains(expected, DesktopFriendlyError.DescribeUserAccessFailure(number));

    [Fact]
    public void Other_numbers_are_not_described_as_user_access_failures()
        => Assert.Null(DesktopFriendlyError.DescribeUserAccessFailure(51460));

    // ------------------------------------------------------------ grant state

    [Fact]
    public async Task Grant_state_is_read_from_the_query_lines()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            function Check($lines, $state, $missing) {
                $r = ConvertFrom-EtpAutomationGrantResult -Lines $lines
                if ($r.State -cne $state) { throw "Expected $state for '$($lines -join ',')', got $($r.State)." }
                if (@($r.Missing).Count -ne $missing) { throw "Expected $missing missing for $state, got $(@($r.Missing).Count): $($r.Missing -join '; ')" }
            }
            $p = 'ETP_AUTOMATION:'
            Check @() 'UNKNOWN' 0
            Check @('-----', ($p+'UNKNOWN')) 'UNKNOWN' 0
            Check @(($p+'CALLER_ADMIN')) 'NOT_STORE_MANAGER' 0
            Check @(($p+'CALLER_ADMIN'),($p+'LOGIN'),($p+'USER')) 'NOT_STORE_MANAGER' 0
            # Store Manager role but the row is inactive: not ready to be granted anything.
            Check @(($p+'CALLER_ADMIN'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER')) 'NOT_STORE_MANAGER' 0
            # Workpc at 16:44 on 2 October: an active Store Manager and nothing else (and the
            # 1.9.2 broker, which records no row counts).
            Check @(($p+'CALLER_ADMIN'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE'),($p+'BROKER')) 'GRANTS_MISSING' 5
            Check @(($p+'CALLER_ADMIN'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE')) 'GRANTS_MISSING' 5
            Check @(($p+'CALLER_ADMIN'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE'),($p+'ROLE:etp_automation'),($p+'ROLE:db_backupoperator'),($p+'BROKER'),($p+'BROKER_EXECUTE'),($p+'BROKER_CURRENT')) 'GRANTS_MISSING' 1
            Check @(($p+'CALLER_ADMIN'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE'),($p+'ROLE:etp_automation'),($p+'ROLE:db_backupoperator'),($p+'BROKER'),($p+'BROKER_EXECUTE'),($p+'BROKER_SIGNED'),($p+'BROKER_CURRENT')) 'READY' 0
            # 1.9.3, A4.4: every right in place, but the broker is from an earlier build. Setup
            # then runs the full install, which replaces and re-signs it.
            $r = ConvertFrom-EtpAutomationGrantResult -Lines @(($p+'CALLER_ADMIN'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE'),($p+'ROLE:etp_automation'),($p+'ROLE:db_backupoperator'),($p+'BROKER'),($p+'BROKER_EXECUTE'),($p+'BROKER_SIGNED'))
            if ($r.State -cne 'GRANTS_MISSING' -or @($r.Missing).Count -ne 1 -or -not $r.Missing[0].Contains('current operations broker')) { throw "Outdated broker: $($r.State) $($r.Missing -join '; ')" }
            # The account itself cannot see the signature, so it is not judged from there.
            Check @(($p+'CALLER_SELF'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE'),($p+'ROLE:etp_automation'),($p+'ROLE:db_backupoperator'),($p+'BROKER_EXECUTE')) 'READY' 0
            Check @(($p+'CALLER_SELF'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE'),($p+'ROLE:etp_automation')) 'GRANTS_MISSING' 2
            # Case matters: nothing but the query's own codes counts.
            Check @(($p+'CALLER_ADMIN'),($p+'login'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE')) 'NOT_STORE_MANAGER' 0
            Write-Output 'Grant states passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Grant states passed.", result.Output);
    }

    [Fact]
    public async Task Grant_query_only_reads_and_refuses_an_unsafe_account_name()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $q = Get-EtpAutomationGrantQuery -Database 'EtpReporting' -AutomationPrincipal 'WORKPC\EtpAutomation'
            if ($q -match '(?i)\b(GRANT|ALTER|CREATE|DROP|DENY|REVOKE|UPDATE|DELETE|MERGE|BACKUP|RESTORE)\b') { throw "The grant check changes something: $($Matches[0])" }
            if ($q -notmatch [regex]::Escape((Get-EtpOperationsProcedureName 'EtpReporting'))) { throw 'The grant check does not look at this database''s broker.' }
            if ($q -notmatch "N'WORKPC\\EtpAutomation'") { throw 'The account is not in the query.' }
            foreach ($bad in @("WORKPC\Etp'Automation", 'WORKPC\Etp]Automation', 'EtpAutomation', 'A\B\C')) {
                $refused = $false
                try { Get-EtpAutomationGrantQuery -Database 'EtpReporting' -AutomationPrincipal $bad | Out-Null } catch { $refused = $true }
                if (-not $refused) { throw "Unsafe account name accepted: $bad" }
            }
            Write-Output 'Grant query passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Grant query passed.", result.Output);
    }

    [Fact]
    public async Task The_masked_failure_is_explained_only_when_a_right_is_really_missing()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $global:answer = @()
            function Invoke-EtpSql { param($SqlCmd,$Server,$Database,$Query) if ($global:answer -ceq 'THROW') { throw 'probe failed' }; return $global:answer }
            $p = 'ETP_AUTOMATION:'
            $args0 = @{ SqlCmd = 'sqlcmd.exe'; Server = 'lpc:.\SQLEXPRESS'; ServerInstance = '.\SQLEXPRESS'; Database = 'EtpReporting'; AutomationPrincipal = 'WORKPC\EtpAutomation' }

            $global:answer = @(($p+'CALLER_SELF'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE'))
            $m = Get-EtpAutomationFailureMessage -Message $EtpMaskedSqlFailure @args0
            foreach ($needle in @('WORKPC\EtpAutomation','the etp_automation database role','the db_backupoperator database role','EXECUTE on the operations broker','install-etp-sql-operations.ps1',"-ServerInstance '.\SQLEXPRESS' -Database 'EtpReporting' -AutomationPrincipal 'WORKPC\EtpAutomation'",'step 7','setup again')) {
                if (-not $m -or -not $m.Contains($needle)) { throw "Missing '$needle' in: $m" }
            }

            $global:answer = @(($p+'CALLER_ADMIN'),($p+'LOGIN'))
            $m = Get-EtpAutomationFailureMessage -Message $EtpMaskedSqlFailure @args0
            if (-not $m -or -not $m.Contains('not an active Store Manager') -or -not $m.Contains('Settings > Users') -or -not $m.Contains('Run as administrator')) { throw "Store Manager guidance missing: $m" }

            # Ready, unreadable, or another failure: the original error stands.
            $global:answer = @(($p+'CALLER_SELF'),($p+'LOGIN'),($p+'USER'),($p+'STORE_MANAGER'),($p+'ACTIVE'),($p+'ROLE:etp_automation'),($p+'ROLE:db_backupoperator'),($p+'BROKER_EXECUTE'))
            if ($null -ne (Get-EtpAutomationFailureMessage -Message $EtpMaskedSqlFailure @args0)) { throw 'A ready account was blamed.' }
            $global:answer = @(($p+'UNKNOWN'))
            if ($null -ne (Get-EtpAutomationFailureMessage -Message $EtpMaskedSqlFailure @args0)) { throw 'An unknown state was explained.' }
            $global:answer = 'THROW'
            if ($null -ne (Get-EtpAutomationFailureMessage -Message $EtpMaskedSqlFailure @args0)) { throw 'A failed probe replaced the original error.' }
            $global:answer = @(($p+'CALLER_ADMIN'),($p+'LOGIN'))
            if ($null -ne (Get-EtpAutomationFailureMessage -Message 'Backup storage is below the required free-space limit.' @args0)) { throw 'A different failure was explained as missing rights.' }
            Write-Output 'Failure explanation passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Failure explanation passed.", result.Output);
    }

    [Fact]
    public void The_masked_message_the_explanation_matches_is_the_one_Invoke_EtpSql_throws()
    {
        var common = File.ReadAllText(Common());
        const string masked = "The database operation failed. Check SQL permissions and operation prerequisites.";
        Assert.Contains($"$EtpMaskedSqlFailure = '{masked}'", common, StringComparison.Ordinal);
        // Invoke-EtpSql: the native call failing outright, and SQL Server failing the statement
        // (that one is built as an exception, to carry what SQL Server said in its Data).
        Assert.Equal(1, common.Split($"throw '{masked}'").Length - 1);
        Assert.Equal(1, common.Split($"[Management.Automation.RuntimeException]::new('{masked}')").Length - 1);
    }

    // ------------------------------------------------------------ setup completes the grants

    [Fact]
    public async Task Setup_completes_the_grants_only_for_an_active_store_manager_and_otherwise_says_what_to_do()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Bootstrap()}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $common = @{ ServerInstance = '.\SQLEXPRESS'; Database = 'EtpReporting'; AutomationPrincipal = 'WORKPC\EtpAutomation'; ScriptsDirectory = 'C:\Program Files\ETP\scripts' }
            function State($s, $m) { [pscustomobject]@{ State = $s; Missing = @($m) } }

            # Ready: nothing is installed.
            $global:installs = 0
            $lines = @(Complete-EtpAutomationGrants @common -GetState { State 'READY' @() } -InstallModule { $global:installs++ })
            if ($global:installs -ne 0 -or $lines.Count -ne 1 -or -not $lines[0].Contains('can run')) { throw "READY: $($lines -join ' | ')" }

            # Not a Store Manager yet: nothing is installed, and the next step is spelt out.
            $global:installs = 0
            $lines = @(Complete-EtpAutomationGrants @common -GetState { State 'NOT_STORE_MANAGER' @() } -InstallModule { $global:installs++ })
            if ($global:installs -ne 0 -or $lines.Count -ne 1 -or -not $lines[0].StartsWith('NEXT STEP: ') -or -not $lines[0].Contains('Settings > Users') -or -not $lines[0].Contains("-File 'C:\Program Files\ETP\scripts\install-etp-sql-operations.ps1'")) { throw "NOT_STORE_MANAGER: $($lines -join ' | ')" }

            # Store Manager without the rights: the module is installed once, then checked again.
            $global:installs = 0; $global:checks = 0
            $lines = @(Complete-EtpAutomationGrants @common -GetState { $global:checks++; if ($global:checks -eq 1) { State 'GRANTS_MISSING' @('the etp_automation database role') } else { State 'READY' @() } } -InstallModule { $global:installs++; 'Restricted SQL backup and recovery module installed for EtpReporting.' })
            if ($global:installs -ne 1 -or $global:checks -ne 2) { throw "GRANTS_MISSING ran install $global:installs and checks $global:checks times." }
            if (-not $lines[0].Contains('the etp_automation database role') -or -not ($lines -contains 'Restricted SQL backup and recovery module installed for EtpReporting.') -or -not $lines[-1].Contains('now has')) { throw "GRANTS_MISSING: $($lines -join ' | ')" }

            # The install refuses: reported with the command, never fatal.
            $global:installs = 0
            $lines = @(Complete-EtpAutomationGrants @common -GetState { State 'GRANTS_MISSING' @('x') } -InstallModule { $global:installs++; throw 'Add WORKPC\EtpAutomation as a Store Manager in Settings > Users first.' })
            if ($global:installs -ne 1 -or -not $lines[-1].StartsWith('WARNING: ') -or -not $lines[-1].Contains('Settings > Users first.') -or -not $lines[-1].Contains('install-etp-sql-operations.ps1')) { throw "Refused install: $($lines -join ' | ')" }

            # Installed but still incomplete.
            $lines = @(Complete-EtpAutomationGrants @common -GetState { State 'GRANTS_MISSING' @('the broker''s module signature') } -InstallModule { 'done' })
            if (-not $lines[-1].StartsWith('WARNING: ') -or -not $lines[-1].Contains('module signature')) { throw "Still incomplete: $($lines -join ' | ')" }

            # The state cannot be read.
            $lines = @(Complete-EtpAutomationGrants @common -GetState { throw 'no SQL' } -InstallModule { throw 'must not run' })
            if ($lines.Count -ne 1 -or -not $lines[0].StartsWith('WARNING: ') -or -not $lines[0].Contains('install-etp-sql-operations.ps1')) { throw "Unreadable: $($lines -join ' | ')" }
            $lines = @(Complete-EtpAutomationGrants @common -GetState { State 'UNKNOWN' @() } -InstallModule { throw 'must not run' })
            if ($lines.Count -ne 1 -or -not $lines[0].StartsWith('WARNING: ')) { throw "Unknown: $($lines -join ' | ')" }
            Write-Output 'Setup grant completion passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Setup grant completion passed.", result.Output);
    }

    [Fact]
    public async Task Setup_completes_the_grants_after_the_tasks_with_the_full_module_install()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{Bootstrap()}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'bootstrap-etp-prerequisites.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            $calls = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Complete-EtpAutomationGrants' }, $true))
            if ($calls.Count -ne 1) { throw "Complete-EtpAutomationGrants is called $($calls.Count) times." }
            $tasks = @($top | Where-Object { $_.Extent.Text -match 'install-daily-backup-task\.ps1' })
            $done = @($top | Where-Object { $_.Extent.Text -eq "Write-SetupLog 'ETP prerequisite bootstrap completed successfully.'" })
            if ($tasks.Count -ne 1 -or $done.Count -ne 1) { throw 'The task step or the end of setup could not be found.' }
            if ($calls[0].Extent.StartOffset -lt $tasks[0].Extent.EndOffset -or $calls[0].Extent.EndOffset -gt $done[0].Extent.StartOffset) { throw 'The grants are not completed between the tasks and the end of setup.' }
            $install = @($calls[0].FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.InvocationOperator -eq 'Ampersand' -and $n.Extent.Text -match 'install-etp-sql-operations\.ps1' }, $true))
            if ($install.Count -ne 1) { throw 'The grants are not completed with install-etp-sql-operations.ps1.' }
            if (@($install[0].CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -eq 'BrokerOnly' }).Count -ne 0) { throw 'The grants step installs the broker only.' }
            if ($install[0].Extent.Text -notmatch '-AutomationPrincipal \$operationConfiguration\.automationPrincipal') { throw 'The grants step does not use the protected configuration''s account.' }
            Write-Output 'Setup grant placement passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Setup grant placement passed.", result.Output);
    }

    // ------------------------------------------------------------ backup and drill name the step

    [Theory]
    [InlineData("backup-etp-database.ps1", "BACKUP", true)]
    [InlineData("invoke-etp-recovery-drill.ps1", "Publish-EtpRecoveryDrillResult", false)]
    public async Task Backup_and_drill_explain_a_masked_failure_through_the_grant_check(string script, string guarded, bool onlyForTheAutomationAccount)
    {
        var path = Path.Combine(RepositoryRoot(), "scripts", script).Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{path}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw '{{script}} does not parse.' }
            $guarded = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.Extent.Text.Contains('{{guarded}}') }, $true))
            if ($guarded.Count -lt 1) { throw 'The guarded step was not found.' }
            foreach ($step in $guarded) {
                $try = $step.Parent
                while ($null -ne $try -and -not ($try -is [System.Management.Automation.Language.TryStatementAst])) { $try = $try.Parent }
                if ($null -eq $try) { throw "Not inside a try: $($step.Extent.Text)" }
                $catch = $try.CatchClauses[0].Body
                $explain = @($catch.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Get-EtpAutomationFailureMessage' }, $true))
                if ($explain.Count -ne 1) { throw 'The catch does not ask the grant check.' }
                if ($catch.Extent.Text -notmatch 'throw \$failure') { throw 'The catch does not keep the original error otherwise.' }
                if ({{(onlyForTheAutomationAccount ? "$true" : "$false")}} -and $catch.Extent.Text -notmatch 'WindowsIdentity\]::GetCurrent\(\)\.Name -ieq \$automationPrincipal') { throw 'The backup explains missing rights to a caller that does not need them.' }
            }
            Write-Output 'Failure explanation placement passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Failure explanation placement passed.", result.Output);
    }

    [Fact]
    public void The_restore_helper_tells_the_owner_to_run_setup_after_adding_the_automation_account_and_how_to_retire_old_accounts()
    {
        var restore = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "restore-etp-database.ps1"));
        var add = restore.IndexOf("as an active Store Manager in Settings > Users.", StringComparison.Ordinal);
        var setup = restore.IndexOf("3. Run the ETP setup once more.", StringComparison.Ordinal);
        Assert.True(add > 0 && setup > add, "The restore helper does not order the steps: add the account, then run setup.");
        Assert.Contains("database update 0042, deactivate them there", restore, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ plumbing

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    private static string Common() => Path.Combine(RepositoryRoot(), "scripts", "etp-operations-common.ps1").Replace("'", "''");
    private static string Bootstrap() => Path.Combine(RepositoryRoot(), "scripts", "bootstrap-etp-prerequisites.ps1").Replace("'", "''");

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string command)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
