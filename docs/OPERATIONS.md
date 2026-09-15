# ETP operations and recovery

## Deployment status

This runbook describes the Phase 4 code and the work required to deploy it. It does not certify the shop installation. This coding task has not configured the live database, changed its folder permissions, installed scheduled tasks, established certificate custody, or signed a production release.

**D9 remains native SQL backup encryption using AES-256 and `EtpBackupCert`.** SQL Server Express and Web cannot create native encrypted backups. Express can restore compatible encrypted backups, but that does not satisfy the backup requirement. Production needs a supported, appropriately licensed SQL edition, selected and provisioned before deployment. There is no automatic plaintext fallback. [Microsoft backup encryption documentation](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/backup-encryption?view=sql-server-ver17).

The following decisions and evidence are still required:

| Item | Required before staff deployment |
| --- | --- |
| SQL edition and local instance | Sagar approves the production edition; a SQL administrator installs and verifies it. |
| Folder access | Sagar resolves the strict folder policy versus the dedicated automation account's required access. See below. |
| Release signing | Acquire the signing authority, trust its publisher on the shop PC, and verify the signed release. Production signing has not been demonstrated here. |
| Recovery custody | Sagar chooses two genuinely off-PC locations and a separate password custodian. |
| Recovery evidence | Complete a native encrypted backup, a same-machine drill, and a second-machine restore using the exported certificate. Native encrypted restore and second-machine recovery have not been verified by this coding task. |

Phase acceptance remains subject to the master plan's audit. Passing isolated tests does not replace these deployment checks.

## Accounts, files and trust

Use separate Windows identities for the Owner, shared staff login, and unattended automation. The automation account must be an enabled local account, outside Administrators, and must not be SYSTEM, Local Service, Network Service, or the built-in Administrator. Give it an active `STORE_MANAGER` application entry and the `etp_store_manager` database role. It is separate from the shared interactive staff login.

Install the application and scripts beneath an administrator-controlled directory, normally `C:\Program Files\Saagar Traders\ETP Reporting Engine`. Task setup rejects writable installation files, unsafe ancestor permissions, links and junctions. Install Microsoft Sqlcmd in a protected Program Files location. Do not make the installation writable to solve a permissions failure.

`%ProgramData%\EtpReporting\Operations\operations.json` is the common machine target for unattended imports, backups, drills and configured setup. It contains the local SQL instance, database and dedicated automation identity, without a SQL password. Only administrators may change it; the automation identity reads it. Interactive saved connection settings are separate: verify that Owner and staff point to this same database. `--initialize-database` uses interactive saved settings; `--initialize-configured-database` uses the protected machine target.

Connections use Windows authentication. The default local setting is `Encrypt=Optional`; remote hosts, TCP endpoints, explicit `Encrypt=False`, attached database files and SQL credentials are rejected. Bootstrap supports ordinary local default or named instances; LocalDB and custom named-pipe endpoints require a separately reviewed manual setup.

### Folder policy decision

`initialize-etp-operation-folders.ps1` resets existing permissions, including explicit permissions on descendants. Schedule this with the shop closed and verify the chosen SQL service identity first.

By default, `Backups`, `Documents`, `Share` and `SetupLogs` beneath `%ProgramData%\EtpReporting` allow SYSTEM and Administrators full control and the SQL service Modify. The parent `EtpReporting` folder and `Operations` give the SQL service read access. The parent is protected too, so a broad parent grant cannot be used to replace a protected child. Inheritance from broad Windows groups is removed. **Strict mode does not configure unattended operations**, because the dedicated account cannot read and write the required files.

The explicit `-GrantAutomationFolderAccess` switch additionally gives the named automation account Modify on those four data folders and read access to their parent and `Operations`; it writes the protected machine configuration. This is an exception to the master plan's literal three-identity folder policy and requires Sagar's decision. It does not grant the shared staff login access. Staff document/share workflows therefore need a separately approved access design before use; do not grant `BUILTIN\Users` access as a shortcut.

Both modes leave source workbook folders and report-output folders outside these locations unchanged. Provision only the access needed by the configured watch-folder workflow. The task uses S4U logon, so do not assume user-mapped drives, an interactive cloud session, or network credentials will be available.

