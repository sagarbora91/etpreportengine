param(
    [string]$ServerInstance = ".\SQLEXPRESS",
    [string]$Database = "EtpReporting",
    [string]$BackupDirectory = "$env:ProgramData\EtpReporting\Backups"
)

$ErrorActionPreference = "Stop"
$sqlcmd = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE"
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "SQLCMD is not installed at the expected SQL Server tools path." }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Database must contain only letters, numbers, or underscore." }
$resolvedDirectory = [System.IO.Path]::GetFullPath($BackupDirectory)
New-Item -ItemType Directory -Path $resolvedDirectory -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $resolvedDirectory "$Database-$stamp.bak"
$escapedPath = $backupPath.Replace("'", "''")
& $sqlcmd -S $ServerInstance -E -b -Q "BACKUP DATABASE [$Database] TO DISK=N'$escapedPath' WITH COPY_ONLY, CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK=N'$escapedPath' WITH CHECKSUM;"
if ($LASTEXITCODE -ne 0) { throw "Database backup or verification failed." }
Get-FileHash -LiteralPath $backupPath -Algorithm SHA256 | Format-List
