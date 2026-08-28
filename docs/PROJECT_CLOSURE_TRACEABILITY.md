# ETP Reporting Engine — Project Closure Traceability

Status date: 28 August 2026
Authority: current .NET 10/WPF product only
Owner: closure sprint Lead Integrator

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
| `GOV-001` | Maintain one atomic requirement ledger (`SRC-FOLLOWUP`) | All active prompt sections mapped; no uncategorized requirement | `IN_PROGRESS` | This ledger establishes grouped atomic controls; Phase 0 must reconcile it against every prompt and follow-up before verification. |
| `GOV-002` | Maintain one input/deferment register (`SRC-FOLLOWUP`) | Each blocker has owner, exact input, impact and unblock gate | `VERIFIED` | `docs/PENDING_INPUT_AND_DEFERMENT_REGISTER.md`. |
| `GOV-003` | Prevent phase completion from implying product completion (`SRC-FOLLOWUP`) | Governance text and release gates prohibit broad completion claims | `VERIFIED` | Status and evidence rules in this ledger. |
| `GOV-004` | Preserve unrelated working-tree changes (`SRC-MOD`) | Scoped diffs and clean attribution | `IMPLEMENTED_NOT_VERIFIED` | Required for every phase; Graphify cache is explicitly excluded. |
| `GOV-005` | Use Graphify, CRG, source and tests for architecture work (`SRC-KNOW`, `SRC-MOD`) | Query records and source-backed change review | `IMPLEMENTED_NOT_VERIFIED` | Tooling and repository instructions exist; enforce per phase. |
| `ARCH-001` | One WPF executable with clear Domain, Application, Import, Reporting, SQL and Desktop responsibilities (`SRC-CORE`, `SRC-MOD`) | Dependency tests plus source inspection | `IN_PROGRESS` | Backend projects are separated, but Desktop still bypasses Application in major workflows. |
| `ARCH-002` | `MainWindow` is only the window shell/workspace host (`SRC-MOD`, `SRC-UI4`) | No feature state, repositories, formulas, imports or export orchestration in shell | `IN_PROGRESS` | Dashboard and Help extracted; Reports, Import, Daily Workflow, Archive, Settings, Accounting, Operations and Administration remain. |
| `ARCH-003` | One composition root constructs dependencies (`SRC-MOD`, `SRC-LIC`) | Construction limited to App/composition/adapters; no service locator | `IN_PROGRESS` | `DesktopCompositionRoot` now owns WPF startup, headless initialization/automation and Dashboard query construction; 82 temporary MainWindow infrastructure constructions remain to migrate. |
| `ARCH-004` | Application use-case contracts are the normal Desktop boundary (`SRC-MOD`) | Desktop workspaces depend on contracts, not SQL/import implementations | `IN_PROGRESS` | Dashboard contract exists; wider production path still bypasses Application. |
| `ARCH-005` | Independent cohesive workspace modules (`SRC-MOD`, `SRC-UI4`) | Each route resolves to one owner module; no God ViewModel | `IN_PROGRESS` | Dashboard and Help are representative slices only. |
| `ARCH-006` | No SQL/repository construction in Views or ViewModels (`SRC-MOD`) | Architecture test and source scan | `IN_PROGRESS` | No raw SQL found in Desktop, but concrete SQL repositories are directly constructed. |
| `ARCH-007` | No report formulas or workbook parsing in Desktop (`SRC-CORE`, `SRC-MOD`) | Architecture/source tests | `IMPLEMENTED_NOT_VERIFIED` | Design boundary exists; must be rechecked after each extraction. |
| `ARCH-008` | Preserve startup modes during refactor (`SRC-MOD`) | Normal, database-initialize and automation-once tests | `VERIFIED` | Framework-neutral coordinator tests independently verify exact argument routing, interactive single-window behavior, headless no-window behavior, exit codes, exceptions and diagnostic labels; full solution and UI smoke pass. |
| `ARCH-009` | Architecture guardrails prevent regression (`SRC-MOD`) | Automated boundary, construction, route and size/responsibility tests | `IN_PROGRESS` | Navigation/lower-layer/module boundaries and the decreasing MainWindow construction inventory are enforced; final zero-construction and shell-only gates remain. |
| `ARCH-010` | Remove obsolete MainWindow paths after migration (`SRC-MOD`) | Source/Graphify references clean; shell-only final audit | `NOT_STARTED` | Cannot begin final cleanup until all workspaces migrate. |

