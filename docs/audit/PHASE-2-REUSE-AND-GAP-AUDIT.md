# Phase 2 reuse and gap audit

Date: 15 September 2026. Scope: audit only, before Phase 2 coding.

## 1. Conclusion

**Phase 2 is an extension of a working application, not a six-report rebuild.** All six areas have reusable data or report infrastructure. Five have existing report destinations (DSR, physical stock, cash reconciliation, service, CRO); the sixth has an invoice summary that deliberately omits customer names. None of those facts establishes acceptance of the owner's complete six reports.

At the eleven-task level: **nine are Fix or extend, one is Build (the brand-row master), and one is Verify (historical loading and resulting LY acceptance).** This is a classification of tasks, not a percentage of effort or completion. Within those tasks there are substantial **Keep** components; separate Phase 4 work is **Integrate**. No full application or reporting-engine rewrite is justified.

The largest genuinely new pieces are the brand-row mapping master, the missing DSR fields/brand blocks, a brand-keyed stock-entry experience with previous-day defaults, and the cash-book ledger/carry-forward/month view. Customer reporting requires a customer-aware query and presentation extension, not another importer. CRO reporting is closest to the requested output. Historical import already uses a working multi-month, financial-year-aware engine.

**Important overlooked gap:** monthly store targets can be saved through a repository method and read by the DSR, but no application caller of that save method exists. Phase 2 needs a monthly-target editor, not a replacement target database.

## 2. Scope, references and evidence standard

Inspected the approved [master plan](ETP-MASTER-AUDIT-AND-PHASED-PLAN.md), [Claude Phase 1 audit](claude-audit-2026-09/PHASE-1-AUDIT.md), [Phase 1 report](claude-audit-2026-09/PHASE-1-REPORT.md), [original report audit](claude-audit-2026-09/01-reports-engine.md), and [import register](IMPORT-FAILURE-REGISTER.md). IF-001–IF-013 are VERIFIED; there are no OPEN Phase 2 rows in that register.

The owner's eight reference images in `C:/Codex/Reporting Manger/Reports to be generated/` were visually inspected: DSR, two stock sheets, cash book, service, two customer lists and CRO report. These are JPEG references, not formula-bearing Excel workbooks. Customer names from those images are not reproduced here. The stock totals shown are Titan 812 and Helios 501; the service example shows cash 700, card 1,807, UPI 577 and total 3,084. The DSR and customer examples include later sales than the supplied ETP export, as the approved plan explains. Do not change correct imported values to match a later trading cutoff.

Evidence labels:

- **Keep:** inspected implementation satisfies the stated subrequirement, with relevant evidence.
- **Fix or extend:** a usable implementation has an identifiable gap.
- **Integrate:** usable implementation exists on another branch; combined behavior remains to be tested.
- **Build:** searched implementation has no corresponding feature; reuse surrounding infrastructure.
- **Verify:** implementation exists, but missing evidence prevents acceptance. This does not imply rebuilding.

Source references below are relative to the repository root, with method names or current line anchors. Paths beginning `P4/` refer to the separate worktree `C:/Codex/Reporting Manger/phase4-security-operations`, at `df35d90`.

### Code reference index

