# Opus recovery track — feature retention fixes R1–R4 (separate from Codex's phases)

Version 1.0 — 17 September 2026. Planner: Claude. Builder: **Opus** (not Codex). Decision owner: Sagar.

Why a separate track: Codex stays on Phases 5 → 6 → 7 → 8 of `ETP-MASTER-AUDIT-AND-PHASED-PLAN.md` without deviation. The four confirmed gaps from `FEATURE-RETENTION-AUDIT.md` (branch `audit/feature-retention`) are small, touch surfaces Codex is not editing in Phase 5, and reuse code that already exists. Opus builds them on its own branch; Claude audits with the acceptance in section 4. The plan itself is unchanged; the audit's proposed D23 wording and the Phase 3 task 8 clarification are folded into plan v1.5 at the next revision together with D22.

Decisions in force: **D22** adjustments and staff targets Owner-only. **Q1 = yes** (Store Manager may create and edit register entries and use investigation search; VERIFIED and approvals stay Owner, enforced in SQL). **Q2 = no** pre-commit review. **Q3 = drop** counters and summary PDF. **Q4 = yes** retry button. **D23 (proposed, factual):** installed baseline is 1.8.1 at `8e35d83`; 1.8.8 (`d7eb515`) was never shipped.

## 1. Scope and rules for Opus

1. Branch `recovery/opus-r1-r4` from the head of `phase-5/secondary-modules` at the moment of starting (record the commit). Rebase onto that branch before hand-over; Codex's Phase 5 commits win on conflict.
2. Touch only the files named per item below plus their tests. No new migrations (none are needed). No changes to navigation structure beyond the tabs named here. No new documents except `docs/audit/RECOVERY-R1-R4-REPORT.md` (what changed · how to verify · test summary lines · screenshots at 1366x768 maximised and 816x480).
3. Every item ships with the four regression tests from the audit's section 7: workflow (service call → persisted rows), export where applicable, durability (restart between act and assert) for R1 and R4, role (UI reachability per role; `EXECUTE AS` denial in SQL where a write exists).
4. Keep the shell as it is: five rail buttons, tabs inside sections, 44 px controls, plain words. Nothing new on the landing screen.
5. **R5 (registers and investigation for Store Manager) is not on this track.** It is Phase 5 scope (Registers row) and Codex is already in those files; the planner adds Q1's answer to the Phase 5 reopen list. Opus does not touch registers.

## 2. Items

### R1 — Durable import history (P1)
- Files: `src/Etp.Reporting.Desktop/Modules/Imports/ImportWorkspaceView.xaml(.cs)`, `Shell/TaskNavigator.cs`, `TaskNavigation.cs`; a new read-only repository method in `Infrastructure.SqlServer` over `import_files` ⨝ `import_batches` ⨝ aggregated `import_row_outcomes`; Application contract beside `FolderImportContracts.cs`.
- Build: Import → **History** gets two sub-views, **Imports** (default) and **Received files** (the existing Source Inbox, relabelled). Imports loads from the database on activation, filtered by the header date scope, newest first: file, report, store, period, outcome (Imported / Duplicate / Duplicate content / Not needed / Conflict / Failed / Empty export), rows / new / present / conflicts; selecting a row shows its safe diagnostics (no customer data, no paths). Never reads the in-session `latestResults`.
- Also: Import → **Problems** loads from `import_row_outcomes` and `import_conflicts` for the header scope, not from session memory.
- Roles: Store Manager and Viewer read; nothing to write.

### R2 — Advanced report filters (P1)
- Files: `Modules/Reports/ReportsWorkspaceView.xaml(.cs)`, `ReportPresentationControl.cs`, `Shell/TaskNavigator.cs` (delete or wire the orphaned `report-filters` dispatch at the line the audit names), export metadata in `Reporting` (`OpenXmlReportExporter`, PDF header).
- Build: a **Filters** chip on the report header opens the existing "Report range and filters" panel (store, brand segment, transaction type, item). The values drive the report query scope, not the detail-row search. An **Applied scope** line renders above the grid and is written into Excel (metadata block) and PDF (page header). **Clear** restores the unfiltered totals. Reports without a filterable dimension hide the chip.
- Roles: all roles may filter.

### R3 — Retry a failed file (P1, small)
- Files: `Modules/Imports/ImportWorkspaceView.xaml(.cs)` only.
- Build: Import → **Problems** gets a **Retry failed** button beside the status filter, enabled exactly when `CanRetry` is true, calling the existing `RetryFailedBatchAsync()`. Ctrl+R stays. Only the failed file is re-processed.
- Roles: Store Manager and Owner; Viewer sees the button disabled with inline text "Owner or store manager can retry".

