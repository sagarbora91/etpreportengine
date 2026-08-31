# ETP Reporting Engine UI and workflow audit

Date: 31 August 2026
Scope: .NET 10/WPF desktop product, all authoritative navigation destinations, all 29 production reports, role behavior, shared UI states and full function-first acceptance inventory.

## Executive verdict

The application is functionally complete enough for an acceptance candidate, but its current interaction model makes users navigate the product's structure instead of completing their work. The most important improvement is not cosmetic: daily work, reporting readiness and exceptions must become the primary organizing model.

The redesigned HTML prototype therefore uses:

- a compact global task rail;
- a searchable contextual workflow list;
- a task-first Today workspace;
- one obvious primary action on each screen;
- persistent store, business-date and role context;
- non-blocking loading and explicit empty/error/locked states;
- visible detail and lineage actions on table rows;
- policy-safe locked previews for deferred functions.

## Evidence and method

The audit combines:

- `WorkspaceModuleOwnershipRegistry` and `UiNavigationRegistry`;
- all active WPF workspace XAML files;
- the 14 workspace routes and 29 report routes exercised by the UI smoke suite;
- 11 baseline UI screenshots plus the all-route screenshot inventory;
- the function-first audit: 244 active functions, 29 reports and 7 deferred functions;
- the external acceptance workspace: 85 role-based scenarios;
- the current v4 design-system, navigation and implementation contracts.

The active XAML surface contains 17 resource/view files with 696 lines, 107 buttons, 78 text inputs, 27 selectors, 24 data grids and only two explicit scroll viewers. Those numbers do not prove poor usability by themselves, but they explain why hierarchy and progressive disclosure are essential.

## Findings

| Priority | Finding | Evidence | Consequence | Prototype response |
|---|---|---|---|---|
| P1 | Daily work is hidden behind module selection. | Module Home asks the user to choose a module before showing readiness or the next action. | Store managers must translate a business goal into product architecture. | Today opens directly with source completeness, missing inputs, controls and the next action. |
| P1 | Navigation duplicates destinations. | Global rail, module home, contextual sidebars, report favourites and catalogue entries can reach the same destination. | Users must remember which navigation layer owns a task. | One global task rail and one searchable contextual list; duplicate favourites become shortcuts to the same screen. |
| P1 | Equal-weight action rows obscure the primary action. | The current DSR surface presents refresh, PDF, Excel, pack, folder and manual entry as adjacent peers. | Users must re-evaluate the action hierarchy on each visit. | Run/continue is primary; export and secondary tools remain grouped but visually subordinate. |
| P1 | Dense forms expose too much at once. | Manual Entry combines status, multiple input types and actions in one long surface. | Increased scanning, scrolling and entry risk at 960–1366 px. | Forms are grouped by task with an audit preview and one save action. |
| P1 | Loading can take over the report workspace. | Current DSR evidence shows a large empty loading surface and repeated loading text. | The interface feels stalled and loses useful context. | Filters and prior context remain visible; progress is local and non-blocking. |
| P2 | The contextual sidebar competes with the workspace. | At 1366 px the 300-DIP sidebar consumes a large part of the report and form surface; at 960 px it overlays content. | Reduced table width and horizontal/vertical scanning pressure. | The global rail remains compact; contextual navigation collapses below the desktop breakpoint. |
| P2 | Role and locked-day consequences are passive. | The current header names the role, but consequences are mostly discovered after navigation or action. | Permission failures can feel arbitrary. | Role can be simulated; restricted screens explain the required role, and locked states explain the approved recovery path. |
| P2 | Table drill-down is not discoverable. | Existing behavior relies on row double-click in report grids. | Users may never discover source details and lineage. | Every row has an explicit Details action opening a contextual drawer. |
| P2 | Missing, empty and zero need stronger visual separation. | Business contracts correctly distinguish them, but generic empty surfaces can weaken that distinction. | Users may infer that absent data is zero. | Empty-state copy explicitly says nothing has been replaced with zero. |
| P2 | Search behavior changes by location. | Ctrl+F can focus module search or open global investigation depending on context. | Keyboard behavior requires memorized context. | One global screen search is always available; investigation remains a named task. |
| P3 | Status patterns lack a common rhythm. | Workspace-specific loading, errors and blank states vary. | The product feels less coherent even when functions are correct. | Shared Ready, Loading, Empty, Error and Locked treatments are available on every prototype screen. |

