---
type: architecture
status: implemented-with-external-gates
module: system
last_verified: 2026-08-29
---

# System Architecture

## Active product flow

```text
approved ETP XLSX / folder / ZIP
  → stable workbook snapshot and source hash
  → actual-sheet preflight and exact approved profile identity
  → typed staging and blocker diagnostics
  → transactional SQL persistence with profile/file/row provenance
  → canonical sales / invoice / tender / stock facts
  → deterministic reporting queries and controls
  → modular WPF workspaces / Excel / PDF
  → finalisation, archive, distribution and operational audit
```

The active runtime is the .NET 10/WPF solution. Legacy `www/` and Capacitor sources are reference material, not the Windows host or persistence authority.

## Layer authority

| Layer | Responsibility | Location |
|---|---|---|
| Domain | Canonical contracts, import identity and deterministic rules | `src/Etp.Reporting.Domain` |
| Application | Technology-neutral use-case contracts | `src/Etp.Reporting.Application` |
| Import | Open XML, preflight, approved profiles, staging and diagnostics | `src/Etp.Reporting.Import` |
| SQL infrastructure | Migrations, transactional persistence, health, operations and audit | `src/Etp.Reporting.Infrastructure.SqlServer` |
| Reporting | Definitions, controls and Excel/PDF rendering | `src/Etp.Reporting.Reporting` |
| Desktop | Explicit composition, thin shell and owned workspaces | `src/Etp.Reporting.Desktop` |

`DesktopCompositionRoot` is the current composition boundary; Generic Host/general DI is not implemented or required by itself.

## Implemented invariants

- SQL Server Express is authoritative only after a completed transactional import.
- Exact profile identity and matched-envelope provenance are required; see [[ADR-007 - Exact Import Profile Identity and Provenance]].
- Reporting and controls are deterministic; AI is not an authority.
- Windows-integrated SQL connections and application/SQL role checks form the active security boundary.
- Existing-database migrations are source-hardened by verified backup and post-migration health gating; see [[ADR-008 - Health-Gated Database Upgrade]].
- Failure retains diagnosis/recovery evidence and never claims an automatic reverse migration.

## Status boundaries

- **Implemented in source:** layered WPF/.NET product, approved-profile imports, SQL schema/adapters, reports/exports, modular Desktop, diagnostics and upgrade hardening.
- **Partial:** approved v1 layouts only, report/workflow UAT coverage and installed operational coverage.
- **Source-blocked:** v2 input until a populated sanitised changed-layout specimen and approved mapping exist.
- **External-blocked:** representative workbook UAT, live SQL permissions/migrations/restores, compiled installer lifecycle, signing and release promotion.
- **Deferred:** runtime offline-device licensing and any optional advisory AI seam. See [[ADR-006 - Offline Device Licensing]].

No architecture note is final test or release evidence. Use `docs/PROJECT_CLOSURE_TRACEABILITY.md` for closure status.

## Detailed authorities

- `docs/01_CURRENT_STATE_ARCHITECTURE.md`
- `docs/02_TARGET_ARCHITECTURE.md`
- `docs/21_DAILY_REPORTING_IMPLEMENTATION_MAP.md`
- `docs/24_PHASE2_OPERATIONS.md`
- [[Data Architecture]]
- [[Import Architecture]]
- [[Desktop Architecture]]
- [[Decision Register]]
