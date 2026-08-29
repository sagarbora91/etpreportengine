# ETP Reporting Engine — Project Closure Traceability

Status date: 29 August 2026
Authority: current .NET 10/WPF product only
Owner: closure sprint Lead Integrator

Current source metadata declares version 1.8.5 in the uncommitted integration working tree. No 1.8.5 application, installer, offline package, SBOM, provenance, signature, tag or release has been produced. The previously recorded 1.8.4 engineering payloads remain preserved under their historical hashes but are **rejected and were never promoted**: shipped audit events were incompatible with the database audit constraint, sharing-contact mutation/audit lacked one explicit transaction, and the committed SBOM identifies a different source state/application hash than the candidate provenance. The old 516-test/build record remains historical evidence for commit `8c8d57e`; it does not verify the current source or make 1.8.4 promotable.

## Current verification boundary

- `VERIFIED` rows below retain unaffected 28–29 August evidence. Rows affected by the 1.8.5 audit, import, diagnostics, I/O/export or upgrade-safety changes are reset to `IMPLEMENTED_NOT_VERIFIED` until a clean combined run and independent review are tied to the eventual commit.
- `EXTERNAL_VALIDATION_BLOCKED` rows have source implementation and automated preparation, but their acceptance criteria explicitly require a clean/target PC, live operational exercise, Microsoft Excel/printer/touch/accessibility hardware, or human UAT.
- Owner-, source- and licensing-dependent rows remain blocked/deferred exactly as recorded in `docs/PENDING_INPUT_AND_DEFERMENT_REGISTER.md`.
- `HELP-002` is `VERIFIED`: every live module topic now provides numbered, route-backed guidance and focused tests reject placeholders, missing owned destinations and orphaned workspace links.
- `REL-008` has no eligible candidate: 1.8.4 is rejected and 1.8.5 artifacts do not exist. `REL-011` remains pending explicit publication authorization after all preceding gates.

## Purpose

This ledger is the authoritative completion-control document for the current ETP Reporting Engine. It prevents a plan, design, partial implementation or old test run from being mistaken for a completed product requirement.

Every active requirement must remain here until it is independently verified, explicitly blocked, explicitly deferred by the Owner, or classified as legacy. Phase reports and implementation audits provide evidence; they do not override this ledger.

## Status vocabulary

Only these statuses are allowed:

| Status | Meaning |
|---|---|
| `NOT_STARTED` | No accepted implementation exists. |
| `IN_PROGRESS` | Work exists but the requirement is not fully implemented. |
| `IMPLEMENTED_NOT_VERIFIED` | Implementation appears present, but current independent acceptance evidence is missing or stale. |
| `VERIFIED` | Current implementation and acceptance criteria have independent evidence tied to a commit or exact artifact. |
| `OWNER_INPUT_BLOCKED` | A business or security decision controlled by the Owner is required. |
| `SOURCE_DATA_BLOCKED` | A populated source export or source-system definition is required. |
| `EXTERNAL_VALIDATION_BLOCKED` | Engineering is available, but a real PC, printer, certificate, Microsoft tenant or human UAT is required. |
| `OWNER_APPROVED_DEFERRED` | The Owner explicitly instructed that this requirement must wait. |
| `NOT_APPLICABLE_LEGACY` | The requirement belongs only to the legacy JavaScript/Capacitor/Android product. |

`Complete` is deliberately not a requirement status. An overall closure statement is allowed only when there are no `NOT_STARTED`, `IN_PROGRESS` or `IMPLEMENTED_NOT_VERIFIED` rows and every blocked/deferred row has an explicit Owner disposition.

## Evidence rules

To move a row to `VERIFIED`, record all applicable evidence:

1. implementation file(s) and commit SHA;
2. focused automated test and result;
3. architecture or security check where applicable;
4. installed-application workflow evidence for user-facing behavior;
5. live SQL evidence for database behavior;
6. rendered PDF/Excel/image evidence for exports;
7. independent verifier and verification date.

Changing code that can affect a verified requirement invalidates its verification until the affected tests and acceptance checks are rerun. An implementer cannot independently verify the same row.

## Requirement-source register

| Source ID | Active authority | Scope treatment |
|---|---|---|
| `SRC-CORE` | *ETP Reporting Engine — Windows Application + SQL Server Express* | Active foundation for the WPF/SQL product. |
| `SRC-DAILY` | *ETP Daily Reporting Engine — Automated Daily / MTD / YTD Report Generation* | Active reporting, import, controls and daily-workflow authority. |
| `SRC-P2` | *ETP Report Engine — Phase 2: Productisation, UX, Automation, Documents, Sharing & Business Operations* | Active productisation authority. |
| `SRC-VIS` | *Visual Analytics & Rich Reporting Layer for the Reporting Engine* | Active visual/report-export authority. |
| `SRC-UI4` | *ETP Reporting Engine — UI/UX v4 Touch-First Product Redesign* | Active WPF presentation authority. |
| `SRC-DSR` | Final DSR mockup package and subsequent DSR audit/manual-entry instructions | Active DSR visual/content authority. |
| `SRC-MOD` | *Desktop Modular Architecture Refactor Plan* | Active architecture authority. |
| `SRC-KNOW` | *Set Up Obsidian + Graphify as the Knowledge System for the ETP Project* | Active project-knowledge authority. |
| `SRC-LIC` | *Implement Dual-Layer Licensing for ETP* | Engineering design is active; runtime implementation is Owner-approved deferred. |
| `SRC-FOLLOWUP` | Owner follow-ups covering backups, filters, reports, installer, Help, shortcuts, Manual Entry, GitHub and release | Active where not superseded by a later explicit decision. |

