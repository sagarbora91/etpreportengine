param([string]$ServerInstance = ".\SQLEXPRESS", [string]$Database = "EtpReporting")
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
Assert-EtpLocalSqlTarget $ServerInstance $Database
$sqlcmd = Resolve-EtpSqlCmd
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "SQLCMD is not installed at the expected SQL Server tools path." }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Database must contain only letters, numbers, or underscore." }
& $sqlcmd -x -S $ServerInstance -E -b -d $Database -Q "SET NOCOUNT ON; DBCC CHECKDB ([$Database]) WITH NO_INFOMSGS; SELECT DB_NAME() DatabaseName,COUNT(*) AppliedMigrations FROM dbo.schema_migrations; SELECT (SELECT COUNT(*) FROM dbo.import_files) ImportedFiles,(SELECT COUNT(*) FROM dbo.source_lineage) LineageRows;"
if ($LASTEXITCODE -ne 0) { throw "Database health check failed." }
