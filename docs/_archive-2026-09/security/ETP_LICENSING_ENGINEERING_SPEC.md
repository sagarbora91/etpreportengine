# ETP dual-layer licensing engineering specification

Status: **approved design; runtime implementation intentionally deferred**
Verified: 2026-08-27
Product: ETP Reporting Engine for Windows (.NET 10 / WPF)

Companion authorities:

- `ETP_MICROSOFT_APP_REGISTRATION.md`
- `ETP_LICENSING_OPERATIONS.md`
- `ETP_LICENSING_TEST_MATRIX.md`
- `ETP_LICENSING_IMPLEMENTATION_BACKLOG.md`
- `ETP_LICENSING_REQUIREMENTS_TRACEABILITY.md`
- `schemas/etp-licence-envelope-v1.schema.json`
- `schemas/etp-licence-payload-v1.schema.json`
- `schemas/etp-activation-request-v1.schema.json`

## 1. Decision and scope

ETP will use two independent activation controls:

1. a Microsoft-authenticated owner authorizes each new or replacement activation; and
2. ETP accepts only a signed perpetual licence bound to a machine-protected installation identity.

The selected issuance model is **offline activation request plus a separate owner-only licence administrator**. The store application creates a request and imports a returned licence. The owner utility authenticates the owner and signs the licence on an owner-controlled computer. The production private key never exists in the store application, store installer, SQL database, repository, Obsidian vault or normal support package.

This document completes the engineering authority. No product-code licensing checks, MSAL packages, key material, activation UI or startup enforcement are added until the application is declared functionally complete. Development builds therefore remain unrestricted for now.

## 2. Required security outcome

```text
Microsoft owner authentication
        +
owner authorization policy
        +
ECDSA-signed licence
        +
DPAPI-bound installation secret
        =
offline activated ETP installation
```

Copying the install directory, licence file, or ProgramData activation files to another Windows installation must not transfer activation. Normal operation after activation makes no network call and does not depend on a Microsoft token.

## 3. Existing architecture audit

### A. Existing architecture

| Project/component | Current responsibility | Licensing relevance |
|---|---|---|
| `Etp.Reporting.Domain` | canonical business contracts and periods | future licence value objects with no OS or crypto dependency |
| `Etp.Reporting.Application` | use-case contracts | future licensing status, service contracts and activation coordinator |
| `Etp.Reporting.Import` | workbook/ZIP intake and normalization | no licensing responsibility |
| `Etp.Reporting.Reporting` | deterministic reports and exports | no licensing responsibility |
| `Etp.Reporting.Infrastructure.SqlServer` | SQL persistence, operational audit and bootstrap | audit adapter only; SQL is not licensing authority |
| `Etp.Reporting.Desktop` | WPF shell and composition | activation presentation and startup routing only |
| Inno Setup/bootstrap scripts | installer, SQL Express and scheduled operations | create ProgramData location/ACL; exclude owner utility and private key |

The active solution has no general DI container. `App.OnStartup` is the appropriate composition boundary. `MainWindow` still constructs concrete SQL/report/import services and must not acquire licensing logic.

### B. Existing startup flow

`App.OnStartup` currently:

1. installs process-level exception handlers;
2. executes `--initialize-database`, if requested;
3. executes `--automation-once`, if requested; otherwise
4. constructs and shows `MainWindow`.

Final enforcement belongs between steps 1 and 2/3/4:

- `--initialize-database` may remain available to the elevated installer before activation because it grants no normal product access;
- `--automation-once` must require a valid licence;
- interactive startup must show `ActivationWindow` instead of `MainWindow` when validation is not successful;
- diagnostic recovery commands must be individually allowlisted, never implicitly bypassed.

### C. Existing security functionality

Reusable elements:

