# Phase 1 — Data truth implementation report

Audit date: **15 September 2026**. Branch: `phase-1/data-truth`. Implementation tree: `a79713f` (report follows in its own commit).

The real consolidated import and all six test suites pass. Phase closure remains pending: Sagar's D11 confirmation, the task-13 phase conflict, native maximized-window screenshots, and Claude's independent audit. The live shop database was not changed.

## 1. What changed (files, one line each; migrations listed with their numbers)

Paths are relative to the repository root. Screenshots are listed separately in section 4.

- `Etp.Reporting.slnx` — Include the command-line import audit project in the solution.
- `README.md` — Document folder imports, isolated review connections, audit database safeguards and Phase 1 reporting values.
- `database/migrations/0017_sales_value_columns.sql` — Upgrade tax and enrichment values, financial-year invoice identity, import ranges, versioned content tracking and restatement constraints.
- `database/migrations/0018_etp_family_tables.sql` — Create typed and generic source tables for every registered report family with source-row lineage, content keys and links from one source document to multiple imports.
- `database/migrations/0019_phase1_masters.sql` — Create editable tender and staff masters, monthly targets and service input definitions; seed proven and provisional tender mappings.
- `database/migrations/0020_remove_document_extraction.sql` — Remove the retired document-extraction table and OCR configuration columns.
- `docs/05_MAPPING_REGISTER.md` — Record approved gross/net/tax, customer, tender, staff, stock and financial-year mappings.
- `src/Etp.Reporting.Application/Imports/FolderImportContracts.cs` — Define folder import options, per-file results, progress, totals and the service interface.
- `src/Etp.Reporting.Application/Imports/ImportPersistenceContracts.cs` — Expose scoped duplicate lookup, content outcomes and new/already-present/conflict counts.
- `src/Etp.Reporting.Application/OperationsAdministration/OperationsAdministrationContracts.cs` — Remove OCR paths from administration configuration contracts.
- `src/Etp.Reporting.Application/Reports/ReportQueryContracts.cs` — Carry fiscal invoice identity, staff names and missing-day counts through application report records.
- `src/Etp.Reporting.Application/SourceInbox/SourceInboxContracts.cs` — Remove text-extraction and extraction-review contracts from document intake.
- `src/Etp.Reporting.Desktop/App.xaml.cs` — Accept temporary connection arguments and start the opt-in review capture session.
- `src/Etp.Reporting.Desktop/Composition/DesktopCompositionRoot.cs` — Wire temporary database connections without changing saved connection settings.
- `src/Etp.Reporting.Desktop/Composition/DesktopStartupCoordinator.cs` — Route startup modes after removing the temporary connection argument pair.
- `src/Etp.Reporting.Desktop/FocusedTaskLayout.cs` — Remove the retired extraction-verification action from primary-button selection.
- `src/Etp.Reporting.Desktop/HelpCentre.cs` — Remove obsolete OCR configuration and health guidance.
- `src/Etp.Reporting.Desktop/ImportReviewSession.cs` — Prepare controlled folder-import and document/settings states for review screenshots.
- `src/Etp.Reporting.Desktop/Modules/Imports/DesktopImportCoordinator.cs` — Apply detected import scope and scoped duplicate handling while retaining original source evidence.
- `src/Etp.Reporting.Desktop/Modules/Imports/ImportOperationState.cs` — Represent folder-run activity in import operation state.
- `src/Etp.Reporting.Desktop/Modules/Imports/ImportWorkspaceView.xaml` — Add folder/ZIP selection, progress, results, diagnostics and restatement controls.
- `src/Etp.Reporting.Desktop/Modules/Imports/ImportWorkspaceView.xaml.cs` — Run folder imports, update per-file progress/results, handle cancellation and expose review states.
- `src/Etp.Reporting.Desktop/Modules/OperationsAdministration/OperationsAdministrationPresentationSession.cs` — Remove OCR fields from administration presentation and save commands.
- `src/Etp.Reporting.Desktop/Modules/Reports/ReportsWorkspaceView.xaml.cs` — Show GST-inclusive values, CRO names and missing-day information in reports and exports.
- `src/Etp.Reporting.Desktop/Modules/Settings/DataTruthMastersView.cs` — Provide editable tender mappings and staff records with Owner access checks.
- `src/Etp.Reporting.Desktop/Modules/Settings/DesktopSettingsPresentationSession.cs` — Keep temporary review connections separate from persisted user settings.
- `src/Etp.Reporting.Desktop/Modules/Settings/SettingsWorkspaceView.Masters.cs` — Attach and create the data-truth master-data editor inside Settings.
- `src/Etp.Reporting.Desktop/Modules/Settings/SettingsWorkspaceView.xaml` — Host tender/staff masters and remove OCR integration controls.
- `src/Etp.Reporting.Desktop/Modules/Settings/SettingsWorkspaceView.xaml.cs` — Initialize master editing and remove obsolete OCR settings handlers.
- `src/Etp.Reporting.Desktop/Modules/SourceInbox/SourceInboxPresentation.cs` — Present scanned documents and remove extraction-specific task state.
- `src/Etp.Reporting.Desktop/Modules/SourceInbox/SourceInboxWorkspaceView.xaml` — Replace extraction review controls with scanned-document attachment and inspection controls.
- `src/Etp.Reporting.Desktop/Modules/SourceInbox/SourceInboxWorkspaceView.xaml.cs` — Handle document attachment, opening and integrity checks without text extraction.
- `src/Etp.Reporting.Desktop/ReviewCapture.cs` — Render controlled WPF review states into screenshot files at requested sizes.
- `src/Etp.Reporting.Desktop/Shell/TaskNavigator.cs` — Route folder import results and master-data tasks; remove extraction destinations.
- `src/Etp.Reporting.Desktop/TaskNavigation.cs` — Map unknown-layout results to Import ETP and remove OCR/extraction task routing.
- `src/Etp.Reporting.Desktop/UiNavigation.cs` — Remove OCR menu entries and describe document storage without extraction.
- `src/Etp.Reporting.Import/Batch/BatchImportSource.cs` — Enforce streamed ZIP limits and CRC integrity, skip lock files and dispose extracted temporary files.
- `src/Etp.Reporting.Import/Batch/ImportPathPolicy.cs` — Identify Excel lock files so folder and ZIP discovery can skip them.
- `src/Etp.Reporting.Import/Conversion/TypedCellConverter.cs` — Normalize numeric identifiers and integral decimals, parse supported dates and treat optional zero dates as empty.
- `src/Etp.Reporting.Import/Etp.Reporting.Import.csproj` — Embed the complete report-family schema catalogue.
- `src/Etp.Reporting.Import/Preflight/ImportPreflight.cs` — Ignore Info sheets, recognize explicitly empty exports and return closest-family layout diagnostics.
- `src/Etp.Reporting.Import/Preflight/ImportScope.cs` — Detect store and date range from staged rows with Info, filename and folder fallback.
- `src/Etp.Reporting.Import/Preflight/MatchedImportEnvelope.cs` — Carry detected scope in the accepted workbook envelope and apply empty-export/multiple-store checks.
- `src/Etp.Reporting.Import/Preflight/WorkbookLayoutNormalizer.cs` — Reject meaningful cells beyond approved repeated-layout columns.
- `src/Etp.Reporting.Import/Profiles/ApprovedImportProfileRegistry.cs` — Build the approved profile collection from the complete family catalogue.
- `src/Etp.Reporting.Import/Profiles/EtpReportFamilies.json` — Define exact headers, canonical columns and types for 31 consolidated families plus raw SOR Ageing.
- `src/Etp.Reporting.Import/Profiles/EtpReportFamilyRegistry.cs` — Load and resolve the embedded family schemas and construct their approved import profiles.
- `src/Etp.Reporting.Import/Profiles/ImportProfileMatcher.cs` — Resolve identical R001/R022 headers using workbook or worksheet identity.
- `src/Etp.Reporting.Import/Profiles/RetailSalesProfiles.cs` — Use the catalogue-backed profiles for established sales and enrichment reports.
- `src/Etp.Reporting.Import/Profiles/StockImportProfiles.cs` — Use the catalogue-backed stock-ledger and closing-stock profiles.
- `src/Etp.Reporting.Import/Staging/ImportRowStager.cs` — Preserve all approved source columns, normalize SR/BC signs and warn/skip unknown transaction types.
- `src/Etp.Reporting.Import/Staging/R022PersistenceProjection.cs` — Include PAYMENTTYPE25 as an eligible signed tender while preserving its source identity.
- `src/Etp.Reporting.Import/Stock/StockWorkbookParser.cs` — Accept all ten approved movement types, preserve source signs and warn/skip unknown types.
- `src/Etp.Reporting.Import/Workbooks/OpenXmlWorkbookReader.cs` — Read date-styled numeric ETP dates safely and retain the workbook source path.
- `src/Etp.Reporting.Import/Workbooks/WorkbookContracts.cs` — Add optional source-path context to workbook snapshots.
- `src/Etp.Reporting.Infrastructure.SqlServer/AutomatedOperationsService.cs` — Run scheduled imports through the shared folder service and aggregate per-file outcomes.
- `src/Etp.Reporting.Infrastructure.SqlServer/DailyReportingPackService.cs` — Export GST-inclusive values, staff names and missing-input-day information in reporting packs.
- `src/Etp.Reporting.Infrastructure.SqlServer/DailyReportingWorkflowRepository.cs` — Recognize imported files across their detected period instead of only one business date.
- `src/Etp.Reporting.Infrastructure.SqlServer/DataTruthMasterRepository.cs` — Load and save tender mappings, staff records and monthly targets with access enforcement.
- `src/Etp.Reporting.Infrastructure.SqlServer/DocumentIntakeService.cs` — Delete the retired native-PDF/OCR extraction implementation.
- `src/Etp.Reporting.Infrastructure.SqlServer/EtpFamilySqlImportOrchestrator.cs` — Persist every remaining family through the shared pipeline and project R010 bin stock into snapshots.
- `src/Etp.Reporting.Infrastructure.SqlServer/EtpInvoiceIdentity.cs` — Calculate financial-year invoice identity and stable, precision-normalized content/line keys.
- `src/Etp.Reporting.Infrastructure.SqlServer/FolderImportService.cs` — Discover and inspect folder/ZIP files, infer scope, order dependencies, retain evidence and report each outcome.
- `src/Etp.Reporting.Infrastructure.SqlServer/ManagedDocumentRepository.cs` — Retain original documents in managed storage and verify their SHA-256 integrity.
- `src/Etp.Reporting.Infrastructure.SqlServer/OperationalCompletionRepository.cs` — Recognize ranged imports and enforce corrected service/manual-input completeness rules.
- `src/Etp.Reporting.Infrastructure.SqlServer/OperationalReportRepository.cs` — Use gross sales, fiscal invoice counts, real staff names, monthly targets and partial-input missing-day counts.
- `src/Etp.Reporting.Infrastructure.SqlServer/PersistenceContracts.cs` — Carry detected periods, accepted family rows, enrichment values and stock snapshots into transactions.
- `src/Etp.Reporting.Infrastructure.SqlServer/Phase2OperationsRepository.cs` — Use GST-inclusive sales in operational summary queries.
- `src/Etp.Reporting.Infrastructure.SqlServer/PhaseOneImportPersistence.cs` — Apply scoped exact/content duplicate detection, safe supersets/restatements, complete family retention and staff placeholder repair.
- `src/Etp.Reporting.Infrastructure.SqlServer/ProductisationModels.cs` — Remove OCR configuration and extraction data models.
- `src/Etp.Reporting.Infrastructure.SqlServer/ProductisationOperationsService.cs` — Store scanned attachments without running extraction or generating extraction-review state.
- `src/Etp.Reporting.Infrastructure.SqlServer/ProductisationRepository.cs` — Remove extraction persistence, link source evidence to scoped imports and use gross sales in accounting export.
- `src/Etp.Reporting.Infrastructure.SqlServer/R022SqlImportOrchestrator.cs` — Persist revenue using detected ranges and financial-year invoice identity.
- `src/Etp.Reporting.Infrastructure.SqlServer/R025SqlImportOrchestrator.cs` — Persist detected ranges, stable line identities and separate gross, net and tax values.
- `src/Etp.Reporting.Infrastructure.SqlServer/RetailEnrichmentSqlImportOrchestrator.cs` — Route signed R003/R013 values, CRO names and stable content identities through transactional persistence.
- `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerAdministrationService.cs` — Map administration settings without retired OCR fields.
- `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerApplicationReportQuery.cs` — Map fiscal identities, staff names and missing-day counts to application report contracts.
- `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerImportPersistenceUseCase.cs` — Route all families and expose scoped duplicate outcomes through the application import use case.
- `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerReportingQueryRepository.cs` — Read gross/net/tax separately and apply editable tender mappings with fiscal identity.
- `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerRepositories.cs` — Persist expanded values and implement transactional source lineage, scoped lookup and audited replacement behavior.
- `src/Etp.Reporting.Infrastructure.SqlServer/SqlServerSourceInboxService.cs` — Expose document listing, attachment and integrity operations without extraction-review APIs.
- `src/Etp.Reporting.Infrastructure.SqlServer/StockImportOrchestrator.cs` — Persist stock with detected period scope and stable source-content identity.
- `src/Etp.Reporting.Reporting/ControlReconciliationService.cs` — Keep invoices from different financial years separate during tender reconciliation.
- `src/Etp.Reporting.Reporting/ReportingQueryContracts.cs` — Carry gross/tax values and financial-year invoice identity into reporting inputs.
- `src/Etp.Reporting.Reporting/RetailReportingPolicy.cs` — Approve gross-value sales, BC returns, Airpay/PhonePe UPI and all ten stock transaction types.
- `src/Etp.Reporting.Reporting/SalesReportingService.cs` — Count distinct invoices by store, financial year and document number.
- `src/Etp.Reporting.Reporting/SqlBackedReportingExecutor.cs` — Use GST-inclusive values and fiscal identities when executing sales and tender reports.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/DesktopImportCoordinatorTests.cs` — Cover scoped duplicate handling and retention of original evidence without new reporting facts.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/OperationsAdministrationWorkspaceViewTests.cs` — Update administration UI assertions after removing OCR configuration.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/SettingsAndOperationsAdministrationPresentationTests.cs` — Verify revised administration presentation contracts without OCR fields.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/SettingsWorkspaceViewTests.cs` — Verify Settings behavior after master-editor and OCR-control changes.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/SourceInboxCompositionTests.cs` — Verify scanned-document composition and remove extraction-review expectations.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/TemporaryConnectionTests.cs` — Verify review connection overrides do not overwrite saved settings.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/UiNavigationTests.cs` — Verify navigation after removing OCR and extraction tasks.
- `tests-dotnet/Etp.Reporting.Desktop.Tests/WorkspaceStateProtectionTests.cs` — Adjust import view assertions for automatic scope controls.
- `tests-dotnet/Etp.Reporting.Import.Tests/BatchImportTests.cs` — Cover Excel lock skipping, ZIP cleanup and forged-size inflation/integrity protection.
- `tests-dotnet/Etp.Reporting.Import.Tests/Etp.Reporting.Import.Tests.csproj` — Copy all artificial family workbooks into test output.
- `tests-dotnet/Etp.Reporting.Import.Tests/EtpCorpusGoldenTests.cs` — Test every family, empty/ambiguous layouts, privacy-safe diagnostics, source signs, bank dates and optional real-corpus totals.
- `tests-dotnet/Etp.Reporting.Import.Tests/ImportPreflightTests.cs` — Update preflight expectations for the expanded approved profile catalogue.
- `tests-dotnet/Etp.Reporting.Import.Tests/MatchedImportEnvelopeTests.cs` — Verify accepted-envelope behavior with the expanded registry and warning-only unknown stock rows.
- `tests-dotnet/Etp.Reporting.Import.Tests/OpenXmlWorkbookReaderAsyncTests.cs` — Exercise date-styled numeric YYYYMMDD, Excel serial and text dates without changing business dates.
- `tests-dotnet/Etp.Reporting.Import.Tests/ProductionWorkbookIngestionTests.cs` — Verify typed sales staging and approved customer-field retention through real workbook reading.
- `tests-dotnet/Etp.Reporting.Import.Tests/R022PersistenceProjectionTests.cs` — Verify signed tender projection with PAYMENTTYPE25 eligible and zero/blank tender omission.
- `tests-dotnet/Etp.Reporting.Import.Tests/RetailSalesProfilesTests.cs` — Remove obsolete assertions that customer fields must be dropped.
- `tests-dotnet/Etp.Reporting.Import.Tests/StockWorkbookParserTests.cs` — Test all ten approved signed stock types, BC receipts and warning-only unknown movements.
- `tests-dotnet/Etp.Reporting.Import.Tests/TypedCellConverterTests.cs` — Verify that numeric identifier decimal scale does not change invoice identity.
- `tests-dotnet/Etp.Reporting.Reporting.Tests/SqlBackedReportingExecutorTests.cs` — Cover GST-inclusive BC reporting and fiscal-year separation of repeated invoice numbers.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/DataTruthReportingSqlTests.cs` — Verify real SQL behavior for manual completeness, monthly targets, staff names, cancellation signs and editable tender mapping.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj` — Copy the artificial family workbooks for SQL integration tests.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/PhaseOneImportSqlTests.cs` — Verify fiscal identity, source corrections, duplicate/superset/restatement atomicity, legacy reimports and real-corpus goldens.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/PhaseOneUpgradeSqlTests.cs` — Verify migration upgrades of legacy invoices and signed tax, including existing locked-day data.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/PhaseZeroSqlTests.cs` — Update migration-era assertions for the current Phase 1 schema.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/ScopedImportDuplicateSqlTests.cs` — Verify report/store/period-scoped duplicates, retained evidence and identical-byte imports across distinct scopes.
- `tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/StaffImportNameSqlTests.cs` — Verify R013 repairs legacy code-only staff names while preserving manually edited names and inactive status.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/DistributionServiceBoundaryTests.cs` — Update distribution test configuration after removing OCR fields.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/FolderImportServiceTests.cs` — Cover automatic scope, per-file failures, cancellation, scoped duplicates, evidence retention and empty exports.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/ImportPersistenceUseCaseTests.cs` — Verify routing for the newly supported report-family persistence path.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/OperationsAdministrationServiceBoundaryTests.cs` — Update administration boundary tests for the reduced configuration contract.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/ProductisationServiceTests.cs` — Update managed-document and configuration tests after removing extraction.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/R022SqlImportOrchestratorTests.cs` — Verify financial-year identity and eligible PAYMENTTYPE25 persistence in revenue packages.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/R025SqlImportOrchestratorTests.cs` — Verify detected scope, stable identity and separate gross/net/tax fields in sales packages.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/SourceInboxServiceAdapterTests.cs` — Verify document attachment/access behavior after removing extraction operations.
- `tests-dotnet/Etp.Reporting.SqlServer.Tests/StockImportOrchestratorTests.cs` — Update stock persistence package expectations for detected scope and accepted source content.
- `tests-dotnet/fixtures/etp-sample/R001_AdvanceOrder_Collection.xlsx` — Artificial one-row AdvanceOrder Collection workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R002_AdvanceOrder_Sales.xlsx` — Artificial one-row AdvanceOrder Sales workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R003_All_Discount_Type.xlsx` — Artificial one-row All Discount Type workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R004_All_Issues_Detail.xlsx` — Artificial one-row All Issues Detail workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R005_All_Issues_Summary.xlsx` — Artificial one-row All Issues Summary workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R006_All_Receipts_Summary.xlsx` — Artificial one-row All Receipts Summary workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R007_All_Receipts_Detail.xlsx` — Artificial one-row All Receipts Detail workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R008_Banking_Details.xlsx` — Artificial one-row Banking Details workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R009_Banking_Summary.xlsx` — Artificial one-row Banking Summary workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R010_BinWise_Stock.xlsx` — Artificial one-row BinWise Stock workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R011_Closing_Stock.xlsx` — Artificial one-row Closing Stock workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R012_CN_Register.xlsx` — Artificial one-row CN Register workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R013_CRO_Wise_Sales.xlsx` — Artificial one-row CRO Wise Sales workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R014_Daywise_Collection.xlsx` — Artificial one-row Daywise Collection workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R015_Encircle_Enrollment.xlsx` — Artificial one-row Encircle Enrollment workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R016_Encircle_Redemption.xlsx` — Artificial one-row Encircle Redemption workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R017_GC_Wise_Redemption.xlsx` — Artificial one-row GC Wise Redemption workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R018_GST_Tax_Report_Issue.xlsx` — Artificial one-row GST Tax Report Issue workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R019_GST_Tax_Report_Receipt.xlsx` — Artificial one-row GST Tax Report Receipt workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R020_Payment_Type_Report.xlsx` — Artificial one-row Payment Type Report workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R021_Purchase_Receipt_Summary.xlsx` — Artificial one-row Purchase Receipt Summary workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R022_Revenue_Report.xlsx` — Artificial one-row Revenue Report workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R023_Scheme_Details.xlsx` — Artificial one-row Scheme Details workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R024_SDB_Document_Wise.xlsx` — Artificial one-row SDB Document Wise workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R025_SDB_VariantwiseSales.xlsx` — Artificial one-row SDB VariantwiseSales workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R026_PRP_SALES.xlsx` — Artificial one-row PRP SALES workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R027_PRP_STM.xlsx` — Artificial one-row PRP STM workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R028_SOR_Sales.xlsx` — Artificial one-row SOR Sales workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R029_Transactionwise_Bank.xlsx` — Artificial one-row Transactionwise Bank workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R030_Variant_Stock_Ledger.xlsx` — Artificial one-row Variant Stock Ledger workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/R031_SDB_VariantwiseSales_OMNI.xlsx` — Artificial one-row SDB VariantwiseSales OMNI workbook for CI parser and persistence coverage.
- `tests-dotnet/fixtures/etp-sample/SOR_AGEING_SOR_Ageing.xlsx` — Artificial one-row SOR Ageing workbook for CI parser and persistence coverage.
- `tools/Etp.Reporting.ImportAudit/Etp.Reporting.ImportAudit.csproj` — Define the disposable-database audit command-line project and copy migration scripts.
- `tools/Etp.Reporting.ImportAudit/Program.cs` — Provide guarded audit database setup, folder imports and aggregate verification output.

- `docs/audit/claude-audit-2026-09/PHASE-1-REPORT.md` — Record commands, actual results, screenshots, mapping proposals, register evidence and outstanding scope decisions.

## 2. How to verify (exact commands, including how to create the empty database and run the folder import)

Run in PowerShell on Windows with .NET 10 and local SQL Express. The commands below use disposable databases. The live shop database `EtpReporting` was not written by this work or its tests.

```powershell
Set-Location 'C:\Codex\Reporting Manger\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f'
dotnet restore Etp.Reporting.slnx
dotnet build Etp.Reporting.slnx -c Debug
dotnet build Etp.Reporting.slnx -c Release
dotnet test Etp.Reporting.slnx -c Release --no-build --nologo --logger 'console;verbosity=minimal'

