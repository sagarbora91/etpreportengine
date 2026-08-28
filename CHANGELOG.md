# Changelog

## Unreleased

## [1.8.3] - 2026-08-28

### Changed

- Split the Windows experience into focused workspaces for Settings, Daily Workflow, Archive, Registers, Accounting, Operations, Investigations and Approvals, Administration, Imports, Source Inbox, Reports, Dashboard and Help, while retaining the existing access rules and workflows.
- Production-verified the Daily Sales Report PDF as a single A4 landscape page with the correct business weekday, FTD/MTD/YTD and TY/LY comparisons, value and quantity measures, Service and targets, plus explicit wording when LY MTD data is unavailable.

### Fixed

- Hardened Windows database settings and lifecycle actions to accept only validated Windows Integrated Security connections, persist settings atomically and reject unsafe filesystem links.
- Restored the automatic-backup runtime source used by the packaged app, including encrypted app-private backup scheduling, safe legacy cleanup and verified off-device delivery coverage.
- Made release packaging fail closed on restore, build, test or publish errors, and added an isolated per-user installer lifecycle path with retained failure diagnostics.

## [1.8.2] - 2026-08-27

### Added

- Added a dedicated Daily Sales Report workspace with a fixed business-date/action toolbar, internally scrolling preview, availability indicators and direct PDF, Excel, report-pack, export-folder and Manual Entry actions.
- Added grouped focused workspaces covering all 29 production reports across Sales, Stock, Tender/Cash/Service, Staff, Exceptions, Management and Investigation.
- Added a searchable, tile-based Help Centre with 19 application-area topics, context-sensitive `F1` help and a complete searchable Keyboard Shortcuts guide.
- Added Windows-style back/forward/home navigation and governed report, export, search, save, import and focus shortcuts.

### Changed

- Moved Comfortable/Compact display density from the bottom status bar into the contextual sidebar and retained the persisted preference.
- Kept workspace filters and primary actions fixed while report previews and result grids scroll internally.

### Fixed

- Prevented Help Centre controls from being assigned to multiple WPF logical parents when the Help home is reopened.

## [1.8.1] - 2026-08-27

### Added

- Added the native WPF UI/UX v4 shell with a touch-first global rail, contextual module navigation, responsive detail drawer and persisted comfortable/compact density.
- Added role-aware module and route registries, including the six-card daily workspace, optional Owner modules, and direct navigation to every production report.
- Added reusable visual resources and controls for colours, typography, spacing, icons, cards, status badges and empty/loading states.
- Added UI navigation contract, design-system and implementation-map documentation plus automated navigation and rendered-shell smoke coverage.
- Added a database-driven Manual Entry workspace for walk-ins and future approved non-ETP fields, with role checks, validation, reasons and audit history.

### Fixed

- Prevented a previously selected generic report from being exported while the governed DSR is still loading, and made missing DSR inputs explicit in the one-page PDF.
- Fixed DSR screen construction so each operational-metric control has exactly one WPF logical parent.

## 1.7.0 - 2026-08-26

### Added

- Added a visible category-based Reports Centre with 29 named operational report entries.
- Added closing-stock, stock-movement, brand-stock, slow/exception-stock and printable management-trend reports.
- Added focused missing-source, unmapped-data, tender, stock and staff exception reports.

### Changed

- Exposed staff targets, achievement, ranking, LY comparison and contribution through clearly named report actions while retaining the existing reconciliation control.
- Preserved canonical `NETVALUE`, source-signed sales returns and revenue-report tender controls across every new view.

## 1.6.0 - 2026-08-26

### Added

- Added product navigation, a Home business-day cockpit, Source Inbox, digital Registers, Accounting, global investigation and Approval Centre surfaces.
- Added immutable document storage, native PDF text detection, an optional isolated PaddleOCR helper boundary and a human verify/reject queue.
- Added row-level overlapping-period handling that distinguishes new, already-present and conflicting business facts without overwriting canonical history.
- Added generation-bound ZIP packages with hashed manifests, safe WhatsApp initiation, attached email drafts and an audited sharing address book.
- Added a controlled KPI catalogue, accounting mappings, balanced batches and one-way Tally XML export.

### Changed

- Expanded unattended intake to supported PDFs/images and prevented automatic report-pack generation for dates containing unresolved import conflicts.

## 1.5.0 - 2026-08-26

### Added

