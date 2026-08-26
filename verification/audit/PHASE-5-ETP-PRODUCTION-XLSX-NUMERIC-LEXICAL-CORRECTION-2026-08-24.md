# Phase 5 ETP production XLSX numeric-lexical correction

Date: 2026-08-24  
Scope: WLMHW production exports, offline/read-only validation  
Publication effect: none

## Finding

The source reporting system serializes identifier, timestamp, year/reference, and business-date integer cells using scientific notation or decimal-zero numeric lexical forms. The previous parser accepted only plain integer lexical identifiers and therefore stopped safely with `XLSX_IDENTIFIER_NUMERIC_UNVERIFIED`. No records were published.

## Correction

`EXACT_XLSX_INTEGER_TEXT` now expands the exact stored decimal/scientific lexical value using string operations. It does not convert through JavaScript `Number`, infer leading zeros, pad, round, or repair source values. Fractional, negative, malformed, padded numeric lexical, and over-limit results remain fail-closed. Numeric business dates use the same exact integer expansion before the existing direct-date/Excel-serial checks.

## Aggregate-only real-file evidence

The unchanged four WLMHW production workbooks passed the production loader offline after the correction:

| Report | Result | Rows |
|---|---|---:|
| R003 | `RETAIL_XLSX_ACCEPTED` | 5,150 |
| R013 | `RETAIL_XLSX_ACCEPTED` | 5,065 |
| R022 | `RETAIL_XLSX_ACCEPTED` | 4,398 |
| R025 | `RETAIL_XLSX_ACCEPTED` | 5,065 |

No workbook rows, cell values, customer data, or workbook bytes were written to the repository or emitted as evidence.

## Guardrail regression

Focused parser/loader suite: 25 passed, 0 failed. Coverage includes exact scientific expansion, decimal-zero canonicalization, fractional/negative/padded/over-limit refusal, numeric-context refusal, and scientific business-date handling.

Complete ETP suite: 161 passed, 0 failed.

## Corrected production APK

- File: `SaagarCC-v2.9-production-8daecc4.apk`
- SHA-256: `8DAECC498134E7143BB0B0DD57C3D427A654084DEA1B627C66F204A931C05769`
- Package: `com.saagartraders.bcc`
- Version: `2.9` (`209`)
- minSdk / targetSdk: `23` / `34`
- Signing certificate SHA-256: `df7877f01d2956a7c9134aca06bf91ff03a953afebc561bf520b2b4d55f98519`
- Certificate continuity: exact match with the previously approved production APK and the APK currently installed on SM-T875/API 33.

Build used the Retail-local production keystore and the verified local Gradle dependency repository. The signing password is stored only as a Windows CurrentUser DPAPI-protected blob under `.android-build/production-signing`; no plaintext credential is retained.

This evidence validates parsing only. It does not claim publication, reconciliation, HEMW evidence, or Phase 5 closure.