| Ref | File / relevant implementation |
|---|---|
| R1 | `src/Etp.Reporting.Infrastructure.SqlServer/OperationalReportRepository.cs`: invoice summary 184; DSR 254; staff 308; service 399; cash 434; physical stock 460; DSR SQL 615; supplementary facts 676; Combine 761 |
| R2 | `src/Etp.Reporting.Reporting/DailySalesReportDocument.cs`: `DailySalesReportBuilder`, `BuildStore`, `BuildService`, `DsrDisplay` |
| R3 | `src/Etp.Reporting.Desktop/Modules/Reports/ReportsWorkspaceView.xaml.cs`: dispatch 95; invoice 245; DSR 258; staff 269; service 289; cash 296; physical 324 |
| R4 | `src/Etp.Reporting.Desktop/Modules/Reports/DailySalesFocusedView.cs`; `src/Etp.Reporting.Desktop/DailySalesReportControls.cs` |
| R5 | `src/Etp.Reporting.Infrastructure.SqlServer/DataTruthMasterRepository.cs`: `Modes`, `SaveMonthlyTargetAsync`, `SaveTenderModeAsync`, `LoadManualAggregateAsync` |
| R6 | `src/Etp.Reporting.Desktop/Modules/Settings/DataTruthMastersView.cs`: existing Owner tender/staff editor; `SettingsWorkspaceView.xaml.cs` wires it |
| R7 | `src/Etp.Reporting.Infrastructure.SqlServer/OperationalCompletionRepository.cs`: `SaveManualStockCountAsync`, `LoadManualStockCountsAsync`, `SaveStaffTargetAsync`, `LoadStaffTargetsAsync` |
| R8 | `src/Etp.Reporting.Desktop/Modules/DailyWorkflow/DailyWorkflowWorkspaceView.xaml.cs`: stock save 183, staff-target save 206; corresponding XAML and presentation session |
| R9 | `src/Etp.Reporting.Domain/Periods/BusinessReportingPeriods.cs`; `src/Etp.Reporting.Reporting/ManagementMetricEngine.cs` |
| R10 | `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerReportingQueryRepository.cs`: mapped tender query 30, `LoadTendersAsync` 100; `src/Etp.Reporting.Reporting/RetailReportingPolicy.cs` |
| R11 | `src/Etp.Reporting.Reporting/CashBalanceReconciliationService.cs`; `ControlReconciliationService.cs`; `SqlBackedReportingExecutor.cs` |
| R12 | `src/Etp.Reporting.Reporting/VisualReporting.cs`: formatter 31, composer 49, total fallback 100; `VisualReportExporters.cs` |
| R13 | `src/Etp.Reporting.Reporting/OpenXmlReportExporter.cs`; `ReportPackExporters.cs` |
| R14 | `src/Etp.Reporting.Reporting/VisualReportPdfDocument.cs`; `DailySalesReportPdfExporter.cs`; `SimplePdfReportExporter.cs` |
| R15 | `src/Etp.Reporting.Desktop/Modules/Reports/ReportExportCoordinator.cs`; `ReportPresentationSession.cs` in the same module |
| R16 | `src/Etp.Reporting.Reporting/ReportDefinition.cs`; `src/Etp.Reporting.Desktop/ReportTaskAliases.cs`; `TaskNavigation.cs` |
| R17 | `src/Etp.Reporting.Infrastructure.SqlServer/FolderImportService.cs`; `PhaseOneImportPersistence.cs`; `EtpFamilySqlImportOrchestrator.cs`; `EtpInvoiceIdentity.cs` |
| R18 | `database/migrations/0017_sales_value_columns.sql`, `0018_etp_family_tables.sql`, `0019_phase1_masters.sql`, `0020_remove_document_extraction.sql` |
| R19 | `src/Etp.Reporting.Infrastructure.SqlServer/DailyReportingPackService.cs`: `GenerateAsync`, DSR control; `Phase2OperationsRepository.cs`: `LoadManagementTrendAsync` |
| R20 | `src/Etp.Reporting.Infrastructure.SqlServer/DailyReportingWorkflowRepository.cs`: `LoadAsync` and `EnsureDayAsync` |

## 3. Fresh validation performed

These are new runs during this audit, not copied completion claims. Commands used Release configuration and `--no-restore --verbosity minimal`; they compiled relevant dependencies. Existing test code was inspected before running database tests. `SqlDatabaseFixture` replaces the configured database name with `EtpPhase0Test_<guid>` and drops only that generated database. No live report screen was opened: Phase 1's workflow read currently creates a day row, so a nominal report read can write to a database.

| Run | Command target / filter | Result | What it establishes |
|---|---|---|---|
| V1 | `dotnet test tests-dotnet/Etp.Reporting.Reporting.Tests -c Release --no-restore --verbosity minimal` | 61 passed, 0 failed, 0 skipped | Existing calculations/exporter behavior covered by that suite; not six owner-sheet goldens. |
| V2 | `dotnet test tests-dotnet/Etp.Reporting.Domain.Tests -c Release --no-restore --verbosity minimal` | 12 passed, 0 failed, 0 skipped | Date/period and domain behavior, including same-date LY and leap day. |
| V3 | `dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests -c Release --no-restore --filter "FullyQualifiedName~DataTruthReportingSqlTests|FullyQualifiedName~PhaseOneImportSqlTests" --verbosity minimal` | 11 passed, 0 failed, 0 skipped; 2m31s test duration | Synthetic SQL target/partial-period/GST/mapping checks and both private real-corpus tests executed, rather than skipped. |
| V4 | `dotnet test tests-dotnet/Etp.Reporting.Desktop.Tests -c Release --no-restore --filter "FullyQualifiedName~Report|FullyQualifiedName~DailyWorkflowPresentation|FullyQualifiedName~SettingsWorkspaceView" --verbosity minimal` | 273 passed, 0 failed, 0 skipped | Filter also matches the `Etp.Reporting` namespace, so it ran the full Desktop suite. Covers report state/routing/export/workspace tests; not a fresh native six-report acceptance walkthrough. |
| V5 | In P4: `dotnet test tests-dotnet/Etp.Reporting.SqlServer.Tests -c Release --no-restore --filter "FullyQualifiedName~PhaseFourBoundaryTests|FullyQualifiedName~BackupCertificateServiceTests" --verbosity minimal` | 35 passed, 0 failed, 0 skipped | Local connection and certificate-service boundaries. Does not install tasks or export real custody keys. |
| V6 | In P4: `dotnet test tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests -c Release --no-restore --filter FullyQualifiedName~PhaseFourSecurityTests --verbosity minimal` | 6 passed, 0 failed, 0 skipped | Real disposable SQL permission/lock/audit tests, including legitimate Owner reopen and older import procedure access. Does not prove the combined Phase 1 import path. |

