param([string]$ServerInstance = ".\SQLEXPRESS", [string]$Database = "EtpReporting", [ValidateRange(30,3650)][int]$AuditRetentionDays = 730)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
Assert-EtpLocalSqlTarget $ServerInstance $Database
$sqlcmd = Resolve-EtpSqlCmd
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Database must contain only letters, numbers, or underscore." }
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "SQLCMD is not installed." }
$query = "SET XACT_ABORT ON; DBCC CHECKDB ([$Database]) WITH NO_INFOMSGS; EXEC sys.sp_updatestats; EXEC dbo.archive_operational_audit @retention_days=$AuditRetentionDays;"
& $sqlcmd -x -S $ServerInstance -E -b -d $Database -Q $query
if ($LASTEXITCODE -ne 0) { throw "Database maintenance failed." }
Write-Host "Integrity, statistics and audit retention maintenance passed."
