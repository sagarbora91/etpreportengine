using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

public sealed class BootstrapPrerequisiteTests
{
    [Fact]
    public async Task Bootstrap_resolves_the_configured_service_and_rejects_unsupported_editions_without_mutations()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $checks = 0
            foreach ($case in @(
                @('.', 'MSSQLSERVER'), @('localhost', 'MSSQLSERVER'), @('(local)', 'MSSQLSERVER'),
                @('.\ConfiguredInstance', 'MSSQL$ConfiguredInstance'), @('lpc:localhost\ConfiguredInstance', 'MSSQL$ConfiguredInstance'),
                @('np:localhost\ConfiguredInstance', 'MSSQL$ConfiguredInstance'))) {
                if ((Resolve-EtpBootstrapServiceName $case[0] 'ConfiguredDatabase') -cne $case[1]) { throw 'Configured service mismatch.' }
                $checks++
            }
            foreach ($endpoint in @('remote.example\Instance', '(localdb)\MSSQLLocalDB', 'np:\\localhost\pipe\sql\query')) {
                $rejected = $false
                try { Resolve-EtpBootstrapServiceName $endpoint 'ConfiguredDatabase' | Out-Null } catch { $rejected = $true }
                if (-not $rejected) { throw 'Unsupported bootstrap endpoint accepted.' }
                $checks++
            }
            # D9 revised: Express and Web are accepted. They cannot encrypt a backup, which
            # the backup path handles by taking an unencrypted one and recording NONE in
            # the receipt, rather than by refusing to run on the edition the shop owns.
            foreach ($case in @(@(16,2,'Standard Edition'), @(16,3,'Enterprise Edition'), @(17,3,'Developer Edition'), @(16,4,'Express Edition'), @(16,2,'Web Edition'))) {
                Assert-EtpBootstrapSqlEdition $case[0] $case[1] $case[2]
                $checks++
            }
            # Too old, or not a local engine at all: still refused.
            foreach ($case in @(@(15,3,'Enterprise Edition'), @(16,8,'Managed Instance'))) {
                $rejected = $false
                try { Assert-EtpBootstrapSqlEdition $case[0] $case[1] $case[2] } catch { $rejected = $true }
                if (-not $rejected) { throw 'Unsupported SQL Server edition accepted.' }
                $checks++
            }
            if ($checks -ne 16) { throw 'Not all bootstrap preflight cases ran.' }
            Write-Output 'Bootstrap preflight behavior passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Bootstrap preflight behavior passed.", result.Output);
    }

    [Fact]
    public async Task User_owned_installation_is_rejected_before_executing_payloads_or_migrations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapBoundary", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var executable = Path.Combine(root, "Etp.Reporting.Desktop.exe");
            var migrationDirectory = Directory.CreateDirectory(Path.Combine(root, "database", "migrations")).FullName;
            await File.WriteAllTextAsync(executable, "Untrusted executable marker");
            await File.WriteAllTextAsync(Path.Combine(migrationDirectory, "9999_marker.sql"), "THROW 51999,'Untrusted migration reached',1;");
            var script = FindBootstrapScript().Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference='Stop'
                . '{{script}}' -ApplicationDirectory '{{root.Replace("'", "''")}}'
                $rejected=$false
                try { Assert-EtpBootstrapPayloads '{{root.Replace("'", "''")}}' }
                catch { $rejected=$_.Exception.Message -match 'non-administrator|owned by Administrators or SYSTEM' }
                if (-not $rejected) { throw 'Untrusted elevated payloads were accepted.' }
                Write-Output 'Untrusted payloads rejected before execution.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Untrusted payloads rejected before execution.", result.Output);
            Assert.Equal("Untrusted executable marker", await File.ReadAllTextAsync(executable));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Missing_protected_machine_configuration_stops_bootstrap_before_creating_files()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapBoundary", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // The child process sees this empty, disposable ProgramData only.
            var result = await RunPowerShellAsync(["-File", FindBootstrapScript(), "-ApplicationDirectory", root], root);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Until 27 September 2026 bootstrap read the protected configuration first, and the
    // configuration can only be written once SQL Server's service account exists, so the
    // "Install SQL Server" option could never do its job on a new PC.
    [Fact]
    public async Task Fresh_machine_installs_sql_and_client_tools_before_creating_the_protected_configuration()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $global:calls = [Collections.Generic.List[string]]::new()
            function Assert-EtpBootstrapAdministrator { $global:calls.Add('admin') }
            function Assert-EtpBootstrapPayloads { param($ApplicationDirectory) $global:calls.Add("payloads($ApplicationDirectory)") }
            function Install-EtpSqlPrerequisitesFromPayload { param($PayloadDirectory, $ServiceName) $global:calls.Add("sql($PayloadDirectory,$ServiceName)") }
            function Invoke-EtpOperationFolderSetup { param($ServiceName, $ServerInstance, $Database, $AutomationPrincipal, [switch]$CreateAutomationAccount) $global:calls.Add("folders($ServiceName,$ServerInstance,$Database,CreateAutomationAccount=$CreateAutomationAccount)") }
            function Test-EtpSqlEngineInstalled { param($ServiceName) $false }
            function Assert-EtpAdoptedSqlInstance { param($ServiceName, $ServerInstance) $global:calls.Add('adopt') }
            function Get-EtpOperationsConfiguration { throw 'The configuration was read before it could exist.' }
            Initialize-EtpFreshMachine -ApplicationRoot 'C:\Unused' -SqlPayloadDirectory 'C:\UnusedMedia'
            $expected = @('admin', 'payloads(C:\Unused)', 'sql(C:\UnusedMedia,MSSQL$SQLEXPRESS)', 'folders(MSSQL$SQLEXPRESS,.\SQLEXPRESS,EtpReporting,CreateAutomationAccount=True)')
            if (($global:calls -join ' / ') -cne ($expected -join ' / ')) { throw "Fresh machine steps ran as: $($global:calls -join ' / ')" }
            Write-Output 'Fresh machine order passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Fresh machine order passed.", result.Output);
    }

    // An SQLEXPRESS instance already on a new PC - another program's, say - was adopted as it
    // was and described as hardened. It is now checked, after the client tools the check
    // needs and before any ETP folder, account or configuration exists, and refused unless it
    // matches what setup would have installed.
    [Fact]
    public async Task An_sql_instance_already_on_a_new_pc_is_checked_before_any_etp_configuration_is_created()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $global:calls = [Collections.Generic.List[string]]::new()
            $global:refuse = $false
            function Assert-EtpBootstrapAdministrator { $global:calls.Add('admin') }
            function Assert-EtpBootstrapPayloads { param($ApplicationDirectory) $global:calls.Add('payloads') }
            function Test-EtpSqlEngineInstalled { param($ServiceName) $global:calls.Add("present($ServiceName)"); $true }
            function Install-EtpSqlPrerequisitesFromPayload { param($PayloadDirectory, $ServiceName) $global:calls.Add('sql') }
            function Assert-EtpAdoptedSqlInstance { param($ServiceName, $ServerInstance) $global:calls.Add("adopt($ServiceName,$ServerInstance)"); if ($global:refuse) { throw 'Refused by the adopted-instance check.' } }
            function Invoke-EtpOperationFolderSetup { param($ServiceName, $ServerInstance, $Database, $AutomationPrincipal, [switch]$CreateAutomationAccount) $global:calls.Add('folders') }
            Initialize-EtpFreshMachine -ApplicationRoot 'C:\Unused' -SqlPayloadDirectory 'C:\UnusedMedia'
            $expected = 'admin / payloads / present(MSSQL$SQLEXPRESS) / sql / adopt(MSSQL$SQLEXPRESS,.\SQLEXPRESS) / folders'
            if (($global:calls -join ' / ') -cne $expected) { throw "Accepted instance ran as: $($global:calls -join ' / ')" }
            $global:calls.Clear(); $global:refuse = $true
            $refused = $false
            try { Initialize-EtpFreshMachine -ApplicationRoot 'C:\Unused' -SqlPayloadDirectory 'C:\UnusedMedia' } catch { $refused = $_.Exception.Message -eq 'Refused by the adopted-instance check.' }
            if (-not $refused) { throw 'A refused instance did not stop setup.' }
            if ($global:calls -contains 'folders') { throw "Folders were set up for a refused instance: $($global:calls -join ' / ')" }
            Write-Output 'Adopted instance order passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Adopted instance order passed.", result.Output);
    }

    [Fact]
    public async Task An_adopted_sql_instance_must_have_the_settings_setup_would_have_installed()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            function Hex([string]$Sid) { $s = [Security.Principal.SecurityIdentifier]::new($Sid); $b = [byte[]]::new($s.BinaryLength); $s.GetBinaryForm($b, 0); '0x' + (($b | ForEach-Object { $_.ToString('X2') }) -join '') }
            $administrators = 'ETP_ADOPT_ADMIN:' + (Hex 'S-1-5-32-544') + '|BUILTIN\Administrators'
            $service = 'ETP_ADOPT_ADMIN:' + (Hex 'S-1-5-80-3880718306-3832830129-1677859214-2598158968-1052248003') + '|NT SERVICE\MSSQL$SQLEXPRESS'
            # As sqlcmd prints it: a blank header and a dashes line before each result.
            $good = @('', '------', 'ETP_ADOPT_SYSADMIN:1', 'ETP_ADOPT_WINDOWS_ONLY:1  ', 'ETP_ADOPT_PROTOCOL:NP=0', 'ETP_ADOPT_PROTOCOL:TCP=0', $administrators, $service)
            function Problems([string[]]$Lines) { @(Get-EtpAdoptedSqlInstanceProblems -Lines $Lines) }
            $found = @(Problems $good)
            if ($found.Count -ne 0) { throw "What setup installs was refused: $($found -join '; ')" }
            function Expect([string[]]$Lines, [string]$Pattern) {
                $found = @(Problems $Lines)
                if (($found -join '; ') -notmatch $Pattern) { throw "Expected '$Pattern', got: $($found -join '; ')" }
            }
            Expect ($good -replace 'SYSADMIN:1', 'SYSADMIN:0') 'not a SQL Server administrator'
            Expect @() 'not a SQL Server administrator'
            Expect ($good -replace 'WINDOWS_ONLY:1', 'WINDOWS_ONLY:0') 'SQL Server logins'
            Expect @($good | Where-Object { $_ -notlike '*WINDOWS_ONLY*' }) 'SQL Server logins'
            Expect ($good -replace 'TCP=0', 'TCP=1') 'over the network'
            Expect ($good -replace 'NP=0', 'NP=1') 'named-pipe'
            Expect @($good | Where-Object { $_ -notlike '*TCP=*' }) 'whether TCP/IP is on'
            Expect ($good + 'ETP_ADOPT_PROTOCOL:NP=0') 'whether named pipes is on'
            Expect ($good + ('ETP_ADOPT_ADMIN:' + (Hex 'S-1-5-32-545') + '|BUILTIN\Users')) 'BUILTIN\\Users'
            Expect ($good + ('ETP_ADOPT_ADMIN:' + (Hex 'S-1-5-21-1-2-3-1001') + '|POSPC\Vendor')) 'POSPC\\Vendor'
            Expect ($good + 'ETP_ADOPT_ADMIN:0xZZ|Broken') 'Broken'
            Write-Output 'Adopted instance settings passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Adopted instance settings passed.", result.Output);
    }

    // Strict folder setup writes no operations.json on purpose. Missing file alone used to count
    // as a new PC, so setup created EtpAutomation and granted it the folders over that decision.
    [Fact]
    public async Task Strict_folders_without_a_configuration_are_refused_and_not_treated_as_a_new_pc()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapBoundary", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var programData = Directory.CreateDirectory(Path.Combine(root, "ProgramData")).FullName;
            var operations = Directory.CreateDirectory(Path.Combine(programData, "EtpReporting", "Operations")).FullName;
            var application = Directory.CreateDirectory(Path.Combine(root, "Application")).FullName;
            var media = Directory.CreateDirectory(Path.Combine(root, "Media")).FullName;
            foreach (var name in FullMedia) await File.WriteAllTextAsync(Path.Combine(media, name), "Disposable media marker " + name);
            // The real script, as setup starts it with the SQL option ticked. The refusal comes
            // before the administrator check, so it is the same elevated or not.
            var result = await RunPowerShellAsync(["-File", FindBootstrapScript(), "-ApplicationDirectory", application, "-SqlPayloadDirectory", media], programData);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("strict folder setup", result.Output);
            Assert.Contains("Nothing was changed.", result.Output);
            Assert.Equal(new[] { operations }, Directory.EnumerateFileSystemEntries(programData, "*", SearchOption.AllDirectories).Where(x => x != Path.GetDirectoryName(operations)));
            foreach (var name in FullMedia) Assert.Equal("Disposable media marker " + name, await File.ReadAllTextAsync(Path.Combine(media, name)));

            var script = FindBootstrapScript().Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
                $operations = '{{operations.Replace("'", "''")}}'
                $missing = Join-Path (Split-Path -Parent $operations) 'NoSuchOperations'
                if (-not (Test-EtpNewMachine -OperationsDirectory $missing)) { throw 'A PC without ETP folders was not new.' }
                $refused = $false
                try { Test-EtpNewMachine -OperationsDirectory $operations | Out-Null } catch { $refused = $_.Exception.Message -like '*strict folder setup*Nothing was changed.*' }
                if (-not $refused) { throw 'Strict folders were taken for a new PC.' }
                Set-Content -LiteralPath (Join-Path $operations 'operations.json') -Value '{}'
                if (Test-EtpNewMachine -OperationsDirectory $operations) { throw 'A configured PC was taken for a new one.' }
                Write-Output 'New machine decision passed.'
                """;
            var decisions = await RunPowerShellAsync(["-Command", command]);
            Assert.True(decisions.ExitCode == 0, decisions.Output);
            Assert.Contains("New machine decision passed.", decisions.Output);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_new_database_gives_its_owner_a_login_of_their_own_the_documented_way()
    {
        // Setup's SQL Server makes only BUILTIN\Administrators a SQL administrator, which an
        // unelevated token does not carry: the Owner of a database setup had just created could
        // not open ETP from the Start menu at all. Needs SQL Server to run, so the SQL and where
        // it runs are checked here.
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $own = @((Get-Command New-EtpSetupOwnerLoginSql).Parameters.Keys | Where-Object { $_ -notin [System.Management.Automation.PSCmdlet]::CommonParameters -and $_ -notin [System.Management.Automation.PSCmdlet]::OptionalCommonParameters })
            if ($own.Count -ne 0) { throw "The Owner login batch takes input: $($own -join ', ')" }
            Write-Output '---SQL---'
            New-EtpSetupOwnerLoginSql
            Write-Output '---END---'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'bootstrap-etp-prerequisites.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            $create = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$databaseAction -ceq ''Create''' })
            if ($create.Count -ne 1) { throw 'There is no single new-database branch.' }
            $uses = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'New-EtpSetupOwnerLoginSql' }, $true))
            if ($uses.Count -ne 1 -or $uses[0].Extent.StartOffset -lt $create[0].Extent.StartOffset -or $uses[0].Extent.EndOffset -gt $create[0].Extent.EndOffset) { throw 'The Owner login is given outside the new-database branch.' }
            $completed = @($top | Where-Object { $_.Extent.Text -eq '$migrationPhaseCompleted = $true' })
            $tasks = @($top | Where-Object { $_.Extent.Text -match 'install-daily-backup-task\.ps1' })
            if ($completed.Count -ne 1 -or $tasks.Count -ne 1) { throw 'The migration end or the task step could not be found.' }
            if ($create[0].Extent.StartOffset -lt $completed[0].Extent.EndOffset -or $create[0].Extent.EndOffset -gt $tasks[0].Extent.StartOffset) { throw 'The Owner login is not given between the checked migration and the tasks.' }
            $refusals = @($create[0].FindAll({ param($node) $node -is [System.Management.Automation.Language.ThrowStatementAst] }, $true))
            if ($refusals.Count -ne 1) { throw 'A failed Owner login does not stop setup.' }
            Write-Output 'Owner login placement passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Owner login placement passed.", result.Output);
        var start = result.Output.IndexOf("---SQL---", StringComparison.Ordinal);
        var end = result.Output.IndexOf("---END---", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, result.Output);
        var sql = result.Output[start..end];
        string[] ordered =
        [
            "SET XACT_ABORT ON",
            "DECLARE @identity nvarchar(200)=SUSER_SNAME();",
            "IF SUSER_ID(@identity) IS NULL",
            "IF NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=@identity AND role_code='OWNER' AND is_active=1)",
            "THROW 51920",
            "BEGIN TRANSACTION;",
            "EXEC dbo.configure_application_role @identity=@identity,@role='OWNER',@active=1;",
            "COMMIT TRANSACTION;",
        ];
        var position = -1;
        foreach (var fragment in ordered)
        {
            var next = sql.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(next > position, $"'{fragment}' is missing or out of order.");
            position = next;
        }
        // Only the account running setup, and nothing that changes who owns the database or its rows.
        Assert.DoesNotContain("ALTER AUTHORIZATION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sysadmin", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Setup_reports_an_owner_it_could_not_give_the_grant_option_without_failing()
    {
        // Migration 0043 gives Owners ALTER ANY LOGIN WITH GRANT OPTION so they can change users
        // unelevated, but SQL Server never lets the account running setup grant it to itself,
        // and that account is usually the Owner. Setup says so; the check is read-only, runs
        // after the migration and the new-database Owner step, and never stops setup.
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $own = @((Get-Command New-EtpOwnerLoginAdministrationSql).Parameters.Keys | Where-Object { $_ -notin [System.Management.Automation.PSCmdlet]::CommonParameters -and $_ -notin [System.Management.Automation.PSCmdlet]::OptionalCommonParameters })
            if ($own.Count -ne 0) { throw "The grant-option check takes input: $($own -join ', ')" }
            Write-Output '---SQL---'
            New-EtpOwnerLoginAdministrationSql
            Write-Output '---END---'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'bootstrap-etp-prerequisites.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            $create = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$databaseAction -ceq ''Create''' })
            $check = @($top | Where-Object { $_.Extent.Text -match '^\$ownerLoginAdministration = try \{ Invoke-SqlScalar -TargetDatabase \$Database -Query \(New-EtpOwnerLoginAdministrationSql\) \} catch \{ ''UNKNOWN'' \}$' })
            $note = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$ownerLoginAdministration -ceq ''MISSING''' })
            $tasks = @($top | Where-Object { $_.Extent.Text -match 'install-daily-backup-task\.ps1' })
            # Sagar's decision, 2 October 2026: before the check, setup has SYSTEM give the grant
            # option to every active Owner that lacks it, and that never stops setup either.
            $count = @($top | Where-Object { $_.Extent.Text -match '^\$ownersWithoutGrantOption = try \{ Invoke-SqlScalar -TargetDatabase \$Database -Query \(New-EtpOwnersWithoutGrantOptionSql\) \} catch \{ ''UNKNOWN'' \}$' })
            $grant = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$ownersWithoutGrantOption -cne ''0''' })
            if ($create.Count -ne 1 -or $count.Count -ne 1 -or $grant.Count -ne 1 -or $check.Count -ne 1 -or $note.Count -ne 1 -or $tasks.Count -ne 1) { throw "The grant-option steps could not be found ($($create.Count), $($count.Count), $($grant.Count), $($check.Count), $($note.Count), $($tasks.Count))." }
            if ($count[0].Extent.StartOffset -lt $create[0].Extent.EndOffset -or $grant[0].Extent.StartOffset -lt $count[0].Extent.EndOffset -or $check[0].Extent.StartOffset -lt $grant[0].Extent.EndOffset) { throw 'The SYSTEM grant is not made between the Owner step and the check.' }
            if ($check[0].Extent.StartOffset -lt $create[0].Extent.EndOffset -or $note[0].Extent.StartOffset -lt $check[0].Extent.EndOffset -or $note[0].Extent.EndOffset -gt $tasks[0].Extent.StartOffset) { throw 'The check is not made between the Owner step and the tasks.' }
            foreach ($step in @($grant[0], $note[0])) {
                if (@($step.FindAll({ param($node) $node -is [System.Management.Automation.Language.ThrowStatementAst] }, $true)).Count -ne 0) { throw 'A missing grant option stops setup.' }
            }
            $attempt = $grant[0].Clauses[0].Item2.Extent.Text
            if ($attempt -notmatch 'try \{ Invoke-EtpOwnerGrantOptionAsSystem -SqlCmdPath \$sqlcmdPath -ServerInstance \$ServerInstance -Database \$Database \} catch \{ \$null \}' -or $attempt -notmatch 'Write-SetupLog \$ownerGrant\.Message') { throw 'The SYSTEM grant is not attempted, guarded and logged.' }
            if ($note[0].Clauses[0].Item2.Extent.Text -notmatch 'Write-SetupLog "NOTE: ' -or $note[0].Extent.Text -notmatch 'Owners and SQL Server logins') { throw 'The NOTE does not say where the fix is.' }
            if ($note[0].Extent.Text -notmatch 'Get-EtpOwnerGrantManualCommand -Identity \$identity\.Name -ServerInstance \$ServerInstance -SqlCmdPath \$sqlcmdPath') { throw 'The NOTE does not give the manual command.' }
            Write-Output 'Grant-option check placement passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Grant-option check placement passed.", result.Output);
        var start = result.Output.IndexOf("---SQL---", StringComparison.Ordinal);
        var end = result.Output.IndexOf("---END---", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, result.Output);
        var sql = result.Output[start..end];
        Assert.Contains("DECLARE @identity nvarchar(200)=SUSER_SNAME();", sql, StringComparison.Ordinal);
        Assert.Contains("role_code='OWNER' AND is_active=1", sql, StringComparison.Ordinal);
        Assert.Contains("permission_name=N'ALTER ANY LOGIN' AND state='W'", sql, StringComparison.Ordinal);
        foreach (var word in new[] { "'NOT_AN_OWNER'", "'GRANT_OPTION'", "'MISSING'" })
            Assert.Contains(word, sql, StringComparison.Ordinal);
        // Read-only.
        foreach (var verb in new[] { "GRANT ", "REVOKE ", "DENY ", "CREATE ", "EXEC", "INSERT", "UPDATE", "DELETE", "MERGE" })
            Assert.DoesNotContain(verb, sql.Replace("GRANT OPTION", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pre_migration_backup_installs_a_missing_broker_first_and_only_after_the_last_refusal()
    {
        // The backup goes through the master broker. A database restored by hand, or after the
        // restore helper's broker step failed, reached setup without one, and the upgrade
        // stopped with only "The database operation failed".
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'bootstrap-etp-prerequisites.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            $upgrade = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$databaseExistedBeforeMigration' })
            if ($upgrade.Count -ne 1) { throw 'There is no single existing-database branch.' }
            $pending = @($upgrade[0].Clauses[0].Item2.Statements | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -match '\$appliedMigrationCount -lt' })
            if ($pending.Count -ne 1) { throw 'There is no single pending-migrations branch.' }
            $body = $pending[0].Clauses[0].Item2
            $isBroker = { param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.Extent.Text -match 'install-etp-sql-operations\.ps1' -and @($node.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -eq 'BrokerOnly' }).Count -eq 1 }
            $broker = @($body.FindAll($isBroker, $true))
            if ($broker.Count -ne 1) { throw 'The pending-migrations branch does not install the broker, and only the broker.' }
            $backup = @($body.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.Extent.Text -match '^& \$backupScript ' }, $true))
            if ($backup.Count -ne 1 -or $broker[0].Extent.EndOffset -gt $backup[0].Extent.StartOffset) { throw 'The broker is not in place before the pre-migration backup.' }
            # The encrypted-edition refusal promises the database has not been changed; the
            # broker must not have been installed by then.
            $refusals = @($body.FindAll({ param($node) $node -is [System.Management.Automation.Language.ThrowStatementAst] }, $true))
            if ($refusals.Count -lt 1 -or @($refusals | Where-Object { $_.Extent.StartOffset -gt $broker[0].Extent.StartOffset }).Count -ne 0) { throw 'The broker is installed before a refusal that says nothing was changed.' }
            $allBroker = @($ast.FindAll($isBroker, $true))
            if ($allBroker.Count -ne 2) { throw "The broker is installed from $($allBroker.Count) places; expected restore mode and the pre-migration backup." }
            Write-Output 'Broker before backup passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Broker before backup passed.", result.Output);
    }

    [Fact]
    public async Task Pre_migration_steps_run_in_order_and_only_the_backup_can_stop_setup()
    {
        // VM rehearsal, 3 October 2026 (1.9.2 with real data to 1.9.3): the broker refresh
        // before the pre-migration backup failed, and setup stopped there with two log lines
        // although the 1.9.2 broker could still take the backup. The order is: say what is
        // happening, refresh the broker (logged, never fatal), take the backup (fatal: nothing
        // may migrate without it), verify its receipt, log where it is.
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'bootstrap-etp-prerequisites.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            $upgrade = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$databaseExistedBeforeMigration' })
            $pending = @($upgrade[0].Clauses[0].Item2.Statements | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -match '\$appliedMigrationCount -lt' })
            if ($pending.Count -ne 1) { throw 'There is no single pending-migrations branch.' }
            $statements = @($pending[0].Clauses[0].Item2.Statements)
            function At($pattern) {
                $found = @(for ($i = 0; $i -lt $statements.Count; $i++) { if ($statements[$i].Extent.Text -match $pattern) { $i } })
                if ($found.Count -ne 1) { throw "Expected one statement matching $pattern, found $($found.Count)." }
                return $found[0]
            }
            $announce = At "Write-SetupLog 'Existing database has pending bundled migrations"
            $refresh = At '^foreach \(\$line in @\(Invoke-EtpPreMigrationBrokerRefresh -Refresh \{ & \(Join-Path \$scripts ''install-etp-sql-operations\.ps1''\) .* -BrokerOnly \}\)\) \{ Write-SetupLog "\$line" \}$'
            $backup = At '^& \$backupScript .*-Purpose PreMigration$'
            $verify = At '^Assert-VerifiedBackupReceipt -ReceiptPath \$receiptPath$'
            $retained = At 'Write-SetupLog "Verified pre-migration backup is retained at'
            if (-not ($announce -lt $refresh -and $refresh -lt $backup -and $backup -lt $verify -and $verify -lt $retained)) { throw "Out of order: $announce $refresh $backup $verify $retained" }
            # The backup is a statement of the branch itself, inside no try: its failure stops setup.
            if (@($pending[0].Clauses[0].Item2.FindAll({ param($n) $n -is [System.Management.Automation.Language.TryStatementAst] }, $true)).Count -ne 0) { throw 'Something in the pending-migrations branch is caught.' }
            # The trap puts what SQL Server reported on its own line after the FAILED line.
            $trap = @($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.TrapStatementAst] }, $true))
            if ($trap.Count -ne 1) { throw 'There is no single trap.' }
            $body = $trap[0].Body.Extent.Text
            $failed = $body.IndexOf('Write-SetupLog "FAILED:'); $detail = $body.IndexOf('Get-EtpExceptionSqlDetail $_.Exception'); $reported = $body.IndexOf('Write-SetupLog "SQL Server reported: $sqlDetail"')
            if (-not ($failed -ge 0 -and $detail -gt $failed -and $reported -gt $detail)) { throw 'The trap does not log the SQL detail after the FAILED line.' }
            Write-Output 'Pre-migration order passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Pre-migration order passed.", result.Output);
    }

    [Fact]
    public async Task A_broker_refresh_that_fails_is_logged_with_what_sql_server_said_and_the_backup_goes_ahead()
    {
        // Case (a) of the VM rehearsal: an unsigned 1.9.2 broker that setup tries to replace.
        // The stand-in refresh fails exactly as install-etp-sql-operations.ps1 did there: the
        // masked message, with Sqlcmd's own complaint attached by Invoke-EtpSql.
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            # A refresh that works: its lines pass through unchanged.
            $ok = @(Invoke-EtpPreMigrationBrokerRefresh -Refresh { 'The unsigned operations broker for EtpReporting was from an earlier build and has been replaced by the current one.' })
            if ($ok.Count -ne 1 -or $ok[0] -cne 'The unsigned operations broker for EtpReporting was from an earlier build and has been replaced by the current one.') { throw "Success: $($ok -join ' / ')" }
            # A signed 1.9.2 broker (case b) is kept by -BrokerOnly; its line passes through too.
            $kept = @(Invoke-EtpPreMigrationBrokerRefresh -Refresh { 'The operations broker for EtpReporting is signed but from an earlier build, which records no row counts.' })
            if ($kept.Count -ne 1 -or $kept[0] -notlike '*signed but from an earlier build*') { throw "Kept: $($kept -join ' / ')" }
            # The failure of 3 October 2026: logged, with the detail, and not thrown.
            $sqlcmdSaid = "Sqlcmd: 'before" + [char]34 + ":'+COALESCE(@countsBefore,N'null')': Unexpected argument. Enter '-?' for help."
            $lines = @(Invoke-EtpPreMigrationBrokerRefresh -Refresh {
                $failure = [Management.Automation.RuntimeException]::new($EtpMaskedSqlFailure)
                $failure.Data['EtpSqlDetail'] = $sqlcmdSaid
                throw $failure
            })
            if ($lines.Count -ne 1) { throw "Lines: $($lines.Count)" }
            $line = $lines[0]
            if (-not $line.StartsWith('WARNING: the operations broker could not be checked or brought up to date before the pre-migration backup: ')) { throw "Line: $line" }
            if (-not $line.Contains("$EtpMaskedSqlFailure (SQL Server reported: $sqlcmdSaid)")) { throw "No SQL detail: $line" }
            if (-not $line.Contains('The backup goes ahead through the broker already installed')) { throw "Line: $line" }
            # Any other failure (a refused template, say) is logged the same way, without a detail.
            $other = @(Invoke-EtpPreMigrationBrokerRefresh -Refresh { throw 'Complete protected recovery-folder setup first.' })
            if ($other.Count -ne 1 -or -not $other[0].Contains('Complete protected recovery-folder setup first.') -or $other[0].Contains('SQL Server reported')) { throw "Other: $($other -join ' / ')" }
            Write-Output 'Broker refresh failure handling passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Broker refresh failure handling passed.", result.Output);
    }

    [Fact]
    public async Task Fresh_machine_without_bundled_media_refuses_before_changing_anything()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $global:calls = [Collections.Generic.List[string]]::new()
            function Assert-EtpBootstrapAdministrator { $global:calls.Add('admin') }
            function Assert-EtpBootstrapPayloads { param($ApplicationDirectory) $global:calls.Add('payloads') }
            function Install-EtpSqlPrerequisitesFromPayload { param($PayloadDirectory, $ServiceName) $global:calls.Add('sql') }
            function Invoke-EtpOperationFolderSetup { param($ServiceName, $ServerInstance, $Database, $AutomationPrincipal, [switch]$CreateAutomationAccount) $global:calls.Add('folders') }
            $refusals = @()
            foreach ($case in @('no media', 'option unticked')) {
                try {
                    if ($case -eq 'no media') { Initialize-EtpFreshMachine -ApplicationRoot 'C:\Unused' }
                    else { Initialize-EtpFreshMachine -ApplicationRoot 'C:\Unused' -SqlPayloadDirectory 'C:\UnusedMedia' -SkipSqlInstallation }
                    $refusals += "ACCEPTED: $case"
                }
                catch { $refusals += $_.Exception.Message }
            }
            foreach ($refusal in $refusals) {
                if ($refusal -notlike '*Install Microsoft SQL Server 2025 Express*' -or $refusal -notlike '*Nothing was changed*') { throw "Unexpected result: $refusal" }
            }
            if (@($global:calls | Where-Object { $_ -in @('sql', 'folders') }).Count -ne 0) { throw "Something changed: $($global:calls -join ' / ')" }
            Write-Output 'Fresh machine without media refused.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Fresh machine without media refused.", result.Output);
    }

    [Fact]
    public async Task Missing_configuration_with_bundled_media_still_changes_nothing_without_elevation()
    {
        // The real script end to end, as setup starts it on a new PC with the option ticked.
        // Unelevated it stops at the administrator check; elevated, at the ownership check on a
        // user-writable installation folder. Either way before SQL Server or any folder.
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapBoundary", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var programData = Directory.CreateDirectory(Path.Combine(root, "ProgramData")).FullName;
            var application = Directory.CreateDirectory(Path.Combine(root, "Application")).FullName;
            var media = Directory.CreateDirectory(Path.Combine(root, "Media")).FullName;
            foreach (var name in FullMedia) await File.WriteAllTextAsync(Path.Combine(media, name), "Disposable media marker " + name);
            var result = await RunPowerShellAsync(["-File", FindBootstrapScript(), "-ApplicationDirectory", application, "-SqlPayloadDirectory", media], programData);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Matches("administrator rights|owned by Administrators or SYSTEM|non-administrator", result.Output);
            Assert.Empty(Directory.EnumerateFileSystemEntries(programData, "*", SearchOption.AllDirectories));
            foreach (var name in FullMedia) Assert.Equal("Disposable media marker " + name, await File.ReadAllTextAsync(Path.Combine(media, name)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Restore_mode_waits_only_when_the_database_is_missing()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $checks = 0
            foreach ($case in @(@('MISSING', $false, 'Create'), @('MISSING', $true, 'AwaitRestore'), @('EXISTS', $false, 'Upgrade'), @('EXISTS', $true, 'Upgrade'))) {
                $action = Get-EtpBootstrapDatabaseAction -DatabaseState $case[0] -DeferDatabaseCreation:$case[1]
                if ($action -cne $case[2]) { throw "$($case[0]) with defer=$($case[1]) gave $action" }
                $checks++
            }
            foreach ($answer in @('exists', 'missing', '', 'EXISTS ')) {
                $refused = $false
                try { Get-EtpBootstrapDatabaseAction -DatabaseState $answer -DeferDatabaseCreation | Out-Null } catch { $refused = $_.Exception.Message -like '*unexpected database-existence result*' }
                if (-not $refused) { throw "An unreadable answer was accepted: '$answer'" }
                $checks++
            }
            if ($DatabaseRestorePendingExitCode -ne 2) { throw 'The restore-pending exit code changed.' }
            if ($checks -ne 8) { throw 'Not every case ran.' }
            Write-Output 'Restore mode decisions passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Restore mode decisions passed.", result.Output);
    }

    // The bundled Sqlcmd (Command Line Utilities 15) stops with error 26010 without ODBC
    // Driver 17, and the SQL Server 2025 media's msodbcsql.msi is Driver 18. The old wildcard
    // msodbcsql*.msi took that one, so Sqlcmd could never install.
    [Fact]
    public async Task Sql_client_plan_puts_odbc17_before_sqlcmd_and_never_takes_driver18_for_17()
    {
        var media = NewPayload(FullMedia);
        try
        {
            var script = FindBootstrapScript().Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
                $media = '{{media.Replace("'", "''")}}'
                function Show-Plan([string[]]$Installed) { @(Get-EtpSqlClientInstallPlan -PayloadDirectory $media -InstalledOdbcDrivers $Installed | ForEach-Object { (Split-Path -Leaf $_.Path) + '=' + $_.Terms }) -join ',' }
                $odbc17 = 'msodbcsql17.msi=IACCEPTMSODBCSQLLICENSETERMS=YES'
                $odbc18 = 'msodbcsql.msi=IACCEPTMSODBCSQLLICENSETERMS=YES'
                $sqlcmd = 'MsSqlCmdLnUtils.msi=IACCEPTMSSQLCMDLNUTILSLICENSETERMS=YES'
                $plan = Show-Plan @()
                if ($plan -cne "$odbc17,$odbc18,$sqlcmd") { throw "No drivers installed: $plan" }
                $plan = Show-Plan @('ODBC Driver 17 for SQL Server')
                if ($plan -cne "$odbc18,$sqlcmd") { throw "ODBC 17 installed: $plan" }
                $plan = Show-Plan @('ODBC Driver 17 for SQL Server', 'ODBC Driver 18 for SQL Server', 'SQL Server')
                if ($plan -cne $sqlcmd) { throw "Both drivers installed: $plan" }
                Remove-Item -LiteralPath (Join-Path $media 'msodbcsql17.msi')
                $refused = $false
                try { Show-Plan @('ODBC Driver 18 for SQL Server') | Out-Null } catch { $refused = $_.Exception.Message -like '*ODBC Driver 17*Nothing was installed*' }
                if (-not $refused) { throw 'Media without ODBC Driver 17 was accepted for a PC without it.' }
                $plan = Show-Plan @('ODBC Driver 17 for SQL Server')
                if ($plan -cne "$odbc18,$sqlcmd") { throw "ODBC 17 already installed, media without it: $plan" }
                Remove-Item -LiteralPath (Join-Path $media 'MsSqlCmdLnUtils.msi')
                $refused = $false
                try { Show-Plan @('ODBC Driver 17 for SQL Server') | Out-Null } catch { $refused = $_.Exception.Message -like '*Sqlcmd*Nothing was installed*' }
                if (-not $refused) { throw 'Media without Sqlcmd was accepted.' }
                Write-Output 'Client install plan passed.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Client install plan passed.", result.Output);
        }
        finally { Directory.Delete(media, recursive: true); }
    }

    // SQL Server 2025's own setup installs an ODBC 18 Sqlcmd, which encrypts by default and
    // refuses the new instance's self-signed certificate. On the first real new PC (1 October
    // 2026) setup counted it as "Sqlcmd installed", skipped the bundled Sqlcmd 15, resolved the
    // ODBC 18 one and failed its first query. The ODBC 17 Sqlcmd must win, and only it counts.
    [Fact]
    public async Task Odbc17_sqlcmd_is_preferred_and_an_odbc18_sqlcmd_alone_does_not_count_as_installed()
    {
        var programFiles = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpSqlCmdChoice", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var script = FindBootstrapScript().Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
                function Assert-EtpProtectedInstall { param($Path) }
                $env:ProgramFiles = '{{programFiles.Replace("'", "''")}}'
                # Only the test's folders: not this PC's own registered or other-drive Sqlcmd.
                $global:registered = @()
                function Get-EtpRegisteredSqlCmdFolders { @($global:registered) }
                function Get-EtpProgramFilesFolders { @($env:ProgramFiles) }
                function Add-SqlCmd([string]$Odbc,[string]$Root = $env:ProgramFiles) {
                    $folder = Join-Path $Root "Microsoft SQL Server\Client SDK\ODBC\$Odbc\Tools\Binn"
                    $null = New-Item -ItemType Directory -Force -Path $folder
                    Set-Content -LiteralPath (Join-Path $folder 'SQLCMD.EXE') -Value 'marker'
                    return (Join-Path $folder 'SQLCMD.EXE')
                }
                $odbc18 = Add-SqlCmd '180'
                if (Test-EtpSqlCmdInstalled) { throw 'The ODBC 18 Sqlcmd alone counted as installed.' }
                if ((Resolve-EtpSqlCmd) -ne $odbc18) { throw 'The ODBC 18 Sqlcmd is no longer the fallback.' }
                # An ODBC 17 Sqlcmd registered on another drive counts, and beats the ODBC 18 one.
                $otherDrive = Add-SqlCmd '170' (Join-Path $env:ProgramFiles 'OtherDrive')
                $global:registered = @([IO.Path]::GetDirectoryName($otherDrive))
                if (-not (Test-EtpSqlCmdInstalled)) { throw 'The ODBC 17 Sqlcmd on another drive did not count as installed.' }
                if ((Resolve-EtpSqlCmd) -ne $otherDrive) { throw 'The ODBC 17 Sqlcmd on another drive was not preferred.' }
                $global:registered = @()
                $odbc17 = Add-SqlCmd '170'
                if (-not (Test-EtpSqlCmdInstalled)) { throw 'The ODBC 17 Sqlcmd did not count as installed.' }
                $resolved = Resolve-EtpSqlCmd
                if ($resolved -ne $odbc17) { throw "The ODBC 17 Sqlcmd was not preferred: $resolved" }
                Write-Output 'Sqlcmd choice passed.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Sqlcmd choice passed.", result.Output);
        }
        finally { Directory.Delete(programFiles, recursive: true); }
    }

    [Fact]
    public async Task Bundled_media_installs_the_engine_then_odbc17_odbc18_and_sqlcmd_with_no_restart()
    {
        var media = NewPayload(FullMedia);
        try
        {
            var result = await RunPrerequisiteHarnessAsync(media);
            Assert.True(result.ExitCode == 0, result.Output);
            var calls = CallsFrom(result.Output);
            string Msi(string file, string terms) => $"msiexec.exe /i {Path.Combine(media, file)} /qn /norestart ADDLOCAL=ALL {terms}";
            Assert.Equal(
                [
                    "engine(MSSQL$EtpTestNoSuchInstance9f)",
                    Msi("msodbcsql17.msi", "IACCEPTMSODBCSQLLICENSETERMS=YES"),
                    Msi("msodbcsql.msi", "IACCEPTMSODBCSQLLICENSETERMS=YES"),
                    Msi("MsSqlCmdLnUtils.msi", "IACCEPTMSSQLCMDLNUTILSLICENSETERMS=YES"),
                ],
                calls);
        }
        finally { Directory.Delete(media, recursive: true); }
    }

    [Fact]
    public async Task Media_without_odbc17_stops_before_sql_server_is_installed()
    {
        var media = NewPayload([.. FullMedia.Where(x => x != "msodbcsql17.msi")]);
        try
        {
            var result = await RunPrerequisiteHarnessAsync(media);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("REFUSED:", result.Output);
            Assert.Contains("ODBC Driver 17", result.Output);
            Assert.Empty(CallsFrom(result.Output));
        }
        finally { Directory.Delete(media, recursive: true); }
    }

    [Fact]
    public async Task Bundled_media_in_a_user_writable_folder_is_refused_before_anything_runs()
    {
        // Setup used to copy the media to its {tmp} under the user profile, which this very
        // check refuses, so the option failed on every interactive install.
        var media = NewPayload(FullMedia);
        try
        {
            var script = FindBootstrapScript().Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
                $global:calls = [Collections.Generic.List[string]]::new()
                function Install-EtpSqlEngineFromPayload { param($PayloadDirectory, $ServiceName) $global:calls.Add('engine'); throw 'The engine must not be installed from this folder.' }
                function Start-EtpProcess { param($FilePath, [string[]]$Arguments, $Description) $global:calls.Add('process'); throw 'Nothing may run from this folder.' }
                $refused = $false
                try { Install-EtpSqlPrerequisitesFromPayload -PayloadDirectory '{{media.Replace("'", "''")}}' -ServiceName 'MSSQL$EtpTestNoSuchInstance9f' }
                catch { $refused = $_.Exception.Message -match 'owned by Administrators or SYSTEM|non-administrator' }
                if (-not $refused) { throw 'Media in a user-writable folder was accepted.' }
                if ($global:calls.Count -ne 0) { throw "Something ran: $($global:calls -join ' / ')" }
                Write-Output 'User-writable media refused.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("User-writable media refused.", result.Output);
            foreach (var name in FullMedia) Assert.Equal("Disposable media marker " + name, await File.ReadAllTextAsync(Path.Combine(media, name)));
        }
        finally { Directory.Delete(media, recursive: true); }
    }

    [Fact]
    public async Task Bundled_sql_engine_keeps_the_hardened_parameters_and_the_recorded_collation()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $setupArguments = @(Get-EtpSqlEngineSetupArguments 'SQLEXPRESS')
            $expected = @('/ACTION=Install', '/QUIET', '/IACCEPTSQLSERVERLICENSETERMS', '/FEATURES=SQLENGINE', '/INSTANCENAME=SQLEXPRESS',
                '/SQLSYSADMINACCOUNTS=BUILTIN\Administrators', '/ADDCURRENTUSERASSQLADMIN=False', '/SQLCOLLATION=Latin1_General_CI_AS', '/TCPENABLED=0', '/NPENABLED=0', '/UPDATEENABLED=0')
            if ((@($setupArguments | Sort-Object) -join ' ') -cne (@($expected | Sort-Object) -join ' ')) { throw "SQL Server setup arguments changed: $($setupArguments -join ' ')" }
            if (@($setupArguments | Where-Object { $_ -match '^/(SECURITYMODE|SAPWD)' }).Count -ne 0) { throw 'SQL authentication was enabled.' }
            foreach ($instance in @('bad name;x', 'SQLEXPRESS /SECURITYMODE=SQL', 'NAMEWITHMORETHAN16')) {
                $refused = $false
                try { Get-EtpSqlEngineSetupArguments $instance | Out-Null } catch { $refused = $true }
                if (-not $refused) { throw "An unsafe instance name was accepted: $instance" }
            }
            Write-Output 'SQL Server setup arguments passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("SQL Server setup arguments passed.", result.Output);
    }

    [Fact]
    public async Task Sql_setup_media_is_never_extracted_into_the_user_profile()
    {
        // The extracted setup.exe runs with the administrator's full token. It used to be
        // extracted into the user's own Temp folder, which the user's unelevated processes can
        // write to. Checked structurally: running it needs the real media and elevation.
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'bootstrap-etp-prerequisites.ps1 does not parse.' }
            $temp = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Member.Extent.Text -eq 'GetTempPath' }, $true))
            if ($temp.Count -ne 0) { throw 'Bootstrap uses the user Temp folder again.' }
            $functions = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Install-EtpSqlEngineFromPayload' }, $true))
            if ($functions.Count -ne 1) { throw 'The engine installer function is missing.' }
            $protected = @($functions[0].FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'New-EtpProtectedDirectory' }, $true))
            if ($protected.Count -ne 1) { throw 'The SQL Server media is not extracted into a protected folder.' }
            $extracts = @($functions[0].FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.Extent.Text -match "'extract the SQL Server media'" }, $true))
            if ($extracts.Count -ne 1 -or $extracts[0].Extent.StartOffset -lt $protected[0].Extent.EndOffset) { throw 'The media is extracted before its protected folder exists.' }
            Write-Output 'Protected extraction passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Protected extraction passed.", result.Output);
    }

    [Fact]
    public async Task Bootstrap_prepares_a_new_pc_before_reading_its_configuration_and_stops_for_a_restore_before_creating_a_database()
    {
        // The top-level flow needs SQL Server and elevation to run, so its order is checked
        // structurally: the new-PC step comes before the configuration is read, and restore
        // mode installs only the broker and exits before the migration step can create a
        // database that a later restore would have to replace.
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'bootstrap-etp-prerequisites.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            $fresh = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -match '^Test-EtpNewMachine ' })
            if ($fresh.Count -ne 1) { throw 'There is no single new-PC branch.' }
            $prepares = @($fresh[0].FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Initialize-EtpFreshMachine' }, $true))
            if ($prepares.Count -ne 1) { throw 'The new-PC branch does not prepare the machine.' }
            $reads = @($top | Where-Object { $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -eq '$operationConfiguration' })
            if ($reads.Count -ne 1 -or $reads[0].Extent.StartOffset -lt $fresh[0].Extent.EndOffset) { throw 'The configuration is read before a new PC is prepared.' }
            $restore = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$databaseAction -ceq ''AwaitRestore''' })
            if ($restore.Count -ne 1) { throw 'There is no single restore-mode branch.' }
            $broker = @($restore[0].FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.Extent.Text -match 'install-etp-sql-operations\.ps1' -and @($node.CommandElements | Where-Object { $_ -is [System.Management.Automation.Language.CommandParameterAst] -and $_.ParameterName -eq 'BrokerOnly' }).Count -eq 1 }, $true))
            if ($broker.Count -ne 1) { throw 'Restore mode does not install the broker, and only the broker.' }
            $exits = @($restore[0].FindAll({ param($node) $node -is [System.Management.Automation.Language.ExitStatementAst] -and $node.Pipeline.Extent.Text -eq '$DatabaseRestorePendingExitCode' }, $true))
            if ($exits.Count -ne 1) { throw 'Restore mode does not exit with the restore-pending code.' }
            $migrationStart = @($top | Where-Object { $_.Extent.Text -eq '$migrationPhaseStarted = $true' })
            $creates = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $node.Value -eq '--initialize-configured-database' }, $true))
            if ($migrationStart.Count -ne 1 -or $creates.Count -ne 1) { throw 'The migration step could not be found.' }
            if ($restore[0].Extent.EndOffset -gt $migrationStart[0].Extent.StartOffset -or $restore[0].Extent.EndOffset -gt $creates[0].Extent.StartOffset) { throw 'Restore mode stops after the migration step.' }
            Write-Output 'Bootstrap order passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Bootstrap order passed.", result.Output);
    }

    [Fact]
    public async Task Protected_work_folder_is_never_an_existing_folder_or_a_path_through_a_link()
    {
        // SQL Server's setup and the backup being restored are placed in a folder created with
        // its protection in one step. A folder that is already there, or a path through a
        // junction, could be someone else's and is refused before anything is written.
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpProtectedFolder", Guid.NewGuid().ToString("N"))).FullName;
        var link = Path.Combine(root, "link");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var common = Path.Combine(Path.GetDirectoryName(FindBootstrapScript())!, "etp-operations-common.ps1").Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{common}}'
                $root = '{{root.Replace("'", "''")}}'
                $null = New-Item -ItemType Junction -Path (Join-Path $root 'link') -Target (Join-Path $root 'target')
                try {
                    $existing = $null
                    try { New-EtpProtectedDirectory -Path (Join-Path $root 'target') | Out-Null } catch { $existing = $_.Exception.Message }
                    if ($existing -notlike '*already exists*') { throw "An existing folder was reused: $existing" }
                    $linked = $null
                    try { New-EtpProtectedDirectory -Path (Join-Path $root 'link\work') | Out-Null } catch { $linked = $_.Exception.Message }
                    if ($linked -notlike '*Linked operation paths*') { throw "A path through a link was used: $linked" }
                }
                finally { [IO.Directory]::Delete((Join-Path $root 'link')) }
                Write-Output 'Protected work folder refusals passed.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Protected work folder refusals passed.", result.Output);
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    private static readonly string[] FullMedia = ["SQLEXPR_x64_ENU.exe", "msodbcsql.msi", "msodbcsql17.msi", "MsSqlCmdLnUtils.msi", "SqlLocalDB.msi"];

    private static string NewPayload(string[] names)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapMedia", Guid.NewGuid().ToString("N"))).FullName;
        foreach (var name in names) File.WriteAllText(Path.Combine(directory, name), "Disposable media marker " + name);
        return directory;
    }

    // Everything that would touch the machine is replaced by a recorder; the order and the
    // exact command lines are what is checked. The ownership check is replaced too, because
    // a test folder cannot be administrator-owned; it has a test of its own above.
    private static Task<(int ExitCode, string Output)> RunPrerequisiteHarnessAsync(string media)
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $global:sqlcmdInstalled = $false
            function Assert-EtpProtectedInstall { param($Path) }
            function Assert-EtpNoLinks { param($Path) }
            function Install-EtpSqlEngineFromPayload { param($PayloadDirectory, $ServiceName) Write-Output "CALL:engine($ServiceName)" }
            function Get-EtpInstalledOdbcDrivers { @() }
            function Test-EtpSqlCmdInstalled { $global:sqlcmdInstalled }
            function Start-EtpProcess {
                param($FilePath, [string[]]$Arguments, $Description)
                Write-Output ('CALL:' + (Split-Path -Leaf $FilePath) + ' ' + ($Arguments -join ' '))
                if (($Arguments -join ' ') -like '*MsSqlCmdLnUtils.msi*') { $global:sqlcmdInstalled = $true }
            }
            try { Install-EtpSqlPrerequisitesFromPayload -PayloadDirectory '{{media.Replace("'", "''")}}' -ServiceName 'MSSQL$EtpTestNoSuchInstance9f' }
            catch { Write-Output ('REFUSED:' + $_.Exception.Message) }
            """;
        return RunPowerShellAsync(["-Command", command]);
    }

    private static string[] CallsFrom(string output) =>
        [.. output.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.StartsWith("CALL:", StringComparison.Ordinal)).Select(x => x["CALL:".Length..])];

    private static string FindBootstrapScript()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "scripts", "bootstrap-etp-prerequisites.ps1");
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string[] arguments, string? programData = null)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(arguments)) start.ArgumentList.Add(argument);
        if (programData is not null) start.Environment["ProgramData"] = programData;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
