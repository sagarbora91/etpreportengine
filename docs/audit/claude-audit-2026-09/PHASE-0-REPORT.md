## 1. What changed (files and one line each)

Phase 0 only, 15 September 2026. Branch: `phase-0/stabilise`, created from `ui/uiux-v4-touch-first-redesign` after preservation commit `05e16a9`. Verified implementation commit: `c57f84f2fcbc7dd7e0565c48e1c8da301fa190b0`. No financial calculation was changed. This is Codex's self-assessment, not Claude's phase closure.

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

## 2. How to verify (exact commands)

Commands run from the repository on this PC:

```powershell
Set-Location 'C:\Codex\Reporting Manger\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f'
dotnet build Etp.Reporting.slnx -c Release
dotnet test Etp.Reporting.slnx -c Release
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests -c Release
& scripts/invoke-security-scan.ps1 -OutputPath '..\phase0-security.json'
git diff --check
(git ls-files).Count
git ls-files | ForEach-Object { Get-Item -LiteralPath $_ } | Where-Object Length -gt 2MB
rg -n 'npm|node' .github/workflows/ci.yml scripts/invoke-security-scan.ps1
rg -n 'File\.(ReadAllText|ReadAllLines|ReadLines)|XDocument.Load|StreamReader|OpenText' tests-dotnet -g '*.cs' -g '!**/obj/**'
```

The remaining file reads inspect generated test outputs/fixtures, not `src/`. The additional XML-loading source ratchet in `TaskNavigationTests` was deleted too.

PowerShell syntax check, also executed:

```powershell
$errors = @()
Get-ChildItem scripts -Filter '*.ps1' -File -Recurse | ForEach-Object {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
    $errors += $parseErrors
}
if ($errors.Count) { throw ($errors.Message -join '; ') }
```

The integration fixture always overwrites Initial Catalog with a generated `EtpPhase0Test_<guid>` name, applies all 16 migrations and drops that database. Its default is `.\SQLEXPRESS`; CI sets `ETP_TEST_SQL_CONNECTION` to Windows-integrated `(localdb)\MSSQLLocalDB` and runs `sqllocaldb start MSSQLLocalDB`.

For A0.7, the executable itself was run with `%LOCALAPPDATA%\EtpReporting\settings.json` temporarily containing:

```json
{"ConnectionString":"Server=.\\SQLEXPRESS;Database=EtpPhase0Cli_20260915;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5"}
```

```powershell
$exe = '.\src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\Etp.Reporting.Desktop.exe'
(Start-Process $exe -ArgumentList '--initialize-database' -WindowStyle Hidden -PassThru -Wait).ExitCode
(Start-Process $exe -ArgumentList '--automation-once' -WindowStyle Hidden -PassThru -Wait).ExitCode
sqlcmd -S .\SQLEXPRESS -E -C -W -h -1 -d EtpPhase0Cli_20260915 -Q "SET NOCOUNT ON; SELECT DB_NAME(),COUNT(*) FROM dbo.schema_migrations;"
```

Both exits were 0; SQL returned `EtpPhase0Cli_20260915 16`. The database did not exist before initialization. The original settings were restored byte-for-byte and the temporary database was dropped. Preserve and restore the settings file when repeating this check; never run the bootstrap check against the shop database.

UI verification launched that same Release executable against `EtpPhase0Review_20260915`, a COPY_ONLY backup/restore copy of `EtpReporting`. Apply migration 0016 to the copy, add the following failed batch, then use task search for `DSR` and `data quality`:

```sql
INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc)
VALUES(NEWID(),'Failed',SYSUTCDATETIME(),SYSUTCDATETIME());
```

A0.5 executed the actual `LoadDsrFactsAsync` SELECT with `SET STATISTICS XML ON` through `sys.sp_executesql`, with date parameters `2026-08-25`, `2026-08-25`, `2025-08-25`, `2025-08-25` and stores JSON `["WLMHW","HEMW"]`. Parameterised execution is significant: an initial ad-hoc local-variable version chose a clustered scan; the application-equivalent parameterised execution chose `IX_sales_invoices_date`. No index hint or report-query change was made. The exact executed SQL and actual XML output remain in `%TEMP%\etp-phase0-dsr-plan.sql` and `%TEMP%\etp-phase0-dsr-plan.txt`.

```powershell
sqlcmd -S .\SQLEXPRESS -E -C -b -d EtpPhase0Review_20260915 -i "$env:TEMP\etp-phase0-dsr-plan.sql" -y 0 -o "$env:TEMP\etp-phase0-dsr-plan.txt"
```

The manual review copy was dropped after verification. Recreate it before repeating copy-targeted commands. The live database was not migrated or seeded.

Build-output maintenance commands actually run successfully against that copy:

