# UI redesign decisions

Date: 12 September 2026. Status: design contract prepared; implementation not started.

Authority: [four-phase sprint plan](ETP-UI-REDESIGN-FOUR-PHASE-SPRINT-2026-09-12.md). This register explains frozen choices; it does not claim the current application already implements them.

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
