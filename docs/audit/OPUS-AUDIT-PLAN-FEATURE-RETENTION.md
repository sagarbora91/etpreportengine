# Opus audit plan — feature retention from 1.8.8 to the Phase 5 candidate

Version 1.0 — 16 September 2026. Author: Claude (planner). Executor: Opus (auditor). Decision owner: Sagar.

Purpose: establish, with evidence, which capabilities of the two-month 1.8.8 development effort were lost, moved, restricted, consolidated, already broken, or intentionally retired in the Phase 0–5 rebuild, and turn the confirmed essential gaps into tasks inside the existing plan. The new sales-first shell (Phase 3) is kept. This is an audit and a backlog, not a rollback.

## 1. Inputs and their authority

| Input | Authority |
|---|---|
| `C:\Codex\Reporting Manger\ESSENTIAL-FEATURES-RECOVERY-REPORT.md` (EF-01…EF-08) and `FEATURE-COMPARISON-1.8.8-TO-PHASE5.md` (Codex, 16 Sep) | Evidence to test, not conclusions to accept. Every EF row gets its own verdict. |
| `docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md` v1.4 on `main` (`0d228f0`) | Governs. Consolidations it ordered (Phase 3 task 1 shell deletion, Phase 2 task 9 alias removal, Phase 5 table) are intentional, and a finding against them is a proposed scope change, never a regression. |
| `docs/audit/SESSION-HANDOFF-2026-09-16.md` on `phase-1/data-truth`; `PHASE-2/3/4-AUDIT.md`; `PHASE-5-REPORT.md` on `phase-5/secondary-modules`; `IMPORT-FAILURE-REGISTER.md` | Current state of the branches and the reopen lists this audit must feed, not duplicate. |
| Owner decisions | D1–D11 frozen; D12–D21 open; **D22 (16 Sep 2026): adjustment submission and sales-target entry are Owner-only.** A Store Manager who cannot reach them is correct behaviour. |

## 2. Rules

1. **No agents, no workflows.** One auditor, one session, source and committed evidence first, runtime second. Sagar's usage is limited; state this in the report header.
2. **Read-only on every worktree.** Inspect committed files with `git show <commit>:<path>` from the main checkout. Never checkout, reset, stash, merge or push. Never touch `EtpReporting`, live permissions, scheduled tasks, source workbooks or committed migrations. No real e-mail or WhatsApp sends.
3. **Pin the baseline before anything else** (section 3). No disposition is written until both snapshots are named.
4. **Eight dispositions only:** REMOVED (worked, gone) · ENTRY-POINT-LOST (code present, no reachable route) · MOVED (equivalent behaviour elsewhere) · RESTRICTED (role gate; state whether appropriate) · CONSOLIDATED (alias into another screen) · PRE-EXISTING-BROKEN (never worked or placeholder) · RETIRED-BY-DECISION (cite the plan task or D-number) · UNVERIFIED (state exactly what could not be run).
5. **Proof standard.** A class, a route constant, a button in XAML, or a passing source-text test proves nothing. A capability is present only when the auditor followed the caller chain to a rendered control in the current shell and, where runtime was available, exercised it. Runtime claims not exercised stay UNVERIFIED.
6. **Business impact over button counts.** Rank by what a shop evening or month-end cannot do without it.
7. **Output goes into the plan, not beside it.** Every confirmed gap becomes a task in an existing reopen list (Phase 2, 3 or 4), in Phase 5, or a proposed v1.5 amendment. No fourth backlog.

## 3. Baseline and candidate

1. Record the installed application's `Etp.Reporting.Desktop.exe` SHA-256 and file version from the shop PC (Sagar supplies it if the auditor cannot read `C:\Program Files\…`). Compare with the build of `d7eb515` (the commit Codex called 1.8.8) and with `ui/uiux-v4-touch-first-redesign` `05e16a9` (14 Sep working-tree fixes committed). Name the **baseline commit** as the one whose build matches, or state that no commit matches and use `d7eb515` with that caveat.
2. Candidate = the newest integrated application code, not the newest document: verify `phase-5/secondary-modules` head (was `0a0c38f`, migrations to 0026) and `integration/phase-2-3-4-fixes` (was `123e1d4`); record whether either has moved and whether any of it is on a remote.
3. Record for both snapshots: `git ls-files src | wc -l`, migration list, and the navigation tables (`UiNavigation.cs` at baseline; `TaskNavigation.cs`, `Shell/TaskNavigator.cs`, `Shell/Navigation/ShellRouteRegistry.cs` at candidate). These two route inventories are the spine of the matrix.

## 4. Procedure per recovery candidate

