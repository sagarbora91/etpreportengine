# Test Strategy

## Purpose and current baseline

This strategy protects a deterministic reporting engine in which source workbooks are evidence, SQL Server is the reporting source of truth, and the Windows UI and exports are renderers rather than calculation engines.

The current .NET baseline has xUnit projects for Domain, Import, and SQL Server infrastructure. It tests canonical/import-profile contracts, workbook preflight and typed conversion, migration planning and checksums, and invalid SQL configuration. The SQL tests currently use an in-memory migration store; they do **not** yet prove migrations or repositories against a running SQL Server Express instance. Reporting, desktop, end-to-end, backup/restore, and installer tests remain delivery work.

The legacy JavaScript suite is reference evidence only. Its failures caused by omitted ZIP content must not be waived into a passing .NET release gate.

## Test pyramid

| Level | Purpose | Examples | Normal execution |
|---|---|---|---|
| Unit | Fast deterministic rules with no filesystem, workbook application, network, or database dependency | header normalization, signature matching, typed conversion, money/quantity rules, period resolution, migration planning, report totals | Every local build and pull request |
| Component | One implementation boundary with controlled dependencies | Open XML workbook reading from synthetic files, profile-to-canonical mapping, SQL repository behavior, report query/result mapping, Excel rendering | Pull request; SQL-tagged tests require the integration environment |
| Integration | Real SQL Server engine and transaction semantics | clean migration, upgrade, constraints, rollback, duplicate handling, concurrency, query reconciliation, backup/restore | Protected CI or a documented Windows test host |
| End-to-end | Installed Windows application through import, SQL persistence, report, and export | synthetic import with diagnostics; golden sales slice when approved data exists | Release candidate |
| Operational acceptance | Deployment and recovery on a production-like Windows machine | prerequisite detection, least-privilege access, upgrade, backup, restore, uninstall/upgrade behavior | Before production promotion |

Tests should be placed at the lowest level that proves the behavior. Business formulas require unit tests, but a formula is not accepted until a SQL-backed report reconciliation also proves it in the vertical slice.

## Core invariants

Automated coverage must progressively enforce these non-negotiable outcomes:

- Unknown workbook layouts stop before canonical writes; no profile is guessed.
- Blocking diagnostics roll back canonical changes while retaining only the approved bounded audit outcome.
- The same source hash cannot create duplicate canonical facts under the approved re-import policy.
- Every canonical row retains import batch, source file, source row, and profile-version lineage.
- Applied migration IDs and SHA-256 checksums are immutable; changed or missing applied scripts fail startup/upgrade.
- Report totals reconcile to canonical SQL facts for the same parameters.
- UI and Excel consume the same renderer-neutral report result and do not recalculate measures.
- Dates, decimals, identifiers, blanks, and formula cells follow explicit parsing policy independent of machine locale.
- Logs, test results, screenshots, and exported diagnostics do not expose credentials or unapproved business data.

## Synthetic data policy

Synthetic fixtures are the default for source-controlled tests and demonstrations before approved real workbooks are available.

- Generate fictitious stores, products, invoices, people, tax identifiers, and amounts. Do not lightly mask production records and call them synthetic.
- Include boundary cases deliberately: duplicate headers, unknown/missing headers, blank cells, numeric text, leading-zero identifiers, invalid dates, negative values, returns/cancellations, zero totals, large row counts, and duplicate files.
- Fix clocks, cultures, random seeds, and expected ordering so results are reproducible.
- Label fixture folders and in-app demonstration data `SYNTHETIC — NOT FOR BUSINESS USE`.
- Store only the smallest fixture needed to prove a behavior. Generated binary workbooks should have a documented generator or reviewable source table.
- Synthetic expected totals may validate engineering behavior, but they cannot approve production mappings, signs, GST treatment, or business formulas.

## Golden data policy

A golden dataset is created only after the business owner supplies a representative workbook and independently approves expected results.

