param(
    [Parameter(Mandatory)][string]$ApplicationDirectory,
    [switch]$SkipSqlInstallation,
    [ValidateRange(0.1, 1048576)][double]$MinimumBackupFreeSpaceGb = 5
)

$ErrorActionPreference = "Stop"
$serviceName = 'MSSQL$SQLEXPRESS'
$ServerInstance = '.\SQLEXPRESS'
$Database = 'EtpReporting'
$applicationRoot = [System.IO.Path]::GetFullPath($ApplicationDirectory)
$application = Join-Path $applicationRoot 'Etp.Reporting.Desktop.exe'
$scripts = Join-Path $applicationRoot 'scripts'
$migrationDirectory = Join-Path $applicationRoot 'database\migrations'
$backupScript = Join-Path $scripts 'backup-etp-database.ps1'
$backupDirectory = Join-Path $env:ProgramData 'EtpReporting\Backups'
$logDirectory = Join-Path $env:ProgramData 'EtpReporting\SetupLogs'
$databaseExistedBeforeMigration = $false
$migrationPhaseStarted = $false
$migrationPhaseCompleted = $false
$preMigrationBackupPath = $null

New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory "bootstrap-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff').log"

function Write-SetupLog([string]$message) {
    $line = "$(Get-Date -Format o) $message"
    Add-Content -LiteralPath $logPath -Value $line -Encoding utf8
    Write-Host $message
}

trap {
    Write-SetupLog "FAILED: $($_.Exception.GetType().Name): $($_.Exception.Message)"
    if ($migrationPhaseStarted -and -not $migrationPhaseCompleted) {
        if ($databaseExistedBeforeMigration -and -not [string]::IsNullOrWhiteSpace($preMigrationBackupPath)) {
            Write-SetupLog "No automatic restore or reverse migration was attempted. The verified pre-migration backup remains at $preMigrationBackupPath. Diagnose the failure before using the documented manual restore procedure."
        }
        elseif ($databaseExistedBeforeMigration) {
            Write-SetupLog 'No automatic restore, reverse migration, database deletion, or user-data deletion was attempted. The existing database is retained for diagnosis.'
        }
        else {
            Write-SetupLog 'No automatic reverse migration, database deletion, or user-data deletion was attempted. The failed clean-install database state is retained for diagnosis.'
        }
    }
    exit 1
}

function Resolve-SqlCmdPath {
    $command = Get-Command sqlcmd.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path $env:ProgramFiles 'sqlcmd\sqlcmd.exe'),
        'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory)][string]$Query,
        [string]$TargetDatabase
    )

    $arguments = @('-S', $ServerInstance, '-E', '-b', '-h', '-1', '-W')
    if (-not [string]::IsNullOrWhiteSpace($TargetDatabase)) { $arguments += @('-d', $TargetDatabase) }
    $arguments += @('-Q', $Query)
    $output = @(& $sqlcmdPath @arguments)
    if ($LASTEXITCODE -ne 0) { throw "SQL Server preflight query failed with exit code $LASTEXITCODE." }
    $lines = @($output | ForEach-Object { "$_".Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -ne 1) { throw "SQL Server preflight query returned an unexpected result." }
    return $lines[0]
}

function Invoke-SqlHealthCommand {
    param([Parameter(Mandatory)][string]$Query)

    & $sqlcmdPath -S $ServerInstance -E -b -d $Database -Q $Query | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Post-migration database health verification failed with exit code $LASTEXITCODE." }
}

