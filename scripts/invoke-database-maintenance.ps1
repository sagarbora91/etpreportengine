param([string]$ServerInstance = ".\SQLEXPRESS", [string]$Database = "EtpReporting", [ValidateRange(30,3650)][int]$AuditRetentionDays = 730)
$ErrorActionPreference = "Stop"
$sqlcmd = "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE"
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Database must contain only letters, numbers, or underscore." }
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "SQLCMD is not installed." }
$query = "SET XACT_ABORT ON; DBCC CHECKDB ([$Database]) WITH NO_INFOMSGS; EXEC sys.sp_updatestats; DELETE dbo.operational_audit WHERE event_utc < DATEADD(day,-$AuditRetentionDays,SYSUTCDATETIME()); SELECT @@ROWCOUNT RemovedAuditRows;"
& $sqlcmd -S $ServerInstance -E -b -d $Database -Q $query
if ($LASTEXITCODE -ne 0) { throw "Database maintenance failed." }
Write-Host "Integrity, statistics and audit retention maintenance passed."
