# UI/UX v4 implementation map

This map is the presentation-layer migration ledger. The repository remains the functional source of truth; existing SQL, import, reporting, control, accounting, archive, permission and audit services are reused unchanged.

| Current screen / action | Current class / handler / service | New module | Sidebar location | New view / control | Role | Status |
|---|---|---|---|---|---|---|
| Application startup | `MainWindow_Loaded`, `Phase2OperationsRepository.LoadCurrentAccessAsync` | Global | Welcome | Windows-identity welcome overlay | All | Implemented |
| Windows user access | `ApplicationAccess`, `RefreshAccessAsync` | Global | Profile | Welcome/profile drawer | Existing role | Implemented; no password store added |
| Product launcher | Former fixed left navigation | Modules Home | Global rail → Home | Registry-driven `ModuleTile` wrap layout | Role filtered | Implemented; supports 3/6/9 |
| Operational dashboard | `RefreshDashboardAsync` | Dashboard | Overview | Existing live dashboard inside v4 shell | Viewer+ | Implemented |
| Daily business workflow | `RefreshDailyWorkflowAsync` | Dashboard | Business Day → Readiness / Finalisation | Existing governed daily workflow | Store Manager+ | Reachable |
| Manual non-ETP values, including walk-ins | `DailyReportingWorkflowRepository`, `SaveManualInput_Click` | Dashboard | Business Day → Manual Entry | Database-driven governed Manual Entry workspace | Store Manager+ | Implemented; future approved fields appear from definitions |
| Manual operational inputs | `SaveManualInput_Click` | Dashboard | Business Day → Readiness | Existing input controls | Store Manager+ | Reachable |
| Physical stock inputs | `SaveStockCount_Click` | Dashboard | Business Day → Readiness | Existing independent physical/system workflow | Store Manager+ | Reachable |
| Staff targets | `SaveStaffTarget_Click` | Dashboard / Reports | Performance → Target Progress | Existing target service | Store Manager+ | Reachable |
| Generate/finalise daily pack | daily workflow handlers and pack service | Reports | Report Packs → Store / Combined Pack | Existing generation actions | Store Manager+ | Reachable |
| Import one workbook | `Validate_Click`, `Persist_Click` | Imports | Intake → Import Files | Existing import workspace | Store Manager+ | Reachable |
| Folder/ZIP batch import | `StartBatchImport_Click`, `BatchImportCoordinator` | Imports | Intake → Bulk Historical Import | Existing progress/cancel/retry controls | Store Manager+ | Reachable |
| Import overlap/duplicate/conflict handling | import orchestrators and preflight | Imports | Quality | Existing diagnostics and batch summaries | Store Manager+ | Reachable |
| Source Inbox | `RefreshSourceInboxAsync`, `ProductisationRepository` | Imports | Intake → Source Inbox | Existing immutable source grid | Viewer+ | Reachable |
| Source document intake | `IntakeSourceDocument_Click` | Imports | Documents & OCR → Document Repository | Existing document intake | Store Manager+ | Reachable |
| Native PDF extraction | `ProductisationOperationsService.IntakeDocumentAsync` | Imports | Documents & OCR → Native PDF Extraction | Existing extraction boundary | Store Manager+ | Reachable |
| OCR review | `ReviewExtractionAsync` | Imports | Documents & OCR → OCR Review Queue | Existing human verification controls | Store Manager+ | Reachable |
| Inward register | `RefreshRegistersAsync`, `SaveRegisterEntry_Click` | Imports / optional Registers | Digital Registers → Inward | Existing audited register workspace | Store Manager+ | Reachable |
| Outward register | same register service | Imports / Registers | Digital Registers → Outward | Shared register workspace | Store Manager+ | Reachable |
| Credit Note register | same register service | Imports / Registers | Digital Registers → Credit Note | Shared register workspace | Store Manager+ | Reachable |
| Service Receipt register | same register service | Imports / Registers | Digital Registers → Service Receipt | Shared register workspace | Store Manager+ | Reachable |
| Stock Transfer register | same register service | Imports / Registers | Digital Registers → Stock Transfer | Shared register workspace | Store Manager+ | Reachable |
| Expense register | same register service | Imports / Registers | Digital Registers → Expense | Shared register workspace | Store Manager+ | Reachable |
| Vendor Invoice register | same register service | Imports / Registers | Digital Registers → Vendor Invoice | Shared register workspace | Store Manager+ | Reachable |
| Courier register | no authoritative schema | Imports / Registers | Digital Registers → Courier | Locked navigation state | — | Deliberately unavailable with reason |
| Reports overview | `ProductReportCatalogue` | Reports | Overview | Registry-driven catalogue navigation | Viewer+ | Implemented |
| All 29 production reports | `RunCatalogueReport_Click`, `ProductReportCatalogue` | Reports | Category generated from catalogue | Grouped focused workspaces with fixed actions and internal preview scrolling | Viewer+ | Implemented and completeness-tested |
| Daily Sales / DSR | `LoadDsrAsync`, DSR WPF/PDF implementation | Reports | Sales → Daily Sales / DSR | Dedicated DSR workspace with business date, availability, preview and exports | Viewer+ | Implemented |
| Titan / Helios / Combined summaries | report catalogue handlers | Reports | Sales | Shared report workspace | Viewer+ | Reachable |
| Invoice/returns/brand/segment/item reports | report catalogue handlers | Reports | Sales | Shared report workspace | Viewer+ | Reachable |
| Closing/physical/movement/variance/group/brand/slow stock | report catalogue handlers | Reports | Stock | Shared report workspace | Viewer+ | Reachable |
| Staff/CRO performance and target views | `LoadStaffPerformanceAsync` | Reports | Staff | Shared report workspace | Viewer+ | Reachable |
| Tender reconciliation/diagnostics/cash | reporting services | Reports | Tender / Cash | Shared report workspace | Viewer+ | Reachable |
| Service sales | `LoadServiceSalesAsync` | Reports | Service | Shared report workspace | Viewer+ | Reachable |
| Daily/missing/unmapped/stock/staff/tender exceptions | report catalogue handlers | Reports / Exceptions | Exceptions | Shared report workspace | Viewer+ | Reachable |
| Management trend | `LoadManagementTrendAsync` | Dashboard / Reports | Performance / Management | Existing governed visual report | Viewer+ | Reachable |
| Report search/sort/variance | `ApplyReportFilter`, DataGrid | Reports | Active report | Shared filter and virtualised grid | Viewer+ | Preserved |
| Report PDF/Excel export | existing exporters | Reports | Active report | Consistent action row | Viewer+ | Preserved |
| Row drill-down | `ReportGrid_MouseDoubleClick` | Reports | Active report | Right-side detail drawer | Viewer+ | Implemented; report context retained |
| Source lineage | invoice lineage report and source fields | Reports | Investigation | Detail drawer / lineage report | Viewer+ | Preserved |
| Accounting preparation | accounting handlers and services | Accounting | Prepare Batch | Existing balanced-batch workspace | Viewer+/authority per action | Reachable |
| Ledger mapping | `ApproveAccountingMapping_Click` | Accounting | Ledger Mapping | Existing owner-controlled mapping | Owner | Reachable |
| Validation and Tally XML export | accounting services | Accounting | Validation / Tally Export | Existing one-way export | Authorised role | Reachable |
| Accounting history | `AccountingBatchGrid` | Accounting | Export History | Existing history grid | Viewer+ | Reachable |
| Immutable generations | `RefreshReportArchiveAsync` | Archive | Report Generations | Existing archive workspace | Viewer+ | Reachable |
| Restatement comparison | `CompareArchivedGenerations_Click` | Archive | Compare Generations | Existing comparison grid | Viewer+ | Reachable |
| Re-export / ZIP / email / WhatsApp initiation | archive handlers | Archive | Re-export / Shared Reports | Existing generation-bound actions | Viewer+/action authority | Preserved |
| Data-quality centre | `RefreshOperationsAsync` | Exceptions | Data Quality | Existing findings grid | Viewer+ | Reachable |
| Adjustment request | `SubmitAdjustment_Click` | Exceptions | Approval Centre | Existing governed request | Store Manager+ | Reachable |
| Approval decisions | `DecideApprovalAsync` | Exceptions / optional Approvals | Approval Centre | Existing owner decision controls | Owner | Reachable and role filtered |
| Global investigation | `RunGlobalSearch_Click` | Global | Rail → Search | Existing cross-domain search with Ctrl+F route | Viewer+ | Implemented |
| Watch folders | `SaveAutomationSettings_Click` | Settings / Imports | Watch Folder | Existing settings and operations | Owner | Reachable |
| Scheduler | schedule handlers | Settings | Scheduler | Existing scheduled operations | Owner | Reachable |
| Backup | `RunBackupNow_Click` | Settings | Backup & Recovery | Existing backup operation | Owner | Reachable |
| Recovery drill | `RunRecoveryDrillNow_Click` | Settings | Backup & Recovery | Existing isolated recovery drill | Owner | Reachable |
| Support package | `CreateSupportPackage_Click` | Settings | System Health | Existing privacy-safe package | Owner | Reachable |
| User/role administration | `SaveUserAccess_Click` | Settings | Users & Roles | Existing Windows identity administration | Owner | Reachable |
| Controlled masters and KPI catalogue | master administration handlers | Settings | Master Data / KPI Catalogue | Existing audited controls | Owner | Reachable |
| SQL and integration settings | settings handlers | Settings | General / System Health | Existing configuration | Owner or setup state | Reachable |
| Help Centre and shortcut guide | `HelpCentreRegistry`, `HelpCentreView` | Global | Sidebar → Help Centre / F1 / Ctrl+/ | Searchable tile workspace and contextual topics | All | Implemented |
| Density preference | `UiPreferenceStore` | Global | Contextual sidebar footer | Comfortable/Compact selector using shared resource values | All | Implemented and persisted |
| Category sales / sell-through / stock turn / days cover | no authoritative source contract | Reports | Data-Dependent Future | Locked navigation entries | — | Not fabricated |

## Preservation statement

The v4 shell calls the existing handlers and repositories. It does not replace canonical `NETVALUE`, signed returns, business-date policies, duplicate/restatement logic, finalisation, lineage, tender/stock controls, accounting rules, permissions or audit history.
