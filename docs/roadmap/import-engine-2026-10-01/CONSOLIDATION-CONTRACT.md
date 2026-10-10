# ETP consolidation contract, version 1

**For:** the Claude-built consolidation tooling (the "builder") that turns raw ETP exports into consolidated workbooks, Retail (`R001`–`R031`, `SOR_AGEING`) and Service Centre (`S001`–`S040`).
**Status:** specification, 1 Oct 2026, revised the same day after a completeness review (no install reads it yet, so the revision keeps version 1). It is written so the builder can be updated from this document alone.
**Importer side:** `CONSOLIDATED-AND-RAW-IMPORT-SPEC.md` (same folder), sections 6 and 9.

**Which ETP release reads what:**

| Release | What the importer does with a contract workbook |
|---|---|
| 1.9.2 and older | **Does not understand it.** It would date stacked snapshots by the latest date it finds anywhere in `Info`. **Never send a contract workbook to these installs.** |
| 1.9.3 (P1) | Skips `ETP_Excluded` and `Snapshot History` as data sheets; uses `family_code` to settle header ties, `store_code` when the data has none, and block `snapshot_date` to date stacked snapshots. Imports `Data` as before. |
| 1.9.4 (P2) | Also runs the full validator (section 8) and refuses a workbook with any blocker. `ImportAudit --validate-contract` is available. |
| 1.9.5 (P4) and later | Full use: every block is rebuilt into its raw export (section 4), and "the latest ETP export wins" is decided between blocks and against everything already imported. |

Workbooks built without the contract keep working through the legacy reader (section 11).

**Privacy:** nothing in the contract may contain a customer name, phone number, address or loyalty number. Info holds file names, counts, dates and hashes only.

---

## 1. Why the contract exists

ETP now imports two kinds of source as equals:
- **raw ETP exports**, one at a time, the old way; and
- **consolidated workbooks**, which hold the whole history of one report family for one store.

Sources may arrive in any order and mixed. Whatever the route, the facts must come out the same.

To make that true, the importer treats every **block** of a consolidated workbook as if it were the raw export it came from. It then decides **"the latest ETP export wins"** for each document itself, such as an invoice, a stock document or a snapshot. For this it needs to know, for every Data row:
1. which ETP export the row came from (file name and SHA-256);
2. when ETP produced that export (the export time);
3. which period or snapshot date the export covered;
4. whether any rows of that export were left out, and exactly which earlier rows they repeated.

Raw ETP exports need nothing extra. Their file name already carries the export time (`yyyyMMddHHmm_…` for Retail), and one raw export is one block.

## 2. Workbook layout

One workbook holds **one family for one store**.

| Sheet | Required | Content |
|---|---|---|
| `Data` | yes | Raw ETP header in row 1 and raw values from row 2. Rows are grouped by block in block-number order. There are no blank rows, no extra columns and no formulas. |
| `Info` | yes | The machine contract at the top (section 3). Human notes may follow it (section 3.4). |
| `ETP_Excluded` | when any block has `excluded_rows > 0` | The exclusion map (section 4). |
| `Snapshot History` | only for rule `current` when earlier snapshots are kept | The earlier snapshots, one block each (section 5). |

There are no other sheets. Sheet names are matched without regard to case.

**`Data` rules:**
- **Header row.** Row 1 is exactly the header ETP exported: every column, in ETP's order and ETP's spelling. Horizontally doubled ETP layouts may be written already collapsed. The importer collapses them anyway (`WorkbookLayoutNormalizer`).
- **Values.** Values are exactly as exported. Never retype identifiers: keep leading zeros, and never turn a document number into a number with a decimal point. Dates may stay as ETP wrote them (numeric `yyyymmdd`) or be Excel date cells. The importer accepts both and normalises them to the same date.
- **No added columns.** Do not add any column to `Data`. In particular, the columns today's builder adds to Service S027 and S028 (`SourcePeriodFrom`, `SourcePeriodTo`, `SourceFile`) must go: each source file becomes a block, and its period goes in the block table. A raw export cannot carry those columns, so with them the two routes would not match.
- **Meaning.** Customer History and the targets workbooks read `Data` as today, so `Data` keeps its current meaning: the resolved history of the family.

## 3. The `Info` sheet: machine section

### 3.1 Cell layout

| Rows | Column A | Column B | Other columns |
|---|---|---|---|
| 1 | `etp_contract` | `1` (contract version) | C1 may hold a human title, e.g. `ETP Consolidation — R025 SDB VariantwiseSales` |
| 2 … k | key | value | empty |
| k+1 | *blank row* | | |
| k+2 | block-table header (section 3.3) | … | … |
| k+3 … | one row per block | … | … |
| next | *blank row* | | |
| after | optional human notes (section 3.4) | | |

