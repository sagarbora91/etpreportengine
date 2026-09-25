# Claude handoff — 25 September 2026

> **16 Sep 2026 — plan v1.4.** `ETP-MASTER-AUDIT-AND-PHASED-PLAN.md` is now v1.4: Phase 7 (Tally transfer, Stage 2) and Phase 8 (Collections reconciliation, Stage 3) added after Phase 6; decisions D12–D21 recorded OPEN; Phase 2 tasks 12–15, Phase 4 task 6 (no certificate purchase) and the Phase 5 Accounting row amended; Section 8 crosswalk to the roadmap documents under `docs/roadmap/`. The auditor's procedure for the new phases is `docs/audit/OPUS-AUDIT-PLAN-PHASES-7-8.md`. Branch status for Phases 1–5 (Phase 1 CLOSED; 2, 3, 4 REOPENED; 5 started) is tracked in `SESSION-HANDOFF-2026-09-16.md` on `phase-1/data-truth`, not here.

Last updated: 15 September 2026, end of day. This is the one file a future Claude session reads first. It supersedes every earlier progress note.

**Read `docs/audit/PHASE-4-CLOSURE-2026-09-24.md` first**, then `docs/audit/WORKING-STATE-2026-09-22.md` ("Resume here", 25 September). The product line is `recovery/opus-r1-r4` in the worktree `C:/Codex/Reporting Manger/opus-recovery` (`38f5f04`); `main` is `acfdfcd` and current with it.

Phase 4 is **working and observed on the owner's PC, part proven in the VM, and untried on the shop's** — that is the honest one-line state, and the closure record says so rather than declaring the phase closed. On the owner's PC, on 24-25 September: migration `0032` applied and integrity-checked, the signed operations module installed on SQL Express with its signer's private key destroyed, three scheduled tasks running with nothing as SYSTEM and no stored password, a verified backup and a recovery drill taken by a non-administrator account, `NT AUTHORITY\SYSTEM` deactivated, and — the following morning, with nobody driving it — the first genuinely unattended backup. In the VM: the second-machine restore passed with row counts matching exactly; its own install is blocked because Developer Edition refuses to back up until the encrypted-backup recovery keys are exported.

Two defects were found by running the software rather than reading it, and both had blocked every machine silently: **every scheduled-task registration would have failed**, because Task Scheduler reports `COMPUTER\User` as the bare `User` and the code compared spellings; and **setup could never register its own tasks at all**, because Windows demands the target account's password once when registering an S4U task for anyone but the caller — an elevated administrator is refused, and so is SYSTEM. The automation account's tasks now go through the Task Scheduler COM API with a password reset to a fresh random value, used once and discarded.

Closing waits on four things, all small, set out in the closure record's section 8: the **shop PC install** (the only machine where the Owner is a SQL administrator only through `BUILTIN\Administrators`, and so the real test of the drill-task fix); Sagar's rulings on **A4.7 signing, D9 backup encryption and A4.4's row counts**, each a contradiction between the plan and the 17 September decisions; four small product fixes; and capturing the **SQL backup folder's permissions** on both machines (28 `.bak` files, three with real customer data, never checked).

Boundaries unchanged: never write to live `EtpReporting` except through Sagar's elevated steps; never edit a committed migration; never touch Codex's worktrees; pushing to `main`, VM power state, elevated backups, account creation and passwords are Sagar's.

Codex finished Phase 5 coding on 24 September; auditing it is separate work that starts after Phase 4 closes.

---

# Earlier handoff — 22 September 2026 (history; superseded above)

## Start here (22 Sep)

The product line is **`recovery/opus-r1-r4`** in the worktree `C:/Codex/Reporting Manger/opus-recovery` (pushed; latest `8139041`). `main` is at `714d117`, which already contains this branch up to `fdcc7fe`; the newer commits are **not** on `main` — Sagar decides that merge (the auto-mode permission check refused it on 22 Sep). **Read `docs/audit/WORKING-STATE-2026-09-22.md` first**: its "Resume here" section is the exact state, what is still running, and the next steps in order.

In one paragraph: Phase 4's last three defects are fixed and proven on real SQL Server Express — P4-13 (the operations module could not be installed on Express), P4-14 (Settings > Users locked every Store Manager and Viewer out, and 0022 locked `NT AUTHORITY\SYSTEM` out of the live database; migration `0032` repairs it), P4-15 (the recovery drill needs a SQL administrator; Owner decision: it runs as the Owner). An adversarial review's 11 findings are all dealt with (one was wrong and is recorded as such). Also fixed: every scheduled-task registration would have failed (Task Scheduler reports `COMPUTER\User` as `User`), and an interactive launch with a rejected `--connection-string` left an invisible process holding the installer's lock. The "startup DISPATCHER_UNHANDLED dialog" was diagnosed as test-launch artefacts, not an owner-facing fault. A3.8 is recorded as accepted on the Owner's report.

