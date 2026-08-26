param(
    [string]$TaskName = "ETP Reporting Monthly Recovery Drill",
    [ValidateRange(1,28)][int]$DayOfMonth = 1,
    [string]$DrillTime = "08:00"
)
$ErrorActionPreference = "Stop"
$drillScript = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "invoke-etp-recovery-drill.ps1"))
if (-not (Test-Path -LiteralPath $drillScript)) { throw "Recovery drill script was not found." }
$null = [datetime]::ParseExact($DrillTime, "HH:mm", [Globalization.CultureInfo]::InvariantCulture)
$taskCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$drillScript`""
& schtasks.exe /Create /TN $TaskName /TR $taskCommand /SC MONTHLY /D $DayOfMonth /ST $DrillTime /F | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Monthly recovery drill task registration failed." }
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
if ($task.State -eq 'Disabled') { throw "Scheduled recovery drill task was installed but is disabled." }
$taskInfo = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop
Write-Host "Scheduled task '$TaskName' installed. Next run: $($taskInfo.NextRunTime)."
