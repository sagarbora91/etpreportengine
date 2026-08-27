---
type: architecture
status: accepted-target
module: desktop
last_verified: 2026-08-27
---

# Desktop Architecture

## Decision

ETP remains one WPF executable, but its Desktop layer is organized as a thin shell plus independently owned workspaces. `MainWindow` hosts navigation, global status and the active workspace; it does not own import, reporting, SQL, archive, accounting or administration workflows.

The migration is incremental and preserves user-facing behaviour, formulas, mappings, database structures and exports. See [[ADR-005 - Modular Desktop Shell]].

## Current baseline

At the 2026-08-27 audit, the five `MainWindow` C# partials contained 2,584 physical lines, approximately 188 methods, 94 event-handler-shaped methods and 40 fields. Desktop directly consumed Import, SQL infrastructure and Reporting, while the Application project was not on the production dependency path. The full evidence and migration risks are recorded in `docs/DESKTOP_MODULAR_ARCHITECTURE_AUDIT.md`.

Existing useful seams include the navigation history/registry, report workspace controls, DSR workspace and Help Centre. Partial files alone are not module boundaries because their state and service construction still belong to one `MainWindow` object.

## Implemented migration slice

The first controlled extraction slice was completed on 2026-08-27:

- framework-neutral route metadata, access decisions and back/forward history now belong to `ShellNavigationService`;
- Help open/close/return state now belongs to `HelpWorkspaceSession`;
- Dashboard presentation and chart rendering now belong to `DashboardView` and `DashboardViewState`;
- the Dashboard read operation is exposed by the dependency-free Application contract `IDashboardQuery` and implemented by `SqlServerDashboardQuery`;
- architecture tests enforce lower-layer independence from Desktop and keep the navigation area free of WPF, import, reporting and SQL dependencies.

After this slice, the five `MainWindow` C# partials contain 2,319 physical lines, approximately 176 methods and 32 fields; `MainWindow.xaml` contains 222 lines. This is a measurable reduction, not completion of the full migration. Import, Reports, Daily Workflow, Archive, Settings, Accounting, Operations and Administration still require incremental extraction with workflow-specific verification.

## Target ownership

```text
ETP executable
  → App composition root
  → Desktop shell and framework-neutral navigation
  → independently owned Desktop workspaces
  → application use-case contracts
  → Import / Reporting / Domain
  → SQL Server infrastructure implementations
```

Workspaces include Dashboard, Daily Workflow, Imports, Reports, Archive, Accounting, Operations and Administration. A workspace owns its view, presentation state and UI-facing orchestration. Business rules and persistence remain in the appropriate non-Desktop layer.

## Guardrails

- `MainWindow` is a shell/workspace host only.
- Views contain presentation concerns only.
- View models do not calculate reports, parse imports or access SQL.
- Navigation has no WPF, Import, SQL Server or Reporting dependency.
- Lower-layer projects never reference Desktop.
- Desktop-specific dialogs and notifications remain Desktop services.
- One composition root constructs dependencies; do not introduce mutable global service location.
- Extract one cohesive module at a time and verify build, tests, launch and affected workflows.
- Compare representative report exports and import/database effects before removing old paths.

## Migration order

Navigation and shell state come first, followed by Help/module home and Settings. Archive/register boundaries provide lower-risk module patterns before Reports, Daily Workflow and Import. Accounting, operations and administration follow once shared contracts are stable. Import and financial report extraction require the strongest characterization and golden-master evidence.

Related: [[System Architecture]], [[Data Architecture]], [[Decision Register]].