- Added Windows-integrated Owner, Store Manager and Viewer roles with audited user administration and protection against removing the last active Owner.
- Added an automatic local watch-folder pipeline for XLSX/ZIP imports, duplicate skipping, processed/failed quarantine, five-minute task execution and automatic combined Excel/PDF packs.
- Added morning and evening report schedules, execution history, and installer-managed task registration/removal.
- Added a SHA-256 verified historical report archive with combined/store generation browsing, comparison and Excel/PDF re-export.
- Added management sales/control trends, a data-quality control centre, controlled Store/Brand Segment/Inventory Group/Tender masters, and in-app backup, recovery-drill and privacy-safe support actions.
- Added full SQL Server and production-corpus acceptance coverage for the Phase 2 migration, roles, masters, archive, analytics, unattended duplicate handling, backup and isolated restore.

### Changed

- The bootstrap now installs daily backup, monthly recovery-drill and five-minute automated-operations tasks; uninstall removes only those tasks and deliberately retains all databases, backups, sources and reports.

## 1.4.1 - 2026-08-26

### Added

- Added explicit atomic source restatement with prior-fact archival, replacement lineage, reason/user metadata and rollback safety.
- Added detailed inventory-group physical counts, independent composition/system variances, and dated staff/CRO targets with LY growth, achievement, ranking and contribution.
- Added workbook/sheet/row invoice drill-down and a traceable daily exception report covering source, tender, staff, cash and physical-stock findings.
- Added complete selected-store and combined Titan + Helios report packs as multi-sheet Excel and paginated PDF.
- Added immutable numbered report generations with SHA-256 control snapshots, plus finalisation linkage and stronger locked-day guards.
- Added missing/unexpected-column diagnostics and expanded privacy-safe audit coverage for sessions, configuration, mappings, restatements, backup and restore drills.

## 1.4.0 - 2026-08-26

- Added an operational business-date workflow with source completeness, controlled manual inputs, daily finalisation and administrator reopen auditing.
- Added import metadata for report code, store, ETP business date, source report date and importing user while retaining a separate import timestamp.
- Added centrally tested Indian financial-year FTD/MTD/YTD/LY period resolution and safe growth/productivity/conversion calculations.
- Added an executable report-to-source registry covering sales summaries, DSR, service, tender/cash, closing stock and staff reporting without guessing unresolved definitions.
- Added database guards that block new imports and manual-input changes against finalised business dates.
- Added a Store Manager Daily Workflow screen and golden business-rule tests.
- Ported and real-corpus verified R003 discount and R013 CRO profiles with atomic, import-order-independent enrichment matching that cannot change canonical revenue.
- Added customer-safe invoice summaries, FTD/MTD/YTD/LY DSR, staff/CRO performance with exact variance diagnostics, controlled service tender reporting, cash-drawer reconciliation and a one-action daily reporting pack.

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and semantic versioning.

## [Unreleased]

## [1.8.0] - 2026-08-27

### Added

- Added a renderer-independent visual report model, report registry, reusable KPI/visual definitions and central Indian-number formatting.
- Added accessible KPI cards and purpose-selected ranking, comparison and trend visuals to the native Reports Centre.
- Added five-sheet analytical Excel workbooks with deterministic support ranges and native Excel charts.
- Added vector PDF management summaries, SVG chart rendering, paginated detail and prominent control status.
- Added golden reconciliation tests for Daily Sales, combined sales and stock, plus large-data performance validation.
- Added the finalized connected Daily Sales Report with reusable WPF cards and an exact one-page A4 landscape Unicode PDF export.
- Added safe DSR formula, missing-source, weekday, Indian-formatting and approved-sample verification coverage.

### Changed

- Selected-report Excel and PDF actions now consume the same visual report model as the on-screen preview while retaining the complete detail table and lineage workflow.

## [1.3.1] - 2026-08-26

### Fixed

- Made monthly recovery-task registration reliable for installed paths containing spaces and made bootstrap failures return a nonzero installer result with a local diagnostic entry.

## [1.3.0] - 2026-08-26

### Added

- Administrator bootstrap installation for SQL Server Express detection/installation, automatic service configuration, database migration, backup access, and scheduled operational tasks.

## [1.2.1] - 2026-08-26

### Changed

- Adopted indefinite backup retention with no automated business-data deletion, two-year operational-audit retention, monthly restore drills, and the approved Owner/Store Manager authority policy.
- Added live backup-destination free-space monitoring with 20 GB warning and 5 GB critical thresholds.

## [1.2.0] - 2026-08-26

### Added

- Privacy-safe operational audit history for application, connection, import, report and export activity.
- Synthetic performance gates covering large sales, stock and tender workloads.
- Database integrity, statistics and audit-retention maintenance automation.
- Offline deployment packaging and a backup-first application rollback workflow.
- Troubleshooting, incident-response and release runbook documentation.

## [1.1.0] - 2026-08-26

### Added

- Operational hardening, expanded import/report workflows, health monitoring, and release-quality automation.

## [1.0.0] - 2026-08-26

### Added

- First verified Windows release with SQL Server imports, sales, tender and stock reports, Excel/PDF export, backup tooling, and installer.