### R4 — Database and recovery health (P1)
- Files: `Modules/Settings/*` Database health view, `Infrastructure.SqlServer/DatabaseOperationalHealth.cs`, `SqlServerDashboardQuery.cs` (reuse), the `0023` operation-receipt readers.
- Build: Settings → Database → **Database health** shows two blocks. **Database and recovery** (new, on top): SQL database size and Express limit, backup folder free space, last successful import (time, files), last verified backup (time, file, fingerprint), last verified recovery drill (time, result), individual warnings, and a **Support package** button. Missing evidence reads **Missing**, stale evidence reads **Stale (n days)**, never Healthy. **Integration health** (existing table, renamed) below it.
- Roles: Owner only (it is under Settings); Store Manager sees the Today badge only when a warning is critical (already in Phase 3 task 11).

## 3. Placement map — the whole app after this track

| Rail section | Tabs | Who |
|---|---|---|
| **Today** | Sales (DSR, Export PDF, Share) · Cash (cash book, expense register, deposits sent / bank credited) · Walk-ins · Close day (readiness, today's register entries, Finalise, Generate pack) | Store Manager and Owner; Viewer read |
| **Import** | Import folder (default) · History → Imports **(R1)** / Received files · Problems (+ **Retry failed**, R3) | Store Manager and Owner; Viewer read |
| **Reports** | Report list with favourites · each report: date range, **Filters** chip **(R2)**, Export Excel / PDF · Exceptions (chips; export carries all categories and says so) · Investigation (invoice lookup; Store Manager per Q1, built by Codex in Phase 5) | all roles |
| **Stock** | Closing stock by brand · Physical count entry · Stock ledger · Registers for stock (inward, outward, stock transfers; Store Manager per Q1, Phase 5) | Store Manager and Owner |
| **Settings** (Owner) | Database (**health R4**, backup, drill, support package, connection) · Stores and masters (brands and DSR rows, tender modes, staff, **staff targets**, **monthly targets**) · Users · Automatic import · Sharing (contacts, e-mail, delivery history) · Accounting / Tally (Phase 5, Phase 7) · Approvals queue (Owner raises and decides under D22) · Help | Owner; Store Manager reaches only Stores and masters → Brands (role 2) |

Registers that are money or document records (credit notes, service receipts, vendor invoices) live under Today → Close day as "today's entries" with a Registers screen reachable from there; stock registers live under Stock. Nothing else is added to the rail.

## 4. Acceptance (Claude audits, one session, disposable database)

- **A-R1** Import the data-only fixture folder; close the app; reopen; Import → History → Imports lists all files with outcomes and row counts equal to `SELECT` over `import_files` / `import_row_outcomes`; row diagnostics contain no customer name or path; re-import the folder → new rows show Duplicate, earlier rows remain. Problems shows the recorded row outcomes after restart.
- **A-R2** Brand-wise Sales for 25 Aug: record the unfiltered total; apply a brand-segment filter; screen total equals SQL for that segment; Excel and PDF show the same total and the Applied scope line; Clear restores the original. The `report-filters` dead dispatch is gone or reachable.
- **A-R3** Folder with one corrupted workbook → Failed; press Retry on the touch screen; only that file re-runs; other files' counts unchanged; Viewer sees the button disabled.
- **A-R4** Disposable database with one failed import and no backup receipt: health shows the failed import, backup Missing, drill Missing; with a receipt present, shows its time and fingerprint; a receipt older than the configured age reads Stale; Store Manager cannot open the page.
- **A-R0** `dotnet test Etp.Reporting.slnx -c Release` green; the four regression tests exist per item; no migration added; branch rebases cleanly on `phase-5/secondary-modules`.

## 5. Ready-to-paste prompt for Opus

```
You are implementing the recovery track R1-R4 for the ETP Reporting Engine. Read first, in order:
docs/audit/OPUS-RECOVERY-TRACK.md on main (this brief), docs/audit/FEATURE-RETENTION-AUDIT.md on branch
audit/feature-retention (your own audit; sections 5 and 7), and section 2 rules of
docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md v1.4 (rules 3, 4, 9, 10 apply).
Repository: C:\Codex\Reporting Manger\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f. Codex is working in other
worktrees under C:\Codex\Reporting Manger\; never touch them. Create your own worktree for branch
recovery/opus-r1-r4 from the current head of phase-5/secondary-modules and record that commit in your report.
Build R1, R2, R3, R4 exactly as specified, in the order R3, R1, R4, R2, one commit per item plus tests.
Do not touch registers, investigation, approvals, accounting, migrations or the rail structure; R5 is Codex's.
No sub-agents or workflows. Run the app and capture screenshots at 1366x768 maximised and 816x480 for every
touched screen. Deliver docs/audit/RECOVERY-R1-R4-REPORT.md with the acceptance checklist A-R0..A-R4
self-assessed, then rebase on phase-5/secondary-modules and push the branch. Do not merge.
```