Total across these six runs: **398 passed, 0 failed, 0 skipped**, across two independently checked-out branches. This is not a test result for a merged application.

V3 test sources: `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/DataTruthReportingSqlTests.cs` and `PhaseOneImportSqlTests.cs`. They establish:

- Monthly store targets overwrite within the same month; CRO targets normalize to a calendar month and load over day/YTD scopes; staff without sales still appear.
- Available manual entries are summed and explicit zero counts as entered; missing-day counts remain available.
- GST-inclusive reporting, negative cancellation, staff names, and runtime editable tender mapping work in SQL.
- Real raw two-store imports assert 25 August totals of 34,215 and 29,290, August totals of 938,197 and 774,868.60, invoice counts 182 and 38, daily tender equality and staff equality.
- The two-year Helios test exercises the corpus, monthly golden file, repeated import and overlapping raw subset. Financial-year identity, content deduplication, locked dates and customer-only correction/restatement paths have regression coverage.

Limits: no new full-solution Debug/Release acceptance run, no fresh native report screenshots, no Excel/Acrobat manual opening, no touch walkthrough, no deployed security validation, and no independent new Python recomputation of every workbook cell. Claude's independent workbook recomputation is historical evidence, strengthened here by fresh execution of the actual SQL/import tests. Green exporter tests do not establish unclipped text: `VisualPdfExportRegressionTests` checks pagination/page dimensions/content streams, not full glyph extraction or visual boundaries. `DailySalesReportTests` uses constructed facts rather than loading the six owner sheets.

## 4. Requirement-by-requirement comparison

### Task 1 — DSR: Fix or extend

| Requirement | Classification | Existing implementation / what works | Exact gap / validation | Smallest necessary change |
|---|---|---|---|---|
| GST-inclusive FTD/MTD/YTD value, signed net volume, combined store totals | Keep | R1 SQL sums `source_gross_amount` and signed quantities; R2/R4 provide a two-store DSR document and screen. | V3 verifies values and negative cancellation; full screen/PDF owner-sheet acceptance still required. | Reuse query/document path. Do not rebuild the sales facts or import amounts. |
| LY same calendar date, April financial year, 29 Feb → 28 Feb | Keep | R9 uses `AddYears(-1)` for comparable date bounds. | V2 directly tests periods/leap day. D2 resolves the old same-date concern. | Retain policy; load missing source history and verify outputs. |
| INV-only invoice count and AUPT/AVPT/conversion denominator | Fix or extend | R1 computes productivity using distinct invoice identities. | `LoadDsrFactsAsync` counts all INV/SR/BC documents because the COUNT expressions do not restrict to INV; R11 guards division but cannot correct an incorrect count. | Restrict count expressions to INV while retaining negative SR/BC in sales/volume; add INV+SR+BC and no-data tests. |
| STORE TGT, DAY TGT, MTD BLA, REQ ADS and help | Fix or extend | R5/R18 persist monthly target; R1 reads it by month; R2 shows monthly target and achievement. | No application caller of `SaveMonthlyTargetAsync`; no day target/balance/required ADS fields. Combined target uses `SumIfAny`, hiding a missing store target. | Add monthly target load/editor using the existing table; add D5 calculations and help; carry target completeness into combined output. |
| Brand rows and store-total reconciliation | Build | Raw brand/segment fields and brand sales summary exist (R10/R3). | R2 has no brand-row model; no brand-row master/query consumer found in source/migrations. D4 owner code-to-row signoff remains outstanding. | Build mapped brand-row projection on existing sales, consuming task 10. Surface unmapped brands; prevent overlapping mappings from double-counting. |
| Full VOL/VALUE/AUPT/AVPT comparison matrix | Fix or extend | R2 carries FTD/MTD/YTD value/quantity and FTD productivity with some MTD/YTD context. | Current card model does not expose the entire requested matrix: e.g. AVPT context omits YTD and the requested LY YTD productivity cells are absent. | Extend the same document and screen/PDF renderers with the missing period cells. |
| Retail walk-in, invoice and conversion per store/period | Fix or extend | R1 aggregates available walk-ins with missing-day count; grid export exposes count (R3). | R2/R4 omit parts of the required matrix and do not propagate missing-day metadata; `Combine` converts absent invoice counts to zero. | Preserve nullable combined denominator and partial labels; expose per-store/period rows. |
| WCC walk-in/sales and WDC bills | Fix or extend | R18 seeds `WCC_WALKIN`, `WCC_SALES`, `WDC_BILLS`, `SERVICE_WDC`; manual input workflow exists. | R1 supplementary query reads only `SERVICE_WDC` and target; R2 lacks the WCC/bill rows. V3 verifies inserts, not their display. | Query seeded fields and extend document/renderers; no duplicate manual-input storage. |
| “—” without LY, single A4 landscape PDF | Fix or extend | R2 supports missing currency; R14 already produces A4 landscape with a Unicode font resolver. | Missing growth renders `N/A`; fixed tile sizes and always-green growth styles remain. Complete enlarged report has no visual acceptance. | Align missing-value text, size to content, color by sign; render long/large values and all brand rows. |

