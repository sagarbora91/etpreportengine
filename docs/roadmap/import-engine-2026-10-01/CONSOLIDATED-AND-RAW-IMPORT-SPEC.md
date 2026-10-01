# ETP import of consolidated workbooks and raw exports: implementation specification

**Version:** 2, 1 Oct 2026. Version 1 was revised after a completeness review; `DESIGN-DECISION-RECORD.md` §7 lists every review finding and how it was resolved.
**For:** Codex, who implements and tests the change, and the Owner, who decides §18.
**Base:** release 1.9.2 as installed. Worktree `V:\ETP\Code\Worktrees\installer-sqlcmd15`, branch `fix/installer-sqlcmd-odbc17`, head `ca4bc21`. All code paths below are relative to that worktree.
**Companions:**
- `CONSOLIDATION-CONTRACT.md`: what the Claude-built consolidation tool must emit.
- `DESIGN-DECISION-RECORD.md`: why this design was chosen.

**Evidence used:**
- `docs\audit\IMPORT-FAILURE-REGISTER.md` and `2026-10-01-NEW-PC-AND-IMPORT-FINDINGS.md`, both in worktree `docs-import-findings`.
- `TITAN-RUNBOOK.md` and `V:\ETP\NEW-PC-HANDOFF.md` section 01.
- The workflow journals `wf_1462d7b3-443` and `wf_4377d20e-a35`.
- The 29 Sep consolidated package and its README.
- `SERVICE-CENTRE-IMPORT-PLAN-2026-09-29.md`, and `SERVICE-CENTRE-IMPORT-DESIGN.md` / `-BRIEF.md` of 29 Sep.
- SELECT-only queries against `.\SQLEXPRESS / EtpReporting` on 1 Oct, evening (database collation `Latin1_General_CI_AS`, 0 LOCKED days).

**Privacy:** nothing here contains, or may require, a customer name, phone number, address or loyalty number. Test fixtures are synthetic.

---

## 0. Reading guide and conventions

- **Planner 1** is today's file-centric planner, `SqlServerTransactionalImportStore.PlanImportAsync` (`PhaseOneImportPersistence.cs:15-84`), together with its promotion and restatement procedures. It stays until phase P9.
- **Planner 2** is the document planner this spec adds. Both planners run inside the same pipeline:
  - same discovery, reading, matching and staging;
  - one SQL transaction per workbook (`SqlServerRepositories.cs:132-180`);
  - the same `append_<table>` landing procedures and the same `persist_*` fact procedures, extended.
- **One switch.** A per-family switch (`import_planner_settings`, §5.2) chooses the planner. Switching a family back to planner 1 is the rollback lever: no second engine and no backup restore. §13.6 defines exactly what planner 1 does with files and facts that planner 2 wrote.
- **Report code, not family code, in the database.** Every new table, key and lock uses the catalogue **ReportCode** (`R025`, `R022`, `STOCK_LEDGER`, `CLOSING_STOCK`, `R010`, `S009` …), as `import_files.report_code` and the applock `ETP_IMPORT:<store>:<report>` (`PhaseOneImportPersistence.cs:19-23`) already do. Only the consolidation contract uses the **FamilyCode** (`R030`, `R011`); the importer maps it through the catalogue (`EtpReportFamilies.json:2314`, `:5930`).
- **Migration numbers.** 0038–0042 belong to this spec. The Service Centre branch's planned `0038_service_centre_family_tables.sql` (commit `f55d06c`) is rebased to 0043 or later.
- **Migration style.**
  - The runner wraps each script in one transaction (`Migrations.cs:185-207`), so scripts use `SET XACT_ABORT ON` and no `BEGIN/COMMIT`.
  - Statements that reference a column added earlier in the same script run through `EXEC(N'…')`, the 0017 pattern.
  - Every script is idempotent: `IF COL_LENGTH(...) IS NULL`, `IF OBJECT_ID(...) IS NULL`, `CREATE OR ALTER`.
  - Adding a `NOT NULL` column with a default is a **size-of-data** operation on SQL Express (the metadata-only add is an Enterprise feature). It rewrites rows but fires no DML trigger, so it is safe on installs with finalised days; on today's 200 MB database it takes seconds.
- **New SQL error numbers:** 51700–51799. The highest number in use today is 51561.
- **Work rules for Codex on the owner PC:**
  - one heavy job at a time;
  - one test project at a time (release script);
  - SQL integration tests run against a scratch database (`EtpPhase1Test_*`), never the live database;
  - acceptance on live data is run by the Owner on a restored copy first.
- **Words:**
  - *Export*: one file ETP produced.
  - *Source*: one workbook imported (a raw export or a consolidated workbook).
  - *Block*: the rows of one export inside a source.
  - *Virtual row*: a row of a block that the builder left out as an exact repeat of an earlier block's row and mapped in `ETP_Excluded`; the importer lands it as its own row (§6.5).
  - *Document*: the unit of truth (§3).
  - *Typed families*: the families whose rows become typed facts (`sales_lines`, controls, tenders, enrichments, `stock_movements`, `stock_snapshots`): R025, R022, R003, R013, STOCK_LEDGER, CLOSING_STOCK, R010. All other families are *landing-only*.

---

## 1. Decision

**We build Design B's document model and Owner workflow. We deliver it the M-1 way: on the existing pipeline, in additive phases, with the urgent fixes first. We use the Fact Ledger's identity discipline.**

The judges' totals were B 20, M-1 19 and Fact Ledger 16. B was first for data correctness and for Service and Owner operability. M-1 was first for migration risk and effort. The decision record has the details.

What this means in practice:

| Area | Choice |
|---|---|
| **Unit of truth** | The **document**: an invoice, a stock document, a snapshot, a business day or an export period, depending on the family. Exactly one CURRENT version per document. |
| **Order** | **Latest ETP export wins per document**, decided by ETP's export time. Import order and route never decide. An unknown export time never wins automatically. |
| **Changes** | Stored facts change without review only when the change cannot alter a quantity or amount that has been settled: a new document; a NULL legacy value filled; late rows added to a landing-only business day; master-data and status attributes from a newer export (OD-3). Two further cases, a day exported before it ended and updated by the next day's export, and a newer reading of the same snapshot date with no row removed, apply automatically **only when the Owner runs the import** (OD-7); for anyone else they become one-click review items. Every automatic change is archived and audited. Everything else is a **change set** that the Owner approves in the app. It is applied from rows already landed, with no re-import. |
| **Absence** | A document missing from a later complete export becomes a review item, whatever the import order. Nothing is ever deleted or withheld without the Owner seeing it. |
| **Customer fields** | Descriptive. Never part of identity or change detection. Latest export wins for display. |
| **Consolidated workbooks** | Read through a contract in the `Info` sheet. Each block rebuilds into its raw export: retained rows plus the excluded copies, which are landed as their own rows. Today's workbooks are read in a safe legacy mode. |
| **Pipeline** | Unchanged: one transaction per workbook, today's landing and fact procedures extended, planner switch per family. |
| **First release (1.9.3, P0–P1)** | Current-engine fixes that release all held Titan data and fix HEMW stock. Failure reasons and source files are kept. |

---

## 2. Requirements trace

| Req | Met by | Proven by |
|---|---|---|
| R1 both kinds first-class; any order; idempotent; same facts | Blocks with virtual rows (§6); export-time order (§6.3); the decision engine (§8); block skip with ruleset check (§6.8); re-decide (§6.9). **Service:** consolidated route from P8; the raw Service route waits for OD-6(a) (CSV reader and raw-name patterns, §7.5). | `ConvergenceSqlTests` (§15 P4/P5/P6); `Reimport_is_idempotent_and_lands_nothing`; P6 replay of 1 Oct in both orders |
| R2 two-year history in one go, no hand-built files for routine cases | Document-level decisions; no declared-range overlap test; legacy and contract readers (§6); row-level locked-day guards shipped with planner 2 (§5.5) | P6 acceptance: the 1 Oct load replayed from the 25 Sep backup (8 v0 files) with the package and the raw 25 Aug folders, both orders, 0 Failed, no reviewed files; plus a fresh-database load |
| R3 never double, never silently drop; diagnostics on every refusal | One CURRENT version per document (`UX_fact_document_versions_current`); hard identity indexes (§5.1); absence and legacy-difference reviews (§6.6, §8.4); held rows listed (§7.4); decisions persisted (§5.2); diagnostics (§11.1) | `Stale_cross_block_copy_not_doubled`; `Missing_from_later_export_creates_review_never_deletes`; `Legacy_blocks_that_differ_are_held`; `ImportDiagnosticsSqlTests` |
| R4 every IF row resolved | §14 | per row |
| R5 governance: approval, archive, lineage, audit | Change sets (§10) through `approval_requests`; `restatement_fact_archive` with an `import_restatements` row for every set, automatic ones included; `v_fact_source_lineage`; `record_operational_audit` | `Approved_set_archives_replaces_audits_atomically`; `Automatic_set_writes_restatement_and_archive` |
| R6 backward compatible, forward-only, Express, phased | Additive migrations (§5); resumable upgrade that never updates `import_files`, `source_lineage`, landing or fact tables (§13); planner switch with defined switch-back (§13.6) | `Migration*Tests`; `Upgrade*SqlTests`; `Switch_back_after_planner2_import_dedupes` |
| R7 Service families on the same mechanism | Scopes `SNAPSHOT` and `PERIOD`; Snapshot History blocks; consolidation columns as block metadata; Service export-name grammar (§6, §7.5) | P8 |
| R8 machine-readable contract; raw needs none | `CONSOLIDATION-CONTRACT.md`; `ImportAudit --validate-contract` (with `--raw` rebuild check) | `ConsolidationContractTests` |
| R9 customer fields never conflict; privacy unchanged | Role `Descriptive` (§7.1) and never a key; diffs show fact fields only; no new store of customer values or hashes; evidence bytes not readable by viewers | `Customer_edit_is_descriptive_only`; `Persisted_details_contain_no_customer_values` |

---

## 3. Concepts

| Term | Meaning | Stored in |
|---|---|---|
| **Source** | One imported workbook: a raw ETP export, a consolidated workbook, or a reviewed file | `import_files` (unchanged role). New columns `planner_version`, `source_kind`, `contract_version`. |
| **Block** | The rows of one ETP export inside a source. A raw export is exactly one block. Block numbers are **append order** inside a workbook; the order of exports is decided by export time, never by block number. | `import_file_blocks` |
| **Virtual row** | One excluded copy of a `delta` or `trimmed` block, landed as its own row: sheet `ETP_Excluded`, row = the map row, values copied from the twin row it repeats. It has its own lineage, so a fact made from it names its own export. | landing tables, `etp_import_content`, `import_file_block_copies` |
| **Export time** | When ETP produced the export, with a **basis**: `MINUTE` (12-digit Retail prefix), `SECOND` (14-digit Service suffix), `DATE` (date-only name) or `UNKNOWN` | per block |
| **Completeness** | Whether a block holds its export's full row multiset: `COMPLETE`, `DELTA` (physical plus virtual rows), `TRIMMED` (physical plus virtual rows, minus rows a later export re-stated), `EMPTY` or `LEGACY` | per block |
| **Role** | Per catalogue column: `Key`, `Fact`, `Attribute`, `Descriptive`, `Label` or `Ignored` (§7.1) | `EtpReportFamilies.json` |
| **Document** | The unit of truth. Scope `DOCUMENT` (invoice or stock document, keyed by financial year + number), `DATE` (one business day of a landing-only family), `SNAPSHOT` (one store-report-date snapshot) or `PERIOD` (one export period of an undated Service list) | `fact_documents` |
| **Observation** | One block's content for one document: a multiset of fact rows plus attribute values | in memory; logged in `import_document_decisions` |
| **Version** | A document's facts at one point: the rows of the block that introduced it, and the typed facts that represent them. Versions are immutable apart from their state, the provisional flag and the latest attesting time; every change to stored facts creates a new version. States: CURRENT, SUPERSEDED, PENDING, REJECTED, OBSOLETE. | `fact_document_versions` |
| **Members** | The typed fact rows (by lineage) that make up a version, keyed by (version, fact). A fact kept through a change is a member of both the old and the new version. | `fact_document_members` |
| **Decision pass** | One run of the decision engine over a stored file: pass 1 is the import; later passes come from "Re-decide this source" (§6.9) | `import_decision_passes` |
| **Attestation** | "Export X also contained this version": a decision row that points at the version | `import_document_decisions` |
| **Change set** | A group of changes to stored documents. Mode AUTO (applied in the import, audited, archived) or REVIEW (waits for the Owner). Both write an `import_restatements` row before any fact is archived. | `import_change_sets`, `import_change_items` |
| **Provisional** | A version whose every attesting export was taken on or before its business date, so the day was not over | flag on the version |
| **Apply context** | A transaction-scoped permission that lets the `persist_*` procedures write facts for a file whose batch is already Completed: used only by approval apply and by re-decide | `import_apply_contexts` |
| **Planner switch** | Per report code: planner 1 or planner 2 | `import_planner_settings` |

---

## 4. Pipeline overview

```
Discover → Read → Match → Stage                                  (unchanged; contract-aware sheet choice from P1)
  → planner switch
     ├─ planner 1: today's path + P1 fixes                       (§5.1)
     └─ planner 2:
         Describe source (blocks, virtual rows, export times, snapshot dates)          §6
         Project documents (one canonicaliser; row rules)                              §7
         Resolve inside the source (latest export wins per document; legacy holds)    §6.6–6.7
         [transaction] applock → exact duplicate or re-decide → upgrade gate → read stored state
         Decide per document (pure C#)                                                 §8
         Insert batch+file → retain bytes → register blocks → land rows and virtual rows (skip attested blocks)
         AUTO change set (restatement row, archive) → NEW documents → versions, members, decisions
         REVIEW change set + approval request
         Refresh enrichment matches → reconciliation warnings → Completed
         Commit or verify (IF-014) → record attempt (every file, immediately)
```

Discovery, reading, matching and staging are unchanged apart from the sheet skips and consolidation-column stripping of §6.1:
- `BatchImportSource.cs:17-51`
- `OpenXmlWorkbookReader.cs:13-80`
- `ImportPreflight.cs:20-91`
- `ImportRowStager.cs:18-68`

The run order of families is unchanged (`FolderImportService.cs:175-179`): R025 creates invoice headers before R022.

---

## 5. Data model and migrations

### 5.1 `0038_import_engine_fixes.sql` (phase P1, release 1.9.3)

**Pre-checks.** These THROW before anything changes:

```sql
SET XACT_ABORT ON;
IF EXISTS(SELECT 1 FROM dbo.sales_invoices
  WHERE invoice_year<>YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END)
  THROW 51700,'Some invoices carry a year other than the financial year of their date. Run scripts/check-import-upgrade.sql and review before upgrading.',1;
IF EXISTS(SELECT 1 FROM dbo.sales_invoice_controls GROUP BY sales_invoice_id HAVING COUNT(*)>1)
  THROW 51701,'An invoice has more than one revenue control. Review before upgrading.',1;
IF EXISTS(SELECT 1 FROM dbo.sales_tenders GROUP BY sales_invoice_id,UPPER(tender_type) HAVING COUNT(*)>1)
  THROW 51702,'An invoice has the same tender type twice. Review before upgrading.',1;
```

Live on 1 Oct, all three checks find 0 rows:
- 0 `sales_invoices` with `invoice_year` ≠ the financial year (FY) of the date;
- 0 duplicate controls per invoice;
- 0 duplicate tenders per (invoice, type).

**A. Failure diagnostics (IF-017).**
- `import_attempts` gains these columns:
  - `failure_code varchar(80)`
  - `failure_stage varchar(12)` with CHECK IN (`READ`, `MATCH`, `SOURCE`, `SCOPE`, `PLAN`, `APPLY`, `COMMIT`, `EVIDENCE`, `RECORD`)
  - `failure_message nvarchar(1000)`
  - `sql_error_number int`
  - `exception_type varchar(120)`
  - `source_sha256 char(64)`
  - `import_batch_id uniqueidentifier`
  - `commit_state varchar(12)` with CHECK IN (`ROLLED_BACK`, `COMMITTED`, `UNKNOWN`)
  - `evidence_state varchar(16)` with CHECK IN (`RETAINED`, `ALREADY_HELD`, `NOT_RETAINED`, `NOT_ATTEMPTED`)
  - `summary_json nvarchar(max)` with CHECK `ISJSON`

  All are NULL-able, so the existing 11 Failed rows stay valid.
- New table `dbo.import_attempt_issues`:
  - columns `import_attempt_id` (FK), `seq int`, `severity varchar(12)`, `code varchar(80)`, `block_no smallint NULL`, `source_row_number int NULL`, `column_name nvarchar(128) NULL`, `document_ref nvarchar(200) NULL` (document number, date and product code only), `message nvarchar(500)`, `occurrences int`;
  - primary key `(import_attempt_id, seq)`.
- `CREATE OR ALTER dbo.record_import_attempt`:
  - today's parameters (`0028:27-30`) plus optional `@failure_code`, `@failure_stage`, `@failure_message`, `@sql_error`, `@exception_type`, `@batch`, `@commit_state`, `@evidence`, `@summary nvarchar(max)` and `@issues nvarchar(max)` (a JSON array);
  - it inserts at most 200 issue rows and aggregates the rest by code into one row each, with `occurrences`;
  - it stores `@hash` in `source_sha256`. Today the hash is used only to find the file (`0028:40-44`);
  - the outcome list (`0028:38`) gains `Imported (changes pending)`, `Held`, `Restatement pending` and `Re-decided`.

**B. Evidence inside the database (IF-023; Owner decision OD-2).**
- New table `dbo.import_source_content`:
  - columns `source_sha256 char(64) PRIMARY KEY`, `size_bytes bigint NOT NULL`, `content varbinary(max) NOT NULL`, `first_import_file_id bigint NULL` (FK `import_files`), `retained_utc datetime2(3)` (default `SYSUTCDATETIME()`), `retained_by nvarchar(200)` (default `SUSER_SNAME()`);
  - `DENY SELECT` to `etp_viewer` and `etp_store_manager`.
- `dbo.retain_import_source @hash char(64), @size bigint, @content varbinary(max), @file bigint`:
  - caller must be Store Manager, Owner or sysadmin, and `@@TRANCOUNT>0`;
  - validates that `@hash` is 64 lowercase hex characters (THROW 51750);
  - inserts the row when none exists for that hash.
- A file's evidence is present when `import_source_content` holds the file's own `import_files.source_sha256`, so no link table is needed. `source_documents` and `source_document_import_links` stay for other documents.

**C. Stock movement identity (IF-018).**

```sql
ALTER TABLE dbo.stock_movements ADD line_seq int NOT NULL CONSTRAINT DF_stock_movements_line_seq DEFAULT(1);
ALTER TABLE dbo.stock_movements ADD from_key AS ISNULL(from_location,N'') PERSISTED,
                                    to_key   AS ISNULL(to_location,N'')   PERSISTED;
DISABLE TRIGGER dbo.trg_stock_movements_protect_locked ON dbo.stock_movements;      -- 0009:46
EXEC(N'WITH s AS (SELECT line_seq, ROW_NUMBER() OVER(PARTITION BY store_code,invoice_year,document_number,document_date,
        product_code,source_transaction_type,from_key,to_key
        ORDER BY CASE WHEN transaction_quantity<0 THEN -opening_quantity ELSE opening_quantity END,
                 CASE WHEN transaction_quantity<0 THEN -closing_quantity ELSE closing_quantity END, stock_movement_id) n
      FROM dbo.stock_movements) UPDATE s SET line_seq=n WHERE n>1');
ENABLE TRIGGER dbo.trg_stock_movements_protect_locked ON dbo.stock_movements;
EXEC(N'CREATE UNIQUE INDEX UX_stock_movements_identity ON dbo.stock_movements(store_code,invoice_year,document_number,
        document_date,product_code,source_transaction_type,from_key,to_key,line_seq)');
```

- **Key width.** About 841 bytes (`0002:66-77`), under the 1,700-byte limit. The live data has no repeated identity, so every row keeps 1.
- **`dbo.persist_stock_movement`** (`0014:445-456`) is re-created with `@line_seq int = 1`:
  - the identity lookup adds `AND line_seq=@line_seq`;
  - the identity text ends `/#<line_seq>`;
  - conflicts are logged with report code `'STOCK_LEDGER'`, not `'R003'` (`0014:453`).
- **`StockUnitSequencer`** (shared by both planners from P1) gives `line_seq` per (store, FY, document, date, product, type, from, to) by running-balance order (§7.2). `StockImportOrchestrator.cs:40` passes it. Rows equal on every Key and Fact field are **kept**, each with its own `line_seq` (ties broken by source row; identical rows are interchangeable, so the stored multiset does not depend on the order), and raise the warning `STOCK_ROW_REPEATED`.

**C2. Control and tender identity (IF-006).** Today `persist_sales_invoice_control` and `persist_sales_tender` find existing rows with `TOP(1)` (`0017:84-85`, `:102-103`), so nothing in the database stops a second control or tender.

```sql
EXEC(N'CREATE UNIQUE INDEX UX_sales_invoice_controls_invoice ON dbo.sales_invoice_controls(sales_invoice_id)');
EXEC(N'CREATE UNIQUE INDEX UX_sales_tenders_invoice_type ON dbo.sales_tenders(sales_invoice_id,tender_type)');
```

- The database collation is case-insensitive (`Latin1_General_CI_AS`, 1 Oct), so the tender index matches the procedure's `UPPER(tender_type)` lookup. `scripts/check-import-upgrade.sql` reports the collation; on a case-sensitive install the migration adds a persisted `UPPER(tender_type)` column and indexes that instead.
- The procedures are unchanged: an identical row is still `ALREADY_PRESENT` and a different one still `CONFLICT`. The indexes only make a double impossible.

**D. Snapshot source and line (IF-020).**