- Windows-identity application roles: `None`, `Viewer`, `StoreManager`, `Owner`;
- owner-only application administration checks;
- SQL operational audit repository with enumerated event types and privacy-safe detail;
- integrated-security SQL connection policy;
- safe LocalAppData settings writes with reparse-point rejection;
- admin-elevated installer and existing `%ProgramData%\EtpReporting` conventions;
- central `App` process startup and diagnostics.

These controls do not prove Microsoft owner identity, licence authenticity or device binding. Existing Windows `Owner` role is an in-application authorization role and must remain separate from activation authorization.

## 4. Threat model

### Protected assets

- right to run normal ETP functionality;
- production private signing key;
- owner identity allowlist;
- machine-bound installation secret;
- licence issuance and replacement history.

### Primary adversary

A normal employee or competitor who can copy the installed directory and visible files, but does not control the owner Microsoft account or owner administration computer.

### In scope

- copied install directory;
- copied licence;
- copied ProgramData activation files;
- manually created or modified licence;
- unauthorised Microsoft account;
- ordinary peripheral and network-adapter changes;
- offline startup and Windows restart;
- accidental corruption and recovery.

### Out of scope / accepted limitations

- a determined local administrator patching IL/native code or replacing an unsigned executable;
- theft of the owner signing key or compromise of the owner Windows account;
- a forensic clone containing Windows DPAPI master keys;
- immediate revocation of a perpetual licence on a permanently offline PC;
- preventing execution inside a fully cloned/controlled virtual machine.

The design provides strong practical protection against casual copying and ordinary employee misuse. It is not claimed to be unbreakable DRM.

## 5. Proposed architecture

```text
Store PC                                      Owner-controlled PC
────────                                      ───────────────────
ActivationView                                ETP Licence Administrator
  → ActivationCoordinator                       → MSAL owner sign-in
  → IDeviceIdentityProvider                     → OwnerAuthorizationPolicy
  → IActivationRequestService      request       → LicenceIssuanceService
  → ILicenceImportService          ───────────▶  → Windows CNG private key
  → ILicenceValidationService      ◀───────────  → signed licence + history
  → IActivationStateStore          licence
          │
          ▼
%ProgramData% machine-protected state
          +
embedded trusted public-key ring
```

The store application has no licence-signing method and no Microsoft credential collection. The owner administrator is built and delivered separately and is excluded from store release artifacts.

## 6. Microsoft owner authentication

### D. Authentication architecture

- Library: supported MSAL.NET `Microsoft.Identity.Client` 4.x selected at implementation time.
- Client type: public desktop client; no client secret.
- Browser: official system-browser or Web Account Manager flow; never an embedded password form.
- Registration audience: `AzureADandPersonalMicrosoftAccount`, supporting organizational accounts and personal accounts such as Outlook.com.
- Authority: `common` for the selected audience. If the final allowlist contains personal accounts only, `consumers` may be selected during the implementation validation gate.
- Redirect: MSAL `.WithDefaultRedirectUri()` and the matching Mobile and desktop application registration. For current .NET desktop/system-browser guidance this resolves to `http://localhost`; WAM configuration is validated separately when implemented.
- Requested identity scopes: `openid profile`. No mail, contacts, calendar, files or Graph data calls.
- Parent window: the owner utility supplies its WPF window handle to interactive authentication.
- MFA: enforced by Microsoft when configured on the owner account.
- Token retention: memory only for the issuance session by default. No normal ETP startup token cache. If a persistent owner-utility cache is later approved, use `Microsoft.Identity.Client.Extensions.Msal` protected for the current user.

### Owner identity key

Authorization uses a trusted pair:

```text
OwnerIdentityKey = normalized(tid) + "/" + normalized(oid)
```

`tid` and `oid` come from the MSAL-validated identity result. `preferred_username`, `name` and email are display-only and are never the sole authorization anchor. The owner allowlist is provisioned only on the owner administration computer, protected for its Windows user, and is never editable by store users.

An issuance requires:

```text
MSAL authentication succeeded
AND token/client/authority context is the registered ETP owner application
AND (tid, oid) exists in ApprovedOwners
AND authentication is recent for the issuance operation
```

