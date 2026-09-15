# ETP Reporting Engine — Master Audit and Phased Rebuild Plan

Version 1.2 — 15 September 2026 (all owner decisions D1–D11 frozen). Author: Claude (audit and plan). Executor: Codex. Decision owner: Sagar.

This document replaces every earlier "acceptance", "sprint ledger", "handoff" and "review plan" document under `docs/` as the single working plan. Detailed evidence lives in `docs/audit/claude-audit-2026-09/01..06-*.md`.

---

## 1. Verdict in one paragraph

The application is real, not vapour: it builds, runs against SQL Express, imports four ETP workbook types, and renders 22 distinct report queries across 29 catalogue entries with Excel and PDF export. But it was built to satisfy its own documents rather than the shop. The three things the owner actually needs every evening — the DSR sheet, the closing-stock-by-brand sheet and the cash book — cannot be produced correctly today: sales are reported ex-GST while the shop reports GST-inclusive, targets and walk-ins are keyed in a way that makes MTD/YTD blank, brand rows and cash tenders do not exist, and only 4 of the 30 ETP exports can be imported. The UI hid content when the window was not exactly 1366x768, and every screen opened on a date with no data. On top of that sits a heavy process layer (128 markdown documents, generated graph files and 47 MB installers committed to git, 37 test files that assert on source text) that costs time and hides regressions. The plan below strips the process, fixes the data truth, builds the six reports the shop uses, and then makes the shell touch-first. Secondary modules (Accounting/Tally, Archive sharing, Registers, OCR, Approvals) are decided in Phase 5 — finished or hidden, never half-shown to staff.

## 2. How we work

**Roles.** Sagar decides business rules and priorities. Codex implements each phase on a branch. Claude audits the result by building, launching and screenshotting the app, running the suites and comparing report numbers with the source workbooks; a phase is closed only when every acceptance criterion is marked PASS by Claude.

**Rules for Codex (read before every phase).**
1. Work on branch `phase-N/<slug>` from `main` after Phase 0 lands. Small commits with imperative messages. No force-push.
2. Do not write new process documents. The only documents Codex maintains are `README.md`, `docs/USER-GUIDE.md` (from Phase 3), `docs/OPERATIONS.md` (from Phase 4) and one `PHASE-N-REPORT.md` per phase at `docs/audit/claude-audit-2026-09/`. Do not touch `knowledge/`, `graphify-out/`, `artifacts/`, `verification/`, `prototypes/` except to delete them when the plan says so.
3. Every behaviour change gets a behavioural test (arrange data → call service or run SQL against a temp database → assert values). Do not add tests that read source files or count lines or controls. Do not "fix" a source-text ratchet by changing the number it asserts; delete the ratchet.
4. Run the app before reporting. Every phase report contains screenshots at 1366x768 maximised and at 816x480 for every screen touched, the test summary lines, and the exact commands used.
5. Business rules come from Section 3 and from the owner's Excel sheets in `..\Reports to be generated\`. If code, docs and the sheets disagree, the sheets win; record the decision in the phase report, not in a new document.
6. Never change financial calculations without a golden test that reproduces the owner's sheet numbers for 25 August 2026 from the real workbooks in `..\ETP Source Data\`.
7. Ask instead of guessing on anything in Section 3 marked OPEN.
8. Read `docs/audit/IMPORT-FAILURE-REGISTER.md` at the start of every phase; close every OPEN row mapped to the phase or state in the phase report why it is deferred. Imports must become seamless: one folder, one button, correct numbers, no manual store or date entry.

**Phase report format** (`docs/audit/claude-audit-2026-09/PHASE-N-REPORT.md`): What changed (files) · How to verify (commands) · Test results (pasted summary lines) · Screenshots (paths) · Decisions taken · Known gaps · Acceptance checklist copied from this plan with Codex's self-assessment.

**Claude audit protocol per phase.** Build Debug and Release · run all five .NET suites · launch the app on Sagar's PC · screenshot every touched screen at 1366x768 maximised and at 816x480 · click through every acceptance criterion with UI Automation · recompute report figures from the source workbooks with Python and compare · check the security items listed for the phase · write `PHASE-N-AUDIT.md` with PASS/FAIL per criterion. FAIL items go back to Codex; the phase reopens.

## 3. Business rules and owner decisions

Verified facts (from the real workbooks, not from docs):
- In SDB-VariantwiseSales, `NETVALUE = NETAMOUNT − TAX`. NETVALUE is **ex-GST**. NETAMOUNT is the GST-inclusive invoice value. Every document in `docs/` and `knowledge/` that says "NETVALUE includes GST" is wrong.
- The owner's DSR VALUE, customer-wise list and MTD/YTD figures use the **GST-inclusive** amount (25 Aug Titan: 5 invoices in the export = ₹34,215 NETAMOUNT vs ₹28,996 NETVALUE; the sheet's 8 invoices / ₹69,880 includes 3 sales made after the 8:51 PM export).
- Stores: WLMHW = Titan World (channel WOT), HEMW = Helios. Financial year starts 1 April. Business date = invoice date.
- ETP export must be taken after the last bill of the day; otherwise every report for that day is short. This is a shop procedure, not a software fix.

Decisions (Sagar answers before the phase that needs them):

