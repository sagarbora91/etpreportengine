param([string]$TaskName='ETP Reporting Automated Operations',[ValidateRange(1,60)][int]$IntervalMinutes=5)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
$application = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Etp.Reporting.Desktop.exe'))
Assert-EtpProtectedInstall $application
$action = New-ScheduledTaskAction -Execute $application -Argument "--automation-once"
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) -RepetitionDuration (New-TimeSpan -Days 3650)
Register-EtpScheduledOperation -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Imports approved ETP files and generates scheduled report packs.'
Write-Output 'Scheduled automation installed.'