### Task 2 — Closing stock by brand: Fix or extend

| Requirement | Classification | Existing implementation / what works | Exact gap / validation | Smallest necessary change |
|---|---|---|---|---|
| Brand System, Display, Backstock, Defective, Y Loc, Physical, Difference, Remark, totals | Fix or extend | R1 physical query, R7 persisted stock counts, R3 physical report/export already include all component data, remarks and totals. System snapshots include brand. | Physical report groups cluster first, then brand. System difference uses separately entered counted physical, not component sum. Totals omit individual component columns. | Group with stable brand identity; report Physical as four-component sum and Difference against System; total every required quantity column. Preserve old independent-count evidence. |
| Brand-keyed entry and previous-day defaults | Fix or extend | R8 already saves and lists stock counts with a reason and component inputs. | Current form asks for free-text inventory group and separate counted physical; clears after save; no previous-day brand defaults. | Extend existing count service/UI with mapped brand rows, previous-day lookup and editable prefill. Store a new day's entry only on save. This also covers deferred Phase 1 task 13; do not implement twice. |
| 25 August System totals 812/501, brand by brand; touch entry | Verify | Owner images visibly show those totals; imported snapshots and stock queries exist. | This audit did not establish snapshot-to-sheet equality by brand. Consolidated R011 is empty; a historical ledger/multi-year corpus is not proof of a dated closing snapshot. Generic UI test success is not touch acceptance. | Identify the dated R011/R010 source, compare exact brands and totals, then touch-test the extended entry. Do not reconstruct or fabricate absent snapshots merely to meet a number. |

### Task 3 — Cash book: Fix or extend

| Requirement | Classification | Existing implementation / what works | Exact gap / validation | Smallest necessary change |
|---|---|---|---|---|
| Opening + cash sales + service cash − expenses − deposit + adjustment | Keep | R11 has a tested cash formula; R1/R8/manual inputs supply its facts. | Formula is reusable; current required-input behavior is a separate defect below. | Reuse arithmetic and reason-bearing manual input storage. |
| Per-mode Dr/Cr layout, total sale and monthly view | Fix or extend | R10 loads mapped tenders; invoice tender controls reconcile (V3); R3 exports a cash-reconciliation row. | Cash query selects only Cash; no Dr/Cr ledger, all-mode/service-mode breakdown or month carry chain. Cash screen uses only scope end date. | Add a cash-book projection and ledger presentation over existing tenders/manual facts; retain reconciliation separately. Noncash collections must not inflate physical closing cash. |
| Previous closing carried forward; editable opening with reason | Build | Existing opening field and reasons can be reused. | No prior-day closing lookup or carry-forward provenance found. | Add deterministic preceding-close resolution plus explicit opening override. Define missing/prior-unclosed-day behavior and restatement recomputation; do not silently use zero. |
| Optional adjustment/count should not block calculated closing | Fix or extend | R11 returns calculated closing and counted variance. | It currently requires `CASH_ADJUSTMENT` and `CLOSING_CASH_COUNTED`, although optional in definitions; R1 also indexes values directly after validation. | Separate calculation readiness from optional counted comparison; handle absent optional keys through the whole caller path. |
| D11 Gift Card and Cheque/RTGS categories | Fix or extend | R5/R6/R10 already provide editable tender mapping and consumers. | R5 mode list and R18 SQL CHECK omit Gift Card/Bank; R18 seeds CHEQUE and CHEQUE_RTGS_REFUND as TC; R10 policy does not accept the new mode names. Owner decision is documented but not implemented. | Add a new migration for constraint/seed corrections, update mode allow-lists and tests, identify exact gift-card source code from profiles. Keep unrelated TC rows until mapped evidence says otherwise. |
| Credit note presentation | Fix or extend | Import stores issued and redeemed credit-note legs; daily identity passes V3. | A net CN zero is easily mistaken for no credit-note activity. | Show issued, redeemed and net credit note, retaining the signed values; do not remove either leg to make the display simpler. |

### Tasks 4–6 — Service, customer and CRO reports

