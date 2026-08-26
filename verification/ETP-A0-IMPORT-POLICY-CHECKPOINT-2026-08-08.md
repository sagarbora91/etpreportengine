# ETP-A0 import-policy checkpoint

**Date:** 2026-08-08 (Asia/Kolkata)
**Baseline:** `main` at `9bf9653bb28b78e50b292fd1a81afbc8e5cf6c38`

## Implemented

- Canonical source: `www/etp-import-foundation.js`.
- Canonical tests: `tests/e1-etp-import-foundation.test.mjs`.
- Current contract: `docs/ETP-A0-IMPORT-POLICY-CHANGE-CONTRACT-2026-08-08.md`.
- Exact header detection/binding, required identifiers/measures, approved PII
  dropping, unknown-field refusal, ENCIRCLE distinction, text IDs, transaction
  effects, deterministic date plausibility and PII-free batch identity.
- Reduced synthetic coverage explicitly spans R022/R025/R013/R003 and does not
  claim complete WLMHW or HEMW acceptance.

The source is not referenced by `www/index.html` and contains no parser,
storage, SQLite, localStorage, file reader or UI integration.

## Source recovery decision

The old E1 documents and drafts survive only on `agent/e1-etp-import` commit
`070841e`. The whole commit was not merged because it contains 147 files and
substantial unrelated historical material. Only the reviewed policy ideas were
selectively rebuilt on current `main`; duplicate crash drafts remain excluded.

## Evidence boundary and next gate

Focused policy verification passes **11/11**, explicit offline verification
passes **256/256**, and the complete regression glob passes **383/383**.
WLMHW metadata evidence exists for
all four reports, covering only WLMHW from 2024-09-16 through 2026-07-01. The
HEMW four-report source set is absent. Therefore parser integration, schema
freeze, sealed persistence, import UI, reconciliation and E2-E6 publication
remain blocked. PHP is excluded. No physical-device or production acceptance
is claimed.
