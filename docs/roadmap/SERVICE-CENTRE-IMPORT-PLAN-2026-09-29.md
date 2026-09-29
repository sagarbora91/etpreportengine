# Service Centre import support (S001–S040) — implementation plan for Opus

Date: 2026-09-29. Worktree: `E:\ETP\Code\Worktrees\service-centre-import`, branch `feature/service-centre-import`, created from `main` at `92a14da` (release 1.9.1). This plan synthesises the 29 Sep design panel (three designs, two judges). Base design: Design 3 (tests/operations first). Grafted from Design 1: folder-path period detection, S038-as-S011 facts, idempotent migration guards, generator equality assertion. Grafted from Design 2: `among` restriction on `IdentifyName`, zero Boolean columns, counts as Decimal, catalogue-invariant test, S012 `BookingStore` → `store_code`, business-unit follow-up held back as 0039. Where the judges disagreed I re-decided below and say why.

Rules that bind Opus: read-only on `E:\ETP\Code\Worktrees\main` and on `E:\ETP\ETP Source Data` (never copy a name, phone, job or invoice number into any file, log, test or message; headers are not customer data). No SQL Server exists on this PC: never run anything against a database. Never push, never merge, never touch other worktrees. Do not ask questions; where a fact is unverifiable, state it in the report and proceed with the decision written here.

