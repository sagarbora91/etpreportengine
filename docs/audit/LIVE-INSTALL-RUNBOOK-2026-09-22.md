# Live install runbook — this PC (DESKTOP-6IBM1J5)

For: Sagar, as Owner, on this PC. Written 22 September 2026 from branch `recovery/opus-r1-r4` (commit `375e3b2`) and read-only checks of this PC made that day. Use it once the new installer exists.

The installer does not exist yet. It will be:

    C:\Codex\Reporting Manger\opus-recovery\artifacts\installer-<sha>\EtpReportingEngine-Setup-1.8.8-x64.exe

`<sha>` stands for the commit the installer is built from. Claude will give you the exact folder name. Replace `installer-<sha>` in every command below with it.

## What this does, in order

| Step | Who / where | What |
|---|---|---|
| A | You, elevated | Close ETP, check nothing else is running it, record the "before" state, take an independent backup |
| B | You, elevated | Run the new setup. It backs up, applies migration 0032, checks the database and re-registers the three scheduled tasks |
| C | You, in ETP (not elevated) | Settings > Users: add `DESKTOP-6IBM1J5\EtpAutomation` as an active Store Manager |
| D | You, elevated | Install the SQL operations module |
| E | You, elevated | Evidence: run the Daily Backup task, run a recovery drill, start the drill task once |
| F | You, elevated | Folder permissions after, compared with before |
| G | You, in ETP, later | Deactivate `NT AUTHORITY\SYSTEM` once the tasks work as EtpAutomation |

Do A to F in one sitting, preferably not close to 22:00. Between B and D the Automated Operations task will fail every five minutes, and a 22:00 Daily Backup would fail too. That is expected and harmless; step E runs the backup again.

## How to use this runbook

- **Elevated window** means Windows PowerShell opened with **Run as administrator**, signed in as yourself (Sagar). Use the same elevated window for every elevated step. Check it: the title bar starts with `Administrator:`.
- Each grey box is **one** command. Copy the whole line, paste it, press Enter.
- Scripts are started as `powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File ...`, never as `& '<script>.ps1'`, because Windows PowerShell on this PC is `Restricted` (see the next section). Do not change the machine's execution policy.
- After each step, tell Claude "Step X done" and paste what you saw. Claude then runs a read-only check (`live-verify.ps1`) and tells you whether to go on. Claude's check runs without elevation, and Windows hides a task registered for another account from that (it already hides the two SYSTEM tasks), so it will most likely not see the two EtpAutomation tasks. That is why some steps ask you to list them yourself.
- If anything differs from "You should see", stop. Do not improvise a fix, and do not restore any database yourself.

## State of this PC today (read-only check, 22 Sep 2026, 19:20)

Checked by Claude without elevation and without changing anything:

- ETP `1.8.8` is installed at `C:\Program Files\Saagar Traders\ETP Reporting Engine`, build `1.8.8+10d622c`. It does not contain migration 0032 or the new grants template.
- `EtpReporting` is at migration `0031` (31 applied; this build ships 32). SQL Server Express 2022 (16.0.1000.6), instance `.\SQLEXPRESS`, service account `NT Service\MSSQL$SQLEXPRESS`, set to start automatically.
- `NT AUTHORITY\SYSTEM` is an active Store Manager in ETP but has **no CONNECT** in `EtpReporting`.
- `DESKTOP-6IBM1J5\EtpAutomation` exists (enabled, not an administrator) and has **no SQL login**. `DESKTOP-6IBM1J5\Sagar` is a SQL administrator through his own login and the database owner.
- The broker `master.dbo.etp_operations_31736fb143c6912a` exists, from 17 Sep: an older version, **not signed**. Its backup folder is `C:\ProgramData\EtpReporting\Backups` and it handles Express, so setup can take its pre-migration backup through it as you. There is no signer certificate, no legacy `EtpOperationsModuleSigner` and no database master key.
- `dbo.verified_operation_receipts` is empty: no verified backup or drill has ever been recorded.
- The verified backup `EtpReporting-pre-0031-20260917-151407.bak` is in `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup`. Nothing in this runbook touches that folder's existing files. Keep it.
- The Monthly Recovery Drill task runs as `Sagar`, Interactive, Limited, with `-ExecutionPolicy Bypass` (the old registration). The Daily Backup and Automated Operations tasks cannot be seen without elevation (Windows answers "Access is denied"), so their current account and folder were not checked.
- `C:\ProgramData\EtpReporting` cannot be read without elevation, so `operations.json` and the folder permissions were not checked today. Step A checks them.
- Windows PowerShell 5.1 on this PC has **no execution policy set** at any scope (LocalMachine, CurrentUser and Group Policy are all `Undefined`), so its effective policy is **`Restricted`**: checked in a fresh `powershell.exe`. A script started as `& '<script>.ps1'` would stop with "running scripts is disabled on this system". This runbook therefore starts every script as `powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File ...`, which is how setup and the scheduled tasks start them. `-ExecutionPolicy` applies to that one run only and changes no setting on the PC. (PowerShell 7 has its own default, `RemoteSigned`; that does not apply to the Windows PowerShell window this runbook uses.)

---

## Step A — Before you start

### A1. Open the elevated window and make an evidence folder

Open **Windows PowerShell** with **Run as administrator**. Then:

```powershell
New-Item -ItemType Directory -Path 'C:\Users\Sagar\ETP-live-install' -Force
```

You should see: a table with `Mode d-----` and `Name ETP-live-install`.

Every file this runbook saves goes into this folder, so Claude can read it without elevation.