# Parser goldens alone: no SQL Server needed. CI uses the artificial fixtures.
dotnet test tests-dotnet/Etp.Reporting.Import.Tests -c Release --no-build

# Create an empty audit database and apply migrations 0001 through 0020.
dotnet run --no-build --project tools/Etp.Reporting.ImportAudit -c Release -- --database EtpReportingHelios --rebuild

# A1.11: one action, every original Info sheet present, no store/date arguments.
dotnet run --no-build --project tools/Etp.Reporting.ImportAudit -c Release -- --database EtpReportingHelios --folder 'C:\Codex\Reporting Manger\ETP Source Data\HEMW\till 6 sep 26'

# Repeat: 31 duplicate report files, control workbook Not needed, zero new rows.
dotnet run --no-build --project tools/Etp.Reporting.ImportAudit -c Release -- --database EtpReportingHelios --folder 'C:\Codex\Reporting Manger\ETP Source Data\HEMW\till 6 sep 26'

# Older raw Helios subset: R025/R022/R013/R003 already present; no new sales.
# Expected exit 1 because R008/R009/R014 contain changed overlapping values (section 5).
dotnet run --no-build --project tools/Etp.Reporting.ImportAudit -c Release -- --database EtpReportingHelios --folder 'C:\Codex\Reporting Manger\ETP Source Data\HEMW\HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026'

