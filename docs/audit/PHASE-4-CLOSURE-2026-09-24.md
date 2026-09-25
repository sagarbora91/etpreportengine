# Phase 4 closure record — 24 September 2026

Written for Sagar, and for whoever audits this later.

Every claim below is either backed by something you can go and look at, or is marked plainly as
not verified. Where a thing was implemented but never run on a real machine, it says so. "A test
exists" is nowhere treated as proof that something works.

- Worktree `C:\Codex\Reporting Manger\opus-recovery`, branch `recovery/opus-r1-r4`, tip `8c05318`, pushed.
- `origin/main` is `849e3f2`, a merge of this branch made on 24 September. The branch is four commits ahead of it, and `main` carries eight commits the branch does not — including the newer plan (section 2).
- Live database checks in this record were run read-only on `DESKTOP-6IBM1J5\SQLEXPRESS` on **25 September 2026**. No row, table, procedure, permission, user or migration record was changed, no task was changed, no account was touched. The acceptance VM was left **Off**.
- One exactness note on "read-only": SQL Server creates small column statistics by itself the first time a query filters on a column that has none. The 22 September investigation recorded five such statistics created on `EtpReporting` by its own read-only `sqlcmd` sessions (`ETPREPORTINGHELIOS-FINDINGS-2026-09-22.md` §9). They are query-planning metadata; no data or definition changed. The same may have happened again on 25 September.
- The file keeps the 24 September date because that is the day the live work was done. The re-checking was finished the next morning.
- Every claim in this record was then checked a second time, independently, on 25 September — the live instance read again with `sqlcmd`, the installed files measured, `git` interrogated, and the raw captures in `C:\Users\Sagar\ETP-live-install` read rather than summarised. What that pass corrected is folded in where it belongs rather than appended: chiefly the folder-permission detail in A4.2, the size of the unchecked SQL backup folder (28 files, not three), the two criteria that pass on tests rather than on a machine (A4.1, A4.6), and the fact that the two-pass VM transcript must be read as a pair.

---

## 1. What Phase 4 was for

Phase 4 is "Security and operations hardening" in the master plan. Stripped of the jargon, it had
to make five things true on the machines that run the shop:

1. Only the right people can change the shop's figures, and a day that has been closed cannot be
   quietly reopened by someone who should not.
2. The folders holding backups and customer documents are readable only by administrators, the SQL
   service and one dedicated account — not by every user of the PC.
3. A backup is taken every night without anyone remembering to do it, it is checked, and it is
   proven to restore — including onto a different machine.
4. Nothing runs as `NT AUTHORITY\SYSTEM`, the account that can do anything.
5. The application can only talk to the SQL Server on that same PC, never to a machine elsewhere.

**Date range.** Codex implemented Phase 4 on branch `phase-4/security-operations`. The first audit
of it is dated **16 September 2026** (`docs/audit/claude-audit-2026-09/PHASE-4-AUDIT.md`, branch at
`df35d90`) and reopened the phase. Defect fixing, the merge into the product line, the installer
build and the live deployment ran from **16 September to 24 September 2026**. The facts in this
record were re-checked on **25 September 2026**.

---

## 2. Acceptance criteria

The criteria are the A4.x list in the **newest** plan, which is on `main` — version 1.4, 16
September 2026. Read it with `git show main:docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md`. The
copy on this branch is version 1.3, 15 September 2026, and does **not** contain A4.4's row-count wording, A4.4a or
A4.7 at all; anyone reading only the branch copy would think Phase 4 had six criteria instead of
eight.

| ID | What it asks | Verdict | Where it was observed |
|---|---|---|---|
| **A4.1** A Viewer cannot reopen a day or delete audit rows; a Store Manager cannot edit facts | **PASSED in tests — never observed as a real login** | Eight named security tests, run by the auditor on 16 Sep against disposable SQL databases with real impersonation, all passed (`PHASE-4-AUDIT.md` §8). Read that verdict for what it is: a test result on throwaway databases, not a measurement on the live one. The criterion's literal method — signing into SSMS as a separate Viewer *Windows* login and as a separate Store Manager *Windows* login — has never been performed on any machine, and no one has sat at the live database as a Viewer and tried to reopen a locked day. The denials are written into migration `0022` and enforced by SQL rather than by the app, so the mechanism is evidenced; the ceremony the plan asks for is not. |
| **A4.2** No `BUILTIN\Users` entry on the ProgramData folders | **PASSED** | `C:\Users\Sagar\ETP-live-install\acl-before.txt` and `acl-after.txt`, captured elevated on this PC on 24 Sep. Six folders (root, Backups, Documents, Share, Operations, SetupLogs): only SYSTEM, Administrators, `DESKTOP-6IBM1J5\EtpAutomation` and `NT SERVICE\MSSQL$SQLEXPRESS`. No `BUILTIN\Users`, no `Everyone`, no inherited entries. The root **and `Operations`** are read-only (`RX`) to the automation account and the SQL service; `Backups`, `Documents`, `Share` and `SetupLogs` carry Modify. The two captures are not merely the same story — they are the same file byte for byte (MD5 `181ce708161b18c60233cbfa9bc6b8d2` on both), so the install changed nothing. First closed on 17 Sep (`PHASE-4-AUDIT.md`, "A4.2 — CLOSED on the shop PC" — **that document calls this machine, `DESKTOP-6IBM1J5`, the shop PC; this record does not**, and the machine open item 1 is waiting on is a different one); unchanged by the 24 Sep install. **Limit, and it matters:** the criterion and the evidence cover those six folders only. SQL Server's own backup folder holds **28 `.bak` files** and has never been captured at all; three of them are known to hold real customer names and phone numbers — `EtpReporting-pre-0031-…`, `EtpReporting-pre-0032-…` and `EtpReportingHelios-archive-…` — and the other 25 are older test and validation databases whose contents nobody has examined. See open item 13. |
| **A4.3** Three ETP tasks, same non-SYSTEM principal, and a daily backup within 24 h | **PASSED, with one owner-decided departure** | Three tasks exist and **none runs as SYSTEM**: `tasks-after-setup.txt` shows Automated Operations and Daily Backup as `EtpAutomation` (S4U, Limited) and Monthly Recovery Drill as `Sagar` (S4U, Highest). A task registered for another account is invisible without elevation, so an unelevated `Get-ScheduledTask` lists only the drill; the three-task evidence is the two elevated captures — `tasks-after-setup.txt` on 24 Sep, and the elevated re-check at 12:52 IST on 25 Sep written up in `LIVE-INSTALL-RUNBOOK-2026-09-22.md` under "First unattended run". I confirmed the drill task myself, unelevated, on 25 Sep: last run 12:48:18, result 0. The departure: that is **two** principals, not one. It is the P4-15 decision (the drill needs a SQL administrator, so it runs as the Owner), taken deliberately, not an oversight. Daily backup within 24 h: **yes** — `dbo.operational_audit` holds `Backup / Succeeded / DESKTOP-6IBM1J5\EtpAutomation` at `2026-09-25 07:18:56` UTC, and the file `EtpReporting-20260925-071854-….bak` (15,454,208 bytes) is in the Backups folder. I read both myself on 25 Sep. |
| **A4.4** (first half) Backup → drill restore → verify passes from the receipt-named file | **PASSED** | 24 Sep on this PC. `operational_audit`: `Backup / Succeeded / EtpAutomation` at `10:22:42` UTC ("Checksum backup verified, not encrypted") and `RestoreDrill / Succeeded / EtpAutomation` at `10:22:50` UTC ("Isolated restore and backup metadata checks passed; the backup was not encrypted" — the full row, read on 25 Sep). `EtpReporting-latest-drill.json` is on disk, last written `2026-09-24 10:22:50`, the same second as the audit row. No `EtpRecovery_*` database was left behind — I re-checked `sys.databases` on 25 Sep and the only user database on the instance is `EtpReporting`. **Two parts of this criterion were not observed.** (a) It also asks that "System status shows the drill as passed with those four counts". Nobody opened the app's System status screen after the 24 Sep drill; no record of it exists in the runbook outcome or anywhere else. (b) The four counts are the row-count block below, which does not exist. |
| **A4.4** (second half) `<database>-latest-drill.json` lists four row-count pairs from the receipt and the restored copy | **NOT VERIFIED — the feature does not exist** | `grep -n "rowCounts"` over `scripts/backup-etp-database.ps1` and `scripts/invoke-etp-recovery-drill.ps1` returns nothing, and neither script selects `COUNT(*)` from `sales_invoices`, `sales_lines`, `import_files` or `daily_reporting_days`. The plan gained this wording on 16 Sep (version 1.4); no code was written for it. |
| **A4.4a** A receipt with one row count altered must make the drill fail and name the table | **NOT VERIFIED — untestable as things stand** | It tests the block above, which does not exist. Never attempted. |
| **A4.5** Support package and diagnostics from a session that imported the real files contain no customer name, phone, file path or SQL text | **PASSED** | Closed by execution on 16 Sep (`PHASE-4-AUDIT.md`, "Sprint addendum — A4.5 closed by execution"): the auditor imported the real Titan and Helios workbooks, 59 files, into a disposable database `EtpPhase1Test_ClaudeA45` — 540 sales lines carrying real names and phone numbers — then generated the support package and searched it against sentinels pulled from that same database rather than eyeballing it: **432 distinct real customer names and 439 distinct real phone numbers, 0 of each found**, and nothing matching path-like strings, SQL keywords, workbook names or the database name. The whole package was 897 bytes in four files. Live `EtpReporting` was never opened for writing, and both the disposable database and the package were deleted afterwards. |
| **A4.6** Connection settings reject `Server=remotehost` and `Encrypt=False` | **PASSED in tests — the Settings screen itself was never exercised** | 16 Sep. One policy class gates every connection; only `.`, `(local)`, `localhost`, the machine name, `lpc:` and local named pipes are accepted. Table-driven tests including `Server=remotehost`, `Encrypt=False`, `Encrypt=no` and `tcp:` were run by the auditor, not merely read — but they are tests against that class. Nobody has typed those strings into the app's connection settings on a real machine and watched them refused, which is what the criterion's words describe. |
| **A4.7** Signatures `Valid` on the exe, every packaged script and the installer; `AllSigned` inside a running task; two SHA-256 values in `CHANGELOG.md`; the "More info → Run anyway" step in `docs/OPERATIONS.md` and no instruction to buy a certificate | **NOT MET — every part of it, and most parts deliberately** | Measured by me on 25 Sep, on the installed files: `Get-AuthenticodeSignature` on `Etp.Reporting.Desktop.exe` (version `1.8.8+70bf46e…`) returns **NotSigned**; so do **all 13** packaged scripts under `C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts`; and so does the installer itself, `artifacts/installer-70bf46e/EtpReportingEngine-Setup-1.8.8-x64.exe`. All three things the criterion names are unsigned, not just the exe. `CHANGELOG.md`'s newest released entry, 1.8.8 dated 12 Sep, carries no SHA-256 values; the installer artefact does carry its own hash in `artifacts/installer-70bf46e/SHA256SUMS.txt`, but that is not where the criterion asks for it. `Get-ExecutionPolicy -Scope Process` inside a running task was **never captured on any machine**; the deployed tasks pass `-ExecutionPolicy RemoteSigned` themselves, so it would not have read `AllSigned` in any case. `docs/OPERATIONS.md` contains **no** "More info → Run anyway" step, and its deployment table still reads "Acquiring a signing certificate remains open" — the opposite of the criterion's last clause. Most of this is not an accident: on 17 Sep the owner accepted unsigned releases and the deployed execution policy moved from `AllSigned` to `RemoteSigned` (commit `514a105`), because `AllSigned` on an unsigned install would refuse the nightly backup and the monthly drill. The plan on `main` was never amended to match. See section 6, items 7 and 14. |

