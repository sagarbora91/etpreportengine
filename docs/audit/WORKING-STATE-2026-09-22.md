# Working state — 22 September 2026

The state of the P4-13/14/15 work and the review that followed it. Read this first when
continuing.

## Resume here (saved 22 Sep, ~18:45 IST)

- Branch `recovery/opus-r1-r4` pushed at `375e3b2` (on top of `9547f06`). `main` untouched at `714d117`.
- Sagar **saved the VM** `ETP-Acceptance-186` to free memory (available RAM went 1.4 GB → 5.2 GB). Resume it with `Start-VM -Name 'ETP-Acceptance-186'` once the installer is built.
- **Running when saved — check their results first:**
  1. Background task `task_61b988d7` "Fix two intermittently failing tests" (separate session and worktree): `ScopedImportDuplicateSqlTests` transport error, `Phase3ShellTests` concurrent-collection race.
  2. Background task `task_b380b90b` "Stop tests writing to the owner's diagnostics log" (separate session and worktree): may change `src/Etp.Reporting.Desktop/DesktopDiagnostics.cs`, so it belongs in the installer.
  3. Workflow `wf_55b339dd-669` (4 prepare agents + 4 verifiers), writing, in this worktree: `docs/audit/LIVE-INSTALL-RUNBOOK-2026-09-22.md`, `docs/audit/VM-AND-SECOND-MACHINE-RUNBOOK-2026-09-22.md`, `docs/audit/ETPREPORTINGHELIOS-FINDINGS-2026-09-22.md`, `docs/audit/D14-FONT-DECISION-2026-09-22.md`, and the read-only check script `scratchpad/recovery/live-verify.ps1`. If it was cut off, resume it: `Workflow({scriptPath: "C:\Users\Sagar\.claude\projects\C--Codex-Reporting-Manger-opus-recovery\38f855f7-3921-4259-b901-2a97df36d8cc\workflows\scripts\phase4-live-prep-wf_55b339dd-669.js", resumeFromRunId: "wf_55b339dd-669"})`. Review its four documents before handing them to Sagar, then commit them.
