# Phase 2 Operations — Implemented Design

## Delivered capability

Version 1.5.0 adds the unattended and administrative layer around the verified ETP reporting engine:

- Windows-integrated roles: Owner, Store Manager and Viewer.
- Owner-audited user and controlled master administration.
- Five-minute local XLSX/ZIP watch-folder processing with a single-run database lease.
- Stable-file delay, content-hash duplicate prevention, and Processed/Failed source quarantine.
- Automatic combined Titan World + Helios Excel/PDF output after a new import, plus configurable morning/evening schedules.
- Immutable full report documents protected by SHA-256, with browse, compare and re-export.
- Daily store sales/control trends and an aggregate data-quality control centre.
- In-app checksum backup, isolated recovery drill and privacy-safe support package actions.

## Default folders and schedules

The migration creates these local defaults:

| Purpose | Default |
|---|---|
| Inbound source | `C:\ProgramData\EtpReporting\Inbound` |
| Successfully processed source | `C:\ProgramData\EtpReporting\Processed` |
| Failed source quarantine | `C:\ProgramData\EtpReporting\Failed` |
| Generated report packs | `C:\ProgramData\EtpReporting\ReportPacks` |
| Morning pack | 08:00 local time |
| Evening pack | 21:30 local time |
| Automation polling | Every 5 minutes |

Folders must be distinct, fully qualified, local, non-root and not linked/reparse-point locations. Processed, Failed and ReportPacks cannot be nested below Inbound.

## Authority boundaries

| Capability | Owner | Store Manager | Viewer |
|---|---:|---:|---:|
| View dashboards/reports/archive | Yes | Yes | Yes |
| Import and operational entry | Yes | Yes | No |
| Run automation immediately | Yes | Yes | No |
| Change users, masters, folders or schedules | Yes | No | No |
| Change SQL connection / initialize database | Yes | No | No |
| Run backup, restore drill or support package | Yes | No | No |

The database prevents deactivation or removal of the last active Owner. User/master changes retain before/after JSON, actor, UTC time and reason. Controlled Tender metadata does not alter accounting rules.

## Automation behavior

The five-minute Windows task starts the application with `--automation-once` under the built-in `SYSTEM` account so it continues when the Owner is signed out. Migration 0012 grants that account only application-database reader/writer/backup-operator access and registers it as the visible ETP Automated Operations Store Manager; it is not a SQL Server administrator. A SQL application lock prevents overlapping runs. Each eligible top-level XLSX/ZIP source is opened through the existing safe batch source, inspected with approved profiles and imported through the same transactional orchestrators as the UI. No unknown layout is guessed.

Every outcome is recorded without source rows. Any newly imported business date triggers a combined management pack; configured schedules run only once per latest complete R025 business date. Source/report failures stay visible and do not change `NETVALUE`, transaction signs, tender controls or tolerances.

## Data retention and uninstall

Report archives, source lineage, audit history and automation history are database records. Existing business-data and backup retention rules remain unchanged. Uninstall removes the ETP scheduled tasks but intentionally retains the SQL Server instance, `EtpReporting` database, backups, processed/failed sources and generated report packs.
