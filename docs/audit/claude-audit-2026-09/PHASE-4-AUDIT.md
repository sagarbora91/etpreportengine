# Phase 4 — Claude audit

Auditor: Claude. Date: 16 September 2026. Branch: `phase-4/security-operations` at `df35d90`, four commits ahead of `main`, **not pushed to origin**.
Method: static audit of the branch against the plan and the security audit, plus verification of the migration interaction with Phase 1. The live acceptance items A4.2 to A4.5 need the shop PC's real ACLs, real scheduled tasks and a non-Express SQL edition, and are recorded as not run rather than guessed at.
The live `EtpReporting` database was never written to and `settings.json` was never modified. No ACL, scheduled task, certificate or signing operation was performed on this machine.

---

## 1. Verdict

**PHASE 4 REOPENED** — the implementation is the strongest of the three phases and much of it exceeds what the plan asked for, but it cannot be merged as it stands: its migration grants permissions on a table that Phase 1 deletes, and it grants no write access at all to the forty-odd tables Phases 1 to 3 introduce. Four of the six acceptance items also still need the shop PC.

The blocker is structural, not sloppy. Phase 4 was cut from `main`, which holds only Phase 0, so it was written against a schema that no longer reflects where the product is.

---

## 2. Acceptance A4.1 – A4.6

| ID | Result | Evidence |
|---|---|---|
| **A4.1** Viewer cannot reopen a day or delete audit rows; Store Manager cannot edit facts | **PASS in automated coverage; not run as a real login** | Migration 0022 carries the exact denials the criterion asks for: `DENY INSERT,UPDATE,DELETE ON dbo.operational_audit` to viewer and store manager, `DENY UPDATE,DELETE ON dbo.import_files`, `DENY INSERT,UPDATE,DELETE ON dbo.sales_lines` to store manager. Integration tests exercise these against a disposable database with impersonation, including a locked day rejecting a raw reopen, delete and key-move even with legacy writer permissions. **Not done:** the test as a real Viewer and Store Manager Windows login in SSMS. |
| **A4.2** No BUILTIN\Users on the three ProgramData folders | **NOT RUN** | Requires running `icacls` against the shop PC and changing its ACLs. The code is there and is stronger than `icacls /inheritance:r`: it protects the ACL, sets an Administrators owner, grants SYSTEM, Administrators and the SQL service, and strips explicit non-inherited entries from existing children. Nothing was executed. |
| **A4.3** Three tasks, same non-SYSTEM principal, a backup within 24 h | **NOT RUN, and currently blocked** | All three installers route through one registration helper using a single configured principal, which rejects SYSTEM, local service, network service, RID-500 and any Administrators member. But in the default strict mode the configuration loader throws, so **the three tasks cannot be installed at all** until the dedicated automation account question is decided. That decision is flagged in `docs/OPERATIONS.md` but not in the phase report. |
| **A4.4** Backup, drill restore and verify from the receipt-named file | **NOT RUN — blocked by SQL edition** | The backup script now requires `ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = EtpBackupCert)` and fails closed with no plaintext fallback. **SQL Server Express does not support backup encryption**, so on this instance the script refuses by design. The drill logic itself is sound: it reads the last verified receipt, rejects any path escaping the backup folder, re-hashes the file before and after, restores to an isolated database, runs `DBCC CHECKDB` and drops it. Rotation keeps 14 daily and 12 monthly. **A4.4 cannot be met on Express.** This needs an owner decision about edition. |
| **A4.5** Support package contains no customer data, paths or SQL | **PARTIAL** | Tests assert aggregate-only content and that a native SQL failure omits submitted values, but with synthetic sentinels. The criterion asks for a package taken after importing the real customer files. Not run. |
| **A4.6** Connection settings reject remote hosts and `Encrypt=False` | **PASS** | A single policy class now gates every connection. Only `.`, `(local)`, `localhost`, the machine name, `lpc:` and local named pipes are accepted; `Encrypt=False`, `AttachDbFilename`, `UserInstance` and `FailoverPartner` are rejected; the `TrustServerCertificate=True` default is replaced by `Encrypt=Optional`. Covered by table-driven tests including `Server=remotehost`, `Encrypt=False`, `Encrypt=no` and `tcp:`. |

