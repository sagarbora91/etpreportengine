# Start the elevated 1.13.0 gate only: the hardened generic Start-Gate.ps1. Run ELEVATED in pwsh 7, ETP app closed. (1.13.0 copy, lane G1.)
& 'E:\ETP\Migration 2026-10-04\Start-Gate.ps1' -Version 1.13.0 -Worktree 'E:\ETP\Code\Worktrees\release-1.13.0'
exit $LASTEXITCODE
