<#
.SYNOPSIS
  Read-only post-restore / new-PC check (DR finding G9). Changes nothing; prints each missing item and the exact command to fix it.
.DESCRIPTION
  Order-dependent setup steps fail with masked messages. This lists, in plain words: SQL service, database and migration journal,
  automation account (Windows, enabled), active Store Manager row and SQL login for it, the three scheduled tasks (present,
  enabled, last result), the Owner grant option, newest backup age (under 24 h) and the last recovery drill.
  Run it in an ordinary PowerShell 7 window as the Owner; elevation is not needed, and a check that cannot run says "cannot tell".
  Exit code: 0 = nothing missing, 1 = something missing.
#>
[CmdletBinding()]
param(
    [string]$ServerInstance = '.\SQLEXPRESS',
    [string]$Database = 'EtpReporting',
    [string]$AutomationPrincipal,
    [int]$MaxBackupAgeHours = 24,
    [int]$MaxDrillAgeDays = 35
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$script:missing = 0
function Report([string]$State, [string]$What, [string]$Fix) {
    $tag = switch ($State) { 'OK' { '[ ok ]' } 'MISSING' { '[MISSING]' } default { '[cannot tell]' } }
    Write-Host ("{0} {1}" -f $tag, $What)
    if ($State -ne 'OK' -and $Fix) { Write-Host ("         fix: {0}" -f $Fix) }
    if ($State -eq 'MISSING') { $script:missing++ }
}
$sqlcmd = (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $sqlcmd) { $p = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'; if (Test-Path $p) { $sqlcmd = $p } }
function Invoke-Sql([string]$Query, [string]$Db = $Database) {
    if (-not $sqlcmd) { throw 'sqlcmd.exe not found.' }
    $out = & $sqlcmd -S $ServerInstance -E -C -d $Db -h -1 -W -s '|' -b -Q ("SET NOCOUNT ON; " + $Query) 2>&1
    if ($LASTEXITCODE) { throw ("sqlcmd failed: " + (($out | Out-String).Trim())) }
    return @($out | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
}
function Escape-Sql([string]$Text) { return $Text.Replace("'", "''") }
Write-Host "ETP restored-PC check on $env:COMPUTERNAME ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))"

# 1. SQL Server service
$instance = if ($ServerInstance -match '\\(.+)$') { $Matches[1] } else { 'MSSQLSERVER' }
$serviceName = if ($instance -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { 'MSSQL$' + $instance }
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq 'Running') { Report OK "SQL Server service $serviceName is running." '' }
else {
    Report MISSING "SQL Server service $serviceName is not running (or not installed)." "Install via ETP setup with the SQL option ticked, or Start-Service '$serviceName' (administrator)."
    Write-Host 'Cannot continue without SQL Server.'
    exit 1
}

# 2. Database and migration journal
try {
    $dbOk = Invoke-Sql "SELECT COUNT(*) FROM sys.databases WHERE name=N'$(Escape-Sql $Database)';" master
    if ($dbOk[0] -eq '1') { Report OK "Database $Database exists." '' }
    else {
        Report MISSING "Database $Database does not exist." "powershell.exe -NoProfile -ExecutionPolicy Bypass -File '<ETP folder>\scripts\restore-etp-database.ps1' -BackupPath '<the .bak>' (administrator), then run ETP setup again."
        exit 1
    }
    $journal = Invoke-Sql "SELECT CAST(COUNT(*) AS varchar(10)) + ' migrations, last ' + ISNULL(MAX(migration_id),'none') FROM dbo.schema_migrations;"
    Report OK "Migration journal: $($journal[0])." ''
}
catch { Report CANNOT "Could not read the database ($($_.Exception.Message))." 'Run this check as the Owner account, which has a SQL login.' }

# 3. Automation account
$opsPath = Join-Path $env:ProgramData 'EtpReporting\Operations\operations.json'
if (-not $AutomationPrincipal) {
    try { $AutomationPrincipal = (Get-Content -Raw -LiteralPath $opsPath | ConvertFrom-Json).automationPrincipal } catch { }
}
if (-not $AutomationPrincipal) {
    Report MISSING 'No automation account is configured (operations.json missing or unreadable).' "Create the local account (for example $env:COMPUTERNAME\EtpAutomation, not an administrator), then run ETP setup again."
    $AutomationPrincipal = "$env:COMPUTERNAME\EtpAutomation"
}
else {
    $short = ($AutomationPrincipal -split '\\')[-1]
    $u = Get-LocalUser -Name $short -ErrorAction SilentlyContinue
    if (-not $u) { Report MISSING "Windows account $AutomationPrincipal does not exist." "Create it as a standard local user (administrator PowerShell: New-LocalUser -Name '$short' -Password (Read-Host -AsSecureString)); do not add it to Administrators." }
    elseif (-not $u.Enabled) { Report MISSING "Windows account $AutomationPrincipal is disabled." "Enable-LocalUser -Name '$short' (administrator)." }
    else { Report OK "Windows account $AutomationPrincipal exists and is enabled." '' }
}

# 4. Store Manager row and SQL login
try {
    $row = Invoke-Sql "SELECT COUNT(*) FROM dbo.application_users WHERE windows_identity=N'$(Escape-Sql $AutomationPrincipal)' AND role_code='STORE_MANAGER' AND is_active=1;"
    if ($row[0] -eq '1') { Report OK "$AutomationPrincipal is an active Store Manager in ETP." '' }
    else { Report MISSING "$AutomationPrincipal is not an active Store Manager (backup and drill then fail with a masked 'database operation failed')." "In ETP, signed in as Owner and run as administrator: Settings > Users > add $AutomationPrincipal as Store Manager. Then do the next fix if it is listed." }
    $login = Invoke-Sql "SELECT COUNT(*) FROM sys.server_principals WHERE name=N'$(Escape-Sql $AutomationPrincipal)';" master
    if ($login[0] -eq '1') { Report OK "$AutomationPrincipal has a SQL login." '' }
    else { Report MISSING "$AutomationPrincipal has no SQL login, so the operations module cannot be installed for it." "After the Store Manager row exists, in an administrator PowerShell: & '<ETP folder>\scripts\install-etp-sql-operations.ps1' (or run ETP setup again)." }
}
catch { Report CANNOT "Could not check the Store Manager row ($($_.Exception.Message))." '' }

# 5. Owner can manage users (ALTER ANY LOGIN WITH GRANT OPTION)
try {
    $own = Invoke-Sql @"
DECLARE @i nvarchar(200)=SUSER_SNAME();
SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=@i AND role_code='OWNER' AND is_active=1) THEN 'NOT_AN_OWNER'
 WHEN EXISTS(SELECT 1 FROM sys.server_permissions WHERE class=100 AND grantee_principal_id=SUSER_ID(@i) AND permission_name=N'ALTER ANY LOGIN' AND state='W') THEN 'GRANT_OPTION' ELSE 'MISSING' END;
"@
    switch ($own[0]) {
        'GRANT_OPTION' { Report OK 'You are an active Owner and can manage users without Run as administrator.' '' }
        'NOT_AN_OWNER' { Report CANNOT 'The account running this check is not an active Owner, so the Owner grant was not checked.' 'Run this check as the Owner.' }
        default { Report MISSING 'The Owner lacks ALTER ANY LOGIN WITH GRANT OPTION (Settings > Users fails silently unless ETP is run as administrator).' 'Run ETP setup again (it gives the grant through a SYSTEM one-off task), or run ETP as administrator.' }
    }
}
catch { Report CANNOT "Could not check the Owner grant ($($_.Exception.Message))." '' }

# 6. Scheduled tasks
$tasks = @(
    @{ Name = 'ETP Reporting Daily Backup'; Fix = "& '<ETP folder>\scripts\install-daily-backup-task.ps1' (administrator)" },
    @{ Name = 'ETP Reporting Monthly Recovery Drill'; Fix = "& '<ETP folder>\scripts\install-monthly-recovery-drill-task.ps1' (administrator)" },
    @{ Name = 'ETP Reporting Automated Operations'; Fix = "& '<ETP folder>\scripts\install-etp-automation-task.ps1' (administrator)" })
foreach ($t in $tasks) {
    $task = Get-ScheduledTask -TaskName $t.Name -ErrorAction SilentlyContinue
    if (-not $task) { Report MISSING "Scheduled task '$($t.Name)' is not registered." $t.Fix; continue }
    $info = Get-ScheduledTaskInfo -TaskName $t.Name
    $last = if ($info.LastRunTime.Year -lt 2000) { 'never run' } else { "last run $($info.LastRunTime.ToString('yyyy-MM-dd HH:mm')), result $($info.LastTaskResult)" }
    if ($task.State -eq 'Disabled') { Report MISSING "Scheduled task '$($t.Name)' is DISABLED ($last)." "Enable-ScheduledTask -TaskName '$($t.Name)' (administrator), once the Store Manager row exists." }
    elseif ($info.LastRunTime.Year -ge 2000 -and $info.LastTaskResult -notin 0, 267009, 267011) { Report MISSING "Scheduled task '$($t.Name)' last failed ($last)." 'Read the newest file in %ProgramData%\EtpReporting\SetupLogs and the ETP error register.' }
    else { Report OK "Scheduled task '$($t.Name)' is present and enabled ($last)." '' }
}

# 7. Newest backup and last drill, from the audit trail (no folder rights needed)
try {
    $b = Invoke-Sql "SELECT TOP 1 CONVERT(varchar(19),event_utc,120) FROM dbo.operational_audit WHERE event_type=N'Backup' AND outcome=N'Succeeded' ORDER BY operational_audit_id DESC;"
    if (-not $b) { Report MISSING 'No successful backup is recorded.' "Start-ScheduledTask -TaskName 'ETP Reporting Daily Backup' after the items above are fixed." }
    else {
        $age = ((Get-Date).ToUniversalTime() - [datetime]::ParseExact($b[0], 'yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)).TotalHours
        if ($age -le $MaxBackupAgeHours) { Report OK ("Newest successful backup is {0:N1} h old." -f $age) '' }
        else { Report MISSING ("Newest successful backup is {0:N0} h old (limit $MaxBackupAgeHours h)." -f $age) "Start-ScheduledTask -TaskName 'ETP Reporting Daily Backup'" }
    }
    $d = Invoke-Sql "SELECT TOP 1 CONVERT(varchar(19),event_utc,120) FROM dbo.operational_audit WHERE event_type LIKE N'%Drill%' AND outcome=N'Succeeded' ORDER BY operational_audit_id DESC;"
    if (-not $d) { Report MISSING 'No successful recovery drill is recorded (a drill task that exits 0 without a result file is not a pass).' "Run the drill from the installed scripts folder: invoke-monthly-recovery-drill-runner.ps1 -DayOfMonth <today's day number> (administrator)." }
    else {
        $dage = ((Get-Date).ToUniversalTime() - [datetime]::ParseExact($d[0], 'yyyy-MM-dd HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture)).TotalDays
        if ($dage -le $MaxDrillAgeDays) { Report OK ("Last successful recovery drill is {0:N0} days old." -f $dage) '' }
        else { Report MISSING ("Last successful recovery drill is {0:N0} days old (limit $MaxDrillAgeDays)." -f $dage) 'Run the monthly drill task.' }
    }
}
catch { Report CANNOT "Could not read the audit trail ($($_.Exception.Message))." '' }

Write-Host ''
if ($script:missing -eq 0) { Write-Host 'Nothing is missing.'; exit 0 }
Write-Host "$($script:missing) item(s) missing. Fix them from the top; the order matters (Windows account, then Store Manager row, then operations module)."
exit 1