Not done yet, in order: Sagar's decisions (merge to `main`; P3-3; D14; `EtpReportingHelios`) → review and commit the two background tasks' uncommitted work in this worktree (flaky tests; test diagnostics isolation) → build the installer on a quiet machine (the release gate starves SQL Express memory under load; see WORKING-STATE) → Sagar's elevated install on this PC (runbook `docs/audit/LIVE-INSTALL-RUNBOOK-2026-09-22.md`, verify with `scratchpad/recovery/live-verify.ps1` from the session scratchpad or re-create it) → VM `ETP-Acceptance-186` and the second-machine restore (`docs/audit/VM-AND-SECOND-MACHINE-RUNBOOK-2026-09-22.md`) → shop PC (still `1.8.1+8e35d83`) last. Decision papers: `docs/audit/D14-FONT-DECISION-2026-09-22.md`, `docs/audit/ETPREPORTINGHELIOS-FINDINGS-2026-09-22.md` (written by a verified agent workflow on 22 Sep; review before relying on them).

Boundaries that hold: never write to live `EtpReporting` except through Sagar's elevated steps; never edit a committed migration; never touch Codex's worktrees; push to `main`, saving/stopping the VM, elevated backups and live migrations are Sagar's to run or approve.

---

# Earlier handoff — 17 September 2026 (history; superseded above)

## Start here (17 Sep)

Phase 5 is **unblocked and in progress**, not complete. Continue in `C:/Codex/Reporting Manger/phase5-secondary-modules`, branch `phase-5/secondary-modules`. Latest application commit: `f33fef8` (Reject); preceding increment: `9f0c0a7` (atomic mapping approval and saved approval reasons). Read `claude-audit-2026-09/PHASE-5-REPORT.md` for commands/evidence.

The latest master plan is on **main**, version 1.4 with Phases 7/8 and subsequent recovery-track additions. The plan copy on the Phase 5 branch is older. Read `git show main:docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md`; do not silently replace or rewrite Claude's plan.

## Completed coding

- Phase 2/3 closure: approved brand mappings, durable import history including duplicates/restarts, query filters with Excel/PDF scope, selective Retry failed; full closure gate was 857 passed / 0 failed / 3 opt-in skipped.
- Phase 5 increment 1: approved positive/negative adjustments can be mapped and prepared through the actual accounting screen.
- Increment 2: mapping request/decision/save are one SQL transaction; migration 0029 persists the batch approval reason. Failure-injection test proves rollback restores the prior mapping and removes orphan approvals/audits.
- Increment 3: Owner-only Reject selected with mandatory reason, actor/time and atomic audit. Migration 0030; already rejected/exported batches are refused and original approval reason survives.
- Latest targeted checks: 9 integration/security + 6 accounting boundary + 9 desktop presentation tests passed, zero failures. Release dependencies built. No full-suite/native acceptance claim is made for these last two increments.

## Resume coding

1. Revised accounting statuses DRAFT/BLOCKED/APPROVED_READY/EXPORTED_AWAITING_IMPORT/REJECTED. Update the rejection procedure's eligible statuses in a NEW migration too.
2. Invoice business-key duplication guard; company and TEST/PRODUCTION settings; persistent export receipts/history; unified Prepare → Review → Export screen and alias removal.
3. Archive/sharing, registers, approval/restatement queue, database-driven store/master lists, honest automatic-import status, remaining OCR cleanup, Help and investigation navigation. Follow the latest plan plus approved retention decisions; preserve retained Received files functionality.
4. Actual Titan 2025–26/Service source acceptance is deferred into Phase 5; no synthetic substitute. Actual Tally transfer/read-back belongs to Phase 7.

## Parallel branches / acceptance

- `phase-2-3/closure` at fa8706f includes Claude's re-audit: Phase 2 has no remaining coding or owner decisions; stock 814/502 accepted; A2.6 deliberately deferred.
- Phase 5 includes Phase 4 script fixes through 2111ba5. P4-7/8/9 were written by the auditor; preserve the independence caveat in PHASE-4-AUDIT.md.
- `phase-4/defect-fixes` at 71a1aa2 records **A3.3 native 125% DPI PASS**. A3.8 shop-staff touch walkthrough remains NOT VERIFIED in that audit.
- `ux/title-version` at a2ec520 adds the running build to the title. This separate branch and its DPI audit are NOT in the current Phase 5 branch. Do not lose them or claim them merged.
- Main remains 1d02059; publishing branches does not close phases or merge them. Sagar controls integration.

## Safety and tooling

Never edit committed migrations; next migration after this branch's 0030 must be checked against other branches before allocation. Do not commit private source workbooks, certificates, credentials, graph databases or generated build/test output. No live shop migration/install was performed by these increments.

User explicitly requested Graphify and Obsidian refresh on 17 September; this overrides the older plan's instruction not to maintain those local notes for this refresh only. Current Graphify output is local/ignored under this Phase 5 worktree's graphify-out; code-review graph is local/ignored under .code-review-graph. Active Obsidian vault: C:/Codex/Reporting Manger/ETP. Its Project Knowledge folder is a reading copy, not two-way sync.
