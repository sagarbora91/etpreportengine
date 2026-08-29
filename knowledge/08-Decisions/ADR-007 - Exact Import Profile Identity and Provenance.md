---
type: adr
status: accepted
date: 2026-08-29
last_verified: 2026-08-29
---

# ADR-007 - Exact Import Profile Identity and Provenance

## Context

Report code alone cannot prove that a workbook layout, mapping and persisted projection belong together. Passing a matched profile separately from a workbook sheet also allows callers to detach validation from persistence.

## Decision

Identify an import profile by report code, layout version, profile version and normalized-header SHA-256. Accept input only as a blocker-free `MatchedImportEnvelope` that carries the stable workbook snapshot, actual matched sheet, approved profile, staged rows and diagnostics.

Every persistence path re-resolves that full identity against the approved code registry. SQL registration locks/compares the stored version and signature, rejects inactive/conflicting definitions and links the import file to the registered profile. Source file hash and sheet/source-row lineage remain attached to persisted facts.

## Consequences

- Unknown or changed layouts fail before canonical writes.
- A new layout requires a new approved identity/version and representative-input tests; report-code fallback is forbidden.
- Desktop, automation and SQL orchestrators share one accepted import boundary.
- v2 support remains source-blocked until populated sanitised inputs and mappings are approved.

## Affected components

Domain import profiles, Import preflight/registry/envelope, Desktop and automation coordinators, SQL import persistence/profile registration, import migrations and focused tests.
