# Changelog

## [Unreleased]

## [1.9.8] - 2026-10-10

Report polish from the 9 Oct 2026 audit, export reliability, two import fixes and the import-register triage. No migration.

- Summary tab and PDF summary: 19 of 22 reports now show 3-5 KPI cards and one chart built from the rows already loaded (sales, stock, staff, tender and diagnostics, cash book, service, exceptions, management trend), instead of a single "Rows: N" card (RA-EXPORT-05). A PDF summary page comes before the detail rows. The unused SVG/visual code and `ManagementTrendChart` were removed.
- Brand-wise and Brand-Segment Sales are grouped by the owner's brand rows (the same rule as the DSR, shared as `BrandRowSql.MappedRowOfL`) instead of the export's distributor brand; a line no row claims shows as "Unmapped: <brand>" (or "<brand> / <cluster>"), never pooled into one "Other", and the status line names each unmapped brand with its value and points to Settings > Stores & masters > Brands and targets. Totals are unchanged (live Sep 2026: HEMW 7,85,777.00 with 55,590.50 unmapped; WLMHW 10,02,352.00 with 74,832.84 unmapped). The Invoice Source Drill-down Brand column uses the same rule.
- "Variance only" applies only to reports with a variance column and is disabled with a hint elsewhere (it used to empty 18 reports); Returns rows are keyed by store and brand and count distinct return documents (Invoices was always 0); Physical Stock hides two internal columns; Favourites uses the report-list tiles; the footer status line shows the new screen on navigation; Management Trend checks the date window before querying.
- Exports: the focused-workspace Excel/PDF export, Ctrl+E/Ctrl+P and the Actions menu observe the export task and report failures on the status line and in diagnostics (`REPORT_EXCEL_EXPORT_FAILED`, `REPORT_PDF_EXPORT_FAILED`) instead of losing them (RA-EXPORT-01). Every report Save dialog is owned by the main window and starts in `Documents\ETP Reporting Engine\Exports`, the folder "Open export folder" opens (RA-EXPORT-02); the same applies to the Archive pack, Service, management summary and daily pack exports.
- Import (IF-025): a workbook edited in Excel after export (several sheets matching one layout, or one matching sheet plus pivot/extra sheets) is refused with `WORKBOOK_EDITED_AFTER_EXPORT`, naming the sheets and asking for a fresh export, instead of "Unknown layout"; nothing from it is imported.
- Import (IF-021): four fixture tests prove that the same line in two overlapping exports gives one sales line and the later export wins; no engine change was needed.
- Import failure register: rows IF-015 to IF-023 (1 Oct 2026, previously only on the unmerged docs branch) are now in the register with the import-engine spec and decision records; a dated "Triage 10 Oct 2026" section records verdicts for every open row (IF-014, 016-020, 022, 023, 027 fixed; IF-024 and IF-026 verified on live; IF-015 superseded) and proposes IF-028 to IF-032 (OPEN, four need the owner's decision). By this release IF-021 and IF-025 are fixed.
- Tests: about 100 new tests (export path, worksheet writer, PDF pagination, DSR PDF, file names, Save dialog, summaries, brand rows, edited-workbook preflight, overlapping exports); gate (10 Oct 2026, elevated, on 9ad87f3): Release build 0 warnings 0 errors; 3,527 tests, 0 failed, 3 skipped; pre- and after-checks clean. Installer SHA-256 01632119092A08B0C6F85FA8D91037D8EC38406A6F5334FFB67830CB2185A153 (`Installers\ETP Reporting Engine 1.9.8\`).

## [1.9.7] - 2026-10-10

Setup reliability fix plus the first fixes from the 9 Oct 2026 report audit (`Reference\Work in progress 2026-10-09\REPORT-AUDIT\`, 115 findings RA-*). No migration; the five lane branches `fix197/*` are merged here.

- Setup (bootstrap): `Microsoft.PowerShell.Security` is imported explicitly right after the setup log opens, with a retry of up to 13 attempts over 60 s (each attempt logged); a permanent failure writes a FAILED line to the log and exits 1 as before. An existing, already-protected `%ProgramData%\EtpReporting\SetupLogs` folder is trusted early (ownership and ACL checked through .NET, not the flaky module), so a preflight failure on an existing PC now leaves `bootstrap-*.log`. On Workpc the 1.9.6 bootstrap failed in under a second after setup copied its files ("Get-Acl ... module could not be loaded", setup 1603, no log) while the same command run a minute later succeeded; the first use of the module is the protected-folder check in `etp-operations-common.ps1`. The installer itself is unchanged.
- Fixed (IF-027): a Not-needed file in a folder import (the `00_` Service consolidation control file, an unsupported report code, a stray CSV) now records one Information diagnostic (`CONTROL_WORKBOOK_NOT_NEEDED`, `REPORT_FAMILY_NOT_NEEDED`, `NOT_AN_ETP_EXPORT`) instead of the matcher's BLOCKER lines (33 per control file; shown as "69 warning(s)" on the 9 Oct live Service import).
- Tender Reconciliation: invoices of store-days that have no Revenue Report (R022) are listed as Blocked documents with a blank tender instead of being dropped, so the invoice total and the grid are complete; the status says which store and how many invoices are listed unreconciled, and "Tender modes" is omitted when there are none. Tender Diagnostics classifies the covered store's documents when the window is Blocked by a gap and shows the gap message on screen.
- Stock Variance and Stock Movement: a store in scope with no stock-ledger rows in the period is listed as Blocked with the existing "Ledger covers to ... / No stock ledger is imported for ..." text instead of vanishing (ledger coverage is seeded from the requested or active Retail stores); Stock Movement names scoped stores without movements.
- Daily Sales / DSR: when the Variantwise Sales export (R025) does not cover the business date for a store in scope, the report is Blocked with "R025 not imported for <date> (<store>: last <date>) ... FTD and MTD are blank, not zero"; YTD is unchanged.
- Report screens: grid headers are the export's headers and order (one mapping per row type in `ReportGridColumns`), with money marked explicitly (no ₹ on stock quantities), enum values as words ("Missing source", "Missing tender"), years without thousands separators and internal key columns hidden; 0-row results read "No data for <window>, <stores>" (not Passed) and exports are disabled without rows (the DSR excepted); "Select a store" is a status line, not an error or a diagnostics entry; a task that sets the end date clamps the start date to it (Today > Cash after choosing a past business date no longer fails with "end date cannot precede the start date"); the Availability text points to Settings > Stores & masters > Brands and targets > Monthly targets, Today > Walk-ins and Today > Cash > Cash and service entries; the Cash Book hint no longer names a "Daily inputs" screen; "Open selected row details" reuses one modeless window with the grid's headers and formatted values; the Slow / Exception Stock Excel file name no longer contains "/".
- Daily workflow: "Service today" (DSR service card, FTD/MTD/YTD and LY) counts only the shop that enters Service money, read from `v_service_manual_money` (Titan World, decision 16), other shops are not applicable rather than missing. Finalise day is refused only for Blocked sections (missing source reports, missing required inputs including cash reconciliation without opening/expenses/deposit); variances and the not-entered Service section (not required) become named warnings in the finalise message.
- Audit items confirmed not to be defects (RA-UI-03): the Cash Book's "R022 missing" dashes for HEMW were the never-entered manual inputs; HEMW's tenders show.
- Gate (10 Oct 2026, elevated, on 4b1c85e): Release build 0 warnings 0 errors; 3,402 tests, 0 failed, 3 skipped (Desktop 784, Domain 12, Import 1,076, Reporting 112, SQL integration 397, SQL 1,021); pre- and after-checks clean. The first gate run (03:51) failed two integration tests caused by this release (the DSR read of `v_service_manual_money` on a database below 0048; a test that waited for the Excel button, which now stays disabled on 0 rows); both fixed in 4b1c85e. Installer SHA-256 37A1C1DB3576E9181BC9520552A2646156AD3908CFBC0B30ADD226820CDC86DC (`Installers\ETP Reporting Engine 1.9.7\`).

## [1.9.6] - 2026-10-07

One importer fix on top of 1.9.5; no migration.

- Fixed (IF-026): the Service purchase registers (S007 "created date" and S008 "received date") imported nothing, because the export writes zero tax as `0E-8` and the decimal reader did not accept scientific notation (`VALUE_INVALID` on the SGST, CGST and UGST columns of every row). Decimals such as `0E-8`, `1.5E2` and `-1.25E-1` are now read; plain, signed and thousands-separated values are unchanged, and malformed exponents (`E5`, `1E`, `1E999`) still fail as `VALUE_INVALID`. After installing 1.9.6, import the Service raw pack again: the two purchase registers load and every other file shows as Duplicate.
- Gate (7 Oct 2026, elevated, on 7687873): Release build 0 warnings 0 errors; 3,334 tests, 0 failed, 3 skipped (Desktop 736, Domain 12, Import 1,073, Reporting 103, SQL integration 397, SQL 1,013); pre- and after-checks clean. Installer SHA-256 16FCEEBCB343B36ED03BDE1A50E6CA67BF35E2C0A259D928F86B0E2D35F28CC6 (`Installers\ETP Reporting Engine 1.9.6\`).

## [1.9.5] - (date after the 1.9.5 gate)

Service Centre interim import (Service review step S-2, decisions 15 and 16, 3-4 Oct 2026), with migration 0048 (`0048_service_centre_interim.sql`). It ships after 1.9.4, whose migrations are 0046 and 0047; 0049 is reserved for the Tally cost-centre migration of GitHub PR #3 (decision 18), and 1.10.0 numbers its own migrations from 0050. Apart from the R020 TC two-cheque rule (decision 21, below), Retail imports, reports and packs do not change.

- Service Centre (AW330) exports can now be imported, both the weekly consolidated workbooks and the daily raw ETP exports (CSV or XLSX). Each file is kept as a dated snapshot: a consolidated workbook takes the date at the end of its folder name ("Service Centre till 05 oct 2026"), a raw export takes the end date of the window in its file name ("JOB REPORT 30.09.2026 TO 03.10.2026.csv"). The date box on the Import screen is used only for a restatement (Restate ticked, with a reason), so a first import of an undated folder needs the dated folder.
- 36 Service families are imported, each into its own table: S002-S004, S006-S026, S029-S037, S039, S040 and S041.
- **GPRC CLAIM is a new family, S041** (decision 16, Q6/Q7): the raw ETP export `GPRC CLAIM <from> TO <to>` (sheet "GPRC Claims Report", 34 columns) lands in its own table `etp_landing_s041`, in 0048 like the other Service tables (no new migration number). GPRC history is read from S023 (consolidated, up to 5 Aug 2026) and from S041 after; where both hold a claim, S041 wins per claim document, so a claim is never counted twice.
- Reported Not needed instead of failing:
  - S001 (Repair register): it is the union of the ten status lists, built by the consolidation tool, so importing it would count every job twice (`FAMILY_DERIVED`).
  - S005 (Tender collection summary): a summary with no date; the Owner decided it is not needed, and S004 is imported in detail (`SERVICE_FAMILY_NOT_NEEDED`).
  - S038 (SRN report): a retired name; its columns are the same as S011's (`SERVICE_FAMILY_NOT_NEEDED`).
  - S027 (TAT) and S028 (Technician productivity): deferred to the full Service import in 1.10.0 (`SERVICE_FAMILY_DEFERRED`).
  - The `00_` consolidation control file, as before.
- A raw TATA REPORT file (S027 TAT as ETP exports it: a title row first, then the raw header) is not imported yet and shows as an unknown layout, never as Failed. A consolidated S027 file is Not needed, as above.
- Four read-only screens on a new "Service centre" tab of the Reports rail, for Viewers and up: Service jobs by status, Service pending lists, Service job history and Service money check. Each can export to Excel and shows "Service data as at <date>". They never show a customer phone number, e-mail or address.
- The screens read SQL views that pick the latest reading per business date, per job or per list, never the latest file. A rolling 4-day raw window and a weekly consolidated workbook therefore combine correctly, and importing an older file after a newer one changes nothing.
- A job that leaves a list (for example Pending repair) is shown as history on Service job history. It is not a problem and creates no review item.
- **Service money check** (decision 16, Q1-Q4): S004 Cash, Card and UPI are compared, by bill date and per tender, with the manual `SERVICE_CASH`, `SERVICE_CARD` and `SERVICE_UPI` entries of the Titan World shop only, where all of the Service centre's money is entered. Differences are shown (S004 minus manual) and never corrected on either side. A Service entry made at any other shop is listed under "Service entries at other shops (not added)" and never added in. `SERVICE_WDC` stays out of the comparison, and no job advance is deducted (advances are 0; a non-zero S004 advance, cheque or RTGS amount is still shown, with no manual side). A day whose Service money changed between two refreshes is listed under "Money changed since the previous refresh". The manual entries stay the cash-book source.
- AW330 is added to the store list as an inactive Service Centre store (business unit SERVICE). A trigger refuses to make it active (error 51900), because an active store would stop the combined Retail date and the daily packs, and would join the DSR and evening store lists; the same trigger refuses to move a Service store out of the Service business unit (error 51904). Settings > Stores shows it as a Service centre that is not a shop, explains either refusal in plain words, and Import History shows its files as "Service Centre (AW330)" under "All stores".
- Service files never start an automatic report pack, there is no Service day locking, and a Service file with no store column takes AW330 (`SERVICE_STORE_DEFAULTED`).
- A Service file in a folder without a date is refused with `SERVICE_SNAPSHOT_DATE_NEEDED`: "Put the Service files in a folder whose name ends with the date, e.g. 'Service Centre till 05 oct 2026'. The Import screen's date is only for a restatement." When the folder date differs from the latest date on the "Snapshot History" sheet of S006, S009 or S010, the import adds a warning (`SERVICE_SNAPSHOT_DATE_DIFFERS_FROM_HISTORY`) and still imports.
- New `scripts/service-centre/measure-service-growth.sql` (SELECT-only) reports the rows and the space the Service tables use, per table and per reading, to run before and after a refresh.
- Changed: the R020 TC tender for an invoice with more than one blank-agency cheque row (decision 21, the "two-cheque" rule). ETP now adds up all of the invoice's blank-agency R020 CHEQUEAMOUNT rows and fills the R022 shortfall when that total equals it exactly (for example 100 + 29 against a shortfall of 129 fills 129). Otherwise a single row that equals the shortfall exactly fills it, as before; otherwise nothing is filled and the tender difference stays visible (100 + 50 against 129 fills nothing). The amount filled is always the shortfall, once per invoice, so an invoice is never over-filled and R022 cover is never counted twice. Invoices with one matching row are unchanged. In 1.9.4 only the largest row counted, so an invoice whose two rows together made the shortfall showed a tender difference. The R020 scan keeps the 1.9.4 scoping (caller's store and dates, current files only).
- Documentation: a new "Service Centre (interim)" section in `docs/USER-GUIDE.md`, the Service tables and views in `docs/03_DATABASE_SCHEMA.md`, the growth check in `docs/OPERATIONS.md`, the number register `docs/service-centre/SERVICE-INTERIM-NUMBERS.md`, and the import failure register row IF-024 marked fixed on `feature/service-interim`, awaiting acceptance.
- Gate: (after the 1.9.5 gate) — build, unit and SQL integration test counts. Scratch rehearsal on a restored copy: (after the 1.9.5 gate) — outcome counts of the consolidated folder, the re-import and the raw pack, and the growth figures.

- Restore accepts a database prepared on another PC (staging copy) and restores it as EtpReporting (shop PC set-up, decision 24). `restore-etp-database.ps1` takes a backup whose source database is `EtpStaging_*` or `EtpAccept_*`, logs "Backup of staging database <name>; restoring as <configured>", and keeps every other check; any other foreign name is still refused, and a receipt must name the database in the backup. ImportAudit `seed` also writes to `EtpStaging_*` databases (never `EtpReporting`).

## [1.9.4] - 2026-10-04

The report-audit release (HEMW and WLMHW report audits of 3 October 2026), with the fix for the 1.9.3 upgrade stopping at the pre-migration backup:

- Report labels corrected (migration 0046): stock MRP is no longer called cost, document counts say they include returns, NET_SALES and INVOICE_COUNT descriptions corrected.
- Stock ledger bin (migration 0047): the R030 LOCATION is stored on each movement; Stock Variance opens each bin on its own chain and Stock Movement shows the bin.
- Stock Variance, cash book and tender, CRO pairing, DSR walk-ins, import problems / data quality and smaller report-screen fixes (lanes L1-L8).

In detail:

- Fixed: upgrading from 1.9.2 stopped at the pre-migration backup with only "The database operation failed" (VM rehearsal, 3 Oct 2026; setup exit 1603, nothing migrated). The 1.9.3 operations broker had two double quotes in the JSON text of its row-count line, and the scripts send each SQL statement to Sqlcmd as one command-line argument, which cannot carry a double quote: the broker's `CREATE OR ALTER` never reached SQL Server. So every install of the 1.9.3 broker failed: setup's refresh of an unsigned 1.9.2 broker before the backup (fatal), the full module install at the end of setup (a WARNING), the restore helper and "restore first" setup (a missing broker), and step 7 run by hand. The broker now builds that line with `FOR JSON` and contains no double quote; the scripts refuse any statement with one before starting Sqlcmd, and check both broker templates before changing anything.
- Setup's pre-migration backup no longer stops when the broker cannot be brought up to date: the failure is logged as a WARNING and the backup goes ahead through the broker already installed (one from 1.9.2 records `rowCountsNotRecorded` `OPERATIONS_MODULE_OUTDATED`). Without any broker the backup itself fails and setup stops before migrating, as before.
- The setup log now says what SQL Server or Sqlcmd reported when a SQL step fails (error number, level, state and message, without the server name), on a line after the FAILED line, and the steps of the pre-migration backup are logged as they start. The application and the scheduled tasks keep the fixed masked message.
- Report labels corrected after the report audit of 3 October 2026 (HEMW FIX-04/FIX-05, WLMHW FIX-08/FIX-11/FIX-12). Wording only: no figure, rule or count changes.
  - Closing, Slow / Exception and Brand Stock: the "Unit Cost" and "Total Cost" columns hold the snapshot's UCP and TOTALUCP, the GST-inclusive MRP value, not a cost. They are now "Unit MRP" and "MRP value (GST incl.)" on screen and in the Excel and PDF exports, and the export note says the MRP value is UCP × quantity. A real cost column waits for Sagar (Q7).
  - Sales Summary "Bills" and Management Trend "Invoices" are now "Documents (incl. returns)" on screen and in the exports, with a note that they count every INV, SR and BC document while the Daily Sales Report INVOICE count includes INV documents only. Customer-wise Invoices says "N documents (invoices, returns and cancellations)". Which count every screen should use waits for Sagar (Q6).
  - The Daily Sales Report availability line says "recorded GST-inclusive sales (R025 NETAMOUNT) are available" instead of NETVALUE. Customer-wise Invoices and Invoice Sales source history show their value column as "Value incl. GST".
  - Migration 0046 corrects Settings > Calculations: NET_SALES is `SUM(R025.NETAMOUNT)`, "Canonical sales lines from R025 NETAMOUNT (GST-inclusive)"; INVOICE_COUNT is "Distinct INV documents (store + financial year + document number)", with returns and bill cancellations kept in value but not counted. Each corrected row's version goes up by one; running the script again changes nothing.
  - Operations > Sales and control trend labels its all-document count "Documents (incl. returns)" like the Management Trend report; it still said "Invoices" (HEMW August 2026: 48 there against the Daily Sales Report INVOICE count of 47).
  - The Sales Summary names the documents it actually counts. View by Returns counts sales returns (SR) and bill cancellations (BC) only and is headed "Documents (SR, BC)"; a Transaction types filter narrows the header and note the same way (for example "Documents (INV)"). The note says "INV, SR and BC" only when all three are counted.
  - The daily reporting pack's Invoice Summary and Invoice Lineage sheets head the GST-inclusive value (R025 NETAMOUNT) "Value incl. GST" instead of "Net Value", matching the workspace exports.
- Stock ledger bin (WLMHW report audit FIX-14, migration 0047): ETP now stores the R030 / STOCK_LEDGER `LOCATION` column (RETAILBIN, DEFECTIVEBIN, ...) on each stock movement. It is not part of the movement's identity, so line numbers, identities and re-imports behave as in 1.9.3 and an old export is still "already present", never a conflict.
  - Stock Variance takes each bin's opening from that bin's own first movement and adds the bins together for the item. Before, the opening of the period's first movement was used whatever its bin, so an item whose first movement was in DEFECTIVEBIN opened with the defective bin's count only. An item with any movement in the period whose bin is still blank keeps the 1.9.3 opening (the period's first movement), because a blank bin is an unknown bin, not a separate one, and adding it to the known bins would count the same units twice.
  - The Stock Movement report has a Location column and one row per bin.
  - Existing movements get their bin from the ledger rows ETP already holds (the typed R030 table). A movement with no such row keeps a blank bin until a later ledger export covering its day is imported; that import fills the blank on the already-present row (not on a finalised day). Re-importing the very same file does not, because ETP skips duplicate content.
  - Files to re-import: none for the data checked. On the 3 October audit copy every stored movement (both stores: the WLMHW ledger 1 Jul - 25 Aug 2026 and the HEMW ledger 16 Sep 2024 - 28 Sep 2026) gets its bin from the R030 rows already held. Only a Variant Stock Ledger (R030) export that was loaded without its typed R030 rows would leave blank bins; re-import a newer ledger export covering those days to fill them.
Stock Variance and Stock Movement fixes from the report audit of 3 Oct 2026 (no migration):

- Stock Variance takes each item's opening from the ledger's own chain, not from the first row stored (HEMW R-HEMW-02, Titan R-WLMHW-05). The export lists some same-day movements out of time order (an SR at 17:39 before the INV at 12:21; a BC before the INV rows of another document), so 1.9.3 took the wrong row and showed false FAILs (Helios `CECRA26801` +1 for full history, `ME3219I` -1 for FY 2026-27). The opening is now the closing of the last day before the From date whose chain end is clear, else the opening of the first day from the From date whose chain start is clear. Only when every day of an item is a closed loop (for example sold and returned the same day) does the reported closing settle which loop value it was. The ledger is never assumed to start at 0, because the Titan ledger starts on 1 Jul 2026.
- Stock Variance and Stock Movement now include items that sold out (R-WLMHW-04). An item with movements in the period that is missing from the To-date closing snapshot counts as closing 0, so a sold-out item passes and an item the ledger still holds fails (the shrinkage case). Titan 1 Jul-25 Aug Stock Movement now covers all 824 ledger rows (net -67). When the store has no closing snapshot on the To date at all, Stock Variance is Blocked with "Closing stock missing for <date>", and Stock Movement stays empty for that store. Showing movements on days without a snapshot waits for Sagar's answer to Q9.
- Stock Variance is Blocked when the stock ledger ends before the To date and the store has sales after the ledger's end, and says "Ledger covers to <date> for <store> (sales on <date> are not in it)" (R-WLMHW-13). The items stay listed for review. ETP stores a ledger's last row date as its end, not the period the export was run for, so a complete ledger whose last days had no stock movement (a closed or quiet day) also "ends" early. A sale moves stock, so a sale after the ledger's end shows the ledger is short; with no sale the result stands, with the note "Last ledger movement <date> for <store>; no sales after it ...". The daily pack therefore does not fail on a day with no movement and no sale. A store with no stock ledger at all is still Blocked. A gap holding only non-sale movements (a transfer or a receipt with no sale on those days) is not detected.
- The R010 BinWise misdating (R-HEMW-01) was already fixed in 1.9.3. This release only adds an integration test proving that a BinWise reading of a Closing Stock day is never added to Closing Stock, Brand Stock (and Brand Stock Entry), Slow Stock, Physical Closing Stock, Stock Variance or Stock Movement. The stored HEMW data still needs one step on live, after the upgrade:
  - Check first: Closing Stock, HEMW, 29 Sep 2026 shows 495 items, 524 units, 1,05,91,087.00 with snapshot source Closing Stock. The BinWise rows of import 26 stay stored under 29 Sep, but they are hidden because that day has Closing Stock rows.
  - Re-import the same `R010_BinWise_Stock.xlsx` (the HEMW file from the 29 Sep 2026 consolidated package, import 26). ETP now dates it 7 Sep 2026 from its Info block. Its period differs from import 26, so it imports as new rows for 7 Sep, with no duplicate refusal and no restatement. Closing Stock, HEMW, 7 Sep 2026 should then show 466 items, 493 units, 96,90,497.00 as the 1.9.3 query counted it, `GIFT CARD` at -4 included; with the gift-card answer below, Closing Stock leaves `GIFT CARD` out (465 items, 497 units, 96,90,501.00).
  - Import 26 cannot be retired in the app: a restatement needs a replacement whose period covers 29 Sep. Leave it as it is, and keep a Closing Stock import for 29 Sep current.
- What the stock fixes change on live: Helios Stock Variance for any period that covers Jul 2025-Mar 2026 now shows `GIFT CARD` failing by -4. The ledger holds -4 and the closing stock does not list it (see Q2). Helios Stock Variance to 29 Sep 2026 is Blocked with "Ledger covers to 28 Sep 2026" while sales of 29 Sep are stored and no stock ledger up to 29 Sep is imported.
- Reports > Staff (CRO report): R013 staff rows are now paired with R025 sales lines by occurrence (report audit of 3 Oct 2026, R-WLMHW-01, fix list WLMHW FIX-01). The nth R013 row for a store, date, invoice and item goes to the nth R025 line for that key, in source row order; a sales line takes at most one R013 row and a surplus R013 row counts as unmatched. Before, an invoice with the same item on two lines left both R013 rows out as "Ambiguous" (WLMHW: 137 rows, 2,58,825.00), and a stale R013 row stored twice was counted twice (WLMHW 17 Aug 2026, 1,850.00). On the audit copy WLMHW's whole-period CRO total rises from 2,66,78,079.20 to 2,69,35,054.20 (the remaining 37,037.80 against R025 is the known R013 return-value source error), 1-25 Aug 2026 is 9,73,862.00 (was 9,75,712.00, now equal to R025 and the owner's CRO-wise sheet), and HEMW is unchanged. The pairing is done when the report runs, so existing data needs no re-import or migration. The invoice lineage CRO column, the Problems list, the trend's unmatched-row count, the data quality summary and the import result counts use the same pairing. R003 (All Discount Type, one row per discount on a line) keeps its stored match. The sales-line side of the pairing numbers only the paired row's own invoice lines for its item, so a report never sorts the whole sales-line table. R003 is left on its import-time match pending a decision (FIX-01 scope lists R003 too); the stored R013 match_status is not rewritten, as no migration was written.
- Cash Book: only a current R022 (Revenue Report) now counts as tender coverage. A day covered by R020 alone used to show 0.00 in every tender mode and a closing of opening + 0 cash; it now shows "R022 missing / not imported", blank tender modes and retail total, and no closing (WLMHW audit R-WLMHW-02). Daily cash reconciliation shows retail cash as blank on such a day.
- Management Trend: the tender variance is blank, with a "Tender Source" column saying "R022 missing / not imported", for a store-day that has sales but no current R022 file. It used to show 0.00. The export total is blank while any day is missing (R-WLMHW-07).
- TC tender: Titan's R022 does not carry TC, so ETP now reads it from R020 (CHEQUEAMOUNT with a blank AGENCYNAME), only for an invoice whose R022 tenders fall short of its NetValue by exactly that amount, so it is never counted twice. It shows as TC in the Cash Book, Tender Reconciliation and the Management Trend variance, and the accounting source's TENDER_TOTAL now includes it too, so the accounting export and the reports count the same tenders (decision 13 Q3; WLMHW audit R-WLMHW-03). Cash Book "Total sale" now leaves TC out (Cash + Card + UPI + CN + service, as the owner's sheet does); the book's Total line still includes it, so it balances.
- Tender Reconciliation is Blocked, naming the store and dates without R022, when any sales day in the period has no current R022 file. It used to pass with 0 against 0 (R-WLMHW-07). The message still says how many documents failed, and the variance, on the days that were reconciled.
- DSR walk-ins not entered are no longer shown as 0 (report audit 3 Oct 2026, HEMW FIX-03 / WLMHW FIX-06). A store with no walk-in entry in the period now has walk-ins "not entered" instead of 0, and the combined figure is the sum only when every store has an entry. The WALK-INS and CONVERSION KPI cards in the Reports visual summary and in the DSR PDF without evening sheets show "—" with "Data not available", and conversion is no longer worked out from a partial combined total. An entered 0 still shows as 0. The evening matrix and the Today tiles already showed "—" and are unchanged.
- Import > Problems in All stores no longer hides a store's failed import when another store later imports a file with the same name cleanly. A clean import now clears a failure only for the same store, report and file name (report audit R-WLMHW-09: 6 WLMHW problems with WLMHW selected, only 3 in All stores). A failure recorded before its store was known is cleared only by a clean import of the same file bytes, so another store's file of the same name no longer hides it; a file that could not be read at all stays listed after a successful retry until it leaves the date range.
- Settings > Open items stays current (report audit R-WLMHW-10: the grid said 10,935 unmatched enrichment rows and 259 quarantined tenders when the live checks said 274 and 0). The saved issues now update after every import, failed ones included (a folder or batch import updates them once, after its last file), and the screen reloads each time it is opened, not only the first time in a session. Every role, Viewers included, sees when the list was last updated from the live checks, also after an update that found nothing. A check that no longer finds anything says "(0 current)" instead of keeping its last failing count.
Report audit of 3 Oct 2026, small UI fixes (lane L7):

- Sales Summary (Store and Combined) status line now uses Indian digit grouping, like the grid and tiles: "Sales incl. GST 10,15,259.10", not "1,015,259.10". The other report status lines (stock, staff, tender, management trend, exceptions) use the same format (HEMW FIX-07).
- Imports > History shows "This source was already imported. No facts were added by this attempt." for a repeat file again. The message only matched the old outcome text "Duplicate"; the history now classifies repeats as "Duplicate content". Both texts now give the message, in the history query and on the History screen (HEMW FIX-08, WLMHW FIX-16).
- The dashboard overview can be opened again: Settings > Database > Dashboard overview (search "dashboard"). It is Owner-only, like Backups, Recovery drill and Audit trail. It shows system status, the last backup and recovery drill, and the import totals (files, batches, source rows, latest import); recent activity stays on Audit trail. Owner destinations: 92 (Store Manager 69, Viewer 54 unchanged) (WLMHW FIX-15).
- Close day > Store pack / All stores pack: "Generate" is disabled for a Viewer, with the reason on the button ("Owner or Store Manager permission is required..."), instead of failing with a database refusal. SQL already allowed only the Owner and Store Managers to save a pack (WLMHW FIX-17).
- A Viewer is no longer offered "Generate report pack" from a report's Actions menu (disabled, with the same reason), and Ctrl+Shift+P tells a Viewer why instead of jumping to Close day and failing there (WLMHW FIX-17).
Owner report answers (lane L9; Sagar's decisions of 3 and 4 Oct 2026, no new migration):

- Gift cards are not stock and not DSR sales value (HEMW Q2). Lines for the item `GIFT CARD` (R025 brand `GC`) are left out of Closing Stock, Slow / Exception, Brand Stock, Brand Stock Entry, Physical Stock, the daily pack's physical sheet, Stock Variance and Stock Movement, so Helios Stock Variance no longer fails on `GIFT CARD` by -4 and the 7 Sep 2026 R010 re-import described above no longer brings a -4 row into stock. On the Daily Sales Report, gift-card lines leave VALUE, VOL and INVOICE (a bill holding only a gift card is not an invoice, so AUPT and AVPT are not diluted) and show on their own GIFT CARD line (after "Other / unmapped" for each store, after AVPT for COMBINED), in the DSR screen, PDF and Excel. Brand rows plus Other still add up to VALUE. On Helios history this moves the 50,000 gift card of 29 Jul 2025 out of LY YTD VALUE (2,186,215.10 becomes 2,136,215.10 for 25 Aug 2026) onto the GIFT CARD line. "GIFT CARD" is a reserved label in Settings > Evening masters. Sales Summary, Management Trend, Customer-wise, tenders and the accounting export still report every source document, gift cards included. The rule is fixed in ETP; a list the Owner can edit would need a migration (1.10.0 if wanted).
- Helios Brand Stock Entry and Brand Physical Stock offer the owner's own rows (HEMW Q3). Items whose cluster is mapped to a DSR brand row in Settings > Evening masters are grouped under that row (G SHOCK, CITIZEN, FOSSIL, GUESS, SEIKO, AMAZEFIT, FIT BIT), every other item under its brand name (ANNE KLEIN, CERUTI, FT Hearables, KENNETH COLE, POLICE, TOMMY HILFIGER, ...), so HELIOS and HELIOS ACCESSORIES are no longer single rows. Reports > Brand Stock and its export gain a "Brand row" column. Only cluster mappings split a brand, so Titan (whose rows are brand names) keeps its layout. A store-date whose physical counts were already saved under the old HELIOS / HELIOS ACCESSORIES keys keeps the old layout, so past physical sheets still reconcile; the new layout starts on the first day counted with it. The new rows appear once they are entered in Settings (next item).
- Citizen ladies (`CTZNL`) count under the DSR CITIZEN row, and G SHOCK, GUESS, AMAZEFIT and FIT BIT become DSR rows (HEMW Q1 and Q5; D-L9-1, D-L9-2). This is an Owner data step, not a code change: the rows are entered in Settings > Evening masters on live after the upgrade (1.9.4 live runbook). The mapping applies when a report runs, so it changes the DSR for every date, LY and YTD included; the small AMAZEFIT and FIT BIT sales leave "Other / unmapped". A fresh install still gets the 0027 rows until a seed migration in 1.10.0. An integration test proves rows saved this way move the sales out of "Other / unmapped" and leave VALUE unchanged.
- Invoices and returns are counted the same way on every screen (HEMW Q6; replaces the "Documents (incl. returns)" labels above). Sales Summary, Management Trend, Operations > Sales and control trend and Customer-wise show "Invoices" (INV documents only, like the Daily Sales Report INVOICE count) and a separate "Returns" count (distinct documents with a sales return SR or bill cancellation BC line), so a cancelled bill (INV + BC) is 1 invoice and 1 return. Values are unchanged. The DSR itself is unchanged. Customer-wise names the types of a mixed document (for example INV+BC) instead of "MIXED".
- Settings > Calculations: migration 0046 (unreleased) was amended in place (D-L9-3) so NET_SALES says the DSR VALUE leaves gift cards out, and INVOICE_COUNT describes "Invoices" plus "Returns". Any scratch or test database that already ran the earlier 0046 must be rebuilt (the migration journal checksum differs).
- R020 TC tender (from the cash book fix above): the inner R020 query now reads only the caller's store and date range (performance; results unchanged) and only current R020 files (not superseded, data-truth version 1, like the R022 and R025 coverage checks). The rule is unchanged: the largest blank-agency CHEQUEAMOUNT per invoice, used only when it equals the R022 shortfall.
- Slow stock ages recently received items by their receipt date and shows them as NEW, not "never sold" (HEMW Q8 / WLMHW Q5). An item with stock whose latest receipt in the stock ledger (Purchase, STM or Stock Receipt) is after its last sale, or that has no sale, and was received less than 60 days ago is NEW; one never sold and received 60 days ago or more is NEVER SOLD with its age from the receipt; the other bands are unchanged. Closing and Slow / Exception (screen and export) gain "Last Receipt" and "Days Since Receipt" columns; the Slow list keeps NEW items, labelled NEW, and says how many there are; the Brand Stock slow-item count leaves them out. Limit: a store whose stock ledger ends early (Titan's audit data stops on 25 Aug 2026) cannot show NEW for later receipts; those items stay NEVER SOLD until the ledger is imported.
- Stock Movement and Stock Variance mark days without a closing snapshot instead of hiding them (HEMW Q9; completes the sold-out fix above). Stock Movement always lists the movements in the range; when a store has no closing-stock snapshot on the To date the result is Blocked (the rows are still listed), a "Snapshot" column says "no snapshot" on that store's rows, and the message reads "No closing-stock snapshot for <store> on <date>; movements are shown, closing stock cannot be checked". Stock Variance lists that store's items with a blank closing, Blocked, "Closing stock missing for <date> (<store>); items are listed without a closing figure", and still passes or fails the stores that have a snapshot. Closing, Slow and Brand Stock on a date without a snapshot say "No closing-stock snapshot for <store> on <date>" instead of "Blocked: 0 item(s)".
- Setup's `NEXT STEP:` line (automation account not yet a Store Manager), the restore helper's next steps and `docs/INSTALL.md` / `docs/OPERATIONS.md` no longer tell the Owner to start ETP with "Run as administrator" to add the automation account in Settings > Users: since 1.9.3 setup gives every active Owner the right to change users (migration 0043). The NOTE that setup or the restore helper writes when that grant is still missing keeps its advice.

## [1.9.3] - 2026-10-03

The import engine release, with the fixes from the move to Workpc:

- Import engine P0-P2 (migration 0041): document-level decisions, restatement targets, financial-year invoice keys, stock snapshot and ledger fixes, evidence states and pre-checks that refuse an unsafe upgrade before anything is applied.
- Phase 7 Tally groundwork (migrations 0038-0040): tables, screens, evidence, validation and reconciliation only. Nothing is posted to Tally.
- Setup fixes for SQL Server and ETP installed on a drive other than C:, and the automatic-import (watch) folders setup now creates.
- Accounts of a retired PC can be deactivated (migration 0042).
- Owners get `ALTER ANY LOGIN WITH GRANT OPTION` (migration 0043), granted automatically by a one-off SYSTEM task during setup and restore, so Settings > Users works without "Run as administrator".
- Phase 4 recovery drill row counts (migration 0045): each backup receipt records the row counts of four key tables, the monthly recovery drill compares them with the restored copy and fails naming the table that differs, and System status and the dashboard show the latest drill result.

In detail:

- Migrations 0038 and 0039 add the Tally transfer tables: Tally companies and their stores, batch and voucher history, evidence files, validation findings, read-backs, reconciliation results and recovery plans. Only the Owner can see them. Existing accounting batches keep their five statuses.
- New Settings screen "Tally companies" (Settings > Integrations). The Owner can add a test Tally company, link stores to it and record why it changed. A Tally address must be on this PC. Changing a company to live books records the intent only; nothing enables live posting yet.
- Evidence files for a Tally batch are written once into the batch's own folder, registered with their checksum, and can be re-checked later (OK, changed or missing). Linked folders are refused.
- Validation, reconciliation and recovery rules for a batch: ETP checks what it would send, reads a Tally Day Book export saved by hand, compares the two voucher by voucher, and writes a recovery plan and a manifest. Differences can be accepted only by the Owner, with a reason.
- New `docs/audit/PHASE-7-DECISION-SHEET-D12-D18.md` for the Owner to tick the open Tally decisions, and a new section "When Tally and ETP disagree" in `docs/OPERATIONS.md`.
- New `docs/audit/PHASE-5-ACCEPTANCE-WALKTHROUGH.md`: a printable A5.1/A5.2 checklist that goes through every screen as Owner, Store Manager and Viewer.
- Fixes from a review of the Tally code, with migration 0040: a Day Book file for the wrong day no longer marks vouchers as missing; a file left behind by a crash no longer blocks its name; a Tally company's short code, name and books cannot change once batches use it; accepting a failure gives the right message; a blocked voucher of an approved batch stays blocked; saving on the Tally companies screen keeps the posting dates; invoice-view vouchers are read with their sales lines.
- Setup and the operations scripts now find Sqlcmd when SQL Server was installed on another drive. They used to look only in `C:\Program Files`, so on Workpc (installed to `E:\Program Files`) setup stopped with "Sqlcmd was installed but was not found". Sqlcmd is now also looked for where the SQL Server client installers record it in the registry, and in the Program Files folder of the drive ETP is installed on. It must still be in a folder only Administrators or SYSTEM can change; one that is not is skipped, and if none qualifies setup now says why instead of "not found". The ODBC Driver 17 Sqlcmd still comes first wherever it is found, and only it counts as the bundled Sqlcmd being installed (1.9.2).
- Setup on a second drive: the protected-folder check no longer refuses a Windows-formatted data drive (Authenticated Users' Modify on `E:\`, which cannot rename a drive root) or ALL APPLICATION PACKAGES' Full Control on `E:\Program Files` (it only ever narrows what an app may do). Everything that could replace or rename the installation folder or a folder above it is still refused, including a folder owned by a user. The refusal now lists every folder at fault, who has which right, and the exact `icacls` command to fix it. The same rule applies in the application. Found on Workpc on 2 Oct 2026, where `E:\` had to be changed by hand.
- 1.9.3, new-PC fixes found on Workpc (2 Oct 2026), with migration 0042 (numbered after the import engine's 0041):
  - Settings > Users can now deactivate an account of a PC that no longer exists (for example the old laptop's `TFRROWJLTULT009\Sagar`). Deactivating no longer creates a SQL login; the account's database roles are dropped, CONNECT is denied and the change is in the user history as before. Giving access to an account Windows cannot find is refused with a message that says it can only be deactivated. The last active Owner can no longer be deactivated or demoted through the procedure either (it already could not through the table).
  - Setup now finishes the operations module for the automation account itself: when `<PC>\EtpAutomation` is an active Store Manager without the backup rights, setup runs `install-etp-sql-operations.ps1` for it; when it is not a Store Manager yet, the setup log ends with a NEXT STEP line saying what to do. The restore helper's next steps and `docs/INSTALL.md` / `docs/OPERATIONS.md` follow the new order (restore, setup, add the account, setup again).
  - The daily backup and the recovery drill's recording no longer fail with only "The database operation failed" when the automation account lacks its rights: they name the missing right (for example the etp_automation role) and give the exact command. Any other failure keeps its message.
  - Settings > Users shows a clear message for the last-Owner refusal and for an account Windows cannot find.
  - The protected-folder check (setup, the operations scripts and the application) now also refuses the folder that holds a checked file, such as Sqlcmd or the application, when anyone but Administrators, SYSTEM or TrustedInstaller may create files or subfolders in it. A program loads DLLs from its own folder, so a Sqlcmd that the registry places in, say, a folder under `C:\ProgramData` (where every user may create files) could have run a planted DLL as administrator or as the automation account. Folders further up may still allow new files, which cannot replace anything on the path. The check also refuses a path on a network share, a mapped drive or a SUBST drive, whose "root" is an ordinary folder that can be renamed (review findings F1 and F6).
- Setup now creates the automatic-import folders the database points at by default (`Inbound`, `Processed` with `Processed\Duplicate`, `Failed` and `ReportPacks` under `%ProgramData%\EtpReporting`), with the same protection as `Backups` and the other ETP folders. Without them the automation account, which may only read the parent folder, failed every 5-minute run with access denied (Workpc, 2 Oct 2026, fixed there by hand). Setup grants no named user: on Workpc, running 1.9.3 setup replaces the hand fix's Modify right for `WORKPC\Sagar` on these folders.
- Upgrade safety (1.9.3): before applying anything, ETP now runs the pre-checks of every pending migration (0041's 51700 financial year, 51701 duplicate revenue controls, 51702 duplicate tenders). Each migration commits on its own, so until now a refusal in 0041 came after Tally's 0038-0040 had committed, and the shop database was left at 0040: 1.9.3 refused it on every start and 1.9.2 stopped with "Applied migration '0038_tally_transfer_foundation' is missing from the migration source", so only a restore could recover it. Now a refusal leaves the database exactly as 1.9.2 left it, and 1.9.2 still opens it; run `scripts/check-import-upgrade.sql` to see what to fix. A pre-check is the `PRECHECK_*` section of its script and must only read. A release that opens a database a newer release has upgraded now says so ("This database was upgraded by a newer release of ETP ...") instead of "missing". 1.9.2 itself still shows the "missing" message: it means the database is newer than 1.9.2 (see `docs/OPERATIONS.md`, Troubleshooting).
- `scripts/check-import-upgrade.sql` has a new information row, `OPEN_MOVEMENT_CONFLICTS_ON_STORED_ROWS`: open stock-ledger (R003) conflicts on a movement identity the database holds. 1.9.2 kept the first unit row of such a group in file order; 0041 numbers that row line 1, while a re-import numbers the running-balance chain start 1, so a re-import that overlaps it is refused with IMPORT_CONFLICT. Plan Owner restatements for those days before relying on re-imports.
- New integration test of the real upgrade order: a 1.9.2 (0037) database with data goes through Tally 0038-0040 and then 0041 to 0043 in one run, and a 0041 pre-check refusal leaves it at 0037.
- Import engine fixes (1.9.3) are migration 0041 (`0041_import_engine_fixes.sql`). It was written as 0038, which Phase 7's Tally migration took; it was renumbered to run after Tally's 0038-0040. A test now fails if two migrations share a number or the numbers skip one.
- Stock fixes from the 1.9.3 review (in 0041, no new migration):
  - A per-unit stock ledger imported again after the upgrade no longer conflicts when the database already holds one row of a unit chain that is not its start (the old importer kept the first row of the file). That row is matched by its quantities and the other units are added, so the P1 re-import of `R030_Variant_Stock_Ledger.xlsx` can give 0 conflicts. A group whose stored values really differ still conflicts.
  - The upgrade rebuilds the R011 closing-stock rows the old importer logged against another row without storing them: ALREADY_PRESENT or CONFLICT against BinWise (R010) rows, and repeats of an item inside one R011 file. A row is rebuilt only from values that hash as the R011 row did (its own landing row, or a stored row), never from a BinWise row that changed later. A row that cannot be rebuilt (a conflict from before 15 Sep 2026 kept only its hash) no longer disappears from closing stock: its item keeps the BinWise reading it had before the upgrade until its R011 file is restated. `scripts/check-import-upgrade.sql` lists these store-days (SNAPSHOT_R011_BACKFILL).
- Source files kept as evidence (1.9.3 review): "Keep source files for earlier imports…" is now also on Imports > Problems (Owner only) and in task search, not only in Settings. Every import attempt records an evidence state: a file that could not be read or that a cancel never reached says "not attempted", and an import that committed but whose evidence could not be read back says "unknown" (new value in 0041). The evidence size and the earlier-imports walk no longer wait on an import that is running: they skip its uncommitted rows, wait at most 3 seconds for anything else, and say an import is running. The walk counts files an import was holding and database errors separately from unreadable files, reports folders below the depth limit, opens a broken connection again once (or stops with its counts), and has a Stop button.
- Settings > Users tells an Owner who opened ETP normally (not "Run as administrator") that user changes need ETP started as administrator, and turns off "Save user access" until then. Every user change ends with a server-level grant that, since 1.9.2, only an elevated Owner can make; the save used to fail with only "The action could not be completed". If SQL Server still refuses a save for that reason (errors 4613, 15247, 15151) the screen says the same thing; an account Windows cannot find (error 15401) is named as such. The security model is unchanged. The diagnostics log now records each SQL error's number, state, class, procedure and line for any failure caused by SQL Server, still without its message text.
- An Owner can add and change users in Settings > Users without "Run as administrator" (Sagar's decision, 2 October 2026). Migration 0043 (needs 0042) gives every active Owner's SQL Server login `ALTER ANY LOGIN WITH GRANT OPTION`, on new Owners and on existing installs. **Security:** this is a deliberate, server-level power of the Owner: without elevation an Owner can create a SQL Server login for any Windows account, give it ETP access, make it an Owner too, and disable or drop other logins that are not SQL administrators. Store Managers and Viewers never get it. Demoting or deactivating an Owner revokes it with CASCADE, which also takes it from accounts that Owner made Owners; ETP gives it back to every Owner who remains, and refuses (nothing changed) taking away your own Owner access (51472), a revoke SQL Server did not complete (51473), and a demotion that would remove your own right (51474). SQL Server does not let an account grant itself a permission, so the Owner who ran setup or the restore helper gets it through a one-off SYSTEM task (next item). A save refused by a procedure older than 0043 (SQL error 4611) asks for setup to be run. Deactivating an account by name (0042) no longer touches a database user that belongs to a live, renamed account. The restore helper no longer reports a failed Owner recovery that in fact succeeded (on Workpc it said 0 with the Owner row, user and role all in place; it now reads role membership from the catalog instead of IS_ROLEMEMBER).
- Setup now gives the Owner `ALTER ANY LOGIN WITH GRANT OPTION` itself, so Settings > Users works without "Run as administrator" right after install (Sagar's decision, 2 October 2026). The account running setup cannot grant itself a permission, so when an active Owner lacks it setup has SYSTEM grant it through a one-off scheduled task: it first checks, read-only, that SYSTEM is a SQL administrator (on an instance ETP's setup installed it is, through Administrators), writes a fixed T-SQL batch and command file into a new folder only SYSTEM and Administrators can change, runs the task with a two-minute limit, and always unregisters the task and deletes the folder. The batch takes no input: it grants only to logins of active Owner rows that lack the right, Windows user logins only (never a group or a service account), each name quoted. It never stops setup; the log has one line for the outcome, and only if the Owner still lacks the right afterwards a NOTE with the exact manual command for that Owner and instance. `restore-etp-database.ps1` does the same after its Owner recovery. The manual one-off grant in `docs/OPERATIONS.md` ("Owners and SQL Server logins") is now the fallback.
- Settings > Users no longer turns "Save user access" off for a deactivation when ETP was not started as administrator (security review 1.9.3, F5). Only a change that leaves the account active needs the server-level grant; deactivating an account without a SQL Server login, such as the old PC's accounts after a restore, works unelevated. A deactivation SQL Server still refuses says why and changes nothing.
- Migration 0043: the last-Owner guard counts only other Owners who can still sign in (a login, or a Windows account that resolves), so the old PC's Owner rows after a restore no longer let the only usable Owner demote or deactivate itself (security review 1.9.3, F4).
- Import engine review fixes (1.9.3, no new migration): every import route (folder, single file, batch, automation) records the same attempt for the same failure, with the scope, counts and evidence the save reported even when reading the result back fails, and a cancel SQL Server reports as error 0 counts as a cancel. Document holds (IN_SOURCE_CONFLICT, LEGACY_BLOCKS_DIFFER, HEADER_DATE_MISMATCH) are warnings everywhere: they hold the document, never the file. A snapshot block covers only the snapshot dates it read, so a raw closing-stock export no longer marks other snapshots' items as missing; held rows still count as observed for the absence check. A typed row its fact tables cannot store is ROW_FACT_VALUE_MISSING (warning) and holds its document instead of the document being applied without it.
- Phase 5 re-audit fix (1.9.3), migration 0044 (numbered after 0043): the database's own reason checks now refuse a reason made only of white space exactly as the app does. They trimmed spaces only, so an accounting approval reason of tabs or non-breaking spaces passed (0029's constraint and 0033's trigger), and the rejection, approval decision, register, adjustment and restatement checks let a non-breaking space or other Unicode space through. Error numbers and messages are unchanged. An upgrade over an existing approval reason made only of white space is refused before anything changes (51562); the app never wrote one. The import use case also opens every connection, including the restatement approval check, through the local SQL policy at the call.
- Phase 5 re-audit fixes outside SQL (1.9.3, no migration): a restatement or a restatement retry checks the user's role again in the database instead of using the role read at sign-in (S-01). A report email opens its attachment once, without following links, proves through that open file that it lies inside the sharing folder and has a single link, and sends the bytes it read, so a junction or hard link swapped in after the check is refused (S-02). The Phase 5 role-walk and fingerprint tests hold fixed role lists and counts, clean up after a failed or killed run, and refuse a test configuration that turns SQL test parallelism back on (F-20, F-22).
- Installer build: `build-windows-installer.ps1` finds the Inno Setup 6 compiler wherever it is installed (new `-InnoSetupCompiler` parameter, then PATH, Inno Setup's uninstall registration and its default folders), and does so before the release build starts. It used to look only in two hard-coded `C:\Program Files` folders and `%LOCALAPPDATA%`, so on Workpc (`E:\Tools\Inno Setup 6`) it would have refused only after the whole release build and test gate.
- ImportAudit command-line tool (developer tool, partial; not shipped in the installer): `inspect` and `validate-contract` read export files without a database; `check-import --database <name>` predicts, SELECT-only, what 1.9.3's planner 1 does with each file (refusal code, duplicate content, or promotion over the current files) using the same decision the import makes; `baseline --database <name>` runs `scripts/check-import-upgrade.sql` byte for byte. The old disposable-database import moves to `seed` (the form without `seed` still works, with a deprecation notice). Planner 2 forecasts, `dump-state` and `preview` arrive with 1.10.0. Synthetic fixtures (R022 superset, R025 legacy blocks, R030 per-unit ledger) and their tests ship with it.
- `scripts/check-import-upgrade.sql`: check 16 (`OPEN_MOVEMENT_CONFLICTS_ON_STORED_ROWS`) failed on every database with Msg 102 and reported NULL, because two doubled quotes were lost in the movement identity it builds (found on the shop database, 3 Oct 2026; the other checks were unaffected). It now builds the identity exactly as 1.9.2's `persist_stock_movement` did. New tests read the script as T-SQL does and refuse dynamic SQL with a lost quote; the SQL integration test that runs the script now runs it on a database before 0041, as an upgrade does.
- Test fixes: the operations boundary test now waits up to 120 s and prints the script output when it times out; the zip retry import test no longer fails when the random temp folder name happens to contain "bad". SQL integration tests found by a static review to fail on their first run: the financial-year repair script is copied next to the tests; the repeated-movement upgrade test gives each movement its own source row; the upgrade-check test seeds a database before 0041 and finalises its day after the facts. ImportAudit's copy of the import plan decision is checked against the import's own (PlannerOnePlanRulesAgreementTests).
- Recovery drill row counts (Phase 4 A4.4 and A4.4a, decided 2 Oct 2026), with migration 0045 (`0045_recovery_drill_row_counts.sql`, written as 0043 and numbered after the Owner grant option 0043 and the reason white-space 0044):
  - The backup receipt records the row counts of `sales_invoices`, `sales_lines`, `import_files` and `daily_reporting_days`, counted by the operations broker just before and just after the backup. If they differ (an import was running) or could not be taken, the receipt records why instead (`rowCountsNotRecorded`). Counting never fails a backup.
  - The recovery drill counts the same tables in the restored copy, after its integrity check and before it is dropped, and fails when a count differs from the receipt, naming the table and both numbers. A failed drill is recorded as `RestoreDrill` / `Failed` in the audit trail and is not recorded as a verified drill. A backup whose receipt has no counts (older receipts, or the reasons above) still passes the drill, and says so everywhere the result is shown.
  - `<database>-latest-drill.json` lists the four pairs (receipt and restored copy) or the reason there are none, and is written for a failed drill too.
  - System status and the dashboard show the latest drill result, passed or failed, with the four pairs. The application cannot read the protected backup folder, so the drill records its result in the new append-only table `dbo.recovery_drill_results` (written only by the operations account or an Owner). A failed latest drill is a critical warning.
  - `invoke-etp-recovery-drill.ps1 -ReceiptPath <file>` drills the backup named by another receipt in the backup folder, verified like the latest one. Used to show that a receipt with one count changed makes the drill fail (A4.4a; steps in `docs/OPERATIONS.md`).
  - Upgrading: the 1.9.3 operations broker replaces the old one. Setup's final operations-module step now also treats a broker from an earlier build as missing and reinstalls and re-signs it; its earlier `-BrokerOnly` step replaces only an unsigned old broker, never a signed one. Until the new broker is installed, backups record `OPERATIONS_MODULE_OUTDATED` instead of counts, and a drill of a receipt that has counts fails and says to reinstall the module (run setup again).

Status: unsigned; SQL integration suite run pending (it needs an account that can create databases; the tests for 0041, 0042 and 0043 are written and compile). Migrations 0038 to 0043 have not been applied to the shop database. ETP does not send anything to Tally yet: no Tally PC has been tested (plan task 5), and decisions D12 to D18 are still open.

## [1.9.2] - 2026-10-01

Fixes the new-PC setup, which failed on its first real run:

- With "Install Microsoft SQL Server 2025 Express" ticked, setup installed SQL Server and the protected folders, then stopped with "SQL Server preparation, database migration or health validation failed". SQL Server 2025's own setup installs an ODBC Driver 18 Sqlcmd; setup took it as Sqlcmd already installed, skipped the bundled one, and every query then failed because ODBC 18 encrypts by default and refuses the new instance's self-signed certificate. Setup now always installs the bundled Sqlcmd (on ODBC Driver 17), and ETP's scripts use it before an ODBC 18 one.
- SQL Server Express setup no longer makes the account running ETP setup a SQL administrator of its own. Express does that by default; setup now turns it off, so only `Administrators` (and SQL Server's own service accounts) are SQL administrators, as intended. An instance 1.9.1 already installed keeps that login until it is removed by hand.
- A PC left half-set-up by 1.9.1 is finished by running 1.9.2 setup: SQL Server, the protected folders, the EtpAutomation account and the configuration it already has are kept, and only the missing Sqlcmd is installed.

Status: unsigned, like 1.9.1.

## [1.9.1] - 2026-09-27

Setup can now prepare a new PC from the installer alone:

- "Install Microsoft SQL Server 2025 Express" now works on a PC with no ETP on it yet. Setup installs SQL Server 2025 Express (Windows accounts only, no network access, Administrators as SQL administrators), ODBC Driver 17, ODBC Driver 18 and Sqlcmd, then creates the protected folders, the EtpAutomation account and the machine configuration. Until now it stopped at the missing configuration before it could install anything.
- The SQL Server media is unpacked into admin-only folders and deleted after setup, instead of the user's Temp folder, which SQL Server setup's own security check refused.
- New option "Create a new empty ETP database". Untick it to move existing data: setup prepares SQL Server and stops, then the new `scripts\restore-etp-database.ps1` restores the backup (it checks the backup, never replaces an existing database, and makes the person running it the Owner), and setup is run again to update it.
- A new database's Owner gets a SQL Server login of their own, so ETP opens for them without administrator rights.
- An SQLEXPRESS instance that was already on the PC is used only if it has the same hardening; otherwise setup refuses and changes nothing.
- The installer build keeps the SQL media path out of the signing loop, which broke every signed build.

Status: unsigned. The new-PC path is covered by automated tests but has not yet run on a real new PC; its first real run is the move to the new PC.

## [1.9.0] - 2026-09-26

Phase 5 finishes, replaces or deletes the secondary modules per D6:

- accounting and Tally export: approved adjustments, atomic mapping approval, approval reasons, Owner-only reject, honest statuses, duplicate-batch refusal and export receipts
- archive and sharing: SMTP email, WhatsApp handoff and delivery history
- registers
- approvals and adjustments, including restatement requests that wait for Owner approval
- a store catalogue in place of hard-coded stores
- automatic import
- a rewritten Help centre
- investigation click-through
- removal of the OCR / Source Inbox code

Also included:

- the Phase 4 fixes of 25 September: Settings says why a save failed; rotation keeps safety backups; a full backup folder can free space; an encrypting SQL edition is refused in plain words
- migrations 0025-0037

Status: an unsigned build (A4.7). Phase 5 acceptance A5.1-A5.7 is not yet proven on an installed copy. Import bug IF-014 (a slow disk can report a saved import as Failed) is open.

Earlier entries that were listed under Unreleased:

- Restore approved brand mappings, durable import history, query filters with export scope, and selective Retry failed.
- Allow Owner-approved adjustments in accounting mappings.
- Make mapping approval atomic and persist batch approval reasons (migration 0029).
- Add Owner-only rejection of unexported accounting batches with reason, actor/time and audit (migration 0030).

## [1.8.8] - 2026-09-12

UI redesign candidate covering the complete active application. Installed, device and full interactive acceptance remain explicitly unverified; see the sprint ledger. Not approved for production.

- Add canonical task paths, persistent task search, breadcrumbs, retained focused workspaces and grouped Today Overview.
- Preserve explicit task context and register drafts, invalidate changed import validation, and avoid stale source-extraction selection results.
- Separate accounting approval, ledger mapping, sharing contacts, backup, recovery and support actions.
- Record source route coverage, offscreen viewport evidence and automated regression results separately from installed interaction.

## [1.8.7] - 2026-09-12

Engineering candidate; observed role, accessibility and external integration acceptance remains open.

- Recognize an already-imported workbook in the single-file desktop flow without attempting another database write.
- Display an explicit no-change duplicate result and reject identical-file controlled restatements.
- Add regression coverage for both paths and record VM duplicate, reporting, automation and recovery evidence.

## [1.8.6] - 2026-09-11

Engineering candidate for the four-phase review; production acceptance remains subject to the recorded workflow, installer and owner gates.

- Block reconciliation success when no source evidence exists.
- Guard Daily Workflow against stale scope results and overlapping submissions, and clarify input labels.
- Preserve previous Windows candidates and bind installer packaging to the selected payload version.

## [1.8.5] - 2026-08-29

> Source-version record only. No 1.8.5 artifact, installer, SBOM, provenance, signature, tag or release has been produced or promoted.

### Added

- Added migration `0015_operational_audit_contract.sql` so the database audit constraint covers every emitted event, including document review, sharing-contact and visual-render events.
- Added exact import-profile identity and a blocker-free matched-import envelope that preserves the approved report/layout/profile/header-signature provenance through Desktop, automation and SQL persistence.
- Added structured, privacy-safe desktop diagnostics with stable source/code/severity fields and serialized concurrent file writes.
- Added verified pre-migration backup receipts and post-migration database health gates for migration-bearing upgrades.

### Changed

- Moved workbook materialization and report exports away from the WPF UI thread, added cancellation/concurrency bounds and prevented overlapping export actions.
- Existing-database bootstrap now checks SQL/database compatibility and backup capacity, verifies backup path/length/SHA-256 before migration, and requires online/read-write state, exact migration journal count and `DBCC CHECKDB` afterward.

### Fixed

- Made sharing-contact mutation and its operational audit write atomic, and normalized report audit outcomes to the database-supported vocabulary.
- Rejected the preserved 1.8.4 engineering payloads from promotion because their shipped audit contract could reject valid emitted events and their committed SBOM identifies a different source state/application hash than the candidate provenance.

## [1.8.4] - 2026-08-29

### Release disposition

- Preserved for forensic/reproducibility purposes but rejected and never promoted. Its binaries, hashes, SBOM and provenance remain unchanged historical evidence; only a future newly built and independently accepted 1.8.5 candidate can replace it.

### Changed

- Completed route-backed step-by-step Help guidance, reconciled closure and retention documentation, and prepared external-acceptance and release evidence for the integrated candidate.

### Fixed

- Prevented concurrent database-backup invocations from selecting the same filename and overwriting an existing backup.

## [1.8.3] - 2026-08-28

### Changed

- Split the Windows experience into focused workspaces for Settings, Daily Workflow, Archive, Registers, Accounting, Operations, Investigations and Approvals, Administration, Imports, Source Inbox, Reports, Dashboard and Help, while retaining the existing access rules and workflows.
- Production-verified the Daily Sales Report PDF as a single A4 landscape page with the correct business weekday, FTD/MTD/YTD and TY/LY comparisons, value and quantity measures, Service and targets, plus explicit wording when LY MTD data is unavailable.

### Fixed

- Hardened Windows database settings and lifecycle actions to accept only validated Windows Integrated Security connections, persist settings atomically and reject unsafe filesystem links.
- Restored the automatic-backup runtime source used by the packaged app, including encrypted app-private backup scheduling, safe legacy cleanup and verified off-device delivery coverage.
- Made release packaging fail closed on restore, build, test or publish errors, and added an isolated per-user installer lifecycle path with retained failure diagnostics.

## [1.8.2] - 2026-08-27

### Added

- Added a dedicated Daily Sales Report workspace with a fixed business-date/action toolbar, internally scrolling preview, availability indicators and direct PDF, Excel, report-pack, export-folder and Manual Entry actions.
- Added grouped focused workspaces covering all 29 production reports across Sales, Stock, Tender/Cash/Service, Staff, Exceptions, Management and Investigation.
- Added a searchable, tile-based Help Centre with 19 application-area topics, context-sensitive `F1` help and a complete searchable Keyboard Shortcuts guide.
- Added Windows-style back/forward/home navigation and governed report, export, search, save, import and focus shortcuts.

### Changed

- Moved Comfortable/Compact display density from the bottom status bar into the contextual sidebar and retained the persisted preference.
- Kept workspace filters and primary actions fixed while report previews and result grids scroll internally.

### Fixed

- Prevented Help Centre controls from being assigned to multiple WPF logical parents when the Help home is reopened.

## [1.8.1] - 2026-08-27

### Added

- Added the native WPF UI/UX v4 shell with a touch-first global rail, contextual module navigation, responsive detail drawer and persisted comfortable/compact density.
- Added role-aware module and route registries, including the six-card daily workspace, optional Owner modules, and direct navigation to every production report.
- Added reusable visual resources and controls for colours, typography, spacing, icons, cards, status badges and empty/loading states.
- Added UI navigation contract, design-system and implementation-map documentation plus automated navigation and rendered-shell smoke coverage.
- Added a database-driven Manual Entry workspace for walk-ins and future approved non-ETP fields, with role checks, validation, reasons and audit history.

### Fixed

- Prevented a previously selected generic report from being exported while the governed DSR is still loading, and made missing DSR inputs explicit in the one-page PDF.
- Fixed DSR screen construction so each operational-metric control has exactly one WPF logical parent.

## 1.7.0 - 2026-08-26

### Added

- Added a visible category-based Reports Centre with 29 named operational report entries.
- Added closing-stock, stock-movement, brand-stock, slow/exception-stock and printable management-trend reports.
- Added focused missing-source, unmapped-data, tender, stock and staff exception reports.

### Changed

- Exposed staff targets, achievement, ranking, LY comparison and contribution through clearly named report actions while retaining the existing reconciliation control.
- Preserved canonical `NETVALUE`, source-signed sales returns and revenue-report tender controls across every new view.

## 1.6.0 - 2026-08-26

### Added

- Added product navigation, a Home business-day cockpit, Source Inbox, digital Registers, Accounting, global investigation and Approval Centre surfaces.
- Added immutable document storage, native PDF text detection, an optional isolated PaddleOCR helper boundary and a human verify/reject queue.
- Added row-level overlapping-period handling that distinguishes new, already-present and conflicting business facts without overwriting canonical history.
- Added generation-bound ZIP packages with hashed manifests, safe WhatsApp initiation, attached email drafts and an audited sharing address book.
- Added a controlled KPI catalogue, accounting mappings, balanced batches and one-way Tally XML export.

### Changed

- Expanded unattended intake to supported PDFs/images and prevented automatic report-pack generation for dates containing unresolved import conflicts.

## 1.5.0 - 2026-08-26

### Added

- Added Windows-integrated Owner, Store Manager and Viewer roles with audited user administration and protection against removing the last active Owner.
- Added an automatic local watch-folder pipeline for XLSX/ZIP imports, duplicate skipping, processed/failed quarantine, five-minute task execution and automatic combined Excel/PDF packs.
- Added morning and evening report schedules, execution history, and installer-managed task registration/removal.
- Added a SHA-256 verified historical report archive with combined/store generation browsing, comparison and Excel/PDF re-export.
- Added management sales/control trends, a data-quality control centre, controlled Store/Brand Segment/Inventory Group/Tender masters, and in-app backup, recovery-drill and privacy-safe support actions.
- Added full SQL Server and production-corpus acceptance coverage for the Phase 2 migration, roles, masters, archive, analytics, unattended duplicate handling, backup and isolated restore.

### Changed

- The bootstrap now installs daily backup, monthly recovery-drill and five-minute automated-operations tasks; uninstall removes only those tasks and deliberately retains all databases, backups, sources and reports.

## 1.4.1 - 2026-08-26

### Added

- Added explicit atomic source restatement with prior-fact archival, replacement lineage, reason/user metadata and rollback safety.
- Added detailed inventory-group physical counts, independent composition/system variances, and dated staff/CRO targets with LY growth, achievement, ranking and contribution.
- Added workbook/sheet/row invoice drill-down and a traceable daily exception report covering source, tender, staff, cash and physical-stock findings.
- Added complete selected-store and combined Titan + Helios report packs as multi-sheet Excel and paginated PDF.
- Added immutable numbered report generations with SHA-256 control snapshots, plus finalisation linkage and stronger locked-day guards.
- Added missing/unexpected-column diagnostics and expanded privacy-safe audit coverage for sessions, configuration, mappings, restatements, backup and restore drills.

## 1.4.0 - 2026-08-26

- Added an operational business-date workflow with source completeness, controlled manual inputs, daily finalisation and administrator reopen auditing.
- Added import metadata for report code, store, ETP business date, source report date and importing user while retaining a separate import timestamp.
- Added centrally tested Indian financial-year FTD/MTD/YTD/LY period resolution and safe growth/productivity/conversion calculations.
- Added an executable report-to-source registry covering sales summaries, DSR, service, tender/cash, closing stock and staff reporting without guessing unresolved definitions.
- Added database guards that block new imports and manual-input changes against finalised business dates.
- Added a Store Manager Daily Workflow screen and golden business-rule tests.
- Ported and real-corpus verified R003 discount and R013 CRO profiles with atomic, import-order-independent enrichment matching that cannot change canonical revenue.
- Added customer-safe invoice summaries, FTD/MTD/YTD/LY DSR, staff/CRO performance with exact variance diagnostics, controlled service tender reporting, cash-drawer reconciliation and a one-action daily reporting pack.

All notable changes follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and semantic versioning.

## [Unreleased]

## [1.8.0] - 2026-08-27

### Added

- Added a renderer-independent visual report model, report registry, reusable KPI/visual definitions and central Indian-number formatting.
- Added accessible KPI cards and purpose-selected ranking, comparison and trend visuals to the native Reports Centre.
- Added five-sheet analytical Excel workbooks with deterministic support ranges and native Excel charts.
- Added vector PDF management summaries, SVG chart rendering, paginated detail and prominent control status.
- Added golden reconciliation tests for Daily Sales, combined sales and stock, plus large-data performance validation.
- Added the finalized connected Daily Sales Report with reusable WPF cards and an exact one-page A4 landscape Unicode PDF export.
- Added safe DSR formula, missing-source, weekday, Indian-formatting and approved-sample verification coverage.

### Changed

- Selected-report Excel and PDF actions now consume the same visual report model as the on-screen preview while retaining the complete detail table and lineage workflow.

## [1.3.1] - 2026-08-26

### Fixed

- Made monthly recovery-task registration reliable for installed paths containing spaces and made bootstrap failures return a nonzero installer result with a local diagnostic entry.

## [1.3.0] - 2026-08-26

### Added

- Administrator bootstrap installation for SQL Server Express detection/installation, automatic service configuration, database migration, backup access, and scheduled operational tasks.

## [1.2.1] - 2026-08-26

### Changed

- Adopted indefinite backup retention with no automated business-data deletion, two-year operational-audit retention, monthly restore drills, and the approved Owner/Store Manager authority policy.
- Added live backup-destination free-space monitoring with 20 GB warning and 5 GB critical thresholds.

## [1.2.0] - 2026-08-26

### Added

- Privacy-safe operational audit history for application, connection, import, report and export activity.
- Synthetic performance gates covering large sales, stock and tender workloads.
- Database integrity, statistics and audit-retention maintenance automation.
- Offline deployment packaging and a backup-first application rollback workflow.
- Troubleshooting, incident-response and release runbook documentation.

## [1.1.0] - 2026-08-26

### Added

- Operational hardening, expanded import/report workflows, health monitoring, and release-quality automation.

## [1.0.0] - 2026-08-26

### Added

- First verified Windows release with SQL Server imports, sales, tender and stock reports, Excel/PDF export, backup tooling, and installer.