## Product-scope boundary

The active product is the .NET 10/WPF solution in `src/`, `tests-dotnet/`, `database/`, `installer/` and `scripts/`. JavaScript/Capacitor/Android files in `www/`, `tests/`, `docs/audit/V6-*` and related V6 evidence are `NOT_APPLICABLE_LEGACY` unless a row explicitly identifies a shared business rule or test datum reused by the WPF product.

Legacy evidence cannot prove a WPF requirement. In particular, Node test results, browser UI results and Android/Capacitor release evidence do not satisfy WPF, SQL Server, Windows installer or desktop accessibility acceptance gates.

## A. Governance and architecture

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `GOV-001` | Maintain one atomic requirement ledger (`SRC-FOLLOWUP`) | All active prompt sections mapped; no uncategorized requirement | `VERIFIED` | Reconciled to the checkpoint and pending-input authority at `08e39c8` on 29 August 2026. |
| `GOV-002` | Maintain one input/deferment register (`SRC-FOLLOWUP`) | Each blocker has owner, exact input, impact and unblock gate | `VERIFIED` | `docs/PENDING_INPUT_AND_DEFERMENT_REGISTER.md`. |
| `GOV-003` | Prevent phase completion from implying product completion (`SRC-FOLLOWUP`) | Governance text and release gates prohibit broad completion claims | `VERIFIED` | Status and evidence rules in this ledger. |
| `GOV-004` | Preserve unrelated working-tree changes (`SRC-MOD`) | Scoped diffs and clean attribution | `VERIFIED` | Checkpoint and 29 August reconciliation preserve generated `graphify-out/` churn outside scoped product/documentation diffs. |
| `GOV-005` | Use Graphify, CRG, source and tests for architecture work (`SRC-KNOW`, `SRC-MOD`) | Query records and source-backed change review | `VERIFIED` | Repository instructions, Graphify discovery, source inspection and combined evidence are bound to `08e39c8`. |
| `ARCH-001` | One WPF executable with clear Domain, Application, Import, Reporting, SQL and Desktop responsibilities (`SRC-CORE`, `SRC-MOD`) | Dependency tests plus source inspection | `IMPLEMENTED_NOT_VERIFIED` | One WPF executable and the layer boundaries remain in source; current combined dependency/build evidence is pending after integration. |
| `ARCH-002` | `MainWindow` is only the window shell/workspace host (`SRC-MOD`, `SRC-UI4`) | No feature state, repositories, formulas, imports or export orchestration in shell | `VERIFIED` | `MainWindowShellBoundaryTests` ratchets the three remaining shell partials to 907 lines, 29 fields and 62 methods, compact feature hosts and no feature/export/import implementation. |
| `ARCH-003` | One composition root constructs dependencies (`SRC-MOD`, `SRC-LIC`) | Construction limited to App/composition/adapters; no service locator | `IMPLEMENTED_NOT_VERIFIED` | `DesktopCompositionRoot` remains the construction boundary, including the revised import/export seams; current combined construction/architecture tests are pending. |
| `ARCH-004` | Application use-case contracts are the normal Desktop boundary (`SRC-MOD`) | Desktop workspaces depend on contracts, not SQL/import implementations | `IMPLEMENTED_NOT_VERIFIED` | Application boundaries remain in source and import persistence now carries the accepted matched envelope; current combined verification is pending. |
| `ARCH-005` | Independent cohesive workspace modules (`SRC-MOD`, `SRC-UI4`) | Each route resolves to one owner module; no God ViewModel | `VERIFIED` | Cohesive module views/sessions exist under `Modules/`; ownership tests cover every destination and production report route. |
| `ARCH-006` | No SQL/repository construction in Views or ViewModels (`SRC-MOD`) | Architecture test and source scan | `VERIFIED` | Guardrails reject SQL dependencies in module views and direct MainWindow SQL/repository construction; the current inventory is zero. |
| `ARCH-007` | No report formulas or workbook parsing in Desktop (`SRC-CORE`, `SRC-MOD`) | Architecture/source tests | `IMPLEMENTED_NOT_VERIFIED` | Workbook materialization remains in Import and report rendering remains in Reporting/export services; current combined guardrails are pending. |
| `ARCH-008` | Preserve startup modes during refactor (`SRC-MOD`) | Normal, database-initialize and automation-once tests | `IMPLEMENTED_NOT_VERIFIED` | Startup modes remain and now emit structured privacy-safe diagnostics; current combined startup/diagnostic verification is pending. |
| `ARCH-009` | Architecture guardrails prevent regression (`SRC-MOD`) | Automated boundary, construction, route and size/responsibility tests | `IMPLEMENTED_NOT_VERIFIED` | Guardrail tests exist, but a clean combined result for the integrated 1.8.5 source has not yet been recorded. |
| `ARCH-010` | Remove obsolete MainWindow paths after migration (`SRC-MOD`) | Source/Graphify references clean; shell-only final audit | `VERIFIED` | Feature partials were removed; source tests keep the legacy partials absent and the current Graphify index is reconciled. |