For each EF row: (a) quote Codex's claim; (b) baseline evidence — file, caller, role gate, and whether it actually worked in 1.8.8 (audits 01–06 already list broken items: cite them); (c) candidate evidence — route, rendering path, role gate at UI and at SQL (`0012`, `0013`, `0021`, `0022`, `0025`); (d) runtime check if available; (e) disposition; (f) business impact and priority; (g) recovery proposal with location in the new shell and observable acceptance.

| EF | What to establish | Where to look (candidate) | Runtime check (disposable DB, fixtures) | Likely disposition to test |
|---|---|---|---|---|
| EF-01 Operational health | Which of: SQL size and limit, backup free space, per-warning list, last successful import, last verified backup, last drill, support-package entry point are rendered anywhere. Distinguish integration health (task status) from database/recovery health. Storage controls were already unmounted at baseline — do not call that new. | `Modules/Dashboard/DashboardView.cs`, `DashboardViewState.cs`, `SqlServerDashboardQuery.cs`, `DatabaseOperationalHealth.cs`, Settings → Database strip (Phase 3 task 11), `0023_operations_status.sql` | Stop the SQL service backup folder access? No. Instead: point at a disposable DB with one failed import and no backup receipt; open Settings → Database and Today badge; screenshot; compare with `SELECT` over `automation_runs`/backup receipts. | MOVED and thinner. Gap likely: last verified backup and last drill not visible. Phase 4 finding that Express cannot encrypt backups raises priority. |
| EF-02 Import traceability | After importing a folder, closing and reopening the app: is there a durable list of files with Imported / Duplicate / Duplicate content / Not needed / Conflict / Failed, row counts and diagnostics per file? Received file ≠ successful import. | `Modules/Imports/ImportWorkspaceView.xaml(.cs)`, `ImportOperationState.cs`, `import_files`, `import_batches`, `import_row_outcomes`, `import_conflicts`; baseline import-history grid and its query | Import `dataonly-for-current-importer` folder twice into a disposable DB; restart app; find the history; check counts equal `SELECT` over `import_files`. | Most likely real gap: in-session results exist, durable history route lost. Priority: before staff rollout. |
| EF-03 Staff access | UI reachability and SQL grants for Store Manager on: registers (seven types), investigation search, adjustment submission, target entry. Apply D22: adjustments and targets Owner-only is correct. Registers and investigation are the open question. | `TaskNavigation.cs` role filters, `Modules/Registers/*`, investigation view, `0013`/`0022` grants, `SqlServerOperationsAdministrationService` role checks | Log in as Store Manager (settings user row) on the disposable DB; walk the rail; attempt each action; `EXECUTE AS USER` in SQL for the same writes. | RESTRICTED-appropriate for adjustments/targets (D22). Registers and investigation: expect ENTRY-POINT-LOST or RESTRICTED-inappropriate; recommend Store Manager read/write on registers with server-side check (Phase 5 Registers row already says so). |
| EF-04 Advanced query filters | Brand segment, transaction type, item filters as query filters (not detail-row search); a visible "applied scope" line; screen totals equal Excel/PDF totals under the filter. | `Modules/Reports/ReportsWorkspaceView.xaml(.cs)`, `ReportDetailFilter.cs`, `ReportPresentationControl.cs`, `Reporting/ReportDefinition.cs`, baseline Report Filters route | Open Brand and Item reports for 25 Aug; try to restrict to CLUSTER and TRANS_TYPE; export; compare totals with SQL. | REMOVED or CONSOLIDATED into detail search (which is not equivalent). Recovery: filter chips on the report header bound into the query, scope line printed on exports. Home: Phase 2 reopen list. |
| EF-05 Import review and retry | Whether separate Validate / Import validated rows and failed-import Retry disappeared; whether folder import still validates, cancels, and is duplicate-safe; whether a pre-commit review is needed. | `ImportWorkspaceView`, `DesktopImportCoordinator.cs`, `FolderImportContracts.cs`, `SqlServerImportPersistenceUseCase.cs`, register IF rows | Import a folder containing one deliberately corrupted workbook; observe per-file failure; look for a retry path; cancel mid-run. | Validate/persist split: CONSOLIDATED (Phase 1 task 3 ordered one action). Retry of a failed file: likely REMOVED; recommend "Retry failed" on the Problems tab. Pre-commit review: recommend NO by default (one-action import is the owner's stated goal); record as decision if Codex disagrees. |
| EF-06 Exception reports | Do the five chips reproduce the five old reports' rows; do exports carry all categories and say so? | Exceptions screen, `LoadDailyExceptionsAsync` filter, export code paths | Select each chip; export Excel and PDF; compare row sets with SQL. | CONSOLIDATED by plan (Phase 2 task 9). Gap to test: export scope not labelled. Fix: export carries the chip filter or a "All categories" label (ties to Phase 2 task 12 amount basis line). |
| EF-07 Inventory-group and staff targets | Is the old Inventory-Group report equal to a current stock screen (same grouping, same totals)? Where is target entry now, and is it Owner-only (D22)? Note any local/cloud difference Codex reported for Brands and targets. | `stock-group`/`stock-physical` history (plan §4.1 says identical query), Stock section screens, `monthly_targets`, `staff_sales_targets`, Settings → Masters | Run both groupings on 25 Aug data; compare totals; enter a target as Owner, attempt as Store Manager. | Inventory group: CONSOLIDATED (alias). Targets: MOVED + RESTRICTED-appropriate. |
| EF-08 Counters and summary PDF | Whether aggregate file/row/import counters, the chart and the management summary PDF have any consumer the owner values. | `DashboardView.cs` dead builders (audit 02), `DailyReportingPackService` | Ask Sagar one question (section 7). | RETIRED-BY-DECISION (Phase 3 task 11). If wanted, home is an Import → History tab, never the landing page. |

Also sweep, in the same pass, every baseline route in `UiNavigation.cs` not covered by EF-01…08 and give each a disposition in the matrix (Codex's "should not be reported as lost" table is the starting list; confirm Accounting, Archive, sharing, approvals, registers are RETAINED-UNFINISHED under Phase 5, and OCR / placeholders RETIRED-BY-DECISION).

## 5. Regression strategy the recovered items must carry

Every recovery task added to the plan states: the workflow test (arrange fixture → act through the service the button calls → assert persisted rows), the export test (open the file, assert totals and the scope line), the durability test (restart between act and assert where history is claimed), and the role test (UI reachability per role and `EXECUTE AS` denial in SQL). No control counts, no source-text assertions.

## 6. Deliverables

1. `docs/audit/FEATURE-RETENTION-AUDIT.md` with: owner summary (lost / moved / uncertain in ten lines); baseline and candidate commits with the installed-exe match; corrections to EF-01…08 with evidence; the retention matrix (route at baseline · route now · intended role · disposition · impact · priority · evidence); one recovery proposal per confirmed gap with location and acceptance; the mapping of each proposal to a Phase 2/3/4 reopen list, Phase 5, or a proposed v1.5 amendment; the regression strategy; the decision list.
2. Proposed plan amendments as exact text (not applied): the planner folds them into v1.5 with D22.
3. No phase is declared closed by this audit.

## 7. Decisions for Sagar (defaults recommended)

| # | Question | Default | Trade-off |
|---|---|---|---|
| Q1 | Store Manager may create and edit register entries and use investigation search? | Yes, with server-side check; VERIFIED stays Owner | Staff can record couriers and cash events without the Owner login; approval still Owner. |
| Q2 | Pre-commit review step before folder import writes facts? | No | One-action import was the goal; duplicate safety and per-file results already protect the data. |
| Q3 | Keep aggregate counters and management summary PDF? | Drop; keep a history tab | Saves Phase 5 time; the pack and DSR already cover management output. |
| Q4 | Failed-file retry button on the Problems tab? | Yes | Small; avoids re-importing a whole folder. |

D22 is already decided and is not re-asked.

## 8. Ready-to-paste prompt for Opus

```
You are the auditor for feature retention in the ETP Reporting Engine rebuild.
Repository: C:\Codex\Reporting Manger\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f (main checkout; other worktrees
under C:\Codex\Reporting Manger\ are active — read committed files with git show, never checkout, reset, merge or push).
Read first: docs/audit/OPUS-AUDIT-PLAN-FEATURE-RETENTION.md (this procedure) on main, then the two Codex reports at
C:\Codex\Reporting Manger\ESSENTIAL-FEATURES-RECOVERY-REPORT.md and FEATURE-COMPARISON-1.8.8-TO-PHASE5.md,
then plan v1.4, SESSION-HANDOFF-2026-09-16.md (on phase-1/data-truth), PHASE-2/3/4-AUDIT.md and PHASE-5-REPORT.md.
Constraints: one session, no sub-agents or workflows (the owner's usage is limited); pin the baseline and candidate commits
before any verdict; eight dispositions only; owner decision D22 = adjustments and targets are Owner-only, not a regression;
read-only on live database, permissions, tasks, workbooks and committed migrations; runtime checks on a disposable database
with the data-only fixtures, and anything not exercised is UNVERIFIED.
Deliverable: docs/audit/FEATURE-RETENTION-AUDIT.md on a new branch audit/feature-retention from main, committed as
"Audit feature retention 1.8.8 -> Phase 5 candidate" and pushed; proposed plan amendments as exact text inside it, not applied.
Do not declare any phase closed. Ask Sagar only the four questions in section 7 of the audit plan, with the defaults.
```