The recommended recent-authentication window is ten minutes. Cancellation, authentication failure and unauthorized owner are distinct results.

## 7. Signed offline licence

### E. Envelope

File extension: `.etplicence`
Product identifier: `SAAGAR-ETP-REPORTING-WINDOWS`

```json
{
  "schemaVersion": 1,
  "keyId": "prod-2026-01",
  "algorithm": "ES256-P1363",
  "payload": "base64url(UTF-8 canonical payload)",
  "signature": "base64url(64-byte signature)"
}
```

The verifier signs/verifies the exact decoded payload bytes before parsing them. It never parses and reserializes attacker-controlled payload data before signature verification.

### Canonical payload

Property order is fixed as listed below. Encoding is UTF-8 without BOM or whitespace. Strings use JSON escaping, arrays use ordinal-sorted unique values, timestamps use UTC `yyyy-MM-dd'T'HH:mm:ss'Z'`, and optional values are explicit `null`.

```json
{
  "schemaVersion": 1,
  "licenseId": "LIC-000001",
  "productId": "SAAGAR-ETP-REPORTING-WINDOWS",
  "storeId": "WLMHW",
  "deviceId": "ETP-A2F4-B7AC-74D2",
  "installationId": "00000000-0000-0000-0000-000000000001",
  "issuedAtUtc": "2026-08-27T12:00:00Z",
  "licenseType": "PerpetualDevice",
  "features": [],
  "expiresAtUtc": null,
  "replacementFor": null
}
```

### Cryptography

- ECDSA using NIST P-256;
- SHA-256;
- IEEE P1363 fixed-field concatenation signature encoding (64 bytes);
- key identifier in the envelope for rotation;
- public keys encoded as SubjectPublicKeyInfo and compiled into a read-only trusted-key ring;
- fixed-time comparisons for derived identifiers and hashes where relevant;
- `RandomNumberGenerator.Fill/GetBytes` for installation secrets, nonces and request identifiers.

No custom cryptography, MD5, SHA-1, XOR, base64-only protection or shared signing secret is permitted.

### Validation order

1. enforce file-size limit and strict JSON depth/property rules;
2. validate envelope version, algorithm and known `keyId`;
3. strict base64url decode;
4. verify ECDSA signature over raw payload bytes;
5. parse payload with duplicate/unknown security-property rejection;
6. validate schema, product and licence type;
7. load and unprotect activation state;
8. recompute device ID from the protected installation secret;
9. compare installation ID, device ID, store binding and licence hash;
10. apply licence policy and return an explicit status.

Any uncertain result fails closed to the activation/recovery screen.

## 8. Device and installation identity

### F. Identity definition

The licence binds to a **Windows installation**, not a fragile list of peripherals.

On first activation preparation ETP creates:

- `installationId`: random RFC 4122 GUID;
- `installationSecret`: 32 cryptographically random bytes;
- `deviceId`: human-safe Base32 representation of the first 80 bits of
  `HMACSHA256(installationSecret, "SAAGAR-ETP-REPORTING-WINDOWS" || installationId)`;
- `requestNonce`: 16 random bytes per activation request.

Example display: `ETP-A2F4-B7AC-74D2`. Raw hardware serials are never displayed or logged.

### Machine protection

The installation secret and activation binding record are protected with Windows DPAPI `ProtectedData` using `DataProtectionScope.LocalMachine` plus application-specific optional entropy. `LocalMachine` is required because several approved Windows users may run the store application. Because any sufficiently privileged local process can potentially invoke machine-scope DPAPI, file ACLs and the accepted local-admin limitation remain material.

Storage root:

```text
%ProgramData%\Saagar Traders\ETP Reporting Engine\Licensing\
  activation.dat       # DPAPI-protected state
  licence.etplicence   # signed public licence envelope
```