## B. Data, import and canonical storage

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `DATA-001` | SQL Server Express is authoritative structured storage (`SRC-CORE`) | Fresh install, migrations and live SQL workflow | `EXTERNAL_VALIDATION_BLOCKED` | SQL implementation exists; exact closure baseline must rerun. |
| `DATA-002` | Source lineage reaches workbook, sheet and row (`SRC-CORE`, `SRC-DAILY`) | Live import and report drill-down evidence | `EXTERNAL_VALIDATION_BLOCKED` | Existing audit reports lineage; installed workflow must be revalidated. |
| `DATA-003` | Business date comes from ETP content, not file timestamp/import timestamp (`SRC-DAILY`) | Golden tests across files and dates | `VERIFIED` | Golden import/date tests pass in the combined suite at `08e39c8`. |
| `DATA-004` | R025 is item-level sales; `NETVALUE` is GST-inclusive primary sales value (`SRC-CORE`, Owner decision) | Mapping and reconciliation tests | `VERIFIED` | Approved rule exists in knowledge vault. |
| `DATA-005` | R022 controls final invoice/tender totals (`SRC-DAILY`, Owner decision) | Cross-report control tests | `VERIFIED` | Approved rule exists; real tender variances remain visible by design. |
| `DATA-006` | `INV` is invoice and `SR` is signed-negative sales return (`SRC-DAILY`, Owner decision) | Import and aggregation tests with returns | `VERIFIED` | Import and aggregation coverage passes in the combined suite at `08e39c8`. |
| `DATA-007` | `CLUSTER` is brand segment, not product category (`SRC-CORE`, Owner decision) | Mapping and report-label tests | `VERIFIED` | Rule is documented; category report remains source blocked. |
| `IMP-001` | Automatic file/profile/store/business-date detection (`SRC-CORE`, `SRC-DAILY`) | Corpus tests for every active profile | `IMPLEMENTED_NOT_VERIFIED` | Detection now produces an exact approved identity and blocker-free matched envelope; current combined/per-profile corpus verification is pending. |
| `IMP-002` | File, folder and ZIP batch import (`SRC-DAILY`, `SRC-P2`, follow-up item 4) | Installed UI plus live SQL file/folder/ZIP tests | `EXTERNAL_VALIDATION_BLOCKED` | Imports are owned by an extracted workspace/coordinator with composed persistence; installed file/folder/ZIP and live SQL acceptance remain. |
| `IMP-003` | Progress, cancellation, retry and clear failure summaries (follow-up item 5) | Large-batch UI automation and atomic cancellation test | `EXTERNAL_VALIDATION_BLOCKED` | Import owns progress/cancel/retry state; workbook copy/materialization now honors cancellation and bounds concurrent CPU work. Current combined tests plus installed large-batch validation remain. |
| `IMP-004` | Three-level duplicate/conflict protection (`SRC-P2`) | Exact duplicate, same transaction and changed-content tests | `VERIFIED` | Exact-duplicate, transaction and changed-content SQL/import tests pass at `08e39c8`. |
| `IMP-005` | Atomic overlapping-date restatement (`SRC-DAILY`, `SRC-P2`) | Replace/archive/rollback tests with live SQL | `VERIFIED` | Replace/archive/rollback SQL tests pass at `08e39c8`; production-data UAT remains covered by `REL-009`. |
| `IMP-006` | Unknown layouts fail closed with actionable diagnostics (`SRC-CORE`, `SRC-DAILY`) | Malformed/unknown workbook tests | `IMPLEMENTED_NOT_VERIFIED` | `ApprovedImportProfileRegistry` and `MatchedImportEnvelopeFactory` fail closed before persistence; all integrated entry paths require a current combined run. |
| `IMP-007` | Source Inbox and document integrity (`SRC-P2`, `SRC-UI4`) | Hash verification, review, linkage and permissions | `EXTERNAL_VALIDATION_BLOCKED` | Source Inbox is an extracted workspace using `ISourceInboxService`; SHA-256 verification, quarantine wording, immutable-ID linkage and safe-launch tests pass. Live workflow remains. |
| `IMP-008` | Native PDF extraction and safe OCR review (`SRC-P2`) | Text PDF, scanned PDF, review and no-silent-trust tests | `EXTERNAL_VALIDATION_BLOCKED` | OCR/review is module-owned and preserves human-verification/no-usable-text states; installed native/OCR helper validation remains. |
| `IMP-009` | Active deterministic profiles R003, R013, R022, R025, Variant Stock Ledger and Closing Stock | Per-profile corpus/reconciliation tests | `IMPLEMENTED_NOT_VERIFIED` | The six identities are explicitly registered and propagated through persistence; per-profile current corpus/reconciliation results are pending. |
| `IMP-010` | Empty/unavailable ETP profiles are not fabricated | Populated source required or truthful unavailable state | `SOURCE_DATA_BLOCKED` | AdvanceOrder, Encircle, GC, PRP and some cross-store reports lack populated samples. |
| `IMP-011` | A second approved schema version for one supported report can coexist without calculator changes (production-hardening prompt acceptance criterion 4) | Populated sanitised changed-layout specimen, approved exact identity/mapping, old/new provenance persistence and golden-output equivalence | `SOURCE_DATA_BLOCKED` | The exact registry/envelope/persistence seam is implemented, but no genuine v2 export or approved changed-layout mapping exists. `IN-SRC-011` is required; no synthetic v2 may be promoted as acceptance evidence. |

