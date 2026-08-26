# V6 ETP completion phase — Wave 4

Date: 2026-08-25

Wave 4 establishes the verified-fact-to-operational boundary and the private composition root for E3/E4.

## Delivered

- R013 exposes bounded `invoice_number` projection and deterministic sorting; filtering by invoice identity remains forbidden.
- `etp-e3-verified-join.js` joins governed R013 and R022 daily pages at invoice grain, aggregates duplicate item rows, quarantines conflicting CRO identity, and returns only the narrow E3 fact envelope.
- `etp-operational-bootstrap.js` privately composes the durable store, orchestrators, engines and gateway. Its public surface is frozen to E3, E4 and status.
- WLMHW E3 requires the approved Owner authority. HEMW remains denied. E4 remains blocked until its independent source and policy authority is supplied.
- Both new runtime assets are shell-loaded, integrity-bound in the module manifest, and source-bound in the Gate 0 receipt.

## Verification

- Wave 4 focused suite: 41 passing.
- Manifest, Gate 0 and Phase 6H integration guards are required to pass before commit.
- Full Phase 6H regression is required to pass before commit.

## Honest remaining boundary

This wave does not activate the operational UI. The next wave must inject the production read facade, session/reauthentication providers and authority records into the bootstrap, then mount the returned E3/E4 facades into the existing module hosts. E4 stays visibly blocked until its real authorities are approved.
