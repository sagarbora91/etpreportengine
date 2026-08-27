---
type: adr
status: accepted
date: 2026-08-27
last_verified: 2026-08-27
---

# ADR-004 - Obsidian for Project Knowledge

## Context

Business meaning, mappings, assumptions and decisions are expensive to reconstruct from code and can be lost across sessions.

## Decision

Use the repository `knowledge/` directory as an Obsidian-compatible Markdown vault. Store compact semantic knowledge and link existing detailed documents; do not mirror source code or Graphify.

## Reason

Repository-local Markdown is versionable, readable without Obsidian and directly accessible to Codex and developers.

## Alternatives considered

- External personal vault: rejected because it can drift from the repository and is harder for agents to access.
- Database/vector/RAG platform: deferred as unnecessary complexity.
- One Markdown file per source file: rejected as documentation bloat.

## Consequences

The vault must remain selective. Meaningful rule/architecture/mapping changes update it; trivial code edits usually do not. Machine-local Obsidian workspace and cache state are ignored.

## Affected components

`knowledge/`, `AGENTS.md`, repository README and `.gitignore`.