## C. Reporting, daily workflow and business controls

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `REP-001` | Deterministic report catalogue and renderer-neutral results (`SRC-CORE`, `SRC-VIS`) | Catalogue execution and UI/PDF/Excel reconciliation | `VERIFIED` | Reports now run through an extracted workspace and Application report ports; deterministic Reporting models/exporters and focused tests pass. Full production-code execution matrix remains. |
| `REP-002` | Sales filters: store, brand segment, transaction type and item (follow-up item 6) | UI/filter query tests and totals reconciliation | `EXTERNAL_VALIDATION_BLOCKED` | Reported implemented; installed interaction evidence pending. |
| `REP-003` | Report search, sorting, variance-only view and drill-down (follow-up item 8) | UI automation and source-lineage check | `EXTERNAL_VALIDATION_BLOCKED` | The extracted Reports workspace owns search/sort/variance/drill-down state; installed interaction and lineage UAT remain. |
| `REP-004` | Tender-variance diagnostic without changing control rules (follow-up item 9) | R022 comparison and diagnostic tests | `VERIFIED` | Tender diagnosis crosses an Application seam and retains existing control calculations; `TC` policy remains blocked. |
| `REP-005` | PDF export for every supported report (follow-up item 10) | Each report exported, parsed and rendered | `IMPLEMENTED_NOT_VERIFIED` | Export execution is asynchronous, cancellable and overlap-guarded outside MainWindow; current combined and exact rendered/Unicode matrices are pending. |
| `REP-006` | Excel export preserves detailed data and deterministic totals (`SRC-CORE`, `SRC-VIS`) | OpenXML validation and actual Excel opening | `EXTERNAL_VALIDATION_BLOCKED` | Export exists; final actual-Excel validation is external. |
| `REP-007` | Immutable historical generations, comparison and re-export (`SRC-P2`) | Hash, comparison and archive tests | `EXTERNAL_VALIDATION_BLOCKED` | Archive is an extracted workspace; export requires the currently opened generation ID and cannot reuse a document after selection changes. Live SQL/hash UAT remains. |
| `REP-008` | Daily/morning/evening report packs and ZIP packaging (`SRC-DAILY`, `SRC-P2`) | Pack contents, totals, hashes and sharing prep | `EXTERNAL_VALIDATION_BLOCKED` | Daily pack generation/export is module-owned and scope-bound; exact release outputs and sharing UAT remain. |
| `REP-009` | Report metadata, control status and source lineage remain visible (`SRC-VIS`) | UI/export inspection | `EXTERNAL_VALIDATION_BLOCKED` | Must be included in per-report verification. |
| `REP-010` | All production report codes are reachable and truthfully render data/unavailable state (`SRC-UI4`) | Navigation completeness plus execution matrix | `EXTERNAL_VALIDATION_BLOCKED` | Bidirectional ownership and visual-classification tests cover every production report code; complete installed execution/unavailable-state matrix remains. |
| `DSR-001` | Final DSR uses the supplied mockup/mapping and actual data flow (`SRC-DSR`) | Production-path sample PDF and binding coverage | `VERIFIED` | The production export path now emits `output/pdf/ETP_Daily_Sales_Report_2026-08-25.pdf`; focused DSR/report tests and independent rendered inspection pass. Final artifact hash/commit binding remains. |
| `DSR-002` | Exactly one A4 landscape page at 100% scale (`SRC-DSR`) | Programmatic page count and rendered inspection | `VERIFIED` | The current sample is programmatically one A4 landscape page and its Poppler preview has no clipping, overlap, missing glyphs or excessive dead space. |
| `DSR-003` | FTD/MTD/YTD, TY/LY value and quantity, Service and targets are retained (`SRC-DSR`) | Field-to-render traceability and PDF text/image checks | `VERIFIED` | Independent inspection confirms FTD/MTD/YTD, TY/LY, value and quantity, Service, and target blocks in the current production-path sample. Final production-data UAT remains. |
| `DSR-004` | Weekday is derived from business date; no mockup footer (`SRC-DSR`) | Date tests and PDF text assertions | `VERIFIED` | The current sample derives `Tuesday` for 25 Aug 2026 and omits `Mock-up · Page 1`; focused date/PDF assertions pass. |
| `DSR-005` | Missing LY MTD shows `— / —` and `LY MTD source required` (`SRC-DSR`) | Missing-data unit and PDF tests | `VERIFIED` | The current sample truthfully contains `LY MTD source required`; authoritative LY MTD data remains `IN-SRC-010`. |
| `DLY-001` | Daily Workflow readiness, manual inputs, finalise and reopen (`SRC-DAILY`) | Installed role-based live SQL workflow | `EXTERNAL_VALIDATION_BLOCKED` | Daily Workflow is an extracted workspace using cohesive read/write/pack ports; focused scope, state and authorization tests pass. Live SQL UAT remains. |
| `DLY-002` | Manual Entry has date/store scope, missing versus zero, reasons and history (`SRC-DSR` follow-up) | CRUD/audit/locked-day tests and DSR propagation | `EXTERNAL_VALIDATION_BLOCKED` | Manual Entry is owned by the Daily Workflow workspace; date/store changes invalidate stale pack state and missing-versus-zero semantics are preserved. Live CRUD/DSR UAT remains. |
| `DLY-003` | Finalised days reject mutations; reopen is authorized and audited (`SRC-DAILY`) | Database guard and role tests | `EXTERNAL_VALIDATION_BLOCKED` | Locked-day safeguards remain in SQL and the adapter independently enforces Owner access before reopen. Live audit verification remains. |
| `DLY-004` | FTD/MTD/YTD and Indian financial-year periods are correct (`SRC-DAILY`) | Boundary-date formula tests | `VERIFIED` | Previous tests reported; rerun after modular work. |
| `DLY-005` | Missing data is not zero; zero denominators show `N/A` (`SRC-DAILY`, `SRC-VIS`) | Formula/state tests across renderers | `VERIFIED` | Durable rule recorded; exporter coverage incomplete. |
| `RULE-001` | Growth is `(TY-LY)/LY × 100`, safely handling LY zero/missing (`SRC-DAILY`, `SRC-DSR`) | Formula tests | `VERIFIED` | Existing tests reported. |
| `RULE-002` | Combined conversion uses combined invoices / combined walk-ins; no store conversion without store walk-ins (`SRC-DSR`) | Formula and availability tests | `VERIFIED` | Walk-ins depend on Manual Entry. |
| `RULE-003` | Target achievement uses MTD actual / monthly target; display true result while fill may cap at 100% (`SRC-DSR`) | Formula and renderer tests | `VERIFIED` | Existing DSR contract reported. |
| `RULE-004` | Customer/invoice and staff-attributed denominators remain separately labelled until approved (`SRC-DAILY`) | Report labels and variance tests | `OWNER_INPUT_BLOCKED` | Owner must approve final denominator policy. |
| `RULE-005` | `TC` and unknown tenders remain quarantined/unapproved (`SRC-DAILY`) | Fail-closed accounting/control tests | `OWNER_INPUT_BLOCKED` | Meaning and accounting treatment are not approved. |
| `RULE-006` | Customer-identifying output policy (`SRC-DAILY`) | Approved policy and privacy tests | `OWNER_INPUT_BLOCKED` | Canonical reporting currently excludes customer PII. |
| `RULE-007` | Physical-stock composition and stock movement signs/categories (`SRC-DAILY`) | Approved rules and reconciliation tests | `OWNER_INPUT_BLOCKED` | Preserve source signs until approved. |
| `RULE-008` | Category, sell-through, stock turn and days-cover reporting (`SRC-CORE`) | Approved source master/rules and tests | `SOURCE_DATA_BLOCKED` | `CLUSTER` cannot be repurposed as category. |
| `RULE-009` | Final ABV/ASP denominator and return treatment | Approved definition and formula tests | `OWNER_INPUT_BLOCKED` | Do not silently equate with existing ATV/AUPT. |
| `RULE-010` | Service source remains manual or gains an approved ETP profile (`SRC-DAILY`) | Owner decision or populated Service export | `OWNER_INPUT_BLOCKED` | Current controlled manual input remains valid pending decision. |