## B. Data, import and canonical storage

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `DATA-001` | SQL Server Express is authoritative structured storage (`SRC-CORE`) | Fresh install, migrations and live SQL workflow | `IMPLEMENTED_NOT_VERIFIED` | SQL implementation exists; exact closure baseline must rerun. |
| `DATA-002` | Source lineage reaches workbook, sheet and row (`SRC-CORE`, `SRC-DAILY`) | Live import and report drill-down evidence | `IMPLEMENTED_NOT_VERIFIED` | Existing audit reports lineage; installed workflow must be revalidated. |
| `DATA-003` | Business date comes from ETP content, not file timestamp/import timestamp (`SRC-DAILY`) | Golden tests across files and dates | `IMPLEMENTED_NOT_VERIFIED` | Documented and tested previously; closure rerun required. |
| `DATA-004` | R025 is item-level sales; `NETVALUE` is GST-inclusive primary sales value (`SRC-CORE`, Owner decision) | Mapping and reconciliation tests | `IMPLEMENTED_NOT_VERIFIED` | Approved rule exists in knowledge vault. |
| `DATA-005` | R022 controls final invoice/tender totals (`SRC-DAILY`, Owner decision) | Cross-report control tests | `IMPLEMENTED_NOT_VERIFIED` | Approved rule exists; real tender variances remain visible by design. |
| `DATA-006` | `INV` is invoice and `SR` is signed-negative sales return (`SRC-DAILY`, Owner decision) | Import and aggregation tests with returns | `IMPLEMENTED_NOT_VERIFIED` | Rule is documented; closure rerun required. |
| `DATA-007` | `CLUSTER` is brand segment, not product category (`SRC-CORE`, Owner decision) | Mapping and report-label tests | `IMPLEMENTED_NOT_VERIFIED` | Rule is documented; category report remains source blocked. |
| `IMP-001` | Automatic file/profile/store/business-date detection (`SRC-CORE`, `SRC-DAILY`) | Corpus tests for every active profile | `IMPLEMENTED_NOT_VERIFIED` | Previous audit claims implementation; exact corpus evidence must be renewed. |
| `IMP-002` | File, folder and ZIP batch import (`SRC-DAILY`, `SRC-P2`, follow-up item 4) | Installed UI plus live SQL file/folder/ZIP tests | `IMPLEMENTED_NOT_VERIFIED` | Functionality reported present; full installed closure test pending. |
| `IMP-003` | Progress, cancellation, retry and clear failure summaries (follow-up item 5) | Large-batch UI automation and atomic cancellation test | `IMPLEMENTED_NOT_VERIFIED` | Reported present; high-risk revalidation required. |
| `IMP-004` | Three-level duplicate/conflict protection (`SRC-P2`) | Exact duplicate, same transaction and changed-content tests | `IMPLEMENTED_NOT_VERIFIED` | Existing SQL/import tests reported; live concurrency evidence pending. |
| `IMP-005` | Atomic overlapping-date restatement (`SRC-DAILY`, `SRC-P2`) | Replace/archive/rollback tests with live SQL | `IMPLEMENTED_NOT_VERIFIED` | Existing behavior reported; closure test pending. |
| `IMP-006` | Unknown layouts fail closed with actionable diagnostics (`SRC-CORE`, `SRC-DAILY`) | Malformed/unknown workbook tests | `IMPLEMENTED_NOT_VERIFIED` | Profile diagnostics exist; all entry paths must be rechecked. |
| `IMP-007` | Source Inbox and document integrity (`SRC-P2`, `SRC-UI4`) | Hash verification, review, linkage and permissions | `IMPLEMENTED_NOT_VERIFIED` | Source/document layer reported present; module extraction pending. |
| `IMP-008` | Native PDF extraction and safe OCR review (`SRC-P2`) | Text PDF, scanned PDF, review and no-silent-trust tests | `IMPLEMENTED_NOT_VERIFIED` | Architecture and implementation reported; installed OCR validation pending. |
| `IMP-009` | Active deterministic profiles R003, R013, R022, R025, Variant Stock Ledger and Closing Stock | Per-profile corpus/reconciliation tests | `IMPLEMENTED_NOT_VERIFIED` | Must be verified individually in closure phase. |
| `IMP-010` | Empty/unavailable ETP profiles are not fabricated | Populated source required or truthful unavailable state | `SOURCE_DATA_BLOCKED` | AdvanceOrder, Encircle, GC, PRP and some cross-store reports lack populated samples. |