```powershell
& src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\scripts\backup-etp-database.ps1 -ServerInstance .\SQLEXPRESS -Database EtpPhase0Review_20260915 -BackupDirectory 'C:\ProgramData\EtpReporting\Phase0VerificationBackups'
& src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\scripts\invoke-etp-recovery-drill.ps1 -ServerInstance .\SQLEXPRESS -Database EtpPhase0Review_20260915 -BackupDirectory 'C:\ProgramData\EtpReporting\Phase0VerificationBackups'
& src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\scripts\new-etp-support-package.ps1 -ServerInstance .\SQLEXPRESS -Database EtpPhase0Review_20260915 -OutputDirectory "$env:TEMP\EtpPhase0Support"
```

The recovery drill returned `8` imported files, `3957` lineage rows and `PASSED` after checksum verification, restore and DBCC CHECKDB.

For the blocked service-stop part of A0.4, the command attempted was:

```powershell
Stop-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction Stop
```

Windows refused to open the service for stopping. As a separate executed fallback, startup against `tcp:127.0.0.1,65001` with a five-second timeout showed the friendly unreachable-server panel. Restoring the correct settings and pressing Retry successfully returned to the Owner welcome state.

## 3. Test results (paste the dotnet test summary lines for each project)

Final local Release build: `Build succeeded. 0 Warning(s), 0 Error(s)`; elapsed `00:00:10.11`.

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 184 ms - Etp.Reporting.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    59, Skipped:     0, Total:    59, Duration: 1 s - Etp.Reporting.Reporting.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 1 s - Etp.Reporting.Import.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   177, Skipped:     0, Total:   177, Duration: 5 s - Etp.Reporting.SqlServer.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 2 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   268, Skipped:     0, Total:   268, Duration: 11 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
```

Final CI run: [34944903179](https://github.com/sagarbora91/etpreportengine/actions/runs/34944903179), **success**, implementation commit `c57f84f2fcbc7dd7e0565c48e1c8da301fa190b0`.

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 171 ms - Etp.Reporting.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    51, Skipped:     0, Total:    51, Duration: 604 ms - Etp.Reporting.Import.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    59, Skipped:     0, Total:    59, Duration: 937 ms - Etp.Reporting.Reporting.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   177, Skipped:     0, Total:   177, Duration: 5 s - Etp.Reporting.SqlServer.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 2 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   268, Skipped:     0, Total:   268, Duration: 11 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
```

PowerShell parse errors: 0. Dependency scan succeeded with no known .NET vulnerabilities. It reported the existing `xunit 2.9.3` package as deprecated; the existing policy reports deprecation without failing the build.

Earlier failures were investigated and fixed: an indirect npm audit after package removal; SQL timeout 258 missing from friendly messages; a pool-starvation timing failure in the export scheduling test. A failed intermediate scan-script edit was corrected before the final green run. The last-Owner test initially encountered the pre-existing 51100 guard, leading to the single-trigger approach below.

## 4. Screenshots (paths)

- `docs/audit/claude-audit-2026-09/phase-0-screenshots/dsr-816x480.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/dsr-maximised.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/operations-failed-import-816x480.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/operations-failed-import-maximised.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/startup-retry-recovered.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/startup-unreachable-maximised.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/welcome-816x480.png`
- `docs/audit/claude-audit-2026-09/phase-0-screenshots/welcome-maximised.png`

All captures are from the actual Release application. Maximised captures were taken on the 1366×768 display and are 1366×728 window images, excluding the taskbar. Compact captures were taken after reducing the window to its declared 816×480 minimum; Windows Print Screen returned 811×478 visible-window images. They have not been resized or fabricated. The normal graphics-capture API failed with `SetIsBorderRequired ... 0x80004002`, so capture used Windows Alt+Print Screen and saved the clipboard image.

The compact welcome branding is clipped, and compact Operations Center has insufficient room to show its grid rows. These pre-existing layout limitations are visible in the evidence; UI redesign is outside Phase 0. At maximised size the failed batch is visibly listed as `CRITICAL`, technical status `FAIL`.

## 5. Decisions taken (anything you had to choose that the plan did not specify)

- Preserve the complete existing working tree in `05e16a9` before creating the Phase 0 branch, including already-present audit/generated changes; remove the unwanted tracked content in the following commit.
- Keep forbidden generated/knowledge trees on disk while removing them from Git, as the user expressly allowed/required. Delete the graph hook and graph-only AGENTS file themselves.
- The audit's H-1 claim was incomplete: migration 0011 already has a last-Owner check inside `trg_application_users_history`. Strengthen that existing trigger with `UPDLOCK,HOLDLOCK` and a clearer error, retaining history writes, instead of installing competing triggers. Include this in 0016 so the plan's reserved Phase 1 migration 0017 remains available.
- Preserve the seven existing chart-selection rules when deleting the visual registry's unused IDs. Export metadata now uses the existing report name; visual values and financial calculations are untouched.
- Use disposable databases and a COPY_ONLY restored review copy for all writes, including startup audit writes, failed imports, index creation and Owner changes. This prioritises the explicit instruction not to modify `EtpReporting` over the acceptance note suggesting query-plan inspection on the live database.
- Cover procedures/triggers actually touched in Phase 0. No Phase 1 or 2 procedures were implemented; the new project is their test foundation for later phases.
- Correct the existing behavioural export test's scheduling mechanism after an actual CI failure. It now uses a dedicated caller thread and asynchronous start observation, preserving the off-thread assertion and original timeout. No production exporter change was made.
- Keep .NET dependency scanning fail-closed and remove the indirect npm scan as part of the Node removal. No package upgrade was introduced.
- Repair only the broken remote-tracking ref, then fetch and push normally. No force-push, merge, or Phase 1 work.