| Requirement | Classification | Existing implementation / what works | Exact gap / validation | Smallest necessary change |
|---|---|---|---|---|
| 4. Service day Cash/Card/UPI/Total; MTD/LY MTD/TY YTD/LY YTD | Fix or extend | R1 `LoadServiceSalesAsync` and R3 service destination/export exist; V3 confirms partial entries sum and missing-day counts. | Standalone service row lacks WDC; R2 DSR service total uses `SumIfAny` and drops partial metadata. No owner service-input golden. A no-entry scope currently produces zeros with missing days, so status alone is not completeness. | Extend existing service model/query with WDC and clear availability; share one total definition across standalone report and DSR; verify the 3,084 example and period totals using actual manual evidence. |
| 5. Customer name, invoice quantity, GST-inclusive value, grand total | Fix or extend | R1 invoice summary correctly sums gross values; R18 stores customer fields; V3 covers customer-only corrections and real import. | R1 DTO/query and R3 report deliberately exclude names. No customer-aware report join or acceptance found. | Extend the invoice summary contract/query/export with current document-wise customer evidence (R024/R022 as appropriate), joined by store/FY/document/date with dedupe. Preserve one invoice grain and missing-name visibility. Do not re-import a second sales fact set. |
| 6. CRO target, net sales/qty, discount, ATV, UPT, transaction count | Verify | R1 staff projection and R3 screen/export contain every requested column; R7 monthly targets and R6 staff names work. V3 verifies day sales match source and monthly targets apply. | All-column owner-sheet comparison is absent. Current transaction count includes matched INV/SR/BC documents; validate the CRO-specific denominator, rather than applying the DSR INV-only rule blindly. Export formatting remains task 7. | Keep report calculation path; add workbook comparison of discounts, quantities, transactions, ATV/UPT and monthly target visibility. Fix only demonstrated differences. |

Task-level classification for task 6 remains Fix or extend because shared exports require changes; its business projection is Verify, not Build.

### Task 7 — Exports: Fix or extend

| Requirement | Classification | Existing implementation / what works | Exact gap / validation | Smallest necessary change |
|---|---|---|---|---|
| Shared report/export wiring | Keep | R3/R15 already expose Excel/PDF for report results; request revision/state prevents stale exports. | V4 exercises wiring/state; not a reason to create six export pipelines. | Feed extended/new report models into existing coordinator. |
| Unicode and measured PDF layout | Fix or extend | R14 `VisualReportPdfDocument` already embeds fonts through the DSR resolver, measures wrapping, paginates vertically and splits wide data into repeated-identifier column bands. V1 regression passes. | Bands still use equal widths; raw detail values ignore column formats. Old `SimplePdfReportExporter` and pack PDF use ASCII replacement and character clipping. DSR fixed rectangles need growth. | Reuse/extract measured renderer for tabular and pack output, add typed formatting/content-driven widths, retain special single-page DSR layout. No new PDF engine needed. |
| Numeric Excel cells, Indian grouping, dates, freeze/filter | Fix or extend | R13 tabular exporter already emits numeric cells, frozen header and autofilter; pack exporter has similar mechanics. | Active visual Excel route (R12/R15) has no freeze/filter and ignores column formats; dates become text. Tabular formatter recognizes only two built-in number styles; null Totals validation also rejects optional totals. | Share typed worksheet creation/styles; add date cells and explicit Indian number formats; preserve freeze/filter while excluding totals from filters where appropriate. Fix null-totals guard. |
| No misleading Executive Summary or double percentages | Fix or extend | R12 supports summary and charts. | Composer sums first four numeric columns, including overlapping periods/nonadditive metrics; percent format multiplies already-percent values by 100; header heuristic treats Invoice as currency. | Smallest option allowed by plan: remove generated summary/KPI sheet for these reports, retain reliable detail; otherwise provide explicit metric metadata/aggregation. Repair formatter used by remaining visual consumers. |
| Excel/Acrobat visual acceptance | Verify | V1/V4 demonstrate exports can be generated through tested paths. | No fresh manual opening, glyph extraction or clipping checks for all six owner reports. Page-size tests are insufficient. | After targeted changes, open actual exports, inspect long names, large/negative amounts, ₹/—, dates and percentages. |

### Tasks 8–11 — correctness, catalogue, master and history

| Requirement | Classification | Existing implementation / what works | Exact gap / validation | Smallest necessary change |
|---|---|---|---|---|
| 8. C9–C16 corrections | Fix or extend | Phase 1 already fixes unknown BC handling; date policy satisfies D2. Detailed disposition below. | Several old findings still apply; others are superseded. | Fix remaining individual expressions/control contracts, not the entire reporting repository. |
| 9. Remove stock-group and exception aliases | Fix or extend | R16 catalogue and R3 dispatch exist; `stock-group` calls the same physical report; five exception destinations filter one query. | Aliases remain exposed. The old dead `InitialReportCatalogue` is already gone, and current catalogue test targets `ProductReportCatalogue`. | Remove duplicate destinations; retain one Exceptions query and add filter chips. Update routing/help/tests; do not delete already-removed dead code again. |
| 10. Owner brand-row master, label/order/codes per store; DSR consumes it | Build | R6 offers a working Settings master-editor pattern; R18 provides migration conventions; sales retain raw brand evidence. | No brand-row table/repository/editor or DSR consumer found. Generic controlled master values are not this feature. | Add a narrowly scoped master, Owner authorization, read path and edit validation. Reuse Settings infrastructure; use one mapping source for DSR and stock identity where appropriate. |
| 11. One-time historical import; LY/growth/LY YTD acceptance | Verify | R17 already detects multi-month scope, deduplicates overlap and keys by financial year; V3 real two-year corpus ran successfully. R9/R1 already query LY periods. | Helios history exists as test input; full Titan 2025-26 coverage and A2.6 report figures have not been established here. Test import into a disposable database is not live historical loading. | Reuse normal folder import with prior-year files; audit coverage and independently compare Titan Apr–25 Aug 2025, then screen/PDF outputs. Build no separate historical importer. |

