# Phase 2 — Claude audit

Auditor: Claude. Date: 16 September 2026. Branch: `phase-2/evening-reports` at `9241e26` (pushed, matches origin).
Method: clean Release build and full test run from an isolated worktree; every figure recomputed independently from the ETP workbooks in Python by a separate agent that never read the application's code, tests or reports; the reports then opened in the running application against a disposable database.
The live `EtpReporting` database was never written to (still 490 invoices, 16 migrations) and `settings.json` was never modified.

---

## 1. Verdict

**PHASE 2 REOPENED** — the six reports are built and every number I could check is exactly right, but two code defects (a dropped report filter and an untouched C10) and one structural gap (the only golden test is skipped on any machine without the private workbooks) have to be closed, and three decisions are the owner's.

This is a reopen on detail, not on substance. The arithmetic is sound and Codex's own report is unusually candid: it states plainly that acceptance is not closed and refuses to fudge the closing-stock difference.

---

## 2. Acceptance A2.1 – A2.6

| ID | Result | Evidence |
|---|---|---|
| **A2.1** DSR for 25 Aug 2026 | **PASS**, with a correction to the plan | Live DSR against a database built from the raw two-store folders. Titan **Units 5, Sales ₹34,215.00, AUPT 1.00, AVPT ₹6,843.00**; Helios **2, ₹29,290.00, AUPT 1.00, AVPT ₹14,645.00**. MTD Titan **₹9,38,197.00**, Helios **₹7,74,868.60**. Combined today **₹63,505.00**, invoices **7**. Every one matches the independent workbook computation to the paisa. Screenshot `p2-dsr-25aug2026.png`. **Correction:** A2.1 asks for MTD invoice counts 182 and 38; the app shows **178 and 37**. The app is right. Plan task 1 says "INVOICE count = INV documents only (SR excluded from the denominator)", and the workbooks give 178/37 INV-only against 182/38 all-document. I reproduced both counts independently. **A2.1's numbers must be corrected, not the code.** |
| **A2.2** Closing stock by brand | **FAIL, and not Codex's fault** | The application reports the imported snapshot unmodified. The workbooks give Titan **814** and Helios **502**; the owner's photographed sheet gives **812** and **501**. The independent pass reconciled the gap exactly: Titan EDGE −1 and cluster WRKWR −1, Helios SEIKO −1 — three single-unit manual adjustments that exist only on the owner's paper. Codex refused to subtract them, which is the correct call. **A2.2 cannot be met from this source data and needs an owner decision, not a code change.** A second, larger problem: the owner's sheet is **not** grouped by brand. It is a hybrid, 15 brand-name rows plus 10 cluster rows for Titan (25 rows against the app's 16) and 6 plus 7 for Helios (13 against 8). The plan's task 2 says "grouped by BRAND (not cluster)", which cannot reproduce the owner's layout. |
| **A2.3** Cash book for 25 Aug | **PASS on the tender figures** | Independent computation: Titan Cash 4,395.00 + UPI (PAYMENTTYPE25, AIRPAY) 29,820.00 = 34,215.00; Helios Cash 27,995.00 + UPI (PAYMENTTYPE20, PHONEPE) 1,295.00 = 29,290.00. Both equal the R025 GST-inclusive invoice total exactly, and the Daywise Collection file agrees with the Payment Type Report to the rupee. The cash-book document reproduces the owner's Dr/Cr structure. The owner's own sheet shows UPI 65,356, which reconciles to the export gap described below, not to an error. |
| **A2.4** Exports open cleanly | **PARTIAL** | Verified in code and by test: the fixed-width PDF writer is gone and replaced by measured column widths with band-splitting for wide reports; Excel writes real number cells with the plan's exact Indian grouping format string, dates as dates, freeze panes and autofilter; the Executive Summary KPI sheet is removed and percentages are no longer multiplied. **Not verified:** I did not open the output in Excel and Acrobat natively, and the PDF font is loaded from `%WINDIR%\Fonts` rather than shipped with the app, so the rupee glyph depends on Segoe UI being present on the target machine. Audit finding D14 is unfixed. |
| **A2.5** Golden test per report, tests green | **PARTIAL** | Clean Release build: **0 warnings, 0 errors**. Full suite in my own run: **680 passed, 0 failed, 0 skipped**. All six reports are covered, but by **one** test method, and that method is gated on the private workbook corpus. On CI or any clean clone it skips, so **zero reports have a golden test there**. |
| **A2.6** LY and GROWTH% after historical import | **FAIL — blocked on data** | The path works: importing the two-year Helios workbook populates LY and LY YTD, asserted with real values. Titan's 2025-26 export has not been supplied, and the test asserts Titan LY stays null. A2.6 cannot close until Sagar provides the Titan history (D8). |

---

## 3. Tasks 1 – 11

All six reports exist, are wired to real repository queries and are reachable from navigation. The DSR carries every field the plan lists, including STORE TGT, DAY TGT, MTD BLA, REQ ADS, the FTD/LY/GROWTH%/MTD/YTD/LY YTD matrix, brand rows, walk-ins, conversion, the WCC and WDC rows and a combined block.

**Done:** 1 (DSR), 2 (closing stock, subject to A2.2 above), 3 (cash book), 4 (service), 5 (customer-wise), 6 (CRO-wise), 9 (alias removal — all six alias catalogue entries gone, replaced by filter chips), 10 (brand rows master, with an Owner-gated Settings screen, server-side authorisation and migration 0024), 11 (historical import path).

**Task 7 exports — Partial**, as A2.4.

**Task 8 (C9–C16) — six fixed, one partial, one untouched.** C9 invoice denominator, C12 tender variance sign, C14 service SumIfAny, C15 `TyInvoices ?? 0`, C16 cash blocking are all genuinely fixed; C13 correctly left alone per D2. **C11 is partial** and **C10 was not addressed at all** — the Sales Summary still blocks on an unmapped transaction type while the DSR silently filters. C10 is listed under task 8 in the plan.

**Migrations: clean.** One added (`0024_evening_report_masters.sql`), none modified. Rule 9 respected. It correctly reserves 0021–0023 for Phase 4.

---

## 4. Defects returned to Codex

**P2-1 — The report filter was dropped from three reports.** `ApplyReportFilter()` was present in `RunDsrAsync`, `RunInvoiceSummaryAsync` and `RunCashReconciliationAsync` before Phase 2 and is gone in all three. The search and variance filter will not apply on first run for the DSR, customer-wise invoices or the cash book. *Fixed looks like:* restore the call in all three, with a test that runs a filtered report and asserts the row count.

**P2-2 — C10 is untouched.** The plan assigns it to task 8. Either fix the Sales Summary/DSR inconsistency or get it formally struck from task 8 with a reason. *Fixed looks like:* one behaviour for an unmapped transaction type across both paths, and a test that proves it.

**P2-3 — The only golden test skips without the private corpus.** One method covers all six reports and is gated on a folder that exists on one machine. *Fixed looks like:* sanitised fixtures under `tests-dotnet/fixtures/` carrying the same shapes, so each of the six reports has a golden that runs in CI, with the private-corpus test kept as an additional check.

**P2-4 — Two navigation labels were not renamed.** The catalogue says "Customer-wise Invoices" and "Cash Book" but the buttons still read "Invoice Summary" and "Physical Stock". Staff looking for the plan's report names will not find them.

**P2-5 — Commit granularity.** The entire phase is one commit: 56 files, +1629/−808, covering six reports, an export rewrite, eight correctness fixes, alias removal, a new Settings screen and a migration. Plan rule 1 asks for small commits. This cannot be bisected or reverted selectively. Not worth rewriting history now, but it should not repeat in Phase 5.

---

## 5. Owner decisions blocking closure

**D4 — tick the brand mapping.** Until it is ticked the seed maps each row label only to itself, so every brand row reads 0 and all value lands in "Other / unmapped". I confirmed this live: Helios SEIKO, CITIZEN and CERRUTI all show 0.00 while ₹29,290.00 sits in Other/unmapped. Two facts you need before ticking:

- **Three of the eight requested rows do not exist as brand names in the data.** AUTOMATIC, SEIKO and CITIZEN exist only at cluster level (GAUTO, SEKOG, CTZNG). The mapping table does accept cluster codes, so this works — but only if you tick the cluster codes, not the brand names.
- **NEBULA does not exist anywhere** in either store's sales or stock, except a packaging line. And the data spells it **CERUTI**, one R, not CERRUTI.

**A2.2 — decide what "System" means.** The source says 814/502, your sheet says 812/501, and the difference is three single-unit manual corrections. Either accept the source figure, or the report needs a place to record your adjustment with a reason.

**A2.6 — supply the Titan 2025-26 export**, or A2.6 stays open.

---

## 6. Observations outside Phase 2 scope

1. **The 25 August export is a 20:50 cut, not end of day.** Your own DSR shows Titan 8 units / ₹69,880 where the export holds 5 / ₹34,215. The gap is exactly one invoice in each store (Titan 3 units / ₹35,665, Helios 1 / ₹46,800), and it reconciles across FTD, MTD and the cash book's UPI line. The Phase 2 figures are right for the file and are not your day's figures. This is the standing procedure point: export after the last bill.
2. **A merge blocker with Phase 4**, detailed in the Phase 4 audit: Phase 4's migration grants on a table Phase 1 drops. Codex identified this itself.
3. **The duplicated-column trap flips by file.** Titan's R025 repeats its 41 columns twice; Helios's does not. But Helios's closing stock and daywise collection repeat and Titan's do not. Any future tooling must detect this per file, never per store.
4. **UPI has no fixed column.** Titan uses PAYMENTTYPE25 (Airpay), Helios PAYMENTTYPE20 (PhonePe). Mapping must key on AGENCYNAME.

---

## 7. Cleanup

Audit database `EtpPhase1Test_ClaudeP3` created and dropped. Live `EtpReporting` re-checked read-only afterwards: **490 invoices, 16 migrations**, unchanged. `settings.json` unchanged (SHA-256 `5A58FC54…`). The application instance I launched was closed. No source, test or migration file was modified. Codex's own `EtpPhase1Test_RawAcceptance` and `EtpPhase1Test_UiReview` were left alone.

---

## 8. Addendum — verification completed 16 September 2026

The first pass left two Phase 2 items unexecuted. Both are now done.

**A2.4 upgraded from PARTIAL to PASS.** I exported the DSR for 25 August from the running application to both formats and inspected the files.

The workbook is correct on every point the criterion asks for: a single sheet named "Daily Sales Report" with **no Executive Summary**, **78 real numeric cells** carrying number styles, the plan's exact Indian grouping format `[>=10000000]##\,##\,##\,##0.00;[>=100000]##\,##\,##0.00;##,##0.00` together with its integer variant, `dd mmm yyyy` as a date format, percentages formatted as `0.00"%"` so they are shown once rather than multiplied, a frozen pane at row 8 and an autofilter over `A8:I60`. **Zero `?` characters** in any cell, and the em dash renders.

The PDF is a two-page A4 document embedding **two Segoe UI subsets**, one regular and one bold, so the em dash and rupee glyphs have real font data behind them rather than falling back to a question mark.

The one caveat from the first pass stands: the font is read from `%WINDIR%\Fonts` at export time rather than shipped with the application, so a machine without Segoe UI throws. That is audit finding D14 and it is still open.

I inspected the file contents rather than opening them in Excel and Acrobat. For an OOXML package that is the stronger check, but if you want the literal "opens in Excel with no complaint" evidence, that remains a two-minute manual step.

**The CRO-wise report verified live.** Opened for Titan, 1 to 25 August. The status line reads **"Passed: recorded 938,197.00, attributed 938,197.00, variance 0.00. R013 attributed sales reconcile to canonical R025 sales."** Four of the six staff rows match your photographed sheet to the rupee. The two that differ, by 2,075 and 33,590, sum to exactly 35,665 — the invoice missing from the 20:50 export. The report is right for the file.

**Two new observations.**

*P2-6 — the cash book cannot open on the default scope.* It requires a single store, but the header opens on "Both stores" and the screen shows "Report not ready — Select a store: Choose Titan World or Helios in the header." A staff member reaching Today → Cash gets an empty screen and has to know to change the header first. The message says what to do, but the default state of the shop's most-used evening screen should not be an error.

*P2-7 — per-CRO ATV and UPT are defined differently from your sheet.* The application computes them per CRO from quantity and transaction count. Your sheet shows UPT 1.00 for nearly every CRO where the app shows 0.95 to 1.37, and your total UPT of 1.11 uses a different denominator again. This is the same class of question as the invoice-count rule in A2.1 and needs the same treatment: pick one definition and write it into the plan.

---

## 9. Re-audit of the integration fixes — 16 September 2026

Branch `integration/phase-2-3-4-fixes`, HEAD `123e1d4`, working tree clean. Merge `8db3958` has parents `1c8eb1b` (phase-3/touch-shell) and `d730839` (phase-4/security-operations) — the two tips I audited, so nothing was rebased under me. This document was **restored from `origin/phase-2/evening-reports`**: the integration merge took phase-3 and phase-4 only, so the Phase 2 audit record was missing from the branch entirely. That is a bookkeeping loss, not a code defect, but it is why this file reappears in this commit.

### Verdict

**PHASE 2 CLOSED on coding.** All six returned defects are fixed and I verified each one myself — four by reading the code and running the tests, two by driving the running application. What remains on Phase 2 is **not code**: two owner decisions and one missing data file.

### Returned defects

| Item | Status | Evidence |
|---|---|---|
| P2-1 report filter dropped from three reports | **PASS** | `ApplyReportFilter()` is called again in `RunInvoiceSummaryAsync` (`ReportsWorkspaceView.xaml.cs:252`), `RunDsrAsync` (`:275`, `:292`) and `RunCashReconciliationAsync` (`:321`), and on every other report path. `EveningReportInteractionTests` 11/11 passed inside my own full-suite run. |
| P2-2 / C10 divergent unknown-type handling | **PASS** | `SalesTransactionConsistencyTests` inserts INV, SR, BC, an unknown type and a NULL type, then runs both SQL paths. Sales Summary returns `Passed` instead of blocking; Sales Summary and DSR both return signed net **59** (236 − 118 − 59) and quantity **0** (2 − 1 − 1); DSR invoices = 1; the message says "skipped 2 rows". I checked the arithmetic against the inserted values rather than trusting the assertion. |
| P2-3 only golden test skipped without private corpus | **PASS** | `SanitisedEveningReportGoldenTests` has **seven unconditional `[Fact]` methods** — one per evening report plus the financial-year invoice-identity regression — with no `[PrivatePhaseOneCorpus]` gate. They executed in my clean-clone full run. `tests-dotnet/fixtures/evening-reports/golden.json` is synthetic. |
| P2-4 stale navigation labels | **PASS** | Verified on screen, not just in code: the live report catalogue shows **Customer-wise Invoices** and **Cash Book**. "Invoice Summary" and "Physical Stock" survive only in `ReportTaskAliases.cs:11,26` as search aliases, which is the right place for an old name. |
| P2-6 cash book opens on an error under the default scope | **PASS** | Driven live. Opened Reports → Cash Book with the header on "Both stores"; the header auto-selected **Titan World** and the report ran. No "Report not ready — Select a store" screen. |
| P2-7 per-CRO ATV and UPT definitions | **PASS** | `OperationalReportRepository.cs:320-327`. Denominator is `COUNT(DISTINCT CASE WHEN UPPER(source_transaction_type)='INV' THEN CONCAT(invoice_year,'|',document_number) END)` — distinct INV documents keyed by financial year, SR and BC excluded. Numerators are `SUM(source_gross_value)` and `SUM(source_quantity)` over `IN('INV','SR','BC')`, so returns stay signed and negative. A CRO with no INV invoice yields denominator 0, and `ManagementMetricEngine.Divide` (`:56-57`) returns `NotApplicable` with a **null** value rather than a fabricated zero. That is exactly the definition Sagar confirmed. |

### Acceptance

| ID | Status | Basis |
|---|---|---|
| A2.1 | **PASS**, with a plan correction | Source figures and the INV-only denominator are retained; the sanitised golden covers signed amounts, store and combined totals, targets and history. The plan's month-to-date text of 182/38 is wrong and I am correcting it to **178/37**; Codex's implementation already follows task 1 and is right. |
| A2.2 | **BLOCKED — owner decision** | Source stock stays 814/502. No silent subtraction to 812/501. Needs Sagar to accept the source figure or approve a recorded adjustment with a reason. |
| A2.3 | **PASS** | Cash carry-forward, per-mode signed tender totals and Dr/Cr totals have an always-on golden; default cash navigation is usable (P2-6). |
| A2.4 | **PASS (carried, not re-executed)** | I upgraded this to PASS in section 8 by inspecting the exported workbook and PDF. The export code paths are unchanged on this branch, so the verdict carries; I did not re-export in this pass and do not claim to have. Finding D14 (Segoe UI read from `%WINDIR%\Fonts` at export time) is still open. |
| A2.5 | **PASS** | Six independent sanitised goldens now run on any clean clone; the private-corpus check remains as extra evidence. |
| A2.6 | **BLOCKED — missing data** | Titan 2025–26 history is still absent. Synthetic last-year data proves behaviour but cannot substitute for the owner's file. |

### Two things I found that were not on the list

**The D4 symptom is now visible on screen, and one seeded label can never match.** `0024_evening_report_masters.sql:23-25` seeds eight brand rows and then self-maps them with `INSERT dbo.brand_row_codes ... SELECT store_code, row_label, brand_row_id FROM dbo.brand_rows` — an exact-label mapping. Its own comment is honest about this: "Exact labels only. Ambiguous source codes remain visible under Other / unmapped until the Owner approves their assignment in Settings." The consequence, confirmed on the running DSR against a synthetic import: Helios shows **CERRUTI 0.00** and **Other / unmapped 118.00** — all of the value in the unmapped row.

Five of the eight seeded rows can never match the source as spelled:

- **CERRUTI** — the data spells it **CERUTI**, one R. Ticking the label as written would still produce zero.
- **NEBULA** — appears only on a packaging line, not as a brand.
- **AUTOMATIC, SEIKO, CITIZEN** — exist only at cluster level (**GAUTO, SEKOG, CTZNG**), not as brand names.

Because `0024` is committed and immutable, correcting these means either an Owner edit through the Settings brand editor or a new migration. This is Sagar's D4 decision, and it now has a precise shape: three rows need cluster codes, one needs a corrected spelling, one needs deleting or re-sourcing.

**The same metric carries two names across two screens.** The DSR labels it **AVPT** (`EveningReportRepository.cs:70`, `DailySalesReportDocument.cs:95`); the CRO report and the report pack label it **ATV** (`ReportsWorkspaceView.xaml.cs:289`, `DailyReportPackService.cs:213`, `TablePresentation.cs:53`). Sagar's confirmed wording was ATV. AVPT may be deliberate because it mirrors a row label on the owner's own DSR sheet — `EveningMasterRepository.cs:33` reserves both AUPT and AVPT as sheet labels, which suggests it is. I am not calling this a defect; it is a one-line question for Sagar: should the DSR row keep the sheet's AVPT, or move to ATV for one vocabulary under task 9?

### Import Failure Register

All thirteen rows remain `VERIFIED (Claude Phase 1 audit)`. The integration work produced no new import failure: my own 32-file fixture import through the restricted Store Manager path completed with every file `Imported` or `empty export`. No new row is warranted.

---

## 10. Owner decisions closed — 17 September 2026

The three items that were blocking Phase 2 are now decided. None of them needs application code: the DSR brand query already matches a row against brand code, brand name **or** cluster (`EveningReportRepository.cs:43`), so D4 is data, not development.

### A2.2 Closing stock — **CLOSED. Source figure accepted.**

Sagar accepts the source export figure of **Titan 814 / Helios 502**. His three single-unit paper corrections (Titan EDGE −1, Titan cluster WRKWR −1, Helios SEIKO −1) stay off the system: they are not subtracted, and no adjustment feature is built for them now. Reports reconcile to the ETP export, and the owner's sheet will differ by three units until an adjustment-with-reason feature is proposed separately. **A2.2 passes.**

### A2.6 Titan 2025–26 history — **DEFERRED TO PHASE 5, not waived.**

Sagar will have the real Titan and Service exports by the time Phase 5 runs, so the criterion is not retired — it moves. Phase 2 does not hold open for it. Record against Phase 5: import the real Titan 2025–26 history and verify last-year comparison figures against the workbook. Until then the last-year logic is evidenced by the synthetic fixture and by Helios only.

### D4 Brand rows — **CLOSED with a corrected mapping.**

Two corrections to my own earlier reporting, found by querying the real 1 Jul–25 Aug exports rather than reading the seed:

- **XYLYS does exist** in the Titan data (₹10,005, one line). My earlier statement that it was absent was wrong.
- **CERUTI is a brand name**, not a cluster-only value (₹98,549). Only the spelling was wrong — the source has one R, the seed has two.
- `AUTOMATIC` is still not a Titan brand. `AUTMC` is a **Helios** cluster belonging to Kenneth Cole, and `GAUTO` does not appear in this period at all. My earlier "AUTOMATIC → GAUTO" was wrong.

**Why the seeded rows read zero.** `0024_evening_report_masters.sql:25` seeds `brand_row_codes` as `source_brand = row_label`, so the row labelled SEIKO looks for a brand literally named "SEIKO". Helios has no such brand: it sells Seiko under brand name `HELIOS` with cluster `SEKOG`. The row label and the matching value are different things, and the query already supports matching either.

**Approved mapping — Titan World (WLMHW), by brand name:**

| Row label | Source values mapped | Value 1 Jul–25 Aug |
|---|---|---:|
| TITAN | `TITAN` | ₹10,17,619 |
| Raga | `Raga` | ₹4,35,267 |
| EDGE | `EDGE` | ₹2,71,586 |
| SONATA | `SONATA` | ₹1,31,059 |
| **Fastrack** | `FASTRACK WATCH`, `FASTRACK WEARABLES` | ₹1,41,445 |
| XYLYS | `XYLYS` | ₹10,005 |

`NEBULA` is **removed** — it appears only on a packaging line and is not a brand. `AUTOMATIC` is removed. Fastrack watches and wearables are combined into one row at Sagar's instruction (17 Sep 2026). Remaining small brands (TITAN FRAGRANCES, VYB, ZOOP, TITAN WEARABLES, CLOCKY, Tees, POZE) stay under Other / unmapped by choice, not by defect.

**Approved mapping — Helios (HEMW), by cluster where the house brand carries the watch:**

| Row label | Source values mapped | Match type | Value 1 Jul–25 Aug |
|---|---|---|---:|
| SEIKO | `SEKOG` | cluster | ₹5,63,500 |
| FOSSIL | `FOSLG`, `FOSLL` | cluster | ₹4,45,746 |
| TOMMY HILFIGER | `TOMMY HILFIGER` | brand name | ₹1,81,471 |
| CERUTI | `CERUTI` | brand name | ₹98,549 |
| KENNETH COLE | `KENNETH COLE` | brand name | ₹96,788 |
| CITIZEN | `CTZNG` | cluster | ₹86,700 |
| POLICE | `POLICE` | brand name | ₹62,997 |
| ANNE KLEIN | `ANNE KLEIN` | brand name | ₹53,988 |

**Implementation note that must not be missed: do not create a `HELIOS` row.** The query orders matches brand code → brand name → cluster (`:44`). A row mapped to the brand name `HELIOS` would capture every Seiko, Fossil, Citizen, G-Shock and Guess line by name before their cluster rules could fire, and silently empty four of the eight rows. `HELIOS` must stay unmapped so those lines fall through to cluster matching. The same ordering is what makes Tommy Hilfiger, Ceruti and Kenneth Cole safe despite all three carrying a `SPORT` cluster — they match by name first.

**How it lands.** `0024` is committed and immutable, and it seeded eight wrong rows. Correcting them needs either a new migration that replaces the contents of `brand_rows` and `brand_row_codes` for both stores, or an Owner edit through Settings → Brands and targets after deployment. A migration is preferable: reproducible, and it fixes a fresh install as well as this one. `brand_row_codes` has primary key `(store_code, source_brand)`, so mapping two source values to one row — Fastrack and Fossil — is supported without change.

**Acceptance for the fix:** with the mapping applied, run the DSR for 1 Jul–25 Aug on a disposable database loaded from the real exports; each row above shows the value in its table, the sum of the mapped rows plus Other / unmapped equals the store total, and no mapped row reads 0.00.

### Revised Phase 2 position

| ID | Status |
|---|---|
| A2.1 | PASS |
| A2.2 | **PASS** — source figure accepted 17 Sep |
| A2.3 | PASS |
| A2.4 | PASS (carried) |
| A2.5 | PASS |
| A2.6 | **DEFERRED to Phase 5** with the owner's agreement |
| D4 | **DECIDED** — mapping above, implementation outstanding |

Phase 2's acceptance criteria are now all either passed or deliberately moved. What remains before Phase 2 can be called closed is **code, not decisions**: the D4 mapping migration, plus R1 (durable import history) and R2 (advanced report filters) carried in from the feature-retention audit.

---

## 11. Closure candidate re-audit — 17 September 2026

Branch `phase-2-3/closure`, HEAD **`dfd9f8c`**, clean tree, correctly based on `0a0c38f` (`git merge-base` confirms). Ten commits, 2,007 insertions. `dfd9f8c` is documents only on top of `8055d99`, which matches Codex's own statement of the verified application commit.

**Migrations are additive.** `git diff --name-status 0a0c38f..HEAD -- database/migrations/` shows only `A 0027_…` and `A 0028_…`; a filter for `0001`–`0026` returns nothing. No committed migration was edited.

**Build and test reproduced independently**, with `-m:1 -nodeReuse:false` as instructed:

```
dotnet build -c Debug   -> 0 Warning(s), 0 Error(s)
dotnet build -c Release -> 0 Warning(s), 0 Error(s)
dotnet test  -c Release --no-build -> exit 0

Desktop 355 | Domain 12 | Import 110 | Reporting 63 | SqlServer 238 | Integration 79
Total: 857 passed, 0 failed, 3 skipped
```

**857 / 0 / 3 — exactly Codex's figure.**

### Job 1 — approved D4 mapping — **PASS**

`0027_approved_evening_brand_rows.sql` matches the approved mapping line for line: 14 rows, 16 code mappings (Fastrack and FOSSIL each take two source values), CERUTI with one R, NEBULA and AUTOMATIC removed. The `DELETE` is scoped to `WLMHW`/`HEMW` only, so any other store's configuration survives — and a test asserts exactly that with a retained "Keep me" row.

**There is no `HELIOS` mapping**, and the migration carries a comment explaining why: the query matches code → name → cluster, so a `HELIOS` row would consume Seiko, Fossil and Citizen before their cluster rules fire. That was the single trap in this job and it is correctly avoided and documented.

Verified independently against the **real** 1 July–25 August exports in a disposable database at migration 28, running the production brand-matching query rather than Codex's fixture:

| Store | Mapped | Other / unmapped | Store total | Rows non-zero |
|---|---:|---:|---:|---:|
| WLMHW | 2,006,980.25 | 136,473.50 | **2,143,453.75** | 6 |
| HEMW | 1,589,739.10 | 46,775.00 | **1,636,514.10** | 8 |

All **14 rows non-zero**, mapped + Other reconciles to the store total exactly, and `HELIOS` confirmed unmapped by direct query. The two store totals also match my own earlier independent measurement (1,205,256.75 + 938,197.00 = 2,143,453.75).

Row values: TITAN 1,017,619.00 · Raga 435,267.25 · EDGE 271,585.50 · Fastrack 141,445.00 · SONATA 131,058.50 · XYLYS 10,005.00 · SEIKO 563,500.00 · FOSSIL 445,746.00 · TOMMY HILFIGER 181,471.00 · CERUTI 98,549.00 · KENNETH COLE 96,788.10 · CITIZEN 86,700.00 · POLICE 62,997.00 · ANNE KLEIN 53,988.00.

Fastrack = 98,960 + 42,485 and FOSSIL = 245,975 + 199,771, so the two-source combinations resolve correctly.

**A correction to my own earlier reporting.** Codex is right that §10 of this audit rounded four amounts. My figures came from `CONVERT(decimal(12,0), …)`, which rounds; the true values carry paise — EDGE is 271,585.50, not 271,586. The approved mapping itself was unaffected, but the figures I published were imprecise and Codex was correct to say so.

Tests also assert the migration changes no facts (`SUM(source_gross_amount)` identical before and after), leaves earlier migration receipts untouched, and is a no-op on re-run.

### Job 2 — durable import history — **PASS**

The defining requirement is survival of an application restart. `ImportWorkspaceView.xaml.cs:20`'s in-memory `latestResults` is no longer the source: `SqlServerImportHistoryQuery.LoadAsync` reads `import_files` + `import_batches` + aggregated `import_row_outcomes`, unioned with `import_attempts`.

**The restart evidence is genuine.** `tests-dotnet/Etp.Reporting.HistoryRestartHost` is a separate executable that builds the real `DesktopCompositionRoot`, shows the real `MainWindow`, navigates the rail to Import → History, waits for the real `ImportHistoryView` to finish loading and serialises its entries. `DurableImportHistoryTests` launches it as **four separate OS processes** — import, exit, read, exit, re-import, exit, read — asserting a non-zero exit each time. That is a real restart, not an in-process reset. The host also refuses any database outside the generated `EtpPhase0Test_` namespace.

**Source-row counts are accurate.** The `OUTER APPLY` de-duplicates outcomes by `(sheet_name, source_row_number, business_identity)` and takes `MAX` per group with precedence conflict > new > present, so a source row producing several facts counts once. R022's multiple projections cannot inflate the display.

**Diagnostics are safe.** `SafeIssue` replaces every message with fixed guidance and allow-lists `SourceColumn` against `ApprovedImportProfileRegistry` headers — anything unrecognised becomes null. Applied on both write and read. No source values, paths, SQL or exception text can reach the view.

**Migration 0028 and its reasoning — accepted.** The stated reason is correct and I verified it: an exact duplicate creates no new `import_files` row, so a query over the canonical tables alone can never show duplicates or pre-persistence failures. `import_attempts` is purely additive — it does not touch fact identity, day locks or any committed migration. Its security follows the 0025 model: `GRANT SELECT` to all three roles, `DENY INSERT/UPDATE/DELETE` to all three, writes only through `record_import_attempt`, which requires Manager or Owner, `DENY EXECUTE` to Viewer, rejects file names containing `\`, `/` or `:`, validates `ISJSON`, and constrains `outcome` to a fixed vocabulary including `Failed` and `Cancelled`. The index `(store_code, period_start, period_end, recorded_utc)` supports the date/store filtering.

**Failed and cancelled attempts are retained**: `FolderImportService` records `Cancelled` results and calls `RecordAttemptAsync` for every result, not only successes.

**Concurrent duplicates — a real bug was found and fixed.** Codex's own review reproduced a misclassification in which the second of two concurrent identical imports reused the first's NEW outcomes. The fix (`SqlServerImportPersistenceUseCase.cs:215-228`) reads back the committed file's `import_batch_id` and compares it with its own attempt's batch; a different batch means another attempt won, so this one is reported `Duplicate` with zero new rows. It uses an immutable identity and changes no lock and no financial value. `ConcurrentImportAttemptTests` holds the real `sp_getapplock` resource until **both** requests are observed waiting in `sys.dm_exec_requests` — a genuine barrier rather than a sleep — then asserts one file, one batch, no extra facts and two durable attempts, across R025, R022, R020 and R013.

**Roles:** history is registered at minimum role 1, so Viewer can read it; `LoadAsync` requires `CanView` and `RecordAttemptAsync` requires `CanImport`.

### Job 3 — report-header filters — **PASS**

The four inputs are reachable again and bound into the query, not the detail view. `ReportScope()` (`ReportsWorkspaceView.xaml.cs:189-193`) carries store, brand segment, transaction type and item into `ApplicationReportScope`, and the repositories parameterise them (`@stores`, `@segments`, `@items`). `AttachQueryFilters` reparents the panel on every activation, which fixes the prior behaviour where visibility depended on navigation history. Disabled inputs are cleared, so a filter cannot silently apply to a report that does not support it.

`ReportFilterClosureTests` proves the whole chain on real SQL: unfiltered total **767**, brand-segment filtered **531**; the screen, the row total and the status line all read 531; the **Excel** export contains the scope-line cell, a final total of 531 and no `BRAND-B`; the **PDF** contains "Applied scope:", "Brand segments: SEG-X", "531.00" and no `BRAND-B`; further combinations give 59, 118 and 0; **Clear restores 767**. Facts are unchanged (`SUM(source_gross_amount)` equal to the unfiltered total) and a Viewer write is denied with SQL error 229.

**Detail search remains separate**: the test sets the detail filter to a non-matching string and asserts the report total is still 531. Exports are also disabled until the report is re-run after a filter change, which prevents exporting a total under a scope it does not belong to.

### Acceptance position

| ID | Status | Basis |
|---|---|---|
| A2.1 | **PASS** | Source figures and INV-only denominator retained; goldens run everywhere |
| A2.2 | **PASS** | Source stock 814/502 accepted by Sagar 17 Sep; no paper adjustment made |
| A2.3 | **PASS** | Cash carry-forward and signed tender totals covered by always-on goldens |
| A2.4 | **PASS (carried)** | Export paths verified earlier; Excel/PDF now additionally assert the scope line |
| A2.5 | **PASS** | Six sanitised goldens plus the private-corpus check |
| A2.6 | **DEFERRED to Phase 5** | Owner decision; real Titan and Service exports will exist by then |
| D4 | **PASS** | Migration 0027, verified above against real data |

**Phase 2 has no outstanding coding work and no outstanding owner decision.** Every criterion passes or was deliberately deferred by Sagar.
