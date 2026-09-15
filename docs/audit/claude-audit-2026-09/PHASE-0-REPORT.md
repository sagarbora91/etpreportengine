## 1. What changed (files and one line each)

Phase 0 only, 15 September 2026. Branch: `phase-0/stabilise`, created from `ui/uiux-v4-touch-first-redesign` after preservation commit `05e16a9`. Reopened fixes verified at `5e2a5407b31430583f86faa18ff8df54d4167ff9`. No financial calculation was changed. This is Codex's self-assessment, not Claude's phase closure.

- `src/Etp.Reporting.Application/Dashboard/DashboardQuery.cs` — Preserve the existing latest-business-date result in task 1.
- `src/Etp.Reporting.Desktop/MainWindow.xaml` — Preserve the existing maximised-window fix in task 1.
- `src/Etp.Reporting.Desktop/MainWindow.xaml.cs` — Preserve existing fixes, catch startup failures, display the recovery panel, and rerun startup on Retry.
- `src/Etp.Reporting.Desktop/MainWindow.Shell.cs` — Preserve the existing wording fix and route Retry through startup recovery.
- `src/Etp.Reporting.Desktop/Modules/Dashboard/DashboardView.cs` — Preserve the existing dashboard reflow in task 1.
- `src/Etp.Reporting.Desktop/Shell/TaskNavigator.cs` — Preserve the latest-data date and singular task wording in task 1.
- `src/Etp.Reporting.Infrastructure.SqlServer/OperationalStatusRepository.cs` — Preserve the existing latest-data-date query in task 1.
- `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerDashboardQuery.cs` — Preserve the existing dashboard date projection in task 1.
- `.gitignore` — Ignore the removed generated/legacy trees and retained nested JavaScript test helpers.
- `graphify-out/`, `artifacts/`, `www/`, `prototypes/`, `verification/`, `build-overrides/`, `node_modules/`, `tests/**/*.mjs`, `package.json`, `package-lock.json`, `capacitor.config.json`, `.graphify*`, `.code-review-graph*`, `EXPORT-*.txt` — Remove tracked matches without rewriting history; retained disk copies are ignored.
- `knowledge/` — Remove from Git and ignore the retained disk copy, following the instruction not to otherwise touch it.
- `AGENTS.md`, `.codex/hooks.json` — Remove graph instructions/hooks from Git and disk.
- `docs/_archive-2026-09/` — Move superseded documents here, retaining the specified audit plan/folder and schema/import/mapping references in place.
- `docs/14_WINDOWS_QUICK_START.md` → `docs/INSTALL.md` — Rename the installation guide.
- `docs/05_MAPPING_REGISTER.md` — Correct the GST statement: NETVALUE excludes GST; NETAMOUNT includes GST.
- `README.md` — Replace with a one-page Windows-app description and build/test/run guidance.
- `.github/workflows/ci.yml` — Remove explicit Node/npm steps; retain restore, Release build, .NET tests, PowerShell parsing and dependency scanning; start LocalDB for integration tests.
- `scripts/invoke-security-scan.ps1` — Remove the indirect npm audit and its output fields; preserve fail-closed .NET vulnerability/deprecation scans.
- `Etp.Reporting.slnx` — Include the new SQL integration project.
- `src/Etp.Reporting.Reporting/ReportDefinition.cs` — Delete `InitialReportCatalogue`.
- `src/Etp.Reporting.Reporting/ReportExecutionContracts.cs` — Delete unused `IReportExecutor`.
- `src/Etp.Reporting.Reporting/ReportSourceRegistry.cs` — Delete the unused registry.
- `src/Etp.Reporting.Reporting/VisualReporting.cs` — Remove `VisualReportRegistry` and its duplicate ID scheme while preserving existing chart selection and values.
- `database/migrations/0016_reporting_indexes.sql` — Add both specified indexes and strengthen the existing last-Owner/history trigger.
- `src/Etp.Reporting.Desktop/Composition/DesktopCompositionRoot.cs` — Add `Connect Timeout=5` to the default and load saved settings in both headless modes.
- `src/Etp.Reporting.Desktop/DesktopFriendlyError.cs` — Distinguish login, permission and unreachable-server failures, including the actual Windows timeout error 258.
- `src/Etp.Reporting.Desktop/Etp.Reporting.Desktop.csproj` — Copy scripts into normal build and publish output.

