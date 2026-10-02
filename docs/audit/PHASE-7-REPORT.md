# Phase 7 report — Tally transfer (Stage 2)

Working record for Phase 7 of [the master plan](ETP-MASTER-AUDIT-AND-PHASED-PLAN.md). Verification labels follow the plan: `UNIT_VERIFIED / SQL_VERIFIED / SIMULATED_TRANSPORT_VERIFIED / WINDOWS_VERIFIED / LIVE_TALLY_7_1_VERIFIED / NOT_RUN`. Nothing here claims that Tally imported anything.

## Preconditions — still open

- Decisions **D12–D18 are OPEN.** Until the owner and accountant freeze them, decision-dependent tasks build validation, fixtures and `BLOCKED` states only (plan rule 7).
- Phase 5 acceptance (A5.1, A5.2) and the Phase 6 release are not closed. See [the Phase 5 walkthrough](PHASE-5-ACCEPTANCE-WALKTHROUGH.md).
- No TallyPrime machine has been probed (task 5): every live item is `NOT_RUN`.

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

These are decision-independent: they hold whatever D12–D18 say. Nothing calls them yet; the migration (tasks 1–4), composer (6), validation (7), export (8), read-back (9), comparison (10) and screen (11) will.

### Choices made where the plan was silent

- **`KEY_UNSAFE` is wider than `:` and `|`.** A document number that is empty, longer than 80 characters or contains white space is also unsafe, because the plan's read-back regex (`[^:|\s]{1,80}`) could never find such a key again.
- **No ten-digit runs in evidence path codes.** The plan's code pattern `^[A-Z0-9][A-Z0-9_-]{0,29}$` would accept a phone number as a store or profile code; task 4's test requires a ten-digit segment to be refused, so any run of ten or more digits is rejected.
- **Quantities with more than three decimals are not parsed** (rather than rounded), so a value is never silently changed; the caller records `NOT_VERIFIABLE`. Thousands separators (`1,000 Nos`) are also not parsed until task 5 shows how the installed build writes them.
- **Self-transitions** `BLOCKED → BLOCKED` and `RECONCILIATION_INCOMPLETE → RECONCILIATION_INCOMPLETE` are allowed because the plan's table lists them (re-validation still failing; a run still incomplete).

## Increment 2 — migration 0038, schema foundation (tasks 1–4)

`database/migrations/0038_tally_transfer_foundation.sql`, tests in `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/TallyFoundationSqlTests.cs` (SQL_VERIFIED on LocalDB when CI is green; not yet applied to the live database). Tables and columns are listed in `docs/03_DATABASE_SCHEMA.md`.

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

## Not started

Status widening and transition procedure (task 3 remainder), Tally profile service and Settings screen (task 1 code), evidence store (task 4 code), composer (6), validation rules (7), XML export (8), read-back gateway (9), reconciliation engine (10), screen (11), golden fixtures (12), and everything in Slices 7b–7d.
