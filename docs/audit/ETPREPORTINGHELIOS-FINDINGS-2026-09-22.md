# EtpReportingHelios: what it is and what to do with it

22 Sep 2026. A read-only investigation on `DESKTOP-6IBM1J5`, SQL instance `.\SQLEXPRESS`.
Nothing was dropped, backed up or altered on purpose. One automatic side effect of my queries
is explained in section 9. The decision is yours.

This closes the fact-finding for Phase 4 item 4 in `WORKING-STATE-2026-09-22.md`
("decide `EtpReportingHelios` (at `0020`): live or leftover").

---

## 1. Short answer

**`EtpReportingHelios` is the Phase 1 import-audit database. It is not a live shop database.**

- It was built on **15 Sep 2026 between 16:22 and 16:31 (India time)** from the two-year
  Helios (HEMW) ETP workbooks. The purpose was to prove the whole-folder import (Phase 1
  acceptance A1.9a and A1.11). Its counts match that acceptance exactly: 759 invoices,
  790 sales lines and 3,955 stock-ledger rows.
- **Nothing has written to it since 16:31 on 15 Sep.** SQL Server's log shows nobody opened it
  between 17 Sep 21:13 and my first query today.
- The app's `settings.json` points at **`EtpReporting`**, not at this database.
- It is **12 migrations behind** (at `0020`; the build has `0032`) and has **never been backed up**.

It does hold something the live database does not: **two years of Helios sales and stock
history (16 Sep 2024 to 6 Sep 2026)**. That includes 673 invoices that are not in `EtpReporting`.

**Recommendation: archive it with a verified backup, then drop it.** First do the two checks in
steps 1 and 2 of section 8. Reasons are in section 7.

---

## 2. Facts

India time (IST) is UTC + 5:30. SQL Server's log and `create_date` use IST. Columns ending in `_utc` use UTC.

