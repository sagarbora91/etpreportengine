# Current-State Architecture

## Scope and evidence

This assessment covers the delivered source archive. The archive has no Git metadata and is not a complete reproducible checkout: 90 of 178 test files referenced by `package.json` are absent, several runtime assets referenced by tests are absent, and the included test set currently reports 554 passing and 16 failing tests. Findings therefore describe the delivered prototype, not an independently verified production release.

## A. Existing architecture

The application is a Capacitor 6 Android wrapper around a single offline WebView shell in `www/index.html`. The shell hosts twelve same-origin iframe modules through `www/module-manifest.js`. Plain browser scripts expose frozen global APIs; there is no module bundler, server process, relational reporting database, or Windows desktop host.

General application data uses `storage-core.js`: an in-memory `Map` provides synchronous reads, while sql.js exports a whole SQLite database file through Capacitor Filesystem. A local-storage write-ahead log and backup rotation provide device durability. Retail ETP facts use a separate Android native plugin and sealed generation lifecycle. These persistence mechanisms are device-oriented and are not suitable as the SQL Server Express reporting source of truth.

The ETP flow is presently validation/publication oriented:

1. Browser UI selects four Retail ETP XLSX report sets.
2. Preflight and profile logic identify exact layouts.
3. A worker parses and normalizes allowed fields.
4. Policy and reconciliation components validate the generation.
5. A native Android plugin stores encrypted, sealed facts.
6. Bounded gateway/query APIs expose verified projections and operational views.

The codebase also contains many controls for authority, evidence, privacy, lifecycle, mounted modules and approval. Those controls are valuable evidence of edge cases, but they dominate the current architecture more than the new reporting product requires.

## B. Existing ETP capabilities

The delivered code knows the following concrete ETP concepts:

- Retail stores `WLMHW` and `HEMW` and financial-year/date scopes.
- Four exact Retail report identities: `R003`, `R013`, `R022`, and `R025`.
- Exact header signatures, filename aliases, approved source-field adapters, identifier handling and PII exclusion in `etp-retail-profile.js`.
- XLSX ZIP/package preflight and workbook loading in `etp-xlsx-preflight.js` and `etp-retail-xlsx-loader.js`.
- Header normalization, source-report detection, datatype conversion and normalized row construction in `etp-import-foundation.js`, `etp-xlsx-parser-policy.js`, and `etp-retail-table-parser.js`.
- Multi-file import coordination, report-set completeness, scope checks, file hashing and confirmation lifecycle.
- Canonical query fields and bounded filters for dates, invoice identity, transaction type, CRO, brand, cluster and gender.
- R022-to-R025 reconciliation and tender/payment classification controls.
- Verified presentation and analytics concepts, including sales, exceptions, CRO reconciliation, targets and incentive-related projections.
- Source lineage concepts: source hash, signature hash, report identity, row count, store/period scope and generation.

This is strong prototype knowledge, but it is expressed as browser globals and policy-specific payloads rather than a durable canonical relational model.

## C. Reuse matrix

| Component | Classification | Recommendation |
|---|---|---|
| Retail report IDs, exact header sets and aliases | ADAPT | Convert into versioned SQL/application `ImportProfile` definitions with provenance and tests. |
| Header normalization and signature detection | REFACTOR | Port deterministic algorithms into a .NET import-profile service. Preserve golden test vectors. |
| Identifier/date/numeric conversion policies | REFACTOR | Port only verified conversion rules; use typed results and row-level diagnostics. |
| XLSX preflight and sheet selection knowledge | ADAPT | Reimplement using a maintained .NET Open XML reader while preserving structural checks. |
| R022/R025 reconciliation definitions | INVESTIGATE | Preserve as candidate business rules; confirm signs, tolerances and aggregation grain before production use. |
| Query projections and safe-field lists | ADAPT | Use as evidence for canonical sales fields and report dimensions. |
| Tender dictionary concepts | ADAPT | Migrate into controlled master/reference data with effective dates. |
| Existing JavaScript tests and synthetic fixtures | ADAPT | Use as behavioral specifications; port relevant cases into .NET tests. |
| WebView ETP import UI | REPLACE | Build an operational Windows import workflow. |
| Capacitor Android wrapper and iframe shell | RETIRE | Not part of the Windows reporting-engine foundation. |
| sql.js/localStorage persistence | RETIRE | Replace with SQL Server Express and transactional imports. |
| Android sealed ETP native plugin | REPLACE | Preserve lineage and integrity goals using SQL transactions, constraints and hashes. |
| Approval/evidence wave machinery | INVESTIGATE | Retain only controls required to protect reporting data; do not make them the product. |
| General non-ETP business modules | RETIRE from new engine | Keep as reference only unless a specific reporting requirement proves reuse value. |

## D. Technical debt and risks

- Browser-global APIs couple loading order, runtime availability and behavior.
- Parsing, validation, authority, lifecycle and presentation are split across many very small scripts with extensive cross-file contracts.
- Current durable storage is not relational, centrally queryable or suitable for multi-period reporting.
- Exact profile logic is largely hardcoded in JavaScript source rather than configured and versioned as data.
- Some calculations and display behavior are coupled to presentation consumers.
- README statements conflict with `ARCHITECTURE.md` about module count and embedding model.
- Manifest byte/hash identities are stale in the delivered archive.
- The delivered package cannot execute all documented tests because files are missing.
- No real ETP workbooks are included; only synthetic fixtures and recorded evidence are available.
- No SQL Server connectivity, schema migrations, Windows executable project or installer exists.

## E. Migration recommendation

Create a new .NET Windows solution beside the prototype. Treat the JavaScript files as a reference specification and port only deterministic report knowledge into isolated import, normalization and business-rule libraries. SQL Server Express becomes authoritative only after a complete import transaction succeeds. Raw file metadata and row lineage remain available for diagnosis, while production reports query canonical sales/stock facts and controlled dimensions.

Do not port the iframe shell, local-storage compatibility layer, sealed Android plugin, or validation-heavy screen flow. Preserve the existing prototype unchanged until equivalent .NET tests prove each reused rule.
