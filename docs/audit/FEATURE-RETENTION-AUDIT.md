# Feature retention audit — 1.8.8 development effort to the Phase 5 candidate

Auditor: Claude (Opus 5). Date: 16 September 2026. Procedure: `docs/audit/OPUS-AUDIT-PLAN-FEATURE-RETENTION.md` v1.0.

**One session, no sub-agents, no workflows — the owner's usage is limited.** Source and committed evidence first, one focused runtime session second. Read-only throughout: no branch was checked out, reset, merged or pushed; the live `EtpReporting` database, Windows permissions, scheduled tasks, source workbooks and committed migrations were not modified. The runtime work used one disposable database built from the data-only fixtures, dropped afterwards.

No phase is declared closed by this audit, and the redesign is not declared feature-complete.

## 1. Owner summary

1. **The shop has never run 1.8.8.** The installed application is **version 1.8.1**, built 27 August from commit `8e35d83`. Codex's baseline `d7eb515` is 41 commits later and was never installed.
2. That single fact rescales the whole question: most of what Codex listed as "lost" was **built but never in your hands**, so losing it costs you nothing you were using.
3. **Genuinely lost and worth fixing: durable import history.** After you close the app, there is no screen that tells you what imported, duplicated, conflicted or failed. Proven at runtime.
4. **Genuinely lost: the advanced report filters.** Brand-segment, transaction-type and item filters still exist in the code but nothing in the new shell opens them.
5. **Genuinely lost for touch: retry of a failed file.** It survives only as Ctrl+R on a keyboard — unusable on a touch shop PC.
6. **"Database health" is not database health.** It shows folder/email/queue status. It never shows SQL size, backup space, last verified backup or last recovery drill.
7. **Staff lost the registers and invoice investigation.** Both moved to Owner-only. That is a policy question for you, not a bug — question Q1.
8. **Adjustments and staff targets being Owner-only is correct** under your decision D22, and is not a regression.
9. Nothing indicates any historical business record was deleted. This is about routes and screens, not data.
10. Nothing here is urgent enough to stop Phase 5; items 3–6 should land before staff use the machine daily.

## 2. Baseline and candidate

### The installed application — read from the shop PC

| Property | Value |
|---|---|
| Path | `C:\Program Files\Saagar Traders\ETP Reporting Engine\Etp.Reporting.Desktop.exe` |
| SHA-256 | `122CD748AA5D3DAE929426905C71B5F87FD273FC1666050CFF2D814DF639F37C` |
| Size | 159,334,393 bytes |
| FileVersion | **1.8.1.0** |
| ProductVersion | **1.8.1+8e35d83f6d442e3add10920f4462042bece8ffce** |
| `release.json` | `{"builtUtc":"2026-08-27T11:36:27Z","commit":"8e35d83f6d44","version":"1.8.1"}` |
| Authenticode | **NotSigned** |

The install folder's own `SHA256SUMS.txt` contains exactly the hash above, so the binary is self-consistent with its manifest and has not been replaced since installation.

### Baseline: `8e35d83f6d442e3add10920f4462042bece8ffce`

The commit SHA is embedded in the binary's informational version and corroborated by `release.json`. **A commit matches, so the plan's fallback to `d7eb515` does not apply.** Baseline is `8e35d83`, 26 August 2026, "feat: expand operational report catalogue".

