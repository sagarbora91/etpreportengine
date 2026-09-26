# Phase 5 audit findings — work instructions

**Audited:** branch `phase-5/continuous-sprint` at `77ec104` ("Record Phase 5 completion gates and audit handoff"), 17 commits and 175 files ahead of the merge base `fd4c8b9`.
**Audited on:** 25 September 2026, by Claude (Opus 5), against the A5.1–A5.7 acceptance criteria in `docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md` (version 1.4, the copy on `main`).
**Method:** six independent read-only audits — navigation and roles, accounting lifecycle, store catalogue and status honesty, security, test quality, and report honesty — plus one Release build and one full test run performed by the auditor. No audit process edited code, ran a build, or touched the live `EtpReporting` database.

Every finding below carries file:line evidence. Each is marked either **[verified by the auditor]** — the auditor personally read the code and confirmed it — or **[reported, not re-checked]** — an audit process found it with evidence, and the auditor did not independently re-open it. Treat the second kind as true but re-read the code before changing it.

---

## 1. Independent gate result

The auditor ran the gate rather than repeating the report's claims. From a short path (`C:\p5audit`), Release, one project at a time:

- `dotnet build Etp.Reporting.slnx -c Release -m:1 -nodeReuse:false` → **0 warnings, 0 errors**
- `dotnet test Etp.Reporting.slnx -c Release --no-build -m:1` → **1,003 passed, 0 failed, 4 skipped**
  (Desktop 447, Domain 12, Import 110, Reporting 63, SQL integration 119, SQL adapters 252)

This reproduces `docs/audit/PHASE-5-REPORT.md:137` exactly, project by project. **The report's headline numbers are true.** The Owner's diagnostics log was unchanged across the run (5,893 lines before and after), so the test isolation added in `1a24b94` holds on this branch.

One caveat that matters for anyone re-running it: use a short working directory. A first attempt from a deeply nested path failed two tests with `Unable to load DLL 'Microsoft.Data.SqlClient.SNI.dll' … The filename or extension is too long`. That is a path-length artefact, not a defect.

## 2. Verdict per acceptance criterion

| Criterion | Verdict | Why |
|---|---|---|
| **A5.1** every role-reachable destination works for that role | **NOT MET** | Five destinations render broken or incomplete screens: F-01, F-02, F-03, F-04, F-05. No destination is unreachable, and no role can reach a screen it may not open. |
| **A5.2** no staff screen is an alias of another | **NOT MET** | `trends` and `report-management-trend` read the same query and show the same seven fields (F-06). Two further duplicate pairs: F-07, F-08. |
| **A5.3** approve an adjustment for 25 Aug, then Prepare Batch succeeds | **MET** | Traced end to end through real SQL and the real WPF view. Proven by `PhaseFiveAccountingTests.cs:21-112`, which re-reads from a fresh service. |
| **A5.4** no hard-coded store codes outside seed migration and tests | **MET as written, NOT MET in substance** | The required grep over `src/` is clean. But store *names* remain hard-coded in three runtime strings, and one screen silently picks a store by index (F-09, F-12, F-13). |
| **A5.5** only the five batch statuses; nothing claims data reached Tally | **MET** | CHECK constraint exact, legacy mapping correct, every write inside the set, and the wording is honest everywhere the auditor looked. One cosmetic exception: F-14. |
| **A5.6** duplicate batch refused naming the earlier batch; succeeds after REJECTED | **MET, with two defects** | The guard is in SQL under an exclusive applock. But the refusal gives impossible advice once the earlier batch is exported (F-10), and the day-level rule has no schema backstop for days without invoices (F-11). |
| **A5.7** one receipt, matching hash, Settings company, TEST label, empty reason refused | **MET** | The file is flushed and moved before it is hashed; company and environment are re-verified inside the transaction; the empty-reason refusal happens before any state change, in both C# and SQL. |

**Two findings outrank the criteria** and are listed first: S-01 and S-02. Neither is an A5 criterion; both are security regressions or holes introduced by this phase.

---

## 3. Rules for this work

