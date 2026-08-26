# V6 ETP full synthetic demo APK evidence — 2026-08-25

## Outcome

A debug-only V6 demo APK was built and exercised on the connected Samsung SM-T875 tablet. The package contains synthetic demonstrations of the governed ETP E3–E7 flows while leaving the clean source profile and production package untouched.

## Safety boundary

- Package: `com.saagartraders.bcc.debug`
- Version: `6-debug` (`versionCode` 600)
- Every seeded authority and approval is labelled `DEMO`, `SYNTHETIC_DEMO_ONLY`, or `NOT-REAL`.
- The persistent shell banner states `SYNTHETIC DEMO DATA · 731 DAYS · WLMHW + HEMW · DO NOT CONTACT`.
- The clean source keeps `DEMO_SEED_ENABLED = false`; demo facades load only after the seeded-build guard.
- Retail ETP and Service ETP use separate synthetic scope and authority bindings.
- No release build, production signing, production install, publication, or production-key operation was performed.

## Included demonstrations

- E3 CRO reconciliation with governed day state and declared/verified evidence.
- E4 targets and planning with approved synthetic source authorities, CRO allocations, verified pace, gaps, projected landing, and Leave/coverage shortfall.
- E5 incentives with synthetic approved scheme authority, finalized calculations, restatement clawback, and controlled Payroll total.
- E6 exception monitoring with ownership, SLA, evidence, append-only history, and governed closure controls.
- E7 service-centre verification using an isolated `SERVICE_ETP_V1` source: three jobs, two exact matches, and one controlled discrepancy.
- English, Marathi, and Hindi operational landmarks plus Staff/Store Manager/Owner synthetic UAT coverage.
- Two stores and 731 days of synthetic cross-module demo data.

## Automated verification

The final focused suite passed 159/159 checks:

- E4 Wave 7: 22/22
- E6 Wave 8: 17/17
- E5 Wave 9: 24/24
- E7 Wave 10: 44/44
- Multilingual, mobile, and synthetic staff UAT: 24/24
- Demo boundary, seeded runtime/profile, API-23 compatibility, and manifest integrity: 28/28

`git diff --check` also passed.

## Physical-tablet verification

- Device: Samsung SM-T875, serial `R52N807PTTE`
- APK installation: successful
- Rendered E4, E5, E6, and E7 readiness states: successful
- Controlled E7 demo verification: 3 jobs, 2 exact matches, 1 discrepancy
- Portrait shell and ETP frame horizontal containment: passed
- Landscape shell and ETP frame horizontal containment: passed at a 1078 CSS-pixel viewport
- Android/Chromium fatal log check after the run: none found
- Rotation settings were restored after testing.

## Deliverable

- File: `C:\Codex\Saagar Control Centre\SaagarCC-V6-ETP-Full-Demo.apk`
- Size: 7,447,101 bytes
- SHA-256: `B2C9D2FA8AAB673BA89D703A22F915E95CE0E95621C769E6FD7506744F0D2C43`

Supporting screenshots:

- `C:\Codex\Saagar Control Centre\SaagarCC-V6-ETP-Demo-E7.png`
- `C:\Codex\Saagar Control Centre\SaagarCC-V6-ETP-Demo-E7-results.png`
- `C:\Codex\Saagar Control Centre\SaagarCC-V6-ETP-Demo-landscape.png`
