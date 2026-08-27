---
type: business-rule-register
status: active
last_verified: 2026-08-27
---

# Business Rules Register

These stable IDs summarize confirmed rules. Detailed executable behaviour remains in source and tests; unresolved decisions remain in `docs/11_DECISION_LOG.md`.

## BR-SALES-001 — Canonical sales value

- **Status:** approved/implemented
- **Rule:** R025 `NETVALUE` is the primary sales value and includes GST.
- **Exceptions:** Unknown transaction classifications remain visible/unresolved.
- **Reports:** All sales, DSR, staff attribution and management outputs.
- **Implementation:** `RetailSalesProfiles`, SQL import orchestration, `SalesReportingService`.

## BR-SALES-002 — Sales returns preserve source sign

- **Status:** approved/implemented
- **Rule:** `INV` is invoice; `SR` is sales return. ETP supplies `SR` quantity/value as negative; never negate it again.
- **Tests:** Import profile, persistence and reporting signed-return coverage.

## BR-TENDER-001 — Revenue control

- **Status:** approved/implemented with unresolved tender codes
- **Rule:** R022 Revenue Report controls final invoice/tender totals. Tender variance remains diagnostic; control rules are not weakened.
- **Exceptions:** Unknown tender types such as `PAYMENTTYPE25`/TC remain quarantined.

## BR-PERIOD-001 — Business reporting periods

- **Status:** implemented
- **Rule:** FTD/MTD/YTD derive from selected business date; financial YTD begins 1 April. LY uses the equivalent prior-year scope.
- **Implementation:** `BusinessReportingPeriods`, `ComparisonPeriods`.

## BR-CALC-001 — Safe percentage denominator

- **Status:** approved/implemented
- **Rule:** Growth is `(TY - LY) / LY × 100`; conversion and achievement divide their approved numerators by approved denominators. Missing or zero denominator displays `N/A`.

## BR-DATA-001 — Missing is not zero

- **Status:** approved/implemented
- **Rule:** Unavailable source/manual data remains unavailable and may block readiness. An explicitly entered zero is distinct from missing.

## BR-DSR-001 — Combined conversion

- **Status:** approved/implemented
- **Rule:** Combined conversion is combined invoices / combined walk-ins. Do not create store-level conversion without reliable store walk-ins.

## BR-CLASS-001 — CLUSTER meaning

- **Status:** approved/implemented
- **Rule:** `CLUSTER` is a brand-segment code, not product category. Resolve labels through controlled master data.

## BR-PRIVACY-001 — Restricted customer data

- **Status:** approved/implemented
- **Rule:** Customer names, contact numbers and sensitive payment identifiers are excluded from canonical reporting facts, logs and support packages.

## Unresolved register

Use `BUS-001` onward in `docs/11_DECISION_LOG.md` for business semantics that still require owner evidence. Do not promote an unresolved item into this confirmed register without approval.

Related: [[Report Catalog]], [[Mapping Knowledge]], [[Data Dictionary]].
