# Changelog

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and semantic versioning.

## [Unreleased]

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

