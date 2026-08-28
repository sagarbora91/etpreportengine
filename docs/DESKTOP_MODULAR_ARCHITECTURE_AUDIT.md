# Desktop Modular Architecture Audit

## Executive assessment

**Risk: HIGH.** The application already has useful project boundaries and several focused Desktop controls, but `MainWindow` remains the composition root, navigation controller, workspace host, presentation-state store and application workflow coordinator. Splitting the class across partial files has improved browsing without establishing independent responsibility ownership.

The required response is an incremental behaviour-preserving extraction, not a rewrite. Import rules, report calculations, database structures and user-facing behaviour remain unchanged.

## Phase 0 baseline

Measured on 2026-08-27 before modular extraction:

| Measure | Baseline |
|---|---:|
| Branch | `ui/uiux-v4-touch-first-redesign` |
| Commit | `9abab67cf8eca8c7cf4c7252b431abc632ac6456` |
| .NET SDK | `10.0.400` |
| Solution build | Passed; 0 warnings, 0 errors |
| Tests | 188 passed; 0 failed; 0 skipped |
| Domain tests | 12 passed |
| Desktop tests | 41 passed |
| Reporting tests | 51 passed |
| Import tests | 40 passed |
| SQL Server tests | 44 passed |

The working tree already contained unrelated and knowledge/Graphify changes. They are part of the baseline work state and must not be attributed to the modular refactor.

The automated baseline does not prove that WPF launched against a live SQL Server, that a representative production workbook imported, or that generated Excel/PDF bytes match a golden master. Those checks are required before high-risk module extraction.

## Current project dependencies

```text
Domain                         → (none)
Application                    → (none)
Reporting                      → (none)
Import                         → Domain
Infrastructure.SqlServer       → Import, Domain, Reporting
Desktop                        → Import, Infrastructure.SqlServer, Reporting
```

`Desktop` does not consume `Application`; no production project currently consumes `Application`. Consequently the intended use-case layer exists in the solution but is bypassed by Desktop workflow code. `MainWindow` directly constructs concrete repositories, import readers/coordinators, reporting executors and exporters.

## Current Desktop structure

```text
Etp.Reporting.Desktop/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml
├── MainWindow.xaml.cs
├── MainWindow.Shell.cs
├── MainWindow.Workspaces.cs
├── MainWindow.VisualReporting.cs
├── MainWindow.Productisation.cs
├── ShellNavigation.cs
├── UiNavigation.cs
├── ReportWorkspaceControls.cs
├── DailySalesReportControls.cs
├── HelpCentre.cs / HelpCentreControls.cs
├── UiControls.cs / DensitySelector.cs
├── PowerShellOperationsService.cs
├── Themes/
└── Assets/
```

There are no established `Views/` or `ViewModels/` module folders and no shell view model. Useful extraction seams already exist in `WorkspaceNavigationHistory`, `ShellShortcutRegistry`, `UiNavigationRegistry`, report workspace controls, the DSR workspace and Help Centre.

## MainWindow measurements

| File | Physical lines | Declared methods | Event-handler-shaped methods |
|---|---:|---:|---:|
| `MainWindow.xaml.cs` | 1,446 | 95 | 56 |
| `MainWindow.Shell.cs` | 436 | 39 | 12 |
| `MainWindow.Productisation.cs` | 349 | 35 | 26 |
| `MainWindow.Workspaces.cs` | 263 | 14 | 0 |
| `MainWindow.VisualReporting.cs` | 90 | 5 | 0 |
| **Total** | **2,584** | **188** | **94** |

The partial class also holds 40 fields. Its public constructor takes no dependencies, yet its methods instantiate more than 20 concrete service/repository/export types. `MainWindow.xaml` has 236 physical lines, 223 named controls and 122 event bindings; compact XAML formatting makes physical line count particularly misleading.

## Responsibility matrix

