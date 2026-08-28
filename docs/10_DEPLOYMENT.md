# Deployment and Operations

## Status and scope

The deployment is a self-contained .NET 10 WPF desktop application backed by Microsoft SQL Server Express. The application includes SQL connectivity checks, safe database creation, checksum-controlled migrations, canonical persistence, SQL-backed on-screen reports, fixed-format Excel output and a single-file `win-x64` publish path. The [Windows release builder](../scripts/build-windows-release.ps1) restores, builds, tests, publishes and emits `SHA256SUMS.txt`. The [Windows installer builder](../scripts/build-windows-installer.ps1) packages that release as a versioned, administrator-elevated Inno Setup executable using the checked-in [installer definition](../installer/EtpReportingEngine.iss), and emits a separate installer checksum.

These are source-implemented packaging paths, not evidence of a production deployment. The setup executable and application are not yet code-signed, and the repository does not establish that clean installation, upgrade, uninstall, SQL bootstrap, backup, or restore has passed on the intended target PC. The release remains blocked on the external validation and signing gates below.

## Supported topology

The initial supported topology is one managed Windows workstation/application deployment connected to a named SQL Server Express instance on the same machine or an explicitly approved Windows database host. Multi-user remote hosting, high availability, and cloud database operation require separate architecture and security approval.

SQL Server is the source of truth. Workbooks are import evidence and exported reports are outputs; neither is a replacement database.

## Prerequisites

The release owner must publish a precise support matrix before the first production release. The installer/preflight must verify, at minimum:

- A supported 64-bit Windows edition and patch level.
- The .NET 10 Windows Desktop Runtime matching the application architecture, unless the application is shipped self-contained.
- A supported SQL Server Express engine version/instance and required database compatibility level.
- Available disk space for application files, database growth, transaction log, temporary import/export files, and backups.
- Permission for the application identity to connect and for the deployment/migration identity to perform only its approved setup operations.
- A writable, business-controlled backup destination accessible to the SQL Server service identity.
- A writable per-user application-data/log location that is not the installation directory.
- Approved endpoint-protection/firewall rules where the SQL instance is remote; SQL Browser or TCP exposure must not be enabled unless required and approved.

The current installer presents a selected-once `sqlbootstrap` task that explicitly states that Microsoft SQL Server 2022 Express may be installed/configured and that Microsoft licence terms are accepted. When retained, the [bootstrap script](../scripts/bootstrap-etp-prerequisites.ps1) requires elevation, uses `winget` to install SQL Server Express and `sqlcmd` when missing, configures `MSSQL$SQLEXPRESS` for automatic start, prepares the controlled backup directory, initializes the database, and registers the backup, restore-drill, and automation tasks. An operator can clear that task when prerequisites are managed separately. Bootstrap failure terminates setup with an actionable pointer to `%ProgramData%\EtpReporting\SetupLogs`.

The bootstrap does not open firewall ports. Remote SQL exposure and any broader permission changes remain separately governed. Its presence in source does not replace a clean-machine test or approval of the Microsoft packages, `winget` availability, network access, service identity, and resulting SQL permissions in the production environment.

## Identity and database permissions

Prefer Windows authentication. Separate duties where practical:

- **Deployment/migration identity:** temporary or controlled rights needed to create/upgrade the application database.
- **Application identity:** least-privilege access to required stored operations/tables; no server administration, arbitrary database creation, or backup-directory browsing.
- **Backup/operator identity:** rights needed for approved backup/restore operations; restore is an administrative recovery operation, not a normal desktop-app permission.

Do not use `sa`, shared administrator credentials, or credentials embedded in the executable, installer, source, scripts, logs, or registry in plaintext. The precise role/grant model must be implemented and reviewed before production.

## Configuration and secrets

Non-secret defaults may ship with the application, but environment-specific values are external configuration. Connection configuration should identify the server/instance and database without embedding a password. If SQL authentication is exceptionally approved, protect the credential with Windows-supported secret protection scoped to the intended user/machine and document rotation/revocation.

Never commit or package:

- Production connection strings containing credentials, tokens, certificates/private keys, or signing secrets.
- Real ETP/customer workbooks, database `.bak`/data files, diagnostic database copies, or exports containing business data.
- User-specific configuration, logs, crash dumps, or CI secret files.

CI/release secrets belong in the protected CI secret store with least privilege, environment scoping, auditability, and rotation. Logs must redact connection strings and sensitive source values. Configuration diagnostics may report server/instance and database only when operationally safe.

## Installation and first start

The Inno Setup package installs under the standard protected Program Files location and includes the self-contained application, database migrations, and operations scripts. Setup is elevated and offers Start-menu and optional desktop shortcuts. Application diagnostics are written under `%LocalAppData%\EtpReporting\Logs`; bootstrap logs and backup files are written beneath `%ProgramData%\EtpReporting`. Uninstall removes the ETP scheduled tasks through [the task-removal script](../scripts/remove-etp-scheduled-tasks.ps1); it does not claim to delete the SQL database or retained backups.