| ID | Question | Needed by | Default if no answer |
|---|---|---|---|
| D1 | **DECIDED 15 Sep 2026:** Reports use GST-inclusive NETAMOUNT as "Value" everywhere; NETVALUE and TAX kept as separate columns for GST reports. | Phase 1 | — |
| D2 | **DECIDED 15 Sep 2026:** LY = same calendar date last year (29 Feb → 28 Feb). | Phase 2 | — |
| D3 | **DECIDED 15 Sep 2026:** Store customer name and full phone number. Consequence: Phase 4 task 2 (backup folder ACLs) and task 5 (privacy scrub) are mandatory before staff use the app; customer columns are excluded from diagnostics and the support package. | Phase 1 | — |
| D4 | **DECIDED 15 Sep 2026:** Both. Codex lists every distinct BRAND/BRANDNAME/CLUSTER from the real exports with a proposed DSR row in `PHASE-2-REPORT.md` and Sagar ticks each; AND Phase 2 ships an Owner-editable **Brand rows** master in Settings (store, DSR row label, order, mapped ETP brand codes) so rows can change later without code. Rows: Titan = NEBULA, EDGE, XYLYS, AUTOMATIC, RAGA; Helios = SEIKO, CITIZEN, CERRUTI. | Phase 2 | — |
| D5 | **DECIDED 15 Sep 2026:** Monthly store target and per-CRO target entered once per month. DAY TGT = monthly ÷ days in month; MTD BLA = monthly − MTD actual; REQ ADS = BLA ÷ days remaining including today. Codex shows the calculation in the DSR help. | Phase 2 | — |
| D6 | **DECIDED 15 Sep 2026:** Keep and finish all four: Accounting/Tally export, report sharing by WhatsApp and email (contacts + delivery history), Digital Registers (all seven types), Approvals workflow (adjustments and restatements). OCR/Source Inbox scaffolding is still deleted. See the revised Phase 5 table. | Phase 5 | — |
| D7 | **DECIDED 15 Sep 2026:** Owner plus staff on one shared Store Manager Windows login on the shop PC; Owner uses their own login for admin. Audit shows the shared account. | Phase 3/4 | — |
| D8 | **DECIDED 15 Sep 2026:** Yes, Sagar will export 2025-26 for both stores. Phase 2 adds a one-time historical import (multi-month files, same profiles) so LY, growth and LY YTD fill in. Sagar exports the files before Phase 2 starts. | Phase 2 | — |
| D9 | **DECIDED 15 Sep 2026:** SQL backup encryption with a certificate. Phase 4 must: create the certificate, export it with a password to two off-PC locations chosen by Sagar, verify a restore on a second machine from the exported certificate, and document the custody steps in `docs/OPERATIONS.md`. The recovery drill fails loudly if the certificate backup is missing. | Phase 4 | — |
| D10 | **DECIDED 15 Sep 2026:** Staff key Display / Backstock / Defective / Y Loc per brand each evening in the app, yesterday's values pre-filled; System from ETP Closing Stock; Difference computed. | Phase 2 | — |
| D11 | **DECIDED 15 Sep 2026:** Cash-book modes are Cash, Card, UPI, CN, TC, Service Cash, Service Card, Service UPI. Codex lists every PAYMENTTYPE column with its AGENCYNAME from the Payment Type Report in `PHASE-1-REPORT.md`; Sagar ticks the mode for each before Phase 1 closes (PAYMENTTYPE25 = Airpay → UPI, PAYMENTTYPE20 = PhonePe → UPI). | Phase 1 | — |

## 4. Audit summary by module

Severity: C critical (wrong money or data loss), H high (feature unusable), M medium, L low. Full detail with file:line in the numbered audit files.

### 4.1 Reports engine (audit 01)
- C — All sales values ex-GST; NETAMOUNT dropped at import (`R025SqlImportOrchestrator.cs:54`).
- C — DSR "Service WDC" reads a field code that does not exist in `manual_input_definitions` (`OperationalReportRepository.cs:673`).
- H — Monthly target must be re-keyed per business date; staff targets need an exact period match.
- H — MTD/YTD walk-ins and service go blank unless every single day is keyed; YTD impossible with July-start data.
- H — Excel "Executive Summary" KPIs sum FTD+MTD+YTD across stores and print percentages ×100; every Excel export goes through this path.
- H — Pack/tabular PDFs clip numbers to ~10 characters and print ₹ and dashes as `?`.
- H — No DSR brand rows, no STORE/DAY target, BLA, REQ ADS; closing stock grouped by cluster not brand; cash report only sees the CASH tender and never carries forward the previous closing balance; customer-wise list impossible (names not stored).
- H — No index on `sales_invoices.transaction_date`; per-line correlated snapshot lookups.
- 7 alias catalogue entries (29 shown, 22 real). Dead `InitialReportCatalogue` with tests that only test the dead code.

### 4.2 Dashboard and Daily Workflow (audit 02)
- H — Four dashboard cards (health warnings, backup space, failed imports, latest import) are built but never attached; backup-critical warnings are invisible.
- H — "% imported" divides completed batches by files; a 6-file batch shows 17% forever.
- H — Reopen day demands a Windows-elevated token on top of the Owner role and then tells the Owner they lack permission.
- M — Finalise writes a generation row before validating; reads insert `daily_reporting_days` rows; pack generation allowed to Viewers; staff targets are write-only (no list); reasons validated only in the repository so raw `ArgumentException` text reaches the status bar.
- Startup `MainWindow_Loaded` is `async void` and only catches database-availability errors.

### 4.3 Imports, Source Inbox, Registers (audit 03)
- C — `PAYMENTTYPE25` in the Revenue Report is **Airpay UPI**: ₹12.71 lakh of ₹21.43 lakh (59%) of Titan tender value on 258 of 404 invoices, hard-quarantined from every tender report and reconciliation (`R022PersistenceProjection.cs:59-61`, `0014:432`). The Payment Type Report's AGENCYNAME column resolves the mode.
- C — R025 line identity is the workbook **row number**; any re-export with shifted rows duplicates facts and silently drops "conflict" rows while the batch still reports Completed (`R025SqlImportOrchestrator.cs:52`, `0014:385-404`).
- C — Nothing stops two different files being current for the same report/store/date; facts double and restatement then throws (`OperationalCompletionRepository.cs:51-77`).
- H — Business scope is one date (`MAX(transaction_date)`); the real exports span 55 days and the user must type exactly that max date or the import is rejected. Batch import applies one store and one date to every file.
- H — R013 (CRO Wise Sales) SR rows carry positive NETVALUE with negative QTY; the staff report sums them, overstating staff sales by returns; CRO NAME is discarded. R003/R013 have no row dedupe.
- H — AdvanceOrder Collection has the identical header as Revenue Report and would register as an empty R022 import.
- H — "OCR" and "native PDF extraction" do not exist: a regex over raw PDF bytes and an external PaddleOCR exe at an Owner-editable path that is not in the repo.
- M — Only 6 of 30 ETP workbook types importable. **Closing Stock with Display/Backstock/Defective does not exist in any ETP export** (Closing Stock has no location column; ledger LOCATION is RETAILBIN on all rows) — location split must be a manual entry. Service sales has no ETP export. No staff master table.
- M — Registers: real CRUD but Courier type missing from UI, every save forced to DRAFT, no server-side access check.
- Zero tests touch SQL Server or open a real workbook.