## Deployment sequence

These are administrator instructions, not actions performed by this coding task. Replace example identities and targets with the approved values. Keep tasks disabled and staff out of the application until the final verification succeeds.

1. **Prepare the SQL instance and recovery storage.** Preinstall a compatible SQL Server 2022-or-newer instance that supports native encrypted backups, start it, and install protected Microsoft Sqlcmd. Record the instance, database, real service identity, version and edition. Bootstrap does not purchase or install a compatible SQL edition.
2. **Prepare and verify a signed release.** Release builders require `-CertificateThumbprint` and `-TimestampServer`. The signing certificate must have its private key and code-signing purpose in the build operator's Current User certificate store. `build-windows-release.ps1` signs the published executable and packaged PowerShell scripts; `build-windows-installer.ps1` signs the resulting installer and writes its SHA-256 file. Signing requires a valid timestamp, and the installer builder verifies payload signatures even with `-SkipReleaseBuild`. Preserve the release receipt and hashes. Verify the publisher and signature on the installed executable, all packaged scripts, and installer. Deployed PowerShell runs with `AllSigned`; unsigned development output is not an installable operational release. Do not switch to `Bypass` to make it run.
3. **Prepare the accounts and folder policy.** Create the approved dedicated local account. Run the signed folder setup from the protected installation as Windows administrator. The following example applies strict permissions only:

   ```powershell
   & 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\initialize-etp-operation-folders.ps1' `
       -SqlServiceIdentity 'NT SERVICE\MSSQL$ETP' `
       -ServerInstance '.\ETP' -Database 'EtpReporting'
   ```

   Only after approval of the folder exception, repeat with `-AutomationPrincipal 'SHOPPC\EtpAutomation' -GrantAutomationFolderAccess`. Use the actual machine name and SQL service identity. Verify the resulting permissions and protected configuration; strict mode alone cannot satisfy the unattended setup prerequisites.
4. **Protect an existing database before its first Phase 4 migration.** Pause imports and obtain an independently verified encrypted pre-migration backup, with the matching exported certificate and private key, under SQL administrator control. Preserve its hash and verification evidence outside the installation. The first upgrade has a dependency: the new backup script needs the master operations module, while its installer expects the Phase 4 database roles and status migration. Bootstrap cannot create that initial safety backup from an unconfigured old installation. Use a DBA-controlled staged backup/migration/module installation; never bypass the backup requirement or fabricate a verified receipt. If the existing server is Express, the edition-transition and pre-upgrade recovery plan must be resolved first.
5. **Apply bundled migrations to the reviewed target.** For the staged setup, use the signed application's `--initialize-configured-database` command after confirming the protected configuration and the pre-migration backup. A new empty database has no pre-migration data to back up. Retain the bundled migration files unchanged and inspect the migration journal and database integrity afterward. First-owner creation and SQL permissions require a supervised SQL administrator context; confirm that the intended Owner, rather than a temporary setup identity, holds application access.
6. **Provision application access and export recovery keys.** Use Owner administration to register staff and the dedicated automation account with their intended roles. Follow the custody procedure below. The Owner role in the application does not by itself grant certificate-creation rights in `master`; a SQL administrator must supervise or provision those specific rights and the required recovery-folder access.
7. **Install the restricted SQL operations module.** With the certificate present, Phase 4 migrations applied, and the dedicated account active as Store Manager, run the signed script as SQL administrator:

   ```powershell
   & 'C:\Program Files\Saagar Traders\ETP Reporting Engine\scripts\install-etp-sql-operations.ps1' `
       -ServerInstance '.\ETP' -Database 'EtpReporting' `
       -AutomationPrincipal 'SHOPPC\EtpAutomation'
   ```

   Use the standard protected backup folder unless a separately reviewed deployment accounts for every UI and task default. Verify the module under the dedicated account before enabling unattended operation.
