# ETP Retail Verified Data Path — Decision and Evidence Register

Date: 2026-08-08
Status: mandatory inputs for production implementation and acceptance

| Gate | Required evidence/decision | Current state | Effect while open |
|---|---|---|---|
| HEMW source pack | Untouched R022, R025, R013 and R003 exports covering representative dates | Missing; owner confirms HEMW uses the same Retail ETP exports as WLMHW | Shared schema implemented; real HEMW conformance evidence remains pending |
| WLMHW identifiers | Confirm whether numeric invoice/item/CRO identifiers are authoritative, and approve any repair rule | Open; real files deliver numeric cells | Production parsing fails closed |
| HEMW identifiers | Same lexical/type/leading-zero evidence as WLMHW | Shared profile owner-confirmed; real lexical evidence pending | No separate HEMW adapter; unsafe numeric identifiers remain fail-closed |
| Report dictionaries | Versioned headers, aliases, required identifiers/measures, PII drops and code lists for each store/report | Not approved | Physical fact schema and adapters blocked |
| R001/R022 recognition | Approved filename/menu/export context or explicit operator confirmation | Not approved | Identical 46-column signature remains ambiguous |
| ENCIRCLE | Confirm bare amount/flag semantics and exact sensitive identifier aliases per context | Partially resolved; store dictionaries open | Unknown aliases fail closed |
| Transaction codes | Approve INV/SR/BC meaning and treatment of any additional codes | INV/SR/BC provisional only | Unknown money-affecting codes reject import |
| Date policy | Earliest valid date/store, invoice-date authority and future skew | Provisional deterministic A0 bounds | Final dictionary acceptance blocked |
| R022↔R025 rule | Exact common grain, mapped measures, rounding scale, tolerance, duplicates, filters and severity | Not approved; engine accepts explicit rules only | Reconciliation returns BLOCKED; no publication |
| Coverage/cut-off | Expected report cadence and manager-declared period end | Not approved | Verified figures withheld |
| Unknown tender workflow | Fail/quarantine/disposition and PAYMENTTYPE mappings including Razorpay/Airpay | Not approved | Unmapped tender remains separate and non-publishable |
| E4 targets | Authoritative target source, versioning and allocation approval | Missing | Target allocation cannot publish |
| E5 incentive | Scheme, bands, eligibility, rounding, restatement and clawback authority | Missing | No monetary incentive calculation |
| Fact-store encryption | Android Keystore AES-256-GCM chunk envelopes with scope/generation/report/chunk AAD | **Approved 2026-08-08; implemented in scaffold** | API-23 crypto/tamper evidence still required before runtime activation |
| Generation retention | Number/age of previous accepted generations to retain | Open | Production cleanup policy blocked |
| Physical API-23 | 2 GB-class device, OEM WebView, document provider, 20 MiB, rotation/background/low-storage/process-death | Not run | Parser cannot be production-approved |
| Owner acceptance | Both stores, recovery/re-import, reconciliation, privacy and E2–E6 views | Not run | Retail phase cannot close |

Until these gates close, synthetic policy engines may advance, but no incomplete or stale value may be labelled verified and no production fact-store schema may be frozen from WLMHW alone.

## Core-contract closure reconciliation — 2026-08-09

This section supersedes the earlier core-gate states for the bounded
`retail-etp-core-v1` freeze:

| Core gate | Frozen state |
|---|---|
| HEMW source pack | Real H003/H013/H022/H025 all-history exports evaluated; exact signatures match WLMHW. |
| Numeric identifiers | Exact non-negative integer lexical values, maximum 15 digits; no padding, rounding or leading-zero repair. Ambiguous values reject. |
| Report dictionaries | R003/R013/R022/R025 exact shared signatures, required fields, measures and PII drops frozen in the versioned profile. |
| R001/R022 | Core v1 accepts only the explicit four-file R003/R013/R022/R025 import route; it never guesses R001 from the shared signature. |
| ENCIRCLE | Bare R022 ENCIRCLE is an approved amount/flag; ENCIRCLE identifier aliases remain non-persistable. |
| Transactions | INV +1, SR -1 and BC -1; any other money-affecting code rejects. |
| Date/coverage | Deterministic date/FY limits, India business date and an explicit Owner/Admin complete-period declaration for all four reports. |
| REC-002 | Invoice/date grain; exact signed quantity; R022 net value versus R025 net amount; ₹1-per-invoice tolerance; blocking severity. Both real store packs passed with zero differences. |
| R013/R003 enrichment | Non-blocking, non-revenue controls. Differences are retained in the receipt and must be surfaced later. |
| PAYMENTTYPE25 | Unresolved non-zero values are quarantined and excluded from persisted/verified facts. |
| Encryption/retention | AES-256-GCM Android-Keystore envelopes; atomic active generation; current plus one previous generation retained. |
| Receipt/reader | Metadata-only durable receipt and report-specific verified projections bound to the active authenticated generation. |

Still open after core freeze: physical API-23/device acceptance, production
document-provider import, true process-death/disk-full/corruption evidence,
owner acceptance, E4 target authority, E5 incentive authority and any future
report families beyond the four-report core. These do not reopen the frozen
core interface unless a versioned v2 contract is deliberately authorized.
