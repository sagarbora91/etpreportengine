# Implementation Roadmap

## Delivery strategy

Build two vertical slices before expanding breadth. Preserve the prototype as reference evidence while new .NET components are proved independently.

## Stage 0 — Repository stabilization

- Obtain or reconstruct a complete source checkout with Git metadata.
- Record missing prototype files and distinguish export exclusions from defects.
- Preserve the current JavaScript behavior tests as reference evidence.
- Keep Graphify current for architectural navigation.

Exit: complete provenance is known; missing files no longer obscure reuse decisions.

## Stage 1 — Solution and database foundation

- Create layered .NET solution and automated test projects.
- Add configuration, logging and SQL connection health checks.
- Implement checksum-verified SQL migrations.
- Create foundation, master, profile and import-lineage tables.
- Add transactional repository and bulk-staging abstractions.

Exit: clean database can be created/upgraded automatically and tested on SQL Server Express.

## Stage 2 — First sales import profile

- Select one real representative report, preferably the source that supplies transaction and product-level sales required for Daily/Brand/Category reports.
- Port header normalization, signature detection and typed conversion behavior.
- Define versioned import profile and canonical mapping.
- Implement hash-based file duplicate detection, staging and transactional canonical writes.
- Build a golden dataset from the approved workbook and manual expected result.

Exit: one real file imports reproducibly with lineage and no direct workbook reporting.

## Stage 3 — First reporting slice

- Implement Daily Sales, Brand-Wise Sales and Category-Wise Sales from SQL.
- Add on-screen result grids and Excel export of the same result model.
- Reconcile every report to canonical fact totals.

Exit: the first success criterion is demonstrated end to end.

## Stage 4 — Historical and LY/TY reporting

- Implement approved comparison-period resolver.
- Add LY/TY, growth and contribution measures with zero-base tests.
- Add historical import and restatement policy.

## Stage 5 — Stock vertical slice

- Confirm authoritative opening, movement and closing sources.
- Implement movement-type signs and stock canonical facts.
- Generate movement, closing and reconciliation reports.

Exit: opening plus movements equals calculated closing and compares to trusted ETP closing.

## Stage 6 — Additional layouts and hardening

- Add profiles rather than report-specific rewrites.
- Add masters administration, backup/restore, roles, installer and operational documentation.
- Add PDF only for reports with an approved PDF requirement.

## Immediate safe implementation work

The .NET solution structure, domain primitives, import-profile contracts, migration runner interface and unit-test foundation can be created without real ETP workbooks. Production mappings, duplicate natural keys, sales/stock signs and report totals must wait for representative samples and the business decisions in `11_DECISION_LOG.md`.
