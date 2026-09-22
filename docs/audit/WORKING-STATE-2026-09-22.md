# Working state — 22 September 2026

The state of the P4-13/14/15 work and the review that followed it. Read this first when
continuing.

- Worktree: `C:\Codex\Reporting Manger\opus-recovery`, branch `recovery/opus-r1-r4`
- Everything below is committed on that branch; see the commit that adds this file.

## Fixed and proven on real SQL Server Express (this machine)

| Defect | Fix | Where |
|---|---|---|
| **P4-13** operations module impossible to install on Express (needed a master key; the only product path that creates one refuses on Express) | Signing cert protected by a password generated inside SQL (`CRYPT_GEN_RANDOM`), broker signed, then `REMOVE PRIVATE KEY`. One signer per broker. `EtpBackupCert` only on editions that encrypt. Grants/preconditions moved to a template | `scripts/sql/etp-operations-grants.sql` (new), `scripts/install-etp-sql-operations.ps1` |
| **P4-14** Settings > Users locked every Store Manager/Viewer out: `configure_application_role` did `REVOKE CONNECT` for active users; `0022`'s cursor did it to `NT AUTHORITY\SYSTEM` on the live DB on 17 Sep | Migration `0032`: procedure redefined with `GRANT CONNECT` (one-line change, rest byte-identical to 0022) + repair of locked-out active users | `database/migrations/0032_active_users_can_connect.sql` (new) |
| **P4-15** recovery drill impossible for a non-sysadmin (restorer becomes server owner but never the copy's `dbo`, so `DBCC CHECKDB` is refused; `SET TRUSTWORTHY/DB_CHAINING` also sysadmin-only) | **Owner decision: drill runs as the Owner.** Broker DRILL requires sysadmin (THROW 51334), verifies TRUSTWORTHY/DB_CHAINING are off instead of setting them. Signer no longer gets ALTER ANY DATABASE. Drill task registered under the Owner, RunLevel Highest | `scripts/sql/etp-operations-broker.sql`, `scripts/install-monthly-recovery-drill-task.ps1`, `scripts/etp-operations-common.ps1` |
| Dropdown test failed on a 3440x1440 monitor (the test read the live screen) | `DropDownHeightFor(items, screenHeight)` overload; tests pinned to the shop's 768 px panel | `src/Etp.Reporting.Desktop/MainWindow.Shell.cs`, `TaskNavigationTests.cs` |

## Review of that work — all findings dealt with

Adversarial review (6 agents, 19 findings, triaged to 11):

| # | Finding | Outcome | Proof |
|---|---|---|---|
| 1 HIGH | Drill-task sysadmin check used `IS_SRVROLEMEMBER('sysadmin', N'<login>')`: NULL for an Owner who is sysadmin only through `BUILTIN\Administrators` (how the bundled SQL install sets it up) | Fixed: this session's own token when the drill runs as the account running setup; `EXECUTE AS LOGIN` then the no-argument form otherwise; `?` result refuses in words | Harness on this machine: self 1, EtpAutomation 0, unknown account refused; group case via a temporary empty server role holding `BUILTIN\Users`: old form NULL, new form 1 |
| 2 MED security | Drill (now sysadmin) ran `record_verified_operation` / `record_operational_audit`, which any app Owner (db_owner) can redefine: Owner → sysadmin | Fixed: new `Invoke-EtpSqlAsAutomationUser` runs them `EXECUTE AS USER = <automation DB user> WITH NO REVERT`, refuses if the database is TRUSTWORTHY | `DrillRecordingScopeTests`: planted procedure reports sysadmin 1 / CONTROL SERVER 1 when called the old way, 0/0 through the function; REVERT refused; recording still works and is attributed to the automation account |
| 3 MED | Failed module reinstall dropped the broker, blocking the next upgrade's pre-migration backup | Fixed: CATCH keeps the broker, drops the signature and signer it created | `A_failed_install_keeps_the_broker_for_an_administrator_and_leaves_no_signer` (includes a sysadmin backup through the kept broker) |
| 4 MED | 0032 repair could `GRANT CONNECT` to the account running it; review said SQL errors | **Premise wrong**: SQL does not error, it skips the statement with a "cannot grant ... to yourself" warning. Exclusion kept to keep the upgrade log clean; comment corrected | `The_0032_repair_steps_over_the_account_running_it` (asserts the warning appears for a direct self-grant and not for the repair) |
| 5 MED | Install script altered the broker before validating/reading the grants template | Fixed: both templates checked and read first | Code order; install path itself is exercised at Sagar's live step |
| 6 MED | Drill principal not persisted; a repair by another admin moved the drill to them | Fixed: default is the existing drill task's account unless it is a service account; else the account running setup | Harness: live task (reported as bare `Sagar`) resolves to `DESKTOP-6IBM1J5\Sagar` |
| 7 MED | `docs/OPERATIONS.md` described the old drill account, signer, task run levels | Fixed: steps 7-9, module section, task table | — |
| 8 LOW | Grants preconditions checked Store Manager but not CONNECT | Fixed: refused with "apply migration 0032" | `An_automation_account_that_cannot_connect_is_refused_before_anything_is_signed` |
| 9 LOW | 0032 repair touched users whose login no longer exists | Fixed: `SUSER_ID(u.windows_identity) IS NOT NULL` | — |
| 10 LOW | Legacy shared `EtpOperationsModuleSigner` (live private key, ALTER ANY DATABASE) never removed | Fixed: removed once it signs nothing | Main signing test plants one and asserts it is gone |
| 11 LOW | `__RESTORE_DIRECTORY__` escaped once but embedded two literals deep | Fixed: held in `@restoreDirectory`, `REPLACE`-escaped where embedded | Main signing test now drills through a folder named `..._it's` |

Found while fixing, also fixed:

- **Every task registration would have failed.** `Register-EtpScheduledOperation` compared account names as text, and Task Scheduler reports `COMPUTER\User` as the bare `User`. Probe with a throwaway task on this machine: registered `DESKTOP-6IBM1J5\Sagar`, reported `Sagar`. Pre-existing; it would have stopped setup at its task step on any local account. Now compared by SID (`Resolve-EtpAccountSid`), and an unresolvable account fails closed.
- **Module install refusals were unreadable.** `Invoke-EtpSql` hides SQL errors, so "Add X as a Store Manager in Settings > Users first" arrived as "The database operation failed". Precondition refusals now come back as one `ETP_MODULE_REFUSED:` line that the installer shows as it is.
- Stray byte-order marks on six files from an earlier editing tool removed so diffs show only real changes.

Record only (not fixing): re-saving the automation account in Settings > Users strips `etp_automation`/`db_backupoperator` (pre-existing, documented); fresh install run as SYSTEM already fails at migration `0012` (pre-existing); 0032 gives SYSTEM access back — deactivate SYSTEM in Settings > Users once the tasks run as `EtpAutomation`; the app itself runs database code as the Owner's own connection, so an Owner who is a SQL administrator is exposed to planted code whenever they use the app — finding 2 closes the unattended drill route only.

## Gate (22 Sep, after all fixes)

Release build 0 warnings 0 errors. Desktop 415 passed (2 skipped); Domain 12; Import 110; Reporting 63; SQL integration 100 passed (1 skipped); SQL unit 239. Two one-off failures, each passed on rerun and in untouched code: `ScopedImportDuplicateSqlTests` (shared-memory transport error 0.6 s into the run) and `Phase3ShellTests` (WPF collection accessed concurrently). No test residue in `master`.

## Phase status and decisions

- Phases 0, 1: closed. Phase 2: closed on coding 17 Sep; open: **D14** (DSR PDF reads `segoeui.ttf` from the Windows Fonts folder — accept or bundle) and a formal closure record.
- Phase 3: closed on coding 17 Sep. **A3.8 touch walkthrough: done by Sagar** (his observation). **P3-3:** recommendation given — keep the one-line footer and amend plan task 8 (a 3-line footer drops content to 598 DIP, below the 610 floor); awaiting Sagar's decision.
- Phase 4 remaining:
  1. merge to `main`; rebuild installer (`artifacts/installer-<sha>`, with `-SqlPayloadDirectory 'C:\Codex\Reporting Manger\SQL Express 2025'`)
  2. this machine (Sagar, elevated): fresh backup; install the new build (applies 0032 — gives SYSTEM back its CONNECT); add `EtpAutomation` as Store Manager in Settings > Users; install the operations module; re-register tasks (backup/automation on `EtpAutomation`, drill on the Owner); evidence: verified backup + recovery drill; ACL before/after check
  3. VM `ETP-Acceptance-186`: Sagar runs `initialize-etp-operation-folders.ps1 -SqlServiceIdentity 'NT Service\MSSQL$SQLEXPRESS' -CreateAutomationAccount`, then install, then second-machine restore. The VM's bundled SQL makes the Owner sysadmin through `BUILTIN\Administrators`: the first real run of review finding 1's group path
  4. decide `EtpReportingHelios` (at `0020`): live or leftover
  5. the startup `DISPATCHER_UNHANDLED` / `ArgumentException` dialog — pre-existing, reproducible, undiagnosed
  6. shop PC (still `1.8.1+8e35d83`) last

## Live machine facts

- `EtpReporting` at `0031`; `NT AUTHORITY\SYSTEM` has **no CONNECT** (so Automated Operations and Daily Backup, which run as SYSTEM, cannot connect since 17 Sep). Owner `DESKTOP-6IBM1J5\Sagar` fine (explicit sysadmin login, db_owner). There is no `BUILTIN\Administrators` login on this instance.
- `EtpAutomation` local account exists (enabled, not an administrator) but has **no SQL login yet** — Settings > Users has not added it.
- Half-installed broker `master.dbo.etp_operations_31736fb143c6912a` (unsigned). No master key. Verified pre-upgrade backup `EtpReporting-pre-0031-20260917-151407.bak` in the SQL backup folder — keep it.
- Scheduled tasks: Automated Operations and Daily Backup run as SYSTEM (older mechanism); Monthly Recovery Drill runs as `Sagar`, Interactive, Limited. Re-registering with this build moves the drill to S4U + Highest under the same account.
- Live `settings.json` points at `EtpReporting` — keep it that way. Screen is 3440x1440.

## Rules still in force

Never write to live `EtpReporting` except through Sagar's elevated steps; never touch Codex's worktrees (nor another session's worktree: `main` is checked out in one); never edit a committed migration; test residue in `master` must be cleaned (check with `scratchpad/recovery/residue.ps1`); integration tests use `Pooling=false` and non-masking cleanup.