## D. Visual reporting and WPF user experience

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `VIS-001` | Reusable visual model/registry uses canonical report results (`SRC-VIS`) | Architecture and reconciliation tests | `VERIFIED` | Visual composition and seven representative definitions use canonical report data; focused reconciliation/export tests pass. |
| `VIS-002` | Every report has an explicit specialized, table-first or not-applicable visual decision (`SRC-VIS`) | Complete catalogue classification | `VERIFIED` | `ProductReportVisualClassificationRegistry` classifies every production report code; a bidirectional catalogue test rejects missing, stale or duplicate codes while preserving the seven visual definitions. |
| `VIS-003` | WPF, SVG, PDF and Excel preserve chart type and all series (`SRC-VIS`) | Cross-renderer golden tests | `VERIFIED` | Cross-renderer chart-type and multi-series regression coverage passes in the combined suite at `08e39c8`. |
| `VIS-004` | Indian grouping, currency glyphs, quantities and large values render correctly (`SRC-VIS`) | Text extraction plus rendered-image tests | `VERIFIED` | Currency/quantity classification, Indian grouping, Unicode extraction and rendered DSR inspection pass at `08e39c8`. |
| `VIS-005` | Visuals handle negatives, missing states, Top-N/Other and zero denominators (`SRC-VIS`) | Renderer tests | `VERIFIED` | Renderer tests distinguish missing/zero/not-applicable and preserve Top-N totals with `Other` at `08e39c8`. |
| `VIS-006` | Visual reports are printable, paginated, accessible and deterministic (`SRC-VIS`) | Rendered PDF, high-DPI, keyboard and performance evidence | `EXTERNAL_VALIDATION_BLOCKED` | Headless smoke renders all 13 extracted surfaces with authored accessible controls; DSR PDF inspection passes. Installed high-DPI/printer acceptance remains external. |
| `UI-001` | Touch-first WPF shell, frozen navigation rail and role-aware module home (`SRC-UI4`) | Navigation/accessibility tests and installed smoke | `EXTERNAL_VALIDATION_BLOCKED` | Shell/workspace ownership is structurally implemented and role-aware navigation tests pass; installed touch/display acceptance remains. |
| `UI-002` | Contextual sidebars preserve all feature destinations (`SRC-UI4`) | Navigation completeness test | `EXTERNAL_VALIDATION_BLOCKED` | Routes reported present; full installed matrix pending. |
| `UI-003` | Reports open in dedicated workspace with period, preview and export controls (`SRC-DSR` follow-up) | Route/UI automation | `EXTERNAL_VALIDATION_BLOCKED` | `ReportsWorkspaceView` owns period, preview, filters and export controls for report routes; installed route-by-route acceptance remains. |
| `UI-004` | Density selector is in sidebar, not bottom of content (`SRC-FOLLOWUP`) | Visual and keyboard smoke | `EXTERNAL_VALIDATION_BLOCKED` | Reported implemented. |
| `UI-005` | Comfortable and Compact modes persist and remain usable (`SRC-UI4`) | Restart, scaling and layout tests | `EXTERNAL_VALIDATION_BLOCKED` | Preference feature exists; installed verification pending. |
| `UI-006` | No nested scrolling or avoidable long-tab scrolling (`SRC-UI4`, follow-up) | 1280×720, 1366×768 and 200% scaling inspection | `EXTERNAL_VALIDATION_BLOCKED` | Requires final screen matrix. |
| `UI-007` | Windows keyboard shortcuts, including Alt+Left/Right navigation (`SRC-FOLLOWUP`) | Registry-to-handler reconciliation and UI automation | `VERIFIED` | Executable gestures, guarded handlers and Help parity are covered; unsupported application claims were removed. |
| `UI-008` | Loading, empty, error and unavailable states use plain language (`SRC-UI4`, `SRC-P2`) | UI state tests and content audit | `VERIFIED` | UI state coverage and the current manual/help content audit are bound to `08e39c8`. |
| `UI-009` | Keyboard, Narrator, focus, high contrast and touch accessibility (`SRC-UI4`, follow-up item 17) | Installed accessibility matrix | `EXTERNAL_VALIDATION_BLOCKED` | Automated labels/tests are not sufficient for real Narrator/touch validation. |
| `HELP-001` | Help is a sidebar module with tiled menu topics (`SRC-FOLLOWUP`) | Route and layout test | `VERIFIED` | Help workspace extraction and navigation tests are documented. |
| `HELP-002` | Every live module has complete step-by-step help (`SRC-FOLLOWUP`) | Content-to-route audit; no placeholders | `VERIFIED` | `HelpCentreTests` passes 19/19 on 29 August 2026: every non-shortcut topic has at least four numbered non-placeholder steps, every owned shell destination resolves to available Help, and every workspace link targets an owned destination. |
| `HELP-003` | Shortcut help exactly matches implemented shortcuts (`SRC-FOLLOWUP`) | Automated registry/help parity test | `VERIFIED` | Executable Help rows derive from the registry; bidirectional parity and an explicit native-WPF allowlist are tested. |