The accepted device-licensing architecture is documented in `security/ETP_LICENSING_ENGINEERING_SPEC.md` but is intentionally not implemented yet. When the final licensing phase is authorized, the installer must create the protected ProgramData licensing location and the application must route unactivated interactive starts to ActivationWindow. Installer database initialization remains separate from normal licensed operation, and unattended automation must be gated.

First-start flow:

1. Load and validate external configuration without logging secrets.
2. Check SQL connectivity and identify configuration, authentication, network, or database errors distinctly.
3. Acquire an application-wide database-upgrade lock so two clients cannot migrate concurrently.
4. Discover ordered migration scripts and compare them with `dbo.schema_migrations`.
5. Fail closed if an applied migration is missing or its SHA-256 checksum differs.
6. Back up the existing production database before a non-trivial upgrade according to the release runbook.
7. Apply pending migrations transactionally where SQL Server permits and record their checksums.
8. Run post-migration health checks before enabling imports or reports.

The current installer invokes the application once with `--initialize-database`. That headless path creates the configured database if absent, discovers the packaged migrations, rejects missing or checksum-changed applied migrations, and applies each pending migration and journal entry in a SQL transaction. `0001_foundation.sql` also uses `XACT_ABORT` and a transaction. Installer/database orchestration is therefore implemented in source. A database-wide migration lock, an automatic pre-upgrade backup, and the complete post-migration acceptance checks listed above are not yet implemented as one enforced upgrade transaction and remain release gates.

Applied migration files are immutable. Corrections require a new migration. A failed migration blocks application use until recovery; the application must not continue against an unknown or partially upgraded schema.

## Upgrade and rollback

Application and schema versions form one tested release unit. The release package must state the minimum supported prior version and database schema.

Before upgrade:

- Confirm a recent verified backup and enough free space.
- Stop imports and ensure no active batch/migration is running.
- Record application version, schema migrations/checksums, database identity, and backup identity.

After upgrade, run connectivity, migration, import-lineage, and representative report smoke checks. The [installer lifecycle test](../scripts/test-installer-lifecycle.ps1) exercises silent installation, same-version repair or a supplied prior-version upgrade, and uninstall in a temporary directory with SQL bootstrap deliberately disabled; it is packaging evidence only, not database-upgrade or target-PC acceptance evidence. Application-binary rollback is allowed only when the older version is compatible with the upgraded schema. The [rollback helper](../scripts/invoke-release-rollback.ps1) takes a verified backup unless explicitly overridden and reinstalls a supplied prior installer while retaining SQL data; operators must still verify schema compatibility and controls. Database rollback means restoring the verified pre-upgrade backup and reconciling any work performed after it; down-migrations are not assumed. The release runbook must define the business outage/data-reentry decision.

## Backup policy

Backups are written by SQL Server to a business-controlled location, not copied from live database files. The current [backup script](../scripts/backup-etp-database.ps1) uses Windows-integrated `sqlcmd`, creates a `COPY_ONLY` full backup with `CHECKSUM`, immediately runs `RESTORE VERIFYONLY WITH CHECKSUM`, records a privacy-safe audit event, and emits the file's SHA-256 hash. It warns below 20 GB free and emits a critical warning below 5 GB. The [daily task installer](../scripts/install-daily-backup-task.ps1) registers this operation as `SYSTEM` at 22:00 by default and describes backups as retained indefinitely. No repository script prunes these backup files, consistent with the current no-deletion policy.

Operations must still decide, record, and validate:

- Backup frequency and schedule.
- Retention and off-device/off-site protection.
- Encryption requirements and key custody.
- Recovery point objective (RPO) and recovery time objective (RTO).
- Monitoring, failure alerts, capacity ownership, and deletion policy.

The application dashboard also evaluates latest full-backup age, backup-volume free space, database growth, and recent failed imports. These indicators are advisory: alert destinations, escalation ownership, off-device protection, and capacity response must still be established. At minimum, confirm the installed task actually runs under the target service identity, take a verified pre-upgrade backup, and maintain scheduled operational backups once production data exists. A job reporting success is insufficient: backup history, file existence/size, integrity verification, and periodic restore rehearsal are required.

## Restore procedure and validation

Restore is a controlled operator procedure:

1. Stop application access/imports and preserve the failed database and logs when safe.
2. Select the approved backup by database, timestamp, release/schema identity, and integrity evidence.
3. Restore first under a new database name when capacity and incident conditions allow.
4. Run SQL integrity checks and validate `schema_migrations` checksums.
5. Compare expected master, import batch/file, lineage, and canonical counts plus approved report control totals.
6. Point the application to the recovered database only after acceptance.
7. Record elapsed recovery time, data-loss window, operator, evidence, and final disposition of temporary copies.

