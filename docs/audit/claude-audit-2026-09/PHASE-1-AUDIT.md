# Phase 1 — Claude audit

Auditor: Claude. Date: 15 September 2026. Branch audited: `phase-1/data-truth` at `c0b2cc7` (31 commits ahead of the merge base `2cdc258`; not pushed to origin).
Method: every acceptance item was re-executed on Sagar's PC. Nothing in `PHASE-1-REPORT.md` was accepted as evidence. All figures were recomputed independently from the Excel workbooks with Python, by two separate parsers, before being compared with what the application stored.
All imports were run into disposable `EtpPhase1Test_Claude*` databases, dropped afterwards. The live `EtpReporting` database was never written to: it still reads 490 invoices, 540 sales lines, latest 2026-08-25, 16 migrations, exactly as before the audit. `settings.json` was never modified (SHA-256 unchanged); the desktop session used an explicit `--connection-string`.

---

## 1. Verdict

**PHASE 1 CLOSED** — all eleven acceptance criteria pass against independently recomputed workbook figures, including the two-year corpus at 790 lines / 759 invoices / zero conflicts and the 25 August money to the paisa.

Two things are the owner's to settle before merge, and neither is a defect in Codex's work: **D11** (tick the tender-mode table in section 6) and **task 13**, which Codex deferred to Phase 2 with a defensible reason (section 3). The branch also has to be pushed and reconciled with `main`, which is one commit ahead of it (section 5).

---

## 2. Acceptance A1.1 – A1.11

