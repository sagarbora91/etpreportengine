# ETP Import Profiles

## Supplied evidence

The first real corpus contains 59 XLSX exports covering 1 July 2026 through 25 August 2026:

- HEMW: 30 reports, 24 populated and 6 header-only.
- WLMHW: 29 reports, 21 populated and 8 header-only.
- Every workbook has one sheet, headers on row 1 and no workbook formulas.
- All files incorrectly declare the worksheet used range as `A1`; readers must scan the actual worksheet XML rather than trusting the dimension.

The source workbooks remain outside Git and must not be copied into diagnostics, test output or release artifacts.

## Confirmed Retail profile identities

| Profile | ETP export | Intended role | Physical grain |
|---|---|---|---|
| R003 | All Discount Type | Discount enrichment and control; not revenue authority | item/discount occurrence |
| R013 | CRO Wise Sales | CRO attribution enrichment; not revenue authority | invoice item line |
| R022 | Revenue Report | Invoice revenue, quantity, tender and refund facts | invoice |
| R025 | SDB-VariantwiseSales | Canonical item-level sales facts | invoice item line |

The real headers match the prototype identities. Existing prototype field allowlists remain candidate mappings, subject to typed conversion and PII-drop enforcement in the new engine.

Confirmed sales transaction classifications: `INV` means a completed invoice and `SR` means a sales return. ETP exports `SR` quantities and values as negative. The importer must preserve those source signs; classification must never cause a second sign reversal.

## First production profile sequence

### Sales slice

1. R025 / SDB-VariantwiseSales — canonical sales lines.
2. R022 / Revenue Report — invoice/tender header facts.
3. SDB Document Wise — independent invoice-level reconciliation control.
4. Payment Type Report — supplemental normalized tender lines.
5. R013 and R003 — CRO and discount enrichment after the core revenue slice reconciles.

Observed candidate keys in this corpus:

- R022 and SDB Document Wise: store plus invoice number is unique in both stores.
- R025 and R013: store plus invoice plus item is unique in both stores.
- R003 is not unique at store/invoice/item grain in WLMHW. Preserve source row lineage and do not invent a natural key.
- Payment Type is multi-row per invoice and requires source-row lineage until tender-line identity is approved.

Candidate keys are duplicate-detection evidence, not final database constraints, until a larger period and corrected/re-exported files are tested.

### Stock slice

1. Variant Stock ledger — canonical movement source.
2. Closing Stock — authoritative as-of snapshot source.
3. All Issues Detail and All Receipts Detail — document/tax enrichment and movement controls.
4. Issues/Receipts summaries and Purchase Receipt Summary — document-level controls.
5. SOR Ageing and SOR Sales — optional lot/ageing sources after cross-store availability is established.

Every observed Variant Stock ledger row satisfies `closing = opening + source transaction quantity`. This validates the source equation but does not approve business movement signs.

## Cross-store layouts

Twenty-three shared reports have identical headers. Six are also logically identical but one store’s export repeats the entire table side-by-side:

| Store | Export | Logical columns | Physical columns | Rows verified identical |
|---|---|---:|---:|---:|
| HEMW | Closing Stock | 20 | 40 | 469 |
| HEMW | Daywise Collection | 29 | 58 | 43 |
| HEMW | Encircle Enrollment | 19 | 38 | 38 |
| WLMHW | Revenue Report | 46 | 92 | 404 |
| WLMHW | Scheme Details | 14 | 28 | 320 |
| WLMHW | SDB-VariantwiseSales | 41 | 82 | 451 |

Importer rule: collapse a repeated layout only when the normalized header halves and every corresponding row half are identical. Block any mismatch. Never import both halves.

In the duplicated WLMHW SDB-VariantwiseSales layout, `TRANS_TYPE` appears again in physical column `AP`, which starts the second 41-column copy. It is the duplicate of logical column `A`, not a separate transaction-type field.

## Deferred profiles

The following exports are header-only in both stores and cannot yet receive production profiles:

- AdvanceOrder Collection
- AdvanceOrder Sales
- Encircle Redemption
- GC Wise Redemption
- PRP SALES
- PRP STM

SOR Sales is present only for HEMW. WLMHW SOR Ageing and Transactionwise Bank are header-only. These require additional populated samples before cross-store production activation.