---

## 3. Tasks 1 – 7

**Task 1 day lock — Done, and stronger than specified.** The plan allowed a trigger or a single granted procedure; both exist. The trigger is `AFTER UPDATE, DELETE` rather than `INSTEAD OF UPDATE`, which is a superset: it also blocks deletes and key moves and fires on any direct update. Owner role and non-empty reason are both enforced, and the reopening event is written in the same statement with the actor stamped from `SUSER_SNAME()`. A second trigger prevents forging the event separately. The client-side administrator flag and the Windows elevation check are genuinely deleted, with zero remaining references.

**Task 2 ACLs, rotation, encryption, custody — Done in code.** Rotation and AES-256 as above. The certificate action creates the certificate, exports it with a 16 to 128 character password to two separate locations, re-verifies the exported thumbprint against SQL, records SHA-256 of both files, and writes an immutable per-export receipt so a rotation can never overwrite an older custody record. The drill fails loudly when no exported certificate is on record. Defences against short-name aliases, junctions and nested paths are tested.

**Task 3 scheduled tasks — Done in code, blocked in practice.** See A4.3. Automation is required to hold the Store Manager role plus a backup-operator role, with elevated restore rights coming from a separate module-signing certificate login rather than sysadmin. That is a sound design.

**Task 4 Store Manager grants — Done.** `db_datawriter`, `db_datareader`, `db_owner` and `db_backupoperator` memberships are stripped and replaced by explicit per-table grants and column-scoped updates. Audit inserts go only through a procedure that takes the actor from `SUSER_SNAME()`, with direct insert denied and an `INSTEAD OF INSERT` trigger overwriting any supplied actor. The audit table and the reporting events table are append-only for everyone. Maintenance archives instead of deleting.

**Task 5 connection string — Done.** As A4.6. Also: `powershell.exe` is resolved by absolute path, `sqlcmd` is resolved only from two absolute Program Files locations and ACL-checked rather than found on PATH, and `-x` is passed on every `sqlcmd` call I could find.

**Task 6 signing — Done, stronger than the plan's fallback.** The plan allowed documenting the SmartScreen "Run anyway" path instead of signing. Codex wired signing and made it mandatory: the release build refuses to run without a certificate thumbprint and timestamp server, signs the executable and every packaged script, and the installer re-verifies every payload signature before compiling and then signs itself. Execution policy moves from `Bypass` to `AllSigned`, and `PrivilegesRequiredOverridesAllowed` is removed. Only the certificate purchase is outstanding.

**Task 7 line endings and operations doc — Done.** Migration checksums normalise line endings before hashing while still rejecting changed SQL. `docs/OPERATIONS.md` exists and is substantial, with the Owner break-glass procedure, certificate custody and a second-machine recovery exercise. GitHub Actions are pinned to commit SHAs. The CI security step runs the boundary tests and is named for what it actually does.

---

## 4. The merge blocker

**P4-1 — Migration 0022 grants on a table Phase 1 drops.** Phase 1's `0020_remove_document_extraction.sql` runs `DROP TABLE IF EXISTS dbo.document_extractions`. Phase 4's `0022_least_privilege_audit.sql` then runs, unguarded, at top level:

```sql
GRANT INSERT ON dbo.document_extractions TO etp_store_manager;
GRANT UPDATE ON dbo.document_extractions (review_status,reviewed_by,reviewed_utc,review_reason) TO etp_store_manager;
```

Migrations apply in numeric order, so on any merged branch 0020 drops the table and 0022 then fails on a fresh database. The migration runner is fail-closed, so bootstrap stops. I confirmed there is no `IF OBJECT_ID` guard around either statement, though the same file guards six other tables that way.

*Fixed looks like:* a new migration (rule 9 forbids editing the committed 0022) that guards or revokes those two grants, plus a test that bootstraps a database from 0001 through the merged set and asserts it completes.