**Cell formatting:**
- Write every cell of the machine section as **text**, with number format `@` or string values. Dates are `yyyy-MM-dd` text and times are `yyyy-MM-ddTHH:mm` text. Excel will then never change them.
- The importer tolerates date-typed cells, but the validator warns (`CONTRACT_CELL_NOT_TEXT`).

**Regenerate `Info` completely on every build.** Do not append a new title block below the old one, as today's builder does (HEMW R025 has three extra title rows). The **blocks** themselves are append-only across builds (section 7, rule 10). `Info` is simply rewritten to describe them.

### 3.2 Header keys (rows 2 … k)

Keys are lowercase and exact. Unknown keys are ignored with the information code `CONTRACT_KEY_UNKNOWN`.

| Key | Required | Value |
|---|---|---|
| `family_code` | yes | Catalogue FamilyCode: `R025`, `R022`, `R030` (not `STOCK_LEDGER`), `R011` (not `CLOSING_STOCK`), `R010`, `SOR_AGEING`, `S009` … |
| `report_name` | no | ETP report name, for humans, e.g. `SDB-VariantwiseSales` |
| `store_code` | yes | `WLMHW`, `HEMW` or `AW330`. It must equal every non-blank store value in `Data`. |
| `business_unit` | yes | `RETAIL` or `SERVICE` |
| `rule` | yes | `transactional`, `snapshot`, `current`, `period` or `empty` (section 6) |
| `data_sheet` | yes | `Data` |
| `header_row` | yes | `1` |
| `data_rows` | yes | Number of data rows on `Data` (excludes the header). `0` for an empty export. |
| `history_sheet` | if rule `current` has history | `Snapshot History`. Otherwise leave blank or omit the key. |
| `history_extra_columns` | if `history_sheet` | `Snapshot_As_Of,SourceFile`: the trailing columns that are not ETP columns (allowed on `Snapshot History` only) |
| `history_rows` | if `history_sheet` | Number of data rows on the history sheet |
| `excluded_sheet` | if any block has `excluded_rows > 0` | `ETP_Excluded` |
| `excluded_rows` | if `excluded_sheet` | Number of map rows on `ETP_Excluded` |
| `block_count` | yes | Number of block rows in the table |
| `coverage_from`, `coverage_to` | no | `yyyy-MM-dd`; informational only, the importer recomputes coverage |
| `built_at` | yes | `yyyy-MM-ddTHH:mm:ss+05:30` |
| `builder` | yes | Tool name and version, e.g. `saagar-etp-consolidation 2.0` |
| `package` | no | Package label, e.g. `Consolidated data import package (29 Sep 2026)` |

### 3.3 Block table

The header row has exactly these names, in this order:

```
block | sheet | first_row | last_row | row_count | source_file | source_format | source_sha256 | export_time | period_from | period_to | period_basis | snapshot_date | raw_rows | excluded_rows | superseded_rows | completeness | disposition
```

| Column | Format | Rule |
|---|---|---|
| `block` | integer 1..n | Unique. **Append order**: a block gets the next number when it is first added to the workbook, and keeps it in every later build. Numbers need not follow export time; an older raw export found later is appended with the next number and its own, earlier, `export_time`. The importer orders exports by `export_time`, never by block number. |
| `sheet` | `Data` or `Snapshot History` | The sheet holding this block's rows |
| `first_row`, `last_row` | integers, inclusive sheet row numbers | Blank only when `row_count=0`. The blocks of one sheet must **partition** its data rows exactly: no gap, no overlap, nothing outside `2 … header_row+rows`. On each sheet, blocks appear in block-number order. |
| `row_count` | integer ≥ 0 | `last_row − first_row + 1`, or 0 |
| `source_file` | text ≤ 260 | The original ETP export file name, with no folder. Examples: `202609291449_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx`, `PENDING REPAIR 29.09.2026.csv` |
| `source_format` | `xlsx` or `csv` | Format of the raw export as received |
| `source_sha256` | 64 lowercase hex | SHA-256 of the raw export file's bytes as received. **Required** for every block built from a raw file the builder holds. May be blank only for a block carried over from a pre-contract build whose raw file is lost; that block's `completeness` is then `legacy`. |
| `export_time` | `yyyy-MM-ddTHH:mm`, `yyyy-MM-ddTHH:mm:ss` or `yyyy-MM-dd` (IST, no offset) | When ETP produced the export. Take it from the file name (section 7, rule 4). Use date-only when the name carries only a date. Leave it blank only for `legacy` blocks whose name carries no time. **Never invent a time.** |
| `period_from`, `period_to` | `yyyy-MM-dd` | The period selected in ETP for this export. Required for `transactional` and `period`. Blank for `snapshot` and `current`. |
| `period_basis` | `declared`, `observed` or `none` | `declared`: taken from the pack or ZIP name (e.g. `TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026`) or from the ETP selection. `observed`: min..max of the row dates. `none`: snapshot blocks. |
| `snapshot_date` | `yyyy-MM-dd` | Required for `snapshot` and `current`. For R011 it must equal the `Date` value of every row in the block. |
| `raw_rows` | integer | Data rows in the raw export as received. Required unless `legacy`. |
| `excluded_rows` | integer ≥ 0 | Rows of this export left out because each exactly repeated a physical row already in a **lower-numbered** block. Every one is a row on `ETP_Excluded` (section 4). |
| `superseded_rows` | integer ≥ 0 | Rows **removed from this block** in a later build because a later export re-stated them (rule 7) |
| `completeness` | `complete`, `delta`, `trimmed`, `empty` or `legacy` | See the next table. |
| `disposition` | text ≤ 400 | Human text, e.g. `New unique rows retained`, `Snapshot 02-Jul retained`. Ignored by the importer. |

