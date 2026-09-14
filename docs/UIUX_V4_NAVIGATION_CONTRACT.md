# ETP desktop navigation contract — 1.8.8 candidate

The governing [four-phase sprint plan](design/ETP-UI-REDESIGN-FOUR-PHASE-SPRINT-2026-09-12.md) defines acceptance. This document describes the implemented candidate; [acceptance evidence](audit/ETP-UI-REDESIGN-ACCEPTANCE.md) records which behaviours remain unverified.

## Hierarchy and exact destinations

Modules Home → module overview → category → focused task. The compact rail, master search, business scope/date and location remain in the shared shell. The old wide sidebar is retained only as compatibility code; focused task navigation does not display it.

TaskNavigation reconciles original menu aliases with canonical WorkspaceRoute.TaskId values. The registry contains 128 active canonical destinations, all 29 report codes and seven explicitly deferred entries. The 34 original report-button labels resolve to tested canonical report aliases. ShellNavigationService checks task/destination/report consistency and role access before adding a route to history. Visibility does not grant service permission.

Search indexes local task names, aliases, report codes and paths; it does not index business records or PII. Ctrl+K focuses persistent search. DSR resolves to Reports → Sales → Daily Sales Report. Settings → Database & Recovery → Support Package opens the dedicated action. Search, tiles and history share the same route guard.

## Context, drafts and navigation

Breadcrumbs show hierarchy; Back/Forward restore prior task context. Editing workspaces retain their explicit displayed task date/store after opening. Header changes update report context and initialize unopened workspaces; the task footer explains overrides. Imports require an explicit store. Report date/store changes invalidate preview/export until refreshed.

Register, daily workflow, settings, schedule, contact, user and master drafts participate in guarded navigation and closing. Save failure retains entered values. Selection-specific drafts and review reasons remain attached to their original record IDs through selection and refresh; closing review drafts requires explicit Stay/Discard. Operation gates prevent reentry and block conflicting navigation while work is running, keeping cancellation available where supported. A completed save followed by a refresh failure is reported as saved with a failed follow-up, so it is not mistaken for permission to repeat the write.

Changing the database after a working module has opened or integration configuration has loaded is refused with an explanation to restart and open Connection first. This deliberate session boundary prevents retained IDs, previews or drafts from being used against another database. The integrated connection rules and existing backup/recovery safeguards remain.

DSR always compares Titan and Helios including combined totals. Named Titan, Helios and combined sales reports retain their fixed scope. Other supported reports expose scope selection. Generating a pack resolves drafts, opens the correct store/combined pack task and uses the report's displayed date/scope. Financial formulas and service permissions remain in their existing owners.

Reports requiring one store explicitly require Titan or Helios; they never silently substitute a store for combined scope. Snapshot reports use the displayed business date for both query endpoints. Range reports, including stock variance, retain From/To. Opening a snapshot does not overwrite the retained range context. A failed query replaces loading with its actionable error and keeps export unavailable.

## Layout and controls

Target normal client area is 1000×600 DIP; constrained checks reach 800×440. Module/category tiles replace long sidebar lists. Today uses four related groups; short windows expose a labelled group selector so secondary groups remain reachable. Focused task actions stay above bounded content.

Generic reports separate Summary and Detail rows. The detail table scrolls independently, retains virtualisation and provides an explicit selected-row source-details action plus Enter/double-click. Report Actions contains PDF/Excel export, report-pack generation and export-folder access; DSR additionally exposes Manual Entry. Export remains disabled for stale previews.

DSR separates Summary, Titan World, Helios and Service & targets into bounded tabs, with source availability in a labelled dialog. Narrow generic reports open Period & store in an Apply/Cancel dialog. Favourite Reports is a focused category selector with report tiles; Reports overview stays bounded. Help uses five categories, contextual exact-task links and reliable return navigation.

Status details opens the complete task/application message. Native file pickers keep Windows appearance; application-owned dialogs use shared controls and bounded content with visible footer actions.

## Preferences and keyboard

Settings → Display → Display & Preferences exposes density on its Display tab, alongside separate pinned-module and favourite-report tabs. Comfortable uses 48-DIP actions and 46-DIP grid rows; Compact uses 34/30. The selected density applies throughout the application; the selector appears only in Settings, not in every screen's status bar. Viewer, Store Manager and Owner retain access to personal preferences; administration and recovery remain restricted. Preferences remain in the existing atomic local preference store.

- Ctrl+K: master task navigation search.
- Ctrl+F: filter the active table locally, or find text in the current page. The labelled popup states that filtering does not change summary totals or exported data. Close clears the local filter.
- Alt+Left/Right: task history; Alt+Home: Dashboard.
- F6: cycle master search, scope and the visible task content; hidden legacy surfaces are skipped.
- F1: contextual help; Ctrl+/: shortcut guide.
- Escape: close active local search/detail/help surface as applicable.
- Existing report refresh/run/export shortcuts use the displayed report scope.

Accessible names and keyboard focus are implemented in shared controls. Actual Narrator, physical touch, real Windows role identities and installed DPI acceptance must be established separately; offscreen rendering is not that evidence.
