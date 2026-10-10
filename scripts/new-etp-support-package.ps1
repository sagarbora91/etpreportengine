param(
    [string]$ServerInstance = ".\SQLEXPRESS",
    [string]$Database = "EtpReporting",
    [string]$OutputDirectory = "$env:USERPROFILE\Documents\EtpReportingSupport",
    # 1.9.9 (IE-CODE-02). The application's diagnostics log: the folder it writes to, which is
    # ETP_DIAGNOSTICS_DIRECTORY when that is a full path (test runs), else
    # %LOCALAPPDATA%\EtpReporting\Logs of the account creating the package.
    [string]$DiagnosticsDirectory,
    [ValidateRange(1,180)][int]$DiagnosticsDays = 30
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')

function Resolve-EtpSupportDiagnosticsDirectory {
    # The same rule as DesktopDiagnostics.DefaultDirectory.
    param([AllowNull()][string]$Configured,[AllowNull()][string]$Environment,[AllowNull()][string]$LocalApplicationData)
    if (-not [string]::IsNullOrWhiteSpace($Configured)) { return $Configured }
    if (-not [string]::IsNullOrWhiteSpace($Environment) -and [IO.Path]::IsPathRooted($Environment) -and $Environment -match '^(?:[A-Za-z]:\\|\\\\)') { return $Environment }
    return (Join-Path $LocalApplicationData 'EtpReporting\Logs')
}

function Copy-EtpSupportDiagnostics {
    # Every message in the support text said "Technical details are available in the support
    # package", and the package held no diagnostics at all. Copies the application's
    # diagnostics-*.jsonl files written in the last $Days days - free of identities and business
    # values by design (DesktopDiagnostics) - and returns how many were copied. A missing folder
    # copies nothing; a linked folder or file is never followed.
    param([Parameter(Mandatory)][string]$SourceDirectory,[Parameter(Mandatory)][string]$Destination,[int]$Days = 30,[Nullable[datetime]]$Now)
    $timestamp = if ($null -ne $Now) { [datetime]$Now } else { [datetime]::Now }
    $source = [IO.Path]::GetFullPath($SourceDirectory)
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { return 0 }
    Assert-EtpNoLinks $source
    $cutoff = $timestamp.AddDays(-$Days)
    $files = @(Get-ChildItem -LiteralPath $source -Filter 'diagnostics-*.jsonl' -File |
        Where-Object { $_.Name -match '^diagnostics-[A-Za-z0-9-]+\.jsonl$' -and $_.LastWriteTime -ge $cutoff -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) } |
        Sort-Object Name)
    if ($files.Count -eq 0) { return 0 }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) }
    return $files.Count
}

# Dot-sourcing exposes the functions above without creating a package.
if ($MyInvocation.InvocationName -eq '.') { return }
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
    $diagnosticsSource = Resolve-EtpSupportDiagnosticsDirectory -Configured $DiagnosticsDirectory -Environment $env:ETP_DIAGNOSTICS_DIRECTORY -LocalApplicationData $env:LOCALAPPDATA
    $diagnosticsCount = 0
    try { $diagnosticsCount = Copy-EtpSupportDiagnostics -SourceDirectory $diagnosticsSource -Destination (Join-Path $staging 'diagnostics') -Days $DiagnosticsDays }
    catch {
        # The package is still worth sending without the log; say so instead of failing.
        Write-Warning 'The application diagnostics log could not be added to the support package.'
        if (Test-Path -LiteralPath (Join-Path $staging 'diagnostics')) { Remove-Item -LiteralPath (Join-Path $staging 'diagnostics') -Recurse -Force }
        $diagnosticsCount = -1
    }
    Set-Content -LiteralPath (Join-Path $staging "privacy.txt") -Value "This package contains aggregate health and environment metadata, and the application diagnostics log of the last $DiagnosticsDays days, which records errors by code and type without customer names, phone numbers or business values. Source rows, customer data, invoice identifiers, workbook names and workbook paths are intentionally excluded."
    Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $archive -CompressionLevel Optimal
    Write-Output 'Aggregate support package created.'
    # 1.9.9 (IE-CODE-02). The application shows where the package was saved; it never said.
    Write-Output ('ETP_SAVED:' + $archive)
    if ($diagnosticsCount -eq 0) { Write-Output "ETP_NOTICE:No application diagnostics log from the last $DiagnosticsDays days was found to include." }
    elseif ($diagnosticsCount -lt 0) { Write-Output 'ETP_NOTICE:The application diagnostics log could not be added to the package.' }
}
finally {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($staging)) -ine $resolvedOutput.TrimEnd('\')) { throw 'Support cleanup escaped its output folder.' }
    Assert-EtpNoLinks $staging
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