### 4.4 Security (audit 04)
Verdict: parameterised SQL everywhere, allow-listed audit vocabulary, fail-closed migrations, checksum-verified backups. Material gaps:
- H — Day lock state is protected only in C#: a Store Manager can `UPDATE daily_reporting_days SET status='OPEN'` in SSMS with their own login; no trigger. The "must be Windows-elevated" reopen check is client-side theatre and pushes the Owner to run the whole app as administrator.
- H — Owner-editable `ocr_helper_path` is executed by the automation task as SYSTEM: arbitrary code as SYSTEM for any db_owner. (Verified on this PC: SYSTEM is **not** SQL sysadmin, so the DENY model holds; Sagar's login is sysadmin.)
- H — **Verified on this PC:** `C:\ProgramData\EtpReporting` and `\Backups` grant BUILTIN\Users Read and Write. Unencrypted full backups of all sales and customer data are readable by any local account; anyone can plant a `.bak` that the recovery drill will restore.
- M — Store Manager has `db_datawriter` on every table, broader than the app's rules (supersede imports, edit facts on unlocked days, mark documents verified, write audit rows with any actor name). Owner can rewrite the audit table.
- M — **Verified on this PC:** only the monthly recovery-drill task is installed, running as Sagar with a limited token; the daily backup and ETP automation tasks are not installed at all. Last backup age is therefore whatever was run manually.
- M — Nothing is Authenticode-signed; PowerShell runs with `-ExecutionPolicy Bypass`; installer allows a user-writable install directory that SYSTEM tasks then execute from.
- L — Connection string accepts remote hosts and `Encrypt=False`; `powershell.exe` resolved by search path; ZIP bomb guard trusts declared sizes; CI "23 security tests" are Android JS tests.
- Licensing: nothing implemented; the deferred design would lock the shop out on a motherboard swap unless a grace mode is added first.
### 4.5 Accounting, Archive, Operations, Settings, Help, navigation (audit 05)
- C — Operations Center refresh will throw a CHECK-constraint violation for every Owner and Store Manager as soon as one failed import or one restatement exists: the data-quality sync writes severities `FAIL`/`INFORMATION` into a column that only allows `INFO`/`WARNING`/`CRITICAL` (`ProductisationRepository.cs:417-420`, `0014:189`).
- C — Approving any adjustment permanently blocks Accounting for that day: the batch source emits an `ADJUSTMENT` event but the mapping screen can only approve three other event types.
- H — No last-Owner guard: an Owner can demote or deactivate themselves and lock everyone out. `--initialize-database` and `--automation-once` ignore `settings.json` and always target `.\SQLEXPRESS\EtpReporting`. Task screens are assembled by positional child indexes into the XAML (`TaskNavigator.ResolveTaskView:422-481`); one inserted control shifts every index and breaks the workspace for the session.
- Aliases and stubs: Mapping Review, Validation, Export History and Accounting Reconciliation are the same two screens under four names; Shared Reports has no history (`share_attempts` is write-only); WhatsApp sharing cannot attach a file; SMTP settings are stored but nothing ever sends mail; Stores, Master Data and Tender Rules are write-only tables with no consumer and every store-dependent feature hard-codes WLMHW/HEMW; Approval Centre only ever contains adjustments; System Health under Settings is a subset of the dashboard; there is no background watch-folder poller; backup, recovery drill and support package work only in installer-packaged builds and the drill needs sysadmin rights. Batch approval reason is validated then discarded. Help content is real but promises features that do not exist.
- Viewer-visible items route to Import screens that deny Viewers ("Access restricted" drawer).
- Tests: no test opens a SQL connection; all T-SQL in the three big repositories is untested. 39 of 55 desktop test files contain source-text assertions.
### 4.6 UI/UX (audit 06)
Fixed on 14 Sep already: window off-screen at 1366x768, dashboard hiding cards when short, date defaulting to yesterday, "1 tasks". Remaining:
- C — Shell chrome uses 231 of 728 px before any content: 135 px header (search box row plus a second row of 48 px breadcrumb buttons, store and date), 65 px footer, 31 px caption. Content area is 497 px at full screen.
- C — The DSR, the one screen staff open daily, scrolls at full screen in the default density: store tabs need ~350 px and get ~279. Other reports show three detail rows.
- H — Every status/result message is forced to one ellipsis-trimmed line with no tooltip (`TaskBodyLayout.cs:20-21`); import results and report status are unreadable.
- H — Three levels of identical text tiles before any work: Home → module → category → task. Importing today's files is 12 taps; the DSR is 4; walk-ins entry is 7.
- H — First screen after a pointless "Continue" overlay is an IT dashboard (file counts, SQL health), not today's sales.
- H — Raw .NET text reaches staff: "A value is required. (Parameter 'reason')".
- H — DSR paints negative growth green (`DailySalesReportControls.cs:48,60`).
- H — About 40% of the shell is dead: the whole legacy panel stack, the context sidebar (populated then hidden on every route), four dashboard card builders, six overlapping navigation registries, "Stock Reports"/"Masters" routes.
- Touch: ComboBox items, radio buttons and scrollbars are default size; no `InputScope=Number` so the touch keyboard opens QWERTY for numbers; rail icons have no labels and no selected state; explanations live in tooltips on disabled controls (unreachable).
- Culture: no UI culture set, so date pickers show US format while labels show "13 Sep 2026"; grids show PascalCase headers, four-decimal numbers and 00:00:00 times; three rupee formats on one screen.
- Header date and store copy to task screens only until first visit; changing the header afterwards does nothing, so staff will save walk-ins against the wrong day. Five vocabularies for two shops (WLMHW, Titan, Titan World; HEMW, Helios; All/Combined/COMBINED/Both).
- No confirmation before Finalise, Reopen, Waive, Reject, Cancel batch, Run backup. Exports and finalise give no progress; the whole view is disabled instead.
- No high-contrast support; no per-monitor DPI manifest (125% scaling makes the 1366 panel behave like 1093x582).

### 4.7 Repository and process
- 1,993 tracked files; 973 are generated `graphify-out/`, 89 `artifacts/` (three 47 MB installers and a 41 MB evidence zip force-added despite `.gitignore`), 143 legacy Android `www/`, 92 legacy JS tests still run in CI. `.git` is 200 MB. Remote ref `origin/ui/uiux-v4-touch-first-redesign` is corrupt.
- 128 markdown documents (~100,000 words) describing acceptance that never happened on a running UI.
- 37 of 107 .NET test files assert on source text (line counts, `x:Name` counts, regexes) instead of behaviour; they block refactoring and catch nothing.
- Uncommitted fixes from 14 Sep are in the working tree (see `CLAUDE-AUDIT-PROGRESS-2026-09-14.md`).

## 5. Phases

Effort is Codex working time. Each phase ends with Claude's audit; the next phase starts only after PASS.

### Phase 0 — Stabilise and clear the ground (1–2 days)
Goal: a clean, fast repository where the only tests are ones that fail for real reasons, and the 14 Sep fixes are committed.

Tasks:
1. Commit the 14 Sep working-tree fixes as one commit: maximised window, latest-data business date default, dashboard reflow, "1 task" wording.
2. Remove from git (keep on disk if wanted): `graphify-out/`, `artifacts/`, `www/`, `prototypes/`, `verification/`, `tests/*.mjs`, `package.json`/`package-lock.json`/`node_modules`, `build-overrides/`, `capacitor.config.json`, `.graphify*`, `.code-review-graph*`, `EXPORT-*.txt`. Add them to `.gitignore`. Rewrite history is NOT required; a single removal commit is enough. Fix the corrupt remote ref (`git remote prune origin` / re-fetch).
3. Delete every source-text ratchet test (the 37 files or the individual `[Fact]`s that use `File.ReadAllText`/`ReadLines` on `src`). Keep behavioural tests. Delete `ReportCatalogueTests` sections that test `InitialReportCatalogue`, and delete `InitialReportCatalogue`, `IReportExecutor`, `ReportSourceRegistry` and the unused `VisualReportRegistry` ID scheme.
4. Move `docs/` to `docs/_archive-2026-09/` except: `14_WINDOWS_QUICK_START.md` (renamed `INSTALL.md`), `03_DATABASE_SCHEMA.md`, `04_ETP_IMPORT_PROFILES.md`, `05_MAPPING_REGISTER.md` (with the GST line corrected), this plan and the `claude-audit-2026-09/` folder. Delete `knowledge/`, `AGENTS.md` graphify instructions, `.codex/hooks.json` graph hooks. Replace `README.md` with a one-page description of the Windows app only.
5. CI (`.github/workflows/ci.yml`): remove Node/npm steps and the JS "security" tests; keep restore, build Release, `dotnet test`, PowerShell parse check, dependency scan.
6. Add SQL indexes via migration `0016_reporting_indexes.sql`: `IX_sales_invoices_date (transaction_date, store_code) INCLUDE (document_number, sales_invoice_id)` and `IX_stock_snapshots_product (store_code, product_code, snapshot_date DESC)`.
7. Fix startup robustness: `MainWindow_Loaded` catches all exceptions and shows a friendly "Cannot reach SQL Server: …" panel with a Retry button instead of crashing; default connection string gets `Connect Timeout=5`; `DesktopFriendlyError` distinguishes login failure, server unreachable and permission denied instead of one generic sentence.
8. Fix the two first-week crashes from audit 05: map data-quality severities to the CHECK-allowed set (`FAIL→CRITICAL`, `INFORMATION→INFO`) in `ProductisationRepository.SyncDataQualityIssuesAsync`; make `--initialize-database` / `--automation-once` read `settings.json` like the interactive app.
9. Add a last-Owner guard: SQL trigger on `application_users` that refuses to demote or deactivate the final active OWNER.
10. Ship `scripts/` with every build (csproj Content include) so Backup / Support package / Recovery drill work outside the installer, or hide those buttons with an explanation when the folder is absent.
11. Add a SQL integration test project that runs against a throwaway database on `.\SQLEXPRESS` (created and dropped per run): apply all migrations, then exercise every stored procedure and trigger touched in Phases 0–2. This is the foundation every later phase builds on.

Acceptance (Claude verifies):
- A0.1 `git ls-files | wc -l` < 600 and no file in git larger than 2 MB (`git ls-files -z | xargs -0 du -b`).
- A0.2 `dotnet test Etp.Reporting.slnx -c Release` passes with zero tests referencing `File.ReadAllText`/`ReadLines` of `src` paths.
- A0.3 CI workflow has no `npm`/`node` steps.
- A0.4 App launches maximised with title bar visible on 1366x768; with SQL Server service stopped the app shows the friendly panel and does not crash; with it running, DSR opens on 25 Aug 2026 with data.
- A0.5 DSR query plan uses the new index (Claude checks `SET STATISTICS IO` or the actual plan on `EtpReporting`).
- A0.6 With one failed import batch in the database, the Operations Center opens for the Owner without error.
- A0.7 `Etp.Reporting.Desktop.exe --initialize-database` against a `settings.json` pointing at a renamed database bootstraps that database, not `EtpReporting`.
- A0.8 Attempting to set the only Owner to Viewer is refused with a clear message.
- A0.9 The SQL integration test project runs green locally and in CI (CI uses the `mcr.microsoft.com/mssql/server` service container or LocalDB on `windows-latest`).

### Phase 1 — Data truth: import everything the reports need (1 week)
Goal: one click imports a day's ETP export folder for both stores, correctly, with the columns the six reports need.

Tasks:
1. **Value columns.** Persist NETAMOUNT (GST-inclusive), NETVALUE (ex-GST) and TAX as three columns on `sales_lines` (migration `0017_sales_value_columns.sql` adding `source_gross_amount`/`source_tax_amount` and back-filling from lineage where possible; for existing rows, re-import is acceptable). Reporting policy selects NETAMOUNT for "Value" per D1. Correct `docs/05_MAPPING_REGISTER.md`.
2. **Auto-detect store and business date.** From `STORE CODE` and the max `INVDATE`/`INVOICEDATE`/snapshot date in the workbook; the Import screen shows the detected values read-only with an "override" toggle for restatements. Remove the requirement to pick the store first.
3. **Folder import.** "Import today's folder" picks the two store folders (or the parent), imports all recognised workbook types in dependency order, skips duplicates (SHA-256) with a per-file result list, and never fails the batch because one unknown workbook type is present.
4. **New import profiles** (exact header signature like the existing ones; headers to be captured from the real files in `..\ETP Source Data`): CRO Wise Sales (staff), Payment Type Report and Daywise Collection (tenders by mode per invoice/day), Banking Details/Summary (bank deposits), CN Register (credit notes), SDB Document Wise (customer-wise invoice header incl. customer name per D3), Closing Stock with location columns (Display/Backstock/Defective/Y Loc if present in the export — Codex verifies the header; if absent, location counts stay manual). Each profile: staging → typed table → lineage, with a golden test loading the real 25 Aug workbook and asserting row counts and totals computed independently in the test.
5. **Manual input definitions.** Seed `SERVICE_WDC`, `WCC_WALKIN`, `WCC_SALES`, `WDC_BILLS`, and monthly `SALES_TARGET` keyed by (store, month) in a new `monthly_targets` table; staff monthly targets in `staff_sales_targets` keyed by (store, cro, month) with overlap lookup.
6. **Completeness rule.** MTD/YTD manual-input aggregates return the sum of available days plus a "missing days" count; never null the whole period.
7. **Tender modes.** Map every `PAYMENTTYPEnn` column to its real mode using the Payment Type Report AGENCYNAME (PAYMENTTYPE25 = Airpay UPI, PAYMENTTYPE20 = PhonePe, …) in a `tender_modes` master table editable in Settings; stop quarantining PAYMENTTYPE25. Tender reconciliation compares GST-inclusive invoice value with tender totals.
8. **Line identity.** Replace the row-number identity for R025/R013/R003 with a content key (store, invoice year, invoice number, item number, transaction type, quantity, net amount, sequence within invoice). Re-importing a re-exported workbook with shifted rows must produce zero new facts. Conflict rows fail the file, never commit silently.
9. **One current file per report/store/period.** Importing a second different file for an already-imported report and period is rejected with "already imported on … (hash …); use Restate" — unless it is a superset (later export of the same period), in which case it supersedes the earlier file automatically with a restatement record.
10. **Multi-day scope.** A workbook's scope is its full date range (min..max), not one date. Locking, evidence and restatement key on the range; single-day imports are the special case.
11. **R013 signs.** Normalise SR rows to negative net value at staging; persist CRO NAME; add a staff master (`staff` table: code, name, store, active) seeded from the first import and editable in Settings.
12. **Ambiguous headers.** Distinguish AdvanceOrder Collection from Revenue Report by sheet name/file name, not header signature alone; unknown-but-harmless workbook types are reported as "Not needed" instead of failing the batch.
13. **Location split for closing stock** is manual: a touch-friendly per-brand entry (Display / Backstock / Defective / Y Loc) with yesterday's numbers pre-filled; System comes from Closing Stock.
14. Delete the OCR/PDF-extraction scaffolding and the Owner-editable helper path (audit 03 §D, audit 04 §1.8). Source Inbox keeps only "attach a scanned document to a day" with hash and viewer.
15. ZIP extraction counts actual inflated bytes; Excel lock files (`~$*.xlsx`) are skipped; temp folders are deleted after each batch.

Acceptance:
- A1.1 Importing the two real folders for 25 Aug 2026 on an empty database completes in one action; result list shows every file as Imported / Duplicate / Unsupported with counts.
- A1.2 SQL check: Titan 25 Aug lines sum NETAMOUNT = 34,215.00 and NETVALUE = 28,995.76; Helios 29,290.00 / 24,822.02; August MTD Titan NETAMOUNT = 938,197 and invoices = 182; Helios 774,869 / 38 (values recomputed by Claude from the workbooks).
- A1.3 Re-importing the same folder writes zero new rows.
- A1.4 CRO, tender-by-mode, banking, CN and customer header tables populated for 25 Aug with counts matching the workbooks.
- A1.5 Golden tests for every profile run in CI without SQL Server (parser level) and the SQL tests run against LocalDB/SQLEXPRESS on Claude's audit.
- A1.6 Tender report for Titan August shows UPI (Airpay + PhonePe + BHIM) as the largest mode and the sum of all modes equals the GST-inclusive invoice total to the paisa for every day; nothing sits in "quarantined".
- A1.7 Importing the Titan SDB workbook, then a copy with three rows moved to the top, adds zero facts and reports "Duplicate content".
- A1.8 Importing the July–August export once as a 55-day file works without the user typing any date; the Daily Workflow shows each day inside the range as "sources present".
- A1.9 Staff report for 25 Aug shows returns as negative and CRO names, and the staff total equals the store total.
- A1.10 `docs/audit/IMPORT-FAILURE-REGISTER.md`: every row mapped to Phase 1 is FIXED, and importing the complete two-year Helios consolidated folder (`ETP Source Data\HEMW	ill 6 sep 26`, Info sheets included, no manual store or date entry) plus the Titan folder produces zero new OPEN rows. Monthly totals match `ETP Source Data\HEMW\golden-monthly-HEMW-R025.csv`.

### Phase 2 — The six evening reports, matched to the owner's sheets (1–2 weeks)
Goal: after the import, the owner opens "Today" and sees the same numbers as the Excel sheets, and can export/share each as PDF and Excel.

Tasks (each report gets its own screen, Excel export, PDF export and a golden test against 25 Aug 2026):
1. **DSR** — per store block with STORE TGT, DAY TGT, MTD BLA, REQ ADS; VOL/VALUE/AUPT/AVPT × FTD, LY, GROWTH%, MTD, YTD, LY YTD; brand rows per D4; RETAIL WALKIN, INVOICE, CONVERSION%; WCC WALKIN/SALES, WDC BILLS; combined WOT+HELIOS block. INVOICE count = INV documents only (SR excluded from the denominator). LY per D2; "—" when no LY data.
2. **Closing stock by brand** — Display, Backstock, Defective, Y Loc, Physical (= sum of the four), System, Difference, Remark; grouped by BRAND (not cluster); physical entry screen keyed by brand with the previous day's counts pre-filled; totals row.
3. **Cash book** — Dr/Cr layout per store per day: opening balance carried from the previous day's closing (editable with reason), expenses, bank cash deposit, per tender mode from Payment Type/Daywise Collection (Cash, Card, UPI, CN, TC), service Cash/Card/UPI, total sale, closing balance; multi-day view for a month.
4. **Service sale report** — WDC, Cash, Card, UPI, Total for the day; MTD, LY MTD, TY YTD, LY YTD.
5. **Customer-wise invoices** — per store per day: customer name, invoice qty, net value (GST-inclusive), grand total; from SDB Document Wise / R022.
6. **CRO-wise sales** — target, net sales, net qty, discount, ATV, UPT, transaction count, with monthly targets from Phase 1.
7. **Exports** — replace the fixed-width tabular PDF with measured column widths and an embedded Unicode font (₹, —); Excel gets real number cells with Indian grouping (`[>=10000000]##\,##\,##\,##0;[>=100000]##\,##\,##0;##,##0`), dates as dates, freeze panes, autofilter; remove the "Executive Summary" KPI sheet or compute it correctly (no summing percentages). DSR PDF stays single A4 landscape but sizes tiles to content.
8. Fix C9–C16 from audit 01 (SR in counts, target halving, `TyInvoices ?? 0`, tender-variance sign, cash blocking on optional inputs, service SumIfAny).
9. Remove alias catalogue entries (`stock-group`, the five `exception-*` filters become filter chips on one Exceptions screen).
10. **Brand rows master** (D4): Owner-editable screen in Settings mapping ETP brand codes to DSR rows per store; the DSR reads from it.
11. **Historical import** (D8): import the 2025-26 exports for both stores through the same profiles (multi-month scope from Phase 1); LY, growth and LY YTD then populate. Acceptance A2.6: after loading the 2025-26 files, the DSR for 25 Aug 2026 shows LY and GROWTH% values, and LY YTD for Titan equals the sum Claude computes from the 2025-26 workbook for 1 Apr–25 Aug 2025.

Acceptance:
- A2.1 For 25 Aug 2026 the DSR screen and PDF show, for the invoices present in the export: Titan VOL 5, VALUE ₹34,215, AUPT 1.00, AVPT ₹6,843; Helios VOL 2, VALUE ₹29,290; MTD Titan ₹9,38,197 / 182 invoices, Helios ₹7,74,869 / 38; brand rows sum to the store value; combined block = sum of stores. Claude recomputes each from the workbooks.
- A2.2 Closing stock by brand for 25 Aug matches the System column of the owner's sheet brand by brand (Titan total 812, Helios 501), with Physical entry working by touch.
- A2.3 Cash book for 25 Aug reproduces the owner's sheet structure with tender totals equal to the Revenue Report per-mode sums Claude computes.
- A2.4 Every export opens in Excel/Acrobat with no `?` characters, no clipped numbers, Indian grouping, and percentages shown once.
- A2.5 Each report has a golden test against the real workbooks; `dotnet test` green.

### Phase 3 — Touch-first shell, two levels deep (1 week)
Goal: shop staff reach any daily task in two taps; nothing scrolls at 1366x768 full-screen; nothing disappears when the window shrinks.

Tasks (from audit 06; §10 of that file has the full screen mapping):
1. **New shell.** Five large labelled buttons in the left rail, always visible, with a selected state and an attention badge: **Today · Import · Reports · Stock · Settings**. Level 2 is a row of tabs inside each section. No module tiles, no category tiles, no overview screens, no context sidebar, no welcome overlay (auto-continue when a role resolves; keep it only for "database setup required"). Delete `ShowTaskOverview`, `ShowReportCategories`, `LegacyWorkspaceScroll` and its twelve panels, `ContextSidebar`, `BreadcrumbText`, `AccessStatus`, `ModuleTile`/`StatusBadge`/`DetailDrawer`, `WorkspaceModuleOwnershipRegistry`, `WorkspaceLocation`/`WorkspaceNavigationHistory`, `ShellPresentationMetadata`, the "Stock Reports" and "Masters" routes, and the six navigation registries in favour of one `TaskNavigation` table plus a small route map.
2. **Chrome budget.** One 48 px header row (section title · business date · store) and a 28 px footer. Search moves behind a rail icon. Content area at 1366x728 must be at least 610 px.
3. **Today section** (landing screen): tabs Sales (DSR for the header date, auto-run, Export PDF and Share as visible primary buttons), Cash (cash book from Phase 2), Walk-ins (walk-ins field pre-selected, reason defaulted), Close day (readiness, Finalise, Generate pack). The DSR fits without scrolling: KPI strip, both store blocks side by side, service/targets column.
4. **Import section**: one flow — store and date detected from files, "Import today's folder" primary button, per-file result list, progress bar with Cancel. Problems tab merges quarantine/duplicates/conflicts/failures into one grid with a status filter.
5. **Reports and Stock sections** as mapped in audit 06 §10; every report has Export PDF / Export Excel as buttons, not a menu; detail grids get explicit columns with `dd MMM yyyy` dates, ₹ with lakh grouping, right-aligned two-decimal numbers, plain-English headers.
6. **One scope.** Header date and store are bound two-way to every task; no per-task date pickers except on multi-day reports (from/to). One vocabulary: "Titan World", "Helios", "Both stores".
7. **Touch.** 44 px minimum for ComboBox items, radio buttons, tab headers, DataGrid rows, calendar cells; 24 px scrollbars; `InputScope=Number` on every numeric box; `ToolTipService.ShowOnDisabled` replaced by inline help text; no hover-only information; density setting becomes Touch (44) / Desktop (36) with Touch as the default on the shop PC.
8. **Feedback.** Status and result messages wrap (max 3 lines, "More" opens details); required fields validated in the presentation session before any database call with a visible "*"; `DesktopFriendlyError` never shows "(Parameter …)" or RAISERROR text; success toast; progress bar plus Cancel for import, export, pack generation and finalise; confirmation sheet before Finalise, Reopen, Waive, Reject, Cancel batch, Run backup/drill/automation.
9. **Culture and consistency.** `en-IN` set at startup for UI culture and number parsing; `dd MMM yyyy` everywhere; one style system (`Themes/`) — delete `DsrUi` literals, `DashboardView` brushes, `ReportWorkspaceControls` literals, the duplicate `Card`/`NavigationButton` in `MainWindow.xaml`; sentence-case buttons; one spelling ("Centre"); copy rewritten for staff (≤ 12 words, no "governed/canonical/lineage/immutable"). Growth colours by sign.
10. **DPI and accessibility.** PerMonitorV2 manifest; one breakpoint (compact below 1000 DIP); `HeadingLevel` on page and card titles; F6 covers rail and footer; high-contrast fallback on the three custom templates.
11. Delete the dead dashboard card builders and the "Today overview" IT dashboard; its SQL/backup facts become a one-line health strip under Settings → Database and a badge on Today only when critical.

Acceptance: A3.1 Launch → DSR in 0 taps (it is the landing screen), → import today's folder in 2 taps, → walk-ins entry in 2 taps. A3.2 Screenshots at 1366x768 in Touch density show no scrollbars on Today/Sales, Today/Walk-ins, Import, Reports list, Stock/Closing stock, Settings. A3.3 At 816x480 and at 125% DPI every control remains reachable (scroll appears, nothing hidden). A3.4 UI Automation dump of every screen shows no interactive control under 44 px tall. A3.5 Changing the header date then opening Walk-ins shows that date. A3.6 Saving walk-ins without a value shows "Enter the walk-in count" inline, never a parameter name. A3.7 `grep -r "governed\|canonical\|lineage\|immutable" src/Etp.Reporting.Desktop --include=*.xaml --include=*.cs` finds no user-facing string. A3.8 A shop-staff walkthrough (open app → import today's folder → enter walk-ins and cash → view DSR → export PDF) completes with touch only, timed under 3 minutes.

