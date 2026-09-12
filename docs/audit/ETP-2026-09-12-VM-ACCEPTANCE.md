# VM acceptance follow-up — 12 September 2026

Status: engineering candidate; external acceptance remains incomplete.

## Reproduced defect and correction

The single-workbook desktop persistence path did not perform the hash check already used by batch imports. Re-importing the installed test workbook on 1.8.6 produced SQL error 2601 at `UX_import_files_source_sha256`. The transaction rolled back; the VM retained 3 sales rows, net 2300, signed quantity 2, one imported file and three lineage rows.

The coordinator now returns an explicit exact-duplicate no-op before persistence. The workspace displays `This workbook was already imported. No rows were added or changed.` and does not retain evidence a second time. Identical-file restatements are rejected with the existing `RESTATEMENT_DUPLICATE_FILE` code. Database uniqueness remains the final concurrent-write guard; this change does not replace it or claim a concurrent multi-process duplicate UX guarantee.

Two focused regression tests cover no second persistence request and identical restatement rejection. The updated 617-test suite passed. A probe compiled from the actual desktop coordinator and production SQL adapter reproduced the failure before the fix and returned ExactDuplicate=true after the fix inside the VM; before/after database counts and totals matched. This is execution of the production coordinator, not observed pointer interaction with the WPF handler.

Version 1.8.7 identifies the correction; previous 1.8.5/1.8.6 candidates are preserved.

## Automated guest and host evidence

Evidence root: `artifacts/acceptance-lab-20260911/acceptance-session-20260912` (local ignored artifacts).

- `job-01-duplicate-baseline.ps1.log`: 1.8.6-source duplicate SQL failure.
- `job-02-duplicate-fixed.ps1.log`: fixed coordinator no-op and unchanged original VM data.
- `job-04-live-writable-fixtures.ps1.log`: all 12 workbooks / six profiles / two stores; 42 evidence rows; overlap duplicate/conflict routing; report controls; manual explicit-zero controls; daily close/reopen/lock; report generations/archive comparison; unattended duplicate handling; CHECKSUM backup, VERIFYONLY, full restore and lineage comparison passed in a separate synthetic database.
- The first live run (job-03) failed during cleanup because DVD read-only attributes were retained on copied fixture files. Its evidence is retained. The second run used writable copies with unchanged workbook content and a fresh separate database.
- `all-tests.log`: 617 passed, none failed/skipped (12 Domain, 51 Import, 63 Reporting, 195 SQL, 296 Desktop).
- Export, DSR and performance logs: host-side synthetic Excel/PDF/DSR export checks and 250000 sales / 100000 stock / 50000 tender workload passed. These are not Microsoft Excel, printer, real-user, or full-production-volume acceptance.

## Previously observed VM results

Initial prerequisite installation required manual remediation: WinGet Store-source certificate mismatch, an unsupported SQL downloader, and Go Sqlcmd local-pipe timeout. Full Microsoft SQL Express media and ODBC Sqlcmd resolved the test-machine prerequisites. The app originally installed by the user was 1.8.5, verified by executable version. The assistant subsequently upgraded the VM to the hash-verified 1.8.6 with checkpoint and verified SQL backup; all 15 migrations and DBCC passed and original test totals were unchanged.

These observations establish an installation with preinstalled prerequisites, not an unattended clean-prerequisite installation pass. Initial screenshots show positive import, wrong-date rejection, net 2300 and signed units 2. The generic duplicate failure and later unhandled dispatcher event were emitted by 1.8.5; the duplicate defect was independently reproduced on 1.8.6 source. The separate dispatcher event lacks stack details and is not yet reproduced or declared fixed.

## Remaining boundaries

Real Windows role identities, interactive keyboard/Narrator/touch and OS 100/125/150% scaling, Microsoft Excel/printer/email-client integration, representative business-source workbooks and owner sign-off remain unverified. The screen-control helper cannot capture or operate the elevated VM window on this host. Direct PowerShell VM access supports installation and backend tests but does not establish those visual and human workflows. The 85-scenario role UAT register is not blanket-marked passed.

Installer repair/uninstall/reinstall and final installed 1.8.7 evidence will be appended after execution. No production release approval is implied.