| Fact | Value | Where it came from |
|---|---|---|
| Created | **15 Sep 2026 16:22:02 IST** (10:52:02 UTC) | `sys.databases.create_date` |
| Owner | `DESKTOP-6IBM1J5\Sagar` | `sys.databases.owner_sid` |
| State | ONLINE, MULTI_USER, read-write | `sys.databases` |
| Recovery model | SIMPLE | `sys.databases` |
| Auto-close | ON: the database closes itself when nobody is connected (EtpReporting is the same) | `sys.databases.is_auto_close_on` |
| Size on disk | data file 72 MB (47 MB used) + log file 72 MB (about 1 MB used) = **144 MB** | `sys.master_files`, `FILEPROPERTY` |
| Files | `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\EtpReportingHelios.mdf` and `EtpReportingHelios_log.ldf` | `sys.master_files` |
| Migration level | **`0020_remove_document_extraction`**: 20 migrations, all applied 15 Sep 2026 10:52:04–10:52:09 UTC | `dbo.schema_migrations` |
| Migrations it lacks | **`0021` to `0032`** (12). EtpReporting lacks only `0032`. | compared with `database/migrations/*.sql` |
| Migration checksums | All 20 match the current files in a form the migration runner accepts (`Migrations.cs` `MigrationChecksum.Matches`), so it could still be upgraded | SHA-256 of each worktree file, LF, as-is and CRLF forms |
| Stores with data | **HEMW (Helios) only.** Both store rows exist, but WLMHW has no data rows. | `stores`, `sales_invoices`, `import_files`, stock tables |
| Sales | **759 invoices, 790 lines, 950 tenders**, dated **16 Sep 2024 to 6 Sep 2026** | `sales_invoices`, `sales_lines`, `sales_tenders` |
| Sales value | NETAMOUNT (GST-inclusive, the D1 reporting value) **12,850,882.24**; NETVALUE (ex-GST) 10,904,673.38; tax 1,946,208.86; tenders 12,846,376.24 | `sales_lines`, `sales_tenders` |
| By financial year | FY24-25 (from 16 Sep 2024): 152 invoices, NETAMOUNT 2,252,826.50. FY25-26: 383 invoices, 6,624,673.95. FY26-27 (to 6 Sep 2026): 224 invoices, 3,973,381.79. | `sales_invoices`, `sales_lines` |
| Invoice identity | `invoice_year` 2025–2027 (financial-year-end year); **0 CONFLICT rows**, so the IF-012 fix is present | `sales_invoices`, `import_row_outcomes` |
| Stock | 3,955 stock movements (16 Sep 2024 to 6 Sep 2026); 935 closing-stock rows: 469 for 25 Aug and 466 for 7 Sep 2026 | `stock_movements`, `stock_snapshots` |
| Other ETP report families | 26 family tables hold rows (R003–R030, landing R004–R028, SOR ageing), 24,626 rows in all | `etp_r0xx`, `etp_landing_*`, `etp_import_content` |
| Imports | 58 batches, all Completed; 58 files, 58 different SHA-256 hashes; 18 marked superseded. Row outcomes: 23,979 NEW, 1,597 ALREADY_PRESENT, 0 CONFLICT. Imported by `Sagar`. | `import_batches`, `import_files`, `import_row_outcomes` |
| Where the files came from | Files 1–31 (names like `R025_SDB_VariantwiseSales.xlsx`, imported 16:22–16:24 IST) fit the Phase 1 report's 31-file `till 6 sep 26` set. Files 32–58 (names like `202608252107_SDB-VariantwiseSales - SDB-VariantwiseSales.xlsx`, imported 16:31 IST) fit its older `HELIOS ALL REPORT 01 JULY 2026 TO 25 AUG 2026` export. The database stores file names, not folders. | `import_files.original_file_name`, `import_batches` |
| **Latest import** | **15 Sep 2026 11:01:22 UTC (16:31:22 IST)** | `import_batches.completed_utc` |
| Latest write of any kind | The same moment. No date or time column in any table holds a later value. | the latest value of every `*utc*`/`*modified*`/`*created*`/`*run*` date column |
| Application users | 2: `DESKTOP-6IBM1J5\Sagar` (Owner) and `NT AUTHORITY\SYSTEM` (Store Manager), both set up 15 Sep 10:52 UTC | `application_users` |
| Database users | `NT AUTHORITY\SYSTEM` has CONNECT, `db_datareader`, `db_datawriter` and `db_backupoperator` here. Migration 0022, which removed SYSTEM's CONNECT in EtpReporting, was never applied here. | `sys.database_principals`, `sys.database_permissions` |
| Staff | 18 HEMW staff rows, loaded by the import on 15 Sep | `staff` |
| Work entered by you | **None.** 0 manual inputs, stock counts, targets, adjustments, finalised days, report packs, accounting batches or register entries | row counts |
| Audit trail | 33 events, all 15 Sep 10:52–11:01 UTC: 1 user setup and 32 import-profile registrations. The actor is your Microsoft account. | `operational_audit` |
| Backups ever taken | **None** in `msdb` backup history (history starts 26 Aug 2026) | `msdb.dbo.backupset` |
| SQL Server last started | 17 Sep 2026 14:05:58 IST | `sys.dm_os_sys_info.sqlserver_start_time` |
| Index usage since start | No rows, for this database or for EtpReporting. Auto-close throws these counters away every time a database closes, so this check cannot answer the question. | `sys.dm_db_index_usage_stats` |
| Opened since SQL started | 17 Sep **14:11, 21:11, 21:13** IST, then not again until my query at 22 Sep 19:04 IST. My query had to start the database up, so it was closed and nobody was connected. The log does not record who opened it on 17 Sep, or why. | SQL Server error log ("Starting up database"), read with `xp_readerrorlog` |
| `settings.json` | `Data Source=.\SQLEXPRESS; Initial Catalog=EtpReporting` (file last written 17 Sep 2026 18:16:57) | `%LOCALAPPDATA%\EtpReporting\settings.json`; only these two names were read out |
| Scheduled tasks | The Monthly Recovery Drill (visible without elevation) does not mention it. **The two SYSTEM tasks could not be read without elevation** (step 1 checks them). | `Get-ScheduledTask`, `schtasks /query` |
| `operations.json` | Not readable without elevation (step 1 checks it) | `%ProgramData%\EtpReporting\Operations\operations.json` |
| Other references | The broker `master.dbo.etp_operations_31736fb143c6912a` names EtpReporting, not Helios. No module in EtpReporting mentions it. No linked servers or synonyms. | `sys.sql_modules`, `sys.servers`, `sys.synonyms` |

---

## 3. Timeline, from SQL Server's own log

Every time an auto-close database is opened, SQL Server writes "Starting up database" to its error log.
That makes the log a record of when anything first connected after a quiet spell.