**P4-2 — Phase 1, 2 and 3 tables have no write grants.** Replacing `db_datawriter` with an explicit enumeration of Phase 0 tables means the roughly 35 ETP family and landing tables from Phase 1, the tender modes, staff and monthly target masters, and Phase 2's brand row tables get `SELECT` from the schema grant but no `INSERT` or `UPDATE`. Phase 1 writes several of them directly rather than through a procedure, and `sales_line_enrichments` is explicitly denied to Store Manager while Phase 1's importer inserts into it directly. **Every Phase 1 to 3 write path will fail for a Store Manager after merge.** Codex's report gestures at this once as future reconciliation work; it is a hard functional break.

*Fixed looks like:* one integration test that runs a real folder import and a brand-master edit as a Store Manager against a database with 0021 to 0023 applied, and a migration extending the boundary to cover those tables or routing the writes through procedures.

---

## 5. Other items returned

**P4-3 — The strict ACL default makes the scheduled tasks uninstallable.** Decide the dedicated automation account question, then either relax the default or document that the switch is mandatory.

**P4-4 — The audit detail filter rejects any digit.** `record_operational_audit` throws when the detail matches `%[0-9:/\]%`, which is intended to keep paths and identifiers out of audit text but will also reject legitimate messages such as "3 files imported". Worth a narrower pattern.

**P4-5 — Two documents still show the dropped default.** `docs/INSTALL.md` and the CI workflow still carry `TrustServerCertificate=True` after the default moved to `Encrypt=Optional`.

**P4-6 — One developer script still resolves `sqlcmd` from PATH.** Not shipped, but inconsistent with the hardening everywhere else.

**P4-7 — The branch is not pushed**, so this work exists only on this machine.

---

## 6. Owner decisions

1. **SQL Server edition.** A4.4 requires an encrypted backup. Express cannot do it. Either move to a paid edition or amend D9 and the acceptance to an alternative protection.
2. **The dedicated automation account.** Nothing installs until this is settled.
3. **The code-signing certificate.** Everything is wired and mandatory; the purchase is the only gap.

---

## 7. Cleanup

Nothing was created on this machine for this phase and nothing needs removing. No database was created or dropped, no ACL changed, no task installed, no certificate created, no artefact signed. Live `EtpReporting` re-checked read-only: **490 invoices, 16 migrations**, unchanged. `settings.json` unchanged. No source, test or migration file was modified.

---

## 8. Addendum — verification completed 16 September 2026

The first pass was static: I had not built this branch or run a single test on it. That gap is now closed, and it changes three verdicts.

**Build and tests verified.** Clean Release build from a wiped tree: **0 warnings, 0 errors**. Full suite: **653 passed, 0 failed, 2 skipped**, which matches Codex's reported figure exactly. The two skips are the opt-in environment-gated render and full-window smoke tests, correctly gated rather than silently absent.

**A4.1 upgraded to PASS on my own execution.** I ran the Phase 4 security tests by name against disposable databases with real impersonation. All passed:

- `Locked_day_rejects_raw_reopen_delete_and_key_move_even_with_legacy_writer_permissions`
- `Staff_permissions_reject_fact_edits_spoofed_audit_and_owner_operations_but_preserve_imports`
- `Owner_without_windows_elevation_reopens_once_with_sql_actor_and_required_reason`
- `Audit_is_append_only_for_owner_and_archiving_keeps_original_history`
- `Staff_submissions_cannot_forge_approval_and_locked_day_adjustments_enter_accounting_only_after_owner_decision`
- `Only_dedicated_automation_and_owner_can_publish_verified_health_while_staff_can_read_safe_status`
- `Recovery_uses_backup_metadata_despite_later_live_changes_and_rejects_path_escape`
- `Support_package_contains_only_aggregate_health_and_omits_private_database_content`

The day lock and the staff denials are genuinely enforced in SQL. The real SSMS login under separate Windows accounts is still worth doing once at deployment, but the mechanism is proven.

**A4.2 is now a definite FAIL, as a statement about this PC.** I read the current permissions rather than changing them:

