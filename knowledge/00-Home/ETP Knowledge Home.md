---
type: home
status: active
last_verified: 2026-08-27
---

# ETP Knowledge Home

This vault stores the business meaning, decisions, mappings and architectural context of the ETP Reporting Engine. Graphify remains responsible for code structure and relationships; source code and migrations remain authoritative for current implementation.

## Start here

- AI agents: [[AI-CONTEXT]] → [[AI-ROUTER]]
- Architecture: [[System Architecture]], [[Data Architecture]], [[Desktop Architecture]] and [[ETP Licensing Architecture]]
- Business meaning: [[Data Dictionary]] and [[Business Rules Register]]
- Data intake: [[Import Architecture]] and [[Mapping Knowledge]]
- Outputs: [[Report Catalog]]
- Decisions: [[Decision Register]]
- Measurement: [[Knowledge Retrieval Evaluation]]

## Current product

The active Windows application uses .NET 10, WPF and SQL Server Express. Its logical flow is:

ETP files → preflight/profile matching → normalization/staging → transactional SQL persistence → deterministic reporting → WPF/Excel/PDF → generation archive and audit.

The repository also contains legacy Android/JavaScript material. It is reference evidence, not the active Windows reporting architecture unless explicitly stated.

## Major modules

- Domain contracts and reporting periods
- XLSX/ZIP import and source-profile recognition
- SQL Server persistence, migration and operational health
- Reporting calculations, reconciliation and exports
- WPF desktop workflows and focused report workspaces
- Installer, backup, restore and release automation

## Current open business items

- Complete transaction classifications beyond confirmed `INV` and `SR`.
- Approve remaining stock movement signs and physical-stock composition policy.
- Approve unresolved tender codes including `PAYMENTTYPE25`/TC.
- Confirm DSR versus staff transaction denominators.
- Supply reliable LY MTD, walk-ins, service or target data where currently unavailable.

The detailed authoritative list remains in `docs/11_DECISION_LOG.md` and `docs/21_DAILY_REPORTING_IMPLEMENTATION_MAP.md`.
