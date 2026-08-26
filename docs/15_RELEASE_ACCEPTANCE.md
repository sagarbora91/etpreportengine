# Release Acceptance — 26 August 2026

## Consolidated operational sprint - version 1.1.0

- Daily SQL backup task: installed, enabled, manually executed and scheduled for 22:00 daily.
- Backup/recovery: `CHECKSUM`, `RESTORE VERIFYONLY`, isolated full restore, `DBCC CHECKDB`, 8-file/3,957-lineage comparison and validation-database cleanup passed.
- Database health: Healthy; size, backup age, growth threshold and failed-import indicators verified in the live application.
- Batch import: folder/XLSX/ZIP discovery, safe extraction, progress, cancellation, retry and privacy-safe summaries implemented; archive traversal/link/bomb limits tested.
- Report UX: parameterized store, Brand Segment, transaction-type and item filters; search, sort, variance-only view and row drill-down implemented.
- Dashboard: aggregate charts, import history, management-summary PDF and automatic health warnings implemented.
- Tender diagnostics: evidence-led likely-cause classifications implemented without changing the authoritative control values or failed status.
- PDF output: available for every supported sales, tender, diagnostic and stock result plus management summary.
- Branding/accessibility: application/installer icon, access keys, UI Automation names, live regions and keyboard-focused UI smoke passed.
- Installer lifecycle: clean install, true 1.0.0-to-1.1.0 upgrade and uninstall passed in an isolated directory.
- Versioning: centralized semantic version, changelog, versioned installer name and release metadata implemented.
- Security: zero npm vulnerabilities; zero vulnerable or deprecated .NET packages. A fail-closed web bridge mismatch found by regression testing was corrected.
- Tests: 95 .NET tests and 229 ETP JavaScript tests passed; live filtered SQL reporting and tender diagnostics passed their expected controls.
- Support package: aggregate-only offline ZIP generated; privacy manifest confirms exclusion of source rows, workbook names/paths, customer data and invoice identifiers.
- Documentation: administrator handbook, user manual, accessibility checklist and recovery guide completed.

## Autonomous completion phase

- Startup SQL connection persistence and automatic health check: Passed.
- Operational dashboard: Passed (8 import files, 8 completed batches, 3,957 lineage rows).
- Recent import history and duplicate-file controls: Passed.
- Sales and brand-segment reporting: Passed.
- Stock reconciliation: Passed for 312 matched items.
- Tender control: Failed by source variance as expected; investigation deferred and no pass was forced.
- Excel export regression: Passed.
- PDF export: Parsed successfully and visually verified in landscape format with metadata, totals and page footer.
- Privacy-safe diagnostic logging: Implemented; no source rows, document numbers, customer data or workbook paths are logged.
- SQL Server `DBCC CHECKDB`: Passed; three migrations present.
- Database backup/verification and daily-task scripts: Added.
- Self-contained Windows release: Built with zero warnings and zero errors.
- Windows installer with shortcuts, upgrade identity and uninstall metadata: Built successfully.
- Automated test suite: 82 passed, 0 failed.
- Real-workbook smoke: 8 of 8 approved ETP workbooks passed.
- Graphify code graph: Refreshed after final source changes.

Deferred business inputs remain fail-closed: tender variance resolution, complete category/brand mappings, LY/TY rules and data, and ABV/ASP formulas.

## Delivered operational state

- Microsoft SQL Server 2022 Express `SQLEXPRESS` is installed, running and configured for automatic start. SQL Browser remains disabled.
- Database `EtpReporting` exists with checksum-journaled migrations `0001`–`0003`.
- Eight authoritative workbooks from HEMW and WLMHW are loaded: R025 item sales, R022 invoice/tender controls, Variant Stock Ledger and Closing Stock for each store.
- The database contains eight immutable import-file identities and 3,957 lineage records. Exact duplicate hashes are discoverable and protected by a unique database constraint.
- Daily and Brand-Segment Sales execute successfully from SQL using source-signed `NETVALUE`; negative `SR` values are not reversed again.
- Stock reconciliation executes successfully for 312 products present in both the ledger period and closing snapshot.
- Tender reconciliation executes and currently reports `Failed`, which is a business control result from the supplied evidence, not an application failure. The UI exposes document variances; `PAYMENTTYPE25` remains quarantined and excluded.
- Fixed-format Excel output is available for sales, tender and stock results and carries report period, generation time, rule version, control result, rows and totals. Excel contains no business-calculation formulas.

## Verification evidence

- Release build: zero warnings and zero errors.
- Automated tests: 81 passed, zero failed (Domain 7, Import 29, Reporting 22, SQL 23).
- Real-workbook smoke: eight passed, zero failed.
- Live SQL: database creation, three migrations, all eight imports, SQL reports and duplicate hash lookup passed.
- Backup/recovery: `BACKUP ... WITH CHECKSUM`, `RESTORE VERIFYONLY`, full restore, and restored/source lineage comparison passed. The temporary restored database and backup were removed after verification.
- Published self-contained `win-x64` application launch passed.
- Graphify refreshed successfully, including SQL parsing support.
- `git diff --check` passed.

## Intentionally fail-closed/deferred business scope

- Category reports require an authoritative product-category master. `CLUSTER` is a brand segment and is never relabelled as category.
- LY–TY values require approval of the comparison-period rule. The centralized period abstraction exists; no calendar/weekday/financial-period assumption is silently activated.
- ABV/ASP, gross-sales and detailed stock movement classifications require the outstanding approved formulas/type register.
- PDF output remains deferred because no specific PDF report/layout has been approved. On-screen and Excel are operational.
- Code signing and MSI packaging require signing identity/certificate and release-owner approval. The delivered single-file executable is self-contained but unsigned.

These deferred items do not alter confirmed sales, tender-control or stock-source values. They remain blocked until the business inputs recorded in `11_DECISION_LOG.md` are supplied.