The installer creates the directory and a protected DACL. `SYSTEM` and local Administrators receive full control. Approved application users receive only the minimum read/write rights required for activation and runtime validation; ordinary inherited broad-write access is removed. The exact trustee strategy is validated on the target store-user model before rollout.

The install directory contains neither file. Copying `{app}` therefore omits activation state. Copying ProgramData state to another Windows installation causes DPAPI unprotect to fail.

### Hardware-change policy

| Change | Expected result |
|---|---|
| monitor, printer, keyboard, mouse, USB device | remains activated |
| RAM, network adapter or IP change | remains activated |
| motherboard change without Windows reinstall | normally remains activated if DPAPI state survives |
| Windows reinstall | new activation required |
| system-drive replacement/reimage | new activation required unless the entire OS/DPAPI state is restored; cloned-state limitation applies |
| new PC | new activation required |

No MAC address, disk serial or motherboard serial is a sole or mandatory identity component.

## 9. Activation request

File extension: `.etpactivation`

```json
{
  "schemaVersion": 1,
  "requestId": "00000000-0000-0000-0000-000000000002",
  "productId": "SAAGAR-ETP-REPORTING-WINDOWS",
  "storeId": "WLMHW",
  "deviceId": "ETP-A2F4-B7AC-74D2",
  "installationId": "00000000-0000-0000-0000-000000000001",
  "createdAtUtc": "2026-08-27T11:55:00Z",
  "applicationVersion": "1.8.3",
  "requestNonce": "base64url(16 random bytes)"
}
```

The request is not an authorization credential. It may be copied or shared with the owner. The owner utility validates syntax, product and freshness, displays store/device details, and requires explicit owner confirmation after Microsoft authentication. A modified request cannot bypass owner authorization; it only changes what the owner is being asked to approve.

## 10. End-to-end sequences

### G. First activation

```text
Install ETP and SQL prerequisites
→ ETP finds no valid activation
→ ActivationView creates/loads machine-protected installation identity
→ owner/store manager selects the intended store
→ export or copy activation request
→ request is transferred to owner-controlled PC
→ owner utility signs in through Microsoft/MSAL
→ (tid, oid) allowlist passes
→ owner reviews request and store
→ owner utility signs licence with CNG private key
→ issuance history is written
→ licence returns to store PC
→ ETP imports to ProgramData
→ signature + device + installation + store validation pass
→ protected activation binding is committed atomically
→ normal ETP access begins
```

### H. Offline startup

```text
App starts
→ load protected activation state
→ verify licence signature using embedded public key
→ recompute and compare device/installation binding
→ validate product/store/policy
→ Activated
→ open MainWindow or run licensed automation
```

No MSAL, Microsoft API, token refresh, SQL query or network request occurs in this path.

### Replacement

A replacement PC creates a new installation and request. The owner authenticates and issues a licence whose `replacementFor` references the prior licence. The old offline licence cannot be remotely disabled; the history marks it replaced and operational policy requires retirement/recovery of the old device where possible.

## 11. Private-key custody

### I. Exact location

Production signing occurs only on a dedicated owner-controlled Windows account/computer. The live key is a Windows CNG persisted ECDSA P-256 key named conceptually:

```text
Saagar.ETP.Licensing.prod-2026-01
```

The imported operational key should be non-exportable and usable only by the owner Windows account. Initial key ceremony creates one password-encrypted PKCS#8 escrow copy, stored on two encrypted offline removable media in separate controlled locations. The escrow password is stored outside computers and outside Codex/Obsidian/GitHub.

The private key will **not** exist in:

- ETP application binaries or installer;
- store computers or SQL Server;
- source control, CI variables or release artifacts;
- project documentation, Graphify, CRG or Obsidian;
- logs, support packages, backups or licence files.

The public key is intentionally distributable. A trusted-key ring supports future rotation; removing a public key requires an explicit compatibility decision for licences signed by that key.

## 12. Application contracts and ownership

### J. Intended additions at implementation time