### A2. Close ETP and check nothing else is running it

Close ETP on this PC the normal way, saving any entries first. Then:

```powershell
Get-Process -Name 'Etp.Reporting.Desktop' -IncludeUserName -ErrorAction SilentlyContinue | Select-Object Id, UserName, StartTime, Path
```

You should see: nothing at all.

If a line appears:

- **User `NT AUTHORITY\SYSTEM`**: the old five-minute Automated Operations task is running. It ends by itself within a minute. Run the command again.
- **User `DESKTOP-6IBM1J5\Sagar`, and no ETP window anywhere on screen**: this is the old build's windowless leftover (fixed in the new build). Nothing is open in it to save. End it in Task Manager > Details > `Etp.Reporting.Desktop.exe` > End task, then run the command again.
- **A Path under `C:\Codex\...`**: a build or test run is using ETP. Wait until it has finished (ask Claude), then run the command again.

Also check that no test run is going on (the recovery drill in step E can wait a long time for memory while tests run):

```powershell
Get-Process -Name 'testhost', 'vstest.console' -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, StartTime
```

You should see: nothing. If anything appears, ask Claude to wait for or stop that run before you continue.

### A3. Check the protected machine configuration

Setup migrates the database this file names, and registers tasks for the account it names.

```powershell
Get-Content -LiteralPath 'C:\ProgramData\EtpReporting\Operations\operations.json'
```

You should see four values, in any order: `"database": "EtpReporting"`, `"automationPrincipal": "DESKTOP-6IBM1J5\\EtpAutomation"`, `"allowAutomationFolderAccess": true`, and a `"serverInstance"` naming this PC's SQLEXPRESS instance (for example `".\\SQLEXPRESS"`). JSON shows each backslash doubled, and Windows PowerShell writes two spaces after each colon (`"database":  "EtpReporting"`); both are normal.

If it fails: "Access ... is denied" means the window is not elevated. Any other difference (another database, another account, `false`): stop and tell Claude. Do not edit the file.

### A4. Record the scheduled tasks as they are now

```powershell
Get-ScheduledTask -TaskName 'ETP Reporting*' | Format-Table TaskPath, TaskName, State, @{Label='User';Expression={$_.Principal.UserId}}, @{Label='Logon';Expression={$_.Principal.LogonType}}, @{Label='RunLevel';Expression={$_.Principal.RunLevel}} -AutoSize | Out-String -Width 220 | Tee-Object -FilePath 'C:\Users\Sagar\ETP-live-install\tasks-before.txt'
```

