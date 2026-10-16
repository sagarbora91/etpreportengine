# Self-diagnostics procedures (1.11.0) - contract between the SQL lane and the store lane

Source: `SELF-DIAGNOSTICS-DESIGN-1.11.0.md` section 6 (migrations renumbered: **0054_error_register.sql**, **0055_health_check.sql**). The SQL lane (L1) implements these; the store lane (L3) calls them (`SqlServerErrorRegisterStore`, `SqlServerHealthCheckStore`, implementing `IErrorRegisterStore` / `IHealthCheckStore` in `src\Etp.Reporting.Application\HealthCheck\HealthCheckContracts.cs`).

General rules
- Schema `diag`, owned by `dbo`, no schema grant. Every procedure is `diag.*`, owned by `dbo`, `SET NOCOUNT ON; SET XACT_ABORT ON;`.
- THROW numbers: **51800-51819** error register, **51820-51839** health check. Each needs a curated row in `DesktopFriendlyError.ProductErrors` (L8).
- JSON in: `nvarchar(max)`, parsed with `OPENJSON ... WITH (...)`; property names are camelCase exactly as below. Dates `yyyy-MM-dd`; times ISO 8601 UTC `yyyy-MM-ddTHH:mm:ss.fffZ`. Enum values are the C# enum names (`New`, `Warning`, `Nightly`, ...).
- Result sets out: column names snake_case as below, in this order.
- Tokens are validated with the `SafeToken` allow-list: `NOT LIKE '%[^-A-Za-z0-9._:<>+]%'` (hyphen first: with it last, `+-]` in an N'' pattern is the range + to ], 1.11.0 gate 2) (event codes `NOT LIKE '%[^A-Z0-9_]%'`). Free text (reasons) uses the existing safe-text check.
- Version key (sortable bigint): `major*10^9 + minor*10^6 + build*10^3 + revision` (missing parts = 0).
- Caller role: `IS_ROLEMEMBER('etp_owner')` -> Owner, else `etp_automation` -> Automation, else `etp_store_manager` -> StoreManager, else `etp_viewer` -> Viewer, else Unknown.

## Migration 0054 - error register

| Number | Meaning |
|---|---|
| 51800 | Malformed ingest batch (not JSON, not an array, more than 500 items, bad `@app_version`) |
| 51801 | Owner only |
| 51802 | A reason is required (Expected, manual Fixed, merge) |
| 51803 | Text is not safe (reason / ticket / version) |
| 51804 | Malformed fix manifest |
| 51805 | Entry not found / invalid status transition / merge into itself or a merged entry |

### diag.ingest_error_occurrences
`@items nvarchar(max)`, `@app_version varchar(20)` - EXECUTE: etp_owner, etp_store_manager, etp_viewer, etp_automation.

`@items` (at most 500, oldest first):
```json
[ { "fingerprint": "<64 lowercase hex>", "fingerprintVersion": 1,
    "eventBase": "REPORT_STORE_REQUIRED", "subCode": "", "source": "Reports.Workspace",
    "site": "SqlServerApplicationReportQuery.LoadStaffPerformanceAsync",
    "exceptionType": "System.InvalidOperationException", "sqlNumber": 0,
    "timestampUtc": "2026-10-09T10:15:02.123Z", "severity": "Error", "reference": "1A2B3C4D5E6F",
    "applicationVersion": "1.11.0.0", "reportCode": "staff", "storeCode": "ALL",
    "businessDate": "2026-10-09", "dateFrom": null, "operation": "RunReport",
    "stack": [ "SqlServerApplicationReportQuery.LoadStaffPerformanceAsync", "ReportsWorkspaceView.RunAsync" ] } ]
```
Behaviour: a bad item is skipped and counted `rejected`; upsert entry by fingerprint (max_severity, last_seen, last_version, occurrence_count, latest_stack = JSON of `stack`, at most 3400 chars); insert occurrence (duplicate `(reference, occurred_utc)` ignored -> `duplicates`); more than 200 occurrences of one fingerprint per UTC day keep only the counter; upsert `error_register_versions`; Fixed entry + occurrence version key >= fixed key -> Recurred (`recurred_utc`, `recurred_version`); prune occurrences older than 180 days keeping the newest 20 per entry.

