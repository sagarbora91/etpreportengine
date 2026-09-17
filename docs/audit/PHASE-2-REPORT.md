# Phase 2 implementation and acceptance evidence

16 September 2026. Branch: `phase-2/evening-reports`.

## Status

The report implementation is ready for owner review. **Phase 2 acceptance is not closed.** D4 mapping approval, Titan historical source files, reconciliation of the reference-photo stock cutoff, and native Excel/Acrobat/touch acceptance remain outstanding. Do not call those missing evidence items a need to rebuild the reports.

This sprint extended the existing application following [the reuse audit](PHASE-2-REUSE-AND-GAP-AUDIT.md) and [the approved plan](ETP-MASTER-AUDIT-AND-PHASED-PLAN.md). It did not replace the import engine, staff attribution, manual-input storage, monthly-target storage, report archive or shell. It did not deploy or change the live database/settings.

The branch starts from `main` (`9a028a4`) and merges the closed Phase 1 branch (`d1e7c48`) in `2b78197`. Original Phase 1 and Phase 4 worktrees are preserved. Phase 4 `df35d90` was inspected, but is not integrated: its migration 0022 grants access to `document_extractions`, removed by Phase 1 migration 0020, and its write boundary does not cover the current Phase 1 enrichment writes. Resolve this compatibility in a separate integration change before staff deployment. No committed migration was edited. Phase 2 adds 0024, leaving 0021–0023 reserved for Phase 4.

## Requirement completion and reuse

Paths are repository-relative; method names identify the implementation.

| Plan item | Implementation and reused code | Evidence / remaining acceptance |
|---|---|---|
| 1. DSR | `Infrastructure.SqlServer/EveningReportRepository.cs` adds per-store and combined matrices to the existing DSR facts; `Reporting/EveningReportDocuments.cs`, `EveningDsrPdf.cs`, Desktop `EveningDsrView.cs` present the same model. Store target, day target, MTD balance and required ADS use existing monthly targets and the approved including-today denominator. Brand rows include an explicit Other / unmapped balance. | Real 25 Aug values and MTD values pass SQL and independent workbook checks. INV-only denominator, nullable comparisons, partial walk-in guard, source-covered zero day and combined missing-store guards are tested. D4 is awaiting owner approval. |
| 2. Brand closing stock | `LoadBrandPhysicalStockAsync` groups existing ETP snapshots by brand, not cluster. Physical requires all four components and sums them; difference = physical − system. `LoadBrandStockEntryAsync` reads existing manual counts, defaults from exactly yesterday, prefers today's saved values, and does not save until requested. Desktop `BrandStockEntryWindow.cs` provides touch-sized numeric fields. | Current-day override, no implicit prefill save, missing component and previous-day behavior tested. Source stock totals and every brand checked independently. Supplied source differs from later reference photos; actual touch acceptance remains. |
| 3. Cash book | `CashBookRepository.cs`, `Reporting/CashBookDocument.cs` reuse tenders and manual inputs. Dr/Cr per store/day, multi-day range, all tender categories, service modes, expenses/deposits, opening carry, reason-recorded overrides, independent counted cash. `LoadCashReconciliationAsync` now uses that same book so packs/exceptions cannot use a second closing formula. | SQL carry/override/count variance/missing-source tests and an independently summed ledger test pass. Credit note issue and redemption are shown separately, even when net is zero. Manual opening/expense/deposit values still require actual shop entry. |
| 4. Service | Existing `LoadServiceSalesAsync` gains WDC and includes it once. FTD/MTD/YTD and LY use existing financial-year periods. Available manual entries sum; missing days remain explicit. No entries produce null, not fabricated zero. DSR combined totals require complete evidence. | Manual fixture WDC 0 + cash 700 + card 1,807 + UPI 577 = 3,084; partial-month and no-source tests pass. This is a labelled input fixture based on the reference, not an imported ETP service total. Actual historical manual entries are not supplied. |
| 5. Customer invoices | Existing invoice query joins active R024 by store, document and date, keeping the original canonical quantities and GST-inclusive value. Names are exposed to report screens/exports; absent names stay blank. | Both stores' 25 Aug names present; invoice quantities/values match the source independently. No new customer importer or customer schema. Private review exports stay in ignored `artifacts/`. |
| 6. CRO | Reuses existing R013 attribution, signed returns, monthly targets, discount/quantity/productivity and source controls. | Real 25 Aug CRO totals match source and canonical store total; prior Phase 1 return/monthly-target tests remain green. No new CRO engine. |
| 7. Exports | One `ReportWorksheetWriter` replaces duplicated XLSX writers. One Unicode, measured-width PDF renderer serves ordinary reports and packs; DSR retains its dedicated single A4 landscape exporter. Summary sheets and automatically summed percentage KPIs are removed. | Typed date/number/negative/percent/missing cells, OpenXML schema, freeze/filter, null totals, Unicode PDF pagination and wide values tested. Rendered DSR/cash previews inspected. Native Excel/Acrobat acceptance remains open. |
| 8. C9–C16 | INV-only DSR counts; existing BC support retained; DSR pack control now compares R025 with independent R022 revenue, instead of comparing two R025 projections; tender variance sign aligned; approved calendar-date LY retained; complete service sum; missing invoices remain null; optional cash adjustment/count no longer block. | Existing regression tests plus new cash/zero-source/partial-evidence tests. Combined target is unavailable unless both stores have a target. |
| 9. Aliases | Removes `stock-group` and five exception aliases across catalogue, visual classification, route aliases and ownership. One Daily Exceptions destination with All / Source / Unmapped / Stock / Staff / Tender / Cash filters. | Navigation registry consistency and accessibility smoke tests pass. Filters affect visible rows; exports retain all exception evidence. |
| 10. Owner masters | `EveningMasterRepository.cs` and `EveningMastersView.cs` add editable store/row label/order/source mappings and monthly store-target UI, using existing target storage. Mapping writes enforce Owner access and atomic uniqueness; reserved calculated-row labels are rejected. | Save/reload, rename, conflicting-code rollback, monthly target and category tests pass. Exact label seeds only; inferred code/cluster assignments await D4 approval. |
| 11. History | Reuses Phase 1 folder/multi-month/financial-year-aware import. No separate historical importer. | Helios historical R025 imported in disposable SQL; 25 Aug 2025 LY **46,797.00**, 1 Apr–25 Aug 2025 LY YTD **21,86,215.10**, verified independently. Titan 2025–26 files are absent; A2.6 remains open. |

