param([ValidateRange(1,28)][int]$DayOfMonth = 1)
$ErrorActionPreference = "Stop"
if ((Get-Date).Day -ne $DayOfMonth) { exit 0 }
# 1.9.9 (IE-CODE-19). A .ps1 does not set $LASTEXITCODE, so the check that stood here read
# whatever the drill's last sqlcmd call left. The drill throws on every failure and logs it
# under <backup folder>\Logs itself; the task's result is 1 for any failure.
try { & (Join-Path $PSScriptRoot 'invoke-etp-recovery-drill.ps1') }
catch {
    [Console]::Error.WriteLine('Monthly recovery drill failed: ' + $_.Exception.Message)
    exit 1
}
exit 0
