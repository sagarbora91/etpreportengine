# ETP Reporting Engine production UI revamp

Date: 31 August 2026

## Outcome

The production .NET 10/WPF desktop application now implements the task-first interaction model approved in the UI/workflow prototype. Existing report calculations, imports, SQL boundaries, permissions, audit behavior and export contracts were preserved.

## Implemented across the application

- Teal and deep-navy design system matching the approved prototype.
- Compact global rail and a reduced-width contextual workflow sidebar.
- Contextual navigation is persistent at wide sizes and closed behind a menu at widths below 1100 DIPs.
- Store, business date and signed-in role remain visible in the shell.
- The application opens on the Today dashboard after Windows identity verification.
- The Today dashboard exposes daily-close readiness, the next action, source activity, system health, recent imports and report activity.
- Buttons, cards, form fields, progress indicators, grids, focus states and status surfaces now share one production theme.
- Primary, secondary and destructive actions have distinct hierarchy across Daily Workflow, Imports, Source Inbox, Reports, Registers, Accounting, Archive, Operations, Approvals, Administration and Settings.
- Report loading retains the previous safe context instead of replacing the workspace with an empty loading surface.
- Report tables now provide an explicit **View selected details** action in addition to double-click, preserving the existing source-lineage drawer.
- Permission denials now open a clear access-restricted drawer containing the authoritative reason.
- Empty, loading, error and status components continue to preserve accessibility names and live-region behavior.
- Daily Sales Report and report-summary presentation surfaces use the same production card language and palette.

## Coverage evidence

The WPF renderer completed:

- 11 baseline desktop views;
- all 14 registered workspace destinations;
- all 29 registered report routes;
- 279 automation-named elements;
- 1366×768 and 960×600 responsive render checks.

The complete render inventory is in `docs/audit/ui-production-revamp/`.

Representative evidence:

- `docs/audit/ui-production-revamp/all-workspace-routes/destination-dashboard.png`
- `docs/audit/ui-production-revamp/03-reports-1366x768.png`
- `docs/audit/ui-production-revamp/04-reports-960x600.png`
- `docs/audit/ui-production-revamp/07-manual-entry-1366x768.png`
- `docs/audit/ui-production-revamp/08-dsr-screen-1366x768.png`

## Safety and remaining external gates

This revamp changes presentation and navigation behavior only. It does not change formulas, source mappings, SQL effects, import orchestration, report generation or permission enforcement. Connected-SQL role validation, installed lifecycle checks and human UAT remain external acceptance gates as documented in the project closure traceability.