All implementation paths above are under `src/Etp.Reporting.*` unless otherwise noted. Entry points are the existing Reports and Daily inputs destinations; Brand rows and Monthly targets are in Owner Settings. Phase 3's shell redesign is not part of this change.

### Why the export change is shared

The old simple and pack PDF writers had fixed-width text/truncation and could replace Unicode with question marks. Patching each independently would preserve inconsistent rendering paths. Their public APIs and report models remain; they now delegate to the existing Unicode renderer with measured columns, wrapping, continuation pages and global pagination. Likewise the three Excel entry points delegate to one typed-cell writer. This removes duplicate implementations rather than rewriting report calculations.

## Verified business figures and conflicts in the plan

| 25 Aug 2026 / August MTD | Titan | Helios |
|---|---:|---:|
| FTD quantity | 5 | 2 |
| FTD value including GST | 34,215.00 | 29,290.00 |
| FTD INV count | 5 | 2 |
| FTD AUPT | 1.00 | 1.00 |
| FTD AVPT | 6,843.00 | 14,645.00 |
| MTD value including GST | 9,38,197.00 | 7,74,868.60 |
| MTD INV-only count | **178** | **37** |
| MTD all documents including SR | 182 | 38 |
| ETP snapshot system quantity | **814** | **502** |
| Later reference-photo quantity | 812 | 501 |

Combined FTD = **63,505.00**, quantity **7**, invoices **7**. Combined MTD value = **17,13,065.60**. R025 calls its GST-inclusive column **NETAMOUNT**; **NETVALUE** is exclusive of GST. The application uses the correct source field.

**A2.1 contains a count contradiction:** its monthly examples include SR, while task 1 explicitly requires INV-only counts. The code follows the explicit denominator rule. This is an acceptance-document correction, not lost invoices. Every return remains in amounts and quantity.

**A2.2 cannot currently match the photos:** both SQL and independent Python reproduce the supplied Closing Stock workbooks brand by brand at 814/502. Do not subtract 2/1 to force 812/501. A matching-cutoff export or an explained bridge is required. No claim is made that the exact cause of that cutoff difference has been proven.

## Validation evidence and limits

