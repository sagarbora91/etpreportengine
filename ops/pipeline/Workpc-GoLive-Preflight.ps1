# Workpc go-live pre-flight (13 Oct 2026, go-live on 1.11.4 decided by Sagar for tonight).
# Run elevated, with ETP CLOSED. Changes nothing in the database. It:
#   1. checks no gate/build is running and ETP is not open;
#   2. records the live state (version of migrations, users, last invoice) into the evidence folder;
#   3. runs DBCC CHECKDB on EtpReporting (must be 0 errors);
#   4. runs the ETP Daily Backup task and waits for a NEW verified backup;
#   5. copies that .bak (and its receipt files) to G:\ETP-GoLive-<stamp>\ (separate disk) and checks the SHA-256.
# Stops at the first problem with a plain message. Next step after it passes: run the 1.11.4 installer as administrator.

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$evidence = "E:\ETP\Reference\GoLive-Workpc-$stamp"
$offDisk = "G:\ETP-GoLive-$stamp"

function Stop-Here([string]$why) { Write-Host "`nSTOPPED: $why" -ForegroundColor Red; Write-Host 'Nothing was changed. Tell Claude the line above.'; exit 1 }
function Sql([string]$query) {
    $out = sqlcmd -S '.\SQLEXPRESS' -E -C -b -W -w 4000 -h -1 -Q "SET NOCOUNT ON; $query" 2>&1
    if ($LASTEXITCODE) { throw "SQL failed: $($out -join ' ')" }
    @($out | Where-Object { $_ -and $_.ToString().Trim() })
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Stop-Here 'Please run this as administrator.' }
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
Start-Transcript -Path (Join-Path $evidence 'preflight.log') -Force | Out-Null

Write-Host '== 1. Nothing else running'
if (Test-Path 'E:\ETP\Code\Worktrees\_tools\PAUSE-LANE-BUILDS') { Stop-Here 'A gate is running (PAUSE-LANE-BUILDS). Wait for it to finish.' }
$etp = Get-Process | Where-Object { $_.ProcessName -like 'Etp.Reporting*' }
if ($etp) { Stop-Here "ETP is open ($(@($etp).Count) window(s)). Close ETP and run this again." }
Write-Host '  OK: no gate, ETP closed'

Write-Host '== 2. Live state before the upgrade'
$state = [ordered]@{
    migrations     = @(Sql "SELECT CAST(COUNT(*) AS varchar(10)) FROM EtpReporting.dbo.schema_migrations;")[0]
    latest         = @(Sql "SELECT TOP 1 migration_id FROM EtpReporting.dbo.schema_migrations ORDER BY migration_id DESC;")[0]
    activeUsers    = @(Sql "SELECT CAST(COUNT(*) AS varchar(10)) FROM EtpReporting.dbo.application_users WHERE is_active=1;")[0]
    lastInvoiceDay = @(Sql "SELECT CONVERT(varchar(10), MAX(transaction_date), 23) FROM EtpReporting.dbo.sales_invoices;")[0]
    sqlVersion     = @(Sql "SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(30));")[0]
}
$state.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-15} {1}" -f $_.Key, $_.Value) }
$state | ConvertTo-Json | Out-File (Join-Path $evidence 'state-before.json') -Encoding utf8
if ($state.migrations -ne '48') { Stop-Here "Expected 48 migrations (1.9.6), found $($state.migrations)." }

Write-Host '== 3. DBCC CHECKDB (a few minutes)'
Sql "DBCC CHECKDB (N'EtpReporting') WITH NO_INFOMSGS, ALL_ERRORMSGS;" | Out-File (Join-Path $evidence 'checkdb.txt') -Encoding utf8
Write-Host '  OK: CHECKDB 0 errors' -ForegroundColor Green

Write-Host '== 4. Fresh verified backup (ETP Daily Backup task)'
$before = [int](@(Sql "SELECT COUNT(*) FROM EtpReporting.dbo.operational_audit WHERE event_type=N'Backup' AND outcome=N'Succeeded';")[0])
$task = Get-ScheduledTask -TaskName 'ETP Reporting Daily Backup'
if ($task.State -eq 'Running') { Write-Host '  the task is already running; waiting for it first' }
else { Start-ScheduledTask -TaskName 'ETP Reporting Daily Backup' }
$deadline = (Get-Date).AddMinutes(20)
do { Start-Sleep 10; $task = Get-ScheduledTask -TaskName 'ETP Reporting Daily Backup'; $now = [int](@(Sql "SELECT COUNT(*) FROM EtpReporting.dbo.operational_audit WHERE event_type=N'Backup' AND outcome=N'Succeeded';")[0]) }
while (($task.State -eq 'Running' -or $now -le $before) -and (Get-Date) -lt $deadline)
if ($now -le $before) { Stop-Here 'No new successful backup was recorded within 20 minutes.' }
$detail = @(Sql "SELECT TOP 1 CONVERT(varchar(19), event_utc, 120) + N' UTC | ' + safe_detail FROM EtpReporting.dbo.operational_audit WHERE event_type=N'Backup' ORDER BY operational_audit_id DESC;")[0]
Write-Host "  OK: $detail" -ForegroundColor Green

Write-Host '== 5. Copy to G: (separate disk) and check'
$bak = @(Sql "SELECT TOP 1 mf.physical_device_name FROM msdb.dbo.backupset bs JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id=bs.media_set_id WHERE bs.database_name=N'EtpReporting' AND bs.type='D' ORDER BY bs.backup_finish_date DESC;")[0].ToString().Trim()
if (-not (Test-Path -LiteralPath $bak)) { Stop-Here "The newest backup file is not where SQL Server says: $bak" }
New-Item -ItemType Directory -Force -Path $offDisk | Out-Null
$dir = Split-Path $bak; $base = [IO.Path]::GetFileNameWithoutExtension($bak)
$files = @(Get-Item -LiteralPath $bak) + @(Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Name -like "$base*" -and $_.FullName -ne $bak }) + @(Get-ChildItem -LiteralPath $dir -File -Filter '*latest-verified*.json')
foreach ($f in $files) {
    $dest = Join-Path $offDisk $f.Name
    Copy-Item -LiteralPath $f.FullName -Destination $dest
    $h1 = (Get-FileHash -LiteralPath $f.FullName).Hash; $h2 = (Get-FileHash -LiteralPath $dest).Hash
    if ($h1 -ne $h2) { Stop-Here "Copy of $($f.Name) to G: does not match." }
    "$h2  $($f.Name)" | Add-Content (Join-Path $offDisk 'SHA256SUMS.txt')
    Write-Host "  copied + checked: $($f.Name)"
}
Copy-Item (Join-Path $offDisk 'SHA256SUMS.txt') $evidence

Write-Host "`nPRE-FLIGHT PASSED." -ForegroundColor Green
Write-Host "Backup kept on G:: $offDisk"
Write-Host "Evidence: $evidence"
Write-Host 'Next: write down the 1.9.6 DSR figures (if not done), then run the 1.11.4 installer as administrator.'
Stop-Transcript | Out-Null
