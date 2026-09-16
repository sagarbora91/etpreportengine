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
