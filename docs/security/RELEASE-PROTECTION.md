# Release protection: obfuscation, symbols and the protected build

Code-protection plan (15 Oct 2026), sections 4.2 and 4.3; decisions D3 (Obfuscar on the non-UI assemblies, Desktop
lightly or not at all, installer build only) and D4 (folder publish instead of single-file). Lane O1.

This file contains no secret. The obfuscation map and the symbols are private and are never put in the repository.

## 1. The rule

- **Only the installer build is protected.** `E:\ETP\Migration 2026-10-04\Build-Installer.ps1` calls
  `scripts\build-windows-release.ps1 -Protect`. Nothing else passes `-Protect`.
- `dotnet build`, `dotnet test`, the fast gate, the elevated gate's test steps, lane builds and CI are unchanged. No csproj,
  no `Directory.Build.props`, no MSBuild hook, no `[Obfuscation]` attribute in the source.
- Claude and developers read and edit plain source. If a protected build misbehaves, **reproduce it on the unprotected
  publish first** (`<OutputDirectory>-unprotected`, same commit, same build): if it happens there too, it is an ordinary
  bug; if not, it is an obfuscation exclusion that is missing (section 5).
- A failed obfuscation fails the build. There is no fallback to an unprotected release labelled protected.
- PDBs and the map are never shipped and never placed in `E:\ETP\Installers\<ver>\`, the USB-KIT or OneDrive.

## 2. What `build-windows-release.ps1 -Protect` does

Same as without `-Protect` up to and including the seven test projects. Then:

| Step | Without `-Protect` (unchanged) | With `-Protect` |
|---|---|---|
| Tool check | - | before the build: the pinned Obfuscar is found and its package hash verified, and `obfuscar.xml` loads; otherwise refused at once |
| Publish | single-file, `-o <OutputDirectory>` | **folder** publish, `-o <OutputDirectory>-unprotected` |
| S-13 natives check, scripts, OneDrive scripts, notices, SQL, database files | into `<OutputDirectory>` | into `<OutputDirectory>-unprotected` |
| Obfuscation | - | `<OutputDirectory>-unprotected` is copied to `<OutputDirectory>`; `scripts\release\Invoke-EtpObfuscation.ps1` rewrites the ETP DLLs and copies them over |
| PDBs | shipped beside the exe (as before) | moved out of `<OutputDirectory>` into `<OutputDirectory>-symbols\original-pdb` |
| Signing | exe + scripts (thumbprint) | after obfuscation: exe + `Etp.Reporting.*.dll` + scripts (thumbprint, or `-PfxPath` per contract 5.1) |
| `SHA256SUMS.txt` | the exe | the exe and every `Etp.Reporting.*.dll` |
| `release.json` | as before | as before plus `protection` (`obfuscated`, tool, profile, desktop mode, `layout: folder`, `pdbShipped: false`, map SHA-256) |

Output folders (frozen contract 5.1):

```
<OutputDirectory>               protected release - the only folder Inno packs
<OutputDirectory>-unprotected   same build, not obfuscated; NOT-FOR-RELEASE.txt inside; the smoke's reference (lane V1)
<OutputDirectory>-symbols       private: Mapping.txt, obfuscation-report.json, obfuscar.filled.xml, obfuscar.log,
                                original-pdb\ (PDBs of the unprotected DLLs), protected-pdb\ (PDBs Obfuscar regenerated)
