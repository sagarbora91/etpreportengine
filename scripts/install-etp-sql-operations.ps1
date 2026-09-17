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
Assert-EtpProtectedInstall (Join-Path $PSScriptRoot 'sql\etp-operations-broker.sql')
$template=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'sql\etp-operations-broker.sql')
$query=$template.Replace('__PROCEDURE__',$procedure).Replace('__DATABASE_LITERAL__',$Database.Replace("'","''")).Replace('__DATABASE_IDENTIFIER__',$Database.Replace(']',']]')).Replace('__BACKUP_DIRECTORY__',$backupRoot.Replace("'","''")).Replace('__RESTORE_DIRECTORY__',$restoreRoot.Replace("'","''"))
$sqlcmd=Resolve-EtpSqlCmd $SqlCmdPath
$ServerInstance=Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $ServerInstance
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query $query | Out-Null
$identity=$AutomationPrincipal.Replace(']',']]'); $literalIdentity=$AutomationPrincipal.Replace("'","''")
$permissions=@"
IF NOT EXISTS(SELECT 1 FROM sys.symmetric_keys WHERE name='##MS_DatabaseMasterKey##') THROW 51332,'Create the backup certificate first.',1;
IF NOT EXISTS(SELECT 1 FROM sys.certificates WHERE name='EtpOperationsModuleSigner') CREATE CERTIFICATE EtpOperationsModuleSigner WITH SUBJECT='Restricted ETP backup and recovery operations';
IF SUSER_ID('EtpOperationsModuleSigner') IS NULL CREATE LOGIN EtpOperationsModuleSigner FROM CERTIFICATE EtpOperationsModuleSigner;
GRANT CREATE ANY DATABASE,ALTER ANY DATABASE,VIEW SERVER STATE TO EtpOperationsModuleSigner;
ADD SIGNATURE TO OBJECT::dbo.[$procedure] BY CERTIFICATE EtpOperationsModuleSigner;
IF SUSER_ID(N'$literalIdentity') IS NULL THROW 51332,'Provision the dedicated Store Manager login first.',1;
IF USER_ID(N'$literalIdentity') IS NULL CREATE USER [$identity] FOR LOGIN [$identity];
GRANT EXECUTE ON dbo.[$procedure] TO [$identity];
GRANT VIEW DEFINITION ON CERTIFICATE::EtpBackupCert TO [$identity];
USE [$Database];
IF IS_ROLEMEMBER('etp_store_manager',N'$literalIdentity')<>1 THROW 51332,'The automation login must have the Store Manager database role.',1;
IF DATABASE_PRINCIPAL_ID(N'etp_automation') IS NULL THROW 51332,'Complete the operations-status database migration first.',1;
IF IS_ROLEMEMBER('etp_automation',N'$literalIdentity')<>1 ALTER ROLE etp_automation ADD MEMBER [$identity];
IF IS_ROLEMEMBER('db_backupoperator',N'$literalIdentity')<>1 ALTER ROLE db_backupoperator ADD MEMBER [$identity];
"@
Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query $permissions | Out-Null
Write-Output 'Restricted SQL backup and recovery module installed. Verify it under the dedicated account before enabling tasks.'