8. **Exercise backup and recovery, then install tasks.** Under the actual dedicated account, run `backup-etp-database.ps1` and then `invoke-etp-recovery-drill.ps1` without target overrides so both use the common configuration. Complete the second-machine exercise below. When these succeed, run the three signed `install-*-task.ps1` scripts as administrator. A fully prepared installation can run `bootstrap-etp-prerequisites.ps1 -ApplicationDirectory <protected-installation>` to perform its configured preflight, any needed verified pre-migration backup, migration/integrity checks and task installation. It requires the protected configuration and preinstalled prerequisites; it is not an account/certificate provisioning wizard.
9. **Verify before handover.** Confirm all three tasks have the same non-SYSTEM account and Limited run level; validate actual task results and a daily backup less than 24 hours old. Confirm Viewer day-reopen/audit deletion fail and Store Manager fact edits/superseding fail. Inspect the three required folder ACLs for any `BUILTIN\Users` access. Check system status, support output privacy, and the saved interactive target under both Owner and staff logins.

### SQL operations module

`install-etp-sql-operations.ps1` installs a database-specific `etp_operations_<hash>` procedure in `master`, with the database and backup/restore roots fixed at installation. Calls accept only the supported operation and a constrained backup filename. The procedure verifies the caller's active application role, creates isolated `EtpRecovery_<guid>` databases for drills, and turns off trust and cross-database ownership chaining on the restored copy.

The separate `EtpOperationsModuleSigner` certificate/login supplies the module's `CREATE ANY DATABASE`, `ALTER ANY DATABASE` and `VIEW SERVER STATE` permissions. It is not the backup-encryption certificate and is not the Windows task login. Restrict control of its private key and module definition to SQL administrators. Altering the module requires reviewed reinstall/re-signing. Do not give the automation account `sysadmin`, `dbcreator`, or control of this signer to resolve a failed drill.

The installer grants the dedicated account module execution, backup-certificate visibility, `db_backupoperator` on the application database, and `etp_automation` for verified status recording. Reprovisioning an application account removes its automation and backup-operator memberships. After an approved account change, review and reapply the dedicated setup explicitly. The master module also checks active application role at each call; remove its old direct grant when retiring an automation identity.

## Certificate and password custody

In Owner **Settings → Database → Encrypted backup recovery keys**, choose two connected, separate recovery folders, confirm that both provide an off-PC copy, and enter a password of 16–128 characters. Select **Create and export recovery keys**.

SQL Server writes a uniquely named `.cer` and password-encrypted `.pvk` into each folder. Its service identity needs write access; the Owner process must be able to read the exports and write `%ProgramData%\EtpReporting\Backups\certificate-custody.json`. Strict folder permissions may require a supervised elevated setup session. Regular Owner day reopening does not require Windows elevation.

Each export creates a unique, never-overwritten `certificate-custody-<thumbprint>-<exportId>.json` receipt containing the certificate thumbprint and both file hashes, without the password. `certificate-custody.json` is only the latest pointer used when creating new backups; each backup receipt retains its exact immutable custody receipt and matching thumbprint. Re-exporting the same certificate or rotating to a new certificate cannot redirect older backups to different recovery copies. Preserve every custody receipt needed by retained backups. Directory checks reject duplicate/nested locations and links, but cannot prove physical independence or successful cloud synchronisation. Sagar must verify that the copies exist away from the PC and remain retrievable. Store the export password separately, with a documented recovery custodian. If the service creates a new `master` database master key, the supplied password also protects that key; retain it accordingly.

Preserve the original certificate/private key for as long as any retained backup needs them. Do not overwrite an old recovery export or replace the encryption certificate without preserving its recovery material. Microsoft documents that the original matching certificate is required for restoring encrypted backups. [Encrypted backup recovery requirements](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/backup-encryption?view=sql-server-ver17).

Daily backup validates recorded custody and the current SQL certificate thumbprint. It can run while recovery media are disconnected. A recovery drill requires both recorded copies to be accessible and match their hashes; reconnect them at their recorded locations before the drill. Copying only the custody JSON is insufficient. Retain a separate off-PC copy of the encrypted database backup and its receipt for machine-loss recovery; automatic off-PC backup replication is not implemented by these scripts.

## Backup, retention and task behaviour

