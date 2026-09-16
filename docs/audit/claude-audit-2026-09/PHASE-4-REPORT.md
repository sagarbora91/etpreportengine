# Phase 4 — Security and operations

## 16 September 2026 integration remediation

Current branch: `integration/phase-2-3-4-fixes`, based on `origin/phase-3/touch-shell` and merged with `origin/phase-4/security-operations`. The original implementation report below is historical; this section supersedes its integration and automation-default statements. Nothing is marked closed; Claude re-audits and Sagar merges. No live shop database was migrated.

### Integration and write model

The chosen model is narrow SQL procedures for protected facts, import lifecycle changes and Phase 1/2 source/master mutations. Store Managers retain the existing Phase 0 import bookkeeping grants, but receive no blanket writer role or new direct fact/master DML. Phase 1 enrichment, staff seeding, typed family rows, retained-document links and duplicate classification now use procedures. The latest fix prompt explicitly requires a Store Manager brand edit; this is a narrow exception for brand rows, with Viewer rejection and Owner-only staff/tender/target administration retained.

This model keeps one SQL authorization boundary even when a caller bypasses the desktop. The 32 static family procedures append typed source rows and their manifests only inside an open import transaction. Automatic superset promotion independently compares typed source values instead of trusting supplied content keys, preserves canonical fact IDs/amounts, and rebinds their lineage. Committing immediately after promotion cannot delete existing facts. Corrective restatement and legacy files without manifests remain Owner-only. The UI now opens the actual brand editor for Managers; monthly-target saving and other administration remain Owner-only.

Migration `0022` is immutable and fails before a later migration can run. `SqlServerMigrationStore` therefore recognises its exact original checksum and, only when `0020` is journalled and the retired table is absent, creates a minimal empty compatibility object in the same transaction. The original SQL runs unchanged, then the object is dropped before journal commit. A failed migration rolls the object and permissions back. The new `0025` extends the procedure boundary; no committed migration was edited or reordered. Tests cover fresh setup, Phase 3-first and Phase 4-first upgrades, journal preservation, rerun idempotence, and forced `0022` failure/retry.

### Returned Phase 4 items

| Item | Change / remaining evidence |
| --- | --- |
| P4-1 / work item 1a | Transactional compatibility bridge plus new migration, with all four bootstrap/upgrade tests passing. |
| P4-2 / work item 1b | Procedure-based cross-phase import and brand edit; restricted-connection folder, duplicate, promotion and denial tests provide the acceptance evidence below. |
| P4-3 | Sagar delegated the choice during this fix session. Selected `<machine>\EtpAutomation`, non-admin, S4U. Folder setup defaults to its required access and writes the protected configuration. `-CreateAutomationAccount` provisions a missing account with an unrecorded random password; explicit `-GrantAutomationFolderAccess:$false` retains strict mode. Task installation still requires elevation, signing and operational SQL prerequisites. |
| P4-4 | New `0026` accepts numeric details only as bounded aggregate-count messages, including `3 files imported`. Paths, invoice-shaped identifiers and phone-sized digit strings remain blocked in the client and SQL. SQL-stamped actors and append-only triggers are unchanged. PDF/Excel completion now emits the safe static detail `Report exported`; its old colon-bearing detail was rejected and omitted from database history by the host audit wrapper. Both formats have completion regressions. |
| P4-5 | INSTALL and CI now use `Encrypt=Optional`. Test connection setup preserves that spelling before validation because SqlClient can serialise it as `False`. Connection policy itself is unchanged. |
| P4-6 | `Invoke-EtpFunctionAudit.ps1` resolves the shared ACL-checked absolute sqlcmd path and retains `-x`. The live resolver selected `C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE`. |

### Shop folder evidence — still blocked by elevation

Read-only verification confirmed machine `DESKTOP-6IBM1J5`, account `DESKTOP-6IBM1J5\Sagar`, and a non-elevated token. `EtpAutomation` does not yet exist. The selected account is not a claim of successful provisioning.

