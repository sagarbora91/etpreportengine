param(
    [string]$TaskName = "ETP Reporting Automated Operations",
    [ValidateRange(1,60)][int]$IntervalMinutes = 5
)

$ErrorActionPreference = "Stop"
$application = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Etp.Reporting.Desktop.exe"))
if (-not (Test-Path -LiteralPath $application)) { throw "The installed ETP Reporting Engine executable was not found." }
if ((Get-Item -LiteralPath $application).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw "A linked application executable cannot be scheduled." }
$action = New-ScheduledTaskAction -Execute $application -Argument "--automation-once"
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) -RepetitionDuration (New-TimeSpan -Days 3650)
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -User "SYSTEM" -RunLevel Highest -Description "Imports approved ETP files from the controlled watch folder and generates due management packs." -Force | Out-Null
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
if ($task.State -eq 'Disabled') { throw "The automated operations task was installed but is disabled." }
$info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop
Write-Host "Scheduled task '$TaskName' installed for every $IntervalMinutes minute(s). Next run: $($info.NextRunTime)."
