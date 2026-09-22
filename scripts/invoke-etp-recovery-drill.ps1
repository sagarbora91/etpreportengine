param(
    [string]$ServerInstance='.\SQLEXPRESS',
    [string]$Database='EtpReporting',
    [string]$BackupDirectory="$env:ProgramData\EtpReporting\Backups",
    [string]$SqlCmdPath
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
# Always read, including when the application passes the database: the result is recorded
# under the automation account's database user (see Invoke-EtpSqlAsAutomationUser).
$configuration=Get-EtpOperationsConfiguration
if (-not $PSBoundParameters.ContainsKey('ServerInstance') -and -not $PSBoundParameters.ContainsKey('Database')) {
    $ServerInstance=$configuration.serverInstance; $Database=$configuration.database
}
Assert-EtpLocalSqlTarget $ServerInstance $Database
$sqlcmd=Resolve-EtpSqlCmd $SqlCmdPath
$ServerInstance=Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $ServerInstance
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
# The drill ran as a SQL administrator; what follows runs code in the application database,
# so it runs with the automation account's database rights and nothing more.
Invoke-EtpSqlAsAutomationUser -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -AutomationPrincipal $configuration.automationPrincipal -Query "EXEC dbo.record_verified_operation 'RestoreDrill','$($receipt.sha256)';" | Out-Null
# The audit trail must describe the backup that was actually drilled. Claiming an
# encrypted restore for an unencrypted backup would put a false statement into an
# append-only compliance record, which is worse than recording nothing.
$drillDetail = if ($receipt.encryption -ceq 'AES_256') { 'Isolated encrypted restore and backup metadata checks passed' } else { 'Isolated restore and backup metadata checks passed; the backup was not encrypted' }
Invoke-EtpSqlAsAutomationUser -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -AutomationPrincipal $configuration.automationPrincipal -Query "EXEC dbo.record_operational_audit 'RestoreDrill','Succeeded',N'$drillDetail',N'operations';" | Out-Null
Write-Output 'Receipt-verified recovery drill completed.'
