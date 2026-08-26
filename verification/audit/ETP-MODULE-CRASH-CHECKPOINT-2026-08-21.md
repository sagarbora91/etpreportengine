# ETP module crash checkpoint — 2026-08-21

Status: ETP implementation phases 1–3 are complete. Phase 4 is paused before
localization import. Do not rebuild, regenerate identities, recapture evidence,
or rerun the full audit until the 119-row wording package is approved.

## Resume authority

- Branch: `agent/modular-phase1-shared-spine-v2`
- Product-code freeze: `4b5c9c5c883f621cc31ea22d18203425ebcdc88d`
- Localization-package integration commit: `34c6f05`
- Final review CSV: `verification/audit/ETP-LOCALIZATION-FINAL-REVIEW-DRAFT-2026-08-21.csv`
- CSV SHA-256: `f073bf322427996c6bbb44828dc46693ade97333b83f52b47fa588a55c1b7bb1`
- Review rows: 119 (97 module HTML + 22 verified-presentation controller)
- Source hashes: module HTML `4124fbb4c97b03cbc958b23252c8ae8e1d1a4d11443178ad9c0f1f7cdde71851`; controller `031fe0d969062cab8319d23111a4e83c92111b1bf1e90f53238c80666acb2cc6`.

## Completed and preserved

- Reports-owned ETP module shell and narrow parent gateway are implemented.
- Import/confirm/read calls enforce trusted role authorization on every call.
- Owner coverage authority cannot be forged by the child module.
- Four-file validation, opaque confirmation, duplicate handling, file cleanup,
  restore fencing, scope history and relaunch scope selection are implemented.
- Verified R022/R025/R013/R003 summaries are bounded, paged and PII-safe.
- PAYMENTTYPE25 remains quarantined and is never projected into verified views.
- The legacy root import UI bypass is no longer loaded.
- The presentation controller is a governed, manifest-pinned shared asset.
- Deterministic four-XLSX fixtures and a blocking mismatch fixture are committed.
- Final focused ETP suite: 155/155 pass. Final focused manifest suite: 17/17 pass.
- Localization inventory was independently reconstructed and corrected from 115
  to 119 source-backed units. All hashes, placeholders and preserved literals pass.

## Exact next action

Sagar reviews all 119 Marathi/Hindi rows in the CSV above and approves the exact
CSV SHA-256. All rows currently remain `pending-fluent-review` with blank reviewer.

After that approval, execute exactly once in this order:

1. import the approved wording and mark the review rows with the approved decision/reviewer;
2. run focused ETP and language tests;
3. regenerate module/MAH/capability identities once;
4. capture one final rendered-language matrix and obtain identity-bound visual approval;
5. build one final seeded APK and run the physical ETP fixture acceptance;
6. run the final controlled audit/comparison and update the handoff.

## Do-not-repeat boundary

- Do not modify or retest ETP phases 1–3 unless a focused test exposes a defect.
- Do not reuse the stale untracked Phase-4C rendered attestation; it is bound to
  the pre-ETP product identity.
- Do not run full offline/audit/APK work before localization import freezes the
  final product bytes.
- Do not claim real production-export acceptance from synthetic XLSX fixtures.

