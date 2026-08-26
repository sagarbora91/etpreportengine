param(
    [string]$TaskName = "ETP Reporting Daily Backup",
    [string]$BackupTime = "22:00",
    [ValidateRange(1,3650)][int]$RetentionDays = 30
)
$ErrorActionPreference = "Stop"
$backupScript = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "backup-etp-database.ps1"))
if (-not (Test-Path -LiteralPath $backupScript)) { throw "Backup script was not found." }
$time = [datetime]::ParseExact($BackupTime, "HH:mm", [Globalization.CultureInfo]::InvariantCulture)
$arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$backupScript`" -RetentionDays $RetentionDays"
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $time
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description "Checksum-verified backup of the ETP Reporting SQL Server database." -Force | Out-Null
Write-Host "Scheduled task '$TaskName' installed for $BackupTime daily."
