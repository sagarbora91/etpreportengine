# 1.13.0 COPY of Build-Installer.ps1 (code-protection lane G1, 15 Oct 2026; the original is untouched). Not run yet, parse-checked only.
# Adds, against the contracts in CODE-PROTECTION-PLAN.md 5.1:
#   -Protect    passes -Protect to the release/installer build (obfuscation of the ETP assemblies, folder publish). The contract: the
#               protected publish is <ReleaseDirectory>, the unprotected one <ReleaseDirectory>-unprotected, and symbols + Mapping.txt +
#               obfuscation-report.json are in <ReleaseDirectory>-symbols.
#   -PfxPath    Authenticode PFX on a USB stick (the passphrase is prompted by sign-etp-artifacts.ps1; nothing is stored here). Required with -Protect.
#   Then, still inside the same serialised job: the protected-vs-unprotected smoke (scripts\release\Test-EtpProtectedBuild.ps1; exit 0 only on
#   identical results; a failure fails this build, so the chain never publishes), a check that no .pdb/.map/Mapping.txt is in the publish folder,
#   and the symbol archive to E:\ETP\Reference\Symbols\<version>-<sha7>\ (never beside the installer).
# Note: the smoke runs after build-windows-installer.ps1 returns (that script builds the release and the installer in one go). A smoke failure
# fails this script, so nothing is published; the installer folder left behind must be discarded.
# Run ELEVATED in pwsh 7, with the USB stick in (the passphrase prompt needs the console window).
#   & 'E:\ETP\Migration 2026-10-04\Build-Installer-1.13.0.ps1' -Version 1.13.0 -Worktree 'E:\ETP\Code\Worktrees\release-1.13.0' -Protect -PfxPath 'F:\ETP-Key\etp-codesign.pfx'
# Writes installer-build-<Version>-exit.txt ("Exit code: N") beside this script; the build log is installer-build-<Version>-<sha7>-b1.log.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Worktree,
    [string]$SqlPayloadDirectory = 'E:\ETP\Installers\SQL payload for the ETP installer (2025 + ODBC 17)',
    [switch]$NoSqlPayload,
    [switch]$Protect,
    [string]$PfxPath,
    [string]$SymbolsRoot = 'E:\ETP\Reference\Symbols',
    [string]$FixtureFolder = 'tests-dotnet\fixtures\etp-sample',
    [string]$InnoSetupCompiler = 'E:\Tools\Inno Setup 6',
    [string]$ToolsDirectory = 'E:\ETP\Code\Worktrees\_tools',
    [string]$PowerShellExe = 'E:\Tools\PowerShell\7\pwsh.exe',
    [int]$WaitMinutes = 90
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Worktree)) { throw "Worktree not found: $Worktree" }
$exitFile = Join-Path $PSScriptRoot "installer-build-$Version-exit.txt"
Remove-Item $exitFile -ErrorAction SilentlyContinue
if ($Protect -and -not $PfxPath) { throw '-Protect needs -PfxPath (the code-signing PFX on the USB stick). An unsigned protected release is not a 1.13.0 release.' }
if ($PfxPath -and -not $Protect) { throw '-PfxPath without -Protect: signing is only part of the protected build.' }
if ($PfxPath) {
    if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) { throw "No PFX at $PfxPath. Is the USB stick in?" }
    $pfxFull = (Resolve-Path -LiteralPath $PfxPath).Path
    if ((Split-Path -Qualifier $pfxFull) -ieq 'E:' -or (Split-Path -Qualifier $pfxFull) -ieq 'C:') { throw 'The PFX must be on the USB stick, not on a fixed drive.' }
}
$payloadArg = ''
if (-not $NoSqlPayload) {
    if (-not (Test-Path -LiteralPath $SqlPayloadDirectory -PathType Container)) { throw "SQL payload folder not found: $SqlPayloadDirectory (run Prepare-SqlPayload.ps1, or pass -NoSqlPayload)." }
    $payloadArg = "-SqlPayloadDirectory '$SqlPayloadDirectory'"
}
$protectArg = ''
if ($Protect) { $protectArg = "-Protect -PfxPath '$PfxPath'" }
$protectOn = if ($Protect) { '$true' } else { '$false' }
$log = Join-Path $PSScriptRoot "installer-build-$Version"
$cmd = @"
`$sha = (git rev-parse --short=7 HEAD).Trim()
`$tag = "`$sha-b1"
`$log = "$log-`$tag.log"
`$rel = "artifacts\windows-release-`$tag"
try {
    & .\scripts\build-windows-installer.ps1 ``
        -ReleaseDirectory `$rel ``
        -OutputDirectory  "artifacts\installer-`$tag" ``
        $payloadArg ``
        $protectArg ``
        -InnoSetupCompiler '$InnoSetupCompiler' *>&1 | Tee-Object -FilePath `$log
    if (`$LASTEXITCODE) { exit `$LASTEXITCODE }
    if ($protectOn) {
        `$relFull = (Resolve-Path `$rel).Path
        `$unprot = "`$relFull-unprotected"
        `$sym = "`$relFull-symbols"
        foreach (`$need in @(`$unprot, `$sym, (Join-Path `$sym 'Mapping.txt'))) { if (-not (Test-Path -LiteralPath `$need)) { throw "Protected build did not produce `$need (contract 5.1)." } }
        `$bad = @(Get-ChildItem -LiteralPath `$relFull -Recurse -File -Force | Where-Object { `$_.Extension -in '.pdb','.map' -or `$_.Name -in 'Mapping.txt','obfuscation-report.json' })
        if (`$bad.Count) { throw ('Symbols or obfuscation map inside the publish folder: ' + ((`$bad | Select-Object -First 5 | ForEach-Object { `$_.FullName }) -join '; ')) }
        `$db = "EtpAccept_`$(`$sha)_protect"
        "SMOKE protected vs unprotected on `$db `$(Get-Date -Format s)" | Tee-Object -FilePath `$log -Append
        & .\scripts\release\Test-EtpProtectedBuild.ps1 -Protected `$relFull -Unprotected `$unprot -FixtureFolder '$FixtureFolder' -Database `$db *>&1 | Tee-Object -FilePath `$log -Append
        if (`$LASTEXITCODE) { throw "Protected-build smoke failed (exit `$LASTEXITCODE): the protected build differs from the unprotected one. NOT publishable." }
        `$arch = Join-Path '$SymbolsRoot' "$Version-`$sha"
        New-Item -ItemType Directory -Force `$arch | Out-Null
        Copy-Item -Path (Join-Path `$sym '*') -Destination `$arch -Recurse -Force
        if (-not (Test-Path -LiteralPath (Join-Path `$arch 'Mapping.txt'))) { throw "Symbol archive incomplete: no Mapping.txt in `$arch" }
        "SYMBOLS archived to `$arch (private; never copy to Installers, USB-KIT or the update feed)" | Tee-Object -FilePath `$log -Append
    }
} catch { ('FAILED: ' + `$_.Exception.Message) | Tee-Object -FilePath `$log -Append; exit 1 }
"@
& $PowerShellExe -NoProfile -File (Join-Path $ToolsDirectory 'Invoke-Serialized.ps1') -WorkingDirectory $Worktree -Label "installer-$Version" -WaitMinutes $WaitMinutes -Command $cmd
"Exit code: $LASTEXITCODE" | Tee-Object -FilePath $exitFile
exit $LASTEXITCODE