# A1.1/A1.2: both raw store folders in ONE action on a second empty database.
dotnet run --no-build --project tools/Etp.Reporting.ImportAudit -c Release -- --database EtpPhase1Test_RawAcceptance --rebuild --folder 'C:\Codex\Reporting Manger\ETP Source Data\WLMHW\TITAN ALL REPORT 01 JULY 2026 TO 25 AUG 2026' --folder 'C:\Codex\Reporting Manger\ETP Source Data\HEMW\HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026'

# Open the actual desktop app on the audit database; saved shop settings stay unchanged.
& '.\src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\Etp.Reporting.Desktop.exe' --connection-string 'Server=.\SQLEXPRESS;Database=EtpReportingHelios;Integrated Security=True;TrustServerCertificate=True'
```

For a fresh UI import, run the empty-database command, launch the isolated desktop session, open **Import today's folder**, choose the consolidated folder, and start. The import detects HEMW and each file's range; it does not need the global header date or a manually chosen store. The R010 export snapshot is 7 September, so the folder's combined displayed scope ends on 7 September although its sales end on 6 September.

The audit command permits only `EtpReportingHelios` or `EtpPhase1Test_...` names. `--rebuild` drops only that validated database and creates it again. Tests use the existing Phase 0 fixture's unique `EtpPhase0Test_<guid>` databases; the migration-upgrade test also creates its own disposable database. The optional environment variable `ETP_TEST_SQL_CONNECTION` selects the SQL instance; the fixture always replaces its database name. No test connects to the saved shop database for writes.

Read-only SQL checks:

```powershell
sqlcmd -S '.\SQLEXPRESS' -E -x -C -d EtpReportingHelios -Q "SELECT COUNT(*) sales_lines FROM dbo.sales_lines; SELECT COUNT(*) invoices FROM dbo.sales_invoices; SELECT COUNT(*) conflicts FROM dbo.import_row_outcomes WHERE outcome='CONFLICT'; SELECT COUNT(*) stock_movements FROM dbo.stock_movements; SELECT store_code,document_number,invoice_year,transaction_date FROM dbo.sales_invoices WHERE document_number='100000068' ORDER BY invoice_year;"

