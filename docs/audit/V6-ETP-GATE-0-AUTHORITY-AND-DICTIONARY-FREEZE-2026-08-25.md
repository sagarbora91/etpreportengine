# V6 ETP Gate 0 authority and data-dictionary freeze — 2026-08-25

Status: **FROZEN FAIL-CLOSED CANDIDATE — OWNER SOURCE APPROVALS PENDING**

This record freezes the implementation boundary for E3, E4, E6, E5 and E7. It does not infer
business approval, activate a money path, authorize HEMW production or authorize Service ETP.
The machine-readable authority is
`verification/audit/V6-ETP-GATE-0-AUTHORITY-FREEZE-2026-08-25.json` and is recomputed by
`scripts/create-v6-etp-gate0-freeze.mjs`.

## Frozen programme authority

The build and acceptance order is `E3 → E4 → E6 → E5 → E7`. E5 remains money-last. E7 uses a
separate Service ETP v1 boundary and cannot widen the frozen Retail ETP v1 report/store contract.

The following rules are not optional:

- verified ETP facts are immutable and declarations are never a payment basis;
- cross-store borrowing and PII-based fuzzy matching are forbidden;
- unknown dictionary values remain quarantined or explicitly Unmapped;
- non-re-derivable operational actions are durable and portable, while sealed facts stay outside
  portable backup;
- restore preserves human actions but fences verified reads until exact re-import;
- the role selector is not a security boundary; privileged changes require Owner authority and
  fresh reauthentication;
- WLMHW has current Retail profile production authority, while HEMW production remains blocked
  pending profile evidence.

## Frozen candidate catalogues

- E3: six day states, four outcomes, five correction reasons and five manager dispositions.
- E4: immutable version rules and five adjustment reasons.
- E6: seven exception families, three statuses and six closure reasons.
- E5: scheme, calculation-run and clawback lifecycles.
- E7: mandatory S003/S004 identities, optional snapshot identities and exact-key-only matching.

These are controlled candidate machine identities. They become active only after Owner review and
the required source artifacts are supplied, hashed and approved.

## Inputs still required

### E3

Owner authority was confirmed on 2026-08-25 and is frozen in
`docs/audit/V6-ETP-E3-OWNER-AUTHORITY-2026-08-25.md`. E3 engineering may proceed through the
fail-closed operational foundation. This does not activate any later capability.

### E4

- Titan store-target source;
- festive override source;
- CRO identity mapping;
- stretch, LY weighting, Leave pro-rating and Coverage Shortfall policy.

### E6

- threshold approval;
- SLA, default owner and acknowledge/reassign/close authority.

### E5

- authoritative incentive scheme;
- CRO-to-Payroll mapping;
- Unassigned, close+15, clawback-period and Payroll pre-lock policy.

### E7

- representative Service exports and exact signatures;
- job-status, transaction, payment and SKU dictionaries;
- delivered-stage and sparse-period decisions;
- custody, consent, privacy and retention authority.

No production activation, signing or publication is authorized by this Gate 0 freeze.