The table splits A4.4 into its two halves because they have different verdicts, but the plan counts
it as one criterion. Counting the plan's way, of the eight: **four passed** (A4.1, A4.2, A4.5,
A4.6), **one passed with a departure the owner chose** (A4.3, two principals instead of one), and
**three are not met** — A4.4 (its row-count block was never built, and the working half was never
shown on the System status screen the criterion names), A4.4a, and A4.7.

Two of those four passes are not the same kind of pass as the other two, and the difference should
not be lost in the count. **A4.2 and A4.5** were observed on a real machine: real folder
permissions captured before and after, and a real support package built from real customer data
and searched. **A4.1 and A4.6** rest on tests the auditor ran against disposable databases and a
policy class. The tests are good tests and the auditor ran them rather than reading about them,
but neither criterion has ever been exercised the way its own words describe.

**The plan's tasks, as opposed to its acceptance criteria.** Phase 4 is seven tasks and eight
criteria. This record tracks the criteria, because they are what closure is judged on. The tasks
were checked once, by reading the code on 16 September (`PHASE-4-AUDIT.md` §3, all seven "Done").
Since then:

- **Task 2 (folder ACLs) and task 3 (scheduled tasks)** have been run on a real machine and are the
  strongest evidence in this record. Task 2's *other* half — certificate-encrypted backups and
  off-PC key custody — does not apply on Express and was superseded by the 17 Sep D9 decision
  (open item 14).
- **Tasks 1, 4 and 5** (day lock, Store Manager grants, connection policy) rest on tests run by the
  auditor against disposable databases on 16 September, plus the fact that the live database has
  carried `0022` and now `0032` without incident. No one has sat at the live database as a Viewer
  and tried to reopen a locked day.
- **Task 6 (signing)** was implemented and then deliberately turned off by the 17 Sep decision. See
  A4.7 and open item 7.
- **Task 7 has not been re-checked since the 16 September reading**: migration-checksum line-ending
  normalisation, GitHub Actions pinned to SHAs, and the CI security step named honestly. The one
  part I did check on 25 Sep is the Owner break-glass procedure, which is in `docs/OPERATIONS.md`
  under "Owner recovery and maintenance". Reading is not running; nothing else in task 7 is claimed
  here as observed.

---

## 3. Phase 4 defects

The numbering runs P4-1 upwards from the 16 September audit. **Note a collision in the record:**
`PHASE-4-AUDIT.md` §5 uses "P4-7" for "the branch is not pushed", and then the same file later
uses "P4-7" again for the `-CreateAutomationAccount` failure found on 17 September. They are two
different things. The table below uses P4-7 for the second, substantive one and disposes of the
first in a footnote.

The last column is the one that matters for independence. **P4-1 to P4-6 were found by the audit
of Codex's branch. Everything from P4-7 onwards was found by this auditor's own runs** — installing
the build, provisioning the VM, running the scripts, or standing over the live install — not by
reading code.

Eighteen rows follow: P4-1 to P4-15, two task-registration defects and the startup-mutex defect.
Separately, the 22 September review of the P4-13/14/15 fixes produced eleven more findings; they
are listed after this table and are deliberately not counted here.

