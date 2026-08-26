param(
    [string]$TaskName = "ETP Reporting Daily Backup",
    [string]$BackupTime = "22:00"
)
$ErrorActionPreference = "Stop"
$backupScript = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "backup-etp-database.ps1"))
if (-not (Test-Path -LiteralPath $backupScript)) { throw "Backup script was not found." }
$time = [datetime]::ParseExact($BackupTime, "HH:mm", [Globalization.CultureInfo]::InvariantCulture)
$arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$backupScript`""
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $time
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -User "SYSTEM" -RunLevel Highest -Description "Checksum-verified backup of the ETP Reporting SQL Server database. Backups are retained indefinitely by policy." -Force | Out-Null
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
if ($task.State -eq 'Disabled') { throw "Scheduled backup task was installed but is disabled." }
$taskInfo = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop
Write-Host "Scheduled task '$TaskName' installed and enabled for $BackupTime daily. Next run: $($taskInfo.NextRunTime)."
