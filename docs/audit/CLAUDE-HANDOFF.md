> **17 September update:** Phase 5 is unblocked and in progress. The current handoff/report are on `phase-5/secondary-modules` in `C:/Codex/Reporting Manger/phase5-secondary-modules`. Application commits `9f0c0a7` and `f33fef8` implement atomic approval, saved reasons and Reject. The older branch-location/verdict statements below are historical. All project branches are being published in the 17 September synchronization; check origin rather than relying on the older local-only note.

# Claude handoff â€” ETP Reporting Engine rebuild

Updated 16 September 2026. Read [Phase 2, 3 and 4 reports](PHASE-2-3-4-REPORTS.md) and [this session's handoff](SESSION-HANDOFF-2026-09-16.md) first. The [15 September handoff](../_archive-2026-09/audit/CLAUDE-HANDOFF-2026-09-15.md) is historical.

## Where to find Phase 4 code

Verified 16 September 2026: the actual implementation is in **`C:/Codex/Reporting Manger/phase4-security-operations`**, a separate Git worktree on **`phase-4/security-operations`**. Its current HEAD is `1268c2bee47e7b159f51a855e219003e7fb9c44c` (Claude's audit); the implementation report is at `df35d90`. The Phase 1 checkout contains report copies, not this application code.

Run these read-only commands from any PowerShell directory; no checkout or merge is needed:

```powershell
$phase4Path = 'C:/Codex/Reporting Manger/phase4-security-operations'
git -C $phase4Path status --short --branch
git -C $phase4Path log -5 --oneline
git -C $phase4Path diff --stat 9a028a4..HEAD
Get-Content -LiteralPath "$phase4Path/src/Etp.Reporting.Infrastructure.SqlServer/LocalSqlConnectionPolicy.cs"
```

Start the source review at these paths relative to that worktree:

- Connection enforcement: `src/Etp.Reporting.Infrastructure.SqlServer/LocalSqlConnectionPolicy.cs`.
- Recovery certificates: `src/Etp.Reporting.Infrastructure.SqlServer/BackupCertificateService.cs`.
- SQL security/status: `database/migrations/0021_day_lock_security.sql`, `0022_least_privilege_audit.sql`, `0023_operations_status.sql`.
- Operations broker: `scripts/sql/etp-operations-broker.sql`; backup/task/recovery scripts are alongside it under `scripts/`.
- Tests: `tests-dotnet/Etp.Reporting.SqlServer.Tests/PhaseFourBoundaryTests.cs` and `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/PhaseFourSecurityTests.cs`, `PhaseFourRecoveryTests.cs`, `PhaseFourOperationsStatusTests.cs`, `PhaseFourSupportPackageTests.cs`.
- Implementation report and later audit: `docs/audit/claude-audit-2026-09/PHASE-4-REPORT.md` and `PHASE-4-AUDIT.md`.
- Deployment instructions: `docs/OPERATIONS.md`.

**Remote visibility:** `git ls-remote --heads origin phase-4/security-operations` returned no branch; Phase 4 has no upstream. A Claude session on this computer can inspect the worktree above. A remote-only session cannot fetch this branch yet. This location check did not publish the branch. Phase 4 remains REOPENED; locating its code does not resolve its integration or deployment blockers.

## Current verdict

**Current verdict: Phases 2, 3 and 4 are REOPENED by Claude's independent audits dated 16 September 2026.** Phase 1 remains CLOSED. Implementation code is present, but targeted defects and acceptance/deployment gates remain. Audit commits are Phase 2 `1e4cd97`, Phase 3 `2fc3846` and Phase 4 `1268c2b`; they review implementation commits `9241e26`, `12f108a` and `df35d90` respectively. Phase 3 includes Phase 2; Phase 4 remains separate. Do not merge Phase 3 before Phase 2 closure.

Read [Phase 2 audit](PHASE-2-AUDIT.md), [Phase 3 audit](PHASE-3-AUDIT.md) and [Phase 4 audit](PHASE-4-AUDIT.md) before the implementation reports.

## Current implementation and acceptance

| Phase | Code and branch | Status |
|---|---|---|
| 1 â€” Data truth | `phase-1/data-truth`, application baseline `d1e7c48`; Claude audit `e1ef5f3` | CLOSED. 790 corpus lines, 759 invoices, 3,955 movements, 25 monthly matches. |
| 2 â€” Evening reports | `phase-2/evening-reports`, `9241e26` | REOPENED by audit `1e4cd97`. Code `9241e26` was pushed. Its report recorded 679 passing tests. |
| 3 â€” Touch shell | `phase-3/touch-shell`, `12f108a`, includes Phase 2 | REOPENED by audit `2fc3846`. Code `12f108a` was pushed; recorded 680 passing tests. |
| 4 â€” Security/operations | `phase-4/security-operations`, `df35d90` | REOPENED by audit `1268c2b`. Separate, unpublished branch with merge/deployment blockers. |

The main project checkout remains on the Phase 1 branch. Documentation-only commits do not add Phase 2/3/4 application code. Inspect source and tests in the matching sibling worktree; the report index gives exact paths and report source commits.

## Owner decisions already given

- CN means Credit Note. Keep redemption and issue visible even when their net is zero.
- Gift Card has its own Gift Card category; CCheque / RTGS maps to Bank. Phase 2 migration 0024 applies these decisions. Do not ask for the same approval again.
- Phase 1 task 13, per-brand stock entry, was implemented in Phase 2. Reuse its owner brand-row master, form and explicit-save prefill behaviour.

## What remains

The implementation reports remain historical evidence. The later Claude audits take precedence for acceptance status; all three phases are **REOPENED**.

- **Phase 2:** restore first-run filters for DSR/invoices/cash; resolve C10 unmapped-type inconsistency; add six CI-safe golden fixtures; correct report labels. Arithmetic reconciled independently. D4, Titan history and the hybrid brand/cluster stock layout/manual corrections remain owner/source decisions. Native exports and bundled-font portability still need validation.
- **Phase 3:** fix full-size Reports-list overflow, 604-versus-610 DIP content height, footer clipping, surviving drawer, typed-date US formatting, control-count test, missing explicit input scopes and mixed Help spelling. Native audit now passes A3.1, A3.4, A3.5, A3.6 and A3.7; A3.2 fails on Reports; actual Windows DPI and the timed staff walk remain open. Do not report all native checks as unrun.
- **Phase 4:** fix combined migration ordering and later-schema least-privilege write coverage. Also resolve task-account/strict-ACL configuration, overly broad audit-detail rejection, stale encryption-default documentation and a developer sqlcmd path. Native deployment prerequisites remain open; the branch has not been published.

Apply findings as targeted fixes, with small commits. Do not alter approved financial figures to match paper-only adjustments. Do not weaken Owner-only brand-master permissions because an audit's suggested integration-test wording mentions Store Manager: valid Store Manager imports/entries must pass, while Owner-only master edits must still be denied. A later migration alone cannot repair an earlier migration that fails before it runs; the combined bootstrap order needs an explicit, tested solution respecting the committed-migration rule.


1. Audit Phase 2 against its implementation and sources: D4 brand mapping approval, missing Titan historical files, stock reference cutoff and native Excel/PDF/touch evidence are open. Missing acceptance is not a reason to rebuild working reports.
2. Fix the native Phase 3 audit failures, then verify actual Windows DPI/high contrast and timed shop-staff touch workflow. Claude already completed the native six-screen UIA sweep; retain those passes rather than marking it unrun. Retain the report's 610-pixel content-area distinction and Share/archive workflow limitation.
3. Integrate Phase 4 deliberately before deployment. Its migration 0022 references the Phase 1-removed `document_extractions` table and its permissions need later enrichment writes. Preserve both touch-shell and SQL authority changes in overlapping desktop files.
4. Complete D9/SQL-edition, signing, account/ACL, installed tasks, encrypted backup and second-machine recovery prerequisites. None was silently deployed here.
5. Continue the approved Phase 5 scope after the intended acceptance/integration sequence. Do not restart report engines, imports, cash calculations or the shell already built.

## Knowledge and evidence locations

- Primary evidence: current plan, phase reports and Git history; then branch-specific source/tests.
- Open Obsidian vault: `C:/Codex/Reporting Manger/ETP`, under `Project Knowledge`.
- Local source notes: this checkout's ignored `knowledge/` directory.
- Graphify: separate ignored `graphify-out/` in each phase worktree. No code-graph merge implies an application merge.
- Graph/knowledge refresh details, validation and preserved state: [session handoff](SESSION-HANDOFF-2026-09-16.md).

No application code, database, live settings, deployment or branch merge is part of this documentation/knowledge refresh.
