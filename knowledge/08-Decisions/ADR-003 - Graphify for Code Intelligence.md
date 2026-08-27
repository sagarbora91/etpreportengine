---
type: adr
status: accepted
date: 2026-08-27
last_verified: 2026-08-27
---

# ADR-003 - Graphify for Code Intelligence

## Context

Repeated whole-repository inspection wastes time and context, while textual search alone does not explain call/dependency relationships.

## Decision

Maintain `graphify-out/` and use `graphify query`, `path` or `explain` before broad source inspection for codebase tasks. Verify returned details against actual source before editing.

## Reason

Graphify provides scoped structural discovery: where a feature lives, what calls it and which components/tests may be affected.

## Alternatives considered

Manual full-tree scans remain a fallback but are not the default. Copying Graphify output into Obsidian is rejected as duplication.

## Consequences

Run `graphify update .` after code changes. Generated graph cache churn is expected and must not be confused with product changes.

## Affected components

`AGENTS.md`, `.graphifyignore`, `graphify-out/` and the AI retrieval workflow.
