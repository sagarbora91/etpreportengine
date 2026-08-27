---
type: architecture
status: accepted-deferred
module: licensing
last_verified: 2026-08-27
---

# ETP Licensing Architecture

## Status

The dual-layer licensing architecture is approved and engineered, but runtime implementation and enforcement are intentionally deferred until the owner declares the application functionally complete. No production keys, Microsoft credentials, owner identifiers, tokens or machine secrets belong in this vault.

Detailed authority: `docs/security/ETP_LICENSING_ENGINEERING_SPEC.md`. Implementation sequencing: `docs/security/ETP_LICENSING_IMPLEMENTATION_BACKLOG.md`. Related decision: [[ADR-006 - Offline Device Licensing]].

## Threat model

The primary threat is an employee copying the installed ETP directory, licence and visible state to another PC. The target is strong practical resistance to casual copying and unauthorized store installation, not unbreakable DRM against a determined local administrator or reverse engineer.

## Three independent decisions

```text
Microsoft owner authentication → who authorized issuance?
ECDSA licence signature       → is the licence genuine and unchanged?
DPAPI installation binding    → is it running on the intended Windows installation?
```

None of these checks substitutes for another.

## Selected issuance model

The store application creates an offline activation request and imports a signed `.etplicence` file. A separate owner-only ETP Licence Administrator authenticates an approved owner through MSAL/Microsoft Identity, reviews the request and signs the licence.

The owner administrator and production private key are never included in store installers. The ETP application contains only trusted public verification keys.

## Offline runtime

After activation, startup loads DPAPI-protected state from `%ProgramData%`, verifies the licence signature, recomputes the device identity and validates product/store/installation policy. No Microsoft login, token refresh, SQL query or network call occurs during ordinary startup.

## Device binding

- one random installation GUID;
- one 32-byte random installation secret;
- human device ID derived with HMAC-SHA-256;
- secret protected by Windows DPAPI LocalMachine plus restrictive file ACLs;
- no dependency on MAC address, disk serial or ordinary peripherals.

Windows reinstall or a new PC requires new activation. Monitor, printer, network adapter, RAM and USB changes do not.

## Licence signature

- ECDSA P-256 with SHA-256;
- IEEE P1363 fixed 64-byte signature;
- exact canonical UTF-8 payload bytes inside a versioned envelope;
- embedded public-key ring with `keyId` rotation;
- perpetual device licence initially; expiry field reserved for future policy.

## Replacement

A replacement PC creates a new request and receives a new licence referencing the previous licence. An old permanently offline licence cannot be remotely revoked; issuance history and physical retirement remain required controls.

## Architecture boundaries

- `App.OnStartup` is the eventual central gate.
- MainWindow contains presentation/navigation only and no licence logic.
- SQL roles do not authorize device activation.
- store binaries verify but never sign.
- Microsoft authentication exists only in the owner utility.
- private key stays in an owner-controlled Windows CNG key and offline encrypted escrow.

## Known limitations

- an unsigned executable can theoretically be patched;
- DPAPI LocalMachine is not a TPM and a forensic OS clone may carry machine secrets;
- an owner-account or signing-key compromise can issue licences;
- offline perpetual licences have no instant remote revocation.

These limitations are explicit release risks, not hidden assumptions.