1. **Merge `origin/main` into this branch before anything else.** `main` is now `e2156d4` and contains four commits this branch does not, including a fix that overlaps F-04. See §7 — there is a known conflict, and resolving it wrongly re-breaks a screen that `main` already fixed.
2. **Never edit an existing migration.** `0014`, `0022`, `0033`, `0034`, `0035` and `0036` have run on the owner's machine. Schema changes go in a new `database/migrations/0037_*.sql`, and any new constraint must preflight for existing violations and `THROW` a plain-words message, in the style of `0033_accounting_foundation.sql:20-21`.
3. **Every fix needs a test that fails before it and passes after.** State that in the commit message. "A test exists" is not evidence; the test must assert the actual claim.
4. **Do not weaken an existing assertion to make it pass.**
5. **Run the full gate before reporting done**, from a short path: `dotnet build Etp.Reporting.slnx -c Release -m:1 -nodeReuse:false` then `dotnet test Etp.Reporting.slnx -c Release --no-build -m:1`. Report real numbers. The baseline to beat is 1,003 passed / 0 failed / 4 skipped.
6. **Do not touch the live `EtpReporting` database**, and do not run tests against anything but disposable `EtpPhase0Test_*` fixtures.
7. **If a fix turns out to be wrong, say so and revert it.** A refuted hypothesis recorded plainly is worth more than a silent workaround.

---

## 4. Findings

Ordered by severity, most serious first. Each says what is wrong, where, what to do, and how to prove it fixed.

### S-01 — BLOCKER — Phase 5 deleted two of the three authorisation layers on restatement

**[verified by the auditor]** A controlled restatement deletes canonical facts — `sales_lines`, `sales_invoice_controls`, `sales_tenders`, `stock_movements`, `stock_snapshots` (`database/migrations/0035_registers_approvals.sql:177-182`). This phase removed both C#-side guards:

- `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerImportPersistenceUseCase.cs:248-254` — the whole `RequireImportAsync(bool ownerRequired, …)` overload and its `if (ownerRequired && !access.CanAdminister) throw new UnauthorizedAccessException("Owner permission is required for a controlled restatement.")` were deleted, replaced by the comment "Corrective replacements remain blocked in SQL until the Owner approves the exact source."
- `src/Etp.Reporting.Desktop/Modules/Imports/ImportWorkspaceView.xaml.cs` — `if (restate && !accessProvider().CanAdminister) throw new UnauthorizedAccessException("Owner permission is required for a restatement.")` was deleted.

The remaining gate is `dbo.prepare_import_restatement` alone (`0035:187-208`). That procedure is well built and genuinely tested, but this contradicts the project's standing rule that every handler re-checks its own role — the same rule that was reinstated for the support package during the Phase 4 recovery. One weakened predicate in that `WHERE` clause and a Store Manager destroys canonical facts with nothing else in the way.

**Fix.** In `SqlServerImportPersistenceUseCase.PersistAsync`, when `request.Restatement is not null`, re-check before the procedure is reached: query `dbo.import_restatement_approvals` joined to `dbo.approval_requests` for a row with `status='APPROVED'`, `applied_import_file_id IS NULL`, and matching `previous_import_file_id`, `replacement_sha256`, `store_code`, `report_code`, `period_start`, `period_end` and `request_reason`; throw `UnauthorizedAccessException` when absent. Restore the desktop-side check in `ImportWorkspaceView.xaml.cs` as well.

**Prove it.** A test in `Etp.Reporting.SqlServer.IntegrationTests` that calls `PersistAsync` with a restatement request and no approval row, as a Store Manager, and asserts `UnauthorizedAccessException` — and that `dbo.sales_lines` still holds its rows afterwards. It must fail against the current code.

### S-02 — MAJOR — email attachments are never confined to the share folder

**[verified by the auditor]** `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerDistributionServices.cs:105-116` — `ValidateEmailAttachmentAsync` checks that the file exists and is under the size limit, then *returns* `new(settings.ShareFolderPath, …)` — the policy it never applies. There is no path containment and no reparse-point check, although the sibling `SqlServerReportEmailService.PreparePdfAsync:14-18` does both. The method is View-gated, and `SqlServerReportEmailService.SendEmailAsync:30-55` passes the caller's `AttachmentPath` straight to MailKit.

Today every in-repo caller supplies a `PreparePdfAsync` output, so there is no live click-path. It is one caller away from a Viewer emailing any file the process can read to any address.

**Fix.** In `ValidateEmailAttachmentAsync`, resolve `Path.GetFullPath(attachmentPath)` and require it to start with `Path.GetFullPath(settings.ShareFolderPath) + Path.DirectorySeparatorChar` (OrdinalIgnoreCase); walk the file and its ancestors rejecting `FileAttributes.ReparsePoint`, reusing the loop at `SqlServerReportEmailService.cs:15-17`; throw `UnauthorizedAccessException("Report attachments must be prepared in the sharing folder.")`.

**Prove it.** A test asserting an out-of-folder path throws and that the transport was never called (`transport.Calls == 0`), alongside the existing role and header-injection tests in `ReportEmailServiceTests`.

### F-01 — MAJOR — A5.1 — "Cash and service entries" never shows the cash-book fields

