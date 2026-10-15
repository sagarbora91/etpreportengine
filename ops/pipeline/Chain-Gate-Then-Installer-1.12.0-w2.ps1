# 1.12.0 WAVE 2 copy (integrator, 13 Oct 2026): same as the wave-1 script, pointed at the integrate/112-w2 worktree.
# 1.12.0: preparation + gate, and only if the gate PASSED build + publish the installer (13 Oct 2026). Run ELEVATED in pwsh 7 (Sagar's UAC),
# ETP app closed, VM not needed. Log: chain-1.12.0-w2.log beside this script.
#   & 'E:\ETP\Migration 2026-10-04\Chain-Gate-Then-Installer-1.12.0-w2.ps1'
$d = 'E:\ETP\Migration 2026-10-04'; $log = "$d\chain-1.12.0-w2.log"
"START $(Get-Date -Format s)" | Set-Content $log
& "$d\Run-Gate-1.12.0-w2.ps1"
Get-Content "$d\prepare-1.12.0-w2-exit.txt" -ErrorAction SilentlyContinue | ForEach-Object { "prepare: $_" } | Add-Content $log
$g = Get-Content "$d\gate-1.12.0-exit.txt" -ErrorAction SilentlyContinue
"gate: $($g -join ' | ')" | Add-Content $log
if (-not ($g -match '^Gate exit code: 0$')) { 'Preparation or gate did not pass; installer NOT built.' | Add-Content $log; exit 1 }
& "$d\Build-Installer-1.12.0-w2.ps1"
$b = Get-Content "$d\installer-build-1.12.0-w2-exit.txt" -ErrorAction SilentlyContinue; "installer: $b" | Add-Content $log
if ($b -notmatch 'Exit code: 0') { 'Installer build failed.' | Add-Content $log; exit 1 }
# Review M-2 (15 Oct 2026): DONE only when the publish step succeeded (the VM sequence waits on DONE); a failed publish
# (e.g. Explorer holding the folder open so Rename-Item fails) logs 'Publish failed' and exits 1.
& E:\Tools\PowerShell\7\pwsh.exe -NoProfile -File "$d\Publish-Installer-1.12.0-w2.ps1" *>&1 | Add-Content $log
$p = $LASTEXITCODE
if ($p -ne 0) { "Publish failed (exit $p); the installer folder may be incomplete. $(Get-Date -Format s)" | Add-Content $log; exit 1 }
"DONE $(Get-Date -Format s)" | Add-Content $log
