# Start the elevated 1.12.0 gate only (no preparation): the hardened generic Start-Gate.ps1 (lane A2). Run ELEVATED in pwsh 7, ETP app closed.
# Use Run-Gate-1.12.0.ps1 the first time: it also commits the objects baseline and the regenerated ExpectedRolePermissions.json.
& 'E:\ETP\Migration 2026-10-04\Start-Gate.ps1' -Version 1.12.0 -Worktree 'E:\ETP\Code\Worktrees\release-1.12.0'
exit $LASTEXITCODE
