# Verification of the protected build

Lane V1 of the code-protection plan. This file says how a protected (obfuscated) ETP release is proven to behave like the unprotected one, how fast it is, what a decompiler still shows, and how to read a crash from a protected build. The result tables at the end are filled in per release; until the first protected release (1.13.0) they are empty on purpose.

Nothing here runs in the gate, in CI or in the developer's inner loop. Sagar runs the scripts, elevated, on Workpc or the VM, after `build-windows-release.ps1 -Protect` has produced the folders (contract: protected publish in `<OutputDirectory>`, unprotected publish in `<OutputDirectory>-unprotected`, symbols and `Mapping.txt` in `<OutputDirectory>-symbols`, archived to `E:\ETP\Reference\Symbols\<version>-<sha7>\`).

## The four scripts (all in `scripts\release`)

| Script | Question it answers | Exit codes |
|---|---|---|
| `Test-EtpProtectedBuild.ps1` | Do the protected and unprotected builds produce identical results on identical fixtures? | 0 identical and complete; 1 a difference; 2 refused; 3 incomplete |
| `Measure-EtpPerformance.ps1` | Is the protected build more than 5 % (drop string hiding) or 10 % (withhold obfuscation) slower? | 0 within limits; 4 over 5 %; 5 over 10 %; 2 refused (fewer than 3 runs is refused); 3 incomplete |
| `Compare-EtpDecompilation.ps1` | What can a reader still see with a decompiler, before and after? | 0 report written; 1 `-Strict` threshold missed; 2 refused (ilspycmd missing or not the pinned version); 3 incomplete |
| `Resolve-EtpStackTrace.ps1` | What were the real names in this stack trace from a protected build? | 0 done; 1 frames present but none resolved; 2 refused |

Self-test of all four (no SQL, no ilspycmd, no exe, temp folder only): `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\tests\Verification.Tests.ps1`. It proves, among other things, that the comparer fails when one Excel cell, one checksum or one PDF content digit differs, that a folder called "protected" that is byte-identical to the unprotected one is refused, and that the performance script refuses fewer than three runs.

## Running the regression comparison

```
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\Test-EtpProtectedBuild.ps1 `
  -Protected <OutputDirectory> -Unprotected <OutputDirectory>-unprotected `
  -FixtureFolder tests-dotnet\fixtures\etp-sample -Database EtpAccept_V1
```

Elevated, from a clone of the commit being released, with the .NET 10 SDK on PATH (it compiles a small probe host). Run it under the heavy-job lock like any other build, not while a gate is running. Output: `%TEMP%\EtpProtectedBuild-<time>\protected-build-report.md` (or `-OutputFolder`).

Scratch databases are `EtpAccept_V1_U` (unprotected) and `EtpAccept_V1_P` (protected). The script refuses any `-Database` that does not match `^EtpAccept_[A-Za-z0-9_]+$`, refuses a non-local SQL Server, refuses if either scratch database already exists, creates them only by running the shipped exe, and drops only those two at the end (`-KeepDatabases` leaves them for inspection; drop them by hand afterwards). `EtpReporting` is never opened.

## What is driven, and what could not be

Driven for real, against each build's own files:

1. `Etp.Reporting.Desktop.exe --initialize-database --connection-string <scratch>`: creates the database and applies every migration and schema object from the build's `database\` folders.
2. `Etp.Reporting.Desktop.exe --plan-database-changes --connection-string <scratch>`: must print `{"pendingMigrations":[],"pendingObjects":[]}`. The JSON itself is compared between builds.
3. Import of `tests-dotnet\fixtures\etp-sample` (32 artificial workbooks) through `FolderImportService`, the same code path as the folder import in the UI and as ImportAudit `seed`. Per file: report code, store, period, status, row counts and message are compared.
4. Fact tables: every user table in the scratch database gets `COUNT_BIG(*)`, `CHECKSUM_AGG(BINARY_CHECKSUM(columns))` and a `SUM` of `CHECKSUM` over the columns that are not excluded. The report lists, per table, how many columns were compared.
5. Report data: `DailyReportingPackService.GenerateCombinedAsync` for the latest business date in the fixtures (or `-Date`), serialised with `ReportPackArchiveCodec` and hashed (`pack.json`).
6. DSR/pack Excel: `OpenXmlReportPackExporter` output read cell by cell (sheet name, cell address, text, formula).
7. DSR/pack PDF: `SimplePdfReportPackExporter` output; page count and a hash of the whole file with every Flate stream decoded and replaced by the hash of its bytes.
8. Health: `DBCC CHECKDB` clean on both databases, applied-migration count equal, plan JSON equal.

Why a probe host: the shipped exe has no headless verb for import or export. Its only headless verbs are `--initialize-database`, `--initialize-configured-database`, `--automation-once` and `--plan-database-changes`. So the script generates a tiny console project in `%TEMP%`, compiles it once against the unprotected folder's public API and runs it twice, loading the Etp assemblies of the build under test from that build's folder. This also proves the plan's assumption that the public API survives protection (`KeepPublicApi`): if a protected assembly lacks a public member the host needs, the run fails with a missing-method error and the result is INCOMPLETE (exit 3), never a pass.

Could not be driven, and why (each is a documented gap, not silently skipped):