**[verified by the auditor]** `src/Etp.Reporting.Desktop/Shell/TaskNavigator.cs:459` — `"manual" => (new int[] {8,3,6}, new int[] {8})`. The root panel's child **7** is `CashQuickFields` (`Modules/DailyWorkflow/DailyWorkflowWorkspaceView.xaml:46`; the auditor counted the root `StackPanel`'s direct children to confirm the index). `FocusedTaskLayout` removes every original child and re-adds only the listed indices, so 7 is orphaned on every visit. `RebuildCashQuickFields` still runs and sets `CashQuickFields.Visibility` on a detached panel (`DailyWorkflowWorkspaceView.xaml.cs:436-470`). The whole feature is dead on this screen. It predates Phase 5 but is live at `77ec104`.

**Fix.** Change the `"manual"` arm to `(new int[] {7,8,3,6}, new int[] {8})`. Do **not** add 7 to the actions array — its children are buttons, and `FocusedTaskLayout.cs:43-49` would strip them into the toolbar and destroy the labelled-tile layout.

**Prove it.** A Desktop test that applies the real `"manual"` layout to a real `DailyWorkflowWorkspaceView` and asserts `CashQuickFields` is present in the task's visual tree.

### F-02 — MAJOR — A5.1 — Investigation carries the Approvals screen's button

**[reported, not re-checked]** `TaskNavigator.cs:496` — `_ => (new int[] {0,1,3,4}, new int[] {2})`. Root child 2 is the search `WrapPanel` in `InvestigationApprovalsWorkspaceView.xaml:7`, which contains `RefreshApprovalsButton`; `FocusedTaskLayout.cs:43-49` lifts every button in an action panel into the toolbar. `UpdateAccess` disables that button unless `CanAdminister` (`InvestigationApprovalsWorkspaceView.xaml.cs:51`), so the Store Manager this screen exists for gets a permanently dead control with no explanation. For an Owner it is worse: `RefreshApprovalsAsync:55-66` overwrites the visible investigation status line and populates `ApprovalGrid`, which is not part of this layout.

**Fix.** Move `RefreshApprovalsButton` out of the search `WrapPanel` and into the `ApprovalActions` panel (`InvestigationApprovalsWorkspaceView.xaml:10`, root child 9), which only the `approval-centre` layout shows. No index arrays need to change.

**Prove it.** A test asserting the Investigation task's toolbar contains no button named `RefreshApprovalsButton`, and that the Approvals task's does.

### F-03 — MAJOR — A5.1 — "Calculations" has no status line and no labelled health grid

**[verified by the auditor]** `TaskNavigator.cs:492` — `"kpi" or "profiles" => (new int[] {10,11,13}, new int[] {})`. Index **14** is `AdministrationStatus`, the only control this screen writes success or failure to (`AdministrationWorkspaceView.xaml.cs:58,63`), so a failed load leaves two silently empty grids. This is the same defect fixed for the other administration layouts; Phase 5 added 14 to `users` and the default arm but missed `kpi`. Index **12** (the "Integration health" heading) is missing from all four arms, so `ProductHealthGrid` renders unlabelled everywhere.

**This one interacts with `main` — read §7 before editing.**

**Fix.** After merging `main`, ensure every administration layout includes 14, and add 12 to all of them.

**Prove it.** `main` already carries a test asserting every administration task includes 14 (`OperationsAdministrationWorkspaceViewTests`). Extend it to assert 12 as well.

### F-04 — MAJOR — A5.1 — "Support package" and "Recovery drill" render as a lone button over a blank line

**[verified by the auditor]** `TaskNavigator.cs:498` gives all three maintenance tasks `body = {29}` (`MaintenanceStatus`, empty until an action runs) and `actions = {28}`. The only other body content is the guidance paragraph in `src/Etp.Reporting.Desktop/FocusedTaskLayout.cs:58-63`, which is keyed on the task *title*: `"Support Package"`, `"Backups"`, `"Restore & Recovery Drill"`. The real titles are `"Support package"`, `"Backups"` and `"Recovery drill"` (`TaskNavigation.cs:79-81`) — only `"Backups"` matches. Two of the three Owner maintenance screens therefore have no explanatory text at all. Note `FocusedTaskLayout.cs:74` was updated in this phase for `"Automatic import"`, so this switch was simply missed.

**Fix.** Change the keys at `FocusedTaskLayout.cs:60,62` to `"Support package"` and `"Recovery drill"`. Add a heading by putting `27` into the body array at `TaskNavigator.cs:498`.

**Prove it.** A test that, for each of the three maintenance task titles, applies the real layout and asserts the guidance text is non-empty. Key it off `TaskNavigation`'s titles so the two can never drift apart again.

### F-05 — MINOR — A5.1 — "Automatic import" has no refresh control

