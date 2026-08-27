---
type: adr
status: accepted
date: 2026-08-27
last_verified: 2026-08-27
---

# ADR-002 - Deterministic Reporting Engine

## Context

Sales, tender, stock, tax-period and management calculations must be reproducible, reconcilable and explainable.

## Decision

Implement core calculations as versioned code/configuration and SQL-backed rules with explicit inputs, safe denominator handling, source lineage and automated tests. AI may assist interpretation or mapping proposals but is not a calculation authority.

## Reason

The same facts must produce the same results and support audit, restatement and diagnosis.

## Alternatives considered

Opaque LLM-generated calculations were rejected for financial/reporting controls. Spreadsheet formulas are not used as the production calculation authority.

## Consequences

Rule changes require approved meaning, impact discovery, focused tests and report-definition updates. Unknown data remains visible rather than guessed.

## Affected components

Domain periods, reporting services, reconciliation, report exporters, rule versions and tests.
