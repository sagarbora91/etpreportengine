# Daily files through OneDrive (three shop PCs and Workpc)

Plain-English setup and daily routine for ETP's two-PC plan (decision 30, option C+). The database lives only on Workpc. Each shop PC sends its day's files through OneDrive; Workpc checks them and ETP imports them. Nothing in this guide contains a password or key, and none of your files should be put in this document.

## The idea in one picture

1. **Shop PC (Titan, Helios or Service)** copies the day's export files and the staff daily-inputs workbook to OneDrive, into `ETP-Inbound\<TITAN, HELIOS or SERVICE>\<date>\`. A small file called `manifest.json` is written last: it lists every file with its size and a fingerprint (SHA-256).
2. **Workpc** reads those folders from its synced OneDrive, checks every file against the manifest, and puts the good ones into ETP's `Inbound` folder as one zip per shop and day (for example `TITAN-2026-10-11.zip`). ETP's own scheduled import then imports it.
3. **Bad or doubtful files** are never imported. They are moved to a `Quarantine` folder on Workpc with a short reason beside each.
4. **Workpc's nightly backup** is encrypted and copied to `OneDrive\ETP-Backups\`. The last 3 are kept (about 0.8 GB at 270 MB each).

## Folder layout

| Where | Folder | What is in it |
|---|---|---|
| OneDrive | `ETP-Inbound\TITAN\2026-10-11\` | files + `manifest.json` from the Titan cashier PC |
| OneDrive | `ETP-Inbound\HELIOS\...`, `ETP-Inbound\SERVICE\...` | same for the other two PCs |
| OneDrive | `ETP-Backups\` | encrypted backups `EtpReporting-<date>.bak.enc` and their receipts |
| Shop PC (local, not OneDrive) | for example `C:\ETP-Exports` and `C:\ETP-DailyInputs` | where staff save the exports and the daily-inputs workbook |
| Workpc | `C:\ProgramData\EtpReporting\Inbound` | ETP's watch folder (zips arrive here) |
| Workpc | `C:\ProgramData\EtpReporting\OneDriveReceive\` | `Quarantine`, `Archive`, `Logs`, `State` |

## One-time setup: each shop PC (Windows 10 Pro)

You need the ETP `scripts\onedrive` folder on the PC. Copy it from the USB stick or the project (the installer does not carry it) into a protected local folder, for example `C:\ETP-Tools\onedrive`, that the staff login cannot change (see "Safety rules" above). Then do the selective-sync step there too.

1. Sign in to OneDrive on the PC and confirm the OneDrive folder is syncing (cloud icon by the clock, no red cross). Use the account the shop agreed on; do not type any password into ETP files.
2. Create two ordinary local folders, **not inside OneDrive**: `C:\ETP-Exports` (the daily Titan/Helios/Service export files) and `C:\ETP-DailyInputs` (the staff daily-inputs workbook, file name starting `ETP-Daily-Inputs`).
3. In the `onedrive` folder copy `shop-daily-files.config.example.json` to `shop-daily-files.config.json` and edit it in Notepad:
   - `role`: `TITAN` on the cashier PC, `HELIOS` on the Helios PC, `SERVICE` on the Service Centre PC.
   - `sourceFolder` and `dailyInputsFolder`: the two folders from step 2 (use `\\` for each backslash in this file).
   - Leave `oneDriveRoot` empty unless the script says it cannot find OneDrive.
4. Test without copying anything: open PowerShell (normal, not administrator) in that folder and run
   `powershell -ExecutionPolicy Bypass -File .\Send-ShopDailyFiles.ps1 -WhatIf`
   It lists what it would copy. Then run it for real without `-WhatIf` and look in OneDrive for the new `ETP-Inbound\<ROLE>\<today>` folder.
5. Schedule it for the evening: `powershell -ExecutionPolicy Bypass -File .\Register-ShopDailyTask.ps1 -At 21:00`
   The task runs as the person who is signed in, only while they are signed in; Windows asks for no password. If the shop closes earlier or later, change `-At` (24-hour time). It tries again every 30 minutes for two hours, so a file that was still open is picked up. Remove it any time with `-Unregister`.

## Safety rules for the account, the scripts and the key (read before setup)

These come from the 12 Oct 2026 security and recovery audits.

**1. Where the scripts live.** The scripts are not carried by the installer (until a later release packages them): the copy on the USB stick or in the project's `scripts\onedrive` folder is the source. Put them in a **protected local folder that ordinary users cannot change**: on Workpc, a folder such as `C:\ETP-Tools\onedrive` where only Administrators and SYSTEM have Modify (give Users Read and Execute at most); on each shop PC, the same, so the staff Windows login cannot edit the scripts. Never run the Workpc scripts elevated from a folder that anyone else can write to, or from the Desktop, Downloads or a OneDrive folder: whoever can edit the file can run what they like as administrator (audit finding S-05). After copying, check the folder's Security tab.

**2. Selective sync: shop PCs take only `ETP-Inbound`.** On every shop PC open OneDrive settings, Sync and backup, Manage backup / Choose folders, and tick **only `ETP-Inbound`**. Do not sync `ETP-Backups` there: it holds the encrypted database backups. Because all four PCs use one Microsoft account, this is the only wall between a shop PC and the backups. On Workpc tick `ETP-Inbound` and `ETP-Backups`. If you can, keep the backups under a different OneDrive account from the shop PCs' account.

**3. Multi-factor sign-in on the Microsoft account.** Turn on two-step verification for the shared account, and keep the phone or authenticator that receives the code with Sagar, not at a shop PC. Keep the recovery e-mail and phone up to date. Without this, one stolen password opens every shop's files and the backups.

**4. Key custody.** The backup key file sits on Workpc outside OneDrive and has one copy on a USB stick kept away from the PCs (not in OneDrive, e-mail or chat). Once a quarter, on a spare PC, use the stick's key to open the newest `.bak.enc`; a key that cannot open a backup is the same as no key. Lost key: the backups in OneDrive cannot be opened, so create a new key at once (`-NewKey`) and keep the next backup. Leaked key: treat as a breach (see `DPDP-BREACH-AND-COUNTER-NOTICE-DRAFT.md`), create a new key and delete the old encrypted files. Creating a new key makes every older `.bak.enc` unreadable unless the old key is kept too, so keep the old key only as long as you need those backups, in the same safe place.

## What staff do each evening

1. Save the day's exports (and fill in and save the daily-inputs workbook: walk-ins, cash, service money) into the two local folders **before the scheduled time**. Close Excel afterwards: a workbook still open in Excel is skipped.
2. Leave the PC on and signed in until the scheduled time has passed.
3. Nothing else. The script never deletes or changes the originals.

## One-time setup: Workpc

1. Sign in to OneDrive on Workpc with the same account (with two-step verification on) and make sure `ETP-Inbound` and `ETP-Backups` sync down (right-click the folder, "Always keep on this device" is the safest).
2. ETP itself must already be set up (watch folders created, automatic import task installed).
3. Copy `workpc-receive.config.example.json` to `workpc-receive.config.json`; the defaults normally need no change.
4. The `Inbound` folder is protected by ETP setup, so the receive script must run **as administrator or as the ETP automation account**. Try it first with: `powershell -ExecutionPolicy Bypass -File .\Receive-ShopDailyFiles.ps1 -WhatIf`
5. Schedule `Receive-ShopDailyFiles.ps1` each morning (for example 07:00) in Task Scheduler under that same account. It checks yesterday's files by default. Schedule it before ETP's automatic import so the zip is picked up the same morning.

### Backup to OneDrive (Workpc)

1. Copy `workpc-backup.config.example.json` to `workpc-backup.config.json`. Set `keyFile` to a local path that OneDrive does not sync (the example is under `C:\ProgramData\EtpReporting\Keys`).
2. Create the key once: `powershell -ExecutionPolicy Bypass -File .\Copy-EtpBackupToOneDrive.ps1 -NewKey`
3. **Copy the key file to a USB stick and keep it somewhere safe (see "Key custody" above), away from the shop PCs and away from OneDrive, email and chat.** If it is lost, the backups in OneDrive cannot be opened. If it is leaked, anyone with the OneDrive files can read your data. The script refuses to use a key that sits inside OneDrive.
4. Schedule `Copy-EtpBackupToOneDrive.ps1` to run after the nightly ETP backup has finished (for example 90 minutes later). It only ever copies a backup that ETP has marked verified, re-checks it, encrypts it, copies it, checks the copy, and keeps the newest 3 (`keepLast`).
5. To restore from a OneDrive copy: `.\Copy-EtpBackupToOneDrive.ps1 -Decrypt -InputFile <file>.bak.enc -OutputFile <file>.bak` (needs the key file), then use ETP's normal restore procedure on the `.bak`.

## How to check it worked

- **Shop PC:** OneDrive shows `ETP-Inbound\<ROLE>\<today>` containing your files and `manifest.json`. A short log is in `%LOCALAPPDATA%\EtpOneDrive\send.log.jsonl`.
- **Workpc, next morning:** `Inbound` (or ETP History after the import ran) shows `<ROLE>-<date>.zip` for each role; `OneDriveReceive\Archive\<ROLE>\<date>` holds the files that were taken. `OneDriveReceive\Logs\receive.log.jsonl` lists every file taken or refused.
- **Missing shop:** if a PC sent nothing for the day, `OneDriveReceive\Logs\missing-<date>.txt` says which one, and the script exits with code 2 (the scheduled task shows a non-zero result). No such file means all three arrived.
- **Backups:** `OneDrive\ETP-Backups` has a new `.bak.enc` each night.

## If something fails

| Symptom | What to do |
|---|---|
| Shop script says "OneDrive folder not found" | Sign in to OneDrive on that PC, or put the OneDrive folder path in `oneDriveRoot`. |
| "Skipped (still being written or open in another program)" | Close Excel/the export program and run the script again; it is safe to repeat. |
| A file is in `Quarantine` | Read the `.reason.txt`. Usually the file changed after the manifest (re-save the file on the shop PC, run Send again: the corrected file arrives and is imported as a second zip `...-r2.zip`), or it is not an .xlsx/.csv. |
| Role shown as MISSING | Check the shop PC is on, signed in, and OneDrive is syncing; run Send by hand there. Then run Receive on Workpc again. |
| "no manifest yet" or "still syncing" | OneDrive has not finished. Wait and run Receive again. |
| "Cannot write to Inbound" | Run Receive as administrator or as the ETP automation account. |
| "already exists in Inbound" | ETP has not imported the earlier zip yet. Run Receive again after the next automatic import. |
| Backup copy says the .bak does not match its receipt | Do not use it. Run ETP's backup again, and tell Sagar. |
| Import fails in ETP | Use ETP's History and the import failure process as for any import; the quarantine and archive folders keep the exact files. |

Keep the key file, passwords and customer lists out of this folder and out of any message you send about a problem. File names, sizes and the log lines are safe to share.