function Assert-VerifiedBackupReceipt {
    param([Parameter(Mandatory)][string]$ReceiptPath)

    if (-not (Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) { throw "The pre-migration backup did not produce a verification receipt." }
    $receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
    if ($receipt.schemaVersion -ne 1 -or $receipt.verified -ne $true) { throw "The pre-migration backup receipt is not a verified version-1 receipt." }
    if ($receipt.database -cne $Database -or $receipt.serverInstance -cne $ServerInstance) { throw "The pre-migration backup receipt does not match the target database." }
    if ([string]::IsNullOrWhiteSpace($receipt.backupPath) -or [string]::IsNullOrWhiteSpace($receipt.sha256)) { throw "The pre-migration backup receipt is incomplete." }

    $resolvedBackupDirectory = [System.IO.Path]::GetFullPath($backupDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedBackupPath = [System.IO.Path]::GetFullPath([string]$receipt.backupPath)
    $backupPrefix = $resolvedBackupDirectory + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedBackupPath.StartsWith($backupPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "The verified backup is outside the configured backup directory." }
    if (-not (Test-Path -LiteralPath $resolvedBackupPath -PathType Leaf)) { throw "The verified pre-migration backup file is missing." }

    $backupFile = Get-Item -LiteralPath $resolvedBackupPath
    if ($backupFile.Length -le 0 -or $backupFile.Length -ne [long]$receipt.lengthBytes) { throw "The verified pre-migration backup file length does not match its receipt." }
    $actualHash = (Get-FileHash -LiteralPath $resolvedBackupPath -Algorithm SHA256).Hash
    if (-not $actualHash.Equals([string]$receipt.sha256, [StringComparison]::OrdinalIgnoreCase)) { throw "The verified pre-migration backup hash does not match its receipt." }

    $script:preMigrationBackupPath = $resolvedBackupPath
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'ETP prerequisite configuration requires administrator rights.'
}
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) { throw "Application executable is missing." }
if (-not (Test-Path -LiteralPath $backupScript -PathType Leaf)) { throw "The database backup script is missing." }
if (-not (Test-Path -LiteralPath $migrationDirectory -PathType Container)) { throw "The bundled migration directory is missing." }
$migrationFiles = @(Get-ChildItem -LiteralPath $migrationDirectory -Filter '*.sql' -File)
if ($migrationFiles.Count -eq 0) { throw "No bundled database migrations were found." }

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service -and -not $SkipSqlInstallation) {
    Write-SetupLog 'SQL Server Express is absent; installing the official Microsoft SQL Server 2022 Express package.'
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) { throw 'Windows Package Manager is required for automatic SQL Server installation. For offline/manual deployment, install SQL Server 2022 Express and Microsoft Sqlcmd first, then retry with -SkipSqlInstallation.' }
    & $winget.Source install --id Microsoft.SQLServer.2022.Express --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw "SQL Server Express installation failed with exit code $LASTEXITCODE." }
    $deadline = [DateTime]::UtcNow.AddMinutes(3)
    do { Start-Sleep -Seconds 3; $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue } while (-not $service -and [DateTime]::UtcNow -lt $deadline)
}
if (-not $service) { throw 'The SQLEXPRESS database-engine service is not installed. Offline/manual deployment can preinstall SQL Server 2022 Express before rerunning this bootstrap.' }

Set-Service -Name $serviceName -StartupType Automatic
if ((Get-Service -Name $serviceName).Status -ne 'Running') { Start-Service -Name $serviceName }
Write-SetupLog 'SQLEXPRESS is installed, configured for automatic startup, and running.'

$sqlcmdPath = Resolve-SqlCmdPath
if (-not $sqlcmdPath -and -not $SkipSqlInstallation) {
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) { throw 'Windows Package Manager is required for automatic Microsoft Sqlcmd installation. Offline/manual deployment can preinstall Sqlcmd and retry with -SkipSqlInstallation.' }
    Write-SetupLog 'Microsoft Sqlcmd is absent; installing the official Microsoft package.'
    & $winget.Source install --id Microsoft.Sqlcmd --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    if ($LASTEXITCODE -ne 0) { throw "Microsoft Sqlcmd installation failed with exit code $LASTEXITCODE." }
    $sqlcmdPath = Resolve-SqlCmdPath
}
if (-not $sqlcmdPath) { throw 'Microsoft Sqlcmd is not installed. Install it from approved offline media or an approved package source, then retry.' }

$serverProperties = Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT CONVERT(varchar(10),SERVERPROPERTY('ProductMajorVersion')) + '|' + CONVERT(varchar(10),SERVERPROPERTY('EngineEdition'));"
$serverParts = $serverProperties.Split('|')
$serverMajorVersion = 0
if ($serverParts.Count -ne 2 -or -not [int]::TryParse($serverParts[0], [ref]$serverMajorVersion)) { throw "SQL Server returned an unreadable version result." }
if ($serverMajorVersion -lt 16) { throw "SQL Server 2022 or newer is required; detected major version $serverMajorVersion." }
Write-SetupLog "SQL Server compatibility preflight passed (major version $serverMajorVersion, engine edition $($serverParts[1]))."

New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
& icacls.exe $backupDirectory /grant 'NT SERVICE\MSSQL$SQLEXPRESS:(OI)(CI)M' /T /C | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not grant the SQL Server service access to the backup directory.' }

$databaseState = Invoke-SqlScalar -Query "SET NOCOUNT ON; IF DB_ID(N'$Database') IS NULL SELECT 'MISSING' ELSE SELECT 'EXISTS';"
$databaseExistedBeforeMigration = $databaseState -ceq 'EXISTS'
if (-not $databaseExistedBeforeMigration -and $databaseState -cne 'MISSING') { throw "SQL Server returned an unexpected database-existence result." }