## C. Reporting, daily workflow and business controls

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `REP-001` | Deterministic report catalogue and renderer-neutral results (`SRC-CORE`, `SRC-VIS`) | Catalogue execution and UI/PDF/Excel reconciliation | `IMPLEMENTED_NOT_VERIFIED` | Catalogue exists; all reports require exact closure execution. |
| `REP-002` | Sales filters: store, brand segment, transaction type and item (follow-up item 6) | UI/filter query tests and totals reconciliation | `IMPLEMENTED_NOT_VERIFIED` | Reported implemented; installed interaction evidence pending. |
| `REP-003` | Report search, sorting, variance-only view and drill-down (follow-up item 8) | UI automation and source-lineage check | `IMPLEMENTED_NOT_VERIFIED` | Reported implemented; module migration and closure evidence pending. |
| `REP-004` | Tender-variance diagnostic without changing control rules (follow-up item 9) | R022 comparison and diagnostic tests | `IMPLEMENTED_NOT_VERIFIED` | Reported implemented; `TC` policy remains blocked. |
| `REP-005` | PDF export for every supported report (follow-up item 10) | Each report exported, parsed and rendered | `IMPLEMENTED_NOT_VERIFIED` | Generic export exists; exact per-report visual/Unicode verification is incomplete. |
| `REP-006` | Excel export preserves detailed data and deterministic totals (`SRC-CORE`, `SRC-VIS`) | OpenXML validation and actual Excel opening | `IMPLEMENTED_NOT_VERIFIED` | Export exists; final actual-Excel validation is external. |
| `REP-007` | Immutable historical generations, comparison and re-export (`SRC-P2`) | Hash, comparison and archive tests | `IMPLEMENTED_NOT_VERIFIED` | SQL safeguards reported; Archive module extraction pending. |
| `REP-008` | Daily/morning/evening report packs and ZIP packaging (`SRC-DAILY`, `SRC-P2`) | Pack contents, totals, hashes and sharing prep | `IMPLEMENTED_NOT_VERIFIED` | Existing pack implementation reported; exact closure output pending. |
| `REP-009` | Report metadata, control status and source lineage remain visible (`SRC-VIS`) | UI/export inspection | `IMPLEMENTED_NOT_VERIFIED` | Must be included in per-report verification. |
| `REP-010` | All production report codes are reachable and truthfully render data/unavailable state (`SRC-UI4`) | Navigation completeness plus execution matrix | `IMPLEMENTED_NOT_VERIFIED` | Routes exist according to current docs; complete installed matrix pending. |
| `DSR-001` | Final DSR uses the supplied mockup/mapping and actual data flow (`SRC-DSR`) | Production-path sample PDF and binding coverage | `IMPLEMENTED_NOT_VERIFIED` | New DSR classes exist, but prior runtime exported the old Daily Sales report; later fix requires full revalidation. |
| `DSR-002` | Exactly one A4 landscape page at 100% scale (`SRC-DSR`) | Programmatic page count and rendered inspection | `IMPLEMENTED_NOT_VERIFIED` | Focused exporter/tests reported; exact current artifact must be regenerated. |
| `DSR-003` | FTD/MTD/YTD, TY/LY value and quantity, Service and targets are retained (`SRC-DSR`) | Field-to-render traceability and PDF text/image checks | `IMPLEMENTED_NOT_VERIFIED` | Binding contract exists; production-data coverage needs closure evidence. |
| `DSR-004` | Weekday is derived from business date; no mockup footer (`SRC-DSR`) | Date tests and PDF text assertions | `IMPLEMENTED_NOT_VERIFIED` | Correct fact: 25 Aug 2026 is Tuesday; any Monday expectation for that date is invalid. |
| `DSR-005` | Missing LY MTD shows `— / —` and `LY MTD source required` (`SRC-DSR`) | Missing-data unit and PDF tests | `IMPLEMENTED_NOT_VERIFIED` | Must not fabricate LY. |
| `DLY-001` | Daily Workflow readiness, manual inputs, finalise and reopen (`SRC-DAILY`) | Installed role-based live SQL workflow | `IMPLEMENTED_NOT_VERIFIED` | Functionality reported; module extraction and final UAT pending. |
| `DLY-002` | Manual Entry has date/store scope, missing versus zero, reasons and history (`SRC-DSR` follow-up) | CRUD/audit/locked-day tests and DSR propagation | `IMPLEMENTED_NOT_VERIFIED` | Manual framework exists; dedicated module and complete interaction evidence pending. |
| `DLY-003` | Finalised days reject mutations; reopen is authorized and audited (`SRC-DAILY`) | Database guard and role tests | `IMPLEMENTED_NOT_VERIFIED` | SQL safeguards reported; closure run pending. |
| `DLY-004` | FTD/MTD/YTD and Indian financial-year periods are correct (`SRC-DAILY`) | Boundary-date formula tests | `IMPLEMENTED_NOT_VERIFIED` | Previous tests reported; rerun after modular work. |
| `DLY-005` | Missing data is not zero; zero denominators show `N/A` (`SRC-DAILY`, `SRC-VIS`) | Formula/state tests across renderers | `IMPLEMENTED_NOT_VERIFIED` | Durable rule recorded; exporter coverage incomplete. |
| `RULE-001` | Growth is `(TY-LY)/LY × 100`, safely handling LY zero/missing (`SRC-DAILY`, `SRC-DSR`) | Formula tests | `IMPLEMENTED_NOT_VERIFIED` | Existing tests reported. |
| `RULE-002` | Combined conversion uses combined invoices / combined walk-ins; no store conversion without store walk-ins (`SRC-DSR`) | Formula and availability tests | `IMPLEMENTED_NOT_VERIFIED` | Walk-ins depend on Manual Entry. |
| `RULE-003` | Target achievement uses MTD actual / monthly target; display true result while fill may cap at 100% (`SRC-DSR`) | Formula and renderer tests | `IMPLEMENTED_NOT_VERIFIED` | Existing DSR contract reported. |
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
| `VIS-001` | Reusable visual model/registry uses canonical report results (`SRC-VIS`) | Architecture and reconciliation tests | `IMPLEMENTED_NOT_VERIFIED` | Core visual model exists. |
| `VIS-002` | Every report has an explicit specialized, table-first or not-applicable visual decision (`SRC-VIS`) | Complete catalogue classification | `IN_PROGRESS` | Seven representative definitions are documented; remaining catalogue decisions are incomplete. |
| `VIS-003` | WPF, SVG, PDF and Excel preserve chart type and all series (`SRC-VIS`) | Cross-renderer golden tests | `IN_PROGRESS` | Known risk: non-line/multi-series and exporter chart-type divergence. |
| `VIS-004` | Indian grouping, currency glyphs, quantities and large values render correctly (`SRC-VIS`) | Text extraction plus rendered-image tests | `IN_PROGRESS` | Known risk: rupee sign replacement and quantity/currency misclassification. |
| `VIS-005` | Visuals handle negatives, missing states, Top-N/Other and zero denominators (`SRC-VIS`) | Renderer tests | `IMPLEMENTED_NOT_VERIFIED` | Requires complete catalogue pass. |
| `VIS-006` | Visual reports are printable, paginated, accessible and deterministic (`SRC-VIS`) | Rendered PDF, high-DPI, keyboard and performance evidence | `IMPLEMENTED_NOT_VERIFIED` | Final installed acceptance pending. |
| `UI-001` | Touch-first WPF shell, frozen navigation rail and role-aware module home (`SRC-UI4`) | Navigation/accessibility tests and installed smoke | `IMPLEMENTED_NOT_VERIFIED` | Shell redesign exists; architecture ownership is incomplete. |
| `UI-002` | Contextual sidebars preserve all feature destinations (`SRC-UI4`) | Navigation completeness test | `IMPLEMENTED_NOT_VERIFIED` | Routes reported present; full installed matrix pending. |
| `UI-003` | Reports open in dedicated workspace with period, preview and export controls (`SRC-DSR` follow-up) | Route/UI automation | `IMPLEMENTED_NOT_VERIFIED` | Implemented pattern reported; verify every applicable report area. |
| `UI-004` | Density selector is in sidebar, not bottom of content (`SRC-FOLLOWUP`) | Visual and keyboard smoke | `IMPLEMENTED_NOT_VERIFIED` | Reported implemented. |
| `UI-005` | Comfortable and Compact modes persist and remain usable (`SRC-UI4`) | Restart, scaling and layout tests | `IMPLEMENTED_NOT_VERIFIED` | Preference feature exists; installed verification pending. |
| `UI-006` | No nested scrolling or avoidable long-tab scrolling (`SRC-UI4`, follow-up) | 1280×720, 1366×768 and 200% scaling inspection | `IMPLEMENTED_NOT_VERIFIED` | Requires final screen matrix. |
| `UI-007` | Windows keyboard shortcuts, including Alt+Left/Right navigation (`SRC-FOLLOWUP`) | Registry-to-handler reconciliation and UI automation | `IN_PROGRESS` | Executable registry has fewer shortcuts than Help advertises. |
| `UI-008` | Loading, empty, error and unavailable states use plain language (`SRC-UI4`, `SRC-P2`) | UI state tests and content audit | `IMPLEMENTED_NOT_VERIFIED` | Closure review pending. |
| `UI-009` | Keyboard, Narrator, focus, high contrast and touch accessibility (`SRC-UI4`, follow-up item 17) | Installed accessibility matrix | `EXTERNAL_VALIDATION_BLOCKED` | Automated labels/tests are not sufficient for real Narrator/touch validation. |
| `HELP-001` | Help is a sidebar module with tiled menu topics (`SRC-FOLLOWUP`) | Route and layout test | `VERIFIED` | Help workspace extraction and navigation tests are documented. |
| `HELP-002` | Every live module has complete step-by-step help (`SRC-FOLLOWUP`) | Content-to-route audit; no placeholders | `IN_PROGRESS` | 18 of 19 topics are documented as overview placeholders. |
| `HELP-003` | Shortcut help exactly matches implemented shortcuts (`SRC-FOLLOWUP`) | Automated registry/help parity test | `IN_PROGRESS` | Help advertises about 35 shortcuts; executable registry has 19. |

