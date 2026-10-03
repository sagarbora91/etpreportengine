# Service Centre interim: number register

**Owner:** lane L0 (Service interim, S-2, decision 15, 3 Oct 2026). Other lanes take numbers only from their own block below and add a row here when they use one.

## Migration

| Number | File | Release |
|---|---|---|
| 0046, 0047 | (on `release/1.9.4`) | 1.9.4 |
| **0048** | `database/migrations/0048_service_centre_interim.sql` | Service interim (proposed 1.9.5) |
| 0049 onwards | | 1.10.0 |

0048 must ship after 1.9.4's 0046/0047. Until `release/1.9.4` is merged into `feature/service-interim`, `MigrationTests.Shipped_migrations_have_unique_contiguous_four_digit_numbers_from_0001` fails on the Service branches. That is expected; no placeholder 0046/0047 files are added.

0048 has three sections, each with one owner:

| Section | Owner | Content |
|---|---|---|
| `A_SERVICE_STORE` | L0 | SERVICE business unit, store AW330 (inactive), trigger `trg_stores_service_unit_inactive` |
| `B_SERVICE_LANDING` | L2 | 35 landing tables, indexes, locked-day triggers, `append_*` procedures, grants (generated) |
| `C_SERVICE_READ` | L4 | read-rule views and grants |

## SQL error numbers: block 51900-51929

| Range | Owner | Used |
|---|---|---|
| 51900-51904 | L0 | 51900 `trg_stores_service_unit_inactive`: "A Service Centre store cannot be made an active shop store." |
| 51905-51914 | L2 | none yet |
| 51915-51924 | L4 | none yet |
| 51925-51929 | spare | |

**Numbers in the block already taken outside the database (do not use):** 51901, 51902, 51903, 51910, 51920. See the check below. They are thrown by setup and restore scripts, not by migrations, so they cannot meet a migration error at run time, but C# code and runbooks that name a number must stay unambiguous. Lanes skip them:

- L0 uses only 51900 (51901-51903 are taken).
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

The tests `RestoreDatabaseScriptTests` (51901) and `BootstrapPrerequisiteTests` (51920) pin two of them. 51900 is unused everywhere.

The 1.10.0 launch kit (`Reference/Work in progress 2026-10-03/1.10.0-LAUNCH-KIT.md`) reserves numbers only in 51700-51799; it names nothing in 519xx.

## Diagnostic codes (strings)

Constants in `src/Etp.Reporting.Import/Service/ServiceInterimFamilies.cs` (`ServiceInterimFamilies.Codes`).

| Code | Severity | Meaning | Raised by |
|---|---|---|---|
| `FAMILY_DERIVED` | Not needed | S001 RepairRegister is the builder's union of the ten status views; it is not imported | L3 |
| `SERVICE_FAMILY_NOT_NEEDED` | Not needed | S005 (tender summary) and S038 (retired SRN report name) are not needed (Owner) | L3 |
| `SERVICE_FAMILY_DEFERRED` | Not needed | S027 (TAT) and S028 (technician productivity) are deferred to P8 | L3 |
| `SERVICE_SNAPSHOT_DATE_NEEDED` | Refusal | a Service file could not be dated; put it in a folder whose name ends with the date, or set the snapshot date | L3 |
| `SERVICE_SNAPSHOT_DATE_DIFFERS_FROM_HISTORY` | Information | the folder date differs from the latest `Snapshot_As_Of` (S006, S009, S010); never refuses | L3 |
| `SERVICE_STORE_DEFAULTED` | Information | no store column and no sibling store: the Service store AW330 was used | L3 |
