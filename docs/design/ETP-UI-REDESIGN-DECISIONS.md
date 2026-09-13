# UI redesign decisions

Date: 12 September 2026. Status: implementation decisions recorded through final candidate preparation; observed acceptance is separate.

Authority: [four-phase sprint plan](ETP-UI-REDESIGN-FOUR-PHASE-SPRINT-2026-09-12.md). This register explains frozen choices and implementation repairs. The ledger and acceptance record establish their verification status.

| ID | Decision | Basis | Affected requirements |
|---|---|---|---|
| D01 | Preserve existing colours and use Today Overview's categorised tile groups consistently | Explicit user preference | UX-01, UX-02 |
| D02 | Design for desktop/tablet, with keyboard, mouse and touch; no phone UI | Explicit user scope | UX-04–06, UX-14–15 |
| D03 | Replace giant combined task pages with module/category/focused-task navigation | User's navigation and scrolling concerns | UX-02–08 |
| D04 | Provide persistent master search, visible result paths and exact DSR navigation | Explicit user example | UX-07, UX-09–11 |
| D05 | Execute all four phases as one sprint, with internal gates rather than user prototype approval | Explicit user request for autonomous execution | UX-24 |
| D06 | Keep native WPF, existing module ownership, one composition root and shared route metadata | Engineering default consistent with existing architecture | UX-02–03, UX-10, UX-20 |
| D07 | Use the sizing, density, accessibility and latency targets defined in plan Section 4 | Frozen implementation defaults; not claimed as individually user-specified numbers | UX-04–06, UX-11, UX-14–15 |
| D08 | Use Settings categories General, Users & Access, Stores & Masters, Database & Recovery, Integrations | Frozen information-architecture default | UX-03, UX-08, UX-21 |
| D09 | Search local navigation/help metadata; do not index business records or PII in this sprint | Scope/privacy default | UX-10, UX-20 |
| D10 | Preserve all financial rules, roles, original data, duplicate handling and recovery controls | Existing product boundaries and user goal | UX-12–13, UX-17–20 |
| D11 | Keep seven existing unavailable features deferred with reasons | Existing missing policy/source authority; redesign does not enable them | UX-02, UX-10, UX-20 |
| D12 | Separate implemented, tested backend and observed installed UI evidence | Existing access limitations and truthful acceptance | UX-22–23 |
| D13 | Review four working representative areas internally, then convert the complete app | Prevent incomplete prototype-only delivery | UX-02, UX-24 |

## How to extend this record

For each significant execution decision, add date, task ID, source evidence, chosen resolution, alternatives if relevant, affected requirement IDs and verification impact. Resolve routine choices locally. Do not silently weaken requirements or remove features. Record a significant accepted architecture change as an ADR under repository rules during Phase 1; this planning register does not replace that ADR.

## Execution decisions — 12 September 2026

