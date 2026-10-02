using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.3, Sagar's decision of 2 October 2026: setup (and the restore helper) give active Owners
/// ALTER ANY LOGIN WITH GRANT OPTION themselves, so Settings > Users works unelevated right after
/// install. SQL Server never lets the account running setup grant itself a permission (4627), so
/// a different SQL administrator, SYSTEM, does it through a one-off scheduled task: the method
/// proven by hand on Workpc. These tests never create a real task: every Task Scheduler cmdlet
/// is replaced by a recorder, and module auto-loading is off so a missed one cannot reach Windows.
/// </summary>
public sealed class OwnerGrantSystemTaskTests
{
    [Fact]
    public async Task The_system_batch_is_fixed_text_that_grants_only_the_grant_option_to_active_owner_user_logins()
    {
        var common = FindScript("etp-operations-common.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{common}}'
            foreach ($name in @('New-EtpOwnerGrantOptionSql', 'New-EtpOwnersWithoutGrantOptionSql', 'New-EtpSystemSqlAdministratorCheckSql')) {
                $own = @((Get-Command $name).Parameters.Keys | Where-Object { $_ -notin [System.Management.Automation.PSCmdlet]::CommonParameters -and $_ -notin [System.Management.Automation.PSCmdlet]::OptionalCommonParameters })
                if ($own.Count -ne 0) { throw "$name takes input: $($own -join ', ')" }
            }
            Write-Output '---GRANT---'; New-EtpOwnerGrantOptionSql
            Write-Output '---COUNT---'; New-EtpOwnersWithoutGrantOptionSql
            Write-Output '---ADMIN---'; New-EtpSystemSqlAdministratorCheckSql
            Write-Output '---END---'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        var grant = Between(result.Output, "---GRANT---", "---COUNT---");
        var count = Between(result.Output, "---COUNT---", "---ADMIN---");
        var admin = Between(result.Output, "---ADMIN---", "---END---");

        // Nothing is granted unless SYSTEM really is a SQL administrator, and then only to the
        // logins of active Owner rows, chosen inside SQL Server, each name through QUOTENAME.
        string[] ordered =
        [
            "SET NOCOUNT ON; SET XACT_ABORT ON;",
            "IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1",
            "SELECT N'ETP_GRANT_SYSADMIN:0';",
            "RETURN;",
            "SELECT N'ETP_GRANT_SYSADMIN:1';",
            "IF OBJECT_ID(N'dbo.application_users',N'U') IS NULL",
            "INSERT @owners(login_name)",
            "WHERE sp.type='U' AND sp.sid<>SUSER_SID()",
            "sp.sid NOT IN(SID_BINARY(N'S-1-5-18'),SID_BINARY(N'S-1-5-19'),SID_BINARY(N'S-1-5-20'))",
            "SUBSTRING(sp.sid,3,10)<>SUBSTRING(SID_BINARY(N'S-1-5-80-0'),3,10)",
            "FROM dbo.application_users u WHERE u.role_code='OWNER' AND u.is_active=1",
            "p.permission_name=N'ALTER ANY LOGIN' AND p.state='W'",
            "SELECT @grants+=N'GRANT ALTER ANY LOGIN TO '+QUOTENAME(login_name)+N' WITH GRANT OPTION;' FROM @owners;",
            "SET @grants=N'USE [master]; '+@grants;",
            "EXEC(@grants);",
            "SELECT N'ETP_GRANT_GRANTED:'",
            "SELECT N'ETP_GRANT_MISSING:'",
        ];
        var position = -1;
        foreach (var fragment in ordered)
        {
            var next = grant.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(next > position, $"'{fragment}' is missing or out of order.");
            position = next;
        }
        Assert.Equal(1, Occurrences(grant, "GRANT ALTER ANY LOGIN TO "));
        Assert.Equal(1, Occurrences(grant, "EXEC("));
        foreach (var forbidden in new[] { "REVOKE", "DENY ", "CREATE ", "DROP ", "ALTER ROLE", "ALTER LOGIN", "ALTER SERVER", "EXECUTE AS", "CONTROL SERVER", "sp_add", "UPDATE ", "DELETE ", "MERGE ", "$(", "INSERT dbo" })
            Assert.DoesNotContain(forbidden, grant, StringComparison.OrdinalIgnoreCase);
        // The probe that decides whether to run the task counts exactly the logins the task
        // grants to (without the SYSTEM exclusion, which is the task's own SID), read-only.
        Assert.Contains("WHERE sp.type='U'", count, StringComparison.Ordinal);
        Assert.Contains("SID_BINARY(N'S-1-5-80-0')", count, StringComparison.Ordinal);
        Assert.Contains("u.role_code='OWNER' AND u.is_active=1", count, StringComparison.Ordinal);
        Assert.Contains("p.state='W'", count, StringComparison.Ordinal);
        Assert.Contains("WHERE r.name=N'sysadmin' AND m.is_disabled=0 AND m.sid IN(SID_BINARY(N'S-1-5-18'),SID_BINARY(N'S-1-5-32-544'))", admin, StringComparison.Ordinal);
        foreach (var readOnly in new[] { count, admin })
            foreach (var verb in new[] { "GRANT", "REVOKE", "DENY", "CREATE", "ALTER", "EXEC", "INSERT", "UPDATE", "DELETE", "MERGE" })
                Assert.DoesNotMatch($@"(?i)\b{verb}\b", readOnly.Replace("N'ALTER ANY LOGIN'", "", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_command_file_carries_only_checked_values_and_the_output_is_read_by_its_markers()
    {
        var common = FindScript("etp-operations-common.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{common}}'
            function Assert-Refused([scriptblock]$Action, [string]$Pattern) {
                $message = $null
                try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
                if ($null -eq $message) { throw "Accepted, but should have been refused with: $Pattern" }
                if ($message -notmatch $Pattern) { throw "Refused with '$message', expected: $Pattern" }
            }
            $sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
            $work = 'C:\ProgramData\EtpReporting\OwnerGrant-0123456789abcdef0123456789abcdef'
            $text = New-EtpOwnerGrantCommandScript -SqlCmdPath $sqlcmd -ServerInstance 'lpc:.\SQLEXPRESS' -Database 'EtpReporting' -SqlPath "$work\grant-owner-option.sql" -OutputPath "$work\grant-owner-option.out"
            Write-Output '---CMD---'; Write-Output $text; Write-Output '---END---'
            $plain = 'plain local folder and program paths'
            foreach ($bad in @('C:\Tools\%PATH%\sqlcmd.exe', 'C:\Tools\a"b\sqlcmd.exe', 'C:\Tools\a&calc\sqlcmd.exe', 'C:\Tools\a^b\sqlcmd.exe', 'C:\Tools\a!b!\sqlcmd.exe',
                               'C:\Tools\a|b\sqlcmd.exe', 'C:\Tools\a<b\sqlcmd.exe', '\\server\share\sqlcmd.exe', 'sqlcmd.exe', ('C:\T' + [char]0xF6 + 'ols\sqlcmd.exe'), "C:\a`r`nb\sqlcmd.exe")) {
                Assert-Refused { New-EtpOwnerGrantCommandScript -SqlCmdPath $bad -ServerInstance '.\SQLEXPRESS' -Database 'EtpReporting' -SqlPath "$work\a.sql" -OutputPath "$work\a.out" } $plain
                Assert-Refused { New-EtpOwnerGrantCommandScript -SqlCmdPath $sqlcmd -ServerInstance '.\SQLEXPRESS' -Database 'EtpReporting' -SqlPath $bad -OutputPath "$work\a.out" } $plain
                Assert-Refused { New-EtpOwnerGrantCommandScript -SqlCmdPath $sqlcmd -ServerInstance '.\SQLEXPRESS' -Database 'EtpReporting' -SqlPath "$work\a.sql" -OutputPath $bad } $plain
            }
            foreach ($bad in @('Etp;DROP', 'Etp Reporting', 'Etp"x', 'Etp%x%')) {
                Assert-Refused { New-EtpOwnerGrantCommandScript -SqlCmdPath $sqlcmd -ServerInstance '.\SQLEXPRESS' -Database $bad -SqlPath "$work\a.sql" -OutputPath "$work\a.out" } 'valid database name'
            }
            foreach ($bad in @('remote\SQLEXPRESS', '.\SQLEXPRESS & calc', '.\SQL"EXPRESS', 'tcp:remote,1433')) {
                Assert-Refused { New-EtpOwnerGrantCommandScript -SqlCmdPath $sqlcmd -ServerInstance $bad -Database 'EtpReporting' -SqlPath "$work\a.sql" -OutputPath "$work\a.out" } 'instance on this computer'
            }

            function Outcome([string[]]$Lines) { $r = ConvertFrom-EtpOwnerGrantOutput -Lines $Lines; "$($r.Outcome)|$($r.Granted)|$($r.Missing)|$($r.Detail)" }
            $cases = [ordered]@{
                Granted = @('', 'ETP_GRANT_SYSADMIN:1', 'ETP_GRANT_GRANTED:1', 'ETP_GRANT_MISSING:0', 'ETP_GRANT_EXIT:0')
                AlreadyHeld = @('ETP_GRANT_SYSADMIN:1', 'ETP_GRANT_GRANTED:0', 'ETP_GRANT_MISSING:0', 'ETP_GRANT_EXIT:0')
                Incomplete = @('ETP_GRANT_SYSADMIN:1', 'ETP_GRANT_GRANTED:1', 'ETP_GRANT_MISSING:1', 'ETP_GRANT_EXIT:0')
                NotSysadmin = @('ETP_GRANT_SYSADMIN:0', 'ETP_GRANT_EXIT:0')
                LoginFailed = @("Sqlcmd: Error: Microsoft ODBC Driver 17 for SQL Server : Login failed for user 'NT AUTHORITY\SYSTEM'..", 'ETP_GRANT_EXIT:1')
                NoExit = @('ETP_GRANT_SYSADMIN:1', 'ETP_GRANT_GRANTED:1', 'ETP_GRANT_MISSING:0')
                Twice = @('ETP_GRANT_SYSADMIN:1', 'ETP_GRANT_GRANTED:1', 'ETP_GRANT_MISSING:0', 'ETP_GRANT_EXIT:0', 'ETP_GRANT_EXIT:0')
                NoUsers = @('ETP_GRANT_SYSADMIN:1', 'ETP_GRANT_USERS_TABLE:0', 'ETP_GRANT_EXIT:0')
                Unconfirmed = @('ETP_GRANT_GRANTED:1', 'ETP_GRANT_MISSING:0', 'ETP_GRANT_EXIT:0')
                Empty = @()
            }
            foreach ($case in $cases.Keys) { Write-Output ("PARSE:$case=" + (Outcome $cases[$case])) }

            $manual = Get-EtpOwnerGrantManualCommand -Identity 'WORKPC\Sagar' -ServerInstance '.\SQLEXPRESS' -SqlCmdPath $sqlcmd
            Write-Output "MANUAL:$manual"
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseInput($manual, [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'The manual command does not parse.' }
            $commands = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true) | ForEach-Object { $_.GetCommandName() } | Sort-Object -Unique)
            Write-Output ('MANUAL_COMMANDS:' + ($commands -join ','))
            $argument = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandParameterAst] -and $node.ParameterName -eq 'Argument' }, $true))
            if ($argument.Count -ne 1) { throw 'The manual command has no single -Argument.' }
            $value = $argument[0].Parent.CommandElements[$argument[0].Parent.CommandElements.IndexOf($argument[0]) + 1]
            Write-Output ('MANUAL_ARGUMENT:' + $value.Value)
            foreach ($identity in @('WORKPC\Sa]gar', "WORKPC\Sa'gar", 'Sagar', 'WORKPC\$(calc)', 'WORKPC\a;b', '', 'WORKPC\a"b')) {
                if ($null -ne (Get-EtpOwnerGrantManualCommand -Identity $identity -ServerInstance '.\SQLEXPRESS' -SqlCmdPath $sqlcmd)) { throw "A manual command was offered for $identity" }
            }
            if ($null -ne (Get-EtpOwnerGrantManualCommand -Identity 'WORKPC\Sagar' -ServerInstance '.\SQLEXPRESS; calc' -SqlCmdPath $sqlcmd)) { throw 'A manual command was offered for an odd instance.' }
            if ($null -ne (Get-EtpOwnerGrantManualCommand -Identity 'WORKPC\Sagar' -ServerInstance '.\SQLEXPRESS' -SqlCmdPath "C:\It's\sqlcmd.exe")) { throw 'A manual command was offered for an odd path.' }
            Write-Output 'Command file checks passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Command file checks passed.", result.Output);

        var cmd = Between(result.Output, "---CMD---", "---END---").Trim().Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        const string work = @"C:\ProgramData\EtpReporting\OwnerGrant-0123456789abcdef0123456789abcdef";
        Assert.Equal(
            [
                "@echo off",
                $"\"C:\\Program Files\\Microsoft SQL Server\\Client SDK\\ODBC\\170\\Tools\\Binn\\SQLCMD.EXE\" -x -S \"lpc:.\\SQLEXPRESS\" -E -b -h -1 -W -d EtpReporting -i \"{work}\\grant-owner-option.sql\" > \"{work}\\grant-owner-option.out\" 2>&1",
                "set ETPEXIT=%ERRORLEVEL%",
                $">> \"{work}\\grant-owner-option.out\" echo ETP_GRANT_EXIT:%ETPEXIT%",
                "exit /b %ETPEXIT%",
            ],
            cmd);

        var parsed = result.Output.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith("PARSE:", StringComparison.Ordinal))
            .ToDictionary(line => line[6..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..]);
        Assert.Equal("Granted|1|0|", parsed["Granted"]);
        Assert.Equal("AlreadyHeld|0|0|", parsed["AlreadyHeld"]);
        Assert.Equal("Incomplete|1|1|", parsed["Incomplete"]);
        Assert.StartsWith("NotSysadmin|", parsed["NotSysadmin"], StringComparison.Ordinal);
        Assert.Equal("Failed|0|-1|sqlcmd ended with exit code 1", parsed["LoginFailed"]);
        foreach (var failed in new[] { "NoExit", "Twice", "NoUsers", "Unconfirmed", "Empty" })
            Assert.StartsWith("Failed|", parsed[failed], StringComparison.Ordinal);
        // sqlcmd's own text, which names accounts, never reaches the result.
        Assert.DoesNotContain(parsed.Values, value => value.Contains("Login failed", StringComparison.Ordinal));

