---
type: report-catalog
status: active
module: reporting
last_verified: 2026-08-27
---

# Report Catalog

Executable definitions and source dependencies are controlled by `ReportDefinition`, `ReportSourceRegistry` and the SQL-backed reporting executor. Detailed human authorities are `docs/07_REPORT_CATALOGUE.md`, `docs/22_REPORT_TO_SOURCE_MATRIX.md` and `docs/26_REPORT_CATALOGUE_EXPANSION.md`.

## Implemented report families

| Family | Reports/capabilities | Principal sources and rules |
|---|---|---|
| Sales | Daily Sales/DSR, Titan, Helios, Combined, Invoice, Returns, Brand, Brand Segment, Item, LY/TY | R025 signed `NETVALUE`/quantity; R022 control |
| Stock | Closing, Movement, Variance, Physical, Inventory Group, Brand, Slow/Exception | Closing snapshot plus source-signed ledger; human counts remain separate |
| Staff | CRO performance, target, achievement, ranking, growth, contribution | R013 enrichment matched to canonical R025; attribution variance visible |
| Tender/cash/service | Tender controls/diagnostics, cash reconciliation, Service | R022 plus controlled manual inputs; missing input is not zero |
| Exceptions | Daily, Missing Source, Unmapped Data, Tender, Stock, Staff | Focused views cannot turn failed controls into pass |
| Management | DSR and management trend/summary packs | Same canonical facts and registered rules |

All 29 named report destinations use shared period/store/segment/type/item filtering, preview, search/sort/variance views, Excel/PDF export and source drill-down where applicable.

### Implemented inventory (29)

- **Sales (9):** Daily Sales / DSR; Titan Sales Summary; Helios Sales Summary; Combined Sales Summary; Invoice Summary; Returns; Brand-wise Sales; Brand-Segment Sales; Item-wise Sales.
- **Stock (7):** Closing Stock; Physical Stock; Stock Variance; Stock Movement; Inventory-Group Report; Brand Stock; Slow / Exception Stock.
- **Staff (1):** Staff/CRO Performance.
- **Tender / Cash (3):** Tender Reconciliation; Daily Cash Reconciliation; Tender Diagnostics.
- **Service (1):** Service Sales.
- **Exceptions (6):** Daily Exception Report; Missing Source Report; Unmapped Data; Stock Exceptions; Staff Exceptions; Tender Exceptions.
- **Management (1):** Management Trend.
- **Investigation (1):** Invoice Source Drill-down.

## Daily Sales Report

Purpose: one-page management view for Titan, Helios and Combined performance.

- FTD, MTD and YTD retain value and quantity where defined.
- Growth: `(TY - LY) / LY × 100`; zero/missing denominator yields `N/A`.
- Combined conversion: combined invoices / combined walk-ins; walk-ins are a combined manual metric unless reliable store-specific data exists.
- Target achievement: MTD actual / monthly target; visual fill may cap at 100%, displayed percentage does not.
- Weekday derives from business date.
- Missing LY MTD displays unavailable/source-required, never fabricated.
- Service, tender and Titan/Helios/Combined target progress remain separate visible blocks.

Implementation entry points: `DailySalesReportDocument`, `DailySalesReportPdfExporter`, `DailyReportingPackService`, `DailySalesReportWorkspace`. Tests: `DailySalesReportTests` plus Desktop/report export tests.

## Planned/deferred

Category-wise sales is deferred because `CLUSTER` is brand segment. Sell-through, stock turn and days-of-cover require authoritative receipt/purchase coverage and approved denominator rules.

Related: [[Business Rules Register]], [[Mapping Knowledge]].