```
C:\ProgramData\EtpReporting\Backups
  NT SERVICE\MSSQL$SQLEXPRESS:(OI)(CI)(M)
  NT AUTHORITY\SYSTEM:(I)(OI)(CI)(F)
  BUILTIN\Administrators:(I)(OI)(CI)(F)
  DESKTOP-6IBM1J5\Sagar:(I)(F)
  CREATOR OWNER:(I)(OI)(CI)(IO)(F)
  BUILTIN\Users:(I)(OI)(CI)(RX)
  BUILTIN\Users:(I)(CI)(WD,AD,WEA,WA)
```

**BUILTIN\Users still holds read and execute plus write-data and append-data** on the root, on Backups and on Documents. The `Share` folder does not exist. This is audit 04's high finding exactly as it was first written: every unencrypted backup of the shop's sales and customer data is readable by any local account, and anyone can drop a file into the backup folder. Phase 4 wrote the code to fix this and the code looks right, but it has never been run, so **the exposure on the shop PC today is unchanged**. This is the item I would fix first, ahead of anything else in the phase, because it needs no merge and no certificate.

**A4.5 cannot be run on this branch at all.** I tried to generate a support package from a database holding real customer names and phone numbers. It failed with "Support health query failed. Check database access and try again." The cause is structural, not a bug: Phase 4's support package reads health objects created by its own migration 0023, and a Phase 1 to 3 database does not have them. Going the other way is also impossible — Phase 4 has **no folder import service, no ETP family profiles and no customer table**, so the real files cannot be imported into a Phase 4 database in the first place.

So A4.5 as written, "a session that imported the real files", is **unreachable until the merge blocker in section 4 is fixed**. It is not merely not run. Worth noting in passing that the failure message itself was a clean generic sentence with no SQL text in it, which is the behaviour Phase 4 intends.

**A4.4 confirmed blocked by edition.** The backup script requires AES-256 with a server certificate and fails closed. SQL Server Express does not support backup encryption. No amount of testing on this machine will close A4.4.

### Revised acceptance position

| ID | First pass | Now |
|---|---|---|
| A4.1 | not run | **PASS** by execution |
| A4.2 | not run | **FAIL** — BUILTIN\Users still has read and write |
| A4.3 | not run | still blocked on the automation-account decision |
| A4.4 | not run | **blocked by SQL edition**, not closeable here |
| A4.5 | partial | **unreachable until the merge blocker is fixed** |
| A4.6 | PASS | **PASS**, now with the test run behind it |

The verdict does not change. Phase 4 stays reopened on the merge blocker, and the folder permissions move to the top of the list because they are exploitable today and cost nothing to fix.

---

## 9. Addendum — direct source reading, 16 September 2026

Read the worktree source, migrations, scripts and tests first-hand rather than through a summary. Two things confirmed, one sharpened considerably.

### The merge blocker is larger and better defined than section 4 said

Section 4 estimated "roughly 35 tables". The exact figure, counted from the migrations:

**Phases 1 to 3 create 39 tables. All 39 have no write grant from Phase 4.** The only schema-wide grant is `GRANT SELECT ON SCHEMA::dbo`, so a Store Manager would be able to read every ETP family table, landing table and master, and write to none of them. There is no grant on `etp_r025`, `etp_r013`, `etp_r020`, the sixteen `etp_landing_*` tables, `tender_modes`, `staff`, `monthly_targets`, `brand_rows` or `brand_row_codes`.

The cause is an architectural mismatch, not an oversight in a list:

- **Phase 4 assumes writes go through stored procedures.** It denies direct DML on the sensitive tables and grants `EXECUTE` on narrow procedures instead: `DENY INSERT,UPDATE,DELETE ON dbo.sales_line_enrichments TO etp_store_manager` with `GRANT EXECUTE ON dbo.persist_sales_enrichment` as the sanctioned path, and the same pattern for `sales_lines` via `persist_sales_line`.
- **Phase 1's importer does direct DML and calls no procedure at all.** Its persistence class issues `MERGE dbo.staff` and `INSERT dbo.sales_line_enrichments` directly, and contains **zero** `EXEC dbo.` calls.

