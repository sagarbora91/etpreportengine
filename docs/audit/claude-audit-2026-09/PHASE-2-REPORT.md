# Phase 2 audit fixes — 17 September 2026

## Closure candidate — 17 September

Branch: `phase-2-3/closure`, based on `phase-5/secondary-modules` at `0a0c38f`. This addendum supersedes the earlier pending decisions below. The candidate implements jobs 1–3 of `PHASE-2-3-CLOSURE-PROMPT.md`; job 4 is recorded in `PHASE-3-REPORT.md`. No phase or criterion is marked closed. Claude re-audits and Sagar merges; neither main nor Phase 5 has been merged into.

### Job 1 — approved D4 mappings

New migration `0027_approved_evening_brand_rows.sql` replaces only the two approved stores' mapping rows. Titan has TITAN, Raga, EDGE, SONATA, Fastrack (both source values) and XYLYS. Helios has SEIKO/SEKOG, FOSSIL/FOSLG+FOSLL, TOMMY HILFIGER, CERUTI, KENNETH COLE, CITIZEN/CTZNG, POLICE and ANNE KLEIN. There is no HELIOS mapping to intercept cluster matches. NEBULA/AUTOMATIC are removed. Production financial C# and earlier migrations are unchanged.

`ApprovedBrandMappingTests` verifies a fresh install and an upgrade from 0026, unchanged existing migration receipts and facts, another store's retained mappings, a new physical connection, and opened Excel cell values/date scope. Its always-on fixture is synthetic. Existing brand-master role tests exercise actual Owner/Manager/Viewer reachability and SQL denial.

Real acceptance imported the two authoritative R025 workbooks for 1 July–25 August into a generated disposable database. All 14 approved rows were nonzero. No private workbook, source row or private export was added to the repository.

| Store | Mapped rows | Other / unmapped | Store total |
|---|---:|---:|---:|
| WLMHW | 2,006,980.25 | 136,473.50 | 2,143,453.75 |
| HEMW | 1,589,739.10 | 46,775.00 | 1,636,514.10 |

TITAN is **1,017,619.00** and SEIKO is **563,500.00**. Independent raw XML summation with Python `Decimal` confirmed that the audit rounded four source amounts: Raga **435,267.25**, EDGE **271,585.50**, SONATA **131,058.50**, KENNETH COLE **96,788.10**. Tests preserve source precision. The independent script is outside the repository at `C:\Codex\Reporting Manger\closure-brand-evidence\independent_source_check.py`.

Commands run in the `closure-brands` worktree:

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~ApprovedBrandMappingTests|FullyQualifiedName~CrossPhaseStoreManagerImportTests.Manager_duplicates_supersets_and_brands_preserve_fact_denials_and_locked_dates|FullyQualifiedName~CrossPhaseStoreManagerImportTests.Viewer_cannot_edit_brands_even_with_accidental_execute_grant' --logger 'console;verbosity=detailed'
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~EveningMastersAccessTests --verbosity minimal
```

```text
Test Run Successful.
Total tests: 6
     Passed: 6
 Total time: 21.6781 Seconds
Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

### Job 2 — durable import history

Import → History defaults to **Imports**, with **Received files** retained as the separate document inbox. The new view queries persisted `import_files`, `import_batches` and aggregated `import_row_outcomes` on activation and header date/store changes; it never uses `latestResults`. Source-row aggregation prevents R022's multiple fact projections from inflating the displayed counts. Newest outcomes appear first, with file/report/store/period/status/counts and selected diagnostics. Attempts whose date cannot be detected explicitly use their recorded UTC date. All assigned roles can read history; importing remains Owner/Manager-only. F1 opens the new import-history guide, whose workspace link returns to this read-only task.

**Reasoned plan amendment:** a read-only query over those three tables cannot retain every duplicate or pre-persistence failure. Exact duplicates create no new canonical file, and the existing hash/scope uniqueness rule must remain intact. New migration `0028_durable_import_attempts.sql` adds separate append-only attempt receipts and a narrowly granted recording procedure. The canonical facts, existing day locks and previous migrations are unchanged. The query represents the original receipt once alongside its canonical row and retains subsequent receipts, including duplicates, failures and cancellations. Direct DML is denied; Viewer cannot execute the recording procedure.

