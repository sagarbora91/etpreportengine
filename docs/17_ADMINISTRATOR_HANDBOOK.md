# ETP Reporting Engine — Administrator Handbook

## Supported environment

- Windows 10/11 x64 with current security updates.
- SQL Server 2022 Express instance `SQLEXPRESS` and Windows authentication.
- Local administrator rights for installation and scheduled-backup registration.
- Source workbooks stored in an access-controlled business-data folder, never in the application repository.

## Installation and upgrade

1. Back up `EtpReporting` and verify the backup before any upgrade.
2. Run the versioned setup executable as administrator and keep the SQL bootstrap task selected. When `SQLEXPRESS` is missing, the bootstrap uses Windows Package Manager to install Microsoft's official SQL Server 2022 Express and Sqlcmd packages; internet access and acceptance of Microsoft's license terms are required.
3. The bootstrap preserves an existing `SQLEXPRESS` instance, configures automatic startup, applies only checksum-controlled application migrations, prepares backup access, and registers the daily backup and monthly recovery tasks.
4. Launch the application, test the database connection, and confirm that the database reports no pending migration.
5. Retain the prior installer and verified database backup until acceptance is complete.

The bootstrap fails closed and records privacy-safe progress under `%ProgramData%\EtpReporting\SetupLogs`. It never uninstalls, replaces, downgrades, or deletes an existing SQL instance or database. A pending Windows restart or unavailable internet/package source can prevent a new SQL Server installation; restart or restore connectivity and rerun setup.

Uninstall from Windows **Installed apps**. Uninstalling the program does not delete SQL Server databases, source workbooks, exported reports, or administrator-created backups.

## Daily operation

- Check database availability, free disk space, backup age, and failed-import warnings before importing.
- Import only approved ETP workbooks or ZIP packages. Review the batch summary; retry only failed files after correcting the reported cause.
- Reconcile sales, tender, and stock controls. Tender differences are diagnostic findings and must not be silently adjusted.
- Export only to approved local folders. Treat every export as confidential business data.

## Approved access policy

- **Owner/Admin:** has all application and administrative rights, including imports, SQL Server connection administration, database maintenance, and approval of mapping or control-rule changes.
- **Store Manager:** may import approved ETP reports and use reports/exports, but may not approve or alter mappings, signs, tolerances, control rules, SQL connections, or database configuration.
- **Other users:** have no import or administration authority unless the Owner explicitly revises this policy.
- Codex may assist the Owner with technical administration only during an Owner-authorized session. Codex is not an independent account, administrator, or approval authority.

The current desktop release is intended for an Owner-controlled Windows computer and does not provide independent user authentication. Until authenticated application roles are implemented, Windows sign-in and physical access are the enforcement boundary: Store Managers should use the application only under the Owner's approved operating procedure, and Settings/mapping changes remain Owner-only.

## Backup and recovery

Use the scripts described in [Operations and Recovery](16_OPERATIONS_AND_RECOVERY.md). Retain every database backup indefinitely and never delete business/import data. A successful job run is not sufficient: monitor backup age and run the isolated restore drill monthly. Never restore over production during a drill. Operational audit history is retained for two years.

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
