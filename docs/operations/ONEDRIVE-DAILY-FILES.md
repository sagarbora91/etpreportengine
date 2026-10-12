# Daily files through OneDrive (three shop PCs and Workpc)

Plain-English setup and daily routine for ETP's two-PC plan (decision 30, option C+). The database lives only on Workpc. Each shop PC sends its day's files through OneDrive; Workpc checks them and ETP imports them. Nothing in this guide contains a password or key, and none of your files should be put in this document.

> **1.11.2 changes the set-up (breaking for shop PCs).** From 1.11.2 every shop PC signs its day's `manifest.json` with its own signing key, and Workpc **refuses unsigned or 1.11.1-style (schema 1) manifests**: their files go to `Quarantine` and nothing is imported. Before turning the shops on, every shop PC needs the 1.11.2 scripts, its own key (`Send-ShopDailyFiles.ps1 -NewSigningKey`), a USB copy of that key on Workpc and its PC name in Workpc's `pcNames`. Workpc without a shop's key or PC name reports a problem for that shop (exit code 2) and moves nothing, so nothing is lost while you finish the set-up.

## The idea in one picture

1. **Shop PC (Titan, Helios or Service)** copies the day's export files and the staff daily-inputs workbook to OneDrive, into `ETP-Inbound\<TITAN, HELIOS or SERVICE>\<date>\`. A small file called `manifest.json` is written last: it lists every file with its size and a fingerprint (SHA-256), the PC's name, and a signature made with that PC's own key (the key never goes into OneDrive).
2. **Workpc** reads those folders from its synced OneDrive, checks the manifest's signature and PC name with its USB copy of that shop's key, copies each file to a local staging folder and checks that copy against the manifest, and puts the good ones into ETP's `Inbound` folder as one zip per shop and day (for example `TITAN-2026-10-11.zip`). ETP's own scheduled import then imports it.
3. **Bad or doubtful files** are never imported. They are moved to a `Quarantine` folder on Workpc with a short reason beside each.
4. **Workpc's nightly backup** is encrypted and copied to `OneDrive\ETP-Backups\`. The last 3 are kept, plus the first copy of each of the last 6 months (about 2.4 GB at 270 MB each).

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

You need the ETP `scripts\onedrive` folder on the PC. From 1.11.2 the installer puts it on Workpc in `C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\onedrive`; copy that folder by USB (shop PCs have no ETP installed) into a protected local folder, for example `C:\ETP-Tools\onedrive`, that the staff login cannot change (see "Safety rules" below). Then do the selective-sync step there too.

1. Sign in to OneDrive on the PC and confirm the OneDrive folder is syncing (cloud icon by the clock, no red cross). Use the account the shop agreed on; do not type any password into ETP files.
2. Create two ordinary local folders, **not inside OneDrive**: `C:\ETP-Exports` (the daily Titan/Helios/Service export files) and `C:\ETP-DailyInputs` (the staff daily-inputs workbook, file name starting `ETP-Daily-Inputs`).
3. In the `onedrive` folder copy `shop-daily-files.config.example.json` to `shop-daily-files.config.json` and edit it in Notepad:
   - `role`: `TITAN` on the cashier PC, `HELIOS` on the Helios PC, `SERVICE` on the Service Centre PC.
   - `sourceFolder` and `dailyInputsFolder`: the two folders from step 2 (use `\\` for each backslash in this file).
   - Leave `oneDriveRoot` empty unless the script says it cannot find OneDrive.
   - Leave `signingKeyFile` as it is (`%LOCALAPPDATA%\EtpOneDrive\shop-signing.key`, outside OneDrive; the script refuses a key inside OneDrive).
3a. Create this PC's signing key once: `powershell -ExecutionPolicy Bypass -File .\Send-ShopDailyFiles.ps1 -NewSigningKey`. It prints the key's fingerprint and **the PC name** to use. Copy the key file by USB to Workpc as `C:\ProgramData\EtpReporting\Operations\shop-keys\<ROLE>.key` (for example `TITAN.key`), write the PC name under `pcNames.<ROLE>` in Workpc's `workpc-receive.config.json`, then wipe the key from the USB stick. Never put the key in OneDrive, e-mail or chat. `-ShowSigningKey` prints the fingerprint again, to compare with Workpc's copy.
4. Test without copying anything: open PowerShell (normal, not administrator) in that folder and run
   `powershell -ExecutionPolicy Bypass -File .\Send-ShopDailyFiles.ps1 -WhatIf`
   It lists what it would copy. Then run it for real without `-WhatIf` and look in OneDrive for the new `ETP-Inbound\<ROLE>\<today>` folder. Each run ends with "Sent N file(s) (C new, U already in OneDrive), skipped M"; skipped files are listed in red with what to do, and the same text is kept in `%LOCALAPPDATA%\EtpOneDrive\last-send.txt` (the evening task runs hidden, so staff look there). It also warns in red when OneDrive is not running on the PC.
