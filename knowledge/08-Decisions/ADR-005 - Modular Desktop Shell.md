---
type: adr
status: accepted
date: 2026-08-27
last_verified: 2026-08-27
---

# ADR-005 - Modular Desktop Shell

## Context

ETP has a useful multi-project backend, but its WPF `MainWindow` has accumulated shell, navigation, import, report, export, archive, accounting, administration and operational workflows. Partial classes make the code easier to browse without reducing shared state, direct infrastructure construction or change blast radius.

## Decision

Keep one WPF executable and make `MainWindow` a thin shell/workspace host. Give each major Desktop workspace a cohesive View/ViewModel boundary, invoke non-Desktop behaviour through application use-case contracts, and keep navigation state free of WPF and feature-layer dependencies.

Perform the migration incrementally. Extract navigation and shell state first, then one bounded workspace at a time. Preserve formulas, mappings, database effects, import diagnostics, exports and user-facing behaviour throughout.

## Reason

Responsibility-based modules reduce coupling and make future report, import and presentation changes reach their natural owners instead of expanding `MainWindow`. Small reversible phases keep a working reporting system safe.

## Alternatives considered

- Keep adding MainWindow partial files: rejected because all partials remain one class with shared state and fan-out.
- Rewrite the WPF application or adopt a new UI framework: rejected because it creates unnecessary behavioural and delivery risk.
- Move everything into one ShellViewModel: rejected because it would replace one God object with another.
- Extract all workspaces in one change: rejected because import, reporting and database regressions would be difficult to isolate.

## Consequences

Desktop gains explicit shell, navigation, workspace and desktop-service boundaries. Application contracts must become the normal route to use cases. Temporary adapter wiring is acceptable during migration, but old handlers are removed only after focused tests and workflow comparisons pass.

Architecture tests prevent lower layers from referencing Desktop and keep the Desktop navigation folder free of WPF, Import, SQL Server and Reporting references. Graphify is refreshed after meaningful structural phases to measure dependency fan-out.

## Affected components

`src/Etp.Reporting.Desktop`, `src/Etp.Reporting.Application`, focused tests, `docs/DESKTOP_MODULAR_ARCHITECTURE_AUDIT.md` and [[Desktop Architecture]].