## E. Productisation, operations and administration

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `OPS-001` | Windows Owner, Store Manager and Viewer access (`SRC-P2`, Owner decision) | Adversarial installed role tests | `IMPLEMENTED_NOT_VERIFIED` | SQL-backed role model exists; final user UAT pending. |
| `OPS-002` | Store Manager can import; Owner always has all rights (`SRC-FOLLOWUP`) | Role matrix and UI/database enforcement | `IMPLEMENTED_NOT_VERIFIED` | Approved rule recorded. |
| `OPS-003` | Only Owner approves mapping/control-rule changes (`SRC-FOLLOWUP`) | Approval and direct-bypass tests | `IMPLEMENTED_NOT_VERIFIED` | Approved rule recorded. |
| `OPS-004` | Digital registers and document linkage (`SRC-P2`) | CRUD, audit, search and permission tests | `IMPLEMENTED_NOT_VERIFIED` | Reported present; module extraction pending. |
| `OPS-005` | Sharing prepares WhatsApp/email/ZIP without falsely claiming delivery (`SRC-P2`) | Installed integration and audit wording | `IMPLEMENTED_NOT_VERIFIED` | External client behavior must be checked on target PC. |
| `OPS-006` | Accounting preview, approved mapping and balanced Tally XML (`SRC-P2`) | Golden XML, balance and segregation tests | `IMPLEMENTED_NOT_VERIFIED` | Existing implementation reported; module extraction pending. |
| `OPS-007` | Scheduler/watch folder/automation are safe and auditable (`SRC-P2`) | Scheduled-task, duplicate and unattended tests | `IMPLEMENTED_NOT_VERIFIED` | Elevated target-PC scheduler test is external. |
| `OPS-008` | Automatic database health and growth/backup/import warnings (follow-up items 3 and 15) | Threshold tests and installed health screen | `IMPLEMENTED_NOT_VERIFIED` | Reported present; final operational verification pending. |
| `OPS-009` | Daily SQL backup schedule with indefinite data retention (follow-up item 1; Owner decision) | Task installation, backup creation, capacity warning | `EXTERNAL_VALIDATION_BLOCKED` | Must be installed and observed on the target PC; application must never auto-delete business data. |
| `OPS-010` | Full backup/restore recovery drill; periodic drill schedule (follow-up item 2) | Checksum, verify-only, isolated restore and lineage compare | `EXTERNAL_VALIDATION_BLOCKED` | Engineering path exists; recurring real environment drill remains. Monthly is the current recommended cadence. |
| `OPS-011` | Privacy-safe offline support package (follow-up item 16) | Package inspection proves no confidential rows/secrets | `IMPLEMENTED_NOT_VERIFIED` | Reported implementation requires adversarial closure scan. |
| `OPS-012` | Audit history retained for two years; business data never automatically deleted (Owner decision) | Retention configuration/query tests and runbook | `IMPLEMENTED_NOT_VERIFIED` | Must verify no conflicting cleanup path exists. |
| `OPS-013` | Harden settings storage and local-path validation (follow-up item 19) | ACL/path traversal/network/invalid path tests | `IMPLEMENTED_NOT_VERIFIED` | Final security scan pending. |
| `OPS-014` | Dependency, vulnerability, secrets and privacy scans (follow-up item 20) | Current clean reports tied to release commit | `IMPLEMENTED_NOT_VERIFIED` | Old scan evidence expires after dependency/code changes. |
| `OPS-015` | Administrator handbook and user manual (follow-up item 18) | Content-to-current-UI review | `IMPLEMENTED_NOT_VERIFIED` | Documents exist; must be updated after modular/UI closure. |