**Completeness values and row arithmetic** (checked by the validator):

| Value | Meaning | Arithmetic |
|---|---|---|
| `complete` | Every row of the raw export is in this block, in the export's order. Identical rows inside the export are all kept. | `row_count = raw_rows`; `excluded_rows = 0`; `superseded_rows = 0` |
| `delta` | Rows that exactly repeated a row of a lower-numbered block were left out, and every one is mapped in `ETP_Excluded` | `row_count + excluded_rows = raw_rows`; `superseded_rows = 0`; map rows for this block = `excluded_rows` |
| `trimmed` | As `complete` or `delta`, but a later build removed rows that a later export re-stated | `row_count + excluded_rows + superseded_rows = raw_rows`; `superseded_rows > 0`; map rows for this block = `excluded_rows` (the map is **required** whenever `excluded_rows > 0`, and map rows are never removed by trimming) |
| `empty` | ETP exported a header and no rows | `row_count = 0`; `raw_rows = 0`; first and last row blank |
| `legacy` | Carried over from a pre-contract build. The raw file is lost or the arithmetic cannot be proven. | Only `row_count` is checked |

**What the importer does with each value:**
- `complete` and `delta` blocks are rebuilt into the full raw export: the physical rows plus one copy per map row. The rebuilt block then gets **the same content hash as the raw export imported directly**, so the same export reached by either route is recognised once. These blocks also take part in the "missing from a later export" check.
- `trimmed` blocks are rebuilt the same way (physical rows plus mapped copies) and used for every document they still hold in full. Because some rows are gone, they never get a content hash and are never used to infer that something is missing.
- `legacy` blocks are used for their rows only, and never to infer that something is missing. For transactional families, the `legacy` blocks of one workbook are merged into one observation per document, and a document whose legacy blocks disagree is held for the Owner (importer spec §6.6).

### 3.4 Human notes

After the blank row that ends the block table, the builder may write any text: today's free-text notes, overlap counts and explanations. The importer ignores everything below the block table.

Notes must not quote customer details. Write counts and row numbers, not names or phone numbers.

## 4. The `ETP_Excluded` sheet

This sheet is required when any block has `excluded_rows > 0` (`delta`, or `trimmed` that was `delta`).

Row 1 has exactly these headers:

```
block | sheet | row
```

| Column | Rule |
|---|---|
| `block` | The block whose export contained the omitted row |
| `sheet` | `Data` or `Snapshot History`: where the retained twin lives |
| `row` | Sheet row number of the retained twin: a **physical** row in a block with a **lower** block number |

**One map row per omitted row.** If an export had the same line three times and all three were left out, write three map rows for that block, each pointing at a **different** physical twin row. Within one block, no twin row may be used twice (`CONTRACT_EXCLUDED_TWIN_REUSED`).

**Exclude only what has distinct twins.** Leave a row out only if a lower-numbered block holds an identical physical row that no other map row of this block already uses. If an export has three copies of a line and the earlier blocks hold only two physical copies, exclude two and keep the third in the block.

**Identical** means equal in every column except the timestamp columns (`STORETIMESTAMP`, `EASTIMESTAMP` and any column whose name contains `TIMESTAMP`). The importer ignores those columns in every comparison.