| Responsibility | Current owner/evidence | Correct owner | Priority | Risk |
|---|---|---|---|---|
| Window startup, chrome, global status and workspace host | `App`, `MainWindow`, `MainWindow.Shell` | Desktop shell | High | Low |
| Module registry, history, shortcuts, destination selection | `UiNavigation`, `ShellNavigation`, `MainWindow.Shell` | Framework-neutral Desktop navigation | Critical | Low |
| Panel visibility and active workspace state | `MainWindow.Navigate_Click`, shell/workspace partials | Shell view model/coordinator and workspace host | Critical | Medium |
| Access checks and current Windows role | `MainWindow` via `Phase2OperationsRepository` | Application access service; shell consumes presentation state | High | Medium |
| Daily workflow, manual inputs, counts, targets, finalise/reopen | `MainWindow.xaml.cs` | Daily-workflow view model plus application workflow | High | High |
| Workbook validation, staging, batch import, retry/cancel/restatement | `MainWindow.xaml.cs` | Import view model plus Import/Application contracts | Critical | High |
| Source inbox and extraction review | `MainWindow.Productisation.cs` | Source-inbox workspace plus application service | High | High |
| Report selection and execution | MainWindow handlers plus focused report controls | Report workspace view models plus reporting application service | Critical | High |
| Visual report rendering | `MainWindow.VisualReporting.cs` | Reusable Desktop report view/control | Medium | Medium |
| Excel, PDF and report-pack export orchestration | `MainWindow.xaml.cs` and workspace callbacks | Report application service; Desktop owns dialogs only | High | High |
| Dashboard and operational status | `MainWindow.xaml.cs` | Dashboard workspace/view model | High | Medium |
| Archive, comparison, packaging and sharing | MainWindow base/productisation partials | Archive workspace plus application service | High | Medium |
| Accounting preparation and Tally export | `MainWindow.Productisation.cs` | Accounting workspace plus application service | High | High |
| Automation, backup, recovery and support packages | `MainWindow.xaml.cs`, `PowerShellOperationsService` | Operations workspace and explicit operational contracts | High | High |
| Masters, users, approvals, registers and product settings | MainWindow base/productisation partials | Focused administration workspaces | Medium | Medium |
| File and message dialogs | MainWindow handlers | Desktop dialog services where reuse/testing warrants it | Medium | Low |
| Connection-string and UI preference persistence | MainWindow and `UiPreferenceStore` | Settings service and module/shell state | Medium | Medium |

No raw SQL statements or `SqlCommand` use were found in Desktop. The violation is direct construction and invocation of SQL infrastructure repositories from UI code, often using a connection string stored in a text control.

## Critical workflows to preserve

- normal startup, database-initialization startup and one-shot automation startup;
- Windows identity/role loading, authorization and database health;
- connection testing, database bootstrap and settings persistence;
- file/folder/ZIP selection, preflight, validation, staging, persistence, restatement, retry and cancellation;
- source-document intake, integrity verification and extraction review;
- daily inputs, stock counts, staff targets, readiness, finalisation and reopening;
- catalogue reports, DSR, invoice, tender, stock, staff, service and exception reports;
- focused preview, visual summaries, Excel/PDF and report-pack export;
- dashboard, operational controls, backup/recovery and support packages;
- historical date ranges, immutable archive, comparison, re-export, ZIP and sharing;
- accounting preparation, approval and Tally XML export;
- masters, access administration, approvals and registers.

## Target Desktop structure

This target evolves the seams already present; it is not a requirement to create every folder immediately.

```text
Etp.Reporting.Desktop/
├── App.xaml / App.xaml.cs             # composition root and process-level failures
├── Shell/
│   ├── MainWindow.xaml / .cs          # window host only
│   └── ShellViewModel.cs               # global presentation state only
│   ├── Navigation/                     # route state; no WPF, Import, SQL or Reporting references
│   │   ├── NavigationService.cs
│   ├── WorkspaceLocation.cs
│   └── NavigationRegistry.cs
├── Modules/
│   ├── Help/
│   ├── Settings/
│   ├── Archive/
│   ├── Reports/
│   ├── Dashboard/
│   ├── DailyWorkflow/
│   ├── Imports/
│   ├── Accounting/
│   └── Administration/
│       # each module owns cohesive Views and ViewModels
├── Services/
│   ├── Dialogs/
│   ├── Notifications/
│   └── DesktopIntegration/
├── Controls/
├── Converters/
├── Themes/
└── Assets/
```

Use cases and database/report/import behaviour belong outside Desktop. A view model may coordinate a UI-facing action through an application contract, but it must not become a replacement God object or a repository layer.

## Incremental migration and risk

| Sequence | Extraction | Behavioural risk | Data/reporting risk | Rollback |
|---:|---|---|---|---|
| 1 | Navigation state and destination selection; reuse history/registry | Low | None | Restore shell calls to current methods |
| 2 | Shell global state, Help and module-home presentation | Low | None | Reconnect existing controls/code-behind |
| 3 | Settings and desktop dialog/persistence services | Medium | Connection configuration | Restore existing settings handlers |
| 4 | Archive, sharing and registers | Medium | Immutable-generation selection | Reconnect existing repository-backed handlers |
| 5 | Reports workspace orchestration and export | High | Financial output and export stability | Keep old handlers until golden comparison passes |
| 6 | Dashboard and daily workflow | High | Finalisation and controlled inputs | Retain old route behind a small reversible seam |
| 7 | Import and Source Inbox | High | Canonical facts, lineage and restatement | Extract one operation at a time; compare database effects |
| 8 | Accounting, operations, approvals and administration | High | Audit, backup and accounting controls | Separate commits per workspace and revert individually |
| 9 | Remove obsolete partial methods/fields and enforce boundaries | Medium | Indirect regression only | Delete only after references and smoke tests are clean |