**[reported, not re-checked]** `TaskNavigator.cs:500` — `actions = [20,23]` omits root child 0, the DockPanel holding "Refresh operations". `OperationsWorkspaceView.RefreshAsync` runs only on the first Operations Center visit (`TaskNavigator.cs:506-511`), so if that refresh failed, `loadedWatch` stays null and Save throws "Refresh automation settings successfully before saving." with no way to retry from the screen.

**Fix.** Change to `actions = [0,20,23]`.

### F-06 — MAJOR — A5.2 — `trends` is an alias of `report-management-trend`

**[reported, not re-checked]** Both live in Reports → Management (`TaskNavigation.cs:102`; the report entry built at `:121` from `ReportDefinition.cs:61`) and both read the same query — `trends` via `OperationsWorkspaceView.xaml.cs:76` → `SqlServerOperationsAdministrationService.cs:197`, the report via `ReportsWorkspaceView.xaml.cs:231` → `SqlServerApplicationReportQuery.cs:76`, both landing on `Phase2OperationsRepository.LoadManagementTrendAsync`. The same seven fields are shown. The only differences are a bar chart and a separate date picker.

**Fix.** Decide which one survives. Either remove `Add("trends", …)` at `TaskNavigation.cs:102` and move the bar chart onto the `management-trend` report, or delete `management-trend` from `ProductReportCatalogue.All` (`ReportDefinition.cs:61`) and its `case` at `ReportsWorkspaceView.xaml.cs:134`. If `trends` goes, also drop the now-dead `id is "trends"` arm at `TaskNavigator.cs:501`.

**Prove it.** Add the fingerprint test described in F-08's fix, which catches this class automatically.

### F-07 — MINOR — A5.2 — "Database health" duplicates the "Support package" destination

**[reported, not re-checked]** `TaskNavigator.cs:493` gives `"health"` action 18, the `SupportPackageButton` in `AdministrationWorkspaceView.xaml:14`, whose handler runs the same `new-etp-support-package.ps1` as the separate `support-package` destination (`OperationsWorkspaceView.xaml.cs:186-189`). Two Settings → Database entries, one operation, different status lines — and only the Operations path writes the audit row.

**Fix.** Delete `SupportPackageButton` and its handler (`AdministrationWorkspaceView.xaml:14`, `AdministrationWorkspaceView.xaml.cs:113-131`), and set `"health"` actions to `new int[] {}`. Keep `support-package` as the single entry point, since it is the one that audits.

### F-08 — MINOR — A5.2 — two Help topics share the title "Sales Reports"

**[reported, not re-checked]** `HelpCentre.cs:78` and `HelpCentre.cs:81` both use the title `"Sales Reports"`; both become tasks in Settings → Help, so the picker lists the same name twice. Content differs, so it is not a true alias — but it is indistinguishable in navigation.

**Fix.** Change `HelpCentre.cs:81` to `"Stock Reports"`. Screenshot mapping and routing key off the id, so nothing else changes.

**Also add the guard that would have caught F-06 and this:** a test that walks every Owner-reachable destination, records a fingerprint (workspace type, visible section heading, and the set of named grids and inputs in the focused host), and asserts no two task ids produce an identical fingerprint, with an explicit allow-list for any pair kept on purpose.

### F-09 — MAJOR — A5.4 — the cash report silently picks a store by position

**[verified by the auditor]** `src/Etp.Reporting.Desktop/Modules/Reports/ReportsWorkspaceView.xaml.cs:106-107`:

```
if (report == "cash" && Csv(StoreFilterInput.Text) is not { Count: 1 } && stores.Stores.Count > 0)
    StoreFilterInput.Text = stores.Stores[0].Code;
```

Catalogue order is `ORDER BY store_id` (`StoreCatalogRepository.cs:15`), so whichever store was seeded first silently wins, and the user is shown a cash reconciliation for a store they never chose. Two lines below, `ReportTaskScope.RequiresSingleStore` would have asked them to choose — `"cash"` is in that set, and this branch deliberately bypasses it. Phase 5 introduced this shape: the pre-phase line was `StoreFilterInput.Text = "WLMHW";`, so the literal was removed but the defect class was kept.

**Fix.** Delete lines 106-107 and let the existing guard ask for a store. If a default is genuinely wanted, gate it on `stores.Stores.Count == 1` so it can never choose between stores.

**Prove it.** A test with two active stores that opens the cash report with no store selected and asserts the "Choose one store in the header." failure rather than a silently loaded report.

### F-10 — MAJOR — A5.6 — the duplicate-batch refusal gives impossible advice