```

Then (outside this script) the protected-build smoke compares the two folders (`Test-EtpProtectedBuild.ps1`, lane V1), the
installer is built from `<OutputDirectory>`, and `scripts\release\Archive-EtpSymbols.ps1` copies the symbols to
`E:\ETP\Reference\Symbols\<version>-<sha7>\` (refuses `E:\ETP\Installers`, OneDrive and the release folder; never
overwrites; writes and checks `SHA256SUMS.txt`). The nightly copy of `E:\ETP\Reference` to G: carries it from there.

Options (only with `-Protect`):

- `-ObfuscationProfile Standard` (default): `KeepPublicApi=true`. Private and internal types and methods renamed, public API
  names kept, strings hidden. **Honest note:** most ETP types are `public` (about 1,170 public against about 100 non-public type declarations in
  the six non-UI assemblies), so with Standard, names such as `SalesReportingService` stay readable; what is hidden are the private
  helpers, method bodies' string literals (SQL text, messages) and, with no PDBs, file names, line numbers and local names.
- `-ObfuscationProfile Strong`: `KeepPublicApi=false`. Public types and methods of the six non-UI assemblies are renamed as
  well; the exclusions in section 5 already cover every reflection and JSON case found in the code. Needs Desktop in the
  project (`-DesktopObfuscation Strings`), because Desktop's calls into the six assemblies must be rewritten. Use only after
  the protected-build smoke has passed with it; the main risk is an interface implemented in Desktop (whose methods are
  never renamed) declared in a renamed assembly: Obfuscar must keep such method groups together, or the program fails at
  start with a TypeLoadException, which the smoke shows at once.
- `-DesktopObfuscation Strings` (default): Desktop is in the Obfuscar project with **every** namespace skipped for renaming;
  only string hiding may apply. `None`: Desktop is left exactly as published (Standard only).
- `-ObfuscarRoot` (default `E:\Tools\Obfuscar`).

## 3. The tool

| | |
|---|---|
| Tool | Obfuscar.GlobalTool 2.2.50 (stable line; MIT; https://github.com/obfuscar/obfuscar). 3.0.0-beta.x only if 2.2.50 fails the smoke |
| Location | `E:\Tools\Obfuscar\2.2.50\obfuscar.console.exe` (a `dotnet tool install --tool-path` folder) |
| Pin | `$script:EtpObfuscarVersion` and `$script:EtpObfuscarPackageSha256` in `scripts\release\Invoke-EtpObfuscation.ps1` |
| Package SHA-256 | **not yet recorded** - the lanes have no internet. The integrator fills it in at install time (below) |
| Runtime | 2.2.x targets an older .NET than the SDK on Workpc; the script sets `DOTNET_ROLL_FORWARD=Major` for the one call |

### Installing the tool (integrator, once per version)

```
dotnet tool install Obfuscar.GlobalTool --version 2.2.50 --tool-path E:\Tools\Obfuscar\2.2.50
Get-FileHash E:\Tools\Obfuscar\2.2.50\.store\obfuscar.globaltool\2.2.50\obfuscar.globaltool\2.2.50\obfuscar.globaltool.2.2.50.nupkg -Algorithm SHA256
```

Compare the hash with the package on nuget.org (or `dotnet nuget verify --all` on the .nupkg; it is signed by nuget.org),
then write it into `$script:EtpObfuscarPackageSha256` and into the table above, in the same commit. Until then
`-Protect` refuses and prints the hash it found. The build never downloads anything. Changing the version = new folder,
new hash, a protected-build smoke and a VM run.

## 4. Configuration (`scripts\release\obfuscar.xml`)

| Setting | Value | Why |
|---|---|---|
| KeepPublicApi | true (Standard) / false (Strong) | section 2 |
| HidePrivateApi | true | rename non-public types and methods |
| RenameProperties | false | WPF bindings, DataGrid auto columns, `TypeDescriptor.GetProperties` (TablePresentation, PageSearch), `GetProperty("Area"/"Code"/"Variance")` (ReportDetailFilter, ReportGridColumns, ReportsWorkspaceView), System.Text.Json |
| RenameEvents | false | WPF |
| RenameFields | false | enum members are parsed and serialised by name: `JsonStringEnumConverter` (EtpReportFamilyRegistry), `Enum.Parse/TryParse` (DiagStoreSupport, ImportSourceLocationStore, SqlServerHealthCheckStore, AuditCommandLine, ErrorRegisterView, ReportsWorkspaceView), enum text sent to SQL |
| HideStrings | true | SQL text, column names, messages; decoded at run time |
| SkipGenerated | true | anonymous types serialised by System.Text.Json (DailyReportingPackService, Tally services, ShopResult, SchemaObjects, SqlServerImportHistoryQuery), async state machines, closures, `[GeneratedRegex]` code |
| UseUnicodeNames, UseKoreanNames | false | ASCII names pass `DesktopDiagnostics.SafeFrame`, the error register's token allow-list `[A-Za-z0-9._:<>+-]` and the `diag` CHECK constraints unchanged |
| ReuseNames | false | |
| OptimizeMethods | true | |
| RegenerateDebugInfo | true | PDBs that match the protected DLLs, for the archive only |

## 5. Exclusions (derived from the code at 8c634449, 15 Oct 2026)

Each keeps a name; none changes behaviour. `Obfuscation.Tests.ps1` fails if an excluded type no longer exists in `src`, or
if a namespace is declared under `src\Etp.Reporting.Desktop` without being skipped.

### Needed in both profiles

| Type (assembly) | Kept | Reason (code) |
|---|---|---|
| `SqlServerImportHistoryQuery.StoredIssue` (SqlServer, private record) | type, constructor with parameter names | `JsonSerializer.Deserialize<StoredIssue[]>` (SqlServerImportHistoryQuery.cs:191) binds constructor **parameter names** to properties; Obfuscar removes parameter names of renamable methods. Without it, reading import history throws in the protected build only |
| `SqlServerTallyProfileService.StoreCostCentre` (SqlServer, private record) | same | `Deserialize<StoreCostCentre[]>` (SqlServerTallyProfileService.cs:153), Tally profile cost centres |
| `ExpectedRolePermissionMatrix.MatrixFile` (SqlServer, private class) | same | `Deserialize<MatrixFile>` of the embedded ExpectedRolePermissions.json (SecurityProbes.cs:82, health SEC-01..03). Default constructor, so already safe; belt and braces |
| `AuditRunner.AuditDatabaseException`, `PlannerOnePredictor.PackageCaptured` (SqlServer, private exceptions) | type name | not strictly needed (both are caught by type, AuditRunner.cs:63, PlannerOnePredictor), kept so that a log or debug dump that names them (`WriteDebugAsync`, the "internal error (X)" line, AuditRunner.cs:97) stays readable. Cheap; no protection value lost |
| Every Desktop namespace + `XamlGeneratedNamespace` (Desktop) | everything (types, methods, fields, properties, events) | Obfuscar does not analyse BAML. XAML refers to Desktop types by name (`x:Class`, `clr-namespace:Etp.Reporting.Desktop` and `.Modules.Reports`, converters, controls); WPF finds `XamlGeneratedNamespace.GeneratedInternalTypeHelper` by reflection; System.Text.Json reads `DesktopConnectionSettings`, `UiPreferences`, `DesktopDiagnosticEntry` through constructors; `[JsonConverter(typeof(UiDensityJsonConverter))]`; `GetType().Name` in FocusedTaskLayout and ReportProbes |

### Needed only in the Strong profile (public, so already kept by Standard)

| Type | Kept | Reason |
|---|---|---|
| `Application.Reports.ReportStatus`, `Reporting.ReconciliationStatus` | type name | Desktop TablePresentation.cs:131 compares `member.GetType().Name` with "ReportStatus" / "ReconciliationStatus" |
| `UpdateAttemptRecord` (Application) | constructor parameter names | attempts.jsonl, `UpdateAttemptJournal.cs:76` |
| `ImportIssue` (Application) | same | `Deserialize<ImportIssue[]>` (SqlServerImportHistoryQuery.cs:193) |
| `EtpReportFamily`, `EtpSourceColumn`, `EtpFamilyIdentity` (Import) | same | embedded EtpReportFamilies.json, `EtpReportFamilyRegistry.Parse` |
| `ExpectedRolePermission`, `SmtpCredential` (SqlServer) | same | SecurityProbes.cs:82; WindowsSmtpCredentials.cs:17 |
| `ReportPackDocument`, `ReportPackTable`, `ExcelReportData`, `ExcelReportColumn` (Reporting) | same | archived report packs, `ReportPackArchiveCodec.Deserialize` (Phase2OperationsRepository.cs:344) |
| Exception types `NoSaleDayRefusedException`, `AuditInputException`, `ImportConflictException`, `ImportSourceException`, `StatementLayoutException`, `ImportCommittedException`, `MigrationIntegrityException`, `SchemaObjectIntegrityException`, `SchemaObjectsNotUpToDateException` | type name | exception type names are recorded: `ImportFailure` type (BatchImportCoordinator.cs:94), diagnostics `ExceptionType`, error-register fingerprints, health findings |

### Checked and found safe without an exclusion

- **Embedded resources** are loaded by literal names (`Etp.Reporting.Import.Profiles.EtpReportFamilies.json`,
  `...SqlServer.Audit.check-import-upgrade.sql`, `...SqlServer.HealthCheck.ExpectedRolePermissions.json`); Obfuscar does not
  rename non-`.resources` resources. No `.resx` in the product. Help screenshots use `pack://application:,,,/Etp.Reporting.Desktop;component/...` (assembly name, never renamed).
