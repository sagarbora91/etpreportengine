# Phase 4 — Security and operations

Branch: `phase-4/security-operations`, from `main` at `9a028a4`. This is the coding implementation; the shop deployment and Claude's acceptance audit are outstanding. All SQL verification uses uniquely named disposable databases. No shop database migration, existing folder ACL change, task installation, certificate export or production signing was performed.

## 1. What changed

| Files | Change |
| --- | --- |
| `database/migrations/0021_day_lock_security.sql` | SQL enforces locked-day transitions, Owner authority and required reason; writes the reopening event atomically and stamps its actor. |
| `database/migrations/0022_least_privilege_audit.sql` | Replaces Store Manager broad writer membership with explicit grants and import procedures; forces staff adjustment requests to PENDING and Owner-only decisions; stamps audit actors, rejects history updates/deletes and archives without deleting originals. |
| `database/migrations/0023_operations_status.sql` | Adds append-only verified operation receipts and a narrow health reader; staff cannot manufacture backup/drill status through ordinary audit messages. |
| `src/Etp.Reporting.Infrastructure.SqlServer/LocalSqlConnectionPolicy.cs`, connection adapters/repositories, Desktop connection validation | One local Windows-authentication boundary rejects remote endpoints, explicit disabled encryption, attached files and credentials. Local default is `Encrypt=Optional`. |
| `Migrations.cs`, `DatabaseBootstrapper.cs` | Normalizes migration line endings before hashing; accepts equivalent legacy LF/CRLF hashes while rejecting changed SQL. |
| Daily Workflow contracts, repository, service and Desktop session/view | Removes client administrator approval and Windows elevation check; SQL Owner reopens with a reason. Loading a day no longer creates a database row. |
| SQL audit writers and enrichment repository | Uses the shared SQL audit procedure and narrow import write procedures. |
| `BackupCertificateService.cs`, Settings view and navigation | Owner action creates/exports password-protected recovery keys to two selected locations and records hashes; validates passwords, path aliases, nesting and links. |
| Dashboard contracts/query/health reader/view/state | Displays trusted successful backup and drill timestamps and their full fingerprints. |
| `scripts/etp-operations-common.ps1`, `ProtectedOperationPath.cs`, `PowerShellOperationsService.cs` | Shared target/path/ACL/receipt checks, absolute executables, `AllSigned`, `sqlcmd -x`, generic operation errors and atomic receipts. |
| `scripts/backup-etp-database.ps1`, `invoke-etp-recovery-drill.ps1` | Requires AES-256 native encryption and custody evidence; restores the receipt-named, hash-checked file; compares its own file metadata; retains 14 daily and 12 monthly backups. |
| `scripts/sql/etp-operations-broker.sql`, `install-etp-sql-operations.ps1` | Constrained SQL backup/restore module with fixed database/folders, generated recovery names and a separate module-signing certificate. |
| `scripts/initialize-etp-operation-folders.ps1`, three task installers | Protects operation folders and their parent; strict ACL default, explicit automation-access option; common dedicated local non-administrator task principal. |
| `scripts/bootstrap-etp-prerequisites.ps1`, Desktop App/composition/startup | Configured initialization uses the protected machine target; manual initialization retains per-user settings. Bootstrap validates the full installation tree before elevated payload execution and rejects missing configuration/incompatible SQL prerequisites. |
| `scripts/new-etp-support-package.ps1`, maintenance/health scripts | Aggregated safe health reader, no source/customer/path exports; archives audit history; validates local targets and SQL tool location. |
| Release/signing scripts and installer | Requires signing and timestamp verification; validates payload signatures before packaging; removes the non-admin installation override. |
| `.github/workflows/ci.yml` | Pins Actions to verified commit SHAs and names the executable .NET boundary-test step accurately. |
| New Phase Four SQL/desktop/PowerShell tests; adjusted existing caller tests and `tools/Etp.Reporting.LiveSmoke/Program.cs` | Behavioral coverage of permissions, audit, receipts, retention, paths, certificate input, privacy, startup target and UI; updates the older smoke caller to the SQL-enforced reopen API. |
| `README.md`, `docs/OPERATIONS.md` | Documents branch scope, deployment prerequisites, custody, second-machine recovery and Owner break-glass procedures. |