5. Schedule it for the evening: `powershell -ExecutionPolicy Bypass -File .\Register-ShopDailyTask.ps1 -At 21:00`
   The task runs as the person who is signed in, only while they are signed in; Windows asks for no password. If the shop closes earlier or later, change `-At` (24-hour time). It tries again every 30 minutes for two hours, so a file that was still open is picked up. Remove it any time with `-Unregister`.

## Safety rules for the account, the scripts and the key (read before setup)

These come from the 12 Oct 2026 security and recovery audits.

**1. Where the scripts live.** From 1.11.2 the installer carries them: on Workpc they are in `C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\onedrive` (ordinary users can only read there), and that is the copy to use and to take by USB to the shop PCs. Put them in a **protected local folder that ordinary users cannot change**: on Workpc, a folder such as `C:\ETP-Tools\onedrive` where only Administrators and SYSTEM have Modify (give Users Read and Execute at most); on each shop PC, the same, so the staff Windows login cannot edit the scripts. Never run the Workpc scripts elevated from a folder that anyone else can write to, or from the Desktop, Downloads or a OneDrive folder: whoever can edit the file can run what they like as administrator (audit finding S-05). After copying, check the folder's Security tab.

**2. Selective sync: shop PCs take only `ETP-Inbound`.** On every shop PC open OneDrive settings, Sync and backup, Manage backup / Choose folders, and tick **only `ETP-Inbound`**. Do not sync `ETP-Backups` there: it holds the encrypted database backups. Because all four PCs use one Microsoft account, this is the only wall between a shop PC and the backups. On Workpc tick `ETP-Inbound` and `ETP-Backups`. If you can, keep the backups under a different OneDrive account from the shop PCs' account.

**3. Multi-factor sign-in on the Microsoft account.** Turn on two-step verification for the shared account, and keep the phone or authenticator that receives the code with Sagar, not at a shop PC. Keep the recovery e-mail and phone up to date. Without this, one stolen password opens every shop's files and the backups.

**4. Key custody.** The backup key file sits on Workpc outside OneDrive and has one copy on a USB stick kept away from the PCs (not in OneDrive, e-mail or chat). `-NewKey` prints the key's fingerprint; write it down with the stick, and check the stick with `Copy-EtpBackupToOneDrive.ps1 -VerifyKey -KeyFile <USB path>` (same fingerprint = same key). Each shop PC's signing key also stays on that PC and in `shop-keys` on Workpc only. Once a quarter, on a spare PC, use the stick's key to open the newest `.bak.enc`; a key that cannot open a backup is the same as no key. Lost key: the backups in OneDrive cannot be opened, so create a new key at once (`-NewKey`) and keep the next backup. Leaked key: treat as a breach (see `DPDP-BREACH-AND-COUNTER-NOTICE-DRAFT.md`), create a new key and delete the old encrypted files. Creating a new key makes every older `.bak.enc` unreadable unless the old key is kept too, so keep the old key only as long as you need those backups, in the same safe place.

## What staff do each evening

1. Save the day's exports (and fill in and save the daily-inputs workbook: walk-ins, cash, service money, and your name in **Filled by**) into the two local folders **before the scheduled time**. Close Excel afterwards: a workbook still open in Excel is skipped. A sheet with an empty **Filled by** is refused by ETP (1.11.2, decision 43).
2. Leave the PC on and signed in until the scheduled time has passed.
3. Nothing else. The script never deletes or changes the originals.

## One-time setup: Workpc

