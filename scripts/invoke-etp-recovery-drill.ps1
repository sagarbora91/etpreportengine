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

# 1.11.0, decision 36 (Q6). After a passed drill, the live database gets DBCC CHECKDB WITH
# PHYSICAL_ONLY (seconds on this database), so the health check's DB-03, which only reads the
# last-good CHECKDB date, sees a fresh date every month and not only after a setup run. It runs
# only after the drill passed and its result was recorded; the drill itself is unchanged.
function New-EtpDrillIntegrityCheckSql {
    param([Parameter(Mandatory)][string]$Database)
    if ($Database -notmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Choose a valid database name.' }
    return "SET NOCOUNT ON; DBCC CHECKDB ([$Database]) WITH PHYSICAL_ONLY, NO_INFOMSGS;"
}

function Get-EtpDrillIntegrityFailureKind {
    # Pure. 'Permission' when SQL Server refused the check itself (7983 and 2571: no DBCC right;
    # 916, 229, 262, 300: no access) - the drill still passed and the check is reported as a
    # notice; anything else, a consistency error above all, is 'Integrity' and fails the drill run.
    param([AllowNull()][string]$Detail)
    if ($Detail -match 'SQL Server error (7983|2571|916|229|262|300)\b') { return 'Permission' }
    return 'Integrity'
}

# Dot-sourcing exposes the functions above without running a drill.
if ($MyInvocation.InvocationName -eq '.') { return }

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
    # Decision 36 (Q6): integrity check of the live database, after the passed drill was recorded.
    $integrityChecked = $false
    try {
        $null = Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database master -Query (New-EtpDrillIntegrityCheckSql -Database $Database)
        $integrityChecked = $true
    }
    catch {
        if ((Get-EtpDrillIntegrityFailureKind (Get-EtpExceptionSqlDetail $_.Exception)) -ceq 'Permission') {
            Write-EtpOperationNotice "The recovery drill passed, but SQL Server did not allow the integrity check of $Database (DBCC CHECKDB WITH PHYSICAL_ONLY) for this account. Run the drill as a SQL Server administrator so the health check sees a current integrity date."
        }
        else {
            $failure = $_
            throw "The recovery drill passed, but the integrity check of the live database $Database (DBCC CHECKDB WITH PHYSICAL_ONLY) failed. Do not ignore this: run DBCC CHECKDB on $Database as a SQL Server administrator and follow docs\OPERATIONS.md before the next backup is relied on."
        }
    }
    if ($integrityChecked) { Write-Output "Integrity check of $Database (DBCC CHECKDB WITH PHYSICAL_ONLY) passed." }
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
