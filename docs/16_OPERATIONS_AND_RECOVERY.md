# ETP Reporting Engine - Operations and Recovery

## Daily operation

1. Open ETP Reporting Engine. The saved SQL Server connection is tested automatically.
2. Confirm the Dashboard shows `Connected`, imported-file totals, source-row totals, and recent import history.
3. Validate each workbook before importing it. Unsupported or changed layouts remain blocked.
4. Run Sales or Stock reports and export the reviewed result to Excel or PDF.

The application retains source signs. `SR` rows are already negative and are never negated a second time. `NETVALUE` remains GST-inclusive. Tender reconciliation remains a visible control and does not silently force a pass.

## Logs

Privacy-safe application diagnostics are stored under `%LOCALAPPDATA%\EtpReporting\Logs`. Logs contain timestamp, exception type, and numeric error code only; source rows, document numbers, customer data, and workbook paths are excluded.

## Database health

Run from an elevated PowerShell prompt:

```powershell
.\scripts\test-etp-database-health.ps1
```

This runs `DBCC CHECKDB`, verifies the migration count, and returns aggregate import counts.

The in-app health service evaluates privacy-safe operational warnings: a full backup older than 36 hours (or missing), database files at 80% of their configured maximum, any failed import during the previous 24 hours, backup storage below 20 GB, and critical backup storage below 5 GB. Unlimited SQL data files do not generate a misleading percentage warning.

## Backup

Create and checksum-verify a backup immediately:

```powershell
.\scripts\backup-etp-database.ps1
```

Backups default to `%ProgramData%\EtpReporting\Backups`, use SQL Server `CHECKSUM`, and run `RESTORE VERIFYONLY`. The approved policy retains every backup indefinitely: the backup automation never deletes a backup or any business/import data. Monitor free disk space and expand or move the approved backup storage before it fills. They do not request backup compression because SQL Server Express does not support it.

Install the daily 10 PM backup task from an elevated PowerShell prompt:

```powershell
.\scripts\install-daily-backup-task.ps1
```

## Restore drill

Run the automated recovery drill:

```powershell
.\scripts\invoke-etp-recovery-drill.ps1
```

The drill restores the latest verified backup into a uniquely named validation database, runs `DBCC CHECKDB`, compares imported-file and lineage aggregates with production, and drops the validation database and files in a `finally` block. It never overwrites the live database.

Install the approved monthly drill (first day of each month at 8 AM by default) from an elevated PowerShell prompt:

```powershell
.\scripts\install-monthly-recovery-drill-task.ps1
```

Operational audit events are retained for 730 days. Database maintenance deletes only audit events older than that period; it never deletes business facts, source lineage, imports, reports, or backups.

## Offline support package

Create a package suitable for offline support transfer:

```powershell
.\scripts\new-etp-support-package.ps1
```

The ZIP contains database aggregate counts, health metadata, operating-system and SQL service status, and scheduled-task state. It deliberately excludes source rows, customer information, invoices, workbook names, workbook paths, connection strings, and application logs that could contain user-entered text.

## Deferred business controls

Tender variance resolution, complete category and brand dictionaries, LY/TY periods, and ABV/ASP formulas remain fail-closed until approved business rules or source data are supplied.
