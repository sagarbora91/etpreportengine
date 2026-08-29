# ETP Reporting Engine — external acceptance evidence pack

Status: **READY FOR EXECUTION; NO EXTERNAL GATE IS PASSED BY THIS TEMPLATE**

This pack records the external evidence still required after the verified 28 August 2026 engineering checkpoint. It does not replace the procedures in the deployment, recovery, accessibility, mapping, administrator or user documentation. Complete one copy for each machine, role or source sample as indicated. Never paste confidential report rows, credentials, certificate secrets or private keys into this file.

## Candidate identity

| Field | Recorded value |
|---|---|
| Version | 1.8.4 engineering candidate |
| Base checkpoint commit | `08e39c889fe2a13b09aa33ffa788df63f0b800fd` |
| Exact committed source identity | `8c8d57e37a26fcd8a9a145ac166b34ac952c8b4b` |
| Application SHA-256 | `73C615C1EA9A943A74893CE8BE6C4CFDF28796B4BD806902A7EA3A5A014A2B37` |
| Installer SHA-256 | `67916955FDE3CDD8BB92075023C4108509FF4102966E691DFB95786092B26AFC` |
| Offline package SHA-256 | `0A1C21DFB9252D77D0DA23EBF5632633B83DE6BC2F7639DE609C42478C023495` |
| Signature state | Unsigned |

Before testing, independently calculate the artifact hash and record the exact matching candidate. This 1.8.4 package was built from the clean committed source recorded above. Do not rebuild between formal acceptance and promotion.

## Result vocabulary

Use only `PASS`, `FAIL`, `BLOCKED`, or `NOT RUN`. A pass requires retained evidence. A blocked or failed row must name the evidence or decision needed; blank rows are not passes.

## A. Clean-PC bootstrap and installer record

Authoritative procedure: [Deployment](../10_DEPLOYMENT.md) and [Windows Quick Start](../14_WINDOWS_QUICK_START.md).

Run on fresh supported x64 Windows snapshots covering SQL Server absent with internet, SQL Server already present, controlled prerequisite failure, and offline/no-internet installation. Do not use a development PC with preinstalled prerequisites as clean-PC evidence.

| Field | Evidence |
|---|---|
| Tester and date/time | |
| Machine/VM identifier | |
| Windows edition, version and build | |
| Snapshot state and internet state | |
| Elevation/administrator context | |
| Candidate path and verified hash | |
| SQL state before installation | |
| Setup-log location | |

| Check | Result | Evidence/reference |
|---|---|---|
| Elevated bootstrap launches and consent is clear | NOT RUN | |
| Missing SQL Server Express and `sqlcmd` are detected and installed | NOT RUN | |
| Existing supported prerequisites are reused safely | NOT RUN | |
| `SQLEXPRESS` instance is created/configured with required permissions | NOT RUN | |
| `MSSQL$SQLEXPRESS` is running and Automatic | NOT RUN | |
| Database initialization/migrations complete | NOT RUN | |
| Application connects using the installed configuration | NOT RUN | |
| Registered backup, drill and automation tasks match policy | NOT RUN | |
| Reboot preserves automatic SQL startup and application reconnection | NOT RUN | |
| Prerequisite failure is understandable and setup rolls back safely | NOT RUN | |
| Same-version repair succeeds without data loss | NOT RUN | |
| Supported upgrade, when a prior candidate is available, preserves data | NOT RUN | |
| Uninstall removes application/tasks but preserves database and backups | NOT RUN | |
| Offline package behaves correctly without internet | NOT RUN | |

Overall result: `NOT RUN`

## B. Live backup and recovery-drill record

Authoritative procedure: [Operations and Recovery](../16_OPERATIONS_AND_RECOVERY.md), [Deployment](../10_DEPLOYMENT.md), and [Test Strategy](../09_TEST_STRATEGY.md).