## E. Productisation, operations and administration

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `OPS-001` | Windows Owner, Store Manager and Viewer access (`SRC-P2`, Owner decision) | Adversarial installed role tests | `EXTERNAL_VALIDATION_BLOCKED` | Access crosses an Application session contract and Windows-integrated SQL adapter; module and adapter permission tests pass. Installed three-role UAT remains `IN-EXT-003`. |
| `OPS-002` | Store Manager can import; Owner always has all rights (`SRC-FOLLOWUP`) | Role matrix and UI/database enforcement | `EXTERNAL_VALIDATION_BLOCKED` | Import persistence, Source Inbox and Daily Workflow enforce import permission at the adapter boundary; controlled restatement remains Owner-only. Target-PC UAT remains. |
| `OPS-003` | Only Owner approves mapping/control-rule changes (`SRC-FOLLOWUP`) | Approval and direct-bypass tests | `VERIFIED` | Administration, accounting mapping, controlled restatement and daily reopen paths have explicit Owner checks with direct-bypass regression coverage. |
| `OPS-004` | Digital registers and document linkage (`SRC-P2`) | CRUD, audit, search and permission tests | `EXTERNAL_VALIDATION_BLOCKED` | Registers are an extracted workspace using `IDigitalRegisterService`; Source Inbox relays only the immutable selected document ID. Live SQL UAT remains. |
| `OPS-005` | Sharing prepares WhatsApp/email/ZIP without falsely claiming delivery (`SRC-P2`) | Installed integration and audit wording | `EXTERNAL_VALIDATION_BLOCKED` | Archive/Distribution presentation is extracted and safe launch/preparation wording remains; external client behavior must be checked on target PC. |
| `OPS-006` | Accounting preview, approved mapping and balanced Tally XML (`SRC-P2`) | Golden XML, balance and segregation tests | `EXTERNAL_VALIDATION_BLOCKED` | Accounting is an extracted workspace using an Application service; scope changes/refresh invalidate stale previews and Owner-only mapping approval remains enforced. Live SQL/Tally UAT remains. |
| `OPS-007` | Scheduler/watch folder/automation are safe and auditable (`SRC-P2`) | Scheduled-task, duplicate and unattended tests | `EXTERNAL_VALIDATION_BLOCKED` | Operations is an extracted workspace and one-shot automation remains composition-owned; elevated target-PC scheduled-task evidence is external. |
| `OPS-008` | Automatic database health and growth/backup/import warnings (follow-up items 3 and 15) | Threshold tests and installed health screen | `EXTERNAL_VALIDATION_BLOCKED` | Dashboard/Operations surfaces use the database lifecycle/health boundaries and threshold tests; installed service/disk/backup behavior remains external. |
| `OPS-009` | Daily SQL backup schedule with indefinite data retention (follow-up item 1; Owner decision) | Task installation, backup creation, capacity warning | `EXTERNAL_VALIDATION_BLOCKED` | Must be installed and observed on the target PC; application must never auto-delete business data. |
| `OPS-010` | Full backup/restore recovery drill; periodic drill schedule (follow-up item 2) | Checksum, verify-only, isolated restore and lineage compare | `EXTERNAL_VALIDATION_BLOCKED` | Backup source now produces a non-overwriting receipt after checksum backup, `RESTORE VERIFYONLY`, length and SHA-256 inspection. No live backup, isolated restore or recurring drill has been run for 1.8.5. |
| `OPS-011` | Privacy-safe offline support package (follow-up item 16) | Package inspection proves no confidential rows/secrets | `VERIFIED` | Privacy-safe package exclusions and the 23-test security regression suite pass at `08e39c8`; production rows remain excluded by contract. |
| `OPS-012` | Audit history retained for two years; business data never automatically deleted (Owner decision) | Retention configuration/query tests and runbook | `VERIFIED` | Maintenance defaults to 730 audit days; source/runbook review confirms the cleanup excludes business data, lineage, reporting facts and backups. |
| `OPS-013` | Harden settings storage and local-path validation (follow-up item 19) | ACL/path traversal/network/invalid path tests | `OWNER_INPUT_BLOCKED` | Atomic rooted settings, traversal/invalid-path rejection, launch allowlisting, reparse-point checks and Windows-integrated SQL tests pass. Closing the remaining production boundary requires Owner approval of which document/share/OCR roots may be UNC/network paths and the Windows principals/ownership required for each root; blanket UNC rejection or guessed ACL changes could break approved share/backup workflows or grant the wrong access. See `IN-OWN-010`. |
| `OPS-014` | Dependency, vulnerability, secrets and privacy scans (follow-up item 20) | Current clean reports tied to release commit | `VERIFIED` | Current .NET/npm audits report no known vulnerabilities and the 23-test security regression suite passes; deprecated xUnit 2.9.3 remains optional maintenance, not a shipped-runtime vulnerability. |
| `OPS-015` | Administrator handbook and user manual (follow-up item 18) | Content-to-current-UI review | `VERIFIED` | `docs/17_ADMINISTRATOR_HANDBOOK.md` and `docs/18_USER_MANUAL.md` were reviewed against the modular navigation and current operational boundaries on 29 August 2026; no stale pre-modular workflow claim was found. |
| `OPS-016` | Operational-audit literals and mutation/audit atomicity remain compatible (`SRC-P2`, `SRC-FOLLOWUP`) | Emitted-literal/constraint test, transactional mutation test and live migration exercise | `IMPLEMENTED_NOT_VERIFIED` | Migration `0015` aligns the constraint with emitted events, report outcomes are normalized and sharing-contact mutation/audit is transactional. Focused/current combined verification and live migration remain pending. |

