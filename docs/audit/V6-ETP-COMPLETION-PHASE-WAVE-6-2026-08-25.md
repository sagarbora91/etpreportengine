# V6 ETP completion phase — Wave 6

Date: 2026-08-25

Wave 6 completes mounted E3 engineering acceptance across the real durable operational chain.

## Accepted lifecycle

- Staff, Store Manager and Owner declarations remain operational overlays and never verified facts.
- Store Manager or Owner closes a day and imports the current verified WLMHW binding.
- Successful import now completes the explicit `CLOSED → IMPORTED` transition atomically under one fresh approval context.
- Reconciliation is a privileged Store Manager/Owner action with fresh reauthentication; Staff cannot compute or confirm it.
- Both clean `RECONCILED` and managed `VARIANCE` branches are accepted.
- Corrections follow the approved 24-hour role boundary, require reasons and remain audited.
- Variance disposition is required before locking when unresolved outcomes remain.
- Locked days reject mutation; only an Owner-authorized new verified generation can start a restatement cycle.

## Production defects closed

- Mounted scope identity no longer drops `scopeKey` before backend calls.
- Successful presentation refresh returns a narrow success result, allowing production composition to mount.
- Reconciliation no longer bypasses foundation authorization.
- Portable restore audit sequences remain valid across multiple overlays and non-empty targets.
- Runtime-wide portable backup removes verified source rows, source-derived outcomes/queues and source-fact audit evidence from serialized E3 records while retaining human declarations, corrections, dispositions, lock state and restatement binding metadata.

## Verification

Wave 6 includes full-chain lifecycle, restart/restatement, portable privacy, mounted UI and adversarial security suites. Full Phase 6H regression, manifest integrity and deterministic Gate 0 receipt are required before commit.

## Remaining

E3 now requires representative staff UAT on the connected Android device. Engineering can proceed in parallel to E4 authority intake, but E4 activation remains blocked until its independent source and policy authorities are approved.