**What the importer does with a map row.** It lands the map row as its own row of the block (a *virtual row*), with the twin's values and its own lineage pointing at `ETP_Excluded` and this map row. A figure made from it therefore names the export that really contained it, and the block's row multiset equals the raw export's.

If the builder cannot produce this map, it must write the block as `complete` (keep the duplicates) rather than `delta`. **Never leave rows out without mapping them.**

## 5. The `Snapshot History` sheet (rule `current`)

- **Headers.** The `Data` headers, followed by the columns listed in `history_extra_columns` (today `Snapshot_As_Of`, `SourceFile`).
- **Blocks.** Each earlier snapshot is one block with `sheet=Snapshot History`, its own `snapshot_date`, `export_time`, `source_file` and `source_sha256`.
- **Extra columns.** In every row of a block, `Snapshot_As_Of` must equal the block's `snapshot_date`, and `SourceFile` must equal its `source_file`.
- **No repeats.** The snapshot held on `Data` must **not** be repeated on `Snapshot History`. If it is, the importer counts the repeat once and warns `CONTRACT_HISTORY_REPEATS_DATA`.

## 6. Consolidation rules

| `rule` | Use for | Blocks | Importer reads it as |
|---|---|---|---|
| `transactional` | Families with a row date: R025, R022, R003, R013, R030 and other dated R families; S003, S004, S019, S023–S026, S039, S040 | One per export; each covers `period_from..period_to` | Documents keyed by the family's document key (invoice, stock document, or business day). The latest export containing a document wins. |
| `snapshot` | R010, R011, R023, SOR_AGEING; Service stock and status snapshots | One per snapshot. Each block is one complete snapshot dated `snapshot_date`. | One snapshot document per (store, family, snapshot date) |
| `current` | Service families whose `Data` holds only the latest snapshot (S006, S009, S010) | `Data`: exactly one block (the latest snapshot). `Snapshot History`: one block per earlier snapshot. | As `snapshot` |
| `period` | Undated job or status lists exported per selected period (Service S002, S007, S008, S011–S018, S020–S022, S027–S037, as the builder classifies them today) | One per export; `period_from/period_to` required | One document per (store, family, block period). The latest export of the same period wins. A block whose period partly overlaps a stored, different period is refused unless it covers that period completely. |
| `empty` | ETP exported a header and no rows | At most one block, `completeness=empty` | Records the export, with no rows |

**Reconciling today's labels.** Today's builder labels the Service job-status families `transactional`. Under this contract:
- a family **with** a row date stays `transactional`;
- an undated family exported per period becomes `period`;
- an undated family exported as a single "as of" list becomes `snapshot`.

The catalogue entry decides which applies. The validator reports `CONTRACT_RULE_INVALID` with the expected rule.

**Families the importer does not take.**
- **S001** is built by the consolidation as a union of the ten RepairRegister status views (S014–S018, S031–S035); there is no raw S001 export. The importer reports it `Not needed` (Owner decision OD-6c), so it can never double the views. The builder may keep building it for its own uses, but it is not imported.
- **S038** stays retired, as in the Service plan.

## 7. Builder obligations

