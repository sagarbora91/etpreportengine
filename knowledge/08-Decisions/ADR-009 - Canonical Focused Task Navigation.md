---
type: adr
status: accepted
date: 2026-09-12
last_verified: 2026-09-12
---

# ADR-009 - Canonical Focused Task Navigation

## Context

The four-phase redesign requires exact task destinations across module tiles, search, breadcrumbs and Back while retaining existing financial/service boundaries. The original menu aliases often opened a combined module workspace.

## Decision

Extend WorkspaceRoute with an optional TaskId. TaskNavigation provides canonical task identity, display path, aliases, availability and role filtering. ShellNavigationService rejects unavailable, inconsistent and unauthorised task routes before adding them to history. All 29 report codes retain their existing report owners.

TaskNavigator composes the persistent header, local metadata search, category tiles and task host. FocusedTaskLayout presents selected existing module controls and pins their existing action buttons; it does not perform business operations. Retained controls preserve drafts and running coordinator jobs across task navigation. Register drafts retain separate field values, store, date and source association per register type. Header context initializes editing workspaces; after opening they retain visibly declared task overrides, so header changes do not silently retarget drafts or imports.

## Consequences and current limitations

This extends [[ADR-005 - Modular Desktop Shell]] rather than moving financial logic into navigation. WPF code remains outside Shell/Navigation. Numeric section selectors are temporary composition adapters and require transition tests whenever module root sections change. They are not proof of semantic or responsive acceptance. Module-owned declarative task views remain desirable where existing sections cannot meet the focused task contract.

The conversion adds module-owned selection drafts, review reasons, report filters, favourites, DSR tabs and settings completion helpers. Shared TaskBodyLayout preserves each live grid on a bounded tab; WorkspaceOperationGate serializes existing actions and prevents conflicting transitions. Save/Discard/Stay resolves persistent drafts; review reasons require explicit discard before closing. Post-save refresh failures retain the successful write outcome.

Database changes require a fresh application session before a working module opens or integration configuration loads. Loaded clean configuration is also bound to its original database; checking that same target remains allowed. This keeps retained record IDs and drafts bound to their original database without introducing a second mutable composition root. Report snapshot scope does not overwrite the retained range dates; single-store reports require explicit store selection. Lower-layer financial calculations and migrations are unchanged.

Current tests verify canonical paths, all 29 report identities, role filtering/direct-route rejection, history, per-record drafts, failed-save retention, operation reentry and repeated retained-control transitions. Source coverage reconciles every original control or its explicit replacement. Offscreen renders and synthetic SQL-to-WPF report states do not verify installed interaction. See the sprint ledger and acceptance evidence for the final candidate, checks and remaining installed/device gates.

## Alternatives

Recreating every workspace on navigation would lose drafts and disrupt jobs. Replacing business coordinators during a UI sprint would unnecessarily expand financial regression risk. Retain those owners and migrate their presentation surfaces incrementally within the authorised continuous sprint.
