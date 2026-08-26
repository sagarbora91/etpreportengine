param(
    [string]$ServerInstance = ".\SQLEXPRESS",
    [string]$Database = "EtpReporting",
    [string]$BackupDirectory = "$env:ProgramData\EtpReporting\Backups"
)

$ErrorActionPreference = "Stop"
$sqlcmd = (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue).Source
if (-not $sqlcmd) { $sqlcmd = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" }
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "SQLCMD is not installed at the expected SQL Server tools path." }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Database must contain only letters, numbers, or underscore." }
$resolvedDirectory = [System.IO.Path]::GetFullPath($BackupDirectory)
New-Item -ItemType Directory -Path $resolvedDirectory -Force | Out-Null
$drive = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($resolvedDirectory))
$freeGb = [math]::Round($drive.AvailableFreeSpace / 1GB, 2)
if ($freeGb -lt 5) { Write-Warning "BACKUP_SPACE_CRITICAL: Backup storage has only $freeGb GB free. Add storage immediately." }
elseif ($freeGb -lt 20) { Write-Warning "BACKUP_SPACE_LOW: Backup storage has only $freeGb GB free. Plan additional storage." }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $resolvedDirectory "$Database-$stamp.bak"
$escapedPath = $backupPath.Replace("'", "''")
& $sqlcmd -S $ServerInstance -E -b -Q "BACKUP DATABASE [$Database] TO DISK=N'$escapedPath' WITH COPY_ONLY, CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK=N'$escapedPath' WITH CHECKSUM;"
if ($LASTEXITCODE -ne 0) { throw "Database backup or verification failed." }
$auditActor = ([Environment]::UserName).Replace("'", "''")
& $sqlcmd -S $ServerInstance -E -b -d $Database -Q "IF COL_LENGTH('dbo.operational_audit','actor_name') IS NOT NULL INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name) VALUES('Backup','Succeeded','Checksum backup verified','operations',N'$auditActor');"
if ($LASTEXITCODE -ne 0) { throw "Database backup succeeded but its audit event could not be recorded." }
Get-FileHash -LiteralPath $backupPath -Algorithm SHA256 | Format-List
