# Design decision record: importing consolidated workbooks and raw exports as equal sources

**Date:** 1 Oct 2026 (version 2, revised the same day after a completeness review; §7)
**Decision owner:** the Owner (Sagar). This record was prepared by Claude from a design workflow.
**Status:** proposed for the Owner's acceptance. The open decisions are in the spec, §18.
**Implementing spec:** `CONSOLIDATED-AND-RAW-IMPORT-SPEC.md`
**Builder contract:** `CONSOLIDATION-CONTRACT.md`
**Basis:**
- Installed release 1.9.2 (`V:\ETP\Code\Worktrees\installer-sqlcmd15`, head `ca4bc21`).
- The live `EtpReporting` database, read with SELECT-only queries on 1 Oct, evening.
- `IMPORT-FAILURE-REGISTER.md` (IF-006, IF-012, IF-015 … IF-023).
- `2026-10-01-NEW-PC-AND-IMPORT-FINDINGS.md`, section 3 (the Owner's direction).
- The workflow journals `wf_1462d7b3-443` and `wf_4377d20e-a35`.
- The 29 Sep consolidated package.
- The Service Centre plan, design and brief of 29 Sep.

---

## 1. The problem

On 1 Oct the Owner decided that ETP must import both kinds of source, in any order and mixed, and that both routes must arrive at the same facts:
- the **consolidated workbooks** Claude builds, which hold the whole history of a report family and load in one go;
- **raw ETP exports**, one at a time.

Today's importer is file-centric:
- **Overlap.** It tests overlap on a file's *declared* date range (`PhaseOneImportPersistence.cs:41-49`).
- **Already present.** It decides this with a whole-row content hash that includes customer name and phone (`:86-104`).
- **Supersets.** It re-points facts to a superset file (`0025:1910-1931`).
- **Restatement.** A restatement deletes all facts of exactly one previous file (`0035:177-182`). That previous file is found by the replacement's end date (`FolderImportService.cs:121-126`).

The first consolidated load on 1 Oct showed where this breaks:
- 8 of 64 files were refused, and Titan's data needed hand-built reviewed files and two Owner restatements.
- Titan R030, the full Titan R022 and R010 in both stores are still held.
- HEMW stock on 29 Sep is double-counted.
- Failure reasons were lost.
- No source file was retained as evidence.

The requirements every design had to meet (R1–R9) are repeated in the spec, §2.

## 2. The three designs, in brief

### M-1: minimal change
- **Unit of change.** Keeps today's unit of work: one `import_files` row and one transaction per workbook, today's procedures, supersession by file.
- **Keys and dates.** Adds a descriptive-free `fact_key` beside the content key. Adds per-(file, date) state (`import_file_dates`), so overlap, promotion and restatement work on the dates a source actually contains. An export time (`as_of`) per date makes the import order irrelevant.
- **Contract.** An optional `ETP_Contract` sheet, plus a fallback that reads today's Info block table when it covers the Data rows exactly.
- **Targeted fixes.** Stock-ledger occurrence, one financial-year rule, a snapshot source column with a reporting view, persisted diagnostics, restatement scope taken from overlap, automatic legacy promotion, and append-only evidence-folder rights.
- **Delivery.** 6 additive migrations in 8 phases. Phases 0–2 (about 9 days) release the held Titan data.

### B: block-aware, document-level "latest export wins"
- **Unit of truth.** Every source is a set of blocks, one per ETP export; a raw export is one block. The **document** is the unit of truth: an invoice, a stock document, a snapshot or a day.
- **Versions.** Each document has exactly one CURRENT version, enforced by a filtered unique index.
- **What changes stored facts.** Only four things: new documents, NULL-fills of legacy values, growth of date-scoped documents, and Owner-approved **change sets**. Identical content is recorded as an attestation; content from an older export is recorded as stale.
- **Absence.** A document missing from a later complete export becomes a review item. Nothing is deleted.
- **Contract.** Lets each block be rebuilt into its full raw export (`complete`/`delta` + `attests`), so the same export reached either way is recognised once.
- **Evidence.** Source bytes are stored in the database inside the import transaction.
- **Delivery.** A second engine runs beside the first behind a flag. 8 phases, about 56 Codex-days.

### Fact Ledger: row-centric
- **Unit of truth.** The **fact** (one line, one movement, one snapshot row), with a stable identity computed once in SQL.
- **History.** A version history with observations at export times. Column roles go beyond fact/descriptive: Reference, Derived, Status and Label.
- **Automatic changes.** Many: provisional (partial-day) facts, snapshot and status readings, reference changes, and retirement of facts a complete export no longer contains. Only changes to settled facts go to the Owner.
- **Engine.** Per-block transactions, TVP set-based apply, unique identity indexes on the typed tables created after pre-checks that THROW, a migration review set, and an R025-against-R022 reconciliation.
- **Delivery.** A second engine, 9 phases.

## 3. How the judges scored them

Three independent judges each looked through one lens. Each verified claims read-only against the code, the live database and the package workbooks.

| Design | Data correctness | Migration, risk, effort | Service fit and Owner operability | Total |
|---|---|---|---|---|
| **B** | **8** | 5 | **7** | **20** |
| **M-1** | 6 | **7** | 6 | 19 |
| **Fact Ledger** | 7 | 4 | 5 | 16 |

### Judge 1: data correctness (B 8, Fact Ledger 7, M-1 6)

All three designs handle the cases the judge could verify in the package:
- the 17 Aug contact-only copy collapses in R025, R022, R003 and R013;
- the 29 Aug per-unit R025 repeats keep their multiplicity, because only STORETIMESTAMP differs and timestamps are ignored;
- R030 per-unit groups get stable ordinals, because openings are distinct;
- the financial year of the date removes the 1-April R022 collisions (package rows 1041, 3558, 3559).

Why the scores differ:
- **B is safest.** Stored facts change only through NEW, FILL, GROW or an Owner-approved change set. Absence never deletes. The contract makes the raw and consolidated routes provably the same export.
- **M-1:**
  - Its identities are still content-derived, with no hard uniqueness.
  - Latest-wins between blocks is left to the builder, through a contract that cannot rebuild exports.
  - It still dates undated snapshots by the *maximum* path token (`ImportScope.cs:33-35`), the IF-020 mechanism.
- **Fact Ledger:**
  - It applies many changes automatically, including retirement on absence.
  - It treats *estimated* export times as real (64 of 80 live files have no export time in their name).
  - As written, after one block fails it carries on with older blocks.

### Judge 2: migration, risk and effort (M-1 7, B 5, Fact Ledger 4)

**M-1** is the only design that keeps the live engine and its tests as the base:
- Changes are additive; old procedures become wrappers.
- Its pure C# decision engine is testable without SQL, which matters on a PC that cannot run SQL suites reliably.
- It front-loads the held data on a small, verified IF-014 cherry-pick (`b9c5968`).
- It is the only design that regenerates the 32 landing-table locked-day triggers (`0018:73`).

**B** is a rewrite, realistically 75–90 days with two engines maintained side by side. As written:
- Its Phase 1 would drop new snapshot rows from the stock report: a NULL `source_report_code`, and `persist_stock_snapshot` unchanged.
- It misses the landing-table triggers.
- It uses an invalid T-SQL `LIKE` pattern.
- It updates `source_lineage`, which fails on installs with finalised days (`0010:214-225`, error 51043).

**Fact Ledger**, as written:
- Its "no behaviour change" Phase 2 breaks the current engine. A unique enrichment index meets NULL ordinals, and live data has 152 repeat groups. A `DEFAULT 'R011'` snapshot source re-breaks R010/R011 precedence.
- It delays the held Titan data until engine-2 phases 3–4.

Two items apply to every design:
- `trg_stock_snapshots_protect_locked` (`0009:59`) is missing from the trigger list the 0017 pattern disables (`0017:6-11`).
- `trg_import_files_protect_locked` fires on *any* UPDATE (`0017:164-171`), so a backfill must never update `import_files` on an install with finalised days.

### Judge 3: Service fit and Owner operability (B 7, M-1 6, Fact Ledger 5)

**B** gives the Owner the cleanest loop:
- Safe changes apply at import.
- Genuine changes become change sets that the Owner approves in a diff view, applied without re-importing and without retyping a reason.
- A rejected change is never raised again.
- There is a dry run and a guard on finalising days.
- It reads Snapshot History as blocks.
- It controls Service snapshot growth without needing natural keys.

**M-1:**
- It ignores Snapshot History, which fails R2 for S006, S009 and S010.
- It keeps the maximum-token date fallback.
- Its change loop is clunky: the whole file is held, then re-imported with a byte-identical reason.

**Fact Ledger:** its Service keys fail on today's data. S003 `Transaction ID` is blank in all 4,968 rows, and S002 job orders repeat within one export.

Gaps no design solved:
- Service raw files are CSV and have no 12-digit export prefix.
- The builder's rule labels do not match any design's catalogue.
- The builder must keep `Data` resolved, because Customer History and the targets workbooks read it.

## 4. The decision

**We build Design B's model and Owner workflow. We deliver it the M-1 way: on the existing pipeline, in additive phases, with the urgent fixes first. And we use the Fact Ledger's identity discipline.**

We do not build a second engine. Within the same pipeline and transaction model, a per-report-code **planner switch** chooses between:
- **planner 1:** today's file-centric `PlanImportAsync`;
- **planner 2:** the document planner.

The switch is also the rollback lever: no backup restore is needed to go back. Planner 2 writes files and facts that planner 1 can read and recognise (planner-1-compatible line labels and content keys), and the spec defines what planner 1 does with them after a switch back (spec §13.6).

### Why B's model

It is the only model that meets R1, R3 and R5 together:
- **R1, any order.** Latest export wins by ETP export time, never by import order or route.
- **R3, never doubles.** There is one CURRENT version per document, and facts are inserted only for new documents or approved replacements.
- **R3, never silently drops.** Absence becomes a review item in both import orders. Every refusal and every decision is persisted.
- **R5, governance.** Genuine changes go through Owner-approved change sets, with archive, lineage and audit. Every archive, automatic ones included, belongs to an `import_restatements` row.

B was first in two of the three lenses and highest in total.

### Why the M-1 chassis

Judge 2's risk findings against B are real: two engines for months, and several as-written defects. M-1's path keeps:
- one pipeline;
- one transaction per workbook (`SqlServerRepositories.cs:132-180`);
- today's `append_<table>` landing and `persist_*` fact procedures, extended rather than replaced;
- today's tests as the regression net.

It also ships the held Titan data in the first release, phase P1 (1.9.3).

### What was grafted from each design

| From | Grafted | Where in the spec |
|---|---|---|
| B | Document as unit of truth; one CURRENT version per document (`UX_fact_document_versions_current`) | §5.2, §8 |
| B | Change sets applied at approval from rows already landed (no re-import, no retyped reason); REJECTED_BEFORE; finalise-day guard | §10, §5.3 |
| B | Absence is a review item, never a delete (MISSING_FROM_LATER), in both import orders | §8.4 |
| B | Contract blocks that rebuild into the full raw export, so the same export is recognised by either route. B's `attests` lists are replaced by a one-row-per-omitted-row exclusion map whose rows are landed as their own *virtual rows* with their own lineage | Contract §3–4, spec §6.5 |
| B | R022 one row per document (IN_SOURCE_CONFLICT); HEADER_DATE_MISMATCH held, never merged | §7, §8 |
| B | Snapshot dates never taken from the maximum token: SNAPSHOT_DATE_AMBIGUOUS / UNKNOWN | §6.4 |
| B | Source bytes kept in the database inside the import transaction (Owner decision OD-2) | §11.2 |
| B | Read-only "check workbook" CLI (`ImportAudit --check-import`) and "Check without importing" | §9, §12 |
| B | Resumable upgrade, one transaction per (store, report), imports refused until it is done | §13 |
| Fact Ledger | One canonicaliser shared by import, re-decide, approval and upgrade, with a pinned test that they give the same keys | §7.1 |
| Fact Ledger | Hard unique identity indexes on `stock_movements`, `stock_snapshots`, `sales_invoice_controls` and `sales_tenders`, created after pre-checks | §5.1 |
| Fact Ledger | Column roles beyond fact/descriptive: **Attribute** (reference/master data and status, latest export wins, archived) and **Label** (INVOICEYEAR, REFERENCEYEAR) | §7.1 |
| Fact Ledger | Provisional business days, narrowed to the next day's export and, for typed families, to imports by the Owner (OD-7) | §8.3 |
| Fact Ledger | MIGRATION_REVIEW change set raised at upgrade for existing descriptive-only doubles (WLMHW 17 Aug R003, R013; R024, R015 landing rows) | §13.3 |
| Fact Ledger | R025-against-R022 per-invoice reconciliation warning (control net value against line gross) | §5.3, §9 |
| Fact Ledger | Bounded `import_attempt_issues` (≤ 200 per attempt); SELECT-only pre-upgrade checker | §11.1, §13.1 |
| Fact Ledger | Never estimate an export time. **Unknown stays unknown and never wins automatically** (this also fixes the Fact Ledger's own defect). | §6.3 |
| M-1 | Current-engine fixes first (release 1.9.3) | §15 P1 |
| M-1 | Pure, table-tested C# decision engine; preview (full transaction, always rolled back) | §8, §9 |
| M-1 | FY guard migration that THROWs before the R022 rule switches | §5.1 |
| M-1 | Content-key self-check before trusting recomputed keys (UNVERIFIED otherwise) | §13.3 |
| M-1 | `persist_stock_snapshot` derives the source from the lineage record type when the caller passes none | §5.1 |
| M-1 | Regenerating the import-file and landing locked-day triggers, now shipped with the first planner-2 release (OD-5) | §5.5 |
| M-1 / Judge 3 | Minimal contract over today's resolved `Data` (no raw-row duplicate sheet), in the `Info` sheet as the Owner's 1 Oct sketch proposed | Contract |
| Judge 3 | Export-name grammar for Service files (14-digit suffix, `dd.MM.yyyy`), with an export-time basis | §6.3 |
| Judge 3 | Contract rule list that covers the builder's labels (`transactional`, `snapshot`, `current`, `period`, `empty`) | Contract §6 |
| Judge 3 | Snapshot History read as dated blocks (with the contract, or from `Snapshot_As_Of`/`SourceFile` without one) | §6.4 (tier 3), §6.5 |
| Judge 1 | Timestamps are *Ignored*, never Descriptive, so the 29 Aug per-unit repeats survive collapse (pinned by a fixture) | §7 |
| Judge 1 | Upgrade verification compares against the facts actually stored, so existing doubles surface instead of being verified away | §13.3 |
| Judge 2 | Add `trg_stock_snapshots_protect_locked` to every backfill's trigger list; never UPDATE `import_files` or `source_lineage` in a backfill | §5.1, §13.3 |

### What was rejected, and why

| Rejected | Why |
|---|---|
| A second engine beside the first for months (B, Fact Ledger) | Double maintenance and double test surface on a PC that cannot run the suites reliably. A per-report-code planner switch on one pipeline gives the same rollback lever. |
| B's `ETP_Blocks` sheet of raw rows beside `Data` | Doubles workbook size. The exclusion map achieves rebuildable blocks while `Data` stays as it is. |
| The Fact Ledger's "never drop re-stated rows" from `Data` | Changes `Data` for Customer History and the targets workbooks. The contract instead records trimming (`trimmed`, `superseded_rows`). |
| Automatic retirement of stored facts on absence (Fact Ledger) | A filtered or partial export would remove facts without approval. |
| Automatic shrink of a snapshot date (M-1 LATEST_WINS) | Same reason. Changed values may apply automatically; *missing* rows go to review. |
| Estimated export times (Fact Ledger) | They make genuine changes look "historical" and leave them unapplied. |
| The maximum-token date fallback (M-1, today's `ImportScope.cs:33-35`) | It is the IF-020 mechanism. |
| Holding a whole workbook for one changed date (M-1) | The clunky loop on 1 Oct. Documents that are new or present apply; only changed documents wait. |
| Per-block commits (Fact Ledger) | One transaction per workbook already fits (the 1 Oct two-year loads). Re-import after a power-off is idempotent, and blocks already applied are skipped. |
| Natural keys for Service job orders (Fact Ledger) | Falsified by the data. Service documents are snapshot or period documents (key-free) until a corpus test proves a key. |
| Loyalty numbers in document keys (version 1's R015 candidate) | A Descriptive column may never be a key, so no customer identifier is stored in plain text in a new table. |

## 5. Consequences

**Effort.** About 87–105 Codex-days in ten phases (spec §15), against judge 2's 45–50 for M-1 alone. The extra buys the guarantees.

**Early value:**
- After **P1** (release 1.9.3, about 3 weeks): every held Titan file loads, HEMW 29 Sep stock is correct, every failure keeps its reason, every import keeps its source file, and a restatement of overlapping files lets the Owner pick the target.
- After **P4, P5 and P7** (release 1.9.5): the seven core families take consolidated and raw sources in any order, and finalised days no longer block two-year workbooks.

**Owner workload.** It goes down:
- Customer edits, stale copies, legacy periods, per-unit ledger rows, multi-snapshot files and late landing rows need no Owner action.
- Genuine changes arrive as a reviewable list with fact-only diffs; safe items are grouped for one-click approval.
- Predicted pending items on today's data: **5**, namely two 17 Aug enrichment doubles (P3), one misdated HEMW R010 snapshot (P5) and two landing-row doubles in R024 and R015 (P6).

**Governance.** It is kept and narrowed to what changed:
- Owner approval for every genuine change to stored facts; for typed families, a stored quantity or amount changes only with the Owner as approver or as importer (OD-7).
- Archive of every replaced fact, each archive tied to an `import_restatements` row.
- Lineage from every fact to its export (raw file, consolidated block, or the virtual row of an excluded copy).
- Audit with the existing event types.
- Self-approval by the Owner remains possible, as today; this is a one-owner business.

**Builder.** It must be updated (contract v1) on the linked PC, where the workspace lives (not on this PC), once 1.9.3 is installed everywhere. Until then the importer reads today's workbooks in legacy mode, safely but with more reviews (Owner decision OD-4).

**Risks.** These are listed with mitigations in spec §17. The largest:
- effort;
- legacy workbooks with genuinely re-stated lines, now held for review rather than merged;
- misclassified column roles, where the default is Fact (reviewed) and every Attribute change is archived;
- the return of IF-021's weakness while a report is switched back to planner 1.

## 6. Alternatives considered and not chosen

| Alternative | Why not chosen |
|---|---|
| M-1 alone | Fails R2 for Service history, keeps content-derived identities with no database uniqueness, and leaves latest-wins to the builder. Judges 1 and 3 ranked it last or second. |
| B as written | Engine rewrite with as-written defects that break the stock report in its first release. Judge 2 ranked it 5/10. |
| Fact Ledger as written | Too much automatic change, Service keys disproved by data, and its "no-change" phase breaks today's engine. |
| Keep today's engine and hand-build reviewed files | This is what 1 Oct took: two approvals and five reviewed files for one store. It fails R2. |

## 7. Completeness review of version 1, and how version 2 resolves it

A completeness critic checked version 1 against R1–R9, the IF rows, the 1.9.2 source and the live database. Every finding was checked again before revising: the cited code was read (for example `0025:1991-1999`, `0026:5-26`, `0021:8`, `DailyReportingWorkflowRepository.cs:107-135`, `SqlServerReportingQueryRepository.cs:67`, `EtpInvoiceIdentity.cs:11-17`), and SELECT-only queries confirmed the data claims (descriptive-only duplicate groups: R003 file 16, R013 file 14, R024 file 12, R015 file 53 and R022 file 10078, one each; control net value equals line gross for all 407 WLMHW invoices with both; file 78 is superseded by 10079; collation `Latin1_General_CI_AS`; 0 orphan headers; `b9c5968`'s parent is `92a14da`). All 34 findings were valid. Three were resolved in a different way from the one the critic suggested; those are marked **(differs)** with the reason.

| # | Finding (short) | Resolution in version 2 | Where |
|---|---|---|---|
| 1 | `ETP_Excluded` copies have no row of their own, so lineage, re-projection and `v_current_*` break | Map is one row per omitted row with a distinct twin; each map row is landed as a **virtual row** with its own lineage (sheet `ETP_Excluded`); the map is stored in `import_file_block_copies`; views include virtual rows | Spec §3, §5.2, §5.4, §6.5; contract §4 |
| 2 | Approval apply calls `persist_phase_one_enrichment`, which refuses Completed batches (51422) and silently skips existing keys | Transaction-scoped **apply context** accepted by the procedure's open-file check; the procedure returns `@outcome` and planner 1 records it; ALREADY_PRESENT for a NEW document is treated as an identity bug. MIGRATION_REVIEW items use TRIM (delete only) and need no insert | §5.1 E, §5.3, §10.3 |
| 3 | Members PK breaks for in-place changes | Members keyed by (version, table, lineage); **every change creates a new version** listing its members; invariant checked per fact table by `v_document_member_check` | §3, §5.2, §8.2 |
| 4 | Attested or duplicate re-imports can never re-raise OBSOLETE items; content hash depends on staging rules | Blocks record `ruleset_version` and `content_hash_version`; ATTESTED only when the earlier decision used current rules and left nothing OBSOLETE; **"Re-decide this source"** (decision passes); hash mismatch across versions re-lands instead of refusing | §5.2, §6.8, §6.9 |
| 5 | `etp_import_content` has no sheet; `current` rule and v0 R022 collide on the row index | `sheet_name` added and filled from lineage in 0039 (pre-checks 51703–51705); index on (file, sheet, row); append procedures regenerated; one v0 content row per distinct (file, sheet, row) | §5.2, §13.3 |
| 6 | Automatic provisional replacement bypasses the Owner for weeks | Window narrowed to the next day's export; for typed families automatic only when the Owner imports, otherwise one-click review; new **OD-7** | §8.2 rules 13–14, §8.3, §10.1, §18 |
| 7 | Builder may adopt the contract before the importer reads it | Contract reader (layout), sheet skips and contract snapshot dates ship in **P1**; validator in P2; OD-4 timed to 1.9.3; contract says never send to 1.9.2 | §6.1, §15 P1, §18 OD-4; contract header |
| 8 | No day-finalise procedure exists | Guard 51740 added to `tr_daily_reporting_day_lock` (`0021:8`), which sees the `UPDATE … SET status='LOCKED'` in `FinaliseAsync` | §5.3, §8.6 |
| 9 | Declared-range INSERT checks refuse two-year planner-2 files once days are locked | P7's migration ships **with P4** in release 1.9.5; acceptance includes a locked-day run; OD-5 needed by P4 | §5.5, §15, §18 |
| 10 | R003/R013 package files are byte-identical (Duplicate); the 17 Aug double also exists in R024 and R015 landing rows | Expected queue corrected (approve directly); MIGRATION_REVIEW extended to landing families (R024, R015 in P6); R022 file 10078 explained (no item) | §13.3, §13.7, §15 P4/P6 |
| 11 | Hashing and Σ `row_count` undefined where one row feeds several fact tables | Row rule applied before hashing; `row_count` = observation rows; member counts and the invariant defined per fact table | §5.2, §13.3 |
| 12 | Trimmed blocks lose their mapped copies | Trimmed blocks rebuild with mapped copies; map required whenever `excluded_rows > 0`; trimming never removes map rows or their twins | §6.5; contract §3.3, §7 rule 7 |
| 13 | Block-order rule stops older exports being added later | Block numbers become **append order**; export time decides order; `CONTRACT_BLOCK_ORDER` becomes information. **(differs)** The critic suggested relaxing the "twin in a lower-numbered block" rule; with append-order numbering that rule is natural (the twin was already in the workbook), so it is kept | §3, §6.5; contract §3.3, §4, §7 rule 10 |
| 14 | Promised control and tender unique indexes missing | Added in 0038 (C2), with a collation note and a test | §5.1 C2 |
| 15 | S027/S028 builder columns; S001 has no raw export; raw Service names tie; R1 for Service | Consolidation columns stripped into blocks, signature from the raw header; S001 `Derived` → Not needed (OD-6c); raw Service needs `RawNamePatterns`; R1 for Service stated as consolidated-only until OD-6a | §6.1, §7.5, §18; contract §2, §6 |
| 16 | Switching back to planner 1 is undefined | Planner-2 labels made planner-1 compatible; §13.6 defines overlap, restatement, ledger and the returning IF-021 weakness; switch-back test | §7.1, §13.6, §15 P4 |
| 17 | Automatic sets archive without a restatement row; internal archive procedure called from C# | `record_change_set_restatements` runs for AUTO and REVIEW sets before any archive; public `apply_auto_change_set` and `apply_change_item` wrap the internal procedure | §5.3, §10.2 |
| 18 | Phase dependencies and P1 tests incomplete | P2 → P3 added; `ExportNameParser` and snapshot sequencing moved to P1; `SNAPSHOT_MULTIPLE_DATES` test added | §15 |
| 19 | R2 not proven: 61 of 64 package files are Duplicate on live | Replay of 1 Oct from the 25 Sep backup (8 v0 files) with the raw 25 Aug folders and the package, both orders (core in P5, all families in P6), plus a fresh-database load | §2, §15 P5, P6 |
| 20 | Legacy merge unions a re-stated line | Documents whose later legacy block holds a fact row no earlier block holds are **held** (`LEGACY_BLOCKS_DIFFER`). **(differs)** The critic suggested holding any document whose multisets differ across blocks; that would also hold every invoice whose later copy was merely trimmed by exact-repeat exclusion, which is the normal legacy shape, so the narrower test is used | §6.6 |
| 21 | Reconciliation compares net instead of gross | Control `source_net_value` compared with Σ `source_gross_amount`; `GROSS_UNKNOWN` for NULL gross | §5.3 |
| 22 | NOT_ADDED vs RETIRE makes the result order-dependent | NOT_ADDED raises a review item (INSERT); both orders ask the same question | §8.2 rule 3, §8.4 |
| 23 | Orphan headers block new documents forever | Orphans ignored by `read_document_state`, archived and deleted before a NEW document; planner 2 never leaves one | §5.3, §7.3, §10.3 |
| 24 | Exact ledger repeats dropped; first-move readers order by id | Repeats **kept** with their own `line_seq` and a warning; reader orders by `line_seq`. **(differs, by choice)** The critic offered "keep or hold"; keeping is lossless and order-independent, so no review is needed | §5.1 C, §7.2 |
| 25 | Wrong claims about current code | R025's FY wording corrected; "metadata-only" corrected for Express; day-finalise corrected | §0, §7.3, §5.3 |
| 26 | `b9c5968` branches from 1.9.1 | P0 rebases it onto `ca4bc21`, keeping its fixture fix | §15 P0 |
| 27 | Role patterns miss GST and loyalty columns | `*gstn*`, `encircle*`, `*contact*`, `*email*` added; loyalty numbers removed from key candidates | §7.4 |
| 28 | Rows with unparseable dates belong to no document | Held with disposition `H`, warning `ROW_DATE_MISSING`, listed on Problems, excluded from current views | §7.4 |
| 29 | Approvals in P4–P5 had no diffs | Per-document diffs and partial approval moved to P4 | §12, §15 P4 |
| 30 | Audit event types and detail vocabulary are restricted | Existing event types named per action; five count phrases added to `record_operational_audit` | §5.2, §10.6 |
| 31 | Expected counts were pre-P1 | P3 verifies against a baseline recorded after P1 acceptance | §13.3, §15 P1/P3 |
| 32 | FamilyCode vs ReportCode unclear | Database uses ReportCode everywhere (`report_code` columns, keys, locks); only the contract uses FamilyCode | §0, §5.2 |
| 33 | `export_key` omits the period | Period or snapshot date (and basis) added to the key | §5.2, §6.8 |
| 34 | `RESTATEMENT_TARGET_AMBIGUOUS` leaves planner 1 stuck | Owner picks the target in a dialog; the ambiguity code is stored only when no pick is possible (automation) | §12, §14 IF-016 |
