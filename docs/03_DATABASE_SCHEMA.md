# SQL Server Express Canonical Schema

## Design principles

- Canonical facts are separate from raw source metadata and staging data.
- Every fact retains batch, file and source-row lineage.
- Monetary values use `decimal(19,4)`; quantities use `decimal(19,4)` unless real samples prove integer-only semantics.
- Business dates use `date`; import timestamps use `datetime2(3)` in UTC.
- Natural source identifiers are retained, while surrogate `bigint` keys support stable joins.
- Mappings and business rules are versioned/effective-dated.

## Foundation tables

### `schema_migrations`

`migration_id varchar(100)` primary key, `checksum char(64)`, `applied_utc datetime2(3)`.

### `stores`

`store_id int identity` primary key; unique `store_code varchar(30)`; `store_name nvarchar(200)`; nullable `business_unit_id`; `is_active bit`.

### `business_units`

`business_unit_id int identity` primary key; unique `business_unit_code varchar(30)`; name and active flag.

### `brands`, `categories`, `subcategories`, `collections`

Surrogate integer key, unique controlled code, display name, active flag. Child tables carry the appropriate foreign key only when the hierarchy is confirmed by source/master data.

### `products`

`product_id bigint identity` primary key; unique `product_code nvarchar(100)`; description; nullable brand/category/subcategory/collection foreign keys; active flag; created/updated timestamps.

### `staff`

Optional reporting dimension: surrogate key, controlled staff code, display name, store association and active dates. Do not populate from unapproved free-text names.

### `transaction_types`

Controlled code and classification (`Sale`, `Return`, `TransferIn`, `TransferOut`, `Adjustment`, etc.), sales sign, stock sign and effective dates. Sign semantics require business approval.

## Import and lineage

### `import_profiles`

Profile ID/version, report type/code, layout version, sheet matcher, header-row strategy, normalized header-signature hash, active/effective dates and source provenance.

Unique constraint: `(report_code, layout_version, profile_version)`.

### `import_profile_fields`

Profile FK, canonical field, source header, datatype, required flag, transform code, default value and ignored flag. Unique `(import_profile_id, canonical_field)` and `(import_profile_id, normalized_source_header)` where applicable.

### `import_batches`

Batch ID, status, started/completed timestamps, initiating user, store/period, profile set version, row counts and control totals.

### `import_files`

File ID, batch FK, original name, SHA-256, size, report code, profile FK, sheet, source period, row count and status.

Duplicate candidate unique index: `(sha256, report_code, store_id, period_start, period_end)` after semantics are confirmed.

### `import_errors`

Batch/file FK, optional source row/column, severity (`Blocker`, `Warning`, `Information`), stable code and safe message. Raw customer data must not be copied into messages unnecessarily.

### Staging

Use profile-specific typed staging tables or bulk-load table types for the first vertical slice. Staging rows include `import_file_id` and `source_row_number` and are deleted/archived by a defined retention policy. Do not create one permanent production table per workbook.

## Sales facts

### `sales_transactions`

`sales_transaction_id bigint identity` primary key; store FK; `transaction_date date`; source transaction/invoice number; transaction-type FK; optional staff FK; batch/file/source-row lineage; gross, discount, tax and net totals; currency code.

Candidate unique key must be derived from real ETP identifiers. Until approved, detect potential duplicates and block ambiguous re-import rather than inventing a key.

### `sales_lines`

Line ID; transaction FK; optional source line number; product FK; quantity; gross, scheme discount, user discount, tax and net amounts; batch/file/source-row lineage.

Indexes initially target `(transaction_date, store_id)`, transaction FK, product FK and lineage FKs.

### `sales_tenders`

Transaction FK, controlled tender type, amount and lineage. This normalizes R022 payment measures instead of adding one column per future tender type.

## Stock facts

### `stock_snapshots`

Snapshot ID; store/product/location; snapshot date/type (`Opening`, `Closing`, other approved); quantity; value; batch/file/source-row lineage. Unique candidate `(store_id, product_id, stock_location_id, snapshot_date, snapshot_type, import_file_id, source_row_number)`.

### `stock_movements`