| When (IST) | What the log shows | What it means |
|---|---|---|
| 15 Sep 14:56 | Opened three times | Probably an earlier database of this name (the IF-012 one, which silently dropped 272 rows). It no longer exists. |
| 15 Sep 15:11 to 15:14 | SQL Server stopped and started twice (new log files; the last starts at 15:14:01) | |
| 15 Sep 15:20 | Opened twice | Nothing that survives; the database was dropped and recreated later that afternoon |
| 15 Sep 15:36 to 16:22 | "Setting database option SINGLE_USER" six times (15:36, 15:37, 15:38, 15:45, 16:14, 16:22), each followed by a fresh start-up. Also plain openings at 15:37, 15:40, 15:41, 16:17, 16:18 and 16:19 | The same drop-and-recreate pattern the audit tool's `--rebuild` uses, six times |
| **15 Sep 16:22:02** | The current database created | Migrations 0001–0020 applied 16:22:04–16:22:09 |
| 15 Sep 16:22:24 to 16:24:00 | (import tables) | The first 31 files imported |
| 15 Sep 16:26:43 | Opened | Nothing written that carries a timestamp (this fits the Phase 1 "repeat" import, which adds zero rows; not proven) |
| **15 Sep 16:31:10 to 16:31:22** | Opened, then (import tables) | The other 27 files imported. **This is the last write.** |
| 15 Sep 16:32:40 | Opened | Nothing written |
| 17 Sep 11:42 to 12:41 | The log ends at 11:42:05; a new log starts at 12:41:31 (SQL Server was stopped and started again) | Any connection still open from 15 Sep ended here. Nothing opened Helios between 12:41 and 14:01. |
| 17 Sep 14:05:58 | SQL Server restarted again (the previous log ends at 14:01) | |
| 17 Sep 14:11, 21:11, 21:13 | Opened three times | Nothing written. Probably the 17 Sep session checking its migration level; not proven. |
| 22 Sep 19:04:08 | Opened | My first query |

That sequence matches the Phase 1 report's command list (`--rebuild`, import `till 6 sep 26`,
repeat, then import the older Helios export). The database itself does not record which tool
built it, so "built by the import-audit tool" is an inference, not a recorded fact.

---

## 4. How it compares with EtpReporting

| | EtpReportingHelios | EtpReporting (live) |
|---|---|---|
| Created | 15 Sep 2026 | 26 Aug 2026 |
| Migration level | `0020` | `0031` |
| Stores with data | HEMW | HEMW and WLMHW |
| Import files | 58 (all HEMW) | 8 (4 HEMW, 4 WLMHW) |
| Sales dates | HEMW 16 Sep 2024 – 6 Sep 2026 | HEMW 2 Jul – 25 Aug 2026; WLMHW 1 Jul – 25 Aug 2026 |
| Invoices / lines / tenders | 759 / 790 / 950 | 490 / 540 / 592 (HEMW 86 / 89) |
| Stock movements / closing-stock rows | 3,955 / 935 | 1,079 / 1,256 |
| Other ETP report families | 26 tables with rows | all empty |
| Daily-workflow records (reporting days / report generations / audit events) | 0 / 0 / 33 (setup and import only) | 11 / 6 / 262 |
| Closing stock for 25 Aug 2026 (HEMW) | 469 rows | 469 rows (the same file) |

**The same source files:** 4 of Helios's 58 files are also in EtpReporting, identical by SHA-256 and by
name. They are the HEMW 25 Aug exports: `202608252100_Closing Stock`, `202608252106_Revenue Report`,
`202608252107_SDB-VariantwiseSales` and `202608252108_Variant Stock ledger`. The other 54 files
are only in Helios.

**The same invoices:** 86 of Helios's 759 invoices are in EtpReporting (same store, number and date).
The 673 that are not:

- 655 dated before 1 Jul 2026 (16 Sep 2024 onwards)
- 17 dated 26 Aug to 6 Sep 2026
- 1 dated 25 Aug 2026: HEMW invoice **100000195** (FY-end year 2027), 1 line, NETAMOUNT
  46,800.00, NETVALUE 39,661.02. It is in the 6 Sep export but not in the 25 Aug 21:07 export
  that EtpReporting imported.

For HEMW, 1 Jul – 25 Aug 2026, Helios shows 87 invoices and NETVALUE 1,426,537.42. EtpReporting
shows 86 invoices and 1,386,876.40. That invoice is the whole difference.