**[reported, not re-checked]** `ProductisationRepository.Accounting.cs:66` and the trigger at `0033_accounting_foundation.sql:43` both say `'… Reject that unexported batch before preparing another.'`. But an `EXPORTED_AWAITING_IMPORT` batch can never be rejected: `dbo.reject_accounting_batch` accepts only `DRAFT`, `BLOCKED` and `APPROVED_READY` (`0033:97`), and `0033:58-59` throws `51455, 'A rejected or exported batch cannot change status.'`. The criterion is satisfied — the batch is named — but the Owner is told to do something the database forbids.

**Fix.** Select the earlier batch's status alongside its id and branch the message: exported → `'This day is already in exported batch N. An exported batch is final; it cannot be replaced.'`; otherwise keep the current wording. Mirror the branch in the trigger via a **new** migration; do not edit `0033`.

**Prove it.** Extend the A5.6 tests so the `EXPORTED_AWAITING_IMPORT` case asserts the new sentence, and the `APPROVED_READY` case keeps the existing one.

### F-11 — MAJOR — A5.6 — the day-level rule has no database backstop for days without invoices

**[reported, not re-checked]** The only day-level guard is the app-issued statement at `ProductisationRepository.Accounting.cs:64-66`. The schema backstop is a filtered unique index on invoice *reservations* (`0033:22`), and reservations are inserted only from `dbo.sales_invoices` (`Accounting.cs:74-75`) — an adjustment-only day inserts none. That is exactly the shape A5.3 itself tests. Concurrency is safe today because every prepare takes the exclusive applock, but nothing in the schema stops a stale client or hand-run SQL from creating two active batches for such a day.

**Fix.** In the new `0037_*.sql`: preflight for existing violations and `THROW` in the style of `0033:20-21`, then `CREATE UNIQUE INDEX UX_accounting_batches_active_day ON dbo.accounting_batches(store_code, business_date) WHERE status<>'REJECTED';`. Keep the existing `THROW 51452` so the friendly message still wins the race. In the same migration, add `CREATE UNIQUE INDEX UX_accounting_export_receipts_batch ON dbo.accounting_export_receipts(accounting_batch_id);` — "exactly one receipt per batch" is likewise enforced only by code today.

**Prove it.** Apply the migration to a disposable fixture and assert a direct second `INSERT` for the same store and date is refused.

### F-12 — MINOR — A5.4 — hard-coded store names in three runtime strings

**[reported, not re-checked]** Store *codes* are gone, but names are not:

- `src/Etp.Reporting.Infrastructure.SqlServer/DailyReportingPackService.cs:47` `"Titan Helios Combined DSR"` and `:51` `"ETP Complete Daily Management Pack — Titan World + Helios"`, while store membership at `:32-34` is taken from the catalogue. A single store named `EAST` produces a pack titled after two stores that are not in it.
- `src/Etp.Reporting.Desktop/Modules/DailyWorkflow/DailyWorkflowTouchLayout.cs:32` `"Choose Titan World or Helios in the header."` — shown on first arrival at every Close day, Walk-ins, Physical count and Staff targets task, because the header defaults to "All stores".
- `src/Etp.Reporting.Desktop/HelpCentre.cs:76` `"… DSR compares Titan and Helios with combined totals."`

**Fix.** Build the pack strings from catalogue display names (`StoreCatalogRepository.LoadAsync()`); the combined evening sheet already does the right thing with `"All stores"` at `EveningReportRepository.cs:95`. Replace the prompt with `"Choose a single store in the header before entering data."` and the Help sentence with one that does not name stores.

**Prove it.** Extend `PhaseFiveStoreCatalogTests` so its single-store `EAST` case asserts the generated pack title contains no store name absent from the catalogue.

### F-13 — MINOR — A5.4 — no test guards the store-literal rule

**[reported, not re-checked]** No test scans source for store literals, so a regression reintroducing `"WLMHW"` into a view-model would pass the entire suite. The criterion is currently enforced by a human running a grep.

**Fix.** Add `StoreLiteralGuardTests` in `Etp.Reporting.Desktop.Tests` that walks `src/**/*.cs` and `src/**/*.xaml` — resolving the repo root from `AppContext.BaseDirectory`, **excluding `bin/` and `obj/`** — and asserts no match for `\b(WLMHW|HEMW)\b`, naming the offending file and line on failure.

> Note for whoever re-runs the A5.4 command by hand: `grep -r "WLMHW\|HEMW" src/` returns hits on a *built* tree, because the migrations are copied into `bin/`. On this branch the source itself is clean — the auditor confirmed 0 hits once `bin/` and `obj/` are excluded.

### F-14 — MINOR — A5.5 — the batch grid shows an always-empty "TallyReference" column