if ($databaseExistedBeforeMigration) {
    $databaseProperties = Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT state_desc + '|' + user_access_desc + '|' + CONVERT(varchar(5),is_read_only) + '|' + CONVERT(varchar(5),compatibility_level) FROM sys.databases WHERE name=N'$Database';"
    $databaseParts = $databaseProperties.Split('|')
    $compatibilityLevel = 0
    if ($databaseParts.Count -ne 4 -or -not [int]::TryParse($databaseParts[3], [ref]$compatibilityLevel)) { throw "SQL Server returned unreadable database compatibility information." }
    if ($databaseParts[0] -cne 'ONLINE' -or $databaseParts[1] -cne 'MULTI_USER' -or $databaseParts[2] -cne '0') { throw "The existing database must be ONLINE, MULTI_USER, and read-write before migration." }
    if ($compatibilityLevel -lt 150) { throw "The existing database compatibility level is $compatibilityLevel; level 150 or newer is required before migration." }

    $appliedMigrationCount = [long](Invoke-SqlScalar -TargetDatabase $Database -Query "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.schema_migrations',N'U') IS NULL SELECT CONVERT(bigint,0) ELSE SELECT COUNT_BIG(1) FROM dbo.schema_migrations;")
    if ($appliedMigrationCount -lt $migrationFiles.Count) {
        $databaseSizeMb = [double](Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT CONVERT(decimal(18,2),SUM(size)*8.0/1024.0) FROM sys.master_files WHERE database_id=DB_ID(N'$Database');")
        $requiredFreeSpaceGb = [math]::Ceiling(([math]::Max($MinimumBackupFreeSpaceGb, ($databaseSizeMb / 1024.0) * 1.25)) * 100) / 100
        $receiptPath = Join-Path $logDirectory "pre-migration-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')-$([Guid]::NewGuid().ToString('N')).json"
        Write-SetupLog 'Existing database has pending bundled migrations. Creating and verifying a pre-migration backup before any migration runs.'
        & $backupScript -ServerInstance $ServerInstance -Database $Database -BackupDirectory $backupDirectory -MinimumFreeSpaceGb $requiredFreeSpaceGb -ResultPath $receiptPath -SqlCmdPath $sqlcmdPath
        Assert-VerifiedBackupReceipt -ReceiptPath $receiptPath
        Write-SetupLog "Verified pre-migration backup is retained at $preMigrationBackupPath."
    }
    else {
        Write-SetupLog 'The existing database has no pending bundled migrations by journal count; bootstrap will still validate migration IDs and checksums.'
    }
}
else {
    Write-SetupLog 'EtpReporting does not exist. A clean database will be created; no pre-migration backup is applicable.'
}

$migrationPhaseStarted = $true
$process = Start-Process -FilePath $application -ArgumentList '--initialize-database' -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "EtpReporting database migration failed with exit code $($process.ExitCode). Review the privacy-safe setup log." }

$postState = Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT state_desc + '|' + CONVERT(varchar(5),is_read_only) FROM sys.databases WHERE name=N'$Database';"
if ($postState -cne 'ONLINE|0') { throw "Post-migration health verification requires the database to be ONLINE and read-write." }
$postMigrationCount = [long](Invoke-SqlScalar -TargetDatabase $Database -Query "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.schema_migrations',N'U') IS NULL THROW 51000,'Migration journal is missing.',1; SELECT COUNT_BIG(1) FROM dbo.schema_migrations;")
if ($postMigrationCount -ne $migrationFiles.Count) { throw "Post-migration health verification found $postMigrationCount applied migrations; $($migrationFiles.Count) bundled migrations are required." }
Invoke-SqlHealthCommand -Query "SET NOCOUNT ON; DBCC CHECKDB ([$Database]) WITH NO_INFOMSGS;"
$migrationPhaseCompleted = $true
Write-SetupLog 'EtpReporting migration completed and post-migration state, journal count, and DBCC integrity checks passed.'

& (Join-Path $scripts 'install-daily-backup-task.ps1')
& (Join-Path $scripts 'install-monthly-recovery-drill-task.ps1')
& (Join-Path $scripts 'install-etp-automation-task.ps1')
Write-SetupLog 'Daily backup, monthly recovery-drill and five-minute ETP automation tasks are installed.'
Write-SetupLog 'ETP prerequisite bootstrap completed successfully.'