### Phase 4 — Security and operations hardening (3–5 days)
Tasks (from audit 04, in priority order):
1. **Day lock in SQL.** `INSTEAD OF UPDATE` trigger (or a stored procedure that is the only granted path) on `daily_reporting_days`: LOCKED→OPEN only for members of an `etp_owner` database role, with a non-empty reason written to `daily_reporting_events` in the same statement. Delete the client `administratorApproved` flag and the elevated-token check (`DailyWorkflowWorkspaceView.xaml.cs:412-416`).
2. **Folder ACLs and backups.** Bootstrap sets `C:\ProgramData\EtpReporting\{Backups,Documents,Share}` to SYSTEM, Administrators and the SQL service only (`icacls /inheritance:r`); backup rotation (keep 14 daily + 12 monthly); `BACKUP … WITH ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = EtpBackupCert)` per D9: a Settings → Database action creates the certificate and exports it plus its private key with a password to a location Sagar chooses (off the PC: USB or cloud folder), records the export hash, and the recovery drill fails loudly if no exported certificate is on record; the restore procedure is verified on a second machine and written in `docs/OPERATIONS.md`. Fix the ACL on the existing PC as part of the phase.
3. **Scheduled tasks.** Install the daily backup and ETP automation tasks (they are missing on the live PC); register all three with the same principal; run automation under a dedicated least-privilege local account with the STORE_MANAGER SQL role, not SYSTEM. Recovery drill restores the file named in the last verified receipt (hash-checked), compares against the backup's own metadata, and reports into System status.
4. **Store Manager grants.** Replace `db_datawriter` with explicit per-table grants matching the app's rules; INSERT on `operational_audit` only via a stored procedure that takes actor from `SUSER_SNAME()`; audit table append-only for everyone (INSTEAD OF UPDATE/DELETE trigger); maintenance script archives instead of deleting.
5. **Connection string.** Allow only local instance forms (`.\SQLEXPRESS`, `(local)`, `localhost`, `lpc:`, `np:`); reject `Encrypt=False`, `AttachDbFilename`; drop `TrustServerCertificate=True` default in favour of `Encrypt=Optional` for local; absolute `powershell.exe` path; `-x` on every sqlcmd call.
6. **Signing and install directory.** Authenticode-sign exe, scripts and installer (Sagar buys a code-signing certificate, or the plan documents the SmartScreen "More info → Run anyway" path); switch to `-ExecutionPolicy AllSigned`; remove `PrivilegesRequiredOverridesAllowed`; task installers refuse a non-admin-writable install folder.
7. Normalise line endings before hashing migrations; write the Owner break-glass procedure (`docs/OPERATIONS.md`); pin GitHub Actions to SHAs; CI security step runs the .NET boundary tests and names them honestly.

