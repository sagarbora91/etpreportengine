# UI redesign sprint ledger

Checkpoint date: 12 September 2026.

**Status: PLANNED. Application implementation has not started.**

This is the initial ledger authored by the planning/handoff session. A separate `docs/audit/ETP-UI-REDESIGN-ACCEPTANCE.md` appeared concurrently with preliminary Phase 1 information. Reconcile current files and any running implementation session before using these initial statuses; never reset newer progress to NOT STARTED.

Governing contract: [four-phase plan](ETP-UI-REDESIGN-FOUR-PHASE-SPRINT-2026-09-12.md).
Resume context: [session handoff](../audit/ETP-SESSION-HANDOFF-2026-09-12-UI-REDESIGN.md).
Frozen choices: [decisions](ETP-UI-REDESIGN-DECISIONS.md).

## Exact next action

On the next implementation session, begin P1-01: read repository instructions and relevant knowledge, inspect actual HEAD/status, preserve unrelated work and use the required graphs to identify current owners. Then perform P1-02 and create the route/action CSV from actual source. Continue the remaining phases without routine user-review pauses.

## Session checkpoint

- Starting HEAD for handoff: 491f5ba; branch: ui/uiux-v4-touch-first-redesign. Recheck both at resume.
- Completed: governing plan, reference image, decision register, handoff and this initial ledger.
- No application code, build, new installer, VM action or acceptance test was performed in this planning/handoff session.
- No phase gate has passed. The historical 617-test result belongs to the prior 1.8.7 candidate.
- Historical VM command access is not an active authenticated session; actual UI access remains to be checked.
- Final route inventory, acceptance report and screenshot evidence will be created during implementation; none is fabricated here.

## Gates

| Gate | Status | Evidence |
|---|---|---|
| G1 — Inventory and design baseline | PENDING | Not executed |
| G2 — Shared foundation and representative journeys | PENDING | Not executed |
| G3 — Complete application conversion | PENDING | Not executed |
| G4 — Final acceptance and delivery | PENDING | Not executed |

## Task register

Deliverables and gate definitions remain in the governing plan. Update status and evidence here as work proceeds.

