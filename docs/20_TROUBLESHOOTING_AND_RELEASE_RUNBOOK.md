# Troubleshooting and Release Runbook

## Decision tree

1. **Application will not open:** run the UI smoke script, verify the release checksum, then review the privacy-safe `%LOCALAPPDATA%\EtpReporting\Logs` entry.
2. **Database unavailable:** confirm `MSSQL$SQLEXPRESS` is running, test the Windows-integrated connection, and run the database-health script.
3. **Import blocked:** confirm the source is XLSX/folder/ZIP, review its safe error code, close Excel for I/O failures, and never bypass an unknown layout.
4. **Report blocked:** check the date boundary and required source profiles. Do not invent mappings or signs.
5. **Tender failed:** use Tender Diagnostics; the classification is an investigation prompt and never changes the authoritative control.
6. **Backup warning:** run an immediate checksum backup and isolated recovery drill before further imports.

## Performance gates

Run `dotnet run --project tools/Etp.Reporting.PerformanceSmoke -c Release -- artifacts/performance/performance-smoke.json`. The standard synthetic gate covers 250,000 sales lines across three dimensions, 100,000 stock keys, and 50,000 tender documents. Each operation must complete within 30 seconds on the release workstation.

## Database maintenance

Run `scripts/invoke-database-maintenance.ps1` monthly. It performs `DBCC CHECKDB`, updates statistics and removes aggregate operational-audit entries older than 365 days. It never deletes import lineage or reporting facts.

## Release and rollback

Build, scan, test, tag and package the release. The offline bundle must contain only the installer, checksums and manuals. Before rollback, create a verified backup, run the previous installer, then repeat database health and reporting controls. Application rollback does not downgrade or remove SQL data.

## Incident evidence

Generate the offline support ZIP. It intentionally excludes workbook names and paths, rows, documents, customers, credentials and backups. Record the application version, event time, safe error code and aggregate control status separately.