Movement ID; store/product/location; movement date; transaction-type FK; signed quantity/value; source document identity; optional linked sale line; lineage.

Stock closing is calculated from approved snapshots and movements, not stored as an unexplained mutable total.

## Reporting configuration

### `business_rule_versions`

Stable rule code, version, effective dates, status, human-readable definition and implementation checksum/reference.

### `report_definitions`

Stable report ID/name, version, purpose, parameter schema, result schema and active flag. Executable SQL/application code remains version-controlled; the table identifies the active approved definition rather than storing arbitrary SQL entered by users.

## Relationships

```mermaid
erDiagram
  IMPORT_BATCHES ||--o{ IMPORT_FILES : contains
  IMPORT_FILES ||--o{ IMPORT_ERRORS : records
  IMPORT_PROFILES ||--o{ IMPORT_PROFILE_FIELDS : maps
  IMPORT_PROFILES ||--o{ IMPORT_FILES : identifies
  STORES ||--o{ SALES_TRANSACTIONS : owns
  SALES_TRANSACTIONS ||--o{ SALES_LINES : contains
  SALES_TRANSACTIONS ||--o{ SALES_TENDERS : paid_by
  PRODUCTS ||--o{ SALES_LINES : sold_as
  STORES ||--o{ STOCK_SNAPSHOTS : measures
  PRODUCTS ||--o{ STOCK_SNAPSHOTS : measures
  STORES ||--o{ STOCK_MOVEMENTS : records
  PRODUCTS ||--o{ STOCK_MOVEMENTS : moves
  IMPORT_FILES ||--o{ SALES_TRANSACTIONS : sourced
  IMPORT_FILES ||--o{ SALES_LINES : sourced
  IMPORT_FILES ||--o{ STOCK_SNAPSHOTS : sourced
  IMPORT_FILES ||--o{ STOCK_MOVEMENTS : sourced
```

Final nullability, unique keys, hierarchy and signs remain subject to real ETP samples and approved business definitions.

## Phase 5 accounting additions — 17 September 2026

Migration 0029 adds nullable `accounting_batches.approval_reason nvarchar(1000)`; historical reasons are not fabricated. Approval persists the supplied reason with its status/actor/time. Mapping request, decision and replacement run in one transaction.

Migration 0030 adds `rejection_reason nvarchar(1000)`, `rejected_by nvarchar(200)` and `rejected_utc datetime2`, plus Owner-only `dbo.reject_accounting_batch`. Rejection and audit are atomic; DRAFT/REVIEW/APPROVED are eligible, EXPORTED/REJECTED are not. The original approval fields remain intact. The planned status renaming and invoice/export-receipt tables are still pending.

## Phase 7 Tally foundation — migration 0038

Additive only: no existing status value, constraint, index or trigger changes. Every new table is Owner-only (DENY to `etp_store_manager` and `etp_viewer`). THROW numbers 51570–51579; see `docs/audit/PHASE-7-REPORT.md`.

| Table | Purpose |
|---|---|
| `tally_profiles` | One Tally company: name, TEST/PRODUCTION, loopback-only endpoint, and the D13/D14/D16/D17 policy columns. CHECKs refuse a remote endpoint, named customer ledgers with tender inside the voucher, a single-ledger policy without its ledger, and live books enabled without who enabled them. |
| `tally_profile_stores` | Which stores a profile covers; a store binds to at most one TEST and one PRODUCTION company. |
| `accounting_vouchers` | One planned Tally voucher; its `correspondence_key` must equal `ETP:{store}:{invoice_year}:{document_number}:{component_role}:{revision}`. A number containing `:`, `|` or white space can be stored only as BLOCKED/EXCLUDED. Immutable once the batch leaves DRAFT/BLOCKED, except `voucher_status`/`blocked_reason`. |
| `accounting_voucher_reservations` | One active reservation per (profile, store, year, document number, role, revision). Never deleted; released once, automatically when a batch is rejected or a voucher is blocked/excluded before any attempt. |
| `accounting_status_history` | Append-only; one row per batch or voucher status change, written by trigger. Existing batches get one starting row at upgrade. |
| `tally_artifacts` | Append-only register of evidence files (kind, relative path, SHA-256, length). |
| `tally_attempts` | One file write or send; may only advance its outcome from RECORDED or SENT; never deleted. |