```sql
ALTER TABLE dbo.stock_snapshots ADD source_report_code varchar(30) NULL,
      line_seq int NOT NULL CONSTRAINT DF_stock_snapshots_line_seq DEFAULT(1);
ALTER TABLE dbo.stock_snapshots ADD item_discriminator AS COALESCE(source_uid,batch_number,ean,N'') PERSISTED;
DISABLE TRIGGER dbo.trg_stock_snapshots_protect_locked ON dbo.stock_snapshots;      -- 0009:59 (missing from 0017:6-11)
EXEC(N'UPDATE s SET source_report_code=CASE WHEN l.source_record_type=''R010_SNAPSHOT'' THEN ''R010'' ELSE ''CLOSING_STOCK'' END
      FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id');
EXEC(N'WITH s AS (SELECT line_seq, ROW_NUMBER() OVER(PARTITION BY store_code,snapshot_date,source_report_code,product_code,
        item_discriminator ORDER BY quantity,unit_cost,total_cost,stock_snapshot_id) n FROM dbo.stock_snapshots)
      UPDATE s SET line_seq=n WHERE n>1');
ENABLE TRIGGER dbo.trg_stock_snapshots_protect_locked ON dbo.stock_snapshots;
EXEC(N'ALTER TABLE dbo.stock_snapshots ALTER COLUMN source_report_code varchar(30) NOT NULL');
EXEC(N'CREATE UNIQUE INDEX UX_stock_snapshots_identity ON dbo.stock_snapshots(store_code,snapshot_date,source_report_code,
        product_code,item_discriminator,line_seq)');
EXEC(N'CREATE INDEX IX_stock_snapshots_source ON dbo.stock_snapshots(store_code,snapshot_date,source_report_code)
        INCLUDE(product_code,quantity,total_cost)');
```

- **Lineage record types.** R010 facts use `R010_SNAPSHOT` (`EtpFamilySqlImportOrchestrator.cs:24`); R011 facts use `CLOSING_STOCK` (`StockImportOrchestrator.cs:41`). Live: 466 R010 and 3,300 CLOSING_STOCK rows.
- **`dbo.persist_stock_snapshot`** (`0014:458-469`) is re-created with `@source varchar(30)=NULL` and `@line_seq int=1`:
  - when `@source` is NULL, it is derived from the lineage record type, as in the backfill above, so an older caller can never insert a NULL or a wrong source;
  - the identity lookup adds source and `line_seq`;
  - conflicts are logged with `@source`, not `'R001'` (`0014:466`).
- **`SnapshotItemSequencer`** (P1, both planners) gives `line_seq` per (store, date, source, product, discriminator), ordered by quantity, unit cost, total cost, then source row. `StockImportOrchestrator.cs:41` (R011) and `EtpFamilySqlImportOrchestrator.cs:15-25` (R010) pass it, so repeated identical snapshot rows are stored, never collapsed into `ALREADY_PRESENT` by `TOP(1)`.
- **`CREATE OR ALTER VIEW dbo.v_stock_snapshots_effective`.** It returns the rows of the preferred source present for each (store, snapshot date): `CLOSING_STOCK` first, then `R010`.

  ```sql
  WITH pick AS (SELECT store_code,snapshot_date,
       MIN(CASE source_report_code WHEN 'CLOSING_STOCK' THEN 1 WHEN 'R010' THEN 2 ELSE 9 END) r
       FROM dbo.stock_snapshots GROUP BY store_code,snapshot_date)
  SELECT s.* FROM dbo.stock_snapshots s JOIN pick p ON p.store_code=s.store_code AND p.snapshot_date=s.snapshot_date
   AND p.r=CASE s.source_report_code WHEN 'CLOSING_STOCK' THEN 1 WHEN 'R010' THEN 2 ELSE 9 END;
  ```

  - SELECT is granted to `etp_viewer`, `etp_store_manager` and `etp_owner`.
  - Readers switch to the view: `OperationalReportRepository.cs:166` (inside `StockInventorySql`, `:158-182`), `OperationalReportRepository.cs:463`, and `SqlServerReportingQueryRepository.cs:18`, `:56`, `:61`, `:77`.
  - `EveningMasterRepository.cs:53`, the brand master `DISTINCT`, stays on the table.
- **First movement of a period.** `SqlServerReportingQueryRepository.cs:67` picks the opening quantity with `ORDER BY m.document_date, m.stock_movement_id`. Per-unit rows first load in P1, so it becomes `ORDER BY m.document_date, m.line_seq, m.stock_movement_id`: inside a per-unit group, `line_seq` 1 is the start of the running-balance chain whatever order the rows were inserted in.

**E. Enrichment outcome.** `dbo.persist_phase_one_enrichment` (`0025:1981-2018`) silently skips a content key that already exists (`:2004`), while C# always records `NEW` (`PhaseOneImportPersistence.cs:175`). It is re-created with `@outcome varchar(16)=NULL OUTPUT`, set to `NEW` or `ALREADY_PRESENT`; planner 1 records that outcome. A `CONFLICT` cannot arise from the key, because the key is derived from the content (`EtpInvoiceIdentity.LineKeys`). Planner 2 treats `ALREADY_PRESENT` for a document it decided was NEW as an identity bug (§9 step 12).

**F. Grants.** EXECUTE on the new procedures to `etp_store_manager` and `etp_owner`. INSERT, UPDATE and DELETE on the new tables are denied to all roles (the 0025 write-boundary pattern).

### 5.2 `0039_document_ledger.sql` (phase P3; schema only)

The script adds tables and columns, and changes existing rows only in `etp_import_content` (which has no trigger, `0018:13-16`). It never UPDATEs `import_files`, `source_lineage`, landing tables or fact tables:
- `trg_import_files_protect_locked` fires on *any* UPDATE (`0017:164-171`);
- `trg_source_lineage_protect_locked` fires on UPDATE (`0010:214-225`).

```sql
-- Planner switch (rollback lever), keyed by ReportCode
CREATE TABLE dbo.import_planner_settings(
  report_code varchar(30) NOT NULL CONSTRAINT PK_import_planner_settings PRIMARY KEY,   -- '*' = default
  planner_version tinyint NOT NULL CONSTRAINT CK_import_planner_version CHECK(planner_version IN(1,2)),
  changed_utc datetime2(3) NOT NULL CONSTRAINT DF_ips_utc DEFAULT SYSUTCDATETIME(),
  changed_by nvarchar(200) NOT NULL CONSTRAINT DF_ips_by DEFAULT SUSER_SNAME());
INSERT dbo.import_planner_settings(report_code,planner_version) VALUES('*',1);

ALTER TABLE dbo.import_files ADD
  planner_version tinyint NOT NULL CONSTRAINT DF_import_files_planner DEFAULT(1),
  source_kind varchar(24) NULL CONSTRAINT CK_import_files_source_kind
     CHECK(source_kind IN('RAW','CONSOLIDATED','CONSOLIDATED_LEGACY','REVIEWED')),
  contract_version smallint NULL;

CREATE TABLE dbo.import_file_blocks(
  import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
  block_no smallint NOT NULL,                       -- append order inside the workbook, never time order
  sheet_name nvarchar(128) NOT NULL,
  first_row int NULL, last_row int NULL, row_count int NOT NULL,
  virtual_row_count int NOT NULL CONSTRAINT DF_ifb_virtual DEFAULT(0),
  source_file_name nvarchar(260) NULL, source_format varchar(8) NULL, source_sha256 char(64) NULL,
  export_time datetime2(0) NULL,
  export_time_basis varchar(8) NOT NULL CHECK(export_time_basis IN('MINUTE','SECOND','DATE','UNKNOWN')),
  export_key char(64) NULL,          -- SHA-256 of REPORT|STORE|instant|basis|period or snapshot date|normalised report name (§6.8)
  period_from date NULL, period_to date NULL,
  period_basis varchar(10) NOT NULL CHECK(period_basis IN('DECLARED','OBSERVED','NONE')),
  snapshot_date date NULL,
  snapshot_date_basis varchar(14) NULL CHECK(snapshot_date_basis IN('COLUMN','CONTRACT','INFO_BLOCK','HISTORY_ROWS',
     'SOURCE_COLUMNS','EXPORT_NAME','INFO_COVERAGE','FOLDER','SIBLING','LEGACY_STAMP')),
  raw_rows int NULL, excluded_rows int NULL, superseded_rows int NULL,
  completeness varchar(10) NOT NULL CHECK(completeness IN('COMPLETE','DELTA','TRIMMED','EMPTY','LEGACY')),
  content_sha256 char(64) NULL,      -- multiset hash of the rebuilt export; NULL when not rebuildable (TRIMMED, LEGACY)
  content_hash_version smallint NULL,-- canonical-hash rules used; hashes compare only within one version
  ruleset_version smallint NOT NULL, -- family RulesetVersion when the block was decided
  origin varchar(14) NOT NULL CHECK(origin IN('RAW','CONTRACT','INFO_LEGACY','HISTORY_ROWS','SOURCE_COLUMNS','WHOLE_FILE','BACKFILL')),
  status varchar(10) NOT NULL CHECK(status IN('APPLIED','ATTESTED','LEGACY')),
  attested_import_file_id bigint NULL, attested_block_no smallint NULL,
  disposition nvarchar(400) NULL,
  CONSTRAINT PK_import_file_blocks PRIMARY KEY(import_file_id,block_no));
CREATE INDEX IX_import_file_blocks_export ON dbo.import_file_blocks(export_key) INCLUDE(content_sha256,content_hash_version,status) WHERE export_key IS NOT NULL;
CREATE INDEX IX_import_file_blocks_sha ON dbo.import_file_blocks(source_sha256) WHERE source_sha256 IS NOT NULL;

-- The exclusion map of contract delta/trimmed blocks: one row per virtual row (§6.5)
CREATE TABLE dbo.import_file_block_copies(
  import_file_id bigint NOT NULL, map_row int NOT NULL,     -- row on ETP_Excluded = the virtual row's source_row_number
  block_no smallint NOT NULL,
  twin_sheet nvarchar(128) NOT NULL, twin_row int NOT NULL, -- the physical row in a lower-numbered block it repeats
  CONSTRAINT PK_import_file_block_copies PRIMARY KEY(import_file_id,map_row),
  CONSTRAINT UQ_import_file_block_copies_twin UNIQUE(import_file_id,block_no,twin_sheet,twin_row),
  CONSTRAINT FK_import_file_block_copies_block FOREIGN KEY(import_file_id,block_no)
     REFERENCES dbo.import_file_blocks(import_file_id,block_no));

-- Per-row index of every landed row (etp_import_content has no trigger: 0018:13-16). It gains the sheet.
IF EXISTS(SELECT 1 FROM dbo.source_lineage WHERE source_record_type LIKE '%[_]SOURCE'
          GROUP BY import_file_id HAVING COUNT(DISTINCT sheet_name)>1)
  THROW 51704,'A planner-1 file has source rows on more than one sheet. Review before upgrading.',1;   -- live: 0
ALTER TABLE dbo.etp_import_content ADD sheet_name nvarchar(128) NULL, block_no smallint NULL,
  document_key_hash binary(32) NULL, business_date date NULL, fact_row_hash binary(32) NULL, occurrence smallint NULL,
  disposition char(1) NULL CONSTRAINT CK_etp_import_content_disposition CHECK(disposition IN('K','C','R','H'));
  -- K kept (part of the authoritative observation); C stale copy collapsed; R older block of the same source; H held row
EXEC(N'UPDATE c SET sheet_name=s.sheet_name FROM dbo.etp_import_content c
       JOIN (SELECT import_file_id,MIN(sheet_name) sheet_name FROM dbo.source_lineage
             WHERE source_record_type LIKE ''%[_]SOURCE'' GROUP BY import_file_id) s ON s.import_file_id=c.import_file_id');
EXEC(N'IF EXISTS(SELECT 1 FROM dbo.etp_import_content WHERE sheet_name IS NULL)
        THROW 51705,''A landed row has no source sheet. Review before upgrading.'',1;');
EXEC(N'IF EXISTS(SELECT 1 FROM dbo.etp_import_content GROUP BY import_file_id,sheet_name,source_row_number HAVING COUNT(*)>1)
        THROW 51703,''A source row is indexed twice. Review before upgrading.'',1;');          -- live: 0
EXEC(N'ALTER TABLE dbo.etp_import_content ALTER COLUMN sheet_name nvarchar(128) NOT NULL');
EXEC(N'CREATE UNIQUE INDEX UX_etp_import_content_row ON dbo.etp_import_content(import_file_id,sheet_name,source_row_number)');
EXEC(N'CREATE INDEX IX_etp_import_content_document ON dbo.etp_import_content(import_file_id,block_no,document_key_hash)
       INCLUDE(sheet_name,source_row_number,disposition) WHERE document_key_hash IS NOT NULL');
```

- **Landing procedures.** Every `append_<table>` procedure (generated from the catalogue, e.g. `append_etp_landing_r001`, `0025:5-73`) is regenerated with `CREATE OR ALTER` so its content insert also copies `sheet_name` from the lineage row (today it copies only `source_row_number`, `0025:68-69`). The procedures live in the database, so any client calling them writes the sheet.
- **Why the sheet matters.** Rule `current` lands both `Data` and `Snapshot History`, and virtual rows sit on `ETP_Excluded`; row numbers repeat across sheets.

```sql
CREATE TABLE dbo.fact_documents(
  fact_document_id bigint IDENTITY PRIMARY KEY,
  store_code varchar(30) NOT NULL, report_code varchar(30) NOT NULL,
  document_scope varchar(8) NOT NULL CHECK(document_scope IN('DOCUMENT','DATE','SNAPSHOT','PERIOD')),
  document_key nvarchar(200) NOT NULL,           -- e.g. '2027|100000068', '2026-08-25', '2026-09-29|R010', '2024-09-01..2024-11-30'
  document_key_hash binary(32) NOT NULL,         -- SHA-256 of REPORT|STORE|document_key
  document_date date NULL, period_to date NULL,
  status varchar(10) NOT NULL CHECK(status IN('CURRENT','PENDING','RETIRED','NOT_ADDED')),
  current_version_id bigint NULL,
  rows_import_file_id bigint NULL, rows_block_no smallint NULL,   -- current landing rows (descriptive values; landing-only facts)
  rows_export_time datetime2(0) NULL, rows_export_time_basis varchar(8) NULL,
  review_state varchar(8) NOT NULL CONSTRAINT DF_fact_documents_review DEFAULT('NONE')
     CHECK(review_state IN('NONE','PENDING','HELD')),
  CONSTRAINT UX_fact_documents_key UNIQUE(document_key_hash));
CREATE INDEX IX_fact_documents_scope ON dbo.fact_documents(store_code,report_code,document_date)
  INCLUDE(status,current_version_id,review_state);

CREATE TABLE dbo.fact_document_versions(
  fact_document_version_id bigint IDENTITY PRIMARY KEY,
  fact_document_id bigint NOT NULL REFERENCES dbo.fact_documents(fact_document_id),
  version_no int NOT NULL,
  state varchar(10) NOT NULL CHECK(state IN('CURRENT','SUPERSEDED','PENDING','REJECTED','OBSOLETE')),
  change_kind varchar(12) NOT NULL CHECK(change_kind IN
     ('BACKFILL','RESYNC','NEW','REPLACE','FILL','GROW','ATTRIBUTE','PROVISIONAL','READING','TRIM')),
  basis varchar(14) NOT NULL CHECK(basis IN('SOURCE_ROWS','CANONICAL_ONLY','UNVERIFIED')),
  fact_sha256 binary(32) NULL, canonical_sha256 binary(32) NULL, attribute_sha256 binary(32) NULL,
  row_count int NOT NULL,                        -- observation rows after the row rule (§7.2)
  member_counts_json nvarchar(400) NULL,         -- members per fact table, e.g. {"sales_invoice_controls":1,"sales_tenders":2}
  source_import_file_id bigint NULL, source_block_no smallint NULL, decision_pass_no smallint NULL,
  ruleset_version smallint NOT NULL,
  export_time datetime2(0) NULL, export_time_basis varchar(8) NOT NULL,
  last_attested_time datetime2(0) NULL, last_attested_basis varchar(8) NULL,
  provisional bit NOT NULL CONSTRAINT DF_fdv_provisional DEFAULT(0),
  previous_version_id bigint NULL, import_change_item_id bigint NULL,
  created_utc datetime2(3) NOT NULL CONSTRAINT DF_fdv_utc DEFAULT SYSUTCDATETIME(),
  created_by nvarchar(200) NOT NULL CONSTRAINT DF_fdv_by DEFAULT SUSER_SNAME(),
  CONSTRAINT UQ_fact_document_versions_no UNIQUE(fact_document_id,version_no));
EXEC(N'CREATE UNIQUE INDEX UX_fact_document_versions_current ON dbo.fact_document_versions(fact_document_id) WHERE state=''CURRENT''');
EXEC(N'CREATE UNIQUE INDEX UX_fact_document_versions_pending ON dbo.fact_document_versions(fact_document_id,fact_sha256) WHERE state=''PENDING''');

CREATE TABLE dbo.fact_document_members(                 -- insert-only; one row per (version, typed fact)
  fact_document_version_id bigint NOT NULL REFERENCES dbo.fact_document_versions(fact_document_version_id),
  fact_table varchar(40) NOT NULL CHECK(fact_table IN('sales_lines','sales_invoice_controls','sales_tenders',
     'sales_line_enrichments','stock_movements','stock_snapshots')),
  source_lineage_id bigint NOT NULL REFERENCES dbo.source_lineage(source_lineage_id),
  CONSTRAINT PK_fact_document_members PRIMARY KEY(fact_document_version_id,fact_table,source_lineage_id));
CREATE INDEX IX_fact_document_members_fact ON dbo.fact_document_members(fact_table,source_lineage_id)
  INCLUDE(fact_document_version_id);

CREATE TABLE dbo.import_decision_passes(
  import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
  pass_no smallint NOT NULL,
  pass_kind varchar(10) NOT NULL CHECK(pass_kind IN('IMPORT','REDECIDE','UPGRADE')),
  ruleset_version smallint NOT NULL, reason nvarchar(500) NULL,
  started_utc datetime2(3) NOT NULL CONSTRAINT DF_idp_utc DEFAULT SYSUTCDATETIME(),
  started_by nvarchar(200) NOT NULL CONSTRAINT DF_idp_by DEFAULT SUSER_SNAME(),
  CONSTRAINT PK_import_decision_passes PRIMARY KEY(import_file_id,pass_no));

CREATE TABLE dbo.import_document_decisions(          -- one row per (file, pass, block, document); doubles as attestation
  import_file_id bigint NOT NULL, pass_no smallint NOT NULL, block_no smallint NOT NULL, document_key_hash binary(32) NOT NULL,
  fact_document_id bigint NULL, fact_document_version_id bigint NULL,
  decision varchar(24) NOT NULL, source_row_count int NOT NULL, detail_code varchar(60) NULL,
  CONSTRAINT PK_import_document_decisions PRIMARY KEY(import_file_id,pass_no,block_no,document_key_hash))
  WITH(DATA_COMPRESSION=PAGE);
CREATE INDEX IX_import_document_decisions_version ON dbo.import_document_decisions(fact_document_version_id)
  WHERE fact_document_version_id IS NOT NULL WITH(DATA_COMPRESSION=PAGE);

CREATE TABLE dbo.import_change_sets(
  import_change_set_id bigint IDENTITY PRIMARY KEY,
  import_file_id bigint NULL REFERENCES dbo.import_files(import_file_id),   -- proposing file; the base file for MIGRATION_REVIEW
  pass_no smallint NULL,
  store_code varchar(30) NOT NULL, report_code varchar(30) NOT NULL,
  origin varchar(18) NOT NULL CHECK(origin IN('IMPORT','REDECIDE','MIGRATION_REVIEW','OWNER_RESTATEMENT')),
  mode varchar(6) NOT NULL CHECK(mode IN('AUTO','REVIEW')),
  date_from date NULL, date_to date NULL, item_count int NOT NULL,
  status varchar(10) NOT NULL CHECK(status IN('PENDING','APPROVED','APPLIED','PARTIAL','REJECTED','KEPT','OBSOLETE')),
     -- APPROVED exists only inside the apply transaction (§10.3); AUTO sets are created APPLIED
  approval_request_id bigint NULL REFERENCES dbo.approval_requests(approval_request_id),
  binding_sha256 binary(32) NOT NULL,
  summary_json nvarchar(max) NOT NULL CHECK(ISJSON(summary_json)=1),   -- counts and per-date quantity/net deltas only
  created_utc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(), created_by nvarchar(200) NOT NULL DEFAULT SUSER_SNAME(),
  decided_utc datetime2(3) NULL, decided_by nvarchar(200) NULL, decision_reason nvarchar(500) NULL, applied_utc datetime2(3) NULL);
CREATE INDEX IX_import_change_sets_queue ON dbo.import_change_sets(status,store_code,date_from);

CREATE TABLE dbo.import_change_items(
  import_change_item_id bigint IDENTITY PRIMARY KEY,
  import_change_set_id bigint NOT NULL REFERENCES dbo.import_change_sets(import_change_set_id),
  fact_document_id bigint NOT NULL REFERENCES dbo.fact_documents(fact_document_id),
  document_date date NULL,
  reason_code varchar(24) NOT NULL CHECK(reason_code IN('NEW_ON_LOCKED_DAY','LATER_EXPORT','UNKNOWN_PROVENANCE',
     'SAME_EXPORT_DIFFERS','SNAPSHOT_SHRINK','MISSING_FROM_LATER','REAPPEARED','REINTERPRETATION','IN_SOURCE_CONFLICT',
     'LEGACY_BLOCKS_DIFFER','HEADER_DATE_MISMATCH','MIGRATION_REVIEW','OWNER_RESTATEMENT','PROVISIONAL','READING',
     'ATTRIBUTE','FILL','GROW')),
  action varchar(8) NOT NULL CHECK(action IN('REPLACE','INSERT','RETIRE','UPDATE','TRIM','NONE')),
  base_version_id bigint NULL, proposed_version_id bigint NULL,
  fact_delta_json nvarchar(max) NULL CHECK(fact_delta_json IS NULL OR ISJSON(fact_delta_json)=1),  -- Fact/Attribute fields only
  status varchar(10) NOT NULL CHECK(status IN('PENDING','APPLIED','REJECTED','KEPT','OBSOLETE','HELD')));
CREATE INDEX IX_import_change_items_set ON dbo.import_change_items(import_change_set_id,status);
CREATE INDEX IX_import_change_items_document ON dbo.import_change_items(fact_document_id,status);

-- Restatement records link to change sets; the immutable trigger (0010:248-251) fires on UPDATE/DELETE only
ALTER TABLE dbo.import_restatements ADD import_change_set_id bigint NULL
   CONSTRAINT FK_import_restatements_change_set REFERENCES dbo.import_change_sets(import_change_set_id),
  scope_kind varchar(10) NOT NULL CONSTRAINT DF_import_restatements_scope DEFAULT('FILE')
   CONSTRAINT CK_import_restatements_scope CHECK(scope_kind IN('FILE','DOCUMENTS'));
ALTER TABLE dbo.import_restatements DROP CONSTRAINT UQ_import_restatements_pair;              -- 0017:25
EXEC(N'CREATE UNIQUE INDEX UX_import_restatements_pair_file ON dbo.import_restatements(previous_import_file_id,replacement_import_file_id) WHERE import_change_set_id IS NULL');
EXEC(N'CREATE UNIQUE INDEX UX_import_restatements_pair_set ON dbo.import_restatements(import_change_set_id,previous_import_file_id) WHERE import_change_set_id IS NOT NULL');
ALTER TABLE dbo.restatement_fact_archive ADD fact_document_version_id bigint NULL, import_change_item_id bigint NULL;
   -- import_restatement_id stays NOT NULL (0010:39): every archive row belongs to a restatement row, automatic sets included

-- Apply context: lets persist_* write for a Completed file inside approval apply or re-decide only
CREATE TABLE dbo.import_apply_contexts(
  session_id smallint NOT NULL, import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
  context_kind varchar(10) NOT NULL CHECK(context_kind IN('CHANGE_SET','REDECIDE')), ref_id bigint NOT NULL,
  opened_utc datetime2(3) NOT NULL CONSTRAINT DF_iac_utc DEFAULT SYSUTCDATETIME(),
  opened_by nvarchar(200) NOT NULL CONSTRAINT DF_iac_by DEFAULT SUSER_SNAME(),
  CONSTRAINT PK_import_apply_contexts PRIMARY KEY(session_id,import_file_id));

CREATE TABLE dbo.import_upgrade_state(
  store_code varchar(30) NOT NULL, report_code varchar(30) NOT NULL,
  state varchar(8) NOT NULL CHECK(state IN('PENDING','RUNNING','DONE','STALE','FAILED')),
  ruleset_version smallint NOT NULL,
  documents int NULL, facts int NULL, unverified int NULL, issues int NULL,
  started_utc datetime2(3) NULL, completed_utc datetime2(3) NULL,
  CONSTRAINT PK_import_upgrade_state PRIMARY KEY(store_code,report_code));
CREATE TABLE dbo.import_upgrade_issues(
  import_upgrade_issue_id bigint IDENTITY PRIMARY KEY, store_code varchar(30) NOT NULL, report_code varchar(30) NOT NULL,
  import_file_id bigint NULL, document_key nvarchar(200) NULL, issue_code varchar(40) NOT NULL,
  detail nvarchar(400) NULL, recorded_utc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME());
```

