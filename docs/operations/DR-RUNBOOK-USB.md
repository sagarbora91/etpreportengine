# DR runbook: new PC from scratch (USB stick copy)

Print this and keep the USB stick away from Workpc (home or a bank locker). Copied from the 12 Oct 2026 disaster-recovery audit (AUDIT-3, section 3) for ETP 1.11.x. **This file holds no password, key or account secret by design.** The passwords, the 2FA phone and the backup key file are kept by Sagar separately; this page only says where they are used.

**Refresh the stick with every release** (installer, its SHA-256 file, `scripts\onedrive`, this page). Write the refresh date here: ____________ (last refresh) and the ETP version on the stick: ________.

**What is on the stick:** (a) the OneDrive backup key file (keep a second copy of the key elsewhere; if it is lost the OneDrive backups cannot be opened); (b) the latest ETP installer and its SHA-256 file; (c) the `scripts\onedrive` folder; (d) this page.

**Expected time.** About 2 to 4 hours of hands-on work. Never yet measured end to end with the encrypted OneDrive file; after the first full drill, write the real time here: ________. Data loss: everything entered since the 22:00 backup (worst case just under 48 hours). Shop exports and daily sheets can be re-sent from the shop PCs. Only the newest 3 nightly backups are in OneDrive.

Elevation: "ADMIN" means the step needs Windows administrator (UAC). "SAGAR" means a password or account only Sagar can type or create (the AI never does these).

**Before you start, have in hand:** the USB stick with (a) the OneDrive backup key file, (b) the latest ETP installer with its SHA-256 file, (c) the `scripts\onedrive` folder and this runbook; the shared Microsoft account's email, password and 2FA phone; a Windows 11 Pro PC with at least 30 GB free on C:.

1. **Windows. SAGAR + ADMIN.** Install Windows, apply updates, create the local administrator account (call it Sagar, same as before so the OWNER row is familiar; any name works). Create the shared staff Windows login if the shop uses one. Passwords are Sagar's.
2. **OneDrive. SAGAR.** Sign in with the shared Microsoft account. In OneDrive settings choose folders to sync and tick **only `ETP-Backups`** (not `ETP-Inbound`). Right-click `ETP-Backups` and choose "Always keep on this device". Wait for the green ticks. You should see up to three `EtpReporting-....bak.enc` files and their `.receipt.json`.
3. **Check the installer. No elevation.** Compare the installer's SHA-256 with the file next to it (`Get-FileHash`). Do not run an installer you cannot verify.
4. **Decrypt the newest backup. No elevation. Needs the key.**
   - Copy `scripts\onedrive` from the USB to `C:\ETP-Tools\onedrive`; copy the key file to a local folder outside OneDrive (for example `C:\ETP-Tools\Keys`).
   - Copy `workpc-backup.config.example.json` to `workpc-backup.config.json`, set `keyFile` to that key path.
   - Run (Windows PowerShell, normal): `powershell -ExecutionPolicy Bypass -File C:\ETP-Tools\onedrive\Copy-EtpBackupToOneDrive.ps1 -Decrypt -InputFile <the .bak.enc> -OutputFile C:\ETP-Restore\<name>.bak`
   - It checks the integrity code first; "wrong key, or the file is damaged" means stop and try the next older file. Compare the printed SHA-256 with the `sha256` inside the receipt.
   - **The output folder must not be inside `C:\ProgramData\EtpReporting`** (the restore helper refuses it), and must not be on a network or mapped drive. Use `C:\ETP-Restore`.
5. **Install SQL Server and ETP. ADMIN.** Run the installer elevated with the SQL Server option ticked and **"Create a new empty ETP database" unticked**. Run it as the Windows account that should become the ETP Owner. It ends with exit code 0 and a file `DATABASE-RESTORE-PENDING.txt`. If it stops with 1603 mentioning `Get-Acl` (fixed in 1.9.7), wait a minute and run the installer again; the database is untouched. Do not put SQL on a disk with 16 KB sectors (Workpc's E: needed a driver override); let setup put it on C:.
6. **Restore. ADMIN, as a SQL administrator.** Elevated PowerShell:
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\restore-etp-database.ps1" -BackupPath "C:\ETP-Restore\<name>.bak" -ReceiptPath "<the receipt>"`
   (the path to the scripts is under `E:\Program Files\...` if the app was installed on E:). It checks the hash, runs RESTORE VERIFYONLY, restores, turns TRUSTWORTHY and DB_CHAINING off, runs CHECKDB, makes the running account an Owner, and has SYSTEM give that Owner `ALTER ANY LOGIN WITH GRANT OPTION` through a one-off task. It never overwrites an existing database. The receipt it expects is the `.receipt.json` copied beside the `.bak.enc` (rename it if needed).
7. **Setup again. ADMIN.** Run the installer again (SQL box can stay unticked). It takes a verified `PRE_MIGRATION` backup, applies any waiting migrations, creates the `EtpAutomation` Windows account (random password, discarded) and registers the three tasks. Read its log for a `NEXT STEP:` line.
8. **First sign-in. No elevation.** Start ETP as the Owner. Check Today and one report for a recent date. Expect: the data stops at the backup time.
9. **Users. Needs the Owner grant from step 6 (unelevated is fine; otherwise Run as administrator).** In Settings > Users: add `<NEWPC>\EtpAutomation` as an active **Store Manager**; add the staff Windows login as Store Manager or Viewer (SAGAR creates the Windows accounts and passwords first); deactivate every account from the old PC (`WORKPC\...`, older laptops) with a reason. If the screen says "not saved", run ETP as administrator and confirm the row in `dbo.application_users`.
10. **Operations module. ADMIN.** Run setup once more (it installs the module when the automation account is an active Store Manager) or run `install-etp-sql-operations.ps1 -ServerInstance .\SQLEXPRESS -Database EtpReporting -AutomationPrincipal <NEWPC>\EtpAutomation` elevated. Until this is done the daily backup and the drill fail with the masked "The database operation failed".
11. **Prove backup and drill. ADMIN for the drill.** Run the Daily Backup task once; check `EtpReporting-latest-verified.json` (new hash, today's time). Then run the drill elevated as Sagar (the monthly runner does nothing unless today is day 1; use `invoke-etp-recovery-drill.ps1` directly, or change `-DayOfMonth` for one run and restore it). The drill needs SQL answering first after a restart.
12. **Re-enter what cannot travel.** SMTP/email credentials for each sender; the Tally TEST company check if used. Check watch folders exist and `Inbound`, `Processed`, `Failed` folder permissions (setup does this).
13. **OneDrive jobs on the new Workpc. ADMIN + SAGAR.** Put `scripts\onedrive` in a protected local folder; create a **new** key (`-NewKey`) or restore the old key to the new PC if you want the 3 old backups to stay readable; re-register the Receive and Copy tasks (use the Windows account that will run the tasks; ask Sagar); sign the shop PCs' tasks back in. A new key makes every older `.bak.enc` unreadable unless the old key is kept as well.
14. **Shops.** The shop PCs have kept sending files; check `OneDriveReceive\Logs` and import the days missing since the backup (the Receive script looks back `lookbackDays` = 3 only; older days need a manual run or lookback change).
15. **Write down** the new PC's name, restore time, the backup used, and the gap in data. Tell Sagar so the notes are updated.

## If a step fails

- Stop and write down the step number and the exact words on the screen. Do not repeat a restore over an existing database: the restore script refuses to overwrite one.
- A step that says "wrong key, or the file is damaged": try the next older `.bak.enc`.
- Do not paste the key file, passwords or customer data into any message. File names, sizes and log lines are safe to share.
