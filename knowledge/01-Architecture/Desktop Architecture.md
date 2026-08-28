---
type: architecture
status: accepted-target
module: desktop
last_verified: 2026-08-28
---

# Desktop Architecture

## Decision

ETP remains one WPF executable. The Desktop layer is organized as a thin shell plus independently owned workspaces: `MainWindow` hosts navigation, global status and workspace controls without owning import, reporting, SQL, archive, accounting or administration implementations. The structural target is implemented in the current working tree; final combined-suite, Graphify refresh, artifact and commit-bound closure evidence remain separate release gates.

The migration is incremental and preserves user-facing behaviour, formulas, mappings, database structures and exports. See [[ADR-005 - Modular Desktop Shell]].

## Current baseline

The 2026-08-27 baseline had five `MainWindow` C# partials, 2,584 physical lines, about 188 methods, 94 event-handler-shaped methods, 40 fields and direct SQL/import/report construction. That remains historical baseline evidence in `docs/DESKTOP_MODULAR_ARCHITECTURE_AUDIT.md`, not the current architecture.

The current shell has three `MainWindow` C# partials totaling 907 lines. Automated ratchets cap it at 29 fields and 62 methods, require compact XAML feature hosts, keep the old Productisation/VisualReporting partials deleted, and forbid report formulas, workbook parsing, export orchestration and SQL infrastructure in the shell.

## Implemented modular architecture

- `DesktopCompositionRoot` owns normal interactive construction plus database-initialization and one-shot-automation infrastructure. It composes every extracted workspace and its Application/SQL adapters without a new DI framework.
- `ShellNavigationService`, `ShellViewModel`, route metadata, history and `WorkspaceModuleOwnershipRegistry` own feature-neutral navigation. Bidirectional tests cover every shell destination and production report route.
- Cohesive modules now own Dashboard, Help, Settings, Reports, Daily Workflow/Manual Entry, Imports, Source Inbox/OCR, Archive/Distribution, Registers, Accounting, Operations/Investigation and Administration presentation state and controls.
- Application contracts are the normal boundary for access, dashboard, reports, archive, daily workflow, source inbox, import persistence, registers, accounting, distribution, operations, administration and database lifecycle work.
- SQL adapters validate Windows-integrated connections. Write boundaries enforce Store Manager/Owner permissions, including Owner-only controlled restatement, mapping approval and daily reopen.
- Report/export orchestration and visual rendering are owned by the Reports module; workbook parsing remains in Import and report formulas remain in Reporting/SQL query services.
- Architecture guardrails enforce zero direct MainWindow infrastructure construction, lower-layer independence, module isolation, compact hosts, deleted legacy partials and shell size/responsibility ceilings.

Focused architecture/composition/ownership/UI-smoke tests pass in the current working tree. This is implementation evidence, not a release-completion claim: live SQL role/UAT, installed workflows, final combined tests, Graphify refresh and commit/artifact binding remain governed by the closure ledger.

## Target ownership

```text
ETP executable
  → App composition root
  → Desktop shell and dependency-neutral route/navigation state
  → independently owned Desktop workspaces
  → application use-case contracts
  → Import / Reporting / Domain
  → SQL Server infrastructure implementations
```

Workspaces include Dashboard, Help, Settings, Reports, Daily Workflow, Imports, Source Inbox, Archive, Registers, Accounting, Operations/Investigation and Administration. A workspace owns its view, presentation state and UI-facing orchestration. Business rules and persistence remain in the appropriate non-Desktop layer.

## Guardrails

- `MainWindow` is a shell/workspace host only.
- Views contain presentation concerns only.
- View models do not calculate reports, parse imports or access SQL.
- `Shell/Navigation` has no WPF, Import, SQL Server or Reporting assembly dependency; UI navigation adapters may translate its decisions into Desktop behavior.
- Lower-layer projects never reference Desktop.
- Desktop-specific dialogs and notifications remain Desktop services.
- One composition root constructs dependencies; do not introduce mutable global service location.
- Assign each new feature to one cohesive module and verify build, tests, launch and affected workflows.
- Compare representative report exports and import/database effects before removing old paths.

## Closure routing

For Desktop architecture work, start with this note and [[ADR-005 - Modular Desktop Shell]], then inspect `WorkspaceModuleOwnershipRegistry`, the owning `Modules/` folder, its Application contract, composed SQL adapter and focused tests. Treat `docs/DESKTOP_MODULAR_ARCHITECTURE_AUDIT.md` as the historical baseline/phase audit; use `docs/PROJECT_CLOSURE_TRACEABILITY.md` for current requirement status.

Related: [[System Architecture]], [[Data Architecture]], [[Decision Register]].
