# Phase 5 ETP synthetic physical-device acceptance — SM-T875

**Date:** 2026-08-23  
**Status:** PASS — final ETP validation, publication, readback and cold-relaunch persistence completed  
**Device:** Samsung SM-T875, Android 13 / API 33, serial `R52N807PTTE`  
**Application:** `com.saagartraders.bcc`, version 2.9, version code 209  
**APK:** `SaagarCC-C1-DemoData-2Years-v2.9.apk`, 7,218,076 bytes  
**APK SHA-256:** `5cea95d2461810b1e318efaa359f2c6a169edee8a26ed43164b1c87eaf3f5fed`  
**Fixed product commit:** `d5a96dbf6f938249d1cff5ca4a79968b0c299c22`  
**Prior evidence checkpoint:** `5364df211d39f84ac786523e78dc93a578e033e3`

## Scope and non-claims

This run used deterministic synthetic-only WLMHW fixtures for 2026-08-21. It is physical-device engineering evidence for the final ETP workflow only. It is not production data, staff UAT, API-23 acceptance, low-storage acceptance, production signing, or independent release approval. No Owner PIN or other secret is recorded in this evidence.

## Defect remediation

1. **ETP-DEV-01 fixed.** ETP import now accepts the active Owner session or requests bounded in-module reauthentication after Android file selection. Publication always requests fresh, action-bound reauthentication. Approval does not grant a persistent Owner session.
2. **ETP-DEV-02 fixed.** The import worker no longer mutates the frozen loader result. It creates and freezes a new report object containing the source and header-signature hashes.
3. **Regression tests passed.** The complete ETP test selection passed 173/173. Focused gateway and worker tests include asynchronous bounded authorization and a frozen-loader-result regression using Web Crypto.

## Fixture identities

| Report | File | SHA-256 | Bytes |
|---|---|---|---:|
| R003 | `W003_All_Discount_Type.xlsx` | `5a526e3b4eaa8ce10b58ddd7e03d345c627f6949563c15b3c072edba14cdb8f1` | 6,170 |
| R013 | `W013_CRO_Wise_Sales.xlsx` | `ede30c2b0db9d7ac6ddc28a9587a5d370069d6a8e49477a41cd61c3ed1962161` | 5,484 |
| R022 | `W022_Revenue_Report.xlsx` | `eb294cc8c961e0b0079935a3ed029bcd95450e3a9d1674c6ae4035537c05a163` | 6,807 |
| R025 | `W025_SDB-VariantwiseSales.xlsx` | `57979fb2d27844561501b34d432381c287b70f1c9818cb3752b55928ad1d4f7a` | 6,570 |

## Final device results

1. **PASS — updated installation.** The updated APK installed in place through ADB and launched successfully on the SM-T875.
2. **PASS — provider selection and worker validation.** Samsung DocumentsUI selected the exact four fixtures. The real worker completed parsing, hashing, validation and reconciliation without `ETP_WORKER_FAILED`.
3. **PASS — bounded authorization.** Import recovered through the supported authorization path after file selection. Confirmation displayed a fresh action-bound prompt for `publish verified Retail ETP reports`.
4. **PASS — native publication.** Logcat records `SaagarEtpStore.finishStage` followed by `SaagarEtpStore.publishStage` for generation `etp_2812bed8bc16ff3dc6dce7634571573c`, scope `WLMHW|2026-27|2026-08-21..2026-08-21`, and the exact four fixture hashes above.
5. **PASS — immediate readback.** ETP reported one verified scope. R022 and R025 each showed one verified row, signed net INR 100.00 and signed quantity 1. Coverage & History showed the scope published on 2026-08-23.
6. **PASS — reconciliation and exception posture.** Blocking reconciliation `rec_002_v1` passed. R003 and R013 enrichment checks each passed with zero differences. PAYMENTTYPE25 remained explicitly quarantined with zero unresolved rows.
7. **PASS — cold-relaunch persistence.** After `am force-stop` and a launcher cold start, the same verified projections, coverage history, reconciliation state and quarantine posture were read back from native storage.
8. **PASS — stability.** Final device logs contained no matching `FATAL EXCEPTION`, application ANR, `ETP_WORKER_FAILED`, or `ETP_ACCESS_DENIED` event.

## Evidence hashes

| Evidence | SHA-256 |
|---|---|
| `etp-fixed-reauth-prompt.png` | `b485a3f67f4fa3cbe11c8e46c6e4f1118bc1d165398fb153b3b08b37608f8db1` |
| `etp-fixed-confirm-reauth.png` | `8a2d871a723ad4c95c841c233ff9a1b09949c1a0db30e6ae7f0ce60947363544` |
| `etp-fixed-publication-logcat.txt` | `45a3c43824a3a626e9228210945facec2396451beb9b2ce9146a336706401032` |
| `etp-fixed-readback-verified.png` | `5080358675a87a50c08946ed336a36c27103ebf55969960af9645ab2aa88dbb6` |
| `etp-fixed-readback-coverage.png` | `cd899c2ed7466fd490d8f8897f8c83906206b1c43ffc5e66d9bd93476822f572` |
| `etp-fixed-readback-reconciliation.png` | `32f9cefadd76f751b62b59ccb030cd93661dbe85b7c6f79cda5e153eb76e3009` |
| `etp-fixed-cold-readback-verified.png` | `214373f96c7b426b91fc93d25aac7f1fc12339b885801b8da2139ff740b86701` |
| `etp-fixed-cold-readback-coverage.png` | `781da4625c802ee2d457aa5454ef712915409dfeb92db6ad2fb081b4acddda2c` |
| `etp-fixed-cold-readback-reconciliation.png` | `642f42295e244d55ffba38222bcbb2c44f4759cea413bc4e058b1cf0ae852609` |

The hashed captures and logs are retained outside the repository at `V:\Co work\Projects\Retail\.audit-drafts` to avoid committing device logs or transient screenshots. The synthetic fixture workspace remains uncommitted.
