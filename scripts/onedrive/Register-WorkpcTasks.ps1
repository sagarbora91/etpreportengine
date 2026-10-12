<#
.SYNOPSIS
  Registers the two Workpc OneDrive tasks (AUDIT-3 G4):
    'ETP Receive Shop Daily Files'  - Receive-ShopDailyFiles.ps1 every morning (default 07:00)
    'ETP Copy Backup To OneDrive'   - Copy-EtpBackupToOneDrive.ps1 every night after ETP's backup (default 23:30)

.DESCRIPTION
  Both tasks run as the account that runs THIS script (Sagar: the only account with the OneDrive client signed in),
  with highest privileges (the Inbound and Backups folders are admin-only), logon type S4U - the same pattern as the
  Monthly Recovery Drill: registering a task for your own account needs no password and none is stored. The
  automation account cannot be used: it has no OneDrive client and cannot write into Sagar's profile.

  It stops, saying why, when:
   - this window is not elevated ("Run as administrator");
   - OneDrive is not running for this account (sign in to OneDrive first);
   - a config file is missing, or has no "oneDriveRoot" (a scheduled task may not see the OneDrive variable; the
     message gives the exact value to put in);
   - the scripts folder can be changed by ordinary users (a task with highest privileges would run whatever they
     put there - keep the scripts in the folder ETP setup installs, or C:\ProgramData\EtpReporting\Operations\onedrive);
   - the backup key file or the shop keys folder sits inside OneDrive.
  Each script itself also refuses to run when OneDrive is not running, so a reboot without sign-in shows up as a
  failed task (non-zero "Last Run Result"), never as a silent gap.

  Windows PowerShell 5.1 compatible. Run in an elevated PowerShell window as Sagar:
    powershell -ExecutionPolicy Bypass -File .\Register-WorkpcTasks.ps1

.PARAMETER ReceiveAt / BackupCopyAt  24-hour "HH:mm". The copy must start after ETP's nightly backup (default 22:00).
.PARAMETER SkipReceive / SkipBackupCopy  Register only one of the two.
.PARAMETER Unregister  Remove both tasks.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidatePattern('^([01]\d|2[0-3]):[0-5]\d$')][string]$ReceiveAt = '07:00',
    [ValidatePattern('^([01]\d|2[0-3]):[0-5]\d$')][string]$BackupCopyAt = '23:30',
    [string]$ReceiveConfigPath = (Join-Path $PSScriptRoot 'workpc-receive.config.json'),
    [string]$BackupConfigPath = (Join-Path $PSScriptRoot 'workpc-backup.config.json'),
    [switch]$SkipReceive,
    [switch]$SkipBackupCopy,
    [switch]$Unregister
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneDriveCommon.ps1')

$receiveTask = 'ETP Receive Shop Daily Files'
$backupTask = 'ETP Copy Backup To OneDrive'

function Assert-EtpElevated {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this in a PowerShell window opened with "Run as administrator" (signed in to Windows as Sagar).'
    }
}

