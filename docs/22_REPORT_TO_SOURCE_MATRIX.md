# Report-to-Source Matrix

The executable registry is `ReportSourceRegistry`. This document is the human-readable control copy.

| Target report | ETP source reports | Canonical facts/fields | Manual inputs | Calculated fields | Reconciliation | Open control item |
|---|---|---|---|---|---|---|
| Titan customer/invoice sales summary | R025, R022 | Store, business date, invoice, item, signed quantity, `NETVALUE`, transaction type, lineage | None | Invoice count, units, net sales | R025 `NETVALUE` equals R022 Revenue total for scope | Customer PII is excluded; a customer-named output needs an approved minimal-PII policy. |
| Helios customer/invoice sales summary | R025, R022 | Same canonical sales grain as Titan | None | Invoice count, units, net sales | Same R025/R022 control | Same PII control. |
| DSR: Titan, Helios, combined | R025, R022; R013/R003 enrichment | Date, store, invoice, item, brand, CLUSTER brand segment, signed quantity/value, transaction type | Walk-ins | FTD, MTD, YTD, equivalent LY, growth, conversion, UPT, ATV | Titan + Helios = combined; R025 = R022 | DSR and staff denominators are separate pending approval. |
| Service sale | Controlled operational service entry, R022 control | Service is deliberately not mixed into retail canonical sales | Service cash, card, UPI; zero must be entered explicitly | FTD/MTD/YTD, equivalent LY and growth | Tender components = service total and service cash feeds cash reconciliation | A future ETP Service profile may replace manual entry only after deterministic approval. |
| Daily cash/tender/DR-CR | R022 and controlled service entry | Revenue control, normalized retail tender amount/type, lineage | Opening cash, service cash, expenses, cash deposit, adjustment, counted closing, remark | Calculated closing, cash variance and tender variance | ETP tender vs Revenue `NETVALUE`; calculated vs counted cash | TC/unapproved tender types remain quarantined. |
| Titan closing stock | Closing Stock, Variant Stock ledger | Store/date/item/inventory hierarchy/system quantity/value/lineage | Display, backstock, defective, Y location, physical, remark | Physical, system, difference | Physical vs snapshot; ledger vs snapshot | Physical composition is never inferred. |
| Helios closing stock | Closing Stock, Variant Stock ledger | Same stock grain as Titan | Same stock inputs | Same stock metrics | Same stock controls | Same physical-composition control. |
| Staff/CRO performance | R013, R025 | Source CRO code, store/date/invoice/item/quantity/value/discount/lineage with deterministic R025 matching | Target | Contribution, UPT, ATV; achievement/rank follow controlled staff targets | Attributed + unassigned = canonical store sales; exact variance remains visible | Staff transaction denominator is independent from DSR invoice count. |

## Business-date contract

Every newly imported file records the ETP-derived `business_date` and `source_report_date` separately from `import_batches.started_utc`. FTD, MTD and YTD are resolved only from the selected business date. YTD starts on 1 April; LY periods end on the equivalent prior-year date, so a partial current period is never compared with a complete prior period.
