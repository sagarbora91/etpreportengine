param(
    [string]$ServerInstance = '.\SQLEXPRESS',
    [string]$Database = 'EtpReporting',
    [string]$BackupDirectory = "$env:ProgramData\EtpReporting\Backups",
    [ValidateRange(0,1048576)][double]$MinimumFreeSpaceGb = 5,
    [string]$ResultPath,
    [string]$SqlCmdPath
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')

function Resolve-EtpLatestCertificateCustody {
    param([Parameter(Mandatory)][string]$BackupDirectory)
    $root = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
    $latest = Join-Path $root 'certificate-custody.json'
    Assert-EtpNoLinks $latest
    $pointer = Get-Content -Raw -LiteralPath $latest | ConvertFrom-Json
    if ($pointer.schemaVersion -ne 2 -or $pointer.certificateThumbprint -notmatch '^(?:[A-Fa-f0-9]{2}){20,64}$' -or
        -not [IO.Path]::IsPathRooted([string]$pointer.immutableReceiptPath)) { throw 'Export the current certificate to immutable recovery custody before backup.' }
    $immutable = [IO.Path]::GetFullPath([string]$pointer.immutableReceiptPath)
    if ([IO.Path]::GetDirectoryName($immutable) -ine $root -or $immutable -ieq $latest) { throw 'The certificate custody pointer must select an immutable receipt in this backup folder.' }
    Assert-EtpCertificateCustody -ReceiptPath $immutable -ExpectedThumbprint $pointer.certificateThumbprint
    return $immutable
}

# Dot-sourcing exposes the pointer resolver without starting a backup.
if ($MyInvocation.InvocationName -eq '.') { return }
if (-not $PSBoundParameters.ContainsKey('ServerInstance') -and -not $PSBoundParameters.ContainsKey('Database')) {
    $configuration = Get-EtpOperationsConfiguration
    $ServerInstance = $configuration.serverInstance; $Database = $configuration.database
}

Assert-EtpLocalSqlTarget $ServerInstance $Database
$sqlcmd = Resolve-EtpSqlCmd $SqlCmdPath
$ServerInstance = Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $ServerInstance
$directory = [IO.Path]::GetFullPath($BackupDirectory)
Assert-EtpNoLinks $directory
if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw 'Complete protected backup-folder setup first.' }
$certificateReceipt = Resolve-EtpLatestCertificateCustody $directory
$certificate = Get-Content -Raw -LiteralPath $certificateReceipt | ConvertFrom-Json
if ($certificate.certificateThumbprint -notmatch '^[A-Fa-f0-9]{40,128}$') { throw 'Certificate custody metadata is invalid.' }
$editionSupported = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "SET NOCOUNT ON; SELECT 'ETP_EDITION_SUPPORTED:'+CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Express%' OR CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Web%' THEN '0' ELSE '1' END;")
if (@($editionSupported | Where-Object { $_.Trim() -ceq 'ETP_EDITION_SUPPORTED:1' }).Count -ne 1) { throw 'Native encrypted backups require a supported SQL Server edition. SQL Express cannot create them.' }
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "IF NOT EXISTS(SELECT 1 FROM master.sys.certificates WHERE name='EtpBackupCert' AND thumbprint=0x$($certificate.certificateThumbprint)) THROW 51321,'Export the current backup certificate first.',1;" | Out-Null
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($directory))
if ($drive.AvailableFreeSpace / 1GB -lt $MinimumFreeSpaceGb) { throw 'Backup storage is below the required free-space limit.' }
$backupPath = Join-Path $directory "$Database-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-$([Guid]::NewGuid().ToString('N')).bak"
$files = @(Invoke-EtpOperationsBroker -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -BackupPath $backupPath -Operation BACKUP)
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "IF NOT EXISTS(SELECT 1 FROM master.sys.certificates WHERE name='EtpBackupCert' AND thumbprint=0x$($certificate.certificateThumbprint)) THROW 51321,'The backup certificate changed during backup; verify the encryption key before publishing a receipt.',1;" | Out-Null
$file = Get-Item -LiteralPath $backupPath
$receipt = [ordered]@{
    schemaVersion=2; verified=$true; serverInstance=$ServerInstance; database=$Database
    backupPath=$file.FullName; sha256=(Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash
    lengthBytes=$file.Length; verifiedAtUtc=[DateTime]::UtcNow.ToString('o'); encryption='AES_256'
    certificateReceipt=$certificateReceipt; certificateThumbprint=$certificate.certificateThumbprint
    files=$files
}
$receiptPath = "$backupPath.receipt.json"
Write-EtpJsonAtomically -Path $receiptPath -Value $receipt
$null = Read-EtpVerifiedReceipt -ReceiptPath $receiptPath -BackupDirectory $directory -Database $Database -SkipCertificateCheck
if ($ResultPath) { Write-EtpJsonAtomically -Path $ResultPath -Value $receipt }
Write-EtpJsonAtomically -Path (Join-Path $directory "$Database-latest-verified.json") -Value $receipt -Replace
# Pre-migration backups may precede this procedure. Only the trusted receipt
# recorder can publish dashboard health after verification evidence is durable.
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -Query "IF OBJECT_ID('dbo.record_verified_operation','P') IS NOT NULL EXEC dbo.record_verified_operation 'Backup','$($receipt.sha256)';" | Out-Null
# Record only after durable verification evidence exists. Upgrades may precede the audit procedure.
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -Query "IF OBJECT_ID('dbo.record_operational_audit','P') IS NOT NULL EXEC dbo.record_operational_audit 'Backup','Succeeded',N'Encrypted checksum backup verified',N'operations';" | Out-Null
# Rotation considers only valid receipts for this database. Unknown/unverified backups are never deleted.
$receipts = @()
foreach ($candidate in Get-ChildItem -LiteralPath $directory -Filter "$Database-*.bak.receipt.json" -File) {
    try { $receipts += Read-EtpVerifiedReceipt -ReceiptPath $candidate.FullName -BackupDirectory $directory -Database $Database -SkipCertificateCheck }
    catch { Write-Warning 'An older backup receipt needs review; its files were retained.' }
}
$keep = @(Get-EtpRetainedBackupReceipts $receipts | ForEach-Object backupPath)
foreach ($old in $receipts) {
    if ($old.backupPath -notin $keep) {
        # Read-EtpVerifiedReceipt already checked absolute containment and rejected junctions.
        Remove-Item -LiteralPath $old.backupPath -Force
        Remove-Item -LiteralPath "$($old.backupPath).receipt.json" -Force
    }
}
Write-Output 'Encrypted backup and verification completed.'
