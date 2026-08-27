---
type: evaluation
status: active
module: ai-workflow
last_verified: 2026-08-27
---

# Knowledge Retrieval Evaluation

## Purpose

Measure whether Obsidian-style knowledge plus Graphify reduces discovery and rework. Do not claim a guaranteed token-saving percentage without measurements.

## Comparison method

For representative report, mapping, bug, database and new-report tasks, compare:

- Workflow A: ordinary repository discovery without the router.
- Workflow B: [[AI-CONTEXT]] → [[AI-ROUTER]] → 1–5 notes → Graphify → exact source/tests.

Record:

| Metric | Workflow A | Workflow B |
|---|---:|---:|
| Files inspected | | |
| Knowledge notes retrieved | | |
| Graphify queries | | |
| Approximate context/tokens | | |
| Incorrect assumptions | | |
| Implementation iterations | | |
| Rework/defects | | |
| Relevant tests identified before editing | | |
| Outcome quality | | |

## Initial real-task trace: DSR investigation

1. Requirement: DSR content and safe missing-data behaviour — [[Report Catalog]].
2. Rules: `BR-SALES-001`, `BR-SALES-002`, `BR-CALC-001`, `BR-DATA-001`, `BR-DSR-001` — [[Business Rules Register]].
3. Sources/mappings: R025, R022 and controlled walk-ins — [[Mapping Knowledge]].
4. Graphify discovery found `DailySalesReportDocument`, `DailySalesReportPdfExporter`, `DailySalesReportTests`, SQL reporting and WPF workspace nodes.
5. Source verification targets only those files plus their direct tests.
6. Affected outputs: DSR preview, PDF, daily pack and management reporting.

This demonstrates complete traceability for a real feature; future work should collect comparative numeric measurements before drawing savings conclusions.
