# Self-diagnostics (1.11.0): error register, health check and the fix loop

Approved design, condensed. Approved by Sagar on 11 Oct 2026 (decisions 33 and 36: all 16 open questions closed). The full working design with live evidence is kept outside the repository. Procedure and JSON contract: [SELF-DIAGNOSTICS-PROCEDURES.md](SELF-DIAGNOSTICS-PROCEDURES.md). User view: USER-GUIDE "Health check and Error register". Operations view: OPERATIONS "Health check and exports".

Migrations: **0054_error_register.sql** and **0055_health_check.sql** (1.10.1 uses 0052 and 0053). New schema `diag`, owned by `dbo`, no schema grant; access only through `dbo`-owned `diag.*` procedures. THROW numbers 51800-51819 (register) and 51820-51839 (health check); each needs a curated message in `DesktopFriendlyError.ProductErrors`.

## 1. What it is

1. **Error register.** Every Error or Critical diagnostics entry is grouped by a fingerprint into one register entry with first and last seen, count, app versions, sample Refs and a status. It lives in the database so that the errors of every Windows account (Owner, Store Managers, automation) are in one place. No customer data, no message text.
2. **Health check.** 51 read-only checks in 7 areas, run by the Owner on demand and by the automation task nightly. Each line: Pass, Warning, Fail, Known or Skipped, a plain explanation and the screen that fixes it.
3. **Fix loop.** Each run writes a privacy-checked JSON export. The weekly audit turns Fails, New and Recurred entries into numbered fix tickets; Sagar approves; fixes go through code-only lanes and the gate; the release manifest marks the register entry Fixed; a repeat after the fix turns it Recurred. Claude never changes the software or the database unattended.

Goals: one list of every distinct error and whether a fix held; one button and one nightly run that prove every function; every finding says what is wrong and where to fix it; a machine-readable export; strictly read-only towards business data; rare warnings (Known with reason and expiry).

Non-goals: no automatic repair; no unattended code change; no telemetry leaves the PC; no exception text ever; not a performance monitor; no full DBCC CHECKDB in the nightly run; not a replacement for the gate.

## 2. Error register

**Included:** Error and Critical entries only. Warnings are counted per event code in the export (decision Q7).

**Fingerprint (version 1):** SHA-256 of `v1|EventBase|SubCode|Source|Site|ExceptionType|SqlNumber`.

- EventBase: event id up to the first `.` or `:`. SubCode: text after `:`; a `.SQLn` suffix feeds SqlNumber instead.
- Site: innermost `Etp.` stack frame, normalised (state machines reduced to the method, lambdas to the enclosing method, generic arity and namespace dropped). Empty for entries written before 1.9.9.
- SqlNumber: first SQL error with class 11 or more, else the `.SQLn` number, else 0.
- Line numbers, report code, store code, date, operation, Ref and HResult are context per occurrence, not identity.
- Weakness: renaming a failing method changes the fingerprint. Remedies: the Owner's Merge action, the audit proposing merges, and a soft match that attaches an old entry without a stack to exactly one existing fingerprint.

**Ingest.** Every ETP process ingests its own Windows account's log: the desktop app once after a successful start (background), the health run, and the register screen's Refresh. A cursor file is only an optimisation; the database ignores duplicates (unique Ref and time). At most 500 entries per call, oldest first. The procedure re-validates every token and counts rejected items. Occurrences store the reporter's role, not the account. More than 200 occurrences of one fingerprint per day keep only the counter. An ingest failure is logged as `ERROR_REGISTER_INGEST_FAILED`, never shown as a dialog.

**Statuses:** New; Seen (looked at, or a ticket linked); Expected (Owner only, reason required, can be undone); Fixed in x; Recurred after fix. Merge moves counts into another entry.

**Fixed and Recurred.** A release ships `ErrorFixManifest.json` (`fingerprint`, `fixedIn`, `ticket`). The first run of that version applies it through `diag.apply_error_fix_manifest`, only when the running version is at least `fixedIn`. An Owner fallback "Mark fixed in version" covers fixes that shipped before the manifest existed. On ingest, a Fixed entry seen again with an application version at or above the fixed version becomes Recurred; a lower version is only counted ("also seen on 1.x, not upgraded"). A Fixed entry with no recurrence for 30 days and 5 nightly runs on the fixed version is exported as Verified.

