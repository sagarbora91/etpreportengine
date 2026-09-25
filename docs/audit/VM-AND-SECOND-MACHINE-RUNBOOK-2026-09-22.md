# Acceptance VM and second-machine restore runbook (22 September 2026)

For Sagar. Written 22 September 2026 on branch `recovery/opus-r1-r4` (HEAD `375e3b2`). **Nobody has run this yet.**

Start it only when this PC (`DESKTOP-6IBM1J5`) is finished: the new installer is built, this PC is upgraded, and the product has made a verified backup here (Phase 4 remaining item 2 in `docs/audit/WORKING-STATE-2026-09-22.md`). Part D restores that backup.

## What this runbook does

| Part | What | Task item |
|---|---|---|
| A | Resume `ETP-Acceptance-186` and copy the new installer and two scripts into it from this PC | (a) |
| B | Install the new build in the VM the way a new shop PC would get it: protected folders and `EtpAutomation`, then setup, then `EtpAutomation` as Store Manager, then the SQL operations module, then a backup under `EtpAutomation` and a drill under the Owner | (b) |
| C | Check the one thing this PC cannot show. On the VM the Owner is a SQL administrator only through `BUILTIN\Administrators`. The drill task must be registered under the Owner at run level Highest, and the setup check that confirms SQL administrator rights through that group must pass. This is the first real run of that code path | (c) |
| D | Second-machine restore: restore this PC's verified backup into the VM under a new database name, run `DBCC CHECKDB`, and compare the file metadata with the receipt | (d) |
| E | What evidence to record, and where to keep it | (e) |
| F | Finish and clean up | — |

## Read this first: two facts I could not confirm

1. **The VM's SQL Server edition.** The task and `WORKING-STATE-2026-09-22.md` say the VM runs the bundled SQL Server Express. The last recorded change to the VM says otherwise. `docs/audit/claude-audit-2026-09/PHASE-4-AUDIT.md` section 11 records that on 17 September, with your approval, the instance was upgraded in place from Express to **Developer Edition** 16.0.1000.6 so encrypted backups could be tested. The handoff written later that night (`SESSION-HANDOFF-2026-09-17.md`) says "Has SQL Express" but does not mention a downgrade. I could not check without connecting to the VM, and I was not allowed to. **Step B6 checks the edition.** Parts B and C are written for Express. If B6 shows Developer, follow Appendix B. Part D works on either edition.
2. **"The Owner is SQL administrator only through `BUILTIN\Administrators`."** The working-state file says this, but I could not find it measured anywhere. Steps B6 and B7 check it. If the Owner turns out to have its own SQL administrator login, everything below still works, but this run does not test the group path. Write that down.

Other things I could not verify are listed in Appendix C.

## Rules for the whole runbook

- Never restore over, rename or drop the VM's own `EtpReporting`. Never use `WITH REPLACE`.
- Never give `EtpAutomation` `sysadmin`, `dbcreator` or membership of Administrators, even to get past a failure.
- Allow scripts only for the current window (`Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned`). Never use `Bypass`, and never change the machine policy.
- Never edit a receipt, the migration journal or `operations.json` by hand.
- On this PC, only copy. Keep `EtpReporting-pre-0031-20260917-151407.bak` and every product backup where they are.
- If what you see does not match "You should see", stop, write down what you saw, and do not work around it.

## Who may run which step

You run every step in this runbook. Steps marked **SAGAR ONLY** must never be handed to an agent or a script, even if one is given access to the VM later. They create an account, type a password, grant access, or delete something:

| Step | Why |
|---|---|
| A8, B1 | You type the VM account's password (at the `Get-Credential` prompt, and at the VM sign-in screen). This document contains no password. |
| B17 | Creates the Windows account `EtpAutomation` and resets the ETP folder permissions |
| B22 case 4, B27 | Create the operations procedure, a signing certificate and a login in `master`, and grant access |
| B25 | Settings > Users: creates a SQL login for `EtpAutomation` and gives it the Store Manager role |
| B29 diagnostic | Registers a temporary task that runs as `EtpAutomation`, then deletes that task and its output file |
| F4, F5 | Delete the test database and the copied backup |

## Where each command runs

| Label | Window |
|---|---|
| **HOST-ADMIN** | This PC. Start > Windows PowerShell > right-click > Run as administrator. The title bar reads "Administrator: Windows PowerShell". The Hyper-V commands need this, and so does reading `C:\ProgramData\EtpReporting\Backups`. |
| **VM-ADMIN** | Inside the VM. Start > Windows PowerShell (not the "(x86)" one) > right-click > Run as administrator. |
| **VM-NORMAL** | Inside the VM, an ordinary Windows PowerShell window that is **not** run as administrator. |
| **VM-APP** | The ETP application, in the VM console. |

How to get the commands into the VM. VMConnect pastes with Ctrl+V in an enhanced session. In a basic session use its menu, Clipboard > Type clipboard text. Or run the VM-ADMIN commands from the host window after `Enter-PSSession -Session $s` (step A9). They then run inside the VM, as the same account, elevated. On 17 September the folder script ran this way and passed its own administrator check. Two things must still happen in the VM console: the app (B25) and the non-elevated check (C2).

Some blocks set variables such as `$sqlcmd`. Later blocks need those variables, so run them in the same window. If you close the window, run the block that sets them again. A few blocks set several variables on one line; paste the whole line. Every other block is one command.

## Before you start

1. The new build exists on this PC: `artifacts\installer-<sha>\EtpReportingEngine-Setup-1.8.8-x64.exe` with `SHA256SUMS.txt` beside it, and `artifacts\windows-release-<sha>` from the same build. `<sha>` is the commit the installer was built from.
2. This PC has a verified backup that the product made after its upgrade: `C:\ProgramData\EtpReporting\Backups\EtpReporting-latest-verified.json` exists. This is needed only for Part D.
3. No build or test run is going on this PC. The VM needs memory: it was using about 4 GB while running idle before it was saved (`WORKING-STATE-2026-09-22.md`).
4. You know the password of the VM's administrator account. The 17 September audit records one: `ETPTest`, a local administrator in the guest `DESKTOP-IPT0J6H`. If you use another account, put its name wherever this runbook says `ETPTest`.

---

## Part A. Resume the VM and copy the installer in

### A1. Find the new build folders (HOST-ADMIN)

```powershell
Get-ChildItem -LiteralPath 'C:\Codex\Reporting Manger\opus-recovery\artifacts' -Directory | Sort-Object LastWriteTime | Select-Object Name, LastWriteTime
```

You should see: an `installer-<sha>` folder and a `windows-release-<sha>` folder with the same `<sha>`, newer than `installer-9d0afbb` and `installer-10d622c`, which are older builds.
If it fails: if neither folder exists, the installer has not been built. Stop.

### A2. Set the host variables (HOST-ADMIN)

Replace `<sha>` with the suffix from A1.

```powershell
$vm = 'ETP-Acceptance-186'; $build = 'C:\Codex\Reporting Manger\opus-recovery\artifacts'; $sha = '<sha>'
```

You should see: nothing.

### A3. Check that the files to copy are there (HOST-ADMIN)

```powershell
Test-Path -LiteralPath "$build\installer-$sha\EtpReportingEngine-Setup-1.8.8-x64.exe", "$build\installer-$sha\SHA256SUMS.txt", "$build\windows-release-$sha\scripts\initialize-etp-operation-folders.ps1", "$build\windows-release-$sha\scripts\etp-operations-common.ps1"
```

You should see: `True` four times.
If it fails: a `False` means that file is missing. Check `$sha` against A1.

### A4. Count the migrations in the new build (HOST-ADMIN)

```powershell
(Get-ChildItem -LiteralPath "$build\windows-release-$sha\database\migrations" -Filter '*.sql' -File).Count
```

You should see: `32` if the build is this branch as of 22 September. Write the number down: steps B9 and B23 compare against it.

### A5. Check that there is memory for the VM (HOST-ADMIN)

```powershell
Get-CimInstance -ClassName Win32_OperatingSystem | Select-Object @{Name='FreeMemoryGB'; Expression={[math]::Round($_.FreePhysicalMemory / 1MB, 1)}}
```

You should see: at least 5.
If it fails: close large programs, and make sure no build or test run is going, before you resume the VM.

### A6. Check the VM's state, then resume it (HOST-ADMIN)

```powershell
Get-VM -Name $vm | Select-Object Name, State, Status, DynamicMemoryEnabled, MemoryStartup
```

You should see: `State` `Saved`.

```powershell
Start-VM -Name $vm
```

You should see: no output. `Start-VM` resumes a saved VM.
If it fails: "not enough memory" means go back to A5. Do not change the VM's memory settings.

### A7. Wait until the guest is up (HOST-ADMIN)

```powershell
Get-VM -Name $vm | Select-Object Name, State, Heartbeat, MemoryAssigned
```

You should see: `State` `Running` and `Heartbeat` starting with `Ok`, for example `OkApplicationsHealthy`. Repeat after a few seconds if it is still starting.

### A8. Type the VM account's password (HOST-ADMIN) — **SAGAR ONLY**

A dialog opens. Type the password there, not in this window and not in any file.

```powershell
$cred = Get-Credential -UserName 'DESKTOP-IPT0J6H\ETPTest' -Message 'Password for the ETP-Acceptance-186 account'
```

You should see: the prompt returns with no output.
If it fails: if the next step says the account name is wrong, run this again with the user name `.\ETPTest`. I have not verified which form this VM accepts.

### A9. Open a PowerShell Direct session to the VM (HOST-ADMIN)

```powershell
$s = New-PSSession -VMName $vm -Credential $cred
```

You should see: no output.
If it fails: "The credential is invalid" means repeat A8. "not in running state" means wait (A7). PowerShell Direct goes over the VM bus, so the guest's firewall does not affect it.

### A10. Confirm who and where you are, and compare the clocks (HOST-ADMIN)

```powershell
Invoke-Command -Session $s -ScriptBlock { [pscustomobject]@{ Computer = $env:COMPUTERNAME; Account = [Security.Principal.WindowsIdentity]::GetCurrent().Name; Elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); VmClockUtc = (Get-Date).ToUniversalTime() } }; "Host clock UTC: $((Get-Date).ToUniversalTime())"
```

You should see: `Computer` `DESKTOP-IPT0J6H`, `Account` `DESKTOP-IPT0J6H\ETPTest`, `Elevated` `True`, and a VM clock within a minute of the host clock.
If it fails: if the clock is far off, wait a minute for Hyper-V time synchronisation after the resume and run this again. Receipts carry UTC times, so the clock matters for the evidence.

### A11. Create two protected folders in the VM (HOST-ADMIN)

`C:\EtpStaging` holds the installer. `C:\EtpEvidence` holds logs and transcripts. Only Administrators and SYSTEM can open either one.

```powershell
Invoke-Command -Session $s -ScriptBlock { foreach ($d in 'C:\EtpStaging', 'C:\EtpEvidence') { New-Item -ItemType Directory -Path $d -Force | Out-Null; icacls $d /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' } }
```

You should see: `Successfully processed 1 files; Failed processing 0 files` twice.

### A12. Create a protected evidence folder on this PC (HOST-ADMIN)

If `C:\EtpEvidence` already exists on this PC and holds other things, choose another name here and in F2.

```powershell
New-Item -ItemType Directory -Path 'C:\EtpEvidence' -Force
```

```powershell
icacls 'C:\EtpEvidence' /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F'
```

You should see: `Successfully processed 1 files; Failed processing 0 files`.

