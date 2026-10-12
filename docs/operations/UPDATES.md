# Updating ETP from OneDrive (1.12)

Plain-English guide to the in-app "Update available" flow. Status: written from the 1.12 wave plan before the code is merged. Items marked **CHECK AT INTEGRATION** must be compared with the B1/B2 lane notes.

## Who does what
- **Only Sagar publishes an update.** Nobody else has the signing key, and shop staff never see an update banner.
- **Only the Owner can install one**, from the banner in ETP on Workpc. Other roles see nothing.
- There is no paid certificate (decision 39). Trust comes from a signature made with a free key that Sagar keeps on two USB sticks.

## One-time: make the signing key (Sagar, offline)
1. On a PC that is not connected to anything shared, run `scripts\release\New-EtpReleaseSigningKey.ps1`.
2. Choose a passphrase. Write it somewhere safe that is not on Workpc or in OneDrive.
3. The script saves the private key (encrypted) to two USB sticks and prints the public key and its fingerprint. Label both sticks "ETP release key".
4. The public key is pasted into `ReleaseSigningKeys.cs` and shipped in a release. Until that is done, ETP says "Updates are not set up on this PC" and offers nothing.
5. If both sticks are lost, make a new key and do one manual install of a release that carries the new public key. Nothing else breaks.

## Publishing an update (Sagar)
1. Build the release and pass the gate and the VM install check as usual.
2. Plug in the USB key stick.
3. Run `Publish-Release.ps1 -SigningKeyPath <the stick>\<key file>`. Type the passphrase when asked.
4. The script writes `release.json` (version, file hash, "needs a database change" flag, the lowest version it can update from, notes), signs it, and copies into `OneDrive\ETP-Updates\<version>\` in this order: `setup.exe`, `release.json.sig`, `release.json`. The manifest goes last, so a half-synced folder is never offered.
5. Wait for OneDrive on Workpc to finish syncing (no spinning icon), then remove the stick.

There is no "latest" file. ETP lists the version folders and takes the highest one whose signature checks out.

## What the Owner sees
- A banner: "ETP 1.12.x is available", with the notes and **Install now**. Update checking is switched off until Sagar turns it on in Settings, after the first real Workpc upgrade has worked.
- The confirm box lists what must be quiet first (no import, nightly check, backup or drill running; no open entry draft) and says ETP will close. Shop hours are allowed.
- ETP then copies the installer to a local folder, checks its fingerprint and version again, takes a verified backup, keeps the last two installers, writes a note of the attempt, runs setup, and restarts.

## If it fails
- ETP shows: "Update failed. Your data is safe. Backup taken at hh:mm (file). Call Sagar." with **Open setup log folder**.
- **ETP never restores the database by itself.** The backup is there if Sagar decides it is needed.
- Do not run setup again and again. Send Sagar the log folder and `Updates\attempts.jsonl` under `C:\ProgramData\EtpReporting`.
- Sagar's checks: was the installer fully downloaded from OneDrive (not a "cloud only" placeholder)? Does the database say "upgraded by a newer release"? See the troubleshooting table in `docs\OPERATIONS.md`.
- ETP refuses by design: a version older than the installed one, a version below `minimumFromVersion`, a wrong signature, an unknown key, an installer that does not match its hash or version.

## CHECK AT INTEGRATION
- Exact script names, parameters and key file name (B1 note).
- Where `attempts.jsonl` and the logs are written.
- Setting names and default feed path (migration 0064 becomes 0065 after the +1 renumber).
