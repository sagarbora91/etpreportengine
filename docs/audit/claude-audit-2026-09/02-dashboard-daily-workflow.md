# Audit 02 — Dashboard & Daily Workflow (14 Sep 2026)

Read-only code audit. DESK = src/Etp.Reporting.Desktop, SQL = src/Etp.Reporting.Infrastructure.SqlServer, MIG = database/migrations.

## Dashboard / Today Overview

Chain: `DashboardView` → `MainWindow.RefreshDashboardAsync` (`DESK/MainWindow.xaml.cs:291-304`) → `SqlServerDashboardQuery.LoadAsync` (Task.WhenAll of OperationalStatusRepository, DatabaseOperationalHealthRepository, OperationalAuditRepository; any failure blanks the whole dashboard).

| Feature | Status | Gap |
|---|---|---|
| SOURCE FILES / SOURCE ROWS | Working | Whole-database counts, unscoped by store/date |
| "% imported" | Partial | Divides completed **batches** by **files** (`DashboardView.cs:111-115`, `OperationalStatusRepository.cs:24-27`) — multi-file batches never reach 100%. Formats with CurrentCulture, parses with Invariant (`DashboardView.cs:435`) |
| System status | Reduced | Only severity word + backup age shown; UTC timestamp unlabelled |
| Health warnings, DB size, backup free space, failed imports, completed batches, latest import | Populated, never shown | `BuildDailyCloseCard/BuildReadinessCard/BuildGlanceCard/BuildSystemCard` (`DashboardView.cs:191,216,236,256`) have no callers — dead code; BACKUP_MISSING/BACKUP_SPACE_CRITICAL invisible (High) |
| Import history grid, rows-by-report chart, recent audit | Working | Only via task navigation; chart built from TOP 50 rows |
| Export management summary PDF | Thin | File/row counts per report code; date range in metadata does not filter data |
| Store filter / date selection | Missing | — |
| Refresh | Working | No in-flight/revision guard; overlapping refreshes can apply out of order (Medium) |

## Daily Workflow

Chain: `DailyWorkflowWorkspaceView` → `DailyWorkflowPresentationSession` → `SqlServerDailyWorkflowService` (role gate per call) → `DailyReportingWorkflowRepository`, `OperationalCompletionRepository`, `DailyReportingPackService`.

| Feature | Status | Gap |
|---|---|---|
| Business date | Working | Default yesterday; ctor fires Scope_Changed so initial hint immediately replaced |
| Store selection | Partial | Hard-coded WLMHW/HEMW in XAML (`:18`) and `DailyReportingPackService.cs:33-35`; no stores master |
| Readiness checklist | Working | `RequiredReports` hard-coded; status RECONCILED never written; **LoadAsync inserts a `daily_reporting_days` row on every read** (`DailyReportingWorkflowRepository.cs:43,173-179`), including Viewer refreshes |
| Manual inputs WALK_INS/OPENING_CASH/CASH_DEPOSIT/EXPENSES | Working | Reason required only at repo level → raw ArgumentException text in UI; stock-kind definitions duplicate stock counts in the ComboBox; text branch keyed on field code not value_kind |
| Stock counts | Working | Reason required only at repo |
| Staff targets | Write-only | `LoadStaffTargetsAsync` never called by Desktop — no grid, no feedback; exact period match required |
| Finalise day | Working w/ side effects | Generates pack before validating: every failed attempt adds `daily_report_generations` + event rows; after success InvalidatePack() so exported file is never the `is_final=1` generation |
| Reopen day | Partial | Requires Windows elevated-admin token in addition to Owner (`:412-416`; repo `:148`); non-elevated Owner gets "Owner permission is required." (High, misleading) |
| Generate pack (store/combined) | Working | Gated on CanView — Viewers create generation rows (Medium) |
| Export pack Excel/PDF | Working | User-chosen path only |

## Bugs
High: dead dashboard cards; % imported units; reopen elevation requirement. Medium: finalise side effects; side-effectful reads; pack gated on CanView; culture mismatch; UTC display; refresh race; `async void MainWindow_Loaded` only catches DB-availability failures → startup crash on other exceptions (`MainWindow.xaml.cs:143-151`). Low: async void handlers, fire-and-forget refreshes, audit failures swallowed while UI says "saved", RECONCILED unreachable.

## Tests
Desktop 21 tests (4 source-text), SQL 13 methods; **none execute any SQL, repository, trigger or migration** for these modules. `DailyWorkflowWorkspaceViewTests` hard-codes a 37 control count.
