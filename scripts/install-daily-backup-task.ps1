param([string]$TaskName='ETP Reporting Daily Backup',[Alias('BackupTime')][string]$RunTime='22:00')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
$script = Join-Path $PSScriptRoot 'backup-etp-database.ps1'
Assert-EtpProtectedInstall $script
$configuration = Get-EtpOperationsConfiguration
$time = [datetime]::ParseExact($RunTime,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
# A4.3 accepted unsigned installs. AllSigned here would refuse the nightly backup on
# every unsigned install and the shop would find out only when a restore was needed.
# Assert-EtpProtectedInstall above still refuses a script a non-administrator can edit.
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "' + $script + '"'
$powerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $time
Register-EtpScheduledOperation -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Encrypted, verified database backup with receipt-based rotation.'
Write-Output 'Scheduled operation installed.'