Commands run:

```powershell
icacls 'C:\ProgramData\EtpReporting'
icacls 'C:\ProgramData\EtpReporting\Backups'
icacls 'C:\ProgramData\EtpReporting\Documents'
Test-Path -LiteralPath 'C:\ProgramData\EtpReporting\Share'
```

Before evidence (the same two Users entries occur on the root, Backups and Documents):

```text
BUILTIN\Users:(I)(OI)(CI)(RX)
BUILTIN\Users:(I)(CI)(WD,AD,WEA,WA)
Share exists: False
```

Attempted `Start-Process` of the prepared ACL evidence script with `-Verb RunAs -WindowStyle Hidden`. Windows returned **“The operation was canceled by the user.”** No setup process ran, no account was created, and no after evidence is claimed. A4.2 remains unmet. The prepared wrapper is `%TEMP%\EtpPhase234AclEvidence\apply-folder-protection.ps1`; it checks the actual SQL service identity, captures before/after `icacls`, provisions only the chosen account, invokes setup and reads back the protected configuration. Run the reviewed setup from an elevated PowerShell session:

```powershell
& 'C:\Codex\Reporting Manger\phase234-integration-fixes\scripts\initialize-etp-operation-folders.ps1' `
    -SqlServiceIdentity 'NT SERVICE\MSSQL$SQLEXPRESS' -CreateAutomationAccount
```

The original high exposure is therefore still present on the PC. Staff Documents/Share access has not been broadened to compensate.

### Executed checks

Final shared gate after all code fixes: Debug and Release solution builds both passed with **0 warnings, 0 errors**. The full Release suite passed with **830 passed, 0 failed, 3 opt-in skips**. The Phase 3 fixture capture was executed separately and passed; the two pre-existing Phase 4 opt-in render/full-window cases were not rerun in this final gate. The private-corpus tests ran, rather than skipping. Only new migrations `0025` and `0026` differ from the merged baseline; the master plan is unchanged.

```powershell
dotnet build Etp.Reporting.slnx -c Debug --no-restore --nologo --verbosity minimal
dotnet build Etp.Reporting.slnx -c Release --no-restore --nologo --verbosity minimal
dotnet test Etp.Reporting.slnx -c Release --no-build --verbosity minimal
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 132 ms - Etp.Reporting.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    63, Skipped:     0, Total:    63, Duration: 1 s - Etp.Reporting.Reporting.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   236, Skipped:     0, Total:   236, Duration: 5 s - Etp.Reporting.SqlServer.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   110, Skipped:     0, Total:   110, Duration: 32 s - Etp.Reporting.Import.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   350, Skipped:     2, Total:   352, Duration: 34 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    59, Skipped:     1, Total:    60, Duration: 2 m 57 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
```

The final console logs are retained outside the repository under `%TEMP%\EtpPhase234Final`. Earlier focused checks follow.

```powershell
dotnet build Etp.Reporting.slnx -c Release --nologo -v minimal
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests -c Release --filter FullyQualifiedName~CrossPhaseMigrationTests --logger 'console;verbosity=minimal' --nologo
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests -c Release --filter 'FullyQualifiedName~AggregateAuditSqlTests|FullyQualifiedName~PhaseFourSecurityTests' --logger 'console;verbosity=minimal' --nologo
dotnet test tests-dotnet/Etp.Reporting.SqlServer.Tests -c Release --filter 'FullyQualifiedName~OperationalAuditRepositoryTests|FullyQualifiedName~PhaseFourBoundaryTests|FullyQualifiedName~Migration' --logger 'console;verbosity=minimal' --nologo
```

```text
Build succeeded. 0 Warning(s), 0 Error(s).
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 5 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 1 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:    61, Skipped:     0, Total:    61, Duration: 186 ms - Etp.Reporting.SqlServer.Tests.dll (net10.0)
```

Ran `& scripts/test-etp-operations-boundaries.ps1 -Scenario <name>` for TargetAliases, BackupReceipts, CertificateCustody, CertificateBinding, Retention, Paths, ProtectedInstall and AtomicReceipts: all eight succeeded, 190 assertions. Both changed PowerShell scripts parsed without errors. A direct Windows PowerShell test-harness invocation was blocked by that host's execution policy; the existing workspace PowerShell host executed the same scenarios successfully. No machine execution policy was changed.

Cross-phase acceptance and adversarial verification:

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj --filter 'FullyQualifiedName~CrossPhaseStoreManagerImportTests|FullyQualifiedName~PhaseOneImportSqlTests|FullyQualifiedName~StaffImportNameSqlTests|FullyQualifiedName~ScopedImportDuplicateSqlTests|FullyQualifiedName~PhaseFourSecurityTests' --no-restore --verbosity minimal -p:BuildProjectReferences=false --logger 'trx;LogFileName=cross-phase-write-boundary.trx'
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj --filter FullyQualifiedName~CrossPhaseStoreManagerImportTests --no-restore --verbosity minimal -p:BuildProjectReferences=false --logger 'trx;LogFileName=cross-phase-restricted-roles.trx'
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~AggregateAuditSqlTests|FullyQualifiedName~CrossPhaseMigrationTests' --verbosity minimal --logger 'trx;LogFileName=phase234-audit-migrations.trx'
```

