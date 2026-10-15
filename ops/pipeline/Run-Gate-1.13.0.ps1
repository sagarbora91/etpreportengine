# 1.13.0 (code-protection lane G1, 15 Oct 2026; copy of the 1.12.0-w2 family, not run yet). Run ELEVATED in pwsh 7 (Sagar's UAC), ETP app closed.
# 1.13.0 needs no objects-baseline / ExpectedRolePermissions regeneration step (no schema lane), so there is no Prepare-Gate here: it runs the
# hardened generic Start-Gate.ps1 on the release-1.13.0 worktree with -CleanLeftovers. Result: gate-1.13.0-exit.txt ("Gate exit code: N" + 'gate:' line).
# If the integrator adds migrations to 1.13.0, copy Prepare-Gate-1.12.0-w2.ps1 to a 1.13.0 version and call it here as Run-Gate-1.12.0-w2.ps1 does.
$ErrorActionPreference = 'Stop'
$d = 'E:\ETP\Migration 2026-10-04'
$wt = 'E:\ETP\Code\Worktrees\release-1.13.0'
if (-not (Test-Path -LiteralPath $wt)) { Write-Host "Worktree $wt not found (the integrator creates release/1.13.0 first)."; exit 1 }
& "$d\Start-Gate.ps1" -Version 1.13.0 -Worktree $wt -CleanLeftovers
exit $LASTEXITCODE