| Task | Default schedule, local time |
| --- | --- |
| ETP Reporting Daily Backup | Daily at 22:00; configurable `-RunTime`. |
| ETP Reporting Monthly Recovery Drill | Daily runner at 08:00; performs the drill only on day 1 by default. `-DayOfMonth` accepts 1–28. |
| ETP Reporting Automated Operations | Every five minutes by default; imports approved sources and generates scheduled packs. |

All use the same account, S4U logon, Limited run level, StartWhenAvailable, IgnoreNew for overlap, and a two-hour execution limit. The monthly runner's day check means a missed run caught up on another day does not perform the missed drill; run a manual drill and inspect its evidence. Task scheduling does not prove that a job succeeded.

A backup uses a unique filename and SQL `COPY_ONLY`, `CHECKSUM`, AES-256 encryption, and `RESTORE VERIFYONLY`. After SQL verification, the script writes `<backup>.receipt.json` and `<database>-latest-verified.json`, containing the exact path, length, SHA-256, certificate reference and backup file metadata. These files contain paths and belong in the protected backup folder, not a general support attachment.

Rotation keeps the newest valid backup for each of the latest 14 UTC days represented in the receipts, plus the newest for each of the latest 12 UTC months represented. Overlapping selections count once; this is not necessarily 26 files. Only verified receipt/file pairs for the configured database are eligible for deletion. Unknown or invalid files remain for administrator review. These are copy-only full backups; the scripts do not provide transaction-log backup or point-in-time recovery scheduling.

The drill selects the file named by the latest verified receipt, validates its hash and custody copies, compares its metadata, restores every supported data/log file into an isolated database, runs `DBCC CHECKDB`, compares restored file metadata with the backup, and removes the isolated database. The hash is checked again before success. It does not compare today's live row counts with yesterday's backup. A successful drill proves restoration and these integrity/file checks; it does not reconcile business-report totals.

Success writes `<database>-latest-drill.json` and a protected SQL status record. System status shows separate UTC times and fingerprints for the latest backup and latest successful drill; a newer backup can correctly have a different hash from the drill. Backup absence is critical; backup age over 36 hours, missing drill, or drill age over 45 days produces a warning. Generic audit events cannot publish trusted recovery health. Staff read safe status through a database procedure without `msdb` access.

## Second-machine recovery exercise

**Required deployment evidence; not yet verified here.** Use an isolated second machine and a reviewed compatible SQL version. Keep the live shop database untouched. Retain the original backup, its receipt, both custody copies and the separately held password.

1. Verify the backup SHA-256 and length against its receipt. Verify each `.cer`/`.pvk` hash against the custody receipt. Work from copies in a protected recovery directory; give the recovery SQL service only the necessary file access.
2. As SQL administrator on the second machine, create a database master key in `master` if one is absent, then import the matching certificate and private key. Use a private administrative session and substitute the actual recovery files/passwords; do not save passwords in shared scripts or logs. For example:

   ```sql
   USE master;
   -- Create only if this instance has no database master key.
   CREATE MASTER KEY ENCRYPTION BY PASSWORD = '<new recovery-instance master-key password>';
   CREATE CERTIFICATE EtpBackupCert
     FROM FILE = N'<protected recovery directory>\EtpBackupCert.cer'
     WITH PRIVATE KEY
     (FILE = N'<protected recovery directory>\EtpBackupCert.pvk',
      DECRYPTION BY PASSWORD = '<original export password>');
   ```

   Do not replace an existing certificate with a different thumbprint. Confirm the imported thumbprint matches the receipt. The import sequence follows Microsoft's [encrypted backup recovery documentation](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/backup-encryption?view=sql-server-ver17).
3. Run `RESTORE VERIFYONLY ... WITH CHECKSUM` and `RESTORE FILELISTONLY` for the verified backup. Restore to a new, unused database name, with `MOVE` entries for **every** listed data/log file into the isolated recovery directory. Do not use `WITH REPLACE` or the live database name. The installed drill script is not a second-machine restore tool: its configuration and receipts refer to the original machine's paths.
4. Set the restored database's `TRUSTWORTHY` and `DB_CHAINING` options OFF. Run `DBCC CHECKDB`. Compare restored file IDs/logical names with the backup metadata; inspect the migration journal and representative report/import records at the backup's date.
5. Record the SQL version, certificate thumbprint, backup hash, restored database name, checks and outcome in restricted recovery evidence. Demonstrate recovery from the second custody copy too, using a clean isolated instance so an already-imported key cannot mask a missing/wrong copy. Remove only the explicitly identified test database and temporary recovery copies when the exercise is approved; retain the original recovery material.