**[verified by the auditor]** `accounting_batches.tally_reference` is defined (`0014_productisation.sql:351`) and selected (`ProductisationRepository.Accounting.cs:91`) but **never written** — those are the only two references in `database/`, `src/` and `tools/`. It reaches the Owner through `AccountingBatchGrid`, which auto-generates columns. The screen therefore implies a Tally voucher identity the system cannot have, which cuts against the honesty A5.5 exists to protect.

**Fix.** Remove `TallyReference` from `AccountingContracts.cs:54`, `ProductisationModels.cs:95`, the SELECT list and reader ordinals at `ProductisationRepository.Accounting.cs:91,94`, and the mapping at `SqlServerAccountingService.cs:230`. **Leave the database column** — history stays immutable; no migration.

### F-15 — MINOR — the new store repository bypasses the connection policy

**[reported, not re-checked]** `StoreCatalogRepository.cs:12` opens `new SqlConnection(connectionString)` directly. Every other repository routes through `LocalSqlConnectionPolicy.Validate` (e.g. `ProductisationRepository.cs:365`), which rejects SQL credentials, non-local servers, `AttachDbFilename`, user instances, failover partners and `Encrypt=False`. This type is called from `AutomatedOperationsService.cs:24,107`, which receives an unvalidated string.

**Fix.** One line: hold `LocalSqlConnectionPolicy.Validate(connectionString)` in the primary constructor and open from that. (The same was done for `tools/Etp.Reporting.ImportAudit` in `8d9cd72`, now on `main`.)

### F-16 — MINOR — digital registers authorise only in SQL

**[reported, not re-checked]** `SqlServerDigitalRegisterService.cs:32-46` — `LoadAsync`, `LoadDayAsync` and `SaveAsync` call straight through, and `ProductisationRepository.SaveRegisterEntryAsync:138-149` has no `EnsureOwnerAsync`, unlike `SaveSharingContactAsync` at `:128`. `counterparty` holds real customer and vendor names, readable with no role test in C#. SQL does enforce writes (`0035:25-37`, `0035:210`, proven by `PhaseFiveOperationsTests:21-29`), so this is not exploitable today — it is the same missing-second-barrier pattern as S-01.

**Fix.** Give the service a `Func<CancellationToken, Task<ApplicationAccess>> loadAccess` as `SqlServerAccountingService` has; check `CanView` on loads, `CanImport` on save, and `CanAdminister` when `entry.VerificationStatus == "VERIFIED"`.

### F-17 — MINOR — Tally export writes a full path into an immutable receipt, and skips the reparse check

**[reported, not re-checked]** `AccountingAndSharingServices.cs:39` calls `Path.GetFullPath` then `Directory.CreateDirectory` with no junction or symlink check, unlike `PreparePdfAsync` and `WindowsSmtpCredentials.RefuseLinks`. `ProductisationRepository.Accounting.cs:143-145,172` stores the full path in `accounting_export_receipts.output_path`, which `0033:77-78` makes permanently unchangeable — file paths are meant to stay out of export receipts. Exposure is Owner-only (`0033:107`), and overwrite protection is sound (`FileMode.CreateNew`).

**Fix.** Add the ancestor reparse-point loop before `Directory.CreateDirectory`. Then either store only `Path.GetFileName(fullPath)`, or record this as a deliberate, documented exception to the no-paths rule.

### F-18 — MINOR — raw SQL error text reaches staff by numeric range

**[reported, not re-checked]** `DesktopFriendlyError.cs:35`, new in this phase, passes `sql.Message` through for whole number *ranges* (51450-51460, 51430-51432, 51220-51222). `0033:43` builds 51452 with an invoice document number inside it, so that number reaches the screen verbatim. Every consumer today is the Owner-only accounting workspace, so there is no staff-facing leak now — but any future `THROW` landing in those ranges is displayed unreviewed.

**Fix.** Replace the ranges with an explicit set of numbers, or map each number to a curated message.

### F-19 — MAJOR (tests) — A5.1's only real evidence is skipped by default

**[verified by the auditor — my own run skipped exactly 4]** `PhaseFiveFullWindowCaptureTests.cs:424-425` sets `Skip` unless `ETP_PHASE5_UI_CAPTURE=1` **and** `ETP_PHASE5_UI_EVIDENCE` are set. A default `dotnet test` reports green with A5.1 entirely unexercised. The ungated navigation tests only assert `IsAllowed` — a permission predicate, not that a screen renders. This is why F-01 through F-05 survived a 1,003-test green run.

**Fix.** Split the test. Keep the PNG writing behind the env vars; extract the role walk — create the window per role, navigate, assert the focused host has content and the status line shows no failure — into an ungated `[Fact]` against the disposable fixture, with no `RenderTargetBitmap`.