```text
Passed!  - Failed:     0, Passed:    20, Skipped:     0, Total:    20, Duration: 2 m 22 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 19 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 7 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
```

`CrossPhaseStoreManagerImportTests` bootstraps the complete migration set, imports a real folder containing all 32 sanitised XLSX families, retains document links, repeats the folder as duplicates, and saves a brand row through the repository. A test-only SQL diagnostic observer covers every connection to that generated database and verifies the Manager role, absence of Owner/db_owner/db_datawriter/sysadmin, and impersonation inside every command. It also checks Viewer rejection, SQL error 229 for direct fact mutations, locked dates, copied-key tampering, immediate commit after promotion, and equivalent 7/07 state codes. No production identity hook was added.

A separate fresh read-only security review identified a full-width-digit bypass in the first draft of `0026`. The final migration preserves collation-aware identifier detection and uses the narrower ASCII grammar only for allowed count messages. The SQL regression rejects `Invoice１２３` and `３ files imported` as Viewer while accepting legitimate aggregate counts. A suspected state-code regression was withdrawn after the reviewer inspected the existing normalization; both R018/R019 cases pass. The migration bridge, Manager brand exception, Owner-only target control and folder initializer had no further supported findings in that review.

The desktop brand route also has real-control tests for Manager/Owner/Viewer behavior, late role assignment and revocation. Role changes refresh button state, and action handlers recheck access before writing. This caught and corrected an embedded editor created before its initial Owner session was assigned.

```powershell
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~EveningMastersAccessTests|FullyQualifiedName~TaskNavigationTests|FullyQualifiedName~ShellNavigationServiceTests|FullyQualifiedName~UiNavigationTests' --verbosity minimal --logger 'trx;LogFileName=brand-access-navigation.trx'
dotnet test tests-dotnet/Etp.Reporting.SqlServer.Tests/Etp.Reporting.SqlServer.Tests.csproj -c Release --no-restore --verbosity minimal --logger 'trx;LogFileName=phase234-sql-unit.trx'
```

```text
Passed!  - Failed:     0, Passed:    56, Skipped:     0, Total:    56, Duration: 891 ms - Etp.Reporting.Desktop.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   236, Skipped:     0, Total:   236, Duration: 1 s - Etp.Reporting.SqlServer.Tests.dll (net10.0)
```

Remaining owner/deployment gates: compatible production SQL edition for native encrypted backups, purchased code-signing certificate, encrypted recovery/custody on a second machine, elevated live ACL setup, task installation/run evidence, and native/DPI audit. The validated day lock, connection policy, append-only audit triggers, certificate design, signing and backup rotation were not refactored.

---

## Original Phase 4 implementation record

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
