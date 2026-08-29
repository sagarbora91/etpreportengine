---
type: decision-register
status: active
last_verified: 2026-08-29
---

# Decision Register

| ADR | Status | Decision |
|---|---|---|
| [[ADR-001 - SQL Server Express]] | accepted | SQL Server Express is the authoritative Windows reporting store. |
| [[ADR-002 - Deterministic Reporting Engine]] | accepted | Financial/reporting calculations remain deterministic and auditable. |
| [[ADR-003 - Graphify for Code Intelligence]] | accepted | Graphify is preferred for scoped code-relationship discovery. |
| [[ADR-004 - Obsidian for Project Knowledge]] | accepted | Repository Markdown/Obsidian stores durable semantic knowledge. |
| [[ADR-005 - Modular Desktop Shell]] | accepted | The WPF executable uses a thin shell and independently owned workspaces. |
| [[ADR-006 - Offline Device Licensing]] | accepted-deferred | Owner-authenticated offline issuance plus signed, machine-bound store activation; implementation waits until product completion. |
| [[ADR-007 - Exact Import Profile Identity and Provenance]] | accepted | Import acceptance and persistence carry one exact approved profile identity and matched-envelope provenance. |
| [[ADR-008 - Health-Gated Database Upgrade]] | accepted-implementation-external-validation | Existing-database migrations require a verified backup first and explicit health gates afterward; operational acceptance remains external. |

Business decisions still awaiting approval are tracked in `docs/11_DECISION_LOG.md`, not converted into architectural decisions.