- Release and Debug solution builds: zero warnings/errors (final command logs under ignored `artifacts/`).
- Full Release suite: **679 tests**, no skips in this environment; private source-corpus tests ran. See `artifacts/phase2-release-evidence/*.trx` for the final run.
- New behavioral tests: `EveningReportsSqlTests.cs`, `EveningReportExportTests.cs`, `EveningDsrViewTests.cs`. Existing import identity, return sign, financial-year, monthly-target, UI routing, export and SQL suites retained.
- Independent verifier: `python tools/audit/verify_phase2_sources.py SOURCE_ROOT artifacts/phase2-review`. Reads original XLSX with openpyxl, resets misleading `A1` dimensions, handles duplicate header blocks, and compares exported figures to source calculations. Produces aggregate-only `independent-source-check.json`. It never accesses SQL.
- All six report families exported to XLSX/PDF for each store. Source totals and each stock brand/CRO checked; service and physical input values are clearly labelled synthetic fixtures. DSR PDFs remain one landscape A4 page.
- WPF component renders at 1366×768 and 816×480 cover Titan, Helios and Combined. Compact view scrolls. These are rendered-control evidence, **not native application-window or touch-device screenshots**. Phase 3 owns the no-scroll shell redesign.
- Review PDFs were rendered using Poppler. Text extraction and visual inspection check the DSR/cash layouts; automated wide-report and Unicode checks complement this. Excel/Acrobat were not driven natively, so A2.4 user acceptance is not claimed.
- Disposable test databases use the guarded `EtpPhase0Test_<guid>` fixture and are dropped by its teardown. Application settings were not written; no live imports, migrations, reports, manual entries or deployment were performed.

## Acceptance checklist

- [x] Reuse working imports, calculations, targets, manual storage and archives.
- [x] Implement six report paths, report-pack parity and exports.
- [x] Implement owner brand/target editors and per-brand stock entry/prefill.
- [x] Apply approved Gift Card → Gift Card and Cheque/RTGS → Bank mappings; CN means Credit Note.
- [x] Verify the real current-period sources independently and retain signed returns.
- [x] Verify Helios historical LY and LY YTD through the existing importer.
- [ ] Owner approves D4 mapping proposal below; then save approved code/cluster mappings. Exact label seeds are already available.
- [ ] Supply Titan 2025–26 history and verify A2.6.
- [ ] Accept corrected INV-only MTD count wording and resolve stock reference cutoff.
- [ ] Validate native application touch entry and opening the generated XLSX/PDF in Excel/Acrobat.
- [ ] Resolve Phase 4 schema/write-boundary compatibility and validate combined permissions before staff deployment.

## D4 source-brand proposal — owner decision pending

The requested approval is a business mapping decision, required by D4 of the approved plan: “Codex lists every distinct BRAND/BRANDNAME/CLUSTER … and Sagar ticks each.” The editor and calculations are implemented; lack of this tick does not justify rebuilding them.

Proposed assignments: Titan ED → EDGE; RG → RAGA; XY → XYLYS; GAUTO cluster → AUTOMATIC. Helios SEKOG → SEIKO; CTZNG/CTZNL → CITIZEN; CE (source name CERUTI) → CERRUTI. NEBULA remains an editable row without observed sales. All other source combinations remain Other / unmapped until intentionally assigned. The table below records distinct evidence across Titan raw R025 and the two-year Helios R025; stock-only labels follow separately. No inferred mapping is silently installed.

