# ETP module specification planning adoption — 2026-08-07

## Decision

The external specification at
`V:\Co work\Titan\audit-program-designer\SAAGAR_CONTROL_CENTRE_ETP_MODULE_SPEC.md`
version 1.0 is incorporated into the SAAGAR Android roadmap as the ETP domain
and target-design specification.

It does not supersede the consolidated Android plan or authorize implementation
by itself. The plan remains authoritative for privacy, sealed-store boundaries,
HEMW, offline Android/API-23, backup/re-import and acceptance.

## Adopted material

- Retail R001-R025 and Service S001-S030 catalogues.
- Recognition fingerprints, source precedence and header anomalies.
- Transaction, payment, service-status and stock-movement semantics.
- Duplicate, overlap, correction and snapshot rules.
- Coverage/error vocabularies, reconciliation inventory and test matrix.
- Machine-readable dictionary dependency and change-control requirements.

## Explicit resolutions

- HEMW is mandatory before E1 both-store freeze/acceptance.
- Known PII is dropped before app persistence; unknown fields fail closed or
  enter a metadata-safe mapping queue. Raw workbooks remain outside the app.
- REST/server/worker concepts are translated into offline native Android
  equivalents; they do not authorize PHP or a remote backend.
- R001/R022 ambiguity requires signature plus approved context/confirmation.
- Bare `ENCIRCLE` is not an identifier alias.
- Service import is deferred to ETP-D after Retail proves the pattern.

## Planned delivery

1. ETP-A Retail ingestion foundation.
2. ETP-B sealed facts and import workflow.
3. ETP-C verified views and reconciliation.
4. ETP-D Service extension.

The earlier E1 candidate remains on `agent/e1-etp-import` at `070841e` and
must be selectively integrated only after its five policy-review findings are
rechecked. No ETP product code, schema, parser, persistence or UI was changed
by this planning decision.