| ID | Result | What I executed and observed |
|---|---|---|
| **A1.1** One action imports both real folders on an empty database, with per-file results and counts | **PASS** | `--database EtpPhase1Test_ClaudeRaw --rebuild --folder <TITAN ALL REPORT…> --folder <HELIOS ALL REPORT…>` in a single invocation: **45 Imported, 14 empty export, 0 failed, 0 unknown layout**, exit 0. Each file reported its own report code, store, detected period and `rows/new/present/conflicts`. Separately through the **actual desktop UI** for the consolidated folder (A1.11). |
| **A1.2** 25 Aug and August MTD money matches the workbooks | **PASS** | Database: Titan 25 Aug **NETAMOUNT 34,215.00 / NETVALUE 28,995.76 / TAX 5,219.24 / 5 lines / 5 invoices**; Helios **29,290.00 / 24,822.02 / 4,467.98 / 2 / 2**. August MTD: Titan **938,197.00 / 182 invoices**, Helios **774,868.60 / 38 invoices**. An independent Python pass over the same workbooks produced all eight figures identically. The plan's Helios MTD of 774,869 is that value rounded; the true figure is 774,868.60 and the 0.40 traces to the R020 `ROUND OFF` column. |
| **A1.3** Re-importing the same folder writes zero new rows | **PASS** | Second run of the consolidated folder against the same database: **31 Duplicate, 1 Not needed, sum of `new` across every file = 0**. `sales_lines` stayed 790, `sales_invoices` 759, `stock_movements` 3,955, conflicts 0. |
| **A1.4** CRO, tender, banking, CN and customer tables populated for 25 Aug with counts matching the workbooks | **PASS** | On 25 Aug 2026: `etp_r013` **WLMHW 5 / HEMW 2**, `etp_r020` **6 / 3**, `etp_r024` **5 / 2** — identical to the independently parsed workbook row counts for that date. Per D3, `etp_r024` carries a customer name **and** phone on **5 of 5** Titan and **2 of 2** Helios rows. |
| **A1.5** Parser goldens run in CI without SQL Server; SQL tests run locally | **PASS** | `dotnet test tests-dotnet/Etp.Reporting.Import.Tests -c Release` with `ETP_TEST_SQL_CONNECTION` pointed at a non-existent host: **110 passed, 0 failed, 0 skipped**. That project references only `src/Etp.Reporting.Import` and resolves its 32 sanitised fixtures from the build output, so it needs no database. Full solution on SQL Express: **670 passed, 0 failed, 0 skipped** across six projects. |
| **A1.6** Tender modes sum to the GST-inclusive invoice total every day; UPI largest; nothing quarantined | **PASS** | Across **45 store-days** in August 2026, days where the tender total differs from the invoice total: **0**; maximum absolute difference **0.00**. Ineligible/quarantined tender rows anywhere: **0**. Titan August by mode: PAYMENTTYPE25 (AIRPAY, UPI) **600,305.00** — the largest — CASH 222,605.00, CARD 115,293.00, ROUND_OFF −6.00. The raw R020 sheet totals 952,067.00, which is 13,870.00 more than sales; the importer models both credit-note legs (`CREDITNOTE_REDEEM` +13,870.00 against `ISSUED_CREDITNOTE` −13,870.00), so the identity closes exactly at **938,197.00 = 938,197.00**. |
| **A1.7** A reordered workbook adds zero facts and reports Duplicate content | **PASS** | I built my own copy of the Titan R025 workbook with the last three data rows moved to the top and rewritten by openpyxl, so the file bytes differ entirely. Import result: **`Duplicate content`, rows=451, new=0, present=451, conflicts=0**; `sales_lines` stayed 540 and `sales_invoices` 490. Identity is genuinely content-keyed, not row-number-keyed. |
| **A1.8** Multi-day import needs no typed date; days inside the range carry sources | **PASS** | No store or date was entered anywhere. Detected scopes: the raw folders auto-detected **2026-07-01 .. 2026-08-25 (56 days)** per file; the consolidated folder auto-detected **16 Sep 2024 – 07 Sep 2026** and the UI displayed exactly that. Sales evidence lands on **55 distinct business dates for Titan and 43 for Helios** inside the range, matching the workbooks (Helios simply has fewer trading rows in that window). |
| *A1.8a* | **N/A** | There is no A1.8a in the plan. Codex was right to refuse to invent one. |
| **A1.9** Staff report shows negative returns and CRO names; staff total equals store total | **PASS** | August 2026: staff (R013) total **938,197.00** vs store (R025) **938,197.00** for Titan and **774,868.60** vs **774,868.60** for Helios — difference **0.00**, quantities equal too. SR rows are stored negative (Titan −13,870.00 / −4 qty, Helios −12,995.00 / −1). Rows with negative quantity but positive amount, the raw-file quirk: **0**. CRO names present and correct on 25 Aug (VITTHAL SATPAL, Pradnya kamble, Maya kamalakar Hakke, Akash Kadam). |
| **A1.9a** Two-year Helios R025 stores 790 lines / 759 invoices, three fiscal-year identities, monthly totals match, R030 imports | **PASS** | My own fresh import: **sales_lines 790, sales_invoices 759, CONFLICT outcomes 0, stock_movements 3,955**. Invoice `100000068` exists three times — FY-end **2025** (2025-01-01), **2026** (2025-06-21), **2027** (2026-05-26). All **25 months** match `golden-monthly-HEMW-R025.csv` exactly on invoices, quantity, NETAMOUNT, NETVALUE and TAX, totalling 12,850,882.24 / 10,904,673.38 / 1,946,208.86. An independent parse of the workbook reproduced the same 25 months and confirmed the golden CSV is itself correct, so this is a workbook-to-database match, not a CSV-to-database match. |
| **A1.10** Every Phase 1 register row fixed; full corpus plus Titan import produces no new OPEN row | **PASS** | All thirteen rows verified on observed behaviour — table in section 7. My corpus, re-import, raw two-store and reorder runs produced **no new failure**, so no row was appended. |
| **A1.11** All 31 consolidated files import in one action, Info sheets present, no manual scope, no unknown layouts, no blocked files | **PASS** | Run through the **actual desktop application**, not a test harness: Imports → Intake → Import Files → "Import today's folder" → chose `till 6 sep 26` → one action. Final status line: **"31 imported · 0 duplicate/already present · 22,006 new rows · 0 conflicts · 0 failed · 0 unknown layouts."** Detected scope shown as **"HEMW · 16 Sep 2024 – 07 Sep 2026"**. Resulting database: 790 / 759 / 3,955 / 0 conflicts, identical to the command-line run. Screenshots `a111-folder-import-in-progress.png` and `a111-folder-import-complete.png` are **native Windows captures of the maximised window at 1366×768**, including the title bar — the check Codex could not run. |

---

## 3. Tasks 1 – 15

**1. Value columns — Done.** `0017_sales_value_columns.sql` adds `source_tax_amount` beside gross and net. Verified in data: gross = NETAMOUNT, net = NETVALUE, tax = TAX, and NETVALUE = NETAMOUNT − TAX held on **790 of 790** corpus rows at zero tolerance in the independent parse.

