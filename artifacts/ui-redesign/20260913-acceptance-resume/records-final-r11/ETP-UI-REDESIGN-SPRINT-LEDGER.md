# UI redesign sprint ledger

**Latest checkpoint, 13 September 09:10:57 UTC: r11 INSTALLED — acceptance INCOMPLETE.** [r10/r11 evidence and exact next action](../audit/ETP-UI-REDESIGN-R10-2026-09-13.md) supersede earlier connection blockers below. VM credentials/worker access are solved. Original baseline/backup preserved; r11 installed hash and counts/settings/integrity checks pass; 669 tests pass. Density moved into personal Settings → Display. Start menu shortcut exists. Actual screen capture still fails with 0x80004002, so Settings sidebar and caption-button reports, app launch, full journeys, roles/device/accessibility/performance and remaining installer lifecycle are unverified. Do not request credentials or reinstall routinely. Runtime changes are in the working tree on d24a525 and bound by the r11 immutable snapshot; documentation below retains historical r9 evidence.

**13 September acceptance resumption: INCOMPLETE — WORK REMAINS.** Settings sidebar defect is user-reported and awaiting reproduction/repair. Fresh VM permission denial and two screen-capture failures prevent installed acceptance. The returned host ETP executable is 1.8.5, not r9; archived r9 hashes match. [Current evidence and exact next action](../audit/ETP-UI-REDESIGN-ACCEPTANCE-RESUME-2026-09-13.md) supersede the previous next action and no-known-defect statement below. No new acceptance pass; P4-09 is reopened for this reported defect.

Verification checkpoint UTC: 2026-09-12T12:53:09.931255+00:00. Documentation/source-push checkpoint: 13 September 2026. Run `20260912-sprint`.

**IMPLEMENTED — VERIFICATION BLOCKED.** Full application conversion and the authorised local follow-up are complete. Mandatory installed/device acceptance remains open. No production deployment.

## Exact next action

Resume with the **1.8.8-r9 receipt and source snapshot** and the [prepared VM runbook](../audit/ETP-UI-REDESIGN-VM-ACCEPTANCE-RUNBOOK.md). Check the already requested elevated worker connection to ETP-Acceptance-186. First run the queued read-only installed/data baseline; preserve original data/backups; then install r9 only in the acceptance VM and execute J01–J18, real Windows roles, native DPI/keyboard/Narrator/touch, performance and installer lifecycle. No source reinventory or prototype restart is required. Do not mark a procedure or backend/export check as an installed interaction pass.

## Current candidate and completed checks

- Start source491f5ba; source implementation is committed and pushed as d7eb51538881c7ce46be2e1653d3ed5e7f9523b3. The r9 build used planning HEAD847f22e with implementation in the working tree; its immutable archive identifies exact build inputs. Only two trailing blank lines were removed before committing. See the [13 September handoff](../audit/ETP-SESSION-HANDOFF-2026-09-13-UI-REDESIGN.md) for release hashes and resumption.
- **1.8.8-r9:668 passing Release tests** — 344 Desktop,195 SQL,66 Reporting,51 Import,12 Domain. Baseline617; r7 had658.
- Full conversion:128 active canonical destinations,29 reports,7 deferred,243 original controls reconciled,527 coverage rows. Existing matrix-r19, report-states-r7 and controls-r3 remain scoped historical visual/component evidence.
- [Completed local follow-up](../audit/ETP-UI-REDESIGN-LOCAL-FOLLOWUP-2026-09-12.md): five findings repaired,58 live synthetic report states exported to Excel/PDF,2,431 exact workbook cells checked,all non-DSR PDF model content present,12 independent fixture anchors and11 DSR PDF checks. Additional797-cell stress report and257-page PDF bounds checks pass.
- Final package audit:33 payload files;9 recovery/support scripts and15 migrations match source; embedded version/self-contained receipt agree; no unexpected payload or credential-pattern findings. Signing and installed lifecycle remain unverified/not signed as recorded.
- Fresh disposable SQL seeding again passed duplicate/overlap,pack/lock,checksum,full restore and lineage checks. All58 detailed report models unchanged by repairs. Follow-up changed report rendering/status/close handling, not Domain/Application/Import/SQL rules or migrations.
- Generated follow-up database removed. Host settings were not redirected. Protected host aggregates remain540 rows,3,203,362.6900 net,514 units. Historical original VM3/2300/2/1/3 remains unverified now.
- r7/r6 native host observations are historical, not r9 interaction evidence. No r9 installed or native device journey passed. Candidates r1–r8, their source snapshots where produced, and all failed attempts are retained.