| Defect | What it was | How it was fixed | Evidence it is fixed | Found by |
|---|---|---|---|---|
| **P4-1** | Migration `0022` granted permissions on `document_extractions`, a table Phase 1's `0020` drops. On a merged branch every fresh database would stop at bootstrap. | A later migration bridges the retired grants; the committed `0022` was not edited (plan rule 9). | Commit `603e2e4`, 16 Sep, "Bridge retired extraction grants without rewriting migrations". The live database is at `0032` with all 32 migrations applied, counted on 25 Sep. The 24 Sep upgrade log records "post-migration state, journal count, and DBCC integrity checks passed" (`bootstrap-20260924-142012-401.log`); the migration-ID and checksum validation is recorded in the second run's log (`bootstrap-20260924-154734-902.log`). | Audit, 16 Sep |
| **P4-2** | Replacing `db_datawriter` with explicit grants left roughly 35 Phase 1–3 tables with no INSERT or UPDATE. Every import by a Store Manager would have failed after the merge. | Phase imports routed through restricted SQL write procedures. | Commit `e698773`, 16 Sep. Backed by a real folder import run as a Store Manager during the 16 Sep sprint. | Audit, 16 Sep |
| **P4-3** | The strict ACL default made the three scheduled tasks impossible to install. | Automation setup now defaults to a dedicated non-administrator account, with guards that refuse SYSTEM, the service accounts, RID-500 and any member of Administrators. | Commit `08bed85`, 16 Sep. Proven in practice on 24 Sep: all three tasks installed, the two automation ones under `EtpAutomation`, which `Get-LocalGroupMember Administrators` shows is not an administrator. | Audit, 16 Sep |
| **P4-4** | The audit-text filter rejected any digit, so "3 files imported" was refused. | Narrower pattern. | Verified live against the real stored procedure on 16 Sep: `3 files imported` and `128 rows imported.` are accepted; `C:\secret\file.xlsx`, `Invoice 100000068 for Rajesh` and a full-width-digit spoof are refused with error 51310. | Audit, 16 Sep |
| **P4-5** | `docs/INSTALL.md` and the CI workflow still showed the dropped `TrustServerCertificate=True`. | Both changed to `Encrypt=Optional`. | Read in the files on 16 Sep. **One residual is still there today:** `tools/Etp.Reporting.ImportAudit/Program.cs:10` still builds `TrustServerCertificate=true`. It is a developer tool, not shipped. | Audit, 16 Sep |
| **P4-6** | One developer script resolved `sqlcmd` from `PATH`. | Uses the shared resolver and keeps `-x`. | Read in `Invoke-EtpFunctionAudit.ps1` on 16 Sep. | Audit, 16 Sep |
| **P4-7** | `initialize-etp-operation-folders.ps1` passed a 65-character string to `New-LocalUser -Description`, which caps at 48. `-CreateAutomationAccount` aborted before granting any folder access. The path had never been executed by anyone. | Description shortened to 40 characters. | Sagar ran `New-LocalUser` with the new string on this PC on 17 Sep and it created `EtpAutomation`, enabled, outside Administrators. The account is still there and holds the SQL login this record verifies. **The fix was written by the auditor, not by Codex.** | Auditor's own run, 17 Sep |
| **P4-8** | A headless start with a bad connection string did not exit. `OnDispatcherUnhandledException` called `MessageBox.Show` — a modal dialog no installer or scheduled task can ever click. | Startup mode resolved before composition; a rejected configuration writes the reason to stderr and exits 2; headless never shows a dialog. | `HeadlessStartupFailureTests` launches the real executable for all three headless modes. Against the **pre-fix** binary it failed, with the dialog visible on screen and photographed; against the fixed binary all three pass in 3 seconds. Commit `05489e6`, 17 Sep. **Written by the auditor.** | Auditor's own run, 17 Sep |
| **P4-9** | `Resolve-EtpSqlCmd` preferred go-sqlcmd, which reaches `.\INSTANCE` over named pipes — off by default on Express and Developer. On any machine with go-sqlcmd installed, the backup, the drill and the module install all failed, behind a message blaming permissions. | ODBC client preferred; protocol settled once by probing and falling back to `lpc:`; the masked error now distinguishes "cannot reach the instance" from "permissions"; `-SqlCmdPath` added to the two scripts that lacked it. | Proven both ways on the VM with go-sqlcmd present throughout: drill exit 1 → exit 0; and with the ODBC client renamed away, drill and a full encrypted backup both exit 0. Commit `2111ba5`, 17 Sep. **Written by the auditor.** | Auditor's own run, 17 Sep |
| **P4-10** | A csproj rule copied the whole repository `scripts/` tree into the publish output — 46 scripts instead of the curated 13 — so a shop PC would receive build and signing tooling, and the release signature would cover it. A regression introduced by commit `216f919`. | The release ships only the operational scripts. | Commit `f41de93`, 17 Sep, "Fix P4-10: ship only the operational scripts with the application". | Auditor's own run, 17 Sep |
| **P4-11** | The installer offered a checkbox, ticked by default, promising to install SQL Server 2022 Express and asking the operator to accept Microsoft's licence — for an action the script never performed. The parameter it passed was declared and never read. Worse, it named Express, the edition the product then refused. | The option exists only when SQL media is actually embedded, via `-SqlPayloadDirectory`; and D9 was revised the same day so Express is supported. | Commit `514a105`, 17 Sep. The installer built on 24 Sep does embed the SQL media: it was built with `-SqlPayloadDirectory 'C:\Codex\Reporting Manger\SQL Express 2025'` (`WORKING-STATE-2026-09-22.md`, Installer section), and the artefact is 861,912,136 bytes (`artifacts/installer-70bf46e/EtpReportingEngine-Setup-1.8.8-x64.exe`, sizes and hash re-read 25 Sep). Nobody has yet run that checkbox's install path on a machine without SQL. | Auditor's own run, 17 Sep |
| **P4-12** | Setup reported a failed install as a success, and hung when run silently. | Fixed. | Commit `9d0afbb`, 17 Sep, proven end to end in the acceptance VM the same night. Confirmed again on 24 Sep, in the direction that matters: the first setup attempt **failed and said so** — exit 1603, `SETUP-INCOMPLETE.txt` written, the old tasks left alone. | Auditor's own run, 17 Sep |
| **P4-13** | The operations module could not be installed on SQL Express at all. It demanded a database master key and the backup certificate; the only product path that creates a master key refuses on Express. Without the module there is no verified backup and no recovery drill — three of Phase 4's four closure items. | Module signing separated from backup encryption. The signing certificate is protected by a password generated inside SQL and then has `REMOVE PRIVATE KEY` applied; `EtpBackupCert` is required only on editions that encrypt. | Live on this PC, 24 Sep step D. Verified by me again on 25 Sep: broker `master.dbo.etp_operations_31736fb143c6912a` carries **1 signature**; signer certificate `EtpOperationsModuleSigner_31736fb143c6912a` is `NO_PRIVATE_KEY`; `##MS_DatabaseMasterKey##` count is **0**. Commit `9547f06`, 22 Sep. | Auditor's own run, 17 Sep |
| **P4-14** | Settings > Users locked out every Store Manager and Viewer: the user procedure ran `REVOKE CONNECT` for active users. On 17 Sep, `0022`'s cursor did exactly that to `NT AUTHORITY\SYSTEM` on the **live** database, which is why the nightly backup and automation had been unable to connect since. | Migration `0032` redefines the procedure with `GRANT CONNECT` — a one-line change, the rest byte-identical to `0022` — and repairs users who were locked out. | `0032_active_users_can_connect` applied to the live database on 24 Sep at 08:50:18 UTC (`schema_migrations`, read 25 Sep). `DESKTOP-6IBM1J5\EtpAutomation` now has CONNECT **GRANT**, with `etp_store_manager`, `etp_automation` and `db_backupoperator`. Commit `9547f06`, 22 Sep. | Auditor's own run, 22 Sep |
| **P4-15** | The recovery drill was impossible for a non-administrator: the restorer becomes server owner but never the copy's `dbo`, so `DBCC CHECKDB` is refused, and `SET TRUSTWORTHY` / `DB_CHAINING` are sysadmin-only. | **Owner decision: the drill runs as the Owner.** The broker refuses the drill without sysadmin rather than pretending, and it *verifies* TRUSTWORTHY and DB_CHAINING are off instead of setting them. The drill task is registered under the Owner with RunLevel Highest. The recording of the result still goes through the automation account, so the drill cannot be used as a route from Owner to sysadmin. | Live on this PC, 24 Sep steps E2 and E3: the drill completed, left no `EtpRecovery_*` copy, and its receipt is attributed to `EtpAutomation`, not to the administrator who ran it. I confirmed on 25 Sep that live `EtpReporting` has `is_trustworthy_on 0` and `is_db_chaining_on 0`. Commit `9547f06`, 22 Sep. | Auditor's own run, 22 Sep |
| **Task registration, part 1** | `Register-EtpScheduledOperation` compared account names as text, and Task Scheduler reports `COMPUTER\User` as the bare `User`. Every task registration for a local account would have failed. Pre-existing, never hit because the tasks had never been installed. | Compared by SID; an account that cannot be resolved fails closed. | Probe with a throwaway task on this machine on 22 Sep: registered `DESKTOP-6IBM1J5\Sagar`, reported `Sagar`. Commit `9547f06`. | Auditor's own run, 22 Sep |
| **Task registration, part 2** | Setup could **never** register its own tasks. Windows demands the target account's password once, at registration, when anyone registers an S4U task for an account other than their own. An elevated administrator is refused; so is SYSTEM, which holds `SeTcbPrivilege` — so no privilege grant could have fixed it. This is why the Owner's drill task always registered and the automation account's two never did. | The automation account's tasks now go through the Task Scheduler COM API, which accepts a password: the account's password is reset to a fresh random value, used for that one call and discarded. S4U still stores no credential. | Found by the live run: first attempt 24 Sep failed at exit 1603 with `CimException: Access is denied` **after** the database half had succeeded (`bootstrap-20260924-142012-401.log`). Second attempt with the rebuilt installer: `bootstrap-20260924-154734-902.log` ends "Daily backup, monthly recovery-drill and five-minute ETP automation tasks are installed." Commit `70bf46e`, 24 Sep. A wrong earlier explanation — that the missing "Log on as a batch job" right caused it — was corrected in the same record. | **The live install itself, 24 Sep** |
| **Startup left a windowless process holding the upgrade lock** (unnumbered) | An interactive launch whose `--connection-string` was rejected let the error escape `async void OnStartup` to the dispatcher handler: a generic dialog, and then — proven by clicking OK through Win32 — a process with no window that kept running and kept the `Global\EtpReportingEngineRunning` mutex, **so setup refused to upgrade until it was killed**. The sibling of P4-8, on the interactive side. | The rejection is handled in interactive launches too: it says why and exits with code 2. | Commit `375e3b2`, 22 Sep. A test launches the real executable, dismisses the dialog through Win32 and requires exit code 2; it fails against the previous `App.xaml.cs`. | Auditor's own run, 22 Sep |

