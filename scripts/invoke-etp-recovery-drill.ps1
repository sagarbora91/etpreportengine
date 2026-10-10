param(
    [string]$ServerInstance='.\SQLEXPRESS',
    [string]$Database='EtpReporting',
    [string]$BackupDirectory="$env:ProgramData\EtpReporting\Backups",
    [string]$SqlCmdPath,
    # A4.4a (1.9.3). Drill the backup another receipt names instead of
    # <database>-latest-verified.json. It must be a .json file directly in the backup folder,
    # and is verified exactly like the latest receipt. Used to prove that a receipt whose row
    # counts were altered makes the drill fail (docs\OPERATIONS.md).
    [string]$ReceiptPath
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
# 1.9.9 (IE-CODE-01, IE-CODE-10). Every failure below is logged under <backup folder>\Logs and
# reported to the application on its own line, then rethrown unchanged: the monthly drill task
# used to leave nothing but Task Scheduler's "last result 0x1".
$failure = $null
try {
    # Always read, including when the application passes the database: the result is recorded
    # under the automation account's database user (see Invoke-EtpSqlAsAutomationUser).
    $configuration=Get-EtpOperationsConfiguration
    if (-not $PSBoundParameters.ContainsKey('ServerInstance') -and -not $PSBoundParameters.ContainsKey('Database')) {
        $ServerInstance=$configuration.serverInstance; $Database=$configuration.database
    }
    Assert-EtpLocalSqlTarget $ServerInstance $Database
    $configuredServerInstance=$ServerInstance
    $sqlcmd=Resolve-EtpSqlCmd $SqlCmdPath
    $ServerInstance=Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $ServerInstance
    $directory=[IO.Path]::GetFullPath($BackupDirectory)
    $receiptPath=Resolve-EtpDrillReceiptPath -BackupDirectory $directory -Database $Database -ReceiptPath $ReceiptPath
    $receipt=Read-EtpVerifiedReceipt -ReceiptPath $receiptPath -BackupDirectory $directory -Database $Database
    $metadata=@(Invoke-EtpOperationsBroker -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -BackupPath $receipt.backupPath -Operation METADATA)
    $expected=($receipt.files | Sort-Object fileId | ConvertTo-Json -Depth 5 -Compress)
    $actual=($metadata | Sort-Object fileId | ConvertTo-Json -Depth 5 -Compress)
    if ($expected -cne $actual) { throw 'Backup metadata does not match the verification receipt.' }
    # No live table counts: imports after this backup cannot cause a false recovery failure. The
    # restored copy is counted inside the broker, before it is dropped, and compared with the
    # counts the receipt recorded when the backup was taken (A4.4).
    $drill=Invoke-EtpOperationsBrokerCall -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -BackupPath $receipt.backupPath -Operation DRILL
    # Detect changes that occurred while SQL read the backup, before recording success.
    $null=Read-EtpVerifiedReceipt -ReceiptPath $receiptPath -BackupDirectory $directory -Database $Database
    if ((Get-FileHash -LiteralPath $receipt.backupPath -Algorithm SHA256).Hash -ine $receipt.sha256) { throw 'The backup changed during the recovery drill.' }
    $verdict=Get-EtpDrillRowCountVerdict -Receipt $receipt -BrokerLines $drill.Lines
    # Written for a failed comparison too: the latest drill is the one that failed.
    Write-EtpJsonAtomically -Path (Join-Path $directory "$Database-latest-drill.json") -Value (New-EtpRecoveryDrillResultDocument -Verdict $verdict -BackupSha256 $receipt.sha256) -Replace
    # The drill ran as a SQL administrator; what follows runs code in the application database,
    # so it runs with the automation account's database rights and nothing more.
    # 1.9.3. Before the automation account has the operations module's rights (etp_automation),
    # this recording failed with only the masked "The database operation failed" - the drill's
    # first run on Workpc, 2 October 2026. The catch names the missing right and the command.
    # A4.4: the row-count result (dbo.recovery_drill_results), then, for a passed drill only, the
    # verified-operation record that System status trusts, then the audit row - Succeeded, or
    # Failed when the restored copy does not match its receipt.
    try {
        Publish-EtpRecoveryDrillResult -Verdict $verdict -BackupSha256 $receipt.sha256 -Encryption $receipt.encryption `
            -Invoke { param($query) Invoke-EtpSqlAsAutomationUser -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -AutomationPrincipal $configuration.automationPrincipal -Query $query }
    }
    catch {
        $failure = $_
        $explained = Get-EtpAutomationFailureMessage -Message $failure.Exception.Message -SqlCmd $sqlcmd -Server $ServerInstance -ServerInstance $configuredServerInstance -Database $Database -AutomationPrincipal $configuration.automationPrincipal
        $prefix = if ($verdict.Succeeded) { 'The isolated restore passed, but its result could not be recorded.' } else { "$($verdict.Message) The failure could not be recorded either." }
        if ($explained) { throw "$prefix $explained" }
        if (-not $verdict.Succeeded) { throw "$prefix ($($failure.Exception.Message))" }
        throw $failure
    }
    if (-not $verdict.Succeeded) { throw $verdict.Message }
    if ($verdict.Status -ceq 'NotRecorded') { Write-EtpOperationNotice $verdict.Message }
    Write-Output $verdict.Message
    Write-Output 'Receipt-verified recovery drill completed.'
}
catch {
    $drillFailure = $_
    # When recording the result failed, $failure is that error: it carries what SQL Server
    # reported, which the message rethrown above no longer does.
    if ($null -ne $failure -and -not [object]::ReferenceEquals($failure.Exception, $drillFailure.Exception)) {
        $drillLogPath = Write-EtpOperationFailureLog -Operation recovery-drill -BackupDirectory $BackupDirectory -ErrorRecord $failure -Explained $drillFailure.Exception.Message -Context "database $Database"
    }
    else { $drillLogPath = Write-EtpOperationFailureLog -Operation recovery-drill -BackupDirectory $BackupDirectory -ErrorRecord $drillFailure -Context "database $Database" }
    Write-EtpOperationFailure -Message $drillFailure.Exception.Message -LogPath $drillLogPath
    throw $drillFailure
}
