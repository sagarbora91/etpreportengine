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

### Sprint addendum — A4.5 closed by execution, 16 September 2026

A4.5 was unreachable on the Phase 4 branch because that branch had no importer. The merged branch does, so I ran it properly.

**A4.5 — PASS.** I built a disposable database (`EtpPhase1Test_ClaudeA45`) and imported the **real** shop workbooks into it — both the Titan and Helios "01 JULY 2026 TO 25 AUG 2026" folders, 59 files. Result: **540 sales lines, 490 invoices, 1,079 stock movements, 0 conflict outcomes**, and **540 rows carrying real customer names and real phone numbers**. The live `EtpReporting` database was never opened for writing.

I then generated a support package from that database with `scripts/new-etp-support-package.ps1` and searched it against sentinels pulled from the database itself, rather than eyeballing it:

| Sentinel set | Tested | Found in package |
|---|---|---|
| Distinct real customer names | 432 | **0** |
| Distinct real phone numbers | 439 | **0** |
| Path-like strings (`C:\`, UNC) | — | **0** |
| SQL keywords (SELECT/EXEC/FROM/INSERT/UPDATE) | — | **0** |
| `.xlsx` workbook names | — | **0** |
| The database name itself | — | **0** |

The whole package is 897 bytes and contains four files: aggregate database health (size, backup timestamps, failed-import count), a scheduled-task name and state, OS version and boot time, and a privacy statement. Nothing else.

This is the criterion met on real customer data, which is the only way it could ever have been met. **Both the disposable database and the generated package were deleted immediately afterwards**; no customer data was copied into the repository at any point, and the sentinel lists were deleted after the comparison.

One incidental confirmation worth recording, because it settles a plan correction empirically rather than by my recomputation alone: the real import yields Titan August **182 documents with 4 return lines** and Helios **38 with 1**. Removing the SR/BC documents that task 1 excludes from the denominator gives **178** and **37** — exactly the correction I applied to A1.2 and A2.1.

**Revised A4 position after the sprint:** A4.1 and A4.6 pass; **A4.5 now passes**; A4.2 and A4.3 still fail on this PC; A4.4 remains blocked by SQL Express. Every remaining item is deployment or edition, none is code.

### Correction: A4.2 and A4.3 are not the same kind of blocked

In the main pass I grouped these together as "deployment". Reading the installers closely, they are gated differently, and the difference matters.

**A4.2 is one elevated command.** `initialize-etp-operation-folders.ps1` never calls `Assert-EtpProtectedInstall`; it only rejects reparse points. It runs from the current worktree with nothing but an elevated shell:

```
& 'C:\Codex\Reporting Manger\phase234-integration-fixes\scripts\initialize-etp-operation-folders.ps1' `
    -SqlServiceIdentity 'NT SERVICE\MSSQL$SQLEXPRESS' -CreateAutomationAccount
```

That creates `EtpAutomation`, applies the ACLs, creates `Share` and writes the protected configuration. It closes the only live exposure on the machine. I did not run it: changing folder ACLs and creating a Windows account is a system security change that belongs to the owner, and the environment's safety controls correctly treat it that way.

**A4.3 cannot be completed even with elevation.** All three task installers call `Assert-EtpProtectedInstall`, which walks every parent directory and throws unless the whole tree is owned by Administrators or SYSTEM and is not writable by a non-administrator. A development worktree under `C:\Codex\…` fails that by construction. They also register their actions with `-ExecutionPolicy AllSigned`, so the scripts must carry a valid Authenticode signature.

So A4.3 needs, in order: the **code-signing certificate**, a signed installed release under an administrator-owned directory, then elevation, then the folder configuration from A4.2. It is blocked behind the certificate purchase, not behind a single command. My earlier "one elevated run" framing was right for A4.2 and wrong for A4.3.

The one existing task, "ETP Reporting Monthly Recovery Drill" running as `Sagar`, predates this work and is not what A4.3 asks for. It should be removed and reinstalled under the dedicated principal when the signed release exists — `scripts/remove-etp-scheduled-tasks.ps1` is the supported route.

---

## 11. Acceptance VM results — 17 September 2026

Tested on the Hyper-V acceptance VM `ETP-Acceptance-186` rather than the shop PC, so a mistake costs nothing. Host `DESKTOP-6IBM1J5`, guest `DESKTOP-IPT0J6H`, Windows 11 Enterprise Evaluation build 26200 (licence valid to 10 Dec 2026), 4 GB assigned, SQL Server 2022 **Express Edition** 16.0.1000.6, ETP 1.8.8 (`d24a525b`) installed.

Access was by **PowerShell Direct over the VMBus**, not the network: the guest firewall has 5985, 5986, 3389 and 445 all closed and does not answer ping, which is correct for a fresh Windows 11 on an unidentified network. PowerShell Direct is unaffected by that. The guest account `ETPTest` holds local administrator rights.

The VM would not previously start because `Create-TestVM.ps1` set 6 GB of **fixed** memory (`-DynamicMemoryEnabled $false`) against a host with 3.6–4.5 GB free. Enabling dynamic memory at 1 GB minimum / 3 GB startup / 6 GB maximum fixed it; the VM has since stayed up and reported "Operating normally".

### A4.2 folder ACLs — **PASS, by execution**

The guest reproduced the shop PC's defect exactly before the fix: `BUILTIN\Users:(I)(OI)(CI)(RX)` **and** `BUILTIN\Users:(I)(CI)(WD,AD,WEA,WA)` on the root, `Backups` and `Documents`; `Share` absent; four backup files exposed.

The two scripts were transferred through PowerShell Direct and **verified by SHA-256 against the host copies before running** — `initialize-etp-operation-folders.ps1` `BD2EF95AD2A9C60C…`, `etp-operations-common.ps1` `F2E3AB7CDBAAB3E5…` — so what ran is exactly what is committed on `integration/phase-2-3-4-fixes`.

Run in strict mode (`-GrantAutomationFolderAccess:$false`), exit code 0, output: *"Strict folder protection applied. Automation remains unconfigured until its folder-access decision is approved."*

After:

```
C:\ProgramData\EtpReporting          SYSTEM:(OI)(CI)(F)  Administrators:(OI)(CI)(F)  MSSQL$SQLEXPRESS:(OI)(CI)(RX)
C:\ProgramData\EtpReporting\Backups  SYSTEM:(OI)(CI)(F)  Administrators:(OI)(CI)(F)  MSSQL$SQLEXPRESS:(OI)(CI)(M)
C:\ProgramData\EtpReporting\Documents SYSTEM:(OI)(CI)(F) Administrators:(OI)(CI)(F)  MSSQL$SQLEXPRESS:(OI)(CI)(M)
C:\ProgramData\EtpReporting\Share    SYSTEM:(OI)(CI)(F)  Administrators:(OI)(CI)(F)  MSSQL$SQLEXPRESS:(OI)(CI)(M)
```

**Every `BUILTIN\Users` entry is gone**, inheritance is correctly broken (no `(I)` flags remain), `CREATOR OWNER` is removed, the interactive user's own grant on `Documents` is removed, `Share` is created, and the four existing backup files were **preserved, not deleted**. That is the criterion met.

**And it does not break the service.** The regression that would actually matter is locking the folder so tightly that SQL can no longer write a backup. It can: `BACKUP DATABASE master ... WITH COPY_ONLY, CHECKSUM` to the locked-down `Backups` folder succeeded — 506 pages, 4.1 MB — after the lockdown. The test file was removed. So the fix is secure **and** functional.

**One gap, and it is mine, not the code's.** I ran strict mode only. The `-CreateAutomationAccount` path — which provisions `EtpAutomation` and grants it folder access — was refused by this environment's safety controls because it creates a Windows account and rewrites permissions. `EtpAutomation` therefore still does not exist in the guest, and that path remains **UNVERIFIED**. It is one command for Sagar to run inside the VM, and A4.3 depends on it.

### A4.4 encrypted backup — **BLOCKED, now proven rather than inferred**

Previously I recorded this as blocked by reading the edition string. The engine has now refused the operation in its own words:

```
Msg 1844, Level 16, State 1
BACKUP DATABASE WITH ENCRYPTION is not supported on Express Edition (64-bit).
Msg 3013: BACKUP DATABASE is terminating abnormally.
```

The VM carries the same Express edition as the shop PC, so moving the test to a clean machine changes nothing. **A4.4 cannot be closed on any Express instance**, and no amount of further testing alters that.

Three ways forward, in order of cost:

1. **Install SQL Server Developer Edition in the VM.** It is free, licensed for non-production, and supports `BACKUP ... WITH ENCRYPTION`. That would close A4.4 as a *mechanism* — certificate creation, encrypted backup, drill restore and receipt verification all proven end to end — while leaving the production edition a separate purchase.
2. **Buy SQL Server Standard** for the shop PC, which closes it for production.
3. **Revise decision D9** to accept a different protection for backups at rest, at which point A4.4 is rewritten rather than met.

Option 1 is the only one that produces evidence without spending money, and I recommend it.

### A4.3 scheduled tasks — still blocked, unchanged

Two prerequisites remain unmet in the guest: the `EtpAutomation` account does not exist (above), and the task installers require a signed, administrator-owned install tree (`Assert-EtpProtectedInstall` plus `-ExecutionPolicy AllSigned`). A self-signed certificate created inside the VM could prove the mechanism, but only after the automation account exists.

### Phase 3 A3.3 — not attempted here, and why

Codex's own runbook lists guest scaling at 100/125/150% as step 7, which is the natural home for A3.3's outstanding DPI half. It was **not** run, because the guest has **1.8.8** (`d24a525b`) installed, not the Phase 2–5 candidate. Testing 125% scaling against the old shell would prove nothing about the Phase 3 redesign. Closing A3.3 in the VM first requires building the candidate and installing it there; the self-contained executable is ~159 MB, so that needs Guest Service Interface enabled for `Copy-VMFile` (currently disabled) or an ISO.

### State left behind

`C:\EtpPhase4Test\scripts\` in the guest holds the two transferred scripts, retained deliberately so the automation-account step can be run without re-transfer. The guest's machine execution policy is **Undefined at every scope** — verified after the run; only a process-scope bypass was used. No host ACL, account, task or database was touched, and the shop PC was not involved in any of this work.

### A4.4 — unblocked and proven at the SQL layer, 17 September 2026

The VM's SQL instance was **upgraded from Express to Developer Edition in place**, on Sagar's approval, so the instance name `.\SQLEXPRESS` and every existing database were preserved and no script needed repointing.

The Microsoft SSEI bootstrapper could not be used: launched over PowerShell Direct it has no interactive desktop, and it stalled at 0.66 seconds of CPU with no window, no child process and nothing written to disk. It was stopped. The upgrade was instead performed headlessly with the Setup Bootstrap already installed in the guest:

```
setup.exe /ACTION=EditionUpgrade /INSTANCENAME=SQLEXPRESS /PID=<Developer> /IACCEPTSQLSERVERLICENSETERMS /QUIET
-> exit code 0
SELECT SERVERPROPERTY('Edition')  ->  Developer Edition (64-bit) | 16.0.1000.6
```

Developer Edition is free and licensed for non-production use, which is exactly what this acceptance VM is.

**The full encrypted backup and drill chain then passed:**

| Step | Result |
|---|---|
| Create master key and server certificate | thumbprint `0x35F0261838D0FC9A2AEC09ED38F855025DCBFAAA` |
| `BACKUP … WITH ENCRYPTION(ALGORITHM=AES_256, SERVER CERTIFICATE=…)` | **succeeded**, 362 pages |
| `msdb.dbo.backupset` | `encryptor_type=CERTIFICATE`, `algorithm=aes_256` |
| `RESTORE VERIFYONLY … WITH CHECKSUM` | "The backup set on file 1 is valid" |
| Drill restore under a recovery name | 362 pages restored |
| Data integrity after the round trip | `restored row: A44-ORIGINAL-ROW` |
| Plaintext leak check on the file | no plaintext row in the first 2 KB |

The identical statement on Express was refused outright (`Msg 1844`). So the blocker was the edition and nothing else, and the mechanism the plan calls for is sound.

All drill artefacts were removed: both databases dropped, the backup file deleted, `sys.databases` confirms zero remaining.

**What this does not yet close, stated plainly.** A4.4's wording is "Backup → drill restore → verify passes on the audit PC **from the receipt-named file**". I proved the SQL layer, not the Phase 4 scripts. `backup-etp-database.ps1` first calls `Resolve-EtpLatestCertificateCustody`, which requires a `certificate-custody.json` pointer of schema version 2 naming an immutable receipt, verified by `Assert-EtpCertificateCustody`. That custody chain is produced by the application's Owner "Encrypted backup recovery keys" action, and the guest runs **1.8.8** (`d24a525b`), not the Phase 2–5 candidate that contains it.

So A4.4 moves from **BLOCKED by edition** — where no further work could have helped — to **PASS at the SQL layer, with the script-level custody chain outstanding**. Completing it needs the candidate build installed in this VM, the Owner certificate export run once, and then `backup-etp-database.ps1` and `invoke-etp-recovery-drill.ps1` executed against their own receipts. That is now ordinary work rather than an impossibility.

**Production remains a separate decision.** The shop PC still runs Express, so the encrypted backup it needs in production still requires either a supported edition there or an explicit revision of decision D9. This VM result proves the code and the procedure are correct; it does not license the shop machine.

### Revised Phase 4 position

| ID | Status |
|---|---|
| A4.1 | PASS |
| A4.2 | **PASS** — closed by execution on the VM, 17 Sep |
| A4.3 | Blocked: needs the `EtpAutomation` account, then a signed install tree |
| A4.4 | **PASS at the SQL layer** — script-level custody chain outstanding; production edition still a purchase decision |
| A4.5 | PASS |
| A4.6 | PASS |

### P4-7 (new defect) — `-CreateAutomationAccount` fails on first use

Found 17 September 2026 when Sagar ran the account-provisioning path on the shop PC.

`scripts/initialize-etp-operation-folders.ps1:31` passes a 65-character string to `New-LocalUser -Description`:

```
'ETP scheduled operations; dedicated non-administrator S4U account'
```

`New-LocalUser` validates `-Description` at **48 characters maximum**, so the call throws `ParameterArgumentValidationError` and the script aborts before granting any folder access:

```
Cannot validate argument on parameter 'Description'. The character length of the 65 argument
is too long. Shorten ... so it is fewer than or equal to "48" characters.
```

**Severity: blocks A4.2's automation half and all of A4.3.** Strict mode (`-GrantAutomationFolderAccess:$false`) is unaffected and works, as proven on the VM — this defect is confined to the branch that provisions `EtpAutomation`.

**Why it survived to now.** This path had never been executed by anyone. Codex's elevation attempt was cancelled by Windows; my own attempts were refused by the environment's safety controls, and section 11 of this audit recorded the path explicitly as **UNVERIFIED** rather than assuming it worked. That disposition proved correct on first contact.

**Fix:** shorten the description to 48 characters or fewer. `'ETP scheduled operations (non-admin S4U)'` is 40 and preserves the meaning. Add a test that provisions the account against a throwaway name and asserts it exists, enabled and outside Administrators — a source-text assertion would not have caught this, but an execution would.

**Workaround in the meantime**, which needs no change to the committed script: create the account manually with a compliant description, then re-run the script with `-CreateAutomationAccount`. The script guards creation with `if (-not (Get-LocalUser -Name $accountName ...))`, so it skips the failing call and proceeds to grant folder access normally.

### A4.2 — **CLOSED on the shop PC**, 17 September 2026

Sagar ran the committed script elevated on `DESKTOP-6IBM1J5` after working around P4-7 by creating the account manually. Output: *"Protected folders and dedicated automation configuration prepared."*

`icacls` on all four folders afterwards:

```
C:\ProgramData\EtpReporting            SYSTEM:(OI)(CI)(F)  Administrators:(OI)(CI)(F)
                                       EtpAutomation:(OI)(CI)(RX)  MSSQL$SQLEXPRESS:(OI)(CI)(RX)
C:\ProgramData\EtpReporting\Backups    SYSTEM:(OI)(CI)(F)  Administrators:(OI)(CI)(F)
                                       EtpAutomation:(OI)(CI)(M)   MSSQL$SQLEXPRESS:(OI)(CI)(M)
C:\ProgramData\EtpReporting\Documents  (same as Backups)
C:\ProgramData\EtpReporting\Share      (same as Backups)
```

**No `BUILTIN\Users` entry on any of the four.** No `CREATOR OWNER`, no inherited `(I)` flags, and no interactive-user grant. That is the criterion, met literally.

The privilege split is correct rather than merely permissive: the **root is read-only** (`RX`) for both the automation account and the SQL service, while only the three working folders carry `Modify`. Neither identity can restructure the parent.

Independently verified, not taken from the script's own success message:

| Check | Method | Result |
|---|---|---|
| Standard users cannot read | non-elevated session attempted all four folders | **Access is denied** on every one |
| Backups preserved | `sys.dm_os_enumerate_filesystem` as the SQL service | **17 `.bak` files**, newest `20260916-102109.bak` at 12.3 MB, unchanged |
| SQL retains access | same enumeration succeeded | Yes |
| `EtpAutomation` exists and is enabled | `Get-LocalUser` | Yes |
| **Not an administrator** | `Get-LocalGroupMember Administrators` | **Correct — absent** |
| SQL service healthy | `Get-Service` | Running |

The original high finding from audit 04 — every unencrypted backup of the shop's sales and customer data readable by any local account, in a folder anyone could write to — **is fixed on the machine that matters**. It stood from the first audit on 15 September until now.

Not claimed: I did not write a test backup on the shop PC, because a non-elevated session could not then delete it and I will not leave litter in the owner's backup folder. The write path is proven on the VM under the identical ACL set, the `Modify` ACE is present here, and the next scheduled backup confirms it in practice.

### Phase 4 position after 17 September

| ID | Status |
|---|---|
| A4.1 | PASS |
| A4.2 | **PASS — closed on the shop PC and the VM** |
| A4.3 | Blocked on a code-signing certificate and a protected install tree; the automation account now exists |
| A4.4 | PASS at the SQL layer on Developer Edition; script custody chain outstanding; production edition still a purchase decision |
| A4.5 | PASS |
| A4.6 | PASS |

Four of six pass. Neither remaining item is a coding defect: A4.3 waits on a certificate, A4.4 on an edition. P4-7 is the one open code defect and it is a one-line fix.

### P4-8 (new defect) — headless startup hangs instead of failing on a bad connection string

Found 17 September 2026 while installing the candidate in the acceptance VM.

`Etp.Reporting.Desktop.exe --initialize-database --connection-string <invalid>` does not exit. The process starts, consumes about one second of CPU, creates no window, and then **sits idle indefinitely with no exit code**. Two runs behaved identically before the cause was found.

The application's own diagnostics record what happened:

```
{"Severity":"Critical","EventId":"DISPATCHER_UNHANDLED",
 "ExceptionType":"System.ArgumentException","HResult":-2147024809}
```

`App.OnStartup` is `async void` and calls `DesktopCompositionRoot.CreateForArguments`, which throws `ArgumentException` when `ConnectionStringValidation` rejects the supplied string. Because the method is `async void`, the exception reaches the WPF dispatcher's unhandled handler, which logs it — and the process then stays alive with a message pump and nothing to pump.

**Why it matters.** Headless initialization is the installer's path and the scheduled-task path. A caller that supplies a malformed connection string gets no exit code, no console output and no window: an installer or task would block indefinitely rather than reporting a bad configuration. Phase 0's A0.7 verified `--initialize-database` returns exit 0 on the *happy* path; the failure path was never exercised.

**Expected behaviour:** exit non-zero and write the validation message to stderr. The message already exists — `ConnectionStringValidation` produces a specific reason — it simply never reaches the caller.

**Reproduction:** run `--initialize-database --connection-string "Server=.\SQLEXPRESS;Database=X;Integrated"` (a truncated string). Observe no exit, no window, and a `DISPATCHER_UNHANDLED` entry in `%LOCALAPPDATA%\EtpReporting\Logs`.

**Severity:** medium. It cannot corrupt data and the happy path is unaffected, but it turns a clear configuration error into a silent hang in exactly the unattended contexts where diagnosis is hardest.

**Not a defect, for the record:** the original failure that led here was my own — I passed the connection string through PowerShell `-ArgumentList` unquoted, and `Integrated Security=True` and `Connect Timeout=5` contain spaces, so the app received a truncated argument. The application was right to reject it. It was wrong to hang.

### P4-7 and P4-8 fixed — 17 September 2026

**Author's note, stated plainly: I wrote these two fixes myself, at Sagar's instruction, rather than returning them to Codex.** That means the auditor and the implementer are the same person for these two changes, and the independence that makes the rest of this audit worth something does not apply to them. Both are small and both carry execution evidence, but a later reviewer should treat them with that in mind.

#### P4-7 — `New-LocalUser -Description` too long — **FIXED**

`scripts/initialize-etp-operation-folders.ps1:31` passed a 65-character description against Windows' 48-character limit, so `-CreateAutomationAccount` aborted before granting any folder access. Shortened to `'ETP scheduled operations (non-admin S4U)'` — 40 characters, same meaning.

**Verified by execution, not by inspection.** Sagar ran `New-LocalUser` with this exact description on the shop PC this morning and it succeeded, creating `EtpAutomation` enabled and outside Administrators. That is the same cmdlet and the same string the script now passes.

No separate automated test was added. Provisioning a real Windows account is precisely the action this environment's safety controls refuse, and a test that asserted the *text* of the description would be the source-text assertion the plan's rules ban. The honest evidence here is the successful live run.

#### P4-8 — headless startup hung instead of exiting — **FIXED**

**My earlier description of the mechanism was wrong and is corrected here.** I recorded the process as idling with "a message pump and nothing to pump". The actual cause is narrower and worse: `OnDispatcherUnhandledException` called **`MessageBox.Show(...)`** and set `e.Handled = true`. Headless, that is a modal dialog waiting for a click that an installer or scheduled task can never provide. The process was not idle — it was blocked on a window nobody could see.

Two changes in `src/Etp.Reporting.Desktop/App.xaml.cs`:

1. The startup mode is now resolved with `DesktopStartupCoordinator.Route(e.Args)` **before** composition. `DesktopCompositionRoot.CreateForArguments` is wrapped so that a rejected configuration in any non-interactive mode records a diagnostic, writes the validation reason to **stderr**, and shuts down with exit code 2.
2. `OnDispatcherUnhandledException` no longer shows a dialog when headless; it writes the message to stderr and shuts down with a non-zero code. The interactive path is unchanged.

The validation reason already existed — `ConnectionStringValidation` produces a specific message — it simply never reached the caller.

**Proven in both directions, which is the only way a regression test is worth adding.**

`HeadlessStartupFailureTests` launches the built application for each of the three headless modes (`--initialize-database`, `--initialize-configured-database`, `--automation-once`) with a deliberately truncated connection string, and asserts the process exits inside 90 seconds with a non-zero code and a non-empty stderr.

- **Against the pre-fix binary** the test **failed**: `--automation-once` did not exit and was still blocked at 41 seconds. The modal dialog was visible on screen during that run and was captured by Sagar — direct confirmation of the mechanism above.
- **Against the fixed binary** all three modes pass in **3 seconds** total.

The test asserts behaviour — exit code, timeliness, stderr — not source text, and it counts no controls.

Scope kept: no day lock, connection policy, audit protection, signing, certificate or backup-rotation code was touched. `LocalSqlConnectionPolicy` is unchanged; the rejection it performs is the same, only its reporting changed.

**Full suite after both fixes:** 860 passed, 0 failed, 3 skipped (857 + 3 new). Debug and Release both 0 errors, reproduced with `-m:1 -nodeReuse:false`. Integration rose 79 -> 82.

---

### A4.4 script-level certificate-custody chain — **PASS** (with one new defect found)

Executed in the acceptance VM against the candidate's own scripts, all six copied by `Copy-VMFile` and **SHA-256 verified identical** to the repository copies before use.

**1. The repository's own boundary harness — 190 checks, all scenarios green.**

`test-etp-operations-boundaries.ps1` run for all eight scenarios in the guest: TargetAliases 46, BackupReceipts 12, CertificateCustody 14, CertificateBinding 26, Retention 71, Paths 11, ProtectedInstall 6, AtomicReceipts 4. Exit 0 in every case. This harness never calls SQL; it is fixture-level, so it is reported as such and not as end-to-end proof.

**2. A real certificate, a real export, a real backup.**

Not fixtures. `CREATE CERTIFICATE EtpBackupCert` in `master`, then `BACKUP CERTIFICATE ... WITH PRIVATE KEY` to two distinct custody locations. Thumbprint `BF088489D2B991E6A821124046357F182E1B29C1`. A custody receipt was written with the product's own `Write-EtpJsonAtomically`, named `certificate-custody-<thumbprint>-<exportId>.json` as the contract requires, with SHA-256 of all four exported files.

- `Assert-EtpCertificateCustody -RequireAvailable -ExpectedThumbprint` **accepted** the genuine export.
- `Resolve-EtpLatestCertificateCustody` **selected the immutable receipt** through the `certificate-custody.json` pointer.
- `backup-etp-database.ps1` completed: "Encrypted backup and verification completed.", producing an 8.1 MB backup, a `.bak.receipt.json` and `EtpCustodyDrill-latest-verified.json`.

**The encryption was verified independently of the script**, from `msdb.dbo.backupset`:

```
alg=aes_256  thumb=BF088489D2B991E6A821124046357F182E1B29C1  type=CERTIFICATE  db=EtpCustodyDrill
```

AES_256, encrypted by certificate, and the thumbprint is exactly the key held in custody. The script's own claim was not taken as evidence.

- `invoke-etp-recovery-drill.ps1` completed a receipt-verified isolated restore: "Receipt-verified recovery drill completed.", with a durable `latest-drill.json` recording the backup SHA-256.

**3. Fail-closed behaviour — proven, and my first reading of it was wrong.**

Tampering with one exported private key and re-running the **backup** did not fail; it completed normally. My initial expectation was that it should refuse. That expectation was incorrect, and the code is right:

- `backup-etp-database.ps1` resolves custody **without** `-RequireAvailable`, and reads receipts with `-SkipCertificateCheck`.
- `invoke-etp-recovery-drill.ps1` reads **with** the check, and with the same tampered copy it refused: "A certificate recovery copy is missing or has changed. Reconnect the recovery storage and verify custody."

So the trade-off is deliberate and coherent: **a disconnected or offline recovery drive never stops a nightly backup, but it always stops the drill.** `Read-EtpVerifiedReceipt` documents exactly this, and the boundary harness asserts it. Recorded as designed behaviour, not a defect.

**One residual operational risk, for the owner rather than the code:** because the media check lives only in the drill, a lost or corrupted private key is not detected until the next monthly drill. Up to a month of backups could in principle be undecryptable before anyone is told. The drill is the control that catches it, so the drill must actually run and its failures must be seen.

**4. The protected-install guard bites.** `Assert-EtpProtectedInstall` **rejected** the script folder used for this test — "The installation folder can be changed by a non-administrator." — because it was a user-writable directory rather than a protected install location. Correct refusal, recorded as evidence that the guard works.

**Not exercised in this run:** the least-privilege grants to the dedicated automation account (`install-etp-sql-operations.ps1`). Creating a Windows local account is outside what this audit environment permits, and the request was refused for a third time. Rather than route around it, the broker procedure alone was installed from the shipped template using the identical substitutions, and the backup and drill were run as an administrator, for which the procedure's own `IS_SRVROLEMEMBER('sysadmin')` branch bypasses the role gate. **The least-privilege half of A4.4 therefore rests on the earlier Phase 4 evidence, not on this run.** Provisioning that account remains Sagar's step.

### P4-9 — operations scripts resolve a SQL client that cannot reach the instance — **OPEN**

Found while running the above. `Resolve-EtpSqlCmd` (`scripts/etp-operations-common.ps1:54-57`) prefers `%ProgramFiles%\sqlcmd\sqlcmd.exe` (go-sqlcmd) ahead of the ODBC `SQLCMD.EXE`. go-sqlcmd resolves `.\INSTANCE` and `localhost\INSTANCE` over **named pipes**, which is disabled by default on SQL Server Express and Developer. On the acceptance VM every call failed:

```
[.\SQLEXPRESS]         exit=1  Timed out waiting for pipe SQLLocal\SQLEXPRESS
[localhost\SQLEXPRESS] exit=1  Timed out waiting for pipe SQLLocal\SQLEXPRESS
[lpc:.\SQLEXPRESS]     exit=0  OK
```

It fails on `SELECT 1`, so it is connectivity, not the query. Proven to be the sole cause: with go-sqlcmd temporarily renamed, the identical recovery drill went from exit 1 to exit 0 and back again, with nothing else changed.

**Impact.** On any machine where go-sqlcmd is installed and named pipes is off, `install-etp-sql-operations.ps1`, `backup-etp-database.ps1` and `invoke-etp-recovery-drill.ps1` all fail — that is scheduled backups and the monthly recovery drill. `backup-etp-database.ps1` has a `-SqlCmdPath` override; **`invoke-etp-recovery-drill.ps1` has none**, so the drill cannot be rescued without changing code or the environment. The failure surfaces as the deliberately masked "The database operation failed. Check SQL permissions and operation prerequisites.", which points the operator at permissions rather than at the client.

go-sqlcmd v1.10.0 on this VM dates from 3 March 2026 and is part of the base image, not something introduced by this audit. **The shop PC does not currently have go-sqlcmd installed, so production is not affected today** — but it ships with recent SSMS builds and any future install would silently break backups.

`Assert-EtpLocalSqlTarget` already accepts `lpc:` and `np:` prefixes, so the protocol prefix is an anticipated concept; it is simply never paired with the client that needs it. Not fixed here: choosing between preferring the ODBC client, probing candidates for reachability, or prefixing `lpc:` for local instances is a deployment decision for the owner.