Footnote on the other P4-7 ("the branch is not pushed"): closed. `origin/recovery/opus-r1-r4` is at
`8c05318`, and `origin/main` at `849e3f2` contains the work.

### The 22 September review of the P4-13/14/15 work

Those three fixes were put through an adversarial review before they went anywhere near the live
machine: six agents, 19 raw findings, triaged to 11. It is recorded in full in
`WORKING-STATE-2026-09-22.md`. It belongs in this record because two of its findings were about
security, not tidiness, and because one of them is the reason the shop PC install still matters.

| # | Finding | Outcome |
|---|---|---|
| 1 HIGH | The drill task's sysadmin check used `IS_SRVROLEMEMBER('sysadmin', N'<login>')`, which returns NULL — neither yes nor no — for an Owner who is a SQL administrator only through `BUILTIN\Administrators`. That is exactly how the bundled SQL install sets up the shop PC. | Fixed: the session's own token when the drill runs as the account that ran setup, `EXECUTE AS LOGIN` then the no-argument form otherwise, and a `?` result refuses in words. Proven on this machine against a **temporary empty server role** holding `BUILTIN\Users` (old form NULL, new form 1) — a stand-in, not the real arrangement. **The shop PC is the first real test.** |
| 2 MED, security | The drill, now running as sysadmin, called `record_verified_operation` / `record_operational_audit` — procedures any app Owner (`db_owner`) can redefine. That is a route from Owner to sysadmin. | Fixed: recording runs `EXECUTE AS USER = <automation user> WITH NO REVERT` and refuses if the database is TRUSTWORTHY. `DrillRecordingScopeTests` plants a procedure that reports sysadmin 1 / CONTROL SERVER 1 the old way and 0 / 0 through the new function. This closes the **unattended** route only; see the note at the end of section 4. |
| 3 MED | A failed module reinstall dropped the broker, blocking the next upgrade's pre-migration backup. | Fixed: the CATCH keeps the broker and drops only the signature and signer it created. Named test. |
| 4 MED | Claimed the `0032` repair would error when granting CONNECT to the account running it. | **The premise was wrong.** See open item 8. |
| 5 MED | The install script altered the broker before validating and reading the grants template. | Fixed: both templates checked and read first. Its proof was "exercised at Sagar's live step" — that step has now run: 24 Sep step D installed the module cleanly. |
| 6 MED | The drill principal was not persisted, so a repair by another administrator moved the drill to them. | Fixed: default is the existing drill task's account unless it is a service account. Proven against the live task on this machine. |
| 7 MED | `docs/OPERATIONS.md` described the old drill account, signer and task run levels. | Fixed in that document. |
| 8–11 LOW | CONNECT missing from the grants preconditions; the `0032` repair touching users whose login is gone; the legacy shared `EtpOperationsModuleSigner` with a live private key never removed; a restore-directory placeholder escaped once but embedded twice. | All four fixed, each with a named test. |

Ten of the eleven were real and were fixed. One (finding 4) rested on a wrong premise and is
carried as open item 8 so nobody reopens it. **None of these eleven is counted in the eighteen
defects in the table above**; they are review findings on a fix, caught before release, not defects
that reached a machine.

---

## 4. What the live install proved on this PC, 24 September

In plain terms, on `DESKTOP-6IBM1J5`, the owner's own machine.

**Backups.** Before anything was touched, an independent backup was taken by hand and checked —
`EtpReporting-pre-0032-20260924-141742.bak`, 15,192,064 bytes, `RESTORE VERIFYONLY` valid, and it is still on disk today. Then setup took its
own pre-migration backup and verified it. Then, once the new build was in, the **product itself**
took a backup under the dedicated `EtpAutomation` account, through the signed module, and recorded
the receipt as `EtpAutomation` — not as an administrator. That is the whole point of P4-13: the
shop's nightly backup no longer needs anybody with full rights.

The next morning it did it again with nobody watching. The laptop had been shut down over the
22:00 slot, so the tasks caught up when it was switched on, which is how they are configured. The
backup at `07:18:56` UTC on 25 September is **the first backup this installation has ever taken on
its own**. I read the audit row and the file myself — `EtpReporting-20260925-071854-….bak`,
15,454,208 bytes. That a task started it, rather than a person, is not something an unelevated
session can see: it rests on the elevated task check recorded at 12:52 IST in
`LIVE-INSTALL-RUNBOOK-2026-09-22.md`, where all three tasks show last run 25 Sep 12:48:18 with
result `0x00000000`.

**The drill.** A recovery drill ran on 24 September: it restored the backup named in the receipt
into an isolated copy, checked it, and reported "Receipt-verified recovery drill completed", exit 0.
It left nothing behind — I checked the instance on 25 September and `EtpReporting` is the only user
database on it. The drill task itself also ran under the Owner's account with S4U and returned 0,
which answers the question left open on 22 September about whether a Microsoft-account-linked local
account can actually start such a task.

One honest limit: the drill task returned 0 again at 12:48 on 25 September, but the drill receipt
on disk is still the 24 September one. It is a *monthly* drill; a successful exit code is not the
same thing as a drill having run. The last real drill was 24 September.