Inputs already on disk (no customer data):
- `E:\ETP\Reference\Work in progress 2026-09-29\service-centre-families.proposed.json` — 39 draft catalogue entries (headers, canonical names, types). Starting point; apply the amendments in §3 and §4 (Boolean → Text; primary dates per §5; S012 store column; BusinessUnit).
- Scratchpad `C:\Users\Sagar\AppData\Local\Temp\claude\C--Codex-Reporting-Manger\f0f230c4-3518-4bb7-92dc-6a9b8501abe5\scratchpad\`: `service-centre-headers.json` (headers per file), `service-centre-type-purity.json` / `sc_type_purity.json` (per-column cell-shape counts), `service-centre-date-shapes-and-stores.json`.
- Real corpus (read-only): `E:\ETP\ETP Source Data\Consolidated data\ETP Service Centre Consolidated  till 29 sep 2026\` (double space in the folder name; 41 workbooks: `00_Service_Centre_Consolidation_Control.xlsx`, `S001_…` … `S040_…`).

## 1. Decisions (final)

| # | Question | Decision |
|---|---|---|
| D1 | Family granularity | One family per `Snnn` consolidated file: 39 families (`S001`–`S037`, `S039`, `S040`). `FamilyCode = ReportCode = Snnn`, `TableName = etp_landing_snnn`, `IsTyped = false`. S038 is not a family. |
| D2 | S038 | Retired. Its file matches S011's now-unique 26-header signature, so preflight accepts it as `EMPTY_EXPORT` (`ImportPreflight.cs:84-85`) and `FolderImportService` would persist an empty S011 import. Add the `FolderImportService` short-circuit (§6.5) so any file whose name carries an `[RS]\d{3}` code that is not in the catalogue is reported `Not needed` **before** the accepted check. Pinned by a unit test. |
| D3 | Store | **No `dbo.stores` row in 0038.** 37 families carry a single-valued exporting-centre column mapped to canonical `store_code` (§4.2); S011 and S013 have none and take store/date from folder siblings (`FolderImportService.cs:97-107`). An active AW330 row would stop scheduled Retail packs (`Phase2OperationsRepository.cs:222-228` needs R025 for every active store) and add a zero-sales store to every combined pack and selector; an inactive row gives no detection benefit (`knownStores` = `ActiveCodesAsync`, `StoreCatalogRepository.cs:23`). Business-unit-aware catalogue = optional held-back commit (§12 step 8, migration 0039). |
| D4 | Period rule | Row date (`PrimaryDateHeader`) only for immutable money/claim/tender logs: S003 `Trans Date`, S004 `BillingDate`, S019 `RepairDate`, S023–S026 `TransDate`, S039/S040 `Transaction Date`. All other 30 families are snapshots (`PrimaryDateHeader: null`): period = export date from Info/path, else sibling fallback. Reason: status columns are mutable (S002 `Current Status` 17 distinct values, S030 10, S011 9 …); with a row date every re-import of a status-bearing file is refused with `IMPORT_PERIOD_ALREADY_PRESENT` (`PhaseOneImportPersistence.cs:69-80`). |
| D5 | Expected period for snapshots in the consolidated folder | **2026-09-29**, from the folder name. `OpenXmlWorkbookReader.cs:31` sets `SourcePath`; `ImportScope.cs:21-23` prepends it to the context; `FindDates` (`ImportScope.cs:38-46`) accepts `d MMM yyyy`, so `till 29 sep 2026` yields 2026-09-29 (same mechanism as the Retail `till 6 sep 26` corpus test, `EtpCorpusGoldenTests.cs:106,121-125`). Both judges verified this; Design 3's 2026-09-28/null expectation is wrong and must not be used. Do **not** extend `FindDates` to ISO dates (changes Retail R010/R023/SOR_AGEING scope). |
| D6 | Typing | Measured purity, conservative: Date only where every non-empty cell in every file sharing the signature parses (after adding `d-M-yyyy`); Decimal for money/rates/quantities/counts (counts are Decimal, matching Retail quantities — not Integer); Integer only for `Month`, `Year`, `Line Num`, `LineNumber`; Identifier for codes/numbers-as-identity and `store_code`; Text for everything else including every boolean-like column (catalogue has zero Boolean columns today) and every impure column. `IsRequired: false` on every column. |
| D7 | Converter | Add `"d-M-yyyy"` to `TypedCellConverter.DateFormats` (`TypedCellConverter.cs:15`). S036/S037 `Created Date` cells are 100% `d-M-yyyy`/`dd-M-yyyy` text (1,611 cells) and do not parse today. `d`/`M` accept one or two digits, so `27-06-2026` and `7-2-2026` both parse; Retail date cells are numeric/DateTime and unaffected. |
| D8 | Name ties | Restrict `IdentifyName` to the signature candidates (`among`). S003 `RevenueReport` and R022 `Revenue Report` normalise identically; S006 `ClosingStock` and the Retail `Closing Stock` family too. Keep the source names; the restriction removes list-order dependence. Tests pin `Revenue Report.xlsx` → R022 and a `Closing Stock` name → the Retail family. |
| D9 | Snapshot History sheet | S006/S009/S010 carry a third sheet `Snapshot History` = Data headers + `Snapshot_As_Of` + `SourceFile`. Today it yields two `UNEXPECTED_COLUMN` warnings (not `LAYOUT_AMBIGUOUS`, not a blocker). Ignore it by name like `Info` (`ImportPreflight.cs:41`). Register/CHANGELOG must describe it as warnings, never as a failure. |
| D10 | Automation | Add `BusinessUnit` (`"RETAIL"` default, `"SERVICE"` for S families) to `EtpReportFamily`; filter `AutomatedOperationsService` (`:53`, pack loop `:81-85`) so a Service `PeriodEnd` never generates an `AUTO_REPORT_PACK`. |
| D11 | Migration 0038 | 39 tables + index + locked-day trigger (0018 shape), 39 append procedures + DENY/GRANT (0025 shape), idempotent `IF OBJECT_ID … IS NULL` / `IF NOT EXISTS` guards, `CREATE OR ALTER` for triggers and procedures, `SET XACT_ABORT ON;`, **no explicit BEGIN/COMMIT TRANSACTION** (the runner wraps each script, `Migrations.cs:190-206`), no store seed, no `import_profiles` seed. Generated; reviewed as SQL; pinned to the catalogue by a unit test. |
| D12 | Identity/duplicates | Engine unchanged. Snapshot families append a full copy per import day (R010 semantics); row-date families behave like R025 (superset promotion, otherwise Restate). Documented with the storage note (≈42k rows per full consolidated import; weekly refresh recommended; Express 10 GB cap). |
| D13 | Desktop | No code change. Service files are imported via Import folder / ZIP; single-file import of S011/S013 gives `SCOPE_NOT_DETECTED` (documented). |

Line numbers were verified by the judges against `main` at 92a14da; where a cite is off by a few lines, trust the file.

## 2. Files to add / change (exact)

Add:
- `scripts/service-centre/families.spec.json` — hand-reviewed source of truth (the proposed JSON with §3/§4 amendments applied).
- `scripts/service-centre/generate.py` — Python 3 + openpyxl; sub-commands `catalogue`, `migration`, `fixtures` (§7).
- `database/migrations/0038_service_centre_family_tables.sql` (§8).
- `tests-dotnet/fixtures/etp-sample/S001_RepairRegister.xlsx` … `S040_WRA_Claim.xlsx` (39 files; the existing `<Content Include>` globs in `Etp.Reporting.Import.Tests.csproj:19` and `Etp.Reporting.SqlServer.IntegrationTests.csproj:13` copy them; no csproj edit).
- `tests-dotnet/Etp.Reporting.Import.Tests/ServiceCentreCatalogueTests.cs` (invariants + migration-text golden).
- `tests-dotnet/Etp.Reporting.Import.Tests/ServiceCentreCorpusTests.cs` (`[RealCorpusFact]` inspection).
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/ServiceCentreFolderImportTests.cs` (CapturePersistence end-to-end over the real folder + fake-reader cases).
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/ServiceCentreImportSqlTests.cs` (cannot run here; marked).

Change:
- `src/Etp.Reporting.Import/Profiles/EtpReportFamilies.json` — append 39 entries after `SOR_AGEING`; existing 32 entries byte-identical.
- `src/Etp.Reporting.Import/Profiles/EtpReportFamilyRegistry.cs` — record gains `string BusinessUnit = "RETAIL"` as last positional parameter; `IdentifyName(string? name, IEnumerable<EtpReportFamily>? among = null)`; regex `(?:^|[^A-Z0-9])([RS]\d{3})(?:[^A-Z0-9]|$)`; `IsRetail(code)`.
- `src/Etp.Reporting.Import/Profiles/ImportProfileMatcher.cs:18` — pass the matched candidates to `IdentifyName`.
- `src/Etp.Reporting.Import/Preflight/ImportPreflight.cs:41` — `ConsolidationSheets = { "Info", "Snapshot History" }` (OrdinalIgnoreCase); the `Info` lookup at `:48` unchanged.
- `src/Etp.Reporting.Import/Conversion/TypedCellConverter.cs:15` — append `"d-M-yyyy"`.
- `src/Etp.Reporting.Infrastructure.SqlServer/FolderImportService.cs:81-92` — regex `[RS]\d{3}`; compute `notNeeded` before the `accepted is null` check and short-circuit.
- `src/Etp.Reporting.Infrastructure.SqlServer/AutomatedOperationsService.cs:53` — filter to `EtpReportFamilyRegistry.IsRetail(code)`.
- Existing tests extended: `EtpCorpusGoldenTests.cs`, `ImportProfileMatcherTests.cs`, `ImportPreflightTests.cs`, `TypedCellConverterTests.cs`, `FolderImportServiceTests.cs`.
- Docs: `docs/04_ETP_IMPORT_PROFILES.md`, `docs/05_MAPPING_REGISTER.md`, `docs/03_DATABASE_SCHEMA.md`, `docs/USER-GUIDE.md`, `CHANGELOG.md`, `docs/audit/IMPORT-FAILURE-REGISTER.md`. `ARCHITECTURE.md` lists no families (grep to confirm; no change).

## 3. Catalogue entry shape — worked family S002 JobReportBooking

Append after `SOR_AGEING`, same key order as the Retail entries. Real headers from the survey (27 columns):

```json
{
  "FamilyCode": "S002",
  "ReportCode": "S002",
  "Name": "JobReportBooking",
  "IsTyped": false,
  "TableName": "etp_landing_s002",
  "PrimaryDateHeader": null,
  "BusinessUnit": "SERVICE",
  "Headers": [
    "Job Order No", "Created date", "Comment", "SC Name", "Store Code", "Territory", "Area", "Region",
    "Store Channel", "Purchase Location", "Guarantee", "Is Stock watch", "Customer Channel", "Customer Type",
    "Brand", "ClusterId", "Brand Code", "JOType(Booking/QuickBilling)", "SRN Issued", "SRN Received",
    "OH", "Major", "Minor", "Battery", "FOC", "MG", "Current Status"
  ],
  "Columns": [
    { "SourceHeader": "Job Order No",                 "CanonicalField": "job_order_no",                "DataType": "Identifier", "IsRequired": false },
    { "SourceHeader": "Created date",                 "CanonicalField": "created_date",                "DataType": "Date",       "IsRequired": false },
    { "SourceHeader": "Comment",                      "CanonicalField": "comment",                     "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "SC Name",                      "CanonicalField": "sc_name",                     "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Store Code",                   "CanonicalField": "store_code",                  "DataType": "Identifier", "IsRequired": false },
    { "SourceHeader": "Territory",                    "CanonicalField": "territory",                   "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Area",                         "CanonicalField": "area",                        "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Region",                       "CanonicalField": "region",                      "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Store Channel",                "CanonicalField": "store_channel",               "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Purchase Location",            "CanonicalField": "purchase_location",           "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Guarantee",                    "CanonicalField": "guarantee",                   "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Is Stock watch",               "CanonicalField": "is_stock_watch",              "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Customer Channel",             "CanonicalField": "customer_channel",            "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Customer Type",                "CanonicalField": "customer_type",               "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "Brand",                        "CanonicalField": "brand",                       "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "ClusterId",                    "CanonicalField": "clusterid",                   "DataType": "Identifier", "IsRequired": false },
    { "SourceHeader": "Brand Code",                   "CanonicalField": "brand_code",                  "DataType": "Identifier", "IsRequired": false },
    { "SourceHeader": "JOType(Booking/QuickBilling)", "CanonicalField": "jotype_booking_quickbilling", "DataType": "Text",       "IsRequired": false },
    { "SourceHeader": "SRN Issued",                   "CanonicalField": "srn_issued",                  "DataType": "Decimal",    "IsRequired": false },
    { "SourceHeader": "SRN Received",                 "CanonicalField": "srn_received",                "DataType": "Decimal",    "IsRequired": false },
    { "SourceHeader": "OH",                           "CanonicalField": "oh",                          "DataType": "Decimal",    "IsRequired": false },
    { "SourceHeader": "Major",                        "CanonicalField": "major",                       "DataType": "Decimal",    "IsRequired": false },
    { "SourceHeader": "Minor",                        "CanonicalField": "minor",                       "DataType": "Decimal",    "IsRequired": false },
    { "SourceHeader": "Battery",                      "CanonicalField": "battery",                     "DataType": "Decimal",    "IsRequired": false },
    { "SourceHeader": "FOC",                          "CanonicalField": "foc",                         "DataType": "Decimal",    "IsRequired": false },
    { "SourceHeader": "MG",                           "CanonicalField": "mg",                          "DataType": "Decimal",    "IsRequired": false },
    { "SourceHeader": "Current Status",               "CanonicalField": "current_status",              "DataType": "Text",       "IsRequired": false }
  ]
}
```

Differences from the proposed JSON: `Is Stock watch` is Text (proposed says Boolean); `ClusterId` is Identifier (proposed says Text; either is safe, Identifier keeps consistency with the repair-register spec); `PrimaryDateHeader` is null (snapshot, D4) although `Created date` stays a Date column; `BusinessUnit` added.

Rule for the other 38 families: take the entry from `service-centre-families.proposed.json`, then (a) every `DataType: "Boolean"` → `"Text"`; (b) set `PrimaryDateHeader` per §5; (c) S012: map `BookingStore` to `CanonicalField: "store_code"`, Identifier; (d) add `"BusinessUnit": "SERVICE"`; (e) verify each typed column against the purity file (a column is Date/Decimal/Integer only when every non-empty cell across every file sharing the signature has that shape; otherwise Text); (f) canonical name = header lower-cased, every run of non-`[a-z0-9]` → `_`, trimmed; uniqueness per family enforced by test; no canonical name may contain `timestamp`. The eleven 83-column repair-register families (S001, S014–S018, S031–S035) share one column spec: generate once, copy.

## 4. Column typing policy per family

### 4.1 SQL mapping
Text, Identifier → `nvarchar(max)`; Decimal → `decimal(19,4)`; Date → `date` (time-of-day dropped: S028 `BOOKING DATE`/`TO DATE`/`SourcePeriodTo`, S039/S040 `Transaction Date`; documented); Integer → `int`. No Boolean anywhere.

### 4.2 Family table

Counts are what the proposed JSON yields after the §3 amendments (Boolean → Text), rounded where marked `~`. Opus must recompute the counts from the final `families.spec.json` and put the recomputed table into `docs/04_ETP_IMPORT_PROFILES.md`; the numbers here are expected magnitudes, not acceptance criteria.

| Family | Name | Cols | Primary date | store_code header | Date | Decimal | Integer | Text+Identifier | Must stay Text (reason) |
|---|---|---|---|---|---|---|---|---|---|
| S001 | RepairRegister | 83 | null | SCNumber | 14 | 6 | 2 | 61 | `Age` ("nn Months"), `ComplaintDetails` (codes/ints mixed), `Module` (bool in some views, int in others), `NumberOfTimes(RR)` (empty), `StockWatch`/`IsRTPassed`/`EmpowermentApplied`/`RADCEmpowermentApplied` (boolean-like), `CaseNumber`, `SupplierCode`, `SparePartsUsed` |
| S002 | JobReportBooking | 27 | null | Store Code | 1 | 8 | 0 | 18 | `Is Stock watch` (boolean-like) |
| S003 | RevenueReport | 55 | Trans Date | SC Number | 1 | ~21 | 1 (`Line Num`) | rest | e-invoice cancellation columns (sparse text) |
| S004 | TenderCollectionDetailed | 13 | BillingDate | StoreCode | 1 | 9 | 0 | 3 | — |
| S005 | Tender_Collection_SUMMARY | 11 | null (no date column) | StoreCode | 0 | 9 | 0 | 2 | — |
| S006 | ClosingStock | 14 | null | Store | 0 | 3 | 0 | 11 | `StoreNumber` (store type text) |
| S007 | PurchaseRegister_CREATED | 24 | null | STORE_CODE | 3 | ~12 | 0 | rest | `STATUS` |
| S008 | PurchaseRegister_RECCIVED | 24 | null | STORE_CODE | 3 | ~12 | 0 | rest | keep ETP spelling in Name |
| S009 | PendingRepair | 36 | null | StoreCode | 3 | 3 | 0 | 30 | `SRNStoreCode`, `PendingStore` (destinations, never store_code) |
| S010 | PendingDelivery | 36 | null | StoreCode | 3 | 3 | 0 | 30 | as S009 |
| S011 | SRNStatusReport | 26 | null | — (sibling) | 8 | 1 | 0 | 17 | `FROM STORE` (2 distinct values) |
| S012 | SRNHistory | 32 | null | BookingStore | 7 | 4 | 0 | 21 | `FromStore`/`ToStore` (endpoints) |
| S013 | GIT | 12 | null | — (sibling) | 1 | 2 | 0 | 9 | `Invent Location ID From/To` (endpoints) |
| S014–S018 | RepairRegister_DC/_IR/_RA/_RWR/_DELIVERED | 83 | null | SCNumber | as S001 | | | | as S001 |
| S019 | RepeatReturn | 13 | RepairDate | StoreCode | 2 | 0 | 0 | 11 | — |
| S020 | ReplacementReport | 55 | null | BookingStoreCode | 7 | 5 | 1 (`LineNumber`) | rest | `StoreSAPCode` (Identifier, not store), `WRAStatus.1` → `wrastatus_1` |
| S021 | Depreciation | 52 | null | BookingStoreCode | 6 | 5 | 1 (`LineNumber`) | rest | `Status.1` → `status_1` |
| S022 | EmpowermentReport | 23 | null | SC NUMBER | 1 | 4 | 0 | 18 | `Cancelled date` (141 non-date texts + 4 dates), `PAYMENT RECEIVED` (boolean-like) |
| S023 | GPRC_Report | 34 | TransDate | SCNumber | 1 | ~17 | 0 | rest | — |
| S024–S026 | GPRC_MB_Report/GPRC_WDC/GPRC_WRA_Report | 33 | TransDate | SCNumber | 1 | ~16 | 0 | rest | one shared signature; names disambiguate |
| S027 | TATReport | 15 | null | StoreCode | 2 (`SourcePeriodFrom/To`) | 2 | 0 | 11 | `EDD` (dates, ints and `hh:mm:ss` mixed), `Label`, `SourceFile` |
| S028 | TechnicianProductivityReport | 25 | null | STORE CODE | 5 | 7 | 0 | 13 | `SourceFile` |
| S029 | DeftranReport | 72 | null | Location | 5 | ~10 | 2 (`Month`,`Year`) | rest | `Parts-GD-Yes/No`, `IsDealer` (boolean-like), `Module`, `CaseNo` |
| S030 | export_grid_MIS_REPORT | 13 | null | Store code | 1 | 0 | 0 | 12 | `IsStockwatch` (boolean-like) |
| S031–S035 | RepairRegister_PD/_PR/_SRN/_REPAIRED/_SRNINV | 83 | null | SCNumber | as S001 | | | | dates are `dd-MM-yyyy` text (parse today) |
| S036 | DeliveryReport | 26 | null | Store Code | 1 (`Created Date`, needs D7) | 8 | 0 | 17 | — |
| S037 | RepairReport | 26 | null | Store Code | 1 | 8 | 0 | 17 | own signature (Major/Minor order differs from S036) |
| S039 | WD_Claim | 33 | Transaction Date | Store Code | 1 | ~16 | 0 | rest | shares signature with S040 |
| S040 | WRA_Claim | 33 | Transaction Date | Store Code | 1 | ~16 | 0 | rest | — |

Never map to `store_code`: `ToStoreCode`, `SRNStoreCode`, `PendingStore`, `FROM STORE`, `TO STORE`, `FromStore`, `ToStore`, `SRNRepairStore`, `Repair Location`, `StoreSAPCode`, `DealerSAPCode`, `Invent Location ID From/To`. Each is Identifier/Text under its own canonical name, so `WORKBOOK_MULTIPLE_STORES` (`MatchedImportEnvelope.cs:94-97`) cannot fire.

## 5. Primary date per family (final)

Row date: S003 `Trans Date`; S004 `BillingDate`; S019 `RepairDate`; S023, S024, S025, S026 `TransDate`; S039, S040 `Transaction Date`.
Snapshot (`null`): S001, S002, S005–S018, S020–S022, S027–S037. Date columns in these families stay typed Date; they just do not define the period.

Expected periods on the real consolidated folder: row-date families = min/max of the column (S003 2024-10-01..2026-09-28, S004 2024-10-01..2026-09-28, S019 2025-04-21..2026-09-08, S023 2024-11-05..2026-08-05, S024 2025-02-03..2025-11-09, S025 2024-11-05..2026-07-01, S026 2024-11-05..2026-07-01, S039/S040 2026-07-01..2026-09-08); every snapshot family = 2026-09-29..2026-09-29 (D5). S038 (`Not needed`) has no period.

## 6. Code changes (exact)

### 6.1 `EtpReportFamilyRegistry.cs`
```csharp
public sealed record EtpReportFamily(string FamilyCode, string ReportCode, string Name, bool IsTyped, string TableName,
    string? PrimaryDateHeader, IReadOnlyList<string> Headers, IReadOnlyList<EtpReportFamilyColumn> Columns,
    string BusinessUnit = "RETAIL");   // LAST positional parameter; System.Text.Json fills the default for Retail entries

private static readonly Regex FamilyCodePattern = new(@"(?:^|[^A-Z0-9])([RS]\d{3})(?:[^A-Z0-9]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

public static EtpReportFamily? IdentifyName(string? name, IEnumerable<EtpReportFamily>? among = null)
{
    if (string.IsNullOrWhiteSpace(name)) return null;
    var pool = (among ?? Families).ToArray();
    var m = FamilyCodePattern.Match(name);
    if (m.Success)
    {
        var byCode = pool.FirstOrDefault(f => f.ReportCode.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        if (byCode is not null) return byCode;
    }
    var normalised = Normalise(name);
    return pool.Where(f => normalised.Contains(Normalise(f.Name))).OrderByDescending(f => Normalise(f.Name).Length).FirstOrDefault();
}
public static bool IsRetail(string reportCode) => Resolve(reportCode).BusinessUnit == "RETAIL";
```
Keep the existing normalisation helper (adapt the name); keep `Resolve`/`CreateProfile` as they are. If `IsRetail` is called with a non-family code (e.g. a legacy profile), return true.

### 6.2 `ImportProfileMatcher.cs` (the several-matches branch, around `:18`)
```csharp
var candidates = matches.Select(m => EtpReportFamilyRegistry.Resolve(m.ReportCode)).ToArray();
var named = EtpReportFamilyRegistry.IdentifyName(fileName, candidates) ?? EtpReportFamilyRegistry.IdentifyName(sheetName, candidates);
```
If `Resolve` throws for non-family profiles, filter to those whose `ReportCode` the registry knows.

### 6.3 `ImportPreflight.cs:41`
```csharp
private static readonly HashSet<string> ConsolidationSheets = new(StringComparer.OrdinalIgnoreCase) { "Info", "Snapshot History" };
foreach (var originalSheet in workbook.Sheets.Where(sheet => !ConsolidationSheets.Contains(sheet.Name)))
```

### 6.4 `TypedCellConverter.cs:15`
Append `"d-M-yyyy"` to `DateFormats`.

### 6.5 `FolderImportService.cs:81-92`
Move the `sourceCode`/`unsupportedFamily`/`notNeeded` computation above `if (accepted is null)` with regex `([RS]\d{3})`; when `notNeeded` is true, record the `Not needed` result with the existing messages and `continue`, regardless of whether a signature matched. Keep the `00_` rule. `DependencyOrder` unchanged.

### 6.6 `AutomatedOperationsService.cs:53`
`.Where(x => x.Status == "Imported" && x.PeriodEnd is not null && x.ReportCode is { } code && EtpReportFamilyRegistry.IsRetail(code))` before adding to `importedDates`. Everything else (Processed/Duplicate/Failed folders, `automation_runs` rows) unchanged.

No change to `ImportScope`, `ImportRowStager`, `MatchedImportEnvelope`, `SelectRoute`, orchestrators, `OpenXmlWorkbookReader`, Desktop.

## 7. Generator `scripts/service-centre/generate.py`

Python 3 + openpyxl (both present on this PC). Reads `families.spec.json`; every output is deterministic and committed.
- `catalogue`: prints the 39 `EtpReportFamily` JSON entries (Retail key order) to a scratch file; Opus merges them after `SOR_AGEING` and diffs to confirm the 32 Retail entries are byte-identical.
- `migration`: writes `0038_service_centre_family_tables.sql` (§8). Asserts per family that the column list, the `@v` parameter list and the INSERT column/value lists have identical length and order.
- `fixtures`: writes `tests-dotnet/fixtures/etp-sample/Snnn_<Name>.xlsx`: sheet `Data` (exact headers row 1, one synthetic row), sheet `Info` (`Family ID | Snnn`, `Source | AW330 sample 20260928`, `Status | Synthetic CI sample`); S006/S009/S010 also get an empty `Snapshot History` sheet with the +2 headers. Values by type: Date → date cell 2026-09-28, except S036/S037 `Created Date` = text `28-9-2026` and S031 dates = text `28-09-2026`; Integer → `Month` 9, `Year` 2026, else 1; Decimal → 100, tax-named 18, inclusive-total-named 118; Identifier → `AW330` for `store_code`, `JOAW330000000001` for job-order headers, `BIAW330000000001` for invoice/billing/document numbers, `9000000000` for mobile/phone, else `SYNTH-0001`; Text → `Sample Customer` for name headers, `sample@example.invalid` for e-mail headers, else `SAMPLE`. Fixed workbook properties (creator/created) so regeneration is byte-stable. Re-reads every written fixture and asserts `Data` headers equal `Headers`. Nothing is read from the source folder.

## 8. Migration `database/migrations/0038_service_centre_family_tables.sql`

Header comment, `SET XACT_ABORT ON;`, then one block per family in code order, no explicit transaction, no store seed. Complete example for S004 (13 columns):

```sql
-- Service Centre (AW330) landing tables S001-S040 (S038 retired, no table).
-- Same shape as 0018 (table, file index, locked-day trigger) and 0025 (append procedure, DENY, GRANT EXECUTE).
-- Generated by scripts/service-centre/generate.py migration from scripts/service-centre/families.spec.json.
-- No dbo.stores row is seeded (an active AW330 would stop scheduled Retail packs). Never edit 0018/0025.
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.etp_landing_s004', N'U') IS NULL
CREATE TABLE dbo.[etp_landing_s004] (
  etp_row_id bigint IDENTITY PRIMARY KEY,
  import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
  source_lineage_id bigint NOT NULL UNIQUE REFERENCES dbo.source_lineage(source_lineage_id),
  content_key varchar(80) NOT NULL,
  [billingnumber] nvarchar(max) NULL, [billingdate] date NULL, [store_code] nvarchar(max) NULL, [state] nvarchar(max) NULL,
  [cashamount] decimal(19,4) NULL, [cardamount] decimal(19,4) NULL, [bharatpe] decimal(19,4) NULL, [phonepe] decimal(19,4) NULL,
  [upi] decimal(19,4) NULL, [chequeamount] decimal(19,4) NULL, [rtgsamount] decimal(19,4) NULL, [advanceamount] decimal(19,4) NULL,
  [totalamount] decimal(19,4) NULL
);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_etp_landing_s004_file' AND object_id = OBJECT_ID(N'dbo.etp_landing_s004'))
  CREATE INDEX IX_etp_landing_s004_file ON dbo.[etp_landing_s004](import_file_id);
EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_etp_landing_s004_locked ON dbo.[etp_landing_s004] AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'') THROW 51243,''A day inside this export is finalised. Reopen it before changing its data.'',1; END');
-- (trigger body must be diffed against 0018 and copied verbatim from there if it differs)

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_s004
  @file bigint, @lineage bigint, @key varchar(80),
  @v0 nvarchar(max), @v1 date, @v2 nvarchar(max), @v3 nvarchar(max),
  @v4 decimal(19,4), @v5 decimal(19,4), @v6 decimal(19,4), @v7 decimal(19,4), @v8 decimal(19,4),
  @v9 decimal(19,4), @v10 decimal(19,4), @v11 decimal(19,4), @v12 decimal(19,4)
AS BEGIN
  SET NOCOUNT ON; SET XACT_ABORT ON;
  IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
     AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
    THROW 51420,''Owner or Store Manager permission is required.'',1;
  IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
      JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
      WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
    THROW 51422,''The source import is not open for writing.'',1;
  IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
      WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''S004'')
    THROW 51422,''The source row does not belong to this report import.'',1;
  INSERT dbo.[etp_landing_s004](import_file_id,source_lineage_id,content_key,[billingnumber],[billingdate],[store_code],[state],[cashamount],[cardamount],[bharatpe],[phonepe],[upi],[chequeamount],[rtgsamount],[advanceamount],[totalamount])
  VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12);
  INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
  SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');
DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_s004] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_s004 TO etp_store_manager,etp_owner;
```
Every guard text, THROW number and the DENY/GRANT lines must be copied verbatim from 0025 (diff one generated block against `append_etp_landing_r001`). `@v{i}` order = `Columns` order (`PhaseOneImportPersistence.cs:131-140` binds positionally). Widest procedure: 86 parameters (limit 2,100). `GRANT SELECT ON SCHEMA::dbo` (0022) covers reads; no per-table SELECT grant. DENY/GRANT re-run safely; leave them unguarded as 0025 does.

## 9. Tests

### 9.1 Unit (run here; whole log saved, never filtered)
- `ServiceCentreCatalogueTests`: 71 families; unique `FamilyCode`/`ReportCode`/`TableName`; every S family `BusinessUnit == "SERVICE"`, `IsTyped == false`, `TableName == $"etp_landing_{code.ToLowerInvariant()}"`; every S column `IsRequired == false`; no `Boolean` DataType in S families; canonical fields unique per family, `^[a-z0-9_]+$`, none containing `timestamp`; `PrimaryDateHeader` null except the nine row-date families, and where set it names a Date column; exactly one `store_code` column per S family except S011/S013 (none); signature groups are exactly {S001,S014–S018,S031–S035}, {S007,S008}, {S024,S025,S026}, {S039,S040}; S036 ≠ S037. **Migration-text golden**: locate `database/migrations/0038_service_centre_family_tables.sql` by walking up from `AppContext.BaseDirectory`; per S family assert `CREATE TABLE dbo.[table]` with the column list in canonical order and the §4.1 SQL types, `IX_{table}_file`, `trg_{table}_locked`, `CREATE OR ALTER PROCEDURE dbo.append_{table}` with exactly `Columns.Count` `@v` parameters and `f.report_code=''Snnn''`, the DENY and GRANT lines; and that the file contains no `etp_landing_s038` and no `INSERT dbo.stores`.
- `EtpCorpusGoldenTests`: add the 39 S codes to the theory data; branch on prefix: Service fixtures assert Accepted, no Blocker, family code, one staged row, `Values.Count == Headers.Count`, `Scope.StoreCode == "AW330"` (S011/S013: null; the golden knownStores list stays `["HEMW","WLMHW"]` so the contextual path is not relied on), `Scope.PeriodEnd == 2026-09-28` (row-date families from the cell; snapshot families from the Info `20260928` token). Spot values: S003 `net_amount == 100m`, `tax_amount == 18m`, `trans_date == 2026-09-28`; S036 `created_date == 2026-09-28` (from `28-9-2026`); S031 `jodate == 2026-09-28` (from `28-09-2026`); S028 `booking_date == 2026-09-28`.
- `ServiceCentreCorpusTests` `[RealCorpusFact] Whole_service_centre_consolidated_folder_inspects_clean`: root via `RealCorpusFactAttribute`, folder `Consolidated data\ETP Service Centre Consolidated*` (`GetDirectories(...).Single()`); for every `S*.xlsx`: Accepted, zero Blockers, `Profile.ReportCode` == code in the file name (S038 → S011 with `EMPTY_EXPORT`), staged rows == list below, `Scope.StoreCode == "AW330"` except S011/S013/S038 null, periods per §5 (snapshots 2026-09-29). Output lines: file name, code, counts, scope only.
  Rows: S001 3655, S002 3954, S003 4968, S004 2218, S005 1865, S006 497, S007 1562, S008 1539, S009 63, S010 128, S011 145, S012 172, S013 37, S014 110, S015 156, S016 31, S017 450, S018 3593, S019 6, S020 22, S021 110, S022 190, S023 53, S024 44, S025 97, S026 36, S027 1184, S028 1486, S029 3861, S030 1522, S031 39, S032 41, S033 12, S034 5, S035 6, S036 783, S037 828, S038 0, S039 13, S040 13.
- `ImportProfileMatcherTests`: `S014_RepairRegister_DC.xlsx` → S014; `S001_RepairRegister.xlsx` → S001; raw-style `RepairRegister_SRNINV_20260929.xlsx` → S035, `RepairRegister_SRN_…` → S033, `PurchaseRegister_RECCIVED` → S008, `GPRC_WDC` → S025, `GPRC_Report` → S023, `WD_Claim` → S039; `export.xlsx` with an 83-header sheet named `Data` → null; `R022_Revenue_Report.xlsx` → R022; **`Revenue Report.xlsx` (46 headers) → R022 and a `Closing Stock` name → the Retail family, with S003/S006 present**; `[RS]` boundary: `Sales S123 report` treated as code, `RS123` not.
- `ImportPreflightTests`: workbook `Data` (S006 headers) + `Info` + `Snapshot History` (+2 headers) → Accepted, no `UNEXPECTED_COLUMN`; a third sheet with another name keeps today's behaviour.
- `TypedCellConverterTests`: `25-4-2026`, `7-2-2026`, `28-09-2026` → dates; `2026-09-28 14:55:37` text still invalid; `20260928` and OADate unchanged.
- `FolderImportServiceTests` (fake reader, CapturePersistence): (a) S011 (no store) beside S002 (AW330) → both Imported, S011 gets AW330 and S002's period; (b) lone S011 → Failed with `SCOPE_NOT_DETECTED`; (c) `S038_SRNReport.xlsx` with S011 headers, 0 rows → `Not needed`, persistence not called; (d) `S041_Future.xlsx` with a known signature → `Not needed` (pins the reorder); (e) `00_…Control.xlsx` → `Not needed`; (f) `R099_x.xlsx` still `Not needed`.
- `ServiceCentreFolderImportTests` (`[RealCorpusFact]`-style attribute copied locally; project `Etp.Reporting.SqlServer.Tests`, where `CapturePersistence` and `FolderImportService` both compile): `new FolderImportService(new CapturePersistence(), knownStores: ["HEMW","WLMHW"]).RunAsync(<service folder>)` → 39 `Imported`, 2 `Not needed`, 0 `Failed`, 0 `Unknown layout`, no message containing `SCOPE_NOT_DETECTED`; every S row `StoreCode == "AW330"`; snapshot rows `PeriodEnd == 2026-09-29`; captured requests carry `ExpectedStoreCode == "AW330"`. **This is the direct proof of non-negotiable 1.**
- `AutomatedOperationsService` unit test (existing fake pattern): a batch with only Service `Imported` files produces no `AUTO_REPORT_PACK`; a batch with an R025 file still does.

### 9.2 SQL integration (write; cannot run here)
Class summary on each new/extended class: `// Cannot run on the authoring PC (no SQL Server); first run is the new PC.`
- `ServiceCentreImportSqlTests`: 39 fixtures in a temp folder → `FolderImportService` + `SqlServerImportPersistenceUseCase` → 39 Imported; one row per `etp_landing_snnn`; `import_files.report_code`/`store_code`; `source_lineage.source_record_type = 'Snnn_SOURCE'`; 39 `import_attempts`; second run → 39 Duplicate; snapshot semantics (S009 with Info `20260928` then `20260929` → 2 rows; same day with one changed cell → `IMPORT_PERIOD_ALREADY_PRESENT`); row-date semantics (S003 one row, then two-row superset → Imported with 1 already present; changed old row → refused); S011+S002 → S011 persists with AW330; Restate on S003 mirrors `PhaseOneImportSqlTests.cs:44-76`.
- `CrossPhaseMigrationTests`: 39 tables, 39 procedures, 39 triggers; `sys.database_permissions` DENY for `etp_store_manager`/`etp_viewer` per table and GRANT EXECUTE for `etp_store_manager`/`etp_owner` per procedure; `dbo.stores` still exactly HEMW and WLMHW.
- `CrossPhaseStoreManagerImportTests` / `PhaseFourSecurityTests`: as store manager S003 imports via the procedure; direct INSERT denied; as viewer EXEC denied.
- `PhaseFiveSharingAutomationTests`: ZIP of 39 fixtures in Inbound → Processed, `WATCH_IMPORT` row with store AW330, no `AUTO_REPORT_PACK`; identical ZIP → Duplicate; ZIP with a `.csv` → Failed `IMPORT_ARCHIVE_LAYOUT`.
- Private-corpus SQL test (extend the attribute to require the Service folder): whole real folder → 39 Imported with §9.1 counts, 2 Not needed; re-run → 39 Duplicate.