        Assert.Contains("MANUAL_COMMANDS:Get-ScheduledTaskInfo,New-ScheduledTaskAction,Out-Null,Register-ScheduledTask,Start-ScheduledTask,Start-Sleep,Unregister-ScheduledTask", result.Output, StringComparison.Ordinal);
        Assert.Contains("MANUAL_ARGUMENT:-S .\\SQLEXPRESS -E -b -d master -Q \"GRANT ALTER ANY LOGIN TO [WORKPC\\Sagar] WITH GRANT OPTION\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("-User 'NT AUTHORITY\\SYSTEM' -RunLevel Highest", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_one_off_task_runs_as_system_is_always_removed_with_its_files_and_never_stops_the_caller()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpOwnerGrantTask", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var common = FindScript("etp-operations-common.ps1").Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                # The modules the code under test really uses, then no more: a Task Scheduler
                # cmdlet this harness forgot to replace fails instead of reaching Windows.
                Import-Module Microsoft.PowerShell.Management, Microsoft.PowerShell.Utility, Microsoft.PowerShell.Security
                $PSModuleAutoLoadingPreference = 'None'
                . '{{common}}'
                $root = '{{root.Replace("'", "''")}}'
                $global:calls = [Collections.Generic.List[string]]::new()
                function Test-EtpRunningElevated { $global:scenario.Elevated }
                function Invoke-EtpOwnerGrantPreflight { param($SqlCmdPath, $ServerInstance) $global:calls.Add('preflight'); $global:scenario.Preflight }
                function New-EtpProtectedDirectory { param($Path, $ReadSid) $global:calls.Add('folder'); [IO.Directory]::CreateDirectory($Path).FullName }
                function New-ScheduledTaskAction { [CmdletBinding()] param($Execute, $Argument) [pscustomobject]@{ Execute = $Execute; Argument = $Argument } }
                function New-ScheduledTaskPrincipal { [CmdletBinding()] param($UserId, $LogonType, $RunLevel) [pscustomobject]@{ UserId = $UserId; LogonType = $LogonType; RunLevel = $RunLevel } }
                function New-ScheduledTaskSettingsSet { [CmdletBinding()] param($ExecutionTimeLimit, $MultipleInstances, [switch]$AllowStartIfOnBatteries, [switch]$DontStopIfGoingOnBatteries) [pscustomobject]@{ Limit = $ExecutionTimeLimit } }
                function Register-ScheduledTask {
                    [CmdletBinding()] param($TaskName, $TaskPath, $Action, $Principal, $Settings, $Description, $Trigger, $User, $Password, [switch]$Force)
                    $global:calls.Add("register:$TaskName")
                    $global:registered = [pscustomobject]@{ Name = $TaskName; Path = $TaskPath; Action = $Action; Principal = $Principal; Trigger = $Trigger; User = $User; Password = $Password }
                    if ($global:scenario.RegisterFails) { throw 'Access is denied.' }
                    [pscustomobject]@{ TaskName = $TaskName }
                }
                function Start-ScheduledTask {
                    [CmdletBinding()] param($TaskName, $TaskPath)
                    $global:calls.Add("start:$TaskName")
                    $folder = @(Get-ChildItem -LiteralPath $root -Directory -Filter 'OwnerGrant-*')
                    if ($folder.Count -ne 1) { throw 'Expected exactly one work folder.' }
                    $global:seen = [pscustomobject]@{
                        Folder = $folder[0].FullName
                        Sql = [IO.File]::ReadAllText((Join-Path $folder[0].FullName 'grant-owner-option.sql'))
                        Cmd = [IO.File]::ReadAllText((Join-Path $folder[0].FullName 'grant-owner-option.cmd'))
                    }
                    if ($global:scenario.StartFails) { throw 'The task could not be started.' }
                    if ($global:scenario.Output) { [IO.File]::WriteAllLines((Join-Path $folder[0].FullName 'grant-owner-option.out'), [string[]]$global:scenario.Output) }
                }
                function Get-ScheduledTask { [CmdletBinding()] param($TaskName, $TaskPath) [pscustomobject]@{ State = $global:scenario.State } }
                function Get-ScheduledTaskInfo { [CmdletBinding()] param($TaskName, $TaskPath) [pscustomobject]@{ LastTaskResult = $global:scenario.LastResult } }
                function Stop-ScheduledTask { [CmdletBinding()] param($TaskName, $TaskPath) $global:calls.Add("stop:$TaskName") }
                function Unregister-ScheduledTask {
                    [CmdletBinding(SupportsShouldProcess)] param($TaskName, $TaskPath)
                    $global:calls.Add("unregister:$TaskName")
                    if ($global:scenario.UnregisterFails) { throw 'Access is denied.' }
                }
                $success = @('ETP_GRANT_SYSADMIN:1', 'ETP_GRANT_GRANTED:1', 'ETP_GRANT_MISSING:0', 'ETP_GRANT_EXIT:0')
                function Invoke-Scenario([string]$Name, [hashtable]$Changes) {
                    $global:scenario = @{ Elevated = $true; Preflight = 'YES'; State = 'Ready'; LastResult = 0; Output = $success; RegisterFails = $false; StartFails = $false; UnregisterFails = $false }
                    foreach ($key in $Changes.Keys) { $global:scenario[$key] = $Changes[$key] }
                    $global:calls.Clear(); $global:registered = $null; $global:seen = $null
                    $result = Invoke-EtpOwnerGrantOptionAsSystem -SqlCmdPath 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -ServerInstance '.\SQLEXPRESS' -Database 'EtpReporting' -WorkRoot $root -TimeoutSeconds 2
                    $left = @(Get-ChildItem -LiteralPath $root -Force).Count
                    $registeredName = if ($global:registered) { $global:registered.Name } else { '' }
                    $unregistered = @($global:calls | Where-Object { $_ -eq "unregister:$registeredName" }).Count
                    $steps = @($global:calls | ForEach-Object { $_.Split(':')[0] }) -join ','
                    [Console]::WriteLine("SCENARIO:$Name|$($result.Outcome)|$steps|left=$left|unregistered=$unregistered")
                    [Console]::WriteLine("MESSAGE:$Name|$($result.Message)")
                    return $result
                }

                $granted = Invoke-Scenario 'Granted' @{}
                if ($global:registered.Path -ne '\' -or $null -ne $global:registered.Trigger -or $null -ne $global:registered.User -or $null -ne $global:registered.Password) { throw 'The task has a trigger, a path of its own or a stored account.' }
                if ($global:registered.Principal.UserId -ne 'S-1-5-18' -or $global:registered.Principal.LogonType -ne 'ServiceAccount' -or $global:registered.Principal.RunLevel -ne 'Highest') { throw 'The task does not run as SYSTEM.' }
                if ($global:registered.Action.Execute -notlike '*\System32\cmd.exe') { throw 'The task does not run cmd.exe from System32.' }
                if ($global:registered.Action.Argument -ne ('/d /v:off /s /c ""' + (Join-Path $global:seen.Folder 'grant-owner-option.cmd') + '""')) { throw "Unexpected task argument: $($global:registered.Action.Argument)" }
                if ($global:seen.Sql -cne (New-EtpOwnerGrantOptionSql)) { throw 'The task runs something other than the fixed batch.' }
                if ($global:seen.Cmd -notlike ('*-i "' + (Join-Path $global:seen.Folder 'grant-owner-option.sql') + '"*')) { throw 'The command file does not run the batch beside it.' }
                if ($granted.Granted -ne 1 -or $granted.Missing -ne 0) { throw 'The counts were not read.' }
                $first = $global:registered.Name
                $null = Invoke-Scenario 'Again' @{ Output = @('ETP_GRANT_SYSADMIN:1', 'ETP_GRANT_GRANTED:0', 'ETP_GRANT_MISSING:0', 'ETP_GRANT_EXIT:0') }
                if ($global:registered.Name -eq $first -or $first -notmatch '^ETP Reporting Owner Grant [a-f0-9]{32}$') { throw 'Task names are not unique.' }
                $null = Invoke-Scenario 'NotElevated' @{ Elevated = $false }
                $null = Invoke-Scenario 'PreflightNo' @{ Preflight = 'NO' }
                $null = Invoke-Scenario 'PreflightUnknown' @{ Preflight = 'UNKNOWN' }
                $null = Invoke-Scenario 'SystemNotSysadmin' @{ Output = @('ETP_GRANT_SYSADMIN:0', 'ETP_GRANT_EXIT:0') }
                $null = Invoke-Scenario 'SqlFailed' @{ Output = @('Msg 4613, Level 16, State 1', 'ETP_GRANT_EXIT:1') }
                $null = Invoke-Scenario 'RegisterFails' @{ RegisterFails = $true }
                $null = Invoke-Scenario 'StartFails' @{ StartFails = $true }
                $null = Invoke-Scenario 'CmdDied' @{ Output = $null; LastResult = 1 }
                $null = Invoke-Scenario 'TimedOut' @{ Output = $null; State = 'Running' }
                $null = Invoke-Scenario 'UnregisterFails' @{ UnregisterFails = $true }
                Write-Output 'Runner scenarios passed.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Runner scenarios passed.", result.Output);
            var scenarios = Lines(result.Output, "SCENARIO:");
            var messages = Lines(result.Output, "MESSAGE:");

            Assert.Equal("Granted|preflight,folder,register,start,unregister|left=0|unregistered=1", scenarios["Granted"]);
            Assert.Equal("AlreadyHeld|preflight,folder,register,start,unregister|left=0|unregistered=1", scenarios["Again"]);
            // Nothing is registered unless this runs elevated and SYSTEM is a SQL administrator.
            Assert.Equal("Skipped||left=0|unregistered=0", scenarios["NotElevated"]);
            Assert.Equal("NotSysadmin|preflight|left=0|unregistered=0", scenarios["PreflightNo"]);
            // An unreadable preflight goes on: the batch checks again as SYSTEM.
            Assert.Equal("Granted|preflight,folder,register,start,unregister|left=0|unregistered=1", scenarios["PreflightUnknown"]);
            Assert.Equal("NotSysadmin|preflight,folder,register,start,unregister|left=0|unregistered=1", scenarios["SystemNotSysadmin"]);
            Assert.Equal("Failed|preflight,folder,register,start,unregister|left=0|unregistered=1", scenarios["SqlFailed"]);
            // Every failure still unregisters (or tries to) and deletes the folder, and returns.
            Assert.Equal("Failed|preflight,folder,register,unregister|left=0|unregistered=1", scenarios["RegisterFails"]);
            Assert.Equal("Failed|preflight,folder,register,start,unregister|left=0|unregistered=1", scenarios["StartFails"]);
            Assert.Equal("Failed|preflight,folder,register,start,unregister|left=0|unregistered=1", scenarios["CmdDied"]);
            Assert.Equal("TimedOut|preflight,folder,register,start,stop,unregister|left=0|unregistered=1", scenarios["TimedOut"]);
            Assert.Equal("Granted|preflight,folder,register,start,unregister|left=0|unregistered=1", scenarios["UnregisterFails"]);

            Assert.Contains("given ALTER ANY LOGIN WITH GRANT OPTION through a one-off scheduled task run as SYSTEM", messages["Granted"], StringComparison.Ordinal);
            Assert.StartsWith("NOTE: SYSTEM is not a SQL Server administrator", messages["PreflightNo"], StringComparison.Ordinal);
            Assert.StartsWith("NOTE: ", messages["NotElevated"], StringComparison.Ordinal);
            Assert.StartsWith("WARNING: ", messages["RegisterFails"], StringComparison.Ordinal);
            Assert.Contains("Access is denied.", messages["RegisterFails"], StringComparison.Ordinal);
            Assert.DoesNotContain("could not be removed", messages["RegisterFails"], StringComparison.Ordinal);
            Assert.StartsWith("WARNING: ", messages["TimedOut"], StringComparison.Ordinal);
            Assert.Contains("did not finish in time", messages["TimedOut"], StringComparison.Ordinal);
            Assert.Contains("WARNING: the scheduled task 'ETP Reporting Owner Grant ", messages["UnregisterFails"], StringComparison.Ordinal);
            Assert.Contains("delete it in Task Scheduler", messages["UnregisterFails"], StringComparison.Ordinal);
            Assert.DoesNotContain("4613", messages["SqlFailed"], StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static Dictionary<string, string> Lines(string output, string prefix) =>
        output.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[prefix.Length..])
            .ToDictionary(line => line[..line.IndexOf('|')], line => line[(line.IndexOf('|') + 1)..]);

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        var to = text.IndexOf(end, from + 1, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, text);
        return text[(from + start.Length)..to];
    }

    private static int Occurrences(string text, string value) => text.Split(value).Length - 1;

    private static string FindScript(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "scripts", name);
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