**Tasks.** Three ETP tasks are installed, and **none of them runs as SYSTEM** — the first time that
has ever been true on any machine. Daily Backup and Automated Operations run as `EtpAutomation`
with a limited token; the Monthly Recovery Drill runs as `Sagar` with the highest token, because a
restore needs administrator rights and pretending otherwise was P4-15. No password is stored
anywhere for any of them.

A second honest limit about the tasks: the five-minute Automated Operations task returns 0, but
`dbo.automation_runs` still holds **only two rows, both from 26 August** — I read that on 25
September. The task records a run only when it has work to do, so on an ordinary day it exits
successfully with nothing to show. That it ran is evidenced by the task result, not by a row in
the database. Nobody has yet watched it actually import something unattended.

**Permissions on the folders.** The captures taken before and after the install are byte for byte
the same file (I compared them on 25 Sep; both are MD5 `181ce708161b18c60233cbfa9bc6b8d2`): the
six ETP folders under `C:\ProgramData\EtpReporting` are open only to Administrators, SYSTEM, the
SQL service and `EtpAutomation`. No `BUILTIN\Users`. No `Everyone`. The original finding from the
very first audit — that every unencrypted backup of the shop's sales and customer data was readable
and writable by any local account — is closed **on this PC, for these six folders**. It is not yet
closed on the shop PC, which has not been touched, and it was never checked for SQL Server's own
backup folder, where 28 `.bak` files now sit — three of them known to hold that same customer data
(open item 13).

**Who can reach the database.** Read on 25 September:

- Inside `EtpReporting`, exactly three identities have any access: `DESKTOP-6IBM1J5\Sagar` as `dbo`,
  `DESKTOP-6IBM1J5\EtpAutomation` with CONNECT granted and the roles `etp_store_manager`,
  `etp_automation` and `db_backupoperator`, and `NT AUTHORITY\SYSTEM`, whose CONNECT is now
  **DENY**. `guest` has no CONNECT.
- SYSTEM being denied is deliberate: on 24 September, once both automation tasks had run as
  `EtpAutomation`, Sagar deactivated SYSTEM in Settings > Users. `EtpAutomation` was unaffected,
  which also shows that saving one user no longer disturbs another.
- At the server level there is a `BUILTIN\Users` login, so any local Windows account can connect to
  the *instance*. It cannot reach the shop's data, because it has no user inside `EtpReporting`.
  Worth knowing; not a Phase 4 failure.
- `sa` is disabled. There is **no `BUILTIN\Administrators` login** on this instance. `Sagar` is the
  only human sysadmin; `EtpAutomation` is not a sysadmin.
- `EtpReporting` is `TRUSTWORTHY off`, `DB_CHAINING off`, recovery model SIMPLE, at migration `0032`.

**Still true, and uncomfortable:** the application runs its database work on the Owner's own
connection. An Owner who is a SQL administrator is therefore exposed to any planted code whenever
they use the app. Phase 4 closed the unattended route (the drill), not this one.

---

## 5. What the VM proved, and what it could not

The acceptance VM `ETP-Acceptance-186` was shrunk from 4 GB to 2.5 GB of memory — the host could
not spare 4 GB — and cold-booted on 24 September. It is **Off** now; I confirmed that and left it
alone.

**Read this first, because the rest of the section is easy to misread: the VM install did not
complete.** Setup failed with exit code 1603 and wrote `SETUP-INCOMPLETE.txt` (the reason is item 1
below), and the VM's own backup and its own recovery drill were therefore never run there. What the
VM did prove is two things: the provisioning script, and the second-machine restore of *this PC's*
backup. Nobody should read "the VM passed" into this section.

**Proved: the second-machine restore. This is the Phase 4 recovery evidence.** This PC's verified
backup `EtpReporting-20260924-102239-….bak` (15,454,208 bytes, recorded `encryption: NONE`) was
checked against its receipt here, copied into the VM, and checked again there — hash and size
matched both times. Inside the VM:

- `RESTORE VERIFYONLY … WITH CHECKSUM`: the backup set is valid.
- Restored under a **new name** — `EtpRestoreCheck_20260924_193201`, and `…_193257` on the second
  pass — with a `MOVE` for each of the two files into `C:\EtpSecondMachineRestore\Data`, a folder
  only Administrators, SYSTEM and the SQL service can read. Never `WITH REPLACE`. The VM's own
  database was untouched.
- `DBCC CHECKDB … WITH NO_INFOMSGS, ALL_ERRORMSGS`: silent, exit 0.
- The copy came up `ONLINE | MULTI_USER | is_trustworthy_on 0 | is_db_chaining_on 0 | read_only 0`.
- Row counts in the copy matched this PC exactly: sales_invoices 490, sales_lines 540, sales_tenders
  592, stock_movements 1079, source_lineage 3957, import_files 8, application_users 3, migrations 32,
  newest `0032_active_users_can_connect`. **I re-ran those seven counts on the live database on 25
  September and every one still matches.** Note for anyone matching this against A4.4: three of that
  criterion's four tables are in this list, but `daily_reporting_days` is not. It was never counted
  on either side.
- `msdb` in the VM recorded the source as `DESKTOP-6IBM1J5\SQLEXPRESS`, copy-only, with checksums
  and no encryptor.
- Cleanup finished, but only on the second pass: the first transcript ends "copied backup removed
  from the VM: **False**". The second one ends "Dropped: yes / Shop data left in the VM: none /
  Restore-check databases left: 0", and that is the line the cleanup claim rests on.

Raw transcripts: `C:\Users\Sagar\ETP-live-install\vm-second-machine-restore.txt` and
`vm-restore-finish.txt`. They must be read together — the first one's trust-settings query failed
on a collation conflict and the whole restore-and-check was re-run in the second. Read alone,
either one is misleading.

**Could not prove, item 1: the upgrade blocker on editions that encrypt.** The VM's SQL turned out
to be **Developer Edition 16.0.1000.6**, with a master key and `EtpBackupCert` already present. The
new installer ran and failed, exit 1603, with:

    Existing database has pending bundled migrations. Creating and verifying a pre-migration backup before any migration runs.
    FAILED: ItemNotFoundException: Cannot find path 'C:\ProgramData\EtpReporting\Backups\certificate-custody.json' because it does not exist.

On an edition that encrypts, setup's own mandatory pre-migration backup needs the certificate
custody receipts. So **an existing database on such an edition cannot be upgraded until the Owner
has exported the recovery keys**. Nothing warns about this before setup runs, and what the operator
sees is a missing-file exception rather than "export the recovery keys first". This PC and the shop
PC both run Express, where backups are unencrypted by the revised D9, so neither is affected today.
The VM's own backup and drill were blocked by the same thing.

**Could not prove, item 2: the group-only administrator case.** The VM's `ETPTest` account holds its
own sysadmin login, so the VM does **not** reproduce the shop PC's arrangement, where the Owner is a
SQL administrator only through membership of `BUILTIN\Administrators`. Sagar declined to have that
simulated in the VM. The 22 September review's highest finding — that the drill's sysadmin check
returned NULL in exactly that case — was fixed and proven against a temporary empty server role, but
the real thing is untested. **The shop PC is the first real test of it.**

Provisioning itself passed: `initialize-etp-operation-folders.ps1 -CreateAutomationAccount` created
`DESKTOP-IPT0J6H\EtpAutomation`, enabled and not an administrator, wrote `operations.json` and
protected the folders — once it was started as `powershell.exe -ExecutionPolicy Bypass -File …`.
Started with `&` it was refused, for the second time on a second machine.

---

## 6. What is still open

