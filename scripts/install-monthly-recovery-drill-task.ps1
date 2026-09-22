param([ValidateRange(1,28)][int]$DayOfMonth=1, [string]$TaskName='ETP Reporting Monthly Recovery Drill',[Alias('DrillTime')][string]$RunTime='08:00',
      # P4-15. The drill restores and integrity-checks a complete copy of the database, which
      # only a SQL administrator can do, so it runs as the Owner rather than as the automation
      # account. Defaults to whoever the drill already runs as, so that a repair or an upgrade
      # run by someone else does not quietly move it to them; on a first install, to the
      # administrator running setup.
      [string]$DrillPrincipal,
      [string]$SqlCmdPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
$script = Join-Path $PSScriptRoot 'invoke-monthly-recovery-drill-runner.ps1'
Assert-EtpProtectedInstall $script
$configuration = Get-EtpOperationsConfiguration
$time = [datetime]::ParseExact($RunTime,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
$serviceAccounts = @('S-1-5-18','S-1-5-19','S-1-5-20')
$current = [Security.Principal.WindowsIdentity]::GetCurrent()

if (-not $PSBoundParameters.ContainsKey('DrillPrincipal')) {
    $DrillPrincipal = $current.Name
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing -and -not [string]::IsNullOrWhiteSpace($existing.Principal.UserId)) {
        # Task Scheduler reports a local account by its bare name ("Sagar"), so resolve it
        # to DOMAIN\User. Builds before P4-15 ran the drill as a service account; that is not
        # kept, because the drill now needs the Owner.
        $existingSid = Resolve-EtpAccountSid $existing.Principal.UserId
        if (-not $existingSid) { Write-Warning 'The existing recovery drill account could not be resolved; the drill will run as the account running setup.' }
        elseif ($existingSid -notin $serviceAccounts) { $DrillPrincipal = ([Security.Principal.SecurityIdentifier]::new($existingSid)).Translate([Security.Principal.NTAccount]).Value }
    }
}

# Refuse at install time rather than fail silently every month. The same rule the
# application applies to a Windows identity in Settings > Users: DOMAIN\User or
# COMPUTER\User, with no characters that could break out of a SQL literal.
if ($DrillPrincipal -notmatch '^[^\\/\[\];''"]+\\[^\\/\[\];''"]+$') { throw 'Choose the Owner as DOMAIN\User or COMPUTER\User to run the recovery drill.' }
$drillSid = ([Security.Principal.NTAccount]::new($DrillPrincipal)).Translate([Security.Principal.SecurityIdentifier])
if ($drillSid.Value -in $serviceAccounts) { throw 'The recovery drill must run as the Owner, not as a built-in service account.' }
$sqlcmd = Resolve-EtpSqlCmd $SqlCmdPath
$server = Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $configuration.serverInstance
# IS_SRVROLEMEMBER with a login name looks only at that login's own server roles, and
# returns NULL for an account that reaches SQL through a Windows group - which is exactly
# how the bundled SQL Server install makes the Owner an administrator (BUILTIN\Administrators).
# Ask about the real token instead: this session's own when the drill runs as the account
# running setup, elevated as setup is and as the task will be; otherwise the token SQL
# builds for that account, groups included.
if ($drillSid.Value -eq $current.User.Value) {
    $probe = "SET NOCOUNT ON; SELECT 'ETP_SYSADMIN:'+CONVERT(varchar(1),COALESCE(IS_SRVROLEMEMBER('sysadmin'),0));"
}
else {
    $literal = $DrillPrincipal.Replace("'","''")
    $probe = "SET NOCOUNT ON; BEGIN TRY EXECUTE AS LOGIN=N'$literal'; DECLARE @r varchar(1)=CONVERT(varchar(1),COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)); REVERT; SELECT 'ETP_SYSADMIN:'+@r; END TRY BEGIN CATCH SELECT 'ETP_SYSADMIN:?'; END CATCH;"
}
$check = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $server -Query $probe)
if (@($check | Where-Object { $_.Trim() -ceq 'ETP_SYSADMIN:?' }).Count -gt 0) {
    throw "Setup could not confirm that $DrillPrincipal is a SQL administrator. Run setup as a SQL administrator, or as the Owner."
}
if (@($check | Where-Object { $_.Trim() -ceq 'ETP_SYSADMIN:1' }).Count -ne 1) {
    throw "The recovery drill needs a SQL administrator, and $DrillPrincipal is not one. Run setup elevated as the Owner, or pass -DrillPrincipal."
}

# A4.3 accepted unsigned installs. AllSigned here would refuse the monthly drill, which
# is the control that proves a backup can actually be restored.
# Assert-EtpProtectedInstall above still refuses a script a non-administrator can edit.
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "' + $script + '"'
$arguments += ' -DayOfMonth ' + $DayOfMonth
$powerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $time
# Highest, not Limited: a UAC-filtered token loses BUILTIN\Administrators, so an Owner who is
# a SQL administrator only through that group would lose it inside the task.
Register-EtpScheduledOperation -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Receipt-verified isolated recovery drill, run as the Owner.' -Principal $DrillPrincipal -RunLevel Highest
Write-Output "Scheduled operation installed. The monthly recovery drill runs as $DrillPrincipal."
