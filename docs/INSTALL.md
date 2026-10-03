# Windows Quick Start

For the packaged release, run the versioned `EtpReportingEngine-Setup-<version>-x64.exe` as administrator.

**The SQL Server option appears only when the installer was built with SQL media embedded** (`build-windows-installer.ps1 -SqlPayloadDirectory`, a folder holding the SQL Server Express package, `msodbcsql17.msi` and `MsSqlCmdLnUtils.msi`). P4-11 recorded why: the option used to be offered on every build, did nothing, and named an edition that was not what it installed. The option reads **Install Microsoft SQL Server 2025 Express, its ODBC drivers and Sqlcmd**. On a new PC keep it ticked: setup installs SQL Server Express (instance `SQLEXPRESS`, Windows authentication only, no TCP or named pipes, `BUILTIN\Administrators` as SQL administrators, collation `Latin1_General_CI_AS`), then ODBC Driver 17 and Sqlcmd, from the media inside the installer with no internet access, and then creates the protected folders, the `EtpAutomation` account and the machine configuration itself. ODBC drivers already installed are skipped, and so is Sqlcmd when the ODBC Driver 17 Sqlcmd is already there; the ODBC Driver 18 Sqlcmd that SQL Server 2025's own setup brings does not count, because it refuses the new instance's self-signed certificate, and ETP's scripts use the ODBC 17 one first. The media is deleted when setup finishes. An `SQLEXPRESS` instance that is already on the PC is neither reinstalled nor reconfigured: setup uses it only if it already has those settings (Windows authentication only, TCP/IP and named pipes off, no SQL administrators but `Administrators` and SQL Server's own service accounts), and otherwise stops before creating any ETP folder, account or configuration. A PC whose ETP folders were set up in strict mode, with no `operations.json`, is also left as it is. The Windows account that runs setup becomes ETP's first Owner; when setup creates the database, it also gives that account a SQL Server login of its own (the documented Owner provisioning), because `Administrators` are SQL administrators only from an elevated window and ETP normally opens unelevated. SQL Server 2025 is one-way: a database restored onto it can no longer be restored onto SQL Server 2022. When the option is absent, install SQL Server yourself first as described under Prerequisite. For a silent install choose tasks with `/MERGETASKS`, not `/TASKS=`, which would also untick the database option below.

Setup then configures automatic startup, initializes `EtpReporting`, prepares backup access, and registers the daily backup, monthly recovery-drill and five-minute ETP automation tasks. It creates a Start Menu entry, and supports upgrades and uninstall through Windows Installed Apps; uninstall removes the ETP tasks but never removes SQL Server, databases, sources, reports or backups.

If the release was built without a signing certificate, which A4.3 accepts, Windows will warn that the publisher is unverified. Confirm the SHA-256 in `SHA256SUMS.txt` beside the installer before running it.

The SQL connection is saved for the current Windows user after a successful connection test and is checked automatically at startup. The Dashboard shows aggregate import status and recent import history. No database password is stored by the default Windows-integrated connection.

Reports can be exported to fixed-format Excel or PDF. PDF output is landscape, paginated, and includes the report period, control status, rule version, totals, generation time, and page numbers.

## Bringing existing data to a new PC

**Create a new empty ETP database if this PC has none** is ticked by default. To carry on with an existing ETP database on a new PC instead:

1. Run setup elevated with the SQL Server option ticked and **Create a new empty database unticked**. Setup prepares SQL Server and the protected configuration, creates no database, and says so. It ends with exit code 0 and leaves `DATABASE-RESTORE-PENDING.txt` in the installation folder.
2. In an elevated PowerShell window, restore the backup. Add `-ReceiptPath "<the .bak.receipt.json>"` if you have the receipt:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\restore-etp-database.ps1" -BackupPath "<full path of the .bak>"
   ```

   It verifies, restores and checks the backup, and makes the Windows account running it the database's Owner (`docs/OPERATIONS.md`, Moving the live database to a new PC). Restore only your own ETP backup: the helper and the next setup run execute the database's own code as a SQL administrator.
3. Run setup again. It takes a verified safety backup of the restored data before it applies the newer database updates, installing the operations broker that backup needs if it is missing.
4. Start ETP with **Run as administrator** and, as Owner, add `<PC>\EtpAutomation` as an active Store Manager in Settings > Users. Deactivate the old PC's accounts there too (untick Active, give a reason).
5. Run setup once more. It installs the SQL operations module for `<PC>\EtpAutomation` (`docs/OPERATIONS.md`, deployment step 7), without which the daily backup and the monthly recovery drill cannot run. Its log says when that is done, or what is still missing.

## Prerequisite

Unless setup installs it (the SQL Server option above), install Microsoft SQL Server with the `SQLEXPRESS` instance and enable Windows authentication for the Windows user running the application. The application is self-contained; a separate .NET runtime is not required.

Express is supported and is the usual choice for a single shop PC. Note one consequence: Express and Web refuse `BACKUP ... WITH ENCRYPTION`, so backups on those editions are taken unencrypted, and their receipts record that plainly rather than claiming encryption. The backup folder policy is then the only thing protecting them at rest. An encrypted backup requires Standard, Developer or Enterprise. See `docs/OPERATIONS.md`.

## Start and configure

1. Run `Etp.Reporting.Desktop.exe` from the release folder.
2. Open **Settings**.
3. Confirm or edit `Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;Encrypt=Optional`.
4. Select **Create/update database**. The application creates the database when absent and applies checksum-controlled migrations.

## Import order

Use **Import ETP** to validate and import each workbook. Import these four exports for each store:

1. `SDB-VariantwiseSales` — item-level sales (`NETVALUE`, including GST).
2. `Revenue Report` — authoritative invoice and tender control.
3. `Variant Stock ledger` — source-signed stock movements.
4. `Closing Stock` — authoritative closing snapshot and product/brand-segment attributes.

Exact duplicate files are rejected. Unknown layouts, mismatched repeated halves, invalid stock equations and unapproved transaction types block persistence. `PAYMENTTYPE25` is retained as quarantined evidence and excluded from reports.

## Reports

Open **Sales Reports** or **Stock Reports**, select an inclusive date range, and run the required report. Sales reports use source-signed `NETVALUE`: `INV` is an invoice and negative `SR` values remain negative. Tender reconciliation uses Revenue Report invoice controls. Stock reconciliation compares the first ledger opening plus source-signed period movements with the closing snapshot for products present in both sources. After running a report, select **Export Excel…** to save the same result grid, totals, period, rule version and control status as a fixed-format `.xlsx` workbook.

## Build a release

From PowerShell at the repository root:

```powershell
.\scripts\build-windows-release.ps1
```

The self-contained application and checksum are written to `artifacts/windows-release`. Production deployment still requires an approved SQL Server backup/restore process and code-signing policy.