**2. Auto-detect store and business date — Done.** The UI showed "Detected: HEMW · 16 Sep 2024 – 07 Sep 2026" with no store picked and no date typed. Both raw store folders imported in one action with per-file store detection.

**3. Folder import — Done.** One action, per-file result list with counts, duplicates skipped, unknown types never fail the batch (0 failed across 32 and 59 file runs).

**4. Every ETP family — Done.** 16 typed profiles (R003, R008, R009, R010, R011, R012, R013, R014, R018, R019, R020, R022, R024, R025, R029, R030) and 16 generic landing tables, exactly the split the plan specifies, defined in `EtpReportFamilies.json` and mirrored in `0018_etp_family_tables.sql`. Empty exports are recorded as "empty export" and succeed: 7 header-only workbooks in the corpus, including R011, which the independent parse confirmed has **no header row at all**.

**5. Manual input definitions — Done.** `0019_phase1_masters.sql` adds the masters; `staff` table populated with 14 rows from the raw import.

**6. Completeness rule — Accepted on Codex's tests.** Partial-period aggregates are covered by tests I ran but did not exercise through a report screen; the reports themselves are Phase 2.

**7. Tender modes — Done.** `tender_modes` master holds 36 mappings including PAYMENTTYPE20 → PHONEPE → UPI and PAYMENTTYPE25 → AIRPAY → UPI. Quarantine is gone: zero ineligible tender rows.

**8. Invoice and line identity — Done.** The financial-year identity is the heart of Phase 1 and it works: 759 invoices where calendar-year keying would have collapsed them to 383, and zero conflicts across 22,006 rows.

**9. One current file per report/store/period — Done for the duplicate path.** Re-import gives 31 Duplicate and zero new rows; a byte-different reordered file gives "Duplicate content". The superset-supersedes path I did not exercise myself and accept on Codex's tests.

**10. Multi-day scope — Done.** 56-day and 721-day ranges detected and stored per file.

**11. R013 signs and stock types — Done.** Signs normalised, CRO names persisted, staff master seeded. The stock ledger imported all 3,955 rows; the independent parse found **nine** distinct movement types (Purchase Receipt 1225, STM Receipt 955, STM Issue 768, INV 754, Purchase Return 206, SR 34, Stock Receipt 6, Stock Issue 5, BC 2), all accepted.

**12. Ambiguous headers — Done.** R001 AdvanceOrder Collection, which shares R022's 46-column header, was recorded as R001 "empty export", not as an empty Revenue Report.

**13. Per-brand location split — NOT DONE, deferred by Codex.** The plan lists it as Phase 1 task 13, and Phase 2 task 2 also specifies the same screen ("physical entry screen keyed by brand with the previous day's counts pre-filled"). Codex deferred it to Phase 2 on the grounds that the brand-rows master it depends on is Phase 2 task 10. That reasoning holds: a per-brand entry screen cannot be built before brands are defined. No acceptance criterion tests it. **Owner decision:** formally move task 13 into Phase 2, or send it back. I recommend moving it.

**14. OCR scaffolding deleted — Done.** Zero case-insensitive hits for "ocr" anywhere under `src/`; `DocumentIntakeService` deleted; `0020_remove_document_extraction.sql` drops the table and the two helper-path settings. Source Inbox keeps attachment and viewer only.

**15. ZIP and temp handling — Accepted on Codex's tests.** Not separately exercised; no ZIP was part of the acceptance runs.

---

## 4. Discrepancies against PHASE-1-REPORT.md

