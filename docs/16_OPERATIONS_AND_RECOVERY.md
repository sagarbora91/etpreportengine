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

## Backup

Create and checksum-verify a backup immediately:

```powershell
.\scripts\backup-etp-database.ps1
```

Backups default to `%ProgramData%\EtpReporting\Backups`, use SQL Server `CHECKSUM`, run `RESTORE VERIFYONLY`, and retain 30 days.

Install the daily 10 PM backup task from an elevated PowerShell prompt:

```powershell
.\scripts\install-daily-backup-task.ps1
```

## Restore drill

Restore into a separate validation database first. Never overwrite the live database during a drill. Use SQL Server Management Studio or `RESTORE DATABASE` with new `MOVE` targets, run `DBCC CHECKDB`, compare import-file and lineage counts, then remove the validation database after approval.

## Deferred business controls

Tender variance resolution, complete category and brand dictionaries, LY/TY periods, and ABV/ASP formulas remain fail-closed until approved business rules or source data are supplied.
