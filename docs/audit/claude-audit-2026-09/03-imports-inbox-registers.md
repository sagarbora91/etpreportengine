# 03 — Imports, Source Inbox / OCR, Registers, Watch Folder, SQL persistence

Audit date: 2026-09-15. Read-only code audit of `src/Etp.Reporting.Import`, `src/Etp.Reporting.Application/{Imports,SourceInbox,Registers}`, `src/Etp.Reporting.Infrastructure.SqlServer/*Import*|*Register*|*Document*|Automated*`, `src/Etp.Reporting.Desktop/Modules/{Imports,SourceInbox,Registers}`, `database/migrations/*.sql`, the Import/SqlServer test projects, and the 59 real ETP workbooks under `ETP Source Data\{WLMHW,HEMW}` (headers dumped with openpyxl, `data_only=True`, non-read-only).

All paths below are relative to the repo root `C:\Codex\Reporting Manger\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f\`. Ratings: **Critical / High / Medium / Low**.

Already known and not re-derived here: `R025SqlImportOrchestrator.cs:54-55` persists `NETVALUE` (ex-GST in the real files: 2995 − 456.86 = 2538.14) into `sales_lines.source_net_amount`, passes `null` for `source_gross_amount`, and drops `NETAMOUNT`. Note that `docs/05_MAPPING_REGISTER.md:18` states the opposite ("`NETVALUE` … includes GST"); the real corpus contradicts the doc.

---

## Executive summary (top findings)

| # | Rating | Finding | Where |
|---|---|---|---|
| 1 | Critical | `PAYMENTTYPE25` is **AIRPAY (UPI)** in the real data — ₹12.71 lakh of ₹21.43 lakh (59 %) of WLMHW tender value, present on 258 of 404 invoices — and it is hard-quarantined from every tender report and from tender reconciliation. The Payment Type Report's `AGENCYNAME` column resolves it unambiguously. | `src/Etp.Reporting.Import/Staging/R022PersistenceProjection.cs:59-61`, `src/Etp.Reporting.Infrastructure.SqlServer/PersistenceContracts.cs:97-98`, `database/migrations/0014_productisation.sql:432`, `0002_reporting_facts.sql:120-122` (view excludes it) |
| 2 | Critical | R025 sales-line business identity is `store/year/invoice/<source ROW NUMBER>`. Any re-export with different row positions (overlapping period, daily vs range export, ETP inserting a late row) produces duplicated facts (`NEW`) and silently dropped rows (`CONFLICT` → `RETURN` without insert). `docs/BULK_IMPORT_AND_DUPLICATION.md:11` promises the opposite. | `src/Etp.Reporting.Infrastructure.SqlServer/R025SqlImportOrchestrator.cs:52`, `database/migrations/0014_productisation.sql:385,402-404` |
| 3 | Critical | Conflict rows are **not persisted** but the batch still commits as `Completed` and the file counts as imported (hash registered). Facts are silently incomplete; the only trace is `import_row_outcomes`. | `0014_productisation.sql:392-393,401-403` (`RETURN` before `INSERT dbo.sales_lines`), `SqlServerRepositories.cs:107-108` |
| 4 | Critical | Nothing prevents two *different* files (different SHA-256) from being imported as current for the same report/store/date. Both stay `is_superseded=0`, facts double, and restatement then becomes impossible ("More than one current source file exists"). | `OperationalCompletionRepository.cs:51-77`, `SqlServerImportPersistenceUseCase.cs:56-72` (no scope guard), `DesktopImportCoordinator.cs:92-115` |
| 5 | High | The whole business scope of an import is one date = `MAX(transaction_date)`. The real exports span 01 Jul–25 Aug (55 dates). The user must guess "25 Aug 2026" in the UI or the import is rejected; `import_files.business_date`, day-locking, restatement and evidence linking all then key a 55-day file to a single day. | `R025SqlImportOrchestrator.cs:60-61,95-96`, `ImportWorkspaceView.xaml.cs:27,280-287` |
| 6 | High | Batch (folder/ZIP) import applies **one store and one date** to every workbook in the batch; a ZIP holding both stores or several dates fails file-by-file with store/date mismatch. | `ImportWorkspaceView.xaml.cs:232-234`, `DesktopImportCoordinator.cs:219-233` |
| 7 | High | R013 (CRO Wise Sales) `SR` rows carry **positive** `NETVALUE` with negative `QTY` in the real files; enrichment stores the value as-is and the CRO report `SUM(source_net_value)`s it → staff sales are overstated by returns. R013 also discards `CRO NAME`. | `RetailSalesProfiles.cs:87-100`, `RetailEnrichmentSqlImportOrchestrator.cs:176`, `OperationalReportRepository.cs:310-325` |
| 8 | High | Enrichment imports (R003/R013) have **no** row-level dedupe: a re-export with a new hash duplicates every enrichment row. | `RetailEnrichmentSqlImportOrchestrator.cs:108-114,153-166` |
| 9 | High | `AdvanceOrder Collection` has the identical 46-column header as `Revenue Report`; signature-only matching would classify it as R022 and register a zero-row "current R022 import" for the selected store/date. | `ImportProfileMatcher.cs:14-21`, real headers (WLMHW/HEMW `*_AdvanceOrder Collection*.xlsx`) |
| 10 | High | Native PDF "extraction" is a regex over raw bytes that only works on uncompressed content streams (almost no real PDF). PaddleOCR is an *external* executable at a DB-configured path; no helper ships in the repo. Extraction `ReviewStatus` is always `REVIEW_REQUIRED` (dead ternaries). Source Inbox is a store-and-review shell; OCR does not exist in the product. | `DocumentIntakeService.cs:17,28-32,42-75` |
| 11 | Medium | Only 6 of the 30 workbook types are importable. None of the owner-needed sources (Payment Type Report, Daywise Collection, Banking Details/Summary, CN Register, SDB Document Wise, service sales) has a profile, table, or orchestrator. | `ApprovedImportProfileRegistry.cs:8-16` |
| 12 | Medium | ZIP bomb guard trusts the ZIP central-directory `Length`; extraction copies without a streamed byte cap. | `ImportPathPolicy.cs:80-88`, `BatchImportSource.cs:107-110` |

---

## A. Supported profiles

### A.1 Header-signature matching — fails closed, but only on the header row

- Matching is an exact SHA-256 over the normalised header list: `src/Etp.Reporting.Import/Profiles/ImportProfileMatcher.cs:14-21,24-32`. Normalisation is whitespace-collapse + upper-case only: `src/Etp.Reporting.Domain/Imports/ImportProfile.cs:96-99`. Any added, removed, renamed or reordered column yields a different hash → `LAYOUT_UNKNOWN` blocker (`ImportPreflight.cs:68-69`) plus `REQUIRED_COLUMN_MISSING`/`UNEXPECTED_COLUMN` diagnostics against the closest profile (`ImportPreflight.cs:82-119`). Duplicate normalised headers are blocked (`:57-61`). A workbook with two matching sheets is blocked as `LAYOUT_AMBIGUOUS` (`:70-71`). A second, non-matching sheet is also a blocker because its `REQUIRED_COLUMN_MISSING` diagnostics are Blockers (`:104-110`) — so a real ETP file with an extra sheet cannot be imported at all. **Verdict: fail-closed on header change — Good.**
- Registry: exactly six profiles (`Profiles/ApprovedImportProfileRegistry.cs:8-16`): R025 SDB-VariantwiseSales (41 cols), R022 Revenue Report (46), R013 CRO Wise Sales (28), R003 All Discount Type (34), STOCK_LEDGER Variant Stock ledger (21), CLOSING_STOCK (20). All six match the real WLMHW and HEMW headers exactly (verified against the dumps).
- Horizontally duplicated exports (WLMHW Revenue Report 92 cols, WLMHW SDB-VariantwiseSales 82, HEMW Closing Stock 40, HEMW Daywise 58, WLMHW Scheme Details 28, HEMW Encircle Enrollment 38) are collapsed only if both halves are byte-identical for every row (`Preflight/WorkbookLayoutNormalizer.cs:16-53`). Good.
- **High — signature collision**: `AdvanceOrder Collection` (both stores) has the *identical* 46-column header as `Revenue Report`. It is header-only today, but `ImportPreflight` has no row-count minimum and the R022 orchestrator accepts zero rows (`R022SqlImportOrchestrator.cs:50-57`: `dates.Length == 0` → uses the UI store/date). Importing it registers a zero-row current R022 file for that scope, which then blocks restatement of the real Revenue Report (finding 4). Nothing looks at the file name or a content fingerprint.
- **Medium — profile identity is not DB-governed**: `SqlServerImportProfileResolver.cs:42-49` auto-INSERTs the compiled profile into `dbo.import_profiles` on first use; the DB row is only checked for equality/active afterwards (`:33-38`). The "approved profile registry in the database" is effectively the C# constant.
- `Purchase Return` / `Purchase Receipt` / `INV` / `SR` are the only stock types accepted (`Stock/StockWorkbookParser.cs:19`); the real ledgers contain exactly those four. Good.

### A.2 Store and business date — derived from file content, then compared with mandatory UI inputs

- The workbook contains `STORE CODE` and `INVDATE` on every row; the orchestrators derive store = single distinct `store_code` (`R025SqlImportOrchestrator.cs:59,77-86`) and business date = `MAX(transaction_date)` (`:60`, R022 `:54-55`, Stock `StockImportOrchestrator.cs:36`, Enrichment `RetailEnrichmentSqlImportOrchestrator.cs:56`).
- The UI still forces the user to pick both (`Modules/Imports/ImportWorkspaceView.xaml:9-15` — a hard-coded `WLMHW`/`HEMW` ComboBox, `SelectedIndex=-1`; `ImportWorkspaceView.xaml.cs:280-287` throws if either is missing; default date = yesterday `:27`). `ValidateScope` (`R025SqlImportOrchestrator.cs:88-98`) then throws "does not match the selected store/business date" on any disagreement. The UI values only *win* when the file has zero rows.
- **High**: For the real 01-Jul→25-Aug exports the "business date" the user must type is the export's last date (25 Aug 2026). If they type the export date (25 Aug) it happens to work; any other day fails with an opaque `InvalidOperationException`. `import_files.business_date`, `source_report_date`, `IX_import_files_current_scope`, the locked-day trigger `trg_import_files_protect_locked` (`0005_daily_reporting_workflow.sql:132-143`), evidence linking (`ProductisationRepository.cs:178-186`) and `FindCurrentImportAsync` all treat a 55-day file as belonging to one day. A future daily export for 24 Aug of the same store is a *different scope*, so the row-identity problem in C.5 applies.
- **High**: Batch mode captures one `DesktopImportRunContext` before the loop (`ImportWorkspaceView.xaml.cs:234`) and reuses it for every file (`DesktopImportCoordinator.cs:219-220`). The real deliverable is a folder of 30 files per store; a ZIP of both stores, or files ending on different dates, fails per-file with mismatch errors classified as `IMPORT_PROCESSING_FAILED` ("Review the support package") because `InvalidOperationException` is not mapped in `BatchImportCoordinator.cs:57-64`.
- Stores are not validated against `dbo.stores` (only the two literal ComboBox items); `import_batches.store_id` is always `NULL` from all orchestrators (`R025SqlImportOrchestrator.cs:65`, `storeId` never supplied by `SqlServerImportPersistenceUseCase.cs:121-127`). Low.

### A.3 Duplicate detection (SHA-256)

- File hash = SHA-256 of the raw bytes snapshot (`Workbooks/OpenXmlWorkbookReader.cs:22-26,47`) — hashing and parsing use the same in-memory copy, so a file being rewritten by Excel cannot desynchronise hash and content. Good.
- Enforced by `UX_import_files_source_sha256` (`0002_reporting_facts.sql:5`) and pre-checked in `DesktopImportCoordinator.cs:92-100,209-216` and `AutomatedOperationsService.cs:113-117`. Exact duplicates are reported, not re-imported. Good.
- Because the hash covers the whole file, a re-export of the same period from ETP (which embeds a new export timestamp in the file name only, but also usually differs in XML) is *not* a duplicate — it is a new file, and row-level identity then decides (C.5). `ImportPreflight.Inspect` accepts `previouslyImportedSha256` (`ImportPreflight.cs:23,34-35`) but no caller passes it (`MatchedImportEnvelopeFactory.Inspect`, `Preflight/MatchedImportEnvelope.cs:85`), so the `DUPLICATE_FILE` diagnostic is dead code in production.

### A.4 Restatement flow

- UI checkbox + reason (`ImportWorkspaceView.xaml:18-19`); Owner only (`ImportWorkspaceView.xaml.cs:275-276`, server-side `SqlServerImportPersistenceUseCase.cs:174-181`). The coordinator resolves the previous current file by (report, store, date) (`DesktopImportCoordinator.cs:252-272`) and the new file must have a new hash (`:95-98`).
- SQL: `dbo.prepare_import_restatement` (`0010_operational_completion.sql:266-323`) archives the previous file's facts as JSON, deletes them, marks the file superseded, refuses locked days and scope mismatch. Runs inside the same transaction as the replacement insert (`SqlServerRepositories.cs:99-100`). Reasonably sound.
- **Critical (finding 4)**: there is no restatement-or-reject rule on the *normal* path. `PersistAsync` never asks whether a current file already exists for the scope. Two normal imports of two different exports for the same store/date both become current; `FindCurrentImportAsync` then throws (`OperationalCompletionRepository.cs:72-77`), so the only recovery tool is blocked precisely when it is needed.
- **Medium**: restatement of a range export is scoped to the single `MAX` date; the archive/delete removes facts for all 55 days of the previous file (correct for the file, but the UI vocabulary "business date" misleads).
- Enrichment restatement is also implemented (`RetailEnrichmentSqlImportOrchestrator.cs:92-103`).

### A.5 ZIP / folder batch flow

- `Batch/BatchImportSource.cs:17-51` accepts a single `.xlsx`, a folder (recursive, depth ≤ 8, ordered) or a `.zip` extracted to `%TEMP%\EtpReporting\<guid>` (`:37`). Sequential per-file processing with per-file outcome and retry list (`Batch/BatchImportCoordinator.cs:81-137`, `DesktopImportCoordinator.cs:148-177`).
- **Medium**: folder discovery keys on extension only (`BatchImportSource.cs:74`); Excel's `~$Book.xlsx` lock files are `.xlsx`, > 0 bytes, and are picked up → each fails with `IMPORT_PROCESSING_FAILED`. The automation path skips `~$` (`AutomatedOperationsService.cs:35`), the UI path does not.
- **Medium**: extracted ZIP contents (which contain customer names/phones) stay in `%TEMP%` until the next batch or app close (`DesktopImportCoordinator.cs:141-146,184-190`); disposal is best-effort and swallows IO errors (`BatchImportSource.cs:116-121`).
- ZIP must contain only `.xlsx` (and folders); anything else fails the whole archive (`BatchImportSource.cs:103-104`). A store's "ALL REPORT" ZIP containing the 24 non-importable reports therefore *cannot be batch-imported at all* — each non-profile workbook is `IMPORT_LAYOUT_BLOCKED` and counted as Failed. In practice the owner must hand-pick the 6 usable files. High UX friction.

### A.6 Cancellation and retry

- Cancellation is checked between files and inside the reader (`BatchImportCoordinator.cs:91-96,114-121`; `OpenXmlWorkbookReader.cs:16,46,56,66`). During SQL persistence the token is passed to each command; a cancel mid-file rolls back (`SqlServerRepositories.cs:110`). Good.
- Retry: only `IOException`/`TimeoutException` are transient, max 2 attempts (`BatchImportCoordinator.cs:55,73`). `SqlException` (deadlock, timeout, 51030 locked-day THROW) is not transient and is reported as generic `IMPORT_PROCESSING_FAILED` — the user never sees "business date is finalised". Medium.
- **Medium**: in batch mode, if persistence commits but evidence retention throws an `IOException` (e.g. `C:\ProgramData\EtpReporting\Documents` not writable), the coordinator retries; the second attempt sees the hash already present and reports `ExactDuplicate = true` (`DesktopImportCoordinator.cs:209-216`). The file shows as a duplicate and no evidence is ever retained.
- "Retry failed" re-runs the same context, so scope-mismatch failures cannot be fixed by retry (`ImportWorkspaceView.xaml.cs:97-102`).

### A.7 `LEGACY_IMPORT`

`imported_by`, `store_code`, `business_date`, `source_report_date`, `report_code` were added to `import_files` in `0005_daily_reporting_workflow.sql:4-9` (nullable). `0006_backfill_import_business_scope.sql:4-39` back-fills any pre-0005 rows by deriving scope from lineage/facts and stamps `imported_by = N'LEGACY_IMPORT'` (`:34`). It therefore means "imported before the scope columns existed; importing user unknown". On a fresh install (all 15 migrations at bootstrap) no row can carry it; the sentinel only matters for pre-0005 databases. No code treats it specially.

### A.8 UX friction list (Import Files screen)

1. Mandatory store/date entry that the file already contains; wrong guess → opaque error (A.2).
2. Store list hard-coded to two literals (`ImportWorkspaceView.xaml:13-14`), not from `dbo.stores`.
3. One store/date for the whole batch (A.2).
4. Any change to date/store/path invalidates validation and requires re-validate (`ImportWorkspaceView.xaml.cs:43-48`) — but the validation result does not depend on those inputs.
5. "Import batch" over a store's full export ZIP fails 24 of 30 files (A.5).
6. Batch progress bar is per file; a 55-day R025 file persists row-by-row (2 round trips per row, `SqlServerRepositories.cs:101-105,122-123`) with no progress and `CommandTimeout = 0` (`:146`).
7. Conflicts show as a count only; `import_conflicts` rows are reachable through a different workspace.
8. Failure text is generic for SQL errors (A.6).
9. No indication that R025 lines are ex-GST while R022 controls are GST-inclusive (C.9).

---

## B. Workbook coverage — what is and is not importable

Real corpus: WLMHW 29 files, HEMW 30 files (SOR Sales exists only for HEMW). One sheet each, headers on row 1, no styles/formulas.

### B.1 Importable today (6)

| Report | Profile | Note |
|---|---|---|
| SDB-VariantwiseSales | R025 | canonical lines; only 22 of 41 columns mapped (`RetailSalesProfiles.cs:46-70`); tax components, UCP, PROMO_GC, HELIOS_CREDITNOTE never persisted (persistence record has no slots — `PersistenceContracts.cs:23-28`). |
| Revenue Report | R022 | 27 tender columns → `sales_tenders`; `ENCIRCLE`, `REFERENCEYEAR` dropped |
| CRO Wise Sales | R013 | enrichment only; `CRO NAME` dropped; see finding 7 |
| All Discount Type | R003 | enrichment; `ACTIVATION DETAILS`/`USER DISCOUNT DETAILS` are *not* in the profile (`RetailSalesProfiles.cs:72-85`) although the orchestrator binds them (`RetailEnrichmentSqlImportOrchestrator.cs:182-183`) → always NULL |
| Variant Stock ledger | STOCK_LEDGER | `LOCATION` (`RETAILBIN` on every real row), `REF_DOCUMENTNUMBER/DATE` not mapped |
| Closing Stock | CLOSING_STOCK | no location column exists in the ETP export |

### B.2 Not importable (24) — and the ones the owner's reports need

Header rows are printed verbatim from the real files. Plug-in points for any new profile: (1) header list + `ImportProfile` in `src/Etp.Reporting.Import/Profiles/*.cs` and registration in `ApprovedImportProfileRegistry.cs:8-16`; (2) a typed projection in `src/Etp.Reporting.Import/Staging/` (pattern: `R022PersistenceProjection.cs`); (3) a persistence record + validation in `Infrastructure.SqlServer/PersistenceContracts.cs` and an insert in `SqlServerRepositories.cs:101-105`; (4) a route in `SqlServerImportPersistenceUseCase.SelectRoute` (`:80-86`) and a branch in `AutomatedOperationsService.ProcessWorkbookAsync` (`:127-135`); (5) a new `database/migrations/0016_*.sql` with the fact table, a `persist_*` procedure that writes `import_row_outcomes` (pattern `0014_productisation.sql:445-456`), a locked-day trigger (pattern `0009_locked_day_fact_guards.sql`), and an entry in `prepare_import_restatement` (`0010:266-323`); (6) the Desktop result text switch in `ImportWorkspaceView.xaml.cs:158-164`; (7) `MigrationTests.Foundation_and_fact_migrations_have_required_control_boundaries` (`tests-dotnet/Etp.Reporting.SqlServer.Tests/MigrationTests.cs:224`).

**Needed for cash book & tenders**

- **Payment Type Report** (36 cols, multi-row per invoice, resolves `PAYMENTTYPE25`):
  `CHANNEL, TYPE, REGION, STORE CODE, STORENAME, CITY, STATE, INVNUMBER, INVDATE, INVOICE AMOUNT, DOC TYPE NO, AGENCYNAME, CREDITCARDNO, APPROVALNUMBER, CASHAMOUNT, CARDAMOUNT, CHEQUEAMOUNT, GVAMOUNT, GCAMOUNT, CREDITNOTE, LOYALTYPOINTS, ROUND OFF, NO REFUND, TATA GV, REFUND, PAYMENTTYPE15, GYFTR, PAYTM, HELIOSOMNI, PAYMENTTYPE19, PAYMENTTYPE20, PAYMENTTYPE21, PAYMENTTYPE22, PAYMENTTYPE23, PAYMENTTYPE24, PAYMENTTYPE25`
  Real-data mapping: `PAYMENTTYPE25` ↔ `AGENCYNAME='AIRPAY'` (263 rows, ₹1,271,483 WLMHW); `PAYMENTTYPE20` ↔ `'PHONEPE'` (= Revenue Report `PHONEPE` column); `CARDAMOUNT` ↔ `CC01/CC02/CC10`. `CREDITCARDNO`/`APPROVALNUMBER` are restricted (card/UPI transaction ids) and must be dropped at staging like customer PII (`Staging/ImportRowStager.cs:42` pattern).
- **Daywise Collection** (29 logical cols; HEMW export is horizontally doubled to 58 — normaliser handles it):
  `STORE CODE, STORE NAME, INVOICEYEAR, INVOICE DATE, CASH, CREDITCARD, CN_UTILISED, GIFTCARD, CHEQUE, ROUND OFF, NO REFUND, TATA GV, LOYALTY, CN_ISSUED, CASH_REFUND, TATACLIQ, GYFTR, PAYTM, HELIOSOMNI, PAYMENTTYPE19, PAYMENTTYPE20, PAYMENTTYPE21, PAYMENTTYPE22, PAYMENTTYPE23, PAYMENTTYPE24, PAYMENTTYPE25, PAYMENTTYPE26, OMNI, TOTAL_REVENUE`
  One row per day = the natural daily cash-book control (`TOTAL_REVENUE` ties to Revenue Report `NetValue`).
- **Banking Details** (16 cols):
  `STORE CODE, STORENAME, BANKDEPOSITNUMBER, TransactionDate, TRANS_TYPE, INVOICE YEAR, INVOICE NUMBER, INVOICE DATE, AMOUNT, BANKEDON, BANKEDAMOUNT, UNBANKED AMOUNT, DEPOSIT_SLIPNO, CC_CHEQUENO, DEPOSIT DATE, CREATEDATE`
  Note `TransactionDate`, `BANKEDON`, `DEPOSIT DATE`, `CREATEDATE` are `0` when unbanked (WLMHW row 1) — the numeric-zero→null rule in `TypedCellConverter.cs:23-26` covers this.
- **Banking Summary** (14 cols): `STORE CODE, STORE NAME, INVOICEYEAR, TRANSACTIONDATE, CASH, CARD, CHEQUE/DD, TOTAL, CASH DEPOSITED, CC DEPOSITED, CHEQUE/DD DEPOSITED, CASH DIFFERENCE, CARD DIFFERENCE, CHEQUE/DD DIFFERENCE`
- **Transactionwise Bank** (9 cols, header-only in WLMHW): `STORE CODE, STORE NAME, BANKDEPOSITNO, INVOICEYEAR, TRANS_TYPE, INVOICENUMBER, TRANS_DATE, BANKEDAMOUNT, BANKEDDATE`

**Needed for credit notes**

- **CN Register** (21 cols):
  `STORE CODE, STORE NAME, STORE TYPE, CHANNEL, REGION, STATE, CITY, CREDITNOTENUMBER, CREDITNOTEDATE, REF_GRNNO, CN_REFDOCNO, EXPIRYDATE, ISSUETO, CREDITNOTEAMOUNT, INVOICENUMBER, INVOICEDATE, INVOICEVALUE, REDEEMED DATE, REDEEMED BY, ISSUED_CNNO, ISSUED_CNAMT`
  `ISSUETO`/`REDEEMED BY` are customer names (restricted). The digital `CREDIT_NOTE` register (`0014:109`) is manual-entry only; nothing populates it from this export.

**Needed for customer-wise invoices**

- **SDB Document Wise** (33 cols, one row per invoice, unique on store+invoice in both stores):
  `TRANS_TYPE, STORE CODE, STORENAME, STORE TYPE, CHANNEL, REGION, CITY, INV NUMBER, INV DATE, QTY, GROSSUCP, SCH_DISCOUNTS, NETGROSS, PRE_DISCOUNTS, NETAMOUNT, SGST/UTGST VALUE, CSGT VALUE, IGST VALUE, cess VALUE, TAX, TAX INC, TAX EXC, NETVALUE, INVREFNO, INVREFDATE, CUSTOMER NO, CUSTOMER NAME, CONTACT NO, ULP NUMBER, Customer GSTIN No, Customer Address, STORE TIMESTAMP, EAS TIMESTAMP`
  Contains the most PII of any export (name, phone, GSTIN, address). A "customer-wise invoice" report cannot be built without deciding to persist some of it; the current PII-drop rule (`ImportRowStager.cs:42`, `docs/05_MAPPING_REGISTER.md:3`) forbids it. Real totals: `NETVALUE` 1,816,486 (ex-GST) + `TAX INC` 326,967 = Revenue Report `NetValue` 2,143,453 — confirming R025/SDB `NETVALUE` is ex-GST and R022 `NetValue` is GST-inclusive.

**Staff sales** — CRO Wise Sales is already R013, but only `CRO NUMBER` is persisted (`RetailSalesProfiles.cs:93`); the name must come from a staff master that does not exist (there is no `staff`/`cro` table in any migration; `staff_sales_targets` (`0010:90`) keys on a free-text code).

**Closing stock with location (Display / Backstock / Defective)** — not available from ETP. `Closing Stock` has no location column; `Variant Stock ledger.LOCATION` is `RETAILBIN` on all 1,079 real rows; `FROM/TO LOCATION` are warehouse codes (`WPUN`, `NRWH`…). The only location-bearing source would be the manual `manual_stock_counts` table (`0010:49`). Any "display vs backstock" report must be a manual register, not an import.

**Service sales** — there is no service export among the 30 (PRP SALES / PRP STM are header-only in both stores). The `SERVICE_RECEIPT` register (`0014:109`) and `manual_operational_inputs` (`0008_service_cash_inputs.sql`) are the only carriers.

**Other non-importable** (all lack profiles): All Issues Detail/Summary, All Receipts Detail/Summary, Purchase Reciept Summary, GST Tax Report Issue/Reciept, Encircle Enrollment/Redemption, GC Wise Redemption, Scheme Details, SOR Ageing, SOR Sales, AdvanceOrder Sales/Collection, PRP SALES, PRP STM.

---

## C. Correctness bugs in parsing / staging / persistence

### C.1 INVDATE numeric `yyyymmdd` — OK, with one latent crash

- Numbers are read as `decimal` (`OpenXmlWorkbookReader.cs:108`); `TypedCellConverter.ParseDate` (`Conversion/TypedCellConverter.cs:68-75`) falls through to `DateOnly.TryParseExact(source.ToString(), ["yyyy-MM-dd","yyyyMMdd",…])`, so `20260804` → 2026-08-04. Closing Stock `Date` is a *string* `'20260825'` → same path. Verified by `TypedCellConverterTests.Numeric_etp_date_conversion_uses_yyyyMMdd`.
- `0` placeholders (`INVREFDATE`, Banking `TransactionDate`) → `null` for optional, `VALUE_REQUIRED` for required (`:23-26`). Good.
- **Low (latent)**: if a cell carrying `20260804` ever has a date number-format style, `OpenXmlWorkbookReader.cs:105-107` calls `DateTime.FromOADate(20260804)` → `ArgumentException` (max OADate ≈ 2,958,465), which is *not* caught by the converter and aborts the read with a generic failure. The real files have no styles today.
- `DateFormats` includes `dd/MM/yyyy` and `dd-MM-yyyy` but not `MM/dd/yyyy`; `STORETIMESTAMP` (`20260804201107`) is mapped as Text. Fine.

### C.2 Decimal precision

- Cells are parsed as `decimal` with `NumberStyles.Float` (`OpenXmlWorkbookReader.cs:108`), so `2538.14` is exact; columns are `decimal(19,4)`. Good. `NumberStyles.Number|AllowLeadingSign` for strings (`TypedCellConverter.cs:58`) rejects thousands separators with parentheses and scientific notation (`1E5`) → `VALUE_INVALID` blocker for the whole file (fail-closed, acceptable).
- Currency is a constant `"INR"` (`R025SqlImportOrchestrator.cs:21`). OK.

### C.3 Negative SR rows / sign conventions

Real data (both stores):

| Export | INV | SR |
|---|---|---|
| SDB-VariantwiseSales (R025) | qty>0, NETVALUE>0 | qty<0, NETVALUE<0 |
| CRO Wise Sales (R013) | qty>0, NETVALUE>0 | **qty<0, NETVALUE>0** |
| Revenue Report (R022) | InvoiceQuantity>0, NetValue>0 | **InvoiceQuantity>0, NetValue<0** |
| Variant Stock ledger | INV TRANS_QTY<0; SR +; Purchase Receipt +; Purchase Return − | |

- R025 preserves source signs (`R025SqlImportOrchestrator.cs:55`; test `R025SqlImportOrchestratorTests.cs:16-17`). Good.
- **High (finding 7)**: R013 `SR` rows are stored with positive `source_net_value` and consumed by `OperationalReportRepository.cs:310-325` (`SUM(e.source_net_value)` grouped by CRO). Returns *add* to staff sales. `source_transaction_type` is stored (`RetailEnrichmentSqlImportOrchestrator.cs:174`) but never used for sign.
- R022 `SR` invoice controls have positive quantity and negative net value; the tender columns for an SR row carry `ISSUED CREDITNOTE` = −value. Projection keeps signs (`R022PersistenceProjection.cs:58`). OK, but the reconciliation compares `source_net_value` to `SUM(source_amount)` of *eligible* tenders — see C.4.
- Stock: sign preserved; `closing = opening + trans` is enforced in code and by `CK_stock_movements_balance` (`0002:74`). Good.

### C.4 TRANS_TYPE handling

- Sales profiles accept any text for `TRANS_TYPE` (no allow-list) — only stock restricts to 4 values. An unexpected sales type (e.g. `ADV`) is persisted silently. Low.
- `source_transaction_type` is stored on lines, controls and enrichments, but the R022 tender rows carry no type; a return's `ISSUED CREDITNOTE` negative tender is indistinguishable from a sale with a credit-note tender except by sign. Medium.

### C.5 Invoice vs line dedupe — the row-number identity (Critical, findings 2 & 3)

- `line_identifier` = `row.SourceRowNumber` (`R025SqlImportOrchestrator.cs:52`). SQL identity = `store/year/doc/<row>` (`0014_productisation.sql:385`); content hash = date|product|type|qty|gross|net|brand|segment|currency (`:386`).
- Consequences with real ETP behaviour (range exports re-run daily; late invoices inserted in date order):
  - Same invoice at a different row → `NEW` → duplicate fact (double revenue).
  - Different invoice line now occupying an old row number of the same invoice → `CONFLICT` → `RETURN` at `:402-403` → **row never inserted**, batch still `Completed` (`SqlServerRepositories.cs:107-108`), file hash registered, `LoadOutcomeByHashAsync` reports conflicts only as a number (`:52-70`).
  - `docs/BULK_IMPORT_AND_DUPLICATION.md:8-11` describes the intended behaviour ("skip identical July facts"); it holds for R022 (identities `…/CONTROL` and `…/TENDER/<type>`, `0014:414,433`) and stock, **not** for R025 which is the canonical sales source. Real data has store+invoice+item unique in both stores (verified: 0 duplicate pairs), so `store/year/doc/product` is the obvious natural line key.
- Enrichment (R003/R013) has no identity check at all (`RetailEnrichmentSqlImportOrchestrator.cs:153-166`); `R003` is legitimately non-unique on store/invoice/item (9 duplicate triples in WLMHW) so lineage-only identity is defensible for R003, but re-imports still double it.
- Stock snapshot identity `store/date/product/COALESCE(uid,batch,ean)` (`0014:463`): `UID_ITEM` is the literal string `'UID'` on 693 of 787 WLMHW rows and blank on all HEMW rows, so the identity is effectively `store/date/product`. No collisions in this corpus (each product is one row with qty), but a second row for the same product/date (e.g. different batch with blank UID) would be silently `CONFLICT`ed away. Medium (latent).
- Tender identity `…/TENDER/<TYPE>` (`0014:433`) collapses multiple tenders of the same type on one invoice into one; Revenue Report is one row per invoice so this is fine, but a Payment Type Report profile (multi-row) could not reuse `persist_sales_tender`.

### C.6 PAYMENTTYPE25 quarantine (Critical, finding 1)

- Quarantined in three places: projection (`R022PersistenceProjection.cs:59-61`), package validation (`PersistenceContracts.cs:97-98`), and the procedure (`0014:432`). `reporting_sales_tenders` view excludes it (`0002_reporting_facts.sql:120-122`); tender reports (`SqlServerReportingQueryRepository.cs:31-37`) and the invoice-vs-tender reconciliation (`ReportingTenderVarianceDiagnostic.cs`) therefore see no AIRPAY money.
- Real impact: WLMHW 258/404 invoices, ₹1,271,483 of ₹2,143,454 net; HEMW 1 invoice (HEMW customers use PhonePe = `PAYMENTTYPE20`, which *is* mapped as `PHONEPE` in R022). Every WLMHW day will show a tender variance ≈ 59 % of sales. The Payment Type Report identifies the agency as `AIRPAY` on every such row; the decision to keep it "unresolved" (`docs/05_MAPPING_REGISTER.md:28`) is stale.
- Fix path: add a resolved tender code (e.g. `AIRPAY_UPI`) in `R022PersistenceProjection.TenderFields` (`:29-43`), remove the three hard quarantines, and back-fill `sales_tenders.is_reporting_eligible` for `PAYMENTTYPE25` under a controlled migration + audit event.

### C.7 Transactions / rollback / partial failure

- `SqlServerTransactionalImportStore.PersistAsync` is one transaction: batch → profile → file → restatement → rows → completion → commit; any exception rolls back (`SqlServerRepositories.cs:88-111`). `import_batches` therefore never records a `Failed` row from this path (`FailAsync` in `:17-18` is unused by orchestrators) — failure history exists only in `operational_audit`/diagnostics. Low.
- Enrichment path is its own transaction (`RetailEnrichmentSqlImportOrchestrator.cs:62-130`). OK.
- Post-commit side effects are not transactional: evidence retention (`DesktopImportCoordinator.cs:234-241`, `ProductisationOperationsService.cs:7-15`) and `LinkDocumentToImportAsync` run after commit; failure leaves an imported file without a Source Inbox record (the UI message at `ImportOperationState.cs:7-9` admits this). Medium.
- Row-by-row inserts with a lineage round-trip per row (`SqlServerRepositories.cs:122-127`) and `CommandTimeout = 0` (`:146`): a 55-day R025 file ≈ 900 statements; acceptable now, but no batching/TVP and no progress. Low.
- `RefreshEnrichmentMatches` runs a full-table `UPDATE … CROSS APPLY` on every sales import (`:128-145`), not scoped to the file. Low.

### C.8 Lineage completeness

- Every persisted row has a `source_lineage` row (file, sheet, source row, record type) with FK + unique constraints (`0002:7-17,35-39`). `PersistenceValidation.Validate` enforces non-empty sheet and positive row (`PersistenceContracts.cs:104-111`). Good.
- Gaps: rows skipped as `CONFLICT`/`ALREADY_PRESENT` still get a lineage row but no fact — lineage → fact is not total, and nothing distinguishes "lineage without fact" from an anomaly except `import_row_outcomes`. The collapsed duplicate half of a doubled layout is not recorded (by design, `WorkbookLayoutNormalizer.cs:42-45`). The physical column index is not stored, so a lineage row cannot point back to a cell. Low.

### C.9 Idempotency

- Same file twice: idempotent (hash). Same content re-exported (new hash): **not idempotent** for R025 (C.5) and enrichment (C.5); idempotent for R022/stock when values are unchanged (`ALREADY_PRESENT`).
- R025 lines are ex-GST (`NETVALUE`), R022 controls are GST-inclusive (`NetValue`): any reconciliation of `sales_lines` totals against `sales_invoice_controls` is off by exactly the tax (₹326,967 for WLMHW Jul–Aug). The persistence layer offers no `source_gross_amount` for lines (always NULL, `R025SqlImportOrchestrator.cs:55`) though `NETAMOUNT` is in the profile (`RetailSalesProfiles.cs:65`). High (adjacent to the known finding).

### C.10 Other

- `SalesImportBlockedException` message concatenates `Code:ColumnName` (`R025SqlImportOrchestrator.cs:11`); it is logged via `DesktopDiagnostics` — no PII. Good.
- `R022SqlImportOrchestrator.PersistAsync` computes the projection twice (once for persistence, once for the result in `SqlServerImportPersistenceUseCase.cs:106-113`). Low.
- `AutomatedOperationsService.ProcessWorkbookAsync` never passes `expectedStoreCode/BusinessDate` (`:127-135`) — correct (file-derived), which shows the UI inputs are unnecessary.

---

## D. Source Inbox / OCR / documents, Registers, Watch folder — implemented or scaffolding?

### D.1 Source Inbox and document repository — implemented (store, hash, review); real

- `ManagedDocumentRepository.StoreAsync` (`DocumentIntakeService.cs:108-134`) copies to `<repo>\yyyy\MM\<sha256><ext>`, re-hashes after copy, rejects reparse points, > 100 MB and disallowed extensions. `dbo.source_documents` has a unique hash, lifecycle CHECK (`0014:48-53`), and SYSTEM is denied UPDATE/DELETE (`0014:518`). Intake, list, extraction list, verify/reject with reason, integrity re-check and shell-open are wired end to end (`SqlServerSourceInboxService.cs`, `ProductisationRepository.cs:79-160`, `Modules/SourceInbox/SourceInboxWorkspaceView.xaml.cs`). ETP workbooks imported through the UI or automation are retained as `ETP_WORKBOOK` evidence and linked to `import_files` (`ProductisationOperationsService.cs:7-15`, `ProductisationRepository.cs:178-186`). **Real.**
- Lifecycle after verify is `VALIDATED`; nothing consumes a validated PDF (no register auto-fill, no structured field mapping). `structured_fields_json` is stored but never read.

### D.2 Native PDF text — scaffolding (High, finding 10)

`NativePdfTextExtractor` (`DocumentIntakeService.cs:15-39`) decodes the whole PDF as Latin-1 and regex-scans for `(…) Tj/TJ` literals. Modern PDFs (including anything from a phone scanner or ETP) use `FlateDecode` content streams and hex strings, so the regex finds nothing → empty text → `REVIEW_REQUIRED`. There is no PDF library reference in `Etp.Reporting.Infrastructure.SqlServer.csproj`. Confidence is hard-coded `1m` when ≥ 20 alphanumerics are found (`:30-31`). The `usable ? "REVIEW_REQUIRED" : "REVIEW_REQUIRED"` ternary (`:32`) shows review-status logic was never finished.

### D.3 PaddleOCR — scaffolding

`PaddleOcrProcessExtractor` (`DocumentIntakeService.cs:41-101`) launches whatever executable `product_settings.ocr_helper_path` points to with `--input/--output/--model-dir`, waits ≤ 2 min, parses a JSON `{Text,Confidence,Version,Page,BoundingBoxes,Fields}`. No helper, model, contract spec or hash verification exists in the repo (`find -iname "*ocr*"` returns only UI screenshots under `artifacts/`); `docs/DOCUMENT_OCR_ARCHITECTURE.md:9` calls a signed helper "recommended". Confidence gating at `:74-75` is dead (both branches `REVIEW_REQUIRED`). The health check merely tests `File.Exists(ocr_helper_path)` (`ProductisationRepository.cs:437`). **Security note**: an Owner-editable DB path decides which executable the app (or the SYSTEM-level scheduled task) runs — path is not restricted to the install directory, unlike `PowerShellOperationsService.cs:18-22`. Medium.

### D.4 Registers — thin but real CRUD; several gaps

- Schema: `register_entries` with 8 types (`INWARD, OUTWARD, CREDIT_NOTE, SERVICE_RECEIPT, COURIER, STOCK_TRANSFER, EXPENSE, VENDOR_INVOICE`, `0014:109`), unique on type/store/date/document, append-only JSON history trigger (`0014:478-489`), locked-day guard in the MERGE (`ProductisationRepository.cs:192-193`). Real.
- **Medium**: the UI offers only 7 types — `COURIER` is missing from `RegistersWorkspaceView.xaml:25-31` and from the task map (`RegistersTaskNavigation.cs:33`).
- **Medium**: every save is forced to `verification_status = "DRAFT"` and `document_date = null` (`RegistersWorkspaceView.xaml.cs:77,83`); there is no UI to move an entry to `VERIFIED`/`REJECTED` even though the CHECK allows it (`0014:111`). The "verify" workflow described in `docs/REGISTER_ARCHITECTURE.md:5` does not exist.
- **Medium**: `SqlServerDigitalRegisterService` has no server-side access check (`SqlServerDigitalRegisterService.cs:28-41`), unlike Source Inbox (`SqlServerSourceInboxService.cs:104-114`); only the view checks `CanImport` (`RegistersWorkspaceView.xaml.cs:103-107`). Viewer accounts with DB access can write registers through any other client.
- Register store code is free text (`RegistersWorkspaceView.xaml:32`), not validated against `dbo.stores`. The grid is filtered client-side by the selected type after loading 500 rows of all types (`RegistersWorkspaceView.xaml.cs:51-52`). Register linkage to a Source Inbox document is by the `LinkedSourceDocumentId` property set from the shell, never displayed. No register is populated from any ETP export (CN Register, Purchase Receipt, Issues/Receipts are all manual re-keying). Low–Medium.

### D.5 Watch folder / scheduler — implemented (one-shot), with caveats

- `AutomatedOperationsService.RunOnceAsync` (`AutomatedOperationsService.cs:17-108`): app-lock lease, settings from `dbo.watch_folder_settings` (`0011_phase2_operations.sql:93-110`), path policy (`Phase2OperationsRepository.cs:488-507`: absolute, local, non-root, distinct, not nested in inbound), stability check (10 s + exclusive open, `:188-197`), per-source processing, move to Processed/Duplicate/Failed, `automation_runs` audit, auto report packs. Triggered by `EtpReporting.exe --automation-once` via `scripts/install-etp-automation-task.ps1:10-14` (SYSTEM, RunLevel Highest, repeating every N minutes). There is no in-process timer/`FileSystemWatcher`; "poll_minutes" is only advisory to the task installer. Real.
- **Medium**: a ZIP/folder source is processed workbook-by-workbook without per-workbook try/catch (`:60-66`); if the 3rd file throws, files 1–2 are already committed, the ZIP moves to Failed, and a retry re-processes 1–2 as duplicates while 3–n never run unless someone moves the ZIP back.
- **Medium**: the task runs as `SYSTEM` while `0013_store_manager_permission_guards.sql:11` and `0014:518-522` deny SYSTEM updates to several tables; imports write via procedures owned by dbo and succeed, but any code path that later UPDATEs `source_documents` (e.g. `LinkDocumentToImportAsync`, `ProductisationRepository.cs:180-183`) will fail under SYSTEM. Evidence linking from the scheduled task is therefore likely broken; `IntakeEtpEvidenceAsync` is called from `ProcessWorkbookAsync:137`. Needs a live test.
- Every non-profile ETP export dropped in the inbound folder goes to Failed with `IMPORT_LAYOUT_BLOCKED`; a store's full daily "ALL REPORT" ZIP always ends in Failed (same as A.5). High UX.
- Import identity is `DOMAIN\user` of the task account (`:199`) — `NT AUTHORITY\SYSTEM` — so `imported_by` is not a person. Low.
- `PowerShellOperationsService` (`src/Etp.Reporting.Desktop/PowerShellOperationsService.cs`) is unrelated to imports: an allow-list of three maintenance scripts under the install `scripts\` folder, executed with `-ExecutionPolicy Bypass`; server/database names are passed as discrete arguments and the DB name is regex-validated (`:51-60`). Reasonable.

---

## E. Security / safety

| Rating | Item | Evidence |
|---|---|---|
| Good | ZIP-slip: rooted paths and `..` escapes rejected against the canonical extraction root; depth ≤ 8; symlink entries rejected | `Batch/ImportPathPolicy.cs:92-106,119-123` |
| Good | Reparse points rejected on source, every discovered file and directory, and the document repository root | `ImportPathPolicy.cs:108-116`, `BatchImportSource.cs:69,73,81`, `DocumentIntakeService.cs:113,120` |
| Medium | ZIP bomb: entry count ≤ 256, per-entry ≤ 100 MB, total ≤ 500 MB, ratio ≤ 200 are all computed from the **declared** `entry.Length/CompressedLength` (`ImportPathPolicy.cs:72-88`); extraction then `CopyToAsync`s without a byte cap (`BatchImportSource.cs:107-109`) and only checks size afterwards (`:110`). A forged central directory can write far more than 100 MB to `%TEMP%` before failing. Wrap the entry stream in a counting stream that aborts at `MaximumEntryBytes`. |
| Medium | Memory: `OpenXmlWorkbookReader` holds the file twice (`MemoryStream` + `ToArray()`, `:24-26`) plus the full row/cell object graph; bounded by the 100 MB file cap × up to 4 concurrent materialisations (`:10-11`). A 100 MB XLSX is a multi-GB object graph. Reduce the cap or stream rows. |
| Low | `.xlsm`/macros: only `.xlsx` extension accepted (`ImportPathPolicy.cs:17-18`), no content sniffing, but parsing uses the OpenXml SDK (no Excel COM) so VBA never executes. Retained originals are opened via `ShellExecute` from Source Inbox (`SourceDocumentLauncher.cs:21`) — a renamed `.xlsm` would then open in Excel with macros. Acceptable. |
| Good | File locking: reader opens with `FileShare.ReadWrite | Delete` (`OpenXmlWorkbookReader.cs:22-23`) so an export still open in Excel imports; automation waits for an exclusive open (`AutomatedOperationsService.cs:193`). |
| Medium | Temp cleanup: ZIP extractions (containing customer PII) persist in `%TEMP%\EtpReporting` until the next batch/app exit and deletion failures are swallowed (`BatchImportSource.cs:116-121`); OCR JSON output is deleted in `finally` (`DocumentIntakeService.cs:80`). No startup sweep of stale temp dirs. |
| Medium | OCR helper path is an Owner-editable, unrestricted executable path run by the app/SYSTEM task (`DocumentIntakeService.cs:48-64`); compare the install-folder restriction used for PowerShell scripts. |
| Low | `--automation-once` shares the interactive connection string; `SqlAdapterConnection.RequireWindowsIntegrated` enforces integrated auth (`SqlServerImportPersistenceUseCase.cs:32`). Good. |
| Low | PII: staging emits only mapped fields, and no sales profile maps customer name/phone (`ImportRowStager.cs:42`, tests `RetailSalesProfilesTests.cs:38`). But the full workbook (with PII) is retained verbatim in the document repository and the ZIP temp dir; `SalesImportBlockedException` messages include column *names* only. |
| Low | Path traversal on user-supplied paths: `ValidateExistingSource` canonicalises with `GetFullPath` and requires existence; no allow-listed root, so any readable file on the machine can be imported (by design for a desktop tool). |

---

## F. Test coverage gaps

What exists: 40 Import unit tests (synthetic workbooks generated in-test; `ProductionWorkbookIngestionTests.cs:14-30` builds its own doubled R022 layout) and SqlServer "boundary" tests that use capture fakes for `ITransactionalImportStore` (`R025SqlImportOrchestratorTests.cs:38-43`) and string inspection of migration files (`MigrationTests.cs:224-271`). **No test opens a SQL Server database**, so every `persist_*` procedure, trigger and `prepare_import_restatement` is untested; **no test opens any of the 59 real files**.

Missing, in priority order:

1. Row-identity/dedupe semantics of `persist_sales_line` with (a) same file re-exported with shifted rows, (b) overlapping-period exports, (c) a changed value → assert facts, `import_row_outcomes`, and that conflicts are *not* inserted (C.5). Requires a SQL Server fixture (LocalDB) — none exists.
2. Real-corpus ingestion: load all 59 files through `MatchedImportEnvelopeFactory`, assert the 6 profiles match, the 24 others block, `AdvanceOrder Collection` is *not* classified as R022, doubled layouts collapse (currently tested only with synthetic halves).
3. `PAYMENTTYPE25` total vs Revenue `NetValue` on real data (the quarantine effect is only tested as "cannot be eligible", `R022SqlImportOrchestratorTests.cs:29`).
4. Sign conventions per report (R013 SR positive net value) — no test.
5. `ValidateScope` with multi-day workbooks vs UI date; batch with mixed stores — no test (`DesktopImportCoordinator` has no tests in `Etp.Reporting.Desktop.Tests` for batch context capture).
6. Restatement end to end (archive JSON, deletes, superseded flag, locked day refusal) — only string-inspected (`MigrationTests.cs:271`).
7. Two current files for one scope → `FindCurrentImportAsync` throws — no test.
8. ZIP bomb with forged sizes; `~$` lock files in folder discovery; temp-dir cleanup on failure — no tests (only traversal and unsupported payload, `BatchImportTests.cs:27,43`).
9. `OpenXmlWorkbookReader` with date-styled numeric `yyyymmdd` cells (C.1 latent crash) — no test.
10. Evidence retention failure after commit (batch path reporting `ExactDuplicate`) — no test.
11. `NativePdfTextExtractor` against a FlateDecode PDF (expect empty) and `PaddleOcrProcessExtractor` contract (helper missing, timeout, bad JSON) — `SourceInboxServiceAdapterTests` only test the adapter mapping with fakes.
12. Register: `COURIER` type reachable; `VERIFIED` transition; locked-day rejection via `SaveRegisterEntryAsync` — no tests beyond mapping (`SharingContactsAndDigitalRegistersAdapterTests.cs:61-124`).
13. Automation: partial-ZIP failure, SYSTEM-account DENY interactions, duplicate routing — `MigrationTests.Automation_paths_require_distinct_local_non_root_folders` (`:326`) is the only automation test.

---

## Appendix — real-data facts used above

- WLMHW Revenue Report (404 invoices, 01 Jul–25 Aug 2026): NetValue 2,143,453.75; CASH 611,171; CARD 259,651; CREDITNOTE REDEEM 65,276; PHONEPE 570; **PAYMENTTYPE25 1,271,483 (258 rows)**. Payment Type Report maps the same 1,271,483 to `AGENCYNAME = AIRPAY` (263 rows).
- HEMW Revenue Report (86 invoices): NetValue 1,636,514.10; PHONEPE 778,574 (48 rows); PAYMENTTYPE25 7,000 (1 row, AIRPAY).
- SDB-VariantwiseSales SR rows: WLMHW 10, HEMW 3, all qty<0 and NETVALUE<0. CRO Wise Sales SR rows: qty<0, NETVALUE>0. Revenue Report SR rows: quantity>0, NetValue<0.
- Store/invoice/item is unique in R025 and R013 for both stores; R003 has 9 duplicate triples in WLMHW; SDB Document Wise is unique on store/invoice.
- Closing Stock: 787 (WLMHW) / 469 (HEMW) rows, all dated 20260825, no location column; `UID_ITEM` = `'UID'` literal on 693 WLMHW rows, blank elsewhere.
- Variant Stock ledger `LOCATION` = `RETAILBIN` on all rows; `FROM LOCATION` ∈ {WPUN, NRWH, WNAG, WBHI, WAMD, ECAL, WIND, EPAT, CFA}.