`accounting_batches` gains `tally_profile_id`, `batch_kind` (`DAY_JOURNAL` for every existing row, or `SALES_VOUCHERS`, which needs a profile), `selection_json`, `selection_version`, `mapping_version_set_json` and `manifest_sha256`. `accounting_entries` gains `accounting_voucher_id` (same batch as the voucher), `tax_rate`, `quantity` and `stock_item`. `product_settings` gains `tally_evidence_root`.

## Phase 7 findings, read-backs and reconciliation — migration 0039

Additive and Owner-only, like 0038.

| Table | Purpose |
|---|---|
| `accounting_validation_findings` | WARN and FAIL results of the validation rules for a batch or one of its vouchers. Never deleted; a WARN can be accepted once, with who, when and why. |
| `tally_readbacks` | One read-back: Tally company as Tally reported it (never filled in from the request), date range, voucher count, complete or the reason it is not, and the stored file. Append-only. |
| `tally_actual_vouchers`, `tally_actual_ledger_entries` | What the read-back says Tally holds, field by field. Fields Tally did not return stay NULL. Append-only. |
| `tally_reconciliation_runs` | One comparison of a batch with a read-back, its outcome, a summary and its evidence file. Append-only; a later run never changes an earlier one. |
| `tally_reconciliation_differences` | One row per failed or warning check, with values A/B/C, delta, rule, rationale and the fixed required action. Never deleted; a WARN can be accepted once. |

## Phase 7 review fixes — migration 0040

No new tables. The finding and difference guards no longer treat an UPDATE that changed no row as a delete. A voucher of a decided batch can no longer go back to PLANNED, BLOCKED or EXCLUDED, and a BLOCKED or EXCLUDED one stays so (51212). `tally_readbacks.incomplete_reason` also allows `PERIOD_MISMATCH`: the file's own period does not contain the dates entered for it.

## Service Centre interim — migration 0048

`0048_service_centre_interim.sql` (Service review step S-2, decisions 15 and 16, 3-4 Oct 2026; release 1.9.5). Additive and idempotent, in one transaction; it must apply after 1.9.4's 0046/0047. 0049 is reserved for the Tally cost-centre migration of GitHub PR #3 (renumbered from its 0041, decision 18), and 1.10.0 numbers its own migrations from 0050. The design is `SERVICE-INTERIM-DESIGN.md` (`Reference\Work in progress 2026-10-03\service\`). The script has three sections, each with one owner, in this order:

**A_SERVICE_STORE.** `business_units` row `SERVICE` ("Service Centre"); `stores` row `AW330` ("Service Centre AW330") with that business unit and `is_active = 0`, inserted only when missing (an existing AW330 row is never updated). Trigger `trg_stores_service_unit_inactive` (AFTER INSERT, UPDATE on `dbo.stores`) THROWs 51900 when a SERVICE-unit store is made active, and 51904 when a SERVICE-unit store is moved out of the SERVICE business unit (`business_unit_id` changed or set to NULL). Settings > Stores explains both refusals in plain words. AW330 stays inactive because an active store would need an R025 import for the combined Retail date, get its own daily pack and join the DSR and evening store lists. Retail stores keep a NULL business unit. `import_files.store_code` has no foreign key, so Service files need no other store change; there is no `daily_reporting_days` row for AW330, so the landing triggers' day lock never applies to Service (no Service day locking).