```text
src/Etp.Reporting.Domain/Licensing/
  LicenceStatus.cs
  LicenceDescriptor.cs
  ActivationRequest.cs

src/Etp.Reporting.Application/Licensing/
  ILicenceValidationService.cs
  IDeviceIdentityProvider.cs
  IActivationStateStore.cs
  IActivationRequestService.cs
  ILicenceImportService.cs
  ILicensingAuditSink.cs
  ActivationCoordinator.cs

src/Etp.Reporting.Infrastructure.Windows/       # new justified OS adapter project
  Licensing/DpapiActivationStateStore.cs
  Licensing/WindowsDeviceIdentityProvider.cs
  Licensing/EcdsaLicenceVerifier.cs
  Licensing/ProgramDataLicensingPaths.cs

src/Etp.Reporting.Desktop/Modules/Licensing/
  ActivationWindow.xaml(.cs)
  ActivationViewModel.cs
  LicenceSettingsView.xaml(.cs)

tools/Etp.Reporting.LicenceAdmin/               # owner-only, separate release
  OwnerAuthenticationService.cs
  OwnerAuthorizationPolicy.cs
  EcdsaLicenceSigner.cs
  LicenceAdministrationRepository.cs
  MainWindow.xaml(.cs)
```

The owner administrator is not referenced by Desktop and is excluded from `artifacts/windows-release` and the Inno Setup store package.

### Conceptual APIs

```text
ILicenceValidationService.GetStatusAsync()
ILicenceValidationService.ValidateAsync()
IDeviceIdentityProvider.GetOrCreateAsync()
IActivationRequestService.CreateAsync(storeId)
ILicenceImportService.ValidateAndImportAsync(path)
IActivationStateStore.LoadAsync()/CommitAsync()
ActivationCoordinator.PrepareRequestAsync()/ImportAsync()/RecoverAsync()
```

All return typed results. Expected validation failures are statuses, not raw cryptographic exceptions.

### Statuses

```text
Activated
NotActivated
LicenceMissing
InvalidSignature
WrongDevice
CorruptActivation
UnsupportedLicenceVersion
ProductMismatch
StoreMismatch
Expired
AuthenticationCancelled
AuthenticationFailed
UnauthorizedOwner
```

Authentication statuses exist only in the owner utility. Store runtime statuses never imply Microsoft login is required for daily use.

### K. Intended modifications

- `App.xaml.cs`: composition and single startup gate; no licence algorithm code.
- Desktop project/solution: reference Application and Windows infrastructure; host activation views.
- Settings navigation: add Owner-visible Licence status/recovery screen.
- installer: create ProgramData licensing directory/ACL and explicitly exclude owner utility/key artifacts.
- operational audit: add licensing event types or an adapter that records aggregate-only events.
- release acceptance and support packaging: verify no private/auth material is included.
- `.gitignore` and scan scripts: reject private keys, licences, activation blobs and owner-admin data.

`MainWindow.xaml.cs` receives no `CheckLicense`, device, MSAL or signature logic.

## 13. Audit events

Required events:

```text
OWNER_AUTH_SUCCESS          # owner utility only
OWNER_AUTH_DENIED           # owner utility only
ACTIVATION_REQUEST_CREATED
LICENCE_IMPORTED
LICENCE_VALID
LICENCE_INVALID
DEVICE_MISMATCH
ACTIVATION_COMPLETED
ACTIVATION_REPLACED
```

Never record passwords, access/refresh/ID tokens, private keys, plaintext installation secrets, raw DPAPI data or raw hardware identifiers. Store-side SQL audit receives event/outcome and aggregate-safe detail only. Pre-database activation events use a bounded local security log and are forwarded without confidential values when SQL becomes available.

## 14. Activation and administration UI contract

### Store ActivationWindow

The activation experience is a dedicated screen opened instead of MainWindow:

```text
ETP Reporting Engine
This computer is not activated.

Status: Activation required
Store:  [approved store selector]
Device ID: ETP-A2F4-B7AC-74D2   [Copy]

[Create / Export Activation Request]
[Import Signed Licence]
[Activation Help]
[Exit]
```

