# Report Catalogue Expansion — v1.7.0

The Reports Centre exposes named categories instead of hiding outputs behind engineering-oriented buttons.

## Sales

Daily Sales/DSR, Titan, Helios and Combined summaries, Invoice Summary, Returns, Brand-wise, Brand-Segment, Item-wise and LY/TY comparison all use canonical R025 `NETVALUE` including GST. `SR` values retain their source-negative sign.

## Stock

- Closing Stock is the exact selected-date ETP snapshot by item.
- Stock Movement retains source transaction type and source-signed quantity.
- Stock Variance retains the existing opening + movement = expected closing control.
- Physical and Inventory-Group reports keep system and human-count evidence separate.
- Brand Stock partitions the selected-date snapshot by store, `CLUSTER` and brand.
- Slow/Exception Stock compares that snapshot with the most recent positive source-signed sale. Sixty days is `WATCH`; ninety days is `SLOW`. This is an operational movement indicator, not statutory inventory ageing.

## Staff, tender, cash and service

Staff/CRO output shows sales, target, achievement, ranking, LY growth and contribution together with the exact canonical attribution variance. Tender, cash and service reports retain their existing controls and missing-input behaviour.

## Exceptions and management

Daily Exceptions remains the complete evidence set. Missing Source, Unmapped Data, Tender, Stock and Staff reports are focused views of that same set; filtering cannot turn a failed technical control into a pass. Management Trend provides printable/exportable daily canonical sales, units, invoices, tender variance and unmatched staff rows.

## Shared workflow

Every named report uses the same period/store/segment/type/item filters, on-screen preview, search, sorting, variance-only view, Excel export and PDF export. Source drill-down remains available from Invoice Source Drill-down.

## Intentionally deferred

Category-Wise Sales is not fabricated because `CLUSTER` is Brand Segment, not product category. Sell-through, stock turn and days-of-cover require authoritative receipt/purchase coverage and approved denominator rules.
