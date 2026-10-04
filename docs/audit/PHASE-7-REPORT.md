# Phase 7 report — Tally transfer (Stage 2)

Working record for Phase 7 of [the master plan](ETP-MASTER-AUDIT-AND-PHASED-PLAN.md). Verification labels follow the plan: `UNIT_VERIFIED / SQL_VERIFIED / SIMULATED_TRANSPORT_VERIFIED / WINDOWS_VERIFIED / LIVE_TALLY_7_1_VERIFIED / NOT_RUN`. Nothing here claims that Tally imported anything.

## Preconditions — still open

- Decisions **D12–D18 are OPEN.** Until the owner and accountant freeze them, decision-dependent tasks build validation, fixtures and `BLOCKED` states only (plan rule 7). The printable [decision sheet](PHASE-7-DECISION-SHEET-D12-D18.md) is ready for the owner and accountant.
- Phase 5 acceptance (A5.1, A5.2) and the Phase 6 release are not closed. See [the Phase 5 walkthrough](PHASE-5-ACCEPTANCE-WALKTHROUGH.md).
- No TallyPrime machine has been probed (task 5): every live item is `NOT_RUN`. The printable [probe check sheet](PHASE-7-TALLY-PROBE-CHECK-SHEET.md) is ready for one visit to the Tally PC; it is read-only and uses the TEST company only.

## Increment 1 — pure Slice 7a rules (no schema, no screen, no Tally)

