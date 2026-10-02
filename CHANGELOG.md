# Changelog

## Unreleased

Phase 7 (Tally transfer) groundwork, and help for the Phase 5 acceptance run at the shop:

- Migrations 0038 and 0039 add the Tally transfer tables: Tally companies and their stores, batch and voucher history, evidence files, validation findings, read-backs, reconciliation results and recovery plans. Only the Owner can see them. Existing accounting batches keep their five statuses.
- New Settings screen "Tally companies" (Settings > Integrations). The Owner can add a test Tally company, link stores to it and record why it changed. A Tally address must be on this PC. Changing a company to live books records the intent only; nothing enables live posting yet.
- Evidence files for a Tally batch are written once into the batch's own folder, registered with their checksum, and can be re-checked later (OK, changed or missing). Linked folders are refused.
- Validation, reconciliation and recovery rules for a batch: ETP checks what it would send, reads a Tally Day Book export saved by hand, compares the two voucher by voucher, and writes a recovery plan and a manifest. Differences can be accepted only by the Owner, with a reason.
- New `docs/audit/PHASE-7-DECISION-SHEET-D12-D18.md` for the Owner to tick the open Tally decisions, and a new section "When Tally and ETP disagree" in `docs/OPERATIONS.md`.
- New `docs/audit/PHASE-5-ACCEPTANCE-WALKTHROUGH.md`: a printable A5.1/A5.2 checklist that goes through every screen as Owner, Store Manager and Viewer.
- Fixes from a review of the Tally code, with migration 0040: a Day Book file for the wrong day no longer marks vouchers as missing; a file left behind by a crash no longer blocks its name; a Tally company's short code, name and books cannot change once batches use it; accepting a failure gives the right message; a blocked voucher of an approved batch stays blocked; saving on the Tally companies screen keeps the posting dates; invoice-view vouchers are read with their sales lines.
- Setup and the operations scripts now find Sqlcmd when SQL Server was installed on another drive. They used to look only in `C:\Program Files`, so on Workpc (installed to `E:\Program Files`) setup stopped with "Sqlcmd was installed but was not found". Sqlcmd is now also looked for where the SQL Server client installers record it in the registry, and in the Program Files folder of the drive ETP is installed on. It must still be in a folder only Administrators or SYSTEM can change; one that is not is skipped, and if none qualifies setup now says why instead of "not found". The ODBC Driver 17 Sqlcmd still comes first wherever it is found, and only it counts as the bundled Sqlcmd being installed (1.9.2).
- Setup on a second drive: the protected-folder check no longer refuses a Windows-formatted data drive (Authenticated Users' Modify on `E:\`, which cannot rename a drive root) or ALL APPLICATION PACKAGES' Full Control on `E:\Program Files` (it only ever narrows what an app may do). Everything that could replace or rename the installation folder or a folder above it is still refused, including a folder owned by a user. The refusal now lists every folder at fault, who has which right, and the exact `icacls` command to fix it. The same rule applies in the application. Found on Workpc on 2 Oct 2026, where `E:\` had to be changed by hand.
- 1.9.3, new-PC fixes found on Workpc (2 Oct 2026), with migration 0042 (numbered after the import engine's 0041):
  - Settings > Users can now deactivate an account of a PC that no longer exists (for example the old laptop's `TFRROWJLTULT009\Sagar`). Deactivating no longer creates a SQL login; the account's database roles are dropped, CONNECT is denied and the change is in the user history as before. Giving access to an account Windows cannot find is refused with a message that says it can only be deactivated. The last active Owner can no longer be deactivated or demoted through the procedure either (it already could not through the table).
  - Setup now finishes the operations module for the automation account itself: when `<PC>\EtpAutomation` is an active Store Manager without the backup rights, setup runs `install-etp-sql-operations.ps1` for it; when it is not a Store Manager yet, the setup log ends with a NEXT STEP line saying what to do. The restore helper's next steps and `docs/INSTALL.md` / `docs/OPERATIONS.md` follow the new order (restore, setup, add the account, setup again).
  - The daily backup and the recovery drill's recording no longer fail with only "The database operation failed" when the automation account lacks its rights: they name the missing right (for example the etp_automation role) and give the exact command. Any other failure keeps its message.
  - Settings > Users shows a clear message for the last-Owner refusal and for an account Windows cannot find.
- Setup now creates the automatic-import folders the database points at by default (`Inbound`, `Processed` with `Processed\Duplicate`, `Failed` and `ReportPacks` under `%ProgramData%\EtpReporting`), with the same protection as `Backups` and the other ETP folders. Without them the automation account, which may only read the parent folder, failed every 5-minute run with access denied (Workpc, 2 Oct 2026, fixed there by hand). Setup grants no named user: on Workpc, running 1.9.3 setup replaces the hand fix's Modify right for `WORKPC\Sagar` on these folders.
- Upgrade safety (1.9.3): before applying anything, ETP now runs the pre-checks of every pending migration (0041's 51700 financial year, 51701 duplicate revenue controls, 51702 duplicate tenders). Each migration commits on its own, so until now a refusal in 0041 came after Tally's 0038-0040 had committed, and the shop database was left at 0040: 1.9.3 refused it on every start and 1.9.2 stopped with "Applied migration '0038_tally_transfer_foundation' is missing from the migration source", so only a restore could recover it. Now a refusal leaves the database exactly as 1.9.2 left it, and 1.9.2 still opens it; run `scripts/check-import-upgrade.sql` to see what to fix. A pre-check is the `PRECHECK_*` section of its script and must only read. A release that opens a database a newer release has upgraded now says so ("This database was upgraded by a newer release of ETP ...") instead of "missing". 1.9.2 itself still shows the "missing" message: it means the database is newer than 1.9.2 (see `docs/OPERATIONS.md`, Troubleshooting).
- `scripts/check-import-upgrade.sql` has a new information row, `OPEN_MOVEMENT_CONFLICTS_ON_STORED_ROWS`: open stock-ledger (R003) conflicts on a movement identity the database holds. 1.9.2 kept the first unit row of such a group in file order; 0041 numbers that row line 1, while a re-import numbers the running-balance chain start 1, so a re-import that overlaps it is refused with IMPORT_CONFLICT. Plan Owner restatements for those days before relying on re-imports.
- New integration test of the real upgrade order: a 1.9.2 (0037) database with data goes through Tally 0038-0040 and then 0041-0042 in one run, and a 0041 pre-check refusal leaves it at 0037.
- Import engine fixes (1.9.3) are migration 0041 (`0041_import_engine_fixes.sql`). It was written as 0038, which Phase 7's Tally migration took; it was renumbered to run after Tally's 0038-0040. A test now fails if two migrations share a number or the numbers skip one.
- Test fixes: the operations boundary test now waits up to 120 s and prints the script output when it times out; the zip retry import test no longer fails when the random temp folder name happens to contain "bad".

Status: not released. Migration 0042 has not been run against SQL Server: its integration tests (RetiredAccountDeactivationTests) are written but need an account that can create databases. Migrations 0038 to 0042 have not been applied to the shop database. ETP does not send anything to Tally yet: no Tally PC has been tested (plan task 5), and decisions D12 to D18 are still open.

## [1.9.2] - 2026-10-01

Fixes the new-PC setup, which failed on its first real run:

- With "Install Microsoft SQL Server 2025 Express" ticked, setup installed SQL Server and the protected folders, then stopped with "SQL Server preparation, database migration or health validation failed". SQL Server 2025's own setup installs an ODBC Driver 18 Sqlcmd; setup took it as Sqlcmd already installed, skipped the bundled one, and every query then failed because ODBC 18 encrypts by default and refuses the new instance's self-signed certificate. Setup now always installs the bundled Sqlcmd (on ODBC Driver 17), and ETP's scripts use it before an ODBC 18 one.
- SQL Server Express setup no longer makes the account running ETP setup a SQL administrator of its own. Express does that by default; setup now turns it off, so only `Administrators` (and SQL Server's own service accounts) are SQL administrators, as intended. An instance 1.9.1 already installed keeps that login until it is removed by hand.
- A PC left half-set-up by 1.9.1 is finished by running 1.9.2 setup: SQL Server, the protected folders, the EtpAutomation account and the configuration it already has are kept, and only the missing Sqlcmd is installed.

Status: unsigned, like 1.9.1.

## [1.9.1] - 2026-09-27

Setup can now prepare a new PC from the installer alone:

- "Install Microsoft SQL Server 2025 Express" now works on a PC with no ETP on it yet. Setup installs SQL Server 2025 Express (Windows accounts only, no network access, Administrators as SQL administrators), ODBC Driver 17, ODBC Driver 18 and Sqlcmd, then creates the protected folders, the EtpAutomation account and the machine configuration. Until now it stopped at the missing configuration before it could install anything.
- The SQL Server media is unpacked into admin-only folders and deleted after setup, instead of the user's Temp folder, which SQL Server setup's own security check refused.
- New option "Create a new empty ETP database". Untick it to move existing data: setup prepares SQL Server and stops, then the new `scripts\restore-etp-database.ps1` restores the backup (it checks the backup, never replaces an existing database, and makes the person running it the Owner), and setup is run again to update it.
- A new database's Owner gets a SQL Server login of their own, so ETP opens for them without administrator rights.
- An SQLEXPRESS instance that was already on the PC is used only if it has the same hardening; otherwise setup refuses and changes nothing.
- The installer build keeps the SQL media path out of the signing loop, which broke every signed build.

Status: unsigned. The new-PC path is covered by automated tests but has not yet run on a real new PC; its first real run is the move to the new PC.

## [1.9.0] - 2026-09-26

Phase 5 finishes, replaces or deletes the secondary modules per D6:

- accounting and Tally export: approved adjustments, atomic mapping approval, approval reasons, Owner-only reject, honest statuses, duplicate-batch refusal and export receipts
- archive and sharing: SMTP email, WhatsApp handoff and delivery history
- registers
- approvals and adjustments, including restatement requests that wait for Owner approval
- a store catalogue in place of hard-coded stores
- automatic import
- a rewritten Help centre
- investigation click-through
- removal of the OCR / Source Inbox code

Also included:

- the Phase 4 fixes of 25 September: Settings says why a save failed; rotation keeps safety backups; a full backup folder can free space; an encrypting SQL edition is refused in plain words
- migrations 0025-0037

Status: an unsigned build (A4.7). Phase 5 acceptance A5.1-A5.7 is not yet proven on an installed copy. Import bug IF-014 (a slow disk can report a saved import as Failed) is open.

Earlier entries that were listed under Unreleased:

- Restore approved brand mappings, durable import history, query filters with export scope, and selective Retry failed.
- Allow Owner-approved adjustments in accounting mappings.
- Make mapping approval atomic and persist batch approval reasons (migration 0029).
- Add Owner-only rejection of unexported accounting batches with reason, actor/time and audit (migration 0030).

## [1.8.8] - 2026-09-12

UI redesign candidate covering the complete active application. Installed, device and full interactive acceptance remain explicitly unverified; see the sprint ledger. Not approved for production.

- Add canonical task paths, persistent task search, breadcrumbs, retained focused workspaces and grouped Today Overview.
- Preserve explicit task context and register drafts, invalidate changed import validation, and avoid stale source-extraction selection results.
- Separate accounting approval, ledger mapping, sharing contacts, backup, recovery and support actions.
- Record source route coverage, offscreen viewport evidence and automated regression results separately from installed interaction.

## [1.8.7] - 2026-09-12

Engineering candidate; observed role, accessibility and external integration acceptance remains open.

- Recognize an already-imported workbook in the single-file desktop flow without attempting another database write.
- Display an explicit no-change duplicate result and reject identical-file controlled restatements.
- Add regression coverage for both paths and record VM duplicate, reporting, automation and recovery evidence.

## [1.8.6] - 2026-09-11

Engineering candidate for the four-phase review; production acceptance remains subject to the recorded workflow, installer and owner gates.

- Block reconciliation success when no source evidence exists.
- Guard Daily Workflow against stale scope results and overlapping submissions, and clarify input labels.
- Preserve previous Windows candidates and bind installer packaging to the selected payload version.

## [1.8.5] - 2026-08-29

> Source-version record only. No 1.8.5 artifact, installer, SBOM, provenance, signature, tag or release has been produced or promoted.

### Added

- Added migration `0015_operational_audit_contract.sql` so the database audit constraint covers every emitted event, including document review, sharing-contact and visual-render events.
- Added exact import-profile identity and a blocker-free matched-import envelope that preserves the approved report/layout/profile/header-signature provenance through Desktop, automation and SQL persistence.
- Added structured, privacy-safe desktop diagnostics with stable source/code/severity fields and serialized concurrent file writes.
- Added verified pre-migration backup receipts and post-migration database health gates for migration-bearing upgrades.

### Changed

- Moved workbook materialization and report exports away from the WPF UI thread, added cancellation/concurrency bounds and prevented overlapping export actions.
- Existing-database bootstrap now checks SQL/database compatibility and backup capacity, verifies backup path/length/SHA-256 before migration, and requires online/read-write state, exact migration journal count and `DBCC CHECKDB` afterward.

### Fixed

- Made sharing-contact mutation and its operational audit write atomic, and normalized report audit outcomes to the database-supported vocabulary.
- Rejected the preserved 1.8.4 engineering payloads from promotion because their shipped audit contract could reject valid emitted events and their committed SBOM identifies a different source state/application hash than the candidate provenance.

## [1.8.4] - 2026-08-29

### Release disposition

- Preserved for forensic/reproducibility purposes but rejected and never promoted. Its binaries, hashes, SBOM and provenance remain unchanged historical evidence; only a future newly built and independently accepted 1.8.5 candidate can replace it.

### Changed

- Completed route-backed step-by-step Help guidance, reconciled closure and retention documentation, and prepared external-acceptance and release evidence for the integrated candidate.

### Fixed

- Prevented concurrent database-backup invocations from selecting the same filename and overwriting an existing backup.

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