Messages are replaced with safe guidance and columns are allowlisted against approved schema headers on both write and read. Codes and source row numbers remain available. Review regressions cover an unexpected header containing sensitive text and linked failed/cancelled/subsequent attempts that would otherwise disappear.

`DurableImportHistoryTests` launches the **actual composed application in four separate processes**: import three sanitised workbook families, exit; reopen/read, exit; repeat import, exit; reopen/read. It checks persisted row outcomes against SQL, three new Duplicate entries alongside the three earlier entries, unchanged facts, selected diagnostics, and application-process termination between assertions. A test-only helper refuses databases outside the generated `EtpPhase0Test_` namespace. Separate SQL tests cover date/store filtering, safe diagnostics, Manager recording, Viewer read/execute denial and direct-write denial; navigation checks cover all three assigned roles. History has no export feature, so export regression is not applicable under retention-audit §7.

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --filter FullyQualifiedName~DurableImportHistoryTests --verbosity minimal -m:1 -nodeReuse:false
```

```text
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 23 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
```

Review also reproduced concurrent duplicate misclassification: two services passed the preliminary existence check, then the second reused the first file's NEW outcomes after the SQL lock. The persistence adapter now compares the committed file's immutable batch identity with the attempted batch and reports the losing attempt as Duplicate. `ConcurrentImportAttemptTests` holds the existing SQL import lock until both requests wait, then verifies one file/batch, no extra facts, and both durable attempts/history entries across R025, R022, R020 and R013. No SQL lock or financial formula was changed. Receipt writes use a parameterised `EXEC` command, so the existing restricted-connection role observer verifies them as well.

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter FullyQualifiedName~ConcurrentImportAttemptTests --verbosity minimal
```

```text
Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 12 s
```

### Job 3 — advanced report filters

The actual focused report header now contains a **Filters** expander with Apply/Clear. It attaches the existing query inputs on every activation, including cached report navigation. Store, brand segment, transaction type and item drive SQL scope; detail-row search remains a separate display filter. Unsupported inputs on specialised reports are disabled and cleared. The applied scope appears above the grid and in Excel/PDF. Editing scope invalidates stale export actions, and refresh preserves the typed store filter. The unreachable `report-filters` dispatch was removed.

The real SQL/WPF regression runs Owner, Store Manager and Viewer through Brand-wise Sales: original **767.00**, segment **531.00**, store **59.00**, transaction type **118.00**, nonmatching item **0.00**, then Clear **767.00**. It opens the Excel file, validates its schema, decodes PDF text, compares totals and scope, and verifies detail search does not alter export totals. SQL read access and Viewer/Manager write denial are exercised. No filter persistence/history is claimed; restart durability is not applicable.