`git ls-files src` at `8e35d83`: **79 files**, **14 migrations**. The entire desktop project is ten files — `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, `MainWindow.Productisation.cs`, `PowerShellOperationsService.cs` and assets. **There is no `UiNavigation.cs`, no module folders and no task catalogue.** The shop's application was a single monolithic window.

### Development head, never shipped: `d7eb515` — "1.8.8"

`d7eb515` (13 September) declares `VersionPrefix 1.8.8`; `8e35d83` declares `1.7.0` and was released as 1.8.1. `8e35d83` is an ancestor of `d7eb515` with **41 commits** between them. `d7eb515` has **224 src files**, the module structure, and the 165-line `UiNavigation.cs` the audit plan names as the matrix spine.

**This is the correction that matters most.** The plan's spine — "every baseline route in `UiNavigation.cs`" — exists only in the unshipped development head. The matrix in section 4 is therefore built from `d7eb515`, and every row carries a column saying whether the shop ever had it. Codex's comparison is not wrong; it compared the right two trees. It simply could not confirm which one the owner was running, and said so explicitly: *"The exact installed version remains unverified."* It is now verified, and the answer changes the business impact of most rows from "lost" to "never delivered".

### Candidate: `0a0c38f53a75a116854c8a14f4bf1bbaa1aba071`

`phase-5/secondary-modules` head, 16 September 17:59, "Record the first Phase 5 accounting increment". Its merge-base with `integration/phase-2-3-4-fixes` is `123e1d4`, so it carries the integrated Phase 2–4 fixes plus three Phase 5 commits. **260 src files, 26 migrations.**

`git branch -r --contains 0a0c38f` is empty: **the candidate is local only and on no remote.** `integration/phase-2-3-4-fixes` has since moved to `65f4859`, but those two commits are my own audit documents — no application code. The candidate for this audit is `0a0c38f` and it did not move during the audit.

Route tables at the candidate: `TaskNavigation.cs` (the live catalogue), `Shell/TaskNavigator.cs` (dispatch), `Shell/Navigation/ShellRouteRegistry.cs`, and a residual 60-line `UiNavigation.cs`.

### Runtime environment used

Candidate built from its own worktree: `dotnet build src/Etp.Reporting.Desktop -c Release` → **0 warnings, 0 errors**. Disposable database `EtpPhase1Test_FeatRetention` bootstrapped through all 26 migrations and loaded from `ETP Source Data\HEMW\dataonly-for-current-importer\` (5 workbooks; 318 invoices across 8 months; invoice 100000068 correctly present three times under FY-end years 2025, 2026 and 2027). Application launched with `--connection-string` against it, driven and measured through UI Automation, then closed and the database dropped.

## 3. Corrections to EF-01 … EF-08

Dispositions use only the eight permitted values.

### EF-01 Operational health — **MOVED (materially thinner)**. Codex's claim upheld.

Claim: *"The current Database health task displays integration health, not full SQL/backup capacity and recovery status."*

Confirmed at runtime. Settings → Database → **Database health** renders a panel titled **"Product integration health"** with five rows: Document repository (Healthy), Share folder (Warning), Email (Warning), Import conflicts (Healthy), Pending approvals (Healthy). There is **no** SQL database size, no maximum size, no backup free space, no last successful import, no last verified backup and no last recovery drill.

Correction to emphasis, not to substance: Codex is right that the storage cards were already unmounted at `d7eb515`, so this is a carried-forward defect rather than a fresh deletion. Against the **installed** baseline the point is stronger still — 1.8.1 had no such screen at all, so nothing was taken from the owner. The gap is against the *plan's* intent (Phase 3 task 11 promised a health strip under Settings → Database), not against 1.8.8.

Priority is raised by an unrelated finding: Phase 4 established that SQL Express cannot produce an encrypted backup, and the Phase 4 audit records 17 unencrypted `.bak` files on the machine. Backup freshness is exactly what this screen should surface.

### EF-02 Import history and outcome traceability — **ENTRY-POINT-LOST**. Confirmed; the most serious finding.

Claim: *"The old import-history task is absent although its grid remains."*

Upheld, and proven at runtime rather than inferred. Source: `ImportWorkspaceView.xaml.cs:20` declares `private IReadOnlyList<FolderImportFileResult> latestResults = [];` and every assignment (`:86`, `:98`, `:104`) comes from an import run in the current session. `SelectTask` (`:44-53`) filters that same in-memory list. **No activation path queries `import_files`, `import_batches` or `import_row_outcomes`.**

Runtime proof: the disposable database held **5 `import_files`, 5 `import_batches` and 8,036 `import_row_outcomes`** written by a prior import. A freshly launched application showed:

- **Import → History** → "Source Inbox", a document-attachment screen ("Attach a scanned document to a business day…"). Not import history.
- **Import → Problems** → **"0 problems. Select a status to filter."** — against 8,036 recorded row outcomes.

So the durable record exists in the database and the renderer exists in XAML, but no route joins them after a restart. Baseline `d7eb515` had `import-history` routed to the Dashboard grid; the installed 1.8.1 had no such task either, so again the shop loses nothing it had — but the plan's own promise of per-file outcomes is unmet once the app closes.

### EF-03 Staff access — **split verdict**. Codex's claim upheld on facts; two of four are correct behaviour.

Baseline `d7eb515` `TaskNavigation.cs` minimum roles versus candidate:

| Capability | Baseline role | Candidate role | Disposition |
|---|---|---|---|
| Registers — inward, outward, credit notes, service receipts, stock transfers, vendor invoices | StoreManager (2) | **Owner (3)** | **RESTRICTED** — appropriateness is question Q1 |
| Investigation (invoice lookup) | **Viewer (1)** | **Owner (3)** | **RESTRICTED** — appropriateness is question Q1 |
| Adjustment request | StoreManager (2) | Owner (3) | **RESTRICTED — appropriate** under D22 |
| Staff targets | StoreManager (2) | Owner (3) | **RESTRICTED — appropriate** under D22 |

Two corrections to Codex. First, the **expense register did not move**: `register-expense` remains role 2 at Today → Cash, so the one register staff use every evening is still reachable. Second, **courier register was an unavailable placeholder** at baseline (`available: false`, "Register schema is not yet configured"), so six registers moved, not seven, and none of the six was proven working at baseline beyond being routable.

Role evaluation is deterministic from `TaskDestination.IsAllowed`: `MinimumRole >= 3` requires `CanAdminister`. No runtime role-switch was needed to establish reachability, and I did not perform one; the SQL-side grant behaviour for these tables was separately exercised in the Phase 4 re-audit (direct DML denied, error 229).

### EF-04 Advanced report query filters — **ENTRY-POINT-LOST**, not removed. Correction to Codex.

Claim: *"Dedicated Report Filters route was removed… input controls remain in the underlying report view."*

The facts are right; the disposition should be ENTRY-POINT-LOST, and the difference matters because recovery is cheap.

- Baseline `d7eb515` `TaskNavigation.cs:105`: `Add("report-filters", "Report Filters", "Reports", "Filters", "Sales Reports", "report-filters", 1, "brand segment", "transaction type", "item filter")` — a Viewer-visible task.
- Candidate `ReportsWorkspaceView.xaml:8,11` still renders a **"Report range and filters"** header and four inputs: `StoreFilterInput`, `BrandSegmentFilterInput`, `TransactionTypeFilterInput`, `ItemFilterInput`.
- Candidate `Shell/TaskNavigator.cs:404` still dispatches `if (task.Section == "report-filters") { view = reportsWorkspaceView; body = {0,1}; }`.
- **But no `TaskDestination` in the candidate carries section `report-filters`.** The dispatch is unreachable code.

Runtime confirmation: opening Reports → Brand-wise Sales, the only inputs rendered were *Report start date*, *Report end date* and *Filter report detail rows*. The four query filters did not appear. Note also that a report activation returns at `TaskNavigator.cs:364` before `FocusedTaskLayout.Show` is called, so which panels are visible depends on prior navigation — a second reason the filters cannot be relied on.

Codex is also right that detail search is not a substitute: `ReportsWorkspaceView.xaml:17` states in the UI itself, "Filters affect the visible list. Exports retain all exceptions."

### EF-05 Import review and retry — **split; CONSOLIDATED plus ENTRY-POINT-LOST**. Correction to Codex.

- **Validate / Import-validated-rows split: CONSOLIDATED — by decision.** Plan Phase 1 task 3 ordered one-action folder import. This is intentional scope, not a regression, and my recommendation on a pre-commit review step is *no* (question Q2).
- **Retry of a failed file: ENTRY-POINT-LOST, not REMOVED.** `ImportWorkspaceView.CanRetry` and `RetryFailedBatchAsync()` exist and are wired: `MainWindow.Shell.cs:269` handles `ShellCommand.RetryImport`, `ShellNavigation.cs:53` binds it to **Ctrl+R**, and `HelpCentre.cs:149` lists it as an executable Help action. There is **no button**. On a touch-first shop PC with no keyboard in routine use, a Ctrl-chord is not a reachable route for staff. The capability is one small control away from working.

### EF-06 Exception reports — **CONSOLIDATED by decision**; the export gap is narrower than claimed.

Plan Phase 2 task 9 ordered the consolidation of the five exception reports into chips, so the consolidation itself is RETIRED-BY-DECISION territory and not a regression. Codex's residual concern — a user filtering to Tender and receiving a full-set export — is real in behaviour but **the UI already states it**: the chip panel carries the literal sentence "Filters affect the visible list. Exports retain all exceptions." (`ReportsWorkspaceView.xaml:17`). So the "silent surprise" framing overstates it. What remains genuinely open is whether category-scoped export is *wanted*; the labelling defect Codex feared is already handled. I did not export each chip and diff the row sets, so the equality of chip rows to the five old reports' rows is **UNVERIFIED**.

### EF-07 Inventory group and staff targets — **CONSOLIDATED / MOVED**, one half UNVERIFIED.

- **Inventory-Group Report: CONSOLIDATED.** The catalogue alias was removed; plan §4.1 records the query as identical to a current stock grouping. I did **not** run both groupings and compare totals, so equivalence is **UNVERIFIED** — and a similarly named screen is explicitly not proof under the plan's own proof standard.
- **Staff targets: MOVED and RESTRICTED — appropriate.** Now Settings → Stores & masters → Staff targets, Owner-only, which D22 makes correct. Codex's noted local/cloud difference is confirmed and resolved in the candidate's favour: `masters` ("Brands and targets") is role **2**, so a Store Manager can edit brands while `staff-target` stays role 3.

### EF-08 Counters and management summary PDF — **RETIRED-BY-DECISION**.

Plan Phase 3 task 11 explicitly deletes the dead dashboard card builders and the "Today overview" IT dashboard. Audit 02 had already recorded those builders as unused at baseline, so they were not working features being removed. Whether anything is wanted in their place is question Q3. If yes, the home is an Import → History tab, never the landing page.

## 4. Retention matrix

Every route in baseline `d7eb515` `UiNavigation.cs`. **"In 1.8.1?"** answers whether the shop's installed build actually had it — for all rows the answer is *no*, because 1.8.1 had no task catalogue at all; the column is kept to make the business-impact column honest. Priority: **P1** before staff use the machine daily · **P2** before the redesign is called complete · **P3** optional.

| Route at baseline (`UiNavigation.cs`) | Route now (candidate) | Intended role | Disposition | Business impact | Pri | Evidence |
|---|---|---|---|---|---|---|
| `dashboard` Overview | — | Viewer | RETIRED-BY-DECISION | None; sales-first landing is the goal | — | Plan P3 task 11 |
| `daily-health` Daily Health | Settings → Database → Database health (thinner) | Owner | MOVED | Owner cannot see capacity/backup freshness | P1 | Runtime §3 EF-01 |
| `manual-entry` Manual Entry | Today → Walk-ins / Cash | StoreManager | MOVED | None | — | `TaskNavigation` `walk-ins`, `cash-input` role 2 |
| `readiness` Readiness | Today → Close day | Viewer | MOVED | None | — | `readiness` role 1 |
| `finalisation` Finalisation Status | Today → Close day → Finalise day | StoreManager | MOVED | None | — | `finalisation` role 2 |
| `trends` Trends | Reports → Management → Trends | Viewer | MOVED | None | — | `trends` role 1 |
| `store-comparison` | Report `sales-combined` | Viewer | CONSOLIDATED | None | — | Catalogue |
| `target-progress` | Report `staff` | Viewer | CONSOLIDATED | None | — | Catalogue |
| `control-summary` Control Summary | Settings → Control centre (Open items / Data quality) | Owner | MOVED + RESTRICTED | Staff cannot see open items | P2 | `open-items` role 3 |
| `recent-activity` Recent Activity | Settings → Database → Audit trail | Owner | MOVED + RESTRICTED | Low | P3 | `audit` role 3 |
| `reports-home` Reports Overview | Reports → All reports | Viewer | MOVED | None | — | `reports-list` |
| 3 favourite report shortcuts | Reports → Favourites | Viewer | MOVED | None | — | `favourite-reports` |
| 29 catalogue reports | 23 catalogue reports | Viewer | CONSOLIDATED | None — 5 exception entries became chips, 1 stock alias dropped | — | §3 EF-06/EF-07 |
| `store-daily-pack`, `combined-pack` | Today → Close day | Viewer | MOVED | None | — | `TaskNavigation` |
| `historical-packs` | Reports → Archive | Viewer | MOVED | None | — | `historical-packs` |
| `category-sales`, `sell-through`, `stock-turn`, `days-cover` | — | Viewer | PRE-EXISTING-BROKEN | None — `Future(...)` unavailable placeholders | — | `UiNavigation.cs` `Future()` |
| `import-overview` Import Overview | Import → Import | StoreManager | MOVED | None | — | `import-files` role 2 |
| `import-files` Import Files | Import → Import folder | StoreManager | MOVED | None | — | `import-files` |
| `source-inbox` Source Inbox | Import → History (Received files) | StoreManager | MOVED | Confusing: History now means received, not imported | P1 | Runtime §3 EF-02 |
| `bulk-import` Bulk Historical Import | — (folder import detects dates) | StoreManager | CONSOLIDATED | None | — | Plan P1 task 10 |
| `watch-folder` Watch Folder | Settings → Integrations | Owner | MOVED + RESTRICTED | Low | P3 | `watch-folder` role 3 |
| `quarantine`, `duplicates`, `already-present`, `conflicts`, `unknown-layouts` | Import → Problems (aggregated) | StoreManager | CONSOLIDATED | Session-only — see below | P1 | `SelectTask` filters `latestResults` |
| `import-failures` Import Failures | Import → Problems | StoreManager | CONSOLIDATED | Session-only | P1 | idem |
| **`import-history` Import History** | **— (History shows Source Inbox)** | StoreManager | **ENTRY-POINT-LOST** | **No durable record of what imported after restart** | **P1** | **DB 5 files / 8,036 outcomes vs "0 problems"** |
| `documents` Document Repository | Import → Documents | StoreManager | MOVED | None | — | `documents` role 2 |
| `native-pdf`, `ocr-review`, `extraction-history`, `ocr-exceptions`, settings `ocr` | — | — | RETIRED-BY-DECISION | None | — | Plan: extraction was scaffolding |
| `register-inward/outward/credit/service/transfer/vendor` | Settings → Registers | StoreManager → **Owner** | **RESTRICTED** | Staff cannot record register entries | P2 (Q1) | role 3 ×6 |
| `register-expense` Expense Register | Today → Cash → Expense entry | StoreManager | MOVED | None — unchanged | — | `register-expense` role 2 |
| `register-courier` Courier Register | — | StoreManager | PRE-EXISTING-BROKEN | None | — | baseline `available: false` |
| `accounting-overview` Overview | — (tasks retained) | Viewer → Owner | ENTRY-POINT-LOST + RESTRICTED | Low; Phase 5 owns accounting | P3 | Settings → Accounting role 3 |
| `prepare-batch`, `ledger-mapping`, `mapping-review`, `validation`, `tally-export`, `export-history`, `accounting-reconciliation` | Settings → Accounting | Viewer → **Owner** | MOVED + RESTRICTED | Low; unfinished in both snapshots | P3 | role 3 each |
| `direct-posting`, `gst-assist` | — | Viewer | PRE-EXISTING-BROKEN | None | — | `Future()` |
| `archive-overview` Overview | — (tasks retained) | Viewer | ENTRY-POINT-LOST | Low | P3 | Reports → Archive |
| `generations`, `final-packs`, `restatements`, `compare`, `re-export`, `shared` | Reports → Archive | Viewer | MOVED | None | — | role 1 each |
| `source-documents` Source Documents | Import → Documents | StoreManager | CONSOLIDATED | None | — | `CanonicalId` alias |
| `open-items`, `data-quality` | Settings → Control centre | Viewer → **Owner** | MOVED + RESTRICTED | Staff cannot triage | P2 | role 3 |
| `missing-sources`, `unmapped`, `tender-exceptions`, `stock-exceptions`, `staff-exceptions` | Exception chips on Daily Exception Report | Viewer | CONSOLIDATED | Chip↔report row equality UNVERIFIED | P2 | Plan P2 task 9 |
| `import-conflicts` | Import → Problems | StoreManager | CONSOLIDATED | Session-only | P1 | `CanonicalId` |
| `accounting-exceptions` | Settings → Accounting | Owner | MOVED + RESTRICTED | Low | P3 | — |
| `approval-centre` Approval Centre | Settings → Control centre → Approvals | Owner | MOVED | None — Owner both | — | role 3 both |
| **`adjustment` Adjustment Request** | Settings → Control centre | StoreManager → Owner | **RESTRICTED — appropriate (D22)** | **None — correct behaviour** | — | D22 |
| **`investigation` Investigation** | Settings → Control centre | **Viewer → Owner** | **RESTRICTED** | Staff cannot look up an invoice | P2 (Q1) | role 3 |
| **`report-filters` Report Filters** | **— (dispatch orphaned)** | Viewer | **ENTRY-POINT-LOST** | **Cannot scope a report by brand segment, type or item** | **P1** | **Runtime §3 EF-04** |
| `staff-target` Staff Targets | Settings → Stores & masters | StoreManager → Owner | **RESTRICTED — appropriate (D22)** | None — correct behaviour | — | D22 |
| `settings` General | Settings → Display | Owner → **Viewer** | MOVED (widened) | None | — | `settings` role 1 |
| `users`, `stores`, `profiles`, `kpi`, `tender-rules` | Settings → Users / Stores & masters | Owner | MOVED | None | — | role 3 each |
| `masters` Master Data | Settings → Stores & masters → Brands and targets | Owner → **StoreManager** | MOVED (widened) | None — resolves Codex's local/cloud note | — | `masters` role 2 |
| `accounting-map` | Settings → Accounting → Ledger mapping | Owner | CONSOLIDATED | None | — | `CanonicalId` |
| `sharing` Email & Sharing | Settings → Integrations | Owner | MOVED | None; SMTP is Phase 5 | — | role 3 |
| `backup` Backup & Recovery | Settings → Database → Backups / Recovery drill | Owner | MOVED | None | — | role 3 |
| `scheduler` Scheduler | Settings → Integrations | Owner | MOVED | None | — | role 3 |
| `health` System Health | Settings → Database → Database health | Owner | MOVED (thinner) | See EF-01 | P1 | Runtime |
| `audit` Audit Trail | Settings → Database → Audit trail | Owner | MOVED | None | — | role 3 |
| Pinned modules | — (fixed five-section rail) | — | RETIRED-BY-DECISION | None | — | Plan P3 task 1 |
| Module/category tiles, breadcrumb, continue overlay | — | — | RETIRED-BY-DECISION | None | — | Plan P3 task 1 |

Not in `UiNavigation.cs` but added by the candidate and worth recording: `profile` (Current profile), `help:*` topics, `stock-count`, `sharing-contacts`, `accounting-approval`, `favourite-reports`. The shell gained routes as well as losing them.

## 5. Recovery proposals for confirmed essential gaps

One proposal per confirmed gap. Each states its location in the new shell and observable acceptance.

### R1 — Durable import history (EF-02). Priority P1.

**Location:** Import → **History** becomes two sub-views: *Imports* (new, default) and *Received files* (the existing Source Inbox, kept and relabelled so the distinction is explicit).

**Behaviour:** on activation, query `import_files` joined to `import_batches` and aggregated `import_row_outcomes`, filtered by the header date scope, newest first: file name, report, store, period, outcome, rows / new / present / conflicts, and safe diagnostics for the selected row. It must not depend on `latestResults`.

**Acceptance:** import the data-only fixture folder; close the application completely; reopen; Import → History → Imports lists all five files with outcomes and row counts equal to `SELECT` over `import_files` / `import_row_outcomes`; selecting a file shows its diagnostics. Re-import the same folder; the five new rows show Duplicate and the earlier rows remain visible.

### R2 — Advanced report filters (EF-04). Priority P1.

**Location:** the existing "Report range and filters" panel in Reports, exposed by a **Filters** affordance on the report header (chip row or expander), bound into the query rather than the detail view.

**Behaviour:** the four existing inputs drive `ReportingQueryScope`; an "applied scope" line renders above the grid and is written into Excel and PDF exports; clearing restores the unfiltered totals. The orphaned `report-filters` dispatch at `TaskNavigator.cs:404` is either given a task entry or deleted — it must not stay unreachable.

**Acceptance:** run Brand-wise Sales for 25 Aug unfiltered and record the total; apply a brand-segment filter; on-screen total equals the SQL total for that segment; exported Excel and PDF show the same total *and* carry the scope line; clearing returns the original total.

### R3 — Retry a failed file (EF-05). Priority P1, small.

**Location:** Import → **Problems**, a "Retry failed" button beside the status filter, enabled exactly when `CanRetry` is true.

**Behaviour:** calls the existing `RetryFailedBatchAsync()`. Ctrl+R stays as the accelerator.

**Acceptance:** import a folder containing one corrupted workbook; the file shows Failed; press Retry on the touch screen (no keyboard); only the failed file is re-processed; successful files are not duplicated; row counts are unchanged for them.

### R4 — Owner diagnostics and recovery health (EF-01). Priority P1.

**Location:** Settings → Database → **Database health**, extended — not a new page. Rename the current table "Integration health" and add a "Database and recovery" block above it.

**Behaviour:** SQL database size and maximum size, backup folder free space, last successful import, last verified backup and last verified recovery drill with their timestamps and hashes, plus the individual warning list. Sources already exist: `DatabaseOperationalHealth`, `SqlServerDashboardQuery`, and the `0023` operation receipts.

**Acceptance:** against a disposable database with one failed import and no backup receipt, the screen shows the failed import and shows backup and drill as **missing** — never as healthy. Against a database with a receipt, it shows that timestamp and fingerprint. Stale evidence must read stale.

### R5 — Staff access to registers and investigation (EF-03). Priority P2, **gated on decision Q1**.

**Location:** unchanged routes; role floor lowered from 3 to 2 for the six registers and for investigation.

**Behaviour:** Store Manager may create and edit register entries and run investigation lookups; VERIFIED status and approvals stay Owner-only; every write re-checks server-side.

**Acceptance:** as Store Manager, create a register entry and run an invoice lookup — both succeed; attempt to set VERIFIED and to approve — both refused in the UI *and* refused by SQL under `EXECUTE AS` with the permission error. As Viewer, all four refused. Do not implement before Sagar answers Q1.

## 6. Where each proposal lands

No fourth backlog. Each maps to an existing list.

| Proposal | Home | Why |
|---|---|---|
| R1 durable import history | **Phase 2 reopen list** (evening reporting and import surfaces) | It is a reporting/traceability surface over data Phase 1 already persists; no schema change. |
| R2 advanced report filters | **Phase 2 reopen list** | Directly extends the Phase 2 report work; touches export metadata, which Phase 2 owns. |
| R3 retry failed file | **Phase 3 reopen list** | A missing touch control in the Phase 3 shell; the service already exists. |
| R4 diagnostics and recovery health | **Phase 4 reopen list** | It surfaces Phase 4's backup and recovery receipts; priority is raised by the Express encryption block. |
| R5 registers and investigation access | **Phase 5** (Registers row already anticipates server-side checks) | Role policy plus server-side enforcement is Phase 5 scope, and it is gated on Q1. |

### Proposed v1.5 amendment — exact text, **not applied**

To be folded into the plan by the planner alongside D22. Add to §2 (owner decisions):

> **D23 (proposed).** The application's installed baseline for all retention comparisons is **1.8.1, commit `8e35d83`, built 27 August 2026** — the build whose SHA-256 matches `C:\Program Files\Saagar Traders\ETP Reporting Engine\Etp.Reporting.Desktop.exe`. Version 1.8.8 (`d7eb515`) was an engineering candidate that was never installed. A capability present only between `8e35d83` and `d7eb515` was never available to the shop, and its absence is recorded as *never delivered*, not as a regression.

Add to Phase 3 task 8 (feedback) — a clarification, not a new task:

> Durable state must survive a restart. Any screen that reports on work already recorded in the database — import outcomes above all — loads from the database on activation and never from in-session memory alone.

## 7. Regression strategy

Per section 5 of the audit plan, every recovered item carries four tests. No control counts, no source-text assertions.

| Test | R1 import history | R2 report filters | R3 retry | R4 health | R5 access |
|---|---|---|---|---|---|
| **Workflow** — arrange fixture, act through the service the button calls, assert persisted rows | Import fixtures; assert history rows equal `import_files` | Apply filter; assert query rows equal SQL for that scope | Corrupt one workbook; retry; assert only it re-processed | Seed a failed import and a backup receipt; assert both surfaced | Create a register entry as Manager; assert the row persists |
| **Export** — open the file, assert totals and the scope line | n/a | Excel and PDF totals equal screen; scope line present | n/a | n/a | n/a |
| **Durability** — restart between act and assert | **Required** — the defining test | n/a | n/a | Receipts survive restart | n/a |
| **Role** — UI reachability per role and `EXECUTE AS` denial in SQL | Manager sees history; Viewer read-only | All roles may filter | Manager may retry; Viewer may not | Owner-only | **Required** — Manager writes succeed, Viewer denied, VERIFIED denied to both in SQL |

## 8. Decision list

Four questions, each with the recommended default, plus one item that arises from the baseline finding.

| # | Question | Recommended default | Trade-off |
|---|---|---|---|
| **Q1** | May a Store Manager create and edit register entries and use investigation search? | **Yes**, with a server-side check; VERIFIED and approvals stay Owner | Staff record couriers and cash events without the Owner login; approval stays controlled. Gates R5. |
| **Q2** | Add a pre-commit review step before folder import writes facts? | **No** | One-action import was the stated goal; duplicate safety and per-file results already protect the data. |
| **Q3** | Keep the aggregate counters and management summary PDF? | **Drop**; keep a history tab instead | Saves Phase 5 time; the report pack and DSR already cover management output. |
| **Q4** | Add a failed-file retry button on the Problems tab? | **Yes** | Small; avoids re-importing a whole folder on a touch screen. |
| **Q5 (new)** | Record D23 — that 1.8.1 / `8e35d83` is the installed baseline and 1.8.8 was never shipped? | **Yes** | Stops future audits measuring loss against software the shop never ran. |

D22 is already decided and was not re-asked.

## 9. What this audit did not establish

Stated plainly rather than implied.

- **Chip-to-report row equality (EF-06)** — I did not export each exception chip and diff the row sets against the five retired reports. **UNVERIFIED.**
- **Inventory-group equivalence (EF-07)** — I did not run both groupings and compare totals. **UNVERIFIED.**
- **Role behaviour by live Windows login (EF-03)** — reachability is deterministic from the role table and I read it, but I did not sign in as a second Windows user. The SQL denials were exercised separately in the Phase 4 re-audit. **UNVERIFIED at the UI for a real Manager login.**
- **Nothing was run against the installed 1.8.1 binary.** Its behaviour is inferred from its source commit only; I did not launch it, because doing so would point it at the live database.
- The candidate `0a0c38f` is **on no remote**. Any acceptance based on it is acceptance of local code.
