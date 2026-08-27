---
type: mapping-index
status: active
module: mapping
last_verified: 2026-08-27
---

# Mapping Knowledge

The authoritative detailed control copy is `docs/05_MAPPING_REGISTER.md`; executable mappings live in import profiles, projections and migrations. This note records rationale and dependencies without copying large mapping tables.

| Source | Canonical role | Core rule | Main dependants |
|---|---|---|---|
| R025 `NETVALUE` | Signed sales value | Includes GST; preserve `SR` negative sign | Sales, DSR, brand/segment/item, LY/TY, management |
| R025 `QTY` | Signed sales quantity | Preserve source sign | DSR, UPT, product and staff outputs |
| R025 `TRANS_TYPE` | Transaction classification | `INV` invoice; `SR` sales return | Sales/returns and stock controls |
| R025 `CLUSTER` | Brand segment | Resolve display via controlled master; not category | Segment sales, stock and DSR |
| R022 invoice/tenders | Invoice-level control and normalized tender facts | Revenue Report controls final tender/payment totals | Tender, cash, DSR reconciliation |
| R013 CRO fields | Staff attribution enrichment | Code is preferred controlled identity | Staff/CRO performance |
| R003 discounts | Discount enrichment | Never creates revenue | Discount diagnostics and enriched sales |
| Variant Stock ledger | Source-signed movements | Preserve raw type/sign until approved mapping | Movement and stock variance |
| Closing Stock | Immutable dated snapshot | Separate from movements | Closing/brand/slow/physical stock |

## Unmapped behaviour

Unknown schema, transaction type, tender or master mapping must produce a visible diagnostic/quarantine state. It must not silently become zero, a generic approved category or revenue.

## Change procedure

1. Record the approved meaning and evidence.
2. Identify affected profiles, projections, facts, reports and tests with Graphify.
3. Version the executable mapping where applicable.
4. Test missing, unknown, duplicated and signed-return cases.
5. Update this note only if the mapping contract or rationale changed.