### Exact disposition of original correctness findings

The plan's task 8 shorthand is not an exact numbering list: target halving is original C3, while C9–C16 includes other issues. Track the behavior, not just the label.

| Original finding | Current disposition / evidence | Necessary next step |
|---|---|---|
| C3 target storage/combined halving | Monthly storage/read fixed by Phase 1 (V3); `SumIfAny` combined target still accepts one store alone (R2). | Preserve monthly storage; fix combined availability and editor gap. |
| C9 SR denominator | Still present in R1 counts. | INV-only DSR counts, retaining negative return amounts. |
| C10 unknown type blocks all sales | Superseded: R11 executor now skips unknown types with a warning; BC is approved. DSR still filters without matching warning. | Keep nonblocking behavior; align warning/completeness presentation if unknown rows exist. |
| C11 pack DSR control tautology | Still compares invoice sum and DSR FTD derived from the same sales lines (R19). | Label as internal consistency; use independent workbook/control evidence for acceptance. Do not advertise it as independent reconciliation. |
| C12 tender sign | Still opposite: R19 trend uses tender − revenue; R11 uses invoice − tender. | Standardize sign/labels and add a deliberate nonzero-variance test across both consumers. |
| C13 LY date | D2 explicitly approves current same-calendar-date behavior; V2 green. | Keep. Only historical data/acceptance remains. |
| C14 service partial total | R1 partial aggregation fixed with counts; R2 `SumIfAny` summary still loses completeness context. | Propagate availability into DSR; preserve partial totals explicitly. |
| C15 missing invoices coerced to zero | R1 `Combine` still sums `TyInvoices ?? 0`/`LyInvoices ?? 0`. | Return missing when no invoice evidence, while preserving genuine zero under a defined source-completeness rule. |
| C16 optional cash inputs block | R11 still requires adjustment and counted closing. | Separate calculable balance from counted reconciliation. |

## 5. Git history and Phase 4 integration

### What is actually in each branch

- Phase 1 audited head: `d1e7c48`, clean at start, tracking `origin/phase-1/data-truth`; includes Claude audit `e1ef5f3` and the owner's new category decision. The category decision commit changes documentation only.
- Local `main`: `9a028a4`; Phase 1 and main differ by **1 main-only / 33 Phase-1-only commits**. “32 commits behind main” is not true of the inspected history. No fetch/merge was performed during this audit.
- Phase 4 head: `df35d90`, clean separate worktree. Merge base with Phase 1 is `2cdc258`; differences are **33 Phase-1-only / 5 Phase-4-only commits**, one of the latter being main's handoff commit. Four commits implement/document Phase 4: `89d1b69`, `4e7abb4`, `b535ba1`, `df35d90`.
- Earlier `d7eb515` contains the visual PDF repair and is already an ancestor of Phase 1, main and Phase 4. It is **Keep/extend**, not unmerged work to cherry-pick.
- Earlier `5490b2e` and the file named `Phase2OperationsRepository.cs` refer to a previous productisation phase. Their names do not establish completion of the approved September rebuild Phase 2. Reuse their actual code where relevant.
- Phase 1 commits `49a4b6d`, `0cd05e8`, `aafa3bc`, `dde2694`, `1fb98a9` explain the current masters, GST/partial totals, staff placeholders, financial-year/content persistence and corpus tests. Those improvements supersede portions of the original audit.

### Phase 4: integrate code, then validate combined behavior

Read [Phase 4 implementation report](../../../phase4-security-operations/docs/audit/claude-audit-2026-09/PHASE-4-REPORT.md) and actual migration, repository, Settings and operations code. (P4 path is authoritative if this cross-worktree link is unavailable.)

