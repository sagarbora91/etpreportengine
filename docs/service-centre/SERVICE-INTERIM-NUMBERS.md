# Service Centre interim: number register

**Owner:** lane L0 (Service interim, S-2, decision 15, 3 Oct 2026). Other lanes take numbers only from their own block below and add a row here when they use one.

## Migration

| Number | File | Release |
|---|---|---|
| 0046, 0047 | (on `release/1.9.4`) | 1.9.4 |
| **0048** | `database/migrations/0048_service_centre_interim.sql` | Service interim (proposed 1.9.5) |
| **0049** | `database/migrations/0049_tally_store_cost_centres.sql` (GitHub PR #3, renumbered from its 0041; decision 18; shipped, merged into 1.10.0 by decision 26) | 1.10.0 |
| **0050** | `database/migrations/0050_service_centre_ui.sql` (Service UI wave, decision 25, lane sql; the design called it `0050_service_job_model.sql`) | 1.10.0 |
| **0051** | `database/migrations/0051_staff_targets_owner_only.sql`: staff targets Owner-only (decision 27) | 1.10.0 |
| 0052 | `0052_service_job_index.sql`: held for the materialised job index (`service_job_index` + `refresh_service_job_index`). **Only if** the 1.10.0 acceptance timing (`docs/roadmap/SERVICE-UI-1.10.0-ACCEPTANCE.md`) shows a Service screen over about 1 s (SD-11) | 1.10.x, if measured slow |
| next free | `service_claim_settlements`: an Owner-entered claim settlement table, once planned as 0051. **Not used in 1.10.0**: decision 25 Q9 = A, claims raised only; it takes the next free number if the Owner asks | later, if the Owner asks |
| 0053 onwards | | free |

0048 must ship after 1.9.4's 0046/0047. Until `release/1.9.4` is merged into `feature/service-interim`, `MigrationTests.Shipped_migrations_have_unique_contiguous_four_digit_numbers_from_0001` fails on the Service branches. That is expected; no placeholder 0046/0047 files are added.

0048 has three sections, each with one owner:

| Section | Owner | Content |
|---|---|---|
| `A_SERVICE_STORE` | L0 | SERVICE business unit, store AW330 (inactive), trigger `trg_stores_service_unit_inactive` |
| `B_SERVICE_LANDING` | L2 | 36 landing tables (35 + S041 GPRC CLAIM, L10), indexes, locked-day triggers, `append_*` procedures, grants (generated) |
| `C_SERVICE_READ` | L4 | read-rule views and grants; plus L10's `v_service_gprc_claims` and `v_service_manual_money` from `scripts/service-centre/c_service_read_gprc_and_money.sql` (emitted by L4's generator at integration) |

0050 has one section, `D_SERVICE_UI_READ`, owned by lane sql (1.10.0 wave). It holds views only: `v_service_status_view_facts`, `v_service_claims`, `v_service_job`, `v_service_job_timeline`, `v_service_parts`, `v_service_parts_transit`, `v_service_stock_summary`, and a `CREATE OR ALTER` of 0048's `v_service_pending_current` (open SRNs only, plus extra columns). Grants and denies are as in 0048 section C. It writes no data and uses no SQL error number. The optional `stores.is_service_money_shop` flag (design SD-07) was not added; `v_service_manual_money` keeps the WLMHW literal. See `docs/03_DATABASE_SCHEMA.md`, "Service Centre UI — migration 0050".

### Service task ids (1.10.0)

The Service rail section (`TaskNavigation.Sections`: Today, Import, Reports, Stock, Service, Settings). Every task is Viewer and up (MinimumRole 1, decision 25 Q13). The four 1.9.5 ids are unchanged; they moved from Reports to Service.

| Tab | Task id | Title (Ctrl+K) | Since |
|---|---|---|---|
| Today | `service-today` | Service today | 1.10.0 |
| Pending | `service-pending` | Service pending board | 1.9.5 (board from 1.10.0) |
| Jobs | `service-job-history` | Service job history | 1.9.5 |
| Jobs | `service-jobs` | Service jobs | 1.9.5 (was "Service jobs by status") |
| Claims | `service-claims` | Service claims | 1.10.0 |
| Parts | `service-parts` | Service parts and purchases | 1.10.0 |
| Money | `service-money` | Service money check | 1.9.5 |

Help topic `service-centre` (unchanged id; its text describes the six tabs). Destination "Service Centre" opens `service-today`.

## Service family codes (S-codes)

S001-S040 come from the consolidation builder (`docs/04a_CONSOLIDATION_CONTRACT.md`); S038 is a retired report name
(Not needed). A new Service family takes the next free code, and lanes add a row here.