1. **The report's tender table reads as if credit notes vanished.** It shows "1–25 Aug CN 0.00" for both stores while the raw R020 sheet carries 13,870.00 of credit note for Titan. The data is not lost: the importer writes a redemption leg of +13,870.00 and an issued leg of −13,870.00, which net to zero. That is correct, and it is what makes the daily reconciliation close, but the line as printed invites the wrong conclusion. Worth one clarifying sentence before Phase 2 builds the cash book on it.
2. **Cash is reported net of rounding.** The report's 222,599.00 is the raw CASHAMOUNT 222,605.00 less ROUND_OFF 6.00. Both are stored separately and both map to mode Cash. Accurate, but again not obvious from the table.
3. **The screenshot limitation was real and is now resolved.** Codex could not produce native captures and said so. I produced them: the maximised window with its Windows frame, mid-import and complete. Nothing in Codex's WPF-rendered captures contradicts what I saw.
4. **The plan, not Codex, is imprecise about R030.** A1.9a says "3,955 rows, five transfer types"; the workbook actually holds nine distinct types. Codex implemented ten approved types, which covers it. The plan line should be corrected rather than the code.
5. **Branch history was edited before any push.** The report states two screenshot versions containing real staff names were removed from the branch's history and replaced with artificial sample records. Since `phase-1/data-truth` has never been pushed, no force-push occurred and rule 1 is not breached. The 31 commits are linear with no merges and identical author/committer timestamps. Removing customer-identifying material before publication is the right instinct; it is recorded here because it is a history edit.
6. **Everything else in the report reproduced.** 790/759/3,955/0 conflicts, 22,006 new rows, the three fiscal-year identities, all 25 months, the eight A1.2 figures, 670 tests, and the 0-warning builds all matched my own runs.

---

## 5. Defects and observations outside Phase 1 scope

1. **The branch is unpushed and one commit behind `main`.** `main` has `9a028a4` (the handoff file); the branch carries its own `e2aa824 Rename Claude handoff file`. These will collide. Resolve before merge — this is the only mechanical obstacle to closing.
2. **Two source workbooks are structurally deceptive, and both were handled.** Every raw sheet declares `dimension ref="A1"`, which makes any read-only parser report zero rows, and six of the 59 raw workbooks repeat their entire column block horizontally (Titan R025 is 82 columns = 41 twice). The importer read them correctly; anyone writing future tooling against these files needs to know.
3. **The Helios "ALL REPORT" export is far thinner than the consolidated set for the same window** — 89 R025 rows against 790 in `till 6 sep 26`. Not an importer problem, but the owner should know which export is authoritative before Phase 2 reports are built on it.
4. **Titan is missing report families.** No `SOR Sales` workbook at all, and `R029 Transactionwise Bank` is header-only. Titan `R008 Banking Details` has `TransactionDate = 0` on all 80 rows and `BANKEDAMOUNT` zero throughout, so banking reconciliation for Titan has nothing to reconcile against yet.
5. **R013 and R025 disagree on return signs in the source.** Ten Titan and three Helios rows carry negative quantity with positive amount in R013, where R025 books the same documents negative. The importer normalises this, which is why staff totals now equal store totals; the divergence is in ETP's exports, not in the code.
6. **Dead audit vocabulary survives.** `OperationalAuditRepository.cs:14` still lists `DocumentExtraction` and `DocumentExtractionReview` as allowed event types for a feature that no longer exists. Nothing emits them. Cosmetic.
7. **Tracked files are now 682.** Phase 0's A0.1 capped the repository at 600; the 32 sanitised fixtures and the new tests took it past that. No Phase 1 criterion covers file count, and the fixtures are legitimate, but the earlier ceiling no longer holds and the plan should say so.
8. **Codex left two audit databases on the instance** (`EtpPhase1Test_RawAcceptance`, `EtpPhase1Test_UiReview`) plus `EtpReportingHelios`. All are disposable and named per the tool's own guard. I left them alone; they can be dropped at any time.

---

## 6. Owner decisions before merge

**D11 — tick the tender modes.** The evidence from the two-year Helios corpus, computed independently from the Payment Type Report:

| Source column | AGENCYNAME | Non-zero rows | Corpus total | Proposed mode |
|---|---|---:|---:|---|
| PAYMENTTYPE20 | PHONEPE | 381 | 5,702,087 | UPI |
| PAYMENTTYPE25 | AIRPAY | 24 | 300,010 | UPI |
| CASHAMOUNT | *(blank)* | 289 | 4,295,516 | Cash |
| CARDAMOUNT | CC01, CC02, CC05, CC08, CC10 | 124 | 2,453,730 | Card |
| CREDITNOTE | HEMW / WLMHW | 34 | 510,001 | CN |
| GCAMOUNT | GIFT CARD | 4 | 92,400 | TC |
| CHEQUEAMOUNT | *(blank)* | 9 | 7,309 | TC |
| PAYTM | PAYTM | 1 | 3,750 | UPI |
| ROUND OFF | *(blank)* | 66 | −24.05 | Cash |
| REFUND | *(blank)* | 4 | 0.29 | Cash |