## 10. Docs, CHANGELOG, register (every sentence true of the built code)
- `docs/04_ETP_IMPORT_PROFILES.md` — "Service Centre families (S001–S040)": the §4.2 table (recomputed counts), signature groups and name rule, S036/S037 distinct, S038 retired → `Not needed`, `Info`/`Snapshot History` ignored, `d-M-yyyy` accepted, snapshot vs row-date families and their re-import/Restate behaviour, raw daily pack unverified (format and names), S027/S028 carry consolidation-added columns, landing-only (no report reads them).
- `docs/05_MAPPING_REGISTER.md` — "Service Centre landing tables": canonical rule, `store_code` source per family, columns never mapped to store, typing rules with the Text exceptions, no Boolean, time-of-day dropped, customer-identifying columns stored as landing text and never surfaced in diagnostics.
- `docs/03_DATABASE_SCHEMA.md` — `etp_landing_s001`…`s040` (39, no s038), `append_etp_landing_snnn`, index, trigger, grants, migration 0038, "no store row".
- `docs/USER-GUIDE.md` — Imports: import the Service folder/ZIP with Import folder; grid shows 39 Imported + 2 Not needed; single-file import of S011/S013 unsupported; do not add AW330 to Settings → Stores (one sentence why); weekly refresh recommendation; Restate only when an imported money row changed.
- `CHANGELOG.md` `## Unreleased`: "Service Centre (AW330) imports: 39 report families S001–S040 (S038 retired) are recognised, validated and stored in landing tables by migration 0038; the consolidated Service folder imports with no unknown layouts; consolidation sheets Info/Snapshot History are ignored; text dates written as d-M-yyyy are accepted; Service imports never trigger automatic Retail packs. Retail behaviour unchanged. Unit and real-corpus tests pass on the authoring PC; SQL integration tests are written but could not run there (no SQL Server) — first run is the new PC."
- `docs/audit/IMPORT-FAILURE-REGISTER.md` — append only: `IF-015 | 2026-09-29 | AW330 / ETP Service Centre Consolidated folder (41 files) | Validate | LAYOUT_UNKNOWN on all 40 S files; 00_ Not needed | Whole Service Centre folder rejected as Unknown layout. | Only Retail (R) families in the catalogue; matcher and folder rules keyed on R\d{3} (EtpReportFamilyRegistry.cs:31, FolderImportService.cs:84). | unsupported-report | feature/service-centre-import: 39 families, migration 0038, fixtures, corpus tests | FIXED (feature/service-centre-import, <commit>) — SQL verification pending on the new PC`. Do not mention `LAYOUT_AMBIGUOUS`.

