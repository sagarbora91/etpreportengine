param(
    [Parameter(Mandatory)][string]$ApplicationDirectory,
    # Retained for installer compatibility; prerequisites now require manual installation.
    [switch]$SkipSqlInstallation,
    [ValidateRange(0.1, 1048576)][double]$MinimumBackupFreeSpaceGb = 5
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')

function Resolve-EtpBootstrapServiceName {
    param([Parameter(Mandatory)][string]$ServerInstance,[Parameter(Mandatory)][string]$Database)
    Assert-EtpLocalSqlTarget $ServerInstance $Database
    $server = $ServerInstance.Trim()
    if ($server -match '^(?i)(lpc|np):') { $server = $server.Substring($server.IndexOf(':')+1) }
    if ($server.StartsWith('\\') -or $server.StartsWith('(localdb)',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Bootstrap requires a standard local SQL Server service endpoint. Configure a supported default or named instance manually first.'
    }
    $parts = $server.Split('\')
    if ($parts.Count -eq 1) { return 'MSSQLSERVER' }
    return 'MSSQL$'+$parts[1]
}

function Assert-EtpBootstrapSqlEdition {
    param([int]$MajorVersion,[int]$EngineEdition,[string]$Edition)
    if ($MajorVersion -lt 16) { throw 'SQL Server 2022 or newer is required. Install a supported edition manually before bootstrap.' }
    if ($EngineEdition -notin @(2,3) -or $Edition -match '(?i)Express|Web') {
        throw 'Native encrypted backups require a supported SQL Server edition. Express and Web cannot create them; install a supported edition manually before bootstrap.'
    }
}

function Assert-EtpBootstrapPayloads {
    param([Parameter(Mandatory)][string]$ApplicationDirectory)
    # Check before any elevated executable, migration or companion script runs.
    # A protected directory can still contain an explicitly writable child file.
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue([IO.Path]::GetFullPath($ApplicationDirectory))
    while ($pending.Count -gt 0) {
        $item = $pending.Dequeue()
        Assert-EtpProtectedInstall $item
        if (Test-Path -LiteralPath $item -PathType Container) {
            foreach ($child in Get-ChildItem -LiteralPath $item -Force) { $pending.Enqueue($child.FullName) }
        }
    }
}

# Dot-sourcing exposes only the pure preflight functions for behavioral tests.
if ($MyInvocation.InvocationName -eq '.') { return }

$setupPreflightValidated = $false
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

$logPath = Join-Path $logDirectory "bootstrap-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff').log"

function Write-SetupLog([string]$message) {
    $line = "$(Get-Date -Format o) $message"
    if ($setupPreflightValidated -and (Test-Path -LiteralPath $logDirectory -PathType Container)) { Add-Content -LiteralPath $logPath -Value $line -Encoding utf8 }
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
    try { return Resolve-EtpSqlCmd } catch { return $null }
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory)][string]$Query,
        [string]$TargetDatabase
    )

    $arguments = @('-S', $ServerInstance, '-E', '-b', '-h', '-1', '-W')
    if (-not [string]::IsNullOrWhiteSpace($TargetDatabase)) { $arguments += @('-d', $TargetDatabase) }
    $arguments += @('-Q', $Query)
    $output = @(& $sqlcmdPath -x @arguments)
    if ($LASTEXITCODE -ne 0) { throw "SQL Server preflight query failed with exit code $LASTEXITCODE." }
    $lines = @($output | ForEach-Object { "$_".Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -ne 1) { throw "SQL Server preflight query returned an unexpected result." }
    return $lines[0]
}

function Invoke-SqlHealthCommand {
    param([Parameter(Mandatory)][string]$Query)

    & $sqlcmdPath -x -S $ServerInstance -E -b -d $Database -Q $Query | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Post-migration database health verification failed with exit code $LASTEXITCODE." }
}

function Assert-VerifiedBackupReceipt {
    param([Parameter(Mandatory)][string]$ReceiptPath)

    $receipt=Read-EtpVerifiedReceipt -ReceiptPath $ReceiptPath -BackupDirectory $backupDirectory -Database $Database -SkipCertificateCheck
    if ($receipt.serverInstance -cne $ServerInstance) { throw 'The verified backup does not match the target server.' }
    $script:preMigrationBackupPath=$receipt.backupPath
}

$operationConfiguration = Get-EtpOperationsConfiguration
Assert-EtpBootstrapPayloads $applicationRoot
if ($operationConfiguration.allowAutomationFolderAccess -ne $true) { throw 'Resolve automation folder access in the protected machine configuration before bootstrap.' }
$ServerInstance = [string]$operationConfiguration.serverInstance
$Database = [string]$operationConfiguration.database
$serviceName = Resolve-EtpBootstrapServiceName $ServerInstance $Database

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
if (-not $service) { throw 'The configured database-engine service is not installed. Manually install a SQL Server edition supporting native encrypted backups, then retry.' }
if ($service.Status -ne 'Running') { throw 'Start the configured SQL Server service manually before bootstrap so its edition can be verified before changes are made.' }

$sqlcmdPath = Resolve-SqlCmdPath
if (-not $sqlcmdPath) { throw 'Microsoft Sqlcmd is not installed. Install it from approved offline media or an approved package source, then retry.' }

$serverProperties = Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT CONVERT(varchar(10),SERVERPROPERTY('ProductMajorVersion')) + '|' + CONVERT(varchar(10),SERVERPROPERTY('EngineEdition')) + '|' + CONVERT(varchar(128),SERVERPROPERTY('Edition'));"
$serverParts = $serverProperties.Split('|')
$serverMajorVersion = 0
$serverEngineEdition = 0
if ($serverParts.Count -ne 3 -or -not [int]::TryParse($serverParts[0], [ref]$serverMajorVersion) -or -not [int]::TryParse($serverParts[1], [ref]$serverEngineEdition)) { throw "SQL Server returned an unreadable version result." }
Assert-EtpBootstrapSqlEdition $serverMajorVersion $serverEngineEdition $serverParts[2]
Write-SetupLog "SQL Server compatibility preflight passed (major version $serverMajorVersion, engine edition $($serverParts[1]))."

Set-Service -Name $serviceName -StartupType Automatic
& (Join-Path $PSScriptRoot 'initialize-etp-operation-folders.ps1') -SqlServiceIdentity ('NT SERVICE\'+$serviceName) -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $operationConfiguration.automationPrincipal -GrantAutomationFolderAccess
$setupPreflightValidated = $true


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
    Write-SetupLog 'The configured database does not exist. A clean database will be created; no pre-migration backup is applicable.'
}

$migrationPhaseStarted = $true
$process = Start-Process -FilePath $application -ArgumentList '--initialize-configured-database' -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "Configured database migration failed with exit code $($process.ExitCode). Review the privacy-safe setup log." }

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
