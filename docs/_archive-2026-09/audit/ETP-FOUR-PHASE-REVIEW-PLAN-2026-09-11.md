# ETP software and UI review — four-phase execution plan

Date: 11 September 2026
Status: planned; execution has not started
Companion: [Session handoff](ETP-SESSION-HANDOFF-2026-09-11.md)

## Objective and scope

Review and fix existing software, UI and workflow issues in the active .NET 10/WPF Windows application, then deliver a verified executable and installer candidate. Use the approved prototype in `prototypes/etp-uiux-demo/` as the visual reference and current runtime registries as the scope authority. Legacy Android/JavaScript functionality is outside this review.

Historical coverage is 244 active functions, 14 workspace destinations, 29 report routes, 85 role-based acceptance scenarios and seven deferred functions. Refresh these counts from current source. Historical passing tests, route mappings and screenshots are not proof that every interaction works.

## Phase 1 — audit and reproduce

- Inspect current repository state, source, tests and existing evidence. Preserve unrelated changes.
- Run the actual desktop application with realistic synthetic data in an isolated test database.
- Review imports, reconciliation, calculations, manual entry, daily close, reports, exports, archive, registers, accounting, roles, administration, settings, automation, backup/recovery and help.
- Walk through all active functions and applicable role scenarios. Include store/date changes, navigation, repeated actions, cancellation and normal, empty, invalid, loading, failed, restricted and locked states.
- Compare production screens and interactions with the approved prototype.
- Create an issue register with ID, severity, affected function/screen, reproduction steps, expected/actual behaviour, evidence, suspected cause, fix status and retest evidence.
- Create a coverage matrix distinguishing static inspection, render-only evidence, automated behaviour tests, observed interaction tests and unverified conditions.

Exit: every active function has an explicit review status; critical defects have reproducible evidence. Publish the issue register and coverage matrix.

## Phase 2 — fix software and workflows

- Prioritise crashes, incorrect results, data-integrity failures, permission failures and broken workflows.
- Address confirmed import, duplicate handling, reconciliation, manual-entry, daily-close, export, archive, accounting, register and operational defects.
- Investigate stale data, repeated submissions, cancellation, recovery and performance where evidence identifies a problem.
- Verify calculations against independently defined synthetic expected totals. Add meaningful regression tests for confirmed defects and retest neighbouring workflows.
- Resolve deviations from approved business rules; record ambiguous rules as precise decision requests rather than guessing.

Exit: critical workflows pass, fixes have evidence, and remaining defects or dependencies are documented.

## Phase 3 — fix UI and usability

- Improve navigation, primary actions, form grouping, labels, feedback and consistency throughout production screens.
- Fix clipping, overlaps, awkward scrolling and unreadable content.
- Make loading, errors, missing data, permission restrictions and locked states understandable and recoverable.
- Preserve store, date, filters and unsaved work appropriately across navigation and failures.
- Verify keyboard operation, visible focus, table interactions, detail/source-lineage actions and full user journeys.
- Test 960×600, 1366×768 and 1920×1080 layouts, and Windows scaling at 100%, 125% and 150% where available. Distinguish actual scaling tests from fixed-size screenshot rendering.
- Capture before/after evidence with realistic data; screenshots supplement interaction testing.

Exit: reviewed workflows have readable information, reachable actions and clear next steps under the tested conditions.

## Phase 4 — final acceptance and candidate

- Run the integrated test suite and function audit. Recheck fixed defects and affected neighbouring workflows.
- Exercise import → validate → correct → report → export → archive, including permissions and failure recovery.
- Verify installation, upgrade and launch in an available isolated Windows environment. Record unavailable conditions explicitly.
- Use repository release scripts to build fresh Windows x64 executable and installer candidates. Follow version policy and preserve prior candidates before builders replace output directories.
- Verify embedded version, source commit, checksums, packaged dependencies/migrations and packaged application launch.
- Deliver artifact links, final issue register, coverage results, verification evidence and remaining acceptance requirements.

Exit: candidate matches verified source and no critical/high-severity defects remain unresolved. If that gate cannot be met, label the output an engineering candidate and state why.

## Working rules and evidence

- Once execution is requested, continue through phase boundaries without routine approval questions. Provide a concise report at each boundary.
- Use synthetic data and isolated resources; preserve production data, backups, audit history and unrelated changes.
- Preserve approved rules: missing data is not zero, source-negative returns retain their signs, and `CLUSTER` remains Brand Segment.
- Keep owner-approved deferred functionality, including runtime licensing, deferred unless separately authorized.
- Check current environment capabilities before declaring an external dependency blocked. Complete independent safe work while a specific dependency is unavailable.
- Use clean, reviewable commits. This plan does not authorize remote publication.
- Store new evidence in a dated directory such as `verification/software-ui-review/2026-09-11/`; preserve older evidence. Tie results to source commits and artifacts.
- Every issue closes only with retest evidence. Mapping or rendering coverage must not be presented as functional acceptance.
- Do not update permanent business/architecture knowledge for routine fixes or session tracking. Update it only if a durable contract or limitation changes.