Result set (one row): `accepted int, duplicates int, rejected int, new_entries int, recurred_entries int`.

### diag.load_error_register
`@status varchar(10) = NULL`, `@reference char(12) = NULL` - EXECUTE: etp_owner, etp_automation.

Result set (one row per entry, merged entries excluded, newest `last_seen_utc` first):
`error_register_id, fingerprint, fingerprint_version, event_code, sub_code, source, site, exception_type, sql_number, max_severity, status, first_seen_utc, last_seen_utc, occurrence_count, first_version, last_version, fixed_in_version, fixed_ticket, fixed_utc, recurred_utc, recurred_version, latest_stack (JSON array), status_reason, status_changed_utc, status_changed_by, merged_into_id`.

### diag.load_error_entry
`@error_register_id int` - EXECUTE: etp_owner (51801 otherwise). Three result sets:
1. the entry (columns as `load_error_register`; zero rows if not found);
2. versions: `app_version, occurrence_count, first_seen_utc, last_seen_utc` (by version key desc);
3. latest 20 occurrences: `error_occurrence_id, occurred_utc, reference, app_version, severity, report_code, store_code, business_date, date_from, operation, reporter_role, ingested_utc`.

### diag.set_error_status
`@error_register_id int, @status varchar(10), @fixed_in_version varchar(20) = NULL, @ticket varchar(12) = NULL, @reason nvarchar(200) = NULL` - EXECUTE: etp_owner. `@status` in `Seen, Expected, New, Fixed`. Reason required for Expected and Fixed (51802). Sets `status_changed_utc`, `status_changed_by = ORIGINAL_LOGIN()` (as `modified_by` elsewhere). No result set.

### diag.merge_error_entries
`@from_id int, @into_id int, @reason nvarchar(200)` - EXECUTE: etp_owner. Moves occurrences and version counts, adds counts, widens first/last seen, sets `from.merged_into_id`. No result set.

### diag.apply_error_fix_manifest
`@manifest nvarchar(max)`, `@running_version varchar(20)` - EXECUTE: etp_owner, etp_automation.
```json
{ "manifest": 1, "fixes": [ { "fingerprint": "<64 hex>", "fixedIn": "1.11.1", "ticket": "FT-007" } ] }
```
For each line with `fixedIn` key <= running key: entries with that fingerprint in New / Seen / Recurred -> Fixed (`fixed_in_version`, key, `fixed_ticket`, `fixed_utc`). Idempotent. Result set (one row): `lines int, entries_marked_fixed int, already_fixed int, not_found int, skipped int`.

## Migration 0055 - health check

| Number | Meaning |
|---|---|
| 51820 | Malformed run JSON |
| 51821 | More than 80 results |
| 51822 | Owner only |
| 51823 | Known: until date in the past or more than 90 days ahead, or reason missing / not safe |
| 51824 | Run not found |

