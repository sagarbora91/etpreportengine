# ETP localization final review package — machine draft

This package inventories every static, dynamic and parameterized user-facing ETP string found in the two governed sources at product commit `4b5c9c5c883f621cc31ea22d18203425ebcdc88d`. It is a review draft only: nothing in this package is approved for import.

## Frozen identity

- Product commit: `4b5c9c5c883f621cc31ea22d18203425ebcdc88d`
- CSV: `ETP-LOCALIZATION-FINAL-REVIEW-DRAFT-2026-08-21.csv`
- CSV SHA-256: `f073bf322427996c6bbb44828dc46693ade97333b83f52b47fa588a55c1b7bb1`
- `www/modules/etp/index.html` SHA-256: `4124fbb4c97b03cbc958b23252c8ae8e1d1a4d11443178ad9c0f1f7cdde71851`
- `www/etp-verified-presentation.js` SHA-256: `031fe0d969062cab8319d23111a4e83c92111b1bf1e90f53238c80666acb2cc6`
- Rows: 119
- Parameterized rows: 14
- Rows carrying preserved technical literals/placeholders: 49
- Exact safe-literal rows: 10
- Draft provider: connected Google Translate endpoint; drafts are unapproved and require fluent review.

## Reviewer procedure

1. Verify the CSV SHA-256 above before review.
2. Review every Marathi and Hindi cell for meaning, terminology, grammar and fit in the cited UI context.
3. Do not translate or alter anything listed in `preserved_literals`; placeholders in braces must remain byte-exact.
4. For an approved row, replace `pending-fluent-review` with `translate` (or `preserve-literal` for exact technical rows) and set `reviewer` to the reviewer's identity. Record corrections directly in the language cells before approval.
5. Approval must quote the reviewed row count and the SHA-256 of the reviewed CSV. This machine-draft hash is not an approval hash after edits.
6. Import only after independent inventory/hash audit confirms the package is complete and bound to this product identity.

## Guardrails

- Current decision for all 119 rows: `pending-fluent-review`.
- Current reviewer for all rows: blank.
- Report identifiers, store identifiers, error/status codes, placeholders and file-format acronyms are intentionally preserved.
- Rendered Marathi/Hindi acceptance remains separate from wording approval and must be re-measured after import.
