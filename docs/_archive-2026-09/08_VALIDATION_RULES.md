# Initial Validation Rules

## File and workbook

| Code | Severity | Rule |
|---|---|---|
| FILE_EMPTY | BLOCKER | File contains no bytes. |
| FILE_HASH_INVALID | BLOCKER | SHA-256 identity is absent or malformed. |
| DUPLICATE_FILE | BLOCKER | Exact file hash was already committed for the same operational scope. |
| WORKBOOK_NO_SHEETS | BLOCKER | No readable worksheet exists. |
| WORKSHEET_DIMENSION_UNTRUSTED | INFORMATION | ETP exports declare `A1`; reader must calculate actual cells rather than reject an apparently empty workbook. |
| HEADER_INVALID | BLOCKER | Header row is missing, incomplete or contains blank canonical positions. |
| HEADER_DUPLICATE | BLOCKER | Duplicate normalized headers remain after approved layout normalization. |
| LAYOUT_UNKNOWN | BLOCKER | No exact active import profile matches. |
| LAYOUT_AMBIGUOUS | BLOCKER | More than one profile/sheet matches. |
| REPEATED_LAYOUT_COLLAPSED | INFORMATION | Exact repeated header and row halves were validated and collapsed. |
| REPEATED_LAYOUT_MISMATCH | BLOCKER | Repeated layout halves differ in any header or row cell. |

## Data protection

- Customer names, contact numbers, card numbers and comparable PII must never appear in diagnostics or application logs.
- Unknown columns resembling PII block profile activation until explicitly classified.
- Identifiers remain text even when Excel presents them numerically.

## Sales controls

- Required R025/R022 store, date, invoice and measure fields must parse successfully.
- R025 aggregates must reconcile to R022 at the approved invoice/store/period grain.
- SDB Document Wise supplies an independent invoice-level control.
- R003 and R013 must never increase canonical revenue totals.
- Payment rows may be multiple per invoice and must preserve row lineage.
- Unknown transaction types are blockers for normalized financial signs but may be staged as unresolved evidence.

## Stock controls

- Each Variant Stock ledger row must satisfy source closing equals source opening plus source transaction quantity at approved precision.
- Closing Stock remains a separate snapshot; it must not be synthesized solely from the supplied period ledger.
- Last ledger closing should reconcile to the supplied snapshot for common store/item pairs; snapshot-only and ledger-only items are reported explicitly rather than silently discarded.
- Unknown movement types block normalized balance calculations.
- Negative stock is reported but its blocker/warning status requires business approval.

## Empty report behavior

A header-only export is a valid zero-row source only after the profile and expected zero-activity semantics are confirmed. It must not be used as the sole evidence for a new production profile.
