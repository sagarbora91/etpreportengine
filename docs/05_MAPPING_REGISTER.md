# Initial ETP Mapping Register

This register records source-to-canonical candidates supported by the supplied exports. It deliberately excludes PII fields from canonical reporting facts.

## Sales lines — R025 / SDB-VariantwiseSales

| Source field | Canonical candidate | Type | Treatment |
|---|---|---|---|
| STORE CODE | store_code | identifier | required master lookup |
| TRANS_TYPE | source_transaction_type | text | `INV` = completed invoice; `SR` = sales return. ETP already exports `SR` quantity/value as negative, so preserve source signs and never negate returns a second time. |
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
| NETAMOUNT / NETVALUE | net measures | decimal | retain source sign; `NETVALUE` is the primary sales value, includes GST, and `SR` is already negative |
| SGST/UTGST, CGST, IGST, CESS rates/values | tax components | decimal | retain separately |
| INVREFNO / INVREFDATE | reference document identity/date | identifier/date | required for returns/restatements where populated |
| STORETIMESTAMP | source timestamp | datetime/text | lineage and tie-break evidence |
| CUSTOMERNAME / CONTACTNO | none | restricted PII | consume only if required for validation; never persist to reporting facts/logs |

## Invoice/tender — R022 / Revenue Report

Map store, invoice, invoice date, transaction type, invoice quantity, NetValue and reference document fields into invoice facts. Tender/refund source columns must normalize to `sales_tenders` rows rather than become permanent one-column-per-payment-type schema. Preserve the raw payment code/column identity and amount.

`CUSTOMERNAME` and `ContactNo` are restricted and excluded from the reporting database. `PAYMENTTYPE25` remains unresolved/quarantined until its meaning is approved.

## Enrichment

- R013 CRO Wise Sales: CRO NUMBER may map to controlled staff/CRO identity. CRO NAME and customer fields are not authoritative identifiers.
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

Observed types are INV, Purchase Return, SR and Purchase Receipt. `INV` is confirmed as an invoice and `SR` as a sales return. Purchase Return and Purchase Receipt meanings, plus all normalized stock signs, remain unapproved.

## Stock snapshots — Closing Stock

Map store, date, item, EAN, brand, brand-segment code (`CLUSTER`), gender, quantity, UCP and total UCP into a separate immutable snapshot fact. `UID_ITEM` is not a usable universal key in this corpus. Preserve source row, batch and file lineage.
