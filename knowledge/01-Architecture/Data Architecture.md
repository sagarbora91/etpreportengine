---
type: architecture
status: implemented
module: data
last_verified: 2026-08-27
---

# Data Architecture

## Implemented concepts

- Foundation masters: stores, business units, brands/categories and controlled transaction types.
- Import lineage: profiles, profile fields, batches, files, errors and staged rows.
- Transactional facts: sales transactions/lines/tenders and stock movements.
- Snapshot facts: dated closing-stock snapshots.
- Operational inputs: versioned manual inputs, physical counts, targets and service/cash controls.
- Reporting controls: rule versions, definitions, generations, finalisation and audit history.

The exact table/column authority is `database/migrations/*.sql`; the human-readable schema guide is `docs/03_DATABASE_SCHEMA.md`. Do not invent a table from this summary.

## Data lifecycle

Source metadata and row lineage are retained through import. Successful validation persists canonical facts transactionally. Restatement archives replaced facts and records reason/user/time. Reporting queries canonical facts plus approved controlled inputs. Final report generations store control evidence and hashes.

## Status distinctions

- **Implemented:** entities and constraints present in migrations and used by repositories.
- **Proposed:** described in target architecture but absent from migrations/code.
- **Inferred — requires confirmation:** behaviour suggested by code or samples without an approved business definition.

## Privacy

Customer name, contact number, card number and similar PII are not canonical reporting dimensions. Logs and support packages must remain row-safe and privacy-safe.

Related: [[Import Architecture]], [[Mapping Knowledge]], [[ADR-001 - SQL Server Express]].