The store screen does not contain a Microsoft password field or a local licence-generation button. It explains that the owner authorizes the request using the separately controlled ETP Licence Administrator.

After import, show signature, device, store and activation checks independently. Only a fully valid result enables **Continue to ETP**.

### Settings → Licence

Owner-visible application settings show:

- status and safe explanation;
- licence ID, store, device ID and issued date;
- licence type and feature list;
- create replacement request;
- export public diagnostic summary;
- open licensing help.

It never reveals the installation secret, DPAPI blob, Microsoft token or raw owner identity claims.

### Owner Licence Administrator

```text
Owner status: Not signed in / Authorized owner
[Sign in with Microsoft]
[Import Activation Request]

Request review:
Store | Device ID | Installation ID suffix | app version | request time

[Issue New Licence]
[Issue Replacement Licence]
[View Issuance History]
```

The signing confirmation must display the exact store, device ID, licence type and replacement relationship. Signing is disabled until recent approved-owner authentication succeeds.

### Error text

| Status | User-facing message |
|---|---|
| `LicenceMissing` | This computer has not been activated. Create an activation request for the owner. |
| `InvalidSignature` | The imported licence could not be verified. Request a new licence from the owner. |
| `WrongDevice` | This licence was issued for another computer. |
| `StoreMismatch` | This licence was issued for a different store. |
| `CorruptActivation` | The protected activation state is damaged or unavailable. Owner-authorized recovery is required. |
| `UnsupportedLicenceVersion` | This licence format is not supported by this ETP version. |
| `AuthenticationCancelled` | Microsoft sign-in was cancelled. No licence was issued. |
| `AuthenticationFailed` | Owner sign-in could not be completed. Check internet access and retry. |
| `UnauthorizedOwner` | This Microsoft account is not authorized to issue ETP licences. |

Internal exception names, claims, key IDs and cryptographic diagnostics are confined to redacted technical logs.

### Accessibility and shortcuts

- full keyboard traversal and visible focus;
- screen-reader names for status, store selector, device ID and every action;
- `Alt+Left` returns only where it cannot bypass activation;
- `Ctrl+C` copies the focused safe device/request code;
- `Ctrl+O` opens the licence-import dialog;
- `Ctrl+E` exports an activation request;
- `F1` opens activation help;
- Escape never dismisses activation into normal application access.

## 15. Performance and reliability

- target warm startup validation: below 100 ms on supported store hardware;
- no network, SQL or hardware enumeration in normal validation;
- maximum licence/request file size: 64 KiB;
- bounded JSON depth and string/array lengths;
- atomic write-through temporary file plus replace for activation state/licence;
- reject reparse points and unexpected file types;
- single-process activation lock prevents concurrent import/state corruption;
- validation service is deterministic and cancellation-aware;
- invalid activation opens recovery UI and never crashes the process.

## 16. Guardrails

- **LIC-001:** MainWindow does not own licence logic.
- **LIC-002:** Production private signing key never ships with ETP.
- **LIC-003:** New/replacement issuance requires authorized Microsoft owner authentication.
- **LIC-004:** Ordinary offline use never requires Microsoft authentication.
- **LIC-005:** Every licence is cryptographically signed.
- **LIC-006:** Every licence is bound to its intended installation identity.
- **LIC-007:** Activation state uses Windows machine-protected storage plus restrictive ACLs.
- **LIC-008:** Licence verification is centralized before operational access.
- **LIC-009:** Microsoft identity, signature validity and device validity remain independent decisions.
- **LIC-010:** A copied installation is unlicensed unless machine-specific validation succeeds.
- **LIC-011:** Store builds contain verification capability only, never issuance capability.
- **LIC-012:** SQL availability or application roles cannot bypass the startup licence gate.
- **LIC-013:** Development enforcement remains disabled until the explicit final integration phase.