So the two halves of the product disagree about how writes reach the database. Closing this is not a matter of adding 39 grants; it is a decision about which model wins, and then either extending the grant surface or routing Phase 1's writes through procedures.

### Why a green test suite did not catch it

`Staff_permissions_reject_fact_edits_spoofed_audit_and_owner_operations_but_preserve_imports` looks like exactly the test that should have found this. Reading it, the "preserve imports" half writes to `import_batches`, `import_files` and `source_lineage` and then calls `EXEC dbo.persist_sales_line`. Every one of those is a **Phase 0** object, and the write goes through the sanctioned procedure.

The test is correct and proves something real: the Phase 0 import path survives the least-privilege model. It cannot prove anything about the Phase 1 path, because none of those tables exists on this branch. That is the whole reason the suite is green here and the product would break after a merge.

The fix that would actually catch it is the one named in section 4: run a real folder import and a master edit **as a Store Manager** against a database carrying migrations 0017 to 0024 alongside 0021 to 0023. Until that test exists, a green Phase 4 suite says nothing about the merged product.

### Confirmed by direct reading

**The connection boundary is enforced everywhere, not just in settings.** Every `new SqlConnection(...)` in the infrastructure layer passes through `LocalSqlConnectionPolicy.Validate`. The only two exceptions build a `master` connection string derived from a string that was already validated on the line above. The policy inspects the raw supplied spelling of `Encrypt` before SqlClient canonicalises `False` and `Optional` to the same value, which is the subtle part and is done correctly and commented. It also caps the connect timeout at five seconds, matching the Phase 0 fix. `tcp:`, bare remote hostnames, attached files, user instances, failover partners and any stored credential are all rejected.

**The day-lock trigger is sound on every path I could think to attack.** It blocks moving a locked day to another store or date, blocks deleting it, and for a reopen requires `etp_owner` membership, a target status of exactly `OPEN`, and a reason that is non-empty after tabs, newlines and carriage returns are stripped. A multi-row update where any one row lacks a reason fails the whole statement. The reopening event is written in the same statement with the actor taken from `SUSER_SNAME()`, and a second trigger prevents forging that event separately. A transition to any status other than `OPEN` is rejected by the reason check rather than slipping through.

Neither of these changes the verdict. They are recorded because the phase deserves the credit: the parts that are right are right for good reasons, and the blocker is a boundary problem between phases rather than a defect inside this one.

---

## 10. Re-audit of the integration fixes — 16 September 2026

Branch `integration/phase-2-3-4-fixes`, HEAD `123e1d4`, clean tree. Merge `8db3958` has parents `1c8eb1b` and `d730839` — the exact tips I audited. I verified everything below by execution against disposable databases and by read-only inspection of this PC. No live ACL, account, task or shop database was changed.

### Verdict

**The merge blocker is genuinely closed.** This was the single item that reopened Phase 4, and it is fixed properly — by deciding the architectural question rather than papering over it with 39 grants. **Phase 4 stays open**, but every remaining item is deployment, owner decision or edition, not code.

### Work item 1a — the `document_extractions` grants

**PASS.** I reproduced the original failure first, to confirm the fix is load-bearing rather than decorative. Applying the migrations in numeric order with `sqlcmd`, bypassing the C# runner, fails exactly where I said it would:

```
Msg 15151, Level 16, State 1, Line 264
```

Line 264 of `0022_least_privilege_audit.sql` is `GRANT INSERT ON dbo.document_extractions TO etp_store_manager`. That is the blocker, unchanged and real.

The fix is a transactional compatibility bridge in `SqlServerMigrationStore` (`Migrations.cs:193-233`) rather than an edit to the committed migration. Assessed:

