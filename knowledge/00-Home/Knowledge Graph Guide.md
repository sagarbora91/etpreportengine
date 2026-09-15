---
type: guide
status: active
last_verified: 2026-09-14
---

# Knowledge Graph Guide

Open this repository's `knowledge` folder as an Obsidian vault. Use **Open graph view** to browse note relationships; the graph is derived from internal links and updates as notes change. No community plugin or cloud sync is required.

## Explore by question

- Start with [[ETP Knowledge Home]], [[AI-CONTEXT]] and [[AI-ROUTER]].
- Follow application structure through [[System Architecture]], [[Desktop Architecture]] and [[Data Architecture]].
- Trace business meaning through [[Data Dictionary]], [[Business Rules Register]], [[Import Architecture]], [[Mapping Knowledge]] and [[Report Catalog]].
- Review design authority in [[Decision Register]], especially [[ADR-004 - Obsidian for Project Knowledge]], [[ADR-003 - Graphify for Code Intelligence]] and [[ADR-009 - Canonical Focused Task Navigation]].
- Check retrieval quality with [[Knowledge Retrieval Evaluation]].

## Current work and evidence

The [14 September UI handoff](../../docs/audit/ETP-SESSION-HANDOFF-2026-09-14-UI-REDESIGN.md), [sprint ledger](../../docs/design/ETP-UI-REDESIGN-SPRINT-LEDGER.md) and [acceptance matrix](../../docs/audit/ETP-UI-REDESIGN-ACCEPTANCE.md) own execution status. They are outside this vault and are not internal graph nodes. Open those repository files in an editor when needed. Do not infer acceptance from graph connectivity or copy transient sprint status into architecture notes.

Graphify's [interactive code graph](../../graphify-out/graph.html) is a separate source-relationship artifact. Its code nodes are not duplicated in this vault. Uncommitted source changes need implementation review before they become accepted architecture or business rules.

## Maintaining the graph

Add meaningful internal links when an architecture, mapping, rule or decision changes. Keep source code and release evidence authoritative. Validate links with `scripts/Test-KnowledgeVault.ps1` from the repository root. Obsidian workspace/cache settings stay local under `.obsidian`; Markdown notes remain versioned.
