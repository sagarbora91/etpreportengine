# ETP closure sprint — Settings, connection and access slice

Status: **IMPLEMENTED AND LOCALLY VERIFIED**

This is a bounded modular-architecture checkpoint. It does not declare the wider closure sprint or product complete.

## Delivered

- Added an injected Desktop settings store with absolute rooted storage, strict Windows-integrated SQL connection validation, atomic same-directory replacement and reparse-point rejection.
- Added a validated connection-state object. Feature workflows now consume that state instead of using `ConnectionStringInput.Text` as a service locator.
- Added the dependency-free Application access-session contract and permission matrix.
- Added a Windows-integrated SQL access adapter over the existing database access repository.
- Injected settings, connection state and the access-query factory through `DesktopCompositionRoot`.
- Migrated role-aware Desktop navigation to the Application access role rather than the SQL infrastructure role.
- Reduced direct MainWindow SQL-infrastructure constructions from 82 to 81 and tightened the ratchet.

## Verification

- Release build: passed, 0 warnings and 0 errors.
- Full automated suite: 289 passed, 0 failed, 0 skipped.
- Focused settings/access/composition/navigation/guardrail checks: passed.
- Headless WPF smoke: 11 views rendered, 190 accessible named elements.
- Settings tests cover missing/corrupt/invalid files, atomic replacement, empty credential aliases, invalid/incomplete connections, connection-state rollback and reparse-point paths.
- Access tests cover active/inactive permission behavior, every known role, unknown-role fail-closed mapping and rejection of SQL authentication.

## Remaining boundary

Settings-screen presentation and the remaining feature workspaces are still owned by `MainWindow`. Archive, sharing and registers are the next lower-risk extraction slice. Reports, Daily Workflow/Manual Entry/DSR, Imports/Source Inbox, Accounting, Operations and Administration follow. The construction inventory must ultimately reach zero before the shell-only architecture gate can close.
