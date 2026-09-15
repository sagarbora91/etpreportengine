# V6 ETP continuous-sprint engineering release evidence — 2026-08-25

**Evidence time:** 2026-08-25 15:59 IST
**Branch:** `agent/modular-phase1-shared-spine-v2`
**HEAD at build:** `9ea88f4d73bb2e51c0e8ee00fb690ea78fc5729d`
**Status:** Engineering debug build PASS; production publication NOT AUTHORIZED

## Scope and evidence posture

This receipt covers the final local engineering checks for the V6 ETP continuous sprint. The candidate was built from a dirty integration worktree containing the sprint changes, so the APK is not a clean-commit or production-release artifact. It does not claim production signing, external authority approval, physical-device acceptance, approved multilingual content, or human staff UAT.

## Canonical V6 identity

| Field | Observed value |
|---|---|
| Display / release version name | `6` |
| Android version code | `600` |
| Release package ID | `com.saagartraders.bcc` |
| Debug package ID | `com.saagartraders.bcc.debug` |
| Built debug version name | `6-debug` |
| Minimum Android API | `23` |
| Target / compile API | `34` |

The canonical identity, Android override, generated-release verifier and release/audit fixtures all use version name `6` and monotonically advanced version code `600`.

## Final automated evidence

| Gate | Result | Detail |
|---|---:|---|
| Phase 0 identity, release-policy, source and historical controls | PASS | 72 / 72 |
| Phase 6H ETP regression | PASS | 229 / 229 core; 81 / 81 integration; 31 / 31 E3–E6 contracts |
| Governed E4–E7 completion waves | PASS | Wave 7: 22 / 22; Wave 8: 17 / 17; Wave 9: 24 / 24; Wave 10: 44 / 44 |
| Final localization/mobile synthetic UAT gate | PASS | 24 / 24 |
| Focused Phase 6B/6C/6D/6G historical reconstruction | PASS | 19 / 19 |
| Debug APK assembly | PASS | Gradle `assembleDebug`; 186 tasks; final rerun successful |

The localization and staff-UAT automation is synthetic engineering evidence only. Marathi and Hindi operational content remains explicitly test-only and unapproved.

## Debug APK artifact

| Field | Observed value |
|---|---|
| Path | `android/app/build/outputs/apk/debug/app-debug.apk` |
| Bytes | `7,262,553` |
| SHA-256 | `AC4DC86B506D72DC63B5E81DF2A56464190D037E52D4764A0CEA8DAECFD982E2` |
| Build timestamp (UTC) | `2026-08-25T10:23:47.0475584Z` |
| Manifest identity | `com.saagartraders.bcc.debug`, version `6-debug`, code `600`, min SDK `23` |
| Signature verification | PASS using APK v1 and v2 schemes |
| Certificate | Android debug certificate; SHA-256 `0b1b5a9cd08f5515b4826aa54b33233ae60ba5870704e01213163512fb871cd9` |

This APK is suitable only for engineering/debug review. The Android debug certificate is not the approved production certificate.

## Toolchain observed

- OpenJDK Temurin `17.0.19+10` from the workspace-pinned JDK.
- Android Debug Bridge `1.0.41`, platform-tools `37.0.1-15733141`.
- Android build-tools `34.0.0` used for manifest and signature inspection.
- Node.js `v24.19.0`; npm `11.17.0`.

## Device result

The workspace ADB executable started successfully, but `adb devices -l` returned an empty device list during both preflight and final verification. Therefore:

- no tablet installation was attempted;
- no launch, focused-activity, screenshot, logcat, rotation, process-death or retained-state result is claimed;
- earlier Phase 5 device evidence does not transfer to this new V6 debug APK hash.

## Production and external blockers

Production publication remains blocked by all of the following:

1. The current candidate is a dirty-worktree debug build, not a clean frozen expected commit.
2. The four process-scoped production signing inputs were absent during this run; the governed release recipe therefore remains fail-closed.
3. No production-signed V6 artifact, production receipt, exact-hash install or post-sign launch exists for this candidate.
4. Owner-approved E4 target/calendar/CRO-map/policy authorities, E6 exception policy authorities, E5 incentive/Payroll authorities and E7 Service authorities/source bindings must be supplied independently; synthetic authorities do not activate production.
5. Marathi/Hindi operational translations require native-language approval.
6. End-to-end physical staff UAT across Staff, Store Manager and Owner roles remains human work and is not replaced by the synthetic suite.
7. E7 physical Service-centre verification remains deferred until approved Service authority and verified S003/S004 source evidence exist.
8. The final Owner release decision must bind the clean commit, exact production-signed APK hash, external approvals and any explicitly carried exceptions.

## Engineering conclusion

The V6 code candidate passes the final automated engineering gates and produces a verifiable V6 debug APK. It is ready for a connected-device debug review after the tablet reappears in ADB. It is not suitable for publication until the clean-commit, production-signing, independent authority, physical-device and human-UAT gates above are completed.

## Post-build physical-device addendum

The tablet subsequently reappeared in ADB and the exact debug APK recorded in this receipt was installed as an in-place upgrade from `2.9-debug` to `6-debug`. Cold launch, native encrypted-storage recovery, ETP portrait/landscape reflow, Marathi/Hindi layout rendering and process-stop recovery passed without an application crash. Instrumented WebView measurements found no document-level horizontal overflow in either orientation. See [V6 ETP physical-device debug acceptance](../../verification/audit/V6-ETP-PHYSICAL-DEVICE-DEBUG-ACCEPTANCE-2026-08-25.md).

This addendum closes the available physical debug-engineering checks. It does not change the production-signing, approved-authority, native-language, human-UAT or Owner-publication blockers above.