## Gates

| Gate | Status | Decision |
|---|---|---|
| G1 | FAIL / BLOCKED | Inventory/design/baseline source deliverables complete. Available host baseline captured; actual VM visual/data baseline remains inaccessible. |
| G2 | FAIL / BLOCKED | Shared shell/components/search/context and representative screens implemented and component-tested. Complete representative actual-UI journeys remain unverified. |
| G3 | PASS | Full active source inventory converted, original controls or replacements reconciled,29 reports and supporting workflows mapped; no active module omitted or financial rule removed. This is the implementation gate, not installed acceptance. |
| G4 | FAIL / BLOCKED | Final source/build/installer/local evidence complete; mandatory installed journeys, lifecycle, real roles and device/scaling evidence remain unavailable. |

Independent source work continued through all phases under plan §3.8 while mandatory environment gates remained explicit. No gate was treated as user approval, and no criterion was waived.

## Task register

IMPLEMENTED means source/design deliverable complete. VERIFIED applies only to the stated source/local/evidence deliverable. BLOCKED identifies remaining mandatory environment work while preserving its completed local component.

| Task | Work | Status | Evidence / remaining step |
|---|---|---|
| P1-01 | Read repository instructions, relevant knowledge/ADRs and source; use Graphify and the precise code graph for ownership/impact | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-02 | Inventory every active navigation entry, report, workspace, toolbar action, dialog, Help/Profile/Settings surface and role variation | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-03 | Record baseline layouts at available desktop/VM sizes and density settings | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P1-04 | Assign canonical module/category/task paths and exact route IDs | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-05 | Choose one shared pattern for each screen and each state | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-06 | Inventory source-level permissions, service guards and unsaved/locked/context behaviour | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-07 | Reconcile business dates, scopes and report/import overrides across workflows | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-08 | Define the search index and aliases against the canonical route map | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-09 | Check build, tests, fonts/theme, VM connectivity and available screenshot/input automation | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-10 | Establish protected original-data checks and disposable fixture environments | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P1-11 | Freeze a layout sketch/component specification for Overview, Settings, Import Files and DSR | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P1-12 | Populate the sprint ledger and record the significant navigation architecture decision | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P2-01 | Extend routing to identify exact tasks, preserving old destinations through compatibility mapping | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-02 | Use a shared descriptor for menu/tile/search/breadcrumb destination metadata | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-03 | Implement the persistent shell with compact navigation and responsive header | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-04 | Implement category navigation and stateful history | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-05 | Build shared overview groups, task tiles, metric tiles and status components | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-06 | Build shared task headers/action bars, focused forms, tables and detail surfaces | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-07 | Implement density, keyboard, touch target and accessibility rules in shared controls | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-08 | Implement the master search index, ranking, result paths, keyboard flow and permission filtering | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-09 | Implement shared context changes, dirty-page guards, stale result handling and busy/cancel behaviour | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-10 | Rebuild Today Overview using the reference grouping and truthful readiness | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-11 | Build Settings category navigation and dedicated connection, backup and support-package pages | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-12 | Rebuild Import Files into clear select/validate/review/import/result stages | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-13 | Rebuild the DSR screen with compact scope/date/actions and bounded preview | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P2-14 | Exercise four complete journeys: find DSR, create a test support package, import a fixture, progress daily close | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P2-15 | Inspect screenshots and controls at wide, standard and constrained sizes; repair shared flaws | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P2-16 | Run focused route/state/search/component and business regression tests | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P3-01 | Complete Modules Home, Dashboard, trends/control/activity details and navigation preferences | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-02 | Complete Daily Close and all Manual Entry types | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-03 | Convert remaining Imports intake, Source Inbox, bulk history, watch folder and import history | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-04 | Convert quarantine, duplicates, already-present facts, conflicts, failures and unknown-layout review | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-05 | Convert document repository, native PDF extraction, OCR queue and extraction history | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-06 | Apply the report workspace to every current report and report-pack route | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-07 | Convert all active registers, including record creation/edit/detail and document links | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-08 | Convert accounting preparation, ledger mapping/review, validation, reconciliation and export/history | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-09 | Convert archive generations, final packs, comparison, restatement, re-export and shared/source history | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-10 | Convert Exceptions inbox, every issue category and Owner approval workflows | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-11 | Complete General, Users & Access and Stores & Masters settings | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-12 | Complete Database & Recovery, Integrations, System Health, scheduler and audit surfaces | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-13 | Complete Help, Profile, preferences, startup/setup and all supporting dialogs | IMPLEMENTED | Implementation complete; all remaining actual-UI/device/installed conditions recorded in acceptance. |
| P3-14 | Synchronise global search, favourites, aliases and cross-module links against the full route registry | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P3-15 | Validate all cross-module journeys, scope/date changes and dirty/locked/busy transitions | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P3-16 | Remove superseded production UI paths/styles and update current UI contracts, Help and architecture notes | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P3-17 | Reconcile the inventory against source and inspect every converted task | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P4-01 | Build the integrated solution and run required repository checks plus the full automated suite | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P4-02 | Validate every live route, report code, alias, tile, breadcrumb, Back path and master-search destination | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-03 | Inspect every distinct screen and relevant dialog at the normal viewport; inspect every shared pattern at all sizing/DPI combinations | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-04 | Exercise keyboard-only journeys, focus restoration, touchscreen-sized controls and available accessibility tools | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-05 | Execute the journey scenarios in Section 8 with positive, negative, empty and busy states | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-06 | Verify Viewer, Store Manager and Owner access, including search and direct route attempts | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-07 | Re-run financial/import/archive/recovery checks in disposable fixtures and compare protected original data | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-08 | Verify startup, search, navigation, table responsiveness and long-running-operation feedback | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-09 | Fix failures, inspect affected areas and rerun relevant checks before the final full validation | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P4-10 | Produce a versioned release candidate from a traceable source state using existing packaging scripts | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P4-11 | Upgrade the acceptance VM, launch the installed app and execute installed UI smoke journeys | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-12 | Check repair/reinstall and relevant offline operation against the candidate; exercise safe rollback in the test environment | BLOCKED | Local supporting work complete; actual VM/UI/role/DPI/device or original-VM verification blocked. See corresponding UX/J row. |
| P4-13 | Complete before/after gallery, requirement traceability, findings and known-limitations report | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |
| P4-14 | Audit the whole sprint against its Definition of Done and write the final handover | VERIFIED | Source inventory, contracts, tests, gallery and acceptance record; scope described there. |

## Requirements, findings and historical attempts

[Requirement-by-requirement UX-01–24 and J01–18 results](../audit/ETP-UI-REDESIGN-ACCEPTANCE.md), [coverage CSV](ETP-UI-REDESIGN-ROUTE-COVERAGE.csv), [decisions](ETP-UI-REDESIGN-DECISIONS.md), and [handover](../audit/ETP-UI-REDESIGN-HANDOVER-2026-09-12.md) are authoritative. No known in-scope source defect remains from the available checks. Unobserved interaction may expose further defects; this is why the full sprint is not declared verified.

Earlier ledger/acceptance/handover checkpoints are preserved under artifacts/ui-redesign/20260912-sprint/records-before-r4. R1–r6 candidates and all failed test/render/input attempts remain unchanged. D34–D39 explain final report/layout/capture/shortcut repairs.


## Local follow-up — complete

All six authorised local activities are complete: focused defect review, targeted tests, report-file comparisons, package audit, acceptance-run preparation and handover. See the linked follow-up report for exact scopes and remaining limits. D40–D41 record the export repairs. The exact next action is the VM baseline and installed run above.
