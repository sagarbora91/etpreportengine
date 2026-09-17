# Initial ETP Mapping Register

This register records source-to-canonical candidates supported by the supplied exports. Customer name and full phone number are retained for customer reports per D3; they must not appear in diagnostics or support output.

## Sales lines — R025 / SDB-VariantwiseSales

| Source field | Canonical candidate | Type | Treatment |
|---|---|---|---|
| STORE CODE | store_code | identifier | required master lookup |
| TRANS_TYPE | source_transaction_type | text | `INV` = completed invoice; `SR` = sales return; `BC` = bill cancellation, treated like SR. ETP already exports `SR` quantity/value as negative, so preserve source signs and never negate returns a second time. |
| ITEMNUMBER | product_code | identifier | required |
| HSNCODE | hsn_code | identifier | optional classification |
| BRAND / BRANDNAME | brand source attributes | text | resolve through controlled master |
| CLUSTER | brand_segment_code | text | approved brand classification/segment; example `GAUTO` means `Titan Automatic`; preserve the source code and resolve its display name through controlled master data |
| GENDER | gender_code | text | optional dimension |
| INVNUMBER | invoice_number | identifier | required lineage/business identity |
| INVDATE | transaction_date | date | required |
| QTY | source_quantity | decimal | retain source sign; `SR` is already negative |
| UCP / GROSSUCP | unit/list and gross source values | decimal | definitions pending |
| SCH_DISCOUNTS / USER_DISCOUNTS / PRE_DISCOUNTS | discount components | decimal | retain separately |
| NETAMOUNT | source_gross_amount | decimal | GST-inclusive Value used by every sales report (D1); SR/BC are negative |
| NETVALUE | source_net_amount | decimal | Ex-GST amount retained for GST reporting |
| TAX | source_tax_amount | decimal | GST; NETVALUE + TAX = NETAMOUNT |
| SGST/UTGST, CGST, IGST, CESS rates/values | tax components | decimal | retain separately |
| INVREFNO / INVREFDATE | reference document identity/date | identifier/date | required for returns/restatements where populated |
| STORETIMESTAMP | source timestamp | datetime/text | lineage and tie-break evidence |
| CUSTOMERNAME / CONTACTNO | customer header name / phone | restricted PII | Retain full values for customer reports per D3; exclude from diagnostics/logs |

## Invoice/tender — R022 / Revenue Report

Map store, invoice, invoice date, transaction type, invoice quantity, NetValue and reference document fields into invoice facts. Tender/refund source columns must normalize to `sales_tenders` rows rather than become permanent one-column-per-payment-type schema. Preserve the raw payment code/column identity and amount.

`CUSTOMERNAME` and `ContactNo` are stored as restricted customer header data per D3. `PAYMENTTYPE25` = AIRPAY → UPI and `PHONEPE` (R020 `PAYMENTTYPE20`) → UPI per D11. All signed tender columns, including round-off and credit-note issuance, participate in reconciliation to GST-inclusive `NetValue`. The Owner-editable `tender_modes` table maps source codes to Cash, Card, UPI, CN, TC, Service Cash/Card/UPI. Other proposed mappings and observed agencies are listed in the Phase 1 report for owner review.

## Enrichment

- R013 CRO Wise Sales: CRO NUMBER identifies staff within the store; CRO NAME seeds the editable `staff` master. R013 SR/BC values are normalised to negative (the export can carry positive NETVALUE with negative QTY). `source_gross_value` retains signed GST-inclusive NETAMOUNT; `source_net_value` retains signed ex-GST NETVALUE.
- R003 All Discount Type: activation/manual discount fields enrich an existing sale line. They do not create revenue facts.
- SDB Document Wise: invoice-level totals are reconciliation controls, not a second canonical revenue source.
- Payment Type Report: agency/payment rows are evidence for normalized tenders; card numbers and approval identifiers require restricted handling.

## Stock movements — Variant Stock ledger

| Source field | Canonical candidate | Treatment |
|---|---|---|
| STORE CODE | store_code | required |
| ITEMNUMBER | product_code | required |
| TRANS_TYPE | source_movement_type | raw value plus future approved classification |
| DOCUMENTNUMBER / DOCUMENTDATE | source document identity/date | required lineage |
| FROM LOCATION / TO LOCATION / LOCATION | location attributes | resolve through location master after semantics confirmed |
| REF_DOCUMENTNUMBER / REF_DOCUMENTDATE | reference identity/date | optional linkage |
| OPENING_QTY | source_opening_quantity | control value |
| TRANS_QTY | source_movement_quantity | source-signed value |
| CLOSING_QTY | source_closing_quantity | control value |
| BRAND / BRANDNAME / CLUSTER / GENDER / HSN CODE | product source attributes | master-resolution evidence |

Approved movement types are INV, SR, BC, Purchase Receipt, Purchase Return, STM Issue, STM Receipt, STM Dispatch, Stock Issue and Stock Receipt. Preserve source-signed movement quantities; BC is a stock receipt. Unknown transaction types warn per row and are skipped.

## Stock snapshots — Closing Stock

Map store, date, item, EAN, brand, brand-segment code (`CLUSTER`), gender, quantity, UCP and total UCP into a separate immutable snapshot fact. `UID_ITEM` is not a usable universal key in this corpus. Preserve source row, batch and file lineage.

## Invoice identity and targets

Invoice identity is (store, financial-year end year, invoice number). Use ETP INVOICEYEAR where present; otherwise April through December belong to calendar year + 1, and January through March to calendar year. Content-based line identity survives row reordering.

Store sales targets are keyed by store and month in `monthly_targets`. Staff targets are keyed by store, CRO and month; reporting reads overlapping monthly records. Manual MTD/YTD values sum available numeric entries and include missing-day counts; an explicit zero counts as entered. Service days are complete when Cash, Card and UPI are all entered.
