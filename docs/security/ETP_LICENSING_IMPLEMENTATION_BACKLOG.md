# Deferred licensing implementation backlog

Status: **HOLD — execute only after explicit product-complete authorization**

The engineering design is complete in `ETP_LICENSING_ENGINEERING_SPEC.md`. This backlog is deliberately not active product work. Each phase is a separate reversible commit and must pass its gate before the next phase begins.

## Phase 0 — implementation revalidation

- confirm product startup, installer and project boundaries have not changed;
- recheck current MSAL, .NET cryptography and Windows DPAPI documentation/package support;
- collect only the deferred owner inputs listed in the engineering specification;
- run the full existing baseline suite and preserve representative report/import outputs;
- threat-model review and owner approval.

Gate: no secrets collected into repository; baseline green; architecture report updated.

## Phase 1 — pure licensing core

- add Domain licence/status/request records;
- add strict bounded envelope/payload parsers and canonical writer;
- implement public-key ring and ECDSA P-256/SHA-256/P1363 verification;
- add deterministic and tamper/fuzz/property tests;
- do not alter startup.

Gate: cryptographic tests green; store assemblies contain no signer/private-key import path.

## Phase 2 — Windows installation identity

- add justified Windows infrastructure project;
- implement random installation identity and derived human device ID;
- implement DPAPI LocalMachine state, reparse rejection, ACL validation and atomic files;
- test multiple intended local users, corruption and cross-machine copy;
- do not alter startup.

Gate: two-machine DPAPI test passes; peripheral changes do not alter device ID.

## Phase 3 — activation request and import

- implement request creation/export/copy;
- implement licence import, signature/binding/policy validation and atomic commit;
- implement typed recovery statuses and privacy-safe audit adapter;
- add ActivationView/Settings Licence UI behind an engineering-only route;
- do not block normal startup.

Gate: request/import/tamper tests and UI smoke pass.

## Phase 4 — owner licence administrator

- create a separately built owner-only WPF utility;
- add MSAL public-client system-browser/WAM authentication;
- add `(tid, oid)` owner authorization and recent-auth policy;
- perform controlled key ceremony and CNG integration outside source control;
- implement signer and owner-local issuance/replacement history;
- enforce release separation so the utility/key can never enter store artifacts.

Gate: approved/unapproved/MFA/no-network flows pass; binary/package inspection proves separation.

## Phase 5 — end-to-end activation

- connect owner-authenticated issuance to request review and signed licence output;
- exercise first activation and replacement on disposable test PCs;
- complete all attack tests except production startup enforcement;
- document operator ceremony and recovery.

Gate: PC-A succeeds offline; PC-B copy cases fail.

## Phase 6 — startup enforcement

- compose `ILicenceValidationService` in `App`;
- permit only explicitly approved installer initialization before activation;
- gate interactive MainWindow and `--automation-once` centrally;
- fail closed to ActivationWindow/recovery;
- do not add checks to individual reports/import handlers.

Gate: all startup/offline/automation tests and full regression suite pass.

## Phase 7 — installer, operations and release

- create ProgramData licensing location and DACL;
- exclude owner utility, admin database, private/escrow keys and tokens from store installer/support package;
- update upgrade/uninstall policy without silently deleting licence evidence;
- run installer/upgrade/uninstall and two-machine final acceptance;
- evaluate obfuscation only after functional acceptance.

Gate: signed release evidence approved by owner. Authenticode remains a separate decision.

## Permanent stop conditions

Stop immediately if:

- any private key/token/password enters the repository, logs or support artifacts;
- licence generation becomes reachable from store binaries;
- Microsoft login is introduced into daily startup;
- SQL or a MainWindow handler becomes licensing authority;
- a copied PC-B installation reaches normal functionality;
- report/import/database outputs diverge from baseline.

## Commit sequence

```text
feat(licensing): add signed licence verification core
feat(licensing): add Windows installation identity storage
feat(licensing): add activation request and import workflow
feat(licensing-admin): add owner authentication and issuance utility
feat(licensing): connect activation coordinator
feat(desktop): enforce central startup licence gate
build(installer): provision protected activation storage
test(licensing): add two-machine and release acceptance evidence
```

No phase may be collapsed into a big-bang commit.