Migrations 0017–0020 are reserved for the parallel Phase 1 branch. Existing committed migrations were not changed. This branch contains the Phase 0 import schema; integrating Phase 1 requires reconciling its new import tables/procedures with these explicit grants and the enrichment changes.

## 2. How to verify

Run from the Phase 4 worktree on Windows with .NET 10 and a local SQL instance. The fixture generates `EtpPhase0Test_<guid>` databases, applies all bundled migrations, and drops only its generated database. Do not point tests at a named shop database; the fixture replaces the database name regardless.

```powershell
dotnet build Etp.Reporting.slnx -c Debug --no-restore
dotnet build Etp.Reporting.slnx -c Release --no-restore
dotnet test Etp.Reporting.slnx -c Release --no-build --verbosity minimal
dotnet test Etp.Reporting.slnx -c Release --no-restore --verbosity minimal
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~PhaseFour"
```

To use LocalDB for an audit, set `ETP_TEST_SQL_CONNECTION` to `Server=(localdb)\MSSQLLocalDB;Integrated Security=True;Encrypt=Optional;Connect Timeout=5` before running. The local execution here uses `.\SQLEXPRESS`. Restore tests create only a synthetic temporary backup, grant the SQL service access only to its generated temporary folder, and restore to generated `EtpRecovery_<guid>` names. They do not prove native encryption or the deployed module signer's permissions.

Workspace evidence command (run alone):

```powershell
$env:ETP_PHASE4_UI_EVIDENCE = Join-Path $PWD 'docs/audit/claude-audit-2026-09/phase-4-screenshots'
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj --no-restore --filter FullyQualifiedName~Phase_four_changed_workspaces_render
Remove-Item Env:ETP_PHASE4_UI_EVIDENCE
```

The PowerShell scenarios run through `OperationsScriptBoundaryTests`; the unsigned development harness uses `Bypass` only inside tests. Installed operation execution requires `AllSigned`. Deployment commands and the supervised first-upgrade sequence are in `docs/OPERATIONS.md`; none were executed on the shop installation.

Full MainWindow launch check (run alone; temporary settings and fixture database):

```powershell
$env:ETP_FULL_WINDOW_SMOKE = '1'
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~PhaseFourFullWindowSmokeTests --verbosity minimal
Remove-Item Env:ETP_FULL_WINDOW_SMOKE
```

## 3. Test results

