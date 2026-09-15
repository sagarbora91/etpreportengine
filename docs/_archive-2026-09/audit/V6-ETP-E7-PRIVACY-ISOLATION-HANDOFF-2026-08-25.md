# V6 ETP E7 privacy and service-isolation handoff — 2026-08-25

## Outcome

The E7 engineering boundary is testable as a separate `SERVICE_ETP_V1` domain. The adversarial acceptance suite passes against synthetic, explicitly test-only authority fixtures. This is not production approval and is not evidence that real Service Centre exports, policy decisions, custody, consent, retention, or staff UAT have been supplied.

## Engineering gates exercised

| Gate | Expected invariant | Automated result |
| --- | --- | --- |
| Service isolation | E7 does not import Retail ETP engines, repositories, report names, or dictionaries | Pass |
| Mandatory report identity | S003 Revenue and S004 Tender Detailed require exact approved report type, header hash, scope, generation, and receipt | Pass |
| Exact-key matching | Job keys are case-sensitive; the verifier does not fuzzy-match or guess | Pass |
| PII projection | Extra customer, mobile, phone, email, address, PAN, or Aadhaar fields reject the row set; persisted evidence contains no raw rows | Pass |
| Separate policy authority | Custody, consent, retention, and custody completeness fields must all exist in the separate Owner-approved Service authority | Pass |
| Drift resistance | Cross-service-unit scope, generation, receipt, and authority identity drift fail closed | Pass |
| Evidence lineage | Verification, evidence addition, and closure append audit events without rewriting the original discrepancy | Pass |
| Portable custody | Restore is fenced until an exact verified generation and receipt are rebound; hostile restore payloads fail atomically | Pass |
| Hostile input | Widened schemas, prototype-like keys, foreign boundaries, and caller-supplied source/authority substitution fail closed | Pass |

Automated evidence: `tests/v6-etp-e7-privacy-isolation-adversarial.test.mjs`.

## Production evidence still required

E7 must remain unavailable for production activation until all of the following are supplied and independently approved:

1. Controlled S003 Revenue and S004 Tender Detailed sample exports with exact header signatures and sample hashes.
2. An approved Service job-status dictionary.
3. An approved Service transaction dictionary.
4. An approved Service payment dictionary.
5. An approved SKU/token dictionary that distinguishes real stock from non-stock service tokens.
6. Owner-approved definitions for delivered stage, sparse periods, purchase scope, custody completeness, custody rule, consent rule, and retention rule.
7. Exact Owner approvals bound to every candidate hash and Service unit scope.
8. A live `ETP_VERIFIED` receipt bound to the same Service scope and import generation.
9. Physical-device staff UAT covering the mounted Service UI, narrow/mobile layout, denied reauthentication, source restatement, evidence attachment, closure, restart, and restore/reimport.
10. Privacy/security review of real exports confirming that the projection contains no customer PII and that operational retention matches the approved authority.

## Release statement

Status: **engineering acceptance passed with synthetic fixtures; production activation deferred**.

No real approval, operational receipt, real-customer export, privacy sign-off, or staff acceptance is asserted by this handoff.
