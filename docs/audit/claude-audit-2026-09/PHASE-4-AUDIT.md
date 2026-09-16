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
