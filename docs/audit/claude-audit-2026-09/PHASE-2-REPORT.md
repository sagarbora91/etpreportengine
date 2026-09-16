# Phase 2 audit fixes — 16 September 2026

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
| A2.2 | Source stock remains 814/502; no manual subtraction to 812/501. Sagar must accept source stock or approve an explicit reasoned adjustment feature. |
| A2.3 | Cash carry-forward, per-mode signed tender totals and Dr/Cr totals have an always-on golden. Default cash navigation is usable. |
| A2.4 | Existing export paths retained; private corpus continues generating Excel/PDF evidence. No claim of native Excel/Acrobat review is made by these tests. |
| A2.5 | Six independent sanitised report goldens now run without private files; the private check remains additional evidence. |
| A2.6 | Titan 2025–26 history remains missing. Synthetic LY proves behaviour but cannot substitute for the missing owner data. |

D4 remains Sagar's decision: AUTOMATIC/SEIKO/CITIZEN need cluster mapping (GAUTO/SEKOG/CTZNG), NEBULA appears only on a packaging line, and source spelling is CERUTI. Synthetic tests use an explicit synthetic brand mapping and do not approve production mappings. The source/export timing gap remains visible; no figures are invented to match the photographed sheets.
