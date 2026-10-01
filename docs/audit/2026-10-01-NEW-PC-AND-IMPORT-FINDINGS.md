# New PC and first consolidated load: findings (1 Oct 2026)

ETP moved to a new owner PC (Windows 10 Pro, SQL Server 2025 Express installed by ETP's own setup). This note records what the first real run of the new-PC path and the first consolidated data load showed, for Codex and the next phase. Import failures are rows IF-015 to IF-022 in `IMPORT-FAILURE-REGISTER.md`. No customer data is quoted here.

## 1. New-PC setup

Fixed in release 1.9.2 (branch `fix/installer-sqlcmd-odbc17`):

| Commit | Problem | Fix |
|---|---|---|
| `1d37a9a` | SQL Server 2025's setup installs an ODBC Driver 18 Sqlcmd. 1.9.1 counted it as "Sqlcmd installed", skipped the bundled Sqlcmd 15, and every query failed: ODBC 18 encrypts by default and refuses the new instance's self-signed certificate. | `Resolve-EtpSqlCmd` prefers the ODBC 17 Sqlcmd; setup counts only that one as installed, so the bundled Sqlcmd 15 is always installed. |
| `c58f534` | SQL Express setup makes the installing account a sysadmin unless told not to (`ADDCURRENTUSERASSQLADMIN` defaults to True), against the documented hardening. | `/ADDCURRENTUSERASSQLADMIN=False`. |
| `605c46d` | Five Phase 5 WPF test classes ran outside `WpfViewCollection`; on a 20-thread CPU two loaded XAML at once and deadlocked inside WPF (`ItemsControl` type initialiser vs `WpfSharedBamlSchemaContext` lock, seen in a hang dump). The release script's "one test project at a time" ran the whole solution in one `dotnet test`, and its build used unlimited compiler processes. | Classes join the collection; the release script loops over the solution's test projects; the build uses `-m:1`. |
| `ca4bc21` | The Phase 5 role walk failed 3/3 on SQL Server 2025: its first command after the fixture bootstrap received a broken pooled shared-memory connection (an unpooled connection passed). | The fixture clears the database's pool after bootstrapping. **Which bootstrap step breaks the connection is not known.** |

Still open (candidates for 1.9.3):
- **Settings → Users cannot deactivate the old PC's accounts.** `dbo.configure_application_role` runs `CREATE LOGIN … FROM WINDOWS` first; Windows on the new PC cannot resolve `OLDPC\user`, so the save fails. On 1 Oct the inactive end state was produced by SQL (role memberships dropped, `DENY CONNECT`, row inactive with a reason).
- **The restore helper's Owner step fails when the restoring account has its own SQL login** (here added by SQL Express setup): its last statement `GRANT ALTER ANY LOGIN` is "granting to yourself" (error 4627). Everything before it committed. The helper reports "making you its Owner failed".
- **Setup creates no watch folders.** The restored `watch_folder_settings` point to `%ProgramData%\EtpReporting\Inbound|Processed|Failed|ReportPacks`; setup creates only Backups, Documents, Share, SetupLogs and Operations, and EtpAutomation has only read access to the root, so the 5-minute automation task exits with 0x1 on every run and records nothing.
- **Restoring a backup next to a database setup created** needs the empty database moved aside (rename plus file rename) because the restore never replaces and reuses `DATA\EtpReporting.mdf`.

## 2. First consolidated load

Package: the Helios and Titan consolidated workbooks (R001–R032, 16 Sep 2024 – 29 Sep 2026), loaded into the restored 25 Sep database with the Import screen's folder import. Result: 56 of 64 files imported (79,147 rows), 8 refused. After re-importing the eight 25 Aug originals (legacy upgrade) and the Helios supersets, **Helios is fully loaded**. Titan R025/R022/R011 have a verified workaround with reviewed files and two Owner-approved restatements. Held for engine fixes: Titan R030 (IF-018), full Titan R022 history (IF-019), R010 BinWise in both stores (IF-020).

Engine fixes, in suggested order:
1. **Failure diagnostics persisted** on the attempt row (IF-017) — every later diagnosis depends on it.
2. **Restatement lookup by overlap**, not by the replacement's end date; reject rather than silently drop (IF-016).
3. **Stock movement identity with a line/occurrence discriminator** (IF-018).
4. **One invoice-year rule** for R022 and R025 (IF-019, related IF-012).
5. **Snapshot dating per block for R010**, and one snapshot source per store-day in the stock report (IF-020); then correct HEMW 29 Sep, which is double-counted now.
6. **Descriptive customer fields outside the fact identity/content key** (IF-021).

## 3. Direction: consolidated workbooks and raw exports as equal sources (Owner, 1 Oct 2026)

The Owner's decision: ETP must import **both** the consolidated workbooks Claude builds (the whole history of a report family in one workbook, loaded in one go) **and** raw ETP exports, in any order, converging on the same facts. Today's importer is file-centric and treats a two-year consolidated workbook as one large raw export, which caused IF-015, IF-016, IF-020, IF-021 and IF-022.

Proposed shape, to be settled by a design review before the Service Centre importer (which will also take consolidated workbooks) is built:
- **Consolidated-source contract.** The consolidated workbook's `Info` sheet carries a machine-readable block table: source export file and SHA-256, export timestamp, dates covered, row range, snapshot date for snapshot families. A raw export is a one-block source; both go through one code path.
- **Block-aware import.** Each block is treated like the raw export it came from. Where blocks overlap, the **latest export wins** per document, so stale copies (the 17 Aug Titan line) never become second rows. Snapshot families take their date from the block.
- **Overlap by actual dates.** A source clashes with stored facts only on the dates it contains: identical facts are Already present, new dates are New, and only dates whose facts genuinely changed need a restatement — scoped to those dates.
- **Facts versus descriptions.** Quantities, amounts, product, dates and document identity form the facts; customer name and contact are descriptive and update with an audit trail instead of conflicting.
- **Legacy periods promoted automatically** when a newer source contains them unchanged (no need to find the original export).
- **Lineage keeps both sources**, so every figure can be traced to the export (raw or consolidated block) it came from.

The consolidation tooling must emit the contract; `check_workbook.py`'s outdated heuristics (handoff, section 5 item 5) should be retired with it.

**Design review outcome (1 Oct 2026, evening; proposed, awaiting the Owner).** A judge-panel design workflow compared three designs: minimal change, block-aware contract and row-centric fact ledger. The judges' totals were 19, 20 and 16. A completeness review followed, and all 34 of its findings were resolved in version 2. The proposal builds the block-aware model on today's pipeline, with a planner switch for each report code, no second engine, and the urgent fixes first in release 1.9.3. The full set is in `docs/roadmap/import-engine-2026-10-01/`:
- `CONSOLIDATED-AND-RAW-IMPORT-SPEC.md`: the implementation spec for Codex, with phases P0–P9 and about 87–105 Codex-days;
- `CONSOLIDATION-CONTRACT.md`: what the builder must emit;
- `DESIGN-DECISION-RECORD.md`: why this design was chosen.

The Owner's decisions OD-1 to OD-7 are in the spec, §18.