| # | Open item | Owner | Next action |
|---|---|---|---|
| 1 | **The shop PC install.** Still on `1.8.1+8e35d83` — that is `docs/audit/WORKING-STATE-2026-09-22.md`, its "Resume here (saved 24 Sep, ~21:05 IST)" section, repeating what that note says; I cannot see that machine from here, so it is not a measurement. It is the only machine with the group-only administrator arrangement, so it is also the real test of the drill-task fix. | Sagar | Follow `docs/audit/LIVE-INSTALL-RUNBOOK-2026-09-22.md` in a window when the shop is closed, using `artifacts/installer-70bf46e/EtpReportingEngine-Setup-1.8.8-x64.exe` (861,912,136 bytes; SHA-256 recorded in `SHA256SUMS.txt` beside it as `0130EEB3…B46703`). Capture tasks and ACLs before and after, as was done here. |
| 2 | **Developer/Standard cannot upgrade until recovery keys are exported.** Setup stops at its own pre-migration backup with a missing-file error. | **Fixed 25 Sep 2026** | Refused in words, in three places. `Resolve-EtpLatestCertificateCustody` now says "This SQL Server edition encrypts backups, and no exported recovery keys were found", naming Settings > Database > Encrypted backup recovery keys — that covers setup, the nightly task, the rollback script and the app's own Backup button. Setup checks the same thing before it takes the pre-migration backup and refuses with "The database has not been changed". A clean install on an encrypting edition takes no pre-migration backup, so it cannot be refused there; it now ends with a warning that the daily backup will refuse until the keys are exported. The unencrypted-fallback option was rejected: the edition probe already fails closed rather than silently produce an unprotected backup, and this would have undone that. Test: `CertificateBinding`, which asserts the refusal names the Settings path. **Still unexercised on an encrypting edition** — both live machines run Express. |
| 3 | **`docs/OPERATIONS.md` is wrong in three ways, and it is the shop's own operations document.** (a) Lines 50 and 62 use `& 'C:\Program Files\…\script.ps1'`; I confirmed on 25 Sep that Windows PowerShell on this PC has no execution policy set at any scope, so the default `Restricted` applies and those commands are refused as written. It has now bitten on two machines. (b) Its "Deployment status" section still says "This coding task has not configured the live database, changed its folder permissions, installed scheduled tasks" and that "second-machine recovery have not been verified" — all three were done on 24 September. (c) It files the unsigned-release decision under "**A4.3** decided", which is the scheduled-tasks criterion; the decision belongs to A4.7. | **Fixed 25 Sep 2026** | All three. Both commands are now `powershell.exe -ExecutionPolicy Bypass -File '…'`, with a sentence saying why and distinguishing it from setting the machine's policy to `Bypass`, which step 2 still rules out. The deployment-status section says what was done, on which machine and on what date, and says plainly that the shop PC has had none of it; the table now carries state rather than "still required", including that the SQL backup folder's ACL has never been captured. A4.3 is corrected to A4.7 in both places. A grep confirms no `& '…ps1'` form remains in the file. |
| 4 | **Setup's pre-migration backup is deleted by the same day's rotation.** Rotation keeps the newest **receipted** backup per UTC day for 14 days, plus the newest per calendar month for 12 (`Get-EtpRetainedBackupReceipts`, `scripts/etp-operations-common.ps1:227`); a `.bak` with no valid receipt is never touched, which is why several same-day files from August are still sitting there. Confirmed on 25 Sep by listing the folder through SQL: setup's `EtpReporting-20260924-085014-e3136d58…bak` is **gone**; only the later `…-102239-…bak` from that day survives, alongside the new `…-20260925-071854-…bak`. | **Fixed 25 Sep 2026** | A backup receipt now records a `purpose`: `SCHEDULED`, `PRE_MIGRATION` (setup) or `PRE_ROLLBACK` (`invoke-release-rollback.ps1`, which had the identical hazard). Rotation deletes only `SCHEDULED` ones, and a safety backup does not occupy a day or month slot, so taking one never shortens the ordinary history. A receipt from an older build has no `purpose` and counts as scheduled; one this build cannot interpret is **kept**, because the cost of keeping a file is disk and the cost of deleting it is the database. Not a schema bump, deliberately, so a machine still running the previous scripts reads these receipts. Seven assertions in the `Retention` scenario, including the live 24 Sep case; they fail against the previous code. **Open for Sagar:** safety backups now accumulate, one per upgrade, each the size of the database. `docs/OPERATIONS.md` says to delete one by hand once its upgrade is proven. Whether the product should cap them automatically is an owner decision, and I have not picked a number. |
| 5 | **Settings > Users does not show why a save failed.** The Users task shows the fields and the two tables but not the status line where the failure message is written. | **Fixed 25 Sep 2026** | It was worse than the item says: **four of the five layouts** on that screen left the status line out — Users, Import profiles, Calculations and Stores — and only Database health included it. The same control carries every refusal on all of them, including "Master value was not saved" and a failed refresh, so a Store or a KPI save was just as silent. The five layouts are now one `TaskNavigator.AdministrationTaskLayout` method, internal so a test can read it, and all five include child 14. Two tests: one asserts every layout keeps it, the other builds the real view, applies the real layout, fails a user save and requires the message to be findable in the task's own tree — not merely in the control, which is what `StatusText` reads whether or not it is on screen. Both fail against the previous arrays. **Not yet seen on a machine**: this is a test, not an observation. |
| 6 | **A4.4's row-count block and A4.4a do not exist.** The plan asked for four table counts in the backup receipt and in the drill result, and for a tampered-receipt test. No code was written. | Sagar to route; then coding | Either implement it (record `COUNT(*)` for `sales_invoices`, `sales_lines`, `import_files`, `daily_reporting_days` immediately before `BACKUP`, compare in the drill, fail naming the table and both numbers) or amend the plan. Do not leave it silently unmet. |
| 7 | **A4.7 and the plan disagree.** Plan v1.4 requires self-signed Authenticode signatures on the exe, every packaged script and the installer, `AllSigned`, two SHA-256 values in `CHANGELOG.md`, and a "More info → Run anyway" step in `docs/OPERATIONS.md`. The 17 Sep decision accepted unsigned releases and moved the deployed policy to `RemoteSigned`. Measured today: the installed exe and the packaged `backup-etp-database.ps1` are both unsigned, the CHANGELOG has no hashes, and OPERATIONS.md has no "Run anyway" step. | Sagar | Amend the plan's Phase 4 task 6 and A4.7 to match the decision, with the date, or reverse the decision. Whichever way it goes, the CHANGELOG hashes and the OPERATIONS.md wording are small and should be done regardless — they do not depend on signing. Right now the record contradicts itself. |
| 8 | **A review finding that was wrong — nothing to do.** Finding 4 claimed the `0032` repair would *error* when granting CONNECT to the account running it. It does not: SQL skips the statement with a "cannot grant … to yourself" warning. The exclusion was kept anyway, to keep the upgrade log clean, and the misleading comment was corrected. **Listed only so a later reader does not reopen it.** | — | None. |
| 9 | **Shared-memory transport error in the SQL integration project — still open, but no longer a mystery about *which* test.** It is not one test. First seen in `ScopedImportDuplicateSqlTests` on 22 September 2026; on 25 September it appeared twice in five runs and the name was captured the second time: `PhaseOneUpgradeSqlTests.Existing_calendar_year_invoices_upgrade_without_rewriting_old_migrations_and_backfill_signed_tax`, failing after 133 ms with `A transport-level error has occurred when receiving results from the server (provider: Shared Memory Provider, error: 0 — The I/O operation has been aborted because of either a thread exit or an application request.)` Soaks later that day put the same error in `DatabaseRecoveryHealthTests`, `PhaseOneImportSqlTests`, `ImportRetryClosureTests`, `PhaseFiveAccountingTests` and `AccountingMappingAtomicityTests` — **seven distinct test classes in one afternoon**. Measured rate on the unmodified tree: **3 failures in 11 runs, about one in four.** **The pattern, across every occurrence: a different test each time, always this exact transport error, always within 10-220 ms — at connection time, not in the test's own work.** So it is infrastructure, not any one test's logic, and the project runs its classes in parallel with only two collections serialised. | **Open. Do not dismiss** | **A hypothesis was tried and refuted, which is the useful part of this row.** `SqlConnection.ClearAllPools()` is called in two teardowns (`SqlDatabaseFixture:35`, `PhaseOneUpgradeSqlTests:111`) and is process-wide, so it looked like the obvious culprit: one class finishing with its scratch database would physically close pooled connections another class had just taken. Replacing both with `SqlConnection.ClearPool()` for the scratch connection string alone made it **much worse, not better — 6 of 6 runs failed, against 3 of 11 on the unmodified tree** (Fisher exact p ≈ 0.0007, so not noise). The change was reverted and the baseline re-measured afterwards to confirm the tree was back where it started. That result is evidence in its own right: **the global pool clear is protective**, and whatever the real mechanism is, it involves pooled connections being kept alive rather than being closed too early. The next attempt should start from there, and should measure over at least six runs, because anything less cannot tell a fix from a quiet afternoon. |
| 10 | **Residual from P4-5.** `tools/Etp.Reporting.ImportAudit/Program.cs:10` still builds `TrustServerCertificate=true` and bypasses the connection policy. Developer tool, not shipped. | Low priority | Route it through the shared policy like everything else. |
| 11 | **Pre-existing, recorded not fixed.** Re-saving the automation account in Settings > Users strips its `etp_automation` and `db_backupoperator` roles. A fresh install run as SYSTEM still fails at migration `0012`. The app's Operations buttons (backup, recovery drill) cannot work from an unelevated app, because the Backups and Operations folders are readable only by Administrators, SYSTEM, the SQL service and the automation account. | Recorded | Decide later whether any of these is worth fixing; none blocks the shop. |
| 12 | **Not Phase 4, carried here only so they are not lost: D14 (Phase 2) and P3-3 (Phase 3).** Both await Sagar. See section 7. **Neither gates Phase 4 closure**, and neither appears in the closure conditions in section 8. | Sagar | Answer both, whenever Phase 2 and Phase 3 are closed formally. |
| 13 | **Unencrypted customer data sits in a folder nobody has checked, and there is more of it than expected.** A4.2 and its evidence cover the six `C:\ProgramData\EtpReporting` folders. But the independent pre-upgrade backups (`pre-0031`, `pre-0032`) and the 47.5 MB `EtpReportingHelios-archive-20260924-140925.bak` were deliberately put in SQL Server's own backup folder, `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup`. Those three are unencrypted and hold real customer names and phone numbers (D3). Listing that folder through SQL on 25 Sep shows it actually holds **28 `.bak` files**, the rest of them older test and validation databases (`EtpReportingSprintValidation*`, `EtpReportingFunctionAudit*`, `EtpReportingUiSprint*`, `EtpReportingReview*`, `EtpReportingCompletionValidation*`) going back to 26 August; several were built from real imports and nobody has checked what is in them. The folder's permissions have never been captured, before or after. The one thing I can say: on 25 Sep an **unelevated** Owner could not list it (access denied), which is what a sensible default looks like — but that is an inference, not the ACL. | Sagar, elevated — five minutes | Run `icacls` on that folder and file the output beside `acl-after.txt`. If `BUILTIN\Users` appears, fix it the way the ProgramData folders were fixed. Then decide what the 25 leftover test backups are doing on the owner's machine. Do the same on the shop PC. |
| 14 | **D9 and the plan disagree too — the same problem as A4.7, on the other decision.** The plan on `main` still records D9 as "SQL backup encryption with a certificate", with the certificate exported to two off-PC locations and a second-machine restore performed *from the exported certificate*. Its own Section 7 says D9 needs an owner revisit and offers two ways out: a paid edition, or file-level encryption of the `.bak` after `BACKUP` with the same custody rules. **What was actually done on 17 September is neither**: on Express the backup is written unencrypted and protected only by the folder policy. The code does this honestly and fails closed, and `docs/OPERATIONS.md` says plainly that nothing in the file protects it — but the plan still says something else. | Sagar | Amend D9 to record the 17 Sep position with its date, or choose one of the plan's two alternatives. Same paperwork as item 7, same reason: an audit record should not close over a contradiction. |
| 15 | **A backup folder that filled up could never empty itself.** Found on 25 September 2026 by a second reviewer checking the item 4 fix, and **older than that fix** — item 4 only makes the limit arrive sooner. Rotation ran only at the end of a successful backup (`backup-etp-database.ps1`, and `Get-EtpRetainedBackupReceipts` has exactly one non-test caller), while the free-space check threw before the backup started. So once the drive fell below `MinimumFreeSpaceGb`, the backup refused, rotation never ran, the folder could not shrink, and every night after that failed identically until somebody deleted files by hand. Not silent — the task fails, no `Backup`/`Succeeded` audit row is written and backup age shows in System status — but it needs somebody to look. | **Fixed 25 Sep 2026** | Rotation is extracted into `Invoke-EtpBackupRotation` and the free-space refusal now calls it first, re-checks, and refuses only if that was not enough, saying how much it reclaimed. It deletes nothing a successful backup would have kept and no safety backup at all, so it cannot trade a recovery point for disk space. Eight on-disk assertions in `Retention`, plus a structural one: **the ordering itself cannot be exercised in that harness**, because reaching the free-space check needs a live SQL connection for the edition probe, so the test parses the script and requires the rotation call to precede the throw inside the same guard. It fails against the previous one-line guard. The behaviour on a genuinely full drive has not been observed. |

