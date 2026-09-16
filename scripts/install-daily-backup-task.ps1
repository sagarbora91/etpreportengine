param([string]$TaskName='ETP Reporting Daily Backup',[Alias('BackupTime')][string]$RunTime='22:00')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
$script = Join-Path $PSScriptRoot 'backup-etp-database.ps1'
Assert-EtpProtectedInstall $script
$configuration = Get-EtpOperationsConfiguration
$time = [datetime]::ParseExact($RunTime,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy AllSigned -File "' + $script + '"'
$powerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $time
Register-EtpScheduledOperation -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Encrypted, verified database backup with receipt-based rotation.'
Write-Output 'Scheduled operation installed.'
