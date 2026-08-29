# Target Architecture

## Product boundary

ETP is an offline-first Windows reporting engine. Approved ETP workbooks are inputs, local SQL Server Express is the canonical reporting store after successful transactional import, deterministic services calculate reports and WPF/Excel/PDF are presentation channels.

The target is an incremental extension of the implemented .NET system, not a rewrite and not the retired Capacitor/WebView product.

## Status map

| Area | Target state | Current status |
|---|---|---|
| Layered .NET 10 solution | Domain/Application independent of WPF and SQL implementations | **Implemented** with manual composition. |
| Modular WPF desktop | Thin shell plus feature-owned workspaces | **Implemented**; combined-suite and installed-workflow evidence remain open. |
| Deterministic import acceptance | Exact approved profile identity carried into persistence | **Implemented** for approved v1 layouts. |
| Canonical SQL store | Checksum migrations, transactional imports, lineage and restatement | **Implemented in source**; live SQL acceptance remains open. |
| Reporting/export | Shared query/result models rendered to WPF, Excel and PDF | **Implemented/partial** by report; UAT remains open. |
| Safe installed upgrade | Verified pre-migration backup and post-migration health gate | **Implemented in source, external-blocked** for compiled/live lifecycle evidence. |
| New/v2 layouts | New approved profile identity based on representative source | **Source-blocked**; no populated sanitised v2 input or approved mapping exists. |
| Offline device licensing | Owner-issued signed, installation-bound licence | **Accepted-deferred**; no runtime enforcement exists. |
| AI assistance | Optional advisory tooling outside deterministic authority | **Deferred**; no production AI runtime exists. |

## Stable logical boundaries

1. **Desktop** — navigation, operator interaction, progress, diagnostics and result presentation.
2. **Application** — technology-neutral use-case contracts.
3. **Import** — file discovery, Open XML reading, structural preflight, profile matching, typed staging and diagnostics.
4. **Domain** — import identity, canonical types and deterministic business definitions.
5. **Infrastructure.SqlServer** — transactions, repositories, migrations, audit, automation and operational queries.
6. **Reporting** — report definitions, controls, result models and Excel/PDF rendering.

The current project structure is the target baseline:

```text
src/
  Etp.Reporting.Domain/
  Etp.Reporting.Application/
  Etp.Reporting.Import/
  Etp.Reporting.Infrastructure.SqlServer/
  Etp.Reporting.Reporting/
  Etp.Reporting.Desktop/
tests-dotnet/
  Etp.Reporting.Domain.Tests/
  Etp.Reporting.Application.Tests/       # only if application-only tests justify a project
  Etp.Reporting.Import.Tests/
  Etp.Reporting.SqlServer.Tests/
  Etp.Reporting.Reporting.Tests/
  Etp.Reporting.Desktop.Tests/
database/migrations/
```

A Generic Host or external DI container is not a target requirement by itself. Introduce one only if service lifetime, background-process or configuration complexity exceeds the explicit composition root; do not add infrastructure solely to match an earlier proposal.

## Import target

The accepted import boundary is an immutable matched envelope containing the workbook snapshot, actual matched sheet, exact approved profile, staged rows and diagnostics. Its profile identity is:

```text
report code + layout version + profile version + normalized-header SHA-256
```

The target import sequence is:

1. Read one stable workbook snapshot and calculate the source SHA-256.
2. Inspect actual sheet content rather than trusting worksheet dimension metadata.
3. Match exactly one active profile from the approved registry.
4. Stage typed values and collect bounded diagnostics.
5. Reject blocker-bearing input before persistence.
6. Pass the accepted envelope through Desktop/automation/Application boundaries unchanged.
7. Re-resolve its full profile identity at SQL persistence and register/compare it transactionally.
8. Persist canonical facts plus file/sheet/source-row lineage in one report-specific transaction.
9. Commit only after scope, reconciliation and duplicate/restatement controls pass.

Unknown or changed layouts stop. A new source version requires a new approved identity, mappings and representative-input tests; report code alone is never a compatibility promise. See ADR-007.

## Data and reporting target

Migrations remain the table/constraint authority. Application and reporting code must not invent compatibility behavior that the schema cannot preserve. Existing facts remain append/supersede oriented: correction uses controlled restatement and lineage rather than silent destructive replacement.

Each report retains an ID, parameter contract, query/result model, rule version and reconciliation state. SQL performs set-based filtering/aggregation; deterministic services own reusable calculations. WPF and exporters consume the same governed result. AI cannot supply a missing value, approve a mapping, alter a control status or replace a deterministic formula.

## Deployment and operations target

Normal operation remains local and Windows-integrated. Settings may store a validated integrated connection string without a SQL password; adding credential persistence would require a separate security decision.

Migration-bearing upgrades must follow ADR-008: compatible/healthy target, adequate backup space, verified existing-database backup, checksum migration run, online/read-write/journal/integrity verification, then operational-task setup. Failure stops and retains evidence; it does not pretend to reverse committed migrations. Restore is an explicit operator action after diagnosis.

The source-level implementation is not sufficient release evidence. Required external acceptance includes compiled installer behavior, clean install, previous-version upgrade, injected failure, preserved business data, real backup and restore, offline prerequisite flow and least-privilege/multi-user SQL UAT.

## Security, licensing and AI boundaries

- Windows-integrated SQL connections and role checks remain the active authentication/authorization boundary.
- Operational logs, diagnostics and support packages remain aggregate/privacy-safe; canonical reporting excludes restricted PII.
- Offline device licensing remains an accepted-deferred separate boundary under ADR-006. No document may describe it as enforced until source, packaging, key custody and two-machine acceptance exist.
- AI may later assist investigation or draft mappings only behind an explicit review seam. It remains non-authoritative and must not be required for offline operation.

## Deliberately deferred or excluded

- v2 input support without representative files and approved mappings.
- Generic drag-and-drop BI design.
- Cloud-required operation or centralized licensing SaaS.
- AI-dependent mapping, calculations or controls.
- Android/WebView compatibility for the active product.
- Automatic reverse migrations or destructive uninstall cleanup of business data.
- Release promotion based only on source or documentation completion.