---

## 7. Decisions

**Taken.**

| Date | Decision |
|---|---|
| 17 Sep 2026 | **D9 revised for Express.** Express and Web refuse `BACKUP … WITH ENCRYPTION`, so on those editions the backup is taken unencrypted and the receipt records `encryption: NONE` rather than claiming AES-256. The edition probe fails closed: an unrecognised answer refuses the backup instead of quietly producing an unprotected one on an edition that could have encrypted it. Commit `514a105`. Visible today in the live audit row: "Checksum backup verified, not encrypted". **The plan's D9 was not amended to match; see open item 14.** |
| 17 Sep 2026 | **Unsigned releases accepted**, and the deployed execution policy moved from `AllSigned` to `RemoteSigned`, because `AllSigned` on an unsigned install would refuse the nightly backup and the monthly drill — and the shop would find that out only when a restore was needed. Commit `514a105`. The allow-list and the protected-install check still refuse any script a non-administrator could edit. **The plan was not amended to match; see open item 7.** |
| 22 Sep 2026 | **P4-15: the recovery drill runs as the Owner.** A restore needs administrator rights; the alternative was to pretend otherwise. The drill refuses without sysadmin rather than failing obscurely, and its result is still recorded through the automation account so the drill is not a route from Owner to sysadmin. |
| 24 Sep 2026 | **Merge to `main`.** Sagar approved it. Two merges that day: `870c9ab` carried the tested code, and `849e3f2` added the live-install record. The merge carried the code byte-identically; only main's own planning documents differed. |
| 24 Sep 2026 | **`EtpReportingHelios` archived and dropped.** It was the Phase 1 import-audit database, built 15 Sep from the two-year Helios workbooks, never written to since, twelve migrations behind, never backed up, and not the database the app points at. Checks first: no scheduled task and no `operations.json` names it, nothing was connected, and all 58 source workbooks are still on disk. Archived copy-only with CHECKSUM and `RESTORE VERIFYONLY` valid — `EtpReportingHelios-archive-20260924-140925.bak`, 49,864,704 bytes, still present in the SQL backup folder on 25 Sep — then dropped. `EtpReporting` is now the only user database on the instance; I confirmed that on 25 Sep. |
| (earlier) | **A3.8, the touch walkthrough, accepted on the owner's report.** A **Phase 3** criterion, not Phase 4; recorded here only because it was accepted during this stretch of work. Sagar performed it; the auditor did not witness it and took no screenshot. Recorded as the owner's observation, not as a measurement. |

