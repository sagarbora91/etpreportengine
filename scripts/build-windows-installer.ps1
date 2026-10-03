param(
    [string]$Configuration = "Release",
    [switch]$SkipReleaseBuild,
    [string]$ReleaseDirectory = "artifacts/windows-release",
    [string]$OutputDirectory = "artifacts/installer",
    [string]$CertificateThumbprint,
    [uri]$TimestampServer,
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
if ([bool]$CertificateThumbprint -ne [bool]$TimestampServer) { throw 'Supply both a signing certificate and a timestamp service, or neither.' }
$signRelease = [bool]$CertificateThumbprint -and [bool]$TimestampServer
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
if (-not $SkipReleaseBuild) { & (Join-Path $PSScriptRoot "build-windows-release.ps1") -Configuration $Configuration -OutputDirectory $ReleaseDirectory -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer }
[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
$version = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
$executable = Join-Path $release "Etp.Reporting.Desktop.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Release executable is missing: $executable" }
$embedded = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
if (($embedded.ProductVersion -split '\+')[0] -ne $version) { throw "Release executable version does not match installer version $version." }
$receipt = Get-Content -Raw -LiteralPath (Join-Path $release "release.json") | ConvertFrom-Json
if ($receipt.version -ne $version) { throw "Release receipt version does not match installer version $version." }
$payloads = @($executable) + @(Get-ChildItem -LiteralPath (Join-Path $release 'scripts') -Filter '*.ps1' -File -Recurse | ForEach-Object FullName)
if ($signRelease) {
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
if ($signRelease) { & (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') -Paths @($installer.FullName) -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer }
Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($installer.Name)" } | Set-Content (Join-Path $installer.DirectoryName "SHA256SUMS.txt") -Encoding ascii
Write-Host "Windows installer created at $($installer.FullName)"
