# ETP Reporting Engine — Administrator Handbook

## Supported environment

- Windows 10/11 x64 with current security updates.
- SQL Server 2022 Express instance `SQLEXPRESS` and Windows authentication.
- Local administrator rights for installation and scheduled-backup registration.
- Source workbooks stored in an access-controlled business-data folder, never in the application repository.

## Installation and upgrade

1. Back up `EtpReporting` and verify the backup before any upgrade.
2. Run the versioned setup executable as administrator. The stable installer AppId upgrades the existing installation in place.
3. Launch the application, test the database connection, and confirm that the database reports no pending migration.
4. Retain the prior installer and verified database backup until acceptance is complete.

Uninstall from Windows **Installed apps**. Uninstalling the program does not delete SQL Server databases, source workbooks, exported reports, or administrator-created backups.

## Daily operation

- Check database availability, free disk space, backup age, and failed-import warnings before importing.
- Import only approved ETP workbooks or ZIP packages. Review the batch summary; retry only failed files after correcting the reported cause.
- Reconcile sales, tender, and stock controls. Tender differences are diagnostic findings and must not be silently adjusted.
- Export only to approved local folders. Treat every export as confidential business data.

## Backup and recovery

Use the scripts described in [Operations and Recovery](16_OPERATIONS_AND_RECOVERY.md). A successful job run is not sufficient: monitor backup age and periodically run a restore drill into a separately named database. Never restore over production during a drill.

Recovery sequence:

1. Stop imports and record the incident time.
2. Select the latest checksum-verified backup that predates the incident.
3. Restore into a temporary validation database and run integrity/lineage checks.
4. Obtain business approval before replacing or redirecting production.
5. Record backup identity, restore result, row/control comparisons, operator, and time.

## Diagnostics and support

Create the offline support package from the application or approved support script. It must contain health results, versions, sanitized logs, and import status counts only—never workbook rows, invoice identifiers, customer details, tender references, connection passwords, or database backups.

Run `scripts/invoke-security-scan.ps1` before each release. Address dependency findings or document a time-bound exception. Use `scripts/test-windows-ui.ps1 -AccessibilityAudit` and the installer lifecycle test for release acceptance.

## Release process

1. Update `CHANGELOG.md` and run `scripts/set-release-version.ps1`.
2. Run automated tests and dependency scans.
3. Build the portable release with `scripts/build-windows-release.ps1`.
4. Build the installer with `scripts/build-installer.ps1`.
5. Run Windows UI and isolated install/upgrade/uninstall tests.
6. Record SHA-256 checksums and acceptance evidence; refresh Graphify; commit only reviewed source and documentation.
