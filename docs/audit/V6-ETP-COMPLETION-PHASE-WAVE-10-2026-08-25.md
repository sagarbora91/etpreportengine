# V6 ETP completion wave 10 — isolated E7 service verification

**Date:** 2026-08-25

**Engineering status:** complete

**Production activation:** deferred pending real Service authority, exports, privacy approval and verified receipts

Wave 10 completes the deferred E7 engineering pattern as a separate `SERVICE_ETP_V1` boundary. It does not reuse retail ETP stores, dictionaries, scopes, authorities or receipts.

Six source packages require independent canonicalization, SHA-256 binding and separate Owner approval: the service report identity set, job-status dictionary, transaction dictionary, payment dictionary, SKU-token dictionary, and custody/consent/retention policy. S003 and S004 identities are mandatory; optional service snapshots are accepted only when explicitly approved. Unknown schemas, values, personal data and non-exact job keys fail closed.

The verifier binds exact service scope, generation, receipt and header signatures. It produces immutable PII-free matches, discrepancies and source evidence. The isolated durable repository preserves append-only verification runs, source restatements, evidence, closures and audit history across restart. Authority drift, binding drift, optimistic write failure, restore fencing and exact rebind are covered.

The ETP module includes a separate service-readiness surface with a dedicated host and responsive presentation. Without a shell-injected approved service facade it renders an explicit `BLOCKED / deferred` state. Controlled verification, evidence and closure actions require fresh reauthentication and an injected authorization decision; no permission is inferred from a displayed role label.

Focused authority, verifier, operational, presentation, mounted-host and independent privacy/isolation adversarial tests are green. Synthetic reports and approvals are test fixtures only. Production requires real S003/S004 exports, approved dictionaries, custody/privacy review, Owner approvals, verified receipts and physical staff UAT.
