# ETP review: scope and acceptance boundary — 11 September 2026

This is a current review ledger, not a release approval. The final executable and installer must be labelled **engineering candidate** until the required observed workflows and installed-environment acceptance are complete. Passing automated tests or rendering every route does not establish those results.

## Refreshed inventory

The companion [coverage CSV](ETP-2026-09-11-COVERAGE.csv) gives an explicit disposition to every inventory entry. The inventory was confirmed against rebuilt assemblies at `verification/review-2026-09-11/final/function-coverage.json` during the intermediate integrated audit completed at 2026-09-11T13:53:40Z. The earlier baseline-only assembly snapshot is superseded. Later source fixes require a final rerun; intermediate outcomes are explicitly marked RETEST_PENDING in the CSV.

| Kind | Total entries | Active | Unavailable |
|---|---:|---:|---:|
| Modules | 9 | 9 | 0 |
| Navigation functions | 118 | 111 | 7 |
| Product reports / executable report routes | 29 | 29 | 0 |
| Workspace destinations | 14 | 14 | 0 |
| Approved import profiles | 6 | 6 | 0 |
| Startup modes | 3 | 3 | 0 |
| Public Application operations | 72 | 72 | 0 |
| **Total** | **251** | **244** | **7** |

These are product-inventory entries, not all C# methods. Help, backup, automation and settings are represented through navigation and Application operations; they are not additional workspace destinations. Report catalogue entries and report navigation shortcuts intentionally remain separate inventory entries.

The source inspected for registry/routing status is `UiNavigation.cs`, `WorkspaceModuleOwnershipRegistry.cs`, `ShellNavigationService.cs`, `DesktopStartupCoordinator.cs` and `ReportDefinition.cs`. `tools/Etp.Reporting.FunctionAudit/Program.cs` was inspected to understand inventory reflection and fixture creation. A static registry inspection establishes an entry and its declared route or policy; it does not review every implementation behind it. Public operations without individually reviewed implementations remain `INVENTORIED_UNVERIFIED`.

The generator's `Evidence` arrays are **assigned evidence categories**. Its `uncoveredCount = 0` means all active entries received a category, not that each was exercised. In particular it assigns `dotnet-tests` broadly, `live-sql-reporting` to every report, and `ui-all-workspaces` to some operations without proving corresponding assertions. The coverage CSV preserves assigned layers separately from actual verification. Historical 601-test and 54-render figures were not copied forward as current passes.

## Route map

All 14 destination rows are present in the CSV: Home, Dashboard, Daily Workflow, Manual Entry, Import ETP, Sales Reports, Stock Reports, Registers, Accounting, Operations Center, Report Archive, Masters, Settings and Admin / Settings.

All 29 report rows are present: `dsr`, `sales-titan`, `sales-helios`, `sales-combined`, `invoice`, `sales-returns`, `sales-brand`, `sales-segment`, `sales-item`, `stock-closing`, `stock-physical`, `stock-variance`, `stock-movement`, `stock-group`, `stock-brand`, `stock-slow`, `staff`, `tender`, `cash`, `tender-diagnostic`, `service`, `exceptions`, `exception-source`, `exception-unmapped`, `exception-stock`, `exception-staff`, `exception-tender`, `management-trend`, `invoice-lineage`. Source maps these report feature codes to the Sales Reports destination; a rendered route is not evidence that its underlying database query or export was exercised.

## Role journeys and state coverage

The refreshed [role UAT register](ETP-2026-09-11-ROLE-UAT.csv) carries forward the 85 curated scenarios and their three role expectations, with **NOT RUN** results until new observed evidence exists. Scenario definitions are reusable acceptance requirements; old executions are not carried forward. Three expected-role columns do not imply that 255 role executions have occurred.

| Area | Scenarios |
|---|---:|
| Reports | 29 |
| Imports | 10 |
| Daily Workflow | 6 |
| Installer | 5 |
| Startup, Access, Registers, Accounting, Operations, Accessibility | 3 each |
| Archive, Administration, Recovery, Privacy/Security | 2 each |
| Dashboard, Source Inbox, Report UX, Export, Sharing, Approvals, Settings, Hardware, Final gate | 1 each |

Each active function still needs the applicable normal, empty, invalid, loading, failed, restricted and locked states. Review store/date changes, repeat submissions, cancellation, navigation and unsaved changes with function-specific evidence. A hidden or disabled control is insufficient permission evidence. Separate Windows identities must exercise allowed and denied mutations without altered database state on denial.

The required connected journey remains: synthetic import → validation → correction → report → Excel/PDF export → archived generation/reopen. Backend smoke coverage of individual stages is not a substitute for observing the complete WPF journey. Keyboard focus, details/lineage discovery and recoverable errors require interaction evidence in addition to screenshots.

## Deferred scope

The seven runtime-unavailable navigation entries remain unavailable with their source reasons: Category-wise Sales (approved category master), Sell-through and Stock Turn (approved purchase/receipt source), Days of Cover (approved replenishment policy), Courier Register (schema not configured), Approved Direct Posting (no authority), GST Return Assist (approved tax policy/source contract). These do not count as delivered active functionality.

