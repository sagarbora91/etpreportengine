param(
    [string]$Configuration = "Release",
    [switch]$SkipReleaseBuild,
    [string]$ReleaseDirectory = "artifacts/windows-release",
    [string]$OutputDirectory = "artifacts/installer",
    [string]$CertificateThumbprint,
    [uri]$TimestampServer,
    # Code protection S1 (decision D5). The ETP code-signing PFX on a USB stick, instead of -CertificateThumbprint. The
    # passphrase is asked once at a hidden prompt. Signs the exe, the Etp.*.dll files, every .ps1/.psm1 and the installer.
    [string]$PfxPath,
    [switch]$AllowFixedDrive,
    # Code protection O1/G1. Passed to build-windows-release.ps1 (obfuscation). Not passed unless set.
    [switch]$Protect,
    # P4-11. Supply the Microsoft SQL Server Express media to build an installer that
    # can install it. Omit it and setup shows no database option at all, rather than
    # offering one it cannot honour.
    [string]$SqlPayloadDirectory,
    # The Inno Setup 6 compiler: ISCC.exe itself or the folder that holds it. Omit it and the
    # build looks on PATH, then in Inno Setup's own uninstall registration (which records a
    # custom install folder), then in Inno Setup's default install folders. Nothing
    # machine-specific is written here; a PC with Inno Setup somewhere unusual passes it.
    [string]$InnoSetupCompiler
)
$ErrorActionPreference = "Stop"