### diag.record_health_check_run
`@run nvarchar(max)` - EXECUTE: etp_owner, etp_automation. One transaction; prunes runs older than 400 days and expired Known rows.
```json
{ "trigger": "Nightly", "businessDate": "2026-10-09",
  "startedUtc": "2026-10-10T21:00:04.000Z", "completedUtc": "2026-10-10T21:00:45.250Z", "durationMs": 41250,
  "appVersion": "1.11.0.0", "machineName": "WORKPC", "runByRole": "Automation", "overall": "Fail",
  "counts": { "pass": 41, "warning": 5, "fail": 1, "known": 2, "skipped": 2 },
  "exportFileName": "health-20261010-210004-Nightly.json",
  "results": [
    { "checkId": "DB-04", "area": "Database", "outcome": "Warning", "findingCode": "AUTO_CLOSE_ON",
      "explanation": "AUTO_CLOSE is on for EtpReporting.", "fixTaskId": null, "durationMs": 12,
      "metricValue": null,
      "detail": [ { "key": "AX123", "outcome": "Warning", "findingCode": "EXPORTS_MISSING", "metricValue": 2,
                    "fields": { "report": "R022" } } ] } ] }
```
`detail` is stored as `detail_json` (at most 2000 chars; tokens and numbers only). Result set (one row): `health_check_run_id bigint`.

### diag.load_health_check_runs
`@top int = 30`, `@trigger varchar(8) = NULL` - EXECUTE: etp_owner, etp_automation. Newest first:
`health_check_run_id, trigger_kind, business_date, started_utc, completed_utc, duration_ms, app_version, machine_name, run_by_role, overall, pass_count, warning_count, fail_count, known_count, skipped_count, export_file_name`.

### diag.load_health_check_results
`@health_check_run_id bigint` - EXECUTE: etp_owner, etp_automation:
`check_id, area, outcome, finding_code, explanation, fix_task_id, duration_ms, metric_value, detail_json` (catalogue order).

### diag.load_health_check_summary
No parameters - EXECUTE: etp_owner, etp_store_manager, etp_viewer, etp_automation. Latest Nightly run, zero or one row:
`business_date, completed_utc, overall, pass_count, warning_count, fail_count, known_count, skipped_count`.

### diag.load_health_check_known
No parameters - EXECUTE: etp_owner, etp_automation. Unexpired rows: `check_id, finding_code, until_date, reason, set_by, set_utc`.

### diag.set_health_check_known / diag.clear_health_check_known
`set`: `@check_id varchar(10), @finding_code varchar(60), @until date, @reason nvarchar(200)` (upsert); `clear`: `@check_id, @finding_code`. EXECUTE: etp_owner (51822 otherwise). No result set.

### diag.load_role_permissions
No parameters, `WITH EXECUTE AS OWNER` - EXECUTE: etp_owner, etp_automation. Output limited to the four etp roles. Three result sets:
1. permissions: `role_name, class_desc, schema_name, object_name, permission_name, state_desc` (`state_desc` GRANT, DENY or GRANT_WITH_GRANT_OPTION; grants to `public` and to individual users are not listed);
2. memberships: `role_name, member_name, is_active_application_user bit, application_role_code, member_count int` - `member_name` filled (one row per member, `member_count` 1) only when the caller `IS_ROLEMEMBER('etp_owner') = 1`; otherwise NULL and grouped by the other columns;
3. owners without `ALTER ANY LOGIN WITH GRANT OPTION`: `member_name, owners_without_grant_option int` - `member_name` NULL for a non-Owner caller (one row with the count), the count repeated on every row.

### diag.load_schema_object_state (1.12.1)
No parameters, `WITH EXECUTE AS N'diag_catalog_reader'` (like `diag.load_role_permissions`) - EXECUTE: etp_owner, etp_automation. Object file `database/objects/40-diag.load_schema_object_state.sql`. Health check DB-07 calls it so the nightly run (automation account: no VIEW DEFINITION, no SELECT on `sys.sql_expression_dependencies`) can compare the objects with the release; before 1.12.1 DB-07 read the catalog itself and the nightly run was Skipped NOT_PERMITTED (VM acceptance, 16 Oct 2026). Two result sets:
1. journal: `object_name, object_type, file_sha256, definition_sha256` (`dbo.schema_objects`, through the ownership chain);
2. live objects: `object_name, type_code, definition` for every view, procedure and function of `dbo` and `diag` (no `pinned_by`: only setup needs it, and only db_owner may read `sys.sql_expression_dependencies`).