**B_SERVICE_LANDING.** Generated from the catalogue by `scripts/service-centre/generate_landing_sql.py`; never edit it by hand. 36 tables `dbo.etp_landing_snnn` (35 consolidated families plus S041 GPRC CLAIM) in the 0018 shape: `etp_row_id bigint IDENTITY` primary key, `import_file_id` (FK `import_files`), `source_lineage_id` (unique, FK `source_lineage`), `content_key varchar(80)`, then one column per catalogue column (Text and Identifier `nvarchar(max)`, Decimal `decimal(19,4)`, Date `date`, Integer `int`), with the canonical names the catalogue holds. The generator reads `src/Etp.Reporting.Import/Profiles/EtpReportFamilies.json` (the S entries), not the spec; `scripts/service-centre/families.spec.json` is the frozen origin of those names, which the catalogue copies, and `ServiceLandingMigrationTests` compares this section with the catalogue. Each table has `IX_etp_landing_snnn_file`, the 0018 locked-day trigger and `append_etp_landing_snnn` with the 0025 guards (role 51420; transaction, file and report-code checks 51422, `report_code = 'Snnn'`), which also writes `dbo.etp_import_content`. DENY INSERT, UPDATE, DELETE to `etp_store_manager` and `etp_viewer`; GRANT EXECUTE on the procedure to `etp_store_manager` and `etp_owner`. `promote_import_superset` needs no Service block: its content-key check covers untyped families.

Every Service file is one dated snapshot ("reading"): `import_files.period_end` is the folder date of a consolidated workbook or the window end in a raw export's name. Rows are never updated or deleted; a new refresh adds a new reading beside the old ones.

| Table | Family | Read rule |
|---|---|---|
| `etp_landing_s002` | Job report (booking) | job list |
| `etp_landing_s003` | Revenue report | date log (Trans Date) |
| `etp_landing_s004` | Tender collection (detailed) | date log (BillingDate) |
| `etp_landing_s006` | Closing stock (spares) | state snapshot |
| `etp_landing_s007`, `etp_landing_s008` | Purchase register, created / received | date log (GRN_DATE) |
| `etp_landing_s009` | Pending repair | state snapshot |
| `etp_landing_s010` | Pending delivery | state snapshot |
| `etp_landing_s011` | SRN status report | job list |
| `etp_landing_s012` | SRN history | job list |
| `etp_landing_s013` | Goods in transit | date log (STM Date) |
| `etp_landing_s014` to `etp_landing_s018` | Repair register DC, IR, RA, RWR, DELIVERED | job list |
| `etp_landing_s019` | Repeat return | date log (RepairDate) |
| `etp_landing_s020`, `etp_landing_s021` | Replacement, depreciation | job list |
| `etp_landing_s022` | Empowerment report | date log (INVOICE DATE) |
| `etp_landing_s023` to `etp_landing_s026` | GPRC, MB, WDC, WRA claims (old format) | date log (TransDate) |
| `etp_landing_s029` | Deftran report | date log (Repair Date) |
| `etp_landing_s030` | MIS export grid | job list |
| `etp_landing_s031` to `etp_landing_s035` | Repair register PD, PR, SRN, REPAIRED, SRNINV | job list |
| `etp_landing_s036`, `etp_landing_s037` | Delivery report, repair report | job list |
| `etp_landing_s039`, `etp_landing_s040` | WD claim, WRA claim | date log (Transaction Date) |
| `etp_landing_s041` | GPRC CLAIM (raw export, decision 16 Q6; no consolidated workbook) | date log (date part of Transaction Date) |

No table exists for S001 (derived), S005 and S038 (not needed) or S027 and S028 (deferred to the full Service import, P8).

**C_SERVICE_READ.** Read-only views; each has GRANT SELECT to `etp_viewer`, `etp_store_manager` and `etp_owner`, and none exposes a customer phone, e-mail or address column. They read only live Service files (`is_superseded = 0`, `data_truth_version = 1`) and choose rows by each family's read rule, never by import order:

- *state snapshot* (S006, S009, S010): the rows of the reading with the latest snapshot date;
- *date log*: for each business date, the rows of the latest reading whose window (earliest row date to snapshot date) contains that date, so a 4-day raw export replaces only its 4 days and a restated row replaces its old version;
- *job list*: per family and job, the rows of the latest reading that holds the job.