# Returns the full path of the ISCC.exe to use, or throws naming every place it looked.
function Resolve-EtpInnoSetupCompiler {
    param([string]$Requested)
    if ($Requested) {
        $candidate = [IO.Path]::GetFullPath($Requested)
        if (Test-Path -LiteralPath $candidate -PathType Container) { $candidate = Join-Path $candidate 'ISCC.exe' }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "The Inno Setup compiler was not found at: $candidate" }
        return $candidate
    }
    $searched = New-Object System.Collections.Generic.List[string]
    $searched.Add('PATH (ISCC.exe)')
    $onPath = Get-Command -Name 'ISCC.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($onPath) { return $onPath.Source }
    $folders = New-Object System.Collections.Generic.List[string]
    foreach ($key in @(
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1')) {
        $location = (Get-ItemProperty -LiteralPath $key -Name 'InstallLocation' -ErrorAction SilentlyContinue).InstallLocation
        if ($location) { $folders.Add($location) }
    }
    # Inno Setup's default folders: per-user install, then the two machine-wide ones.
    if ($env:LOCALAPPDATA) { $folders.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6')) }
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) { if ($base) { $folders.Add((Join-Path $base 'Inno Setup 6')) } }
    foreach ($folder in $folders) {
        $candidate = Join-Path $folder 'ISCC.exe'
        $searched.Add($candidate)
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw ("Inno Setup 6 is required and was not found. Pass -InnoSetupCompiler with the path of ISCC.exe or its folder, put that folder on PATH, or install it with: winget install JRSoftware.InnoSetup. Searched: " + ($searched -join '; '))
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$release = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($ReleaseDirectory)) { $ReleaseDirectory } else { Join-Path $repoRoot $ReleaseDirectory }))
$output = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }))
if (Test-Path -LiteralPath $output) { throw "Installer output already exists. Choose a new OutputDirectory to preserve previous candidates: $output" }
# A4.3 accepted unsigned: signing is applied when a certificate is supplied and
# skipped when it is not. An unsigned build is a deliberate, recorded choice, so the
# release still builds; what it loses is the guarantee of who produced it.
# Partial credentials are a mistake, not a choice: supplying one and not the other
# used to throw, and must not now degrade silently into an unsigned release.
if ($PfxPath -and $CertificateThumbprint) { throw 'Use -PfxPath or -CertificateThumbprint, not both.' }
if ([bool]($CertificateThumbprint -or $PfxPath) -ne [bool]$TimestampServer) { throw 'Supply both a signing certificate and a timestamp service, or neither.' }
$signWithPfx = [bool]$PfxPath
$signRelease = [bool]($CertificateThumbprint -or $PfxPath) -and [bool]$TimestampServer
$pfxPassphrase = $null
if ($signWithPfx) {
    # Fail early (wrong drive, missing file) before the long build; ask the passphrase once.
    if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) { throw "The signing certificate file was not found: $PfxPath" }
    $pfxPassphrase = Read-Host -AsSecureString 'Code-signing key passphrase'
}
# Check the SQL media before anything is built, so media that cannot work stops here and
# not after a full release build. Setup installs, from exactly these files, the engine,
# ODBC Driver 17, then Sqlcmd; see Get-EtpSqlClientInstallPlan in bootstrap.
# $sqlMedia, never $payload: the signing loop below walks $payloads with its own loop
# variable, and PowerShell's foreach leaves that variable set to the last item afterwards.
# Sharing the name once handed ISCC the last signed .ps1 as the media folder, so every
# signed build failed.
$sqlMedia = $null
if ($SqlPayloadDirectory) {
    $sqlMedia = [IO.Path]::GetFullPath($SqlPayloadDirectory)
    if (-not (Test-Path -LiteralPath $sqlMedia -PathType Container)) { throw "The SQL media directory does not exist: $sqlMedia" }
    if (-not (Get-ChildItem -LiteralPath $sqlMedia -Filter 'SQLEXPR*_x64_*.exe' -File)) { throw "No SQL Server Express package was found in: $sqlMedia" }
    if (-not (Test-Path -LiteralPath (Join-Path $sqlMedia 'MsSqlCmdLnUtils.msi') -PathType Leaf)) { throw "No Sqlcmd package (MsSqlCmdLnUtils.msi) was found in: $sqlMedia" }
    if (-not (Test-Path -LiteralPath (Join-Path $sqlMedia 'msodbcsql17.msi') -PathType Leaf)) { throw "No ODBC Driver 17 package (msodbcsql17.msi) was found in: $sqlMedia. The bundled Sqlcmd (Command Line Utilities 15) cannot install without it; copy MSODBCSQL.MSI from the SQL Server 2022 media and name it msodbcsql17.msi." }
}
# Find the compiler before the release build too: a missing compiler used to surface only
# after the whole build and test gate had run.
$compiler = Resolve-EtpInnoSetupCompiler -Requested $InnoSetupCompiler
Write-Host "Using Inno Setup compiler: $compiler"
if (-not $SkipReleaseBuild) {
    # With -PfxPath the release build is left unsigned on purpose: this script signs everything itself afterwards, in one
    # pass, from the USB key (so the PFX path never reaches the release build).
    $releaseArguments = @{ Configuration = $Configuration; OutputDirectory = $ReleaseDirectory }
    if ($CertificateThumbprint) { $releaseArguments.CertificateThumbprint = $CertificateThumbprint; $releaseArguments.TimestampServer = $TimestampServer }
    if ($Protect) { $releaseArguments.Protect = $true }
    & (Join-Path $PSScriptRoot "build-windows-release.ps1") @releaseArguments
}
# Code protection 4.2: debug symbols never ship. Symbols belong in <ReleaseDirectory>-symbols, outside the folder that is packed.
$symbolFiles = @(Get-ChildItem -LiteralPath $release -Recurse -File -Include '*.pdb','Mapping.txt','obfuscation-report.json','obfuscar.filled.xml' -ErrorAction SilentlyContinue)
if ($symbolFiles.Count -gt 0) {
    throw ("The release folder contains debug symbols or obfuscation maps and cannot be packed: " + (($symbolFiles | Select-Object -First 5 | ForEach-Object { $_.FullName.Substring($release.Length).TrimStart('\') }) -join ', ') + $(if ($symbolFiles.Count -gt 5) { " and $($symbolFiles.Count - 5) more" }) + ". Move them to the symbols folder first.")
}
# O1: the unprotected comparison publish (<release>-unprotected) carries this marker and must never be packed.
if (Test-Path -LiteralPath (Join-Path $release 'NOT-FOR-RELEASE.txt')) { throw "The release folder is the unprotected comparison build (NOT-FOR-RELEASE.txt) and cannot be packed: $release" }
# A -Protect build must really be the protected one: its receipt says so (no unprotected build labelled protected).
if ($Protect) {
    $protectReceipt = Get-Content -Raw -LiteralPath (Join-Path $release 'release.json') | ConvertFrom-Json
    if ($null -eq $protectReceipt.protection -or $protectReceipt.protection.obfuscated -ne $true) { throw "-Protect was given but $release\release.json does not record an obfuscated build." }
}
[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
$version = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
$executable = Join-Path $release "Etp.Reporting.Desktop.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Release executable is missing: $executable" }
$embedded = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
if (($embedded.ProductVersion -split '\+')[0] -ne $version) { throw "Release executable version does not match installer version $version." }
$receipt = Get-Content -Raw -LiteralPath (Join-Path $release "release.json") | ConvertFrom-Json
if ($receipt.version -ne $version) { throw "Release receipt version does not match installer version $version." }
$payloads = @($executable) + @(Get-ChildItem -LiteralPath (Join-Path $release 'scripts') -Filter '*.ps1' -File -Recurse | ForEach-Object FullName)
if ($signWithPfx) {
    # Everything ETP ships that Windows can check: the exe, the ETP assemblies (Microsoft's are already signed), all scripts.
    $pfxPayloads = @($executable) +
        @(Get-ChildItem -LiteralPath $release -Filter 'Etp.*.dll' -File -Recurse | ForEach-Object FullName) +
        @(Get-ChildItem -LiteralPath $release -Include '*.ps1','*.psm1' -File -Recurse | ForEach-Object FullName)
    $pfxPayloads = @($pfxPayloads | Select-Object -Unique)
    $signArguments = @{ Paths = $pfxPayloads; PfxPath = $PfxPath; Passphrase = $pfxPassphrase; TimestampServer = $TimestampServer }
    if ($AllowFixedDrive) { $signArguments.AllowFixedDrive = $true }
    & (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') @signArguments
    # The exe and the ETP assemblies changed when they were signed: the release folder's checksum file must describe the signed
    # files, in the same shape build-windows-release.ps1 writes (exe, plus the Etp.Reporting.*.dll files of a folder publish).
    $sumFiles = @($executable) + @(Get-ChildItem -LiteralPath $release -Filter 'Etp.Reporting.*.dll' -File | Sort-Object Name | ForEach-Object FullName)
    @($sumFiles | ForEach-Object { $h = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($h.Hash)  $(Split-Path -Leaf $h.Path)" }) |
        Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
    # Refuse an unsigned payload: every file must now carry a signature from one single signer, with a timestamp.
    $signers = @()
    foreach ($payload in $pfxPayloads) {
        $signature = Get-AuthenticodeSignature -LiteralPath $payload
        if ("$($signature.Status)" -notin @('Valid', 'UnknownError', 'NotTrusted') -or $null -eq $signature.SignerCertificate -or $null -eq $signature.TimeStamperCertificate) {
            throw "Every shipped executable, assembly and script must be signed before packaging; not signed: $([IO.Path]::GetFileName($payload))."
        }
        $signers += $signature.SignerCertificate.Thumbprint
    }
    if (@($signers | Select-Object -Unique).Count -ne 1) { throw 'The payload is signed by more than one certificate.' }
}
elseif ($signRelease) {
    foreach ($payload in $payloads) {
        $signature = Get-AuthenticodeSignature -LiteralPath $payload
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ine $CertificateThumbprint) {
            throw 'Every executable and PowerShell payload must have the selected valid release signature before packaging.'
        }
    }
}
else { Write-Warning 'Building an UNSIGNED installer. Windows will warn on install and the payload cannot be traced to its publisher.' }
$compilerArguments = @("/DAppVersion=$version", "/DReleaseDirectory=$release", "/DInstallerOutputDirectory=$output")
if ($sqlMedia) {
    $compilerArguments += "/DSqlPayloadDirectory=$sqlMedia"
    Write-Host "Embedding SQL Server media from $sqlMedia"
}
else { Write-Warning 'Building without SQL media: setup will not offer to install the database engine.' }
& $compiler @compilerArguments (Join-Path $repoRoot "installer\EtpReportingEngine.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
$installer = Get-Item -LiteralPath (Join-Path $output "EtpReportingEngine-Setup-$version-x64.exe")
if ($signWithPfx) {
    $installerSignArguments = @{ Paths = @($installer.FullName); PfxPath = $PfxPath; Passphrase = $pfxPassphrase; TimestampServer = $TimestampServer }
    if ($AllowFixedDrive) { $installerSignArguments.AllowFixedDrive = $true }
    & (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') @installerSignArguments
}
elseif ($signRelease) { & (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') -Paths @($installer.FullName) -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer }
Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($installer.Name)" } | Set-Content (Join-Path $installer.DirectoryName "SHA256SUMS.txt") -Encoding ascii
Write-Host "Windows installer created at $($installer.FullName)"
