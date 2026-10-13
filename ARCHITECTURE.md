# ETP Reporting Engine Architecture

## Active product

The active product is an offline-first .NET 10 WPF reporting engine for Windows. Approved ETP workbooks are imported into local SQL Server Express, deterministic services produce governed reports and the same result models feed WPF, Excel and PDF outputs.

The Capacitor/WebView application under `www/` is legacy reference material. It is not the active reporting runtime, persistence authority, installer or target architecture.

Detailed status and direction live in:

- `docs/01_CURRENT_STATE_ARCHITECTURE.md`
- `docs/02_TARGET_ARCHITECTURE.md`
- `knowledge/01-Architecture/System Architecture.md`
- `knowledge/08-Decisions/Decision Register.md`

## Runtime shape

```text
approved XLSX / ZIP / folder source
  -> stable workbook snapshot and source SHA-256
  -> structural preflight and exact approved profile match
  -> typed staging and blocker diagnostics
  -> transactional SQL persistence with profile/file/row provenance
  -> canonical sales, tender, stock and controlled operational facts
  -> deterministic query/report services
  -> modular WPF workspaces and Excel/PDF exporters
```

The production layers are:

| Project | Boundary |
|---|---|
| `Etp.Reporting.Domain` | Canonical types and rules, including import-profile identity. |
| `Etp.Reporting.Application` | Use-case contracts. |
| `Etp.Reporting.Import` | Open XML reading, preflight, exact matching and typed staging. |
| `Etp.Reporting.Infrastructure.SqlServer` | SQL transactions, migrations, adapters, audit and operations. |
| `Etp.Reporting.Reporting` | Report composition, controls and Excel/PDF rendering. |
| `Etp.Reporting.Desktop` | WPF composition root, shell and feature-owned workspaces. |

`DesktopCompositionRoot` is the active composition mechanism. Generic Host and a general DI container are not currently used. Domain and Application remain independent of Desktop; SQL implementation stays behind application/import/reporting boundaries.

## Architectural invariants

- SQL Server becomes authoritative only after a successful transactional import.
- Import compatibility is exact. Identity is report code + layout version + profile version + normalized-header SHA-256.
- Only an accepted matched envelope may reach persistence; persistence revalidates the full identity.
- Every persisted source-derived fact retains file hash and available sheet/source-row lineage.
- Unknown or changed workbook layouts stop before canonical writes.
- Reporting calculations and controls are deterministic. AI is not an authority.
- Corrected data uses controlled restatement/supersession; business data is not silently deleted.
- `MainWindow` hosts shell behavior and composed workspaces; parsing, SQL and report formulas stay with their owning layers.
- Windows-integrated SQL connections avoid persisted SQL passwords in the normal configuration.
- Diagnostics, logs and support evidence remain privacy-safe and must not contain restricted source rows or credentials.

## Database and upgrade boundary

Versioned SQL scripts under `database/migrations/` are the schema authority. Applied migration IDs and checksums are recorded in `schema_migrations`; changed or missing applied scripts fail closed. Each migration is executed transactionally.

For an existing database with pending migrations, the implemented bootstrap source requires compatible SQL/database state, adequate disk space and a verified `COPY_ONLY, CHECKSUM` backup before migration. The backup receipt is checked for target, path, length and SHA-256. Setup reports success only after online/read-write state, exact migration journal count and `DBCC CHECKDB` pass. Failure retains the database and backup for diagnosis; there is no automatic reverse migration or database deletion.

This upgrade chain is implemented in source, not yet operationally accepted. Installer compilation, live clean/upgrade/failure testing and real backup/restore evidence remain external release gates.

## Current status boundaries

- **Implemented in source:** layered .NET/WPF product, modular workspaces, exact approved import identity, transactional SQL adapters/migrations, deterministic reports/exports, local diagnostics and upgrade hardening.
- **Partial:** approved v1 input coverage, operational automation, report-by-report/UAT coverage and installed multi-user workflows.
- **Source-blocked:** any v2 input until a populated sanitised changed-layout specimen and approved mapping exist.
- **External-blocked:** representative-workbook UAT, live SQL/restore evidence, compiled installer lifecycle, signing and release promotion.
- **Deferred:** signed offline device licensing enforcement and any non-authoritative AI-assistance seam.

Source or documentation completion alone is not a final test pass or release decision.

## 1.12 additions (wave 2)

<!-- CHECK AT INTEGRATION: written from the wave-2 plan before the lanes were merged; confirm each paragraph against the merged code. -->

**Database objects as files.** Views, procedures and functions live one file each in `database/objects` and are applied by setup when their text changes (migration 0062 holds the journal). Migrations hold tables, columns, constraints and grants for tables only; a migration never creates a view or procedure. Interactive start now calls the same planner (`DatabaseChangePlanner`) after the migration check; if any migration or object is pending, start stops with the existing configuration-rejected path and A3's message ("run setup again"). The cost is one journal read, not a text comparison.

**Startup wait.** `SqlStartupWait` retries the first connection of the headless verbs (automation run, nightly health, plan verb, backups) for up to 120 seconds with backoff, for transient SQL errors only. Permission and login errors are never retried. Each wait writes one line to the run log; on giving up the existing `STARTUP_FAILED` line is written. The interactive start does not wait; its failure text gains one sentence when the cause is transient.

**Stores and Service Centres as data (migration 0067).** `business_units.unit_kind` marks a Service business unit and `stores.service_job_prefix` holds its job-number prefix. Code asks `IServiceCentreCatalog` (an older database with neither column answers with the one legacy Service Centre) instead of naming a store. From 0067 on no migration inserts stores, brands or brand rows.

**No-sale day is a fact with a lock (migration 0068).** `dbo.no_sale_days` holds one row per store and business date (reason code, reason, who and when), with an append-only `no_sale_day_history`. It is written only by `mark_no_sale_day` and `withdraw_no_sale_day`, in one transaction with its history and audit rows. Marking is refused when the day is locked, in the future, or already holds sales or non-zero entries. Readiness reads the marks: for a marked store-day the export and daily-entry items are dropped, targets are not, and the result lists `NoSaleStoreCodes`. Finalise locks the day as usual, and from then on the lock protects the mark like every other fact (withdrawing needs the day reopened first). The Daily Sales Report shows "No sales (marked by ...)". `INoSaleDayService` is the one service; screens place its buttons through `NoSaleDayActions` and never build the dialog themselves.

**Import source locations (migration 0069).** `dbo.import_source_locations` remembers where each routed or hand-imported file came from, so a Fix button can re-apply a kept file after a reopen. Viewer has no access.

**Shell.** A screen registry and shell banner slot (wave 1) now carry the Morning page, pins and recents (`UiPreferences`), the update banner and the new task rows. Screens receive services; connection strings stay in the SQL layer (`ConnectionStringAllowlist` only shrinks).

**Disk copy.** `copy-etp-backup-to-disk.ps1` copies the verified backup to a second local disk; health check OPS-11 reads its receipt (`latest-disk-copy.json`) without SQL, like OPS-10.
