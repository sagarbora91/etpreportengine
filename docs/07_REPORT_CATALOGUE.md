# Initial Report Catalogue

All reports query canonical SQL data. Excel and PDF are renderers of the same result used on screen.

## Sales

| ID | Report | Primary dimensions | Measures | Reconciliation control |
|---|---|---|---|---|
| RPT-SALES-001 | Daily Sales | date, store, business unit | gross sales, returns, discounts, net sales, units, bills, ABV, ASP | totals equal canonical sales facts for identical filters |
| RPT-SALES-002 | Brand-Wise Sales | brand, date range, store, business unit | net sales, units, contribution % | brand totals sum to report grand total plus explicit unclassified bucket |
| RPT-SALES-003 | Brand-Segment Sales | ETP `CLUSTER` brand segment, brand, store | net sales, units, contribution % | segment totals plus unclassified reconcile to canonical product-linked lines |
| RPT-SALES-004 | LY-TY Sales | approved comparison period plus selected dimensions | LY/TY value and units, absolute growth, growth %, contribution | each period independently reconciles to canonical facts |
| RPT-SALES-005 | Staff-Wise Sales | staff, date range, store | net sales, units, bills and approved productivity measures | unassigned sales remain visible; staff totals reconcile to store total |

### Sales formula candidates

- `Absolute Growth = TY - LY`.
- `Growth % = (TY - LY) / LY * 100` only after zero-base and comparison-period policies are approved.
- `Contribution % = group net sales / report net sales * 100`, with explicit zero-total handling.
- ABV and ASP require approved denominator and return/cancellation treatment.

## Stock

| ID | Report | Primary dimensions | Measures | Reconciliation control |
|---|---|---|---|---|
| RPT-STOCK-001 | Closing Stock | as-of date, store, location, product hierarchy | quantity and value | calculated closing reconciles to trusted closing snapshot where available |
| RPT-STOCK-002 | Stock Movement | period, store, product/location | opening, inward, outward, sales, adjustments, closing | opening + signed movements = calculated closing |
| RPT-STOCK-003 | Brand-Wise Stock | brand, store, as-of date | closing quantity/value | brands plus unclassified reconcile to closing total |
| RPT-STOCK-004 | Category-Wise Stock | category hierarchy, store, as-of date | closing quantity/value | category totals reconcile to closing total |
| RPT-STOCK-005 | SKU Stock | product, location, store, as-of date | quantity/value and approved attributes | detail total equals higher-level stock report |
| RPT-STOCK-006 | Stock Reconciliation | store, product/location, period | calculated closing, ETP closing, variance | `ETP closing - calculated closing`, subject to approved sign rules |

The desktop Reports Centre also exposes physical stock, inventory-group stock and a **Slow / Exception Stock** operational view. Slow-stock status is deterministic: the selected ETP snapshot is compared with the most recent positive source-signed sale on or before that snapshot; 60 days is a watch and 90 days is an exception. It is not presented as a statutory ageing valuation.

## Tender, cash, service and exceptions

The visible catalogue includes tender reconciliation, tender diagnostics, daily cash reconciliation, service sales, daily exceptions, missing sources, unmapped data, and focused tender/stock/staff exception reports. Focused reports filter the same underlying exception evidence and never change technical control status.

## Management

Management Trend exports daily canonical `NETVALUE`, source-signed units, invoice counts, tender variance and unmatched staff-enrichment counts for up to 366 days.

## Parameter and output standard

Every report exposes only supported filters, stable sorting, subtotals and grand totals. Result metadata includes report/version, generated timestamp, active business-rule versions, filter values and source-through date. Exports carry the same metadata and control total.

## Deferred reports

Category-Wise Sales remains deferred until an authoritative category field or product-category master is supplied; ETP `CLUSTER` is a brand segment and must not be relabelled as Category. Sell-through, stock turn and days-of-cover remain deferred because purchasing/receipt coverage and an approved denominator policy have not been supplied.