Commands run in the `closure-filters` worktree:

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests -c Release --no-restore -m:1 -nodeReuse:false --filter FullyQualifiedName~ReportFilterClosureTests --verbosity minimal
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~ReportFilterNavigationTests|FullyQualifiedName~PhaseThreeAuditRegressionTests|FullyQualifiedName~ReportsRequestOrderingTests|FullyQualifiedName~ReportsPresentationStateTests' --verbosity minimal
dotnet test tests-dotnet/Etp.Reporting.Reporting.Tests -c Release -m:1 -nodeReuse:false --verbosity minimal
```

```text
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1
Passed! - Failed: 0, Passed: 34, Skipped: 0, Total: 34
Passed! - Failed: 0, Passed: 63, Skipped: 0, Total: 63
```

### Scope and acceptance limits

Sagar accepted A2.2's source stock **814/502**; there is no paper adjustment. A2.6's actual Titan 2025–26 and Service sources are deferred to Phase 5; synthetic history is not a substitute. Registers/investigation access remain Phase 5. The new UI is retained. Native 125% DPI and timed shop-staff touch acceptance remain with Claude/Sagar. No existing day-lock trigger, connection policy, append-only audit design, signing/certificate wiring or backup rotation was changed.

The earlier 16 September evidence below is historical and describes a different integration snapshot.

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

Branch: `integration/phase-2-3-4-fixes`. This report records implementation and verification for re-audit. No phase or acceptance criterion is marked closed; Claude re-audits and Sagar merges.

## Changes and audit items

| Item | Change and behavioural evidence |
|---|---|
| P2-1 | Restored `ApplyReportFilter()` after loading DSR, Customer-wise Invoices and Cash Book. Cash uses a separate filterable collection because WPF's default `DataView` view cannot accept a predicate; search reads actual cash-book cells. `EveningReportInteractionTests` sets search/variance before each report's first run, asserts visible row counts, clears the filter and verifies complete rows return. Export data stays unchanged. |
| P2-2 / C10 | The integration ancestry already contains commit `0cd05e8`, whose Sales Summary warns and excludes unknown transaction types instead of blocking the period. DSR likewise excludes them. `SalesTransactionConsistencyTests` inserts legacy INV, SR, BC, unknown and null-type facts and runs both SQL-backed paths: both retain signed sales of 59 and quantity 0; Sales Summary passes and warns that two rows were skipped. No financial amount was changed for C10. New imports already warn and skip unknown sales types in staging. |
| P2-3 | Added six separate always-on SQL golden tests, one per evening report, plus the CRO financial-year identity regression. `tests-dotnet/fixtures/evening-reports/golden.json` contains entirely synthetic values; the fixture applies them to the existing sanitised XLSX shapes for R011/R013/R020/R022/R024/R025. It covers two stores, multiple product rows per invoice, signed returns/cancellations, reused invoice numbers across financial years, prior-year comparison, targets, cash carry-forward, customer names and physical counts. No private workbook or source row is copied. The existing private-corpus test remains and now verifies independently recomputed per-CRO aggregates. Private export review files go under the temporary `EtpPhase2Review` directory, outside the repository. |
| P2-4 | Phase 3 navigation is generated from the catalogue and already displays **Customer-wise Invoices** and **Cash Book**. Added behavioural navigation/search assertions for both exact labels. The old names remain only as search aliases; Physical Stock is the separate stock report, not the Cash Book button. |
| P2-6 | Opening Cash from Both stores selects **Titan World** in the shared header before activation. A selected Helios or Titan World scope is preserved. Focused workspace and direct report loading use the same default; the cash query runs immediately rather than displaying Select a store. Tests cover all three incoming scopes. |
| P2-7 | Sagar confirmed the definitions below. CRO ATV/AUPT now divide by distinct INV documents, retaining signed INV/SR/BC sales and quantity in the numerators. Product lines on one invoice count once; financial year participates in identity; return-only CRO rows have no denominator and show a missing ratio. Screen and pack headers say **Unique invoices** and **AUPT**. |

Files changed: `ReportsWorkspaceView.xaml.cs`, `ReportTaskScope.cs`, `ReportWorkspaceControls.cs`, `PageSearch.cs`, the cash-scope section of `TaskNavigator.cs`, `OperationalReportRepository.cs`, `DailyReportingPackService.cs`, and the tests/fixture listed above. There are no Phase 2 migration changes.

## Owner-confirmed CRO definitions

Sagar's instruction during this fix session: “ATV = CRO net sales / CRO unique number of invoices; AUPT = CRO net quantity / CRO unique number of invoices.” The existing invoice rule excludes SR/BC documents from the denominator. The master plan remains untouched because Claude owns its corrections.

| Measure | Previous application definition | Confirmed definition |
|---|---|---|
| ATV | Signed CRO sales / distinct INV, SR and BC documents | Signed CRO sales / distinct INV invoices |
| AUPT | Signed CRO quantity / distinct INV, SR and BC documents | Signed CRO quantity / distinct INV invoices |
| Identity | Store/CRO group with financial year and document number | Same identity; multiple items do not create extra invoices |
| No INV invoice | Return/cancellation documents could supply a denominator | Null ratio; no invented zero or denominator |

The photographed owner sheet displays per-CRO UPT of 1.00 on most rows and total UPT 1.11; those displayed values are not a consistent application of the newly confirmed formula. The private export independently yields August Titan net sales **938,197**, net quantity **196**, and **178** unique INV documents. The photo's **973,862 / 199 / 179** includes the later invoice absent from the export. No source amount or stock count is adjusted to match the photo.

The private R013 golden uses anonymous aggregate rows ordered by sales: `(213660.5,39,37)`, `(177432.5,37,38)`, `(166775.5,35,33)`, `(132041.5,26,19)`, `(129547.5,29,27)`, `(89880.5,24,22)`, `(28859,6,2)` for `(net sales, net quantity, INV invoices)`. These were recomputed independently with Python/openpyxl from the private workbook, applying the approved negative SR/BC sign before grouping; staff/customer identities are omitted.

## Commands and results

Run from the integration worktree:

```powershell
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj --filter FullyQualifiedName~EveningReportInteractionTests --no-restore -v minimal
```

```text
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11, Duration: 822 ms - Etp.Reporting.Desktop.Tests.dll (net10.0)
```

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj --filter FullyQualifiedName~SanitisedEveningReportGoldenTests --no-restore -v minimal
```