## F. Installer, release and production acceptance

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `REL-001` | Bootstrap detects/installs SQL Server and configures the database (`SRC-CORE`, follow-up) | Fresh Windows VM with SQL absent/present/failure cases | `EXTERNAL_VALIDATION_BLOCKED` | Bootstrap code exists; exact installer must be tested on clean machines. |
| `REL-002` | SQL Server Express starts automatically and storage capacity warnings are visible (Owner decision) | Reboot/service/capacity tests | `EXTERNAL_VALIDATION_BLOCKED` | Requires target/VM validation. |
| `REL-003` | Generic branding and installer icon (follow-up item 11) | Installed Programs, shortcuts and executable inspection | `IMPLEMENTED_NOT_VERIFIED` | Assets reported; final artifact inspection pending. |
| `REL-004` | Installer, upgrade, repair and uninstall preserve data (`follow-up item 12`) | Clean VM matrix and rollback tests | `EXTERNAL_VALIDATION_BLOCKED` | Uninstall must preserve SQL databases, backups, sources and exports. |
| `REL-005` | Automatic versioning and changelog generation (follow-up item 13) | Clean-tag build evidence | `IMPLEMENTED_NOT_VERIFIED` | Tooling reported; exact final pipeline run pending. |
| `REL-006` | End-to-end Windows UI automation (follow-up item 14) | Installed application workflow suite | `IMPLEMENTED_NOT_VERIFIED` | Existing smoke coverage is not complete end-to-end acceptance. |
| `REL-007` | Code-sign installer/executable to remove Unknown Publisher warning | Signed artifact and Windows trust verification | `EXTERNAL_VALIDATION_BLOCKED` | Requires purchased certificate and final publisher identity. |
| `REL-008` | Exact tested installer is the released artifact | Hash, manifest, SBOM and no-rebuild promotion | `NOT_STARTED` | Final release gate. |
| `REL-009` | Owner, Store Manager and Viewer UAT on target PC | Signed role-specific scripts/results | `EXTERNAL_VALIDATION_BLOCKED` | Requires human acceptance. |
| `REL-010` | Printer, actual Excel and PDF output work on target equipment | Printed/PDF/Excel acceptance record | `EXTERNAL_VALIDATION_BLOCKED` | Requires target printer and Microsoft Excel. |
| `REL-011` | Publish accepted commits/releases to the configured GitHub repository | Remote commit/tag/release verification | `NOT_STARTED` | Push only after reviewed phase commits or explicit release authorization. |