| Store | BRAND | BRANDNAME | CLUSTER | Proposed DSR row | Owner tick |
|---|---|---|---|---|---|
| WLMHW | CL | CLOCKY | WCLAS | Other / unmapped | Pending |
| WLMHW | CL | CLOCKY | WCONT | Other / unmapped | Pending |
| WLMHW | ED | EDGE | GEDGE | EDGE | Pending |
| WLMHW | ED | EDGE | LEDGE | EDGE | Pending |
| WLMHW | FD | Tees | TEPRO | Other / unmapped | Pending |
| WLMHW | FD | Tees | TESAL | Other / unmapped | Pending |
| WLMHW | FT | FASTRACK WATCH | CASHL | Other / unmapped | Pending |
| WLMHW | FT | FASTRACK WATCH | FASHN | Other / unmapped | Pending |
| WLMHW | FT | FASTRACK WATCH | SPORT | Other / unmapped | Pending |
| WLMHW | FT | FASTRACK WATCH | STPRO | Other / unmapped | Pending |
| WLMHW | FV | VYB | FASHN | Other / unmapped | Pending |
| WLMHW | RG | Raga | LRAGA | RAGA | Pending |
| WLMHW | SO | SONATA | ESSEN | Other / unmapped | Pending |
| WLMHW | SO | SONATA | GMDEL | Other / unmapped | Pending |
| WLMHW | SO | SONATA | GMDES | Other / unmapped | Pending |
| WLMHW | SO | SONATA | GTDEL | Other / unmapped | Pending |
| WLMHW | SO | SONATA | GTDES | Other / unmapped | Pending |
| WLMHW | SO | SONATA | LMDEL | Other / unmapped | Pending |
| WLMHW | SO | SONATA | LMDES | Other / unmapped | Pending |
| WLMHW | SO | SONATA | LTDEL | Other / unmapped | Pending |
| WLMHW | SO | SONATA | LTDES | Other / unmapped | Pending |
| WLMHW | SO | SONATA | TRNDZ | Other / unmapped | Pending |
| WLMHW | SP | POZE | FASHN | Other / unmapped | Pending |
| WLMHW | TF | TITAN FRAGRANCES | DCLAS | Other / unmapped | Pending |
| WLMHW | TF | TITAN FRAGRANCES | PBOHE | Other / unmapped | Pending |
| WLMHW | TF | TITAN FRAGRANCES | PCLAS | Other / unmapped | Pending |
| WLMHW | TI | TITAN | GAUTO | AUTOMATIC | Pending |
| WLMHW | TI | TITAN | GCLSQ | Other / unmapped | Pending |
| WLMHW | TI | TITAN | GKARI | Other / unmapped | Pending |
| WLMHW | TI | TITAN | GOCTN | Other / unmapped | Pending |
| WLMHW | TI | TITAN | GPURP | Other / unmapped | Pending |
| WLMHW | TI | TITAN | GREGL | Other / unmapped | Pending |
| WLMHW | TI | TITAN | LKARI | Other / unmapped | Pending |
| WLMHW | TI | TITAN | LPURP | Other / unmapped | Pending |
| WLMHW | TI | TITAN | LWKWR | Other / unmapped | Pending |
| WLMHW | TI | TITAN | PBAND | Other / unmapped | Pending |
| WLMHW | WK | FASTRACK WEARABLES | SMRTW | Other / unmapped | Pending |
| WLMHW | WN | TITAN WEARABLES | GSMRT | Other / unmapped | Pending |
| WLMHW | XY | XYLYS | CLASS | XYLYS | Pending |
| WLMHW | ZP | ZOOP | ZOOP | Other / unmapped | Pending |
| WLMHW | ZP | ZOOP | ZOOPE | Other / unmapped | Pending |
| WLMHW | ZP | ZOOP | ZOOPM | Other / unmapped | Pending |
| HEMW | AI | AIGNER-Intl Brand | AGENT | Other / unmapped | Pending |
| HEMW | AK | ANNE KLEIN | BOXED | Other / unmapped | Pending |
| HEMW | AK | ANNE KLEIN | CERIC | Other / unmapped | Pending |
| HEMW | AK | ANNE KLEIN | CRYML | Other / unmapped | Pending |
| HEMW | AK | ANNE KLEIN | DIADL | Other / unmapped | Pending |
| HEMW | AK | ANNE KLEIN | METLS | Other / unmapped | Pending |
| HEMW | AK | ANNE KLEIN | TREND | Other / unmapped | Pending |
| HEMW | BR | TOMMY HILFIGER | CASUL | Other / unmapped | Pending |
| HEMW | BR | TOMMY HILFIGER | SPORT | Other / unmapped | Pending |
| HEMW | CE | CERUTI | CLASS | CERRUTI | Pending |
| HEMW | CE | CERUTI | DRESS | CERRUTI | Pending |
| HEMW | CE | CERUTI | JEWEL | CERRUTI | Pending |
| HEMW | CE | CERUTI | SPORT | CERRUTI | Pending |
| HEMW | FE | FT Hearables | HEATW | Other / unmapped | Pending |
| HEMW | GC | GIFT CARD | GC | Other / unmapped | Pending |
| HEMW | HA | HELIOS ACCESSORIES | AMZTG | Other / unmapped | Pending |
| HEMW | HA | HELIOS ACCESSORIES | FBWBU | Other / unmapped | Pending |
| HEMW | HL | HELIOS | AREXG | Other / unmapped | Pending |
| HEMW | HL | HELIOS | CGSHG | Other / unmapped | Pending |
| HEMW | HL | HELIOS | CTZNG | CITIZEN | Pending |
| HEMW | HL | HELIOS | CTZNL | CITIZEN | Pending |
| HEMW | HL | HELIOS | CVINU | Other / unmapped | Pending |
| HEMW | HL | HELIOS | FOSLG | Other / unmapped | Pending |
| HEMW | HL | HELIOS | FOSLL | Other / unmapped | Pending |
| HEMW | HL | HELIOS | FOSLP | Other / unmapped | Pending |
| HEMW | HL | HELIOS | FOSLU | Other / unmapped | Pending |
| HEMW | HL | HELIOS | GUSSG | Other / unmapped | Pending |
| HEMW | HL | HELIOS | GUSSL | Other / unmapped | Pending |
| HEMW | HL | HELIOS | JUSCL | Other / unmapped | Pending |
| HEMW | HL | HELIOS | SEKOG | SEIKO | Pending |
| HEMW | HL | HELIOS | VSACG | Other / unmapped | Pending |
| HEMW | KC | KENNETH COLE | AUTMC | Other / unmapped | Pending |
| HEMW | KC | KENNETH COLE | CERIC | Other / unmapped | Pending |
| HEMW | KC | KENNETH COLE | CLASS | Other / unmapped | Pending |
| HEMW | KC | KENNETH COLE | SPORT | Other / unmapped | Pending |
| HEMW | PL | POLICE | EXREL | Other / unmapped | Pending |
| HEMW | PL | POLICE | ROREL | Other / unmapped | Pending |
| HEMW | PL | POLICE | SMART | Other / unmapped | Pending |
| HEMW | PL | POLICE | URREL | Other / unmapped | Pending |