function Assert-EtpFolderNotUserWritable {
    # Fails when Everyone, Users, Authenticated Users or Interactive may write to the folder or a script in it.
    param([string]$Folder)
    $broad = @('S-1-1-0', 'S-1-5-32-545', 'S-1-5-11', 'S-1-5-4')
    $writeRights = [Security.AccessControl.FileSystemRights]::WriteData -bor [Security.AccessControl.FileSystemRights]::AppendData -bor
        [Security.AccessControl.FileSystemRights]::WriteAttributes -bor [Security.AccessControl.FileSystemRights]::Delete -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    $items = @((Get-Item -LiteralPath $Folder)) + @(Get-ChildItem -LiteralPath $Folder -Filter '*.ps1' -File)
    foreach ($item in $items) {
        foreach ($rule in @((Get-Acl -LiteralPath $item.FullName).Access)) {
            if ($rule.AccessControlType -ne 'Allow') { continue }
            try { $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { continue }
            if ($broad -contains $sid -and (([int]$rule.FileSystemRights) -band ([int]$writeRights)) -ne 0) {
                throw "'$($item.FullName)' can be changed by $($rule.IdentityReference). A task with highest privileges must not run scripts that ordinary users can change. Put the scripts in the folder ETP setup installs (or C:\ProgramData\EtpReporting\Operations\onedrive, admin-only) and run this from there."
            }
        }
    }
}

function Assert-EtpConfigForTask {
    # The config must exist and name the OneDrive folder explicitly; returns the parsed config.
    param([string]$Path, [string]$Example)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Create the config first: copy $Example to '$Path' and edit it." }
    $config = Read-EtpOneDriveConfig -Path $Path
    $root = [string](Get-EtpConfigValue $config 'oneDriveRoot' '')
    if ([string]::IsNullOrWhiteSpace($root)) {
        $found = $null
        try { $found = Resolve-EtpOneDriveRoot -Override '' } catch { }
        $hint = if ($found) { "set ""oneDriveRoot"": ""$($found -replace '\\', '\\')""" } else { 'sign in to OneDrive, then set "oneDriveRoot" to its folder' }
        throw "'$Path' has no oneDriveRoot. A scheduled task may not see the OneDrive variable, so $hint in that file and run this again."
    }
    [void](Resolve-EtpOneDriveRoot -Override $root)
    return $config
}

function Register-EtpWorkpcTask {
    param([string]$TaskName, [string]$Script, [string]$ConfigPath, [string]$At, [string]$Description, [int]$LimitMinutes)
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -ConfigPath "{1}"' -f $Script, $ConfigPath
    $action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Argument $arguments -WorkingDirectory (Split-Path -Parent $Script)
    $time = [datetime]::ParseExact($At, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
    $trigger = New-ScheduledTaskTrigger -Daily -At $time
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes $LimitMinutes) -MultipleInstances IgnoreNew
    $me = [Security.Principal.WindowsIdentity]::GetCurrent()
    # S4U for the caller's own account: no password asked for or stored (as the Monthly Recovery Drill).
    $principal = New-ScheduledTaskPrincipal -UserId $me.Name -LogonType S4U -RunLevel Highest
    if (-not $PSCmdlet.ShouldProcess("$TaskName ($($me.Name), daily $At)", 'Register scheduled task')) { return }
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description $Description -Force | Out-Null
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    $sid = $null
    try { $sid = (New-Object Security.Principal.NTAccount($task.Principal.UserId)).Translate([Security.Principal.SecurityIdentifier]).Value } catch { }
    if ($sid -ne $me.User.Value -or [string]$task.Principal.RunLevel -ne 'Highest' -or [string]$task.Principal.LogonType -ne 'S4U') {
        throw "The task '$TaskName' did not keep the required account, run level and logon type. Remove it in Task Scheduler and run this again."
    }
    Write-Host "Registered '$TaskName' for $($me.Name), daily at $At."
}

if ($Unregister) {
    foreach ($name in @($receiveTask, $backupTask)) {
        if ((Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess($name, 'Unregister scheduled task')) {
            Unregister-ScheduledTask -TaskName $name -Confirm:$false
            Write-Host "Removed '$name'."
        }
    }
    return
}

Assert-EtpElevated
if (-not (Test-EtpOneDriveClientRunning)) {
    throw "OneDrive is not running for $([Security.Principal.WindowsIdentity]::GetCurrent().Name). Sign in to OneDrive on this PC (cloud icon by the clock) and run this again."
}
Assert-EtpFolderNotUserWritable -Folder $PSScriptRoot

if (-not $SkipReceive) {
    $config = Assert-EtpConfigForTask -Path $ReceiveConfigPath -Example 'workpc-receive.config.example.json'
    $keysFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'shopKeysFolder' "$env:ProgramData\EtpReporting\Operations\shop-keys"))
    Assert-EtpKeyOutsideOneDrive -KeyFile (Join-Path $keysFolder 'x.key') -ConfiguredRoot ([string](Get-EtpConfigValue $config 'oneDriveRoot' ''))
    if (-not (Test-Path -LiteralPath $keysFolder -PathType Container)) { Write-Warning "No shop keys yet in $keysFolder. Receive refuses every shop until each role's key is copied there by USB (see the guide)." }
    Register-EtpWorkpcTask -TaskName $receiveTask -Script (Join-Path $PSScriptRoot 'Receive-ShopDailyFiles.ps1') -ConfigPath $ReceiveConfigPath -At $ReceiveAt -LimitMinutes 60 `
        -Description 'Checks the shop PCs'' signed files in OneDrive\ETP-Inbound and hands verified ones to ETP''s Inbound folder.'
}
if (-not $SkipBackupCopy) {
    $config = Assert-EtpConfigForTask -Path $BackupConfigPath -Example 'workpc-backup.config.example.json'
    $keyFile = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'keyFile' ''))
    if ([string]::IsNullOrWhiteSpace($keyFile)) { throw "'$BackupConfigPath' has no keyFile." }
    Assert-EtpKeyOutsideOneDrive -KeyFile $keyFile -ConfiguredRoot ([string](Get-EtpConfigValue $config 'oneDriveRoot' ''))
    if (-not (Test-Path -LiteralPath $keyFile -PathType Leaf)) { throw "Backup key not found: $keyFile. Create it first: Copy-EtpBackupToOneDrive.ps1 -NewKey (then two USB copies)." }
    Register-EtpWorkpcTask -TaskName $backupTask -Script (Join-Path $PSScriptRoot 'Copy-EtpBackupToOneDrive.ps1') -ConfigPath $BackupConfigPath -At $BackupCopyAt -LimitMinutes 120 `
        -Description 'Encrypts the newest verified ETP backup into OneDrive\ETP-Backups and waits for the upload.'
}
Write-Host 'Done. Check each task''s "Last Run Result" in Task Scheduler the next morning (0 = done, 2 = something missing; read the log).'