You should see three rows, each with TaskPath `\`: Automated Operations and Daily Backup as `SYSTEM`, Monthly Recovery Drill as `Sagar`, `Interactive`, `Limited`.

If there are more than three rows, or a TaskPath other than `\`: stop and tell Claude. Setup registers the tasks in `\`; a copy somewhere else would keep running as SYSTEM beside the new ones.

### A5. Record the folder permissions as they are now

Setup re-applies the folder protection, so this is the "before" half of step F.

```powershell
'EtpReporting', 'EtpReporting\Backups', 'EtpReporting\Documents', 'EtpReporting\Share', 'EtpReporting\Operations', 'EtpReporting\SetupLogs' | ForEach-Object { icacls (Join-Path $env:ProgramData $_) } | Tee-Object -FilePath 'C:\Users\Sagar\ETP-live-install\acl-before.txt'
```

You should see six blocks, each ending `Successfully processed 1 files; Failed processing 0 files`. The 17 Sep audit recorded the parent, `Backups`, `Documents` and `Share` with no `BUILTIN\Users` entry; `Operations` and `SetupLogs` were not recorded then. Nothing needs to be right here yet; this is only the record.

### A6. Take an independent backup (required)

Setup takes its own verified backup before migrating, but that one lives in `C:\ProgramData\EtpReporting\Backups`, and ETP's backup rotation removes it. Every ETP backup (setup's included) ends by keeping only the newest backup of each UTC day, so the first ETP backup later the same day (step E1) deletes setup's one. This backup goes next to the pre-0031 one, in SQL Server's own backup folder, where rotation never looks, and is your rollback point for 0032.

```powershell
$bak = 'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\EtpReporting-pre-0032-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak'; & 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -Q "BACKUP DATABASE [EtpReporting] TO DISK = N'$bak' WITH COPY_ONLY, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM;"; "Backup file: $bak"
```

You should see, with your own numbers:

    Processed N pages for database 'EtpReporting', file 'EtpReporting' on file 1.
    Processed N pages for database 'EtpReporting', file 'EtpReporting_log' on file 1.
    BACKUP DATABASE successfully processed N pages in N seconds (N MB/sec).
    The backup set on file 1 is valid.
    Backup file: C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\EtpReporting-pre-0032-<date>-<time>.bak

Its backups are about 12 MB (the 17 Sep ones were 12.3 MB; the data file is 72 MB allocated, 14 MB used), so this takes seconds. It is copy-only and unencrypted (Express cannot encrypt, D9); it does not change the database.

If it fails: any `Msg` line in red means no usable backup. Stop and send Claude the output. Do not run setup without this backup.

### A7. Find the installer and check it is the one that was built

```powershell
Get-ChildItem -LiteralPath 'C:\Codex\Reporting Manger\opus-recovery\artifacts' -Directory -Filter 'installer-*' | Sort-Object LastWriteTime | Select-Object -Last 3 Name, LastWriteTime
```

You should see the folder Claude named. Use that name for `installer-<sha>` from here on.

```powershell
Get-FileHash -LiteralPath 'C:\Codex\Reporting Manger\opus-recovery\artifacts\installer-<sha>\EtpReportingEngine-Setup-1.8.8-x64.exe' -Algorithm SHA256 | Format-List Hash
```

```powershell
Get-Content -LiteralPath 'C:\Codex\Reporting Manger\opus-recovery\artifacts\installer-<sha>\SHA256SUMS.txt'
```

You should see: the same 64-character hash in both.

If they differ, or the file is missing: stop. Do not run it.

Tell Claude "Step A done". Claude runs the check; today's result is in the section above.

---

## Step B — Run the new setup (elevated)

### B1. What setup does by itself

You do not need to do any of this; it is here so you know what "working" looks like. From `installer/EtpReportingEngine.iss` and `scripts/bootstrap-etp-prerequisites.ps1`:

1. Refuses to start if ETP is running anywhere on the PC.
2. Copies the new files into `C:\Program Files\Saagar Traders\ETP Reporting Engine`.
3. Runs `scripts\bootstrap-etp-prerequisites.ps1` hidden, with your administrator rights. It:
   1. checks the installation folder can be changed only by administrators, reads `operations.json`, and checks `EtpAutomation` is enabled and not an administrator;
   2. checks the SQL service is running (sets it to start automatically, which it already does), finds Sqlcmd, and checks for SQL Server 2022 or newer. On Express it warns that backups are unencrypted (expected, D9);
   3. re-applies the protection on `C:\ProgramData\EtpReporting` and its `Backups`, `Documents`, `Share`, `SetupLogs` and `Operations` folders (`initialize-etp-operation-folders.ps1`);
   4. sees 31 migrations applied and 32 shipped, so **takes a verified pre-migration backup** into `C:\ProgramData\EtpReporting\Backups` through the existing broker, as you, and checks its SHA-256 against its receipt. This also records a `Backup` row in the database, recorded by you;
   5. **applies migration 0032** (`Etp.Reporting.Desktop.exe --initialize-configured-database`). 0032 fixes Settings > Users so a Store Manager keeps the right to connect, and gives that right back to every active user who lost it, which here is `NT AUTHORITY\SYSTEM`;
   6. checks the database is online and read-write, that 32 migrations are recorded, and runs **`DBCC CHECKDB`**;
   7. **registers the three tasks** again, all with S4U logon (runs whether or not anyone is signed in, no stored password):
      - ETP Reporting Daily Backup, daily 22:00, as `DESKTOP-6IBM1J5\EtpAutomation` (from `operations.json`), run level Limited;
      - ETP Reporting Automated Operations, every 5 minutes, as `EtpAutomation`, Limited;
      - ETP Reporting Monthly Recovery Drill, daily 08:00 (the drill itself only on the 1st), under the account it already runs as, `DESKTOP-6IBM1J5\Sagar`, run level **Highest**, after confirming that account is a SQL administrator.
4. Records the outcome: exit code 0 on success; on failure exit code 1603, a `SETUP-INCOMPLETE.txt` file in the installation folder, and the ETP shortcuts removed.

Setup does **not** add EtpAutomation in Settings > Users (step C) or install the operations module (step D).

### B2. Run it

```powershell
$setup = Start-Process -FilePath 'C:\Codex\Reporting Manger\opus-recovery\artifacts\installer-<sha>\EtpReportingEngine-Setup-1.8.8-x64.exe' -ArgumentList '/LOG="C:\Users\Sagar\ETP-live-install\setup.log"' -Wait -PassThru; "Setup exit code: $($setup.ExitCode)"
```

The setup wizard opens. The release is unsigned (decision A4.3), so a publisher warning, if Windows shows one, is expected. In the wizard:

- If a page offers **Install Microsoft SQL Server ... Express**, untick it. SQL Server is already installed. (Leaving it ticked would not reinstall it, because setup installs SQL only when the service is missing, but it also means accepting Microsoft's licence terms for nothing.)
- **Create a desktop shortcut**: as you like.
- Click through to **Install**. After the files are copied, the wizard waits while the database steps run hidden. On a database this small (backups about 12 MB) this should take a few minutes at most (not measured). Do not cancel it.
- On the last page, **untick "Launch ETP Reporting Engine"**, then click **Finish**. (Launched from here, ETP would run with administrator rights. Open it normally in step C instead.) If you forget and ETP opens, close it: `Start-Process -Wait` also waits for programs setup started, so the exit code appears only after ETP is closed.

You should see, back in PowerShell: `Setup exit code: 0`.

The exact wizard pages come from the setup script and Inno Setup's defaults for an upgrade; they have not been seen on this build.

### B3. Check it worked

Read the setup log (it is readable only elevated, so this also copies it into your evidence folder):

```powershell
Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\SetupLogs' -Filter 'bootstrap-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1 | Copy-Item -Destination 'C:\Users\Sagar\ETP-live-install\' -PassThru | Get-Content
```

You should see five lines, each starting with today's date and time:

    ... Existing database has pending bundled migrations. Creating and verifying a pre-migration backup before any migration runs.
    ... Verified pre-migration backup is retained at C:\ProgramData\EtpReporting\Backups\EtpReporting-<date>-<time>-<id>.bak.
    ... EtpReporting migration completed and post-migration state, journal count, and DBCC integrity checks passed.
    ... Daily backup, monthly recovery-drill and five-minute ETP automation tasks are installed.
    ... ETP prerequisite bootstrap completed successfully.

List the tasks:

```powershell
Get-ScheduledTask -TaskName 'ETP Reporting*' | Format-Table TaskPath, TaskName, State, @{Label='User';Expression={$_.Principal.UserId}}, @{Label='Logon';Expression={$_.Principal.LogonType}}, @{Label='RunLevel';Expression={$_.Principal.RunLevel}} -AutoSize | Out-String -Width 220 | Tee-Object -FilePath 'C:\Users\Sagar\ETP-live-install\tasks-after-setup.txt'
```

You should see exactly three rows, all TaskPath `\`, none as SYSTEM:

| TaskName | User | Logon | RunLevel |
|---|---|---|---|
| ETP Reporting Automated Operations | EtpAutomation | S4U | Limited |
| ETP Reporting Daily Backup | EtpAutomation | S4U | Limited |
| ETP Reporting Monthly Recovery Drill | Sagar | S4U | Highest |

Task Scheduler shows local accounts without the `DESKTOP-6IBM1J5\` part; that is normal.

Tell Claude "Step B done". Claude's check should now show: the installed build carries 0032, migration journal at `0032`, SYSTEM `GRANT` connect, and a `Backup` receipt recorded by `DESKTOP-6IBM1J5\Sagar` (the pre-migration backup).

### B4. If setup fails

| What you see | What it means | What to do |
|---|---|---|
| Message "ETP Reporting Engine is running. Close it and run setup again. ..." and exit code 1603 | Some ETP process was running when setup started (for example the old five-minute task, or a test run) | Repeat A2, then B2. Nothing was changed. |
| Message "Mandatory database migration and health validation failed after application files were installed. ..." and exit code 1603 | The database step failed. `SETUP-INCOMPLETE.txt` is in the installation folder and the shortcuts are removed | Do not open ETP. Run the B3 log command and send Claude the output (the last line starts `FAILED:`). See below for what the log tells you. |
| Exit code 1603 and **no new** `bootstrap-*.log` | It failed before its folder step, before it writes a log, so nothing in the database was changed | Run the command below to see the message on screen, and send it to Claude. |
| Any other exit code | Inno Setup's own code, for example the wizard was cancelled | Tell Claude the number. |

Reading a failed log:

- `FAILED:` **before** the "Verified pre-migration backup is retained" line: no migration ran, so the database's structure and data are as before (at most, the backup step added its `Backup` receipt row).
- `FAILED:` **after** that line and **before** "migration completed": the migration may have started. The log names the verified backup. No automatic restore is attempted, and you must not restore on your own either; Claude and you decide next steps together, with the A6 backup as the rollback point.
- `FAILED:` **after** "migration completed": the database is upgraded and checked; a task registration failed. Send the line to Claude.

To see a failure that left no log (this runs exactly what setup ran, with the same arguments, and prints each step):

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\bootstrap-etp-prerequisites.ps1' -ApplicationDirectory 'C:\Program Files\Saagar Traders\ETP Reporting Engine' -SkipSqlInstallation
```