## Workflow assessment

| Workflow | Current friction | Redesigned starting point | Target outcome |
|---|---|---|---|
| Daily close | Split across Dashboard, Manual Entry, Daily Workflow and Reports. | Today overview | User always sees the next incomplete step and why it matters. |
| Report review | Large catalogue plus shared action row; drill-down is implicit. | Reports overview and focused report surface | Filters, KPIs, details, lineage and export stay in one context. |
| Import | Intake, quality, source documents and register tasks share a broad workspace. | Import overview with ordered workflow groups | File state and control outcomes remain visible from selection to commit. |
| Accounting | Preparation, mapping, validation and export are related but visually separate. | Four-step accounting workflow | Users cannot export before mapping and balance controls pass. |
| Exceptions | Issues are divided by technical source and report family. | Open Items | Priority, owner, business impact and recovery action lead the presentation. |
| Archive and sharing | Generations, restatements, re-export and sharing require historical context. | Archive overview | Actions stay bound to the selected immutable generation. |
| Administration | Many settings and operational actions are owner-only. | Settings and System Health | Impact preview and validation precede every governed change. |

## Proposed information architecture

```text
Today
  Daily close · readiness · performance · controls
Reports
  Overview · Sales · Stock · Staff · Tender/Cash · Service · Exceptions · Management · Investigation · Packs
Imports
  Intake · Quality · Documents/OCR · Digital registers
Accounting
  Prepare · Map · Validate · Export · History · Reconcile
Archive
  Generations · Packs · Restatements · Compare · Re-export · Shared reports · Sources
Exceptions
  Open items · Data quality · Source/import/tender/stock/staff/OCR/accounting · Approvals
Settings
  Access · Stores · Masters · Profiles · Rules · Integrations · Backup · Scheduler · Health · Audit
```

This is a presentation reorganization only. It does not change canonical facts, signed returns, business-date rules, duplicate/restatement logic, report formulas, permissions, lineage or audit history.

## Prototype coverage

- 244 active functions mapped to a valid prototype destination.
- 115 clickable screens and state presentations.
- 29 production reports populated with consistent dummy data.
- 7 deferred functions shown as locked with their approved reasons.
- Viewer, Store Manager and Owner role simulation.
- Ready, Loading, Empty, Error and Locked state simulation.
- Responsive layouts verified at 1366×768 and 960×600.

## Implementation guidance for WPF

1. Introduce the Today workspace and next-action model without changing lower-layer contracts.
2. Normalize the report command hierarchy and add explicit Details actions to grids.
3. Replace full-workspace loading with shared non-blocking state controls.
4. Split Manual Entry and other dense forms into task sections with an audit preview.
5. Make contextual navigation responsive while preserving keyboard routes.
6. Port the shared role, unavailable, empty, error and locked-state language.
7. Run the same 85 UAT scenarios against the updated WPF candidate and compare completion time, clicks, errors and recovery success.

## Acceptance targets

- 100% of active functions remain reachable for an authorised role.
- All seven deferred functions remain visibly unavailable with an accurate reason.
- Daily-close status and next action are understandable without module knowledge.
- No primary workflow requires an undiscoverable double-click.
- Keyboard-only operation completes the daily close and representative report workflows.
- At 960×600, the primary action and current task remain visible without horizontal page scrolling.
- Missing values are never presented as zero.
- Loading, error and permission states preserve the user's store, date and filter context.

## Prototype evidence

- `docs/audit/ui-workflow-audit/screenshots/01-task-first-dashboard-1366x768.png`
- `docs/audit/ui-workflow-audit/screenshots/02-report-with-lineage-drawer-1366x768.png`
- `docs/audit/ui-workflow-audit/screenshots/03-function-coverage-1366x768.png`
- `docs/audit/ui-workflow-audit/screenshots/04-dashboard-960x600.png`