Acceptance: A4.1 As a Viewer login in SSMS, `UPDATE daily_reporting_days SET status='OPEN'` and `DELETE FROM operational_audit` both fail; as Store Manager, updating `import_files.is_superseded` and `sales_lines` fails. A4.2 `icacls` on the three ProgramData folders shows no BUILTIN\Users entry. A4.3 `Get-ScheduledTask` shows three ETP tasks with the same non-SYSTEM service principal, and a daily backup has run within 24 h. A4.4 Backup → drill restore → verify passes on the audit PC from the receipt-named file. A4.5 Support package and diagnostics for a session that imported the real files contain no customer name, phone, file path or SQL text. A4.6 Connection settings reject `Server=remotehost` and `Encrypt=False`.

### Phase 5 — Secondary modules: finish, hide or delete (per D6; 3–10 days)
Disposition per D6 (decided 15 Sep 2026): all four business modules are kept and finished; OCR scaffolding is deleted.

| Module | Disposition | Work |
|---|---|---|
| Accounting / Tally export | **Keep and finish, Owner-only** (D6) | Fix the ADJUSTMENT mapping gap; persist the approval reason; make mapping approval one transaction; add Reject; delete the four alias tasks (Mapping Review, Validation, Export History, Reconciliation) and show one screen: Prepare → Review → Export, with a real export history (batch, time, hash, file). |
| Archive and sharing | **Keep and finish** (D6) | One screen: list of generated packs with Open / Excel / PDF / ZIP / Share on every row. Sharing: contacts list (name, WhatsApp number, email), WhatsApp Desktop launch with the PDF path copied, real SMTP sending (MailKit) using the stored settings with a test-send button, and a delivery history per pack (`share_attempts` gets a reader and a final outcome). Delete Compare and the Restatements filter. |
| Registers | **Keep and finish** (D6) | Fix Courier type, allow DRAFT → VERIFIED with reason, server-side access check, register entries visible from the day's Close-day tab; seven types. |
| Source Inbox / OCR / native PDF | **Delete** | Remove `DocumentIntakeService`, PaddleOCR helper path, `document_extractions`, OCR settings and Help topic. Keep "attach a scanned document to a business day" only if D6 asks. |
| Approvals / Adjustments | **Keep and finish** (D6) | Store Manager raises adjustment and restatement requests; Owner approves from one queue that also shows decided history; wire the RESTATEMENT producer (import restatement waits for approval); delete the unused REOPEN_DAY / MASTER_MAPPING / CONTROL_WAIVER types. |
| Stores / Master data / Tender rules | **Replace** | Stores table becomes the single source for every store list (delete all hard-coded WLMHW/HEMW); tender modes master from Phase 1 replaces "Tender rules"; delete `controlled_master_values` and its screens. |
| Scheduler / Watch folder | **Keep, honest** | Rename to "Automatic import"; show the installed task status and last run; remove `poll_minutes`; fix the app-lock release; a full ETP ZIP with unsupported workbook types must succeed with "not needed" entries. |
| Help centre | **Rewrite to match** | One topic per top-level tab of the Phase 3 shell, screenshots included; delete Coming Soon states. |
| Investigation search | **Keep** | Add click-through from a result row to the report or import it names. |