## Owner recovery and maintenance

Retain an approved SQL administrator recovery path separate from the shared staff account. Windows administrator membership alone does not guarantee SQL administrator access.

For a locked business date, the normal Owner uses the app's reopen action with a reason. SQL validates `etp_owner` membership and records the reason and SQL actor atomically. A client administrator flag, direct row deletion, or moving the date is not a recovery method.

If all working Owner access is lost, stop unattended activity and have the approved SQL administrator connect locally to the correct database. Verify the intended Windows identity, then in one reviewed transaction call `dbo.configure_application_role` with that identity, `OWNER`, and active status, and create/update its matching `dbo.application_users` row with a non-empty recovery reason, `modified_by=SUSER_SNAME()` and current UTC time. Both SQL membership and the application entry are required. Preserve the user-history/audit records and last-Owner guard. Sign in as the recovered Owner, verify access, and remove any temporary access. Do not promote the shared staff account, disable audit triggers, or rewrite migration checksums to regain access.

SQL `sysadmin` retains the documented emergency path in the role guards and can change database security objects. This is a trusted administrative capability, not a staff account permission. Protect and review its use. Module-signing keys and backup-recovery keys serve different purposes and must remain separately controlled.

Run signed `invoke-database-maintenance.ps1` as Owner/SQL administrator with the explicit approved local instance and database. It performs integrity checks, updates statistics and copies old audit rows to the archive; originals remain append-only. Its default archive age is 730 days. It does not shrink the database or delete audit history.

## Troubleshooting

| Symptom | Action |
| --- | --- |
| Native encryption unsupported | Verify the actual SQL edition. Complete the approved edition transition; do not remove encryption from the backup command. |
| No protected operations configuration | Resolve the folder-access decision and run supervised folder setup. Do not substitute per-user settings for unattended tasks. |
| Setup cannot take its first pre-migration backup | Follow the staged existing-database procedure above. Preserve the old database and independent backup; no automatic rollback or restore is performed. |
| Script/signature or installation-permission failure | Verify the release publisher, signatures and protected installation ACLs. Repair/reinstall the signed package; do not weaken execution policy. |
| Certificate export fails | Check SQL `master` permissions, both existing recovery folders, SQL-service write access, Owner read access and custody-receipt write access. Partial files are not proof of completed custody. |
| Missing/changed recovery copy | Reconnect both original copies and compare hashes. A drill must fail until custody is valid; do not edit receipt hashes to silence it. |
| Backup or metadata hash mismatch | Retain evidence, stop using the suspect pair, and investigate. Produce a new verified backup; do not select the newest `.bak` by modification time. |
| SQL module permission failure | Verify active Store Manager application/SQL roles, dedicated automation membership, module signature and certificate visibility. Reprovisioning may have intentionally removed automation privileges. |
| Cancelled drill or isolated database remains | Have the SQL administrator inspect active sessions and exact `EtpRecovery_<guid>` ownership/files. Clean up only the confirmed isolated drill after review; do not restart the live service or remove databases by an unchecked wildcard. |
| Task green but recovery status stale | Inspect last-run result and the separate verified backup/drill receipts. The monthly runner can exit successfully on an ordinary day without a drill. |
| Low backup space | Resolve capacity before the next backup. Default backup minimum is 5 GB; dashboard warnings start below 20 GB. Invalid or unknown backups are deliberately retained for review. |

Use the aggregate support package and privacy-safe application diagnostics for ordinary support. Do not attach source workbooks, customer records, connection strings, recovery passwords/keys, raw receipts, or private setup/SQL logs. Restrict administrator investigation to the necessary local evidence and record an accurate outcome; never manufacture a successful backup/drill status to clear a warning.
