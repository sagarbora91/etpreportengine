# ops/pipeline - release pipeline scripts

Copies (K1, code-protection plan, decision 65) of the gate / chain / installer / publish / VM scripts that
previously lived only on Workpc, so a fresh PC with only this repo can gate and release. Nothing here is built by
any csproj and no test runs these files. Scripts are unchanged except as listed under "Scrubbed data".

## Expected layout (hard-coded paths, kept as they are)

The scripts expect Workpc's layout. They dot-source / call each other through `$PSScriptRoot` or the fixed folder:

| Path | Purpose | Copy these files there |
|---|---|---|
| `E:\ETP\Migration 2026-10-04\` | pipeline scripts | everything at the top level of `ops/pipeline/` |
| `E:\ETP\Code\Worktrees\_tools\` | build lock | `tools\Invoke-Serialized.ps1` (writes `heavy-jobs.log`; honours `PAUSE-LANE-BUILDS` there) |
| `E:\ETP\Reference\Work in progress 2026-10-04 (CONTAINS BUSINESS DATA)\` | VM harness | `harness\*.ps1` only (the harness data, packs and checksums stay out of git) |
| `E:\Tools\PowerShell\7\pwsh.exe`, `E:\Tools\Inno Setup 6`, `E:\Tools\NuGetCache` | tools | - |
| `E:\ETP\Installers\...`, `E:\ETP\Reference\...` | outputs, SQL payload, evidence | created by the scripts |
| `F:\ETP-Key\` (USB stick) | code-signing PFX and release signing key | never on a fixed drive |

Dependencies: `Start-Gate.ps1` and `Start-FastGate.ps1` dot-source `GateCommon.ps1`; `Prepare-Gate-*` dot-sources `GateCommon.ps1`;
Run-Gate / Chain / Build / Publish call `tools\Invoke-Serialized.ps1` at `E:\ETP\Code\Worktrees\_tools\`. `Run-VM-1.12.0-Sequence.ps1`,
`Run-VM-1.13.0-Sequence.ps1` and `Run-VM-Rollback-1.12.0-Sequence.ps1` call the harness scripts at the business-data path above.

## Files and the paths they assume

- 1.12.0-w2 family: `Prepare-Gate-1.12.0-w2.ps1`, `Run-Gate-1.12.0-w2.ps1`, `Chain-Gate-Then-Installer-1.12.0-w2.ps1`, `Build-Installer-1.12.0-w2.ps1`, `Publish-Installer-1.12.0-w2.ps1` - worktree `E:\ETP\Code\Worktrees\integrate-112-w2`, `E:\ETP\Installers\ETP Reporting Engine 1.12.0`, SQL payload folder under `E:\ETP\Installers`.
- Gate: `Start-Gate.ps1` (default worktree `release-1.11.2`), `Start-FastGate.ps1` (`E:\ETP\Reference\FastGate\`), `GateCommon.ps1`, `Start-Gate-1.12.0.ps1` / `Rerun-Gate-1.12.0.ps1` (`release-1.12.0`).
- Generic: `Build-Installer.ps1`, `Publish-Release.ps1` (default worktree `release-1.11.2`, `E:\ETP\Installers`, key `F:\ETP-Key\etp-release-signing-key.json`), `Prepare-SqlPayload.ps1` (SQL payload under `E:\ETP\Installers`).
- 1.13.0 family (lane G1): `Start-Gate-1.13.0.ps1`, `Rerun-Gate-1.13.0.ps1`, `Run-Gate-1.13.0.ps1`, `Chain-Gate-Then-Installer-1.13.0.ps1`, `Build-Installer-1.13.0.ps1` (PFX `F:\ETP-Key\etp-codesign.pfx`, symbols `E:\ETP\Reference\Symbols`), `Publish-Release-1.13.0.ps1`, `Run-VM-1.13.0-Sequence.ps1` - worktree `release-1.13.0`.
- VM: `Run-VM-1.12.0-Sequence.ps1`, `Run-VM-Rollback-1.12.0-Sequence.ps1`, `harness\Run-VM-Acceptance-1.12.0.ps1` (also reads installers 1.9.2/1.9.3/1.9.6/1.12.0 under `E:\ETP\Installers` and the consolidated import package), `harness\Run-VM-Rollback-1.12.0.ps1` (guest `C:\ETP-Restore`). There is no `Run-VM-Acceptance-1.13.0.ps1` on Workpc yet; `Run-VM-1.13.0-Sequence.ps1` is written against the 1.12.0 harness name pattern.
- Go-live / host: `Workpc-GoLive-Preflight.ps1` (`E:\ETP\Reference\GoLive-Workpc-*`, `G:\ETP-GoLive-*`), `Disable-FastStartup.ps1`, `Retire-Old-Databases.ps1` (`E:\ETP\Reference\Old databases (CONTAINS BUSINESS DATA)`).
- Elevated scripts run in pwsh 7 (UAC); the harness runs in Windows PowerShell 5.1 elevated. Signing passphrases are prompted, never stored.

## Scrubbed data (parameter instead of embedded figures)

`harness\Run-VM-Acceptance-1.12.0.ps1` embedded live-derived business figures. They are removed; the script now takes an optional
`-ExpectedFiguresPath <json>` (keep that file OUT of git, on the business-data path). Shape:

```json
{ "titan": { "R030": {"new":0,"present":0,"conflicts":0}, "R022": {...}, "R010": {...} },
  "s004": [ {"Date":"yyyy-MM-dd","Cash":0,"Card":0,"Upi":0} ],
  "serviceSrnOpen": 0 }
```

Without the file: held-Titan counts are only recorded (INFO), the S004 comparison list is empty, and the Q5 open-SRN check is REVIEW.
Also removed from user-facing text: stock totals, cash book / DSR / tender figures, live row counts. The VM checkpoints names, a
Titan file hash prefix (8 hex chars), expected import-file counts (`$ServiceExpect`) and retired machine names remain (no credentials, no customer data).

## Kept on Workpc only (not in git)

Older per-version copies (1.9.x - 1.11.x: `Build-Installer-*`, `Chain-Gate-Then-Installer-*`, `Publish-Installer-*`, `Run-Gate-*`,
`Rerun-Gate-*`, `Start-Gate-*`, `Run-VM-1.11.*-Sequence`, `Run-VM-Rollback-1.11.4*`, `Run-VM-Acceptance-1.9.*-1.12.0` other than 1.12.0),
`.bak-*` copies, the superseded 1.12.0 wave-1 files (`Run-Gate-1.12.0`, `Build-Installer-1.12.0`, `Chain-Gate-Then-Installer-1.12.0`,
`Publish-Installer-1.12.0`, `Prepare-Gate-1.12.0`), one-off fix/trace/recovery scripts, the nightly-integration scripts, all logs and evidence,
and the Run-VM-Acceptance harness data.