## 11. Gate commands and acceptance criteria
Run from `E:\ETP\Code\Worktrees\service-centre-import`; save every complete console log to the scratchpad (never filter; add `--logger trx`).
1. `dotnet build Etp.Reporting.slnx -c Release` → 0 warnings, 0 errors.
2. `dotnet test Etp.Reporting.slnx -c Release --no-build -m:1 --logger trx` (the SQL integration project will skip/fail for lack of a server exactly as it does today on main — run the same command main's gate uses and compare; the non-SQL projects must all pass). Baseline 969 existing unit tests unchanged plus the new ones; quote the new total from the log.
3. The `[RealCorpusFact]` tests ran (not skipped) because the corpus is beside the checkout; the log shows 40 S-file lines with counts only.
4. `git status` clean after each commit; `git diff main --stat` lists only the files in §2.
5. No customer value in any committed file, log or message (grep the diff for digit runs ≥ 10 outside the fixtures' synthetic values).
Acceptance: 39 Imported + 2 Not needed + 0 Failed/Unknown layout/SCOPE_NOT_DETECTED on the real folder via CapturePersistence; every S row AW330; snapshot periods 2026-09-29; existing Retail tests untouched and green; 0038 pinned to the catalogue by the golden test; SQL tests compile.

## 12. Commit plan (small commits on the branch; no push, no merge)
1. `Add Service Centre family spec and generator; append S001-S040 to the catalogue` (+ catalogue invariants test).
2. `Recognise S### family codes, ignore Snapshot History, accept d-M-yyyy dates` (registry/matcher/preflight/converter + tests).
3. `Add Service Centre golden fixtures and real-corpus inspection tests`.
4. `Report uncatalogued family codes as Not needed; keep Service imports out of automatic Retail packs` (FolderImportService, AutomatedOperationsService + tests incl. the CapturePersistence real-folder run).
5. `Add migration 0038 Service Centre landing tables with catalogue golden test`.
6. `Write Service Centre SQL integration tests (unrun on this PC)`.
7. `Document Service Centre import support; register IF-015; CHANGELOG`.
8. Optional, separate and last, only if time remains: `Business-unit store catalogue (0039) — unverified` (seed `business_units` RETAIL/SERVICE, `AW330` active under SERVICE, `StoreCatalogEntry.BusinessUnit`, `ActiveRetailCodesAsync`, filter `DailyReportingPackService.cs:32`, `EveningReportRepository.cs:59`, `OperationalReportRepository.cs:264,309,412,685-687`, `Phase2OperationsRepository.cs:222-228`, `EveningMastersView.cs:50`, `MainWindow.Stores.cs`; keep `AutomatedOperationsService`/`DesktopImportCoordinator` on all active codes). The owner may drop this commit.

## 13. What Opus must report back
- Commit hashes and one-line messages, in order; `git diff main --stat`.
- Build warning count; unit test totals before/after (from the unfiltered logs, path to each log).
- The real-folder result table: file, code, outcome, rows, store, period (no cell values); confirmation of 39/2/0/0/0.
- The recomputed §4.2 counts and any column retyped to Text during the run, with the reason (which cell shape blocked it).
- Any deviation from this plan and why; anything unverifiable (SQL, raw pack, DesktopImportCoordinator single-file path).
- Open items for the new PC: run the SQL integration tests, run the migration, acceptance steps (import folder twice, Retail packs still generate, Settings → Stores unchanged, database size check, raw pack ZIP entry-extension check).

## 14. Open decisions for Sagar (owner's call only)
1. Whether to take the optional 0039 business-unit commit (makes AW330 a first-class store for Service reporting; changes nine Retail call sites; unverifiable until the new PC).
2. Refresh cadence for the consolidated Service folder (weekly recommended; daily grows ≈4–5 GB/year under the snapshot rule against the Express 10 GB cap) and whether a snapshot retention rule should be scheduled.
3. Whether the raw daily Service pack must be supported as-is (needs one real pack: if entries are CSV, a CSV reader is a separate change; if S027/S028 raw layouts differ, two extra families).
4. Whether the eleven repair-register view names (`RepairRegister_DC` etc.) are the tokens the raw exports actually use in file names — only a raw pack can confirm.