sqlcmd -S '.\SQLEXPRESS' -E -x -C -d EtpReportingHelios -Q "SELECT YEAR(i.transaction_date) yr,MONTH(i.transaction_date) mon,COUNT(DISTINCT i.sales_invoice_id) invoices,SUM(l.source_quantity) qty,SUM(l.source_gross_amount) NETAMOUNT,SUM(l.source_net_amount) NETVALUE,SUM(l.source_tax_amount) TAX FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id GROUP BY YEAR(i.transaction_date),MONTH(i.transaction_date) ORDER BY yr,mon;"
```

`Whole_Helios_folder_matches_monthly_golden_reimports_and_raw_subset_without_new_sales` compares every month directly with the private golden CSV, including quantities, tax and SR counts/amounts. `Both_raw_store_folders_import_in_one_action_with_golden_sales_tenders_staff_and_headers` checks daily/monthly money, source-table counts, tender reconciliation, staff totals and row reordering. Private corpus tests give an explicit skip reason when the necessary folders/CSV are absent; the 32 artificial workbook fixtures still run.

## 3. Test results (paste the dotnet test summary lines for every project)

Final Release run: **670 passed, 0 failed, 0 skipped**, across all six projects in the solution (the plan's reference to five suites is outdated). Actual summary lines:

```text
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 147 ms - Etp.Reporting.Domain.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    61, Skipped:     0, Total:    61, Duration: 1 s - Etp.Reporting.Reporting.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   188, Skipped:     0, Total:   188, Duration: 7 s - Etp.Reporting.SqlServer.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   273, Skipped:     0, Total:   273, Duration: 27 s - Etp.Reporting.Desktop.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:   110, Skipped:     0, Total:   110, Duration: 32 s - Etp.Reporting.Import.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    26, Skipped:     0, Total:    26, Duration: 3 m 23 s - Etp.Reporting.SqlServer.IntegrationTests.dll (net10.0)
```

Final Debug build:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:16.04
```

