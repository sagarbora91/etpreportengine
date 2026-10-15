# Retires the two leftover databases on Workpc (decision 51 #6, 13 Oct 2026): EtpAudit_HEMW and EtpStaging_20261009.
# For each one: full backup WITH CHECKSUM -> RESTORE VERIFYONLY -> copy to E:\ETP\Reference\Old databases (CONTAINS BUSINESS DATA)
# -> SHA-256 of both copies must match -> only then, after you type DROP, the database is dropped.
# Anything that fails stops the script before any drop. The live database EtpReporting is never touched.
# Run elevated, NOT while a gate is running. -BackupOnly makes and checks the backups and drops nothing.
param([switch]$BackupOnly)

$ErrorActionPreference = 'Stop'
$databases = 'EtpAudit_HEMW', 'EtpStaging_20261009'
$keep = 'E:\ETP\Reference\Old databases (CONTAINS BUSINESS DATA)'
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host 'Please run this as administrator.' -ForegroundColor Red; return }
if (Test-Path 'E:\ETP\Code\Worktrees\_tools\PAUSE-LANE-BUILDS') { Write-Host 'A gate seems to be running (PAUSE-LANE-BUILDS exists). Run this after it finishes.' -ForegroundColor Red; return }

function Sql([string]$query) {
    # -w 4000: sqlcmd wraps output at 80 characters by default, which split the backup folder path (13 Oct).
    $out = sqlcmd -S '.\SQLEXPRESS' -E -C -b -W -w 4000 -h -1 -Q "SET NOCOUNT ON; $query" 2>&1
    if ($LASTEXITCODE) { throw "SQL failed: $($out -join ' ')" }
    @($out | Where-Object { $_ -and $_.ToString().Trim() })
}

$backupDir = @(Sql "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400));")[0].ToString().Trim()
New-Item -ItemType Directory -Force -Path $keep | Out-Null
Start-Transcript -Path (Join-Path $keep "retire-$stamp.log") -Force | Out-Null
Write-Host "SQL backup folder: $backupDir"
Write-Host "Kept copies go to: $keep"

$ready = @()
foreach ($db in $databases) {
    Write-Host "`n== $db"
    if ($db -eq 'EtpReporting') { throw 'Refusing to touch the live database.' }
    if (-not (Sql "SELECT name FROM sys.databases WHERE name = N'$db';")) { Write-Host '  not on this server (already dropped?) - skipped' -ForegroundColor Yellow; continue }
    $size = @(Sql "SELECT CAST(SUM(size)/128 AS varchar(12)) FROM sys.master_files WHERE database_id = DB_ID(N'$db');")[0]
    Write-Host "  size: $size MB"

    $bak = Join-Path $backupDir "$db-RETIRE-$stamp.bak"
    Sql "BACKUP DATABASE [$db] TO DISK = N'$bak' WITH COPY_ONLY, CHECKSUM, INIT, NAME = N'$db retire $stamp';" | Out-Null
    Sql "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM;" | Out-Null
    Write-Host '  backup made and verified (checksum)'

    $copy = Join-Path $keep (Split-Path $bak -Leaf)
    Copy-Item -LiteralPath $bak -Destination $copy
    $h1 = (Get-FileHash -LiteralPath $bak).Hash; $h2 = (Get-FileHash -LiteralPath $copy).Hash
    if ($h1 -ne $h2) { throw "The kept copy of $db does not match the backup. Nothing dropped." }
    "$h2  $(Split-Path $copy -Leaf)" | Add-Content (Join-Path $keep 'SHA256SUMS.txt')
    Write-Host "  kept copy matches: $copy" -ForegroundColor Green
    $ready += $db
}

if ($BackupOnly -or -not $ready) { Write-Host "`nBackups done. Nothing was dropped." -ForegroundColor Green; return }

Write-Host "`nReady to DROP: $($ready -join ', '). Their verified backups are in: $keep"
Write-Host 'Copy that folder to a USB stick first if you want a copy off this PC.'
$answer = ([string](Read-Host 'Type DROP to drop them now, anything else to stop')).Trim()
Write-Host "  you typed: [$answer]"
if ($answer -cne 'DROP') { Write-Host 'Stopped. Nothing was dropped.'; return }

foreach ($db in $ready) {
    Sql "ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db];" | Out-Null
    Write-Host "  dropped $db" -ForegroundColor Green
}
Write-Host "`nDone. Remaining databases:"; Sql "SELECT name FROM sys.databases ORDER BY name;" | ForEach-Object { "  $_" }