- **No committed migration was touched.** `git diff 8db3958..HEAD -- database/migrations/` shows only `0025` and `0026` added; `git log --name-only` over the branch shows nothing in `0001`-`0024`. Confirmed independently.
- **The bridge is pinned to the exact original bytes.** It recomputes the checksum and throws `MigrationIntegrityException` unless it matches `e272fa70…`. I verified that constant is the real SHA-256 of the LF-normalised file, consistent with the Phase 0 line-ending fix.
- **It is narrow.** It fires only for `0022_least_privilege_audit`, and only when `0020` is journalled *and* the table is absent. `0022` references `document_extractions` on exactly two lines — both grants — so the minimal four-column stand-in carries the column-level `GRANT UPDATE` correctly.
- **It leaves nothing behind.** After a fresh bootstrap of all 26 migrations: `OBJECT_ID('dbo.document_extractions')` is **NULL**, and **zero** rows in `sys.database_permissions` reference a dropped object. (My first query counted 217 "orphans"; those are negative `major_id` system grants present in every database — a false alarm from my own query, not a leak.)

One residual risk worth recording: the bridge lives in the C# runner, so a database built by any other route would still hit Msg 15151. I checked for such a route and found none — migrations are applied only through `SqlServerDatabaseBootstrapper`, via `--initialize-database`, the installer or the ImportAudit tool, all of which use the same runner.

**Fresh bootstrap:** 26 of 26 migrations journalled, clean. **Upgrade paths:** `CrossPhaseMigrationTests` 4/4 passed in my own run (fresh setup, Phase 3-first, Phase 4-first, journal preservation, rerun idempotence, forced `0022` failure and retry).

### Work item 1b — the 39 ungranted tables

**PASS, and the architectural question was answered rather than dodged.** The chosen model is narrow SQL procedures for protected facts and Phase 1/2 source and master mutations; Store Managers keep only the Phase 0 import bookkeeping grants.

`0025_phase_integration_write_boundary.sql` creates **32 static `append_*` family procedures** — complete coverage, one per family, R001-R031 plus `CLOSING_STOCK` (r011), `STOCK_LEDGER` (r030) and `SOR_AGEING` — plus seven workflow procedures. Each family table gets `DENY INSERT,UPDATE,DELETE` to `etp_store_manager` and `etp_viewer`, with `GRANT EXECUTE` on its procedure.

The C# side matches: `PhaseOneImportPersistence` no longer contains `MERGE dbo.staff` or a direct `INSERT dbo.sales_line_enrichments`; its writes are `EXEC dbo.append_*`, `EXEC dbo.persist_phase_one_enrichment` and `EXEC dbo.complete_duplicate_import`. The only direct DML left is `import_row_outcomes`, a Phase 0 table with an explicit `0022` grant.

Each `append_*` procedure is well built: it requires role membership, **refuses to run outside a transaction** (`@@TRANCOUNT=0` throws 51422), takes `UPDLOCK,HOLDLOCK` on the import file, and pins the supplied lineage to its own report code — so a Store Manager cannot use one family's procedure to write rows attributed to another.

**I verified the boundary myself rather than trusting the test harness.** Against a fresh disposable database with my own user in `etp_store_manager`:

| Attempt | Result |
|---|---|
| Insertable tables for a Store Manager | **18** — all Phase 0 bookkeeping. No family table, no `staff`, no `brand_rows`, no fact table. |
| Role membership | `etp_store_manager` = 1; `db_owner`, `db_datawriter`, `sysadmin` all 0. |
| `INSERT dbo.etp_r025` | blocked, **229** |
| `INSERT dbo.staff` | blocked, **229** |
| `INSERT dbo.brand_rows` | blocked, **229** |
| `UPDATE dbo.sales_lines` | blocked, **229** |
| `DELETE dbo.operational_audit` | blocked, **229** |
| Viewer `EXEC dbo.save_evening_brand` | blocked, **229** |
| Manager `EXEC dbo.prepare_import_restatement` | blocked, **229** (Owner-only) |
| Manager `EXEC dbo.replace_import_facts_internal` | blocked, **229** (revoked) |
| `EXEC dbo.append_etp_r025` outside a transaction | blocked, **51422** |

### Superset promotion, examined as asked

**Copied-content-key attack: blocked.** The content-key superset check alone would be forgeable, because content keys are caller input. `promote_import_superset` therefore re-compares the **typed source values** per report under `Latin1_General_100_BIN2` with `EXCEPT`, and also rejects a key whose typed row is missing (`fresh.etp_row_id IS NULL`), which closes manifest-only forgery. All 32 families have a comparison branch; there is no report code that falls through to the key check alone. `CrossPhaseStoreManagerImportTests` drives this directly, copying rows with a changed `customer_name` under the original key, and gets **51424**.