Every other column (GVAMOUNT, LOYALTYPOINTS, NO REFUND, TATA GV, GYFTR, HELIOSOMNI, PAYMENTTYPE15/19/21/22/23/24) is zero across the whole two years. The seeded master already maps all of these; what is missing is your tick. Note that a non-blank AGENCYNAME is a reliable key for the electronic tenders, and dropping unnamed `PAYMENTTYPEnn` columns would have lost 6,002,097 of collections.

**Task 13.** Confirm it moves to Phase 2 alongside the brand-rows master, or send it back to Codex now.

---

## 7. Import Failure Register — IF-001 to IF-013

Each row below is marked **VERIFIED** on behaviour I observed myself, not on Codex's tests. The register file has been updated to match.

| ID | Status | What I observed |
|---|---|---|
| IF-001 | VERIFIED | All 31 consolidated files carry an `Info` sheet and all 31 imported; zero validation blocks. |
| IF-002 | VERIFIED | The corpus stores dates as yyyyMMdd integers with R025's `INVDATE` as a true Excel datetime; both parsed, and all 25 monthly buckets landed correctly. |
| IF-003 | VERIFIED | The two `BC` rows import and behave as returns: November 2024 shows 4 return lines = 3 SR + 1 BC, and the ledger carries the 2 BC receipts. |
| IF-004 | VERIFIED | 56-day and 721-day scopes detected per file with no date entered. |
| IF-005 | VERIFIED | Store detected from the file; both raw store folders imported in one action with no store chosen. |
| IF-006 | VERIFIED (duplicate path) | Re-import gives 31 Duplicate and zero new rows; a byte-different reordered file gives "Duplicate content". The superset-supersedes branch rests on Codex's tests. |
| IF-007 | VERIFIED | R011 is genuinely empty — no header row at all — and is recorded as "empty export"; R010 BinWise now has a typed profile and imported 466 rows. |
| IF-008 | VERIFIED | PAYMENTTYPE25 (Airpay) totals 600,305.00 for Titan August and is the largest mode; zero rows are quarantined. |
| IF-009 | VERIFIED | Returns negative, CRO names persisted, staff total equals store total to 0.00, zero rows with negative quantity and positive amount. |
| IF-010 | VERIFIED | 0 unknown layouts and 0 failed files across the 32-file corpus and the 59-file two-store run. |
| IF-011 | VERIFIED | R001, which shares R022's header, was recorded as R001 "empty export". |
| IF-012 | VERIFIED | 790 lines / 759 invoices / 0 conflicts, and invoice 100000068 exists three times across FY-end 2025, 2026 and 2027. |
| IF-013 | VERIFIED | All 3,955 ledger rows imported across nine movement types; none blocked. |

No new row was appended: my four import runs produced no failure.

---

## 8. Cleanup

**Databases.** `EtpPhase1Test_ClaudeCorpus`, `EtpPhase1Test_ClaudeRaw` and `EtpPhase1Test_ClaudeUi` created and dropped. Remaining on the instance: `EtpReporting`, `EtpReportingHelios`, and Codex's `EtpPhase1Test_RawAcceptance` and `EtpPhase1Test_UiReview`, which I did not touch. Every transient `EtpPhase0Test_*`, `EtpPhase1Upgrade_*` and `EtpRecovery_*` database created by the test run was dropped by its own fixture.

**Live database untouched.** Re-checked read-only after all work: **490 invoices, 540 sales lines, latest 2026-08-25, 16 migrations** — identical to the pre-audit state. Phase 1's migrations 0017–0020 have deliberately not been applied to it.

**Settings.** `%LOCALAPPDATA%\EtpReporting\settings.json` was never modified; SHA-256 `5A58FC545219D87D41CA95E120AB5D7473C57495AAF888DE8CC0BB22B2B1E207` before and after. The desktop session ran on an explicit `--connection-string`.

**Files.** The temporary reordered workbook and its folder were deleted. No source workbook was modified. Screenshots added under `phase-1-audit-screenshots/`.

**Service.** `MSSQL$SQLEXPRESS` was never stopped; final state **Running**. My application instance was closed; the one that was already open before the audit was left running.

**Repository.** No source, test or migration file was modified. The only changes are this document, its screenshots, and the register status column.