- **Assembly names** are never renamed: `DesktopFriendlyError.IsProductMessage` (`Assembly.GetName().Name.StartsWith("Etp.")`), `GetCustomAttribute<AssemblyInformationalVersionAttribute>` (DesktopProductVersion, UpdatePaths, SchemaObjects, AuditReport).
- **InternalsVisibleTo** (SqlServer, Reporting, Desktop -> test projects only): tests never run against the protected DLLs; no product assembly uses another's internals.
- **System.Text.Json serialisation** (no deserialisation): property names are kept, so the JSON written is identical. No `JsonSerializerContext`, `JsonDerivedType` or `JsonPolymorphic` in the product.
- **No DI container** (DesktopCompositionRoot is hand-wired), no `Activator.CreateInstance` of ETP types (only the COM `Schedule.Service`), no `Type.GetType("...")`, no `Assembly.Load`, no Dapper-style mappers: SqlClient maps by column-name strings.
- **`nameof`, `CallerMemberName`, `CallerArgumentExpression`** are compile-time constants.
- **`[GeneratedRegex]`** (38 uses): plain IL calls, no reflection.
- **PowerShell scripts** never load ETP DLLs.
- `exception.GetType().Name` elsewhere names BCL/SqlClient exceptions (never renamed) or the kept types above.

