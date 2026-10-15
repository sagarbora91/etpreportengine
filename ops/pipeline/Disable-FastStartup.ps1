# Turns off Windows Fast Startup on Workpc (13 Oct 2026).
# Why: with Fast Startup on, "Shut down" hibernates the running system (SQL Server included). On 13 Oct the
# resume failed (0xC000007B) and Windows cold-booted, which SQL Server sees as a power cut.
# Same as Control Panel > Power Options > "Turn on fast startup" unticked. Hibernate itself is left as it is.
# Run elevated. Undo: set HiberbootEnabled back to 1.

$ErrorActionPreference = 'Stop'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host 'Please run this as administrator (right-click PowerShell > Run as administrator).' -ForegroundColor Red; return }

$key = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power'
$before = (Get-ItemProperty -Path $key -Name HiberbootEnabled -ErrorAction SilentlyContinue).HiberbootEnabled
Write-Host "Fast Startup before: $(if ($before -eq 1) { 'ON' } else { 'OFF' })"

Set-ItemProperty -Path $key -Name HiberbootEnabled -Value 0 -Type DWord

$after = (Get-ItemProperty -Path $key -Name HiberbootEnabled).HiberbootEnabled
if ($after -eq 0) { Write-Host 'Fast Startup is now OFF. Shut down will now close SQL Server properly.' -ForegroundColor Green }
else { Write-Host "Something went wrong: HiberbootEnabled = $after" -ForegroundColor Red }
