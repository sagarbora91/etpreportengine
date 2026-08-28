# ETP closure sprint checkpoint — 28 August 2026

Status: **PAUSED BY OWNER AT A CLEAN VERIFIED BOUNDARY**

This checkpoint records the exact state at which the consolidated closure sprint was paused. It is a resume authority, not a product-completion claim.

## Repository identity

- Branch: `ui/uiux-v4-touch-first-redesign`
- Checkpoint commit before this record: `7547f48` (`refactor(desktop): establish composition root and shell state`)
- Governance/baseline commit: `6c4d50f` (`test(desktop): establish closure baseline and architecture ratchets`)
- Non-Graphify working tree at pause: clean
- Generated `graphify-out/` changes are expected cache/index output and are excluded from product commits.

## Completed in this sprint

### Closure control and reproducible baseline

- Established `docs/PROJECT_CLOSURE_TRACEABILITY.md` with 118 active WPF/.NET requirements and strict evidence statuses.
- Established `docs/PENDING_INPUT_AND_DEFERMENT_REGISTER.md`.
- Superseded the stale broad-completion claim in `docs/23_MASTER_PROMPT_COMPLETION_AUDIT.md`.
- Captured build, test, import, SQL availability, performance, export and WPF smoke baselines.
- Added architecture ratchets for lower-layer boundaries, module isolation and direct MainWindow infrastructure construction.

### First composition and shell slice

- Added `DesktopCompositionRoot` as the executable construction boundary.
- Added a framework-neutral, behavior-tested startup coordinator.
- Preserved normal, `--initialize-database` and `--automation-once` behavior.
- Added a feature-neutral `ShellViewModel` for navigation decisions, history and route presentation metadata.
- Changed MainWindow to receive its Shell ViewModel, Dashboard view and connection-aware Dashboard query factory explicitly.
- Moved `ShellNavigationService`, `DashboardView` and `SqlServerDashboardQuery` construction out of MainWindow.
- Updated UI smoke to use the production composition path.
- Refreshed Graphify after the structural change.

## Verification at pause

- Release build: passed with 0 warnings and 0 errors.
- Automated tests: 254 passed, 0 failed, 0 skipped.
  - Domain: 12
  - Import: 40
  - Reporting: 51
  - Desktop: 103
  - SQL Server: 48
- Independent focused verification: 47/47 passed.
- Headless WPF smoke: all 11 production-shell views rendered; 190 accessible named elements.
- Startup behavior tests cover exact/case-sensitive arguments, no-window headless paths, exit codes, exceptions, diagnostic labels and exactly one interactive window request.
- Independent verifier verdict: commit-ready; no P0, P1 or P2 code findings.

## Architecture state at pause

The modular architecture is not complete.

- MainWindow direct SQL-infrastructure constructions: 82, reduced from the 83-construction ratchet baseline.
- `ARCH-003` / DESK-011 remains `IN_PROGRESS` until all feature construction moves to composition/adapters.
- Reports, Daily Workflow, Manual Entry, Imports, Source Inbox, Archive, Settings, Accounting, Operations, Approvals and Administration still require workspace extraction.
- MainWindow feature panels, fields, handlers and partial files remain until their owning modules pass regression gates.

## Requirement ledger state

| Status | Count |
|---|---:|
| `VERIFIED` | 8 |
| `IMPLEMENTED_NOT_VERIFIED` | 69 |
| `IN_PROGRESS` | 14 |
| `NOT_STARTED` | 3 |
| `OWNER_INPUT_BLOCKED` | 6 |
| `SOURCE_DATA_BLOCKED` | 2 |
| `EXTERNAL_VALIDATION_BLOCKED` | 9 |
| `OWNER_APPROVED_DEFERRED` | 3 |
| `NOT_APPLICABLE_LEGACY` | 4 |

These counts total the 118 active ledger rows. The overall product must not be described as complete at this checkpoint.

## Work deliberately not started after the checkpoint

- Settings/connection-state extraction
- Windows-access Application contract and SQL adapter
- Remaining workspace extraction
- Help/shortcut closure
- Visual renderer/export corrections
- Final live SQL, installer, recovery, accessibility, security and performance acceptance
- Owner/source-data decisions
- Runtime licensing and production activation

## Exact resume point

Resume with the **Settings, connection state and Windows access** slice:

1. Query Graphify and verify source/tests for `ConnectionStringInput`, local settings persistence, database health/bootstrap and `Phase2OperationsRepository.LoadCurrentAccessAsync`.
2. Create a tested Desktop settings store with atomic path-safe persistence and integrated-security validation.
3. Create an Application access-session contract and a SQL adapter without moving role decisions into the UI.
4. Inject both through `DesktopCompositionRoot`.
5. Replace MainWindow settings/access construction and UI-control-as-dependency behavior.
6. Reduce the architecture ratchet; build; run all tests; run UI smoke; obtain independent verification; update the ledger; commit separately.

Do not skip directly to Reports or Imports. Do not begin runtime licensing until the functional/modular closure gate and explicit Owner authorization are both present.

## Resume safety check

Before changing code after this pause:

1. confirm the branch and commits above;
2. preserve unrelated working-tree changes;
3. treat Graphify cache churn separately;
4. run `dotnet build Etp.Reporting.slnx --configuration Release --no-restore`;
5. run the focused Desktop architecture/startup tests;
6. compare results with this checkpoint;
7. keep the overall closure objective open.
