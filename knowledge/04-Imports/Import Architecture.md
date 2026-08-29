---
type: module
status: implemented-for-approved-profiles
module: import
last_verified: 2026-08-29
---

# Import Architecture

## Purpose

Convert only known, approved ETP workbook structures into typed, auditable SQL facts without guessing changed layouts, detaching a profile from its matched workbook or losing source provenance.

## Current workflow

```text
file / folder / ZIP selected
→ safe discovery and stable workbook snapshot
→ source SHA-256 and duplicate/restatement checks
→ Open XML actual-sheet scan and structural preflight
→ repeated-layout normalization only when both halves match exactly
→ exact match against ApprovedImportProfileRegistry
→ typed staging and bounded diagnostics
→ blocker-free MatchedImportEnvelope
→ application persistence request carrying that envelope
→ registry revalidation and transactional SQL profile registration
→ canonical facts plus file/sheet/source-row lineage
→ import completion/audit and report availability
```

## Exact identity and accepted envelope

`ImportProfileIdentity` is `(ReportCode, LayoutVersion, ProfileVersion, HeaderSignatureSha256)`. The header signature is calculated from normalized actual headers. `MatchedImportEnvelope` binds together the workbook snapshot, actual matched sheet, approved profile, staged rows and diagnostics.

Desktop and automation use that accepted envelope. SQL orchestrators re-resolve its full identity against `ApprovedImportProfileRegistry`; `SqlServerImportProfileResolver` locks/registers the profile version and rejects inactive or signature-conflicting stored definitions. Report code alone never authorizes persistence.

This boundary prevents a caller from matching one profile but persisting a separately reconstructed sheet/projection. See [[ADR-007 - Exact Import Profile Identity and Provenance]].

## Implemented profiles

- R003 enrichment
- R013 enrichment
- R022 invoice/tender control
- R025 sales lines
- variant stock ledger
- closing stock

All currently use layout version `ETP_2026_08` and profile version `1` with profile-specific header signatures. These are approved v1 identities, not a claim of v2 compatibility.

## Invariants

- Do not trust worksheet `dimension`; inspect actual populated cells.
- Collapse repeated layouts only when normalized headers and row halves match exactly.
- Block unknown, ambiguous or mismatched schemas before canonical writes.
- Preserve source signs and workbook/sheet/positive-row lineage.
- Exclude restricted PII from canonical facts, diagnostics and logs.
- Enforce expected store/business-date scope and controlled duplicate/restatement behavior.
- Keep mapping/profile approval deterministic and human-controlled; AI cannot approve or silently repair a layout.

## Status

- **Implemented in source:** exact registry/matcher/envelope flow and report-specific persistence for the listed profiles.
- **Partial:** folder/ZIP, cancellation, automation and restatement flows require combined and representative-source validation after integration.
- **Source-blocked:** real v2 inputs and header-only source variants without populated samples.
- **External-blocked:** representative workbook UAT and live SQL persistence evidence.
- **Deferred:** any new profile until source samples, mappings, identity/version and approval tests exist.

## Code entry points

- `ImportProfileIdentity`, `ApprovedImportProfileRegistry`, `ImportProfileMatcher`
- `MatchedImportEnvelopeFactory`, `ImportPreflight`, `ImportRowStager`
- `DesktopImportCoordinator`, `AutomatedOperationsService`
- `SqlServerImportPersistenceUseCase`, `SqlServerImportProfileResolver`
- `R022SqlImportOrchestrator`, `R025SqlImportOrchestrator`, `RetailEnrichmentSqlImportOrchestrator`, `StockSqlImportOrchestrator`

Related: [[Mapping Knowledge]], [[Data Architecture]], [[Business Rules Register]], [[Decision Register]].