### Closing Stock brand / cluster evidence

These are physical-count source labels, not additional DSR assignments. Physical counts are grouped by BRAND; multiple clusters remain one brand row.

| Store | Source BRAND | Distinct CLUSTER values |
|---|---|---|
| WLMHW | CLOCKY | WCLAS, WCONT, WDECO |
| WLMHW | EDGE | GEDGE, LEDGE |
| WLMHW | FASTRACK WATCH | CASHL, FASHN, MMPRO, SPORT, STPRO |
| WLMHW | FASTRACK WEARABLES | SMRTW |
| WLMHW | FT Hearables | HEATW |
| WLMHW | GIFT WITH PURCHASE | TESTR |
| WLMHW | PACKAGING | NEPKG |
| WLMHW | Raga | LRAGA |
| WLMHW | SONATA | ASTRA, ESSEN, GMDEL, GMDES, GTDEL, GTDES, LMDEL, LMDES, LTDEL, LTDES, PAIRS |
| WLMHW | TITAN | GAUTO, GCLSQ, GKARI, GOCTN, GREGL, LKARI, LPURP, LWKWR, PBAND, WRKWR |
| WLMHW | TITAN FRAGRANCES | PAMAL, PBOHE, PCLAS, PGIFT, SKINN |
| WLMHW | TITAN WEARABLES | GSMRT, LSMRT |
| WLMHW | Tees | TEPRO, TESAL, TOPCL |
| WLMHW | VYB | FASHN |
| WLMHW | XYLYS | CHRON, CLASS, FASHN, SPORT |
| WLMHW | ZOOP | ZOOP, ZOOPE, ZOOPM |
| HEMW | ANNE KLEIN | CERIC, CRYML, METLS, WRING |
| HEMW | CERUTI | CLASS, DRESS, JEWEL, SPORT |
| HEMW | FT Hearables | HEATW |
| HEMW | HELIOS | CGSHG, CTZNG, CTZNL, FOSLG, FOSLL, GUSSG, GUSSL, SEKOG |
| HEMW | HELIOS ACCESSORIES | AMZTG, FBWBU |
| HEMW | KENNETH COLE | AUTMC, CERIC, CLASS, SPORT |
| HEMW | POLICE | EXREL, ROREL, URREL |
| HEMW | TOMMY HILFIGER | CASUL, SPORT |