Helper views `v_service_families` (read rule, date column, list label and lifecycle rank per importable family), `v_service_reading_windows` (each live reading's window), `v_service_datelog_readings` (the date-log ranking: reading_rank 1 wins a date), `v_service_status_view_rows` (line rows of the ten status lists) feed the views below.

| View | What it holds |
|---|---|
| `v_service_readings` | One row per Service import file: report code, snapshot date, window start, import file, rows, import time, source kind (CONSOLIDATED or RAW) and `is_latest` per family. The refresh log, and the base of the growth check. |
| `v_service_job_readings` | Job, report code, snapshot date, import file and status date for every job list and for S009/S010. The base of the next three views. |
| `v_service_job_status_current` | One row per job: the current status (the status list whose winning reading is latest, ties broken by the lifecycle rank), money summed over the line rows (spare value, labour), line count and how many other lists held the job. |
| `v_service_pending_current` | Pending repair (S009) and pending delivery (S010) from the latest snapshot with age in days, plus SRN status (S011). |
| `v_service_job_list_events` | History of jobs in lists: FirstSeen, Reappeared, LeftList and StillListed, for S009/S010 and for the job lists. Information only; it writes nothing and creates no review item. |
| `v_service_s004_daily` | S004 tender amount per business date and tender (CASH, CARD, UPI with BharatPe and PhonePe, CHEQUE, RTGS, ADVANCE), from the winning reading. Columns `business_date, tender, amount, row_count, snapshot_date, import_file_id`. |
| `v_service_money_changes` | For S003 and S004, each business date whose total differs between the winning reading and the previous covering reading (or the reading it restated), with both snapshots and amounts. |
| `v_service_gprc_claims` | GPRC claim lines from S023 (consolidated history, up to 5 Aug 2026) and S041 (raw GPRC CLAIM, from 1 Aug 2026), each first chosen by its own date-log rule. S041 wins per claim document: an S023 line is kept only when no winning S041 line has the same document number (an S023 line with no document number only on a date with no S041 line), so the 1-5 Aug overlap is counted once. No customer column. |
| `v_service_manual_money` | The manual `SERVICE_*` entries of `dbo.manual_operational_inputs` per shop, date and field, with the tender (CASH, CARD, UPI; NULL for `SERVICE_WDC`) and `is_service_money_shop`, which marks the Titan World shop (WLMHW) where all of the Service centre's money is entered (decision 16, Q1). The store code is written in this view, not in C#. |

`SqlServerServiceReportQuery` reads these views for the four Service screens. The money check (decision 16, rules in `ServiceMoneyCheck`) compares S004 CASH, CARD and UPI with the Titan World shop's `SERVICE_CASH`, `SERVICE_CARD` and `SERVICE_UPI` entries by bill date and per tender, and shows the difference (S004 minus manual); nothing is corrected. Entries at any other shop are returned apart, for the "Service entries at other shops (not added)" grid, and never summed. `SERVICE_WDC` is not compared, and no advance is deducted (advances are 0; a non-zero S004 ADVANCE, CHEQUE or RTGS amount is shown without a manual side).

SQL error numbers 51900–51929 are the Service block (`docs/service-centre/SERVICE-INTERIM-NUMBERS.md`); 0048 uses 51900 and 51904 only. Storage: about 35k landing rows per weekly consolidated reading plus small daily raw readings; measure it with `scripts/service-centre/measure-service-growth.sql` (`docs/OPERATIONS.md`).

## Permissions: staff targets Owner-only — migration 0051

`0051_staff_targets_owner_only.sql` (decision 27, Sagar, 10 Oct 2026, extends D22; release 1.10.0). Only the Owner writes `dbo.staff_sales_targets`. It revokes the 0022 `GRANT INSERT,UPDATE` from `etp_store_manager` and adds `DENY INSERT,UPDATE,DELETE` to `etp_store_manager` and `etp_viewer`, the same shape as `approval_requests` and `controlled_adjustments` in 0022. Both roles keep SELECT through the schema grant. The Owner (`db_owner` + `etp_owner`) is not affected. The trigger `trg_staff_sales_targets_audit_lock` still writes `dbo.staff_sales_target_history` through the dbo ownership chain, so the history table has no grant and needs none. The automation account is a member of `etp_store_manager` but never writes staff targets. The screen and `SqlServerDailyWorkflowService.SaveStaffTargetAsync` have refused non-owners since 1.9.9.
