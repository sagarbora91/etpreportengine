# 1.13.0 COPY (code-protection lane G1, 15 Oct 2026; the original Publish-Release.ps1 is untouched). Differences: refuses if any .pdb/.map/Mapping.txt/obfuscation-report
# is in the publish folder or the installer folder; requires the protected-build evidence (symbols folder with Mapping.txt) unless -AllowUnprotected;
# records obfuscation + Authenticode signing in the notes of release.json. Not run yet, parse-checked only.
# R1-01/R1-05 (16 Oct review fixes): a build whose release.json claims protection is published ONLY with
# artifacts\publish-evidence-<tag>.json (written by Build-Installer-1.13.0 after the smoke passed and the symbols were archived), and
# only when its installer SHA-256, commit (= worktree HEAD), signer and map SHA-256 (archived copy re-hashed) match what is published;
# there is no override. An unsigned or untimestamped installer is refused unless -AllowUnprotected is passed for a build that does
# NOT claim protection. Folders marked NOT-PUBLISHABLE.txt (renamed FAILED-* by the build) are refused.
# Parameterised publish (replaces Publish-Installer-<ver>.ps1; Arch 14, 12 Oct 2026). Run after Build-Installer.ps1 exited 0.
#   & 'E:\ETP\Migration 2026-10-04\Publish-Release.ps1' -Version 1.11.2 -Worktree 'E:\ETP\Code\Worktrees\release-1.11.2'
# Copies the newest artifacts\installer-* folder to <InstallersRoot>\ETP Reporting Engine <Version>\, adds the build log, then writes
#   release.json   (version, installer file name, size, SHA-256, commit, built UTC, hasMigration, schemaCompatLevel = latest migration number,
#                   sqlPayloadIncluded) and SHA256SUMS.txt (every file in the folder except SHA256SUMS.txt itself).
# The first lines of release.json are the contract the 1.12 in-app update will verify; nothing here signs or uploads anything.
#
# 1.12 lane B1 (plan section B, decision 44 #1). With -SigningKeyPath (the key file on one of Sagar's USB sticks) the script
# instead writes release.json schema 2 (adds schema, keyId, hasSchemaChange, minimumFromVersion, notes), asks for the key's
# passphrase, writes the detached release.json.sig (scripts\release\EtpReleaseSigning.psm1 in -Worktree), and then copies the
# installer, release.json.sig and release.json (last) into <FeedRoot>\<Version>\ (default <OneDrive>\ETP-Updates; -SkipFeed
# to sign without copying). Only Sagar runs this. Without -SigningKeyPath the behaviour below is unchanged (schema 1, unsigned).
#   & 'E:\ETP\Migration 2026-10-04\Publish-Release.ps1' -Version 1.12.0 -Worktree '<worktree>' -PreviousVersion 1.11.3 `
#       -SigningKeyPath 'F:\ETP-Key\etp-release-signing-key.json' -MinimumFromVersion 1.11.0 -NotesPath '<notes.txt>'
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Worktree,
    [string]$InstallersRoot = 'E:\ETP\Installers',
    [string]$PreviousVersion,
    [switch]$SqlPayloadIncluded,
    [string]$SigningKeyPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$MinimumFromVersion,
    [string]$Notes = '',
    [string]$NotesPath,
    [string]$FeedRoot,
    [switch]$SkipFeed,
    # 1.12 B2: hasSchemaChange comes from the release candidate's own --plan-database-changes, read against a database that is
    # still on the PREVIOUS release (the live Workpc database before the update). Defaults: the candidate in
    # Worktree artifacts windows-release folder and the local SQLEXPRESS ETP database.
    [string]$CandidateExe,
    [string]$PlanConnectionString = 'Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;Encrypt=Optional;Connect Timeout=5',
    [switch]$SkipSchemaPlan,
    [switch]$AllowUnprotected
)
if ($SigningKeyPath) {
    $ErrorActionPreference = 'Stop'
    if (-not $MinimumFromVersion) { throw '-MinimumFromVersion is required with -SigningKeyPath (the oldest version that may update straight to this one).' }
    if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) { throw "No key file at $SigningKeyPath. Is the USB stick in?" }
    if ($NotesPath) { $Notes = [IO.File]::ReadAllText($NotesPath) }
    Import-Module (Join-Path $Worktree 'scripts\release\EtpReleaseSigning.psm1') -Force
    $signingKey = Read-EtpReleaseKeyFilePublic -Path $SigningKeyPath
    if (-not $SkipFeed -and -not $FeedRoot) {
        $oneDrive = @($env:OneDrive, $env:OneDriveCommercial, $env:OneDriveConsumer) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
        if (-not $oneDrive) { throw 'OneDrive is not set up for this user. Pass -FeedRoot, or -SkipFeed to sign only.' }
        $FeedRoot = Join-Path $oneDrive 'ETP-Updates'
    }
}
$ErrorActionPreference = 'Stop'
$artifacts = Join-Path $Worktree 'artifacts'
# Build-Installer-1.13.0 renames a failed build's folders to FAILED-<time>-<name>, so they never match 'installer-*' (R1-01).
$src = Get-ChildItem $artifacts -Directory -Filter 'installer-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $src) { throw 'No artifacts\installer-* folder found.' }
$setup = Get-ChildItem $src.FullName -Filter "EtpReportingEngine-Setup-$Version-x64.exe" -File
if (-not $setup) { throw "The newest installer folder ($($src.Name)) has no EtpReportingEngine-Setup-$Version-x64.exe." }
# --- 1.13.0 protection checks (G1; R1-01 / R1-05 review fixes 16 Oct) ---
$tag = $src.Name -replace '^installer-', ''
$relName = "windows-release-$tag"
$relDir = Join-Path $artifacts $relName
if (Test-Path -LiteralPath (Join-Path $src.FullName 'NOT-PUBLISHABLE.txt')) { throw "Refusing to publish: $($src.FullName) is marked NOT-PUBLISHABLE (its build failed after the installer step: $((Get-Content -Raw -LiteralPath (Join-Path $src.FullName 'NOT-PUBLISHABLE.txt')).Trim()))." }
if (-not (Test-Path -LiteralPath $relDir -PathType Container)) { throw "No publish folder $relDir for installer folder $($src.Name); cannot prove the release carries no symbols." }
if (Test-Path -LiteralPath (Join-Path $relDir 'NOT-PUBLISHABLE.txt')) { throw "Refusing to publish: $relDir is marked NOT-PUBLISHABLE." }
$forbidden = @(Get-ChildItem -LiteralPath $relDir, $src.FullName -Recurse -File -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.pdb', '.map' -or $_.Name -in 'Mapping.txt', 'obfuscation-report.json' })
if ($forbidden.Count) { throw ("Refusing to publish: symbol or obfuscation-map files are inside the release/installer folder: " + (($forbidden | Select-Object -First 10 | ForEach-Object { $_.FullName }) -join "; ")) }
# Integration 15 Oct: the publish folder's own release.json (written by build-windows-release.ps1 -Protect) must say obfuscated, and the
# unprotected comparison folder's marker must never be in the publish folder.
$relReceipt = $null
try { $relReceipt = Get-Content -LiteralPath (Join-Path $relDir 'release.json') -Raw | ConvertFrom-Json } catch { $relReceipt = $null }
if (Test-Path -LiteralPath (Join-Path $relDir 'NOT-FOR-RELEASE.txt')) { throw "Refusing to publish: $relDir is the unprotected comparison build (NOT-FOR-RELEASE.txt)." }
$claimsProtected = $null -ne $relReceipt -and $null -ne $relReceipt.protection -and $relReceipt.protection.obfuscated -eq $true
$headCommit = (git -C $Worktree rev-parse HEAD).Trim()
$installerHashNow = (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash
$sig = Get-AuthenticodeSignature -LiteralPath $setup.FullName
$signed = $null -ne $sig.SignerCertificate
$timestamped = $null -ne $sig.TimeStamperCertificate
if ($signed -and $sig.Status -notin @([System.Management.Automation.SignatureStatus]::Valid, [System.Management.Automation.SignatureStatus]::UnknownError, [System.Management.Automation.SignatureStatus]::NotTrusted)) {
    throw "Refusing to publish: the installer's signature status is $($sig.Status) (changed after signing?)."
}
# R1-05: an unsigned or untimestamped installer is published only on purpose (-AllowUnprotected), and never as a protected build.
if ((-not $signed -or -not $timestamped -or [string]::IsNullOrWhiteSpace([string]$sig.SignerCertificate.Thumbprint)) -and ($claimsProtected -or -not $AllowUnprotected)) {
    throw "Refusing to publish: the installer is $(if (-not $signed) { 'NOT SIGNED' } else { 'signed WITHOUT a timestamp' }). A 1.13.0 release is signed and timestamped; pass -AllowUnprotected only for a deliberate unprotected test publish of an unprotected build."
}
# R1-01: a protected build is published only with the evidence Build-Installer-1.13.0 writes after the smoke passed, bound to the exact
# installer bytes, the commit, the signer and the archived map. No override: an unprotected test publish (-AllowUnprotected) is only
# possible for a build whose release.json does NOT claim protection.
$obfuscated = $false
$evidence = $null
if ($claimsProtected -or -not $AllowUnprotected) {
    $evidencePath = Join-Path $artifacts "publish-evidence-$tag.json"
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { throw "Refusing to publish: no smoke evidence $evidencePath. Build-Installer-1.13.0 writes it only after the protected-vs-unprotected smoke passed and the symbols were archived." }
    $evidence = Get-Content -Raw -LiteralPath $evidencePath | ConvertFrom-Json
    $problems = @()
    if (-not $claimsProtected) { $problems += "$relDir\release.json does not record protection.obfuscated=true" }
    if ([string]$evidence.result -ne 'SMOKE-PASSED' -or [string]$evidence.smoke -ne 'IDENTICAL') { $problems += "evidence result is '$($evidence.result)/$($evidence.smoke)', not SMOKE-PASSED/IDENTICAL" }
    if ([string]$evidence.tag -ne $tag) { $problems += "evidence is for build $($evidence.tag), not $tag" }
    if ([string]$evidence.installer -ne $setup.Name) { $problems += "evidence names installer $($evidence.installer), not $($setup.Name)" }
    if ([string]$evidence.installerSha256 -ine $installerHashNow) { $problems += "installer SHA-256 is $installerHashNow but the smoke evidence recorded $($evidence.installerSha256) (changed or rebuilt after the smoke)" }
    if ([string]$evidence.commit -ine $headCommit) { $problems += "evidence commit $($evidence.commit) is not $Worktree HEAD $headCommit" }
    if ($relReceipt -and [string]$relReceipt.commit -ine [string]$evidence.commit) { $problems += "release.json commit $($relReceipt.commit) is not the evidence commit $($evidence.commit)" }
    if ($signed -and [string]$evidence.signerThumbprint -ine [string]$sig.SignerCertificate.Thumbprint) { $problems += "installer signer $($sig.SignerCertificate.Thumbprint) is not the evidence signer $($evidence.signerThumbprint)" }
    $mapWanted = if ($relReceipt -and $relReceipt.protection) { [string]$relReceipt.protection.mappingSha256 } else { '' }
    if (-not $mapWanted -or [string]$evidence.mappingSha256 -ine $mapWanted) { $problems += "evidence map SHA-256 $($evidence.mappingSha256) is not release.json's $mapWanted" }
    $archivedMap = Join-Path ([string]$evidence.symbolsArchive) 'Mapping.txt'
    if (-not $evidence.symbolsArchive -or -not (Test-Path -LiteralPath $archivedMap -PathType Leaf)) { $problems += "no archived map at $archivedMap" }
    elseif ((Get-FileHash -LiteralPath $archivedMap -Algorithm SHA256).Hash -ine $mapWanted) { $problems += "the archived map $archivedMap is not this build's (SHA-256 differs)" }
    if ($problems.Count) { throw ("Refusing to publish: the smoke evidence does not match what would be published: " + ($problems -join '; ') + '.') }
    $obfuscated = $true
    Write-Host "Smoke evidence: $evidencePath (IDENTICAL, report $($evidence.smokeReport)); installer SHA-256 and commit match; map archived at $($evidence.symbolsArchive)."
}
$protNote = 'Build protection: obfuscation=' + $(if ($obfuscated) { "Obfuscar, profile $($relReceipt.protection.profile), protected-build smoke IDENTICAL (Mapping.txt archived under Reference\Symbols, not shipped)" } else { 'NONE' }) +
    '; Authenticode=' + $(if ($signed) { "signed, signer thumbprint $($sig.SignerCertificate.Thumbprint), status $($sig.Status)$(if (-not $timestamped) { ', NO timestamp' })" } else { 'NOT SIGNED' }) +
    '; pdb-in-release=none.'
Write-Host $protNote
if ($Notes) { $Notes = $Notes.TrimEnd() + [Environment]::NewLine + $protNote } else { $Notes = $protNote }
# --- end 1.13.0 protection checks ---
$dst = Join-Path $InstallersRoot "ETP Reporting Engine $Version"
if (Test-Path $dst) {
    $old = Join-Path $dst ('superseded-' + (Get-Date -Format yyyyMMdd-HHmm))
    New-Item -ItemType Directory $old | Out-Null
    Get-ChildItem $dst -File | Move-Item -Destination $old
}
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item (Join-Path $src.FullName '*') $dst -Recurse
Get-ChildItem $PSScriptRoot -Filter "installer-build-$Version-*.log" | Sort-Object LastWriteTime | Select-Object -Last 1 | Copy-Item -Destination $dst
$commit = (git -C $Worktree rev-parse HEAD).Trim()
$migrations = @(Get-ChildItem (Join-Path $Worktree 'database\migrations') -Filter '[0-9][0-9][0-9][0-9]_*.sql' | ForEach-Object { [int]($_.Name.Substring(0,4)) } | Sort-Object)
$schemaLevel = if ($migrations.Count) { $migrations[-1] } else { 0 }
$hasMigration = $true
$planned = $false
if ($SigningKeyPath -and -not $SkipSchemaPlan) {
    # The plan is read-only (A3). A pending migration OR a pending database object means the update changes the database.
    # Integration (13 Oct): Build-Installer writes artifacts\windows-release-<sha>-<tag>, so the default is the newest such folder.
    # 16 Oct: the release folder this installer was made from (not the newest windows-release* folder, which could be another build).
    $candidate = if ($CandidateExe) { $CandidateExe } else { Join-Path $relDir 'Etp.Reporting.Desktop.exe' }
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        try {
            $psi = New-Object System.Diagnostics.ProcessStartInfo
            $psi.FileName = $candidate
            $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
            $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
            $psi.ArgumentList.Add('--plan-database-changes'); $psi.ArgumentList.Add('--connection-string'); $psi.ArgumentList.Add($PlanConnectionString)
            $proc = [System.Diagnostics.Process]::Start($psi)
            $out = $proc.StandardOutput.ReadToEndAsync(); $err = $proc.StandardError.ReadToEndAsync()
            if (-not $proc.WaitForExit(180000)) { try { $proc.Kill() } catch { }; throw 'the plan did not finish within 3 minutes' }
            if ($proc.ExitCode -ne 0) { throw "exit code $($proc.ExitCode)" }
            $line = @(([string]$out.Result) -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_.StartsWith('{') })[-1]
            $plan = $line | ConvertFrom-Json
            $hasMigration = (@($plan.pendingMigrations).Count -gt 0) -or (@($plan.pendingObjects).Count -gt 0)
            $planned = $true
            Write-Host "Database change plan from the release candidate: pending migrations $(@($plan.pendingMigrations).Count), pending objects $(@($plan.pendingObjects).Count) -> hasSchemaChange $hasMigration."
        } catch { Write-Warning "The release candidate's --plan-database-changes could not be read ($($_.Exception.Message)); falling back to comparing schemaCompatLevel with the previous release." }
    } else { Write-Warning "No release candidate at $candidate; falling back to comparing schemaCompatLevel with the previous release." }
}
if (-not $planned -and $PreviousVersion) {
    $prev = Join-Path $InstallersRoot "ETP Reporting Engine $PreviousVersion\release.json"
    if (Test-Path $prev) { $hasMigration = ([int](Get-Content -Raw $prev | ConvertFrom-Json).schemaCompatLevel) -ne $schemaLevel }
}
$installerHash = (Get-FileHash (Join-Path $dst $setup.Name) -Algorithm SHA256).Hash
if ($SigningKeyPath) {
    # Schema 2, signed. UTF-8 without a byte order mark; the signature covers these exact bytes.
    $manifestText = ConvertTo-EtpReleaseManifestText -Version $Version -InstallerBytes (Get-Item (Join-Path $dst $setup.Name)).Length -Sha256 $installerHash `
        -Commit $commit -SchemaCompatLevel $schemaLevel -HasSchemaChange $hasMigration -MinimumFromVersion $MinimumFromVersion -Notes $Notes `
        -SqlPayloadIncluded ([bool]$SqlPayloadIncluded) -KeyId $signingKey.KeyId
    [IO.File]::WriteAllText((Join-Path $dst 'release.json'), $manifestText, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Signing release.json with key $($signingKey.KeyId) (fingerprint $($signingKey.Fingerprint))."
    $passphrase = Read-Host -AsSecureString 'Release key passphrase'
    $null = New-EtpReleaseSignature -ManifestPath (Join-Path $dst 'release.json') -KeyPath $SigningKeyPath -Passphrase $passphrase
} else {
[ordered]@{
    product            = 'ETP Reporting Engine'
    version            = $Version
    installer          = $setup.Name
    installerBytes     = (Get-Item (Join-Path $dst $setup.Name)).Length
    sha256             = $installerHash
    commit             = $commit
    publishedUtc       = [DateTime]::UtcNow.ToString('o')
    schemaCompatLevel  = $schemaLevel
    hasMigration       = $hasMigration
    sqlPayloadIncluded = [bool]$SqlPayloadIncluded
    signature          = $null
    notes              = $Notes
} | ConvertTo-Json | Set-Content (Join-Path $dst 'release.json') -Encoding utf8
}
Get-ChildItem $dst -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name } | Set-Content "$dst\SHA256SUMS.txt" -Encoding ascii
Get-Content "$dst\SHA256SUMS.txt"
Write-Host "Published to $dst (schemaCompatLevel $schemaLevel, hasMigration $hasMigration)."
if ($SigningKeyPath -and -not $SkipFeed) {
    $feedFolder = Copy-EtpReleaseToFeed -SourceFolder $dst -Version $Version -FeedRoot $FeedRoot -PublicKey $signingKey.PublicKey
    Write-Host "Signed release copied to the update feed: $feedFolder (installer, then release.json.sig, then release.json)."
}