1. Sign in to OneDrive on Workpc with the same account (with two-step verification on) and make sure `ETP-Inbound` and `ETP-Backups` sync down (right-click the folder, "Always keep on this device" is the safest).
2. ETP itself must already be set up (watch folders created, automatic import task installed).
3. Copy `workpc-receive.config.example.json` to `workpc-receive.config.json` and fill in `pcNames` (the PC name each shop PC printed at `-NewSigningKey`) and `oneDriveRoot` (Register-WorkpcTasks prints the value to use if it is empty). Put each shop's key file in `shopKeysFolder` (default `C:\ProgramData\EtpReporting\Operations\shop-keys\<ROLE>.key`). A config that gives the same PC name to two roles is refused.
4. The `Inbound` folder is protected by ETP setup, so the receive script must run **as administrator**. Try it first, in an administrator PowerShell, with: `powershell -ExecutionPolicy Bypass -File .\Receive-ShopDailyFiles.ps1 -WhatIf`
5. Register both Workpc tasks with one command, in an administrator PowerShell, as Sagar: `powershell -ExecutionPolicy Bypass -File .\Register-WorkpcTasks.ps1` (default: 'ETP Receive Shop Daily Files' at 07:00, before ETP's automatic import, and 'ETP Copy Backup To OneDrive' at 23:30; change with `-ReceiveAt` / `-BackupCopyAt`, remove with `-Unregister`). Pass `-ReceiveConfigPath` / `-BackupConfigPath` if the configs are not beside the scripts. It refuses to run when the window is not elevated, OneDrive is not running, a config has no `oneDriveRoot`, ordinary users can write to the scripts folder, a key is inside OneDrive or the backup key is missing, and it checks the registered tasks afterwards. Windows asks for Sagar's password once (S4U task).

### Backup to OneDrive (Workpc)

1. Copy `workpc-backup.config.example.json` to `workpc-backup.config.json`. Set `keyFile` to a local path that OneDrive does not sync (the example is under `C:\ProgramData\EtpReporting\Keys`).
2. Create the key once: `powershell -ExecutionPolicy Bypass -File .\Copy-EtpBackupToOneDrive.ps1 -NewKey` (it prints the key's fingerprint).
3. **Copy the key file to a USB stick and keep it somewhere safe (see "Key custody" above), away from the shop PCs and away from OneDrive, email and chat.** If it is lost, the backups in OneDrive cannot be opened. If it is leaked, anyone with the OneDrive files can read your data. The script refuses to use a key that sits inside OneDrive.
4. `Register-WorkpcTasks.ps1` (above) schedules `Copy-EtpBackupToOneDrive.ps1` after the nightly ETP backup. It only ever copies a backup that ETP has marked verified, re-checks it, encrypts it, copies it, checks the copy, and keeps the newest 3 (`keepLast`) plus the first copy of each of the newest 6 months (`keepMonthly`). It fails when OneDrive is not running, and waits up to `syncWaitMinutes` (30) for OneDrive to report the file uploaded; if Windows gives no sync status it logs `SyncUnknown` and carries on. The receipt beside each copy records the key fingerprint and when it was copied.
5. To restore from a OneDrive copy: `.\Copy-EtpBackupToOneDrive.ps1 -Decrypt -InputFile <file>.bak.enc -OutputFile <file>.bak -KeyFile <key>` (on a new PC no config is needed with `-KeyFile`), then use ETP's normal restore procedure on the `.bak`. From 1.11.2 copies use the format `ETPBAK02`, which seals the file name and key id: a renamed file (possibly an older backup put in place of a newer one) is refused unless you add `-AllowRenamed`, a wrong key is named as such, and if the `.receipt.json` is beside the file the decrypted copy is checked against it. Older `ETPBAK01` copies still open.

## How to check it worked

- **Shop PC:** OneDrive shows `ETP-Inbound\<ROLE>\<today>` containing your files and `manifest.json`. `%LOCALAPPDATA%\EtpOneDrive\last-send.txt` says "Sent N, skipped M"; the full log is `send.log.jsonl` in the same folder.
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
| Manifest in `Quarantine`: bad signature, schema 1 or wrong PC | The shop PC still runs the 1.11.1 scripts, has a new key Workpc does not have, or `pcNames` is wrong. Update the scripts on the shop PC, compare `-ShowSigningKey` with the key in `shop-keys`, fix `pcNames`, and send again. |
| Receive reports "no key" or "no pcName" for a role (exit 2) | Copy that shop's key into `shop-keys\<ROLE>.key` and fill `pcNames.<ROLE>`. Nothing was moved. |
| "not downloaded yet" | OneDrive Files On-Demand has not brought the file down. Set `ETP-Inbound` to "Always keep on this device" on Workpc. |
| Decrypt refuses a renamed backup | Use the file under its original name. Only if you are sure it is the backup you want, add `-AllowRenamed`. |
| `copy.log.jsonl` shows `SyncUnknown` every night | Windows gives no OneDrive status to the task; the copy still happens. Check in OneDrive that the newest `.bak.enc` shows as uploaded. |
| Import fails in ETP | Use ETP's History and the import failure process as for any import; the quarantine and archive folders keep the exact files. |

Keep the key file, passwords and customer lists out of this folder and out of any message you send about a problem. File names, sizes and the log lines are safe to share.
