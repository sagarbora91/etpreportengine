# Re-run the elevated 1.13.0 gate after a fix: drops ONLY leftover EtpPhase0Test_* / EtpRecovery_* databases and etp_operations_test_* master
# procedures (EtpReporting is never touched), then the hardened gate. Run ELEVATED in pwsh 7, ETP app closed. (1.13.0 copy, lane G1.)
& 'E:\ETP\Migration 2026-10-04\Start-Gate.ps1' -Version 1.13.0 -Worktree 'E:\ETP\Code\Worktrees\release-1.13.0' -CleanLeftovers
exit $LASTEXITCODE
