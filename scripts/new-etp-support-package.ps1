param(
    [string]$ServerInstance = ".\SQLEXPRESS",
    [string]$Database = "EtpReporting",
    [string]$OutputDirectory = "$env:USERPROFILE\Documents\EtpReportingSupport"
)

$ErrorActionPreference = "Stop"
$sqlcmd = (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue).Source
if (-not $sqlcmd) { $sqlcmd = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE" }
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "SQLCMD is not installed at the expected SQL Server tools path." }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Database must contain only letters, numbers, or underscore." }
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$staging = Join-Path $resolvedOutput "support-$stamp"
$archive = Join-Path $resolvedOutput "EtpReporting-Support-$stamp.zip"
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $healthFile = Join-Path $staging "database-health.txt"
    $query = @"
SET NOCOUNT ON;
SELECT DB_NAME() DatabaseName,CAST(SUM(size)*8.0/1024.0 AS decimal(18,2)) DatabaseSizeMb FROM sys.database_files;
SELECT COUNT_BIG(*) ImportedFiles FROM dbo.import_files;
SELECT COUNT_BIG(*) LineageRows FROM dbo.source_lineage;
SELECT status,COUNT_BIG(*) BatchCount FROM dbo.import_batches GROUP BY status ORDER BY status;
SELECT MAX(completed_utc) LatestCompletedImportUtc FROM dbo.import_batches WHERE status='Completed';
SELECT MAX(backup_finish_date) LatestFullBackupLocalTime FROM msdb.dbo.backupset WHERE database_name=DB_NAME() AND type='D';
"@
    & $sqlcmd -S $ServerInstance -E -b -d $Database -W -Q $query 2>&1 | Out-File -LiteralPath $healthFile -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "Support health query failed." }
    Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,OSArchitecture,LastBootUpTime |
        Format-List | Out-File -LiteralPath (Join-Path $staging "system.txt") -Encoding utf8
    Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue | Select-Object Name,Status,StartType |
        Format-List | Out-File -LiteralPath (Join-Path $staging "sql-service.txt") -Encoding utf8
    Get-ScheduledTask -TaskName "ETP Reporting Daily Backup" -ErrorAction SilentlyContinue |
        Select-Object TaskName,State | Format-List | Out-File -LiteralPath (Join-Path $staging "backup-task.txt") -Encoding utf8
    Set-Content -LiteralPath (Join-Path $staging "privacy.txt") -Value "This package contains aggregate health and environment metadata only. Source rows, customer data, invoice identifiers, workbook names and workbook paths are intentionally excluded."
    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $archive -CompressionLevel Optimal
    Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