**Outstanding.** The first two are other phases' business and are listed only so they are not
lost; they do not gate Phase 4. The last two are Phase 4's own.

| ID | Phase | Question | Recommendation on record | Waiting on |
|---|---|---|---|---|
| **D14** | Phase 2 | The DSR and every other PDF read `segoeui.ttf` from the Windows Fonts folder. Accept that, or bundle a font? | **Accept Windows' Segoe UI** (Microsoft's licence forbids shipping the file), **but fix how a missing font is reported first** — today it surfaces as a generic error with only a `NullReferenceException` in the log, because PDFsharp swallows the clear message the code already throws. About half a day. Full working: `docs/audit/D14-FONT-DECISION-2026-09-22.md`, 22 Sep. | Sagar |
| **P3-3** | Phase 3 | The status footer shows one line and a hidden-line count, not the three-line wrap the plan's task 8 asks for. | **Keep the one-line footer and amend plan task 8.** A three-line footer drops the content area to 598 DIP, below the 610 DIP floor the same plan sets. The auditor applied this amendment to the branch's plan copy under Sagar's instruction to finish without further input; it is a one-line revert if he disagrees, and it is not on `main`'s copy. | Sagar |
| **A4.7 / signing** | Phase 4 | See open item 7. | Amend the plan to match the 17 Sep decision, or reverse the decision. | Sagar |
| **A4.4 row counts / A4.4a** | Phase 4 | See open item 6. | Implement or amend; do not leave it unmet in silence. | Sagar |
| **D9 / backup encryption** | Phase 4 | See open item 14. | Amend D9 to match what Express actually does, or choose an edition or file-level encryption. | Sagar |

---

## 8. Can Phase 4 be called closed?

**No — not yet, and the reason is specific.**

Three of the five things section 1 said Phase 4 had to make true have been made true and **observed
on a real machine**: the folders are locked down (thing 2), the three scheduled tasks exist with
nothing running as SYSTEM (thing 4), and the product takes its own verified backup under a
dedicated non-administrator account, the recovery drill passes and leaves nothing behind, migration
`0032` has unlocked the accounts that `0022` had locked out, and that backup restored cleanly onto
a second machine with the row counts matching (thing 3). That is the substance of the phase, and it
is real.

The other two have **not** been watched on a machine. Thing 1 — only the right people can change
the figures, and a closed day cannot be quietly reopened — rests on the A4.1 tests of 16 September.
Thing 5 — the app can only talk to the SQL Server on its own PC — rests on the A4.6 tests of the
same day. Both were run by the auditor rather than read, and the day lock has been carried by the
live database under `0022` and now `0032` without incident, which is worth something. Neither is
the same as sitting at the live database as a Viewer, or typing `Server=remotehost` into the app's
settings, and nobody has done either.

It is not the whole of what the plan asks for. Three of the eight written criteria are not met:
**A4.4** (half of it was never built, and the half that works was never shown on the System status
screen the criterion names), **A4.4a**, and **A4.7**. A fourth, **A4.3**, passes with a departure
the owner chose. Those are set out in section 2 and are not swept into the paragraph above.

Eighteen defects were found and fixed along the way — the fifteen numbered P4-1 to P4-15, two
unnumbered task-registration defects, and the startup-mutex defect — and **twelve of the eighteen
were found by running the software rather than by reading it**, including one, on the day of the
live install, that meant setup could never have registered its own scheduled tasks on any machine.
One of the eighteen, P4-5, still has a residual that is open (item 10). A further eleven findings
came out of the 22 September review of the P4-13/14/15 fixes; ten were real and were fixed before
any of that code reached a machine.

What closing waits on is this:

1. **The shop PC install has not happened.** That machine is the one the staff actually use, it is
   still on `1.8.1+8e35d83`, and it is the only machine where the Owner is a SQL administrator only
   through `BUILTIN\Administrators`. The 22 September review's highest finding was precisely about
   that case, and it has been fixed and tested against a simulated equivalent — never against the
   real thing. Until that install runs and its tasks, ACLs, backup and drill are captured the way
   this PC's were, Phase 4 is proven on the auditor's own machine — plus the one piece, restoring
   onto a different machine, that the VM proved.
2. **Three of the plan's eight acceptance criteria are not met**: the row-count half of A4.4 and all
   of A4.4a, which were never implemented, and A4.7, which the owner's own 17 September decision
   overrides in part but which nobody has written down in the plan. A4.4 and A4.4a are code; A4.7 is
   mostly paperwork, with two small pieces of work in it (the CHANGELOG hashes, the OPERATIONS.md
   wording) that do not depend on signing. None is hard. All three are currently a contradiction
   between what the plan says and what the product does, and an audit record should not close over a
   contradiction. D9 is a fourth such contradiction (item 14), on the same pattern.
3. **The VM did not finish.** Its setup failed at exit 1603 and it never took its own backup or ran
   its own drill. It is evidence for the second-machine restore and for the provisioning script, and
   for nothing else. Saying "it worked in the VM" would be false.
4. **One folder holding customer data has never been checked** (item 13) — and it holds 28 backup
   files, not the three anybody had in mind. Five minutes, elevated, on both machines.

**Phase 4 can be declared closed when: (a) the shop PC install is completed and its evidence
captured; (b) Sagar rules on A4.7, on A4.4's row counts and on D9, and the plan is amended to say
whatever he decides; (c) ~~the four small product fixes in section 6 — the OPERATIONS.md corrections,
the pre-migration backup rotation, the encrypted-edition upgrade message, and Settings > Users
showing why a save failed — are either done or explicitly deferred with a date~~ **— done, 25
September 2026; see the addendum below**; and (d) the SQL backup folder's permissions are captured
on both machines.** Nothing on that list is large. Until it is done, the honest description is:
**Phase 4 is working and observed on the owner's PC, part proven in the VM, and untried on the
shop's.**

### Addendum — 25 September 2026: condition (c) is met

The four product fixes in section 6, items 2 to 5, are done, and each of those rows now records
what was changed and how it was tested. Two things a later reader should not have to dig for.

**Item 5 was bigger than it was written up as.** The report said Settings > Users did not show why
a save failed. In fact **four of the five layouts** on that screen left the status line out — Users,
Import profiles, Calculations and Stores — and only Database health included it. Every refusal on
that screen, including a failed master-data save and a failed refresh, is written to that one
control, so a Store or a KPI save was silent in exactly the same way. Fixing only the reported
screen would have left three others broken.

**A fifteenth defect came out of the fixes.** A second reviewer, checking item 4, found that a
backup folder which had filled up could never empty itself: rotation ran only after a successful
backup, and the free-space check refused before the backup started. It is older than item 4 — item
4 only makes the limit arrive sooner — and it is now item 15, fixed the same day. That is the
second time in this phase that reviewing a fix found a defect the fix had not caused, and both
times the defect was in the path nobody had run.

**A caution about every "green gate" in this record, including the ones above.** The SQL
integration project fails intermittently with a shared-memory transport error (item 9) at a rate
measured on 25 September at 3 runs in 11, about one in four. A single green gate on this machine therefore
does not mean much on its own, and a single red one does not mean a regression. Where this record
says a gate passed, it means that run passed. Where a change needed more confidence than that, it
was soaked over six runs and the record says so.

**Two things are deliberately left for Sagar, not decided here.** Safety backups now accumulate —
one per *attempt*, not one per upgrade, because a setup run that fails after taking its
pre-migration backup leaves that copy behind — each the size of the database; whether the product
should cap that automatically is an owner decision and I have not picked a number. And the
encrypted-edition refusal has never been exercised on an edition that encrypts — both live machines
run Express — so it is tested, not observed.

These are tests and code, not machine evidence. **None of it changes the one-line state**: Phase 4
is still working and observed on the owner's PC, part proven in the VM, and untried on the shop's.
Conditions (a), (b) and (d) are unchanged and all three are Sagar's.
