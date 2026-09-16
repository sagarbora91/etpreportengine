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
