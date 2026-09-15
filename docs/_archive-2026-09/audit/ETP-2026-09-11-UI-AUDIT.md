# Desktop interaction audit — 11 September 2026

Scope: production WPF modules, using `prototypes/etp-uiux-demo` and the August UI audit/revamp as design intent. Source baseline inspected: `65511fa3eda7c368b65b2901be88c0e1968f791d`. This is the bounded UI agent report; the integrated acceptance register and root execution logs are authoritative for executed results.

## Method and limits

Read AGENTS.md, AI-CONTEXT, AI-ROUTER and Desktop Architecture. Queried Graphify and code-review-graph before source inspection. The latter reported its index built at `367f528` and did not match HEAD, so implementation and tests were inspected directly. Current ownership registry still declares 14 destinations and 29 report routes. These counts prove registration only.

Reviewed actual source and existing tests for Daily Workflow/manual entry, report request/presentation/export, import entry points, accounting preview/save boundaries and Settings. No independent build, SQL execution, interactive desktop run or installer operation was performed by this agent; root coordinates those sequentially. New tests operate actual WPF controls with synthetic deferred Application-port responses. They are interaction regressions, not SQL or human UAT evidence.

## Issue register and before/after evidence

| ID | Severity | Function/screen | Reproduction / before | Expected / fix | Retest evidence |
|---|---|---|---|---|---|
| UI-001 | High | Daily Workflow scope/readiness | Start readiness load for WLMHW, switch to HEMW before it completes; old completion applies readiness and finalise eligibility to current selectors. Even an already loaded scope leaves its readiness visible when changed. | Scope change immediately clears readiness rows and disables finalise. Revision checks discard older success/failure completions. Existing unsaved text remains with a visible instruction to review it for the selected scope. | `Scope_switch_clears_readiness_and_ignores_an_older_pending_refresh`; execution result in root test log. |
| UI-002 | High | Manual input, stock, staff target, finalise/reopen and pack generation | Invoke save again while its Application command is pending. No command guard; fields stay editable and the first completion clears text, potentially erasing subsequent edits. | One operation at a time across this workspace, with visible working status and disabled form controls while awaiting completion. Finally restores controls; failed persistence preserves draft. | `Pending_save_prevents_duplicate_commands_and_preserves_draft_on_failure`; existing zero/access/finalise/pack tests retained. |
| UI-003 | Medium | Manual Entry / physical stock / staff targets | Blank production text boxes have only hover tooltips and automation names; quantities and reasons lack persistent visible labels. | Persistent labels wrap above each text field, with bounded field groups in the existing responsive wrap panels. | Source/XAML inspection. Baseline screenshots: `verification/review-2026-09-11/before`. After screenshots and size checks recorded by root. No independent visual-pass claim here. |
| UI-004 | High | All report routes and exports | Start slow Management Trend, run Titan Sales, then complete the older query. The older routine assigns shared rows and export metadata while the active report code belongs to the newer request. Old failures also overwrite newer status. | Each query routine captures a request revision and discards stale completion before touching rows, preview or export. All 29 routes use these guarded routines. | `Older_report_completion_cannot_replace_new_route_preview_or_export` (older success and failure). Uses independent synthetic old sales 999 versus current 42; verifies only current preview/export data is published. |
| UI-005 | High | Report dates/store/query filters | Run a report, change dates, then export; export stays enabled. A pending query can also attach old rows to metadata from edited selectors. | Editing any query scope field or sales dimension invalidates export and pending result revision, with a visible rerun instruction. Search/variance display filters retain their existing semantics. | `Date_change_invalidates_export_and_pending_results_until_report_is_rerun`; checks no stale preview, disabled export and correct new metadata after rerun. |
| UI-006 | High | Settings database connection | Begin health check for connection A, edit to B and successfully check B, then complete A. A completion calls CompleteHealthCheck, changing the active/persisted connection and overwriting B text/status. | Text edits, newer checks and bootstrap invalidate older checks. Old success and failure cannot apply to the active session. | `Older_health_check_cannot_replace_new_connection_or_its_status` (older success and failure), verifying session connection, text and status. |

Before behavior above is directly established from baseline source control flow. The newly added regression sequences define executable reproduction; do not interpret this document as claiming a baseline executable was run with failing tests unless root records that separately. Source fixes are implemented; verified pass/fail status must come from the integrated execution evidence.

## Coverage distinctions

| Surface / behavior | This agent coverage | Remaining verification |
|---|---|---|
| Daily Workflow/manual entry: scope, repeat save, failure recovery, permissions, measured zero, finalise/reopen, pack invalidation | Source inspected; new and retained WPF test paths identified | Root focused suite, isolated SQL effects, actual keyboard/scroll interactions |
| Reports: all 29 route routines | Static control-flow inspection; stale success/failure guards across shared routines | All route rendering is separate; representative deferred-response tests do not validate every report formula/query |
| Reports: cross-route overlap, dates and export metadata | New WPF regression tests with independent synthetic amounts | Integrated pass logs and packaged journey |
| Settings: connection overlap | Source inspected; two deferred WPF cases | Integrated pass logs; connected SQL role lifecycle |
| Imports | Partial static entry-point inspection | Pending validation versus file/date edits; cancel/retry and duplicate behavior require runtime coverage |
| Accounting | Partial static review: save checks expected scope and balanced complete draft; export requires approved batch | Repeated preview/save/approve interactions and SQL idempotency remain unverified here |
| Source Inbox, Registers, Archive, Operations, Administration, Dashboard, Help | Ownership/navigation context only | Root suite and explicit runtime status; no interaction-pass claim |
| 960×600, 1366×768, 1920×1080; 100/125/150% scaling | Source change retains wrapping layout; before evidence exists from root | Root rendering and environment scaling evidence; unsupported combinations must remain explicit |

## Requested sequential verification

Run Desktop tests with filter `FullyQualifiedName~DailyWorkflowWorkspaceViewTests|FullyQualifiedName~ReportsRequestOrderingTests|FullyQualifiedName~SettingsWorkspaceViewTests`, then the integrated suite and function audit. Capture after Manual Entry at target sizes and inspect labels, field alignment and scrolling. Recheck report navigation/filter changes while loading and Settings connection edits against isolated resources. Root should perform the single coordinated `graphify update .` after integration.

No financial formula, SQL migration, approved rule, role definition or deferred licensing feature was changed by this UI work.

## Render follow-up

UI-007 (Medium): root's verified 960x600 Manual Entry render showed the business date truncated to 9/10/202. Inspected verification/review-2026-09-11/verified/ui/all-workspace-routes/destination-manual-entry-960x600.png. Increased Daily Workflow business-date and neighbouring staff-target date controls from 140â€“145 to 180 DIPs, preserving wrap-panel layout. Root owns after-render verification; no build was run by this agent.

