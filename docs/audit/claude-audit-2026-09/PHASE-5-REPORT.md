# Phase 5 — Secondary modules

Status: **started; not complete and not submitted for closure**.

## Third increment — reject an accounting batch, 17 September 2026

Accounting now has **Reject selected**, using the renamed Batch decision reason field. The service and SQL procedure require Owner permission and a nonblank reason. Migration `0030_accounting_rejection.sql` adds the rejection reason, actor and timestamp. Rejection and its audit event commit together. The original approval reason is retained. Only DRAFT/REVIEW/APPROVED batches can be rejected; already rejected or exported batches are refused. Existing schema already permits REJECTED; migration to the new Phase 5 status vocabulary remains a separate increment and must update this procedure's eligible statuses too.

The real WPF/SQL positive and negative adjustment cases now click Reject selected and verify the persisted status/reason while preserving the approval reason. They reject blank reasons, refuse repeat/exported rejection and confirm SQL EXECUTE denial for Viewer and Store Manager. Service boundary tests independently enforce Owner-only access and mandatory reasons. Only disposable fixture databases were migrated. No financial amounts or existing migrations changed.

Third-increment validation (Release project/dependency builds succeeded; full solution/native acceptance not rerun):

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~PhaseFiveAccountingTests|FullyQualifiedName~AccountingMappingAtomicityTests|FullyQualifiedName~PhaseFourSecurityTests' --verbosity minimal -m:1 -nodeReuse:false
dotnet test tests-dotnet/Etp.Reporting.SqlServer.Tests/Etp.Reporting.SqlServer.Tests.csproj -c Release --filter FullyQualifiedName~AccountingServiceBoundaryTests --verbosity minimal -m:1 -nodeReuse:false
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~ProductisationPresentationSessionTests --verbosity minimal -m:1 -nodeReuse:false
```

```text
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 14 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 217 ms - Etp.Reporting.SqlServer.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 148 ms - Etp.Reporting.Desktop.Tests.dll (net10.0)
```

## Second increment — accounting approval integrity, 17 September 2026

Implemented on `phase-5/secondary-modules` after `2111ba5`:

- Accounting mapping request creation, Owner decision, previous-version deactivation and new-version insertion now share **one SQL connection and one transaction**. Existing approval/audit procedures participate in that transaction; permissions and financial formulas are unchanged.
- New migration `0029_accounting_approval_reason.sql` adds nullable `accounting_batches.approval_reason nvarchar(1000)`. Existing approvals retain NULL rather than inventing historical reasons. New approval writes the trimmed reason alongside the status/actor/time. Blank reasons are refused.
- The real WPF/SQL positive and negative adjustment tests now verify blank-reason rejection, persisted approval reason and the approved batch through a fresh service instance.
- `AccountingMappingAtomicityTests` injects a SQL trigger failure during replacement insertion. It verifies that the previous mapping remains active and that the new request, decision, audit entries and deactivation all roll back. Removing the fixture trigger allows a successful version-2 replacement with exactly one active mapping.

Validation commands:

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~PhaseFiveAccountingTests|FullyQualifiedName~AccountingMappingAtomicityTests|FullyQualifiedName~PhaseFourSecurityTests' --verbosity minimal -m:1 -nodeReuse:false
dotnet test tests-dotnet/Etp.Reporting.SqlServer.Tests/Etp.Reporting.SqlServer.Tests.csproj -c Release --filter FullyQualifiedName~AccountingServiceBoundaryTests --verbosity minimal -m:1 -nodeReuse:false
```

```text
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 18 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 668 ms - Etp.Reporting.SqlServer.Tests.dll (net10.0)
```

Release project/dependency builds succeeded. The first integration attempt exposed SQL batch compilation of the new-column check constraint; the uncommitted migration was corrected to compile that constraint after the column exists, then all nine tests passed. No existing migration was edited and no live shop database was migrated. The full solution suite and native acceptance were not rerun for this bounded increment; the previous 857-test result belongs to the earlier closure candidate.

**Next:** implement the revised accounting batch statuses and Reject, invoice duplication guard, company/environment settings and permanent export receipts, then consolidate Prepare → Review → Export. The other Phase 5 modules remain pending. Actual Tally transfer/read-back is Phase 7. Phase 5 remains unblocked and in progress.

The first-increment evidence below is historical.

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

- Accounting: revised statuses, invoice duplication guard, company/TEST-PRODUCTION settings, permanent export receipts, one Prepare → Review → Export screen and removal of alias tasks. Atomic mapping approval, persisted approval reason and Reject are implemented above.
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

Next coding increment: revised accounting statuses, followed by invoice duplication protection and the review/export workflow. This report does not close Phase 5 or any earlier phase.