| Task | Work | Status | Evidence / next action |
|---|---|---|---|
| P1-01 | Read repository instructions, relevant knowledge/ADRs and source; use Graphify and the precise code graph for ownership/impact | NOT STARTED | — |
| P1-02 | Inventory every active navigation entry, report, workspace, toolbar action, dialog, Help/Profile/Settings surface and role variation | NOT STARTED | — |
| P1-03 | Record baseline layouts at available desktop/VM sizes and density settings | NOT STARTED | — |
| P1-04 | Assign canonical module/category/task paths and exact route IDs | NOT STARTED | — |
| P1-05 | Choose one shared pattern for each screen and each state | NOT STARTED | — |
| P1-06 | Inventory source-level permissions, service guards and unsaved/locked/context behaviour | NOT STARTED | — |
| P1-07 | Reconcile business dates, scopes and report/import overrides across workflows | NOT STARTED | — |
| P1-08 | Define the search index and aliases against the canonical route map | NOT STARTED | — |
| P1-09 | Check build, tests, fonts/theme, VM connectivity and available screenshot/input automation | NOT STARTED | — |
| P1-10 | Establish protected original-data checks and disposable fixture environments | NOT STARTED | — |
| P1-11 | Freeze a layout sketch/component specification for Overview, Settings, Import Files and DSR | NOT STARTED | — |
| P1-12 | Populate the sprint ledger and record the significant navigation architecture decision | NOT STARTED | — |
| P2-01 | Extend routing to identify exact tasks, preserving old destinations through compatibility mapping | NOT STARTED | — |
| P2-02 | Use a shared descriptor for menu/tile/search/breadcrumb destination metadata | NOT STARTED | — |
| P2-03 | Implement the persistent shell with compact navigation and responsive header | NOT STARTED | — |
| P2-04 | Implement category navigation and stateful history | NOT STARTED | — |
| P2-05 | Build shared overview groups, task tiles, metric tiles and status components | NOT STARTED | — |
| P2-06 | Build shared task headers/action bars, focused forms, tables and detail surfaces | NOT STARTED | — |
| P2-07 | Implement density, keyboard, touch target and accessibility rules in shared controls | NOT STARTED | — |
| P2-08 | Implement the master search index, ranking, result paths, keyboard flow and permission filtering | NOT STARTED | — |
| P2-09 | Implement shared context changes, dirty-page guards, stale result handling and busy/cancel behaviour | NOT STARTED | — |
| P2-10 | Rebuild Today Overview using the reference grouping and truthful readiness | NOT STARTED | — |
| P2-11 | Build Settings category navigation and dedicated connection, backup and support-package pages | NOT STARTED | — |
| P2-12 | Rebuild Import Files into clear select/validate/review/import/result stages | NOT STARTED | — |
| P2-13 | Rebuild the DSR screen with compact scope/date/actions and bounded preview | NOT STARTED | — |
| P2-14 | Exercise four complete journeys: find DSR, create a test support package, import a fixture, progress daily close | NOT STARTED | — |
| P2-15 | Inspect screenshots and controls at wide, standard and constrained sizes; repair shared flaws | NOT STARTED | — |
| P2-16 | Run focused route/state/search/component and business regression tests | NOT STARTED | — |
| P3-01 | Complete Modules Home, Dashboard, trends/control/activity details and navigation preferences | NOT STARTED | — |
| P3-02 | Complete Daily Close and all Manual Entry types | NOT STARTED | — |
| P3-03 | Convert remaining Imports intake, Source Inbox, bulk history, watch folder and import history | NOT STARTED | — |
| P3-04 | Convert quarantine, duplicates, already-present facts, conflicts, failures and unknown-layout review | NOT STARTED | — |
| P3-05 | Convert document repository, native PDF extraction, OCR queue and extraction history | NOT STARTED | — |
| P3-06 | Apply the report workspace to every current report and report-pack route | NOT STARTED | — |
| P3-07 | Convert all active registers, including record creation/edit/detail and document links | NOT STARTED | — |
| P3-08 | Convert accounting preparation, ledger mapping/review, validation, reconciliation and export/history | NOT STARTED | — |
| P3-09 | Convert archive generations, final packs, comparison, restatement, re-export and shared/source history | NOT STARTED | — |
| P3-10 | Convert Exceptions inbox, every issue category and Owner approval workflows | NOT STARTED | — |
| P3-11 | Complete General, Users & Access and Stores & Masters settings | NOT STARTED | — |
| P3-12 | Complete Database & Recovery, Integrations, System Health, scheduler and audit surfaces | NOT STARTED | — |
| P3-13 | Complete Help, Profile, preferences, startup/setup and all supporting dialogs | NOT STARTED | — |
| P3-14 | Synchronise global search, favourites, aliases and cross-module links against the full route registry | NOT STARTED | — |
| P3-15 | Validate all cross-module journeys, scope/date changes and dirty/locked/busy transitions | NOT STARTED | — |
| P3-16 | Remove superseded production UI paths/styles and update current UI contracts, Help and architecture notes | NOT STARTED | — |
| P3-17 | Reconcile the inventory against source and inspect every converted task | NOT STARTED | — |
| P4-01 | Build the integrated solution and run required repository checks plus the full automated suite | NOT STARTED | — |
| P4-02 | Validate every live route, report code, alias, tile, breadcrumb, Back path and master-search destination | NOT STARTED | — |
| P4-03 | Inspect every distinct screen and relevant dialog at the normal viewport; inspect every shared pattern at all sizing/DPI combinations | NOT STARTED | — |
| P4-04 | Exercise keyboard-only journeys, focus restoration, touchscreen-sized controls and available accessibility tools | NOT STARTED | — |
| P4-05 | Execute the journey scenarios in Section 8 with positive, negative, empty and busy states | NOT STARTED | — |
| P4-06 | Verify Viewer, Store Manager and Owner access, including search and direct route attempts | NOT STARTED | — |
| P4-07 | Re-run financial/import/archive/recovery checks in disposable fixtures and compare protected original data | NOT STARTED | — |
| P4-08 | Verify startup, search, navigation, table responsiveness and long-running-operation feedback | NOT STARTED | — |
| P4-09 | Fix failures, inspect affected areas and rerun relevant checks before the final full validation | NOT STARTED | — |
| P4-10 | Produce a versioned release candidate from a traceable source state using existing packaging scripts | NOT STARTED | — |
| P4-11 | Upgrade the acceptance VM, launch the installed app and execute installed UI smoke journeys | NOT STARTED | — |
| P4-12 | Check repair/reinstall and relevant offline operation against the candidate; exercise safe rollback in the test environment | NOT STARTED | — |
| P4-13 | Complete before/after gallery, requirement traceability, findings and known-limitations report | NOT STARTED | — |
| P4-14 | Audit the whole sprint against its Definition of Done and write the final handover | NOT STARTED | — |

## Requirement and journey verification

UX-01 through UX-24: NOT RUN. J01 through J18: NOT RUN. Update individual results in the plan traceability and final acceptance report with evidence; do not assign blanket passes based on existing tests.

## Changes and limitations

No implementation decisions beyond the frozen plan have been made. The session handoff and prior VM acceptance record describe existing limitations. Record newly discovered issues here with affected tasks and independent work that can continue.

## Final disposition

INCOMPLETE — WORK REMAINS: all 59 execution tasks remain to be performed. This is a completed planning/handoff checkpoint, not a completed redesign.