**Legacy seed (decision Q9, yes):** the first manifest marks the five entries found in the 1.9.2 and 1.9.6 logs: REPORT_STORE_REQUIRED fixed in 1.9.7, CASH_BOOK_FAILED 1.9.7, MANAGEMENT_TREND_REPORT_FAILED 1.9.8, USER_ACCESS_SAVE_FAILED (SqlException) 1.9.3, and USER_ACCESS_SAVE_FAILED (ArgumentException) Expected.

## 3. Health check

Business date checked: yesterday, in manual and nightly runs. Rules against crying wolf: a data gap is reported once by the check that owns it (the others say Skipped, see that line); a report that completes with a data status is a Pass, only an exception or a 30 second timeout fails it; sub-results are rolled up; unchanged warnings say "unchanged since".

| Area | Checks |
|---|---|
| Database (5) | DB-01 reachable; DB-02 migrations complete (checksums match); DB-03 quick integrity (suspect pages, page verify, last good CHECKDB within 35 days); DB-04 database options (AUTO_CLOSE, state, read-write); DB-05 size headroom |
| Security (3) | SEC-01 role permission matrix against the expected matrix; SEC-02 role membership; SEC-03 Owner grant option |
| Reports (22) | RPT-01 to RPT-22, one per catalogue report, through the same query classes the Reports screen uses; rows are counted and dropped; Pass under 10 s, Warning 10 to 30 s or more than 3 times the 7-run median, Fail on exception or timeout |
| Imports (3) | IMP-01 all 72 export families still recognised from built-in synthetic samples; IMP-02 recent import failures; IMP-03 automatic import state |
| Data (5) | DATA-01 yesterday is complete; DATA-02 sales against tender; DATA-03 closing-stock snapshot freshness; DATA-04 Service freshness; DATA-05 open critical data-quality issues |
| Operations (9) | OPS-01 last verified backup; OPS-02 recovery drill; OPS-03 scheduled tasks; OPS-04 disk space; OPS-05 protected folder permissions; OPS-06 SQL Server build; OPS-07 unattended runs; OPS-08 backup and drill failure logs; OPS-09 Windows event log |
| Errors (4) | ERR-01 register ingest; ERR-02 new errors; ERR-03 recurred after fix; ERR-04 crash-like errors |

**Known:** a Warning accepted by the Owner for a check id and finding code, with reason and end date of at most 90 days (decision 33). Greyed, not counted, not ticketed, never hides a Fail.