1. **Never edit values.** No retyping, trimming, re-dating or recomputing. Copy cell values from the raw export.
2. **One block per raw export.** Never merge two exports into one block, and never split one export across blocks (except a `current` workbook's split between `Data` and `Snapshot History`). Number blocks in append order (section 3.3).
3. **Hash every raw export** when it is first consolidated: SHA-256 over the file bytes as received, in lowercase hex. Keep the raw files: they are the evidence and let a block be rebuilt and checked.
4. **Export time from the file name:**
   - Retail `^(\d{12})_` → `yyyyMMddHHmm`; write `yyyy-MM-ddTHH:mm`.
   - A trailing `_(\d{14})` before the extension (e.g. `ClosingStock_20260901133606.csv`) → `yyyyMMddHHmmss`; write `yyyy-MM-ddTHH:mm:ss`.
   - A `dd.MM.yyyy` date in the name (e.g. `PENDING REPAIR 29.09.2026.csv`) → date only; write `yyyy-MM-dd`.
   - Anything else → ask the Owner, or leave it blank and mark the block `legacy`. **Never use the file's modified time**, and never use the time the builder ran.
5. **Period.** Use the pack or ZIP folder name when it states one (`01 JULY 2026 TO 25 AUG 2026`, `01 SEP 2024 TO 30 NOV 2024`) and write `declared`. Otherwise write the min..max of the block's row dates as `observed`.
6. **Exact duplicates across exports.** Either keep them (the block is `complete`) or leave them out and map every one in `ETP_Excluded`, one map row per omitted row, each with its own distinct twin (section 4; the block is `delta`).
7. **Rows re-stated by a later export.** The builder may keep removing an earlier export's rows that a later export re-stated, as it does today ("578 rows re-stated … superseded"). That way `Data` stays resolved for Customer History and the targets workbooks. When it does:
   - add the count to the earlier block's `superseded_rows`;
   - mark that block `trimmed`;
   - keep that block's `ETP_Excluded` map rows unchanged;
   - **never** remove rows from the latest block (by `export_time`) that contains the document;
   - never remove a physical row that a map row of another block uses as its twin; if it must go, first keep a copy of it in that other block instead of the map row.

   The importer decides "latest export wins" itself, so trimming is optional.
8. **Repeats inside one export are genuine** (per-unit sales lines, per-unit stock-ledger rows). Always keep all of them.
9. **Snapshots:**
   - One block per snapshot date.
   - Never stack two snapshots without separate blocks. This was the cause of IF-020, where WLMHW R010 held three undated snapshots.
   - R011's `Date` column must equal the block's `snapshot_date`.
   - Two exports of the same snapshot date: keep only the later one, and record the earlier one only in the human notes.
10. **Blocks are append-only across builds.** Once a block is written, its number, its rows, their order and its metadata do not change in later builds. There are two exceptions: trimming under rule 7, which changes `completeness` to `trimmed` and increases `superseded_rows`; and, for rule `current`, moving the `Data` block to `Snapshot History` when a newer snapshot arrives, which changes only its `sheet` and row range (example 10.3). A new export always becomes a new block with the next number, **even when it is older** than exports already in the workbook (for example a raw export found later). A block with unchanged rows lets the importer skip that export cheaply on re-import.
11. **Carried-over blocks** from pre-contract builds keep their rows. Each gets:
    - `source_file` from today's Info block table;
    - `export_time` from that name;
    - `source_sha256` if the raw file still exists, otherwise blank;
    - `completeness` `complete`, `delta` (only with a map), `trimmed`, or `legacy` when the arithmetic cannot be proven.

    Rows in today's workbooks that no Info block covers (e.g. HEMW R025 rows 2:684) need a `legacy` block of their own: `source_file` = `unknown (pre-contract)`, `export_time` blank.
12. **Service S027 and S028.** Drop the added columns `SourcePeriodFrom`, `SourcePeriodTo` and `SourceFile` from `Data`; write one block per source file with that period (section 2).
13. **Validate before shipping.** Run `Etp.Reporting.ImportAudit --validate-contract <workbook> --raw <folder of raw exports>` on every workbook (available from ETP 1.9.4; it needs no database). With `--raw`, every block whose raw file is in the folder is rebuilt and compared with that raw export. Ship only when it reports **0 blockers**. Copy its summary into the control workbook. Retire `check_workbook.py`: its heuristics are outdated (package README, "What to expect in the data").
14. **Where to send it.** Only to installs on ETP 1.9.3 or later (see the table at the top).
15. **Control workbook.** Keep `00_<area>_ETP_Consolidation_Control.xlsx`. Its `Family Register` should gain `contract_version`, `validator_result` and `blocks`. The importer reports `00_` files as `Not needed`.
16. **Privacy.** No customer values anywhere in `Info`, `ETP_Excluded` or the control workbook.

## 8. Validation rules

All codes are blockers (the workbook is refused, nothing is written, and the diagnostics are stored) unless marked **W** (warning) or **I** (information). The validator and the importer share one implementation, `ConsolidationContractValidator`.

| Code | Rule |
|---|---|
| `CONTRACT_UNREADABLE` | `Info!A1` is `etp_contract` but the header keys or block table cannot be read (checked from 1.9.3) |
| `CONTRACT_VERSION_UNSUPPORTED` | `B1` is not a supported version |
| `CONTRACT_KEY_MISSING` | A required key is absent or blank |
| `CONTRACT_KEY_UNKNOWN` (I) | Key not recognised; ignored |
| `CONTRACT_FAMILY_MISMATCH` | `family_code` differs from the family the importer matched by header signature |
| `CONTRACT_STORE_MISMATCH` | `store_code` differs from a non-blank store value in the data |
| `CONTRACT_RULE_INVALID` | Rule not allowed for the family (section 6); the message names the expected rule |
| `CONTRACT_SHEET_MISSING` | `data_sheet`, `history_sheet` or `excluded_sheet` not found |
| `CONTRACT_EXTRA_COLUMNS` | `Data` has a column that is not in the ETP header (e.g. `SourcePeriodFrom`, `SourcePeriodTo`, `SourceFile`) |
| `CONTRACT_ROW_COUNT_MISMATCH` | `data_rows`, `history_rows` or `excluded_rows` differs from the sheet, or Σ `row_count` per sheet differs |
| `CONTRACT_BLOCK_TABLE_HEADER` | Block-table header not exactly as in section 3.3 |
| `CONTRACT_BLOCK_GAP` / `CONTRACT_BLOCK_OVERLAP` / `CONTRACT_BLOCK_OUTSIDE_DATA` | The blocks do not partition the sheet rows, or a sheet's blocks are not in block-number order |
| `CONTRACT_BLOCKS_NOT_IN_TIME_ORDER` (I) | Block numbers do not follow `export_time` (allowed: an older export was appended later) |
| `CONTRACT_BLOCK_ARITHMETIC` | Completeness arithmetic (section 3.3) fails |
| `CONTRACT_SHA_INVALID` | `source_sha256` not 64 lowercase hex, or blank on a non-`legacy` block |
| `CONTRACT_EXPORT_TIME_INVALID` | Unparseable, or contradicts the time or date in `source_file` |
| `CONTRACT_EXPORT_TIME_MISSING` | Blank on a non-`legacy` block |
| `CONTRACT_PERIOD_MISSING` | `transactional` or `period` block without `period_from` or `period_to` |
| `CONTRACT_SNAPSHOT_DATE_MISSING` | `snapshot` or `current` block without `snapshot_date` |
| `CONTRACT_SNAPSHOT_DATE_DUPLICATE` | Two blocks with the same `snapshot_date` |
| `CONTRACT_SNAPSHOT_DATE_DISAGREES` | R011 `Date`, or `Snapshot_As_Of`, differs from the block's `snapshot_date` |
| `CONTRACT_CURRENT_SHAPE` | `current` without exactly one `Data` block |
| `CONTRACT_EXCLUDED_MAP_MISSING` | A block has `excluded_rows > 0` and no, or too few, map rows |
| `CONTRACT_EXCLUDED_UNRESOLVED` | A map row names a block that does not exist, or a twin that is not a physical row of a lower-numbered block |
| `CONTRACT_EXCLUDED_TWIN_REUSED` | Two map rows of one block point at the same twin row |
| `CONTRACT_BLOCK_REBUILD_MISMATCH` | With `--raw`: a block rebuilt from its rows and map rows does not have the row multiset of its raw export (timestamps ignored) |
| `CONTRACT_DUPLICATE_EXPORT` | Two blocks with the same `source_sha256`, or the same `source_file` and `export_time` |
| `CONTRACT_ROW_OUTSIDE_PERIOD` (W) | A `transactional` row is dated outside its block's period |
| `CONTRACT_HISTORY_REPEATS_DATA` (W) | The `Data` snapshot also appears on `Snapshot History` |
| `CONTRACT_CELL_NOT_TEXT` (W) | A machine-section cell is not text |
| `CONTRACT_LEGACY_BLOCKS` (I) | Count of `legacy` blocks. They are imported, but never used to infer missing documents. |

## 9. Versioning

- The version is the integer in `Info!B1`. This document defines version **1**.
- **Additive changes** keep the version: a new optional key, or a new optional block column appended **after** `disposition`. The importer ignores what it does not know, with information `CONTRACT_KEY_UNKNOWN`.
- **Any other change** raises the version: a new required key, a changed meaning, a renamed column or a new completeness value.
- The importer supports the current version and the previous one for at least one release. An unsupported version is refused with `CONTRACT_VERSION_UNSUPPORTED`; it is never guessed.
- **Machine-readable copy.** The JSON Schema `docs/schemas/etp-consolidation-contract-v1.schema.json` (written in phase P2) describes the logical object the validator builds from the sheet, for the builder's own tests. Section 3 of this document governs the sheet layout. Shared test workbooks are kept under `tests-dotnet/fixtures/contract/` (synthetic data only).

## 10. Examples

All values are illustrative. SHA-256 values are shown as `<64 hex>`. No customer data.

### 10.1 WLMHW R025, transactional, with delta blocks

Row ranges and counts are those of today's Info sheet. Export times come from the source-file prefixes.

`Info` rows 1–18:

| A | B |
|---|---|
| etp_contract | 1 |
| family_code | R025 |
| report_name | SDB-VariantwiseSales |
| store_code | WLMHW |
| business_unit | RETAIL |
| rule | transactional |
| data_sheet | Data |
| header_row | 1 |
| data_rows | 5879 |
| excluded_sheet | ETP_Excluded |
| excluded_rows | 945 |
| block_count | 4 |
| coverage_from | 2024-09-16 |
| coverage_to | 2026-09-29 |
| built_at | 2026-10-05T10:15:00+05:30 |
| builder | saagar-etp-consolidation 2.0 |
| package | Consolidated data import package (5 Oct 2026) |
| *(blank)* | |

Block table:

| block | sheet | first_row | last_row | row_count | source_file | source_format | source_sha256 | export_time | period_from | period_to | period_basis | snapshot_date | raw_rows | excluded_rows | superseded_rows | completeness | disposition |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Data | 2 | 5066 | 5065 | 202607021507_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx | xlsx | `<64 hex>` | 2026-07-02T15:07 | 2024-09-16 | 2026-07-02 | observed | | 5065 | 0 | 0 | complete | First export |
| 2 | Data | 5067 | 5352 | 286 | 202608071858_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx | xlsx | `<64 hex>` | 2026-08-07T18:58 | 2026-07-01 | 2026-08-07 | declared | | 297 | 11 | 0 | delta | New unique rows retained |
| 3 | Data | 5353 | 5693 | 341 | 202609062107_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx | xlsx | `<64 hex>` | 2026-09-06T21:07 | 2026-07-01 | 2026-08-31 | declared | | 638 | 297 | 0 | delta | New unique rows retained |
| 4 | Data | 5694 | 5880 | 187 | 202609291449_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx | xlsx | `<64 hex>` | 2026-09-29T14:49 | 2026-07-01 | 2026-09-29 | declared | | 824 | 637 | 0 | delta | New unique rows retained |

`ETP_Excluded`: 945 rows, one per omitted row (11 for block 2, 297 for block 3, 637 for block 4). For example, if the 29 Sep export had one line twice and both copies were already in earlier blocks:

| block | sheet | row |
|---|---|---|
| 4 | Data | 5361 |
| 4 | Data | 5362 |

Rows 5361 and 5362 are two distinct physical copies of that line in block 3.

**How the importer reads the 17 Aug line.** The 6 Sep export's copy sits in block 3. The 29 Sep export's copy, which differs only in the contact number, sits in block 4 as a physical row (it is not an exact repeat, so it was not excluded). The importer takes block 4's observation of that invoice as the latest and counts the line once, because a contact number is a descriptive field. The builder needs to do nothing special.

**An older export found later.** If the raw 25 Aug export (`202608252051_…`) turns up after this build, it is appended as block 5 with `export_time` 2026-08-25T20:51, most of its rows mapped to twins in blocks 1–3. The importer orders the five exports by time, so block 5 counts as older than blocks 3 and 4.

### 10.2 WLMHW R010, snapshot, three stacked snapshots

| block | sheet | first_row | last_row | row_count | source_file | source_format | source_sha256 | export_time | period_from | period_to | period_basis | snapshot_date | raw_rows | excluded_rows | superseded_rows | completeness | disposition |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Data | 2 | 845 | 844 | `<prefix>_BinWise Stock … .xlsx` | xlsx | `<64 hex>` | 2026-07-02T`<hh:mm>` | | | none | 2026-07-02 | 844 | 0 | 0 | complete | Snapshot 02-Jul retained |
| 2 | Data | 846 | 1698 | 853 | `<prefix>_BinWise Stock … .xlsx` | xlsx | `<64 hex>` | 2026-08-07T`<hh:mm>` | | | none | 2026-08-07 | 853 | 0 | 0 | complete | Snapshot 07-Aug retained |
| 3 | Data | 1699 | 2403 | 705 | `<prefix>_BinWise Stock … .xlsx` | xlsx | `<64 hex>` | 2026-09-29T`<hh:mm>` | | | none | 2026-09-29 | 705 | 0 | 0 | complete | Snapshot 29-Sep retained |

`rule` = `snapshot`; `data_rows` = 2402.

### 10.3 Service S009 PendingRepair, rule `current`

Header keys include:

| Key | Value |
|---|---|
| `rule` | `current` |
| `history_sheet` | `Snapshot History` |
| `history_extra_columns` | `Snapshot_As_Of,SourceFile` |
| `data_rows` | `63` |
| `history_rows` | `<n>` |

| block | sheet | first_row | last_row | row_count | source_file | source_format | source_sha256 | export_time | period_from | period_to | period_basis | snapshot_date | raw_rows | excluded_rows | superseded_rows | completeness | disposition |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Snapshot History | 2 | `<a>` | `<a−1>` | PENDING REPAIR 07.08.2026.csv | csv | `<64 hex>` | 2026-08-07 | | | none | 2026-08-07 | `<a−1>` | 0 | 0 | complete | Earlier snapshot |
| 2 | Data | 2 | 64 | 63 | PENDING REPAIR 29.09.2026.csv | csv | `<64 hex>` | 2026-09-29 | | | none | 2026-09-29 | 63 | 0 | 0 | complete | Current snapshot |

Block numbers are append order: the 7 Aug snapshot was added first. The `export_time` values are date-only because the Service file names carry only a date. When a newer snapshot arrives, the 29 Sep rows move to `Snapshot History` **as the same block 2** (its rows, `source_file` and `snapshot_date` unchanged; only its `sheet` and row range change), and the new snapshot is block 3 on `Data`.

### 10.4 Empty export

`rule` = `empty`, `data_rows` = `0`, and one block:

| block | sheet | row_count | source_file | export_time | raw_rows | completeness |
|---|---|---|---|---|---|---|
| 1 | Data | 0 | `<prefix>_<report>.xlsx` | `<time>` | 0 | empty |

## 11. Legacy workbooks (no contract): what the importer still reads

This section is for reference, so the builder knows what happens to workbooks built before this contract.

**Detection.** `Info!A1` is not `etp_contract`, and `Info` contains a header row with `Source file` and `Data row block`. Today's header is `Package | Source file | Raw rows | Rows retained | Rows excluded | Period from | Period to | Data row block | Disposition`.

**Reading.** The importer collects every block row under every repeated header and removes exact duplicates. It parses `a:b` ranges and takes the export time from the 12-digit prefix of `Source file`.

**Tiling test.** The blocks are used only if both of these hold:
- the ranges partition `2..last Data row` with no gap and no overlap;
- the `Rows retained` values add up to the number of Data rows.

Otherwise the importer warns `INFO_BLOCKS_UNUSABLE` and treats the workbook as a single block. Seen today: the HEMW R025 gap at 2:684, the stale overlapping WLMHW R030 ranges, and no Service Info table tiles.

**How the blocks are used:**
- **Snapshot families.** Tiling blocks become dated snapshots: WLMHW R010 gives 2 Jul, 7 Aug and 29 Sep; HEMW R010 gives 7 Sep.
- **Transactional families.** Exact repeats were left out without a map, so a later block may hold only part of a document. The rows are merged into one observation per document, keeping the largest count of each identical line. If a later block holds a line that no earlier block holds, the importer cannot tell a grown invoice from a re-stated line, so it **holds** that document for the Owner (`LEGACY_BLOCKS_DIFFER`) instead of guessing. The export time of a merged observation is **unknown**, so any difference from stored facts goes to the Owner for review rather than being applied.
- **`Snapshot History` without a contract.** Rows are grouped by `Snapshot_As_Of` and `SourceFile` into dated blocks.
- **S027 and S028 with `SourcePeriodFrom`, `SourcePeriodTo`, `SourceFile`.** The importer strips these columns and groups rows into blocks by their values.

This is why contract v1 matters: only a contract workbook gets full "latest export wins" between its blocks, no legacy holds, and the cheap skip of exports already imported.

## 12. Checklist for updating the builder

- [ ] Write `Info` fresh each build: machine section, then notes.
- [ ] Add the header keys and the block table (sections 3.2 and 3.3) for every family.
- [ ] Number blocks in append order and never renumber; append late-found older exports as new blocks.
- [ ] Hash raw exports (SHA-256) when they are first consolidated, and keep the raw files.
- [ ] Parse export times from file names (rule 4). Never invent one.
- [ ] Record the declared period from the pack or ZIP name.
- [ ] Emit `ETP_Excluded` with one row per omitted row and a distinct twin each, or keep the duplicates and mark the block `complete`.
- [ ] Record trimming (`superseded_rows`, `trimmed`) instead of silently removing rows; keep map rows and their twins.
- [ ] One block per snapshot. Fix stacked R010 and R011 by splitting them into dated blocks.
- [ ] `current` rule: `Data` holds the latest snapshot only; earlier snapshots go to `Snapshot History` as blocks, without repeating `Data`.
- [ ] Map today's Service `transactional` labels onto `transactional`, `period` or `snapshot` (section 6).
- [ ] Drop the S027/S028 added columns; one block per source file.
- [ ] Run `ImportAudit --validate-contract … --raw …` on every workbook. 0 blockers.
- [ ] Send contract workbooks only to ETP 1.9.3 or later.
- [ ] No customer values in `Info`, the map or the control workbook.
