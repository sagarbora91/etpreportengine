# Business Decision Log

Only unresolved business semantics are recorded here. Technical implementation choices belong in architecture records.

| ID | Business decision required | Why it matters | Evidence needed |
|---|---|---|---|
| BUS-001 | Define LY comparison: calendar dates, weekday-aligned dates, financial period or equivalent trading days. | Controls every LY/TY report. | Approved examples covering month boundaries and leap years. |
| BUS-002 | Define gross sales, net sales, returns and cancellation treatment, including GST and discount inclusion. | Determines all sales totals and growth measures. | Approved report/formula examples. |
| BUS-003 | Define ABV/ABT and ASP denominators and treatment of returns/zero-value bills. | Prevents inconsistent productivity measures. | Approved formula definitions. |
| BUS-004 | Complete mappings for exchange, cancellation and credit documents. `INV` = completed invoice; `SR` = sales return; ETP supplies `SR` quantities and values as negative and the engine preserves that sign. | Affects sales and stock simultaneously. | Remaining source codes plus an approved classification table. |
| BUS-005 | Approve stock equation signs and whether transfers, damage, shortage, excess and RTV affect each store/location. | Required for calculated closing stock. | Movement-code register and worked examples. |
| BUS-006 | Identify the authoritative opening and closing stock sources and their timing/cut-off semantics. | Required for stock reconciliation. | Representative ETP exports and operational cut-off rule. |
| BUS-007 | Confirm which product hierarchy source controls brand, category, subcategory and collection. | Required for reliable grouped reports. | Approved product master or mapping source. |
| BUS-008 | Confirm natural transaction/line identifiers available across ETP exports. | Required for record-level duplicate protection. | Real samples for sale, return and corrected/re-exported transactions. |
| BUS-009 | Confirm whether HEMW and WLMHW use identical production layouts and business meanings. | Prototype code shares a profile but authorizes them differently. | Approved representative exports from both stores. |
| BUS-010 | Approve reporting currency, rounding precision and round-at-line versus round-at-total policy. | Required for reproducible financial totals. | Approved accounting/reporting rule. |
| BUS-012 | Confirm whether R022 Revenue or Payment Type Report is authoritative for tender totals and how unknown `PAYMENTTYPE` columns are classified. | Required for normalized tender reporting. | Approved tender dictionary and reconciliation example. |
| BUS-013 | Confirm stock treatment/signs for `INV`, `SR`, `Purchase Return` and `Purchase Receipt`. The shared meanings `INV` = invoice and `SR` = sales return are confirmed. | Required before converting source-signed movements into business movement categories. | ETP transaction code register and worked stock examples. |
| BUS-014 | Resolve the two-row WLMHW difference between ledger `INV` movements and All Issues Detail for the supplied period. | Required to set the stock document reconciliation tolerance/policy. | Operational explanation or corrected export. |