Runtime licensing, Microsoft registration/allowlist and production licensing keys remain separately Owner-deferred under `DEF-001`–`DEF-003` in `docs/PENDING_INPUT_AND_DEFERMENT_REGISTER.md`. No runtime enforcement or key ceremony is authorized by this review. Unknown tenders, source-signed returns, missing values and other pending business/source inputs retain their approved rules and fail-closed treatment.

## Release recipe and version policy

`Directory.Build.props` supplies the Windows version; `scripts/set-release-version.ps1` updates it. The reviewed baseline was 1.8.5. The integrator selected fresh patch candidate **1.8.6** for this defect-fix review, preserving earlier 1.8.4/1.8.5 candidates. `scripts/build-windows-release.ps1` builds/tests and publishes self-contained win-x64; `scripts/build-windows-installer.ps1` compiles the Inno installer. `build-production-release.ps1` is legacy Android and is not the Windows release recipe.

Baseline packaging hazards were reported to the integrator: the release builder removed an existing output directory recursively; the installer helper selected the newest executable and used a fixed payload path/version. Candidate production must use a new versioned output, explicit source/payload identity and matching installer version. Final verification must inspect the revised scripts and their regression results.

Before candidate handoff, bind executable/installer SHA-256, embedded version, exact source commit, build time, migration file hashes and bundled dependency inventory. Verify packaged startup separately from source-run startup. A rebuild invalidates artifact-bound acceptance. Require a clean committed application source state, explain preserved unrelated repository changes, and never infer cleanliness from a short commit string in `release.json`.

The repository's production gate additionally requires matching SBOM/provenance/offline bundle, Authenticode signature, clean-PC lifecycle matrix, three-role observed UAT, hardware/accessibility/integration checks, live operational recovery, business/operations/release sign-off and explicit promotion authority. This task does not authorize push, tag or publication.

## Available isolated verification and precise limits

The function audit runner generates a uniquely named `EtpReportingFunctionAudit_...` SQL database, validates its name and drops only that database in `finally`. Its synthetic oracle is independently stated as 12 workbooks, stores WLMHW/HEMW, 6 sales rows, 6 invoice controls, net sales 4,600, signed units 4, eligible tender total 4,600, 2 quarantined tender rows, 4 stock movements, 4 closing rows/quantity 60 and 6 rows of each R003/R013 enrichment. No production rows are needed.

Read-only environment discovery on 11 September found no `WindowsSandbox.exe`, `vmrun.exe`, `VBoxManage.exe` or `Get-VM` command; `C:\Windows\System32\WindowsSandbox.exe` was absent and `Win32_ComputerSystem.HypervisorPresent` was false. There is no discovered ready isolated Windows VM for installer lifecycle testing. This is a statement about available facilities, not a claim that virtualization could never be installed.

**Do not substitute an alternate installer directory for a VM.** The inspected installer always runs `bootstrap-etp-prerequisites.ps1`; deselecting SQL prerequisites only skips SQL installation. The bootstrap targets `.\SQLEXPRESS`, `EtpReporting` and production ProgramData roots. Running it on this host could touch existing operational state. Payload inspection, isolated process launch and a disposable application SQL database are safe independent checks, but do not prove installation, repair, upgrade, rollback, uninstall preservation or reboot behavior.

The clean environment matrix therefore remains NOT RUN: SQL absent/present, offline prerequisites, controlled prerequisite failure, repair, supported-version upgrade, uninstall preservation, service/task reboot and reconnection. No destructive production test is warranted to close it.

UI smoke source uses reflection to set simulated roles and navigate, then renders WPF content. That is useful render/navigation evidence; it does not verify real Windows identity authorization, user input, keyboard operation or end-to-end mutations. Its baseline dimensions include 960×600, 1366×768 and one 1920×1080 home view. A bitmap rendered at a size is not Windows 125%/150% display scaling. Record tested screenshot dimensions and actual scaling independently; Narrator, real touch, printer and Microsoft Excel checks remain unverified unless separately observed.

## Intermediate integrated evidence (retest required after later fixes)

The audit at `verification/review-2026-09-11/final/audit-summary.json` ran from **2026-09-11T13:52:17Z to 13:53:40Z** and passed all ten steps. The logs record **608 tests passed, zero failed/skipped**, 12 synthetic workbooks imported with 42 persisted evidence rows, 15 migrations, duplicate/conflict routing, a locked daily workflow, report pack, tender/stock controls and CHECKSUM backup, VERIFYONLY, full restore and lineage comparison. Cleanup reports that the disposable audit database was removed. This is useful backend workflow evidence and does not close the observed role-UAT register.

UI smoke recorded 11 baseline views, 14 workspace routes and 29 report routes (54 images), with 279 automation-named elements. CSV report and destination rows record render-only evidence; the six import-profile rows record limited preflight/live-import behavior. Passing the entire test suite is retained as aggregate evidence and is not assigned as a function-specific pass to all 244 functions. Additional source fixes were underway after this run, so final acceptance must rerun the integrated audit and refresh these timestamps.

## Gate decision

Keep **BLOCKED_PENDING_EXTERNAL_ACCEPTANCE** until observed requirements above are satisfied. Unresolved S1 Critical or S2 High defects prohibit release; S3/S4 need disposition. Even zero known unresolved high defects does not make unexecuted workflows pass. Final integrated audit, fixed-issue retests and packaged evidence must be linked by the integrator without upgrading unobserved rows in this ledger.

