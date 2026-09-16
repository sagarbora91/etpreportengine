# Phase 5 — Secondary modules

Status: **started; not complete and not submitted for closure**.

Branch: `phase-5/secondary-modules`.
Worktree: `C:\Codex\Reporting Manger\phase5-secondary-modules`.
Starting commit: `123e1d4` on `integration/phase-2-3-4-fixes`.

Sagar requested that Phase 5 coding start while the earlier phases await re-audit. This is a separate branch based on the integrated fixes so development does not repeat the Phase 4 integration problem. The plan normally calls for branching from main; this branch is temporarily based on the integration branch until that baseline is accepted and merged. Neither main nor the integration worktree has been changed by this increment. Earlier phase acceptance and deployment gates remain open.

Read before starting: the master plan rules, D6, Phase 5 scope/acceptance, `05-secondary-modules.md`, and `IMPORT-FAILURE-REGISTER.md`. The register currently contains IF-001 through IF-013, all marked VERIFIED by Claude; there are no OPEN rows assigned to Phase 5. The master plan and failure statuses were not edited.

## First increment: approved adjustments can be mapped for accounting

The source query already emits `ADJUSTMENT` for Owner-approved controlled adjustments, but the actual accounting mapping selector offered only NET_SALES, TENDER_TOTAL and SERVICE_SALES. An Owner therefore had no screen action to resolve the missing mapping. Added ADJUSTMENT to that selector in `AccountingWorkspaceView.xaml`.

`PhaseFiveAccountingTests` creates a disposable SQL database through the complete migration set and exercises the real WPF view, presentation session, SQL service and repository. For positive and negative synthetic adjustments dated 25 August 2026 it verifies:

- Pending adjustments are excluded from accounting source events.
- After approval, preview reports ADJUSTMENT as the missing mapping.
- The Owner selects ADJUSTMENT, enters ledgers/reason and clicks Approve mapping.
- Preview produces matching debit/credit totals and reverses the ledger direction for a negative adjustment.
- Save for review persists the balanced batch and adjustment entries without changing the adjustment amount.

Both cases failed on the missing selector option before the production change, then passed. No financial calculation, source amount, permission, migration or live database was changed. These are synthetic behavioral cases, not a claim that the real owner-data acceptance walkthrough is complete.

The pre-existing opt-in full-window smoke test still referred to the removed ConnectionStatus and DashboardHost controls. Updated that test to verify the current Owner/Today startup state and focused workspace. It now shows and closes the real composed MainWindow against a disposable database, checks startup audit events, renders both window sizes, and verifies the user's settings/preferences hashes remain unchanged. This is test-host startup, not production App.OnStartup or a native screenshot capture.

## Commands and results

Run from this worktree:

```powershell
# Before the selector change: reproduced the original defect in both cases.
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --filter FullyQualifiedName~PhaseFiveAccountingTests --verbosity minimal

# After the fix: new end-to-end cases plus the existing SQL security regressions.
$env:ETP_PHASE5_UI_EVIDENCE = Join-Path $PWD 'docs\audit\claude-audit-2026-09\phase-5-screenshots'
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~PhaseFiveAccountingTests|FullyQualifiedName~PhaseFourSecurityTests' --verbosity minimal
Remove-Item Env:ETP_PHASE5_UI_EVIDENCE

# Run this opt-in case alone.
$env:ETP_FULL_WINDOW_SMOKE = '1'
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --no-restore --filter FullyQualifiedName~PhaseFourFullWindowSmokeTests --verbosity minimal
Remove-Item Env:ETP_FULL_WINDOW_SMOKE
```

```text
Before fix:
Failed!  - Failed:     2, Passed:     0, Skipped:     0, Total:     2, Duration: 3 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)

After fix:
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 5 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 5 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
```

The Release project/dependency builds succeeded during these runs. A new full-solution Debug/Release gate and full suite have not been run for this small starting increment; the inherited baseline evidence remains in the Phase 4 report. The first opt-in startup attempt failed on stale removed-control lookups before the test was corrected. All test databases were fixture-owned and disposed.

## Rendered evidence

- [Accounting adjustment at 1366×768](phase-5-screenshots/Accounting-adjustment-1366x768-wpf.png)
- [Accounting adjustment at 816×480](phase-5-screenshots/Accounting-adjustment-816x480-wpf.png)

These images show the isolated Accounting view with real synthetic SQL preview rows, using the test host's default WPF styling. They are diagnostic renders, not the complete themed shell or native/DPI acceptance evidence. The compact view retains horizontal scrolling for the entry table. The unified Phase 5 Accounting screen and final themed/native captures remain outstanding.

## Remaining Phase 5 work

- Accounting: atomic mapping approval, persisted batch approval reason, Reject, one Prepare → Review → Export screen, removal of alias tasks and real export history.
- Archive/sharing: consolidated pack actions, contacts, actual SMTP/test-send, WhatsApp PDF handoff and final delivery history. No external messages have been sent by this work.
- Registers: Courier, verified transitions/reasons, server authorization and Close-day entries.
- Approvals: adjustment/restatement queue and decided history, import restatement producer and removal of unused request types.
- Store/tender masters: database-driven store lists and replacement of obsolete controlled masters.
- Automatic import: honest task status/history, app-lock release and unsupported-ZIP handling.
- Remaining OCR/Source Inbox cleanup, Help rewrite and investigation click-through.

## Acceptance self-assessment

| Criterion from the master plan | Current evidence / status |
| --- | --- |
| A5.1 Every navigation destination reachable by a role leads to a working screen for that role (Claude walks all of them as Owner, Store Manager and Viewer). | Not complete; the full role/navigation walkthrough remains. |
| A5.2 No screen in staff navigation is an alias of another. | Not complete; alias removal remains. |
| A5.3 Accounting: approve an adjustment for 25 Aug, then Prepare Batch for 25 Aug succeeds. | Initial implementation supported by two real-UI/SQL synthetic cases for 25 Aug; final real-data/Owner acceptance remains. |
| A5.4 `grep -r "WLMHW\|HEMW" src/` returns only the seed migration and tests. | Not complete; database-driven store replacement remains. |

Next coding increment: make mapping approval atomic and persist the batch decision reason before extending the Accounting review/export workflow. This report does not close Phase 5 or any earlier phase.
