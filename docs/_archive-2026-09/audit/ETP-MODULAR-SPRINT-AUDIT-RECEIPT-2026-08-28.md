# ETP modular sprint — audit receipt

Date: 28 August 2026
Scope: current .NET 10/WPF working tree
Purpose: source-backed modular-sprint handoff; **not** a product-completion or release-promotion record

## Implemented architecture received

- The application remains one WPF executable with `DesktopCompositionRoot` as its explicit construction boundary.
- The three remaining `MainWindow` partials total 907 lines and are guarded as shell/workspace-host code. The old Productisation and VisualReporting partials are absent.
- Dashboard, Help, Settings, Reports, Daily Workflow/Manual Entry, Imports, Source Inbox/OCR, Archive/Distribution, Registers, Accounting, Operations/Investigation and Administration have focused Desktop module owners.
- `WorkspaceModuleOwnershipRegistry` maps all shell destinations and production report routes bidirectionally.
- Application contracts and Windows-integrated SQL adapters are the normal feature boundary. MainWindow direct SQL-infrastructure construction is ratcheted to zero.
- Report/export state, visual rendering, import persistence, archive-generation binding and accounting/daily scope state are outside the shell.
- Adapter authorization is fail-closed for import persistence, Source Inbox and Daily Workflow, including Owner-only restatement/reopen paths.

## Confirmed focused evidence

| Evidence | Result | What it supports |
|---|---:|---|
| Desktop architecture/composition/ownership/UI-smoke filter | 25 passed, 0 failed | Shell ceilings, zero direct construction, route ownership, composition and extracted-workspace rendering |
| Desktop integration regression filter | 31 passed, 0 failed | Archive/accounting/daily/import/source state isolation and safe source launch |
| Reporting DSR/visual/classification filter | 20 passed, 0 failed | DSR formulas/export model, visual behavior and complete production-code classification |
| SQL Server project suite | 166 passed, 0 failed | Application/SQL adapters, authorization, integrated authentication and persistence mappings |
| Reporting project suite | 59 passed, 0 failed | Deterministic reporting/export behavior |
| Import project suite | 40 passed, 0 failed | Workbook/profile/import behavior |
| Desktop Release build | 0 warnings, 0 errors | Current production project compiles after extraction |
| Independent extracted-workspace smoke | 2 passed, 0 failed | All 13 extracted surfaces render, measure/arrange, expose authored accessible controls, accept focus and reject duplicate WPF parenting |
| Full Release solution suite | 512 passed, 0 failed | Domain 12, Import 40, Reporting 59, SQL Server 166 and Desktop 235 |
| Web/Android security regression suite | 23 passed, 0 failed | Automatic/private/off-device backup, export, pin, persistence and security invariants |
| Knowledge-vault validation | 20 notes, 0 broken links, 0 stale warnings | Obsidian navigation and engineering evidence remain internally consistent |

The current DSR production-path artifact is `output/pdf/ETP_Daily_Sales_Report_2026-08-25.pdf`; its Poppler preview is `tmp/pdfs/ETP_Daily_Sales_Report_2026-08-25-preview.png`. Independent inspection confirmed one A4 landscape page, `Tuesday` for 25 August 2026, FTD/MTD/YTD, TY/LY, value and quantity, Service and target blocks, `LY MTD source required`, no mockup footer, and no clipping, overlap, missing glyph, currency or excessive-dead-space defect.

The executable visual-classification registry covers every production report code and preserves the seven existing visual definitions. Broad cross-renderer chart-type/series and installed high-DPI/printer acceptance remain open; this receipt does not overstate those gates.

## Security and deployment reconciliation

- New SQL adapters reject SQL/mixed credential strings and require Windows-integrated authentication.
- Import, Daily Workflow and Source Inbox permissions are enforced at adapter boundaries rather than only by disabled UI controls.
- Source-document shell launch requires an existing full path with an allowlisted managed-document extension.
- `artifacts/security-scan.json` reports a successful scan with no .NET/npm vulnerabilities and one unresolved deprecated dependency finding: `xunit` 2.9.3.
- The scanner captures native stderr without weakening its fail-closed policy, allowing its documented system-CA retry to complete the npm registry audit.
- Installer/bootstrap, database initialization, backup/verify, monthly restore drill, lifecycle/rollback and task-removal paths are implemented in source. Clean-machine, service-reboot, real backup/restore, signed-publisher and installed-client evidence remain external gates.

## Lead Integrator source-verification closure

- `dotnet build Etp.Reporting.slnx -c Release --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test Etp.Reporting.slnx -c Release --no-build`: 512 passed, 0 failed, 0 skipped.
- `npm run test:security`: 23 passed, 0 failed.
- Graphify 0.9.48 refreshed the repository graph to 12,739 nodes, 27,038 edges and 602 communities.
- Code Review Graph performed a full 427-file rebuild: 5,652 AST nodes, 44,851 edges, 634 flows and 23 communities, with no parser errors. Its high risk score reflects the deliberately broad sprint; the highlighted access, manual-input and reopen paths are covered by the SQL and Desktop authorization suites above.
- Release-content hygiene passed for all 120 intended non-Graphify paths: no secrets, real PII, machine-specific paths, generated binaries, temporary output or orphaned WPF views were found, and `git diff --check` passed.

## Explicitly reserved for Lead Integrator closure

This receipt deliberately does **not** record the following as passed or complete:

- installer, offline-package, SBOM/manifest or release-artifact hashes;
- the final reviewed commit SHA, tag, GitHub push or release URL;
- clean-VM installer/upgrade/repair/uninstall evidence;
- live target-PC SQL roles, backup/restore drill, printer/Excel, Narrator/touch or business-owner UAT;
- code-signing and Windows publisher trust.

The Lead Integrator must bind those results to the exact reviewed commit and promoted artifacts. Until then, affected closure-ledger rows remain `IMPLEMENTED_NOT_VERIFIED`, `IN_PROGRESS` or explicitly blocked.

## Remaining non-engineering/source gates

The existing `docs/PENDING_INPUT_AND_DEFERMENT_REGISTER.md` remains current. This sprint did not resolve the Owner business decisions, missing populated ETP sources, external production validation or Owner-approved licensing deferment recorded there.