Final Debug and Release solution builds both succeeded with **0 warnings and 0 errors**. Final full Release test run (`--no-restore`, exit 0): **653 passed, 0 failed, 2 intentionally opt-in checks skipped**. Both opt-in checks were run separately and passed. Exact summary lines:

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 182 ms - Etp.Reporting.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    59, Skipped:     0, Total:    59, Duration: 719 ms - Etp.Reporting.Reporting.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 861 ms - Etp.Reporting.Import.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   215, Skipped:     0, Total:   215, Duration: 2 s - Etp.Reporting.SqlServer.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    21, Skipped:     1, Total:    22, Duration: 7 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   295, Skipped:     1, Total:   296, Duration: 15 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
```

The first whole-solution build identified an older LiveSmoke caller still passing the deleted administrator flag; the call was updated. A legacy audit test expected the original connection label; it now asserts the SQL acting identity required by Phase 4 (`SUSER_SNAME()`). The final run above includes those corrections. Whitespace and PowerShell syntax checks passed.

Focused evidence collected during implementation:

- SQL permission/status/recovery run: 6 passed, 0 failed. Covers original reopen/fact/audit attacks, valid Owner reopen and import, trusted status, source changes after backup, restore metadata and filename escapes.
- Certificate validation/custody: 18 passed. Reproduced and fixed whitespace passwords, alias/nested-folder and short-name validation defects; covers immutable export records, key rotation and exported certificate identity.
- Desktop startup/configuration/PowerShell boundaries: 44 passed, 0 failed.
- Standalone PowerShell scenarios: 8 passed, 190 assertions, including receipt tampering, retention across UTC days/months, missing/mismatched custody, path links and atomic replacement. All eight scenarios pass through the full Desktop suite; ten final test-only pointer-resolver assertions were then added and all eight standalone scenarios rerun successfully.
- Opt-in WPF workspace rendering: 1 passed; visual inspection completed at both requested sizes.
- All operational PowerShell scripts parse successfully.
- Full MainWindow smoke: 1 passed. Created a real visible window, connected only to the generated database, opened Dashboard, rendered at both sizes and closed; existing user settings hashes were unchanged. This used the full MainWindow test host rather than production `App.OnStartup`.
- Support/native CLI recovery: 3 passed. Checked every generated archive entry against synthetic customer/name/phone/workbook/path/SQL sentinels; SQL error output was generic with no archive; PowerShell/SQLCMD metadata matched the SQL client result.

One fresh independent read-only candidate review found three concrete issues. All were confirmed and addressed within Phase 4: pre-execution protection of the complete install tree (`BootstrapPrerequisiteTests`, 3 passed); staff pre-approved adjustment injection (`PhaseFourSecurityTests`, 6 passed, including valid pending requests and Owner decisions); and certificate A/B custody mismatch (immutable per-export receipts, thumbprint binding and rotation regression tests). No second review cycle was used.

The real SQL command invocation exposed incompatible `-h -1` and `-y 0` options; the common helper now retains unlimited metadata output and callers identify their expected result records. Earlier recovery timeout occurred during concurrent SQL test activity; the focused rerun restored and cleaned up successfully. No unrelated test process was interrupted.

## 4. Screenshots

All evidence is under `docs/audit/claude-audit-2026-09/phase-4-screenshots/`. Each following basename has `-1366x768-wpf.png` and `-816x480-wpf.png` variants:

- `Settings-overview`
- `Settings-recovery-keys`
- `Daily-Workflow-overview`
- `Daily-Workflow-owner-reopen`
- `Dashboard-overview`
- `Dashboard-recovery-status`

These are **WPF workspace renderings with synthetic data**, not native OS screenshots or proof of a maximized production window. The native screenshot surface is unavailable. Recovery controls, Owner reason/action and wrapped full backup/drill fingerprints were visually checked. A separate full MainWindow launch/render check passed in an isolated host; Claude must complete the native screenshot and DPI audit.

## 5. Decisions taken

1. D9 remains native AES-256 SQL backup encryption. SQL Express/Web cannot create it, so code fails closed; no plaintext fallback was added. A supported production edition or an explicitly revised D9 decision is still required. See the primary Microsoft source in Operations.
2. The strict folder policy excludes the account required for scheduled operation. Setup defaults to the strict policy; `-GrantAutomationFolderAccess` is a concrete option for a later approved deployment, not approval inferred from silence. Staff Documents/Share access also needs a decision.
3. Restore runs through a constrained, signed SQL module because native restore metadata and temporary database creation need permissions above Store Manager. The dedicated Windows account does not receive `sysadmin` or `dbcreator`. This deployed signature/permission path remains to be exercised on an isolated supported instance.
4. Append-only audit maintenance copies older events to an archive and retains originals. Owner/sysadmin DDL powers remain a documented administrative trust boundary; ordinary DML cannot edit/delete history.
5. Stored UTC receipts are the dashboard evidence. A free-text audit message from a Viewer/Store Manager cannot create a successful backup/drill status.
6. Existing-database first upgrade requires a supervised encrypted pre-migration backup: installing the operations broker itself depends on Phase 4 roles. Bootstrap fails instead of running unbacked migrations. Later prepared upgrades use the common machine target and verified receipt.
7. Scope stays on the Phase 4 branch. The import failure register has no Phase 4 rows; IF-001–IF-013 remain assigned to Phase 1 and were not changed here.
8. Each recovery-key export now has a unique, immutable custody receipt. The common latest file is only a pointer for future backups; old backups retain their original receipt and matching certificate thumbprint. Re-exporting or rotating a key cannot silently substitute its recovery evidence.

## 6. Known gaps

- Live folder ACLs, scheduled tasks and a daily backup within 24 hours: not run, coding-only scope.
- Native encrypted backup, recovery-key export/custody, signed-module execution under the dedicated account, and second-machine restore: not run. The available SQL Express engine cannot create the approved backup. Synthetic plaintext restore verifies the restore algorithm only.
- Production Authenticode signing, signed installer build and clean-machine deployment: not run without signing authority and deployment prerequisites.
- Native full-app screenshots at both sizes and 125% DPI: outstanding; supplied images are synthetic workspace renders.
- Real-import-session privacy acceptance: not run against real customer data on this branch. Synthetic sentinel package tests and existing diagnostic scrub tests provide executable coverage without copying customer files into the repository.
- A forcibly cancelled SQL restore can escape SQL TRY/CATCH and leave a uniquely named recovery database; inspect and remove only the verified recovery copy using the runbook. No automatic action targets the source database.
- Task S4U cannot use network credentials. Off-PC key media must be accessible at drill time; automatic off-PC database-backup replication is not implemented.
- Phase 1 financial/import changes and their permissions still require integration before this branch can replace the user's combined app.
- The repository's existing Git hook generated an ignored `.code-review-graph` analysis cache on the first commit. Automatic approval review blocked both recursive and explicit-file cleanup, reporting only "blocked by policy". It remains outside the commits. Later commits suppressed that optional hook for the individual command; no shared hook or Git configuration was changed.

Overall security-fix verification is **blocked at deployment-dependent gates**, not a claim that all Phase 4 acceptance is complete. Original SQL attacks and supported local controls are exercised by disposable tests; unsupported native/signing/installation paths remain explicit gaps.

## 7. Acceptance checklist

| Plan criterion | Self-assessment | Evidence / outstanding work |
| --- | --- | --- |
| A4.1 Viewer `UPDATE daily_reporting_days SET status='OPEN'` and `DELETE FROM operational_audit` fail; Store Manager updating `import_files.is_superseded` and `sales_lines` fails. | PASS in disposable SQL tests | `PhaseFourSecurityTests`; includes SQL impersonation, attempted actor/event forgery and legitimate imports/Owner reopen. Actual shop-login SSMS audit still required. |
| A4.2 `icacls` on the three ProgramData folders shows no BUILTIN\Users entry. | NOT RUN | Setup code supplied; existing machine ACLs deliberately not changed in this coding task. |
| A4.3 Three tasks have the same non-SYSTEM service principal, and a daily backup ran within 24 hours. | NOT RUN | Shared principal/configuration code supplied. Account, ACL decision and compatible SQL deployment required. |
| A4.4 Backup → drill restore → verify passes on the audit PC from the receipt-named file. | PARTIAL | Synthetic backup restore and receipt/hash/metadata boundary tests; native encrypted end-to-end path and module signature permissions unverified. |
| A4.5 Support package and diagnostics after real imports contain no customer name, phone, path or SQL text. | PARTIAL | Synthetic private-content/error sentinels and diagnostic tests; real import-session audit outstanding. |
| A4.6 Connection settings reject `Server=remotehost` and `Encrypt=False`. | PASS | `PhaseFourBoundaryTests` through shared policy/Desktop argument boundary, including aliases and legitimate local connections. |

Phase closure requires Claude's independent audit and the outstanding deployment evidence.
