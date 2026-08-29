---
type: architecture
status: implemented-with-external-gates
module: data
last_verified: 2026-08-29
---

# Data Architecture

## Authority

`database/migrations/*.sql` is the exact table/column/constraint authority. `docs/03_DATABASE_SCHEMA.md` is a human-readable guide and must not override migrations. Migration `0015_operational_audit_contract.sql` is present in the working tree but, like every new migration, is not a claim that a live database has been upgraded.

## Implemented model

- Foundation masters: business units, stores, brands/categories and controlled transaction types.
- Import provenance: batches, files, source hashes, exact profile registration, errors/staging and sheet/source-row lineage.
- Transactional facts: invoice controls, sales lines, tenders and stock movements.
- Snapshot facts: dated closing-stock snapshots.
- Enrichment/restatement: R003/R013 matching plus controlled supersession/history.
- Operational inputs: manual inputs, physical counts, targets and service/cash controls.
- Reporting/productisation: rule versions, definitions, generations, finalisation, archive/distribution and audit history.

## Exact profile identity

An import profile is identified by report code, layout version, profile version and normalized-header SHA-256. The database uniqueness key is the version triple; SQL registration additionally compares the stored full identity and rejects a conflicting signature or inactive profile. `import_files.import_profile_id` records which exact registered profile governed the source.

Code also revalidates the identity against the approved in-process registry before persistence. A changed header layout therefore requires a new approved identity/version; database presence alone does not approve a profile. See [[ADR-007 - Exact Import Profile Identity and Provenance]].

## Lifecycle invariants

- A source-derived fact requires file provenance and valid sheet/source-row lineage.
- SQL becomes authoritative only after the report-specific transaction commits.
- Controlled restatement supersedes prior facts with reason/user/time; it does not silently overwrite history.
- Applied migration IDs/checksums are immutable; changed or missing applied scripts fail closed.
- An existing database with pending bundled migrations requires a verified backup before migration and health verification afterward. Restore remains a deliberate operator action. See [[ADR-008 - Health-Gated Database Upgrade]].

## Privacy and security

Customer name, contact number, card number and similar restricted PII are not canonical reporting dimensions. Logs, diagnostics, receipts and support packages must remain row-safe and credential-safe. Normal SQL configuration uses Windows-integrated authentication; live least-privilege role behavior remains an external acceptance gate.

## Status

- **Implemented in source:** schema/migrations, checksum journal, profile/file/row provenance, transactional persistence and controlled restatement.
- **Partial:** coverage is bounded to approved v1 profiles and implemented reporting/operational slices.
- **External-blocked:** live migration, multi-user permission, backup/restore and representative-workbook UAT evidence.
- **Deferred:** v2 schema/profile work until representative input and mapping approval exist.

Related: [[System Architecture]], [[Import Architecture]], [[Mapping Knowledge]], [[ADR-001 - SQL Server Express]].