## 6. Known gaps (anything in tasks 1 to 11 you could not finish, and why)

- Tasks 1–11 are implemented. The literal SQL-service-stopped acceptance experiment could not be completed: this Windows token lacks service-control rights. A0.4 therefore remains NOT RUN as a complete criterion, despite successful maximised launch, DSR and unreachable-endpoint/Retry checks. An elevated operator must execute its stopped-service part before phase closure.
- Migration 0016 was deliberately not applied to real `EtpReporting`. Its final read-only check still showed 490 invoices, latest date 2026-08-25, and no `IX_sales_invoices_date`. The query-plan verification used the restored copy and is explicitly scoped to it.
- Compact captures reflect the declared minimum window size but the saved visible-window PNG dimensions are 811×478, not literal 816×480 raster dimensions; maximised PNGs omit the taskbar. No image was rescaled to conceal this difference.
- All `EtpPhase0Test_*`, CLI and review databases were dropped; the SQL service remains running and original settings are restored byte-for-byte. Cleanup of remaining verification backup/support files was rejected by automatic approval review with only “blocked by policy”; no unsafe workaround was attempted. Remaining paths: `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\EtpPhase0Review_20260915.bak`, `C:\ProgramData\EtpReporting\Phase0VerificationBackups\`, and `%TEMP%\EtpPhase0Support\`. They contain only verification copies/output and may be removed by an authorised operator.
- The existing xUnit deprecation and pre-existing compact UI limitations are recorded above. They do not change the green test results. Financial/report-completeness defects listed for later phases remain out of scope.

## 7. Acceptance checklist A0.1 to A0.9 with PASS / FAIL / NOT RUN and the evidence for each

| Item | Result | Executed evidence |
|---|---|---|
| A0.1 — Fewer than 600 tracked files and no tracked file larger than 2 MB | PASS | 590 tracked files including this report; 0 exceed 2 MB. Largest: `docs/_archive-2026-09/design/ETP-UI-REDESIGN-ROUTE-COVERAGE.csv`, 455,690 bytes. Checked with `git ls-files` and filesystem lengths. |
| A0.2 — Release tests pass; zero tests read src as text | PASS | All six final local suites passed: 575 tests, zero failures/skips. Reviewed text/XML/stream file-read call sites; remaining reads inspect generated outputs or fixtures, not src. Deleted the XML-loading navigation ratchet as well as ReadAllText-based tests. |
| A0.3 — CI has no npm/node steps | PASS | Zero explicit node/npm matches in both `.github/workflows/ci.yml` and its dependency-scan script; final CI succeeded. GitHub's own action runtime is not a project Node build/test step. |
| A0.4 — Maximised title visible; stopped SQL gives friendly panel; running SQL opens dated DSR | NOT RUN | Maximised launch/title and 25-Aug-2026 DSR with data were executed and screenshot; DSR displayed combined FTD ₹53,818 and 7 units under unchanged current calculations. The actual Stop-Service command was denied by Windows, so the whole criterion cannot be marked PASS. Separate unreachable-endpoint test showed the friendly SQL panel and Retry recovered to Owner, with screenshots and an integration test. |
| A0.5 — DSR query plan uses the new index | PASS | Actual parameterised DSR plan on `EtpPhase0Review_20260915`, restored from the real 490-invoice dataset, references `[IX_sales_invoices_date]`; no hint. Both requested index definitions were validated against SQL metadata by integration tests. Live EtpReporting was intentionally not migrated. |
| A0.6 — Operations Center opens for Owner with a failed import | PASS | Inserted one Failed batch in the disposable copy, opened Data Quality/Operations Center through the app, observed “Loaded 19 daily store result(s), 3 governed quality issue(s)” and the FAILED_IMPORT_BATCH row as CRITICAL/FAIL; captured both sizes. SQL dashboard/sync test also passed. |
| A0.7 — Headless initialization honours a renamed settings database | PASS | Actual exe `--initialize-database` created `EtpPhase0Cli_20260915`; exit 0 and 16 migrations. Actual `--automation-once` also exited 0. Fixture test uses an intentionally invalid fallback connection, proving saved settings select the target. Original settings restored and database dropped. |
| A0.8 — Only Owner cannot be changed to Viewer | PASS | Direct UPDATE in the copy returned SQL 51230: “Keep at least one active Owner. Add another Owner before changing this account.” Readback remained OWNER/active=1. Tests cover demotion, deactivation, deletion, multi-row rejection and permitted changes when another Owner remains. |
| A0.9 — SQL integration project green locally and in CI | PASS | 8/8 locally on SQLEXPRESS and 8/8 in successful GitHub run 34944903179 on LocalDB; fixture-created databases are dropped after the run. Final local query found zero remaining EtpPhase0 databases. |
