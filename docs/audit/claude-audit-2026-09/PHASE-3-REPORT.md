# Phase 3 audit fixes — 17 September 2026

## Closure candidate — 17 September

Branch: `phase-2-3/closure`, created from Phase 5 at `0a0c38f`. Job 4 of the closure prompt is implemented for re-audit. The other three jobs are in `PHASE-2-REPORT.md`. No phase or criterion is marked closed; Claude re-audits, Sagar merges, and neither main nor Phase 5 has been merged into.

### Job 4 — touch-accessible selective retry

Import → Problems now exposes a **Retry failed** button beside the status filter. The button follows `CanRetry`, including role/busy changes; Ctrl+R remains available. The operation re-reads only the previous failed paths and preserves successful results. Changing the source chooser cannot retarget a retry. Extracted ZIP inputs remain available while the coordinator owns the source, and retry preserves sibling scope needed by empty exports. Failed files with identical names in different directories remain distinct by full path.

`ImportRetryClosureTests` imports a real sanitised R025 workbook beside a deliberately corrupt workbook through `ImportWorkspaceView` and `DesktopImportCoordinator`. It repairs the failed input with the R022 fixture and raises the actual WPF button event. The reader observes only that failed path; successful R025 files, facts and row outcomes remain unchanged. Owner and Store Manager can retry; Viewer cannot reach the write route, the button is disabled, direct method invocation is rejected, and SQL denies write/execute access. It also checks the busy state and retained Ctrl+R command.

This is automated button-path evidence, **not physical touch acceptance**. No restartable retry queue or retry export is claimed, so durability/export are not applicable under retention-audit §7. Durable outcome history and its separate-process restart test are covered by job 2.

Focused commands run in `closure-retry`:

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.Tests/Etp.Reporting.SqlServer.Tests.csproj -c Release --filter FullyQualifiedName~FolderImportServiceTests --verbosity minimal -m:1 -nodeReuse:false
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~DesktopImportCoordinatorTests --verbosity minimal -m:1 -nodeReuse:false
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --filter FullyQualifiedName~ImportRetryClosureTests --verbosity minimal -m:1 -nodeReuse:false
```

```text
Passed! - Failed: 0, Passed: 16, Skipped: 0, Total: 16
Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10
Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3
```

### Remaining acceptance

Claude still needs to install the candidate in the acceptance VM and perform A3.3's native Windows 125% DPI check. Sagar still needs to perform A3.8's timed touch walkthrough, including Retry with no keyboard. WPF layout/button tests do not substitute for those checks. The approved footer/More details design remains unchanged. Registers/investigation access is Phase 5 work. A2.2's 814/502 stock figures are owner-accepted, D4 now has the approved mapping, and A2.6's actual historical exports are deferred to Phase 5; there are no new owner design decisions requested by this branch.

The 16 September evidence below is historical and describes the earlier integration snapshot.

### Full combined validation — 17 September

Verified application/test commit: `8055d99`; the remaining changes are these reports. Commands ran from `C:\Codex\Reporting Manger\phase23-closure`:

```powershell
dotnet build Etp.Reporting.slnx -c Debug --verbosity minimal -m:1 -nodeReuse:false
dotnet build Etp.Reporting.slnx -c Release --verbosity minimal -m:1 -nodeReuse:false
dotnet test Etp.Reporting.slnx -c Release --no-build --verbosity minimal -m:1 -nodeReuse:false --logger 'trx;LogFilePrefix=closure'
```

Both builds and the final test command exited 0. Build summaries:

```text
Debug:
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:20.11