- Keep raw customer workbooks and directly identifying extracts outside Git and outside general CI artifact storage.
- Record an opaque dataset ID, source SHA-256, report/layout/profile versions, approval reference, expected row counts, control totals, and report outputs.
- Prefer an approved, irreversible de-identified derivative for automated regression. If de-identification would invalidate the behavior, run the encrypted restricted dataset only in an authorized environment.
- Expected outputs must be manually approved; never regenerate expected values from the implementation under test in the same change.
- Changes to a golden expectation require a business decision reference and reviewer explanation. Code changes alone must not silently update the baseline.
- Golden failures block release until classified as a defect or an explicitly approved rule/profile revision.
- Apply the organization's retention and deletion policy to raw and derived datasets and maintain an owner for each restricted dataset.

## SQL Server Express integration approach

The target database test environment is a disposable database on the same supported SQL Server engine/compatibility level as deployment. SQL Server Express on a controlled Windows host is the primary compatibility target. A SQL Server container may provide additional CI coverage only if its engine/version differences are understood; it does not replace Express acceptance.

The harness must:

1. Receive its administrative setup connection through protected environment/CI secret configuration, never a committed connection string.
2. Create a uniquely named test database and least-privilege application principal.
3. Run every migration from an empty database and verify `schema_migrations` IDs/checksums.
4. Exercise an upgrade from the last supported released schema and reject altered/missing applied scripts.
5. Test constraints, transactional rollback, duplicate-file races/idempotency, lineage, decimal/date fidelity, cancellation, and report queries.
6. Isolate parallel runs by database name and dispose of databases in `finally`/test teardown, retaining a failed database only under an explicit diagnostic option.
7. Redact connection strings and credentials from output.

Tests that cannot connect must fail as an environment/setup failure in the protected integration job; they must not report success by skipping. Developer unit-test runs remain database-independent.

## Backup and restore validation

Backup is not proven by a successful `BACKUP DATABASE` command alone. Before release, an automated or operator-run recovery test must:

1. Import a known synthetic/golden batch and capture schema version, lineage counts, and report control totals.
2. Create a full backup to an approved path using the production-equivalent service identity.
3. Verify the backup is readable and record its timestamp, size, and integrity result.
4. Restore it under a new database name on the supported SQL Server target.
5. Run integrity checks, migration/startup health checks, row/lineage comparisons, and the same report reconciliation.
6. Record recovery duration and compare it with the agreed recovery objective.
7. Remove the restored test database and backup according to retention policy.

At least one restore rehearsal is required for each release candidate that changes schema, migration, backup, authentication, or installer behavior. Production operations also need a scheduled periodic restore drill; frequency and retention are business/operations decisions.

## CI and release gates

The repository currently runs restore, Release build, and tests on `windows-latest` for pushes to `main` and pull requests. Warnings are errors and builds are deterministic. The following gates are required as the implementation grows:

### Pull request gate

- Clean `dotnet restore`, Release build, and all database-independent .NET tests.
- No compiler warnings, committed secrets, connection strings, customer workbooks, database files/backups, or unapproved generated exports.
- New/changed deterministic rules have tests; migrations are additive, ordered, and never edit an applied script.
- Relevant architecture and decision documentation is updated.
- SQL integration job passes for database/migration/repository/report-query changes once that job is available.

### Release-candidate gate

- All pull-request gates on a clean checkout using locked reviewed dependencies.
- Live SQL Server Express clean-install and supported-upgrade suites pass.
- End-to-end import-to-SQL-to-report-to-Excel reconciliation passes with approved data for each production profile.
- Installer/prerequisite, least-privilege, backup/restore, logging/privacy, and dependency/security checks pass.
- Artifacts are versioned, checksummed, signed when signing is established, and generated only by the release pipeline.
- Known limitations and unresolved business decisions are reviewed; no blocker is waived informally.

### Production promotion gate

- Business owner approves the golden totals and report presentation.
- Operations accepts backup location, retention, restore evidence, support ownership, and rollback/runbook.
- Release artifact identity and database migration set match the tested candidate exactly.

Coverage percentage is a diagnostic, not an acceptance substitute. Gate on critical behaviors and reconciliations first; set a numerical threshold only after baseline collection and agreement.

## Evidence and defect handling

Each test run used for a release should retain the commit/release ID, .NET and SQL versions, migration checksums, test result files, fixture/dataset IDs, and artifact checksums. Evidence must follow the same privacy and retention controls as its source data.

Flaky tests are defects. A quarantine must have an owner, reason, issue, expiry date, and compensating manual gate; quarantined financial correctness, migration-integrity, or recovery tests cannot permit production promotion.
