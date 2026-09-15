# ETP licensing security and regression test matrix

Status: designed; execution deferred with runtime implementation
Authority: `ETP_LICENSING_ENGINEERING_SPEC.md`

## Test layers

| Layer | Environment | Purpose |
|---|---|---|
| pure unit | any CI agent | canonical serialization, parsing, signature and policy |
| Windows unit/integration | Windows CI/local | DPAPI, ACLs, atomic persistence and restart |
| owner-auth contract | mocked MSAL adapter | cancellation/failure/allowlist decisions without live credentials |
| owner-auth manual | owner-controlled PC | real Microsoft/MFA/consent flow |
| two-machine attack | PC-A and independently installed PC-B | mandatory copy-resistance evidence |
| application regression | current solution/smoke/goldens | prove licensing does not change imports, SQL or reports |

## Cryptographic and format tests

| ID | Scenario | Type | Expected |
|---|---|---|---|
| LIC-T001 | approved canonical payload signed by matching P-256 key | automated | `Activated` after binding checks |
| LIC-T002 | fake random signature | automated | `InvalidSignature` |
| LIC-T003 | one-bit payload change | automated | `InvalidSignature` |
| LIC-T004 | change `storeId`, `deviceId`, `installationId`, `licenseId`, `features` or `productId` | automated/data driven | every mutation is `InvalidSignature` |
| LIC-T005 | unknown `keyId` | automated | unsupported/untrusted key status |
| LIC-T006 | wrong algorithm value | automated | rejected before crypto operation |
| LIC-T007 | DER signature supplied where P1363 is required | automated | rejected |
| LIC-T008 | invalid/overlong base64url | automated | rejected without excessive allocation |
| LIC-T009 | duplicate or unknown security property | automated | rejected |
| LIC-T010 | payload/envelope above 64 KiB or excessive depth | automated | rejected safely |
| LIC-T011 | deterministic serialization repeated across culture/time zone | automated | byte-identical payload |
| LIC-T012 | feature order/duplicate input | automated | ordinal unique canonical output |
| LIC-T013 | perpetual licence has null expiry | automated | accepted |
| LIC-T014 | future schema version | automated | `UnsupportedLicenceVersion` |
| LIC-T015 | product mismatch | automated | `ProductMismatch` |

## Device and storage tests

| ID | Scenario | Type | Expected |
|---|---|---|---|
| LIC-T100 | first identity creation | Windows integration | random installation ID/secret created once and protected |
| LIC-T101 | same machine/user restart | Windows integration | same device ID; activation remains valid |
| LIC-T102 | second approved local user | Windows integration | shared licensed application can validate under intended ACL policy |
| LIC-T103 | copied activation blob to independent PC-B | two-machine | DPAPI unprotect fails; `CorruptActivation`/`WrongDevice`; no access |
| LIC-T104 | copied licence only to PC-B | two-machine | signature valid but installation/device binding fails |
| LIC-T105 | copied entire `{app}` only | two-machine | `NotActivated`/`LicenceMissing` |
| LIC-T106 | copied `{app}` + licence + ProgramData files | two-machine | no normal access |
| LIC-T107 | delete activation state | Windows integration | activation required; no crash |
| LIC-T108 | truncate/corrupt state | Windows integration | `CorruptActivation`; recovery UI |
| LIC-T109 | symlink/reparse licensing directory or file | Windows integration | rejected |
| LIC-T110 | interrupted atomic import/write | Windows integration | prior valid state retained or safe unactivated state; never partial success |
| LIC-T111 | concurrent import attempts | Windows integration | serialized; one committed result |
| LIC-T112 | monitor/printer/keyboard/mouse change | manual | remains activated |
| LIC-T113 | network adapter/IP change | manual | remains activated |
| LIC-T114 | RAM upgrade | manual | remains activated |
| LIC-T115 | Windows reinstall/system reimage | manual | new activation required |

## Owner authentication and issuance tests

| ID | Scenario | Type | Expected |
|---|---|---|---|
| LIC-T200 | approved personal Microsoft account | live/manual | authentication and authorization succeed |
| LIC-T201 | approved work account when configured | live/manual | succeeds |
| LIC-T202 | unapproved account | mocked + live/manual | authentication valid, authorization denied |
| LIC-T203 | display email matches owner but `(tid, oid)` differs | automated | denied |
| LIC-T204 | owner cancels browser/MFA | mocked + manual | `AuthenticationCancelled`; no signing |
| LIC-T205 | no internet during issuance | mocked + manual | clear authentication-required network message; no signing |
| LIC-T206 | expired recent-auth window | automated | reauthentication required |
| LIC-T207 | correct auth but malformed request | automated | no signing |
| LIC-T208 | correct auth and new request | automated + manual | one signed licence and history record |
| LIC-T209 | replacement issuance | automated + manual | new licence references prior licence |
| LIC-T210 | utility closed after issuance | inspection | no token/private-key bytes in logs/temp/clipboard |
| LIC-T211 | Microsoft account has MFA | live/manual | Microsoft controls MFA; utility does not implement it |
| LIC-T212 | consent inspection | live/manual | only sign-in/profile scopes; no mail/files/calendar/contacts |

## Startup and offline tests

| ID | Scenario | Expected |
|---|---|---|
| LIC-T300 | valid licence + correct state | MainWindow opens |
| LIC-T301 | no licence/state | ActivationWindow opens; normal modules unavailable |
| LIC-T302 | wrong-device licence | ActivationWindow shows safe wrong-computer message |
| LIC-T303 | invalid signature | ActivationWindow shows verification failure without crypto internals |
| LIC-T304 | internet disconnected after activation | normal startup and daily operation succeed |
| LIC-T305 | Windows restart while offline | remains activated |
| LIC-T306 | Microsoft outage while activated | normal work unaffected |
| LIC-T307 | `--automation-once` without valid licence | blocked with nonzero exit and safe audit |
| LIC-T308 | `--automation-once` with valid licence | runs normally |
| LIC-T309 | installer `--initialize-database` before activation | schema bootstrap permitted; no normal product access |
| LIC-T310 | validation throws unexpected exception | fail closed to recovery, diagnostic contains no secrets |

## Mandatory final copy attack

1. Install and activate release candidate on PC-A.
2. Disconnect PC-A from the internet and prove normal startup/reporting.
3. Copy the entire installation directory to independent PC-B and run the executable.
4. Add the copied licence and retry.
5. Add copied ProgramData licensing files and retry.
6. Preserve screenshots, status codes, event logs and hashes as release evidence.
7. Pass only if PC-B never reaches MainWindow or unattended operational modes without a newly owner-authorized licence.

PC-B must be an independently installed Windows instance, not merely another user account on PC-A.

## Regression gate

Before and after each licensing phase:

- Release solution build has zero errors;
- all Domain, Import, Reporting, Desktop and SQL tests pass;
- UI smoke renders every production shell view;
- representative import produces identical staging/persistence results;
- DSR and representative Excel/PDF golden outputs retain calculations and structure;
- report archive and restatement behavior remain unchanged;
- installer lifecycle tests pass;
- support package excludes licensing secrets and Microsoft tokens.

## Evidence record

For each manual test record only:

- test ID and release version;
- Windows versions and machine labels PC-A/PC-B;
- result/status and timestamp;
- redacted screenshot/log location;
- tester and approval;
- deviations/limitations.

Do not record raw tokens, private keys, plaintext installation secrets, activation blobs or full hardware identifiers.
