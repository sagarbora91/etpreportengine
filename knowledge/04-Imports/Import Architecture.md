---
type: module
status: implemented
module: import
last_verified: 2026-08-27
---

# Import Architecture

## Purpose

Convert known ETP workbook structures into typed, auditable SQL facts without silently accepting changed layouts or duplicate data.

## Current workflow

```text
File/folder/ZIP selected
→ safe-path and package discovery
→ file fingerprint and duplicate check
→ Open XML workbook read
→ preflight and actual-sheet scan
→ repeated-layout normalization when both halves match exactly
→ exact report/profile signature match
→ typed conversion and row staging
→ diagnostics and reconciliation
→ transactional SQL persistence
→ import audit/status and report availability
```

## Important contracts

- Do not trust worksheet `dimension`; real ETP files may declare `A1` despite populated data.
- Collapse repeated side-by-side layouts only when normalized headers and every row half are identical.
- Block unknown or mismatched schemas instead of guessing a mapping.
- Preserve source signs and workbook/sheet/row lineage.
- Exclude restricted PII from canonical reporting facts and logs.
- Folder/ZIP batches support progress, cancellation and retry while preserving atomic persistence boundaries.

## Profiles

Implemented production slices include R022, R025, stock ledger/closing stock and enrichment flows described in `docs/04_ETP_IMPORT_PROFILES.md`. Some header-only exports remain deferred until populated samples exist.

## Code entry points

- `ImportPreflight`, `WorkbookLayoutNormalizer`, `ImportProfileMatcher`
- `RetailSalesProfiles`, `StockImportProfiles`
- `BatchImportCoordinator`, `ImportRowStager`
- `R022SqlImportOrchestrator`, `R025SqlImportOrchestrator`, `StockImportOrchestrator`

Use Graphify to locate current callers and dependent reports before modifying these components. Related: [[Mapping Knowledge]], [[Data Architecture]], [[Business Rules Register]].
