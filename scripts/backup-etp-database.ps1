param(
    [string]$ServerInstance = '.\SQLEXPRESS',
    [string]$Database = 'EtpReporting',
    [string]$BackupDirectory = "$env:ProgramData\EtpReporting\Backups",
    [ValidateRange(0,1048576)][double]$MinimumFreeSpaceGb = 5,
    [string]$ResultPath,
    [string]$SqlCmdPath,
    # A pre-migration backup is the only copy of the database as it was before a schema
    # change, so it is recorded as such and rotation never deletes it. Setup's own backup
    # used to be removed by the same day's rotation: on the owner's PC on 25 September 2026
    # the 08:50 pre-migration file was already gone and only the 10:22 daily one remained.
    [ValidateSet('Scheduled','PreMigration','PreRollback')][string]$Purpose = 'Scheduled'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')

function Resolve-EtpLatestCertificateCustody {
    param([Parameter(Mandatory)][string]$BackupDirectory)
    $root = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
    $latest = Join-Path $root 'certificate-custody.json'
    Assert-EtpNoLinks $latest
    # An edition that encrypts has no unencrypted fallback: a backup taken without exported
    # recovery keys could never be restored. Say so. Until 25 September 2026 this arrived as
    # "Cannot find path ...certificate-custody.json", which stopped setup on Developer
    # Edition at its own pre-migration backup with nothing to act on.
    if (-not (Test-Path -LiteralPath $latest -PathType Leaf)) {
        throw 'This SQL Server edition encrypts backups, and no exported recovery keys were found. In the application, open Settings > Database > Encrypted backup recovery keys and select "Create and export recovery keys", then run this again.'
    }
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
# D9 revised: Express and Web cannot encrypt a backup, so on those editions the backup
# is taken unencrypted and the receipt records that plainly. Certificate custody is
# required only where a certificate is actually used to encrypt something.
$editionSupported = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "SET NOCOUNT ON; SELECT 'ETP_EDITION_SUPPORTED:'+CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Express%' OR CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Web%' THEN '0' ELSE '1' END;")
# Fail closed: an unrecognised probe result must not quietly produce an unencrypted
# backup on an edition that could have encrypted it. Exactly one answer, or stop.
$supported = @($editionSupported | Where-Object { $_.Trim() -ceq 'ETP_EDITION_SUPPORTED:1' }).Count
$unsupported = @($editionSupported | Where-Object { $_.Trim() -ceq 'ETP_EDITION_SUPPORTED:0' }).Count
if ($supported + $unsupported -ne 1) { throw 'The SQL Server edition could not be determined; refusing to take a backup whose protection is unknown.' }
$encrypts = $supported -eq 1
$certificateReceipt = $null; $certificateThumbprint = $null
if ($encrypts)
{
    $certificateReceipt = Resolve-EtpLatestCertificateCustody $directory
    $certificate = Get-Content -Raw -LiteralPath $certificateReceipt | ConvertFrom-Json
    if ($certificate.certificateThumbprint -notmatch '^[A-Fa-f0-9]{40,128}$') { throw 'Certificate custody metadata is invalid.' }
    $certificateThumbprint = $certificate.certificateThumbprint
    Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "IF NOT EXISTS(SELECT 1 FROM master.sys.certificates WHERE name='EtpBackupCert' AND thumbprint=0x$certificateThumbprint) THROW 51321,'Export the current backup certificate first.',1;" | Out-Null
}
else { Write-Warning 'This SQL Server edition cannot encrypt backups. The backup file is unencrypted; protect the backup folder at rest.' }
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($directory))
if ($drive.AvailableFreeSpace / 1GB -lt $MinimumFreeSpaceGb) {
    # Rotation runs only after a successful backup, so once the drive falls below the limit
    # the folder can never shrink by itself: the backup refuses, rotation never runs, and
    # every night after that fails the same way until somebody deletes files by hand.
    # Reclaim what retention no longer needs, then look again. This deletes nothing that a
    # successful backup would have kept, and no safety backup at all, so it cannot trade a
    # recovery point for disk space. Found reviewing the purpose change on 25 September 2026;
    # the defect is older than that change, which only makes the limit arrive sooner.
    $reclaimedBytes = Invoke-EtpBackupRotation -Directory $directory -Database $Database
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($directory))
    if ($drive.AvailableFreeSpace / 1GB -lt $MinimumFreeSpaceGb) {
        throw "Backup storage is below the required free-space limit. Expired backups were removed first and reclaimed $([math]::Round($reclaimedBytes / 1GB, 2)) GB, which is still not enough. Free space on this drive, or remove backups the retention policy is keeping."
    }
    Write-Warning "Backup storage was below the required free-space limit; removing expired backups reclaimed $([math]::Round($reclaimedBytes / 1GB, 2)) GB. Review how much this drive has left."
}
$backupPath = Join-Path $directory "$Database-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))-$([Guid]::NewGuid().ToString('N')).bak"
$files = @(Invoke-EtpOperationsBroker -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -BackupPath $backupPath -Operation BACKUP)
if ($encrypts) { Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "IF NOT EXISTS(SELECT 1 FROM master.sys.certificates WHERE name='EtpBackupCert' AND thumbprint=0x$certificateThumbprint) THROW 51321,'The backup certificate changed during backup; verify the encryption key before publishing a receipt.',1;" | Out-Null }
$file = Get-Item -LiteralPath $backupPath
$receipt = [ordered]@{
    schemaVersion=2; verified=$true; serverInstance=$ServerInstance; database=$Database
    backupPath=$file.FullName; sha256=(Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash
    lengthBytes=$file.Length; verifiedAtUtc=[DateTime]::UtcNow.ToString('o')
    encryption=$(if ($encrypts) { 'AES_256' } else { 'NONE' })
    # New in this receipt, and deliberately not a schema bump: a receipt without it is an
    # ordinary scheduled backup, so an installation still running the previous scripts
    # reads these receipts and an older receipt is read here.
    purpose=$(switch ($Purpose) { 'PreMigration' { 'PRE_MIGRATION' } 'PreRollback' { 'PRE_ROLLBACK' } default { 'SCHEDULED' } })
    certificateReceipt=$certificateReceipt; certificateThumbprint=$certificateThumbprint
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
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -Query "IF OBJECT_ID('dbo.record_operational_audit','P') IS NOT NULL EXEC dbo.record_operational_audit 'Backup','Succeeded',N'$(if ($encrypts) { "Encrypted checksum backup verified" } else { "Checksum backup verified, not encrypted" })',N'operations';" | Out-Null
# Rotation considers only valid receipts for this database. Unknown/unverified backups are never deleted.
$null = Invoke-EtpBackupRotation -Directory $directory -Database $Database
Write-Output $(if ($encrypts) { 'Encrypted backup and verification completed.' } else { 'Backup and verification completed. The file is NOT encrypted on this SQL Server edition; protect the backup folder at rest.' })
