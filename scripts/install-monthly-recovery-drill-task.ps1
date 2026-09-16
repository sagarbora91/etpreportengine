param([ValidateRange(1,28)][int]$DayOfMonth=1, [string]$TaskName='ETP Reporting Monthly Recovery Drill',[Alias('DrillTime')][string]$RunTime='08:00')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
$script = Join-Path $PSScriptRoot 'invoke-monthly-recovery-drill-runner.ps1'
Assert-EtpProtectedInstall $script
$configuration = Get-EtpOperationsConfiguration
$time = [datetime]::ParseExact($RunTime,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
$arguments = '-NoProfile -NonInteractive -ExecutionPolicy AllSigned -File "' + $script + '"'
$arguments += ' -DayOfMonth ' + $DayOfMonth
$powerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $time
Register-EtpScheduledOperation -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Receipt-verified isolated recovery drill.'
Write-Output 'Scheduled operation installed.'
