# Re-run the elevated 1.12.0 gate after a fix: drops ONLY leftover EtpPhase0Test_* / EtpRecovery_* databases and etp_operations_test_*
# master procedures (EtpReporting is never touched), then the hardened gate. Run ELEVATED in pwsh 7, ETP app closed.
& 'E:\ETP\Migration 2026-10-04\Start-Gate.ps1' -Version 1.12.0 -Worktree 'E:\ETP\Code\Worktrees\release-1.12.0' -CleanLeftovers
exit $LASTEXITCODE
