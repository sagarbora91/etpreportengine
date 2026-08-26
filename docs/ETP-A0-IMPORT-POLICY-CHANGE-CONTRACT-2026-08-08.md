# ETP-A0 import-policy change contract

**Date:** 2026-08-08 (Asia/Kolkata)

## Authorized outcome

ETP-A0 introduces one pure, dependency-free, no-write policy module and its
synthetic tests. It establishes deterministic report admission, privacy,
lineage, store, date and transaction controls for R022, R025, R013 and R003.
It is not loaded by the shell and cannot parse a workbook or persist a row.

## Controls

- Report recognition uses an exact normalized header signature supplied by an
  approved dictionary, never a filename guess.
- A prepared row must match the exact signature that detected its report.
- Each adapter declares non-empty report-specific identifiers and measures.
- Approved known PII headers are dropped before persistable output is created;
  unknown fields and unapproved PII fail closed with no partial output.
- Bare `ENCIRCLE` can be an approved amount/flag. `ENCIRCLE ID`, `ENCIRCLE NO`
  and `ENCIRCLE NUMBER` remain identifier fields and cannot be persisted.
- IDs stay text-first, including leading zeros.
- Invoice dates require a deterministic caller-supplied historical boundary,
  as-of date and bounded future skew.
- WLMHW and HEMW are the only recognized stores and are never merged.
- INV is positive; SR and BC are negative; unknown transaction codes have zero
  business effect and an explicit warning.
- Batch metadata is bounded and PII-free. Identity is deterministic from
  store, FY, period, report and source SHA-256.

## Explicit exclusions

No XLSX/CSV parser, production dictionary, schema freeze, SQLite table, sealed
ETP store, import UI, reconciliation, dashboard metric, incentive, raw workbook
retention, PHP/backend work or production acceptance is authorized here.

## Blocking gates after A0

- Untouched HEMW R022/R025/R013/R003 exports and sanitized complete headers.
- Approved WLMHW/HEMW dictionaries, grains, identifiers, measures and PII drops.
- R001/R022 disambiguation context.
- Approved R022/R025 common grain, rounding, tolerance and refusal rules.
- Parser licence/security/container limits/APK-size/memory/time/API-23 evidence.

Until those gates pass, ETP-A0 remains an unintegrated policy foundation.
