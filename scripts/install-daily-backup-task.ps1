<#
Registers ETP's nightly backup task (default 22:00).

1.12 G1 (decision 54): with -DiskCopyRoot it ALSO registers 'ETP Copy Backup To Disk' (copy-etp-backup-to-disk.ps1), which
copies the verified backup to a second disk inside this PC (Workpc: G:\ETP-Backups, BitLocker on) and writes the small
config file health check OPS-11 reads. Run it in an elevated window as the Windows user who owns the backups (Sagar):
    powershell -ExecutionPolicy Bypass -File install-daily-backup-task.ps1 -DiskCopyRoot G:\ETP-Backups
  -Times            when the disk copy runs (default 22:45, after the 22:00 backup; '14:45','22:45' to follow a 14:00 backup too).
  -DiskCopyOnly     register only the disk copy task, leave the backup task as it is.
The disk copy task runs as the account that registers it (S4U, no password stored), like the monthly recovery drill, because
the automation account cannot read the admin-only backup folder.
#>
param([string]$TaskName='ETP Reporting Daily Backup',[Alias('BackupTime')][string]$RunTime='22:00',
      [string]$DiskCopyRoot,[string[]]$Times=@('22:45'),[switch]$DiskCopyOnly,
      [string]$DiskCopyTaskName='ETP Copy Backup To Disk')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
$powerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'

if (-not $DiskCopyOnly) {
    $script = Join-Path $PSScriptRoot 'backup-etp-database.ps1'
    Assert-EtpProtectedInstall $script
    $configuration = Get-EtpOperationsConfiguration
    $time = [datetime]::ParseExact($RunTime,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
    # A4.3 accepted unsigned installs. AllSigned here would refuse the nightly backup on
    # every unsigned install and the shop would find out only when a restore was needed.
    # Assert-EtpProtectedInstall above still refuses a script a non-administrator can edit.
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "' + $script + '"'
    $action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments
    $trigger = New-ScheduledTaskTrigger -Daily -At $time
    Register-EtpScheduledOperation -TaskName $TaskName -Action $action -Trigger $trigger -Description 'Encrypted, verified database backup with receipt-based rotation.'
    Write-Output 'Scheduled operation installed.'
}

if (-not [string]::IsNullOrWhiteSpace($DiskCopyRoot)) {
    $copyScript = Join-Path $PSScriptRoot 'copy-etp-backup-to-disk.ps1'
    Assert-EtpProtectedInstall $copyScript
    $me = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not (New-Object Security.Principal.WindowsPrincipal($me)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this in a PowerShell window opened with "Run as administrator" (signed in as the user who owns the backups).'
    }
    $full = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($DiskCopyRoot)).TrimEnd('\')
    if ($full.StartsWith('\\')) { throw 'The disk copy must go to a disk inside this PC, not a network share.' }
    if ([IO.Path]::GetPathRoot($full) -ieq [IO.Path]::GetPathRoot($env:SystemRoot)) { throw 'The disk copy folder must not be on the system drive. Use the second disk (for example G:\ETP-Backups).' }
    if (-not (Test-Path -LiteralPath ([IO.Path]::GetPathRoot($full)))) { throw "The disk $([IO.Path]::GetPathRoot($full).TrimEnd('\')) is not connected or not unlocked." }
    foreach ($oneDriveName in 'OneDrive','OneDriveCommercial','OneDriveConsumer') {
        $oneDrive = [Environment]::GetEnvironmentVariable($oneDriveName)
        if ($oneDrive -and ($full.TrimEnd('\') + '\').StartsWith(($oneDrive.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) { throw 'The disk copy folder must not be inside OneDrive.' }
    }
    $triggers = @()
    foreach ($text in $Times) {
        $at = [datetime]::ParseExact($text,'HH:mm',[Globalization.CultureInfo]::InvariantCulture)
        $triggers += New-ScheduledTaskTrigger -Daily -At $at
    }
    $backupFolder = Join-Path $env:ProgramData 'EtpReporting\Backups'
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "{0}" -DiskCopyRoot "{1}" -BackupFolder "{2}"' -f $copyScript, $full, $backupFolder
    $action = New-ScheduledTaskAction -Execute $powerShell -Argument $arguments -WorkingDirectory (Split-Path -Parent $copyScript)
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)
    $principal = New-ScheduledTaskPrincipal -UserId $me.Name -LogonType S4U -RunLevel Highest
    Register-ScheduledTask -TaskName $DiskCopyTaskName -Action $action -Trigger $triggers -Settings $settings -Principal $principal `
        -Description 'Copies the newest verified ETP backup to the second disk (plain file, BitLocker), checks it by hash, keeps 30 nightly and 12 monthly copies.' -Force | Out-Null
    $task = Get-ScheduledTask -TaskName $DiskCopyTaskName -ErrorAction Stop
    if ($task.State -eq 'Disabled' -or [string]$task.Principal.LogonType -ne 'S4U' -or [string]$task.Principal.RunLevel -ne 'Highest') { throw 'The disk copy task did not keep the required principal and settings.' }

    # What health check OPS-11 reads. Written only after the task is registered, so "configured" means "scheduled".
    $configDirectory = Join-Path $env:ProgramData 'EtpReporting\Operations'
    if (-not (Test-Path -LiteralPath $configDirectory)) { [void](New-Item -ItemType Directory -Path $configDirectory -Force) }
    Write-EtpJsonAtomically -Path (Join-Path $configDirectory 'disk-copy.json') -Replace -Value ([ordered]@{
        schemaVersion = 1; diskCopyRoot = $full; backupFolder = $backupFolder; database = 'EtpReporting'; taskName = $DiskCopyTaskName })
    Write-Output "Scheduled disk copy installed: '$DiskCopyTaskName' at $($Times -join ', ') for $($me.Name), to $full."
}