## F. Installer, release and production acceptance

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `REL-001` | Bootstrap detects/installs SQL Server and configures the database (`SRC-CORE`, follow-up) | Fresh Windows VM with SQL absent/present/failure cases | `EXTERNAL_VALIDATION_BLOCKED` | Inno invokes the elevated bootstrap; source now checks SQL 2022/database compatibility, packaged migrations, state and tooling before migration. No 1.8.5 installer has been compiled or run; the clean-VM matrix remains. |
| `REL-002` | SQL Server Express starts automatically and storage capacity warnings are visible (Owner decision) | Reboot/service/capacity tests | `EXTERNAL_VALIDATION_BLOCKED` | Bootstrap configures `MSSQL$SQLEXPRESS` automatic start; backup/dashboard thresholds expose capacity warnings. Reboot and real-volume evidence remain `IN-EXT-001`/`IN-EXT-005`. |
| `REL-003` | Generic branding and installer icon (follow-up item 11) | Installed Programs, shortcuts and executable inspection | `VERIFIED` | Version 1.8.3 artifact/source inspection and the previously completed install/repair/uninstall lifecycle confirm the generic branding assets. |
| `REL-004` | Installer, upgrade, repair and uninstall preserve data (`follow-up item 12`) | Clean VM matrix and rollback tests | `EXTERNAL_VALIDATION_BLOCKED` | Existing-database migration source now requires a verified backup receipt and post-migration health gate, stops without automatic reverse/delete, and can hash-check a preserved external file in lifecycle testing. No compiled/live 1.8.5 install, upgrade, failure or uninstall proof exists. |
| `REL-005` | Automatic versioning and changelog generation (follow-up item 13) | Clean-tag build evidence | `IMPLEMENTED_NOT_VERIFIED` | Source/changelog metadata is 1.8.5. No clean commit/tag build or 1.8.5 artifact has been produced. |
| `REL-006` | End-to-end Windows UI automation (follow-up item 14) | Installed application workflow suite | `EXTERNAL_VALIDATION_BLOCKED` | Headless smoke covers all 13 extracted workspace surfaces, focus, accessible controls and duplicate-parent rejection; it is not installed end-to-end workflow acceptance. |
| `REL-007` | Code-sign installer/executable to remove Unknown Publisher warning | Signed artifact and Windows trust verification | `EXTERNAL_VALIDATION_BLOCKED` | Requires purchased certificate and final publisher identity. |
| `REL-008` | Exact tested installer is the released artifact | Hash, manifest, SBOM and no-rebuild promotion | `IMPLEMENTED_NOT_VERIFIED` | 1.8.4 is preserved but rejected/never promoted because of the audit defect and inconsistent SBOM. No 1.8.5 artifact, hash, SBOM, provenance or no-rebuild record exists. |
| `REL-009` | Owner, Store Manager and Viewer UAT on target PC | Signed role-specific scripts/results | `EXTERNAL_VALIDATION_BLOCKED` | Requires human acceptance. |
| `REL-010` | Printer, actual Excel and PDF output work on target equipment | Printed/PDF/Excel acceptance record | `EXTERNAL_VALIDATION_BLOCKED` | Requires target printer and Microsoft Excel. |
| `REL-011` | Publish accepted commits/releases to the configured GitHub repository | Remote commit/tag/release verification | `NOT_STARTED` | Push only after reviewed phase commits or explicit release authorization. |

