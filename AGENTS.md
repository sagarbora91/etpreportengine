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
3. Use Graphify for broad architecture/community discovery and code-review-graph for precise AST callers, callees, test coverage and change-impact queries.
4. Inspect the actual source code and relevant tests before changing anything.
5. Make and verify the smallest appropriate change.
6. Update the vault only when architecture, a business rule, mapping, canonical schema, report definition, workflow, interface contract or major limitation meaningfully changes.
7. Record an accepted significant architecture decision as an ADR; do not create an ADR for routine implementation choices.

Authority and conflicts:

- Source code, migrations and executable configuration define current implementation.
- Graphify is the preferred broad relationship/discovery layer; code-review-graph complements it with a local SQLite AST index for precise review queries. Verify both against source.
- Approved requirements and business-rule notes define business intention.
- Accepted ADRs define architecture unless superseded.
- Git/issues/release evidence define current work state.
- If documentation expects one behaviour and code implements another, report both and recommend a resolution; do not silently choose or rewrite history.

Do not load the full vault for every task, duplicate source code into notes, copy Graphify's graph into Markdown, or store secrets/PII/production rows in the vault.

<!-- code-review-graph MCP tools -->
## MCP Tools: code-review-graph

**This project has complementary Graphify and code-review-graph indexes.** Use Graphify first for
broad architecture and community discovery. Use the code-review-graph MCP tools first for precise
AST-level callers, callees, dependents, test coverage and review impact, then read the source.

### When to use code-review-graph first

- **Exploring code**: `semantic_search_nodes_tool` or `query_graph_tool` instead of Grep
- **Understanding impact**: `get_impact_radius_tool` instead of manually tracing imports
- **Code review**: `detect_changes_tool` + `get_review_context_tool` instead of reading entire files
- **Finding relationships**: `query_graph_tool` with callers_of/callees_of/imports_of/tests_for
- **Architecture questions**: `get_architecture_overview_tool` + `list_communities_tool`

### Verify in the source

- Narrow scope with the graph, then read the source. Do not change code from graph output alone.
- For any non-trivial change, read the implementation and the relevant tests before concluding.
- Verify the exact source when touching behavior, database logic, migrations, retries, fallbacks,
  recovery, or compatibility code.
- When the graph and the source disagree, the source wins. The graph may be stale or may not
  model that relationship.
- An empty graph result can mean "not indexed" or "not statically visible", not "does not exist".

### Key Tools

| Tool | Use when |
| ------ | ---------- |
| `detect_changes_tool` | Reviewing code changes — gives risk-scored analysis |
| `get_review_context_tool` | Need source snippets for review — token-efficient |
| `get_impact_radius_tool` | Understanding blast radius of a change |
| `get_affected_flows_tool` | Finding which execution paths are impacted |
| `query_graph_tool` | Tracing callers, callees, imports, tests, dependencies |
| `semantic_search_nodes_tool` | Finding functions/classes by name or keyword |
| `get_architecture_overview_tool` | Understanding high-level codebase structure |
| `refactor_tool` | Planning renames, finding dead code |

### Workflow

1. The graph auto-updates on file changes (via hooks).
2. Use `detect_changes_tool` for code review.
3. Use `get_affected_flows_tool` to understand impact.
4. Use `query_graph_tool` pattern="tests_for" to check coverage.
<!-- /code-review-graph MCP tools -->
