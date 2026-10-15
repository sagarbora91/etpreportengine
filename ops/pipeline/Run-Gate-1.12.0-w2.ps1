# 1.12.0 WAVE 2 copy (integrator, 13 Oct 2026): same as the wave-1 script, pointed at the integrate/112-w2 worktree.
# Run the 1.12.0 gate with its preparation (wave-1 integrator, 13 Oct 2026). Run ELEVATED in pwsh 7 (Sagar's UAC), ETP app closed.
#   1. Prepare-Gate-1.12.0-w2.ps1 under the heavy-job lock with lane builds paused: objects baseline (committed), ExpectedRolePermissions.json
#      regenerated (committed), both proven. Result line: prepare-1.12.0-w2-exit.txt beside this script.
#   2. Only if that PASSED: the hardened full gate (Start-Gate.ps1, lane A2: per-step timeouts, files not pipes, gate: line always
#      written) with -CleanLeftovers. Result: gate-1.12.0-exit.txt ("Gate exit code: N" + the gate: line).
$ErrorActionPreference = 'Stop'
$d = 'E:\ETP\Migration 2026-10-04'
$wt = 'E:\ETP\Code\Worktrees\integrate-112-w2'
$tools = 'E:\ETP\Code\Worktrees\_tools'
$pause = Join-Path $tools 'PAUSE-LANE-BUILDS'
$prepExit = Join-Path $d 'prepare-1.12.0-w2-exit.txt'
# Review m-3 (15 Oct 2026): keep the wave-1 gate's pass evidence before the shared exit file is cleared (first run only).
$gateExit = Join-Path $d 'gate-1.12.0-exit.txt'; $wave1Exit = Join-Path $d 'gate-1.12.0-wave1-exit.txt'
if ((Test-Path $gateExit) -and -not (Test-Path $wave1Exit)) { Copy-Item -LiteralPath $gateExit -Destination $wave1Exit }
Remove-Item $prepExit, $gateExit -ErrorAction SilentlyContinue
$code = 1
try {
    'gate-1.12.0-w2' | Set-Content $pause
    & E:\Tools\PowerShell\7\pwsh.exe -NoProfile -File (Join-Path $tools 'Invoke-Serialized.ps1') -Label gate-1.12.0-w2 -WaitMinutes 90 `
        -WorkingDirectory $wt -Command "& '$d\Prepare-Gate-1.12.0-w2.ps1' -Worktree '$wt'"
    $code = $LASTEXITCODE
}
finally { Remove-Item $pause -ErrorAction SilentlyContinue }
$line = Get-ChildItem 'E:\ETP\Reference' -Directory -Filter 'Gate-1.12.0-w2-prep-*' | Sort-Object Name -Descending | Select-Object -First 1 |
    ForEach-Object { Get-Content (Join-Path $_.FullName 'prepare-result.txt') -ErrorAction SilentlyContinue }
@("Prepare exit code: $code") + @($line) | Set-Content $prepExit
Get-Content $prepExit | Write-Host
if ($code) { Write-Host 'Preparation did not pass; the gate was NOT started.'; exit 1 }
& "$d\Start-Gate.ps1" -Version 1.12.0 -Worktree $wt -CleanLeftovers
exit $LASTEXITCODE