**Side note on the live database.** EtpReporting's HEMW figures for 25 Aug 2026 lack invoice
100000195 (NETAMOUNT 46,800.00). That is not part of this decision, but you may want to know it.
Bringing it in means importing a later HEMW export that covers 25 Aug. The importer treats a
changed overlapping period as a restatement, which must be approved explicitly (README, "Import
ETP data"). I have not tried it.

---

## 5. What earlier notes said about it

No note in the worktree uses the word "throwaway" for this database. The word appears only about
other things: the `EtpClaudeAudit` copy, `EtpD9Proof`, a probe scheduled task, a test account
name (PHASE-4-AUDIT), and test databases in general. The note closest to it is PHASE-1-AUDIT item 8, which calls it "disposable".

- **`IMPORT-FAILURE-REGISTER.md` IF-012 (15 Sep):** the first two-year HEMW import into a fresh
  `EtpReportingHelios` said "Imported 790 sales rows successfully" but stored only 675 of 759
  invoices. 272 rows were silently dropped as conflicts because the importer keyed invoices by
  calendar year. That was an **earlier database of the same name**. The current one (created
  16:22) holds all 759 invoices and 790 lines, with 0 conflicts.
- **`claude-audit-2026-09/PHASE-0-AUDIT.md` (15 Sep):** a second app instance was running against
  it. It then held 675 invoices at 16 migrations (the earlier database). `settings.json` pointed at it,
  and the audit restored it to point there at the end of that pass. By 17 Sep 18:16 `settings.json`
  had been changed to `EtpReporting`, where it still points.
- **`ETP-MASTER-AUDIT-AND-PHASED-PLAN.md` (Phase 0 close note):** "two-year Helios corpus, created
  15 Sep ... holds the 272 dropped-row import; Phase 1's whole-folder test drops and rebuilds it."
- **`claude-audit-2026-09/PHASE-1-REPORT.md` and `README.md`:** the import-audit tool
  (`tools/Etp.Reporting.ImportAudit`) uses `EtpReportingHelios` as its **default** database.
  `--rebuild` drops and recreates it, and the tool refuses `EtpReporting`. Acceptance A1.9a:
  "790 lines/759 invoices, three FY identities ... no conflicts; R030 imports 3,955 rows". Those
  are exactly the counts this database holds today.
- **`claude-audit-2026-09/PHASE-1-AUDIT.md` item 8:** "`EtpPhase1Test_RawAcceptance`,
  `EtpPhase1Test_UiReview` plus `EtpReportingHelios`. All are disposable ... they can be dropped at
  any time." The tender-mode evidence for D11 was computed from "the two-year Helios corpus".
- **`claude-audit-2026-09/PHASE-4-AUDIT.md`:** the audit tool is "a local dev tool restricted to
  `EtpReportingHelios` / `EtpPhase1Test_*`".
- **`SESSION-HANDOFF-2026-09-17.md`:** "still at `0020` ... If that database is live for the Helios
  store it is now a schema behind, with no pre-upgrade backup. Decide before relying on it."
  After that session's clean-up, only `EtpReporting` and `EtpReportingHelios` remained.
- **`WORKING-STATE-2026-09-22.md`:** Phase 4 item 4, "decide `EtpReportingHelios` (at `0020`):
  live or leftover".

---

## 6. What dropping it would lose

Dropping deletes the database and its two files (144 MB). You would lose, **unless you keep the backup from section 8**:

1. **The only imported copy of two years of Helios history:** the 673 HEMW invoices (and their lines
   and tenders) that EtpReporting does not have, as listed in section 4.
2. HEMW stock movements outside 1 Jul – 25 Aug 2026, and the 7 Sep 2026 closing stock.
3. The rows of 26 other ETP report families (discounts, banking, collections, GST, receipts,
   schemes, SOR and so on). EtpReporting has none of these yet.
4. The 18 HEMW staff rows loaded from the import. EtpReporting has 0 staff rows.
5. The import record: 58 file entries with their SHA-256 hashes, 25,576 row outcomes and 35,194
   source-lineage rows. This is the evidence behind the Phase 1 acceptance counts and the D11
   tender figures.

Dropping would **not** lose:

- anything in `EtpReporting`
- any work you entered by hand (there is none in Helios)
- the ETP workbooks themselves (they are separate files on disk)
- `settings.json`, the scheduled tasks, or the pre-0031 backup

With the backup kept, everything above can be restored exactly. If the workbooks are still on disk
(step 2 checks this), the data can also be rebuilt by importing them again.

---

## 7. Options

| Option | What it means | Verdict |
|---|---|---|
| **Keep it as a live database** | Nothing uses it. To rely on it you would first need a backup and an upgrade from 0020 to 0032, and you would then run two databases for one store. | Not recommended |
| **Leave as is** | Costs 144 MB and does no harm day to day. But it falls further behind, has never been backed up, and its name looks like a live "Helios" database. `settings.json` pointed at it once before (Phase 0 audit). Running the import-audit tool with `--rebuild` would wipe it without asking. | Acceptable, but it keeps the confusion |
| **Archive with a verified backup, then drop** | One copy-only, checksummed backup (probably about 50 MB; not measured), verified, then the database removed | **Recommended** |

Why archive and drop:

- It is not live.
- It is a test artefact that the audit tool can wipe at any time, so it cannot be relied on anyway.
- NT AUTHORITY\SYSTEM can still read and write it.
- Removing it leaves one clear shop database.
- The backup keeps every row in case you ever want the evidence back.

If you want the two-year Helios history in the live system, the right way is to import the ETP
workbooks into `EtpReporting` through the app ("Import today's folder"), after a fresh verified
backup of `EtpReporting`, not to revive this database. I have not tested that against the live database. Phase 1 found that the older
July–August export has changed overlapping values in R008, R009 and R014, which would need an
explicit restatement.

When: it does not depend on the new installer. Do it on a day when the test suite is not running
on this PC (a backup adds load), and not while setup is running.

---

## 8. Steps, if you choose to archive and then drop

Open **Windows PowerShell as administrator** (Start, type `PowerShell`, right-click, *Run as
administrator*). Paste each command on its own and wait for it to finish before the next one.
If a step gives a result it does not describe, stop there. Nothing up to step 6 changes anything
except step 4, which only writes a backup file.

### Step 1. Check that nothing unattended is pointed at it

1a. List what the three ETP scheduled tasks run:

```powershell
Get-ScheduledTask -TaskName 'ETP Reporting*' | ForEach-Object { $t = $_; $t.Actions | ForEach-Object { '{0} | runs as {1} | {2} {3}' -f $t.TaskName, $t.Principal.UserId, $_.Execute, $_.Arguments } }
```

You should see one line for each ETP task (Automated Operations, Daily Backup, Monthly Recovery
Drill). **None of them should contain the word `Helios`.** If one does, stop. Do not drop anything,
and bring the output back to Claude. If you see only the Monthly Recovery Drill line, the window
is not the administrator one: the two SYSTEM tasks are hidden from an ordinary window. Open it as
administrator and run 1a again.

1b. Show which database unattended operations are configured for:

```powershell
Get-Content -LiteralPath "$env:ProgramData\EtpReporting\Operations\operations.json" -Raw | ConvertFrom-Json | Select-Object serverInstance, database
```

You should see `database` = `EtpReporting`.

- If it says "Cannot find path", the file does not exist. It is written by the folder setup
  (`initialize-etp-operation-folders.ps1`), not by the operations module, so that setup has not
  written it here. Nothing unattended can then be pointed at Helios through it. Carry on.
- If it says "Access to the path ... is denied", the window is not the administrator one. Open
  it as administrator and run 1a and 1b again.
- If it says `EtpReportingHelios`, or both columns are blank, stop and bring the output back to Claude.

### Step 2. Check whether the source workbooks are still on disk

This decides how carefully you must keep the backup. The command reads the hashes stored in
Helios and compares them with the workbooks under the HEMW source folder.

```powershell
$db = & 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -h -1 -W -d EtpReportingHelios -Q "SET NOCOUNT ON; SELECT source_sha256 FROM dbo.import_files"; $want = @($db | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[0-9a-fA-F]{64}$' }); $have = @(Get-ChildItem -LiteralPath 'C:\Codex\Reporting Manger\ETP Source Data\HEMW' -Recurse -File -Filter *.xlsx | Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash }); $found = @($want | Where-Object { $have -contains $_ }).Count; "Source workbooks still on disk: $found of $($want.Count)"
```

You should see `Source workbooks still on disk: 58 of 58`.

- **58 of 58:** the data can be rebuilt from the workbooks at any time. The backup is a convenience.
- **Fewer than 58** (or a red "Cannot find path" error followed by `0 of 58`): some or all workbooks
  have moved or gone. You can still go ahead, but the backup becomes the only copy of that data.
  Keep it permanently and do step 6.
- **`of 0`:** the query could not read Helios. Stop and bring the output back to Claude.

### Step 3. Check that nobody is connected to it

```powershell
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -W -Q "SET NOCOUNT ON; SELECT session_id, login_name, program_name, host_name FROM sys.dm_exec_sessions WHERE database_id = DB_ID(N'EtpReportingHelios');"
```

You should see only the column headings and a line of dashes, with no rows. If rows appear, the
`program_name` says what is connected. Close that program (for example, an ETP window opened on
Helios) and run step 3 again.

### Step 4. Back it up (copy-only, with checksums)

This writes one new file to the SQL backup folder
(`C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup`), where the pre-0031
backup already is. It does not change the database.

```powershell
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -Q "DECLARE @f nvarchar(4000) = CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) + N'\EtpReportingHelios-archive-' + FORMAT(SYSDATETIME(), 'yyyyMMdd-HHmmss') + N'.bak'; BACKUP DATABASE [EtpReportingHelios] TO DISK = @f WITH COPY_ONLY, CHECKSUM, NAME = N'EtpReportingHelios archive before drop'; PRINT @f;"
```

You should see two `Processed ... pages for database 'EtpReportingHelios'` lines, then
`BACKUP DATABASE successfully processed ...`, then the full path of the new `.bak` file. Write
that path down. If you see any line starting with `Msg`, the backup failed. Do not go on to the drop.
Bring the message back to Claude.

This backup is not encrypted, the same as every backup on SQL Express here (decision D9).
It holds customer names, phone numbers, addresses and GSTINs from the ETP reports (for example
the R024, R025 and R003 tables), so anyone who can read the file can read them.

### Step 5. Verify the backup

```powershell
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -Q "DECLARE @f nvarchar(4000) = (SELECT TOP (1) m.physical_device_name FROM msdb.dbo.backupset b JOIN msdb.dbo.backupmediafamily m ON m.media_set_id = b.media_set_id WHERE b.database_name = N'EtpReportingHelios' ORDER BY b.backup_finish_date DESC); PRINT @f; RESTORE VERIFYONLY FROM DISK = @f WITH CHECKSUM;"
```

You should see the same path as in step 4, then `The backup set on file 1 is valid.` If you see
anything else, **do not drop**. Run step 4 again, then step 5 again.

### Step 6. (Recommended if step 2 showed fewer than 58.) Copy the backup off this PC

The file is not encrypted and contains customer personal data (step 4). Use a drive that only you
use and keep it locked away. Do not use a shared drive or a cloud-synced folder (OneDrive and
similar).

First change `E:\` at the end to your USB drive or other disk, then run:

```powershell
Copy-Item -Path 'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\EtpReportingHelios-archive-*.bak' -Destination 'E:\'
```

No output means it worked. Check that the file is on the drive. If you get "Access is denied",
make sure the PowerShell window is the administrator one.

### Step 7. Drop the database

The command refuses to run unless a checksummed copy-only backup of Helios finished in the last
12 hours. It touches only `EtpReportingHelios`.

```powershell
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -Q "IF NOT EXISTS (SELECT 1 FROM msdb.dbo.backupset WHERE database_name = N'EtpReportingHelios' AND is_copy_only = 1 AND has_backup_checksums = 1 AND backup_finish_date > DATEADD(hour, -12, GETDATE())) THROW 50000, 'No checksum backup of EtpReportingHelios from the last 12 hours. Nothing was dropped.', 1; ALTER DATABASE [EtpReportingHelios] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [EtpReportingHelios]; PRINT 'EtpReportingHelios dropped.';"
```

You should see `EtpReportingHelios dropped.`

- If you see `No checksum backup ... Nothing was dropped.`, do steps 4 and 5 first.
- If you see any other `Msg` line, the database may have been left in single-user mode. Put it
  back with the command below, then bring the message to Claude.

```powershell
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -Q "ALTER DATABASE [EtpReportingHelios] SET MULTI_USER;"
```

### Step 8. Confirm

```powershell
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -W -Q "SET NOCOUNT ON; SELECT name, state_desc FROM sys.databases WHERE name LIKE N'EtpReporting%'; SELECT MAX(migration_id) AS live_migration FROM EtpReporting.dbo.schema_migrations;"
```

You should see a single row, `EtpReporting ONLINE`, and `live_migration` =
`0031_health_counts_what_problems_shows` (or `0032_active_users_can_connect` if the new build is
already installed). `EtpReportingHelios` must not be in the list. If another name starting
`EtpReporting` appears (an old test or audit tool can create one), leave it alone and bring the
output back to Claude. Keep the new `EtpReportingHelios-archive-*.bak` next to
`EtpReporting-pre-0031-20260917-151407.bak`, and delete neither.

If anyone runs the import-audit tool again (README, "Import ETP data"), it will create
`EtpReportingHelios` again, because that is its default name. That is expected and harmless.

---

## 9. Side effect of this investigation

All my SQL was SELECTs on tables, catalog views and system views. I also used the read-only
system procedures `xp_enumerrorlogs` and `xp_readerrorlog` to read the SQL error log, and
`fn_trace_gettable` to read the default trace. None of these writes data.

However, SQL Server's **automatic statistics** option (`AUTO_CREATE_STATISTICS`, on by default)
creates a small column statistic the first time a query filters or joins on a column that has none.
My queries triggered it:

- **EtpReporting:** the default trace shows 5 statistics created by my `sqlcmd` sessions between
  19:06 and 19:11 IST:
  - one on `dbo.import_files.original_file_name` (from my file-name comparison)
  - four on SQL Server's internal system tables (from reading catalog views)

  Six more existing statistics on internal system tables were refreshed.
- **EtpReportingHelios:** at least 41 statistics created the same way. The trace writes in
  batches, so my last few queries may not be counted yet.

After 19:11 IST I sent no further queries to EtpReporting.

A second, independent check of this document (22 Sep, 19:15 to 19:30 IST) ran more read-only
queries. On EtpReporting it used only counts, minimums and maximums with no filters; the default
trace confirms it created no statistics there (still the 5 above). On EtpReportingHelios it added
at least 2 more: 43 in all when last checked at 19:32. One of its error-log reads used a temporary table in `tempdb` that
lasted only for that session; it touched neither database.

Statistics are query-planning metadata only. **No rows, tables, procedures, permissions, users or
migration records changed.** The app creates the same kind of statistic in normal use. Migration
0032 redefines one procedure (`configure_application_role`) and gives CONNECT back to active
users; it does not touch statistics, so these do not affect it. I left them in place, because
taking them out would itself be a change.

---

## 10. What I could not verify

- **The two SYSTEM scheduled tasks** ('ETP Reporting Automated Operations', 'ETP Reporting Daily
  Backup'): their actions cannot be read without elevation. Step 1a checks them. The log does give
  indirect evidence. A task running every few minutes against an auto-close database would open it
  many times, and nothing opened it between 17 Sep 21:13 and today. Also, no script in the
  installed `C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts` folder mentions
  `Helios`; the installed `backup-etp-database.ps1` uses `EtpReporting` unless it is given a
  database or reads one from `operations.json`.
- **`operations.json`**: not readable without elevation, and I do not know whether it exists
  (step 1b).
- **Whether the source workbooks are still on disk**: I did not look, because the source folder is
  outside this worktree (step 2).
- **Who opened the database on 17 Sep at 14:11, 21:11 and 21:13 IST.** The error log does not
  record who. The default trace only covers the last 20 to 40 minutes on this PC, because the test
  runs fill it.
- **Use between 15 Sep 16:32 and 17 Sep 11:42.** A program holding a connection open would not
  add new log lines. SQL Server was stopped after 11:42 on 17 Sep, which ends every connection,
  and nothing opened Helios again until 14:11. Either way, nothing was written in that time.
- **Which tool built it.** The import-audit tool is inferred from the timing, the drop-and-recreate
  pattern, its default name and the matching Phase 1 counts. The database does not record it.
- **The file sizes on disk** come from SQL Server's metadata. The DATA folder cannot be listed
  without elevation. **The backup size** (about 50 MB) is an estimate from the 47 MB of used data
  space.
- **Whether importing the two-year Helios workbooks into `EtpReporting` would go cleanly.** Not tested.
