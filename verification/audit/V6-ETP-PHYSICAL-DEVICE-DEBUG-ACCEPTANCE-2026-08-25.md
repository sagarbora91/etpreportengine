# V6 ETP physical-device debug acceptance — 2026-08-25

## Acceptance boundary

This receipt records engineering checks performed on the connected physical tablet using the V6 debug APK. It proves installation, launch, responsive rendering, language switching and process recovery for the exact debug artifact below. It does not constitute production signing, native-language approval, human staff UAT, Owner authority approval or publication authorization.

## Artifact and device

| Field | Observed value |
|---|---|
| APK | `android/app/build/outputs/apk/debug/app-debug.apk` |
| SHA-256 | `AC4DC86B506D72DC63B5E81DF2A56464190D037E52D4764A0CEA8DAECFD982E2` |
| Package | `com.saagartraders.bcc.debug` |
| Installed version | `6-debug` (`versionCode 600`) |
| Upgrade source | `2.9-debug` (`versionCode 209`) |
| Install mode | ADB replace/upgrade with application data retained |
| Device | Samsung SM-T875 (`R52N807PTTE`) |
| Android | API 33 |
| Physical display | 1600 × 2560; physical density 340; observed override density 380 |
| Notification permission | Granted through the Android system permission dialog |

## Executed checks

| Check | Result | Evidence |
|---|---:|---|
| Exact APK checksum before install | PASS | SHA-256 matched the engineering release receipt |
| In-place upgrade from 2.9-debug to 6-debug | PASS | ADB install returned `Success`; package manager reported code 600 |
| Cold launch and focused activity | PASS | `MainActivity`; cold launch completed and remained focused |
| Existing encrypted storage recovery | PASS | Native incremental SQLite became active; a later cold start reported 12 encrypted records |
| Fatal exception / ANR scan | PASS | No `FATAL EXCEPTION`, `AndroidRuntime` crash or ANR found for the application process |
| Home and module catalogue layout | PASS | V6 identity visible; cards and navigation remained contained |
| Retail ETP navigation | PASS | Import, Verified Reports, Coverage & History, and Reconciliation & Exceptions tabs rendered |
| Financial-year control | PASS | Dropdown rendered on the physical tablet |
| Governed blocked states | PASS | E5 remained authority-blocked; E7 rendered `E7_AUTHORITY_DEFERRED` without borrowing Retail authority |
| Portrait ETP overflow measurement | PASS | Shell 673/673 px and ETP frame 673/673 px (`scrollWidth/clientWidth`); no overflow |
| Landscape ETP overflow measurement | PASS | Shell 1078/1078 px and ETP frame 1078/1078 px; no overflow |
| Rotation reflow | PASS | E7 fields changed from one-column portrait layout to bounded two-column landscape layout |
| Process-stop recovery | PASS | Fresh cold launch completed; encrypted native storage reopened; no crash |
| Marathi physical rendering | PASS WITH BOUNDARY | Long labels and ETP tabs stayed contained; unapproved phrases correctly remained English fallback |
| Hindi physical rendering | PASS WITH BOUNDARY | Long labels and ETP tabs stayed contained; unapproved phrases correctly remained English fallback |
| Device restored after testing | PASS | Language restored to English and user rotation mode restored to `free` |

The first launch after upgrade logged expected recovery probes for absent temporary, backup and key files before native encrypted storage became active. A subsequent clean cold launch opened native incremental SQLite without those missing-file probe messages or any crash.

## Screenshots

- [Initial V6 portrait launch](device-evidence/v6-etp-2026-08-25/01-launch.png)
- [Tablet module catalogue](device-evidence/v6-etp-2026-08-25/03-modules.png)
- [Retail ETP portrait import and financial-year dropdown](device-evidence/v6-etp-2026-08-25/04-etp-portrait.png)
- [E5/E7 governed blocked states](device-evidence/v6-etp-2026-08-25/05-etp-reconciliation-operations.png)
- [Retail ETP landscape reflow](device-evidence/v6-etp-2026-08-25/06-etp-landscape.png)
- [Cold process recovery](device-evidence/v6-etp-2026-08-25/07-process-recovery.png)
- [Marathi ETP landscape rendering](device-evidence/v6-etp-2026-08-25/12-etp-marathi-landscape.png)
- [Hindi ETP landscape rendering](device-evidence/v6-etp-2026-08-25/13-etp-hindi-landscape.png)
- [Final English home state](device-evidence/v6-etp-2026-08-25/14-final-english-home.png)

## Remaining external gates

1. Owner-approved E4, E5, E6 and E7 production authority packages and source bindings.
2. Native-language review and approval of Marathi and Hindi content.
3. Representative Staff, Store Manager and Owner human UAT using approved authority-ready scenarios.
4. Production signing inputs, a clean production-signed APK, exact-hash install verification and release receipt.
5. Final Owner publication approval.

## Conclusion

The exact V6 debug APK is physically installable and stable on the connected Samsung tablet. The tested Home, module catalogue and Retail ETP surfaces reflow without document-level horizontal overflow in portrait or landscape. This closes the available physical engineering checks for the debug candidate; production publication remains fail-closed on the external gates above.
