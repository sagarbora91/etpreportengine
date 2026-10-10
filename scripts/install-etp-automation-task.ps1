param([string]$TaskName='ETP Reporting Automated Operations',[ValidateRange(1,60)][int]$IntervalMinutes=5,
      # 1.9.9, IE-RT-08. Setup passes this while the automation account cannot open the
      # database yet (not an active Store Manager): the task is registered as usual and then
      # disabled, before its first run a minute later, so it does not fail to sign in every
      # five minutes. Setup run again later, without it, registers the task enabled.
      [switch]$Disabled)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
$application = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Etp.Reporting.Desktop.exe'))
Assert-EtpProtectedInstall $application
$action = New-ScheduledTaskAction -Execute $application -Argument "--automation-once"
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) -RepetitionDuration (New-TimeSpan -Days 3650)
Register-EtpScheduledOperation -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Imports approved ETP files and generates scheduled report packs.'
if ($Disabled) {
    Disable-ScheduledTask -TaskName $TaskName -ErrorAction Stop | Out-Null
    if ((Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop).State -ne 'Disabled') { throw 'The scheduled automation task could not be disabled.' }
    Write-Output 'Scheduled automation installed, disabled until the automation account can open the database.'
}
else { Write-Output 'Scheduled automation installed.' }
