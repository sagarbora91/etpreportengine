param(
    [string]$ServerInstance = ".\SQLEXPRESS",
    [string]$Database = "EtpReporting",
    [string]$OutputDirectory = "$env:USERPROFILE\Documents\EtpReportingSupport"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
Assert-EtpLocalSqlTarget $ServerInstance $Database
$sqlcmd = Resolve-EtpSqlCmd
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "SQLCMD is not installed at the expected SQL Server tools path." }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Database must contain only letters, numbers, or underscore." }
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
Assert-EtpNoLinks $resolvedOutput
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N')
$staging = Join-Path $resolvedOutput "support-$stamp"
$archive = Join-Path $resolvedOutput "EtpReporting-Support-$stamp.zip"
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $healthFile = Join-Path $staging "database-health.txt"
    try {
        $health = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -Query 'SET NOCOUNT ON; EXEC dbo.load_database_operational_health;')
    }
    catch {
        # Native SQL stderr can contain submitted values and server diagnostics.
        # The package is created only after the safe reader completes successfully.
        [Console]::Error.WriteLine('Support health query failed. Check database access and try again.')
        exit 1
    }
    @('DatabaseSizeMb | MaximumDatabaseSizeMb | VerifiedBackupUtc | FailedImportsLastDay | BackupSha256 | VerifiedRecoveryDrillUtc | RecoveryDrillBackupSha256') + $health |
        Out-File -LiteralPath $healthFile -Encoding utf8
    Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,OSArchitecture,LastBootUpTime |
        Format-List | Out-File -LiteralPath (Join-Path $staging "system.txt") -Encoding utf8
    Get-ScheduledTask -TaskName "ETP Reporting Daily Backup","ETP Reporting Monthly Recovery Drill","ETP Reporting Automated Operations" -ErrorAction SilentlyContinue |
        Select-Object TaskName,State | Format-List | Out-File -LiteralPath (Join-Path $staging "scheduled-tasks.txt") -Encoding utf8
    Set-Content -LiteralPath (Join-Path $staging "privacy.txt") -Value "This package contains aggregate health and environment metadata only. Source rows, customer data, invoice identifiers, workbook names and workbook paths are intentionally excluded."
    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $archive -CompressionLevel Optimal
    Write-Output 'Aggregate support package created.'
}
finally {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($staging)) -ine $resolvedOutput.TrimEnd('\')) { throw 'Support cleanup escaped its output folder.' }
    Assert-EtpNoLinks $staging
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