| Existing Phase 4 work | Classification | Actual files / useful behavior | Integration or evidence gap / smallest action |
|---|---|---|---|
| SQL day lock and Owner reopen without Windows elevation; reads no longer insert a day | Integrate | P4 `database/migrations/0021_day_lock_security.sql`; daily workflow repository/contracts/service/UI. Trigger rejects invalid locked transitions and procedure requires reason. | Bring this into shared workflow once; reconcile changed caller signatures. Do not rebuild an Owner-reopen mechanism for Phase 2. |
| Narrow grants and append-only audit | Integrate | P4 `0022_least_privilege_audit.sql`, audit procedures, enrichment procedure. | Phase 1 directly writes new family/content tables, enrichment and staff (R17). P4 denies staff enrichment DML and lacks the complete Phase 1 write path. Preserve narrow access by extending approved procedures/grants; do not restore broad writer membership. |
| Local connection checks and migration hash normalization | Integrate | P4 `LocalSqlConnectionPolicy.cs`, `Migrations.cs`, `DatabaseBootstrapper.cs`, adapters. V5 passes. | Preserve Phase 1 finance changes and add validation at new connection entry points. Integrate checksum behavior without modifying committed migration contents. |
| Recovery-key Settings, trusted health receipts, backup/task/signing scripts | Integrate | P4 `BackupCertificateService.cs`, Settings UI, `0023_operations_status.sql`, `scripts/etp-operations-common.ps1`, backup/recovery/install scripts; `PowerShellOperationsService.cs` uses AllSigned. | Keep the implementation. Combine Settings tabs with Phase 1 masters rather than replacing the view. Deployment prerequisites remain separate. |
| Privacy/support-package and actual deployment acceptance | Verify | P4 support-package script and synthetic tests; implementation report separates coding from deployment. | Real imported-customer support-package check, native screenshots, live ACLs/tasks, signed deployment, approved encrypted backup and second-machine restore remain unaccepted here. Do not duplicate implemented scripts because these checks are missing. |

**Concrete semantic conflict:** P4's `persist_sales_enrichment` interface was designed around the older net-value schema. Phase 1's active `PhaseOneImportPersistence.InsertEnrichmentAsync` carries gross value, staff name, content key and financial year and seeds staff. Merely resolving `RetailEnrichmentSqlImportOrchestrator.cs` text conflicts does not integrate the active Phase 1 path. Combined tests must import all families as Store Manager, exercise duplicates/supersets/restatements and preserve gross/net/tax, staff names and FY identities.

**Migration numbering:** Phase 1 has 0017–0020; Phase 4 deliberately reserves those and adds 0021–0023. Phase 2 must reserve its next migration after that shared range (0024 or next free number), even if coding begins before integration. Never edit already-committed 0019 just to add Gift Card/Bank.

**Files changed on both branches (19):** README, handoff, Desktop App/composition/startup, Settings XAML/code, TaskNavigator, AutomatedOperationsService, DailyReportingWorkflowRepository, OperationalCompletionRepository, OperationalReportRepository, Phase2OperationsRepository, ProductisationRepository, RetailEnrichmentSqlImportOrchestrator, SqlServerReportingQueryRepository, SqlServerRepositories, integration-test project and PhaseZeroSqlTests. This is an intersection of changed paths, not a claim that every file will have a textual merge conflict. No merge simulation or branch mutation was performed. The handoff add/rename divergence also needs reconciliation.

P4 deployment report explicitly identifies supported SQL edition, key custody, task principal/folder access and signing prerequisites. Its backup script rejects Express/Web for the approved native encrypted path. This audit did not change that policy, machine configuration or live data, and did not independently certify the deployment.

## 6. Duplicated or overlapping implementations to consolidate carefully

1. **PDF:** retain the newer PDFsharp/font/wrapping implementation; adapt the two older ASCII/clipping exporters to shared rendering. Changing a character limit cannot solve absent Unicode fonts or measured pagination, so replacing those small renderer internals is justified; replacing the reporting engine is not.
2. **Excel:** three writers share cell/style/sheet concerns but diverge in formatting and freeze/filter behavior. Reuse a shared typed worksheet helper rather than fixing only the less-used tabular route. R15 routes any non-null visual model to visual Excel.
3. **DSR, invoice, staff, trend and pack:** reuse the same approved financial facts and period/metric policy. Preserve independent acceptance against workbook evidence; don't create another totals engine in a new screen. Fix sign/denominator inconsistencies at their ownership point.
4. **Stock:** `stock-group` and `stock-physical` duplicate a query; brand stock currently groups by both brand and inventory group. Extend the physical report to the owner's brand layout while retaining genuinely different system/item reports.
5. **Exceptions:** one query already supports all five aliases. Expose filters on it, rather than building five screens.
6. **Masters:** extend the working tender/staff Settings area with brand rows and monthly targets. Older “Tender rules”/generic master settings are not a substitute for the consumed `tender_modes` master; their broader removal remains Phase 5.
7. **Deferred stock entry:** Phase 1 task 13 and Phase 2 task 2 describe the same feature. Schedule one implementation alongside brand mapping.
8. **Historical imports:** no separate “historical facts” tables, parser or importer. Use Phase 1 normal imports and coverage checks.
9. **Security/operations:** integrate Phase 4's implementations once. Missing deployment acceptance is not missing source code.
10. **Sharing and shell:** export integration is Phase 2; full WhatsApp/email delivery/history is explicitly Phase 5, and the Today landing/touch shell is Phase 3. The Phase 2 goal mentions sharing/Today, but rebuilding those subsystems here would overlap later phases. Keep existing hooks and identify cross-phase acceptance dependencies.

## 7. Report acceptance is distinct from coding completion

