# Phase 5 Archive, sharing and automatic import implementation

24 September 2026. Scope: Phase 5 Archive/sharing and Scheduler/watch-folder rows. This is coding evidence, not installation or external delivery acceptance.

## Implemented

- A single pack list with Open, Excel, PDF, ZIP and Share actions on every row. Excel/PDF/ZIP loads the selected generation directly and verifies its archived document, so no preceding Open or task switching is required. Compare and Restatements filters removed. Global shell consolidation is integrated by the sprint coordinator.
- Active contact picker for sharing, Owner contact editor with New contact action. WhatsApp Desktop uses the `whatsapp://` protocol, a validated international number, and a prepared PDF whose path is copied. The outcome is `HANDOFF_READY`: the user still attaches and sends, and delivery is never claimed.
- Real MailKit 4.18 SMTP sending with configured host/port/TLS/from and attachment size. Port 465 uses implicit TLS; other TLS ports require STARTTLS. No TLS certificate validation bypass. Optional authentication is protected with Windows DPAPI for the current Windows user and host/port/sender, outside SQL and audit. Clearing credentials removes only that protected local credential file.
- Owner-only test-send button uses saved SMTP settings and no attachment. Sending a report or test is an explicit UI action with recipient confirmation. No real external send was performed by this sprint.
- Each report email records an initial attempt before submission, then an append-only final outcome: `SMTP_ACCEPTED`, `FAILED`, or `UNKNOWN`. Acceptance means the server accepted SMTP DATA, not delivery to an inbox. A lost confirmation requires checking the mail server before retrying. History shows the latest state for each attempt and remains scoped to the selected generation. A final-history write failure explicitly tells the user not to blindly retry.
- Automatic import reads installed Windows task state, last run/result and next run separately from saved configuration. Missing/unreadable task information is shown honestly. Saving the enable switch does not claim to install/start a watcher. The unused `poll_minutes` database column and C# values are removed; Windows owns cadence.
- An explicit `sp_releaseapplock` runs before a session connection returns to the pool, even after cancellation. A failed release clears the affected pool. A ZIP's unsupported numbered ETP workbook is `Not needed`, without blocking other supported files. Unknown layouts of supported families still require review in automatic import. Only XLSX/ZIP is scanned; obsolete OCR/image intake is removed from automation.

## Database change

Migration **0034_sharing_and_automatic_import.sql** adds `share_attempts.attempt_key`, observable outcome values, and grants Viewer append permissions for export/share records to match the existing Viewer-authorized Archive boundary. Existing append-only triggers remain. The unused watch-folder poll constraint/default/column are dropped. No committed migration was edited.

## Verification

- Desktop and SQL integration projects build with zero warnings/errors.
- All 250 SQL Server unit tests passed, including existing boundary/folder/operations tests and the new ZIP case.
- 10 email tests passed: fake transport checks access/limits/header injection/history and an actual MailKit transport talks only to an ephemeral loopback SMTP server for accept, refuse and lost-confirmation scenarios. The attachment is synthetic.
- 3 disposable SQL tests passed: generation-scoped final history, append-only enforcement, Viewer append; independent-session lock contention and reacquisition; poll-column removal and watch-settings save.
- 25 targeted Desktop tests passed, 1 optional screenshot test skipped; role-aware task state, retained drafts, archive generation binding and WhatsApp protocol validation included.

Still requiring deployment acceptance: configured real SMTP TLS/authentication test, actual WhatsApp Desktop handoff, installed-task observation under each Windows role, and touch-size review of the consolidated shell. No live database was migrated and no task was installed or triggered.
