---
type: adr
status: accepted-deferred
date: 2026-08-27
last_verified: 2026-08-27
---

# ADR-006 - Offline Device Licensing

## Context

ETP is deployed to retail Windows computers that must work offline. Copying the application folder must not transfer activation, employees must never receive owner Microsoft credentials, and normal operation must not depend on a recurring licensing service.

## Decision

Use a dual-layer activation design:

1. a separate owner-only licence administrator requires MSAL-authenticated, allowlisted Microsoft owner identity for each new/replacement issuance; and
2. the store application verifies an ECDSA P-256 signed perpetual licence bound to a random installation secret protected by Windows DPAPI LocalMachine and restrictive ProgramData ACLs.

Use offline activation-request/licence-file transfer. Keep the production private key in an owner-controlled Windows CNG key with encrypted offline escrow. Distribute only the public verification key with ETP. Centralize final enforcement in `App.OnStartup`; MainWindow never owns licensing logic.

Engineering is accepted now, but implementation and enforcement remain deferred until the functional product is complete.

## Reason

The separate utility prevents signing authority from reaching store PCs, allows Microsoft MFA without recurring cloud dependency, and keeps daily use fully offline. Random installation identity avoids fragile hardware fingerprints while DPAPI prevents ordinary file-copy transfer to another Windows installation.

## Alternatives considered

- Microsoft sign-in on every startup: rejected because it breaks offline operation and confuses identity with licensing.
- local licence generation inside ETP: rejected because the private signing authority would ship to stores.
- central licensing SaaS: rejected because it adds recurring cost and availability dependency.
- MAC/disk-serial fingerprint: rejected because it is fragile and easy to copy/spoof.
- symmetric HMAC licence: rejected because the verification secret would enable licence creation from store binaries.

## Consequences

ETP gains a separate owner-administration deliverable, key-custody ceremony, offline request/import workflow and two-machine security acceptance test. Replacement licences require owner action, and old offline licences cannot be instantly revoked. A determined local administrator may still patch an unsigned application or clone an entire OS; this is accepted and documented.

## Affected components

Future changes affect `App.xaml.cs`, Application licensing contracts, a Windows infrastructure adapter, Desktop activation/settings views, installer ACL provisioning, operational audit and a separately packaged owner utility. See [[ETP Licensing Architecture]].