Final Release build:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:16.95
```

Fresh consolidated import: **31 report files imported/empty export**, the separate control workbook **Not needed**, **22,006 new source rows**, **0 unknown layouts**, **0 failures**, **790 sales lines**, **759 invoices**, **3,955 stock movements**, **466 R010 stock snapshots**, **0 CONFLICT outcomes**. Invoice `100000068` exists on 2025-01-01 / FY 2025, 2025-06-21 / FY 2026, and 2026-05-26 / FY 2027. All 25 CSV months match. Repeating all 31 reports produces `Duplicate` and zero new rows.

| Raw export check | WLMHW | HEMW |
|---|---:|---:|
| 25 Aug NETAMOUNT | 34,215.00 | 29,290.00 |
| 25 Aug NETVALUE | 28,995.76 | 24,822.02 |
| 1–25 Aug NETAMOUNT | 938,197.00 | 774,868.60 |
| 1–25 Aug invoices | 182 | 38 |
| 1–25 Aug UPI | 600,305.00 | 415,616.00 |
| 1–25 Aug Cash, including source rounding/refunds | 222,599.00 | 255,709.60 |
| 1–25 Aug Card | 115,293.00 | 101,990.00 |
| 1–25 Aug TC | 0.00 | 1,553.00 |
| 1–25 Aug CN | 0.00 | 0.00 |

UPI is the largest mode in both stores. Mode totals equal GST-inclusive invoices to the paisa on every sales day; no tender is quarantined. Consolidated HEMW August is **1,015,259.10**, and its daily tender sums also reconcile exactly. The raw export's HEMW MTD is **774,868.60**; the plan's **774,869** is that amount rounded to whole rupees.

Migration coverage starts from the actual committed Phase 0 scripts, upgrades existing calendar-year invoices on locked days, verifies all six write guards are restored, and verifies the old migration journal/checksums are unchanged. Separate tests cover partial manual totals and explicit zero, monthly target uniqueness/overlap, customer-only corrected imports, staff placeholder repair, multiple-file supersession, scoped identical-byte empty files, evidence links, and full rollback on changed overlap.

Local command transcripts (outside the repository): `C:\Codex\Reporting Manger\phase1-verified-tests.txt`, `phase1-build-debug.txt`, `phase1-build-release.txt`, `phase1-verified-consolidated.txt`, `phase1-verified-repeat.txt`, `phase1-verified-subset.txt`, and `phase1-verified-raw-both.txt`. The commands and assertions above are the reproducible evidence; private workbooks and CSV were not copied into the branch.

## 4. Screenshots (paths under docs/audit/claude-audit-2026-09/phase-1-screenshots/)

Twenty PNGs show actual rendered WPF controls. Each path below is relative to this report's directory.

| Screen | 1366×768 | 816×480 |
|---|---|---|
| Folder import in progress | [1366×768](phase-1-screenshots/folder-progress-1366x768.png) | [816×480](phase-1-screenshots/folder-progress-816x480.png) |
| Per-file results | [1366×768](phase-1-screenshots/folder-results-1366x768.png) | [816×480](phase-1-screenshots/folder-results-816x480.png) |
| Results after scrolling | [1366×768](phase-1-screenshots/folder-results-scrolled-1366x768.png) | [816×480](phase-1-screenshots/folder-results-scrolled-816x480.png) |
| Source and restatement options | [1366×768](phase-1-screenshots/folder-restatement-options-1366x768.png) | [816×480](phase-1-screenshots/folder-restatement-options-816x480.png) |
| Deliberately corrupted column diagnostics | [1366×768](phase-1-screenshots/folder-diagnostics-1366x768.png) | [816×480](phase-1-screenshots/folder-diagnostics-816x480.png) |
| Tender mode settings | [1366×768](phase-1-screenshots/settings-masters-1366x768.png) | [816×480](phase-1-screenshots/settings-masters-816x480.png) |
| Staff settings with artificial sample records | [1366×768](phase-1-screenshots/settings-staff-1366x768.png) | [816×480](phase-1-screenshots/settings-staff-816x480.png) |
| Integration settings after helper removal | [1366×768](phase-1-screenshots/settings-integrations-1366x768.png) | [816×480](phase-1-screenshots/settings-integrations-816x480.png) |
| Source Inbox attachment and viewer actions | [1366×768](phase-1-screenshots/scanned-documents-1366x768.png) | [816×480](phase-1-screenshots/scanned-documents-816x480.png) |
| Source Inbox after scrolling | [1366×768](phase-1-screenshots/scanned-documents-scrolled-1366x768.png) | [816×480](phase-1-screenshots/scanned-documents-scrolled-816x480.png) |

The live review used the disposable `EtpPhase1Test_UiReview` database and an explicit temporary connection. Folder results show R022 **759 Rows / 759 New** and the 31-report successful import. Corruption was created only in an external temporary copy by renaming `QTY` to `BROKEN_QUANTITY`; diagnostics identify both the missing and unexpected columns and the closest R025 family. Original source workbooks were unchanged.

**Staff Settings screenshots use four artificial sample staff records in the disposable review database.** Other corpus screenshots show actual imports. The two source-name screenshot versions were removed from the unpublished Phase 1 branch history before the sample captures were added; no force-push was used.

**Capture limitation:** native Windows capture failed with `SetIsBorderRequired`, HRESULT `0x80004002`. The images were captured from the live window using WPF `RenderTargetBitmap`, at 1366×768 and 816×480, with scrolling exercised for results/diagnostics. These are actual application surfaces, but do **not** prove the Windows frame or native maximized-window requirement. Claude still needs to run that native screenshot check. No mockups or painted UI were substituted.

## 5. Decisions taken (anything the plan did not specify)

- Reuse the existing `source_gross_amount` and `source_net_amount` columns and add `source_tax_amount`; gross = NETAMOUNT, net = NETVALUE. Unknown legacy gross remains unknown until the original workbook is re-imported. `data_truth_version` allows those retained Phase 0 bytes to be processed once under the corrected importer.
- Scope exact-byte duplicates by report, store, start/end dates and data-truth version. Identical empty workbook bytes can legitimately belong to several contexts. One retained source document can link to multiple scoped imports.
- Treat a reordered file or subset as `Duplicate content`. Retain a typed non-current source copy and original evidence for a distinct hash, without adding canonical sales/tender/stock facts. A true superset replaces all covered current files in one transaction and records each replacement. Different content in an overlapping period requires Restate; a Phase 0 file without a manifest must first be reconstructed from its original bytes or explicitly restated.
- Compare full source content for R025/R003/R013, so customer/staff/detail corrections cannot silently disappear as duplicates. Financial line identity remains independent of physical worksheet row numbers. Decimal content comparison uses the persisted four-decimal scale; numeric state codes normalize equivalent `6`/`06` for identity while retaining their source values.
- For R030 canonical duplicate comparison, use store/document/date/item/type/location/quantity/balance fields. One historical reference-document correction in the older raw export does not create another stock movement; its full reference data is retained in the typed source copy.
- Add generic `SOR_AGEING` support because it occurs in both raw folders outside the numbered R001–R031 set. Existing internal `STOCK_LEDGER` and `CLOSING_STOCK` report names remain for compatibility; they map to R030/R011 tables.
- Detect row-based periods from the minimum and maximum actual business dates. Raw HEMW sales begin on **2 July**, although the export selection starts 1 July; raw stock includes 1 July. Header-only/snapshot files use explicit Info/export filename context, with sibling scope only as a fallback. R010's actual export snapshot is **7 September**, not the parent folder's label.
- Accept the explicitly identified, empty R011 consolidated export; an arbitrary blank workbook remains unsupported. Ignore `Info` as data, use it as metadata, and report the consolidation control workbook as `Not needed`.
- The actual raw folders contain **29 Titan + 30 Helios = 59** workbook files, rather than the prompt's estimated 30 each. All 59 import successfully into the empty raw acceptance database.
- On importing the older raw Helios folder into consolidated history, R025/R022/R013/R003 are subsets with no new sales. **R008, R009 and R014 correctly require Restate**: the later exports record 25-Aug cash banking of 27,995 on 27 Aug and a late 46,800 sale. R014's 25-Aug PhonePe changes from 1,295 to 48,095 and total revenue from 29,290 to 76,090. Suppressing those differences would violate task 9. Historical closing snapshots and the extra raw-only family may still add source/stock rows; the zero-new-sales assertion passes.
- Repair a legacy staff-code-as-name placeholder from a real R013 name, while preserving an owner-edited name and inactive state. Names are available even when a staff target overlaps a period with no sales.
- Source row counts in the UI count each physical worksheet row once; R022's multiple tender/control outcomes are grouped with conflict priority. A document-copy warning can be repaired by retrying an exact or content duplicate.
- Task 13 conflicts with D10/Phase 2 task 2 and the explicit instruction not to build Phase 2 brand rows. The per-brand manual stock form and yesterday-prefill are deferred and called out in section 8. The Phase 1 R010 source snapshot is implemented; no Phase 2 report layout or targets UI was added.
- Keep existing register statuses for Claude to change during the phase audit, as the register explicitly requires. Section 7 provides the requested implementation self-assessment. No new unresolved importer defect was found in the required corpus runs.

## 6. Tender mode mapping table for Sagar — D11

D11 fixes **PAYMENTTYPE25/AIRPAY → UPI** and **PAYMENTTYPE20/PHONEPE → UPI**. Other rows below are proposals for Sagar to confirm. All-zero columns do not establish an agency association; seeded mappings for those columns are provisional. Settings permits editing the reporting mode/active flag.

| Literal source PAYMENTTYPE column | Observed R020 AGENCYNAME | Proposed mode | Evidence / confirmation |
|---|---|---|---|
| PAYMENTTYPE15 | None established; all zero | TC | Confirm |
| PAYMENTTYPE19 | None established; all zero | TC | Confirm |
| PAYMENTTYPE20 | PHONEPE | UPI | Frozen D11; nonzero in both stores |
| PAYMENTTYPE21 | None established; all zero | UPI | Provisional; confirm |
| PAYMENTTYPE22 | None established; all zero | TC | Confirm |
| PAYMENTTYPE23 | None established; all zero | UPI | Provisional; confirm |
| PAYMENTTYPE24 | None established; all zero | TC | Confirm |
| PAYMENTTYPE25 | AIRPAY | UPI | Frozen D11; nonzero in both stores |
| PAYMENTTYPE26 | R014 header only; no R020 association, all zero | TC | Proposal only; no nonzero use or canonical tender mapping demonstrated |

Literal PAYMENTTYPE16, PAYMENTTYPE17 and PAYMENTTYPE18 are absent. The intervening R020 columns are named **GYFTR, PAYTM, HELIOSOMNI**; they must not be relabelled as PAYMENTTYPE16–18.

Every nonblank agency value observed in either store's R020 data:

| Literal AGENCYNAME | Observed source column | Stores/source sets | Proposed mode |
|---|---|---|---|
| AIRPAY | PAYMENTTYPE25 | Both raw stores and consolidated HEMW | UPI (D11) |
| PHONEPE | PAYMENTTYPE20 | Both raw stores and consolidated HEMW | UPI (D11) |
| CC01 | CARDAMOUNT | Both stores | Card |
| CC02 | CARDAMOUNT | Both stores | Card |
| CC05 | CARDAMOUNT | Consolidated HEMW | Card |
| CC08 | CARDAMOUNT | Consolidated HEMW | Card |
| CC10 | CARDAMOUNT | Both stores | Card |
| GIFT CARD | GCAMOUNT | Consolidated HEMW | TC |
| HEMW | CREDITNOTE | HEMW | CN |
| WLMHW | CREDITNOTE | WLMHW | CN |
| PAYTM | PAYTM | Consolidated HEMW | UPI |
| INSTCASHBK | None established: all 22 tender columns are zero on its two rows | Consolidated HEMW | Unmapped; TC proposed only after confirmation of source field |
| Blank / empty string | CASHAMOUNT, ROUND OFF, REFUND | Both stores | Cash |
| Blank / empty string | CHEQUEAMOUNT | Both stores | TC |

`GIFT CARD` contains a space in the source; `GIFTCARD` is the internal tender code. INSTCASHBK's invoice amounts are nonzero; only its tender columns are zero, so no tender field can be inferred from those rows.

Remaining named source columns **GVAMOUNT, LOYALTYPOINTS, TATA GV, GYFTR, HELIOSOMNI** have no nonzero agency evidence in the inspected R020 sets; proposed mode **TC**. **NO REFUND** is all zero, proposed **Cash**. R022's source aliases (including credit-note issuance/refunds and explicit round-off) remain signed and participate in reconciliation. **Service Cash, Service Card, Service UPI** are manual service inputs; no ETP agency column establishes them.

R020 evidence counts: WLMHW raw **480 rows**, HEMW raw **110**, consolidated HEMW **942**. Nonzero PAYMENTTYPE20 rows respectively **1 / 51 / 381**; PAYMENTTYPE25 **263 / 1 / 24**. Blank agency cells respectively **176 / 43 / 372**. Raw and consolidated HEMW overlap; these counts must not be added as independent transactions.

**Sagar confirmation pending:** all proposed mappings other than the two frozen D11 decisions, especially all-zero columns and INSTCASHBK. The phase is not declared closed by this report.

## 7. Register: IF-001 to IF-013, with FIXED (test) or DEFERRED (reason)

The source register retains its existing OPEN labels because it assigns status changes to Claude. This table is Codex's implementation assessment, supported by the passing tests. No new OPEN row was appended: the three older changed-overlap files are the intended task-9 rejection, and the required fresh corpus import has no new failure.

| ID | Self-assessment | Passing behavioral evidence |
|---|---|---|
| IF-001 | FIXED | `Whole_private_corpus_matches_all_profiles_with_Info_and_both_raw_date_layouts`; whole-folder SQL test |
| IF-002 | FIXED | `Date_styled_numeric_etp_serial_and_text_dates_have_the_same_business_date`; `Numeric_etp_date_conversion_uses_yyyyMMdd` |
| IF-003 | FIXED | `Cro_returns_and_cancellations_are_negative_and_keep_staff_name`; `Retail_policy_uses_gross_and_treats_bill_cancellation_as_a_return`; gross/cancellation reporting SQL test |
| IF-004 | FIXED | `Financial_year_identity_content_dedupe_superset_and_conflicts_are_atomic`; `A_locked_day_inside_the_range_prevents_the_whole_file`; scoped evidence-link SQL test |
| IF-005 | FIXED | `Both_raw_store_folders_import_in_one_action_with_golden_sales_tenders_staff_and_headers`; whole-folder SQL test, both without entered scope |
| IF-006 | FIXED | `Financial_year_identity_content_dedupe_superset_and_conflicts_are_atomic`; `A_superset_replaces_two_separate_period_files_in_one_transaction`; raw subset assertion |
| IF-007 | FIXED | `Explicit_empty_snapshot_metadata_succeeds_but_an_arbitrary_blank_workbook_does_not`; `Every_family_has_a_sanitised_parser_golden`; whole-folder SQL test asserts 466 dated R010 snapshots |
| IF-008 | FIXED | `Reports_use_gst_inclusive_sales_negative_cancellation_cro_names_and_editable_tender_mapping`; raw-store SQL test asserts largest UPI, daily exact reconciliation and zero ineligible tenders |
| IF-009 | FIXED | `Cro_returns_and_cancellations_are_negative_and_keep_staff_name`; `R013_import_replaces_legacy_code_names_and_preserves_manually_edited_names`; raw-store staff totals |
| IF-010 | FIXED | `Every_family_has_a_sanitised_parser_golden`; `Corrupted_layout_reports_closest_family_and_does_not_stop_other_files`; whole-folder SQL test |
| IF-011 | FIXED | `Shared_revenue_headers_are_disambiguated_by_file_or_sheet`; `Identical_empty_workbook_bytes_preserve_each_report_store_and_date_and_reimport_as_duplicates` |
| IF-012 | FIXED | `Whole_Helios_folder_matches_monthly_golden_reimports_and_raw_subset_without_new_sales` asserts 790/759, all three fiscal years, monthly CSV and zero conflicts; migration upgrade test |
| IF-013 | FIXED | `Every_approved_stock_type_stages_and_preserves_signed_movements_including_cancellation_receipts` (all ten in CI); `Unknown_ledger_transaction_type_warns_and_skips_the_row`; whole-folder SQL test asserts 3,955 movements |

## 8. Known gaps

- **Task 13 deferred:** per-brand Display/Backstock/Defective/Y Loc entry with yesterday's numbers prefilled is not implemented. The current manual form still uses an inventory-group field and clears after saving; its existing stock report prefers cluster grouping. D10 and Phase 2 also assign this work to the next phase, and the user explicitly excluded Phase 2 brand rows. This is a phase-scope conflict, not a completed task. No report layout or targets UI was added.
- **D11 confirmation pending:** the source establishes AIRPAY and PHONEPE; zero-value columns and other proposed modes still require Sagar's review before phase closure.
- **Native maximized screenshot check not run successfully:** live WPF captures exist at both required dimensions, but native Windows capture failed. Claude must verify the maximized Windows view independently.
- **A1.8a is undefined:** the supplied plan includes A1.9a but contains no text for A1.8a. No acceptance requirement has been invented for that identifier.
- **Existing data upgrade needs source re-import where values were never retained:** migration cannot reconstruct missing gross amounts or the historical rows already dropped by Phase 0. Re-import the original workbooks using the new importer; conflicting legacy content fails visibly and requires a reviewed replacement. The live shop database has not been migrated by this task.
- **Older-source differences:** R008/R009/R014 intentionally refuse to replace later consolidated values silently. They require a deliberate restatement if the owner intends that replacement; the sales subset is already present.
- Independent Claude audit and owner sign-off remain pending. This report does not start Phase 2 or claim phase closure.

## 9. Acceptance checklist A1.1–A1.11 with PASS / FAIL / NOT RUN and evidence

| ID | Plan criterion | Self-assessment and evidence |
|---|---|---|
| A1.1 | Import both real folders on an empty database in one action with per-file results/counts. | **PASS** — 59 actual raw files (29 WLMHW + 30 HEMW), all Imported/empty export, one `RunFilesAsync` action; raw-store SQL test and audit command. |
| A1.2 | Titan and Helios 25-Aug NETAMOUNT/NETVALUE and August MTD/invoice counts match source. | **PASS** — exact values in section 3; raw-store SQL test. HEMW 774,868.60 rounds to the plan's 774,869. |
| A1.3 | Re-importing the same folder writes zero new rows. | **PASS** — all 31 report files Duplicate, zero new source rows; counts stay 790/759/3,955. Control workbook stays Not needed. |
| A1.4 | CRO, tender mode, banking, CN and customer header tables have matching 25-Aug counts. | **PASS** — raw-store SQL test compares R013/R020/R008/R009/R029/R012/R024 dated typed-table counts with parsed workbook rows, and checks staff totals/names. |
| A1.5 | Every profile has CI parser goldens without SQL; SQL integration tests run locally. | **PASS** — 32 artificial one-row workbooks; 110 parser tests, including every approved stock type; private corpus tests also ran; 26 SQL integration tests passed on SQLEXPRESS. |
| A1.6 | Titan August UPI largest; modes equal gross invoice totals daily; no quarantine. | **PASS** — UPI 600,305.00 is largest; every sales day reconciles exactly, zero ineligible tender rows. Also checked HEMW raw and consolidated August. |
| A1.7 | Moving three Titan SDB rows to the top adds zero facts and reports Duplicate content. | **PASS** — real Titan reorder assertion in raw-store SQL test; content identity is independent of worksheet row number. |
| A1.8 | July–August multi-day import needs no entered date and every day inside the range has sources present. | **PASS** — detected min/max periods; `Financial_year_identity_content_dedupe_superset_and_conflicts_are_atomic` checks an interior date's source presence; whole-file locked-interior-day rejection and scoped empty-file source presence are also tested. |
| A1.8a | No criterion exists for this identifier in the supplied plan. | **NOT RUN — undefined.** Range/evidence/lock behavior is covered under A1.8; this does not invent a substitute criterion. |
| A1.9 | 25-Aug staff report shows negative returns, CRO names and totals equal the store. | **PASS** — R013 SR/BC staging theory, reporting SQL test, raw-store exact staff/store equality and zero variance. |
| A1.9a | Two-year Helios R025 has 790 lines/759 invoices, three FY identities, CSV monthly totals and no conflicts; R030 imports 3,955 rows. | **PASS** — whole-folder SQL test verifies every assertion, including all 25 CSV months, qty/tax/SR fields; fresh audit database reproduces the counts. |
| A1.10 | All Phase 1 register items fixed; full corpus/Titan import produces no new OPEN defect and monthly totals match. | **PASS for implementation evidence** — IF-001–IF-013 all supported in section 7; fresh full-corpus/Titan tests pass; no new unresolved defect appended. Formal register status changes remain Claude's audit responsibility. |
| A1.11 | All 31 consolidated Helios files, Info sheets present, import in one action with no manual scope, unknown layouts or blocked files. | **PASS** — 31 Imported/empty export, zero Unknown layout, zero blocked files; original control workbook Not needed. Actual live folder-result screenshot and fresh SQL corpus test agree. |

These are Codex's self-assessments. Phase closure still depends on D11, resolution of the task-13 phase conflict, the native screenshot check, and Claude's independent audit.