## 6. Consequences for diagnostics (read before supporting a protected release)

- Renamed private types and methods appear renamed in the diagnostics log (`stack`, `exceptionType`) and in the error
  register. `Resolve-EtpStackTrace.ps1` (lane V1) translates them with the archived `Mapping.txt` of that release.
- `ErrorFingerprint.NormaliseFrame` keeps only frames whose type starts with `Etp.`. If Obfuscar moves a renamed type out
  of its namespace, those frames are dropped and the fingerprint's `Site` falls back to the next public ETP frame. Nothing
  is rejected (ASCII names pass every token check), but **fingerprints of the same fault differ between an unprotected and
  a protected build, and can differ between two protected releases**. Entries in `Diagnostics\ErrorFixManifest.json`
  must be fingerprinted from the protected build that the fix applies to.
- Reproduce on `<OutputDirectory>-unprotected` first (section 1).

## 7. What obfuscation does and does not do

Does: strips the names Claude and the compiler gave to private classes, methods and variables, and hides the text of SQL
statements and messages inside the program so a decompiler shows `a.b(c)` instead of
`SalesReportingService.Summarize(lines)`. Combined with no PDBs, a reader must work for days instead of minutes to
understand the C# half.

Does not: hide the structure or the arithmetic (control flow is untouched); hide anything in SQL files, the database,
PowerShell scripts, `ErrorFixManifest.json` or the help screenshots; stop anyone copying or running the program; stop a
determined expert. It also does not change a single number in a report - the smoke proves that per release.

## 8. Per-release checklist (protected build)

1. Obfuscar at the pinned version and hash under `E:\Tools\Obfuscar\<version>\` (section 3).
2. `Build-Installer.ps1` with `-Protect` (lane G1); the build prints the three folders.
3. Protected-build smoke passes (`Test-EtpProtectedBuild.ps1 -Protected <out> -Unprotected <out>-unprotected ...`).
4. `<out>` contains no `*.pdb`, no `Mapping.txt`, no `NOT-FOR-RELEASE.txt`; `release.json` has `protection.obfuscated: true`.
5. `Archive-EtpSymbols.ps1 -SymbolsDirectory <out>-symbols -ReleaseDirectory <out>` -> `E:\ETP\Reference\Symbols\<version>-<sha7>\`.
6. Decompiler comparison and performance results recorded in `docs\security\VERIFICATION.md` (lane V1).
7. VM acceptance on the protected installer.
8. Delete `<out>-unprotected` and `<out>-symbols` from `artifacts` after the archive is verified.

Self-test of the scripts: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\release\tests\Obfuscation.Tests.ps1`
(no Obfuscar, no build, temp folders only).
