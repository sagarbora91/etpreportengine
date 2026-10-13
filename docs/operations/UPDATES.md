# Updating ETP from OneDrive (1.12)

Plain-English guide to the in-app "Update available" flow. Status: checked against the B1 code at the 1.12.0 wave-1 integration (13 Oct 2026). **1.12.0 ships the engine only:** the banner, the confirm box, Settings > Software update and the result screen come in wave 2 (B2). Until then nothing below is visible in ETP.

## Who does what
- **Only Sagar publishes an update.** Nobody else has the signing key, and shop staff never see an update banner.
- **Only the Owner can install one**, from the banner in ETP on Workpc (wave 2). Other roles see nothing.
- There is no paid certificate (decision 39). Trust comes from a signature made with a free key that Sagar keeps on two USB sticks.

## One-time: make the signing key (Sagar, offline)
1. Plug in **two USB sticks**. On Workpc run, in Windows PowerShell:
   `powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\New-EtpReleaseSigningKey.ps1 -UsbFolder 'F:\ETP-Key','G:\ETP-Key'`
   - The two folders must be on two different removable drives; the system drive, a network drive and OneDrive are refused.
   - Some USB sticks and USB SSDs tell Windows they are "fixed" disks. For those add `-AllowFixedDrive`; the script then asks you to type `FIXED <letter>` for each one, to confirm it is a stick you will unplug.
2. Choose a passphrase of **at least 16 characters**, typed twice. Write it on paper and keep the paper apart from the sticks (never on Workpc or in OneDrive).
3. The script writes the same encrypted key file `etp-release-signing-key.json` (and `etp-release-public-key.txt`) to both sticks, never overwrites an existing key, reads both back, and prints the key id, the fingerprint and one line of C#.
4. That line is pasted into `src\Etp.Reporting.Application\Updates\ReleaseSigningKeys.cs` (`Trusted`) and shipped in a release. Until then ETP says "Updates are not set up on this PC" and offers nothing.
5. If both sticks (or the passphrase) are lost, make a new key and do one manual install of a release that carries the new public key. Nothing else breaks.

## Publishing an update (Sagar)
1. Build the release and pass the gate and the VM install check as usual.
2. Plug in one key stick.
3. Run `E:\ETP\Migration 2026-10-04\Publish-Release.ps1 -Version <x.y.z> -Worktree <release worktree> -SigningKeyPath 'F:\ETP-Key\etp-release-signing-key.json' -MinimumFromVersion <oldest version that may update straight to this one> -NotesPath <notes.txt>`. Type the passphrase when asked. (`-MinimumFromVersion` is required with `-SigningKeyPath`; `-FeedRoot` overrides the folder; `-SkipFeed` signs without copying. Without `-SigningKeyPath` the script works exactly as before, unsigned.)
4. The script writes `release.json` (schema 2: version, installer hash and size, the lowest version it can update from, notes), signs it, adds the `.sig` to `SHA256SUMS`, and copies into `OneDrive\ETP-Updates\<version>\` in this order: the installer, `release.json.sig`, `release.json` last, so a half-synced folder is never offered. A version already in the folder is never replaced.
5. Wait for OneDrive on Workpc to finish syncing (no spinning icon), then remove the stick.

There is no "latest" file. ETP lists the version folders and takes the highest one whose signature checks out and that may be installed over the running version.

## What the Owner will see (wave 2)
- A banner: "ETP 1.12.x is available", with the notes and **Install now**. Update checking is off (`product_settings.update_check_enabled = 0`, migration 0065) until Sagar turns it on in Settings > Software update, after the first real Workpc upgrade has worked. The feed folder is `product_settings.update_feed_path`; empty means `<your OneDrive>\ETP-Updates`.
- The confirm box lists what must be quiet first (no import, nightly check, backup or drill running; no open entry draft; no other ETP window) and says ETP will close. Shop hours are allowed.
- ETP then copies the installer to `C:\ProgramData\EtpReporting\Updates\<version>\`, checks its hash and version again, takes a verified backup (`backup-etp-database.ps1 -Purpose PreUpdate`, kept by rotation), keeps the two previous installers in `Updates\PreviousReleases\`, writes the attempt to `Updates\attempts.jsonl`, runs setup (which asks for administrator approval itself), and setup restarts ETP (not as administrator) when it succeeded.
- Setup creates the protected `Updates` folder (SYSTEM and Administrators full control, active Owners read and write). Without it ETP says "Updates are not set up on this PC": run setup again.

## If it fails
- ETP shows: "Update failed. Your data is safe. Backup taken at hh:mm (file). Call Sagar." with **Open setup log folder** (wave 2 screen; setup writes `Updates\setup-result.json` and its log beside the staged installer).
- **ETP never restores the database by itself.** The backup is there if Sagar decides it is needed.
- Do not run setup again and again. Send Sagar the log folder and `Updates\attempts.jsonl` under `C:\ProgramData\EtpReporting`.
- Sagar's checks: was the installer fully downloaded from OneDrive (not a "cloud only" placeholder)? Does the database say "upgraded by a newer release"? See the troubleshooting table in `docs\OPERATIONS.md`.
- ETP refuses by design: a version older than the installed one, a version below `minimumFromVersion`, a wrong signature, an unknown key, an installer that does not match its hash or version.