**Facts survive an immediate commit.** Promotion never deletes. It inserts replacement lineage rows, repoints `sales_lines`, `sales_invoice_controls`, `sales_tenders`, `stock_movements`, `stock_snapshots` and `sales_line_enrichments` onto them via a `MERGE … OUTPUT` id map, and marks the old file superseded. The test stages valid rows, promotes, commits without calling any canonical persistence procedure, and asserts both the **fact id list** and the **summed gross amount** are unchanged. I had expected a `UQ_source_lineage` collision on the synthesized rows; the committing test disproves it, so I am not raising it.

Locked dates are refused (**51021**), and a locked day inside the replacement period blocks promotion (**51042**).

### How much the cross-phase test actually proves

`CrossPhaseStoreManagerImportTests` is the acceptance evidence for work item 1, so it deserves scrutiny rather than a tick. Its `RestrictedConnections` observer subscribes to SqlClient diagnostics and, for every production-created connection to that database, rewrites **every command** to prefix `EXECUTE AS USER` plus an assertion that the context is `etp_store_manager` and is *not* `db_owner`, `db_datawriter` or `sysadmin`. Pooling is off so connections are not reused, and `AssertCoverage` requires at least 128 observed connections in the folder test and a command on each.

That is strong: it is every command, not a sample, and it does not replace the production connection factory. The real-folder case imports all 32 sanitised families, retains document links, repeats the folder as duplicates, and saves a brand row through the repository — as a Manager throughout.

**The residual gap, stated plainly:** `EXECUTE AS USER` switches the database security context inside a session opened by my own privileged login. It faithfully exercises the SQL permission boundary — my independent 229s above confirm the same denials — but it is not a **separate Windows account**. A real second login mapped to the role, checked once through SSMS at deployment, is still worth doing, mainly because `SUSER_SNAME()` actor stamping cannot be exercised properly under impersonation.

### Returned Phase 4 items

| Item | Status | Evidence |
|---|---|---|
| P4-3 strict ACL default blocks task install | **PASS (code)** | `initialize-etp-operation-folders.ps1` now defaults `-GrantAutomationFolderAccess` to `$true` and `-AutomationPrincipal` to `<machine>\EtpAutomation`, with `-CreateAutomationAccount` provisioning it under an unrecorded random password. Guards are sound: rejects SYSTEM / LOCAL SERVICE / NETWORK SERVICE SIDs, rejects RID-500, rejects any member of Administrators, and requires the account enabled. Never executed — see deployment below. |
| P4-4 audit filter rejects any digit | **PASS** | Verified live against the real procedure. Allowed: `3 files imported`, `128 rows imported.` Blocked with **51310**: `C:\secret\file.xlsx`, `Invoice 100000068 for Rajesh`, a phone-length digit run, `3 customers named Rajesh`, and the full-width `３ files imported`. The full-width defence is the subtle part and it works — the collation-aware `LIKE` catches the digits, then the binary-collation allowlist refuses them. |
| P4-5 stale `TrustServerCertificate` docs | **PASS** | `docs/INSTALL.md` and `.github/workflows/ci.yml:15` both now use `Encrypt=Optional`. One residual: `tools/Etp.Reporting.ImportAudit/Program.cs:10` still builds `TrustServerCertificate=true` and does not pass through `LocalSqlConnectionPolicy`. It is a local dev tool restricted to `EtpReportingHelios` / `EtpPhase1Test_*`, so low severity, but it is the same class of inconsistency P4-6 tidied and should follow it. |
| P4-6 `sqlcmd` resolved from PATH | **PASS** | `Invoke-EtpFunctionAudit.ps1:9` uses the shared `Resolve-EtpSqlCmd` and retains `-x`. |

### Acceptance

