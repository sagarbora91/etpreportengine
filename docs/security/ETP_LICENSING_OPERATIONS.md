# ETP licensing key ceremony and operations

Status: procedure designed; do not execute until final licensing implementation is authorized

## Roles

| Role | Responsibility |
|---|---|
| Business owner | approves owner identities, stores, issuance, replacement and revocation policy |
| Key custodian | performs controlled key ceremony and safeguards escrow |
| Licence administrator | authenticates and issues licences on the dedicated owner PC |
| Store manager | creates requests and imports returned licences; cannot issue |
| Support operator | diagnoses safe statuses and coordinates owner recovery; cannot access secrets |

One person may initially hold several roles, but each action must remain explicit and auditable.

## Production key ceremony

Perform offline on the selected owner-administration Windows account:

1. verify the owner PC is patched, access-controlled and free of unapproved remote access;
2. record the application version, utility hash, date and participants;
3. generate an ECDSA P-256 key using Windows/.NET cryptography—not an online generator;
4. assign a non-secret key ID such as `prod-2026-01`;
5. export one password-encrypted PKCS#8 escrow copy during the ceremony;
6. write two identical escrow copies to encrypted removable media;
7. verify each escrow copy can be read and matches the public key using an isolated test environment;
8. store the two copies in separate controlled physical locations;
9. import the operational key into the owner account's Windows CNG key store as non-exportable;
10. securely remove working plaintext/export files and verify no key entered logs, clipboard history, repository, cloud sync or support folders;
11. export only the public SubjectPublicKeyInfo for the trusted verification-key ring;
12. record ceremony evidence without key bytes or escrow password.

The escrow password is recorded offline and never in Codex, Obsidian, email, source control or the admin database.

## Initial owner enrollment

1. configure the approved Microsoft app registration;
2. authenticate each intended owner through official Microsoft UI;
3. record the validated `(tid, oid)` fingerprint in owner-only protected configuration;
4. have the business owner verify the account identity independently;
5. test an unapproved account and confirm authorization denial;
6. enable MFA on every approved owner account.

Do not provision by typing an email into a store-side configuration file.

## New-device issuance

1. store manager generates a `.etpactivation` request;
2. transfer it through an approved channel—the request contains no secret but still identifies a store/device;
3. licence administrator imports it on the owner PC;
4. utility validates format/product/freshness and displays exact store/device details;
5. owner signs in with Microsoft and passes allowlist policy;
6. owner confirms intended store and device out-of-band where appropriate;
7. utility allocates a unique licence ID, signs once and writes issuance history;
8. return `.etplicence` to the store manager;
9. store manager imports it and verifies Activated status;
10. test an offline restart and retain redacted evidence.

## Replacement

1. create a request on the replacement Windows installation;
2. identify the prior licence from administration history;
3. require fresh owner authentication;
4. issue a new licence with `replacementFor` set to the prior licence ID;
5. mark the prior record `Replaced` in owner history;
6. retire/wipe/recover the old PC where possible;
7. acknowledge that a permanently offline old installation cannot be remotely revoked.

## Store transfer

A store change is never an editable local setting. Create a new request, require fresh owner authorization and issue a new licence for the new store. Record the former licence as replaced.

## Key rotation

Planned rotation:

1. perform a new key ceremony with a new key ID;
2. add the new public key to ETP's trusted-key ring;
3. release that verifier before issuing licences with the new key;
4. retain the old public key while old licences must remain valid;
5. move signing to the new CNG key and protect its escrow;
6. retire the old private operational key after the compatibility window.

Emergency compromise:

1. stop issuance immediately;
2. preserve owner-PC and audit evidence;
3. remove/disable compromised owner accounts;
4. create a new key under incident procedure;
5. release an ETP version that distrusts the compromised key if operationally possible;
6. reissue legitimate licences;
7. because offline PCs cannot receive revocation instantly, document the residual exposure.

## Lost key

If the operational key is lost but uncompromised, restore from one verified encrypted escrow copy into a new controlled CNG key. If all copies are lost, create a new key/version and reissue licences. Never attempt to reconstruct or copy key material through chat, email or source control.

## Licence administration history

Owner-local history fields:

```text
LicenseId
StoreId
DeviceId
InstallationId
IssuedAtUtc
IssuedByTenantId
IssuedByObjectId
Status
ReplacementFor
Reason
PayloadSha256
```

History is stored under the owner account, backed up encrypted and excluded from store releases. Do not store tokens, passwords or private-key bytes.

## Support and diagnostics

Store support packages may include:

- application/licence schema version;
- safe status code;
- licence ID and device ID only when explicitly approved for support;
- public licence hash;
- file existence/ACL/DPAPI operation outcome without blob contents;
- application/Windows version and timestamps.

They must exclude licence file contents by default, activation blobs, plaintext secrets, tokens, owner claims, private keys and admin history.

## Release evidence

The first licensed release is not accepted until:

- key ceremony evidence is approved;
- owner auth/MFA and unauthorized-account tests pass;
- PC-A works after internet disconnection and reboot;
- PC-B fails install-only, licence-copy and full-state-copy cases;
- owner utility is absent from the store installer;
- secret scan and support-package inspection pass;
- import, SQL and report goldens remain unchanged.