Be aware: this is the real database step, not a dry run. If whatever stopped setup has gone away in the meantime, it carries on exactly as setup would: pre-migration backup, migration 0032, `DBCC CHECKDB` and the three tasks. It does nothing that setup itself would not do, but send Claude the whole output either way before doing anything else. `SETUP-INCOMPLETE.txt` stays until setup itself (B2) completes.

Where the logs are:

- Setup's own log: `C:\Users\Sagar\ETP-live-install\setup.log` (from `/LOG`).
- Database steps: `C:\ProgramData\EtpReporting\SetupLogs\bootstrap-<yyyyMMdd-HHmmss-fff>.log`, and the pre-migration backup receipt beside it, `pre-migration-backup-<...>.json` (elevated only).
- On failure: `C:\Program Files\Saagar Traders\ETP Reporting Engine\SETUP-INCOMPLETE.txt`, which gives the bootstrap exit code.
- If the migration itself failed, ETP records the error in `C:\Users\Sagar\AppData\Local\EtpReporting\Logs\diagnostics-*.jsonl` (code `STARTUP_FAILED`).

---

## Step C — Add EtpAutomation as a Store Manager (in ETP, as Owner)

Settings > Users creates the account's SQL login and database user, makes it a Store Manager, and, since 0032, lets it connect.

1. Open ETP **normally** from the Start menu or desktop shortcut (not "Run as administrator").
2. In the left bar, click **Settings**.
3. At the top, the first box shows **Display**. Open it and choose **Users**. (If **Users** is not in the list, ETP does not see you as Owner. Stop and tell Claude.)
4. In the second box, choose **Users** (the other choice is Import profiles).
5. The **Save user access** button is at the top. Below it are the tabs **Entry fields**, **Application users** and **Integration health**. Stay on **Entry fields**.
6. Fill in:

   | Field | Type exactly |
   |---|---|
   | Windows user identity | `DESKTOP-6IBM1J5\EtpAutomation` |
   | User display name | `ETP Automation` |
   | Application role | **Store Manager** (already selected) |
   | Active | ticked (already ticked) |
   | User access change reason | `Dedicated account for scheduled backups and automation` |

7. Click **Save user access**.

You should see: the three text boxes empty themselves. On the **Application users** tab there is a new row for `DESKTOP-6IBM1J5\EtpAutomation`, role `STORE_MANAGER`, active, modified by `DESKTOP-6IBM1J5\Sagar`.

If it fails: the text stays in the boxes and no row appears. **This screen does not show the reason** (from reading the code: the save message goes to a status line the Users screen does not display). Do not keep retrying. Tell Claude; the check shows which part is missing. ETP also records `USER_ACCESS_SAVE_FAILED` in `C:\Users\Sagar\AppData\Local\EtpReporting\Logs`.

