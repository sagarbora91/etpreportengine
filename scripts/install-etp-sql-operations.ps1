param(
    [Parameter(Mandatory)][string]$ServerInstance,
    [Parameter(Mandatory)][string]$Database,
    [Parameter(Mandatory)][string]$AutomationPrincipal,
    [string]$BackupDirectory="$env:ProgramData\EtpReporting\Backups",
    [string]$SqlCmdPath
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
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query $query | Out-Null
$output=@(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query $grants)
# A step the Owner has to take first comes back as one line of fixed text. Show it as it is.
$refused=@($output | Where-Object { $_.StartsWith('ETP_MODULE_REFUSED:') })
if ($refused.Count -gt 0) { throw $refused[0].Substring(19) }
$installed=@($output | Where-Object { $_.StartsWith('ETP_MODULE:') })
if ($installed.Count -ne 1) { throw 'The operations module did not confirm its signature and grants.' }
$module=$installed[0].Substring(11) | ConvertFrom-Json
Write-Output ("Restricted SQL backup and recovery module installed for $Database. Backups on this edition: $($module.backupEncryption). Verify it under the dedicated account before enabling tasks.")
