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

Interactive connection settings are saved in `%LOCALAPPDATA%\EtpReporting\settings.json`. The default database is `EtpReporting`. Scheduled operations use the protected machine configuration in `%ProgramData%\EtpReporting\Operations\operations.json`. Maintenance scripts and migrations accompany build output.

SQL integration tests create and drop their own uniquely named database. They never target the shop database. Set `ETP_TEST_SQL_CONNECTION` to use another local SQL instance; the default is Windows authentication on `.\SQLEXPRESS`.

## Reference

- [Installation](docs/INSTALL.md)
- [Database schema](docs/03_DATABASE_SCHEMA.md)
- [Import profiles](docs/04_ETP_IMPORT_PROFILES.md)
- [Mapping register](docs/05_MAPPING_REGISTER.md)
- [Approved rebuild plan](docs/audit/ETP-MASTER-AUDIT-AND-PHASED-PLAN.md)
- [Security and operations setup](docs/OPERATIONS.md)
- [Phase 4 implementation and validation](docs/audit/claude-audit-2026-09/PHASE-4-REPORT.md)

This branch implements Phase 4 security and operations from the Phase 0 baseline. Phase 1 financial/import corrections must be integrated separately. Phase 4 deployment requires a SQL edition supporting native encrypted backups, a signing certificate, and the folder-access decision described in Operations. SQL Express supports the local development tests but cannot create the approved native encrypted backups. Older design and process documents are retained under `docs/_archive-2026-09/` for reference.