| ID | Status | What is actually left |
|---|---|---|
| A4.1 fact/audit/permission denials | **PASS** | Confirmed twice: Codex's suite, and my own independent adversarial script above. Deployment-time SSMS check under a real second Windows account still advisable. |
| A4.2 no `BUILTIN\Users` on the three folders | **FAIL — unchanged, exploitable today** | Read-only `icacls` on this PC right now still shows `BUILTIN\Users:(I)(OI)(CI)(RX)` **and** `BUILTIN\Users:(I)(CI)(WD,AD,WEA,WA)` on the root, `Backups` and `Documents`. `Share` still does not exist. **17 unencrypted `.bak` files** sit in `Backups`, latest `EtpReporting-20260916-102109.bak` (12.9 MB, today) — every one readable by any local account, and anyone can drop a file in. This is the original audit-04 high finding, untouched. **Remaining work: deployment.** |
| A4.3 three tasks, one non-SYSTEM principal, backup within 24h | **FAIL — deployment** | `EtpAutomation` **does not exist** on this machine. Exactly **one** ETP task is installed — "ETP Reporting Monthly Recovery Drill", state Ready — and it runs as **`Sagar`**, the owner's own interactive account, not a dedicated service principal. The backup half is satisfied in fact (a backup exists from today), but the principal and three-task requirements are not. |
| A4.4 encrypted backup then drill restore | **BLOCKED — SQL edition** | Confirmed on the instance: `Microsoft SQL Server 2022 … **Express Edition**`. Express cannot create an encrypted backup, and the script fails closed rather than writing plaintext. No amount of work on this machine closes this. |
| A4.5 support package after a real import carries no private content | **BLOCKED, but now reachable** | Previously unreachable, because Phase 4 alone had no importer. The merged branch does, so this can finally be run — it needs a session that imported real customer data, which I will not create on this PC outside the agreed constraints. **Remaining work: manual acceptance on real data.** |
| A4.6 connection settings reject remote hosts and `Encrypt=False` | **PASS** | Policy unchanged; covered by tests that ran green in my own full-suite run. |

### Independent verification of Codex's claims

I ran the whole gate myself from a clean restore rather than reading the reported numbers.

```
dotnet build Etp.Reporting.slnx -c Debug   --no-restore   -> Build succeeded. 0 Warning(s), 0 Error(s)
dotnet build Etp.Reporting.slnx -c Release --no-restore   -> Build succeeded. 0 Warning(s), 0 Error(s)
dotnet test  Etp.Reporting.slnx -c Release --no-build --verbosity minimal   -> exit 0
```

```
Domain               12 passed,   0 failed,  0 skipped
Reporting            63 passed,   0 failed,  0 skipped
SqlServer           236 passed,   0 failed,  0 skipped
Import              110 passed,   0 failed,  0 skipped
Desktop             350 passed,   0 failed,  2 skipped
SqlServer.Integration 59 passed,  0 failed,  1 skipped
-------------------------------------------------------
Total               830 passed,   0 failed,  3 skipped
```

**830 / 0 / 3 — exactly the reported figure**, with the three skips being the opt-in render, full-window and live-capture harnesses. Both builds clean. Codex's numbers are accurate.

### State of this PC after my work

- Live `EtpReporting`: **490 invoices, 16 migrations, latest 2026-08-25** — unchanged, never opened for writing. Note it is still at 16 migrations, so the shop database does **not** carry the merged schema.
- `settings.json` SHA-256 `5A58FC54…` — unchanged. `ui-preferences.json` was rewritten by my app launch; disclosed in the Phase 3 audit.
- My two disposable databases (`EtpPhase1Test_ClaudeReaudit`, `EtpPhase1Test_ClaudeUi`) were dropped; zero remain.
- **Four orphaned databases remain that are not mine:** `EtpCrossPhaseMigration_*`, all created 2026-09-16 14:07, from Codex's interrupted earlier runs — its own report mentions runs failing on locked output DLLs. My clean run created and dropped its own, so this is residue, **not a fixture leak**. They should be dropped; I left them alone because they are not mine to remove.
- No ACL, account, scheduled task, certificate or service state was changed. I hold a non-elevated token.