Use a real, non-confidential test database. A drill restores to an isolated database or instance and never overwrites production. Operational database backups are not automatically deleted; operational audit history is retained for two years. The documented recovery-drill cadence is monthly unless an approved decision changes it.

| Field | Evidence |
|---|---|
| Operator and date/time | |
| Host/instance and test database | |
| Backup destination and free capacity | |
| Backup filename, size and SHA-256 | |
| Restore database/instance | |
| Start/end time, observed RPO and RTO | |

| Check | Result | Evidence/reference |
|---|---|---|
| Full checksum backup is created | NOT RUN | |
| `RESTORE VERIFYONLY` succeeds | NOT RUN | |
| Isolated restore succeeds | NOT RUN | |
| `DBCC CHECKDB` succeeds | NOT RUN | |
| `import_files` and `source_lineage` counts reconcile | NOT RUN | |
| Approved privacy-safe control totals reconcile | NOT RUN | |
| Application connects to the restored database in a controlled test | NOT RUN | |
| Daily backup schedule creates an observed backup | NOT RUN | |
| Monthly recovery-drill schedule is observed | NOT RUN | |
| Backup-age, capacity and failure warnings are observed | NOT RUN | |
| Failure notification/escalation reaches its approved destination | NOT RUN | |
| No operational-backup pruning or business-data deletion occurs | NOT RUN | |
| Privacy-safe audit evidence is retained without source rows | NOT RUN | |

Overall result: `NOT RUN`

## C. Production hardware and accessibility record

Authoritative procedure: [Accessibility Audit](../19_ACCESSIBILITY_AUDIT.md) and [User Manual](../18_USER_MANUAL.md).

| Field | Evidence |
|---|---|
| Tester and date/time | |
| Device, Windows version/build | |
| Resolution, scaling and text size | |
| Touch/keyboard details | |
| Printer model/driver | |
| Microsoft Excel version | |

| Check | Result | Evidence/reference |
|---|---|---|
| PDF output opens and matches the accepted report presentation | NOT RUN | |
| Physical print is legible, complete and correctly paginated | NOT RUN | |
| Excel export opens correctly in actual Microsoft Excel | NOT RUN | |
| Supported resolutions and 100%, 125%, 150% and 200% scaling are usable | NOT RUN | |
| Comfortable and Compact sidebar density modes persist and remain usable | NOT RUN | |
| Report workspace avoids excessive tab or nested scrolling | NOT RUN | |
| Touch targets and scrolling are usable | NOT RUN | |
| Tab/Shift+Tab order and visible focus are correct | NOT RUN | |
| Enter, Space, Escape, Alt+Left and supported copy/select shortcuts work | NOT RUN | |
| Narrator announces labels, state, progress, errors, headers and context | NOT RUN | |
| High-contrast presentation remains understandable | NOT RUN | |

Overall result: `NOT RUN`

## D. Role-based business UAT record

Execute separately as Owner, Store Manager and Viewer using target-PC identities. Record denied actions as well as successful ones. Accounting, control and mapping rules must not be changed merely to obtain a pass.

| Field | Evidence |
|---|---|
| Business tester, role and Windows identity | |
| Date/time and machine | |
| Candidate hash | |
| Non-confidential test-data scope | |

