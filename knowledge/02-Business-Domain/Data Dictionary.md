---
type: glossary
status: active
module: business-domain
last_verified: 2026-08-27
---

# Data Dictionary

| Term | Meaning | Source/internal equivalent | Notes |
|---|---|---|---|
| Business date | Date represented by the ETP report, independent of import timestamp | `business_date` | Drives FTD/MTD/YTD; financial YTD begins 1 April. |
| R003 | All Discount Type | Discount enrichment/control | Does not create revenue facts. |
| R013 | CRO Wise Sales | Staff/CRO enrichment | Attribution control, not revenue authority. |
| R022 | Revenue Report | Invoice and tender control | Controls final payment/tender totals. |
| R025 | SDB-VariantwiseSales | Canonical item-level sales | `NETVALUE` is primary sales value including GST. |
| NETVALUE | Source-signed GST-inclusive net sales measure | Canonical sales value | Returns remain negative. |
| INV | Completed invoice | Sales transaction type | Confirmed business meaning. |
| SR | Sales return | Sales transaction type | ETP already supplies negative quantity/value; never reverse twice. |
| CLUSTER | Brand-segment code | `brand_segment_code` | Not product category; `GAUTO` = Titan Automatic. |
| FTD | For-the-day | Selected business date only | Value and quantity where report definition requires both. |
| MTD | Month-to-date | Month start through business date | Missing LY input remains unavailable. |
| YTD | Financial year-to-date | 1 April through business date | Compare with equivalent prior-year scope. |
| LY / TY | Last year / this year | Comparison periods | Denominator/date alignment follows approved reporting-period policy. |
| Walk-ins | Combined operational footfall | Controlled manual input when ETP lacks it | Store conversion is not fabricated without store-specific walk-ins. |
| CRO | Customer relationship officer/staff sales attribution | Controlled staff identity | Unassigned attribution variance remains visible. |
| Restatement | Controlled replacement of imported facts | Archived original + replacement lineage | Requires reason, user and transactional integrity. |

For authoritative field-by-field mappings see `docs/05_MAPPING_REGISTER.md` and [[Mapping Knowledge]].
