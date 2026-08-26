# Daily Reporting Current-State Implementation Map

This map records the repository evidence used before extending the daily workflow. It distinguishes implemented capability from business definitions that must remain explicit and unresolved.

| Feature | Current implementation | Existing files/classes | Completeness before this change | Implemented extension / remaining work |
|---|---|---|---|---|
| Import | XLSX preflight, exact profile matching, repeated-layout normalization, ZIP/folder batch import, progress/cancel/retry and atomic SQL persistence | `OpenXmlWorkbookReader`, `ImportPreflight`, `BatchImportCoordinator`, `SqlServerTransactionalImportStore` | Operational for R022, R025, stock ledger and closing stock | Import files now retain report code, store, ETP business date, source report date, import user and separate import timestamp. Exact real-corpus R003/R013 profiles and enrichment persistence were added; service still needs an approved populated profile. |
| Sales | R025 line facts preserve source-signed `INV`/`SR`, with `NETVALUE` as primary GST-inclusive sales value | `RetailSalesProfiles.R025`, `R025SqlImportOrchestrator`, `SalesReportingService` | Operational core sales slice | Business-date metadata and central FTD/MTD/YTD/LY period policy added. Exchange/cancellation/credit-note codes remain unresolved. |
| Tender | R022 invoice controls and normalized tender rows; `PAYMENTTYPE25` quarantined; document-level diagnostics | `R022PersistenceProjector`, `InvoiceTenderReconciliationService`, `TenderVarianceDiagnosticService` | Operational, with genuine source variance visible | Daily finalisation requires the existing control to pass. TC/unapproved tender meanings remain configuration items. |
| Stock | Separate immutable movement and closing-snapshot facts, source balance checks and reconciliation | `StockWorkbookParser`, `StockSqlImportOrchestrator`, `StockReconciliationService` | Operational system-stock path | Manual physical/location values are now stored separately. Movement business signs beyond preserved source signs remain unresolved. |
| Reporting | Daily/store/brand/brand-segment/item/returns summaries plus tender and stock controls | `SqlBackedReportingExecutor`, `OperationalReportRepository`, `ReportSourceRegistry` | Core reports operational | Invoice summary, FTD/MTD/YTD/LY DSR, staff/CRO, separate service tender reporting and cash reconciliation were added. Missing manual service values visibly block rather than become zero. |
| Exports | Same renderer-neutral result exports to XLSX and PDF | `OpenXmlReportExporter`, `SimplePdfReportExporter` | Operational per supported report | A one-action privacy-safe daily pack control summary exports to both formats; blocked source families remain visible. |
| Manual input | Not previously present | — | Missing | Versioned definitions, value storage, change reasons, history and locked-day protection added in migration `0005`. Zero remains distinct from missing. |
| UI | Dashboard, import, reports, health, diagnostics, filters and exports | `MainWindow.xaml`, `MainWindow.xaml.cs` | Operational but report-centric | Daily Workflow page added for business date/store readiness, manual values, finalise and administrator reopen. |
| Reconciliation | Sales, invoice/tender, tender diagnosis and stock controls | Reporting services above | Operational controls | Daily readiness and finalisation consume controls without weakening their rules. Cross-report rules are registered centrally. |
| Audit | Privacy-safe operational audit | `OperationalAuditRepository`, `operational_audit` | Operational for app/import/report/export | Manual input, finalisation, reopen and report-pack event types added; manual before/after history is separate. |
| Finalisation | Not previously present | — | Missing | Per-store/business-date lock, administrator reopen reason and database guards against manual changes or new imports added. |
| Testing | Import, persistence, reporting, reconciliation, export, UI and operations suites | `tests-dotnet`, `tests`, scripts | Broad core coverage | Period boundaries, leap year, zero/missing semantics, formula correction, denominator separation, report-source registry and migration boundaries added. |
| Deployment | SQL bootstrap installer, migrations, backups, health and recovery tasks | installer and PowerShell scripts | Operational | Migration `0005` deploys through the existing upgrade-safe bootstrap. |

## Deliberately unresolved business items

- TC and any tender not in the approved dictionary.
- DSR invoice denominator versus staff-attributed transaction denominator.
- Exchange, cancellation, credit-note and zero-value bill classifications beyond existing confirmed `INV` and `SR`.
- Whether a future populated ETP Service export should replace controlled manual service tender entry.
- Physical-stock composition and stock movement signs beyond the preserved ETP source signs.
- Customer display: customer PII is intentionally excluded from canonical reporting facts.

These items are surfaced by `ReportSourceRegistry`; they are not silently guessed.
