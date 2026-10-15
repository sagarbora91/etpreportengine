# 1.13.0 (code-protection lane G1, 15 Oct 2026; copy of Chain-Gate-Then-Installer-1.12.0-w2.ps1, not run yet). Run ELEVATED in pwsh 7 (Sagar's UAC),
# ETP app closed, the USB stick with the code-signing PFX and the release signing key IN (two passphrase prompts: signing, then release.json).
# Gate -> protected installer build (obfuscate, smoke vs unprotected, sign, Inno, symbols archived) -> signed publish.
# Log: chain-1.13.0.log beside this script. Every ending writes exactly one of: DONE / "did not pass" / "Installer build failed" / "Publish failed"
# (Run-VM-1.13.0-Sequence.ps1 and the watcher key on these).
#   & 'E:\ETP\Migration 2026-10-04\Chain-Gate-Then-Installer-1.13.0.ps1' -PfxPath 'F:\ETP-Key\etp-codesign.pfx' -SigningKeyPath 'F:\ETP-Key\etp-release-signing-key.json' -PreviousVersion 1.12.0 -MinimumFromVersion 1.12.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PfxPath,
    [Parameter(Mandatory)][string]$SigningKeyPath,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$PreviousVersion,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$MinimumFromVersion,
    [string]$NotesPath,
    [switch]$SkipFeed
)
$d = 'E:\ETP\Migration 2026-10-04'; $log = "$d\chain-1.13.0.log"
$wt = 'E:\ETP\Code\Worktrees\release-1.13.0'
"START $(Get-Date -Format s)" | Set-Content $log
foreach ($f in @($PfxPath, $SigningKeyPath)) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { "Preparation or gate did not pass; installer NOT built. Missing key file $f (is the USB stick in?)" | Add-Content $log; exit 1 }
}
& "$d\Run-Gate-1.13.0.ps1"
$g = Get-Content "$d\gate-1.13.0-exit.txt" -ErrorAction SilentlyContinue
"gate: $($g -join ' | ')" | Add-Content $log
if (-not ($g -match '^Gate exit code: 0$')) { 'Preparation or gate did not pass; installer NOT built.' | Add-Content $log; exit 1 }
& "$d\Build-Installer-1.13.0.ps1" -Version 1.13.0 -Worktree $wt -Protect -PfxPath $PfxPath
$b = Get-Content "$d\installer-build-1.13.0-exit.txt" -ErrorAction SilentlyContinue; "installer: $b" | Add-Content $log
if ($b -notmatch 'Exit code: 0') { 'Installer build failed.' | Add-Content $log; exit 1 }
# DONE only when the publish step succeeded (the VM sequence waits on DONE). Publish-Release-1.13.0 refuses symbols in the publish folder and
# asks for the release key passphrase (interactive, so it runs in this window, not in a child with redirected input).
$pubArgs = @{ Version = '1.13.0'; Worktree = $wt; PreviousVersion = $PreviousVersion; SigningKeyPath = $SigningKeyPath; MinimumFromVersion = $MinimumFromVersion; SqlPayloadIncluded = $true }
if ($NotesPath) { $pubArgs.NotesPath = $NotesPath }
if ($SkipFeed) { $pubArgs.SkipFeed = $true }
$p = 0
try { & "$d\Publish-Release-1.13.0.ps1" @pubArgs *>&1 | Tee-Object -FilePath $log -Append | Out-Host }
catch { "Publish error: $($_.Exception.Message)" | Add-Content $log; $p = 1 }
if ($p -ne 0) { "Publish failed (exit $p); the installer folder may be incomplete. $(Get-Date -Format s)" | Add-Content $log; exit 1 }
"DONE $(Get-Date -Format s)" | Add-Content $log
