# ETP Reporting Engine

Windows reporting app for Saagar Traders: Titan World and Helios, Latur. Built with .NET 10 WPF and SQL Server Express, it imports ETP workbooks and provides sales, stock, tender and daily workflow reports with Excel/PDF export.

## Build and run

Requires Windows, .NET SDK 10.0.400 or compatible .NET 10 SDK, and SQL Server Express (`.\SQLEXPRESS`).

```powershell
dotnet restore Etp.Reporting.slnx
dotnet build Etp.Reporting.slnx -c Release
dotnet test Etp.Reporting.slnx -c Release
.\src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\Etp.Reporting.Desktop.exe
```

Connection settings are saved in `%LOCALAPPDATA%\EtpReporting\settings.json`. The default database is `EtpReporting`. Use `--connection-string` to launch an isolated review session without changing saved settings. Maintenance scripts and migrations accompany build output.

SQL integration tests create and drop their own uniquely named database. They never target the shop database. Set `ETP_TEST_SQL_CONNECTION` to use another local SQL instance; the default is Windows authentication on `.\SQLEXPRESS`.

## Import ETP data

Open **Import today's folder**, choose the export folder or ZIP, and start. Store and date range come from the workbook; keep its Info sheet. Each file shows its result and source-row counts. Identical files and content subsets add no reporting facts. Changed overlapping periods require an explicit restatement.

The audit tool can create a disposable database and run the same folder service:

```powershell
dotnet run --project tools/Etp.Reporting.ImportAudit -c Release -- --database EtpReportingHelios --rebuild --folder "C:\Codex\Reporting Manger\ETP Source Data\HEMW\till 6 sep 26"
```

`--rebuild` deletes only the named audit database. The tool refuses the live `EtpReporting` database. Only `EtpReportingHelios` and names beginning `EtpPhase1Test_` are allowed. Repeat `--folder` to import both stores in one action. Private corpus tests run when the supplied folders exist; CI uses the artificial samples under `tests-dotnet/fixtures/etp-sample/`.

## Reference

- [Installation](docs/INSTALL.md)
- [Database schema](docs/03_DATABASE_SCHEMA.md)
- [Import profiles](docs/04_ETP_IMPORT_PROFILES.md)
- [Mapping register](docs/05_MAPPING_REGISTER.md)
- [Approved rebuild plan](docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md)

Phase 1 data-truth work is ready for the independent audit described in [PHASE-1-REPORT](docs/audit/claude-audit-2026-09/PHASE-1-REPORT.md). Sales value uses GST-inclusive NETAMOUNT; NETVALUE and tax remain separate. The shop-sheet report layouts and targets UI are Phase 2 work. Older design and process documents are retained under `docs/_archive-2026-09/` for reference.
