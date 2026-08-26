param([ValidateRange(1,28)][int]$DayOfMonth = 1)
$ErrorActionPreference = "Stop"
if ((Get-Date).Day -ne $DayOfMonth) { exit 0 }
& (Join-Path $PSScriptRoot 'invoke-etp-recovery-drill.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Monthly recovery drill failed.' }
