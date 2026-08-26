# ETP-A1 Parser Evaluation Checkpoint

Date: 2026-08-08
Branch/starting HEAD: `main` / `9bf9653bb28b78e50b292fd1a81afbc8e5cf6c38`
Status: evaluation foundation complete; candidate not production-approved

## Implemented

- Exact evaluation dependency `read-excel-file@9.3.7` (MIT), development-only.
- Pure, app-unloaded `ETP_XLSX_LIMITS_V1` classic-ZIP preflight.
- Deterministic refusal of invalid signatures, missing OOXML parts, unsafe/duplicate paths, encryption, ZIP64, unsupported compression, declared bombs and active-content paths.
- Private aggregate-only WLMHW benchmark runner; no filenames, sheet names, headers, cells or rows are emitted.
- Reproducible generated API-23 evaluation fixture/page/test. Generated assets and Android test source are removed after capture and are never referenced by the production app.
- A0 batch metadata ceiling reconciled from 10,000,000 to the evaluated 250,000-row limit.

## Real WLMHW evidence

The external archive SHA-256 remained `5E3AE616442C71A7EF892EAD868FB1D380D63B229EFE5FAA1941ADEC79EC44A2`. Source hashes matched the inventory. Aggregate results:

| Report | Rows | Columns | Parse time | Structure | Numeric required-ID cells |
|---|---:|---:|---:|---|---:|
| R022 | 4,398 | 46 | 539 ms | match | 4,398 |
| R025 | 5,065 | 41 | 553 ms | match | 5,065 |
| R013 | 5,065 | 28 | 405 ms | match | 5,065 |
| R003 | 5,150 | 34 | 507 ms | match | 5,150 |

All aggregate store and transaction checks were known, and the date range was `2024-09-16` through `2026-07-01`. This is WLMHW structural engineering evidence only, not WLMHW acceptance.

## API-23 evidence

- Stock Android 6 / Chrome 44 emulator `saagar_api23_evidence`.
- Generated 5,000-row, four-column XLSX: 5000 rows and four columns parsed; leading-zero text fixture preserved.
- Elapsed: 537 ms. Maximum 50 ms heartbeat delay: 1 ms.
- Test result: `OK (1 test)`.
- Clean normal debug APK: 7,323,416 bytes. Evaluation-only APK: 7,323,750 bytes. Delta: 334 bytes after APK compression, below the provisional 2 MiB gate.
- No physical-device, thermal, low-memory, document-provider, rotation/background, or OEM WebView evidence was produced.

## Blocking finding

Every tested required identifier in all four real WLMHW exports was delivered by the candidate as a numeric cell. Silent conversion to text cannot prove that leading zeros were never lost. The benchmark therefore reports `BLOCKED_NUMERIC_IDENTIFIERS`; this is an intentional fail-closed outcome.

The parser boundary must preserve numeric lexical text/type and return `XLSX_IDENTIFIER_NUMERIC_UNVERIFIED` for required numeric identifiers until the dictionaries and real both-store evidence authorize a deterministic rule. HEMW R022/R025/R013/R003 remains absent.

## Still required before parser nomination

- Expand the malicious fixture suite to inspect decompressed relationships/XML and prove formula, DTD/entity, external-link, hidden/ambiguous-sheet, cell/shared-string/dimension and timeout/cancellation refusal.
- Capture repeat-run median/p95 and Total PSS/Private Dirty evidence on API 23.
- Run a 20 MiB synthetic stress fixture.
- Verify candidate dependency subtree/license record and normal APK rebuild after generated-asset cleanup.
- Obtain physical API-23/device evidence.
- Resolve numeric identifier semantics and obtain the real HEMW four-report pack.

No parser is loaded by `www/index.html`; no schema, SQLite fact store, import UI, raw-row persistence or publication path has started.
