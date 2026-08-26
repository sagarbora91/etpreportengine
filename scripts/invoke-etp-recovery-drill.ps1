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
$resolvedBackupDirectory = [IO.Path]::GetFullPath($BackupDirectory)
$backup = Get-ChildItem -LiteralPath $resolvedBackupDirectory -Filter "$Database-*.bak" -File |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $backup) { throw "No database backup is available for the recovery drill." }

$validationDatabase = "EtpReportingRecoveryDrill_$(Get-Date -Format 'yyyyMMddHHmmss')"
$dataDirectory = [IO.Path]::GetFullPath((Join-Path $resolvedBackupDirectory "RecoveryDrill"))
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$dataPath = Join-Path $dataDirectory "$validationDatabase.mdf"
$logPath = Join-Path $dataDirectory "$validationDatabase`_log.ldf"
$escapedBackup = $backup.FullName.Replace("'", "''")
$escapedData = $dataPath.Replace("'", "''")
$escapedLog = $logPath.Replace("'", "''")

try {
    $logicalNames = & $sqlcmd -S $ServerInstance -E -b -W -h -1 -s "|" -Q "RESTORE FILELISTONLY FROM DISK=N'$escapedBackup';"
    if ($LASTEXITCODE -ne 0) { throw "Could not read backup file metadata." }
    $records = @($logicalNames | Where-Object { $_ -match '\|' } | ForEach-Object {
        $fields = $_ -split '\|'
        [pscustomobject]@{ LogicalName = $fields[0].Trim(); FileType = $fields[2].Trim() }
    })
    $dataLogical = ($records | Where-Object FileType -eq 'D' | Select-Object -First 1).LogicalName
    $logLogical = ($records | Where-Object FileType -eq 'L' | Select-Object -First 1).LogicalName
    if (-not $dataLogical -or -not $logLogical) { throw "Backup logical file names could not be determined." }
    $escapedDataLogical = $dataLogical.Replace("'", "''")
    $escapedLogLogical = $logLogical.Replace("'", "''")

    $query = @"
RESTORE VERIFYONLY FROM DISK=N'$escapedBackup' WITH CHECKSUM;
RESTORE DATABASE [$validationDatabase] FROM DISK=N'$escapedBackup'
 WITH MOVE N'$escapedDataLogical' TO N'$escapedData', MOVE N'$escapedLogLogical' TO N'$escapedLog', RECOVERY;
DBCC CHECKDB ([$validationDatabase]) WITH NO_INFOMSGS;
DECLARE @liveFiles bigint=(SELECT COUNT_BIG(*) FROM [$Database].dbo.import_files);
DECLARE @drillFiles bigint=(SELECT COUNT_BIG(*) FROM [$validationDatabase].dbo.import_files);
DECLARE @liveRows bigint=(SELECT COUNT_BIG(*) FROM [$Database].dbo.source_lineage);
DECLARE @drillRows bigint=(SELECT COUNT_BIG(*) FROM [$validationDatabase].dbo.source_lineage);
IF @liveFiles<>@drillFiles OR @liveRows<>@drillRows THROW 51000,'Recovery drill aggregate comparison failed.',1;
SELECT '$validationDatabase' ValidationDatabase,@drillFiles ImportedFiles,@drillRows LineageRows,'PASSED' DrillStatus;
"@
    & $sqlcmd -S $ServerInstance -E -b -d master -Q $query
    if ($LASTEXITCODE -ne 0) { throw "Recovery drill failed." }
    $auditActor = ([Environment]::UserName).Replace("'", "''")
    & $sqlcmd -S $ServerInstance -E -b -d $Database -Q "IF COL_LENGTH('dbo.operational_audit','actor_name') IS NOT NULL INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name) VALUES('RestoreDrill','Succeeded','Isolated restore and aggregate comparison passed','operations',N'$auditActor');"
    if ($LASTEXITCODE -ne 0) { throw "Recovery drill passed but its audit event could not be recorded." }
}
finally {
    & $sqlcmd -S $ServerInstance -E -b -d master -Q "IF DB_ID(N'$validationDatabase') IS NOT NULL BEGIN ALTER DATABASE [$validationDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$validationDatabase]; END;"
    if (Test-Path -LiteralPath $dataPath) { Remove-Item -LiteralPath $dataPath -Force }
    if (Test-Path -LiteralPath $logPath) { Remove-Item -LiteralPath $logPath -Force }
}