| Item | Plan task | Where | Label |
|---|---|---|---|
| Batch and voucher status names | 2, 3 | `src/Etp.Reporting.Application/Accounting/TallyTransferContracts.cs` | UNIT_VERIFIED (CI run 145, commit 7f81458) |
| Allowed batch transitions (C# copy of the table the SQL procedure will enforce) | 3 | same | UNIT_VERIFIED (CI run 145, commit 7f81458) |
| `AccountingBatchStatusRules.Derive` — the seven-step outcome table | 3 | same | UNIT_VERIFIED (CI run 145, commit 7f81458) |
| Correspondence key build and read-back regex, `KEY_UNSAFE` | 2, 9 | same | UNIT_VERIFIED (CI run 145, commit 7f81458) |
| `TallyEvidencePaths` — evidence folder and file names | 4, 21 | `src/Etp.Reporting.Infrastructure.SqlServer/Tally/` | UNIT_VERIFIED (CI run 145, commit 7f81458) |
| `TallyQuantityParser` — `1 Nos` → 1.000 / `Nos` | 9 | same | UNIT_VERIFIED (CI run 145, commit 7f81458) |

Tests: `tests-dotnet/Etp.Reporting.SqlServer.Tests/TallyTransferRulesTests.cs`.

These are decision-independent: they hold whatever D12–D18 say. Nothing calls them yet; the migration (tasks 1–4), validation (7), export (8), read-back (9), comparison (10) and screen (11) will.

### Choices made where the plan was silent

- **`KEY_UNSAFE` is wider than `:` and `|`.** A document number that is empty, longer than 80 characters or contains white space is also unsafe, because the plan's read-back regex (`[^:|\s]{1,80}`) could never find such a key again.
- **No ten-digit runs in evidence path codes.** The plan's code pattern `^[A-Z0-9][A-Z0-9_-]{0,29}$` would accept a phone number as a store or profile code; task 4's test requires a ten-digit segment to be refused, so any run of ten or more digits is rejected.
- **Quantities with more than three decimals are not parsed** (rather than rounded), so a value is never silently changed; the caller records `NOT_VERIFIABLE`. Thousands separators (`1,000 Nos`) are also not parsed until task 5 shows how the installed build writes them.
- **Self-transitions** `BLOCKED → BLOCKED` and `RECONCILIATION_INCOMPLETE → RECONCILIATION_INCOMPLETE` are allowed because the plan's table lists them (re-validation still failing; a run still incomplete).

## Increment 2 — migration 0038, schema foundation (tasks 1–4)

`database/migrations/0038_tally_transfer_foundation.sql`, tests in `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/TallyFoundationSqlTests.cs` (SQL_VERIFIED on LocalDB, CI run 146, commit 9775eb8; not yet applied to the live database). Tables and columns are listed in `docs/03_DATABASE_SCHEMA.md`.

### THROW numbers

| Symbol | Number | Used in 0038 |
|---|---|---|
| `E-KEY-DUP` | 51570 | reserved (the duplicate reservation itself is index error 2601 on `UX_accounting_voucher_reservations_active`; the save code will re-throw it with this number) |
| `E-KIND-CLASH` | 51571 | reserved |
| `E-TRANSITION` | 51572 | reserved |
| `E-IMMUTABLE` | 51573 | history, artifacts, attempts, reservations |
| `E-SELF-APPROVE` | 51574 | reserved |
| `E-MAP-OVERLAP` | 51575 | reserved |
| `E-SEND-UNKNOWN` | 51576 | reserved |
| `E-PROD-FIRST-DAY` | 51577 | reserved |
| `E-RELEASE-UNPROVEN` | 51578 | reserved |
| reservation or attempt does not match its voucher, batch or company | 51579 | reservations, attempts |

Decided vouchers reuse **51212**, the number Phase 5 already uses for decided accounting entries (the plan's 51202 belongs to the 0014 trigger that 0033 replaced).

### Where 0038 differs from the plan, and why

The plan was written before Phase 5's remediation added invoice reservations, a one-active-batch-per-store-day index and a status trigger. 0038 keeps all of those unchanged:

- **Batch statuses stay the five Phase 5 values.** Widening `CK_accounting_batches_status` to the plan's fifteen, the transition procedure `usp_accounting_batch_transition`, and the audit and approval type additions wait for the increment that first writes a new status (file export, task 8). Widening now would change the A5.5 evidence before Phase 5 is accepted. The C# transition table from increment 1 already carries the full plan list.
- **History is written by trigger, not only by the save path and the procedure.** Every status change, from any code path including the existing Phase 5 approve, reject and export, writes exactly one row. A caller can supply a reason through `SESSION_CONTEXT(N'etp.status_reason')`; approval and rejection reuse the reason the batch already stores.
- **E-KIND-CLASH needs no code yet.** `UX_accounting_batches_active_day` (0037) already allows only one non-rejected batch per store and day, whatever its kind. It also means one Tally batch per store-day, and TEST and PRODUCTION cannot both hold the same day. That fits D13 = daily summary; if D13 chooses per-invoice batches with a selection, that index has to be narrowed in a later migration.
- **Reservation release is enforced in the database.** Rules 1 and 2 of task 2 (rejected batch with no attempt; voucher blocked or excluded before any attempt) run in triggers. A CANCELLED batch releases nothing yet: CANCELLED is not a valid status until the status list widens, and rule 3 needs a read-back.
- **No Store Manager grants.** The plan grants Store Managers SELECT/INSERT on the Tally tables; Phase 5 denies them all accounting tables, and the Phase 5 walkthrough checks those DENYs. 0038 denies the new tables to Store Managers and Viewers too; the read-back and compare increments will grant exactly what they need.
- **Extra integrity:** a voucher's entries must be in the voucher's batch, an attempt's payload and response artifacts must belong to its batch, an attempt must use its batch's Tally company, and a reservation must match a PLANNED voucher of that company.

## Increment 3 — Tally company service and screen (task 1 code)

| Item | Where | Label |
|---|---|---|
| `TallyProfile`, `ITallyProfileService`, `TallyProfileRules` (plain-word checks before the database's own) | `src/Etp.Reporting.Application/Accounting/TallyTransferContracts.cs` | UNIT_VERIFIED (CI run 147, commit b32a7b7) |
| `SqlServerTallyProfileService` — Owner only, Windows sign-in only, one transaction per save, audited as `ConfigurationChange` | `src/Etp.Reporting.Infrastructure.SqlServer/Tally/` | SQL_VERIFIED (CI run 147) |
| Settings → Integrations → **Tally companies** (`tally-companies`, Owner only) | `src/Etp.Reporting.Desktop/Modules/Accounting/TallyCompaniesView.cs` | UNIT_VERIFIED (CI run 147); not yet seen on the installed app (WINDOWS_VERIFIED = NOT_RUN) |

Tests: `TallyProfileRulesTests` (SqlServer.Tests), `TallyProfileServiceSqlTests` (IntegrationTests), `TallyCompaniesViewTests` (Desktop.Tests). The Phase 5 role walk also opens the new screen as Owner.

Where this differs from plan task 1:

- **No new shell destination.** The plan names a `TallySettings` destination with three tabs and five registry wirings. The screen is instead one task under the existing Settings → Integrations section, opened the same way as Settings → Stores & masters → Tender mapping. Ledger mappings (task 13) and masters (task 14) can join it as further tasks or tabs.
- **Audit type.** Saves are audited as the existing `ConfigurationChange` type with a fixed sentence; the plan's `TallyProfileChange` type waits for the audit-type widening (task 3 remainder).
- **Stricter than the database.** The screen refuses JSON files (task 24 not verified) and sending straight to Tally without a this-PC address. Delivery stays FILE; the screen never sets it to HTTP.
- **The Phase 5 "Tally file destination"** on Settings → Integrations → Email, sharing and Tally is unchanged and still drives today's day-journal export. It will be replaced by these companies when the export moves to Tally batches (task 8).

## Increment 4 — evidence, validation, read-back file and comparison (tasks 4, 7, 9, 10, 21)

Everything here works without D12–D18 and without a Tally machine. UNIT_VERIFIED and SQL_VERIFIED on LocalDB in CI: evidence store run 148 (e2bb886), validation rules run 149 (c26a81f), engine and XML reader run 150 (818913b), migration 0039 and service run 151 (988883b), recovery plan and manifest run 152 (91de674). Not yet applied to the live database; nothing is WINDOWS_VERIFIED or LIVE_TALLY_7_1_VERIFIED.

| Item | Plan task | Where |
|---|---|---|
| `TallyEvidenceFiles` / `TallyEvidenceStore`: write once, SHA-256, register in `tally_artifacts`, verify OK / CHANGED / MISSING | 4, 21 | `Infrastructure.SqlServer/Tally/TallyEvidenceStore.cs` |
| `AccountingValidationRules`: every task 7 rule except RULE-PAY-001, the approval gate and blocked reasons | 7 | `Application/Accounting/AccountingValidationRules.cs` |
| `TallyReconciliationEngine`: three-way comparison, fixed required actions, batch outcome via `Derive` | 10 | `Application/Accounting/TallyReconciliation.cs` |
| `TallyVoucherXmlReader`: hardened reader for the written file and a Day Book read-back | 8 (re-parse), 9 | `Infrastructure.SqlServer/Tally/TallyVoucherXmlReader.cs` |
| Migration 0039: findings, read-backs, actual vouchers and lines, runs, differences | 7, 9, 10 | `database/migrations/0039_tally_findings_readbacks_reconciliation.sql` |
| `SqlServerTallyReconciliationService`: save findings, accept a WARN once, load a hand-exported Day Book file, compare and record a run | 7, 9, 10 | `Infrastructure.SqlServer/Tally/SqlServerTallyReconciliationService.cs` |
| `TallyRecoveryPlanBuilder` and `SaveRecoveryPlanAsync`: one proposed step per unreconciled voucher, saved as `recovery-plan-<n>.json`; never resends the batch | 22 | `Application/Accounting/TallyRecoveryPlan.cs` |
| `BuildManifestAsync`: `manifest.json` with company, selection, control totals, versions and every file's SHA-256; refused while a file is changed or missing; hash stored on the batch | 21 | same service |
| `docs/OPERATIONS.md` "When Tally and ETP disagree" in staff words | 22 | docs |

Tests: `TallyEvidenceFilesTests`, `AccountingValidationRulesTests`, `TallyReconciliationEngineTests`, `TallyVoucherXmlReaderTests` (SqlServer.Tests); `TallyEvidenceStoreSqlTests`, `TallyReconciliationSqlTests` (IntegrationTests, including a G01 file round trip and a second compare that leaves the first run untouched).

Not verified against Tally: the XML tags are TallyPrime's published format, not an export from the installed build (task 5 is NOT_RUN). The G01 fixtures are synthetic and have not been reviewed by the accountant.

Where this differs from the plan:

- **Nullable actual-voucher fields.** The plan makes `voucher_type`, `voucher_date` and `total_amount` NOT NULL in `tally_actual_vouchers`; they are nullable so a field Tally did not return is stored as missing and compared as NOT_VERIFIABLE, never as zero. `voucher_index` was added to tie differences to the voucher in the file.
- **Header mismatches** (type, date, number, cancellation) are recorded as `STATUS_MISMATCH`, the nearest type in the plan's list, with the field named in the rationale.
- **Voucher numbers** are compared only when D18 says Tally keeps ETP's number; otherwise they are not checked at all, rather than recorded as NOT_VERIFIABLE (which would hold every batch incomplete).
- **No batch status change.** A comparison records the run and each voucher's outcome; the batch outcome is returned but not written, because the batch status list widens only with the file-export step (task 8).
- **No tolerances table yet.** `tally_reconciliation_tolerances` needs its approval type; until then the engine uses none and money must match to the paisa.
- **Source re-hash is the caller's.** `CompareAsync` takes `sourceUnchanged` from the caller, because recomputing the source hash belongs to the invoice composer (task 6, waiting on D13–D17).
- **Recovery plan file name.** Task 21's fixed file list has no name for a recovery plan; `recovery-plan-<n>.json` in the batch folder was added. The Owner's approval of a plan waits for the approval type widening.
- **Manual file only.** The HTTP read-back gateway (task 9) waits for the task 5 probe to show which request returns full vouchers.

## Increment 5 — fixes from a code review of increments 1–4 (migration 0040)

A read-only review of the merged Phase 7 code found no high-severity defect. These were confirmed and fixed. UNIT_VERIFIED and SQL_VERIFIED on LocalDB in CI: run 158 attempt 2 (687d853). Attempt 1 timed out with every WPF desktop test hanging from the first one, including screens this change does not touch; the re-run on another runner passed unchanged. Not yet applied to the live database.

| # | Defect | Fix | Where |
|---|---|---|---|
| 1 | The dates of a hand-exported Day Book are typed by the operator. With the wrong day, every voucher became MISSING (FAIL), a false "proven absence" that would later invite a resend. | A file that states its own period (SVFROMDATE/SVTODATE) must contain the dates entered, or it is stored incomplete (`PERIOD_MISMATCH`). A file with no voucher at all on a voucher's day never proves it missing: `NOT_VERIFIABLE`, RECO-COV-008. | `TallyVoucherXmlReader`, `SqlServerTallyReconciliationService`, `TallyReconciliationEngine`, 0040 |
| 2 | A file written but not registered (crash between the two) blocked its name for good; `manifest.json` could then never be written. The file was also deleted after an INSERT whose outcome was unknown, leaving a row with no file. | A leftover unregistered file is renamed to `<name>.unregistered-<utc>` and the name is used. After a failed INSERT the file is deleted only when the row is certainly absent. | `TallyEvidenceStore` |
| 3 | Manifest registered but `manifest_sha256` not recorded could never be completed. | A retry records the hash of the registered manifest. | `BuildManifestAsync` |
| 4 | A batch with no registered written file (B) was compared as if B matched. | `NOT_VERIFIABLE`, RECO-INT-006; the voucher stays ACTUAL_LOCATED, not RECONCILED. | engine, service |
| 5 | A Tally company's short code, company name or books could be changed after batches used it (splitting its evidence folders, or turning test batches into live ones). | Refused once any batch or read-back uses the company. | `SqlServerTallyProfileService` |
| 6 | Accepting a failure, or a warning twice, showed "cannot be deleted" (51573): the guard triggers treated a zero-row UPDATE as a delete. | The triggers return when no row changed; the caller's 51579 is shown. | 0040 |
| 7 | In a decided batch a BLOCKED voucher could be set back to PLANNED after its reservation was released. | Never back to PLANNED, BLOCKED or EXCLUDED, and BLOCKED/EXCLUDED stay so (51212). | 0040 |
| 8 | A blocked, excluded or unsent voucher of the batch found in Tally produced no difference. | EXTRA (WARN) with RECO-COV-009, naming the voucher. | engine |
| 9 | The written file was re-read after its hash check, by a path that skipped the link checks. | Read once through the checked path; the same bytes are hashed and parsed. | `TallyEvidenceFiles.ReadVerifiedAsync` |
| 10 | A short code with ten or more digits, or a Windows device name (CON, NUL, COM1…), was saved but refused by every evidence write. | The same rules on save. | `TallyProfileRules.IsFolderSafeCode` |
| 11 | Saving from the Tally companies screen reset the posting dates, delivery mode and file format. | Fields the screen does not show are kept. | `TallyCompaniesView` |
| 12 | Invoice-view vouchers keep the sales ledger inside each stock item; the reader missed it, so every such voucher would show LEDGER_MISMATCH. | Those lines are read too. Still TallyPrime's published layout, not checked against the installed build (task 5). | `TallyVoucherXmlReader` |

Migration 0040 replaces two 0039 triggers and one 0038 trigger with `CREATE OR ALTER` and widens `CK_tally_readbacks_reason`; 0038 and 0039 are unchanged.

## Increment 6 — Tally batch creation, one voucher per invoice (task 6, migration 0041)

Built on the decision sheet's recommendations, accepted by the owner on 3 Oct 2026 for building: D13 one voucher per invoice, D14 one retail ledger, D15 one GST ledger per tax and rate, D16 payment inside the voucher, D17 accounting only; and D12 one company for the firm with each store as a cost centre. The ledger names stay settings (approved mappings) until the accountant fills in the sheet. If the accountant decides differently, the composer refuses the company in plain words rather than guessing. UNIT_VERIFIED and SQL_VERIFIED on LocalDB in CI: run 161 (a0f89b2). Run 160 (b7e886b) failed on three test-setup and text faults, fixed in a0f89b2. Not yet applied to the live database.

| Item | Where |
|---|---|
| `TallySalesVoucherComposer`: pure; per invoice a tender debit, round-off line, sales revenue credit and one credit per GST component and rate; never derives tax, never adds a balancing line; canonical-JSON `source_sha256` and `plan_sha256` | `Application/Accounting/TallySalesVoucherComposer.cs` |
| `SqlServerTallySalesBatchService`: reads the day (latest final generation, lines, GST rows from one current R018 import per invoice, tenders with their mode), previews, and saves a `SALES_VOUCHERS` batch with vouchers, entries, reservations for planned vouchers and the Phase 5 invoice reservations, in one transaction; a preview that no longer matches the database is refused | `Infrastructure.SqlServer/Tally/SqlServerTallySalesBatchService.cs` |
| Migration 0041: `tally_profile_stores.cost_centre` (D12); edited on Settings → Tally companies as `STORE=Name; STORE=Name` | `database/migrations/0041_tally_store_cost_centres.sql`, `TallyCompaniesView` |

**Blocked invoices.** Every invoice the composer cannot plan exactly is stored as a BLOCKED voucher with a reason. Two kinds:

- *Left out by this step's limits* (`NOT_IN_SCOPE_7A`: returns, refund tenders, split payments, mixed GST rates, zero or negative amounts; `CESS_NOT_SUPPORTED`; `KEY_UNSAFE`): the rest of the day can still be approved. These invoices need a later step, and because a day has one active batch they cannot be sent until that one-batch-per-day rule is narrowed.
- *Faults to fix* (`MAPPING_MISSING`, `TENDER_MODE_UNKNOWN`, `TAX_ROW_MISSING`, `TAX_AMOUNT_MISSING`, `TAX_SPLIT_MISMATCH`, `SOURCE_AMOUNT_MISSING`, `UNBALANCED`, `COST_CENTRE_MISSING`): the batch is saved BLOCKED with the list, so nothing from the day is approved until they are fixed, the batch rejected and the day prepared again.

Where this differs from the plan:

- **Composer in Application, service in Tally.** The plan puts the composer in Infrastructure and routes it through `SqlServerAccountingService.PreviewAsync`. It is pure, so it sits next to the other Phase 7 rules, and a separate service keeps the Phase 5 day-journal path untouched.
- **GST per invoice, not per line.** `etp_r018` rows are matched to the invoice and summed per component and rate; every product on the invoice must have a GST row and the components must equal the lines' tax to the paisa. Matching a row to one line by product code would double-count an invoice with the same product twice.
- **One R018 import per invoice.** Only rows from the latest non-superseded import that has rows for the invoice are read, so a day imported twice never doubles its tax.
- **The day-journal and invoice-voucher kinds exclude each other.** A day already in an unrejected day-journal batch is refused with 51571 (`E-KIND-CLASH`).
- **Cost centre on every line.** The store's cost centre is recorded on each entry; which ledgers carry it in the Tally file is decided by the file export (task 8), because Tally accepts cost centres only on ledgers set up for them.
- **No fixture folder yet.** The G01 and round-off cases are written in the test code; `tests-dotnet/fixtures/tally-golden/` with accountant-reviewed `expected-postings.json` comes with task 12.
- **Validation findings are not saved at preparation yet.** The task 7 rules exist; running them at save and blocking approval on FAIL comes with the approval step.

Tests: `TallySalesVoucherComposerTests` (SqlServer.Tests); `TallySalesBatchSqlTests`, cost-centre cases in `TallyProfileServiceSqlTests` (IntegrationTests); cost-centre parsing in `TallyCompaniesViewTests`.

## Increment 7 — Tally vouchers and Tally ledgers screens, validation gate (tasks 7, 11 part, 13)

Makes batch creation usable from the app. Not yet verified in CI at the time of writing.

| Item | Where |
|---|---|
| **Settings → Accounting → Tally vouchers** (Owner): choose a test company in use, a store and a business day; *Prepare vouchers* shows each invoice as Ready, Left out or Fix needed with its reason, and the validation findings; *Save batch* stores the batch with its findings; a warning is accepted with a reason | `Desktop/Modules/Accounting/TallyVouchersView.cs` |
| **Settings → Integrations → Tally ledgers** (Owner): every version of the ledger names for Tally business events per store; the events a store still needs (one per active payment mode, round-off, sales, and each GST component and rate seen in its R018 imports); a new version from a date with a reason, through the existing mapping approval | `Desktop/Modules/Accounting/TallyLedgersView.cs`, `Infrastructure.SqlServer/Tally/SqlServerTallyLedgerMappingService.cs` |
| Validation at preparation (task 7): the rules run over the planned vouchers; a FAIL blocks the voucher (`VALIDATION_FAILED: <rules>`) and the day; findings are saved with the batch in the same transaction | `Application/Accounting/TallySalesBatchContracts.cs`, `SqlServerTallySalesBatchService` |
| Approval gate: a `SALES_VOUCHERS` batch is approved only when its Tally company is a test company in use and every finding is cleared (failures fixed, warnings accepted); 51221 otherwise | `ProductisationRepository.ApproveAccountingBatchAsync` |
| The Phase 5 day-journal export refuses a `SALES_VOUCHERS` batch (51571), so approved invoice vouchers can never be written as one journal | `ProductisationRepository.ExportAccountingBatchAsync` |

Where this differs from the plan:

- **Own screen, not the accounting workspace steps.** Task 11 extends the Phase 5 accounting screen with five steps. Preparation and warnings sit on their own Settings screen for now; approval and rejection stay on the accounting screen, which already lists every batch. Steps for the Tally file and read-back join when tasks 8 and 9 are built.
- **Ledger mappings stay per store.** The existing approval path records a mapping for one store from a date; the screen keeps that. An all-stores mapping is still honoured when one exists.
- **Validation is not repeated for left-out invoices.** The composer already holds them back with a reason; judging them again would only repeat it as a failure and stop the rest of the day.

## Not started

Status widening, transition procedure and audit types (task 3 remainder), sales voucher XML export (8), HTTP read-back gateway (9), screen steps for the Tally file and read-back (11), golden fixtures (12), and everything in Slices 7b–7d.