The implemented [recovery-drill script](../scripts/invoke-etp-recovery-drill.ps1) selects the latest ETP backup, verifies its checksum, restores it under a timestamped temporary database name, runs `DBCC CHECKDB`, compares `import_files` and `source_lineage` counts with the live database, records a privacy-safe audit event, and removes the temporary database/files. The [monthly task installer](../scripts/install-monthly-recovery-drill-task.ps1) creates a daily check at 08:00 that runs the drill on day 1 by default. This automated drill does not switch the application connection or constitute an incident recovery.

Every release that changes schema, authentication, installer, or backup behavior requires the restore validation described in [the test strategy](09_TEST_STRATEGY.md). A successful production-like execution, retained evidence, reviewed RTO/RPO, and operator sign-off are mandatory before backup/recovery can be described as operationally proven.

## Data handling and privacy

ETP files, canonical facts, reports, backups, logs, and support bundles may contain commercially sensitive or personal data. Apply least access and approved retention throughout their lifecycle.

- Import from a controlled local/inbox path and hash the source for lineage; do not rename or mutate the original as evidence.
- Keep temporary workbook/extract files for the shortest practical time and delete them through an approved cleanup process after use.
- Do not transmit business data to AI, public services, analytics, or crash-reporting systems without explicit approval.
- Exports inherit the sensitivity of their source and should carry report parameters, generation time, and data/version identity without exposing credentials.
- Support bundles default to metadata and bounded/redacted diagnostics. Inclusion of source rows or database content requires explicit authorization.
- Backups and restricted golden datasets require access controls, encryption/physical protection, retention, and auditable disposal appropriate to production data.

## Logging and monitoring

Operational logs should include application/release version, correlation/import batch ID, profile and migration versions, event severity, and safe error classifications. They must exclude passwords, full connection strings, unnecessary source cells, and personal data. Define log location, rotation, maximum size, retention, and support collection before production.

Monitor database connectivity, disk/capacity trends, failed/long imports, migration failures, backup age/failure, and restore-drill status. The application exposes database-size, latest-backup, backup-free-space, and failed-import indicators, and the [privacy-safe support-package script](../scripts/new-etp-support-package.ps1) collects aggregate health, SQL service, and scheduled-task state without source rows, customer data, invoice identifiers, or workbook paths. Alert destinations and ownership are operational decisions and must be tested rather than documented only.

## CI, packaging, and release

The current [GitHub Actions workflow](../.github/workflows/ci.yml) restores, builds, and tests the solution in Release mode on Windows. Versioning and changelog preparation are supported by [the release-version script](../scripts/set-release-version.ps1); a versioned offline ZIP can be assembled from an already-built installer by [the offline-package script](../scripts/new-offline-deployment-package.ps1). A local [dependency security scan](../scripts/invoke-security-scan.ps1) checks vulnerable/deprecated .NET dependencies and npm audit results, but it is not currently an enforced step in the checked-in CI workflow. A production release pipeline must additionally:

1. Build from a protected, reviewed commit/tag with pinned/reviewed dependencies.
2. Run all gates in `09_TEST_STRATEGY.md`, including live SQL Server Express and recovery evidence where required.
3. Publish a versioned installer/application package and checksums; code-sign the executable/installer when signing infrastructure is approved.
4. Create a software/dependency inventory and complete vulnerability/license review.
5. Include immutable migration scripts, release notes, supported upgrade path, known limitations, and operator runbook.
6. Promote the exact tested artifacts without rebuilding them.
7. Restrict artifact and signing-secret access and retain release provenance.

No production release is permitted directly from a developer workstation or from an unreviewed ZIP snapshot. SHA-256 checksums provide integrity evidence, not publisher identity; the Windows executable and installer must remain explicitly described as unsigned until an approved publisher identity and code-signing certificate are used and the signatures are verified.

## Deployment acceptance checklist

- Supported Windows, .NET deployment mode, SQL Server Express version, and instance name are documented.
- Clean install and supported upgrade pass on production-like Windows.
- Least-privilege application access and migration/backup separation are verified.
- Migration checksums, concurrency protection, failure behavior, and post-upgrade health checks pass.
- Synthetic smoke import and approved report reconciliation pass against SQL.
- Backup completes and a restore rehearsal reproduces lineage and report totals within the accepted RTO/RPO.
- Secrets, business files, logs, exports, and support bundles satisfy privacy/retention controls.
- Installer/package identity, checksums, signature, release notes, and rollback runbook are approved.
- Business owner and operations sign off; unresolved blockers are recorded and prevent promotion.