## 17. Risk assessment

### L. Risks

| Risk | Rating | Control |
|---|---|---|
| private key loss/compromise | High | controlled key ceremony, non-exportable operational key, two offline encrypted escrow copies, key rotation plan |
| unsigned executable patched to bypass checks | High | centralized gate, later obfuscation review, hashes/release verification; Authenticode remains a separate future decision |
| full OS/VM clone carries DPAPI state | High | documented limitation; evaluate TPM-bound key in a later hardening phase if threat justifies it |
| owner Microsoft account compromise | High | Microsoft MFA, small immutable allowlist, recent-auth requirement, issuance history |
| incorrect allowlisted identity | High | two-person verification during provisioning, display and record `(tid, oid)` fingerprint |
| DPAPI LocalMachine accessible to privileged local processes | Medium | protected ProgramData DACL, least-privilege users, accepted local-admin boundary |
| activation blocks legitimate store after corruption | Medium | typed recovery statuses, owner-authorized replacement, backup of public licence/history—not secret reuse |
| offline replacement cannot revoke old licence | Medium | replacement history and operational device retirement; no claim of remote revocation |
| fragile hardware fingerprint causes false deactivation | Low | random installation identity; no peripheral serial dependency |
| Microsoft outage during daily work | Low | Microsoft is absent from normal startup/runtime |
| licence format/parser abuse | Medium | size/depth limits, strict schema, signature-before-parse, fuzz/property tests |

### M. Limitations

A determined reverse engineer with local administrator control can patch an unsigned executable, intercept the validation result, or reproduce a fully cloned machine. DPAPI machine scope is not a hardware security module. Perpetual offline licences cannot be remotely revoked. The architecture materially raises the cost of casual copying but does not promise absolute copy protection.

## 18. Security-adjacent findings kept separate

- SQL integrated security is correctly preferred, but current settings remain MainWindow-owned and should move to a Desktop configuration service.
- The current operational audit event allowlist will need explicit licensing events or an adapter.
- Connection strings are stored in LocalAppData only when integrated security is used; licensing state must instead be application-wide ProgramData.
- Installer execution is elevated and suitable for directory ACL creation.
- Application licence signing is unrelated to Windows Authenticode signing. ETP can verify its own licence without a commercial code-signing certificate, while Windows may still display an unknown-publisher warning.

## 19. Deferred owner inputs

These values are intentionally collected only at final implementation:

1. Microsoft App Registration client ID;
2. final supported account audience (`common` or personal-only `consumers`);
3. approved owner `(tid, oid)` identifiers;
4. initial production `keyId` and completed key ceremony;
5. authoritative store IDs and store-transfer approval procedure;
6. Windows trustees/local group for ProgramData ACLs;
7. owner-administrator computer and escrow custodians;
8. whether a TPM-bound enhancement is required after copy-attack testing.

No email address, password, key or token should be entered into repository documentation.

## 20. Verified primary guidance

Microsoft guidance checked on 2026-08-27:

- MSAL public-client configuration and redirect URI: https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-application-configuration
- WPF/desktop app configuration: https://learn.microsoft.com/en-us/entra/identity-platform/scenario-desktop-app-configuration
- supported account types: https://learn.microsoft.com/en-us/entra/identity-platform/supported-accounts-validation
- OIDC scopes: https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc
- identity claims: https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference
- MSAL token cache guidance: https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization
- .NET cryptography model: https://learn.microsoft.com/en-us/dotnet/standard/security/cryptography-model
- Windows DPAPI scope: https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope
- Windows ACL model: https://learn.microsoft.com/en-us/windows/win32/secauthz/access-control-lists

## 21. Implementation hold point

Do not implement runtime licensing until the owner explicitly declares the functional product complete and authorizes the licensing integration phase. At that point follow `ETP_LICENSING_IMPLEMENTATION_BACKLOG.md` sequentially. Startup enforcement is the final phase, not the first.
