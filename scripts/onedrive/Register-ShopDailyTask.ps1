<#
.SYNOPSIS
  Schedules Send-ShopDailyFiles.ps1 once a day for the CURRENT Windows user (runs only while that user is logged on).
  No password is asked for or stored. Run it from a normal (not administrator) PowerShell on the shop PC.

.PARAMETER At             Evening start time, 24h "HH:mm" (default 21:00).
.PARAMETER RetryEveryMinutes / RetryForHours  Re-run to catch files that were still open (safe: the script is idempotent). 0 turns it off.
.PARAMETER Unregister     Remove both tasks.

Also registers 'ETP Show Shop Results': at every logon of this user it runs Show-ShopResults.ps1, which shows ETP's verdict on the
shop's last files (green = accepted, red = refused and why). It needs no password, like the send task.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidatePattern('^([01]\d|2[0-3]):[0-5]\d$')][string]$At = '21:00',
    [int]$RetryEveryMinutes = 30,
    [int]$RetryForHours = 2,
    [string]$TaskName = 'ETP Send Shop Daily Files',
    [string]$ResultsTaskName = 'ETP Show Shop Results',
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'shop-daily-files.config.json'),
    [switch]$Unregister
)
$ErrorActionPreference = 'Stop'

if ($Unregister) {
    foreach ($name in @($TaskName, $ResultsTaskName)) {
        if ((Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess($name, 'Unregister scheduled task')) { Unregister-ScheduledTask -TaskName $name -Confirm:$false }
    }
    return
}
if (-not (Test-Path -LiteralPath $ConfigPath)) { throw "Create the config first: copy shop-daily-files.config.example.json to '$ConfigPath' and edit it." }

$script = Join-Path $PSScriptRoot 'Send-ShopDailyFiles.ps1'
$arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -ConfigPath "{1}"' -f $script, $ConfigPath
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments
$time = [datetime]::ParseExact($At, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
$trigger = New-ScheduledTaskTrigger -Daily -At $time
if ($RetryEveryMinutes -gt 0 -and $RetryForHours -gt 0) {
    # Borrow the repetition settings from a throwaway "once" trigger (the usual way for a daily trigger on PowerShell 5.1).
    $repeat = New-ScheduledTaskTrigger -Once -At $time -RepetitionInterval (New-TimeSpan -Minutes $RetryEveryMinutes) -RepetitionDuration (New-TimeSpan -Hours $RetryForHours)
    $trigger.Repetition = $repeat.Repetition
}
$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
# Interactive logon = runs as the logged-on user, only while logged on; Windows needs no password for that.
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 30) -MultipleInstances IgnoreNew

if ($PSCmdlet.ShouldProcess("$TaskName ($user, daily $At)", 'Register scheduled task')) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Copies the day''s ETP export files to OneDrive (ETP-Inbound).' -Force | Out-Null
    Write-Host "Registered '$TaskName' for $user, daily at $At. Leave this PC switched on and signed in at that time."

    # The verdict window: at logon of this user, hidden console, the form itself is visible. Interactive logon = no password.
    $showScript = Join-Path $PSScriptRoot 'Show-ShopResults.ps1'
    $showArguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -ConfigPath "{1}"' -f $showScript, $ConfigPath
    $showAction = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $showArguments
    $showTrigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $showSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 10) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $ResultsTaskName -Action $showAction -Trigger $showTrigger -Principal $principal -Settings $showSettings -Description 'Shows ETP''s verdict on the last files this shop sent.' -Force | Out-Null
    Write-Host "Registered '$ResultsTaskName' for $user, at logon."
}