After every step: build, run relevant tests, launch the application, exercise the affected workflow and compare representative outputs/effects with the baseline. Do not combine visual redesign, formula change, mapping change or schema change with structural extraction.

## Expected file changes

Likely creations include shell/navigation classes, module views/view models, Desktop service abstractions, application use-case contracts and architecture tests. Likely modifications include `App.xaml.cs` as composition root, `MainWindow` host wiring, Desktop project references and focused tests. Existing `MainWindow.*` partial files become removable only after their responsibilities have moved and source/Graphify references are clean. No planning phase should delete them.

## Guardrails

- **DESK-001:** `MainWindow` is the application shell and workspace host only.
- **DESK-002:** Views contain presentation and control interaction only.
- **DESK-003:** View models expose module presentation state and invoke application abstractions; they contain no report formulas or data access.
- **DESK-004:** Desktop does not execute SQL or construct SQL repositories inside views/view models.
- **DESK-005:** Reporting formulas and deterministic export models remain outside Desktop.
- **DESK-006:** Workbook parsing, profile recognition, staging and normalization remain outside Desktop.
- **DESK-007:** Every major workspace has an explicit owner and may be decomposed by cohesive sub-feature.
- **DESK-008:** New features are assigned to an owning module before code is added.
- **DESK-009:** Shell route/navigation state has no WPF, Import, SQL Server or Reporting assembly dependency. Route metadata and access policy may use stable feature identifiers without calling feature implementations.
- **DESK-010:** Domain, Application, Import, Reporting and SQL infrastructure never reference Desktop.
- **DESK-011:** One composition root constructs application dependencies; mutable global service location is prohibited.
- **DESK-012:** Every extraction preserves import/database/report behaviour and is independently reversible.

`DesktopArchitectureTests` enforces DESK-009 and DESK-010 without introducing an architecture-test framework. Graphify should be updated after structural phases and used to confirm that MainWindow responsibility and fan-out decrease.

## Implemented checkpoint — 2026-08-27

The first low-risk slice and one representative data-backed workspace are now implemented:

1. `ShellNavigationService` owns route selection, metadata, access decisions and back/forward history without WPF or feature-layer dependencies.
2. `HelpWorkspaceSession` owns Help Centre open/close and return-state behavior.
3. `DashboardView` owns Dashboard controls, accessibility labels, formatting and chart rendering.
4. `IDashboardQuery` defines the Application-layer read contract; `SqlServerDashboardQuery` adapts the existing SQL repositories without changing their queries or mappings.
5. Focused navigation, Help, Dashboard presentation, Dashboard contract, SQL adapter and architecture tests protect these boundaries.

Measured after the slice, the five `MainWindow` C# partials reduced from 2,584 to 2,521 physical lines and `MainWindow.xaml` reduced from 236 to 222 lines. The application remains a single WPF executable. The implementation intentionally preserves panel visibility, permissions, refresh, audit and PDF behavior; the automated suite, UI shell smoke run and representative DSR PDF comparison passed. Live connected-SQL workflow validation remains a separate operator acceptance step. Dashboard dependency construction also remains temporarily in `MainWindow`, so DESK-011 is not yet complete.

This checkpoint does not declare the wider modular migration complete. Reports, Daily Workflow, Import, Archive, Settings, Accounting, Operations and Administration remain in `MainWindow` and must continue as separate reversible slices. Report formulas, mappings, schemas and import behavior were deliberately not changed in this checkpoint.

## Architecture ratchet baseline — 2026-08-28

The Desktop test suite now treats direct construction of concrete SQL-infrastructure collaborators as a ratchet. Views and view models have a zero-tolerance boundary: they may not reference `Etp.Reporting.Infrastructure.SqlServer` or `Microsoft.Data.SqlClient`, and Desktop modules may not reference another module directly. The framework-neutral navigation folder also may not reference a Desktop module.

`MainWindow` still has 83 temporary direct constructions of SQL-infrastructure concrete classes: 56 in `MainWindow.xaml.cs` and 27 in `MainWindow.Productisation.cs`. The executable inventory is recorded by type and file in `DesktopCompositionGuardrailTests`. Existing counts may decrease during extraction, but a new type/file pair or an increase above any recorded maximum fails the suite. This is a containment checkpoint, not compliance with DESK-004 or DESK-011; those guardrails remain incomplete until the inventory reaches zero and composition is owned outside the shell.