**Audit vocabulary.** `dbo.record_operational_audit` (`0026:5-26`) accepts digits only in six fixed count phrases, and `operational_audit` accepts only the event types of `CK_operational_audit_type` (`0015:4-9`) and the outcomes `Succeeded`, `Failed`, `Blocked`, `Cancelled` (`0004:15`). 0039 re-creates the procedure with five more count phrases: `documents changed`, `documents pending`, `documents retired`, `documents held`, `documents registered`. No new event type is needed (§10.6).

**Procedures in 0039.** All require Store Manager, Owner or sysadmin, the same check as `0025:1737-1741`, and `@@TRANCOUNT>0`.
- **`dbo.record_source_index @file bigint, @mode varchar(8), @blocks nvarchar(max), @copies nvarchar(max), @rows nvarchar(max)`**
  - `@mode` is `IMPORT` or `UPGRADE`.
  - `IMPORT`: the file must belong to an open batch (Processing).
  - `UPGRADE`: the file must be current.
  - It writes `import_file_blocks` and `import_file_block_copies`, then sets the new `etp_import_content` columns by (file, sheet, row), set-based from OPENJSON.
  - In `UPGRADE` mode it may also INSERT content rows for v0 files, which have none: **one row per distinct (file, sheet, row)** of the file's fact lineage, with key `V0:<row>:<first 8 hex of SHA-256(sheet)>`. A v0 R022 source row has one `R022_INVOICE` and several `R022_TENDER_*` lineage rows (`R022SqlImportOrchestrator.cs:84`, `:94`); they share one content row.
  - It THROWs 51711 if any content row of the file was left without metadata.
- **`dbo.write_upgrade_ledger @store, @report, @payload nvarchar(max)`**: writes `fact_documents`, versions (`BACKFILL` or `RESYNC`), members and issues for one (store, report).
- **`dbo.set_upgrade_state @store, @report, @state, @counts nvarchar(max)`.**
- **`dbo.mark_upgrade_stale @store, @report`**: called by planner 1 inside its import transaction (§13.5).

### 5.3 `0040_document_planner.sql` (phase P4)

| Procedure or object | Purpose |
|---|---|
| `dbo.read_document_state @store, @report, @documents nvarchar(max)` | Input is a JSON array of document key hashes (hex). Returns rowsets: (1) each document's status, review state, rows pointer and its CURRENT version (hashes, basis, times, provisional, row count); (2) PENDING and REJECTED version hashes; (3) for documents the caller flags as differing, the current version's per-row fact hashes and Fact/Attribute values for the diff; (4) LOCKED days of the store within the document dates; (5) for sales and revenue families, existing `sales_invoices` (store, year, document) with `transaction_date` and an **orphan** flag (no line, control or tender); (6) registered blocks of (store, report) with known time and `COMPLETE`/`DELTA` completeness whose coverage overlaps the incoming dates, for the absence check. Takes `UPDLOCK` on the `fact_documents` rows it returns. |
| `dbo.write_document_decisions @file, @pass, @payload nvarchar(max)` | Set-based: inserts or updates `fact_documents`, versions, members and `import_document_decisions`; flips version states. Writes nothing to fact tables. |
| `dbo.archive_document_facts_internal @version, @restatement, @item, @mode, @facts nvarchar(max)=NULL` | **Internal.** EXECUTE is denied to every role; it is reached only through ownership chaining from `apply_auto_change_set` and `apply_change_item`. `@mode` `DELETE` archives then deletes the member facts (or only those listed in `@facts`, for TRIM), un-matching dependent enrichments first (as `0035:174-176`) and deleting any `sales_invoices` header left with no line, control or tender; `KEEP` archives only (FILL, ATTRIBUTE). Archive rows use the JSON shapes of `0035:149-172`, plus `fact_document_version_id` and `import_change_item_id`; a deleted header is archived as fact type `sales_invoice`. |
| `dbo.apply_auto_change_set @file, @set, @payload nvarchar(max)` | **Public**, Store Manager or Owner, inside the import transaction of `@file` (open batch). For each AUTO item: archive (`KEEP` or `DELETE`) and the in-place UPDATE of Attribute or FILL columns. Calls `record_change_set_restatements` first, so the archive rows have their restatement. Fact-table triggers still guard locked days. |
| `dbo.begin_import_apply_context @file, @kind, @ref` / `dbo.end_import_apply_context @file` | Insert or delete the `import_apply_contexts` row for `@@SPID`. `CHANGE_SET`: Owner or sysadmin, `@ref` is a set in status APPROVED whose proposing file is `@file`. `REDECIDE`: Store Manager or Owner, `@ref` is the pass number just inserted for `@file` with kind REDECIDE. Both require `@@TRANCOUNT>0`, so a context never outlives its transaction. |
| `CREATE OR ALTER dbo.persist_phase_one_enrichment` | The open-file check (`0025:1991-1995`) also accepts a file with an apply context for `@@SPID`. Everything else as §5.1 E. The other `persist_*` procedures do not check the batch status (`0017:42-106`, `0014:445-469`). |
| `dbo.create_import_change_set @file, @pass, @store, @report, @origin, @mode, @items nvarchar(max), @set bigint OUTPUT` | REVIEW sets also insert `approval_requests` (`approval_type='RESTATEMENT'`, allowed by `0014:211`; `subject_type='ImportChangeSet'`; `subject_id=` set id; `business_date=` latest item date; payload = counts only). Computes `binding_sha256` = SHA-256 over sorted (document hash, base version, proposed version, action). |
| `dbo.record_change_set_restatements @set` | One `import_restatements` row per (set, base file), for **AUTO and REVIEW sets alike**: `scope_kind='DOCUMENTS'`, `replacement_import_file_id` = the proposing file (the base file itself for MIGRATION_REVIEW; `import_restatements` has no check that the two differ, `0010:17-33`), `business_date` = latest item date, `requested_by` = `SUSER_SNAME()` truncated to 100, `reason` = the Owner's reason or a fixed text for AUTO sets (e.g. "Newer export: attribute values"), `impact_summary` = counts only. Runs before any archive row is written. |
| `dbo.decide_import_change_set @set, @approve bit, @reason nvarchar(500), @items nvarchar(max)=NULL` | Owner or sysadmin only, the same check as `decide_approval_request` (`0022:65-69`). `@items` NULL means all items, otherwise a JSON list of item ids. Checks the binding, PENDING status and that each base version is still CURRENT (51721; such items become OBSOLETE). Sets the set to APPROVED (or REJECTED), records the decision on `approval_requests`, and returns the items to apply. It runs inside the C#-owned apply transaction (§10.3). |
| `dbo.apply_change_item @set, @item, @payload nvarchar(max)=NULL` | **Public**, Owner or sysadmin, set APPROVED. Archives through `archive_document_facts_internal` (`DELETE` for REPLACE/RETIRE, the listed facts for TRIM, `KEEP` for UPDATE) and performs UPDATE in place. Inserting proposed facts is done by C# through `persist_*` under the apply context. |
| `dbo.complete_change_items @set, @payload` | After C# has inserted the proposed facts: creates the new versions and their members (kept facts are copied as members of the new version), flips states, sets item and set status, ends the apply context, calls `record_operational_audit`. |
| `dbo.acknowledge_change_items @set, @items, @reason` | Owner. "Keep current" for MISSING_FROM_LATER (either direction), REINTERPRETATION, LEGACY_BLOCKS_DIFFER and held items: status KEPT, document review state cleared. |
| `dbo.begin_decision_pass @file, @kind, @reason, @pass smallint OUTPUT` | Inserts the next `import_decision_passes` row (§6.9). |
| `CREATE OR ALTER dbo.decide_approval_request` | Adds `IF @subject_type='ImportChangeSet' THROW 51725,…`, so there is one approval path for change sets. |
| Pending sets from P3 | 0040 inserts the `approval_requests` row for every PENDING change set that has none (the MIGRATION_REVIEW sets written by the P3 upgrade). |
| `CREATE OR ALTER TRIGGER dbo.tr_daily_reporting_day_lock` (`0021:8`) | Becomes `AFTER INSERT, UPDATE, DELETE`, keeps every existing check, and adds: a row moving **to** LOCKED (inserted LOCKED, deleted absent or not LOCKED) THROWs 51740 when that store-day has change items in status PENDING or HELD. Finalising is inline SQL in `DailyReportingWorkflowRepository.FinaliseAsync` (`:107-135`), an `UPDATE … SET status='LOCKED'`, so the trigger catches it and any other path. |
| `CREATE OR ALTER VIEW dbo.v_invoice_reconciliation` | Per invoice: the R022 control (`source_invoice_quantity`, `source_net_value`) against the R025 lines (`SUM(source_quantity)`, `SUM(source_gross_amount)`). R022 NetValue equals R025 NETAMOUNT, which is stored as `sales_lines.source_gross_amount`; live on 1 Oct the 407 WLMHW invoices with both sides match exactly (2,179,118.75 each, quantity 434). Status `MATCH`, `MISMATCH`, `ONE_SIDE`, or `GROSS_UNKNOWN` when any line's gross is NULL (v0 lines; 0 live). SELECT is granted to all application roles. |

### 5.4 `0041_landing_read_paths.sql` (phase P6, generated from the catalogue)

- **`v_current_<landing table>`**, one per `etp_r…`/`etp_landing_r…` table. It returns the landing rows of each CURRENT document's rows pointer (`rows_import_file_id`, `rows_block_no`), joined through `etp_import_content` on (file, sheet, row) with the document hash and disposition `K`, **virtual rows included**. Held rows (disposition `H`) are excluded. For report codes not yet upgraded, it falls back to rows of current files (`is_superseded=0`).
- **`v_fact_source_lineage`.** Path: fact type and id → `source_lineage` (file, sheet, row) → `etp_import_content` (block, document) → `import_file_blocks` (source export name, SHA, export time, basis) → for a virtual row, `import_file_block_copies` (the twin row) → `import_files` → evidence presence (`import_source_content`).
- **`v_import_outcomes`.** Planner-1 `import_row_outcomes` UNION ALL planner-2 `import_document_decisions`, per file and pass.
- **Reader switch.** The customer-name lookup `OperationalReportRepository.cs:195-197` reads `v_current_etp_r024` instead of `etp_r024` with `is_superseded=0 … ORDER BY etp_row_id DESC`.

### 5.5 `0042_document_locked_guards.sql` (phase P7, shipped in the same release as P4; Owner decision OD-5)

- `trg_import_files_protect_locked` (`0017:164-171`) and the 32 landing triggers (pattern `0018:73`, THROW 51243) are re-created to **skip the INSERT check** for rows of files with `planner_version=2`. UPDATE and DELETE checks are kept for all files.
- The fact-table triggers (`0009:4-82`, 51030–51035), `trg_sales_invoices_protect_locked` (`0010:202`) and `trg_source_lineage_protect_locked` (`0010:214`) are unchanged. They still protect every finalised fact.
- From this migration, planner 2 refuses nothing because of a locked day. It *holds* the changes dated on it (§8.6).
- **Why with P4.** Without it, the INSERT checks refuse any planner-2 file whose declared period contains a LOCKED day, and days are being finalised again after 1 Oct (runbook step 11). A two-year workbook would then fail as soon as one day in two years is finalised (R2).

### 5.6 Service Centre migration (phase P8)

- The Service plan's migration is rebased as `0043_service_centre_family_tables.sql`, keeping its D1–D3, D5–D11 and D13.
- Its D4 and D12 ("engine unchanged; snapshot families append a full copy per import day") are replaced by §7.5.
- S027 and S028 landing tables are created from the **raw** header: the consolidation-added columns `SourcePeriodFrom`, `SourcePeriodTo` and `SourceFile` become block metadata, not columns (§7.5).
- S001 gets no landing table (`Derived`, §7.5; OD-6).
- Optional in the same phase: `dbo.snapshot_row_spans` (§7.5).

### 5.7 Space and permissions summary

- New tables hold no customer values. The exception is `import_source_content`, which holds the bytes of files whose rows are already in the landing tables; it is denied to viewers and store managers.
- `fact_delta_json` and `summary_json` contain only Fact and Attribute field names and values, document numbers, product codes and dates.
- Document keys are financial year plus document number, dates or periods. A Descriptive column (customer, loyalty, GST number) can never be a key (catalogue test, §7.1), so no customer value or hash of one is stored anywhere new.

---

## 6. Describing a source: blocks

New code goes in `src/Etp.Reporting.Import/Sources/`:
- `SourceDescriptionReader`
- `ConsolidationContractReader` (P1: layout parse only) and `ConsolidationContractValidator` (P2)
- `LegacyInfoBlockReader` (P1)
- `HistorySheetBlockReader`, `SourceColumnBlockReader` (P8)
- `ExportNameParser`, `ExportOrder` (P1)
- `SnapshotDateResolver` (P1)

The output is a `SourceDescription` (kind, contract version, blocks, virtual rows, diagnostics).

### 6.1 Kinds of source, in order of detection

| Kind | Detected by | Blocks |
|---|---|---|
| `CONSOLIDATED` (contract v1) | `Info!A1` = `etp_contract` | Block table (contract §3.3). Any contract blocker refuses the workbook with stored diagnostics. |
| `CONSOLIDATED_LEGACY` | An `Info` sheet with a header row containing `Source file` and `Data row block`; or, for S027/S028, trailing consolidation columns (§7.5) | `LegacyInfoBlockReader` (§6.6) or `SourceColumnBlockReader` |
| `REVIEWED` | The Owner ticks "Import as reviewed file" | One `COMPLETE` block, export time `UNKNOWN` |
| `RAW` | Otherwise | One `COMPLETE` block over all rows; export time from the file name (§6.3) |

**Preflight changes** (`ImportPreflight.cs:40-41`), **from P1** for both planners:
- Skip the sheets `Info`, `ETP_Excluded` and `Snapshot History` as data sheets. Service plan D9 already planned the `Snapshot History` skip.
- When `Info!A1` = `etp_contract`, read the contract's header keys and block table (`ConsolidationContractReader`, layout only). Planner 1 uses it for three things only: the `family_code` tie-break (R001/R022, S003/R022, S006/R011) before `IdentifyName` (`EtpReportFamilyRegistry.cs:28-37`); the contract `store_code` when the data has none; and per-block snapshot dates for undated families (§6.4 tier 1). Planner 1 imports `Data` as today and ignores `ETP_Excluded`. An unreadable contract refuses the file with `CONTRACT_UNREADABLE`. From P2 the full validator runs for every contract workbook under either planner.
- In contract mode under planner 2, read `Snapshot History` through `HistorySheetBlockReader` with the declared trailing columns removed before staging. `ImportRowStager.cs:29-35` must accept the columns named in `history_extra_columns` for that sheet only.
- **Consolidation columns** (P8): when a header ends with any of `SourcePeriodFrom`, `SourcePeriodTo`, `SourceFile` (S027, S028 today), the preflight strips them before the signature (`ImportProfileMatcher.cs:22-30`) is computed and hands them to `SourceColumnBlockReader`. The raw export and the consolidated workbook then have the same signature and the same fact rows.

### 6.2 Raw exports

| Field | Value |
|---|---|
| Block | One block: `sheet` = the matched sheet, rows 2..n, `COMPLETE` |
| `source_sha256` | The workbook SHA-256 (`OpenXmlWorkbookReader.cs:47`) |
| `export_time` | From §6.3 |
| Coverage, declared | When a parent folder or ZIP name matches `d MMMM yyyy TO d MMMM yyyy` (e.g. `TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026`) |
| Coverage, observed | Otherwise, min..max of the row dates |
| `content_sha256` | §6.8 |

### 6.3 Export names and the order of exports (P1)

`ExportNameParser.Parse(fileName)` returns `(instant, basis)`:

| Pattern (file name, without folder) | Basis | Example |
|---|---|---|
| `^(\d{12})_` → `yyyyMMddHHmm` | `MINUTE` | `202609291449_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx` |
| `_(\d{14})\.(xlsx\|csv)$` → `yyyyMMddHHmmss` | `SECOND` | `ClosingStock_20260901133606.csv` |
| `(?<!\d)(\d{2})\.(\d{2})\.(\d{4})(?!\d)` → `dd.MM.yyyy` | `DATE` | `PENDING REPAIR 29.09.2026.csv` |
| otherwise | `UNKNOWN` | `R025_SDB_VariantwiseSales.xlsx` |

All times are IST. They are never converted and **never estimated**.

**`ExportOrder.Compare(a, b)`** returns `NEWER`, `SAME`, `OLDER` or `UNKNOWN`:
- `UNKNOWN` if either side is `UNKNOWN`.
- If either side is `DATE`: compare the dates. Equal dates give `UNKNOWN`.
- Otherwise compare the instants. An equal minute gives `SAME`.

### 6.4 Snapshot dates (P1)

This applies to undated families: R010, R023, SOR_AGEING and the undated Service families. It replaces `ImportScope.cs:33-35`, which takes the **maximum** of every date token in the path and Info.

`SnapshotDateResolver` takes the **first tier that yields any date**:
- One distinct date in that tier → use it.
- More than one distinct date → refuse `SNAPSHOT_DATE_AMBIGUOUS`. Never fall through to the next tier, and never take a maximum.
- No tier yields a date → refuse `SNAPSHOT_DATE_UNKNOWN`.

