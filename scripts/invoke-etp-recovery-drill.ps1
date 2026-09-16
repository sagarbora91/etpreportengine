param(
    [string]$ServerInstance='.\SQLEXPRESS',
    [string]$Database='EtpReporting',
    [string]$BackupDirectory="$env:ProgramData\EtpReporting\Backups"
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
if (-not $PSBoundParameters.ContainsKey('ServerInstance') -and -not $PSBoundParameters.ContainsKey('Database')) {
    $configuration=Get-EtpOperationsConfiguration
    $ServerInstance=$configuration.serverInstance; $Database=$configuration.database
}
Assert-EtpLocalSqlTarget $ServerInstance $Database
$sqlcmd=Resolve-EtpSqlCmd
$directory=[IO.Path]::GetFullPath($BackupDirectory)
$receipt=Read-EtpVerifiedReceipt -ReceiptPath (Join-Path $directory "$Database-latest-verified.json") -BackupDirectory $directory -Database $Database
$metadata=@(Invoke-EtpOperationsBroker -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -BackupPath $receipt.backupPath -Operation METADATA)
$expected=($receipt.files | Sort-Object fileId | ConvertTo-Json -Depth 5 -Compress)
$actual=($metadata | Sort-Object fileId | ConvertTo-Json -Depth 5 -Compress)
if ($expected -cne $actual) { throw 'Backup metadata does not match the verification receipt.' }
# No live table counts: imports after this backup cannot cause a false recovery failure.
Invoke-EtpOperationsBroker -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -BackupPath $receipt.backupPath -Operation DRILL | Out-Null
# Detect changes that occurred while SQL read the backup, before recording success.
$null=Read-EtpVerifiedReceipt -ReceiptPath (Join-Path $directory "$Database-latest-verified.json") -BackupDirectory $directory -Database $Database
if ((Get-FileHash -LiteralPath $receipt.backupPath -Algorithm SHA256).Hash -ine $receipt.sha256) { throw 'The backup changed during the recovery drill.' }
$result=[ordered]@{ schemaVersion=1; succeeded=$true; backupSha256=$receipt.sha256; completedAtUtc=[DateTime]::UtcNow.ToString('o') }
Write-EtpJsonAtomically -Path (Join-Path $directory "$Database-latest-drill.json") -Value $result -Replace
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -Query "EXEC dbo.record_verified_operation 'RestoreDrill','$($receipt.sha256)';" | Out-Null
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -Query "EXEC dbo.record_operational_audit 'RestoreDrill','Succeeded',N'Isolated encrypted restore and backup metadata checks passed',N'operations';" | Out-Null
Write-Output 'Receipt-verified recovery drill completed.'
