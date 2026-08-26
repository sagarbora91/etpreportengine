param(
    [Parameter(Mandatory)][string]$ApplicationDirectory,
    [switch]$SkipSqlInstallation
)

$ErrorActionPreference = "Stop"
$serviceName = 'MSSQL$SQLEXPRESS'
$applicationRoot = [System.IO.Path]::GetFullPath($ApplicationDirectory)
$application = Join-Path $applicationRoot 'Etp.Reporting.Desktop.exe'
$scripts = Join-Path $applicationRoot 'scripts'
$backupDirectory = Join-Path $env:ProgramData 'EtpReporting\Backups'
$logDirectory = Join-Path $env:ProgramData 'EtpReporting\SetupLogs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory "bootstrap-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

function Write-SetupLog([string]$message) {
    $line = "$(Get-Date -Format o) $message"
    Add-Content -LiteralPath $logPath -Value $line -Encoding utf8
    Write-Host $message
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'ETP prerequisite configuration requires administrator rights.'
}
if (-not (Test-Path -LiteralPath $application)) { throw "Application executable is missing." }

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service -and -not $SkipSqlInstallation) {
    Write-SetupLog 'SQL Server Express is absent; installing the official Microsoft SQL Server 2022 Express package.'
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) { throw 'Windows Package Manager is required to install SQL Server Express. Update App Installer and retry.' }
    & $winget.Source install --id Microsoft.SQLServer.2022.Express --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw "SQL Server Express installation failed with exit code $LASTEXITCODE." }
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    do { Start-Sleep -Seconds 3; $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue } while (-not $service -and [DateTime]::UtcNow -lt $deadline)
}
if (-not $service) { throw 'The SQLEXPRESS database-engine service is not installed.' }

Set-Service -Name $serviceName -StartupType Automatic
if ((Get-Service -Name $serviceName).Status -ne 'Running') { Start-Service -Name $serviceName }
Write-SetupLog 'SQLEXPRESS is installed, configured for automatic startup, and running.'

$sqlcmd = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
if (-not $sqlcmd) {
    $legacySqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
    if (-not (Test-Path -LiteralPath $legacySqlcmd)) {
        $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
        if (-not $winget) { throw 'Windows Package Manager is required to install Microsoft Sqlcmd.' }
        Write-SetupLog 'Microsoft Sqlcmd is absent; installing the official Microsoft package.'
        & $winget.Source install --id Microsoft.Sqlcmd --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
        if ($LASTEXITCODE -ne 0) { throw "Microsoft Sqlcmd installation failed with exit code $LASTEXITCODE." }
    }
}

New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
& icacls.exe $backupDirectory /grant 'NT SERVICE\MSSQL$SQLEXPRESS:(OI)(CI)M' /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not grant the SQL Server service access to the backup directory.' }

$process = Start-Process -FilePath $application -ArgumentList '--initialize-database' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'EtpReporting database initialization failed. Review the privacy-safe setup log.' }
Write-SetupLog 'EtpReporting database exists and all bundled migrations are applied.'

& (Join-Path $scripts 'install-daily-backup-task.ps1')
& (Join-Path $scripts 'install-monthly-recovery-drill-task.ps1')
Write-SetupLog 'Daily backup and monthly recovery-drill tasks are installed.'
Write-SetupLog 'ETP prerequisite bootstrap completed successfully.'
