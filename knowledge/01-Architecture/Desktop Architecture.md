---
type: architecture
status: implemented-with-external-gates
module: desktop
last_verified: 2026-08-29
---

# Desktop Architecture

## Decision and active baseline

ETP remains one .NET 10 WPF executable. `MainWindow` is the shell/workspace host; `DesktopCompositionRoot` constructs the active dependencies explicitly. Major workspaces own their presentation state and UI-facing orchestration while business rules, workbook parsing, SQL persistence and report rendering remain in lower layers.

This is the implemented structural direction from [[ADR-005 - Modular Desktop Shell]]. It is not a claim that the current combined suite, installed workflows, live SQL roles, UAT or release artifact have passed.

## Ownership

`WorkspaceModuleOwnershipRegistry` maps shell destinations and report routes to cohesive owners. Current workspaces cover:

- Dashboard, Daily Workflow and Manual Entry
- Imports and Source Inbox/OCR
- Reports and report export
- Archive/Distribution
- Registers and Accounting
- Operations/Investigation and Administration
- Settings and Help

`ShellNavigationService`, framework-neutral route/history state and the ownership registry drive navigation. Views call composed Application/SQL adapters; report formulas stay in Reporting/query services, Open XML parsing stays in Import and persistence stays in SQL infrastructure.

## Composition and dependencies

`DesktopCompositionRoot` owns interactive construction plus explicit database-initialization and one-shot automation entry points. The active product does not use Generic Host or a general DI container. Introducing one is optional future work, not an unmet architecture requirement by itself.

Normal database configuration is a validated Windows-integrated connection string. The local settings file stores connection configuration but no SQL password. Application/SQL role checks guard importing, administration, controlled restatement and daily workflow operations; live multi-user permission evidence remains external-blocked.

## Import and report boundaries

Desktop import uses `MatchedImportEnvelopeFactory` and passes the accepted exact-profile envelope to the application persistence use case. It does not reconstruct a profile/sheet pair after matching. See [[Import Architecture]] and [[ADR-007 - Exact Import Profile Identity and Provenance]].

Reports module owns preview state and export orchestration. Reporting/SQL services own deterministic queries and result models; Excel/PDF exporters do not become calculation engines.

## Diagnostics and startup

`App` owns startup mode and records privacy-safe startup/unhandled failures through local Desktop diagnostics. `MainWindow` records operational audit through its injected database lifecycle service. Runtime licensing is not active; ADR-006 remains accepted-deferred.

## Guardrails

- `MainWindow` remains a shell/workspace host.
- Navigation state remains free of WPF, Import, SQL Server and Reporting dependencies.
- Lower-layer projects never reference Desktop.
- Views do not calculate reports, parse workbooks or issue direct SQL.
- One composition root constructs dependencies; avoid mutable global service location.
- A new feature belongs to one workspace and reaches persistence/queries through an explicit boundary.
- Preserve formulas, mappings, SQL effects, diagnostics and exports when changing ownership.

Architecture/composition/ownership tests exist for these boundaries, but the current multi-agent integration still requires a clean combined test result. Launch, representative report export, import/database comparison, installed lifecycle and UAT are separate external gates.

## Status

- **Implemented in source:** modular shell/workspaces, explicit composition, import-envelope boundary, report/export ownership and local diagnostics.
- **Partial:** workflow-by-workflow installed/UAT coverage.
- **External-blocked:** final combined tests, live SQL roles, installer lifecycle, artifact binding and release promotion.
- **Deferred:** runtime licence enforcement and any optional AI-assistance UI.

For Desktop work, start here and [[ADR-005 - Modular Desktop Shell]], then inspect the registry, owning `Modules/` folder, Application contract, composed adapter and focused tests. Historical extraction evidence remains in `docs/DESKTOP_MODULAR_ARCHITECTURE_AUDIT.md`; closure status remains in `docs/PROJECT_CLOSURE_TRACEABILITY.md`.

Related: [[System Architecture]], [[Data Architecture]], [[Decision Register]].
