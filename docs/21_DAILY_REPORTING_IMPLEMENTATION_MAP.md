# Daily Reporting Current-State Implementation Map

This map records the repository evidence used before extending the daily workflow. It distinguishes implemented capability from business definitions that must remain explicit and unresolved.

| Feature | Current implementation | Existing files/classes | Completeness before this change | Implemented extension / remaining work |
|---|---|---|---|---|
| Import | XLSX preflight, exact profile matching, repeated-layout normalization, ZIP/folder batch import, progress/cancel/retry and atomic SQL persistence | `OpenXmlWorkbookReader`, `ImportPreflight`, `BatchImportCoordinator`, `SqlServerTransactionalImportStore` | Operational for R022, R025, stock ledger and closing stock | Import files retain report code, store, ETP business date, source report date, import user and separate import timestamp. Real-corpus R003/R013 profiles, missing/unexpected-column diagnostics, and explicit atomic restatement with archived prior facts are included; service still needs an approved populated profile. |
| Sales | R025 line facts preserve source-signed `INV`/`SR`, with `NETVALUE` as primary GST-inclusive sales value | `RetailSalesProfiles.R025`, `R025SqlImportOrchestrator`, `SalesReportingService` | Operational core sales slice | Business-date metadata and central FTD/MTD/YTD/LY period policy added. Exchange/cancellation/credit-note codes remain unresolved. |
| Tender | R022 invoice controls and normalized tender rows; `PAYMENTTYPE25` quarantined; document-level diagnostics | `R022PersistenceProjector`, `InvoiceTenderReconciliationService`, `TenderVarianceDiagnosticService` | Operational, with genuine source variance visible | Daily finalisation requires the existing control to pass. TC/unapproved tender meanings remain configuration items. |
| Stock | Separate immutable movement and closing-snapshot facts, source balance checks and reconciliation | `StockWorkbookParser`, `StockSqlImportOrchestrator`, `StockReconciliationService` | Operational system-stock path | Detailed per-group display, backstock, defective, Y-location and independently counted physical quantities are audited separately from ETP system stock. Composition and system variances remain visible. Movement business signs beyond preserved source signs remain unresolved. |
| Reporting | Daily/store/brand/brand-segment/item/returns summaries plus tender and stock controls | `SqlBackedReportingExecutor`, `OperationalReportRepository`, `ReportSourceRegistry` | Core reports operational | Invoice summary and workbook/sheet/row drill-down, FTD/MTD/YTD/LY DSR, staff/CRO LY/targets/rank/contribution, detailed physical stock, service/cash controls and traceable daily exceptions are included. Missing values visibly block rather than become zero. |
| Exports | Same renderer-neutral result exports to XLSX and PDF | `OpenXmlReportExporter`, `SimplePdfReportExporter`, `OpenXmlReportPackExporter`, `SimplePdfReportPackExporter` | Operational for individual and full-pack output | Selected-store and combined Titan + Helios packs contain every management report as separate Excel sheets/PDF sections, with metadata, filters, totals, exact warning states and no spreadsheet formulas. |
| Manual input | Not previously present | — | Missing | Versioned general inputs plus detailed physical counts and dated staff/CRO targets have change reasons, history and locked-day protection. Zero remains distinct from missing. |
| UI | Dashboard, import, reports, health, diagnostics, filters and exports | `MainWindow.xaml`, `MainWindow.xaml.cs` | Operational but report-centric | Daily Workflow page added for business date/store readiness, manual values, finalise and administrator reopen. |
| Reconciliation | Sales, invoice/tender, tender diagnosis and stock controls | Reporting services above | Operational controls | Daily readiness and finalisation consume controls without weakening their rules. Cross-report rules are registered centrally. |
| Audit | Privacy-safe operational audit | `OperationalAuditRepository`, database audit/history triggers | Operational for app/import/report/export | Session, configuration, mapping/profile, manual input, stock count, target, finalisation, reopen, pack, restatement, backup and restore-drill events are captured without customer rows. |
| Finalisation | Not previously present | — | Missing | Per-store/business-date lock, administrator reopen reason, immutable hashed report generations and database guards over facts, invoice identity, lineage, imports and manual data are included. |
| Testing | Import, persistence, reporting, reconciliation, export, UI and operations suites | `tests-dotnet`, `tests`, scripts | Broad core coverage | 124 automated tests plus real 12-workbook SQL import, pack export, atomic-restatement rollback, lock/immutability checks, backup/full-restore comparison, UI accessibility smoke and dependency security scans are included. |
| Deployment | SQL bootstrap installer, migrations, backups, health and recovery tasks | installer and PowerShell scripts | Operational | Forward migration `0010` deploys through the existing checksum-controlled bootstrap and has been applied to the local production database after isolated validation. |

## Deliberately unresolved business items

- TC and any tender not in the approved dictionary.
- DSR invoice denominator versus staff-attributed transaction denominator.
- Exchange, cancellation, credit-note and zero-value bill classifications beyond existing confirmed `INV` and `SR`.
- Whether a future populated ETP Service export should replace controlled manual service tender entry.
- Physical-stock composition and stock movement signs beyond the preserved ETP source signs.
- Customer display: customer PII is intentionally excluded from canonical reporting facts.
- Whether independently counted physical stock must equal the sum of display, backstock, defective and Y-location quantities.

These items are surfaced by `ReportSourceRegistry`; they are not silently guessed.