**Skipped, never Fail, for what cannot be read** (folder permissions and the Windows log need the nightly run's account; CHECKDB needs sysadmin, so DB-03 reads the last good date). **CHECKDB (decision Q6, yes):** the monthly recovery drill, which runs elevated, adds `DBCC CHECKDB WITH PHYSICAL_ONLY` so DB-03 has a fresh date each month. **SQL Server build (Q15):** OPS-06 is a Warning while the engine is on an RTM build without a cumulative update.

**Run order and budget.** Database, Errors, Operations, Security, Data, Imports, Reports. Caps: 10, 10, 15, 5, 10, 10 and 60 seconds; 30 seconds per report; 120 seconds in total. When the total is reached the rest are Skipped (time budget) and the run is still recorded and exported. DATA-02 reuses RPT-16's result; the lane notes record how the order is reconciled.

**Read-only proof.** Every inline SQL text passes `ReadOnlySqlGuard`; report probes never call the view, so no audit row is written; a write-detector integration test compares every `dbo` table before and after a full run; a reflection test forbids probes from referencing persistence types; the nightly account cannot change schema or grants anyway.

**Expected permission matrix.** `ExpectedRolePermissions.json` is generated by a gate test from a fresh database with all migrations applied. A migration that changes a grant fails the gate until the file is regenerated and the CHANGELOG names the change (decision Q16, yes). Entries can carry a decision id and "absent": the staff-targets rule (Owner only; migration 0051 in 1.10.0 removed the Store Manager grant) is the first.

## 4. Data model

`diag.error_register`, `diag.error_register_versions`, `diag.error_occurrences` (migration 0054); `diag.health_check_runs`, `diag.health_check_results`, `diag.health_check_known` (migration 0055). Procedures: `ingest_error_occurrences`, `load_error_register`, `load_error_entry`, `set_error_status`, `merge_error_entries`, `apply_error_fix_manifest`, `record_health_check_run`, `load_health_check_runs`, `load_health_check_results`, `load_health_check_summary`, `load_health_check_known`, `set_health_check_known`, `clear_health_check_known`, `load_role_permissions` (`WITH EXECUTE AS OWNER`, fixed result shape, the four `etp_*` roles only). Details and grants: the procedures document.

| Capability | Owner | Store Manager | Viewer | Automation |
|---|---|---|---|---|
| Ingest own errors | yes | yes | yes | yes |
| Read register, runs, results | yes | no | no | yes |
| Change status, merge, mark Known | yes | no | no | no |
| Apply fix manifest, record a run | yes | no | no | yes |
| Latest nightly summary (dashboard tile) | yes | yes | yes | yes |

Retention (decision Q14, yes): runs 400 days, occurrences 180 days (newest 20 per entry always kept), exports 90 days, register entries for ever. Migration text is checksummed: once 0054 or 0055 has run on any real database it is frozen.

## 5. Nightly run

Inside the five-minute automation task, after the import step: due when local time is 02:30 or later and no Nightly run exists for yesterday, so a PC that was off catches up at its first tick (decision 33). It runs whether or not watch-folder import is enabled, under the same automation lease. A manual run during a nightly run is refused. A health Fail does not fail the task; a run that could not complete logs `HEALTH_RUN_FAILED`.

## 6. Export

`%ProgramData%\EtpReporting\HealthExports` (decision 33), readable and writable by each active Owner's Windows account and the automation account. One file per run, `health-yyyyMMdd-HHmmss-<Manual|Nightly>.json`, schema `etp-health-export/1`: run, results, change against the previous run, register, warning counts, privacy block. `HealthExportPrivacyGuard` allows only tokens, dates, versions, numbers and template-built explanations; anything else is replaced by "redacted" and counted. Not exported: account names, file names, paths, exception text, report rows. The support package adds the newest 14 exports.

## 7. Screens

Settings → Database, Owner only: **Health check** (Run, Cancel, grouped lines with Where to fix, Compare with, Mark as Known, Open export folder; confirmation between 09:00 and 21:00, decision Q13) and **Error register** (Find Ref, status filter, details with latest stack, versions and 20 occurrences, Mark seen, Mark expected, Link ticket, Mark fixed in version, Merge, Copy summary). Store Managers and Viewers see only a dashboard tile with last night's result.

## 8. Privacy

No exception text; stored context limited to report code, one store code, dates, fixed operation text, method names, exception type names, SQL numbers, version, Ref and role; report probes count rows and discard them; synthetic import samples (store `ZZSYN`, item `SYN-0001`, dates in 2001) never reach disk or the database; no account names in exports; nothing leaves the PC automatically.

## 9. Fix loop

The weekly audit's Part 0 reads the week's exports and appends rows to `FIX-TICKET-REGISTER.md` outside the code worktrees (decisions Q5, Q11). It may change only the Status cell of existing rows. Ticket rule (decision Q12): always for any Fail, any New Critical and any Recurred; for a New Error entry with two or more occurrences or seen on two days; for a Warning only if it was present on all 7 nights and is not Known; never for Known or Expected. Statuses: PROPOSED, APPROVED, WONTFIX, DUPLICATE, IN-LANE, FIXED, VERIFIED, RECURRED. Shipping, pushing and live upgrades remain Sagar's.

## 10. Decisions

- **33 (11 Oct 2026):** nightly run at 02:30 with catch-up at any time; Known per item for up to 90 days; HealthExports folder under ProgramData; weekly audit keeps the ticket register; health screens Owner only plus a one-line tile.
- **36 (11 Oct 2026):** CHECKDB PHYSICAL_ONLY in the monthly drill (Q6); only Error and Critical in the register (Q7); seed the five legacy entries (Q9); register file in Reference (Q11); warning tickets only after 7 nights and not Known (Q12); manual runs allowed during shop hours with a confirmation (Q13); retention 400/180/90 days (Q14); SQL RTM is a Warning (Q15); permission matrix friction accepted (Q16). Q10 resolved: migration 0051 shipped in 1.10.0. Q8 moot: no ETP at the shop under the C+ plan.