### F-20 — MAJOR (tests) — the role walk covers a fraction of the destinations

**[verified by the auditor]** `PhaseFiveFullWindowCaptureTests.cs:126-133` walks a hard-coded list: 8 tasks, plus 3 for non-Viewers, plus 3 for the Owner. `TaskNavigation.cs` defines **51** `Add(` destinations (90 counting report and help destinations). Roughly 14 of 51 for the Owner. "Every navigation destination" is not proven.

**Fix.** Replace the literal list with `TaskNavigation.All.Where(t => t.Available && t.IsAllowed(window.CurrentShellAccess))` and drive every one. Keep a separate named subset for screenshots so the artefacts do not explode.

### F-21 — MAJOR (tests) — weak assertions on the criteria's own cases

**[reported, not re-checked]** Three gaps, each one line to close:

1. `AccountingFoundationTests.cs:34` and `:69` — the `APPROVED_READY` and `EXPORTED_AWAITING_IMPORT` refusals assert only `ThrowsAsync<SqlException>`. A deadlock, a login failure or a timeout satisfies them. Add `Assert.Equal(51452, error.Number)` and `Assert.Contains($"batch {id}", error.Message)`. Only the `BLOCKED` case (`:25`) checks the message today, and A5.6 names the other two states, not that one.
2. The `CK_accounting_batches_status` constraint is never exercised. Add an `UPDATE … SET status='EXPORTED_TO_TALLY'` that must throw, plus an assertion that the constraint definition lists exactly the five values — otherwise a future migration can widen it silently.
3. The empty-reason refusal is proven only at the C# boundary (`ArgumentException`). The SQL guard at `0033:63` is untested. Add a direct `UPDATE … SET status='APPROVED_READY', approval_reason='   '` that must throw, then re-read and assert `approval_reason` is still NULL.

Also missing: the `APPROVED_READY → REJECTED → save succeeds` leg, which is half of A5.6.

### F-22 — MINOR (tests) — parallel collections plus process-wide pool clears

**[reported, not re-checked — and note the caution below]** `SqlDatabaseFixture.cs:35` and `AccountingFoundationTests.cs:183` call `SqlConnection.ClearAllPools()`, which is process-wide, while the integration assembly has no `xunit.runner.json` and only two collections opt out of parallelism. `PhaseFiveOperationsTests.cs:80` already defends itself with `Pooling = false`; the accounting tests do not.

**Treat this one carefully.** A parallel investigation in this repo tested the obvious fix — narrowing `ClearAllPools()` to a per-connection clear — and it made an intermittent failure **much worse**: 6 of 6 runs failed, against 3 of 11 before. It was reverted, and closure-record item 9 records the refuted hypothesis. The evidence says the global clear is *protective*. If you touch this, prefer `[assembly: CollectionBehavior(DisableTestParallelization = true)]` or `pooling: false` (the parameter already exists at `TestSqlConnections.cs:8`) over removing the clear, measure over **at least six runs** before and after, and do not filter `dotnet test` output down to summary lines — that destroys the failing test's name.

---

## 5. Documentation corrections

All documentation-only; no code changes. The report is accurate about Tally, about what remains NOT_RUN, and about the numbers — the corrections below are about evidence and scope.

