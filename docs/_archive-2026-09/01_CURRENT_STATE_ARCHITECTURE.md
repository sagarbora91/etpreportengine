# Current-State Architecture

## Scope and evidence

This document describes the active ETP Reporting Engine source in the current working tree as of 2026-08-29. Source, migrations, executable configuration and tests are the implementation authority. The legacy Capacitor/WebView application under `www/` is reference material and is not the active Windows product.

The status labels used here are intentionally strict:

- **Implemented** — present in source and covered by focused executable or structural tests.
- **Partial** — a working source slice exists, but its supported inputs or operational coverage are bounded.
- **External-blocked** — source is present, but validation needs an installed product, live SQL Server, real input, signing authority or business user.
- **Deferred** — accepted direction with no active runtime enforcement.

This is an architecture snapshot, not a release-completion claim. The current combined test run, installer compilation, clean-install/upgrade execution, live SQL migration/restore, representative-workbook UAT, signing and release promotion remain separate gates.

## Active runtime and layers

ETP is a .NET 10 Windows application with one WPF executable and six production projects:

| Layer | Current responsibility |
|---|---|
| `Etp.Reporting.Domain` | Canonical types, import-profile identity and deterministic business semantics. |
| `Etp.Reporting.Application` | Use-case contracts independent of WPF and SQL implementation. |
| `Etp.Reporting.Import` | Open XML reading, workbook preflight, exact profile matching, typed staging and diagnostics. |
| `Etp.Reporting.Infrastructure.SqlServer` | SQL Server adapters, transactional imports, migrations, operational queries, automation and audit. |
| `Etp.Reporting.Reporting` | Report models, deterministic composition, Open XML and PDF rendering. |
| `Etp.Reporting.Desktop` | WPF shell, composition root and independently owned workspaces. |

`DesktopCompositionRoot` performs explicit composition; the product does not currently use Generic Host or a general dependency-injection container. Lower-level projects do not reference Desktop. SQL adapters accept Windows-integrated connections rather than embedded SQL credentials.

## Implemented architecture

### Exact import identity and provenance

Known inputs are fail-closed. `ImportProfileIdentity` is the four-part identity `(report code, layout version, profile version, normalized-header SHA-256)`. `ApprovedImportProfileRegistry` is the in-process allowlist. `MatchedImportEnvelopeFactory` produces an accepted envelope only after workbook preflight, exact profile matching, staging and blocker-free diagnostics.

Desktop and automation persistence accept that envelope rather than an independently supplied sheet/profile pair. Every SQL import path re-resolves the identity against the approved registry. SQL registration compares the full identity, rejects inactive or version/signature-conflicting rows and links `import_files.import_profile_id` to the registered profile. Persisted facts retain file hash plus sheet and positive source-row lineage.

Implemented approved profiles are R003, R013, R022, R025, variant stock ledger and closing stock. A changed layout is a new profile identity; it is not accepted through report-code-only fallback.

See `knowledge/08-Decisions/ADR-007 - Exact Import Profile Identity and Provenance.md`.

### Transactional SQL authority

Local SQL Server Express is authoritative only after a successful transaction. Migrations in `database/migrations/` define the actual relational schema, import lineage, canonical sales/tender/stock facts, operational inputs, role controls, audit and productisation records.

The migration runner orders scripts, records SHA-256 checksums in `schema_migrations`, rejects missing or changed applied scripts and runs each pending script with a SQL transaction. Import stores validate approved profile identity and lineage before writes. Controlled restatement requires Owner authority and retains supersession history rather than silently replacing source history.

### Reporting and desktop

The WPF shell hosts separately owned Dashboard, Daily Workflow, Imports, Reports, Source Inbox, Archive, Registers, Accounting, Operations/Investigation, Administration, Settings and Help workspaces. `WorkspaceModuleOwnershipRegistry` is the route-to-owner authority. The shell delegates feature behavior through composed application/SQL services; parsing remains in Import and report calculation/rendering remains outside the shell.

Deterministic report/query implementations cover sales, invoice, returns, brand/segment/item, stock, staff, tender, cash, service, exception, management-trend and lineage views. Excel and PDF exporters exist, including governed daily-report and report-pack paths. Generated output still requires business comparison against approved examples before release acceptance.

### Local diagnostics and operational audit

Desktop startup/unhandled-failure diagnostics write privacy-safe local diagnostic records. Operational audit event types are constrained in application code and migration `0015_operational_audit_contract.sql`. Audit/logging remains operational rather than a centralized telemetry platform; no network telemetry service is required for normal use.

## Partial implementation

- Import behavior is complete only for the approved identities above. Header-only exports, altered layouts and any v2 workbook are unsupported until representative inputs and mapping approval exist.
- Folder/ZIP intake, cancellation, duplicate detection, restatement and automation paths exist, but representative-store workbook UAT has not been completed in this working tree.
- Windows role and permission boundaries exist in migrations/adapters; live multi-user SQL permission/UAT evidence is still required.
- Reporting/export implementations exist; pixel/layout acceptance and business-total comparison remain external gates.
- Local diagnostics and support tooling exist, but there is no centralized log aggregation or remote operations service.

## Upgrade and recovery safety

The installer/bootstrap source now implements a guarded local SQL Express upgrade path:

1. Validate packaged migrations, administrator context, SQL tooling, SQL Server 2022+, database state and compatibility.
2. For an existing database with pending bundled migrations, calculate a size-aware free-space floor.
3. Run a unique `COPY_ONLY, CHECKSUM` backup and `RESTORE VERIFYONLY`.
4. Publish a non-overwriting JSON receipt and independently recheck target identity, location, length and SHA-256.
5. Run the application migration mode.
6. Require online/read-write state, exact bundled journal count and `DBCC CHECKDB` before installing operational tasks or reporting success.

Failure does not invoke reverse SQL or delete a database. A verified backup is retained for a documented manual restore decision. Inno Setup raises an installer exception instead of terminating the process through a raw Win32 call. Offline/manual deployment remains possible by preinstalling SQL Server/Sqlcmd and using the bootstrap's skip-install option.

This safety chain is **implemented in source but external-blocked for operational acceptance**: the installer has not been compiled in this working tree, and no live clean install, 1.8.4-to-1.8.5 upgrade, injected migration failure, SQL restore or scheduled-task exercise has been performed. See `knowledge/08-Decisions/ADR-008 - Health-Gated Database Upgrade.md`.

## Deferred boundaries

- Runtime licensing is not implemented. ADR-006 defines an accepted-deferred offline, signed, machine-bound design.
- AI is not part of import acceptance, canonical mapping, calculations, controls or release decisions. No production AI runtime or licensing seam is active.
- v2 workbook support is not implemented and must not be inferred from v1 profiles.
- Generic drag-and-drop BI, cloud-required operation and Android/WebView compatibility are outside the active product boundary.

## External closure gates

Architecture remains subject to the closure ledger and release evidence. At minimum, closure requires a clean combined test result after integration, representative approved workbook UAT, live SQL permission and migration exercises, real backup/restore evidence, compiled installer lifecycle evidence, signing/verification where applicable and explicit release promotion. None is implied by this document.
