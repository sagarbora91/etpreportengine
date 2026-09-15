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

## Final 1.8.7 installed results

Candidate built from clean source commit `093b5ef92378a1496d8ac9298dddb31f9e86b602`. The release build passed all 617 tests after synchronizing the changelog version; the initial version-consistency failure is retained in `release-build.log`, and the passing rerun is `release-build-r2.log`.

- Executable SHA-256: `B5C74F5415EF1C6DE91C58424ABF131E3DBAD1399567DA152C300FAE8001264B`.
- Installer SHA-256: `9D8CBD9E81F7DB3400829A1BBC41D5B9FBE256978EA76BB9878F92B7B9B7F899`.
- `job-05-installer-lifecycle.ps1.log` and `Lifecycle187/`: upgrade, repair, uninstall, reinstall and the installed recovery drill all passed. Each install verified the executable hash and unchanged original data. Uninstall removed the executable and all three ETP tasks while retaining the original database and SHA-verified backup. Reinstall restored all three tasks. DBCC passed, with 15 applied migrations.
- `job-06-oracle.ps1.log`: separate full-fixture database independently asserted exactly six sales rows, 4600 net, four signed units, -400 returns and 60 closing-stock units.
- `job-09-offline-retry.ps1.log`: offline repair passed with the VM network adapter disconnected, using the already-installed SQL/ODBC prerequisites; the host helper restored the original switch in a finally block. The first offline attempt stopped because the remote session could not gracefully close the test-launched app; the retry stopped only a process ID recorded by the prior automated launch. Both attempts' evidence is retained.
- `job-10-final-state.ps1.log` and `final-state.json`: correct 1.8.7 executable hash; application process present in interactive Windows session 1; SQL running; original database still three sales rows, net 2300, signed units two. Remote process observation is not proof of rendered/interactive UI correctness. No new desktop diagnostic events were observed in the collected log; this does not prove the historical dispatcher defect fixed.

The original application database and verified backups are retained. Separate synthetic acceptance databases are retained in the VM for inspection. Temporary launch tasks were removed. The host-side connection worker was stopped at the end; VM credentials were held in memory only and were not saved. No code was pushed and no production system was installed or changed.

Final disposition: duplicate defect fixed and automated/installed verification passed as scoped above. Do not treat this as completion of the full observed 85-scenario role register, unattended SQL-absent installation, or a production release. The prerequisite download/Go Sqlcmd issues, unreproduced historical dispatcher failure, real role identities, accessibility/scaling/touch, external applications/hardware, representative-source UAT and owner approval remain open.