| Tier | Source | Basis |
|---|---|---|
| 1 | Contract block `snapshot_date` | `CONTRACT` |
| 2 | Legacy Info block table that tiles (§6.6): each block's source-file export date | `INFO_BLOCK` |
| 3 | `Snapshot History` rows without a contract: group by `Snapshot_As_Of` (+ `SourceFile`) | `HISTORY_ROWS` |
| 4 | The workbook's own export name (§6.3) | `EXPORT_NAME` |
| 5 | Info `Coverage` value when it is a single date, or the date in a single block's disposition (`Snapshot 07-Sep-2026 retained`). Free-text notes are never read. | `INFO_COVERAGE` |
| 6 | Parent folder or ZIP names (e.g. `till 29 sep 2026`, Service plan D5). Warning `SNAPSHOT_DATE_FROM_FOLDER`. | `FOLDER` |
| 7 | Siblings in the same folder agree on one end date (today's `FolderImportService.cs:104-105` fallback, with the maximum removed). Warning. | `SIBLING` |

- **R011** has a `Date` column. Under planner 2 its rows are partitioned by `Date`, one snapshot per distinct date, with basis `COLUMN`.
- Under **planner 1** (P1), a CLOSING_STOCK file with more than one `Date` is refused with `SNAPSHOT_MULTIPLE_DATES`. This replaces today's misleading legacy or period refusal (IF-022).
- **In P1 the resolver also feeds planner 1.** `ImportScope.Detect` returns `SnapshotBlocks` (row range → date) for undated families. `EtpFamilySqlImportOrchestrator.cs:21` stamps each R010 row with its block's date instead of `scope.BusinessDate`. The declared period becomes min..max of the block dates.

### 6.5 Contract v1 and virtual rows

- Read and validated exactly as `CONSOLIDATION-CONTRACT.md` sections 3–8 describe. One class, `ConsolidationContractValidator`, serves the importer and `ImportAudit --validate-contract`.
- **Block numbers are append order.** Blocks are ordered for every decision by export time (§6.3), never by block number. An older raw export found later is appended as a new block with its own, earlier, export time (contract rule 10).
- **Virtual rows.** Each `ETP_Excluded` map row is one row of the export that the builder left out because it repeated, exactly, a physical row of a lower-numbered block (its *twin*). The importer turns each map row into a **virtual row** of its block:
  - it is staged with the twin's values (timestamps included, because they are `Ignored` in every comparison);
  - it is landed through `append_<table>` like any row, with its own lineage row (`<Family>_SOURCE`, sheet `ETP_Excluded`, row = the map row's sheet row) and its own `etp_import_content` row;
  - the map is stored in `import_file_block_copies` (block, map row, twin sheet and row);
  - a fact made from a virtual row has the virtual row's lineage, so lineage names the export that actually contained it, and a copy that `UQ_source_lineage` (`0002:16`) and the fact tables' unique lineage (`0002:38`, `:62`, `:76`) would otherwise reject never needs a shared lineage row.
- **Rebuild.**
  - `COMPLETE`: its physical rows.
  - `DELTA`: physical rows plus its virtual rows.
  - `TRIMMED`: physical rows plus its virtual rows (the map is required whenever `excluded_rows > 0`, contract §3.3). The rows a later build removed are missing, so a trimmed block never gets a `content_sha256` and never takes part in the absence check; but for every document it still holds in full it is a normal observation.
  - `LEGACY`: physical rows only.
- **Rule `current`.** The `Data` block plus one block per `Snapshot History` block.
- **Re-projection** (approval apply, re-decide, upgrade) reads the landed physical and virtual rows of the file, so the rebuilt multiset is always available from the database.

### 6.6 Legacy consolidated workbooks (today's package)

**`LegacyInfoBlockReader`:**
1. Collect every row under every header row that contains `Source file` and `Data row block`. Info is appended per update, so headers repeat. Remove exact duplicate rows.
2. Parse `a:b` ranges and the 12-digit source-file prefix.
3. **Tiling test.** The ranges must partition `2..last Data row` with no gap or overlap, and `Rows retained` must add up to the Data rows. If either fails: warning `INFO_BLOCKS_UNUSABLE` and one `WHOLE_FILE` block.

Observed on 1 Oct:
- WLMHW R010 tiles: 2:845, 846:1698, 1699:2403.
- HEMW R010 tiles: 2:467, prefix 202609071457.
- WLMHW R025, R003 and R013 tile (verified by judge 1).
- HEMW R025 does not tile: rows 2:684 have no block.
- WLMHW R030 does not tile: its ranges are stale and overlapping.
- No Service Info table tiles.

**How legacy blocks are used:**
- **Snapshot families.** Each tiling block is one snapshot, dated by tier 2.
- **Transactional families.** Blocks are registered for lineage (`LEGACY` completeness). The builder left out exact cross-export repeats without a map, so a later legacy block may hold only part of a document. The importer therefore builds one observation per document (`LEGACY_MERGE`):
  1. Inside each block, apply the row rule's descriptive collapse (§7.2).
  2. Order the blocks that hold the document by export time (unknown last).
  3. **Consistent document**: every later block holds only fact rows that some earlier block also holds (what exclusion of exact repeats produces). Take each distinct fact row with the **largest** count it has in any one block (never the sum). Keep those copies from the latest block that holds them; mark the others `C` (stale copy) or `R` (older block).
  4. **Inconsistent document**: some later block holds a fact row that no earlier block holds. Without a map the importer cannot tell a grown invoice from a re-stated line, and taking both would double a re-stated line. The document is **held**: item `LEGACY_BLOCKS_DIFFER` (action INSERT for a document not yet stored, REPLACE otherwise), proposing the merged observation of step 3, with the per-block rows in the diff. The Owner approves, keeps current, or uses "Restate dates…" with a checked file. Nothing is applied automatically.
  5. If the merged observation equals the stored facts, the decision is PRESENT whatever step 4 found.
  6. The merged observation's export time is `UNKNOWN`, so any other difference from stored facts also goes to review (§8).

  The rest of the import works the same on a `WHOLE_FILE` block, which is one block and therefore never inconsistent.

### 6.7 Resolution inside one source

This step is pure C# and runs before the decision engine. Per document:
- **Authoritative observation.** The observation of the block whose export time is greatest by `ExportOrder`. `UNKNOWN` ranks below every known time.
- **Two blocks with the `SAME` time but different content** → `IN_SOURCE_CONFLICT`. The document is held, as an item with action NONE, and is not applied.
- **Earlier blocks.**
  - Identical facts → decision `ATTESTED_IN_SOURCE`.
  - Different facts → decision `RESTATED_IN_SOURCE`, with rows marked `R`.

  Both are logged; neither needs approval.
- **Absence inside the source.** A document present in an earlier block but missing from a later `COMPLETE` or `DELTA` block of the same source is decided by the absence rule (§8.4): rule 3 raises a review item, so the Owner sees it.

### 6.8 Export identity and skipping exports already imported

- **`content_sha256`** = SHA-256 over the sorted list of row hashes of the **rebuilt** block (physical plus virtual rows). A row hash is the canonical hash of every column except `Ignored` ones (§7.1). It is `NULL` for `TRIMMED` and `LEGACY` blocks. **`content_hash_version`** records the canonical rules used; it is raised whenever the canonicaliser, the staging type list (`ImportRowStager.cs:71-72`) or a role that affects the hash changes.
- **`export_key`** = SHA-256 of `REPORT|STORE|instant|basis|period_from..period_to or snapshot date|normalised report name`. The instant must be known. The period is part of the key, so two exports taken in the same minute with different selections are two exports.
- **Registration in the import transaction:**
  - **ATTESTED.** A new block is registered `ATTESTED`, pointing at an earlier block A, and is **not landed and not re-decided**, only when all of these hold:
    - A is `APPLIED` and matches by `source_sha256` or `export_key`;
    - both have `content_sha256`, with the **same** `content_hash_version`, and the hashes are equal;
    - A's file was last decided (latest `import_decision_passes` row) under the family's **current** `RulesetVersion`;
    - A's file has no change item in status OBSOLETE that was not re-decided since;
    - the family's upgrade state is `DONE`.

    Otherwise the block is landed and decided like a new block. Re-processing an export whose earlier decision is still valid cannot change state (proof in §8.7).
  - **Conflict.** Only when both blocks are rebuildable, their hash versions are equal and their content hashes differ → refuse `EXPORT_CONTENT_MISMATCH`: the builder wrote a block that is not the export it names. When the hash versions differ, the block is simply landed and decided.
- **Effect.** Re-importing a contract workbook that gained one block lands only the new block. Re-importing an unchanged workbook is caught earlier as `Duplicate`, by SHA (`FolderImportService.cs:111-120`), unless it needs re-deciding (§6.9).

### 6.9 Re-deciding a source

A stored planner-2 file must be decided again when the rules changed after it was decided, when its change items became OBSOLETE (51722), or when a family returns to planner 2 after a switch back. Re-importing the same bytes would only give `Duplicate` or `ATTESTED`, so there is an explicit path:
- **Action.** Imports → History → **"Re-decide this source"** (Store Manager or Owner), and automatically when the exact-duplicate check (§9 step 7) finds a planner-2 file that needs it (result `Re-decided`).
- **Work.** One transaction under the applock: `begin_decision_pass` (kind `REDECIDE`), `begin_import_apply_context`, re-project the file's landed physical and virtual rows with the current rules, `read_document_state`, decide (§8), apply AUTO changes and NEW documents, create REVIEW items, record the decisions under the new pass number, `end_import_apply_context`, commit. No row is landed again.
- **Upgrade gate.** As for an import, the family's upgrade state must be `DONE` with the current `RulesetVersion`.

---

## 7. Identity and content rules per family

### 7.1 Catalogue schema, roles and the canonicaliser

`EtpReportFamilies.json` and `EtpReportFamilyRegistry.cs:8-18` gain these fields.

**`EtpSourceColumn`** gains `Role`:

| Role | Meaning | Default |
|---|---|---|
| `Key` | Part of the document key; also part of every fact row | — |
| `Fact` | A change needs review (except under the automatic rules of §8) | **default** for every column not listed |
| `Attribute` | Master, reference or status data stored with facts (brand, HSN, segment, REF_DOCUMENT…, CN redeemed date, job status). Latest known export wins, **archived and audited**, no approval (OD-3). Held on locked days. | — |
| `Descriptive` | Customer, loyalty and store description. Never in identity or change detection, and never a Key. Latest export wins on the landing rows pointer. | — |
| `Label` | ETP's own year labels (INVOICEYEAR, REFERENCEYEAR). Kept, never compared. Information diagnostic when they disagree with the identity. | — |
| `Ignored` | `*timestamp*` columns | default for names containing `timestamp` |

**`EtpReportFamily`** gains:
- `BusinessUnit`: `RETAIL` or `SERVICE` (Service plan D10).
- `Derived`: `true` for a family that is a consolidation-made union with no raw export (S001, §7.5). A derived family is reported `Not needed` with information `FAMILY_DERIVED`.
- `ConsolidationColumns`: trailing columns a legacy consolidated workbook may add (S027, S028: `SourcePeriodFrom`, `SourcePeriodTo`, `SourceFile`).
- `RawNamePatterns`: for Service families, the ETP report names their raw files carry (needed before raw Service import, OD-6).
- `Identity`, with these fields:

| Field | Values |
|---|---|
| `Scope` | `Document`, `Date`, `Snapshot` or `Period` |
| `DocumentKey` | Canonical fields (Document scope) |
| `YearRule` | `FinancialYearOfPrimaryDate` or `None` |
| `RowRule` | `Multiset`, `SingleRowPerDocument`, `StockUnitChain` or `SnapshotItems` |
| `RowKey` | SnapshotItems: fields that pair rows between readings |
| `ChangePolicy` | `Review` or `LatestReadingWins` |
| `SnapshotDate` | `Column:<field>` or `Block` |
| `LegacyNullable` | Canonical fields that may be NULL on v0 facts |
| `Route` | `Sales`, `Revenue`, `Enrichment`, `StockMovement`, `StockSnapshot` or `Landing` |
| `RulesetVersion` | Integer |

- **Routing.** `SqlServerImportPersistenceUseCase.SelectRoute` (`:117-124`) becomes data-driven from `Route`.
- **Catalogue test.** `EtpReportFamilyCatalogueTests` pins, for every family: the Identity, every named field existing in `Columns`, **no Key field being Descriptive**, and the exact Descriptive and Attribute lists. **Any role or identity change raises `RulesetVersion`**, which marks that family's ledger `STALE` until the upgrade re-synchronises it (§13) and makes earlier decisions eligible for re-deciding (§6.9).

**One canonicaliser:** `FactCanonicalizer` in `src/Etp.Reporting.Import/Identity/`. `EtpInvoiceIdentity.ContentHash` (`EtpInvoiceIdentity.cs:19-32`) moves there unchanged: sorted keys, `len:key:len:value`, decimals rounded to 4 places and printed `0.####`, dates `yyyy-MM-dd`, strings trimmed. The `state_code` normalisation of `PhaseOneImportPersistence.cs:97-98` is kept. Import, re-decide, approval apply and upgrade use the same class; SQL never rebuilds a tuple.

| Hash | Over |
|---|---|
| `content_key` (unchanged) | Every staged field except Ignored, plus `:n` (`PhaseOneImportPersistence.cs:86-104`). Planner 1 keeps using it. |
| `fact_row_hash` | Key + Fact fields of one row |
| `attribute_hash` | Attribute fields of one row |
| `document_key_hash` | SHA-256 (UTF-8) of `REPORT\|STORE\|document key text` (ReportCode, §0) |
| `fact_sha256` (version) | SHA-256 of the sorted `fact_row_hash` list of the observation **after the row rule**, with multiplicity. Row order never matters. |
| `canonical_sha256` (version) | The same, over the canonical projection rows the typed fact tables store (typed families only). Used when a version's basis is `CANONICAL_ONLY` or `UNVERIFIED`. |

**Document key text:**

| Scope | Key text |
|---|---|
| Document | `{FY}\|{UPPER(TRIM(number))}`, where FY = `EtpInvoiceIdentity.FinancialYearEnd(date)` (`EtpInvoiceIdentity.cs:9`) |
| Date | `yyyy-MM-dd` |
| Snapshot | `yyyy-MM-dd\|{ReportCode}` |
| Period | `yyyy-MM-dd..yyyy-MM-dd` |

**Fact labels stay planner-1 compatible.** Planner 2 labels the facts it inserts exactly as planner 1 would for the same rows, so either planner recognises the other's facts (§13.6):
- sales lines: `line_identifier` = `EtpInvoiceIdentity.LineKeys` (`EtpInvoiceIdentity.cs:34-50`) computed over the **kept** rows of the authoritative observation (today over all rows of the file, `R025SqlImportOrchestrator.cs:46`), so a stale copy can never take `:1`;
- enrichments: `content_key` = `LineKeys` over the kept rows, as `RetailEnrichmentSqlImportOrchestrator.cs:54` does today; `UX_enrichment_content` (`0017:18-19`) is unchanged;
- stock movements and snapshots: `line_seq` from the P1 sequencers;
- landing rows: `content_key` with today's formula.

### 7.2 Row rules (inside one block, before resolution)

| Rule | Behaviour |
|---|---|
| `Multiset` (R025, R003, R013, landing families) | Group rows by (document, `fact_row_hash`) and partition each group by its Descriptive values. The multiplicity is the **largest** partition. That partition's rows are kept (`K`), preferring the one holding the highest sheet row. Other rows are `C` (`STALE_COPY_COLLAPSED`, information with a count). Customer fields are invoice-level in ETP, so one genuine export cannot carry two different ones in one invoice. Genuine repeats (identical descriptive values) keep their full count. **Timestamps are `Ignored`, never Descriptive.** Pinned by the 29 Aug WLMHW fixture: 4 groups of 5 lines that differ only in STORETIMESTAMP must stay 5 each. |
| `SingleRowPerDocument` (R022) | Rows of one invoice that differ only in Descriptive or Ignored fields collapse to the last row (`C`). Rows that differ in a Fact → `IN_SOURCE_CONFLICT`, and the document is held. |
| `StockUnitChain` (R030) | Rows of one (document, date, product, type, from, to) get `line_seq` by running-balance order: by opening quantity, descending when `transaction_quantity < 0` and ascending otherwise; then by closing quantity in the same direction; then REF_DOCUMENTNUMBER, REF_DOCUMENTDATE, source row. Rows equal on every Key and Fact field are **kept** with consecutive `line_seq` and raise the warning `STOCK_ROW_REPEATED` (a per-unit row normally moves the balance, so the Owner should see it; nothing is dropped). `StockUnitSequencer` is shared by both planners from P1. In the package's 26 per-unit groups (104 rows) every opening is distinct and file order is not monotone (sample openings 0/1/3/4/2), so `line_seq` does not depend on row order. |
| `SnapshotItems` (R011, R010, snapshot landing and Service families) | Rows keep their multiplicity. `line_seq` comes from `RowKey` ordered by the Fact values, then source row (`SnapshotItemSequencer`, P1). Repeated identical rows are stored, never collapsed. |

### 7.3 Core Retail families

Columns are canonical names from `EtpReportFamilies.json`.

| Family (ReportCode) | Scope, key, row rule, policy, route | Fact | Attribute | Descriptive | Label / Ignored |
|---|---|---|---|---|---|
| **R025** | Document `FY(transaction_date)\|invoice_number`; Multiset; Review; Sales | `source_transaction_type`, `product_code`, `transaction_date`, `source_quantity`, `source_ucp`, `source_gross_ucp`, `scheme_discount`, `user_discount`, `helios_creditnote`, `promo_gc`, `netgross`, `pre_discount`, `source_net_amount`, GST/CESS % and values, `source_tax_amount`, `source_net_value` | `hsn_code`, `source_brand_code`, `source_brand_name`, `brand_segment_code`, `gender_code`, `reference_invoice_number`, `reference_invoice_date` | `storename`, `storetype`, `channel`, `region`, `city`, `customer_name`, `customer_phone`, `ulpnumber` | Ignored: `source_store_timestamp` |
| **R022** | Document `FY(transaction_date)\|invoice_number` (**not** INVOICEYEAR); SingleRowPerDocument; Review; Revenue | `source_transaction_type`, `transaction_date`, `source_invoice_quantity`, every `tender_*`, `source_net_value` | `reference_invoice_number` | `store_name`, `store_type`, `channel`, `region`, `state`, `city`, `customer_name`, `customer_phone`, `encircle` | Label: `invoice_year`, `referenceyear`; Ignored: `source_store_timestamp` |
| **R013** | Document `FY(transaction_date)\|invoice_number`; Multiset; Review; Enrichment | `source_transaction_type`, `product_code`, `transaction_date`, `cro_number`, `source_quantity`, `ucp`, `grossucp`, `scheme_discount`, `netgross`, `pre_discount`, `source_net_amount`, `source_net_value` | `brand`, `brandname`, `cluster`, `gender`, `invrefno`, `invrefdate` | store columns, `cro_name`, `customer_name`, `customer_phone` | — |
| **R003** | Document `FY(transaction_date)\|invoice_number`; Multiset; Review; Enrichment | `source_transaction_type`, `product_code`, `transaction_date`, `source_quantity`, `ucp`, `grossucp`, `scheme_discount`, `netgross`, `user_discount`, `other_charges`, `source_net_amount`, `tax`, `source_net_value` | `brand`, `brand_name`, `cluster`, `gender`, `activation_details`, `user_discount_details`, `invoice_ref_no`, `invoice_ref_date` | store columns, `customernumber`, `customer_name`, `customer_phone`, `ulp_no` | Ignored: `eastimestamp`, `storetimestamp` |
| **STOCK_LEDGER** (R030) | Document `FY(document_date)\|document_number`; StockUnitChain; Review; StockMovement | `source_transaction_type`, `product_code`, `document_date`, `from_location`, `to_location`, `opening_quantity`, `transaction_quantity`, `closing_quantity` | `hsn_code`, `brand`, `brandname`, `cluster`, `gender`, `ref_documentnumber`, `ref_documentdate` (ETP re-stated these on 578 + 302 rows) | `store_name`, `city`, `state`, `location` | — |
| **CLOSING_STOCK** (R011) | Snapshot `snapshot_date` = `Column:snapshot_date`; SnapshotItems, RowKey (`product_code`, COALESCE(`source_uid`,`batch_number`,`ean`)); LatestReadingWins; StockSnapshot (`source_report_code='CLOSING_STOCK'`) | `product_code`, `ean`, `batch_number`, `source_uid`, `quantity`, `unit_cost`, `total_cost` | `hsn_code`, `brand_code`, `cluster`, `gender` | store columns, `itemdescription` | — |
| **R010** | Snapshot, date = `Block` (§6.4); SnapshotItems, RowKey (`itemnumber`, COALESCE(`uid`,`lotnumber`)); LatestReadingWins; StockSnapshot (`'R010'`) | `itemnumber`, `lotnumber`, `uid`, every bin column, `ucp`, `closingbalance`, `totalucp` | `hsn_code`, `brand`, `brandname`, `cluster`, `gender`, `ean_category` | store columns | — |

**Notes on the core families:**
- **Invoice-year rule (IF-019; Owner decision OD-1).** `EtpInvoiceIdentity.FinancialYearEnd(date, values)` (`EtpInvoiceIdentity.cs:11-17`) returns the staged `invoice_year` when one is present and the FY of the date otherwise. R025 (`R025SqlImportOrchestrator.cs:53`) and R022 (`R022SqlImportOrchestrator.cs:78`, `:89`) both call it; R025 gets the FY of its date only because R025 has no INVOICEYEAR column. The overload stops reading `invoice_year`, so every caller uses FY(date). When INVOICEYEAR differs from the FY of the date, the importer records the information diagnostic `INVOICE_YEAR_DIFFERS` with a count and the row numbers. Evidence:
  - the package R022 has exactly 3 such rows (1041, 3558, 3559), all 1-April returns;
  - under FY(date) + number, the only remaining repeat is the 4847/5032 pair, which differs only in ContactNo and collapses;
  - live, 0 `etp_r022` rows and 0 invoices differ.
- **Tax.** R025 tax becomes a compared Fact. Today `persist_sales_line` omits it (`0017:50`). Live v1 facts already carry tax; v0 facts are `LegacyNullable` (`source_gross_amount`, `source_tax_amount`), which FILL handles.
- **Shared invoice headers.** The header `sales_invoices (store, FY, number)` is shared by R025 and R022. A NEW R025 or R022 document whose header exists with a **different** `transaction_date` is held (`HEADER_DATE_MISMATCH`, action NONE). It is never merged and never silently re-dated. A header with no line, control or tender (an **orphan**, left by a restatement that deleted its facts, brief §1.10.4) does not count: it is archived and deleted in the AUTO set before the NEW document is inserted (0 orphans live on 1 Oct). Planner 2 never leaves an orphan behind (§5.3, `archive_document_facts_internal`).

### 7.4 Other Retail (landing-only) families

- **Dated families** (R001, R002, R004–R009, R012, R014–R021, R024, R026–R029, R031). Default:
  - `Scope=Date` (one document per report, store and business day);
  - `RowRule=Multiset`, `ChangePolicy=Review`, `Route=Landing`.

  Late rows on a day are handled by GROW, and a day exported before it ended by the provisional rule (§8.3). This needs no natural key.
- **Rows without a usable date.** A row whose primary date is blank or does not parse (Helios R012: 2 of 37 rows, package README) belongs to no document. It is landed for evidence with disposition `H` (held), counted in the warning `ROW_DATE_MISSING` with its row numbers, listed on Imports → Problems, and excluded from `v_current_*`. It is never silently dropped and never attached to a guessed date. The Owner corrects it with a `REVIEWED` file if it matters.
- **Undated families** (R023, SOR_AGEING): `Scope=Snapshot`, date by `Block`, `LatestReadingWins`.
- **Upgrading a family to `Scope=Document`** is allowed only after `FactIdentityCorpusTests` proves the candidate key unique in every raw corpus export (ordinals allowed). Candidates to test:
  - R001 and R024: FY + invoice number;
  - R005: FY + `docno`;
  - R006: FY + `docnumber`;
  - R012: `creditnotenumber`.

  Keys may never include a Descriptive column, so loyalty numbers (`encircle`, `encircle_no`, `encircle_number`) cannot be part of a key; R015 and R016 stay `Date` scope.
- **Roles.** The generator proposes them by pattern, and the PR pins them with the catalogue test:
  - store columns, `customer*`, `*phone*`, `*contact*`, `ulp*`, `*gstin*`, `*gstn*` (R018/R019 `issue_gstn_no`, `recipient_gstn_no`), `encircle*` (R001/R022 `encircle`, R015 `encircle_no`, R016 `encircle_number`), `*address*`, `*email*` and `cro_name` → Descriptive;
  - `*timestamp*` → Ignored;
  - R012 `REDEEMED DATE/BY` and `ISSUED_CN*` → Attribute.

### 7.5 Service Centre families (phase P8)

| Group | Families (Service plan §4.2, §5) | Identity |
|---|---|---|
| Row-date | S003, S004, S019, S023–S026, S039, S040 | `Scope=Date` on the plan's primary date; Multiset; Review; GROW and provisional as §8. **No natural key**: S003 `Transaction ID` is blank in all 4,968 rows. |
| Snapshot and current | S005, S006, S009, S010 and any undated family exported as one "as of" list | `Scope=Snapshot`, date by `Block` (contract, History rows, export name, Info coverage, then folder, §6.4); SnapshotItems with RowKey = all Fact fields (key-free); LatestReadingWins |
| Period exports | Undated job or status lists exported per selected period (the builder's `transactional` label today), e.g. S002, S007, S008, S011–S018, S020–S022, S027–S037. The catalogue decides. | `Scope=Period` (block `period_from..period_to`); Multiset; LatestReadingWins. A block whose period **partly** overlaps a stored, different period document → refused `PERIOD_OVERLAP_UNRESOLVED` with diagnostics. A block that **covers** stored period documents supersedes them by the latest-reading rule when newer, or by review when the order is unknown. Without a contract (legacy whole file) these families fall back to `Snapshot` dated by folder (Service plan D5). |
| Derived | **S001**: a consolidation-made union of the ten RepairRegister status views (S014–S018, S031–S035) with no raw export (`SERVICE-CENTRE-IMPORT-BRIEF.md:35-36`, `SERVICE-CENTRE-IMPORT-DESIGN.md:44`) | `Derived=true`: reported `Not needed` (`FAMILY_DERIVED`), never imported, so its rows can never double the views it was built from (OD-6c). |

**Rules that apply to every Service family:**
- **Status columns** (S002 `Current Status` and similar) are `Attribute` in Snapshot and Period documents. A status change never conflicts.
- **Customer columns** are `Descriptive`: CustomerName, MobileNumber, Email, EndCustomer*, Cust Name, Name, CustomerMobile, CustomerEmail.
- **Store.**
  - Service plan D3 (no `dbo.stores` row for AW330) and D8 (name ties) are kept.
  - Endpoint store columns are never `store_code` (plan §4.2).
  - The contract `store_code` settles store detection for S011 and S013.
- **S027 and S028 consolidation columns.** Today's consolidated headers add `SourcePeriodFrom`, `SourcePeriodTo` and `SourceFile` (`SERVICE-CENTRE-IMPORT-DESIGN.md:42`), which raw exports cannot carry. The catalogue signature for S027/S028 is the **raw** header. Without a contract, `SourceColumnBlockReader` strips the three columns and groups rows into blocks by (`SourceFile`, `SourcePeriodFrom`, `SourcePeriodTo`): origin `SOURCE_COLUMNS`, period basis `DECLARED`, export time from the `SourceFile` name grammar, completeness `LEGACY`. A contract workbook must not carry them at all (contract §2); each source file is a block. Both routes therefore give the same signature and the same fact rows.
- **Raw Service files.** Raw names carry no `Snnn` code, and five signature groups tie ({S001, S014–S018, S031–S035}, {S007, S008}, {S011, S038}, {S024–S026}, {S039, S040}; `SERVICE-CENTRE-IMPORT-DESIGN.md:37`). Raw Service import therefore needs `RawNamePatterns` per family, the CSV reader below and the export-name grammar (§6.3), which gives `SECOND` or `DATE` bases. A `DATE` basis orders exports across days but not within a day (`UNKNOWN`), so same-day differences go to review.
- **R1 for Service, stated plainly.** Until OD-6(a) is decided and that work is done, Service Centre data comes in **only through consolidated workbooks**. The mechanism is the same, but the raw route is not yet available.
- **CSV.** Service raw packs are CSV (`PENDING REPAIR 29.09.2026.csv`). A CSV reader (`CsvWorkbookReader`, producing the same `WorkbookSnapshot`) is part of P8 only if the Owner says raw Service packs must be imported (OD-6a).
- **Storage.** Each snapshot document stores its rows. A daily full refresh would grow about 4–5 GB a year (Service plan D12, §14.2). P8 adds key-free **`snapshot_row_spans`**: (report, store, `row_hash binary(32)`, occurrence, `etp_row_id`, `first_snapshot_date`, `last_snapshot_date`), with PAGE compression.
  - A new snapshot extends the spans of unchanged rows and lands only new or changed rows.
  - An out-of-order (older) snapshot falls back to full landing.
  - `v_snapshot_rows_as_of(@report, @store, @date)` reads the spans.
  - Until growth is measured, weekly refresh is recommended (OD-6b).

---

## 8. The decision engine

`DocumentDecisionEngine.Decide` is a **pure C#** function in `src/Etp.Reporting.Import/Documents/`. It is tested by table, with no SQL. Its inputs are the incoming authoritative observations (§6.7, with legacy holds from §6.6), the stored state read by `read_document_state`, the blocks for the absence check, the locked days, and the importing user's role.

### 8.1 Notation

| Symbol | Meaning |
|---|---|
| `O` | The incoming authoritative observation for document `d`, from block `b` |
| `T(b)` | The export time of block `b`, with its basis |
| `cur` | The CURRENT version of `d` |
| `cur.T` | The latest known time among the exports that attested `cur` (`last_attested_time` and basis) |
| `Ord` | `ExportOrder.Compare(T(b), cur.T)` |
| Facts equal | Same `fact_sha256`, or same `canonical_sha256` when `cur.basis` is `CANONICAL_ONLY` or `UNVERIFIED` (projecting `O` to canonical rows) |
| `L(d)` | A day of `d` is LOCKED: the document date, the snapshot date, or any day of a Period document |
| `Policy` | `Review` or `LatestReadingWins` |
| `Typed(d)` | `d` belongs to a typed family (§0), so its facts live in typed tables |
| `Owner` | The importing user is in `etp_owner` (or sysadmin) |
| `A(d)` | The absence condition of §8.4 |

### 8.2 Rules, evaluated in order. The first match wins.

| # | Situation | Decision | Effect | Owner? |
|---|---|---|---|---|
| 1 | `O` equals the PENDING version of `d` | `PENDING_EXISTS` | Attest the pending version | (already queued) |
| 2 | `O` equals a REJECTED version of `d` | `REJECTED_BEFORE` | Record only | No |
| 3 | No current facts (no `fact_documents` row, or status `NOT_ADDED`, `PENDING`), and **A(d)** holds (§8.4) | `NOT_ADDED` | Document status NOT_ADDED; nothing inserted; **review item** `MISSING_FROM_LATER`, action INSERT, proposing `O`. Approve inserts it; Keep current leaves it out; Reject records `O` as REJECTED. | Yes |
| 4 | No current facts; a non-orphan header exists with a different date (sales or revenue) | `HELD_HEADER_DATE` | Item `HEADER_DATE_MISMATCH`, action NONE | Resolve |
| 5 | No current facts; `L(d)` | `HELD_LOCKED` | Item `NEW_ON_LOCKED_DAY`, action INSERT, applicable after the day is reopened | Yes |
| 6 | No current facts; status RETIRED and `Ord`=NEWER | `PENDING_CHANGE` | Item `REAPPEARED`, action INSERT | Yes |
| 7 | No current facts; status RETIRED, otherwise | `STALE` | Record only | No |
| 8 | No current facts, otherwise (this includes a NOT_ADDED document now seen in an export newer than every export that lacked it) | `NEW` | Facts inserted; version CURRENT (NEW). Any pending INSERT item of `d` becomes OBSOLETE. | No |
| 9 | Facts equal | `PRESENT` | Attest, and raise `last_attested`. Then: (a) Attribute hash differs and (`Ord`=NEWER, or `cur.T` unknown with `T(b)` known) → `ATTRIBUTE_UPDATED` (AUTO, new version ATTRIBUTE, archived; held if `L(d)`). Otherwise information `ATTRIBUTE_NOT_APPLIED`. (b) Move the rows pointer when `T(b)` is newer than the pointer's time, or the pointer is unknown and `T(b)` known, or both unknown (import order; descriptive values only) → `REFRESHED`. (c) `cur.provisional` and `T(b)` known with export date > document date → settle (`provisional=0`). | No |
| 10 | `cur.basis=CANONICAL_ONLY`; differences only in `LegacyNullable` columns where `cur` is NULL | `FILLED` | AUTO, archive then UPDATE in place, new version FILL; held if `L(d)` | No |
| 11 | `Ord`=OLDER | `STALE` | Record only. An older export never changes a newer one. | No |
| 12 | Scope Date or Period (landing-only); `O` ⊇ `cur` (multiset); `Ord`=NEWER, or `cur.T` unknown with `T(b)` known | `GROWN` | AUTO, new version GROW, rows pointer moves to `b`; held if `L(d)` | No |
| 13 | `cur.provisional`; `Ord`=NEWER; `exportDate(b) ≤ document date + 1 day` | `PROVISIONAL_UPDATED` when not `Typed(d)`, or `Typed(d)` and `Owner`; otherwise `PENDING_CHANGE` | AUTO REPLACE, archived / REVIEW item reason `PROVISIONAL`, shown as "safe: the day was not over"; held if `L(d)` | Only for a typed family imported by someone other than the Owner (OD-7) |
| 14 | `Policy`=LatestReadingWins and `Ord`=NEWER | If a `RowKey` of `cur` is missing from `O`: `PENDING_CHANGE` (item `SNAPSHOT_SHRINK`). Otherwise `READING_UPDATED` when not `Typed(d)`, or `Typed(d)` and `Owner`; else `PENDING_CHANGE` (item `READING`, "safe: newer reading, nothing removed") | AUTO REPLACE, archived / review; held if `L(d)` | Shrink always; typed reading when not the Owner (OD-7) |
| 15 | Otherwise | `PENDING_CHANGE` | Item REPLACE, reason `LATER_EXPORT` (NEWER), `SAME_EXPORT_DIFFERS` (SAME), `UNKNOWN_PROVENANCE` (UNKNOWN) or `LEGACY_BLOCKS_DIFFER` (§6.6); HELD if `L(d)` | Yes |

**Results the engine also produces:**
- `IN_SOURCE_CONFLICT` from §6.7, held.
- `LEGACY_BLOCKS_DIFFER` from §6.6, held (rule 15 or, for a document not yet stored, an INSERT item).
- `REINTERPRETED` (§8.5).
- `MISSING_FROM_LATER` RETIRE items (§8.4).

**Every change creates a version.** NEW, REPLACE, FILL, GROW, ATTRIBUTE, PROVISIONAL, READING and TRIM each create a new version (previous CURRENT → SUPERSEDED). Members of the new version are the facts that represent it: facts kept through the change (FILL and ATTRIBUTE keep every fact, updated in place) are listed again under the new version, and inserted facts are added. Retiring a document sets its CURRENT version to SUPERSEDED and the document to RETIRED; no version is created.

### 8.3 Provisional days

A version is **provisional** when every export that attested it has a known date on or before the document's business date. In other words, ETP exported it before the day was over: the 29 Sep 14:49 export is provisional for 29 Sep.

Rule 13 lets a later known export change a provisional document without review only when that export was taken **by the end of the next day** (export date ≤ business date + 1). This covers an invoice completed or edited later the same day and a day-level summary exported mid-day and again the next morning. With the observed cadence (2 Jul, 7 Aug, 25 Aug, 6 Sep, 29 Sep) an export weeks later is a change to a settled day and goes to review (rule 15). For typed families the automatic path also needs the Owner as importer (OD-7); for anyone else, including EtpAutomation (a Store Manager), it becomes a one-click review item.

Once any attesting export is dated after the business date, the version is settled and rule 15 applies.

### 8.4 Absence: "missing from a later export"

**A(d)** holds for an incoming document when some registered block `B′` of the same store and report has all of these:
- a known time with `ExportOrder(T(B′), T(b)) = NEWER`;
- completeness `COMPLETE` or `DELTA` (rebuildable);
- coverage containing `date(d)`;
- `date(d) < exportDate(B′)` (the day was over when B′ was taken);
- `d` not observed in `B′`.

`B′` may be stored, or in the same file.

**Rule 3 uses it:** the older export's document is **not added** yet. It becomes a review item (`MISSING_FROM_LATER`, action INSERT), so the Owner sees it and decides.

**After the file's documents are decided**, for each incoming block `b` that is rebuildable, has a known time and has coverage: every CURRENT document of the same store and report with all of the following becomes a **RETIRE item, reason `MISSING_FROM_LATER`, for review**:
- `date(d)` within coverage(b);
- `date(d) < exportDate(b)`;
- not observed in `b`;
- `Ord(T(b), cur.T)` = NEWER, or `cur.T` unknown.

Stored facts are **never** deleted automatically. Approving the RETIRE item retires the document: facts are archived, then deleted, and the status becomes RETIRED. "Keep current" acknowledges it.

**Both orders ask the Owner the same question** ("this document is missing from a later complete export: is it real?"). Older-then-newer gives a RETIRE item with the document present until decided; newer-then-older gives an INSERT item with the document absent until decided. Once the Owner answers, the facts are the same either way.

### 8.5 Reinterpretation of a legacy reading

When the incoming workbook's SHA-256 equals a **planner-1** file's SHA, and that file is current with the same store and report but a different declared scope:
- documents that the old file introduced and the new reading does not produce become RETIRE items, reason `REINTERPRETATION`;
- new documents follow the rules above.

Example: HEMW R010, file 26. It is stored as 29 Sep and re-reads as 7 Sep. The result is one RETIRE item for 29 Sep R010 and NEW for 7 Sep.

### 8.6 Locked days

- **Until P4/P7 (release 1.9.5).** The declared-range triggers (`0017:164-171`, `0018:73`) still refuse any file whose period contains a LOCKED day (51021/51243), as today. Planner 2 is not available before 1.9.5.
- **From 1.9.5** (P7's migration ships with P4, §5.5):
  - `PRESENT`, `REFRESHED` (no fact change), `STALE` and `NOT_ADDED` are allowed on locked days.
  - Every change, automatic or reviewed, on a locked day is **held**: it becomes an item with status HELD, reason kept, and can be applied only after `REOPEN_DAY` approval.
  - Fact triggers (`0009`) remain the backstop.
- **Finalising a day** is refused while that store-day has PENDING or HELD items (51740, in `tr_daily_reporting_day_lock`).

### 8.7 Why the result does not depend on order or route

Assume known export times. The stored facts of each document always converge to **the observation of the latest export that contains it**, once the Owner has decided every pending item:
- **Older after newer** gives STALE (rule 11).
- **Newer after older** gives an AUTO change (rules 12–14) or an item whose approval applies the newer observation (rules 13–15).
- **Absence:** both orders raise one review item about the same document (§8.4); the Owner's answer decides the final state in either order.
- **Consolidated workbooks:** a contract block rebuilds into its raw export, virtual rows included (§6.5), so the same export gives the same observation by either route. Inside a source, the latest block by export time wins (§6.7), which is what importing the raw exports in time order gives.
- **Re-processing an already processed export** (an ATTESTED block) can only produce STALE, PRESENT, PENDING_EXISTS or REJECTED_BEFORE **as long as its earlier decision used the current rules and left nothing OBSOLETE**; that is exactly when §6.8 allows the skip. Otherwise the block is decided again (§6.8, §6.9).
- **Who imports** changes only whether rules 13–14 apply at once or as a one-click item; after approval the facts are the same.

With **unknown** times (legacy workbooks, renamed files), differences go to the Owner (`UNKNOWN_PROVENANCE`, `LEGACY_BLOCKS_DIFFER`). The Owner's decision settles the result, in whatever order the sources arrived.

The one remaining order effect is **descriptive values** when both times are unknown: import order wins. They are display-only (R9).

`ConvergenceSqlTests` pins all of this (§15).

---

## 9. Import algorithm for one workbook (planner 2)

`FolderImportService.RunFilesCoreAsync` (`FolderImportService.cs:40-161`) keeps discovery, dependency order and per-file isolation. It is used by both `DesktopImportCoordinator.cs:155-167` and `AutomatedOperationsService.cs:47-48`. Planner 2 replaces the body that calls `PrepareRestatementAsync` and `PersistAsync` (`:121-130`). The `ImportRestatement` and override paths (`:43-46`, `:99-103`, `:121-126`) are **not used** by planner 2.

1. **Read, match and stage.** Unchanged, apart from the sheet skips, contract-aware sheet choice and consolidation columns (§6.1).
2. **Planner.** `import_planner_settings` for the report code, falling back to `'*'`.
3. **Describe the source** (§6), including virtual rows. Contract or snapshot-date blockers refuse the file with stored diagnostics.
4. **Scope.**
   - **Store.** Precedence: staged single store (`ImportScope.cs:15-16`), then contract `store_code`, then known store in Info or path (`:24-30`), then same-folder siblings — the last only for families with no store column (S011, S013).
   - **Dates.** Document dates come from §7. Rows without a usable date are held (§7.4).
   - **Declared period.** `period_start` and `period_end` = min..max of the document dates. They are still stored for the coverage readers (`OperationalReportRepository.cs:639`, `DailyReportingWorkflowRepository.cs:47`, `CashBookRepository.cs:34`).
5. **Project documents.** `DocumentProjector` applies the row rules (§7.2) per block, over physical and virtual rows. It reuses today's mapping code:
   - `R025SqlImportOrchestrator.cs:46-58` (line keys over kept rows, §7.1);
   - `R022PersistenceProjection` with FY(date);
   - `StockWorkbookParser` plus `StockUnitSequencer`;
   - `EtpFamilySqlImportOrchestrator.cs:15-25` with the snapshot date from the block and `SnapshotItemSequencer`;
   - `RetailEnrichmentSqlImportOrchestrator.cs`.
6. **Resolve inside the source** (§6.7), with legacy merge and holds (§6.6).
7. **Exact duplicate.** Same SHA, report, store and declared period at `data_truth_version=1` (`FolderImportService.cs:111-120`):
   - a planner-2 file that needs re-deciding (§6.9) → run a re-decide pass on it; result `Re-decided`;
   - otherwise → `Duplicate`, as today.

   Either way, missing evidence bytes are retained (§11.2).
8. **Begin the transaction** (`SqlServerRepositories.cs:132-180` path):
   1. applock `ETP_IMPORT:<store>:<report>` (`PhaseOneImportPersistence.cs:19-23`);
   2. the exact-duplicate recheck (`:24-37`);
   3. the **upgrade gate**: `import_upgrade_state` must be `DONE` with the family's current `RulesetVersion`, otherwise refuse `IMPORT_UPGRADE_PENDING`;
   4. `read_document_state`.
9. **Decide** (§8), and run the absence check and the reinterpretation check.
10. **Preview** ("Check without importing", `ImportAudit --preview`). Run steps 11–14, then **always roll back**. No approval request is created. The predicted grid row and decision counts are returned.
11. **Insert the batch and the file** (`SqlServerRepositories.cs:143-145`, `:182-183`) with `planner_version=2`, `data_truth_version=1`, `source_kind` and `contract_version`. Then:
    - call `retain_import_source` for the workbook bytes (§11.2);
    - `begin_decision_pass` (kind `IMPORT`, pass 1);
    - register the blocks;
    - for every block that is not ATTESTED, land its physical **and virtual** rows through `append_<table>` with one lineage row `<Family>_SOURCE` per row (`PhaseOneImportPersistence.cs:124-146`, outcome writes removed);
    - call `record_source_index` (blocks, the exclusion map, per-row metadata).
12. **Apply:**
    - **AUTO changes** (rules 9a, 10, 12, 13, 14): `create_import_change_set` (mode AUTO, status APPLIED), then `apply_auto_change_set`, which writes the `import_restatements` row, archives, and updates Attribute or FILL columns in place; for REPLACE kinds it deletes the base facts, and C# then inserts the replacement facts through the `persist_*` procedures.
    - **Orphan headers** in the way of a NEW sales or revenue document are archived and deleted in the AUTO set (§7.3).
    - **NEW documents:** insert facts through the existing procedures, with the new parameters: `persist_sales_line` (`0017:42-70`), `persist_sales_invoice_control` and `persist_sales_tender` (`:72-106`), `persist_phase_one_enrichment` (`0025:1981-2018`, with `@outcome`), `persist_stock_movement @line_seq`, `persist_stock_snapshot @source,@line_seq`. Landing-only families have no typed facts.
    - Then `write_document_decisions` (versions, members, decisions, rows pointers).
    - **Review items:** `create_import_change_set` (mode REVIEW) with its `approval_requests` row.
    - Any `CONFLICT` outcome from a `persist_*` procedure, or `ALREADY_PRESENT` for a document decided NEW, means an identity bug. Throw `IMPORT_CONFLICT` with a sample of 20 (§11.1) and roll back the whole file.
13. Call `refresh_enrichment_matches` when sales lines or enrichments changed (`SqlServerRepositories.cs:175`). Query `v_invoice_reconciliation` for the touched invoices; `MISMATCH` rows become the warning `INVOICE_TOTAL_MISMATCH`, with document numbers only.
14. Mark the batch Completed. **Commit, or verify after a commit timeout** (P0, IF-014).
15. **Record the attempt** at once for this file, not after the whole run as today (`FolderImportService.cs:156-158`). It carries the result, the decision counts, the diagnostics and the evidence state.

**Grid result:**

| Result | When |
|---|---|
| `Imported` | Some documents are NEW or changed automatically, and there are no items |
| `Imported (changes pending)` | Review items were created |
| `Held` | Every changed document is held |
| `Already present` | Every document is PRESENT, STALE or ATTESTED |
| `Duplicate` | Same SHA and scope, and the earlier decision is still valid |
| `Re-decided` | Same SHA and scope, decided again under the current rules (§6.9) |
| `empty export`, `Not needed`, `Unknown layout`, `Failed`, `Cancelled` | As today |

The counts are per source row, as today (`SqlServerRepositories.cs:76-97`): New, Present, Updated (automatic), Older, Awaiting approval, Held, Rows held (no date).

---

## 10. Change sets, approval, archive, lineage, audit

### 10.1 What needs the Owner

| Needs the Owner (REVIEW) | Automatic (AUTO), archived and audited |
|---|---|
| A genuine change to a settled document (LATER_EXPORT, SAME_EXPORT_DIFFERS, UNKNOWN_PROVENANCE, LEGACY_BLOCKS_DIFFER) | Customer and store descriptions (no fact change; rows pointer only) |
| Removal or omission: MISSING_FROM_LATER (either direction), SNAPSHOT_SHRINK, REINTERPRETATION | Attribute values from a newer export (OD-3) |
| REAPPEARED documents | NULL fills of v0 facts (FILL) |
| New documents on a locked day | Late rows on a landing-only business day (GROW) |
| HEADER_DATE_MISMATCH and IN_SOURCE_CONFLICT (fix the source, or "Restate dates…") | Orphan invoice headers removed before a new document uses the number |
| MIGRATION_REVIEW (upgrade) and OWNER_RESTATEMENT | Provisional-day updates and same-date snapshot readings with nothing removed: landing-only families for any importer; typed families **only when the Owner imports** (OD-7) |
| Provisional or reading updates of typed families imported by a Store Manager or EtpAutomation (one-click items) | |

**Governance compared with today:**
- *Unchanged:* Owner approval for genuine changes, archive of replaced facts, the immutable restatement records, audit, and Owner self-approval (single owner). Today every change to stored facts needs the Owner (`0035:130-132`, `0025:1754-1757`); under this spec every change to a stored quantity or amount of a typed family still needs the Owner, either as the approver or as the importer (OD-7).
- *Changed, and stated plainly:*
  - legacy (v0) periods no longer need the Owner when unchanged, because their facts are compared directly;
  - descriptive edits never need a restatement;
  - attribute changes follow the latest export (OD-3);
  - NULL fills of legacy gross and tax are automatic for any importer.

### 10.2 Creating a change set (in the import transaction)

- **One AUTO set and at most one REVIEW set** per (file, pass, store, report), with items per document.
- **The proposal.** A PENDING version that points at the proposing file and block. Its landed rows (physical and virtual) *are* the proposal, so no copy of the rows is stored.
- **Diff and summary.**
  - `fact_delta_json` per item: changed Fact and Attribute fields, before and after (quantities, amounts, product, date, tender).
  - `summary_json` per set: counts and per-date quantity and net deltas.
  - Never any Descriptive value.
- **Restatement record.** `record_change_set_restatements` runs for AUTO sets in the import transaction and for REVIEW sets at apply, always before an archive row is written (`restatement_fact_archive.import_restatement_id` is NOT NULL, `0010:39`).
- **Approval request.** `approval_requests` (`RESTATEMENT`, `ImportChangeSet`) shows in Settings → Approvals and in Imports → Changes.

### 10.3 Approving and applying (one transaction, `ChangeSetApplyService`)

1. The Owner chooses Approve (all, by date or by item) or Reject, with a reason of at most 500 characters.
2. Take the applock for the store and report. Call `decide_import_change_set` (set → APPROVED). Call `begin_import_apply_context` for the proposing file.
3. For each approved item:
   1. **Re-project.** `FactCanonicalizer` + `DocumentProjector` read the proposing file's landed physical and virtual rows for that document. The result must hash to the proposed `fact_sha256`; otherwise the item becomes OBSOLETE (51722) with "The import rules changed since this was proposed. Use Re-decide this source." (§6.9).
   2. **Locked day.** A locked day makes the item HELD (51723).
   3. **Archive and delete.** `apply_change_item`: REPLACE and RETIRE archive and delete the base facts; TRIM archives and deletes the listed surplus facts only; UPDATE archives and updates in place. A `sales_invoices` header left without facts is archived and deleted.
   4. **Insert.** The proposed facts go in through the `persist_*` procedures with the proposing rows' lineage. The apply context lets `persist_phase_one_enrichment` accept a file whose batch is Completed (§5.3).
   5. **Shared header.** If a header date would change while another family's current document on that header keeps the old date → 51724, the item stays PENDING, and the message names the other family's pending item.
4. `complete_change_items` creates the new versions and members, flips the old versions, sets the statuses, writes `import_restatements` (scope `DOCUMENTS`) and the audit, and ends the apply context. `refresh_enrichment_matches` runs.
5. **Reject.** Proposed versions become REJECTED and are never raised again for the same content (rule 2).
6. **Keep current.** For MISSING_FROM_LATER, REINTERPRETATION, LEGACY_BLOCKS_DIFFER and held items, `acknowledge_change_items` sets them KEPT.

Store Managers can view, but they cannot decide. Automation never decides.

### 10.4 "Restate dates…" (Owner; replaces the tick box for planner-2 families)

This replaces today's restatement option (`ImportWorkspaceView.xaml:18-28`) and its end-date lookup (`FolderImportService.cs:121-126`, `OperationalCompletionRepository.cs:51-79`).

1. The Owner picks a file already imported under planner 2 (a raw export, a consolidated workbook, or a hand-checked `REVIEWED` file), a store and report, a date range and a reason.
2. `DateRestatementBuilder` compares that file's landed observations with the current documents in the range:
   - REPLACE for each document that differs, even when the file is older (Owner override of rule 11);
   - INSERT for documents not present;
   - RETIRE for current documents absent from the file, only if the file's block covers the whole range.
3. The items form one change set (origin `OWNER_RESTATEMENT`), approved as in §10.3.
4. **Nothing differs** → refused with `RESTATEMENT_MATCHES_NOTHING` and stored diagnostics, never dropped (IF-016).

### 10.5 Lineage

| Question | Path |
|---|---|
| Where did this figure come from? | Fact → `source_lineage` (file, sheet, row) → `etp_import_content` (block, document) → `import_file_blocks` (export file name, SHA-256, export time and basis; for a consolidated workbook also its block number and row range) → for a virtual row, `import_file_block_copies` (the twin row it repeats) → `import_files` → `import_source_content` (the bytes) |
| Which other exports contained it? | `import_document_decisions` rows that point at the same version |
| What did it replace? | `restatement_fact_archive` by `fact_document_version_id` and `import_change_item_id`, and the change items |
| Legacy chains | Unchanged: `import_files.superseded_by_import_file_id` and `import_restatements` |

- Facts are **never re-pointed** by planner 2. Today `promote_import_superset` re-points them (`0025:1910-1931`).
- The view `v_fact_source_lineage` (§5.4) feeds the Lineage panel (§12).

### 10.6 Audit

`record_operational_audit` (`0026:5-26`) is called with existing event types and outcomes (`0015:4-9`, `0004:15`) and the count phrases added in 0039:

| When | Event type | Outcome | Detail |
|---|---|---|---|
| REVIEW set created | `Approval` | `Succeeded` | `<n> documents pending` |
| Set decided (approve, reject, keep) | `Approval` | `Succeeded` or `Cancelled` (reject) | `Import changes decided` |
| Set applied, REVIEW or AUTO (one row per set) | `Restatement` | `Succeeded` | `<n> documents changed` or `<n> documents retired` |
| Items held on a locked day | `Restatement` | `Blocked` | `<n> documents held` |
| Upgrade state changes | `DatabaseSetup` | `Succeeded` or `Failed` | `<n> documents registered` |
| Planner switch changes | `ConfigurationChange` | `Succeeded` | `Import planner changed` |
| Re-decide pass | `ImportBatch` | `Succeeded` | `Source decided again` |

No identifier, document number or path is written to the audit; the change items hold the detail.

---

## 11. Diagnostics and evidence

### 11.1 Diagnostics (IF-017), from P1, for both planners

- **Classification.** `SafeImportFailureClassifier` (`BatchImportCoordinator.cs:53-65`) gains `DescribeDetailed(Exception) → ImportFailure(Code, Stage, SafeMessage, ExceptionType, SqlNumber, Issues)`:

  | Exception | Recorded as |
  |---|---|
  | `ImportSourceException` | Its code and message (text written by the importer) |
  | `ImportConflictException` (new) | Count plus up to 20 samples: business identity (store/FY/document/line or movement), date, report and safe difference, taken from `import_conflicts` before the rollback |
  | `SqlException` | `SQL_<number>`. Messages for numbers 50000–59999 (our own THROWs) are kept verbatim. Any other number keeps only "Database error <n> in <procedure>, line <l>". `-2` becomes `IMPORT_TIMEOUT`. |
  | Commit timeout | `COMMIT_OUTCOME_UNKNOWN`, then the batch status is checked on a fresh connection (P0) |
  | Missing approval (`SqlServerImportPersistenceUseCase.Restatement.cs:57`) | `ImportSourceException("RESTATEMENT_APPROVAL_REQUIRED")`. Today it throws `UnauthorizedAccessException`, which is misreported as "The workbook could not be accessed." |

- **`FolderImportService.cs:145-148`** keeps `FailureCode`, `FailureStage`, `FailureMessage` and `Issues` on `FolderImportFileResult`. Today it keeps only the safe message.
- **`SqlServerImportHistoryQuery.RecordAttemptAsync`** (`:19-39`):
  - stores all the new fields;
  - `SafeIssue` (`:13-17`) keeps per-code message templates from a whitelist, `ImportDiagnosticCatalogue`, and makes only unknown codes generic;
  - the attempt is recorded **per file, immediately**.
- **Stager skips** (`ImportRowStager.cs:38-45`, `:71-72`) and held rows without a date (§7.4) become counted issues with row numbers.
- **Privacy.** Messages are written by the code and never contain cell values. `ImportDiagnosticsPrivacyTests` scans `failure_message`, `summary_json` and the issues for the fixture's customer names and phones.

### 11.2 Evidence (IF-023; OD-2)

- **In the import transaction.** Both planners call `retain_import_source` with the workbook bytes from the reader's single in-memory snapshot (`OpenXmlWorkbookReader.cs:13-80`). The bytes are deduplicated by SHA. A `Duplicate`, `Re-decided` or `Already present` result retains the bytes if they are missing, in a small transaction of its own.
- **`RetainEvidenceAsync`** (`FolderImportService.cs:163-173`) and its swallowed exception are removed for imports. `ManagedDocumentRepository` stays for other documents.
- **Existing 80 files.** Settings → Database → **"Keep source files for earlier imports…"** lets the Owner pick folders (e.g. the package, `C:\ETP-Fix`, `V:\ETP\ETP Source Data`). It hashes every `.xlsx`, `.csv` and `.zip` entry, and stores those whose SHA matches an `import_files.source_sha256`. It imports nothing.
- **Raw exports inside consolidated blocks.** They are linked through `import_file_blocks.source_sha256` when the raw file itself was retained.
- **Size.** About 13 MB per two-store consolidated package and about 1.2 MB per raw store pack. Measured growth is shown in Settings → Database.

---

## 12. Owner UI changes (WPF, `Modules/Imports` and Settings)

| Screen | Change | Phase |
|---|---|---|
| Import (`ImportWorkspaceView`) | New columns: Result, Rows, New, Present, Updated, Older, Awaiting approval, Held, Rows held, Blocks, Evidence. New results `Imported (changes pending)`, `Held` and `Re-decided`. Each consolidated workbook expands to its blocks (export file, export time and basis, snapshot date, counts, virtual rows). | P4 |
| Import | **"Check without importing"** (preview) | P4 |
| Import, planner-1 families | **Restatement target picker** (IF-016 interim): when the restatement tick is set and the replacement's period overlaps more than one current file, a dialog lists them (file id, file name, period, rows) and the Owner picks the one to restate; the others go through promotion as today. No overlap → `RESTATEMENT_MATCHES_NOTHING`. Automation never restates. | P1 |
| Import | The *Source and restatement options* expander (`ImportWorkspaceView.xaml:18-28`) stays only for planner-1 families. Planner-2 families get "Import as reviewed file" (Owner only) and "Restate dates…" | P4 / P6 |
| Imports → **Changes** (new) | Change sets by store, report and dates. **Per-document diff of Fact and Attribute fields only** (quantities, amounts, product, date, tender), with the observing exports and their times; for LEGACY_BLOCKS_DIFFER the rows of each legacy block. Approve all, by date or by item; Reject; Keep current — all with a reason. Items marked "safe" (PROVISIONAL, READING) are grouped for one-click approval. Owner decides; Store Manager and Viewer see only. Held items show "Reopen the day first" or "Fix the source". | P4 |
| Imports → **History** | Failure code, stage, message and issues for every attempt; decision counts per pass; evidence state; **"Re-decide this source"** for planner-2 files | P1 / P4 |
| Imports → **Problems** | Upgrade issues; reconciliation warnings; rows held without a date; contract warnings; "Keep source files for earlier imports…" | P1 / P6 |
| **Lineage panel** ("Where did this come from?") from invoice, stock-document and snapshot rows | Current version, the export that introduced it (file name and time; workbook, block and virtual-row twin), every attesting export, archived versions | P6 |
| **Dashboard banner** | "N changes need your review"; finalising a day with pending or held items is refused with that message (51740) | P4 |
| Settings → Database | "Upgrade import history" (resumable, with progress and the verification report); "Import rules" (planner per report code, Owner only, with the switch-back summary of §13.6); evidence size | P3 / P4 |
| Settings → Approvals | `ImportChangeSet` requests open the Changes tab | P4 |
| Stock report | States the snapshot source used per store-day (Closing Stock or BinWise) | P1 |

---

## 13. Migration of today's live data (and other installs)

### 13.1 Before any upgrade

`scripts/check-import-upgrade.sql`, run SELECT-only on the live database, on a shop-PC backup (1.8.1) and on old backups. It reports:
- current v0 files;
- LOCKED days;
- duplicate movement, snapshot, control and tender identities;
- FY mismatches;
- duplicate `etp_import_content` rows per (file, sheet, row), and planner-1 files with source rows on more than one sheet;
- facts whose lineage file is superseded;
- current files sharing one SHA with different scopes;
- orphan invoice headers;
- the database collation and SQL Server version;
- the fact counts per table (the baseline for P3 verification).

Every migration pre-check repeats the blocking items with a THROW.

### 13.2 P1 (0038) changes to existing data

- `stock_movements.line_seq`, all 1 live;
- `stock_snapshots.source_report_code` (466 R010 and 3,300 CLOSING_STOCK live) and `line_seq`, all 1 live.

No other existing row changes.

### 13.3 P3, the upgrade (`ImportHistoryUpgradeService`)

Started by setup after migration 0039, or from Settings → Database. It runs one transaction per (store, report), is resumable and runs one heavy job at a time. Migration 0039 has already filled `etp_import_content.sheet_name` (§5.2). For each (store, report):
1. **Blocks.** One `BACKFILL` block per current file:
   - export time from the 12-digit name prefix (16 of 80 files), otherwise `UNKNOWN`;
   - coverage = the declared period;
   - snapshot families get one block per distinct snapshot date of their facts (`LEGACY_STAMP`);
   - superseded files get no blocks.
2. **Content self-check** (current v1 files). Rebuild the staged values from the landing rows (`etp_r…` typed tables; `etp_landing_r…` parsed by catalogue type). Recompute `content_key` with the canonicaliser and compare it with the stored `etp_import_content.content_key` for **every** row:
   - all equal → basis `SOURCE_ROWS`;
   - any difference → `UNVERIFIED`, with issue `CONTENT_KEY_MISMATCH`.

   Then write the content metadata with `record_source_index` (UPGRADE): block, document hash, date, row hash, occurrence. This is an UPDATE on `etp_import_content` only, which has no trigger.
3. **v0 files** (none current live; possible on the shop PC and in backups). Insert one content row per distinct (file, sheet, row) of the file's fact lineage (key `V0:<row>:<sheet hash>`, §5.2). Versions get basis `CANONICAL_ONLY`, hashed from the typed facts.
4. **Observation per document, row rule first.** The landing rows of each document are put through the family row rule (§7.2) **before** hashing: Multiset collapses descriptive-only copies, SingleRowPerDocument keeps one row per R022 invoice, StockUnitChain and SnapshotItems sequence. `fact_sha256` and `row_count` come from that observation.
5. **Versions mirror the facts actually stored.** Each current typed fact is mapped through its lineage to (file, sheet, row) and then to its document, and becomes a member of the document's version. `canonical_sha256` comes from those members; `member_counts_json` counts them per fact table. One R022 observation row is represented by one control and one tender per non-zero tender column, so per document the members are counted per fact table, not against `row_count`. Landing-only families get documents from current files' landing rows, with the rows pointer set (live: no two current files of one store and report overlap, so no landing row is counted twice).
6. **Facts that differ from their observation.** When the canonical projection of the observation differs from the stored members:
   - if the stored facts are the observation plus copies that differ from kept rows only in Descriptive columns → the version mirrors the stored facts and the document joins **MIGRATION_REVIEW** (step 7);
   - otherwise → basis `UNVERIFIED`, issue `FACTS_DIFFER_FROM_SOURCE`. Identical incoming content later verifies it; any difference becomes a review item, never an automatic change.
7. **MIGRATION_REVIEW** (one REVIEW set per affected store and report, origin `MIGRATION_REVIEW`, proposing file = the base file). In P3 the set and its items are recorded **without** an `approval_requests` row, so today's Approvals screen (`decide_approval_request`) cannot decide it before the apply path exists; migration 0040 (P4) creates the request for every PENDING set that lacks one. Items use action `TRIM`: archive and delete exactly the surplus facts (typed families), or mark the surplus landing rows `C` (landing-only families, no fact to delete). Expected on 1 Oct live data (verified by a SELECT-only count of descriptive-only duplicate groups in current files):

   | Report | File | Groups | Family kind | Raised in |
   |---|---|---|---|---|
   | R003 | 16 (WLMHW) | 1 (17 Aug) | typed (enrichment) | P3 |
   | R013 | 14 (WLMHW) | 1 (17 Aug) | typed (enrichment) | P3 |
   | R024 | 12 (WLMHW) | 1 (17 Aug) | landing-only | P6 (when R024 joins the ledger) |
   | R015 | 53 (WLMHW) | 1 | landing-only | P6 |
   | R022 | 10078 (WLMHW) | 1 landing pair | typed (revenue) | **no item**: the R022 row rule keeps one row per invoice, which matches the single stored control |
   | R025 | 10079 | 0 | typed | — (corrected by restatement B on 1 Oct) |

   The same pass checks current files that share a SHA with different scopes (the reinterpretation case). It raises REINTERPRETATION items, expected 0 unless HEMW R010 was re-imported under P1 against advice (§15 P1).
8. **Verify, per fact table.**
   - Every current typed fact is a member of exactly one CURRENT version, and every member of a CURRENT version is a current fact (`v_document_member_check`).
   - For each fact table, Σ of that table's member counts over CURRENT versions = `COUNT(*)` of the table for the report's facts (sales lines ← R025; controls and tenders ← R022; enrichments ← R003/R013 by `enrichment_type`; movements ← STOCK_LEDGER; snapshots ← CLOSING_STOCK/R010 by `source_report_code`).
   - The totals equal the **baseline recorded by `check-import-upgrade.sql` after P1 acceptance**, not the 1 Oct counts: P1 adds 577 movements (4,862 → 5,439), 2,402 R010 snapshot rows (3,766 → 6,168), the full WLMHW R022 controls and tenders, and any invoices they create. For reference, 1 Oct evening: 5,822 invoices; 6,697 sales lines; 1,194 controls; 1,473 tenders; 4,862 movements; 3,766 snapshots; 13,494 enrichments.
   - The upgrade writes the counts to `import_upgrade_state` and the differences to `import_upgrade_issues`.
   - State `DONE`.

**Writes forbidden in the upgrade:**
- It **never UPDATEs** `import_files`, `source_lineage`, landing tables or fact tables. It inserts into new tables and updates `etp_import_content` only, so it works on installs with finalised days.
- It never runs inside the import transaction, unlike M-1's lazy indexer.

### 13.4 Preserved unchanged

- all 80 `import_files` rows and the superseded chains (8 v0 and 6 v1 superseded live);
- the 14 `import_restatements` (8 legacy upgrades, 4 superset promotions, 2 Owner restatements);
- the 5,295 `restatement_fact_archive` rows;
- the 2 applied restatement approvals;
- `import_row_outcomes`, `import_conflicts`, `etp_import_content` keys, landing rows and `source_lineage`.

**The 1 Oct Titan files** (from `import_files` and `import_restatements`, 1 Oct evening):

| File | Becomes |
|---|---|
| 66, 68 (raw 25 Aug R025/R022 re-imports) | Superseded (by 78 and 10078, Owner restatements B and C): chain unchanged, no blocks |
| 78 (B, reviewed R025 1 Jul–25 Aug) | **Superseded** by 10079 (promotion, restatement 10014): chain unchanged, no blocks |
| 10078 (C, reviewed R022 1 Jul–25 Aug) | Current: ordinary documents; its 17 Aug landing pair collapses under the R022 row rule |
| 10079 (A, full R025 16 Sep 2024–29 Sep 2026) | Current: ordinary documents |
| 72, 10080, 10081 (WLMHW R011 25 Aug, 2 Jul, 29 Sep) | Current: one snapshot document each |

The runbook's K1–K12 values are unchanged by the upgrade (verified in P3 acceptance).

### 13.5 Planner-1 imports after the upgrade

A planner-1 import of an upgraded (store, report) calls `mark_upgrade_stale` in its own transaction. Before the next planner-2 import or re-decide of that report, a **resync** compares members with current facts and writes `RESYNC` versions where they differ (planner-1 promotion re-points lineage, so the re-pointed facts appear under new lineage ids; the RESYNC version lists them, and the old version keeps its own members).

### 13.6 Switching a report back to planner 1

Planner 2 writes nothing planner 1 cannot read:
- its files are ordinary v1 files (`data_truth_version=1`, declared period = min..max of document dates) with landing rows, `<Family>_SOURCE` lineage and `etp_import_content` keys of today's formula;
- its facts carry planner-1 labels (`LineKeys` over kept rows, `line_seq`; §7.1), so planner 1's `persist_*` calls find them as `ALREADY_PRESENT`.

What changes for planner 1 after a switch back:
- **Overlap.** Planner 2 never supersedes files, so several current files of one report may overlap. Planner 1 treats them as it treats any overlapping current files today (`PhaseOneImportPersistence.cs:41-81`): a new file must contain every overlapping file's content keys (it then promotes each one) or it is refused with `IMPORT_PERIOD_ALREADY_PRESENT`, with stored diagnostics. A consolidated two-year workbook normally contains them all.
- **Restatement.** The P1 target picker (§12) lets the Owner choose which overlapping file to restate.
- **Ledger.** Planner 1 never reads the ledger. The switch marks the report `STALE` and makes its PENDING change sets OBSOLETE with the reason "Report switched back to planner 1".
- **Known weakness returns.** Planner 1 labels lines over all rows of its file, so a consolidated workbook with a stale descriptive-only copy can again add a second line (IF-021), exactly as in 1.9.3.
- **Settings shows the effect before confirming:** the number of overlapping current files and the pending sets that will become OBSOLETE.
- **Switching forward again** runs the resync (§13.5) and lets the Owner re-decide the planner-2 files whose sets became OBSOLETE (§6.9).
- Pinned by `Switch_back_after_planner2_import_dedupes`: planner 2 imports raw 25 Aug, raw 29 Sep and a contract workbook; the report is switched to planner 1; the contract workbook is re-imported (Duplicate) and a raw 29 Sep export is re-imported (promotion or Duplicate content); fact counts are unchanged and no line is doubled.

### 13.7 Expected review queue on today's data

| Phase | Items | Notes |
|---|---|---|
| P3 (upgrade) | 2 MIGRATION_REVIEW: WLMHW 17 Aug R003 and R013 (TRIM) | The package R003 and R013 workbooks are byte-identical to files 16 and 14 (same SHA and period), so re-importing them gives `Duplicate`; the two items are approved directly in Imports → Changes once P4 is installed. |
| P5 | 1 REINTERPRETATION: HEMW R010, stored as 29 Sep | Raised when the package HEMW R010 is re-imported; NEW 7 Sep. |
| P6 | 2 MIGRATION_REVIEW: WLMHW R024 (17 Aug) and R015 | Landing-only; TRIM marks the surplus landing row `C`. |

Approving all five:
- leaves 17 Aug enrichments equal to the 17 Aug R025 lines;
- leaves HEMW R010 only as 7 Sep (466 rows);
- leaves one current R024 and R015 row per group, so the customer-name lookup reads one row;
- the HEMW 29 Sep stock report shows R011 only: 495 rows, quantity 524. It already does after P1, through the view.

---

## 14. How each failure-register row is resolved

| IF | Root cause today | Resolution | Phase | Test |
|---|---|---|---|---|
| **IF-006** (Jul–Aug would double) | No guard against a second current file; content-derived line identity; stale cross-block copy gets `:2` (`EtpInvoiceIdentity.cs:34-50`); controls and tenders found by `TOP(1)` (`0017:84-85`, `:102-103`) | One CURRENT version per document (filtered unique index). Facts inserted only for NEW documents, or after the base facts are deleted in the same transaction. Hard identity indexes on movements, snapshots, controls and tenders (P1), plus the existing `UQ_sales_lines_natural` and `UX_enrichment_content`. Stale copies collapse (§7.2); line labels computed over kept rows. Legacy merge takes the maximum, never the sum, and holds documents whose blocks differ (§6.6). R025-against-R022 reconciliation warning. | P1, P3–P4 | `Stale_cross_block_copy_not_doubled`; `Raw_and_consolidated_converge_in_every_order`; `Movement_identity_index_rejects_duplicates`; `Control_and_tender_indexes_reject_duplicates`; `Legacy_blocks_that_differ_are_held` |
| **IF-012** (calendar-year key) | Fixed in 1.9.x; residual is IF-019 | One FY(date) rule for every family (§7.3). Conflicts are never silent: planner 1 rolls back with stored diagnostics; planner 2 holds the document. | P1 | `April_first_return_joins_R025_header` |
| **IF-015** (legacy v0 blocks two-year files) | Overlap tested on the declared range (`PhaseOneImportPersistence.cs:41-49`); v0 has no content keys; v0 promotion needs an identical hash (`0025:1754-1762`) | Planner 2 has no declared-range test. v0 facts become `CANONICAL_ONLY` versions: identical → PRESENT; NULL gross or tax → FILL; changed → review. The Owner-only legacy upgrade is no longer needed. | P3–P4 | `Legacy_v0_documents_present_or_filled` (replaces `PhaseOneImportSqlTests.cs:101`, `:121`); P6 replay from the 25 Sep backup |
| **IF-016** (restatement silently dropped) | Previous file found by the end date (`FolderImportService.cs:121-126`; `OperationalCompletionRepository.cs:63`); none → nothing attached (`:125`) | P1 interim (planner 1): candidates = current files whose declared range **overlaps** the replacement. None → `RESTATEMENT_MATCHES_NOTHING`; one → used; several → the Owner picks the target in a dialog (`RESTATEMENT_TARGET_AMBIGUOUS` is stored only when no target was picked, e.g. from automation). Planner 2: no file lookup at all; change sets are scoped to documents; "Restate dates…" refuses an empty match. | P1, P4/P6 | `Restatement_with_no_overlapping_target_is_rejected`; `Restatement_with_two_targets_uses_the_picked_one`; `Restate_dates_matching_nothing_refused` |
| **IF-017** (diagnostics lost) | Code dropped (`FolderImportService.cs:145-148`); generic text (`SqlServerImportHistoryQuery.cs:13-17`); no message column (`0028:4-22`) | §11.1: code, stage, message, SQL number, commit state, ≤ 200 issues; recorded per file immediately | P1 | `ImportDiagnosticsSqlTests` |
| **IF-018** (R030 per-unit rows) | Movement identity has no line (`0014:450-452`) | `line_seq` from `StockUnitSequencer`; `UX_stock_movements_identity`; `persist_stock_movement @line_seq`; exact repeats kept with their own `line_seq` and a warning; first-movement readers order by `line_seq` | P1 | `Per_unit_rows_get_chain_line_seq_in_any_row_order`; `Exact_ledger_repeats_are_kept_and_warned`; package acceptance 824 present / 577 new / 0 conflicts |
| **IF-019** (1-April returns) | R022 year from INVOICEYEAR (`R022SqlImportOrchestrator.cs:78`, `:89`, through `EtpInvoiceIdentity.cs:11-17`) | FY(date) for every caller, with information `INVOICE_YEAR_DIFFERS`; guard migration 51700; header date mismatch held (planner 2) | P1 (OD-1) | `Invoice_year_is_financial_year_of_date_when_INVOICEYEAR_differs` |
| **IF-020** (stacked R010; R010+R011 double count) | Max date token (`ImportScope.cs:33-35`); one date stamped on all rows (`EtpFamilySqlImportOrchestrator.cs:21`); no source column; report sums all rows (`OperationalReportRepository.cs:158-182`) | `source_report_code` and `v_stock_snapshots_effective` read at six call sites (P1). Snapshot dating by tiers, never the maximum; per-block R010 dates (P1), including contract blocks. REINTERPRETATION retire of HEMW 29 Sep R010 (P5). | P1, P5 | `R010_and_R011_same_day_report_reads_R011_only`; `Stacked_R010_blocks_get_their_dates`; `Reinterpretation_of_misdated_R010_creates_retire_item` |
| **IF-021** (customer edits; stale 17 Aug line) | Content keys include customer fields (`PhaseOneImportPersistence.cs:94-96`); promotion compares them (`0025:1853`, `:1865`); line keys exclude them, so a stale copy becomes `:2` | Role `Descriptive`; change detection uses Fact fields only; descriptive collapse; MIGRATION_REVIEW for the existing doubles: R003 and R013 (P3), R024 and R015 landing rows (P6) | P3–P4, P6 | `Customer_edit_is_descriptive_only` (replaces `:75`); `Migration_review_finds_descriptive_only_doubles` (typed and landing) |
| **IF-022** (multi-snapshot R011) | Period min..max of `Date` (`ImportScope.cs:31`); no per-snapshot scope | P1: refuse with a clear `SNAPSHOT_MULTIPLE_DATES`. P5: one snapshot document per date; other dates untouched. | P1, P5 | `Multi_date_closing_stock_refused_with_SNAPSHOT_MULTIPLE_DATES` (P1); `Multi_snapshot_R011_leaves_other_dates` (P5) |
| **IF-023** (no evidence) | Folder ACL; error swallowed (`FolderImportService.cs:168-171`); link by SHA + period end (`0025:2076-2077`) | Bytes in the database inside the import transaction; "Keep source files for earlier imports…"; evidence state on every attempt | P1 (OD-2) | `Bytes_retained_in_import_transaction_and_deduplicated` |

**Adjacent rows:** IF-014 (commit timeout) is P0. IF-001 … IF-005 and IF-007 … IF-011, IF-013 stay as recorded.

---

## 15. Phased delivery

**Effort** is in realistic Codex-days.

**Release mapping:**

| Release | Phases |
|---|---|
| 1.9.3 | P0 + P1 |
| 1.9.4 | P2 + P3 |
| 1.9.5 | P4 + P7 + P5 (P7's locked-day guards ship with the first planner-2 release, §5.5) |
| 1.9.6 | P6 |
| 1.10.0 | P8 |
| 1.10.x | P9 |

**Dependencies:**
- P1 → P3 and P2 → P3 (the upgrade needs P1's schema and sequencers and P2's catalogue roles, canonicaliser and row rules).
- P2 can start alongside P1; it builds on P1's `ExportNameParser`, `ExportOrder`, `SnapshotDateResolver` and contract reader.
- P3 → P4 → P5 → P6.
- P7 needs P4 and ships in the same release.
- P8 needs P2, P5 and P6.

**Gate for every phase:**
- the release script, one test project at a time;
- SQL tests on `EtpPhase1Test_*` scratch databases;
- `PhaseOneImportSqlTests` golden totals (`:162`, `:220`) and `EtpCorpusGoldenTests.cs:101-133` unchanged unless this spec says otherwise;
- **HEMW stays at 819 lines and net 11,416,955.60**, and the WLMHW K1–K12 values hold, except where a phase intends a change.

### P0: commit safety (IF-014). 1–2 days.

**Deliverables:**
- `b9c5968` (`fix/phase5-defects`) branches from `92a14da` (release 1.9.1), four commits behind the installed `ca4bc21`. **Rebase it onto `ca4bc21`**, keeping `ca4bc21`'s fixture fix ("Start test commands on fresh connections after the fixture bootstrap") and the 1.9.2 installer changes, then review and merge: its own COMMIT budget, a verify-committed check after a timeout, and a rollback that no longer hides the original exception (`SqlServerRepositories.cs:179`).
- Apply the same check to the migration runner (`Migrations.cs:185-207`): after a commit timeout, check `schema_migrations`.

**Tests:**
- the existing IF-014 tests, re-run after the rebase;
- `CommitVerificationSqlTests.Commit_timeout_after_server_commit_reports_imported`;
- `Migration_commit_timeout_is_verified_against_journal`.

**Acceptance:** a clean gate on the rebased branch.

### P1: current-engine fixes, release 1.9.3 (migration 0038). 13–15 days.

**Deliverables:**
- §5.1 in full: diagnostics, evidence, movement identity, **control and tender unique indexes**, snapshot source and line, the enrichment outcome, the stock readers on `v_stock_snapshots_effective`, the first-movement ordering.
- `StockUnitSequencer` (exact repeats kept with a warning) and `SnapshotItemSequencer`; the R030, R011 and R010 orchestrators pass `line_seq` (`StockImportOrchestrator.cs:40-41`, `EtpFamilySqlImportOrchestrator.cs:15-25`).
- FY(date) for every caller of `EtpInvoiceIdentity.FinancialYearEnd` (`EtpInvoiceIdentity.cs:11-17`; `R022SqlImportOrchestrator.cs:78`, `:89`; `R025SqlImportOrchestrator.cs:53`) with `INVOICE_YEAR_DIFFERS`.
- `ExportNameParser` and `ExportOrder` (§6.3).
- `SnapshotDateResolver` (§6.4) with `LegacyInfoBlockReader` and the contract reader (layout only) for undated families. Per-block R010 dates in `EtpFamilySqlImportOrchestrator.cs:21`.
- Preflight sheet skips (`Info`, `ETP_Excluded`, `Snapshot History`) and the contract tie-break and store (§6.1); `CONTRACT_UNREADABLE`.
- `SNAPSHOT_MULTIPLE_DATES`.
- Diagnostics (§11.1) and History UI.
- Evidence (§11.2) and "Keep source files for earlier imports…".
- IF-016 interim with the restatement target picker (§12, §14).
- `RESTATEMENT_APPROVAL_REQUIRED`.
- Attempts recorded per file.
- `scripts/check-import-upgrade.sql` (needed to record the post-P1 baseline).

**Tests:**
- `ImportDiagnosticsSqlTests`:
  - `Failed_conflict_attempt_persists_code_stage_and_sample`
  - `Locked_day_refusal_persists_SQL_51021`
  - `Pending_restatement_persists_request_code`
  - `Attempt_is_recorded_per_file_before_the_run_ends`
  - `Persisted_details_contain_no_customer_values`
- `SafeImportFailureClassifierTests`: every exception type; `-2` → `IMPORT_TIMEOUT`; ≥ 50000 keeps its message.
- `StockUnitSequencerTests` (unit): shuffled groups (openings 0/1/3/4/2) give the same `line_seq`; exact repeats are kept with consecutive `line_seq` and a warning.
- `SnapshotItemSequencerTests` (unit): repeated identical rows get distinct `line_seq`; the order of input rows does not change the stored multiset.
- `StockIdentitySqlTests`:
  - `Per_unit_rows_get_chain_line_seq_in_any_row_order`
  - `Reimport_of_per_unit_ledger_is_already_present`
  - `Movement_identity_index_rejects_duplicates`
  - `Exact_ledger_repeats_are_kept_and_warned`
  - `First_movement_uses_chain_start_in_any_insert_order`
- `SalesIdentitySqlTests.Control_and_tender_indexes_reject_duplicates`.
- `R022SqlImportOrchestratorTests.Invoice_year_is_financial_year_of_date_when_INVOICEYEAR_differs`.
- `PhaseOneImportSqlTests.April_first_return_joins_R025_header`. Fixture: a return dated 2026-04-01 with INVOICEYEAR 2026, plus an earlier-year return with the same number.
- `PhaseOneImportSqlTests.Enrichment_reimport_reports_already_present` (the procedure's new `@outcome`).
- `SnapshotSourceSqlTests`:
  - `R010_and_R011_same_day_report_reads_R011_only`
  - `Repeated_identical_snapshot_rows_both_stored`
  - `Caller_without_source_gets_it_from_lineage`
  - `Locked_snapshot_day_backfill_succeeds_with_trigger_list`
  - `Multi_date_closing_stock_refused_with_SNAPSHOT_MULTIPLE_DATES` (IF-022)
- `ExportNameParserTests`: Retail prefix, Service suffix, `dd.MM.yyyy`, none; `ExportOrderTests` for every basis pair.
- `SnapshotDateResolverTests`:
  - a WLMHW-shaped stacked R010 gives 3 dates;
  - a HEMW-shaped R010 in a `(29 Sep 2026)` folder gives 7 Sep;
  - a contract-shaped R010 gives its block dates;
  - two folder tokens → `SNAPSHOT_DATE_AMBIGUOUS`;
  - a renamed raw R010 in a dated folder gives the folder date plus `SNAPSHOT_DATE_FROM_FOLDER`;
  - Info free text is never read.
- `ContractUnderPlanner1Tests.Contract_workbook_dates_R010_blocks_and_ignores_excluded_sheet`.
- `FolderImportServiceTests`:
  - `Restatement_with_no_overlapping_target_is_rejected`
  - `Restatement_with_two_targets_uses_the_picked_one`
  - `Restatement_with_two_targets_and_no_pick_is_refused_with_candidates`
  - `Missing_approval_reports_RESTATEMENT_APPROVAL_REQUIRED`
- `EvidenceSqlTests`:
  - `Bytes_retained_in_import_transaction_and_deduplicated`
  - `Duplicate_result_retains_missing_bytes`
  - `Viewer_cannot_read_evidence`
- `Migration0038Tests`: each pre-check THROWs on its fixture and leaves the schema unchanged; triggers are re-enabled; a second run is a no-op.

**Acceptance.** First on a restored copy of the 1 Oct 21:01 backup, then on live, one heavy job at a time:
- Package WLMHW R030 → **Imported, 824 Present, 577 New, 0 conflicts**. Stock movements 4,862 → 5,439 (WLMHW 824 → 1,401).
- Full package WLMHW R022 → **Imported, 0 conflicts**, via superset promotion over file 10078. The 1-April rows are on R025's headers.
- Package WLMHW R010 → **Imported, snapshots 2 Jul (844), 7 Aug (853), 29 Sep (705)**, dates from the tiling Info blocks.
- HEMW 29 Sep stock report → **495 rows, quantity 524**.
- **Do not re-import HEMW R010 under P1.** Its misdated file 26 is hidden by the view and retired in P5.
- A forced failure of each kind shows its code in History.
- `import_source_content` holds the bytes of every new import. The earlier-imports action stores the package and `C:\ETP-Fix` files.
- **Record the post-P1 baseline**: `check-import-upgrade.sql` output on live, attached to the release notes; P3 verifies against it.

### P2: identity catalogue, source reader and validator (pure C#, no database). 7–9 days.

**Deliverables:**
- Catalogue `Role`, `Identity`, `Derived`, `ConsolidationColumns` for R001–R031 and SOR_AGEING, proposed by a generator and reviewed.
- `FactCanonicalizer`, `DocumentProjector`, the row rules (§7.2) and planner-1-compatible labels (§7.1).
- `SourceDescriptionReader`, `ConsolidationContractValidator` (every code in contract §8), virtual-row rebuild (§6.5), `HistorySheetBlockReader`, the in-source resolver with legacy merge and holds (§6.6–6.7).
- `DocumentDecisionEngine` (§8; pure).
- `ImportAudit --check-import <path>`: prints blocks, virtual rows, documents, hashes and predicted decisions against a JSON state dump. Read-only.
- `ImportAudit --validate-contract <path> [--raw <folder>]`: with `--raw`, every block whose raw file (matched by SHA-256) is in the folder is rebuilt and compared with the raw export's row multiset.
- `docs/04a_CONSOLIDATION_CONTRACT.md` (copied from `CONSOLIDATION-CONTRACT.md`), `docs/schemas/etp-consolidation-contract-v1.schema.json`, synthetic fixtures under `tests-dotnet/fixtures/contract/`.

**Tests:**
- `EtpReportFamilyCatalogueTests`: invariants, pinned role lists, `Descriptive_column_can_never_be_a_key`.
- `FactCanonicalizerTests`: vectors; same output from staged rows and from typed landing rows.
- `DocumentProjectorTests`:
  - R025 genuine repeats keep their count (the **29 Aug fixture: 4 groups × 5 lines that differ only in STORETIMESTAMP**);
  - the **17 Aug contact-only copies collapse in R025, R022, R003 and R013**;
  - line labels over kept rows equal planner 1's labels for files without stale copies;
  - R030 chain in any order, exact repeats kept;
  - an R022 Fact-differing duplicate → `IN_SOURCE_CONFLICT`;
  - customer fields never change `fact_sha256`.
- `ConsolidationContractTests`: each blocker code; a valid round trip; `Virtual_rows_rebuild_raw_export_hash`; `Trimmed_block_rebuild_includes_mapped_copies`; `Block_numbers_are_append_order_and_time_decides`; `Excluded_twin_reused_is_refused`.
- `LegacyInfoBlockReaderTests` (anonymised shapes): WLMHW R010 gives 3 blocks; HEMW R010 gives 7 Sep; the HEMW R025 gap and the WLMHW R030 overlaps give a whole file.
- `LegacyMergeTests`: consistent blocks merge by maximum; `Legacy_blocks_that_differ_are_held` (a later block with a fact row no earlier block holds).
- `DocumentDecisionEngineTests`: every rule × order (OLDER, SAME, NEWER, UNKNOWN, DATE-only) × policy × locked × basis × typed × importer role; `Not_added_document_raises_review_item_in_either_order`; `Provisional_window_is_next_day_only`.

**Acceptance** (on this PC; low load; one process):
- `--check-import` over all 64 package workbooks reports 0 unexpected blockers, and lists every legacy hold it would raise.
- The projected document and fact counts reconcile with the live per-family fact counts (report attached).

### P3: ledger schema and upgrade (migration 0039; every family stays on planner 1). 11–13 days.

**Deliverables:**
- §5.2, including the sheet fill, the regenerated `append_<table>` procedures and the audit vocabulary.
- `ImportHistoryUpgradeService` (§13.3) with Settings progress and the verification report.
- The typed MIGRATION_REVIEW set (R003, R013).
- STALE marking by planner 1, and resync.

**Tests:**
- `Migration0039Tests`: idempotent; checksums of the existing tables other than `etp_import_content` are unchanged; the sheet fill and `UX_etp_import_content_row` succeed on a fixture with a `current`-rule file.
- `UpgradeSqlTests`:
  - `One_current_version_per_document_matching_fact_counts`
  - `Member_counts_per_fact_table_match`
  - `Upgrade_hashes_after_the_row_rule` (an R022 landing pair differing only in contact gives one row and no item)
  - `Upgrade_is_resumable_after_a_kill_between_families`
  - `V0_facts_get_canonical_only_versions`
  - `V0_R022_content_rows_one_per_source_row`
  - `Content_self_check_marks_unverified_on_mismatch`
  - `Install_with_locked_days_upgrades_without_touching_triggered_tables`
  - `Migration_review_finds_descriptive_only_doubles`
  - `Upgrade_keys_equal_import_keys_for_the_same_workbook` (the canonicaliser pin)
  - `Planner1_import_after_upgrade_marks_stale_and_resync_restores`

**Acceptance** on a restored copy of live **taken after P1**:
- The upgrade completes on the owner PC; the time is recorded.
- The verification equals the post-P1 baseline per fact table, with 0 unexplained differences.
- The MIGRATION_REVIEW set holds 2 items (R003, R013).
- The planner-1 SQL suite passes, one project at a time.
- K1–K12 unchanged.

### P4: planner 2 for the sales families (R025, R022, R003, R013), change sets, preview (migration 0040). 17–19 days.

**Deliverables:**
- §5.3; §8–§10 for these routes; apply contexts; re-decide (§6.9).
- The planner switch and Settings → Import rules, with the switch-back summary (§13.6).
- Import grid changes; the **Changes tab with per-document fact diffs, partial approval, one-click safe items** (§12); the dashboard banner and the finalise guard (51740).
- Preview.
- `v_invoice_reconciliation` warnings.
- **Ships with P7** (row-level locked-day guards).

**Tests** (`DocumentImportSqlTests`):
- `Raw_and_consolidated_converge_in_every_order`: the 6 orderings of {raw 25 Aug, raw 29 Sep, contract workbook}, for R025, R022, R003 and R013; a canonical dump of current facts is identical after approving all.
- `Reimport_is_idempotent_and_lands_nothing`: a second import adds 0 rows to every table except `import_attempts`.
- `Stale_cross_block_copy_not_doubled`.
- `Customer_edit_is_descriptive_only`: replaces `PhaseOneImportSqlTests.cs:75`. Imported; 1 line; 0 approvals; the rows pointer moves.
- `Changed_fact_waits_for_approval_rest_applied`: replaces `:44`'s whole-file atomicity. The changed 1 Jul amount is one item; the sum stays until approval; then 1 archive row and 1 `import_restatements` row.
- `Older_export_after_newer_is_stale`.
- `Not_added_document_raises_review_item_in_either_order`.
- `Missing_from_later_export_creates_review_never_deletes`.
- `Provisional_update_is_automatic_for_owner_and_review_otherwise` and `Provisional_window_is_next_day_only`.
- `Automatic_set_writes_restatement_and_archive`.
- `Header_date_mismatch_held`; `Orphan_header_removed_before_new_document`.
- `Approval_applies_from_landed_rows_without_reimport`; `Approval_of_enrichment_item_inserts_under_apply_context`; `Apply_context_refused_without_owner_or_outside_transaction`.
- `Approval_obsolete_when_base_changed`; `Redecide_after_rules_change_raises_items_again`.
- `Rejected_content_not_reraised`.
- `Only_owner_decides`.
- `Preview_rolls_back_and_creates_no_approval`.
- `Legacy_v0_documents_present_or_filled`: replaces `:101` and `:121`.
- `Superset_needs_no_supersession`: replaces `:137`. Three lines, no file superseded.
- `Finalise_refused_with_pending_items` (through `DailyReportingWorkflowRepository.FinaliseAsync`).
- `Switch_back_after_planner2_import_dedupes` (§13.6).

**Acceptance** (copy of live after P3, sales families on planner 2). Re-import the **unmodified** package `Retail` folder, sales families, both stores:
- 0 Failed.
- WLMHW R025 → every invoice PRESENT; 17 Aug stays 5 lines; one stale copy collapsed.
- WLMHW R022 → `Duplicate` (imported in P1).
- WLMHW R003 and R013 → `Duplicate` (byte-identical to files 16 and 14). Approve the 2 MIGRATION_REVIEW items in Imports → Changes → the 17 Aug enrichments equal the R025 lines.
- Re-running the folder → every file `Duplicate`.
- **Locked-day run:** finalise one WLMHW day in August on the copy, then re-import the package R025 → `Already present`; a fixture change on that day is HELD.
- HEMW 819 lines / net 11,416,955.60.

### P5: planner 2 for the stock families (R030, R011, R010). 6–8 days.

**Deliverables:**
- StockMovement and StockSnapshot routes.
- Snapshot rules (rule 14), REINTERPRETATION, the absence check for snapshots.

**Tests:**
- `Per_unit_ledger_documents_stable_in_any_order`
- `Multi_snapshot_R011_leaves_other_dates` (IF-022)
- `Stacked_R010_blocks_get_their_dates`
- `Same_date_newer_snapshot_updates_values_for_owner_and_review_otherwise`
- `Same_date_snapshot_shrink_needs_review`
- `Reinterpretation_of_misdated_R010_creates_retire_item`
- `ConvergenceSqlTests` for R030, R011 and R010

**Acceptance.**
- Re-import the package stock families on the live copy:
  - R030 → `Duplicate` (imported in P1);
  - WLMHW R011 → 2 Jul and 29 Sep PRESENT; 25 Aug untouched;
  - HEMW R010 → 7 Sep NEW plus 1 REINTERPRETATION item. Approve → HEMW R010 only on 7 Sep;
  - the stock report per store-day equals R011 where R011 exists.
- **Core replay of 1 Oct.** Restore `V:\ETP\Backups\EtpBackups\EtpReporting-20260925-071854-….bak` (8 current v0 files) as `EtpPhase1Test_Replay`, upgrade to 0042 and run the history upgrade, put the seven core families on planner 2, then import the raw 25 Aug exports and the package core families: once raw-first, once (on a second restore) package-first. Expect 0 Failed, no hand-built or reviewed file, every review item listed and explained, and identical fact dumps for the two orders once the items are decided the same way.
- **Total review items over P3–P5 on the live copy: 3.**

### P6: other Retail families, read paths, full Owner UI, contract v1 end to end (migration 0041). 13–15 days.

**Deliverables:**
- §7.4 for every remaining R family; `FactIdentityCorpusTests`; held rows without a date.
- The landing MIGRATION_REVIEW (R024, R015).
- §5.4 views; the r024 lookup switched.
- The Lineage panel, Problems page, "Restate dates…" and "Import as reviewed file".
- **The builder update on the linked PC** (Claude tooling, outside this repo; OD-4) and the contract round trip.

**Tests:**
- `Landing_reimport_reports_present`
- `Date_scope_grow_adds_late_rows`
- `Rows_without_date_are_held_and_listed`
- `Current_landing_view_returns_one_copy_per_document_including_virtual_rows`
- `Landing_migration_review_trims_descriptive_double`
- `R024_lookup_returns_latest_descriptive`
- `Restate_dates_matching_nothing_refused`
- `Restate_dates_retires_absent_documents_after_approval`
- `Contract_round_trip_with_builder_fixture`
- `Convergence_with_contract_workbook` (real builder output)
- Desktop view-model tests in `WpfViewCollection`
- `Review_DTO_never_carries_descriptive_values`

**Acceptance:**
- **Full replay of 1 Oct (R2 proof).** As P5's core replay, with every family on planner 2, both orders: the raw 25 Aug folders (`V:\ETP\ETP Source Data\HEMW`, `\WLMHW`) and the package `Retail` folder. Expect 0 Failed, no hand-built or reviewed file, no restatement typed by the Owner, every review item listed and explained, identical fact dumps for the two orders after decisions, and the runbook's final values (WLMHW 5,878 sales lines, net 22,857,804.66; 17 Aug 5 lines; HEMW 819 lines, net 11,416,955.60).
- **Fresh database.** An empty `EtpPhase1Test_Fresh`, planner 2 for all families: import the package, then the raw folders. 0 Failed; facts equal the replay's.
- Both raw corpus folders and the package import with 0 Failed on the live copy; the landing MIGRATION_REVIEW holds 2 items (R024, R015).
- A builder-made contract-v1 package validates (0 blockers, including the `--raw` rebuild check) and imports after the raw packs with every block ATTESTED or new documents only.
- On a copy of live, raw-then-contract and contract-then-raw give identical fact dumps.

### P7: row-level locked-day guards (migration 0042; OD-5). 3–4 days, shipped in release 1.9.5 with P4.

**Deliverables:** §5.5; the HELD paths of §8.6.

**Tests:**
- `A_locked_day_blocks_only_changes_on_that_day`: replaces `PhaseOneImportSqlTests.cs:150`. Identical rows on a locked day import; a changed row on it is HELD; nothing is written to locked facts.
- `Promotion_never_touches_locked_dates`.
- `Planner1_file_with_locked_day_is_still_refused_whole` (planner 1 keeps today's rule).

**Acceptance:** with one locked day, a two-year contract re-import is `Already present` (run as part of the P4 locked-day acceptance).

### P8: Service Centre on the same mechanism (migration 0043+). 13–16 days.

**Deliverables:**
- The Service plan rebased (D1–D3, D5–D11, D13 kept); §7.5.
- `Snapshot History` blocks; `PERIOD` scope; Service export-name grammar.
- S027/S028 consolidation columns as block metadata (`SourceColumnBlockReader`); catalogue signatures from the raw header.
- S001 `Derived` (`Not needed`), subject to OD-6(c).
- `snapshot_row_spans` with growth measurement.
- `CsvWorkbookReader` and `RawNamePatterns` only if OD-6(a) says yes.
- The automation filter (Service plan D10).

**Tests:**
- the Service plan's §9 tests, adapted;
- `Service_reimport_is_present`;
- `Snapshot_history_loads_as_dated_blocks`;
- `Status_change_is_latest_reading_without_review`;
- `Period_overlap_refused_unless_covering`;
- `Consolidation_columns_stripped_and_blocks_formed` (S027/S028 consolidated and raw-shaped fixtures give the same fact rows);
- `Derived_family_reported_not_needed`;
- `Unchanged_snapshot_adds_no_payload_rows`;
- `Service_import_never_triggers_retail_pack`.

**Acceptance:**
- The consolidated Service folder gives 38 Imported or empty and 2 Not needed (S001 derived, S038 retired).
- A second import is all Duplicate or Already present.
- Database growth per refresh is measured and recorded against the 10 GB cap.

### P9: retire planner 1. 3–4 days.

**Deliverables:**
- Remove planner 1: `PlanImportAsync`, the calls to `promote_import_superset`, `complete_duplicate_import`, `request/prepare_import_restatement` and `FindCurrentImportFileIdAsync`, the target picker and the tick box.
- The procedures stay for history.
- `import_row_outcomes` is frozen.
- Close IF-015 … IF-023 in the register with phase and commit.

**Acceptance:** full gate; a Claude audit.

**Total: about 87–105 Codex-days.** The value arrives early:
- P1 (≈ 3 weeks) loads all held data and fixes stock, diagnostics and evidence.
- P4–P5 with P7 (≈ 9–10 weeks cumulative) give any-order consolidated and raw imports for the core families, with finalised days no longer blocking two-year workbooks.

---

## 16. Performance and storage (SQL Express, weak PC)

- **One transaction per workbook**, as on 1 Oct when the two-year loads worked. Planner 2 writes much less on re-import:
  - unchanged documents get one decision row (PAGE-compressed) and no fact or outcome rows;
  - blocks already imported, and still validly decided, are not landed;
  - fact procedures run only for NEW documents and applied changes.
- **Virtual rows** are landed once per new delta block, as a raw import of that export would land them. Today's WLMHW R025 shape would add 945 virtual rows (11 + 297 + 637) the first time its contract version is imported, and none on re-import.
- **Set-based statements:** source index (OPENJSON), state read (one call per file), decisions (one call).
- **Plan reads** touch only documents of one store and report on the incoming dates (`IX_fact_documents_scope`).
- **Growth estimates:**
  - ledger after the upgrade: about 10–25 MB on today's 200 MB;
  - decisions: a few hundred KB per two-year import or re-decide pass;
  - evidence: about 0.2–0.7 GB a year (OD-2);
  - Service snapshots: §7.5 and OD-6.
- **Targets:**
  - preview of a two-year WLMHW R025 (5.9k rows) under 60 s on the owner PC;
  - a re-import of an unchanged contract workbook under 10 s;
  - a daily raw pack under 2 minutes.
- **Heavy jobs** (the upgrade, a full-package import, the replay acceptance) run one at a time. Each leaves nothing half-written after a power-off: the upgrade is resumable per (store, report), and an import file, a re-decide pass and an approval are each one transaction.
- **Compatibility.** SQL Server 2016 SP1 or later: `CONCAT`, `OPENJSON`, filtered indexes, PAGE compression in Express. Confirm the shop PC's version with the pre-upgrade checker.

---

## 17. Risks and mitigations

| # | Risk | Mitigation |
|---|---|---|
| 1 | Effort (≈ 87–105 days) and complexity | Phases ship independently. P1 delivers the held data. The pure C# engine is testable without SQL. The planner switch per report code allows rollback without a restore. |
| 2 | The PC cannot run the SQL suites reliably (power-offs) | One test project at a time; scratch databases; heavy jobs one at a time; P2 is pure C#. |
| 3 | Legacy workbooks (no contract) holding a genuinely re-stated line for a document **not yet stored** | The merge holds any document whose later block has a fact row no earlier block holds (§6.6); otherwise it takes the maximum, never the sum. Unknown time sends every difference against stored facts to review. R025-against-R022 reconciliation warning. Preview. Contract v1 removes the case. |
| 4 | Unknown export times (64 of 80 live files) cause more reviews | Raw files and contract blocks carry times; legacy differences are rare (3 predicted on live); safe items are grouped for one-click approval. |
| 5 | A column classified Attribute that is really a figure changes silently | The default role is Fact. The catalogue test pins every list. Every Attribute change is archived, audited and listed in History. OD-3. |
| 6 | Shared invoice headers across families | HEADER_DATE_MISMATCH held; orphan headers removed with an archive; apply refuses inconsistent header dates (51724). |
| 7 | Ledger drift if planner 1 is used after the upgrade | Automatic STALE, then resync; the upgrade gate refuses planner 2 until in sync. |
| 8 | Rules change between import and approval | Re-projection must match the proposed hash, otherwise OBSOLETE with "Re-decide this source"; re-decide raises the items again (§6.9). |
| 9 | Switching back to planner 1 | Defined in §13.6: planner-2 files and facts are planner-1 compatible; overlapping files behave as today; IF-021's weakness returns while planner 1 is in use; pending sets become OBSOLETE and are re-decided when switching forward. |
| 10 | The apply context widens who may write facts for a Completed file | It is transaction-scoped, requires the Owner (approval) or a Store Manager running a re-decide pass, and is written only by procedures; tested by `Apply_context_refused_without_owner_or_outside_transaction`. |
| 11 | The builder is not on this PC; contract adoption is delayed | Legacy mode is safe; the reader ships in P1 and the validator in P2; OD-4. |
| 12 | Database growth from evidence, virtual rows and Service snapshots | Measured in Settings; OD-2; OD-6; row spans. |
| 13 | Relaxing locked-day checks (P7) | Only INSERT checks on planner-2 files are relaxed. Fact triggers stay. Changes on locked days are held. Finalising is refused while items are pending. OD-5. |
| 14 | Automation task currently exits 0x1 (no watch folders; findings §1) | Separate 1.9.3 fix. Automation never approves, never restates, and refuses families pending upgrade. |
| 15 | Owner self-approval | Unchanged governance for a one-owner business; recorded in the audit. |

---

## 18. Open decisions for the Owner

| # | Decision | Needed by | Recommendation |
|---|---|---|---|
| **OD-1** | **1-April returns.** Key every invoice by the financial year of its **date**, and keep ETP's INVOICEYEAR only as a label (with a notice when they differ)? | P1 | **Yes.** The data supports it: the 3 package rows that differ are 1-April returns, R025 already ends up keyed this way, and live has 0 invoices that would move. |
| **OD-2** | **Where to keep source files as evidence:** inside the database (atomic with the import, no folder permissions, about 0.2–0.7 GB a year against the 10 GB cap), or in the Documents folder after fixing its permissions? | P1 | **Inside the database.** Revisit if growth passes 1 GB; an export-and-prune action can follow. |
| **OD-3** | **Master-data and status changes** (brand, segment, HSN, ETP reference fields, CN redeemed date, job status): apply from a newer export automatically (archived, audited, listed), or send each to review? | P4 | **Apply automatically.** They are not quantities or amounts. Review would bury real changes, e.g. ETP re-stated REF fields on 880 R030 rows. |
| **OD-4** | **Updating the consolidation builder** to contract v1: it lives on the linked PC (`E:\…\saagar-traders`). Who updates it, and when? | Any time after 1.9.3 is installed | **Update it once 1.9.3 is installed on every PC that imports**, using `CONSOLIDATION-CONTRACT.md` and `--validate-contract` (from 1.9.4). 1.9.3 already skips the new sheets and dates stacked snapshots from the contract; full benefit (rebuilt blocks, latest-export-wins between blocks) arrives with 1.9.5. **Never send a contract workbook to an install older than 1.9.3**: 1.9.2 would date stacked R010 by the maximum date it finds in Info. Keep the raw exports and their hashes from now on. |
| **OD-5** | **Finalised days:** once days are locked, may a workbook that changes nothing on those days still import (the changes on locked days held for you), instead of being refused whole as today? | P4 (ships with it) | **Yes.** Otherwise no two-year workbook can import after the first finalised day, and days are being finalised again from now. Finalised facts stay protected by the fact-level guards, and a day cannot be finalised while its changes wait. |
| **OD-6** | **Service Centre:** (a) must raw Service packs (CSV, names without a family code) be importable, or only consolidated workbooks? (b) refresh cadence until snapshot storage is measured. (c) S001 is built by the consolidation from the ten status views and has no raw export: report it as Not needed? | P8 | (a) **Consolidated only at first.** Add the CSV reader and raw-name patterns once one real raw pack has been checked. (b) **Weekly.** (c) **Yes**; the status views carry the same jobs. |
| **OD-7** | **Automatic replacement of stored figures.** Two cases are safe in principle: an invoice or day exported before the day ended and changed by the next day's export, and a newer reading of the same stock-snapshot date with no row removed. Apply them without approval **only when you run the import** (one-click review items when a Store Manager or the automatic import runs it), always send them to review, or apply them for anyone? | P4 | **Only when you run the import.** Today every change to a stored figure needs you; this keeps that true while saving you clicks. NULL fills of old gross and tax values stay automatic for anyone (they only add missing values, archived). |

---

## Appendix A: pinned tests whose meaning changes

From `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/PhaseOneImportSqlTests.cs`:

| Line | Today | New expectation (planner 2) | Phase |
|---|---|---|---|
| `:44` | FY identity, dedupe, superset; conflicts roll the whole file back | Same identity and dedupe. A changed fact is one review item while the rest imports; nothing changes until approval. | P4 |
| `:75` | Customer-only corrections require restatement | Imported; descriptive only; 0 approvals | P4 |
| `:101` | Re-importing a phase-zero file upgrades it | PRESENT plus FILL (gross and tax); no Owner role needed | P4 |
| `:121` | A changed legacy file cannot auto-supersede | The change is a review item; facts untouched | P4 |
| `:137` | A superset replaces two period files | Documents PRESENT or NEW; no file superseded | P4 |
| `:150` | A locked day blocks the whole file | Only changes on the locked day are held | P7 (release 1.9.5) |
| `:162`, `:220`; `EtpCorpusGoldenTests.cs:101-133` | Golden totals | Unchanged totals (`:220`'s "Duplicate content" becomes "Already present") | P4–P6 |

Planner-1 tests stay green while planner 1 exists (until P9).

## Appendix B: new codes

**SQL errors (51700–51799):**

| Number | Meaning |
|---|---|
| 51700 | FY pre-check |
| 51701 | Duplicate controls pre-check |
| 51702 | Duplicate tenders pre-check |
| 51703 | Duplicate content-row pre-check (file, sheet, row) |
| 51704 | A planner-1 file has source rows on more than one sheet |
| 51705 | A landed row has no source sheet |
| 51710 | Source index: the file is not open, or the role is wrong |
| 51711 | Source index: rows left without metadata |
| 51715 | Apply context: wrong role, no transaction, or the set or pass does not belong to the file |
| 51720 | Change set binding or status |
| 51721 | Base version no longer current |
| 51722 | Proposed rows no longer match (re-decide the source) |
| 51723 | Locked day in the change set |
| 51724 | Shared header date conflict |
| 51725 | `decide_approval_request` refuses `ImportChangeSet` |
| 51740 | Day finalisation refused while items are pending |
| 51750 | Evidence hash format |

**Import codes** (blockers unless marked **W**, warning, or **I**, information):
- `IMPORT_UPGRADE_PENDING`
- `EXPORT_CONTENT_MISMATCH`
- `SNAPSHOT_DATE_UNKNOWN`, `SNAPSHOT_DATE_AMBIGUOUS`, `SNAPSHOT_DATE_FROM_FOLDER` (W), `SNAPSHOT_MULTIPLE_DATES`
- `INFO_BLOCKS_UNUSABLE` (W)
- `CONTRACT_UNREADABLE`
- `PERIOD_OVERLAP_UNRESOLVED`
- `IN_SOURCE_CONFLICT` (document held)
- `LEGACY_BLOCKS_DIFFER` (document held)
- `HEADER_DATE_MISMATCH` (document held)
- `ROW_DATE_MISSING` (W, rows held)
- `FAMILY_DERIVED` (I)
- `INVOICE_YEAR_DIFFERS` (I)
- `STALE_COPY_COLLAPSED` (I)
- `STOCK_ROW_REPEATED` (W)
- `ATTRIBUTE_NOT_APPLIED` (I)
- `INVOICE_TOTAL_MISMATCH` (W)
- `RESTATEMENT_MATCHES_NOTHING`, `RESTATEMENT_TARGET_AMBIGUOUS`, `RESTATEMENT_APPROVAL_REQUIRED`
- `IMPORT_TIMEOUT`
- `COMMIT_OUTCOME_UNKNOWN`
- `EVIDENCE_NOT_RETAINED` (W)
- `CONTRACT_*` (contract §8)

**Decision values** (`import_document_decisions.decision`):

```
NEW, PRESENT, REFRESHED, ATTRIBUTE_UPDATED, FILLED, GROWN, PROVISIONAL_UPDATED, READING_UPDATED,
STALE, NOT_ADDED, PENDING_CHANGE, PENDING_EXISTS, REJECTED_BEFORE, HELD_LOCKED, HELD_HEADER_DATE,
IN_SOURCE_CONFLICT, LEGACY_BLOCKS_DIFFER, ATTESTED_IN_SOURCE, RESTATED_IN_SOURCE, REINTERPRETED, MISSING_FROM_LATER
```