Release:
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:22.56
```

Final test summaries, **857 passed, 0 failed, 3 opt-in skipped**:

```text
Passed!  - Failed:     0, Passed:   355, Skipped:     2, Total:   357, Duration: 15 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 103 ms - Etp.Reporting.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   110, Skipped:     0, Total:   110, Duration: 16 s - Etp.Reporting.Import.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    63, Skipped:     0, Total:    63, Duration: 542 ms - Etp.Reporting.Reporting.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    79, Skipped:     1, Total:    80, Duration: 4 m 10 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
Passed!  - Failed:     0, Passed:   238, Skipped:     0, Total:   238, Duration: 984 ms - Etp.Reporting.SqlServer.Tests.dll (net10.0)
```

The three skips are the existing opt-in `PhaseThreeLiveCaptureTests`, `ExtractedWorkspaceUiSmokeTests` capture case, and `PhaseFourFullWindowSmokeTests`. The new history application-process restart test is always on and passed. Available private-corpus tests also ran. Detailed TRX files are in each test project's ignored `TestResults` directory. Console logs are outside Git in `C:\Codex\Reporting Manger\closure-validation`.

The first full run found the missing History F1 route, an obsolete test expecting Viewer to have no Import tasks, and a WPF package-resource exception during parallel shell tests. History now has contextual help and a return route; the Viewer regression asserts read-only History plus denied import/problem routes; the new report-navigation tests run in a dedicated nonparallel WPF collection. The complete solution was rebuilt and rerun successfully after those corrections. The restricted Manager connection regression passes with the parameterised receipt command.

Only new migrations 0027 and 0028 differ from the base migration set. `git diff --check` passed. This is coding/test evidence for re-audit, not phase closure or native DPI/touch acceptance.

## Historical report — 16 September

Historical branch: `integration/phase-2-3-4-fixes`. No item is marked closed; Claude re-audits and Sagar merges. The owner decisions mentioned in this earlier snapshot were resolved on 17 September as recorded above.

## Changes and regression evidence

| Item | Implemented change | Behavioural evidence |
| --- | --- | --- |
| P3-1 | The complete report catalogue uses three columns at wide sizes and one scrollable column below the existing 1000 DIP breakpoint. | The test activates every report and compares the resulting report codes with `ProductReportCatalogue.All`; all touch targets are at least 44 DIP and the full catalogue has no scroll range at full size. |
| P3-2 | Section navigation shares the 48 DIP header. Today keeps its directly accessible tabs; larger sections use a selector. Navigation moves below the header in compact windows. | The measured focused workspace is 652 DIP at a 1366×728 client area, exceeding 610 DIP. |
| P3-3 | The 28 DIP footer shows one line and an explicit hidden-line count. The persistent More details button exposes the full message. | A four-line message displays “3 more lines · More details”; the complete text is retained, and the count disappears for a short message. |
| P3-4 | Removed the drawer and overlay. Current profile occupies the normal content area; report-row details use an owned dialog; access denials stay inline. | The profile displays the current identity in the content area, then Reports opens normally. |
| P3-5 | The shared DatePicker style formats the editor after initial selection, typed Enter, and focus loss, including commits that do not change the selected date. | Eight real shell/report/staff/Cash Book date pickers accept typed input and retain `25 Aug 2026` after initial and same-date commits. The shared style covers other pickers. |
| P3-6 | Replaced the exact rail-button count with a comparison of section names against `TaskNavigation.Sections`. | The real shell's named sections match the navigation table. |
| P3-7 | Added explicit Number input scopes to staff CRO, schedule time, sharing phone and contact phone inputs. | Parameterised tests inspect each actual editor's Number scope. |
| P3-8 | Help now consistently says Operations Centre / Approval Centre. | Behavioural Help catalogue assertion checks the displayed guidance. |
| Lower priority | Density values and persisted output are Touch/Desktop; the converter accepts old numeric values and Comfortable/Compact names. DSR matrix headings expose Level2. The shell route registry is now only a small legacy destination-to-module map, with screen information in TaskNavigation. | Legacy/current density round trips, a rendered DSR heading assertion, and existing navigation/access/history/help tests pass. |
| Compact report review | Both date pickers and Refresh stay visible; the existing Actions menu contains exports. The detail search label moves beside its field and the compact table removes its top gap. | At 816×440 a complete 44 DIP Cash row is inside the scrolling viewport, with search, variance and details available. Tests also verify export dispatch and invalidation after a date change. |

The changes are split into commits `f3abf67`, `bd05c70`, `f2a8d6b` and `d261268`, followed by evidence updates. The full solution build also caught stale density-enum references in `tools/Etp.Reporting.UiSmoke`; these now use Touch/Desktop and no longer invoke the deleted drawer handler. Financial calculations were not changed by these Phase 3 fixes.

The footer deliberately uses the audit's one-line-plus-hidden-count option, preserving the 28 DIP footer and 652 DIP content area. This is the proposed interpretation of the plan's “up to three lines” requirement; it is not a claim that an expanding three-line footer, or its acceptance, has been verified.

The integration correction also exposes the existing brand editor to Store Managers through a narrow `masters` route exception. Monthly targets and other administration routes remain Owner-only; SQL permissions enforce the same distinction. The displayed master workspace now uses the evening brand/target editor. Its integration tests and commit are recorded in the Phase 4 report.

## Commands and results

The final shared solution gate after the smoke-tool compatibility fix passed both Debug and Release builds with zero warnings/errors. `dotnet test Etp.Reporting.slnx -c Release --no-build --verbosity minimal` passed **830 tests, 0 failed, 3 opt-in skipped**. The final Desktop summary is:

```text
Passed!  - Failed:     0, Passed:   350, Skipped:     2, Total:   352, Duration: 34 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
```

The fixture capture was run separately and passed. Full commands and all six project summaries are recorded in `PHASE-4-REPORT.md`; earlier focused checks follow.

```text
dotnet build src/Etp.Reporting.Desktop/Etp.Reporting.Desktop.csproj -c Release --no-restore --verbosity quiet
Build succeeded.
    0 Warning(s)
    0 Error(s)

dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj -c Release --no-restore --verbosity minimal
Passed!  - Failed: 0, Passed: 334, Skipped: 1, Total: 335

dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PhaseThree|FullyQualifiedName~Phase3Shell|FullyQualifiedName~UiNavigationTests|FullyQualifiedName~HelpCentreTests|FullyQualifiedName~ShellNavigationServiceTests|FullyQualifiedName~ShellViewModelTests|FullyQualifiedName~FocusedReportScopeTests|FullyQualifiedName~ReportsRequestOrderingTests" --verbosity minimal
Passed!  - Failed: 0, Passed: 109, Skipped: 1, Total: 110
```

The full Desktop run preceded the final heading/Help/navigation assertions; the final focused run includes those changes, the compact Cash row regression, both Cash date inputs and the integration export-audit tests. The skipped focused case is the explicit live-database capture harness, which was also executed separately below. No private source corpus is needed for these regression tests.

```powershell
dotnet run --project tools/Etp.Reporting.ImportAudit/Etp.Reporting.ImportAudit.csproj -c Release -- --database EtpPhase1Test_IntegrationUiReview --folder tests-dotnet/fixtures/etp-sample
$env:ETP_PHASE3_LIVE_UI_EVIDENCE = '<repository>\docs\audit\claude-audit-2026-09\phase-3-fix-screenshots'
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj -c Release --no-restore --no-build --filter FullyQualifiedName~PhaseThreeLiveCaptureTests --verbosity minimal
```

```text
Audit database ready: EtpPhase1Test_IntegrationUiReview
31 files imported. R010 was rolled back because its synthetic stock row conflicts with R011's stock row.
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

That ImportAudit invocation exited 1 for the conflicting fixture. This is not the all-files-success acceptance evidence for the integration boundary; the dedicated Store Manager import test supplies that evidence in the Phase 4 report. The retained disposable database was used only for visual review; no live shop database was opened. After final capture, only `EtpPhase1Test_IntegrationUiReview` was dropped and `DB_ID` returned `NULL`.

## Visual evidence and remaining checks

The 20 refreshed images in [phase-3-fix-screenshots](phase-3-fix-screenshots) render the actual composed WPF shell against the disposable fixture database. They use client areas of 1366×728 and 816×440, excluding the Windows frame/taskbar. They include Sales, Walk-ins, Import, the complete Reports list, Stock, Settings, Cash, Customer-wise Invoices, Staff/CRO and the populated brand master. Cash starts from Both stores and defaults to Titan World; Customer-wise Invoices and CRO use the fixture's populated Helios scope. The final capture asserts that a complete Cash row fits above the horizontal scrollbar at compact size. The wide CRO image shows Unique invoices / AUPT / ATV.

- [Full report catalogue](phase-3-fix-screenshots/Reports-1366x728-wpf.png)
- [Today/Sales at full size](phase-3-fix-screenshots/Sales-1366x728-wpf.png)
- [Today/Sales at compact size](phase-3-fix-screenshots/Sales-816x440-wpf.png)
- [Cash Book dates at compact size](phase-3-fix-screenshots/Cash-816x440-wpf.png)
- [Customer-wise Invoices at compact size](phase-3-fix-screenshots/Customer-invoices-816x440-wpf.png)
- [Staff/CRO detail labels](phase-3-fix-screenshots/Staff-CRO-1366x728-wpf.png)
- [Populated brand master as Owner](phase-3-fix-screenshots/Brand-master-1366x728-wpf.png)

Native launch used an explicit `--connection-string` to the disposable database. Native accessibility successfully enumerated the running application, but the computer-use screenshot API failed twice with `SetIsBorderRequired failed: No such interface supported (0x80004002)`. Clicking then reported `coordinate input geometry is unavailable`; setting an editor reported `Requested property was not in the CacheRequest (0x80070057)`. Native interaction verification was stopped. These WPF images are therefore not claimed as native screen captures, and no successful native typed-date edit is claimed.

The owned native application was closed. User `settings.json` and `ui-preferences.json` hashes were unchanged. Actual Windows 125% DPI and the timed shop-staff touch walkthrough remain unverified. The bitmap-scale regression is a layout check, not Windows DPI acceptance.