- `tests-dotnet/Etp.Reporting.Desktop.Tests/AccountingCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ArchiveWorkspaceExtractionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/BackupScriptContractTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/CiWorkflowContractTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DailyWorkflowCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DailyWorkflowPresentationTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DailyWorkflowWorkspaceViewTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DashboardPresentationSessionTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DatabaseLifecycleCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DesktopArchitectureTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DesktopCompositionGuardrailTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DesktopCompositionRootTests.cs` — Update the behavioural default-connection expectation for the required timeout.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DesktopConnectionAuthorizationTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ExtractedWorkspaceUiSmokeTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/HandledFailureDiagnosticsTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ImportCoordinatorCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ImportPersistenceCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ImportWorkspaceExtractionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/MainWindowShellBoundaryTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/OperationsAdministrationWorkspaceViewTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/RegistersAccountingWorkspaceExtractionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/RegistersAndSharingCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ReleaseVersionConsistencyTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ReportArchiveCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ReportExportCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ReportExportCoordinatorTests.cs` — Avoid worker-pool starvation while preserving the off-thread scheduling assertion and five-second timeout.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ReportsCompositionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ReportsPresentationStateTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/SettingsAndOperationsAdministrationPresentationTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/SettingsWorkspaceViewTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/SourceInboxCompositionTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/StartupFailureTests.cs` — Test SQL error categories, saved settings and the five-second default timeout.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/TaskNavigationTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/UiNavigationTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.Reporting.Tests/ManagementMetricEngineTests.cs` — Delete tests of removed report registries; retain remaining behavioural coverage.
- `tests-dotnet/Etp.Reporting.Reporting.Tests/ProductReportVisualClassificationTests.cs` — Delete tests of removed report registries; retain remaining behavioural coverage.
- `tests-dotnet/Etp.Reporting.Reporting.Tests/ReportCatalogueTests.cs` — Delete tests of removed report registries; retain remaining behavioural coverage.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj` — Add the SQL integration project and migration content.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/PhaseZeroSqlTests.cs` — Exercise indexes, Owner history/guard, severity sync, dashboard, saved headless settings, and unreachable SQL.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/SqlDatabaseFixture.cs` — Create a unique disposable database, apply migrations, and drop it after the run.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/ImportProfilePersistenceContractTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/MigrationTests.cs` — Delete source-reading test methods; retain behavioural tests.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/OperationalAuditContractTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/ProductisationAuditTransactionTests.cs` — Delete the source/script/SQL-text ratchet test file.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/ReportingQueryRepositoryTests.cs` — Delete the source/script/SQL-text ratchet test file.

- `docs/audit/claude-audit-2026-09/phase-0-screenshots/*.png` — Save the six required screen captures plus startup failure and successful Retry evidence.
- `docs/audit/claude-audit-2026-09/PHASE-0-REPORT.md` — Record executed checks and limitations.

The corrupted remote-tracking ref contained NUL bytes. Prune/update-ref could not repair it; deleting that specific broken ref file and fetching recreated it successfully. No branch history was rewritten or force-pushed.

Reopened-audit changes (R1–R4):

- `database/migrations/0016_reporting_indexes.sql` — Restore the omitted operational audit insert alongside history and the strengthened Owner guard.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/PhaseZeroSqlTests.cs` — Verify successful demotion, deactivation and deletion each append exactly one audit event attributed to ORIGINAL_LOGIN and one history row.
- `src/Etp.Reporting.Desktop/Modules/Settings/ConnectionStringValidation.cs` — Cap validated connection timeouts at five seconds, including zero (infinite), preserving shorter explicit limits.
- `src/Etp.Reporting.Desktop/Composition/DesktopCompositionRoot.cs` — Normalize headless fallback strings too.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/StartupFailureTests.cs` — Cover older saved JSON with absent, 30-second, infinite and two-second timeouts through headless and interactive connection paths.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/ReportExportCoordinatorTests.cs` — Capture exporter thread identity after completion, removing the blocked-worker handshake and timing deadline.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/{DailyWorkflowPresentationTests,DailyWorkflowWorkspaceViewTests,DashboardPresentationSessionTests,ExtractedWorkspaceUiSmokeTests,HandledFailureDiagnosticsTests,OperationsAdministrationWorkspaceViewTests,ReportsPresentationStateTests,SettingsAndOperationsAdministrationPresentationTests,SettingsWorkspaceViewTests,SourceInboxCompositionTests,UiNavigationTests}.cs` — Delete unused root finders, source-path/diagnostic data and frozen module-count tests.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/{ImportProfilePersistenceContractTests,MigrationTests}.cs` — Delete two further unused root finders.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/TaskNavigationTests.cs` and `tests-dotnet/Etp.Reporting.Reporting.Tests/ReportCatalogueTests.cs` — Delete fixed catalogue counts while retaining mapping and uniqueness assertions.

## 2. How to verify (exact commands)

Run from the repository root with the application closed:

```powershell
dotnet build Etp.Reporting.slnx -c Release
dotnet test Etp.Reporting.slnx -c Release
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests -c Release --filter Successful_owner_changes
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests -c Release --filter "FullyQualifiedName~StartupFailureTests|FullyQualifiedName~ReportExportCoordinatorTests"
& .\src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\Etp.Reporting.Desktop.exe
```

Integration fixtures create, migrate and drop their own `EtpPhase0Test_<guid>` database on SQLEXPRESS; CI uses LocalDB. Do not initialize or migrate the live EtpReporting database.

Additional executed follow-up commands:

```powershell
dotnet test Etp.Reporting.slnx -c Release --no-build
$env:DOTNET_PROCESSOR_COUNT='1'
1..5 | ForEach-Object { dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests -c Release --no-build --filter 'FullyQualifiedName~ReportExportCoordinatorTests.Every_synchronous_exporter' }
Remove-Item Env:DOTNET_PROCESSOR_COUNT
git diff e1b56e6 --check
sqlcmd -S '.\SQLEXPRESS' -E -C -d EtpReporting -Q "SET NOCOUNT ON; SELECT COUNT(*) invoices,MAX(transaction_date) latest FROM dbo.sales_invoices; SELECT COUNT(*) migrations FROM dbo.schema_migrations"
sqlcmd -S '.\SQLEXPRESS' -E -C -d master -Q "SET NOCOUNT ON; SELECT COUNT(*) remaining_phase0_test_databases FROM sys.databases WHERE name LIKE 'EtpPhase0Test[_]%'"
```

The standard full local build and build-enabled test commands were attempted but blocked by a newly reopened app locking Release DLLs (PID 16500). The targeted build/tests had succeeded before that launch; the complete current suite was then executed with `--no-build`. The clean full build ran successfully in CI.

For UI follow-up, reserve exclusive use of the app and settings file, save settings byte-for-byte, point to a disposable database, stop SQL with an elevated token, launch and time the friendly panel, restart SQL and click Retry. Restore settings and drop only the disposable database afterward. This follow-up did not perform those actions because exclusive use was not confirmed.

## 3. Test results (paste the dotnet test summary lines for each project)

Current local binaries: **580 passed, zero failures/skips**, full solution with `--no-build`:

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 176 ms - Etp.Reporting.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 1 s - Etp.Reporting.Import.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    59, Skipped:     0, Total:    59, Duration: 1 s - Etp.Reporting.Reporting.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   177, Skipped:     0, Total:   177, Duration: 5 s - Etp.Reporting.SqlServer.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 2 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   270, Skipped:     0, Total:   270, Duration: 11 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
```

Targeted build/tests: 3/3 new SQL audit cases and 17/17 startup/export cases. Five consecutive export-thread tests with `DOTNET_PROCESSOR_COUNT=1` passed, 72–77 ms each. Normal local full build attempts failed on DLL locks, not compilation errors; they are not claimed as clean build passes.

[CI run 27 / 34951364795](https://github.com/sagarbora91/etpreportengine/actions/runs/34951364795) is **green** on implementation commit `5e2a5407b31430583f86faa18ff8df54d4167ff9`: clean Release build, all suites, LocalDB integration and dependency scan.

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 85 ms - Etp.Reporting.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 338 ms - Etp.Reporting.Import.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    59, Skipped:     0, Total:    59, Duration: 569 ms - Etp.Reporting.Reporting.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   177, Skipped:     0, Total:   177, Duration: 5 s - Etp.Reporting.SqlServer.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   270, Skipped:     0, Total:   270, Duration: 8 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 3 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
```

The subsequent report-only head must also finish green before handoff. Its run is available on the [branch CI page](https://github.com/sagarbora91/etpreportengine/actions/workflows/ci.yml?query=branch%3Aphase-0%2Fstabilise); the final task reply records the exact head/run after checking it, without another report commit afterward.

R2 diagnosis: retrieved run 25's job log (`104305145219`) and run 21's log. Both failed `ReportExportCoordinatorTests.Every_synchronous_exporter_is_scheduled_away_from_the_caller_thread`; run 25 threw TimeoutException at line 92 awaiting the exporter-start signal. Both SQL integration suites passed 8/8. The test blocked an exporter worker on a manual-reset event while requiring an observing continuation within five seconds, making success depend on worker scheduling under load. The replacement waits for completion using a dedicated caller and asserts the actual exporter thread differs from that caller; it does not block the exporter or assert an elapsed-time deadline. Production export code is unchanged.

The original report cited run 23. That did not establish green CI at later documentation heads; the run-specific evidence above supersedes that claim.

## 4. Screenshots (paths)

Original Phase 0 captures, retained:

- `docs/audit/claude-audit-2026-09/phase-0-screenshots/welcome-maximised.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/welcome-816x480.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/dsr-maximised.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/dsr-816x480.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/operations-failed-import-maximised.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/operations-failed-import-816x480.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/startup-unreachable-maximised.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/startup-retry-recovered.png`

The last image duplicates welcome-maximised and is not independent Retry evidence. Claude independently captured recovery in `docs/audit/claude-audit-2026-09/phase-0-audit-screenshots/a04b-retry-recovered-owner.png`.

Fresh follow-up startup screenshots/timing: **NOT RUN**, pending exclusive access to the app/settings. The initial idle app was inspected and closed through Computer Use, but another process reopened it during the build. No reply to the exclusive-use question had arrived when this report was prepared.

Original maximised captures are 1366×728 visible-window images on a 1366×768 display. Compact captures followed reduction to the declared 816×480 minimum; Print Screen yielded 811×478 visible-window images. Images were not rescaled. Existing compact clipping is outside Phase 0.

## 5. Decisions taken (anything you had to choose that the plan did not specify)

- Correct 0016 in place, explicitly permitted by R1 while this branch is unmerged; preserve 0017 for Phase 1. The previous omission of operational audit was a regression, now restored exactly from 0011. Retain ORIGINAL_LOGIN attribution and existing history semantics.
- Normalize timeout in shared connection validation so saved settings, interactive updates and CLI execution receive the same bound. Zero means infinite and is capped; explicit limits below five seconds remain.
- Finish R4 now within reopened Phase 0. Delete dead helpers and frozen counts instead of adjusting numbers. Keep the one called root helper used to execute a behavioural PowerShell-script test.
- Diagnose CI using actual logs. Fix the export test's scheduling dependency without widening a timeout or altering production exports. LocalDB was not the failing component.
- Keep the prior chart mappings and all financial calculations unchanged. The observed ₹53,818 DSR is ex-GST; Phase 1 addresses that separately.
- Make no changes to Claude's audit or master plan. No merge, force-push or live migration. No new process documents.

## 6. Known gaps (anything in tasks 1 to 11 you could not finish, and why)

- R1–R4 code changes and behavioural tests are complete. Fresh local full build and UI timing/screenshots remain pending because an external app reopened and locked DLLs. Exclusive access was requested. Targeted builds, 580 current local tests and the clean CI build pass.
- Claude executed the actual stopped-service test and marked A0.4 PASS. This follow-up does not claim to have repeated it or measured the saved-settings panel delay. Older saved settings without timeouts are covered by behavioural tests.
- Existing disposable databases containing the former 0016 must be recreated because migration hashes are immutable. Live EtpReporting remains untouched at 490 invoices, latest 2026-08-25, 14 migrations. Automatic startup migrations remain out of scope.
- No fixture database remains; SQL service is Running. Follow-up settings hash stayed `5A58FC545219D87D41CA95E120AB5D7473C57495AAF888DE8CC0BB22B2B1E207`, already different from the audit's historical hash before any follow-up action. No settings changes, verification backups or support packages were made. Claude confirmed removal of all original leftover files.
- Other audit findings outside the four returned items remain unchanged, including maintenance environment, accessibility, legacy scripts, date culture and later-phase financial work. Original screenshot limitations are disclosed above.
- Phase 0 remains open for Claude's follow-up audit; nothing was merged.

## 7. Acceptance checklist A0.1 to A0.9 with PASS / FAIL / NOT RUN and the evidence for each

Statuses distinguish this follow-up's executed tests from Claude's recorded UI audit. No inherited evidence is presented as a fresh execution.

| Item | Result | Evidence |
|---|---|---|
| A0.1 — Under 600 tracked files; none over 2 MB | PASS | Follow-up: 597 files including Claude's audit/screenshots; largest 455,690 bytes. No new files in this follow-up. |
| A0.2 — Release tests pass; no src-reading tests | PASS | 580 current tests locally with --no-build and in clean CI. Build-enabled local repeat blocked by app locks. Remaining reads inspect generated outputs; 13 dead root helpers and frozen counts removed. |
| A0.3 — No npm/node CI steps | PASS | Unchanged workflow from Claude's PASS; run 27 green. |
| A0.4 — Maximised launch; stopped SQL recovery; DSR with data | PASS | Claude independently executed all parts in PHASE-0-AUDIT.md. Follow-up UI/timing NOT RUN pending exclusive access. Four saved-timeout cases pass; no new five-second UI measurement claimed. |
| A0.5 — DSR uses new index | PASS | Claude's restored-copy actual plan used the index; current SQL metadata tests pass. Query/index definitions unchanged in follow-up. Live deliberately unmigrated. |
| A0.6 — Operations opens with failed import | PASS | Claude UI PASS retained; current SQL dashboard/severity test passes. No follow-up UI change. |
| A0.7 — CLI honours renamed settings database | PASS | Claude exe check retained; current headless integration case passes against disposable database with invalid fallback. |
| A0.8 — Guard preserves final Owner and audit | PASS | SQL tests reject loss of final Owner. Three new successful-change cases each append one audit row with ORIGINAL_LOGIN and one history row. |
| A0.9 — SQL integration green locally and CI | PASS | 11/11 locally and in green run 27 on 5e2a540; no fixture databases remain. Report-only head run is checked before final handoff, as described in section 3. |
