---
type: architecture
status: implemented
module: system
last_verified: 2026-08-27
---

# System Architecture

## Implemented flow

```text
ETP XLSX / folder / ZIP
  → workbook preflight and repeated-layout detection
  → exact import-profile matching
  → typed conversion, staging and diagnostics
  → SQL transaction, lineage and reconciliation
  → canonical sales / invoice / tender / stock facts
  → reporting query and deterministic calculations
  → WPF preview / Excel / PDF
  → immutable generation, finalisation and audit
```

## Active solution layers

| Layer | Responsibility | Location |
|---|---|---|
| Domain | Canonical contracts, money/quantity, periods | `src/Etp.Reporting.Domain` |
| Application | Use-case contracts | `src/Etp.Reporting.Application` |
| Import | Open XML reading, preflight, profiles, conversion, staging | `src/Etp.Reporting.Import` |
| SQL infrastructure | Migrations, repositories, import orchestration, health/operations | `src/Etp.Reporting.Infrastructure.SqlServer` |
| Reporting | Definitions, formulas, reconciliation, Excel/PDF generation | `src/Etp.Reporting.Reporting` |
| Desktop | WPF shell, modules, focused report workspaces and Help | `src/Etp.Reporting.Desktop` |

The accepted Desktop direction is a thin shell with independently owned workspaces; see [[Desktop Architecture]] and [[ADR-005 - Modular Desktop Shell]].

The accepted but deferred security direction uses owner-authenticated offline licence issuance and Windows-installation binding; see [[ETP Licensing Architecture]] and [[ADR-006 - Offline Device Licensing]]. Runtime licensing is not yet implemented.

## Boundaries

- SQL Server Express is authoritative only after a complete transactional import.
- Input files and every persisted fact retain lineage sufficient for diagnosis.
- Reporting logic is deterministic; AI may assist analysis but cannot replace approved calculations.
- Legacy `www/` and Capacitor code is not the active Windows persistence/reporting host.

## Existing detailed authorities

- `docs/02_TARGET_ARCHITECTURE.md`
- `docs/21_DAILY_REPORTING_IMPLEMENTATION_MAP.md`
- `docs/24_PHASE2_OPERATIONS.md`
- `docs/VISUAL_REPORTING_ARCHITECTURE.md`
- [[ADR-001 - SQL Server Express]]
- [[ADR-002 - Deterministic Reporting Engine]]
- [[ADR-005 - Modular Desktop Shell]]
