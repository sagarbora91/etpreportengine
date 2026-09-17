# Phase 3 — Touch shell implementation report

Date: 16 September 2026
Branch: `phase-3/touch-shell`
Base: Phase 2 `9241e26` (`phase-2/evening-reports`)

## Outcome

The Phase 3 shell implementation is delivered on an isolated branch. It reuses the Phase 2 reports, import coordinator, daily-entry commands, pack generation, exports and access checks. It does not replace the report engine or change SQL migrations, calculations, tender mapping or saved report contracts.

**Coding delivery is separate from acceptance closure.** The automated suite, component renders and calendar checks provide implementation evidence. Native-window screenshots, a complete UI Automation inventory, actual Windows DPI/high-contrast checks and the timed shop-staff touch walkthrough are still required before Phase 3 can be marked CLOSED. No deployment or branch merge was performed.

The source requirements are [the approved master plan](ETP-MASTER-AUDIT-AND-PHASED-PLAN.md) and [Claude's UI audit, section 10](claude-audit-2026-09/06-ui-ux.md). Phase 2 findings remain in [the reuse audit](PHASE-2-REUSE-AND-GAP-AUDIT.md) and [implementation report](PHASE-2-REPORT.md).

## Requirement comparison

Paths below are relative to `src/Etp.Reporting.Desktop/` unless stated otherwise.

| Plan item | Implementation and reuse | Evidence / acceptance limit |
|---|---|---|
| 1. Five-section shell, two levels, remove redundant navigation | `MainWindow.xaml`, `MainWindow.Shell.cs`, `TaskNavigation.cs`, `Shell/TaskNavigator.cs`. Today, Import, Reports, Stock and Settings stay visible. Section tabs select actual tasks. Related tasks share a selector; all reports also have a direct list. Role resolution opens Sales automatically. Setup overlay remains for missing database access. Removed module/category overview builders, twelve legacy hosts, sidebar, breadcrumbs, dead tile/drawer classes, ownership/location/presentation registries and Stock Reports/Masters route aliases. Report controls are cached per report from the existing catalogue. | Navigation/role tests, report catalogue reachability tests, startup-to-DSR assertion and six-screen renders. A complete role-by-role native walkthrough remains open. Secondary-module workflow aliases assigned to Phase 5 remain in their section selectors. |
| 2. Chrome budget | `MainWindow.xaml` has a 48 DIP header and 28 DIP footer. Search opens from the rail. At a 728 DIP client height, the section canvas is 652 DIP, including its tab row; the focused task host is about 604 DIP after the 48 DIP tab strip. | Six primary views fit at 1366×728 client DIP. The plan's 610-pixel content target is met for the section canvas, **not** for the focused task host if tabs must be excluded. Keep that measurement distinction explicit at acceptance. A native 1366×768 screenshot has not been substituted by an offscreen render. |
| 3. Today: Sales, Cash, Walk-ins, Close day | `Modules/Reports/TodaySalesView.cs` renders the existing `DailySalesReportDocument`: KPI strip, store blocks, service and targets. Full matrix retains the complete Phase 2 comparison view. PDF, Excel and Share are visible. Cash opens the existing cash-book report; Cash and service entries and Expense entry reuse existing entry commands. Walk-ins preselects the field, focuses the count and defaults its reason. Close day retains readiness, Finalise and pack generation. | Report/calculation tests remain unchanged. Layout uses synthetic rows; full matrix/export fidelity remains covered by Phase 2 reporting tests. Cash field switching and blank validation are checked. Share opens the existing archived-report sharing workflow: select an archived report and recipient there. It does not silently send or automatically attach the current preview. Phase 5 owns the improved per-pack sharing workflow. |
| 4. Import and Problems | Existing folder detection, progress, cancellation and per-file results remain in `Modules/Imports/ImportWorkspaceView`. `ImportProblemsView.cs` combines the current batch problems with persisted Source Inbox problem documents, using a single status filter. Store names display as Titan World/Helios. Cancellation asks for confirmation. | Existing import tests plus populated results layout. Problems reads the existing inbox API's latest 500 documents, plus the current batch. It is not an unbounded historical query. History remains separately reachable. Real corpus imports were not repeated against live data. |
| 5. Reports and Stock | `ReportListView.cs`, `ReportWorkspaceControls.cs`, `Modules/Reports/ReportWorkspaceSession.cs`, `Controls/TablePresentation.cs`. Visible PDF/Excel buttons, one cached control per report, typed columns, Indian amount/date formatting, numeric alignment, store labels, and a bounded detail grid. Existing row filters/drilldown and brand stock entry are retained. Removed the old hidden report-button catalogue. | Catalogue, workspace, report ordering and export tests. Six populated stock rows and a populated import result render at all three sizes. Large datasets intentionally scroll inside their table; the page does not grow indefinitely. Typed columns are generated from row metadata rather than maintaining a second report schema. |
| 6. Shared date/store | `Shell/TaskNavigator.cs` and `Shell/ShellScopeApplier.cs` apply the header scope to daily, register, accounting, archive and document tasks. Scope changes resolve old-scope drafts before applying the new scope, and are blocked during active work. Day reports use the header date; range reports retain From/To. Imports do not silently change the selected date. | Header date is checked on first and repeated Walk-ins visits. Existing draft/request-ordering tests remain. Fixed-scope reports, including the two-store DSR, normalize and disable the header store to describe their actual scope. Historical import restatement overrides and target effective dates remain explicit data fields. They do not retarget the shell. |
| 7. Touch targets and density | `Themes/Controls.xaml`, its code-behind, `Themes/Spacing.xaml`, `DensitySelector.cs`, `DailyWorkflowTouchLayout.cs`. Touch defaults to 44 DIP; Desktop uses 36. Combo items, tabs, radio/checkbox areas, rows, expander headers and calendar cells use touch targets. Scrollbars use 24 DIP. Numeric entry boxes receive a number input scope. Existing brand-stock numeric input scopes are reused. | Six rendered primary screens measure Buttons, ToggleButtons, TextBoxes, ComboBoxes, tabs and realized grid rows. Calendar tool verifies 42 day cells. This is **not** an exhaustive native UI Automation dump of every secondary screen or popup. Scrollbar arrows use the explicit 24-pixel scrollbar design rather than the 44-pixel button rule. |
| 8. Validation and feedback | Existing presentation validators retained; blank Walk-ins reports exactly `Enter the walk-in count` before constructing the command service. Cash input requires amount, store and reason. Task status wrapping and the rail's More details expose complete current-task messages. `DesktopFriendlyError.cs` removes parameter suffixes and replaces SQL errors with safe descriptions. `ConfirmationSheet.cs`, `OperationProgress.cs`, `Modules/Reports/ExportStaging.cs` provide confirmation, progress/cancel and destination-preserving export cancellation. Finalise, Reopen, Waive, Reject, import cancellation and backup/drill/automation launch handlers confirm. Success status produces a transient toast. | Blank-input, cancellation, existing operation-state and out-of-order completion tests. Cancellation is cooperative: the current atomic operation may finish; cancelled finalisation/pack messages ask the user to refresh status. Imported rows already committed are not rolled back. Task statuses/toasts are capped; the fixed footer shows a short viewport and More details holds the complete message. Existing legacy secondary forms still need visual validation for unusually long messages. |
| 9. Culture, theme and staff copy | `PresentationCulture.cs` initializes en-IN and dd MMM yyyy. `Themes/Theme.xaml` is the shared resource entry point. DSR/dashboard/report-workspace brush literals use theme resources. Staff labels replace governed/canonical/lineage/immutable wording. Growth colour follows sign. | Currency/date conversion tests and date picker parsing checks. Search hits for `invoice-lineage` remain stable internal report IDs; the comment and search keyword are not visible copy. Long explanatory Help content is intentionally retained pending the Phase 5 Help rewrite; this is not a claim that every Help sentence is at most 12 words. |
| 10. DPI and accessibility | `app.manifest` declares PerMonitorV2. The responsive threshold is 1000 DIP. Heading levels identify shell/report/card titles. F6 traverses rail, header, content and footer. Ctrl+L focuses the header for day reports. Theme startup provides system-colour high-contrast fallbacks. | Renders at 816×440 client DIP and 1093×582 at 1.25 bitmap scale. These simulate size constraints, not changing Windows monitor DPI. Actual monitor movement, high-contrast mode and assistive-technology behaviour need native acceptance. |
| 11. Remove IT landing dashboard | `Modules/Dashboard/DashboardView.cs` removes dead card builders and the IT overview. Startup routes to Sales. A health line appears under Settings → Database; the rail only shows its attention message for critical database health. Audit history remains available. | Dashboard/navigation tests and source inspection. Actual backup/security state is not altered by this UI change. |

## Reuse and branch boundaries

- No changes under `database/`, `Etp.Reporting.Domain`, `Etp.Reporting.Application`, `Etp.Reporting.Reporting` or `Etp.Reporting.Infrastructure.SqlServer` compared with `9241e26`.
- DSR figures still come from the Phase 2 document; the compact summary is another view of it. Full matrix and exports retain the complete report. Missing figures remain unavailable, not fabricated zeroes.
- Cash opening, deposit, adjustment, counted cash and service entries use the existing manual-input pipeline. No second cash ledger was created.
- Imports use the existing desktop coordinator. Brand stock uses the Phase 2 entry window and masters.
- Source Inbox and other secondary-module services remain available. Their Phase 5 replacement/removal is not smuggled into this shell change.
- Phase 4 remains separate at `df35d90` on `phase-4/security-operations`. Its security/operations implementation was not merged. The measured file overlap is `App.xaml.cs`, `Modules/DailyWorkflow/DailyWorkflowWorkspaceView.xaml.cs`, `Modules/Dashboard/DashboardView.cs`, `Shell/TaskNavigator.cs`, `DashboardViewTests.cs` and `ExtractedWorkspaceUiSmokeTests.cs`. Reconcile those changes and the associated operations/settings contracts before deployment; do not take either side wholesale on conflict.
- Existing Phase 2 report acceptance items (brand-row approval, missing Titan prior-year evidence and stock cut-off validation) remain open as documented in the Phase 2 report. A new UI does not close them.

## Validation

Commands run from this worktree:

```powershell
dotnet build Etp.Reporting.slnx -c Release --no-restore --verbosity quiet
dotnet test Etp.Reporting.slnx -c Release --no-build --verbosity quiet --logger trx --results-directory artifacts/phase3-tests/final
dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests -c Release --filter FullyQualifiedName~Phase3ShellTests --verbosity quiet
dotnet run --project tools/Etp.Reporting.UiSmoke -c Release --no-build -- artifacts/phase3-controls --controls
git diff --check
```

The solution build passes with zero warnings/errors. The full solution test run passes **680 / 680**, with zero failures or skips:

| Project | Passed |
|---|---:|
| Domain | 12 |
| Reporting | 63 |
| Import | 110 |
| SQL Server unit/boundary | 188 |
| Desktop | 276 |
| SQL Server integration | 31 |

After adding the final heading metadata and startup assertion, the solution was rebuilt and the entire Desktop suite passed again (276 / 276). The separate control audit passed typed-date parsing, calendar selection, all 42 day-cell targets and status-dialog composition. `git diff --check` is clean. Obsolete navigation-registry tests were replaced with tests of the actual task table; the total count is not presented as a simple count of added tests. SQL integration tests create randomly named `EtpPhase0Test_*` databases and dispose them; their fixture overrides the configured database name. No test is pointed at the live application's database.

`tests-dotnet/Etp.Reporting.Desktop.Tests/Phase3ShellTests.cs` supplies reproducible fixtures and checks:

- Role resolution goes straight to DSR without a Continue click.
- Header scope follows the Walk-ins editor across revisits; blank count validation stays inline.
- Switching between cash and walk-in entry restores the correct field visibility and validation.
- Sales, Walk-ins, Import, Reports, Stock and Settings render at three sizes; full-size fixtures have no vertical overflow. The stock/import fixtures contain rows.
- Realized interactive controls meet 44 DIP, including expander toggles.
- Changing DSR scope disables the old matrix as well as export actions.
- Cancelling export preserves an existing destination and removes temporary output.
- Typed columns preserve rows and identifiers while formatting dates and amounts.

Generated evidence stays in ignored `artifacts/phase3-review/`, `artifacts/phase3-controls/` and `artifacts/phase3-tests/`. The 18 review images show synthetic fixtures and explicitly say so. They are not live-shop acceptance screenshots and are not included in the commit. The UI smoke tool now requires an explicit connection string for modes that create a database-backed shell.

## Acceptance status

| Criterion | Status |
|---|---|
| A3.1 Zero-tap DSR, short import/Walk-ins paths | Implemented and startup route tested. Native tap-count confirmation open. Folder chooser interactions are additional OS interactions. |
| A3.2 Six full-size screens without scrollbars | Synthetic full-client-size render checks pass, including populated stock/import. Native maximized screenshots with representative real data remain open. |
| A3.3 Compact and 125% reachability | Responsive fixture renders pass. Actual Windows 125% DPI and every expanded secondary form remain to verify. |
| A3.4 Every interactive target ≥44 | Shared styles, primary rendered controls and calendar cells checked. Exhaustive native UIA inventory remains open; scrollbars follow the plan's 24-pixel exception. |
| A3.5 Header date → Walk-ins | Automated pass, including a revisited editor. |
| A3.6 Blank count message | Automated pass with exact required text, before a database command. |
| A3.7 Staff wording | Source scan has only internal IDs/comment/search metadata matches. Full visual copy review belongs with the walkthrough. |
| A3.8 Touch-only workflow under three minutes | **Not run.** Requires a shop-staff participant and a physical touch device. |

## Next steps, in order

1. Review the branch and generated synthetic images; do not rebuild existing report or import services.
2. Run native acceptance against an isolated restored database: full-size screenshots, actual 125% DPI, all expanded forms/popups, keyboard/F6 and high contrast. Record any differences as targeted UI fixes.
3. Have shop staff complete the timed import → walk-ins/cash → DSR → PDF workflow. Capture the duration and result; this is the remaining human acceptance evidence.
4. Reconcile the separate Phase 4 branch, rerun its security/operations checks, and complete Phase 2 report approvals before release validation.
5. Carry out the approved Phase 5 secondary-module work separately. No deployment is part of this sprint.
