# G16 rollback rehearsal launcher for the 1.12.0 go-live (decision 63; written 13 Oct 2026, NOT RUN YET, parse-checked only).
# Elevated, interactive. Shows the plan (dry run), asks YES, then runs
#   Reference\Work in progress 2026-10-04 (CONTAINS BUSINESS DATA)\Run-VM-Rollback-1.12.0.ps1
# which asks the ETPTest password once and YES again before every step that changes the VM, and stops at the first failure.
# Needs first: Run-VM-1.12.0-Sequence.ps1 -Path From196 (its stage 1 leaves the 1.12.0 start checkpoint
# 'VM195 Upgrade after automated steps, <date time>' on top of 'VM196 delta*').
# Sagar types: YES here, the ETPTest password, YES at each step. Log: E:\ETP\Migration 2026-10-04\vm-rollback-1.12.0-sequence.log
# Plain-English notes: E:\ETP\Reference\Work in progress 2026-10-13\VM-ROLLBACK-REHEARSAL-1.12.0-NOTES.md
# -DryRun: only the plan and the read-only host checks; nothing is changed (works unelevated).
# -StartCheckpoint '<exact name>': use that checkpoint as the 1.12.0 start (any parent; never the 1.11.4 checkpoint of 13 Oct 13:25).
param([switch]$DryRun, [string]$StartCheckpoint)

$log = 'E:\ETP\Migration 2026-10-04\vm-rollback-1.12.0-sequence.log'
$stagesFile = 'E:\ETP\Migration 2026-10-04\vm-rollback-1.12.0-stages.txt'
$script = 'E:\ETP\Reference\Work in progress 2026-10-04 (CONTAINS BUSINESS DATA)\Run-VM-Rollback-1.12.0.ps1'
$ps = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$vmName = 'ETP-Acceptance-186'
# The same rule the rehearsal script uses (its defaults): prefix, not the 1.11.4 checkpoint of 13 Oct 13:25, parent 'VM196 delta*'.
$startPattern = 'VM195 Upgrade after automated steps, *'
$excludePattern = 'VM195 Upgrade after automated steps, 2026-10-13 13:25*'
$parentPattern = 'VM196 delta*'
$extra = @()
# (Empty strings do not survive powershell.exe -File, so 'any parent' is passed as '*'; the 13:25 exclusion stays.)
if ($StartCheckpoint) { $startPattern = $StartCheckpoint; $parentPattern = '*'; $extra = @('-StartCheckpoint', $StartCheckpoint, '-StartCheckpointParent', '*') }

function Stop-Here([string]$Message, [int]$Code = 1) {
    Write-Host ''
    Write-Host $Message -ForegroundColor Red
    "ABORT $Message $(Get-Date -Format s)" | Add-Content $stagesFile
    try { Stop-Transcript | Out-Null } catch { }
    Read-Host 'Press Enter to close' | Out-Null
    exit $Code
}

Start-Transcript -Path $log -Force | Out-Null
if (-not (Test-Path -LiteralPath $script)) { Stop-Here "The rehearsal script is missing: $script" }

if ($DryRun) {
    & $ps -NoProfile -ExecutionPolicy Bypass -File $script -DryRun @extra
    try { Stop-Transcript | Out-Null } catch { }
    Read-Host 'Dry run finished (nothing changed) - press Enter to close' | Out-Null
    exit 0
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Stop-Here 'This window is not elevated. Close it, right-click Windows PowerShell > Run as administrator, and run this file again.'
}

Write-Host "Checkpoints on ${vmName}:" -ForegroundColor Cyan
try { Get-VMSnapshot -VMName $vmName | Sort-Object CreationTime | Format-Table Name, ParentSnapshotName, CreationTime -AutoSize | Out-String -Width 250 | Write-Host }
catch { Stop-Here "Hyper-V could not list the VM's checkpoints: $($_.Exception.Message)" }
$start = @(Get-VMSnapshot -VMName $vmName | Where-Object {
        $_.Name -like $startPattern -and (-not $excludePattern -or $_.Name -notlike $excludePattern) -and
        (-not $parentPattern -or ([string]$_.ParentSnapshotName) -like $parentPattern) } | Sort-Object CreationTime)
if ($start.Count -eq 0) {
    Stop-Here "No 1.12.0 start checkpoint (a '$startPattern' checkpoint taken on top of '$parentPattern', not the 1.11.4 one of 13 Oct 13:25). Run Run-VM-1.12.0-Sequence.ps1 -Path From196 first, or tell Claude which checkpoint holds 1.12.0 upgraded from 1.9.6."
}
$startName = $start[-1].Name
Write-Host "1.12.0 start checkpoint: $startName (parent '$($start[-1].ParentSnapshotName)')" -ForegroundColor Green

Write-Host ''
Write-Host '===== The plan (dry run, nothing changed) =====' -ForegroundColor Cyan
& $ps -NoProfile -ExecutionPolicy Bypass -File $script -DryRun @extra
if ($LASTEXITCODE) { Stop-Here "The dry run itself failed (exit $LASTEXITCODE). Nothing was changed." }

Write-Host ''
Write-Host 'This takes about 65-80 minutes (95 with the negative check). It restores VM checkpoints and changes the VM only;' -ForegroundColor Yellow
Write-Host 'Workpc''s own ETP and database are never touched. The current VM state is kept in a safety checkpoint first.' -ForegroundColor Yellow
$go = Read-Host 'Type YES to start the 1.12.0 rollback rehearsal (anything else stops)'
if ($go -cne 'YES') { Stop-Here 'Not started (no YES). Nothing was changed.' 0 }
$neg = Read-Host 'Also run the optional negative check afterwards (1.9.6 setup over the 69-migration database, on a throwaway copy)? Type YES or press Enter to skip'
$runArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script) + $extra
if ($neg -ceq 'YES') { $runArgs += '-NegativeCheck' }

"START 1.12.0 rollback rehearsal $(Get-Date -Format s) start='$startName' negative=$($neg -ceq 'YES')" | Add-Content $stagesFile
Write-Host ''
Write-Host '===== Rollback rehearsal 1.12.0 -> 1.9.6 =====' -ForegroundColor Cyan
& $ps @runArgs
$rc = $LASTEXITCODE
"END 1.12.0 rollback rehearsal exit $rc $(Get-Date -Format s)" | Add-Content $stagesFile

Write-Host ''
switch ($rc) {
    0 { Write-Host 'PASSED. Read SUMMARY.md in the newest E:\ETP\Reference\VM195-*-Rollback112 folder and tick G16 / checklist section 1.' -ForegroundColor Green }
    75 { Write-Host 'NOT RUN: a gate or build held the heavy-job lock for 30 minutes. Nothing was changed. Run this file again when it has finished.' -ForegroundColor Yellow }
    default { Write-Host 'FAILED or STOPPED. The red STOPPED line above says at which step and why. Read SUMMARY.md in the newest E:\ETP\Reference\VM195-*-Rollback112 folder and tell Claude the step and the exact words.' -ForegroundColor Red }
}
Write-Host ''
Write-Host 'To put the VM back on 1.12.0 later (elevated):' -ForegroundColor Cyan
Write-Host "  Stop-VM -Name $vmName -TurnOff -Force"
Write-Host "  Get-VMSnapshot -VMName $vmName | Where-Object Name -eq '$($startName -replace "'", "''")' | Restore-VMSnapshot -Confirm:`$false"
Write-Host "  Start-VM -Name $vmName"
try { Stop-Transcript | Out-Null } catch { }
Read-Host 'Rollback rehearsal finished - press Enter to close' | Out-Null
exit $rc