### A13. Record the hashes on this PC (HOST-ADMIN)

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath "$build\installer-$sha\EtpReportingEngine-Setup-1.8.8-x64.exe", "$build\windows-release-$sha\scripts\initialize-etp-operation-folders.ps1", "$build\windows-release-$sha\scripts\etp-operations-common.ps1" | Format-List Hash, Path
```

```powershell
Get-Content -LiteralPath "$build\installer-$sha\SHA256SUMS.txt"
```

You should see: the hash in `SHA256SUMS.txt` matches the installer's hash above. Write all three hashes into the evidence table (E1).
If it fails: if they differ, the installer changed after it was built. Stop.

### A14. Copy the installer and the two scripts into the VM (HOST-ADMIN)

The installer is large (the last build was about 860 MB), so this takes a while.

```powershell
Copy-Item -ToSession $s -LiteralPath "$build\installer-$sha\EtpReportingEngine-Setup-1.8.8-x64.exe", "$build\installer-$sha\SHA256SUMS.txt" -Destination 'C:\EtpStaging\'
```

```powershell
Copy-Item -ToSession $s -LiteralPath "$build\windows-release-$sha\scripts\initialize-etp-operation-folders.ps1", "$build\windows-release-$sha\scripts\etp-operations-common.ps1" -Destination 'C:\EtpStaging\'
```

You should see: no output.
If it fails: "Access is denied" means the session is not elevated (check A10). Do not change the VM's Hyper-V integration settings to use `Copy-VMFile` instead.

Why the two scripts are copied separately: setup's post-install step reads the protected machine configuration `operations.json` before doing anything else, and stops if it is missing (`bootstrap-etp-prerequisites.ps1` lines 188-190). The folder script writes that configuration, so it has to run before setup. The copy of the script already installed on the VM belongs to an older build. Running the new build's own copy from `C:\EtpStaging` means the version that runs is the one that ships.

### A15. Check the hashes inside the VM (HOST-ADMIN)

```powershell
Invoke-Command -Session $s -ScriptBlock { Get-FileHash -Algorithm SHA256 -Path 'C:\EtpStaging\*' | Format-List Hash, Path }
```

You should see: four hashes. Three are the same as A13 (installer and two scripts); the fourth is the hash of the `SHA256SUMS.txt` file itself, which A13 did not list.
If it fails: if any of the three differs, delete that file in the VM and copy it again.

---

## Part B. Install the new build in the VM

### B1. Open the VM console and sign in (HOST-ADMIN) — **SAGAR ONLY**

```powershell
vmconnect.exe localhost 'ETP-Acceptance-186'
```

Sign in as `ETPTest` (you type the password on the VM's screen). Then open a **VM-ADMIN** window: Start > Windows PowerShell > right-click > Run as administrator > Yes.

### B2. Allow scripts in this window only (VM-ADMIN)

Windows client editions block scripts by default. This allows them for this one window, which is the same `RemoteSigned` that setup itself uses. The setting disappears when the window closes.

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy RemoteSigned -Force
```

You should see: nothing.
If it fails: a message that the setting is "overridden by a policy defined at a more specific scope" means Group Policy controls it. Record that and stop.

### B3. Start a transcript (VM-ADMIN)

This records the commands and their output in the VM's protected evidence folder.

```powershell
Start-Transcript -Path ('C:\EtpEvidence\vm-186-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.txt')
```

You should see: `Transcript started, output file is C:\EtpEvidence\vm-186-....txt`. I have not confirmed that Windows PowerShell 5.1 captures `sqlcmd` output in a transcript. Check the file at F1, and if the SQL output is missing, copy it into the evidence table by hand. If you work through `Enter-PSSession` from the host, start the transcript in the host window before you enter the session.

### B4. Confirm the account and elevation (VM-ADMIN)

```powershell
[pscustomobject]@{ Computer = $env:COMPUTERNAME; Account = [Security.Principal.WindowsIdentity]::GetCurrent().Name; Elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); Is64Bit = [Environment]::Is64BitProcess }
```

You should see: `DESKTOP-IPT0J6H`, `DESKTOP-IPT0J6H\ETPTest`, `True`, `True`.
If it fails: `Elevated False` means this window is not "Run as administrator". `Is64Bit False` means you opened Windows PowerShell (x86); the folder script needs the 64-bit one.

### B5. Find sqlcmd (VM-ADMIN)

This checks the two ODBC places, in the same order, that the product's `Resolve-EtpSqlCmd` tries first. The product then falls back to go-sqlcmd at `C:\Program Files\sqlcmd\sqlcmd.exe`; this runbook does not, because its SQL commands need the ODBC client.

```powershell
$sqlcmd = @("$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE", "$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1; $sqlcmd
```

You should see: one path ending in `SQLCMD.EXE`.
If it fails: if it prints nothing, the ODBC sqlcmd is missing. Setup would then fall back to go-sqlcmd, which this VM has (P4-9), and its first SQL query over `.\SQLEXPRESS` would most likely fail over named pipes; it stops with "Microsoft Sqlcmd is not installed" only if go-sqlcmd is missing too. The 17 September audit found the ODBC client installed on this VM. Stop and record.

Every SQL command below uses `-S 'lpc:.\SQLEXPRESS'` (shared memory). This matters because the VM also has go-sqlcmd, which fails over named pipes (P4-9).

### B6. SQL version, edition and how you are a SQL administrator (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -Q "SET NOCOUNT ON; SELECT CONVERT(varchar(20), SERVERPROPERTY('ProductVersion')) AS version, CONVERT(varchar(60), SERVERPROPERTY('Edition')) AS edition, CONVERT(varchar(60), SERVERPROPERTY('Collation')) AS server_collation, SUSER_SNAME() AS me, IS_SRVROLEMEMBER('sysadmin') AS sysadmin_now, IS_SRVROLEMEMBER('sysadmin', SUSER_SNAME()) AS sysadmin_by_name, (SELECT COUNT(*) FROM sys.server_principals WHERE sid = SUSER_SID()) AS own_login; SELECT name, type, usage FROM sys.login_token;"
```

You should see: `version` `16.0.1000.6` or a later 16.0 build; `edition` `Express Edition (64-bit)`; `me` `DESKTOP-IPT0J6H\ETPTest`; `sysadmin_now` `1`. In the second list, `BUILTIN\Administrators | WINDOWS GROUP | GRANT OR DENY` and `sysadmin | SERVER ROLE | GRANT OR DENY`.

Look at the `usage` column, not only the name. A window that is not elevated also lists `BUILTIN\Administrators`, but as `DENY ONLY`, which gives no rights (measured on this PC on 22 September from a window that was not elevated). Only `GRANT OR DENY` shows the group is really in use.

The group path looks like this: `sysadmin_now` is `1`, but `sysadmin_by_name` is `NULL` (no login of its own) or `0` (a login of its own that is not a SQL administrator). Review finding 1 is exactly that `NULL`.

If it fails: `Developer Edition` means Appendix B. `sysadmin_now 0` means this window is not elevated, or the account is not a SQL administrator; stop. A "Login failed" error means the same.

### B7. Who holds SQL administrator by name (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -Q "SET NOCOUNT ON; SELECT p.name AS sysadmin_member, p.type_desc, p.is_disabled FROM sys.server_role_members m JOIN sys.server_principals r ON r.principal_id = m.role_principal_id JOIN sys.server_principals p ON p.principal_id = m.member_principal_id WHERE r.name = N'sysadmin' ORDER BY p.name;"
```

You should see: `BUILTIN\Administrators | WINDOWS_GROUP`, plus `sa` and some `NT SERVICE\...` entries, and **not** `DESKTOP-IPT0J6H\ETPTest`.
If it fails: if `ETPTest` is listed as a `WINDOWS_LOGIN`, carry on, but record that this VM does not test the group path.

### B8. Databases, operations procedure, certificates (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -Q "SET NOCOUNT ON; SELECT name, state_desc, collation_name FROM sys.databases WHERE database_id > 4 ORDER BY name; SELECT DB_NAME(database_id) AS db, name AS logical_name, physical_name FROM sys.master_files WHERE database_id = DB_ID(N'EtpReporting'); SELECT name AS broker FROM master.sys.procedures WHERE name LIKE N'etp[_]operations[_]%'; SELECT name AS certificate FROM master.sys.certificates WHERE name NOT LIKE N'##%'; SELECT COUNT(*) AS master_key FROM master.sys.symmetric_keys WHERE name = N'##MS_DatabaseMasterKey##';"
```

You should see:

- the VM's databases, and where its own `EtpReporting` files live. A `collation_name` of `NULL` only means that database is closed at the moment by AUTO_CLOSE, which ETP databases have on (this PC's does);
- whether `etp_operations_31736fb143c6912a` exists. The name is derived from the database name alone, so it is the same on every machine. A procedure with a different suffix serves a different database and does not help `EtpReporting`;
- any leftover certificates, and whether a master key exists. The 17 September tests created some on this VM. Leave them alone.

### B9. The VM database's journal and users, only if B8 listed `EtpReporting` (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -d EtpReporting -Q "SET NOCOUNT ON; SELECT COUNT(*) AS migrations, MAX(migration_id) AS latest FROM dbo.schema_migrations; SELECT windows_identity, role_code, is_active FROM dbo.application_users ORDER BY role_code, windows_identity;"
```

You should see: the migration count, and an active `OWNER` row for `DESKTOP-IPT0J6H\ETPTest`.
If it fails: if there is no active Owner row for the account you signed in with, Settings > Users will refuse at B25. Record it and stop before B21.

### B10. The SQL service's real identity (VM-ADMIN)

```powershell
Get-CimInstance -ClassName Win32_Service -Filter 'Name="MSSQL$SQLEXPRESS"' | Select-Object Name, State, StartMode, StartName
```

You should see: `State` `Running` and `StartName` `NT Service\MSSQL$SQLEXPRESS`.
If it fails: if `StartName` is different, record it, but still use `'NT Service\MSSQL$SQLEXPRESS'` in B17. Setup re-applies the folder permissions with exactly that name, whatever account the service logs on as (`bootstrap-etp-prerequisites.ps1` line 228), so a different value in B17 would be replaced at B21 anyway. In D1, grant the `StartName` account as well. If `State` is not `Running`, stop: setup refuses a stopped SQL service it cannot start.

### B11. Local accounts and Administrators (VM-ADMIN)

```powershell
Get-LocalUser | Select-Object Name, Enabled, Description
```

```powershell
Get-LocalGroupMember -SID 'S-1-5-32-544' | Select-Object Name, ObjectClass, PrincipalSource
```

You should see: `EtpAutomation` either absent (B17 creates it) or present and enabled; and `EtpAutomation` not among the Administrators.
If it fails: if `EtpAutomation` is an administrator, B17 refuses. Stop and decide. If `Get-LocalGroupMember` errors with "Failed to compare two elements in the array", the Administrators group contains a member Windows can no longer resolve; this is a known Windows PowerShell 5.1 limitation. B17 calls the same cmdlet and will fail the same way. Record it and stop.

### B12. What ETP state is already there (VM-ADMIN)

```powershell
Test-Path -LiteralPath 'C:\ProgramData\EtpReporting\Operations\operations.json'
```

```powershell
Get-Item -LiteralPath 'C:\Program Files\Saagar Traders\ETP Reporting Engine\Etp.Reporting.Desktop.exe' -ErrorAction SilentlyContinue | ForEach-Object { $_.VersionInfo.ProductVersion }
```

```powershell
Get-ScheduledTask | Where-Object TaskName -like 'ETP Reporting*' | Select-Object TaskName, State, @{Name='RunAs'; Expression={$_.Principal.UserId}}, @{Name='Logon'; Expression={$_.Principal.LogonType}}, @{Name='RunLevel'; Expression={$_.Principal.RunLevel}}
```