```text
Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 439 ms - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
```

The SQL tests bootstrap generated disposable databases through the complete merged migration set. They require the configured local SQL test instance but not the private corpus. Earlier attempts encountered other concurrent test/build processes holding output DLLs and an in-progress shell edit; those were build coordination failures, followed by the successful targeted runs above.

```powershell
dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj --filter 'FullyQualifiedName~Six_evening_reports_use_real_25_Aug_sources|FullyQualifiedName~SalesTransactionConsistencyTests' --no-restore -c Release -v minimal
```

```text
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 1 m 4 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
```

This private-corpus run verifies the unchanged real six-report figures and the new owner-confirmed per-CRO golden.

After visual review, the Cash Book/CRO detail-grid correction was verified with:

```powershell
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests/Etp.Reporting.Desktop.Tests.csproj --no-restore --filter 'FullyQualifiedName~EveningReportInteractionTests|FullyQualifiedName~Phase3ShellTests.Table_columns_format_dates_amounts_and_identifiers_without_losing_rows' -v minimal
```

```text
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16, Duration: 3 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
```

## Visual evidence and limits

The interaction tests instantiate the real WPF report workspace, execute report loading and inspect the visible row collection and scope selection. Visual review then caught Cash Book's detail grid exposing `DataRowView` infrastructure properties instead of its actual columns. The correction preserves the table schema during filtering, including empty results, and uses typed date/money cells. Rendered regression assertions inspect the actual column headings and displayed date, store, debit/credit, Indian-grouped amounts and missing-value cells. Staff/CRO detail headings now explicitly say Unique invoices, AUPT and ATV.

The actual composed shell against a disposable synthetic database is rendered in [Cash Book](phase-3-fix-screenshots/Cash-1366x728-wpf.png), [Customer-wise Invoices](phase-3-fix-screenshots/Customer-invoices-1366x728-wpf.png) and [Staff/CRO](phase-3-fix-screenshots/Staff-CRO-1366x728-wpf.png), with compact counterparts in the same folder. The shared application launch and shell verification are recorded in `PHASE-3-REPORT.md`. Native screenshot capture hit an operating-system capture failure; the WPF client-area images at 1366×728 and 816×440 are not claimed as native screen captures at 1366×768 and 816×480. Native/DPI and shop-staff touch acceptance remain for re-audit. No phase is closed on the strength of the tests alone.

## Acceptance and remaining owner items

The final shared gate passed Debug and Release solution builds with zero warnings/errors, then `dotnet test Etp.Reporting.slnx -c Release --no-build --verbosity minimal`: **830 passed, 0 failed, 3 opt-in skipped**. Private-corpus cases ran successfully. Full commands and project summary lines are in `PHASE-4-REPORT.md`; the sanitised and private Phase 2 focused results above remain the specific numeric evidence.

| Criterion | Self-assessment for re-audit |
|---|---|
| A2.1 | Existing source figures and INV-only DSR denominator retained. Sanitised golden verifies signed amounts, store/combined totals, targets and history. The plan's MTD 182/38 text remains for Claude to correct to 178/37. |
| A2.2 | Source stock remains 814/502; Sagar accepted it on 17 September. No manual subtraction or adjustment feature is requested. |
| A2.3 | Cash carry-forward, per-mode signed tender totals and Dr/Cr totals have an always-on golden. Default cash navigation is usable. |
| A2.4 | Existing export paths retained; private corpus continues generating Excel/PDF evidence. No claim of native Excel/Acrobat review is made by these tests. |
| A2.5 | Six independent sanitised report goldens now run without private files; the private check remains additional evidence. |
| A2.6 | Actual Titan 2025–26 history remains unavailable and is deferred to Phase 5 by Sagar. Synthetic LY cannot substitute for owner data. |

D4's earlier pending decision is superseded by Sagar's exact mapping and migration 0027 documented above. AUTOMATIC and NEBULA are removed. The source/export timing gap remains visible; no figures are invented to match the photographed sheets.