## G. Knowledge system and licensing

| ID | Requirement and source | Acceptance evidence required | Initial status | Current evidence or gap |
|---|---|---|---|---|
| `KNOW-001` | Obsidian-compatible repository vault with routing and authority rules (`SRC-KNOW`) | Link validator and retrieval evaluation | `VERIFIED` | `knowledge/`, `AI-CONTEXT.md`, `AI-ROUTER.md` and ADRs are present and validated. |
| `KNOW-002` | Graphify is configured and indexed (`SRC-KNOW`) | Query against current graph and config inspection | `VERIFIED` | Graph exists and returns WPF architecture/document nodes. Generated `graphify-out` remains non-release evidence. |
| `KNOW-003` | CRG SQLite AST index and strict ignore rules (`SRC-FOLLOWUP`) | AST query plus ignore/config inspection | `VERIFIED` | `.code-review-graph`, `.crgignore` and repository instructions exist. |
| `KNOW-004` | Knowledge reflects accepted architecture/rule changes without duplicating task state (`SRC-KNOW`) | Vault validation and spot audit each phase | `IMPLEMENTED_NOT_VERIFIED` | Must be maintained throughout the closure sprint. |
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

This is an initial Phase 0 classification, not a completion result. Counts must be regenerated whenever a row changes.

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
