# Recovering ETP development on a fresh PC (and with a new Claude)

Written 15 Oct 2026 (lane C1). Purpose: someone - Sagar, a developer, or a Claude session with no memory of earlier
conversations - can rebuild the development set-up, build, test and release ETP from this file plus the repository and
the `E:\ETP` folder. **No secret is written here.** Where a password, passphrase or key is needed, this file says who
holds it and where it is used, never the value.

This is about the *development* set-up. Restoring the *shop's data* onto a new PC is `docs/operations/DR-RUNBOOK-USB.md`.

Markers: **SAGAR** = an account, password or decision only Sagar can do. **ADMIN** = needs an elevated window (UAC).

## 0. What you need in hand

- A Windows 11 Pro x64 PC, 30 GB free on C:, internet for the downloads and GitHub.
- **SAGAR:** the GitHub account `sagarbora91` (passkey / authenticator; recovery codes are on paper in the USB-kit
  envelope). The repository `sagarbora91/etpreportengine` is private.
- A copy of the `E:\ETP` folder (old PC, or its backup on G:). It holds what is **not in git**: the gate/release/VM scripts
  (`E:\ETP\Migration 2026-10-04\`), the build lock (`E:\ETP\Code\Worktrees\_tools\Invoke-Serialized.ps1`), the handoff
  (`E:\ETP\NEW-PC-HANDOFF.md`), plans and notes (`E:\ETP\Reference\`), installers (`E:\ETP\Installers\`), the USB-kit
  source (`E:\ETP\USB-KIT\`), and the Hyper-V acceptance VM. Without it you can still build and unit-test from git alone
  (sections 1-4), but not run the gates, the installer chain or the VM.
- **For a release only:** the two USB key sticks (section 7). Not needed to build or test.

## 1. Tools (versions as on Workpc, Oct 2026)

| Tool | Version | Notes |
|---|---|---|
| Git for Windows | 2.5x | keep Git Credential Manager (`credential.helper = manager`); first fetch opens a browser sign-in (**SAGAR**) |
| .NET SDK | 10.0.401 (any 10.0.4xx) | `dotnet --version` |
| PowerShell 7 | 7.6 | Workpc keeps a copy at `E:\Tools\PowerShell\7\pwsh.exe`, which the gate scripts call by that path |
| Windows PowerShell 5.1 | built in | the VM harness and OneDrive script tests run in it |
| Inno Setup 6 | 6.x | installer builds only; scripts expect `E:\Tools\Inno Setup 6\ISCC.exe` |
| Windows SDK signtool | 10.0.26100 | signing only (`Set-AuthenticodeSignature` also works) |
| Obfuscar, ilspycmd | pinned versions in `RELEASE-PROTECTION.md` / `VERIFICATION.md` | release builds only, installed under `E:\Tools` |
| Hyper-V | Windows feature (**ADMIN**) | for the acceptance VM |
| Claude Code | current | `irm https://claude.ai/install.ps1 \| iex`, then sign in (**SAGAR**) |

Put the drive letters back as they were (`E:` for the project, `G:` for the backup disk) - many scripts use absolute
`E:\ETP\...` paths. If the new PC has no `E:`, map one (`subst E: <folder>`) rather than editing scripts. Do not create
folders at the root of `E:` unelevated; Windows refuses - create them under `E:\ETP\`.

## 2. SQL Server Express (for the integration tests and the app)

Needed for SQL tests and running the app; not needed for a unit-test-only build.

1. **ADMIN.** Install SQL Server 2025 Express (media under `E:\ETP\Installers\`), instance name **`SQLEXPRESS`**,
   **Windows authentication only**, sysadmins = **`BUILTIN\Administrators` only** (do not add your own account), TCP/IP
   and named pipes off, collation `Latin1_General_CI_AS`. Then apply the latest cumulative update (Workpc runs CU9; the
   RTM build crashed once under the gate).
2. **Put the SQL data on C:**, never on a disk with 16 KB sectors (Workpc's E: NVMe refuses SQL Server without a driver
   override).
3. Check: `sqlcmd -S .\SQLEXPRESS -E -C -Q "select @@version"` from an elevated window.

Because only `BUILTIN\Administrators` is sysadmin, anything that creates databases (the SQL integration tests, the gate)
must run **elevated**. That is by design. Never make the developer account a sysadmin login to avoid UAC.

Optional: a second instance `.\ETPTEST` for the nightly integration run (`E:\ETP\Migration 2026-10-04\Install-EtpTestInstance.ps1`,
**ADMIN**; see `docs/operations/CI.md` section 4).

## 3. Clone and lay out the worktrees

```powershell
# into the copied E:\ETP, or a fresh E:\ETP\Code if none was copied
git clone https://github.com/sagarbora91/etpreportengine.git "E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f"
cd "E:\ETP\Code\SaagarCC-V6-ETP-Source-and-Report-Engine-605002f"
git fetch --all --tags
# one worktree per lane or release line, e.g.
git worktree add "E:\ETP\Code\Worktrees\release-1.13.0" release/1.13.0
```

The folder name of the main clone is historical; keep it, scripts and notes refer to it. Each lane works only in its own
`E:\ETP\Code\Worktrees\<lane>`. Read `CLAUDE.md` at the repository root next.

## 4. Build and unit tests (no SQL, no elevation)

```powershell
cd E:\ETP\Code\Worktrees\<lane>
dotnet restore Etp.Reporting.slnx
dotnet build Etp.Reporting.slnx -c Release -m:1 -nodeReuse:false
dotnet test tests-dotnet\Etp.Reporting.Domain.Tests\Etp.Reporting.Domain.Tests.csproj -c Release --filter "Category!=Sql"
# likewise Import, Reporting, SqlServer, Desktop, Infrastructure.Http
```

The build treats warnings as errors and fails on NuGet security advisories; both are intended. On Workpc wrap every build
and test in the build lock (`CLAUDE.md` section 4). Expected: 0 warnings, 0 errors; unit projects green (the permissions
guard may fail until the integrator regenerates `ExpectedRolePermissions.json` at the elevated gate).

## 5. Gates

- **Fast gate** (unelevated, no SQL, a few minutes):
  `& 'E:\ETP\Migration 2026-10-04\Start-FastGate.ps1' -Worktree <worktree>` - results in `E:\ETP\Reference\FastGate\`.
- **Elevated release gate** (**ADMIN**, Sagar approves UAC, ETP app closed):
  `& 'E:\ETP\Migration 2026-10-04\Start-Gate.ps1' -Version <x.y.z> -Worktree <worktree>`, or the per-release
  `Chain-Gate-Then-Installer-<ver>.ps1` (gate -> `Build-Installer` -> `Publish-Installer`). It needs SQL from section 2
  and a working daily backup (it takes a fresh verified backup first). Read `gate-<ver>-exit.txt` and the whole log; never
  a filtered view.

## 6. The acceptance VM

- Hyper-V VM **`ETP-Acceptance-186`**, about 83 GB, kept in the main clone's git-ignored
  `artifacts\acceptance-lab-20260911\VM` folder (copied with `E:\ETP`, never in git). Import it in Hyper-V Manager
  (**ADMIN**) if the PC is new.
- Checkpoints are named `VM195 ...` / `VM196 ...` (shop-upgrade baselines) plus `Tally base`. Restoring, saving or stopping
  the VM is **SAGAR**'s step.
- Harness: `E:\ETP\Migration 2026-10-04\Run-VM-<ver>-Sequence.ps1` (elevated Windows PowerShell 5.1) drives
  `E:\ETP\Reference\Work in progress 2026-10-04 (CONTAINS BUSINESS DATA)\Run-VM-Acceptance-<ver>.ps1`. **SAGAR** types the
  VM account password at its prompt; nothing is stored. Evidence goes to `E:\ETP\Reference\VM195-<time>-*\`. Notes for the
  current version: `E:\ETP\Reference\Work in progress 2026-10-13\VM-HARNESS-1.12.0-NOTES.md`.

## 7. Keys: where they physically are

| Key | Physical place | Used by |
|---|---|---|
| Release signing key (`etp-release-signing-key.json`, passphrase-encrypted) | both USB key sticks, folder `ETP-Key` | `Publish-Release.ps1 -SigningKeyPath <stick>\ETP-Key\...` |
| Code-signing PFX (`etp-codesign.pfx`, passphrase-protected; from 1.13.0) | both USB key sticks | `Build-Installer.ps1 -Protect -PfxPath <stick>\ETP-Key\etp-codesign.pfx` |
| OneDrive backup key file | USB kit stick, `key\` | OneDrive backup decrypt (DR runbook) |
| Passphrases | **on paper**, kept apart from the sticks, with the public fingerprints | typed by **SAGAR** at the prompt |
| GitHub recovery codes | on paper in the USB-kit envelope | GitHub sign-in recovery |

One stick at home, one with a trusted person or in a locker; never both next to Workpc. Claude never asks for, reads, copies
or prints any of these. A development build needs none of them. Details of the ceremonies: `CODE-SIGNING.md` and
`scripts/release/New-EtpReleaseSigningKey.ps1`; register of ids and dates: `SECURITY.md` section 6.

## 8. A new Claude session

1. Open Claude Code in the lane's worktree. The repository `CLAUDE.md` loads automatically.
2. Read the top block of `E:\ETP\NEW-PC-HANDOFF.md` (current releases, next steps, decisions).
3. Memory notes are under `C:\Users\<you>\.claude\projects\E--ETP\memory\` (`MEMORY.md` is the index). On a new PC copy
   that folder from the old profile (it travels with the `E:\ETP` copy only if someone copied it there). If it is lost,
   `CLAUDE.md`, the handoff and `E:\ETP\Reference` still carry the rules and state; nothing in the memory is a secret.
4. Global Claude settings and the graphify skill: optional; `NEW-PC-RESTORE.md` in `E:\ETP` lists the pinned versions.

## 9. Diagnosing a crash in a protected (released) build

1. Take the version (`ApplicationVersion`) from the diagnostics log entry and the commit from the release notes or `release.json`.
2. Open `E:\ETP\Reference\Symbols\<version>-<sha7>\` (PDBs and `Mapping.txt`; also on G:).
3. Translate renamed frames with `scripts\release\Resolve-EtpStackTrace.ps1` (lane V1) and the map.
4. Reproduce on the **unprotected** build of the same commit first (`<OutputDirectory>-unprotected`); only if it does not
   reproduce there is the obfuscation suspect - then see `RELEASE-PROTECTION.md`.

## 10. Recovery acceptance test (run on the VM)

Run once with 1.13.0, then after any big change to tools or layout. Its purpose is to prove this file is enough.

**Setup.** A **clean** Windows checkpoint of the Hyper-V VM - not a `VM195`/`VM196` shop-upgrade checkpoint. **SAGAR**
creates or restores it and takes a checkpoint named `Recovery test base <date>` so it can be undone.

**Procedure.** An agent (a fresh Claude session with no conversation memory) follows this file literally, recording each
step's result and time:

1. Install the section 1 tools (Git, .NET SDK, PowerShell 7, Inno Setup).
2. **SAGAR** signs in to GitHub when Git asks.
3. Clone (section 3); copy in the parts of `E:\ETP` the test needs (`Migration 2026-10-04`, `Code\Worktrees\_tools`).
4. SQL Express as in section 2 (**ADMIN**).
5. Build and run all unit tests (section 4). Pass = 0 errors, 0 failures except the documented permissions guard.
6. Make a harmless change (a comment in a `.cs` file), rebuild, run the affected unit project, revert the change.
7. Dev build of the app; start it against a scratch database `EtpAccept_Recovery` (`--initialize-database`, then
   `--automation-once` on `tests-dotnet\fixtures\etp-sample`). Pass = the app opens and the import succeeds.
8. **SAGAR** plugs in one key stick; protected build:
   `build-windows-release.ps1 -Protect` via `Build-Installer.ps1 -Protect -PfxPath <stick>` (lanes O1/S1/G1). Pass = the
   protected smoke passes, signatures verify, no `*.pdb` in the output, symbols in the archive folder.
9. Diagnose a deliberate exception frame with the archived map (section 9).
10. **SAGAR** reverts the VM to `Recovery test base <date>`.

**Pass criteria.** Every step done with knowledge found in this repository or `E:\ETP`; no secret appeared in any log,
note or chat. **Any step that needed knowledge not written down is a failure of this document**: fix the document in the
same lane, then re-run that step.

### Record of recovery tests

| Date | ETP version / commit | Who ran it | Result | Steps that needed a fix |
|---|---|---|---|---|
| (not run yet; planned with 1.13.0) | | | | |