| Code | Family | Since | Owner | Notes |
|---|---|---|---|---|
| **S041** | `GPRC_Claim`: raw ETP export `GPRC CLAIM*` (sheet "GPRC Claims Report", 34 columns, header row 1) | 4 Oct 2026, decision 16 (Q6) | L10 | Its own family, not a second layout of S023 (Sagar's choice). Landing table `etp_landing_s041` in 0048 section B (no new migration number). Read rule DateLog on `transaction_date` (the date part of Transaction Date). Raw export only: the consolidated set has no S041 workbook, so the week fixture folders do not hold one. GPRC history is S023 up to 5 Aug 2026 and S041 afterwards; `dbo.v_service_gprc_claims` reads both, S041 winning per claim document. |
| S042 onwards | | | | free |

1.10.0 adds no Service family. S027 TAT and S028 Technician productivity stay Not needed (`SERVICE_FAMILY_DEFERRED`): decision 25 Q6, because TAT comes from the S018/S029 dates and S029 carries the mechanic.

## SQL error numbers: block 51900-51929

| Range | Owner | Used |
|---|---|---|
| 51900-51904 | L0 | `trg_stores_service_unit_inactive`: 51900 "A Service Centre store cannot be made an active shop store." (insert or update with is_active=1); 51904 "A Service Centre store cannot be moved out of the Service Centre business unit." (business_unit_id changed or set to NULL). Lane L6 maps both in Settings > Stores. |
| 51905-51914 | L2 | none yet |
| 51915-51924 | L4 | none yet |
| 51925-51929 | spare | |

1.10.0 (0050) takes no number from this block.

**Numbers in the block already taken outside the database (do not use):** 51901, 51902, 51903, 51910, 51920. See the check below. They are thrown by setup and restore scripts, not by migrations, so they cannot meet a migration error at run time, but C# code and runbooks that name a number must stay unambiguous. Lanes skip them:

- L0 uses only 51900 and 51904 (51901-51903 are taken; L0 has no number left).
- L2 has 51905-51909 and 51911-51914 (51910 is taken).
- L4 has 51915-51919 and 51921-51924 (51920 is taken).

### The check (3 Oct 2026)

`git grep -nwE "519[0-2][0-9]"` on `v1.9.3`, `release/1.9.4` and every `fix194/*` branch (`l1-stock`, `l2-cro`, `l3-cashbook`, `l4-walkins`, `l5-labels`, `l6-imports-dq`, `l7-small-ui`, `l8-ledger-location`). Every ref gave the same hits, and none is in `database/migrations/` or `src/`:

| Number | Where | What |
|---|---|---|
| 51901 | `scripts/restore-etp-database.ps1` | the database already exists |
| 51902 | `scripts/restore-etp-database.ps1` | SQL Server reported no default data or log folder |
| 51903 | `scripts/restore-etp-database.ps1` | a database file of that name already exists |
| 51910 | `scripts/restore-etp-database.ps1` | Windows could not resolve the account running the restore |
| 51920 | `scripts/bootstrap-etp-prerequisites.ps1` | the setup account is not the new database's Owner |

The tests `RestoreDatabaseScriptTests` (51901) and `BootstrapPrerequisiteTests` (51920) pin two of them. 51900 and 51904 were unused everywhere when L0 took them.

The 1.10.0 launch kit (`Reference/Work in progress 2026-10-03/1.10.0-LAUNCH-KIT.md`) reserves numbers only in 51700-51799; it names nothing in 519xx.

## Diagnostic codes (strings)

Constants in `src/Etp.Reporting.Import/Service/ServiceInterimFamilies.cs` (`ServiceInterimFamilies.Codes`).

| Code | Severity | Meaning | Raised by |
|---|---|---|---|
| `FAMILY_DERIVED` | Not needed | S001 RepairRegister is the builder's union of the ten status views; it is not imported | L3 |
| `SERVICE_FAMILY_NOT_NEEDED` | Not needed | S005 (tender summary) and S038 (retired SRN report name) are not needed (Owner) | L3 |
| `SERVICE_FAMILY_DEFERRED` | Not needed | S027 (TAT) and S028 (technician productivity) are deferred to P8 | L3 |
| `SERVICE_SNAPSHOT_DATE_NEEDED` | Refusal | a Service file could not be dated. Message: "Put the Service files in a folder whose name ends with the date, e.g. 'Service Centre till 05 oct 2026'. The Import screen's date is only for a restatement." (`ServiceRouting.DateNeededMessage`) | L3 |
| `SERVICE_SNAPSHOT_DATE_DIFFERS_FROM_HISTORY` | Information | the folder date differs from the latest `Snapshot_As_Of` (S006, S009, S010); never refuses | L3 |
| `SERVICE_STORE_DEFAULTED` | Information | no store column and no sibling store: the Service store AW330 was used | L3 |