| Scenario | Owner | Store Manager | Viewer | Evidence/reference |
|---|---|---|---|---|
| Navigate all permitted modules and separate report workspaces | NOT RUN | NOT RUN | NOT RUN | |
| Select period; preview; search; sort; filter; variance-only; drill down | NOT RUN | NOT RUN | NOT RUN | |
| Export permitted PDF/Excel outputs | NOT RUN | NOT RUN | NOT RUN | |
| Manual Entry preserves missing versus zero, reason and history | NOT RUN | NOT RUN | NOT RUN | |
| Locked-day mutation is denied; authorized reopen is audited | NOT RUN | NOT RUN | NOT RUN | |
| Import file, folder and ZIP; retry/cancel; inspect failure summary | NOT RUN | NOT RUN | NOT RUN | |
| DSR FTD/MTD/YTD, TY/LY, quantities, Service and targets are accepted | NOT RUN | NOT RUN | NOT RUN | |
| Missing LY MTD shows `— / —` and `LY MTD source required` | NOT RUN | NOT RUN | NOT RUN | |
| Revenue/tender and signed-negative Sales Return controls reconcile | NOT RUN | NOT RUN | NOT RUN | |
| Store, brand-segment and transaction mappings behave as approved | NOT RUN | NOT RUN | NOT RUN | |
| Administration, mapping approval, reopen and restatement enforce roles | NOT RUN | NOT RUN | NOT RUN | |

Business comments or rejected expectations:

- None recorded.

Overall result: `NOT RUN`

Sign-off requires printed or controlled electronic identity, name, role, date and an explicit acceptance/rejection statement. An engineering operator must not sign on behalf of a business tester.

## E. Source-mapping intake record

Authorities: [Mapping Register](../05_MAPPING_REGISTER.md), [Report-to-Source Matrix](../22_REPORT_TO_SOURCE_MATRIX.md), and [Pending Input and Deferment Register](../PENDING_INPUT_AND_DEFERMENT_REGISTER.md).

Create one row per missing field or supplied source. Do not infer uncertain financial mappings or include confidential customer rows.

| Input ID | Desired report/location | Source report/sheet/column | Redacted sample value | Imported/derived/manual/unavailable | Formula and zero/null behavior | Owner approval/evidence |
|---|---|---|---|---|---|---|
| | | | | | | |

Focused tests must be added before production mappings change. Preserve source lineage, manual-entry audit history, mapping version and approving identity.

## F. Windows code-signing record

Do not commit a private key, password, token or production secret. Signing starts only after the final legal publisher identity and certificate/provider are supplied.

| Field | Evidence |
|---|---|
| Legal publisher name | PENDING OWNER INPUT |
| Certificate type/provider | PENDING OWNER INPUT |
| Certificate thumbprint (public identifier only) | |
| Expiry and renewal owner/date | |
| Timestamp service | |

| Check | Result | Evidence/reference |
|---|---|---|
| Exact accepted application executable is signed and timestamped | NOT RUN | |
| Exact accepted bootstrap installer is signed and timestamped | NOT RUN | |
| Authenticode verification reports valid signatures | NOT RUN | |
| Clean-PC setup shows the approved publisher | NOT RUN | |
| Renewal and secret-recovery procedure is controlled and tested | NOT RUN | |

Overall result: `BLOCKED`

## G. Licensing authorization record

Runtime licensing remains Owner-approved deferred. Do not activate startup enforcement from this pack. When explicitly authorized, re-read the licensing specification and test matrix and record the authorization, approved Microsoft tenant/object identities, controlled signing-key ceremony, offline/online behavior, grace periods, clock-tamper behavior, recovery/support paths, and continued access to database recovery and legally required records.

Current result: `BLOCKED — OWNER AUTHORIZATION REQUIRED`

## H. Final gate review

| Gate | Result | Approver and evidence |
|---|---|---|
| Engineering and automated verification | NOT RUN | Reconfirm against the candidate commit |
| Clean-PC bootstrap/installer | NOT RUN | |
| Backup and recovery | NOT RUN | |
| Hardware and accessibility | NOT RUN | |
| Business UAT | NOT RUN | |
| Required source mappings | BLOCKED | See pending-input register |
| Windows signing | BLOCKED | Publisher identity and certificate required |
| Runtime licensing | BLOCKED | Explicit Owner authorization required |
| No-rebuild release promotion | NOT RUN | |

The product may be described as production-approved only when every applicable gate is `PASS`, every blocked business/source item affecting the intended scope is resolved or explicitly accepted, and the promoted artifacts are the exact tested and signed artifacts.
