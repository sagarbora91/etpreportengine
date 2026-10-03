param(
    [Parameter(Mandatory)][string]$ServerInstance,
    [Parameter(Mandatory)][string]$Database,
    [Parameter(Mandatory)][string]$AutomationPrincipal,
    [string]$BackupDirectory="$env:ProgramData\EtpReporting\Backups",
    [string]$SqlCmdPath,
    # Used by setup's restore mode and by restore-etp-database.ps1. Creates the broker only
    # where it is missing: a SQL administrator - and so setup's pre-migration backup - can use
    # it unsigned. Signing and the automation account's grants follow later, once that account
    # is an active Store Manager. A signed broker is left untouched, because re-creating it
    # discards the signature the dedicated account's backups rely on. 1.9.3: an unsigned one
    # from an earlier build is replaced (it has no signature to lose), so setup's pre-migration
    # backup records row counts; a signed out-of-date one is replaced and re-signed by the full
    # install that ends every setup run (Get-EtpBrokerOnlyAction).
    [switch]$BrokerOnly
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
Assert-EtpLocalSqlTarget $ServerInstance $Database
Assert-EtpNoLinks $BackupDirectory
if ($AutomationPrincipal -notmatch ('^'+[regex]::Escape([Environment]::MachineName)+'\\[^\\]+$')) { throw 'Choose the dedicated local automation account.' }
$backupRoot=[IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\')
$restoreRoot=Join-Path $backupRoot 'RecoveryDrill'
if (-not (Test-Path -LiteralPath $restoreRoot -PathType Container)) { throw 'Complete protected recovery-folder setup first.' }
$procedure=Get-EtpOperationsProcedureName $Database
# Both templates are checked and read before anything in SQL changes. An installation folder
# a non-administrator can edit, or a grants template that is missing, now stops the install
# with the working broker untouched, instead of after it has been replaced by an unsigned one.
Assert-EtpProtectedInstall (Join-Path $PSScriptRoot 'sql\etp-operations-broker.sql')
Assert-EtpProtectedInstall (Join-Path $PSScriptRoot 'sql\etp-operations-grants.sql')
$template=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'sql\etp-operations-broker.sql')
$grants=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'sql\etp-operations-grants.sql')
$query=$template.Replace('__PROCEDURE__',$procedure).Replace('__DATABASE_LITERAL__',$Database.Replace("'","''")).Replace('__DATABASE_IDENTIFIER__',$Database.Replace(']',']]')).Replace('__BACKUP_DIRECTORY__',$backupRoot.Replace("'","''")).Replace('__RESTORE_DIRECTORY__',$restoreRoot.Replace("'","''"))
# P4-13. Signing, grants and every precondition live in one SQL template beside the broker,
# so the integration tests run the exact SQL that ships. It validates before it signs, and
# if anything fails it removes the signer it created and keeps the broker, which a SQL
# administrator - and so the next upgrade's pre-migration backup - can still use.
# One signer per broker: reinstalling one database's module must never invalidate the
# signature on another's. The suffix is the same database hash the procedure name uses.
$signer='EtpOperationsModuleSigner_'+$procedure.Substring('etp_operations_'.Length)
$grants=$grants.Replace('__PROCEDURE__',$procedure).Replace('__SIGNER__',$signer)
$grants=$grants.Replace('__IDENTITY_LITERAL__',$AutomationPrincipal.Replace("'","''")).Replace('__DATABASE_LITERAL__',$Database.Replace("'","''"))
$sqlcmd=Resolve-EtpSqlCmd $SqlCmdPath
$ServerInstance=Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $ServerInstance
if ($BrokerOnly) {
    $brokerAction=Get-EtpBrokerOnlyAction -Lines @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query (Get-EtpBrokerStateQuery -Procedure $procedure))
    # Only a missing broker, or an unsigned one from an earlier build, is (re)created here.
    if ($brokerAction -ceq 'Install' -or $brokerAction -ceq 'Replace') {
        Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query $query | Out-Null
        if ($brokerAction -ceq 'Install') { Write-Output "Operations broker installed for $Database. Its signing and the automation account's grants follow once $AutomationPrincipal is an active Store Manager (docs\OPERATIONS.md, step 7)." }
        else { Write-Output "The unsigned operations broker for $Database was from an earlier build and has been replaced by the current one, which records row counts. Its signing and the automation account's grants follow once $AutomationPrincipal is an active Store Manager (docs\OPERATIONS.md, step 7)." }
    }
    elseif ($brokerAction -ceq 'KeepSigned') { Write-Output "The operations broker for $Database is signed but from an earlier build, which records no row counts. It was left unchanged here so that the automation account's backups keep working; the full module install at the end of setup replaces and re-signs it (docs\OPERATIONS.md, step 7)." }
    else { Write-Output "The operations broker for $Database is already installed and current; it was left unchanged." }
    return
}
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query $query | Out-Null
$output=@(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query $grants)
# A step the Owner has to take first comes back as one line of fixed text. Show it as it is.
$refused=@($output | Where-Object { $_.StartsWith('ETP_MODULE_REFUSED:') })
if ($refused.Count -gt 0) { throw $refused[0].Substring(19) }
$installed=@($output | Where-Object { $_.StartsWith('ETP_MODULE:') })
if ($installed.Count -ne 1) { throw 'The operations module did not confirm its signature and grants.' }
$module=$installed[0].Substring(11) | ConvertFrom-Json
Write-Output ("Restricted SQL backup and recovery module installed for $Database. Backups on this edition: $($module.backupEncryption). Verify it under the dedicated account before enabling tasks.")
