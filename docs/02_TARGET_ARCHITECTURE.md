# Target Architecture

## Product boundary

The target is a Windows desktop rules-driven reporting engine. ETP files are inputs, SQL Server Express is the structured source of truth, deterministic business services calculate metrics, and Windows/Excel/PDF renderers are output channels.

## Technology direction

- .NET 10 LTS codebase using C#, matching the installed supported toolchain.
- WPF desktop shell for mature Windows deployment and straightforward operational grids/forms.
- Generic Host for dependency injection, configuration, logging and background services.
- SQL Server Express through `Microsoft.Data.SqlClient`; migrations are explicit versioned SQL scripts executed by a small migration runner.
- Open XML SDK for workbook ingestion/export foundations. A higher-level MIT-licensed wrapper may be evaluated after real workbook tests.
- xUnit for unit/integration tests; Testcontainers or a configured local SQL Express instance for database integration tests.

The domain and application layers must not reference WPF, Excel rendering or SQL implementation classes.

## Logical layers

1. **Desktop Presentation** — navigation, file selection, import progress, diagnostics and report grids.
2. **Application** — use cases such as identify file, import batch, generate report and manage masters.
3. **Import** — readers, workbook preflight, profile matching and typed source-row extraction.
4. **Normalization** — maps source fields into canonical commands and rejects unsafe ambiguity.
5. **Domain** — business definitions, money/quantity semantics, comparison periods and reconciliation rules.
6. **Infrastructure.SqlServer** — transactions, repositories, migrations, bulk loading and report query implementations.
7. **Reporting** — fixed report definitions, measures, filters, controls and renderer-neutral result models.
8. **Exports** — Excel and later PDF formatters; never calculation engines.

## Proposed solution structure

```text
src/
  Etp.Reporting.Desktop/
  Etp.Reporting.Application/
  Etp.Reporting.Domain/
  Etp.Reporting.Import/
  Etp.Reporting.Infrastructure.SqlServer/
  Etp.Reporting.Reporting/
  Etp.Reporting.Exports.Excel/
tests/
  Etp.Reporting.Domain.Tests/
  Etp.Reporting.Import.Tests/
  Etp.Reporting.SqlServer.Tests/
  Etp.Reporting.Reporting.Tests/
database/
  migrations/
  seeds/
test-data/golden/
```

## Import transaction

1. Hash and register the selected file as a pending `import_file`.
2. Detect candidate profile by report code, sheet and normalized header signature.
3. Parse cells into typed source values without business aggregations.
4. Map into canonical staging records with source row lineage.
5. Validate blockers and warnings.
6. Resolve controlled masters and transaction classifications.
7. Insert/update canonical facts inside one SQL transaction.
8. Execute control-total and duplicate checks.
9. Commit the batch as `Completed`; otherwise roll back canonical changes and retain bounded diagnostics.

Known layouts are configured once and reused. Unknown layouts stop before canonical writes.

## Reporting architecture

Each predefined report has an ID, parameter contract, query service, formula definitions, result schema and reconciliation control. SQL performs set-based filtering and aggregation; centralized domain/application services handle definitions that cannot safely live in SQL. UI and export render the same report-result model.

LY/TY comparison is represented by an `IComparisonPeriodResolver`. No report may calculate its own comparison dates. Stock movement signs are controlled by effective-dated transaction-type master data and one stock-balance service.

## Deployment and operations

The installer will deploy the desktop app and check for a named SQL Server Express instance. Configuration uses Windows-protected local settings and a least-privilege database login or Windows authentication. Database upgrades run before application startup and are recorded in `schema_migrations`. Backup invokes SQL Server backup to a configured local/business-controlled folder with retention handled by an operational policy.

## Boundaries deliberately excluded from MVP

- Generic drag-and-drop BI design.
- AI-dependent mappings or calculations.
- Android/WebView compatibility.
- Complex approval workflow not required to protect canonical data.
- PDF output until a report explicitly requires it.
