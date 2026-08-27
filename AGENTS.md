## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## Project knowledge retrieval

The repository-local Obsidian-compatible vault is `knowledge/`. It contains business intent, mappings, rules and decisions; it does not replace source code or Graphify.

Before a significant change:

1. Read `knowledge/AI-CONTEXT.md`.
2. Use `knowledge/AI-ROUTER.md` to classify the task and retrieve only the relevant 1–5 notes.
3. Use Graphify to identify the relevant implementation and relationships.
4. Inspect the actual source code and relevant tests before changing anything.
5. Make and verify the smallest appropriate change.
6. Update the vault only when architecture, a business rule, mapping, canonical schema, report definition, workflow, interface contract or major limitation meaningfully changes.
7. Record an accepted significant architecture decision as an ADR; do not create an ADR for routine implementation choices.

Authority and conflicts:

- Source code, migrations and executable configuration define current implementation.
- Graphify is the preferred code relationship/discovery layer, verified against source.
- Approved requirements and business-rule notes define business intention.
- Accepted ADRs define architecture unless superseded.
- Git/issues/release evidence define current work state.
- If documentation expects one behaviour and code implements another, report both and recommend a resolution; do not silently choose or rewrite history.

Do not load the full vault for every task, duplicate source code into notes, copy Graphify's graph into Markdown, or store secrets/PII/production rows in the vault.
