# Phase 7 report — Tally transfer (Stage 2)

Working record for Phase 7 of [the master plan](ETP-MASTER-AUDIT-AND-PHASED-PLAN.md). Verification labels follow the plan: `UNIT_VERIFIED / SQL_VERIFIED / SIMULATED_TRANSPORT_VERIFIED / WINDOWS_VERIFIED / LIVE_TALLY_7_1_VERIFIED / NOT_RUN`. Nothing here claims that Tally imported anything.

## Preconditions — still open

- Decisions **D12–D18 are OPEN.** Until the owner and accountant freeze them, decision-dependent tasks build validation, fixtures and `BLOCKED` states only (plan rule 7).
- Phase 5 acceptance (A5.1, A5.2) and the Phase 6 release are not closed. See [the Phase 5 walkthrough](PHASE-5-ACCEPTANCE-WALKTHROUGH.md).
- No TallyPrime machine has been probed (task 5): every live item is `NOT_RUN`.

## Increment 1 — pure Slice 7a rules (no schema, no screen, no Tally)

| Item | Plan task | Where | Label |
|---|---|---|---|
| Batch and voucher status names | 2, 3 | `src/Etp.Reporting.Application/Accounting/TallyTransferContracts.cs` | UNIT_VERIFIED when CI is green |
| Allowed batch transitions (C# copy of the table the SQL procedure will enforce) | 3 | same | UNIT_VERIFIED when CI is green |
| `AccountingBatchStatusRules.Derive` — the seven-step outcome table | 3 | same | UNIT_VERIFIED when CI is green |
| Correspondence key build and read-back regex, `KEY_UNSAFE` | 2, 9 | same | UNIT_VERIFIED when CI is green |
| `TallyEvidencePaths` — evidence folder and file names | 4, 21 | `src/Etp.Reporting.Infrastructure.SqlServer/Tally/` | UNIT_VERIFIED when CI is green |
| `TallyQuantityParser` — `1 Nos` → 1.000 / `Nos` | 9 | same | UNIT_VERIFIED when CI is green |

Tests: `tests-dotnet/Etp.Reporting.SqlServer.Tests/TallyTransferRulesTests.cs`.

These are decision-independent: they hold whatever D12–D18 say. Nothing calls them yet; the migration (tasks 1–4), composer (6), validation (7), export (8), read-back (9), comparison (10) and screen (11) will.

### Choices made where the plan was silent

- **`KEY_UNSAFE` is wider than `:` and `|`.** A document number that is empty, longer than 80 characters or contains white space is also unsafe, because the plan's read-back regex (`[^:|\s]{1,80}`) could never find such a key again.
- **No ten-digit runs in evidence path codes.** The plan's code pattern `^[A-Z0-9][A-Z0-9_-]{0,29}$` would accept a phone number as a store or profile code; task 4's test requires a ten-digit segment to be refused, so any run of ten or more digits is rejected.
- **Quantities with more than three decimals are not parsed** (rather than rounded), so a value is never silently changed; the caller records `NOT_VERIFIABLE`. Thousands separators (`1,000 Nos`) are also not parsed until task 5 shows how the installed build writes them.
- **Self-transitions** `BLOCKED → BLOCKED` and `RECONCILIATION_INCOMPLETE → RECONCILIATION_INCOMPLETE` are allowed because the plan's table lists them (re-validation still failing; a run still incomplete).

## Not started

Migration and THROW-number block (tasks 1–4), composer (6), validation rules (7), XML export (8), read-back gateway (9), reconciliation engine (10), screen (11), golden fixtures (12), and everything in Slices 7b–7d.