You should see: most likely `False` (the 17 September handoff says there is no `operations.json`), an older `1.8.8+...` version, and possibly no ETP tasks. Record whatever is there.

### B13. Folder permissions before (VM-ADMIN)

```powershell
'C:\ProgramData\EtpReporting', 'C:\ProgramData\EtpReporting\Backups', 'C:\ProgramData\EtpReporting\Documents', 'C:\ProgramData\EtpReporting\Share', 'C:\ProgramData\EtpReporting\SetupLogs', 'C:\ProgramData\EtpReporting\Operations' | ForEach-Object { if (Test-Path -LiteralPath $_) { icacls $_ } else { "$_ does not exist" } }
```

You should see: whatever is there now. The 17 September strict-mode run left SYSTEM, Administrators and `MSSQL$SQLEXPRESS` only. This is the "before" half of the ACL evidence.

### B14. Free disk space (VM-ADMIN)

```powershell
Get-PSDrive -Name C | Select-Object @{Name='FreeGB'; Expression={[math]::Round($_.Free / 1GB, 1)}}
```

You should see: at least 10. Setup refuses a pre-migration backup with less than 5 GB free.

### Decide before going on

| Check | Carry on when | Otherwise |
|---|---|---|
| Edition (B6) | `Express Edition (64-bit)` | `Developer Edition`: Appendix B. Anything else: stop. |
| Version (B6) | 16.0, at least `16.0.1000.6` (this PC's version, checked 22 Sep) | Part D cannot restore this PC's backup on an older version. |
| `sysadmin_now` (B6) | `1` | Stop: the window is not elevated, or the account is not a SQL administrator. |
| Group path (B6, B7) | `ETPTest` not listed by name in B7, and B6 lists `BUILTIN\Administrators` as `GRANT OR DENY` | Carry on, and record that the group path was not tested. |
| SQL service (B10) | `NT Service\MSSQL$SQLEXPRESS`, `Running` | A different `StartName`: record it, keep `NT Service\MSSQL$SQLEXPRESS` in B17, and grant both in D1. Not running: stop. |
| `EtpAutomation` (B11) | absent, or enabled and not an administrator | Stop. |
| VM `EtpReporting` (B8, B9) | **Case 1:** absent. **Case 2:** present, journal count equals A4. **Case 3:** present, fewer migrations, and B8 lists exactly `etp_operations_31736fb143c6912a`. **Case 4:** present, fewer migrations, and that name is not listed. Any other `etp_operations_<hash>` name belongs to a different database and does not count. `PHASE-4-AUDIT.md` records that the 17 September tests installed the procedure for a test database, `EtpCustodyDrill`; whether it is still there is not known. | Case 4 is handled in B22. More migrations than A4: stop and record; this runbook does not cover it. |

### B15. Safety copy of the VM's own database, in cases 2, 3 and 4 only (VM-ADMIN)

This is the same kind of independent copy as this PC's `EtpReporting-pre-0031-...bak`. A file name with no folder goes into the instance's default backup folder, which the SQL service can already write.

```powershell
$safety = 'EtpReporting-vm-before-setup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bak'; $safety
```

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -Q "BACKUP DATABASE [EtpReporting] TO DISK = N'$safety' WITH COPY_ONLY, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'$safety' WITH CHECKSUM;"; "Exit code: $LASTEXITCODE"
```

You should see: `BACKUP DATABASE successfully processed ... pages`, then `The backup set on file 1 is valid.`, then `Exit code: 0`. Record the file name. Keep this file until the VM work is finished.

### B16. Check the staged scripts once more, just before running (VM-ADMIN)

```powershell
Get-FileHash -Algorithm SHA256 -Path 'C:\EtpStaging\*.ps1' | Format-List Hash, Path
```

You should see: the two script hashes from A13.

### B17. Protect the ETP folders and create `EtpAutomation` (VM-ADMIN) — **SAGAR ONLY**

The real parameters of `initialize-etp-operation-folders.ps1`, read from the script:

| Parameter | Value here | Notes |
|---|---|---|
| `-SqlServiceIdentity` | `'NT Service\MSSQL$SQLEXPRESS'` | Required. Keep the single quotes, so PowerShell does not treat `$SQLEXPRESS` as a variable. |
| `-ServerInstance` | default `.\SQLEXPRESS` | not passed |
| `-Database` | default `EtpReporting` | not passed |
| `-AutomationPrincipal` | default `<computer>\EtpAutomation` | not passed |
| `-GrantAutomationFolderAccess` | on by default | not passed |
| `-CreateAutomationAccount` | passed | Creates `EtpAutomation` if it is missing, with a long random password that is never shown or stored. S4U tasks do not need one. |

Once setup has run, the installed copy lives at `C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\initialize-etp-operation-folders.ps1`. Setup's post-install step runs that copy again itself, without `-CreateAutomationAccount` (bootstrap line 228). That is why the account has to exist before setup.

The script replaces the permissions of `C:\ProgramData\EtpReporting` itself, and resets the permissions of everything inside `Backups`, `Documents`, `Share`, `SetupLogs` and `Operations`, including files already there. On the VM that is expected.

```powershell
& 'C:\EtpStaging\initialize-etp-operation-folders.ps1' -SqlServiceIdentity 'NT Service\MSSQL$SQLEXPRESS' -CreateAutomationAccount
```

You should see: `Protected folders and dedicated automation configuration prepared.`
If it fails:

| Message | Meaning |
|---|---|
| "Run folder setup as a Windows administrator." | The window is not elevated. |
| "Some or all identity references could not be translated." | The SQL service identity is wrong; use the B10 value. |
| "The automation account must not be an administrator." | `EtpAutomation` is in Administrators. Stop. |
| "Enable the dedicated automation account first." | The account is disabled. Stop and decide. |
| "Linked operation paths are not allowed." | A folder in the path is a link or junction. Stop. |
| "running scripts is disabled" | B2 was not run in this window. |

### B18. Check the new account (VM-ADMIN)

```powershell
Get-LocalUser -Name 'EtpAutomation' | Select-Object Name, Enabled, Description
```

You should see: `Enabled` `True` and `Description` `ETP scheduled operations (non-admin S4U)`. If the account existed before, its description is whatever was set then.

```powershell
Get-LocalGroupMember -SID 'S-1-5-32-544' | Select-Object Name
```

You should see: `EtpAutomation` not listed.

### B19. Check the protected configuration (VM-ADMIN)

```powershell
Get-Content -Raw -LiteralPath 'C:\ProgramData\EtpReporting\Operations\operations.json'
```

You should see: `serverInstance` `.\\SQLEXPRESS` (JSON doubles the backslash), `database` `EtpReporting`, `automationPrincipal` `DESKTOP-IPT0J6H\\EtpAutomation`, `allowAutomationFolderAccess` `true`.

### B20. Folder permissions after (VM-ADMIN)

Run the B13 command again.

You should see: the same pattern as this PC after 17 September (`PHASE-4-AUDIT.md`, "A4.2 CLOSED on the shop PC", which lists the root, `Backups`, `Documents` and `Share`; the `SetupLogs` and `Operations` lines below come from `initialize-etp-operation-folders.ps1` lines 74-77):

- `C:\ProgramData\EtpReporting`: SYSTEM (F), Administrators (F), `EtpAutomation` (RX), `MSSQL$SQLEXPRESS` (RX)
- `Backups`, `Documents`, `Share`, `SetupLogs`: SYSTEM (F), Administrators (F), `EtpAutomation` (M), `MSSQL$SQLEXPRESS` (M)
- `Operations`: SYSTEM (F), Administrators (F), `EtpAutomation` (RX), `MSSQL$SQLEXPRESS` (RX)
- no `BUILTIN\Users`, and no `(I)` inherited entries

### B21. Run setup (VM-ADMIN)

This runs setup silently and clears the SQL Server option, because the VM already has SQL Server. If you left the option ticked, setup would still skip the SQL install, since the service exists (bootstrap line 207), but you would be accepting Microsoft's licence for nothing. Inno Setup's own log goes to the evidence folder.

```powershell
$setup = Start-Process -FilePath 'C:\EtpStaging\EtpReportingEngine-Setup-1.8.8-x64.exe' -ArgumentList '/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/MERGETASKS="!sqlprerequisites"', '/LOG="C:\EtpEvidence\setup-run1.log"' -Wait -PassThru; "Setup exit code: $($setup.ExitCode)"
```

You should see: a progress window, then `Setup exit code: 0`. The migrations and integrity checks run inside setup, so it can take several minutes.
If it fails: exit code `1603` is this product's "setup did not complete" code, and also its code for "ETP is running". Go to B22. Do not launch ETP until setup succeeds.

If you prefer the wizard, run the installer from the VM console, untick the SQL Server option, and untick "Launch" on the last page.

### B22. Read the setup log (VM-ADMIN)

First check that the newest log belongs to this setup run:

```powershell
Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\SetupLogs' -Filter 'bootstrap-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1 | Format-List Name, LastWriteTime
```

You should see: a `bootstrap-<date>-<time>.log` whose `LastWriteTime` is from the last few minutes.

**If there is no log, or the newest one is older than this run,** setup stopped before its log starts. The log is written only from line 229 of `bootstrap-etp-prerequisites.ps1` on (see line 133), after the SQL Server version check and setup's own run of the folder script. So a stop on `operations.json`, on "Resolve automation folder access in the protected machine configuration before bootstrap", on administrator rights, on the SQL service, on sqlcmd, on the SQL version, or in that folder-script run is never written to it. Do not read an older log as this run's result. Instead:

1. Read the marker setup leaves, with its time:

   ```powershell
   Get-Item -LiteralPath 'C:\Program Files\Saagar Traders\ETP Reporting Engine\SETUP-INCOMPLETE.txt' | ForEach-Object { $_.LastWriteTime; Get-Content -LiteralPath $_.FullName }
   ```

   You should see: a time from the last few minutes, then "Setup did not complete. Bootstrap exit code: 1." and two lines of advice. If the file does not exist, or its time is older than this run, setup never reached its post-install step: most likely it refused to start because ETP is running (exit code 1603, B21), or it failed while copying files. The Inno log `C:\EtpEvidence\setup-run1.log` says which. Record it; if ETP was running, close it and run B21 again.
2. Check the protected configuration the way setup reads it. This only reads; it runs in a child scope so it does not change this window.

   ```powershell
   & { . 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\etp-operations-common.ps1'; Get-EtpOperationsConfiguration | Format-List }
   ```

   You should see: the four `operations.json` values from B19, with `allowAutomationFolderAccess` `True`. A red error instead is the reason setup stopped: B17 did not complete, or the account it checks has changed. Record the message and stop.
3. If step 2 shows the configuration without an error, the stop was one of the other early checks: administrator rights (B4), sqlcmd (B5), the SQL version or edition (B6), the SQL service (B10), or the permissions of the installation folder. Record what you find and stop.

**If the log is from this run,** read it:

```powershell
Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\SetupLogs' -Filter 'bootstrap-*.log' | Sort-Object LastWriteTime | Select-Object -Last 1 | Get-Content -Tail 25
```

You should see one of these, then the same last three lines:

- Case 1: `The configured database does not exist. A clean database will be created; no pre-migration backup is applicable.`
- Case 2: `The existing database has no pending bundled migrations by journal count; ...`
- Case 3: `Existing database has pending bundled migrations. Creating and verifying a pre-migration backup ...`, then `Verified pre-migration backup is retained at ...`

and then:

```
EtpReporting migration completed and post-migration state, journal count, and DBCC integrity checks passed.
Daily backup, monthly recovery-drill and five-minute ETP automation tasks are installed.
ETP prerequisite bootstrap completed successfully.
```

The second of those three lines appears only if the recovery-drill task installer did not refuse. That installer runs the group-membership SQL administrator check, so this line is your first evidence for Part C.

**Case 4: the log says "Creating and verifying a pre-migration backup", then `FAILED: RuntimeException: The database operation failed. ...`.** The VM has no operations procedure for `EtpReporting` yet, so setup could not take its mandatory backup. It stopped before any migration, and the database is unchanged. The same line in case 3 most likely means the VM's procedure is from an older build; the steps below replace it with this build's. Do this: **SAGAR ONLY**

1. Run the module installer once. It creates the procedure, then stops at its first precondition, which is expected at this point. The failure keeps the procedure, and an administrator (so setup's pre-migration backup) can use it (`OPERATIONS.md` step 7).

   ```powershell
   & 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\install-etp-sql-operations.ps1' -ServerInstance '.\SQLEXPRESS' -Database 'EtpReporting' -AutomationPrincipal "$env:COMPUTERNAME\EtpAutomation"
   ```

   You should see: a red error ending "Add DESKTOP-IPT0J6H\EtpAutomation as a Store Manager in Settings > Users first. That creates the SQL login this module grants to."
2. Run B8 again. You should now see `etp_operations_31736fb143c6912a`.
3. Run setup again with a second log:

   ```powershell
   $setup = Start-Process -FilePath 'C:\EtpStaging\EtpReportingEngine-Setup-1.8.8-x64.exe' -ArgumentList '/SILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/MERGETASKS="!sqlprerequisites"', '/LOG="C:\EtpEvidence\setup-run2.log"' -Wait -PassThru; "Setup exit code: $($setup.ExitCode)"
   ```

4. Run B22 again. You should now see the case 3 lines and the three final lines.

**Any other `FAILED:` line:**

- If it names the migration (for example `Configured database migration failed with exit code ...`), the VM database may carry migrations from an older test build that differ from the committed ones. Do not edit the journal. Record it and stop. Starting the VM on a clean database is a separate decision this runbook does not cover.
- If it names the recovery drill (`The recovery drill needs a SQL administrator ...` or `Setup could not confirm ...`), that is the group-path check failing. Record the exact line: it is the main Part C result.
- A problem with `operations.json` or "Resolve automation folder access ..." never appears in this log; see "If there is no log" above.

### B23. Check the install (VM-ADMIN)

```powershell
Test-Path -LiteralPath 'C:\Program Files\Saagar Traders\ETP Reporting Engine\SETUP-INCOMPLETE.txt'
```

You should see: `False`. Setup writes this file only when it fails.

```powershell
(Get-Item -LiteralPath 'C:\Program Files\Saagar Traders\ETP Reporting Engine\Etp.Reporting.Desktop.exe').VersionInfo.ProductVersion
```

You should see: `1.8.8+` followed by the build's commit.

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -d EtpReporting -Q "SET NOCOUNT ON; SELECT COUNT(*) AS migrations, MAX(migration_id) AS latest FROM dbo.schema_migrations;"
```

You should see: the count from A4. With 32 migrations, `latest` is `0032_active_users_can_connect`.

### B24. Check the three tasks (VM-ADMIN)

Run the task command from B12 again.

You should see:

| TaskName | RunAs | Logon | RunLevel |
|---|---|---|---|
| ETP Reporting Automated Operations | EtpAutomation | S4U | Limited |
| ETP Reporting Daily Backup | EtpAutomation | S4U | Limited |
| ETP Reporting Monthly Recovery Drill | ETPTest | S4U | Highest |

None of them should run as SYSTEM. Task Scheduler shows a local account by its bare name (measured on this PC on 22 Sep); the full `DESKTOP-IPT0J6H\...` form is also fine. The plan's original A4.3 wording, "the same non-SYSTEM service principal" for all three, was superseded by the P4-15 decision that the drill runs as the Owner.

Automated Operations starts every five minutes. It fails at least until B25 gives `EtpAutomation` a SQL login, and may keep failing until B27 adds it to `etp_automation`. That is harmless.

### B25. Add `EtpAutomation` as a Store Manager (VM-APP) — **SAGAR ONLY**

First, in the VM-ADMIN window, print the exact name to type:

```powershell
"$env:COMPUTERNAME\EtpAutomation"
```

Then in the VM console:

1. Open **ETP Reporting Engine** from the Start menu. If it cannot connect, or Settings later says Owner permission is required, close it and start it with right-click > **Run as administrator**. On this VM your SQL administrator rights come through the Administrators group, which an ordinary window does not carry. Use the elevated app only for this step. I have not verified which of the two the VM needs.
2. Check Settings > Database: the connection must name `Database=EtpReporting`.
3. Open **Settings > Users**. Under "Windows-integrated users", fill in:
   - Windows user identity: the name printed above, for example `DESKTOP-IPT0J6H\EtpAutomation`
   - Display name: `ETP Automated Operations`
   - Application role: `Store Manager`
   - Active: ticked
   - Reason: `Dedicated automation account for scheduled backups and imports (acceptance VM)`
4. Select **Save user access**.

You should see: the account appears in the users list below the form, as Store Manager, active.
If it fails: record the message shown. "Owner permission is required for this change" means the account you are signed in with is not an active Owner in B9's list.

Once saved, do not save this account again later. Re-saving it removes its `etp_automation` and `db_backupoperator` memberships (documented in `OPERATIONS.md`), and B27 would have to be run again.

### B26. Check the new user in SQL (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -d EtpReporting -Q "SET NOCOUNT ON; SELECT u.windows_identity, u.role_code, u.is_active, IS_ROLEMEMBER('etp_store_manager', dp.name) AS store_manager_role, (SELECT COUNT(*) FROM sys.database_permissions p WHERE p.grantee_principal_id = dp.principal_id AND p.permission_name = 'CONNECT' AND p.state IN ('G','W')) AS can_connect FROM dbo.application_users u LEFT JOIN sys.database_principals dp ON dp.sid = SUSER_SID(u.windows_identity) WHERE u.windows_identity = N'$env:COMPUTERNAME\EtpAutomation';"
```

You should see: `DESKTOP-IPT0J6H\EtpAutomation | STORE_MANAGER | 1 | 1 | 1`. `can_connect` `1` is the P4-14 fix in migration 0032 working.

### B27. Install the SQL operations module (VM-ADMIN) — **SAGAR ONLY**

```powershell
& 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\install-etp-sql-operations.ps1' -ServerInstance '.\SQLEXPRESS' -Database 'EtpReporting' -AutomationPrincipal "$env:COMPUTERNAME\EtpAutomation"
```

You should see: `Restricted SQL backup and recovery module installed for EtpReporting. Backups on this edition: NONE. Verify it under the dedicated account before enabling tasks.`
If it fails:

| Message | Meaning |
|---|---|
| "Add ... as a Store Manager in Settings > Users first ..." | B25 did not save. |
| "The automation account cannot connect to the database ... migration 0032 ..." | Setup did not reach migration 0032. Go back to B23. |
| "This SQL Server edition encrypts backups, so it needs the backup certificate ..." | The VM is not Express. See Appendix B. |
| "Complete protected recovery-folder setup first." | `Backups\RecoveryDrill` is missing. Run B17 again. |
| "The installation folder can be changed by a non-administrator." | The install folder's permissions are wrong. Stop; do not loosen anything. |

A failed run keeps the procedure and removes the signer it created.

### B28. Check that the module is signed and granted (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -d master -Q "SET NOCOUNT ON; SELECT o.name AS broker, CASE WHEN EXISTS (SELECT 1 FROM master.sys.crypt_properties cp WHERE cp.class = 1 AND cp.major_id = o.object_id) THEN 'signed' ELSE 'NOT signed' END AS signature, (SELECT COUNT(*) FROM master.sys.database_permissions dp WHERE dp.class = 1 AND dp.major_id = o.object_id AND dp.permission_name = 'EXECUTE' AND dp.grantee_principal_id = DATABASE_PRINCIPAL_ID(N'$env:COMPUTERNAME\EtpAutomation')) AS automation_can_execute FROM master.sys.procedures o WHERE o.name LIKE N'etp[_]operations[_]%';"
```

You should see: `etp_operations_31736fb143c6912a | signed | 1`.

### B29. A backup taken by `EtpAutomation`, through its own task (VM-ADMIN)

`EtpAutomation` has a random password that nobody knows, so the only way to run something as that account is its S4U task. This is the first time a backup runs as `EtpAutomation` on any machine.

```powershell
Start-ScheduledTask -TaskName 'ETP Reporting Daily Backup'
```

Wait a minute, then:

```powershell
Get-ScheduledTaskInfo -TaskName 'ETP Reporting Daily Backup' | Select-Object LastRunTime, LastTaskResult, NextRunTime
```

You should see: `LastRunTime` a minute ago and `LastTaskResult` `0`. `267009` means it is still running, so wait and ask again.

```powershell
Get-Content -Raw -LiteralPath 'C:\ProgramData\EtpReporting\Backups\EtpReporting-latest-verified.json' | ConvertFrom-Json | Select-Object backupPath, sha256, lengthBytes, verifiedAtUtc, encryption, certificateThumbprint
```

You should see: `verifiedAtUtc` a few minutes ago, `encryption` `NONE`, and `certificateThumbprint` empty.

Expected side effect in cases 3 and 4: the backup's rotation keeps only the newest verified backup per day (plus one per month), so it deletes the pre-migration backup setup made in B21/B22 earlier the same UTC day (`Get-EtpRetainedBackupReceipts` in `etp-operations-common.ps1`). That is the product's normal rotation. The B15 safety copy, which is outside the `Backups` folder, is the one that protects the VM's pre-upgrade data; keep it.

```powershell
(Get-Acl -LiteralPath ((Get-Content -Raw -LiteralPath 'C:\ProgramData\EtpReporting\Backups\EtpReporting-latest-verified.json' | ConvertFrom-Json).backupPath + '.receipt.json')).Owner
```

You should see: `DESKTOP-IPT0J6H\EtpAutomation`. The account that wrote the receipt owns it, which shows the task, not you, took this backup. (Expected from how Windows assigns file owners; not yet observed on this product.)

If `LastTaskResult` is not `0`:

- `1` means the script stopped with an error. The task runs hidden, so its message is not visible. Use the diagnostic below.
- `2147943785` (hex `0x80070569`, "the user has not been granted the requested logon type") would mean `EtpAutomation` lacks the "Log on as a batch job" right that S4U tasks use. Granting it is a local security policy change that would also apply to the shop PC. Record it and decide; do not change it on the spot. I do not know whether this will happen.

**Diagnostic, only if the backup task failed.** This registers a temporary task that runs the same backup as `EtpAutomation` and writes its messages to a file. Delete the task afterwards.

```powershell
$diagArgs = '-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -Command "try { & ''C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\backup-etp-database.ps1'' *>&1 | Out-File -FilePath ''C:\ProgramData\EtpReporting\Backups\diagnostic-backup.txt'' } catch { $_ | Out-File -FilePath ''C:\ProgramData\EtpReporting\Backups\diagnostic-backup.txt'' -Append }"'
```

```powershell
Register-ScheduledTask -TaskName 'ETP Diagnostic Backup - delete after use' -Action (New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument $diagArgs) -Principal (New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\EtpAutomation" -LogonType S4U -RunLevel Limited) -Force
```

```powershell
Start-ScheduledTask -TaskName 'ETP Diagnostic Backup - delete after use'
```

Wait a minute, then read the file and remove the temporary task:

```powershell
Get-Content -LiteralPath 'C:\ProgramData\EtpReporting\Backups\diagnostic-backup.txt'
```

```powershell
Unregister-ScheduledTask -TaskName 'ETP Diagnostic Backup - delete after use' -Confirm:$false
```

Copy the message into the evidence, then delete the file:

```powershell
Remove-Item -LiteralPath 'C:\ProgramData\EtpReporting\Backups\diagnostic-backup.txt'
```

### B30. The recovery drill, run by you as the Owner (VM-ADMIN)

This is the same step as on this PC: the drill restores a full copy and runs `DBCC CHECKDB` on it, which only a SQL administrator may do (P4-15).

```powershell
& 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\invoke-etp-recovery-drill.ps1'
```

You should see: `Receipt-verified recovery drill completed.`

```powershell
Get-Content -Raw -LiteralPath 'C:\ProgramData\EtpReporting\Backups\EtpReporting-latest-drill.json' | ConvertFrom-Json
```

You should see: `succeeded` `True`, `backupSha256` equal to the `sha256` from B29, and `completedAtUtc` a minute ago.

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -Q "SET NOCOUNT ON; SELECT name AS leftover_drill_database FROM sys.databases WHERE name LIKE N'EtpRecovery[_]%';"
```

You should see: no rows. The drill drops its own copy.

If the drill fails:

| Message | Meaning |
|---|---|
| "Access to the path ... is denied" or "Attempted to perform an unauthorized operation" | The window is not elevated, so it cannot read `operations.json`. The drill's own "only a SQL administrator can do" message never reaches the screen: the script hides every SQL message. |
| "Backup metadata does not match the verification receipt." | Stop; keep the files as they are. |
| "The database operation failed. ..." | This hides the SQL message. The command below runs only the read-only metadata check and shows its real message. |

To see the real SQL message of the metadata check:

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -Q "EXEC master.dbo.[etp_operations_31736fb143c6912a] 'METADATA', N'$([IO.Path]::GetFileName((Get-Content -Raw -LiteralPath 'C:\ProgramData\EtpReporting\Backups\EtpReporting-latest-verified.json' | ConvertFrom-Json).backupPath))';"
```

You should see: a line starting `ETP_METADATA:` if the metadata check itself is fine. In that case the failure came later, in the restore and `DBCC CHECKDB` of the copy, or in recording the result. If `EtpReporting-latest-drill.json` has a new `completedAtUtc` although the drill reported a failure, the restore passed and only the recording (as `EtpAutomation`'s database user) failed. That needs `EtpAutomation` in the database (B25) and in `etp_automation` (B27), and the database not marked TRUSTWORTHY. Record what you saw and stop.

---

## Part C. What to verify specifically on the VM

This PC's Owner has its own SQL administrator login, so this PC never takes the group path. The VM is where review finding 1's fix meets the real case.

### C1. The group path is what is being tested (from B6 and B7)

This step has no new command. It passes when all of these hold:

- B6, elevated: `sysadmin_now` `1`, `sysadmin_by_name` `NULL` or `0`, and `sys.login_token` lists `BUILTIN\Administrators` with usage `GRANT OR DENY`;
- B7: `ETPTest` does not appear by name as a SQL administrator.

### C2. Without elevation, the right disappears (VM-NORMAL)

Open an ordinary Windows PowerShell window in the VM console, not "Run as administrator".

```powershell
$sqlcmd = @("$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE", "$env:ProgramFiles\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1; & $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -W -s '|' -Q "SET NOCOUNT ON; SELECT SUSER_SNAME() AS me, IS_SRVROLEMEMBER('sysadmin') AS sysadmin_now; SELECT name, type, usage FROM sys.login_token;"
```

You should see one of these:

- a "Login failed for user 'DESKTOP-IPT0J6H\ETPTest'" error. This is the most likely result if the Owner has no SQL login of its own;
- or `sysadmin_now` `0`, with `BUILTIN\Administrators` still in the token list but with usage `DENY ONLY`. The group's name staying in the list is expected (measured on this PC on 22 September, not elevated). What matters is `DENY ONLY` and `sysadmin_now` `0`.

Either one shows that an unelevated window cannot use the Administrators group. That is why the drill task must run at **Highest**, not Limited. If you see `BUILTIN\Administrators` with `GRANT OR DENY` here, or `sysadmin_now` `1`, this window is elevated after all, or the Owner has its own administrator login. Record it. Close this window afterwards.

### C3. The drill-task installer's own check, run directly (VM-ADMIN)

Setup already ran this (B22's middle line). Running it again shows its message. It keeps the account the drill already runs as, and replaces the task with identical settings.

```powershell
& 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\install-monthly-recovery-drill-task.ps1'
```

You should see: `Scheduled operation installed. The monthly recovery drill runs as DESKTOP-IPT0J6H\ETPTest.`

This is the check that asks SQL about the real, elevated token of the account running setup. Before the fix it asked by login name, which returns `NULL` for an account that is a SQL administrator only through a Windows group. That `NULL` was measured on this PC with a simulated group (review finding 1 in `WORKING-STATE-2026-09-22.md`), never on this VM. This step is the first test of the fix on a real group-only Owner.

If it fails:

| Message | Meaning |
|---|---|
| "The recovery drill needs a SQL administrator, and ... is not one." | In an elevated window, the group-path fix does not work on the real case. Record it word for word. This is a finding. (A window that is not elevated fails earlier, with an "access denied" or "unauthorized operation" error on `operations.json`.) |
| "Setup could not confirm that ... is a SQL administrator." | The script took its other branch, because the existing drill task belongs to a different account. Record which account (B12, B24). |

### C4. The task's principal (VM-ADMIN)

```powershell
Get-ScheduledTask -TaskName 'ETP Reporting Monthly Recovery Drill' | Select-Object TaskName, State, @{Name='RunAs'; Expression={$_.Principal.UserId}}, @{Name='Logon'; Expression={$_.Principal.LogonType}}, @{Name='RunLevel'; Expression={$_.Principal.RunLevel}}, @{Name='Arguments'; Expression={$_.Actions.Arguments}}
```

You should see: `ETPTest | S4U | Highest`, `State` `Ready`, and arguments ending `-DayOfMonth 1`.

### C5. The drill really runs through the task, at Highest (VM-ADMIN)

Registration alone proves nothing (`WORKING-STATE-2026-09-22.md` says: confirm the task starts, not only that it registers). The runner does nothing unless today is the configured day (`invoke-monthly-recovery-drill-runner.ps1` line 3). So set the day to today, run the task, then set it back to 1. This works only on days 1 to 28; the script refuses 29 to 31.

```powershell
(Get-Date).Day
```

If it is 29, 30 or 31, skip C5 today, record that it was skipped and why, and do it on a day from 1 to 28. The drill then uses whatever verified backup is latest on that day (the daily backup task runs at 22:00), which is fine: the check below compares `completedAtUtc`, not the backup.

If that is 28 or less:

```powershell
& 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\install-monthly-recovery-drill-task.ps1' -DayOfMonth (Get-Date).Day
```

```powershell
Start-ScheduledTask -TaskName 'ETP Reporting Monthly Recovery Drill'
```

Wait a minute or two, then:

```powershell
Get-ScheduledTaskInfo -TaskName 'ETP Reporting Monthly Recovery Drill' | Select-Object LastRunTime, LastTaskResult
```

You should see: `LastTaskResult` `0` (`267009` means it is still running).

```powershell
Get-Content -Raw -LiteralPath 'C:\ProgramData\EtpReporting\Backups\EtpReporting-latest-drill.json' | ConvertFrom-Json
```

You should see: `completedAtUtc` later than in B30. That proves the drill ran inside the task, under S4U at Highest, and not just that the task started.

Put the day back:

```powershell
& 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\install-monthly-recovery-drill-task.ps1' -DayOfMonth 1
```

Then run C4 again. The arguments must end `-DayOfMonth 1`.

If `LastTaskResult` is `1` and `completedAtUtc` did not move, the drill failed inside the task, most likely because the S4U token at Highest did not carry the Administrators group. That is a finding: record it. Your B30 drill still counts as the Owner's drill.

### C6. Optional: what another administrator's setup would see (VM-ADMIN)

This runs the script's other probe, the one used when the drill account is not the account running setup, against your own account. It only impersonates and reads.

```powershell
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().Name; & $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -W -Q "SET NOCOUNT ON; BEGIN TRY EXECUTE AS LOGIN = N'$owner'; DECLARE @r varchar(1) = CONVERT(varchar(1), COALESCE(IS_SRVROLEMEMBER('sysadmin'), 0)); REVERT; SELECT 'ETP_SYSADMIN:' + @r AS probe; END TRY BEGIN CATCH SELECT 'ETP_SYSADMIN:? ' + ERROR_MESSAGE() AS probe; END CATCH;"
```

Record the result. I do not know which one this VM gives:

- `ETP_SYSADMIN:1` means a different administrator could later re-register the drill for you.
- `ETP_SYSADMIN:?` means they would be refused with "Setup could not confirm ...". The script fails closed there, which is safe, but it is worth knowing.

### C7. No `BUILTIN\Users` anywhere (VM-ADMIN)

```powershell
'C:\ProgramData\EtpReporting', 'C:\ProgramData\EtpReporting\Backups', 'C:\ProgramData\EtpReporting\Documents', 'C:\ProgramData\EtpReporting\Share', 'C:\ProgramData\EtpReporting\SetupLogs', 'C:\ProgramData\EtpReporting\Operations' | ForEach-Object { icacls $_ } | Select-String -SimpleMatch 'BUILTIN\Users'
```

You should see: no output.

### C8. Optional: Automated Operations now works (VM-ADMIN)

```powershell
Get-ScheduledTaskInfo -TaskName 'ETP Reporting Automated Operations' | Select-Object LastRunTime, LastTaskResult
```

Record the result of a run after B27. I have not established which exit code `--automation-once` returns when there is nothing to import.

---

## Part D. Second-machine restore: this PC's backup, restored in the VM

What `OPERATIONS.md` requires ("Second-machine recovery exercise"): an isolated second machine, a compatible SQL version, the live database untouched, and a restore to a new name with `MOVE` for every file and never `WITH REPLACE`. Then `TRUSTWORTHY` and `DB_CHAINING` off, `DBCC CHECKDB`, a comparison of the file metadata, and a look at the migration journal.

What does not apply here: this PC runs Express, so its backups are unencrypted and their receipts say `"encryption": "NONE"`. The "Recovery evidence" row says of Express: "it is unencrypted and needs none". So `OPERATIONS.md` step 2 (master key and certificate import), the custody hash checks in step 1, and "the second custody copy" in step 5 do not apply. The plan's original D9 wording, a restore "from the exported certificate", was revised for Express.

Why not use the product's drill: `OPERATIONS.md` says it "is not a second-machine restore tool: its configuration and receipts refer to the original machine's paths." Also, keep this PC's backup out of the VM's own `Backups` folder. The file names and database name are the same, so the VM's rotation would treat the copy as one of its own backups and could delete it.

This part does not depend on Part B, or on the VM's edition. It needs Part A's session `$s`, and a VM-ADMIN window where you are SQL administrator (B6) and `$sqlcmd` is set (B5; run B5 again in a new window).

### D1. A protected restore folder in the VM (VM-ADMIN)

The SQL service needs to read the backup and write the restored files. Nobody else may read them: they are the shop's data, unencrypted.

```powershell
New-Item -ItemType Directory -Path 'C:\EtpSecondMachineRestore\Data' -Force
```

```powershell
icacls 'C:\EtpSecondMachineRestore' /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' 'NT SERVICE\MSSQL$SQLEXPRESS:(OI)(CI)M'
```

You should see: `Successfully processed 1 files; Failed processing 0 files`.

Only if B10 showed a different `StartName`: also run this, with that account in place of `<StartName>`, and record it.

```powershell
icacls 'C:\EtpSecondMachineRestore' /grant '<StartName>:(OI)(CI)M'
```

### D2. Read this PC's latest verified receipt (HOST-ADMIN)

```powershell
$latest = Get-Content -Raw -LiteralPath 'C:\ProgramData\EtpReporting\Backups\EtpReporting-latest-verified.json' | ConvertFrom-Json; $latest | Select-Object database, serverInstance, backupPath, sha256, lengthBytes, verifiedAtUtc, encryption, certificateThumbprint
```

You should see: `database` `EtpReporting`, `encryption` `NONE`, `certificateThumbprint` empty, and `backupPath` like `C:\ProgramData\EtpReporting\Backups\EtpReporting-<yyyyMMdd-HHmmss>-<32 characters>.bak`. `verifiedAtUtc` should be after this PC's upgrade.
If it fails: "Access is denied" means the window is not elevated. If the file is missing, this PC has no product-made backup yet. Finish this PC first.

### D3. Check the backup against its receipt on this PC (HOST-ADMIN)

```powershell
(Get-FileHash -LiteralPath $latest.backupPath -Algorithm SHA256).Hash -eq $latest.sha256 -and (Get-Item -LiteralPath $latest.backupPath).Length -eq $latest.lengthBytes
```

You should see: `True`.
If it fails: `False` means stop. Do not use this backup, and follow the "Backup or metadata hash mismatch" row in `OPERATIONS.md`.

### D4. Copy the backup and its own receipt into the VM (HOST-ADMIN)

The copy uses the per-backup receipt `<backup>.receipt.json`, which is never overwritten. `...-latest-verified.json` is only a pointer that changes with every backup. The originals stay where they are.

```powershell
Copy-Item -ToSession $s -LiteralPath $latest.backupPath, ($latest.backupPath + '.receipt.json') -Destination 'C:\EtpSecondMachineRestore\'
```

You should see: no output.
If it fails: if `$s` is gone (for example you closed the window), repeat A2, A8 and A9 first. "Cannot find path" for the `.receipt.json` means the receipt is missing. Stop.

### D5. Row counts on this PC now, for comparison later (HOST-ADMIN)

This is a read-only query on this PC's live database. Run it straight after D4 and note the time.

```powershell
& 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE' -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -d EtpReporting -Q "SET NOCOUNT ON; SELECT 'sales_invoices' AS table_name, COUNT_BIG(*) AS row_count FROM dbo.sales_invoices UNION ALL SELECT 'sales_lines', COUNT_BIG(*) FROM dbo.sales_lines UNION ALL SELECT 'sales_tenders', COUNT_BIG(*) FROM dbo.sales_tenders UNION ALL SELECT 'stock_movements', COUNT_BIG(*) FROM dbo.stock_movements UNION ALL SELECT 'stock_snapshots', COUNT_BIG(*) FROM dbo.stock_snapshots UNION ALL SELECT 'source_lineage', COUNT_BIG(*) FROM dbo.source_lineage UNION ALL SELECT 'import_batches', COUNT_BIG(*) FROM dbo.import_batches UNION ALL SELECT 'schema_migrations', COUNT_BIG(*) FROM dbo.schema_migrations; SELECT MAX(completed_utc) AS newest_completed_import_utc FROM dbo.import_batches WHERE status = 'Completed';"
```

You should see: eight counts and one time. Write them down (E2).

### D6. See what arrived in the VM (VM-ADMIN)

```powershell
Get-ChildItem -LiteralPath 'C:\EtpSecondMachineRestore' -File | Select-Object Name, Length
```

You should see: exactly two files, `EtpReporting-....bak` and `EtpReporting-....bak.receipt.json`.

### D7. Set the restore variables (VM-ADMIN)

```powershell
$receiptPath = (Get-ChildItem -LiteralPath 'C:\EtpSecondMachineRestore' -Filter '*.bak.receipt.json' -File | Select-Object -First 1).FullName; $bak = $receiptPath -replace '\.receipt\.json$', ''; $receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json; $bak
```

```powershell
$newDb = 'EtpRestoreCheck_' + (Get-Date -Format 'yyyyMMdd'); $dataDir = 'C:\EtpSecondMachineRestore\Data'; $newDb
```

You should see: the full path of the `.bak`, then a name like `EtpRestoreCheck_20260923`.

### D8. Check the copy against the receipt, inside the VM (VM-ADMIN)

```powershell
$receipt | Select-Object database, sha256, lengthBytes, verifiedAtUtc, encryption, certificateThumbprint
```

```powershell
(Get-FileHash -LiteralPath $bak -Algorithm SHA256).Hash -eq $receipt.sha256 -and (Get-Item -LiteralPath $bak).Length -eq $receipt.lengthBytes
```

You should see: the same values as D2, then `True`.
If it fails: `False` means the copy is damaged. Delete both copied files and repeat D4.

### D9. Version, rights, and a free name (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -Q "SET NOCOUNT ON; SELECT CONVERT(varchar(20), SERVERPROPERTY('ProductVersion')) AS vm_version, IS_SRVROLEMEMBER('sysadmin') AS sysadmin_now, DB_ID(N'$newDb') AS new_name_in_use;"
```

You should see: a 16.0 version at least `16.0.1000.6`, `1`, and `NULL`.
If it fails: if the name is in use, pick another in D7, for example with `_2` on the end. SQL Server cannot restore a backup onto an older version.

### D10. Is the backup readable and are its checksums good? (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -Q "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM;"; "Exit code: $LASTEXITCODE"
```

You should see: `The backup set on file 1 is valid.` and `Exit code: 0`.

### D11. List every file inside the backup (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -Q "RESTORE FILELISTONLY FROM DISK = N'$bak';"
```

You should see: one row per file. For this PC's database I expect two:

- `LogicalName` `EtpReporting`, `Type` `D`, `FileId` `1`
- `EtpReporting_log`, `L`, `2`

Their `PhysicalName`s are under `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\`. That is what this PC's live database reported on 22 September; the backup's own list is what counts.

If there are more rows, or different logical names, D14's command needs one `MOVE` per row with those names (see Appendix A).

Why `MOVE` matters: without it, SQL Server puts each file back at the `PhysicalName` recorded in the backup. Compare with B8: on the VM that is very likely exactly where the VM's own live `EtpReporting` keeps its files. Without `REPLACE`, SQL refuses to overwrite them. With `REPLACE`, it could destroy them. So: a `MOVE` for every file, and never `REPLACE`.

### D12. Prepare the file-list helper (VM-ADMIN)

This holds the file list in a temporary table, the same way the product's operations procedure does, so the next two steps can compare it exactly. It only sets a variable.

```powershell
$fileTable = "CREATE TABLE #f (LogicalName nvarchar(128), PhysicalName nvarchar(260), Type char(1), FileGroupName nvarchar(128), Size numeric(20,0), MaxSize numeric(20,0), FileId bigint, CreateLSN numeric(25,0), DropLSN numeric(25,0), UniqueId uniqueidentifier, ReadOnlyLSN numeric(25,0), ReadWriteLSN numeric(25,0), BackupSizeInBytes bigint, SourceBlockSize int, FileGroupId int, LogGroupGUID uniqueidentifier, DifferentialBaseLSN numeric(25,0), DifferentialBaseGUID uniqueidentifier, IsReadOnly bit, IsPresent bit, TDEThumbprint varbinary(32), SnapshotURL nvarchar(360)); INSERT #f EXEC (N'RESTORE FILELISTONLY FROM DISK = N''$bak''');"
```

### D13. Compare the backup's file list with the receipt (VM-ADMIN)

The receipt's `files` list was made from this same query on this PC. The comparison is the one `invoke-etp-recovery-drill.ps1` makes (lines 21-23).

```powershell
$fromBackup = @(((& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -h -1 -y 0 -Q "SET NOCOUNT ON; $fileTable SELECT LogicalName AS logicalName, Type AS type, FileId AS fileId, Size AS sizeBytes, UniqueId AS uniqueId FROM #f ORDER BY FileId FOR JSON PATH;") -join '').Trim() | ConvertFrom-Json)
```

```powershell
($receipt.files | Sort-Object fileId | ConvertTo-Json -Depth 5 -Compress) -ceq ($fromBackup | Sort-Object fileId | ConvertTo-Json -Depth 5 -Compress)
```

You should see: `True`.
If it fails: `False` means print both sides (`$receipt.files` and `$fromBackup`) into the evidence, and stop. The file does not match its receipt. If the first command errors with "Column name or number of supplied values does not match table definition", the VM's SQL version lists files in a different shape. Compare D11's output with `$receipt.files` by eye instead, and record that you did.

### D14. Restore under the new name (VM-ADMIN)

The first half refuses to run if the name is taken. Both files are moved into `C:\EtpSecondMachineRestore\Data`. There is no `REPLACE`.

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -Q "IF DB_ID(N'$newDb') IS NOT NULL THROW 50001, N'That database name is already in use. Choose another name.', 1; RESTORE DATABASE [$newDb] FROM DISK = N'$bak' WITH MOVE N'EtpReporting' TO N'$dataDir\$newDb.mdf', MOVE N'EtpReporting_log' TO N'$dataDir\${newDb}_log.ldf', CHECKSUM, RECOVERY, STATS = 10;"; "Exit code: $LASTEXITCODE"
```

You should see: progress in steps of 10 percent, then `RESTORE DATABASE successfully processed ... pages in ... seconds`, then `Exit code: 0`.
If it fails:

- "Logical file 'X' is not part of database" means D11 showed different names. Change the `MOVE` names to match.
- "Operating system error 5" means the SQL service cannot read or write the folder. Check D1.
- A message about files that "cannot be overwritten" means something already exists at the target. Do not add `REPLACE`; choose a new name in D7.

### D15. The copy must not trust cross-database access (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -d $newDb -Q "SET NOCOUNT ON; SELECT name, state_desc, user_access_desc, is_trustworthy_on, is_db_chaining_on, is_auto_close_on, collation_name FROM sys.databases WHERE name = N'$newDb';"
```

You should see: `ONLINE | MULTI_USER | 0 | 0 | 1 | Latin1_General_CI_AS`. That collation is this PC's database collation (checked 22 September). The copy keeps it even if the VM's server collation (B6) differs. `is_auto_close_on` `1` is expected: this PC's `EtpReporting` has AUTO_CLOSE on (checked 22 September), and the copy keeps it. That is also why this command connects to the copy with `-d`: while an AUTO_CLOSE database is closed, SQL Server shows its `collation_name` as `NULL`.

`OPERATIONS.md` step 4 says to set both options off in any case:

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -Q "ALTER DATABASE [$newDb] SET TRUSTWORTHY OFF; ALTER DATABASE [$newDb] SET DB_CHAINING OFF;"; "Exit code: $LASTEXITCODE"
```

You should see: `Exit code: 0`.

### D16. Integrity check (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -Q "DBCC CHECKDB ([$newDb]) WITH NO_INFOMSGS, ALL_ERRORMSGS;"; "Exit code: $LASTEXITCODE"
```

You should see: no lines at all, then `Exit code: 0`. With `NO_INFOMSGS`, silence means no errors.
If it fails: any message is a failed restore test. Copy it into the evidence in full, and stop. Do not run a repair option.

### D17. Compare the restored files with the backup (VM-ADMIN)

This uses the same rule as the product's drill: every file ID and logical name in the backup appears in the restored database, and nothing else. The explicit `COLLATE` avoids a collation clash between the temporary table and the copy if the VM's server collation differs.

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -Q "SET NOCOUNT ON; $fileTable SELECT file_id, name, type_desc, physical_name, CONVERT(bigint, size) * 8192 AS size_bytes FROM [$newDb].sys.database_files ORDER BY file_id; SELECT CASE WHEN EXISTS (SELECT FileId, LogicalName COLLATE Latin1_General_100_BIN2 FROM #f EXCEPT SELECT file_id, name COLLATE Latin1_General_100_BIN2 FROM [$newDb].sys.database_files) OR EXISTS (SELECT file_id, name COLLATE Latin1_General_100_BIN2 FROM [$newDb].sys.database_files EXCEPT SELECT FileId, LogicalName COLLATE Latin1_General_100_BIN2 FROM #f) THEN 'FILES DIFFER' ELSE 'FILES MATCH' END AS verdict;"
```

You should see: the restored files, each with its `physical_name` under `C:\EtpSecondMachineRestore\Data\`, and then `FILES MATCH`. Also compare `size_bytes` with the receipt's `sizeBytes` and record whether they are equal. The product does not compare sizes, and I have not checked whether a restore always reproduces them exactly.

### D18. What SQL Server recorded about the restore (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -Q "SET NOCOUNT ON; SELECT TOP (1) rh.restore_date, rh.destination_database_name, bs.server_name, bs.database_name, bs.backup_start_date, bs.backup_finish_date, bs.is_copy_only, bs.has_backup_checksums, bs.key_algorithm, bs.encryptor_type, bs.software_major_version, bs.software_build_version FROM msdb.dbo.restorehistory rh JOIN msdb.dbo.backupset bs ON bs.backup_set_id = rh.backup_set_id WHERE rh.destination_database_name = N'$newDb' ORDER BY rh.restore_date DESC;"
```

You should see: `server_name` `DESKTOP-6IBM1J5\SQLEXPRESS`, `database_name` `EtpReporting`, `is_copy_only` `1`, `has_backup_checksums` `1`, `key_algorithm` and `encryptor_type` `NULL` (unencrypted, as the receipt says), and software version 16.

### D19. The copy's migration journal and row counts (VM-ADMIN)

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -d $newDb -Q "SET NOCOUNT ON; SELECT COUNT(*) AS migrations, MAX(migration_id) AS latest, MAX(applied_utc) AS last_applied_utc FROM dbo.schema_migrations;"
```

You should see: `32` and `0032_active_users_can_connect` if this PC's backup was taken after its upgrade. `31` means it was taken before. The restore is still valid, but record which it was.

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -W -s '|' -d $newDb -Q "SET NOCOUNT ON; SELECT 'sales_invoices' AS table_name, COUNT_BIG(*) AS row_count FROM dbo.sales_invoices UNION ALL SELECT 'sales_lines', COUNT_BIG(*) FROM dbo.sales_lines UNION ALL SELECT 'sales_tenders', COUNT_BIG(*) FROM dbo.sales_tenders UNION ALL SELECT 'stock_movements', COUNT_BIG(*) FROM dbo.stock_movements UNION ALL SELECT 'stock_snapshots', COUNT_BIG(*) FROM dbo.stock_snapshots UNION ALL SELECT 'source_lineage', COUNT_BIG(*) FROM dbo.source_lineage UNION ALL SELECT 'import_batches', COUNT_BIG(*) FROM dbo.import_batches UNION ALL SELECT 'schema_migrations', COUNT_BIG(*) FROM dbo.schema_migrations; SELECT MAX(completed_utc) AS newest_completed_import_utc FROM dbo.import_batches WHERE status = 'Completed';"
```

You should see: the same numbers as D5. The one allowed difference is growth on this PC from an import completed after the backup's `verifiedAtUtc`, which D5's newest-import time shows. `operational_audit` is deliberately left out, because every backup and drill adds audit rows after the backup is taken.

Do not point the VM's ETP application at this copy. Its users belong to this PC's accounts and have no meaning on the VM.

---

## Part E. Evidence to record, and where

**Where to keep it:**

- **Raw evidence** stays in protected folders that only Administrators and SYSTEM can open: `C:\EtpEvidence` in the VM (transcript, Inno logs), then copied to `C:\EtpEvidence\VM-186` on this PC (F2). Receipts contain paths, so they belong here, not in a support package (`OPERATIONS.md`).
- **The summary** is the table below with the results filled in. It goes in a new file, `docs/audit/VM-AND-SECOND-MACHINE-EVIDENCE-<yyyy-mm-dd>.md`. Record values, not raw files: hashes, file names, versions, counts, exact messages. Never passwords, and no customer data.
- **After success, three records change:** `WORKING-STATE-2026-09-22.md` Phase 4 remaining item 3; the "Recovery evidence" row in `OPERATIONS.md`; and the "Required deployment evidence; not yet verified here" line under its "Second-machine recovery exercise". Change `OPERATIONS.md` only when the evidence is complete.

### E1. VM install (Parts A to C)

| # | Evidence | From | Passes when |
|---|---|---|---|
| 1 | Installer and the two staged scripts: SHA-256 on this PC and in the VM, plus `SHA256SUMS.txt` | A13, A15 | all equal |
| 2 | VM name, account, elevated, clock difference | A10, B4 | as expected |
| 3 | SQL version, edition, server collation | B6 | Express 16.0, at least 16.0.1000.6 |
| 4 | `sysadmin_now`, `sysadmin_by_name`, `own_login`, `login_token` with `usage`; sysadmin member list | B6, B7 | group path: 1, NULL or 0, Administrators present as `GRANT OR DENY`, Owner not named |
| 5 | Unelevated result | C2 | login failed, or 0 with Administrators only `DENY ONLY` |
| 6 | Before state: databases, procedure, certificates, journal count, users, tasks | B8, B9, B12 | recorded |
| 7 | Safety copy file name, if taken | B15 | "valid" |
| 8 | Folder script output; `EtpAutomation` enabled and not an administrator; `operations.json` | B17 to B19 | as expected |
| 9 | ACLs before and after; no `BUILTIN\Users` | B13, B20, C7 | as expected |
| 10 | Setup exit code(s); which case; last 25 log lines; Inno log file names | B21, B22 | 0 and "completed successfully" |
| 11 | Installed version; journal count; `SETUP-INCOMPLETE.txt` absent | B23 | count equals A4 |
| 12 | Three tasks with RunAs, Logon, RunLevel | B24, C4 | as in the B24 table |
| 13 | Store Manager row and SQL check | B25, B26 | `STORE_MANAGER` 1 1 1 |
| 14 | Module output; signed; execute grant | B27, B28 | `NONE`, signed, 1 |
| 15 | Backup through the task: result, receipt `sha256`, `verifiedAtUtc`, `encryption`, receipt owner | B29 | 0, `NONE`, owner `EtpAutomation` |
| 16 | Owner's drill: output; `latest-drill.json`; no leftovers | B30 | completed; hash matches 15 |
| 17 | Drill-task installer message | C3 | "runs as DESKTOP-IPT0J6H\ETPTest" |
| 18 | Drill through the task at Highest: result; new `completedAtUtc`; day put back to 1 | C5 | 0; time moved; `-DayOfMonth 1` |
| 19 | Optional other-branch probe | C6 | recorded as 1 or ? |

### E2. Second-machine restore (Part D)

| # | Evidence | From | Passes when |
|---|---|---|---|
| 20 | Source: this PC's SQL version (16.0.1000.6 Express, checked 22 Sep), backup file name, `sha256`, `lengthBytes`, `verifiedAtUtc`, `encryption` | D2 | `NONE`, no thumbprint |
| 21 | Hash and length check on this PC, then in the VM | D3, D8 | `True`, `True` |
| 22 | This PC's row counts and the time they were taken | D5 | recorded |
| 23 | VM version; name free | D9 | 16.0 at least 16.0.1000.6; `NULL` |
| 24 | `VERIFYONLY` result | D10 | "valid" |
| 25 | File list from the backup; comparison with the receipt | D11, D13 | `True` |
| 26 | Restored database name; pages and seconds | D14 | exit 0 |
| 27 | `TRUSTWORTHY` and `DB_CHAINING` | D15 | 0 and 0 |
| 28 | `CHECKDB` | D16 | no output, exit 0 |
| 29 | Restored files, their physical paths and sizes; verdict | D17 | `FILES MATCH`, paths under `Data` |
| 30 | msdb restore record | D18 | copy-only, checksums, not encrypted, from `DESKTOP-6IBM1J5\SQLEXPRESS` |
| 31 | Journal count and latest; row counts compared with 22 | D19 | equal, or explained by later imports |
| 32 | Clean-up done, or kept on purpose, with date | F4, F5 | recorded |

---

## Part F. Finish

### F1. Stop the transcript and gather the logs in the VM (VM-ADMIN)

```powershell
Copy-Item -Path 'C:\ProgramData\EtpReporting\SetupLogs\bootstrap-*.log' -Destination 'C:\EtpEvidence\'
```

```powershell
Stop-Transcript
```

Open the transcript and check that the `sqlcmd` results are in it. If they are not, copy them into the evidence table by hand.

### F2. Copy the VM's evidence to this PC (HOST-ADMIN)

The new folder inherits the protection set in A12.

```powershell
New-Item -ItemType Directory -Path 'C:\EtpEvidence\VM-186' -Force
```

```powershell
Copy-Item -FromSession $s -Path 'C:\EtpEvidence\*' -Destination 'C:\EtpEvidence\VM-186\' -Recurse
```

You should see: no output from the copy. The transcript, bootstrap logs and Inno logs are now in `C:\EtpEvidence\VM-186` on this PC.

### F3. Keep

- the VM's safety copy from B15, until you decide the VM work is finished;
- everything on this PC. This runbook only copied from it.

### F4. Remove the test database, when the evidence is recorded (VM-ADMIN) — **SAGAR ONLY**

The guard refuses anything that is not a restore-check database, so a typo cannot drop `EtpReporting`. Dropping a database also deletes its files in `C:\EtpSecondMachineRestore\Data`.

```powershell
& $sqlcmd -S 'lpc:.\SQLEXPRESS' -E -b -Q "IF N'$newDb' NOT LIKE N'EtpRestoreCheck[_]%' THROW 50002, N'Refusing: this is not a restore-check database.', 1; ALTER DATABASE [$newDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$newDb];"; "Exit code: $LASTEXITCODE"
```

You should see: `Exit code: 0`.

### F5. Remove the copied backup from the VM (VM-ADMIN) — **SAGAR ONLY**

This deletes the VM's copy only. The original stays on this PC.

```powershell
Remove-Item -LiteralPath $bak, $receiptPath
```

### F6. Close the session and, if you want the memory back, save the VM (HOST-ADMIN)

```powershell
Remove-PSSession -Session $s
```

```powershell
Save-VM -Name $vm
```

Not part of this runbook: migration 0032 gives `NT AUTHORITY\SYSTEM` back its database access. `WORKING-STATE-2026-09-22.md` suggests deactivating SYSTEM in Settings > Users once the tasks run as `EtpAutomation`. Decide that separately.

---

## Appendix A. The restore T-SQL, with placeholders

These are the statements Part D runs, written out for reading or for SQL Server Management Studio. Run them as a SQL administrator on the VM, connected to `master`. In SSMS, keep the guard and the `RESTORE` in the **same batch**, with no `GO` between them; otherwise the guard cannot stop the restore.

```sql
-- 1. The backup is readable and its page checksums are good.
RESTORE VERIFYONLY FROM DISK = N'<backup path>' WITH CHECKSUM;

-- 2. Every file inside the backup. Step 3 needs one MOVE per row.
RESTORE FILELISTONLY FROM DISK = N'<backup path>';

-- 3. Restore under a new name. Never add REPLACE.
IF DB_ID(N'<new database>') IS NOT NULL
    THROW 50001, N'That database name is already in use. Choose another name.', 1;
RESTORE DATABASE [<new database>]
    FROM DISK = N'<backup path>'
    WITH MOVE N'<logical data name>' TO N'<data folder>\<new database>.mdf',
         MOVE N'<logical log name>'  TO N'<data folder>\<new database>_log.ldf',
         CHECKSUM, RECOVERY, STATS = 10;

-- 4. The copy must not trust cross-database access.
ALTER DATABASE [<new database>] SET TRUSTWORTHY OFF;
ALTER DATABASE [<new database>] SET DB_CHAINING OFF;

-- 5. Integrity. With NO_INFOMSGS, no output means no errors.
DBCC CHECKDB ([<new database>]) WITH NO_INFOMSGS, ALL_ERRORMSGS;

-- 6. What was restored, and where. Compare file_id and name with the receipt's fileId and logicalName.
SELECT file_id, name, type_desc, physical_name, CONVERT(bigint, size) * 8192 AS size_bytes
FROM [<new database>].sys.database_files
ORDER BY file_id;

-- 7. The copy's migration journal.
SELECT COUNT(*) AS migrations, MAX(migration_id) AS latest
FROM [<new database>].dbo.schema_migrations;

-- 8. Only when you decide to remove the test copy.
IF N'<new database>' NOT LIKE N'EtpRestoreCheck[_]%'
    THROW 50002, N'Refusing: this is not a restore-check database.', 1;
ALTER DATABASE [<new database>] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
DROP DATABASE [<new database>];
```

| Placeholder | What to put there | Rules |
|---|---|---|
| `<backup path>` | The full path of the copied backup inside the VM, for example `C:\EtpSecondMachineRestore\EtpReporting-20260923-093012-<32 characters>.bak`. `$bak` in D7. | Keep this PC's file name unchanged, so the file can be traced to its receipt. |
| `<new database>` | `EtpRestoreCheck_<yyyyMMdd>`, for example `EtpRestoreCheck_20260923`. `$newDb` in D7. | Letters, digits and underscores only. It must not exist yet (D9). Never `EtpReporting`, and never `EtpRecovery_...` (the product's drill uses that prefix). |
| `<data folder>` | `C:\EtpSecondMachineRestore\Data`. `$dataDir` in D7. | Made in D1. The SQL service has Modify on it and nobody else but Administrators and SYSTEM. |
| `<logical data name>` | `LogicalName` of the `Type D` row in step 2. For this PC's database, `EtpReporting` (FileId 1). | Taken from the backup, not from memory. |
| `<logical log name>` | `LogicalName` of the `Type L` row. For this PC's database, `EtpReporting_log` (FileId 2). | Same. |
| *(more rows)* | If step 2 lists more files, add `MOVE N'<that LogicalName>' TO N'<data folder>\<new database>_<FileId>.ndf'` for each one. | Every target file name must be unique and must not exist. |

The two logical names come from this PC's live database file list, read on 22 September 2026 (`EtpReporting` ROWS file 1, `EtpReporting_log` LOG file 2). They are not from the backup itself, so step 2 decides.

## Appendix B. If the VM runs Developer Edition

You will know from B6 (`edition` `Developer Edition (64-bit)`).

**What changes:**

- On Developer the product **encrypts** backups (D9). So:
  - B27 refuses with "This SQL Server edition encrypts backups, so it needs the backup certificate. Create and export the recovery keys in Settings > Database first." It refuses only if `master` has no certificate named `EtpBackupCert`. The 17 September tests created one (thumbprint `BF088489D2...`); if B8 lists it, B27 may succeed, and B29's backup then fails for want of `certificate-custody.json`. Either way, skip B27 as below.
  - Every backup needs recorded certificate custody: `certificate-custody.json` and two recovery copies (`backup-etp-database.ps1`).
  - The drill needs both copies connected.
  - Setup's own pre-migration backup (cases 3 and 4) fails for the same reason.
- The VM then does not mirror the shop PC, which runs Express.
- B8 may show `EtpBackupCert` certificates and a master key left by the 17 September tests (thumbprints beginning `35F0261838` and `BF088489D2` in `PHASE-4-AUDIT.md`). Do not drop them without deciding.

**What still works:** setup itself accepts Developer (bootstrap allows engine editions 2, 3 and 4). If the VM has no `EtpReporting` (case 1) or no pending migrations (case 2), B16 to B24 still work, and so do C1 to C4, C6 and C7: the group-path check does not depend on the edition. Part D does not depend on it either.

**What to do:** do Part D. Do B16 to B24 and Part C only in cases 1 and 2. B25 and B26 are optional. Skip B27 to B30 and C5. Record the edition. Then decide separately between:

1. testing the encrypted path. That needs Settings > Database > Encrypted backup recovery keys: two recovery folders and a password you type (**SAGAR ONLY**; see "Certificate and password custody" in `OPERATIONS.md`). This runbook does not cover it.
2. replacing the VM's SQL with Express. That is not covered here and needs its own plan.

## Appendix C. What I could not verify

I wrote this without starting or connecting to the VM, and without running anything on this PC except read-only SQL queries. These are open until the run shows them:

1. **The VM's edition**: Express, as the task says, or Developer, as `PHASE-4-AUDIT.md` last recorded (B6).
2. **That the VM Owner is a SQL administrator only through `BUILTIN\Administrators`** (B6, B7).
3. **That the VM is still `DESKTOP-IPT0J6H`, with the local administrator `ETPTest`** (recorded 17 September), and which user-name form PowerShell Direct accepts (A8).
4. **The VM's current state**: whether `EtpReporting` exists, its migration count, whether the operations procedure exists, and the installed version (B8, B9, B12). This decides case 1 to 4.
5. **That S4U tasks run as `EtpAutomation`.** No backup has ever run as that account on any machine. The "Log on as a batch job" right, and `Get-LocalGroupMember` under that token, are unproven (B29).
6. **That the drill task, S4U at Highest, carries `BUILTIN\Administrators` into SQL** (C5).
7. **What the `EXECUTE AS LOGIN` probe returns** for a Windows user with no login of its own (C6).
8. **Whether the unelevated app can connect on the VM** (B25).
9. **Whether a Windows PowerShell 5.1 transcript captures `sqlcmd` output** (B3, F1).
10. **That `/MERGETASKS="!sqlprerequisites"` behaves as documented** for this installer (B21). The option exists only in a build made with SQL media, which the planned build command includes.
11. **That the new build's migration count is 32** (A4 checks it), and that `artifacts\windows-release-<sha>` belongs to the same build as the installer.
12. **That this PC's backup lists exactly the files `EtpReporting` and `EtpReporting_log`.** Measured from the live database's file list, not from a backup; D11 decides.
13. **That restored file sizes always equal the receipt's `sizeBytes`** (D17). The product compares only file IDs and logical names.
14. **That the receipt file's owner shows `EtpAutomation`** after a task backup (B29).

Facts checked on this PC on 22 September 2026, read-only:

- SQL Server 2022 Express 16.0.1000.6 RTM, server collation `Latin1_General_CI_AS`;
- `EtpReporting` collation `Latin1_General_CI_AS`, files `EtpReporting` (1, ROWS) and `EtpReporting_log` (2, LOG) under `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\`;
- journal at 31, latest `0031_health_counts_what_problems_shows`;
- the tables used in D5 and D19 exist;
- `EtpReporting` has AUTO_CLOSE on, so `sys.databases` shows its collation as `NULL` while it is closed (D15);
- from a window that is not elevated, `sys.login_token` still lists `BUILTIN\Administrators`, with usage `DENY ONLY` (B6, C2). This PC's Owner connects through its own login, so the VM's unelevated result may instead be "Login failed".

---

## Outcome — 24 September 2026

The VM had to be shrunk from 4 GB to 2.5 GB of RAM and cold-booted: the host could not spare
4 GB. Its SQL is **Developer Edition 16.0.1000.6**, as the 17 September note warned, with a
master key and `EtpBackupCert` already present. `ETPTest` holds its own sysadmin login, so the
VM as found does **not** reproduce the shop PC's group-only administrator case.

**Provisioning (Part B) passed.** `initialize-etp-operation-folders.ps1 -CreateAutomationAccount`
created `DESKTOP-IPT0J6H\EtpAutomation` (enabled, not an administrator), wrote `operations.json`,
and protected the folders. One trap confirmed for the second time: the script must be started as
`powershell.exe -ExecutionPolicy Bypass -File ...`; invoked with `&` it is refused, because the
VM's effective execution policy is the default `Restricted`. `docs/OPERATIONS.md` still shows the
`&` form and needs the same correction the live runbook already carries.

**Setup (Part C) is blocked on this edition, and that is a finding.** The new installer ran
silently and failed with exit code 1603, leaving `SETUP-INCOMPLETE.txt`. The bootstrap log:

    Existing database has pending bundled migrations. Creating and verifying a pre-migration backup before any migration runs.
    FAILED: ItemNotFoundException: Cannot find path 'C:\ProgramData\EtpReporting\Backups\certificate-custody.json' because it does not exist.

On an edition that encrypts backups, setup's own mandatory pre-migration backup needs the
certificate custody receipts, so **an existing database cannot be upgraded until the Owner has
exported the recovery keys** (Settings > Database > Encrypted backup recovery keys, two
locations, a password of 16-128 characters). Nothing warns about this before setup runs, and the
message an operator sees is a missing-file exception rather than "export the recovery keys
first". Both this PC and the shop PC run Express, where backups are unencrypted (D9), so neither
is affected. Recorded for a later product fix; the VM install stops here until the keys are
exported.

**Second-machine restore (Part D) passed — this is the Phase 4 recovery evidence.** This PC's
verified backup `EtpReporting-20260924-102239-....bak` (15,454,208 bytes, `encryption: NONE`) was
checked against its receipt on this PC, copied into the VM, and checked again there: hash and
size matched both times. Then, inside the VM:

- `RESTORE VERIFYONLY ... WITH CHECKSUM`: "The backup set on file 1 is valid."
- Restored as `EtpRestoreCheck_<timestamp>`, a new name, with a `MOVE` for each of the two files
  into a folder only Administrators, SYSTEM and the SQL service can read. Never `WITH REPLACE`,
  and the VM's own `EtpReporting` was untouched.
- `DBCC CHECKDB ... WITH NO_INFOMSGS, ALL_ERRORMSGS`: no output, exit code 0.
- The copy came up `ONLINE | MULTI_USER | is_trustworthy_on 0 | is_db_chaining_on 0 | read_only 0`.
- Row counts in the copy matched this PC exactly: sales_invoices 490, sales_lines 540,
  sales_tenders 592, stock_movements 1079, source_lineage 3957, import_files 8,
  application_users 3, schema_migrations 32, newest `0032_active_users_can_connect`.
- `msdb` recorded the source as `DESKTOP-6IBM1J5\SQLEXPRESS`, copy-only, with checksums and no
  encryptor.
- The copy was dropped, the copied backup and its receipt deleted: **no shop data is left in the
  VM**, and no `EtpRestoreCheck_*` database remains.

**Not done in the VM:** its own backup and drill (blocked by the encrypted-key export above), and
the group-only administrator case, which Sagar declined to have simulated there. That case is
exercised for real by the shop PC install.