1. **`docs/audit/PHASE-5-REPORT.md:137`** — the gate numbers are correct (the auditor reproduced them exactly), but every log cited lives in `tmp/`, which is gitignored and absent from the checkout. Add: "These logs and the capture manifest exist only on the sprint machine and are not preserved in the repository; an auditor must rerun the commands." Consider committing the sanitised capture manifest under `docs/audit/`.
2. **`docs/audit/CLAUDE-HANDOFF.md`** — this phase re-inserted "Last updated: 15 September 2026" under a heading dated 22 September 2026, and says nothing about Phase 5, the branch, or this report. Update it to name the branch, the final implementation commit `99c1f2e`, and `PHASE-5-REPORT.md`, and fix the stale date.
3. **"New unused REOPEN_DAY, MASTER_MAPPING and CONTROL_WAIVER requests are refused"** — **[verified by the auditor]** they are not refused. `0035:4-5` only cancels rows that were already `PENDING`, and `CK_approval_requests_type` (`0014:211`) still accepts all three values. Reword to say no producer creates them and pending rows were cancelled, or add an explicit guard in the new migration.
4. **A5.4's claim "No store-code literals remain in runtime C#/XAML"** — true for codes, but say plainly that store *names* remain in three runtime strings (F-12), and that the scan covered codes only.
5. **"WhatsApp uses the `whatsapp://` protocol with a validated international number"** — the phone is optional and validated only when non-blank (`ArchiveShareLauncher.cs:29-34`); a blank box opens WhatsApp with no recipient. Also note the history row records the constant "Configured phone", not the number.
6. **"Five real themed shell screenshots"** — they are captured against a synthetic fixture database with a single "Demo store" and most metrics blank. Say so, so nobody reads them as production evidence.
7. **"resource SHA-256 checks match the source PNGs"** — no test does this; mark it a one-off manual check, or add a test comparing the pack resource bytes with the files on disk.
8. **"real MailKit against an ephemeral loopback SMTP server"** — true, but all three cases run plaintext with no credentials, so the 465/STARTTLS branches and `AuthenticateAsync` have no coverage. Add that qualifier to the verification table, not only to the NOT_RUN paragraph.
9. **"All 110 Import tests ran with zero skips"** — only on a machine holding the private corpus; without it two golden tests skip and the count is 108. (The auditor's machine has it: 110 passed.)

---

## 6. Dead code worth removing while you are in here

**[reported, not re-checked]** `TaskNavigation.CanonicalId` (`TaskNavigation.cs:33-40`) has no callers and three of its mappings point at ids this phase deleted. `TaskNavigator.cs:482` still handles `id == "settings"`, unreachable because `:397` intercepts it. `"profiles"` in the `:492` switch is unreachable because `:396` intercepts it. `tabOrder` at `TaskNavigation.cs:123` still lists `"Documents"`, a tab no task uses. Also `TaskNavigator.cs:366-367` leaves `focusedWorkspaceKind` stale for Favourites and sets no visibility on either list view, which can leave a blank workspace under a live tab strip after a Help visit.

---

## 7. Merge `main` first — there is a known conflict

`origin/main` is now `e2156d4` and carries four commits this branch does not, including Phase 4 fixes and a change that **overlaps F-03**.

- On `main`, the administration layouts were extracted into `TaskNavigator.AdministrationTaskLayout(string id)`, and **all four arms include 14**, including `"kpi" or "profiles" => ({10,11,13,14}, {})`.
- On this branch the layouts are still an inline `id switch` at `TaskNavigator.cs:492`, where `users` and the default arm have 14 but **`kpi` does not**.

Both sides changed the same lines, so `git merge origin/main` will conflict there. **Resolve in favour of `main`'s extracted method**, keeping every arm's 14, then apply F-03 by adding 12. `main` also carries a test over those layouts; keep it and extend it.

`main` additionally brings: a per-run `%TEMP%` diagnostics folder for tests (so test runs no longer write to the Owner's real log), the backup rotation and pre-migration retention fixes, and the `ImportAudit` connection-policy change referenced in F-15.

---

## 8. What no static audit could settle

These need the application run against a populated database under each of the three roles. They are the real proof of A5.1 and A5.2, and none of them is covered by the suite today:

1. **Walk all 51 task destinations** (90 including report and help destinations) as Owner, Store Manager and Viewer, reading the status line on each. F-01 to F-05 were all found by reading layout index arrays; there may be more of the same class.
2. **Confirm F-06's two trend screens** show row-for-row identical data over the same dates.
3. **Click every Archive share button as a Viewer** — `ArchiveTaskNavigation.cs:20-22` leaves all six Open/Export/Share buttons visible for every role, and only the SMTP test button is role-gated.
4. **Confirm migrations 0033-0036 are applied** on the installed database, and that the accounting DENYs for `etp_store_manager` and `etp_viewer` exist there — without 0033 those roles inherit `GRANT SELECT ON SCHEMA::dbo` from `0022:220`.
5. **Real SMTP** — TLS negotiation, certificate validation and authentication against a real server. No test touches any of it.

---

## 9. Suggested order of work

1. Merge `origin/main`, resolving the `TaskNavigator` conflict as described in §7. Run the gate; confirm 1,003 / 0 / 4 before changing anything.
2. **S-01**, then **S-02** — the two security items.
3. **F-19** and **F-20** — un-gate and widen the role walk. Do this *before* the screen fixes, so F-01 to F-05 are caught by a failing test first.
4. **F-01, F-02, F-03, F-04, F-05** — the broken screens.
5. **F-09**, then **F-10**, **F-11** — the wrong-store defect and the accounting guards, with the new `0037_*.sql`.
6. **F-21** — the weak assertions.
7. **F-06, F-07, F-08** — the aliases, with the fingerprint guard.
8. **F-12 to F-18** — the remaining minors.
9. §5 documentation corrections, §6 dead code.
10. Full gate from a short path, real numbers reported. Then hand back for re-audit.

Nothing in this document should be taken on trust. Every claim names the file and line that supports it; re-read them before you change anything, and if one is wrong, say so.
