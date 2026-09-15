# V6 ETP completion wave 8 — governed E6 exception lifecycle

**Date:** 2026-08-25

**Engineering status:** complete

**Production activation:** blocked pending independently supplied Owner-approved E6 policy

Wave 8 completes the governed E6 engineering path without inventing business authority. The application now accepts the exact five E6 authority inputs, canonicalizes and hashes a candidate, and requires a separate Owner approval before producing an active policy.

The exception monitor binds every item to the approved policy, verified scope, generation and receipt. It records the approved SLA and default owner, preserves append-only action and evidence histories, and supports only governed acknowledgement, evidence addition, reassignment and closure. Mutations require a fresh authenticated session, exact binding and an approved role. Closed exceptions are immutable and missing signals never silently close an item.

The operational store persists one revision-checked E6 snapshot. Restart, optimistic concurrency, portable restore fencing, exact verified rebind, authority drift and generation drift are covered. The mounted ETP screen exposes honest `BLOCKED` and `READY` states, controlled action inputs, denial retry, policy identity, SLA/age/overdue state and evidence/history. Its layout is bounded for mobile without changing the desktop presentation.

Acceptance includes authority/schema adversarial cases, all seven exception families, atomic lifecycle mutations, restart/restore behavior, scope/actor/policy drift, mounted UI security and API-23-safe source checks. Synthetic policies are test fixtures only and confer no production authority.

Production still requires the real Owner-approved values for `SLA_BY_TYPE`, `DEFAULT_OWNER_BY_TYPE`, `ACKNOWLEDGE_AUTHORITY`, `REASSIGN_AUTHORITY` and `CLOSE_AUTHORITY`. Until those exact approved inputs are supplied, E6 fails closed and never reports a false all-clear state.