> **Never save `DESKTOP-6IBM1J5\EtpAutomation` in this screen again**, not even with the same values. Every save first removes the account from all its database roles, including `etp_automation` and `db_backupoperator`, and then adds back only Store Manager. The nightly backup would then fail until step D is repeated. If it happens by accident, repeat step D. (The one exception is the second refusal in the D table, where re-saving is the fix.)

You can leave ETP open, but close it before any future setup.

Tell Claude "Step C done". The check should now show the EtpAutomation login, an active Store Manager entry, a database user with CONNECT `GRANT`, and membership of `etp_store_manager` (not yet `etp_automation` or `db_backupoperator`; step D adds those).

---

## Step D — Install the SQL operations module (elevated)

This replaces the old, unsigned broker procedure in `master` with this build's, signs it with a new certificate whose private key is then removed, and gives EtpAutomation exactly what it needs to take backups: EXECUTE on that one procedure, and the `etp_automation` and `db_backupoperator` roles. No database master key is created.

The parameter names are those of `scripts\install-etp-sql-operations.ps1` in this build. `-BackupDirectory` is left at its default, `C:\ProgramData\EtpReporting\Backups`.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\install-etp-sql-operations.ps1' -ServerInstance '.\SQLEXPRESS' -Database 'EtpReporting' -AutomationPrincipal 'DESKTOP-6IBM1J5\EtpAutomation'
```

You should see:

    Restricted SQL backup and recovery module installed for EtpReporting. Backups on this edition: NONE. Verify it under the dedicated account before enabling tasks.

`NONE` is right: Express cannot encrypt backups (D9). The last sentence is the script's standard advice. Here the tasks are already registered, and step E1 is that check.

If it is refused, the script prints one of these messages in red (the fixed text from `scripts\sql\etp-operations-grants.sql`, without its `ETP_MODULE_REFUSED:` prefix):

| Message | What it means | What to do |
|---|---|---|
| Add DESKTOP-6IBM1J5\EtpAutomation as a Store Manager in Settings > Users first. That creates the SQL login this module grants to. | The account has no SQL login: step C was not done, or did not save | Do step C, then run D again |
| The automation account must be an active Store Manager. Add it in Settings > Users first. | The login exists, but its database user is not an active Store Manager (for example saved as Viewer, or not Active) | Check its row on the Application users tab. Save it once more as an active Store Manager (this is the one time re-saving is right), then run D again |
| The automation account cannot connect to the database. Install this build first: its database migration 0032 gives Store Managers back the right to connect. | The account is a Store Manager without the right to connect: migration 0032 is not applied, so the old Settings > Users procedure removed it | Tell Claude; the check shows the migration journal. Step B may not have completed |
| Complete the operations-status database migration first. | The `etp_automation` role is missing (migration 0023) | Not expected: the role exists today. Stop and tell Claude |
| The configured database does not exist on this instance. | Wrong database name or instance | Check the command was pasted exactly |
| The operations broker procedure is missing. Install etp-operations-broker.sql first. | The first half of the script did not create the procedure | Not expected. Stop and tell Claude |
| This SQL Server edition encrypts backups, so it needs the backup certificate. Create and export the recovery keys in Settings > Database first. | The instance is not Express or Web | Not expected on this PC (Express). Stop and tell Claude |

After a refusal: the broker in `master` is already this build's version, but unsigned, and no signer, grant or role was left behind. It still works for you as a SQL administrator, so future setups can still take their backup. Fix the named step and run the same command again; running it again is safe (each run makes a new signer).

Other errors it can print:

| Message | What to do |
|---|---|
| Choose the dedicated local automation account. | The account name was mistyped. Paste the command again |
| Complete protected recovery-folder setup first. | `C:\ProgramData\EtpReporting\Backups\RecoveryDrill` is missing, so setup's folder step did not run. Tell Claude |
| The installation folder can be changed by a non-administrator. / Install operations in a folder owned by Administrators or SYSTEM. | A permission problem on the installation. Tell Claude; do not change permissions yourself |
| Could not reach the SQL Server instance with the installed command-line client. ... | SQL Server is not running. Tell Claude |
| The database operation failed. Check SQL permissions and operation prerequisites. | A SQL statement failed; the script deliberately hides SQL's own text. Tell Claude, who can see the state read-only |
| The operations module did not confirm its signature and grants. | Not expected. Tell Claude |
| ... running scripts is disabled on this system | The command was typed as `& '<script>'` instead of the `powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File ...` form above (this PC's own policy is `Restricted`). Paste the command above exactly. Do not change the machine's execution policy |

Tell Claude "Step D done". The check should now show: broker is this build and signed by `EtpOperationsModuleSigner_31736fb143c6912a`, the signer certificate has no private key (`NA`), the signer login has exactly CREATE ANY DATABASE and VIEW SERVER STATE, EXECUTE granted to EtpAutomation, EtpAutomation in `etp_automation` and `db_backupoperator`, no legacy signer, no database master key.

---

## Step E — Evidence (elevated)

### E1. Run the Daily Backup task now, as EtpAutomation

```powershell
Start-ScheduledTask -TaskName 'ETP Reporting Daily Backup'
```

You should see: nothing (it starts in the background). Wait one minute, then:

```powershell
Get-ScheduledTask -TaskName 'ETP Reporting Daily Backup' | Get-ScheduledTaskInfo | Format-List LastRunTime, LastTaskResult
```

You should see: `LastRunTime` a minute or so ago, and `LastTaskResult : 0`.

- `267009` means it is still running: wait and run the check again.
- Any other number: see the result codes at the end. The task does not keep the backup script's own error message, so Claude finds the cause from the permission checks. Do not re-save the user and do not change the task.

Look at the new backup's receipt:

```powershell
Get-Content -LiteralPath 'C:\ProgramData\EtpReporting\Backups\EtpReporting-latest-verified.json'
```

You should see (Windows PowerShell puts two spaces after each colon): `"verified": true`, `"database": "EtpReporting"`, `"encryption": "NONE"`, a `"backupPath"` in `C:\ProgramData\EtpReporting\Backups` with a time from a minute ago, and a 64-character `"sha256"`.

This backup's rotation keeps only the newest backup of each day, so setup's pre-migration backup from step B is normally deleted now if it was taken the same day (UTC). It may also remove older ETP backups in that folder that are not the newest of their day. That is the designed retention. Your A6 backup and the pre-0031 backup are in the SQL folder and are not affected.

Tell Claude "E1 done". The check should show a `Backup` receipt recorded by `DESKTOP-6IBM1J5\EtpAutomation`.

### E2. Run a recovery drill as yourself

The drill restores a complete copy of the latest verified backup into a temporary database named `EtpRecovery_<id>` in `Backups\RecoveryDrill`, checks every page with `DBCC CHECKDB`, compares its files with the receipt, and deletes the copy. The live `EtpReporting` is not touched. Only a SQL administrator can do this (P4-15), which you are.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\invoke-etp-recovery-drill.ps1'
```