- `--automation-once`: it reads the machine-wide `%ProgramData%\EtpReporting\Operations\operations.json`, which names the live database. Pointing it at a scratch database would mean rewriting that file, which the boundaries forbid. Its import step is covered by item 3; its nightly health step is not.
- The in-app health check (`HealthCheckRunner`): internal to the Desktop assembly, reachable only from `--automation-once` or the UI. Health is covered by `CHECKDB`, the migration count and the plan JSON instead.
- `--capture-review` (screenshot session): it refuses any database that is not `EtpPhase1Test_*`, so it cannot be used under the `EtpAccept_*` rule of this plan.
- The WPF UI, the export dialogs and the Dashboard PDF: `scripts\test-windows-ui.ps1` starts the exe with no arguments, so it opens whatever database the machine's `settings.json` names. It stays a separate, manual step on the VM (the 43-step sign-off and HealthNightOne are the full UI regression). `Measure-EtpPerformance.ps1` does start the real exe with `--connection-string <scratch>` to time the main window.
- Per-store reports, Tally XML, evening and service-centre exports: not exercised; only the combined daily pack is.
- Nothing is executed from a single-file bundle. Both publishes must be folder publishes (decision D4); the script refuses otherwise.

## Excluded as nondeterministic (the explicit list)

Everything not on this list must be identical. The same list is printed in every report.

| Item | Why it is left out |
|---|---|
| SQL columns of type `timestamp`/`rowversion`, `uniqueidentifier`, `datetime`, `datetime2`, `smalldatetime`, `datetimeoffset`, `xml`, `text`, `ntext`, `image`, `geography`, `geometry`, `hierarchyid`, `sql_variant` | clock stamps and generated ids differ per run; `BINARY_CHECKSUM` cannot take the rest. `date` columns (business dates) are compared. If a fact table keeps a business value in a `datetime2` column, it is not checksummed: the report's `cols=K/M` shows it, and the column should be added by hand to a follow-up check |
| Tables whose name matches `(^|_)(log|logs|audit|diagnostic|diagnostics|heartbeat|lease|session|sessions|health_check_run|health_check_runs)($|_)` | compared by row count only |
| `docProps/core.xml`, `docProps/app.xml` in the xlsx | created/modified time and application name |
| PDF `/CreationDate`, `/ModDate`, `/ID`, everything after the last `xref` | clock stamps and byte offsets |
| `ReportPackDocument.GeneratedUtc` | the probe host replaces it with 2000-01-01T00:00:00Z in both builds before export, so no "generated at" text exists in the files |
| Batch ids and timings in the import summary | not written to the compared summary |

PDF text is not extracted: no text-extraction library ships with the product and PDFsharp subsets embed glyph ids, so the comparison is of the decoded page content streams, which is stricter than text. If a PDF differs, the report names the page count or the content digest; open both PDFs from `<OutputFolder>\unprotected\probe\pack.pdf` and `...\protected\probe\pack.pdf` to see where.

## Decompilation comparison

`Compare-EtpDecompilation.ps1` uses `ilspycmd` pinned at 11.1.0 in `E:\Tools\ilspycmd` (install once, by hand: `dotnet tool install ilspycmd --version 11.1.0 --tool-path E:\Tools\ilspycmd`). The script never downloads it and refuses if it is missing or a different version. It decompiles each ETP assembly from both folders and reports: readable non-public type and method names, the four named services (`SalesReportingService`, `StockReconciliationService`, `InvoiceTenderReconciliationService`, `DailyReportingPackService`) and their visibility, plain SQL string literals, and shipped `.pdb` files. Expected honest result: non-public names and SQL text gone; public types (including services declared `public`) still findable by name because `KeepPublicApi` keeps them; structure and arithmetic still readable; the SQL files untouched. The report states these limits itself.

## Performance

`Measure-EtpPerformance.ps1 -Runs 3` (three is the minimum; five is better) interleaves unprotected and protected runs and reports medians of: cold start to main window, fixture import, daily pack generation, Excel plus PDF export. A difference under 50 ms counts as equal. Verdicts: over 5 % = `DROP-STRING-HIDING` for the slow assembly; over 10 % = `WITHHOLD-OBFUSCATION` for the release. The result is `performance-<label>.json`. The cold-start figure is a warm-file-cache start (the OS cache cannot be cleared from a script); compare protected against unprotected, not against a boot. It needs an interactive desktop session. ReadyToRun can be measured by passing a third folder as `-Protected`; it is not a protection and is off by default.

## Reading a crash from a protected build

`Resolve-EtpStackTrace.ps1 -SymbolsFolder E:\ETP\Reference\Symbols\<version>-<sha7> -TraceFile crash.txt` rewrites `at A.b(...)` frames and any renamed type name back to the originals using the release's `Mapping.txt`. The parser follows Obfuscar's documented text format but was written before any real map existed; the first real `Mapping.txt` from lane O1 is the test. If it resolves nothing, fix `ConvertFrom-EtpObfuscarMapping` and extend the sample map in `Verification.Tests.ps1`.

## Results per release

Fill in at integration; keep the old rows.

| Release | Commit | Date | Regression (`Test-EtpProtectedBuild`) | Performance (`performance-<label>.json`) | Decompilation report | Notes |
|---|---|---|---|---|---|---|
| 1.13.0 | | | | | | not yet run |