| Acceptance | Current assessment | Evidence needed to close |
|---|---|---|
| A2.1 DSR exact source totals, productivity, brand sum, combined screen/PDF | Not accepted; data foundation verified, implementation gaps known | INV-only denominator and missing matrix/brand/target fields; actual screen/PDF comparison against workbook-derived values. Preserve 774,868.60 underlying Helios MTD even when display rounds to 7,74,869. |
| A2.2 stock 812/501 and touch entry | Not accepted; implementation extension plus source verification | Dated system snapshot per brand, full entry/prefill flow and touch check. |
| A2.3 cash structure and all tender modes | Not accepted; reconciliation engine exists | Complete ledger/month/carry-forward model, revised categories, per-mode comparison and opening/closing evidence. |
| A2.4 all exports readable and correctly formatted | Not accepted; some repaired renderer behavior exists | Actual six-report Excel/Acrobat inspection, full Unicode/large amount/percentage/date checks across report and pack routes. |
| A2.5 six workbook goldens and green tests | Partial: fresh relevant tests green | Existing sales/tender/staff import checks do not cover all six final report models. Add missing output goldens and run full solution after implementation. Service/physical/opening inputs require manual evidence; do not invent ETP service exports. |
| A2.6 historical DSR LY/growth and Titan LY YTD | Verify | Coverage of both stores' prior-year exports and independent Titan 1 Apr–25 Aug 2025 total; compare resulting screen/PDF. |

Reference cautions before implementation:

- D5 specifies monthly/days-in-month, target minus actual, and remaining days including today. The old screenshot's DAY TGT and negative BLA are not a reliable implementation of those formulas. Surface this discrepancy in Phase 2 evidence; do not quietly copy its literal numbers or reverse the newly approved formula.
- The listed DSR brands are a subset of the stock brands visible in the owner's stock sheets. D4 requires owner code-to-row mapping; A2.1 requires totals to reconcile. Make handling of other/unmapped brands explicit rather than dropping sales or forcing unrelated brands into a named row.
- D11 is partially clarified: Gift Card → Gift Card, Cheque/RTGS → Bank, CN = Credit Note. This audit does not silently approve remaining mapping decisions.

## 8. Prioritized minimum implementation checklist

### P0 — Establish the shared base and prevent rework

- [ ] Preserve Phase 1 as the financial/import baseline; reconcile the handoff/main divergence during a separately authorized integration.
- [ ] Integrate Phase 4 connection/workflow/permissions changes and test the active Phase 1 import path as Store Manager; preserve each branch's behavior.
- [ ] Reserve migration numbers beyond 0023; apply Gift Card/Bank correction through a new migration plus mode/policy consumers.
- [ ] Prepare D4 distinct source-brand mapping proposal; resolve coverage/unmapped rows. Confirm source snapshots/history/manual inputs needed for acceptance.

### P1 — Correct and expose existing calculations

- [ ] Fix DSR INV-only counts, missing combined invoice/target semantics, tender-variance sign and optional cash behavior with behavioral regressions.
- [ ] Add monthly store-target editor/readback over `monthly_targets`; expose D5 fields and help.
- [ ] Add brand-row master and DSR projection; wire WCC/WDC fields already seeded; complete matrix and partial-period labels in one shared document.
- [ ] Extend existing stock entry/report to brand, previous-day prefill, component-based Physical and all totals. Implement deferred task 13 only here.
- [ ] Extend cash reconciliation into the daily/monthly Dr/Cr cash book with opening provenance, override reason and all tender/service categories.
- [ ] Add WDC/availability to service report; extend existing invoice query with customer evidence. Retain CRO report and verify its remaining columns.

### P2 — Finish shared output and acceptance

- [ ] Reuse measured Unicode PDF renderer across pack/tabular routes; resize DSR to retain a readable single landscape page.
- [ ] Consolidate typed Excel cells/formats/freeze/filter; remove or correctly define generated KPIs and percent formatting.
- [ ] Remove catalogue aliases in favor of one Exceptions screen with filters.
- [ ] Reuse folder import for historical data; verify coverage before treating missing LY as a code bug.
- [ ] Add final report goldens for all six outputs; validate large/negative/partial/missing/return cases and actual owner references.
- [ ] Complete Claude's report acceptance and native screen/export checks. Track Phase 4 machine deployment and privacy acceptance separately before staff use.

## 9. Change boundary

This audit adds only this requested document. No application code, migrations, reference/source files, settings, branch merges, deployments or live data were changed. Test runs compiled existing projects and created/disposed isolated test databases. Existing worktrees and commits were preserved. No commit or push was made as part of this audit.

**Plain-language summary:** keep the import engine, financial facts, period calculations, monthly target storage, staff reporting, manual entry storage and export wiring. Fix and extend the DSR, stock, service, customer and cash presentations; build the missing brand master and cash carry-forward pieces. Integrate Phase 4 security rather than implementing it again. A plan to rebuild six reports from scratch would duplicate substantial completed work; missing acceptance evidence should trigger verification, not replacement.