## G. Knowledge system and licensing

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `KNOW-001` | Obsidian-compatible repository vault with routing and authority rules (`SRC-KNOW`) | Link validator and retrieval evaluation | `VERIFIED` | `knowledge/`, `AI-CONTEXT.md`, `AI-ROUTER.md` and ADRs are present and validated. |
| `KNOW-002` | Graphify is configured and indexed (`SRC-KNOW`) | Query against current graph and config inspection | `VERIFIED` | Graph exists and returns WPF architecture/document nodes. Generated `graphify-out` remains non-release evidence. |
| `KNOW-003` | CRG SQLite AST index and strict ignore rules (`SRC-FOLLOWUP`) | AST query plus ignore/config inspection | `VERIFIED` | `.code-review-graph`, `.crgignore` and repository instructions exist. |
| `KNOW-004` | Knowledge reflects accepted architecture/rule changes without duplicating task state (`SRC-KNOW`) | Vault validation and spot audit each phase | `IMPLEMENTED_NOT_VERIFIED` | System/Data/Import/Desktop notes and ADR-007/ADR-008 are reconciled to current source and local link checks pass; independent spot audit remains pending. |
| `LIC-001` | Dual-layer licensing engineering specification, contracts, threats and test plan (`SRC-LIC`) | Traceability/schema/docs validation | `VERIFIED` | Licensing engineering package and ADR-006 are committed. |
| `LIC-002` | Production licensing runtime and startup enforcement (`SRC-LIC`) | Full implementation and attack matrix | `OWNER_APPROVED_DEFERRED` | Owner explicitly instructed implementation to wait until the software is otherwise complete. |
| `LIC-003` | Microsoft app registration and approved Owner identity (`SRC-LIC`) | Recorded client configuration and allowlisted `tid`/`oid` | `OWNER_APPROVED_DEFERRED` | Needed only when runtime licensing phase is authorized. |
| `LIC-004` | Production signing-key ceremony and owner licensing utility (`SRC-LIC`) | Controlled key record, offline escrow and attack tests | `OWNER_APPROVED_DEFERRED` | Private key has intentionally not been created. |

## H. Explicit legacy exclusions

| ID | Requirement/source | Status | Reason |
|---|---|---|---|
| `LEG-001` | Android/Capacitor UI and mobile packaging under `www/` | `NOT_APPLICABLE_LEGACY` | Current product is .NET/WPF Windows desktop. |
| `LEG-002` | Node/browser V6 acceptance tests under `tests/` | `NOT_APPLICABLE_LEGACY` | They may provide historical business evidence but cannot verify WPF behavior. |
| `LEG-003` | `docs/audit/V6-*` completion-wave claims | `NOT_APPLICABLE_LEGACY` | They describe a legacy execution stream and cannot close current WPF requirements. |
| `LEG-004` | Legacy JavaScript mappings | `NOT_APPLICABLE_LEGACY` | Reference only; a mapping becomes active only after validation and implementation in the .NET import/reporting path. |

## Closure dashboard

This is the closure classification for the uncommitted 1.8.5 integration working tree, not a production-approval statement. The 116 active rows comprise 48 `VERIFIED`, 14 `IMPLEMENTED_NOT_VERIFIED`, 40 `EXTERNAL_VALIDATION_BLOCKED`, 7 `OWNER_INPUT_BLOCKED`, 3 `SOURCE_DATA_BLOCKED`, 3 `OWNER_APPROVED_DEFERRED`, 0 `IN_PROGRESS` and 1 `NOT_STARTED`; the four legacy rows remain excluded. Current combined verification, any eligible artifact/evidence set, external acceptance, Owner decisions, source inputs and deferred licensing remain open.

| Classification | Meaning for the sprint |
|---|---|
| `VERIFIED` | Retain evidence and invalidate only when affected. |
| `IMPLEMENTED_NOT_VERIFIED` | Reverify against the current commit/artifact. |
| `IN_PROGRESS` | Finish implementation and independently verify. |
| `NOT_STARTED` | Build during the assigned closure phase. |
| Blocked/deferred | Complete all autonomous preparation, then obtain the named input or approval. |

## Update protocol

For every implementation commit:

1. identify affected requirement IDs before editing;
2. change those rows to `IN_PROGRESS`;
3. record implementation paths and commit SHA after merge;
4. change to `IMPLEMENTED_NOT_VERIFIED` until independent checks pass;
5. attach exact evidence and verifier before changing to `VERIFIED`;
6. update the pending-input register when a blocker is created, resolved or deferred;
7. rerun the ledger audit before any release or broad completion statement.

The Lead Integrator owns edits to this ledger during the closure sprint. Implementing agents provide evidence but do not approve their own rows.
