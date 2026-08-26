# V6 ETP Completion Phase — Wave 3

Status: **GATEWAY/RUNTIME/MOUNT COMPONENTS IMPLEMENTED — LIVE VERIFIED JOIN PENDING**

Wave 3 adds:

- a durable shell-owned operational runtime with staged writes, verification, rollback and restart;
- a narrow E3/E4 gateway that exposes no repository, native plugin or raw-fact surface;
- an iframe-side mount controller that coordinates scope changes and destroys listeners cleanly.

The runtime preserves restore fences and exact generation/receipt rebind across restart. The gateway
requires active store-bound authority and fails closed on stale reauthentication, cross-store
requests and mismatched verified identities. E4 still refuses readiness without four approved
hash-bound sources.

## Remaining live-integration gate

The current governed Retail read projection does not expose `invoice_number` for R013 even though
the Retail profile and query contract support it. E3 requires invoice-grain R013↔R022 correlation.
The next change must extend the parent-owned projection, implement a bounded daily join and prove
duplicate/missing/cross-store behavior before the bridge or iframe UI is mounted.

These Wave 3 components are manifest-bound but deliberately not bootstrapped into a live operational
gateway yet. No raw verified row, E4 target publication, E5 money action, HEMW production or E7
Service ETP is authorized by this wave.