| ID | Task / decision | Evidence and reason | Requirements / verification |
|---|---|---|---|
| D14 | P2-01/06: retain existing module controls and handler ownership in focused task composition | Avoid recreating drafts/jobs or changing business coordinators; ADR-009 records the adapter and its limitations | UX-03/12/13; repeated transition test and module regression suite, installed checks open |
| D15 | P2-09: opened editing workspaces retain explicit date/store overrides when the header changes | Direct inspection found the prior date fan-out could retarget unfinished values | UX-12/13; footer/status describe overrides; task-level dirty transition checks remain open |
| D16 | P3-08/09: separate accounting approval reason, ledger mapping, sharing contacts and distribution surfaces | Existing combined forms conflated actions belonging to different roles/tasks | UX-02/03/20; existing service permission checks retained; explicit action names replace positional button lookup |
| D17 | P2-15: use three category columns when more than six tiles, and a compact Daily Close surface at the smallest height | Offscreen matrix exposed clipped overview cards | UX-04/05; not physical tablet/DPI acceptance, secondary task reachability still requires review |
| D18 | P4-01: update Help restoration and XAML name-count contracts for retained focused tasks and named actions | Four r7 failures reflected deliberate UI-contract changes; financial/access assertions unchanged | r8 626 passed, r9 628 passed; counts are supporting evidence only |
| D19 | P4-03: sanitize task IDs in screenshot filenames | Help IDs contain colons, which can produce Windows alternate streams rather than distinct evidence files | matrix-r3 uses safe filenames; prior matrices retained but not complete Help galleries |
| D20 | P3-15: reject stale focused previews and exports after date/store changes | Tests reproduce late completion after scope changes; refresh/run shortcuts now use the visible selectors | UX-12/18; r25 passed 637 tests; installed date/export journey remains open |
| D21 | P3-06: retain fixed DSR/Titan/Helios/combined scope and show it explicitly | Existing DSR query always produces both stores and combined totals; fixed report scopes are applied before preview activation | UX-12/18/20; no financial calculation changed |
| D22 | P4-03: compact report Actions menu and Summary/Detail rows tabs; short Today group selector | Small-client captures showed the report toolbar consuming preview space and hidden Today groups lacking a direct selector | UX-04/05/06/14; new captures required; details callback preserves exact source row |
| D23 | P3-15: report-pack actions use the displayed date/store after draft resolution and show the exact pack task | Prior action used the last Daily Workflow store and could overwrite its draft date | UX-12/13/18; existing pack generation services and permission checks retained |
| D24 | P3-12: maintenance launcher passes validated active server/database arguments | Scripts previously relied on their default database regardless of displayed connection | UX-20; four target-validation tests; live disposable backup/restore audit passed |
| D25 | P2-08: separate Ctrl+F local row filtering/text finding from Ctrl+K navigation | Plan explicitly reserves these different purposes; local filtering uses an independent collection and never modifies export totals | UX-07/15/18; actual keyboard confirmation pending |
| D26 | P1-02/P3-14: reconcile 243 original controls and 34 legacy report buttons | The 34 labels invoke 29 report codes, so they become tested search aliases of those exact reports; original Export, variance filter and row detail actions have explicit focused replacements | UX-02/03/10/18; retained control identity, alias and filter tests |
| D27 | P2-09/P3-15: retain drafts per schedule, contact, master type and review record | Selection/refresh previously risked retargeting reasons or discarding fields. Save/discard guards cover persistent drafts; review reasons remain attached to IDs and close requires explicit discard | UX-12/13/20; failed-save, row/refresh round-trip, busy and reentry tests |
| D28 | P2-06/P3: one bounded table per selected tab; input fields on a separate tab | Nested scrolling tables had unusable heights. Live tables and fields remain retained, with actual context above tabs and selected tab remembered | UX-05/06/12; component state tests and matrix-r14 |
| D29 | P3-06/13: dedicated favourites and five Help categories | An unbounded favourites section and 20 Help tiles broke the normal-size overview contract. Reports uses eight categories plus favourites/filters/packs; favourites groups by category | UX-04/08/21; 50 overview bounds checked, Help round trips tested |
| D30 | P2-07/P4-04: keep palette values; use existing AccentDark for primary action text contrast and at least 12-DIP metadata | White on the existing bright Accent was below 4.5:1. The darker existing teal meets it; focus visuals and calendar hit areas are enlarged | UX-01/14/15; full App date component audit passes; actual touch remains unverified |
| D31 | P3-15: keep a session on its database after opening a work task | Retained drafts, selected IDs and previews belong to that database. A connection change is rejected before health/bootstrap calls after task context starts; restart and open connection settings first to switch databases | UX-12/20; explicit product message and negative service-call tests; deliberate session boundary, not a changed financial rule |
| D32 | P3-15: distinguish committed operations from failed follow-up refresh | Integration/access saves must not be reported as unsaved merely because a later display refresh failed | UX-13/16; committed-save regression and separate diagnostic codes |
| D33 | P2-06: bounded status summary plus visible Status details | Full messages are available without hover or crowding the short task screen; details use a bounded native WPF dialog and Close | UX-05/14/15/16/21; offscreen short-dialog composition, actual keyboard check required |
| D34 | P3-06/P4-05: explicit single-store selection, snapshot dates and failure-to-preview propagation | Actual synthetic report queries exposed combined-scope rejection and loading left visible after failure. Snapshot From equals To for the query without destroying the saved range; stock variance remains a range | UX-12/16/18; all 29 reports reach populated and missing states; financial queries unchanged |
| D35 | P3-06/P4-03: DSR tabs and short-window Period & store dialog | Populated 800×440 previews were cramped; bounded Summary/store/service tabs and secondary filter dialog leave readable values and actions. Source availability remains explicit | UX-05/06/14/18/21; populated report gallery and dialog component evidence |
| D36 | P4-03: composite the Window background in offscreen dialog capture | Content-only rendering left a transparent backdrop, hiding black labels in image viewers. Correct the capture method rather than changing valid product colours | UX-23; controls-r2 retained, controls-r3 is corrected capture; no native-interaction claim |
| D37 | P4-09: keyboard refresh uses the same snapshot scope helper as the report action | Final review found the shortcut still replacing a retained range start with the snapshot date. R5 routes it through ApplyTaskScope and adds a snapshot-to-range regression | UX-12/18; final Release suite; no visual or financial-layer change |
| D38 | P4-04/09: F6 cycles current visible regions | Final keyboard review found the old shortcut targeting the hidden legacy scroll host. It now cycles search, scope, current content and density, skipping unavailable targets | UX-07/15; r6 source and full regression; actual focus order remains separately unverified |
| D39 | P3-12/15: bind loaded integration settings to the current database session | Integration pages use the Settings owner and could previously evade the general working-module guard after loading clean configuration. Connection tests and bootstrap now refuse a different target once integrations load; checking the same target remains allowed | UX-12/13/20; r7 regression verifies both entry points do not call the replacement database and preserve the original session |


## Local follow-up decisions

- **D40 — Complete report-file fidelity (UX-18/J08).** Reuse existing PDFsharp and Segoe UI font support for generic PDF output. Wrap and paginate complete values, use column sections with repeated identifiers, include totals, retain signed chart axes and every composed point/series. No financial calculation or query changes. Reason: actual saved-file comparisons exposed truncated fields, absent totals and misleading chart signs in the previous exporter. Evidence: local-followup comparison/fixture/stress records.
- **D41 — Export completion and closing (UX-12/13/J18).** Capture report identity/revision before asynchronous writes; prevent reentry; suppress stale visible status; report saved-file versus audit-history failure accurately; keep the app open during report file writing while allowing report navigation. R9 supersedes r8 for the final close guard. No new permission flow or business rule. Deferred export tests pass; actual installed close/file-dialog interaction remains pending.
