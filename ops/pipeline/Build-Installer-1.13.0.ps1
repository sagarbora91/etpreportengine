# 1.13.0 COPY of Build-Installer.ps1 (code-protection lane G1, 15 Oct 2026; the original is untouched). Not run yet, parse-checked only.
# Adds, against the contracts in CODE-PROTECTION-PLAN.md 5.1:
#   -Protect    passes -Protect to the release/installer build (obfuscation of the ETP assemblies, folder publish). The contract: the
#               protected publish is <ReleaseDirectory>, the unprotected one <ReleaseDirectory>-unprotected, and symbols + Mapping.txt +
#               obfuscation-report.json are in <ReleaseDirectory>-symbols.
#   -PfxPath    Authenticode PFX on a USB stick (the passphrase is prompted by sign-etp-artifacts.ps1; nothing is stored here). Required with -Protect.
#   Then, still inside the same serialised job: a check that no .pdb/.map/Mapping.txt is in the publish folder, a check that the setup exe
#   carries one signer and a timestamp, the protected-vs-unprotected smoke (scripts\release\Test-EtpProtectedBuild.ps1; exit 0 only on
#   identical AND complete results) and the symbol archive to E:\ETP\Reference\Symbols\<version>-<sha7>\ (never beside the installer).
# ORDER (what really happens): build-windows-installer.ps1 builds the release, signs it, runs Inno and signs the installer in one go; the
# smoke runs AFTER that, on the release folder the installer was made from. So a signed installer exists before the smoke has passed.
# R1-01 (16 Oct review fix) closes that gap:
#   - on ANY failure after build-windows-installer.ps1 started (its own failure, symbols in the folder, unsigned/untimestamped setup, smoke
#     exit != 0, archive failure) the installer, release, -unprotected and -symbols folders get NOT-PUBLISHABLE.txt and are renamed
#     FAILED-<time>-<name>, so Publish-Release-1.13.0.ps1 (newest artifacts\installer-* folder) never sees them;
#   - only when everything passed is artifacts\publish-evidence-<tag>.json written: commit, installer SHA-256, signer, mapping SHA-256,
#     symbols archive, smoke result and report. Publish-Release-1.13.0.ps1 refuses a protected build without it or when any value
#     differs from what it is about to publish (no override).
#   - R1-14: after a passed smoke the -unprotected folder (a runnable build WITH PDBs) is deleted; after the archive's read-back the
#     -symbols folder is deleted (the archive under Reference\Symbols is the only copy, as RELEASE-PROTECTION.md step 8 says).
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
    # Integration 15 Oct: build-windows-installer.ps1 refuses -PfxPath without a timestamp service (RFC 3161; needs internet during the build).
    [uri]$TimestampServer = 'http://timestamp.digicert.com',
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
if ($Protect) { $protectArg = "-Protect -PfxPath '$PfxPath' -TimestampServer '$($TimestampServer.AbsoluteUri)'" }
$protectOn = if ($Protect) { '$true' } else { '$false' }
$log = Join-Path $PSScriptRoot "installer-build-$Version"
$cmd = @"
`$sha = (git rev-parse --short=7 HEAD).Trim()
`$commit = (git rev-parse HEAD).Trim()
`$tag = "`$sha-b1"
`$log = "$log-`$tag.log"
`$rel = "artifacts\windows-release-`$tag"
`$inst = "artifacts\installer-`$tag"
`$evidence = "artifacts\publish-evidence-`$tag.json"
`$started = `$false
`$ok = `$false
`$rc = 1
function Lock-NotPublishable([string]`$Why) {
    # R1-01: every folder of this build is marked and moved out of Publish-Release's 'installer-*' / 'windows-release*' patterns.
    `$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Remove-Item -LiteralPath `$evidence -Force -ErrorAction SilentlyContinue
    foreach (`$f in @(`$inst, `$rel, "`$rel-unprotected", "`$rel-symbols")) {
        if (-not (Test-Path -LiteralPath `$f -PathType Container)) { continue }
        try { [IO.File]::WriteAllText((Join-Path `$f 'NOT-PUBLISHABLE.txt'), "Build `$tag failed after the installer step started: `$Why (`$stamp). Never publish or install this.``r``n") } catch { }
        try { Rename-Item -LiteralPath `$f -NewName ("FAILED-`$stamp-" + (Split-Path -Leaf `$f)) -ErrorAction Stop; ("QUARANTINED `$f -> FAILED-`$stamp-" + (Split-Path -Leaf `$f)) | Tee-Object -FilePath `$log -Append }
        catch {
            ("COULD NOT RENAME `$f (`$(`$_.Exception.Message)); NOT-PUBLISHABLE.txt marks it and Publish-Release-1.13.0 refuses it.") | Tee-Object -FilePath `$log -Append
            if (`$f -eq `$inst) { Get-ChildItem -LiteralPath `$f -Filter '*.exe' -File -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue }
        }
    }
}
try {
    # A folder left by an earlier build of this commit is NOT this build's: refuse before anything is marked, so an earlier passed build
    # (and its evidence) is never quarantined by a repeat run. Publish it, or move it away by hand first.
    foreach (`$f in @(`$inst, `$rel, "`$rel-unprotected", "`$rel-symbols")) { if (Test-Path -LiteralPath `$f) { throw "`$f already exists from an earlier build of `$tag; publish that one or move it away first." } }
    Remove-Item -LiteralPath `$evidence -Force -ErrorAction SilentlyContinue   # stale evidence of a build whose folders are gone
    `$started = `$true
    & .\scripts\build-windows-installer.ps1 ``
        -ReleaseDirectory `$rel ``
        -OutputDirectory  `$inst ``
        $payloadArg ``
        $protectArg ``
        -InnoSetupCompiler '$InnoSetupCompiler' *>&1 | Tee-Object -FilePath `$log
    if (`$LASTEXITCODE) { `$rc = `$LASTEXITCODE; throw "build-windows-installer.ps1 exited `$LASTEXITCODE" }
    if ($protectOn) {
        `$relFull = (Resolve-Path `$rel).Path
        `$unprot = "`$relFull-unprotected"
        `$sym = "`$relFull-symbols"
        foreach (`$need in @(`$unprot, `$sym, (Join-Path `$sym 'Mapping.txt'))) { if (-not (Test-Path -LiteralPath `$need)) { throw "Protected build did not produce `$need (contract 5.1)." } }
        `$bad = @(Get-ChildItem -LiteralPath `$relFull -Recurse -File -Force | Where-Object { `$_.Extension -in '.pdb','.map' -or `$_.Name -in 'Mapping.txt','obfuscation-report.json' })
        if (`$bad.Count) { throw ('Symbols or obfuscation map inside the publish folder: ' + ((`$bad | Select-Object -First 5 | ForEach-Object { `$_.FullName }) -join '; ')) }
        `$setup = @(Get-ChildItem -LiteralPath `$inst -Filter 'EtpReportingEngine-Setup-*-x64.exe' -File)
        if (`$setup.Count -ne 1) { throw "Expected one setup exe in `$inst, found `$(`$setup.Count)." }
        `$sig = Get-AuthenticodeSignature -LiteralPath `$setup[0].FullName
        if (`$null -eq `$sig.SignerCertificate -or `$null -eq `$sig.TimeStamperCertificate -or "`$(`$sig.Status)" -notin @('Valid','UnknownError','NotTrusted')) { throw "The setup exe is not signed with a timestamp (status `$(`$sig.Status)). NOT publishable." }
        `$db = "EtpAccept_`$(`$sha)_protect"
        `$smokeOut = Join-Path ([IO.Path]::GetTempPath()) ("EtpProtectedBuild-`$tag-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        "SMOKE protected vs unprotected on `$db `$(Get-Date -Format s), output `$smokeOut" | Tee-Object -FilePath `$log -Append
        & .\scripts\release\Test-EtpProtectedBuild.ps1 -Protected `$relFull -Unprotected `$unprot -FixtureFolder '$FixtureFolder' -Database `$db -OutputFolder `$smokeOut *>&1 | Tee-Object -FilePath `$log -Append
        `$smokeExit = `$LASTEXITCODE
        if (`$smokeExit) { throw "Protected-build smoke failed (exit `$smokeExit; 1 = different, 2 = refused, 3 = incomplete): NOT publishable." }
        `$report = Join-Path `$smokeOut 'protected-build-report.md'
        if (-not (Test-Path -LiteralPath `$report) -or (Get-Content -Raw -LiteralPath `$report) -notmatch 'Result: \*\*IDENTICAL\*\*') { throw "The smoke exited 0 but its report does not say IDENTICAL (`$report). NOT publishable." }
        Copy-Item -LiteralPath `$report -Destination "artifacts\smoke-report-`$tag.md" -Force
        # R1-14: the unprotected comparison build has done its job; it is a runnable build with PDBs, so it goes now.
        Remove-Item -LiteralPath `$unprot -Recurse -Force
        Remove-Item -LiteralPath `$smokeOut -Recurse -Force -ErrorAction SilentlyContinue
        # Integration 15 Oct: O1's archive script (SHA256SUMS + read-back, map checked against release.json, never overwrites,
        # refuses Installers/OneDrive/the release folder). It names the folder <version>-<sha7> from release.json.
        & .\scripts\release\Archive-EtpSymbols.ps1 -SymbolsDirectory `$sym -ReleaseDirectory `$relFull -ArchiveRoot '$SymbolsRoot' *>&1 | Tee-Object -FilePath `$log -Append
        `$receipt = Get-Content -Raw -LiteralPath (Join-Path `$relFull 'release.json') | ConvertFrom-Json
        `$archive = Join-Path '$SymbolsRoot' ("`$(`$receipt.version)-" + ([string]`$receipt.commit).Substring(0, 7))
        `$archivedMap = Join-Path `$archive 'Mapping.txt'
        if (-not (Test-Path -LiteralPath `$archivedMap) -or (Get-FileHash -LiteralPath `$archivedMap -Algorithm SHA256).Hash -ine [string]`$receipt.protection.mappingSha256) { throw "Symbol archive `$archive does not hold this build's map. NOT publishable." }
        if ([string]`$receipt.commit -ine `$commit) { throw "release.json commit `$(`$receipt.commit) is not HEAD `$commit." }
        # R1-14: the archive's read-back proved the copy; the working copy of the symbols goes.
        Remove-Item -LiteralPath `$sym -Recurse -Force
        [ordered]@{
            schema = 1; result = 'SMOKE-PASSED'; smoke = 'IDENTICAL'; tag = `$tag; version = [string]`$receipt.version; commit = `$commit
            installer = `$setup[0].Name; installerSha256 = (Get-FileHash -LiteralPath `$setup[0].FullName -Algorithm SHA256).Hash
            signerThumbprint = [string]`$sig.SignerCertificate.Thumbprint; timestamped = `$true
            mappingSha256 = [string]`$receipt.protection.mappingSha256; profile = [string]`$receipt.protection.profile
            symbolsArchive = `$archive; smokeReport = (Resolve-Path "artifacts\smoke-report-`$tag.md").Path; writtenUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json | Set-Content -LiteralPath `$evidence -Encoding utf8
        "EVIDENCE `$evidence (smoke IDENTICAL, installer `$(`$setup[0].Name))" | Tee-Object -FilePath `$log -Append
    }
    `$ok = `$true
    `$rc = 0
} catch { ('FAILED: ' + `$_.Exception.Message) | Tee-Object -FilePath `$log -Append; if (-not `$rc) { `$rc = 1 } }
if (-not `$ok) {
    if (`$started) { Lock-NotPublishable 'see the FAILED line in the build log' }
    exit `$rc
}
exit 0
"@
& $PowerShellExe -NoProfile -File (Join-Path $ToolsDirectory 'Invoke-Serialized.ps1') -WorkingDirectory $Worktree -Label "installer-$Version" -WaitMinutes $WaitMinutes -Command $cmd
"Exit code: $LASTEXITCODE" | Tee-Object -FilePath $exitFile
exit $LASTEXITCODE