You should see: `Receipt-verified recovery drill completed.` It should take seconds to a few minutes on this database (not measured on this PC).

If it fails:

| Message | What it means | What to do |
|---|---|---|
| Access is denied (an access-denied message naming `operations.json` or its folder) | The window is not elevated | Use the elevated window |
| ... running scripts is disabled on this system | The command was typed as `& '<script>'` | Paste the command above exactly |
| A verified backup receipt is required. / Cannot find path '...EtpReporting-latest-verified.json' because it does not exist. | There is no valid `EtpReporting-latest-verified.json` | No verified backup is there to drill. Go back to E1 and tell Claude |
| Backup verification failed: the receipt and file differ. / Backup metadata does not match the verification receipt. / The backup changed during the recovery drill. | The backup file does not match its receipt | Stop. Keep everything as it is and tell Claude |
| The database operation failed. Check SQL permissions and operation prerequisites. | A SQL step failed (the script hides SQL's text). The drill removes its copy on failure | Tell Claude; the check shows whether a copy is left and which permission is missing |

The result is recorded under the EtpAutomation database user, by design (so code in the application database never runs with your administrator rights). Tell Claude "E2 done". The check should show a `RestoreDrill` receipt recorded by `DESKTOP-6IBM1J5\EtpAutomation`, and no drill copy left. In ETP, Settings > Database > **Database health** should now show times for "Last verified backup" and "Last verified recovery drill" instead of "Missing".

### E3. Prove the drill task can start

The drill task now runs as you with S4U logon and run level Highest. Your Windows account is linked to a Microsoft account, and whether such an account can run an S4U task has not been proven on this PC. This step proves only that.

```powershell
Start-ScheduledTask -TaskName 'ETP Reporting Monthly Recovery Drill'
```

Wait 30 seconds, then:

```powershell
Get-ScheduledTask -TaskName 'ETP Reporting Monthly Recovery Drill' | Get-ScheduledTaskInfo | Format-List LastRunTime, LastTaskResult, NextRunTime
```

You should see: `LastRunTime` just now and `LastTaskResult : 0`.

Read this correctly: the task runs a small runner every day at 08:00 that does the drill only on the 1st and on every other day exits at once with 0. So **0 today proves the task can start as you with S4U and Highest. It does not prove that a drill ran.** The first scheduled drill is 08:00 on 1 October 2026. That day, check this task again and ask Claude to confirm a new `RestoreDrill` receipt dated 1 October. (If you run E3 on the 1st of a month, it performs a real drill.)

If the result is not 0: see the codes at the end. `2147943785` or `2147943726` would mean Windows refused this kind of logon for your account. Do not change the task, your account or any Windows security setting yourself. Tell Claude the number; the choices (for example running the drill by hand each month with E2) are a decision for you.

### E4. Automated Operations

It runs by itself every five minutes. Wait until at least five minutes after step D, then:

```powershell
Get-ScheduledTask -TaskName 'ETP Reporting Automated Operations' | Get-ScheduledTaskInfo | Format-List LastRunTime, LastTaskResult
```

You should see: a `LastRunTime` after step D and `LastTaskResult : 0`. ETP returns 1 when an import source failed or the run failed, and 2 when it could not start with its configuration (from reading `DesktopCompositionRoot.cs`, `DesktopStartupCoordinator.cs` and `App.xaml.cs`). This runbook does not cover import problems; if it is not 0, tell Claude the number.

Optional, if Claude asks: the full check with elevation (it only reads; it shows the two EtpAutomation tasks that Claude's own run cannot see):

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File 'C:\Users\Sagar\AppData\Local\Temp\claude\C--Codex-Reporting-Manger\38f855f7-3921-4259-b901-2a97df36d8cc\scratchpad\recovery\live-verify.ps1'
```

The script is in Claude's working folder for this session, which may be cleaned up later. If the command answers that the file does not exist, ask Claude for its current path.

---

## Step F — Folder permissions after (elevated)

```powershell
'EtpReporting', 'EtpReporting\Backups', 'EtpReporting\Documents', 'EtpReporting\Share', 'EtpReporting\Operations', 'EtpReporting\SetupLogs' | ForEach-Object { icacls (Join-Path $env:ProgramData $_) } | Tee-Object -FilePath 'C:\Users\Sagar\ETP-live-install\acl-after.txt'
```

You should see, for each folder, exactly these four entries and nothing else (their order may differ):

- `Backups`, `Documents`, `Share`, `SetupLogs`: `NT AUTHORITY\SYSTEM:(OI)(CI)(F)`, `BUILTIN\Administrators:(OI)(CI)(F)`, `NT SERVICE\MSSQL$SQLEXPRESS:(OI)(CI)(M)`, `DESKTOP-6IBM1J5\EtpAutomation:(OI)(CI)(M)`
- `EtpReporting` (the parent) and `Operations`: the same, but `(RX)` instead of `(M)` for the SQL service and EtpAutomation.

`Backups`, `Documents` and `Share` are the three folders the plan's acceptance check (A4.2) names; the other three are shown too because setup protects them the same way.

Check for anything broad:

```powershell
Select-String -LiteralPath 'C:\Users\Sagar\ETP-live-install\acl-after.txt' -Pattern 'BUILTIN\Users', 'Everyone', 'Authenticated Users', 'CREATOR OWNER', '(I)' -SimpleMatch
```

You should see: nothing. (`(I)` would mean an entry inherited from `C:\ProgramData`, which the protection removes.)

Compare before and after:

```powershell
Compare-Object (Get-Content -LiteralPath 'C:\Users\Sagar\ETP-live-install\acl-before.txt') (Get-Content -LiteralPath 'C:\Users\Sagar\ETP-live-install\acl-after.txt')
```

You should see: nothing, if the folders were already protected on 17 Sep, because setup re-applies the same policy. Any line shown is a difference. Tell Claude either way, and paste the lines.

Tell Claude "Step F done".

---

## Step G — Later: deactivate NT AUTHORITY\SYSTEM (in ETP, as Owner)

Migration 0032 gave `NT AUTHORITY\SYSTEM` its database access back, because it is still an active Store Manager in ETP. Nothing needs it once both EtpAutomation tasks work.

Do this only when both **Daily Backup** and **Automated Operations** run as EtpAutomation with last result 0, preferably after the first 22:00 backup has succeeded on its own. Claude's own check cannot see those two tasks without elevation, so show it: run the B3 task-list command and the E1 and E4 result commands again (or the elevated full check at the end of E4) and send Claude the output.

1. In ETP: **Settings** > first box **Users** > second box **Users** > **Entry fields**.
2. Fill in:

   | Field | Type exactly |
   |---|---|
   | Windows user identity | `NT AUTHORITY\SYSTEM` |
   | User display name | `ETP Automated Operations` (its current name) |
   | Application role | **Store Manager** (as now) |
   | Active | **untick** |
   | User access change reason | `Scheduled tasks now run as DESKTOP-6IBM1J5\EtpAutomation` |

3. Check the identity says `NT AUTHORITY\SYSTEM`, not EtpAutomation. Click **Save user access**.

You should see: the boxes empty; on **Application users**, the `NT AUTHORITY\SYSTEM` row is no longer active. Claude's check then shows SYSTEM `DENY` connect with an inactive entry (PASS).

If something still ran as SYSTEM, it now fails; the B3 task-list command (elevated) shows every task's account. To undo, save the same row with **Active** ticked.

---

## Scheduled task result codes

`Format-List` shows `LastTaskResult` as a decimal number.

| Decimal | Hex | Meaning |
|---|---|---|
| 0 | 0x0 | Success |
| 1 | 0x1 | The script or program failed (for a PowerShell task: the script stopped with an error) |
| 267009 | 0x41301 | Running now |
| 267011 | 0x41303 | Has not run since it was registered |
| 267014 | 0x41306 | Stopped by a user |
| 2147750687 | 0x8004131F | An instance was already running |
| 2147942405 | 0x80070005 | Access denied |
| 2147943726 | 0x8007052E | Logon failure for the task's account |
| 2147943785 | 0x80070569 | The task's account is not allowed this kind of logon (batch logon) |
| 2147943645 | 0x800704DD | The task's account is not logged on |
| 2147946720 | 0x800710E0 | Windows refused the request (task conditions or policy) |

## What was not verified when this was written

- The installer, its wizard pages and its behaviour: it has not been built. Timings are estimates.
- Whether Windows lets the S4U drill task start under a Microsoft-account-linked account (step E3 tests it).
- Whether Task Scheduler grants EtpAutomation the logon right an S4U task needs when setup registers it (step E1 tests it).
- The contents of `operations.json`, the current folder permissions, and the current account and folder of the two SYSTEM tasks: none can be read without elevation (steps A3, A4, A5 record them).
- That the Users screen hides the save error, that rotation removes setup's pre-migration backup on the same UTC day, and the exact `icacls` layout: from reading the code and the 17 Sep audit record, not from running them.
- Inno Setup's `/LOG` option and exit codes other than 0 and 1603 are Inno Setup's documented behaviour, not tried on this PC.

## Found while writing this (record only; nothing changed)

1. **Setup's pre-migration backup is not kept.** It is written to `C:\ProgramData\EtpReporting\Backups` with a normal receipt, so the next ETP backup's rotation (newest per UTC day, 14 days, plus newest per month) deletes it the same day, and within about two weeks otherwise, although the setup log says it "is retained". Step A6 works around it. A product fix would keep pre-migration backups out of rotation.
2. **Settings > Users does not show why a save failed.** The Users task shows the entry fields and the two tables, but not `AdministrationStatus`, where the failure message is written (`Shell\TaskNavigator.cs`, the `"users"` layout).
3. **The SQL instance has a `BUILTIN\Users` login.** Any local Windows user can connect to the instance (not to `EtpReporting` without a database user). It does not affect this runbook.
4. **A task registered for another account is invisible without elevation.** `Get-ScheduledTask` does not list it and `schtasks` answers "Access is denied", so Claude's unelevated check reports those tasks as PENDING, and steps B3 and E ask you to list them.
5. **Windows PowerShell's execution policy on this PC is `Restricted`.** No scope sets a policy (the registry value under `HKLM:\SOFTWARE\Microsoft\PowerShell\1\ShellIds\Microsoft.PowerShell` is empty), so a script cannot be started with `&` from a PowerShell window. Setup and the scheduled tasks are not affected, because they pass `-ExecutionPolicy RemoteSigned` themselves. `docs/OPERATIONS.md` (the `& '...initialize-etp-operation-folders.ps1'` and `& '...install-etp-sql-operations.ps1'` commands in its steps) assumes a policy that allows scripts, so on this PC those commands would be refused as written.

---

## Outcome — 24 September 2026, this PC

Run with the installer built from `70bf46e`. Every step below was observed, not inferred.

| Step | Result |
|---|---|
| A | No ETP or test process running; `operations.json` correct; three tasks recorded as they were (two as SYSTEM); no `BUILTIN\Users` on any of the six folders; installer hash matched `SHA256SUMS.txt`; independent backup `EtpReporting-pre-0032-20260924-141742.bak` (14 MB) taken and `RESTORE VERIFYONLY` reported it valid |
| B, first attempt (build `1a24b94`) | **Failed, exit 1603.** The database half succeeded — verified pre-migration backup, migration `0032`, journal count and `DBCC CHECKDB` all passed — and then task registration failed with `CimException: Access is denied`. Setup wrote `SETUP-INCOMPLETE.txt` and left the three old SYSTEM tasks untouched. Cause and fix: see the defect note below |
| B, second attempt (build `70bf46e`) | **Exit code 0.** No migration pending, so no second backup. Tasks registered: Automated Operations and Daily Backup as `EtpAutomation` (S4U, Limited), Monthly Recovery Drill as `Sagar` (S4U, **Highest**). None as SYSTEM. First time this step has ever completed on any machine |
| C | `DESKTOP-6IBM1J5\EtpAutomation` saved as an active Store Manager. Database check: SQL login created, database user created, **CONNECT = GRANT** (migration 0032 doing its job), `etp_store_manager` = 1 |
| D | "Restricted SQL backup and recovery module installed for EtpReporting. Backups on this edition: NONE." Broker carries 1 signature; signer certificate `EtpOperationsModuleSigner_31736fb143c6912a` has **NO_PRIVATE_KEY**; no database master key; `EXECUTE` granted only to `EtpAutomation`; the legacy shared `EtpOperationsModuleSigner` was retired; `etp_automation` and `db_backupoperator` both granted |
| E1 | Daily Backup task ran as `EtpAutomation`, result `0x00000000`, new 15 MB backup, receipt **recorded by `DESKTOP-6IBM1J5\EtpAutomation`** — the non-administrator path through the signed module, which is what P4-13 was about |
| E2 | Recovery drill as the Owner: "Receipt-verified recovery drill completed", exit 0, **no `EtpRecovery_*` copy left behind**. Its receipt is recorded by `EtpAutomation`, not by the administrator who ran it — the drill's database work stays inside the automation account's rights |
| E3 | Monthly Recovery Drill task started under the Microsoft-account-linked `Sagar` with S4U: result `0x00000000`. The open question from 22 Sep is answered |
| E4 | Automated Operations ran as `EtpAutomation`, result `0x00000000` |
| F | Six folders: no `BUILTIN\Users`, no `Everyone`; only Administrators, `EtpAutomation`, SYSTEM and the SQL service; **no change at all** from the record taken before setup |
| G | `NT AUTHORITY\SYSTEM` deactivated in Settings > Users the same day, once both automation tasks had run: its application entry is inactive and its database access is now `DENY`. `EtpAutomation` was unaffected — CONNECT `GRANT`, with `etp_store_manager`, `etp_automation` and `db_backupoperator` all intact — which also shows that saving one user does not disturb another |

### The defect this run found: setup could never register its own tasks

Windows requires the target account's password once, at registration, when anyone registers a
task with S4U logon for an account **other than their own**. It authenticates the principal and
is discarded; S4U still stores no credential. Measured here: an elevated administrator is
refused, and so is SYSTEM, which does hold `SeTcbPrivilege` — so no privilege grant could have
fixed it. Registering for the caller's own account needs nothing, which is why the Owner's drill
task always registered and the automation account's two never did.

`Register-ScheduledTask`'s `-Principal` parameter set takes no password, and its `-User`/`-Password`
set would register a Password-logon task that stores the credential. The automation account's
tasks now go through the Task Scheduler COM API, which accepts both: its password is reset to a
fresh random value, used for that one call, and discarded — nobody ever knows it, as designed.
Fixed in `70bf46e`.

Corrected along the way: the "Log on as a batch job" right is **not** what caused this. A missing
batch right returns `SCHED_S_BATCH_LOGON_PROBLEM`, which registers the task and only warns. The
grant stays (a batch task does need it to start) but the earlier explanation was wrong.