Acceptance: A5.1 Every navigation destination reachable by a role leads to a working screen for that role (Claude walks all of them as Owner, Store Manager and Viewer). A5.2 No screen in staff navigation is an alias of another. A5.3 Accounting: approve an adjustment for 25 Aug, then Prepare Batch for 25 Aug succeeds. A5.4 `grep -r "WLMHW\|HEMW" src/` returns only the seed migration and tests.

### Phase 6 — Release (2–3 days)
Installer that installs/updates SQL Express and the app in one run on a clean Windows 10/11 PC; daily backup task; one-page `docs/USER-GUIDE.md` with screenshots; version 2.0.0. Acceptance: Claude installs on a clean VM or a second PC, imports the two folders, produces the six reports, upgrades from the previous build without data loss.

## 6. Sequence and estimate

| Phase | Codex effort | Depends on | Owner decisions (all decided 15 Sep) |
|---|---|---|---|
| 0 Stabilise | 1–2 days | — | none |
| 1 Data truth | 5–7 days | 0 | D1, D3, D11 |
| 2 Six reports | 7–10 days | 1 | D2, D4, D5, D8, D10 |
| 3 Touch shell | 5–7 days | 2 (can start UI scaffolding after 0) | D7 |
| 4 Security & ops | 3–5 days | 0 (independent of 1–3) | D9 |
| 5 Secondary modules | 8–12 days (all four kept) | 3 | — |
| 6 Release | 2–3 days | all | — |

Phases 1 and 4 can run in parallel on separate branches. Total: roughly 6–8 weeks of Codex time plus Claude audit turnaround after each phase.

## 7. Open items log
- 14 Sep fixes uncommitted — Phase 0 task 1.
- Version 1.0 of this plan, 15 Sep 2026: all six audits incorporated. Changes to this document happen only through Claude's phase audits.
