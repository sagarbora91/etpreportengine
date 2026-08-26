# ETP Core Contract Closure handoff — 2026-08-09

## Checkpoint

- Branch: `agent/etp-retail-runtime`
- Starting HEAD: `f5421edf9649c55b88f9f6e95113265eec3d593d`
- Frozen contract: `retail-etp-core-v1`
- Scope: Retail R003/R013/R022/R025 for both WLMHW and HEMW
- PHP, Service ETP, E2–E6 presentation and full Modular HTML: excluded
- `docs/audit/HANDOFF.md` and `docs/audit/AUDIT-PROGRAM-v1.md` are owner-owned and excluded from the closure commit.

## What is frozen

- One exact shared WLMHW/HEMW report profile and signature set.
- Known PII is consumed and dropped before persistence; unknown fields fail closed.
- Bare ENCIRCLE remains distinct from ENCIRCLE identifier fields.
- Numeric identifiers accept only exact non-negative integer lexical values up to 15 digits; no padding, rounding or leading-zero repair is guessed.
- India business-date, date/FY limits, store/FY/period isolation and explicit complete-period declaration.
- Offline bounded OOXML preflight and worker parsing with stable refusal codes.
- Blocking REC-002 at invoice/date grain: INV +1, SR -1, BC -1; exact quantity; R022 net value versus R025 net amount with ₹1-per-invoice tolerance.
- R013 attribution and R003 discount are non-revenue enrichment controls whose exceptions remain visible.
- PAYMENTTYPE25 is unresolved: non-zero values are quarantined and excluded from facts and verified projections.
- Manager reauthorization before publication; metadata-only verified receipt/history; report-specific verified reader.
- Separate AES-256-GCM Android-Keystore ETP database, bounded authenticated chunks, atomic publication, current-plus-one-previous retention, restore fence and reset.
- Portable backups include only the ETP scope/control registries, never workbook bytes, raw rows, PII or ETP facts.

## Real-source conformance

The two external all-history archives were read in memory and only aggregate metadata was written to `verification/ETP-CORE-REAL-CONFORMANCE-2026-08-09.json`.

- WLMHW: R003 5,440×34; R013 5,351×28; R022 4,658×46; R025 5,351×41.
- HEMW: R003 739×34; R013 736×28; R022 708×46; R025 736×41.
- The four frozen signatures match across both stores.
- REC-002 passed with zero differences for 4,658 WLMHW and 708 HEMW groups.
- R013 attribution exceptions: WLMHW 136, HEMW 33.
- R003 discount exceptions: WLMHW 162, HEMW 6.
- Quarantined PAYMENTTYPE25 non-zero rows: WLMHW 2,802, HEMW 18.
- No PII canary was present in persistable output.

The source workbooks remain outside the app and repository. This is structural and reconciliation evidence, not a published production batch or device acceptance.

## Automated evidence

- Complete offline regression: **492/492 passed**.
- ETP suite: **128/128 passed**.
- Modular architecture: **72/72 passed** after deterministic fingerprint refresh.
- Main offline/storage/security suite: **262/262 passed**.
- Normal non-seeded APK build: passed for version 2.9 / versionCode 209 / minSdk 23.
- Generated debug APK (external build output, not a tracked repository path):
  7,326,431 bytes, SHA-256 `A5502378EB5877BCD3CAA36172DBC7D6777ABE19FAF340A81053F6D5BCDBCDDB`.
- API-23 native ETP instrumentation: **2/2 passed** (Keystore round-trip, unique IVs, non-first-chunk tamper refusal, plaintext-canary absence, reset/key deletion and incomplete-stage recreation safety).
- Rebuilt-APK update harness: two attempts hit the ten-minute baseline synthetic-seed deadline before candidate installation; no pass is claimed for this APK hash. The earlier storage-hotfix emulator pass remains bound to its earlier APK hash.

## Honest remaining gates

- Physical-device update-in-place smoke for the storage hotfix remains owner-only evidence; do not uninstall or clear data before testing.
- Rebuilt-APK API-23 install-replace evidence remains pending because the evidence harness could not finish seeding its baseline within its fixed budget.
- Physical API-23 parser/import, document-provider selection, process-death, disk-full/corruption and OEM WebView evidence remain formal acceptance gates.
- No real workbook has been published into a production native generation by this closure task.
- R013/R003 exceptions need user-facing treatment after Modular HTML; they never add revenue.
- PAYMENTTYPE25 remains quarantined until an approved mapping exists.
- E2–E6 views, Service ETP, PHP/server work, production signing and release acceptance remain out of scope.

## Next phase

Proceed with complete Modular HTML hardening against this frozen contract. Keep the native security/storage boundary native. Build E2–E6 UI and analytics only after the new module/device-gateway architecture is stable.
