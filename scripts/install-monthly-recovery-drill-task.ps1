param(
    [string]$TaskName = "ETP Reporting Monthly Recovery Drill",
    [ValidateRange(1,28)][int]$DayOfMonth = 1,
    [string]$DrillTime = "08:00"
)
$ErrorActionPreference = "Stop"
$drillScript = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "invoke-monthly-recovery-drill-runner.ps1"))
if (-not (Test-Path -LiteralPath $drillScript)) { throw "Monthly recovery drill runner was not found." }
$null = [datetime]::ParseExact($DrillTime, "HH:mm", [Globalization.CultureInfo]::InvariantCulture)
$time = [datetime]::ParseExact($DrillTime, "HH:mm", [Globalization.CultureInfo]::InvariantCulture)
$arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$drillScript`" -DayOfMonth $DayOfMonth"
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $time
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description "Checks daily and runs the isolated ETP recovery drill on day $DayOfMonth of each month." -Force | Out-Null
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
if ($task.State -eq 'Disabled') { throw "Scheduled recovery drill task was installed but is disabled." }
$taskInfo = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop
Write-Host "Scheduled task '$TaskName' installed; the daily trigger runs the drill only on day $DayOfMonth. Next check: $($taskInfo.NextRunTime)."