- **Next, in order:** (1) Sagar's answers: merge to `main` yes/no; P3-3 (recommended: keep the one-line footer, amend plan task 8); D14 (after the workflow's findings); `EtpReportingHelios` (after the workflow's findings). (2) Review and merge both background tasks' branches into `recovery/opus-r1-r4`; full gate `-m:1`. (3) Build the installer on a quiet machine (no test runs in other sessions): command in the Installer section below. (4) Sagar's elevated install on this PC, following the live runbook, with `live-verify.ps1` after each step. (5) VM + second-machine restore. (6) Shop PC last.

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

Record only (not fixing): re-saving the automation account in Settings > Users strips `etp_automation`/`db_backupoperator` (pre-existing, documented); fresh install run as SYSTEM already fails at migration `0012` (pre-existing); 0032 gives SYSTEM access back — deactivate SYSTEM in Settings > Users once the tasks run as `EtpAutomation`; the app itself runs database code as the Owner's own connection, so an Owner who is a SQL administrator is exposed to planted code whenever they use the app — finding 2 closes the unattended drill route only; the app's Operations buttons (backup, recovery drill) cannot work from an unelevated app, because `%ProgramData%\EtpReporting\Backups` and `Operations` are readable only by Administrators, SYSTEM, the SQL service and the automation account (checked: an unelevated Owner cannot list either folder) — the drill's new read of `operations.json` does not change that. (Recording as `dbo` instead, to avoid the configuration read, was tested and rejected: `IS_ROLEMEMBER('etp_owner')` is 0 for `dbo`, so `record_verified_operation` refuses it.)

Also record only: the test suites write into the Owner's real diagnostics log, `%LOCALAPPDATA%\EtpReporting\Logs` — 4,177 of this month's 5,615 entries carry the test host's version 15.0.0.0, and the headless-startup tests add `STARTUP_CONFIGURATION_REJECTED` entries under 1.8.8.0. That noise is what made the startup dialog look like a recurring owner-facing fault. Tests should write to their own folder.

Watch at the live step: the drill task is S4U under `Sagar`, which is a Microsoft-account-linked local account. Confirm the task actually starts (Start-ScheduledTask, then LastTaskResult), not only that it registers.

## Gate (22 Sep, after all fixes)

Release build 0 warnings 0 errors. Desktop 415 passed (2 skipped); Domain 12; Import 110; Reporting 63; SQL integration 100 passed (1 skipped); SQL unit 239. Two one-off failures, each passed on rerun and in untouched code: `ScopedImportDuplicateSqlTests` (shared-memory transport error 0.6 s into the run) and `Phase3ShellTests` (WPF collection accessed concurrently). No test residue in `master`.

## Installer: not built yet — the test gate needs a machine with memory to spare

`build-windows-installer.ps1` runs the whole test suite and publishes only if it passes. Three attempts on 22 Sep failed that gate with 2–5 integration failures, always timeouts, never assertions about behaviour. Measured cause, with a read-only monitor on `sys.dm_exec_requests`: the recovery drill's `DBCC CHECKDB` waited 57 s on `RESOURCE_SEMAPHORE` (query memory) behind up to ten other sessions until the test's 60 s limit killed it; the same test passes in 1 s on its own. Two test-side corrections are committed: the release gate runs test projects one at a time (`-m:1`; it failed four runs out of four in parallel), and the recovery test gives the drill 300 s (the shipped drill, through sqlcmd, has no timeout).

It still is not enough on this machine as it stands: 16 GB RAM, about 1.4 GB available, 20 GB committed, 4 GB of it the running acceptance VM `ETP-Acceptance-186` (idle, console closed). A serial run of the integration project alone then took 7 min and timed out 7 UI and import tests that passed at 17:40 in 2.5 min. Saving the VM to free that memory was refused by the auto-mode permission check, so it is Sagar's call. To build: free memory (save or stop the VM, close large apps), then run from the worktree

    .\scripts\build-windows-installer.ps1 -ReleaseDirectory 'artifacts/windows-release-<sha>' -OutputDirectory 'artifacts/installer-<sha>' -SqlPayloadDirectory 'C:\Codex\Reporting Manger\SQL Express 2025'

## Phase status and decisions

- Phases 0, 1: closed. Phase 2: closed on coding 17 Sep; open: **D14** (DSR PDF reads `segoeui.ttf` from the Windows Fonts folder — accept or bundle) and a formal closure record.
- Phase 3: closed on coding 17 Sep. **A3.8 touch walkthrough: done by Sagar** (his observation). **P3-3:** recommendation given — keep the one-line footer and amend plan task 8 (a 3-line footer drops content to 598 DIP, below the 610 floor); awaiting Sagar's decision.
- Phase 4 remaining:
  1. merge to `main` — **Sagar's decision**: the auto-mode permission check refused both the push to `main` and keeping the merge on a local branch. The merge was clean and differs from the tested branch only by main's own planning documents (`docs/audit/*PLAN*`, `docs/roadmap/*`). `main` is still `714d117`. Then build the installer (see the section above)
  2. this machine (Sagar, elevated): fresh backup; install the new build (applies 0032 — gives SYSTEM back its CONNECT); add `EtpAutomation` as Store Manager in Settings > Users; install the operations module; re-register tasks (backup/automation on `EtpAutomation`, drill on the Owner); evidence: verified backup + recovery drill; ACL before/after check
  3. VM `ETP-Acceptance-186`: Sagar runs `initialize-etp-operation-folders.ps1 -SqlServiceIdentity 'NT Service\MSSQL$SQLEXPRESS' -CreateAutomationAccount`, then install, then second-machine restore. The VM's bundled SQL makes the Owner sysadmin through `BUILTIN\Administrators`: the first real run of review finding 1's group path
  4. decide `EtpReportingHelios` (at `0020`): live or leftover
  5. ~~the startup `DISPATCHER_UNHANDLED` / `ArgumentException` dialog~~ — **diagnosed 22 Sep; not an owner-facing defect, but a real one underneath.** Every such entry in the diagnostics log came from my own launches: 08:19–08:21 UTC on 17 Sep were the P4-8 regression proof against deliberately reverted code; 16:04 and 16:06 (twice) were launches through `Start-Process -ArgumentList '--connection-string',$cs`, which joins arguments with spaces unquoted, so the app received `...;Integrated` and rejected it. The 17 Sep handoff's "the only defect a shop owner actually sees" was wrong: Sagar's two screenshots that day were VM database errors, and the shortcuts and setup's "Launch" pass no arguments. The real defect: an interactive launch with a rejected `--connection-string` let the exception escape `async void OnStartup` to the dispatcher handler — a generic dialog, then (proven by clicking OK through Win32) a process with no window that keeps running and **keeps the `Global\EtpReportingEngineRunning` mutex, so setup refuses to upgrade**. Fixed: the rejection is now handled in interactive launches too — it says why, and the process exits with code 2. `Interactive_startup_says_why_and_exits_when_the_connection_string_is_rejected` launches the real executable, clicks OK through Win32 and requires exit code 2; it fails against the previous `App.xaml.cs` (finds the generic dialog). (A separate `DISPATCHER_UNHANDLED` at 12:47 UTC on 17 Sep was an `InvalidOperationException` from the then-installed 1.8.5; superseded, not pursued.)
  6. shop PC (still `1.8.1+8e35d83`) last

## Live machine facts

- `EtpReporting` at `0031`; `NT AUTHORITY\SYSTEM` has **no CONNECT** (so Automated Operations and Daily Backup, which run as SYSTEM, cannot connect since 17 Sep). Owner `DESKTOP-6IBM1J5\Sagar` fine (explicit sysadmin login, db_owner). There is no `BUILTIN\Administrators` login on this instance.
- `EtpAutomation` local account exists (enabled, not an administrator) but has **no SQL login yet** — Settings > Users has not added it.
- Half-installed broker `master.dbo.etp_operations_31736fb143c6912a` (unsigned). No master key. Verified pre-upgrade backup `EtpReporting-pre-0031-20260917-151407.bak` in the SQL backup folder — keep it.
- Scheduled tasks: Automated Operations and Daily Backup run as SYSTEM (older mechanism); Monthly Recovery Drill runs as `Sagar`, Interactive, Limited. Re-registering with this build moves the drill to S4U + Highest under the same account.
- Live `settings.json` points at `EtpReporting` — keep it that way. Screen is 3440x1440.

## Rules still in force

Never write to live `EtpReporting` except through Sagar's elevated steps; never touch Codex's worktrees (nor another session's worktree: `main` is checked out in one); never edit a committed migration; test residue in `master` must be cleaned (check with `scratchpad/recovery/residue.ps1`); integration tests use `Pooling=false` and non-masking cleanup.
