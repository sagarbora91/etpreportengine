# ETP-A1 Parser Evaluation Contract

Date: 2026-08-08
Status: evaluation only; no production parser, persistence, import UI, or runtime loading

## Purpose

Nominate or reject an offline XLSX reader for the Retail ETP path without weakening the ETP-A0 fail-closed policy. This phase may read private source workbooks transiently for aggregate evidence. It must not place workbook bytes, filenames, sheet names, headers, cells, rows, or parser exception text in the repository, application storage, or logs.

## Candidates

1. `read-excel-file@9.3.7` (MIT) is the first benchmark candidate because its browser bundle is small and its scope is read-only XLSX ingestion.
2. SheetJS CE `0.20.3` (Apache-2.0, exact official tarball/hash required) is the API-23 compatibility fallback.
3. `xlsx@0.18.5` from npm, ExcelJS, and node-xlsx are rejected because of unresolved advisories, excessive footprint/scope, or an unsuitable browser boundary.

Candidate selection is not production approval. Every candidate remains behind the same independent preflight and policy gates.

## ETP_XLSX_LIMITS_V1

- Input: 32 MiB maximum; classic ZIP only; ZIP64 and encryption refused.
- Container: 512 entries; STORE/DEFLATE only; 32 MiB per expanded entry; 128 MiB total; 100:1 maximum compression ratio.
- Workbook: 8 sheets maximum; deterministic single import-sheet selection; 250,000 rows/sheet; 128 columns; 2,000,000 nonblank cells.
- Text: 250,000 shared-string items; 16 MiB sharedStrings XML; 4,096 UTF-16 code units/cell.
- Execution: worker boundary; 30-second API-23 timeout; deterministic cancellation; no network.
- Content refused: macros, ActiveX, OLE/embedded objects, custom XML, external relationships, DTD/entities, formulas (including cached formulas), hidden/ambiguous import sheets, malformed references, unsupported cell types, and duplicate/unsafe ZIP paths.

The A0 batch ceiling is 250,000 rows so accepted metadata cannot imply a capacity larger than the evaluated parser boundary.

## Identifier rule

Required identifiers must cross the parser boundary as text. A numeric source cell is refused as `XLSX_IDENTIFIER_NUMERIC_UNVERIFIED`; it is never silently stringified or padded. A repair rule needs real WLMHW and HEMW evidence plus an approved dictionary decision.

### 2026-08-24 production-export amendment

The source reporting system consistently serializes identifier and business-date integers as XLSX numeric lexical values, including scientific notation and decimal-zero forms. The approved `EXACT_XLSX_INTEGER_TEXT` rule canonicalizes only the exact decimal integer represented by the stored XLSX lexical string. It performs string arithmetic only: no IEEE-754 conversion, rounding, padding, or leading-zero repair. Fractional, negative, malformed, padded numeric lexical, and over-limit results remain refused as `XLSX_IDENTIFIER_NUMERIC_UNVERIFIED`. Dates remain subject to the existing calendar/Excel-serial validity rules after exact integer canonicalization.

## Provisional engineering gates

- Parser-only debug APK delta: at most 2 MiB.
- Each current approximately 1 MiB WLMHW workbook: at most 5 seconds median.
- Synthetic 20 MiB stress workbook: at most 15 seconds p95.
- API-23 peak Total PSS delta: at most 256 MiB; no OOM/process death.
- Worker main-thread maximum heartbeat gap: at most 250 ms.
- Four of four WLMHW structures match the controlled inventory.
- Every malformed fixture fails closed; zero persistence and zero network.

Emulator evidence can nominate a candidate. Physical API-23/device testing is still required before production selection.

## Exit rule

ETP-A1 closes only when one candidate passes container, privacy, malformed-input, size, timing, memory, responsiveness, offline and API-23 gates. If identifiers are numeric or lexical fidelity cannot be proven, the candidate remains blocked even when row and header counts match.
