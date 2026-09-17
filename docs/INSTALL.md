# Windows Quick Start

For the packaged release, run the versioned `EtpReportingEngine-Setup-<version>-x64.exe` as administrator.

**The SQL Server option appears only when the installer was built with SQL media embedded** (`build-windows-installer.ps1 -SqlPayloadDirectory`). P4-11 recorded why: the option used to be offered on every build, did nothing, and named an edition that was not what it installed. When the option is present, keep **Install and configure Microsoft SQL Server Express** selected and setup installs the engine from the media inside the installer, with no internet access required. When it is absent, install SQL Server yourself first as described under Prerequisite.

Setup then configures automatic startup, initializes `EtpReporting`, prepares backup access, and registers the daily backup, monthly recovery-drill and five-minute ETP automation tasks. It creates a Start Menu entry, and supports upgrades and uninstall through Windows Installed Apps; uninstall removes the ETP tasks but never removes SQL Server, databases, sources, reports or backups.

If the release was built without a signing certificate, which A4.3 accepts, Windows will warn that the publisher is unverified. Confirm the SHA-256 in `SHA256SUMS.txt` beside the installer before running it.

The SQL connection is saved for the current Windows user after a successful connection test and is checked automatically at startup. The Dashboard shows aggregate import status and recent import history. No database password is stored by the default Windows-integrated connection.

Reports can be exported to fixed-format Excel or PDF. PDF output is landscape, paginated, and includes the report period, control status, rule version, totals, generation time, and page numbers.

## Prerequisite

Install Microsoft SQL Server with the `SQLEXPRESS` instance and enable Windows authentication for the Windows user running the application. The application is self-contained; a separate .NET runtime is not required.

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
