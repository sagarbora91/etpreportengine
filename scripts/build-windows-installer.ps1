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
    [string]$SqlPayloadDirectory
)
$ErrorActionPreference = "Stop"
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
if (-not $SkipReleaseBuild) { & (Join-Path $PSScriptRoot "build-windows-release.ps1") -Configuration $Configuration -OutputDirectory $ReleaseDirectory -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer }
$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw "Inno Setup 6 is required. Install it with: winget install JRSoftware.InnoSetup" }
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
if ($SqlPayloadDirectory) {
    $payload = [IO.Path]::GetFullPath($SqlPayloadDirectory)
    if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw "The SQL media directory does not exist: $payload" }
    if (-not (Get-ChildItem -LiteralPath $payload -Filter 'SQLEXPR*_x64_*.exe' -File)) { throw "No SQL Server Express package was found in: $payload" }
    $compilerArguments += "/DSqlPayloadDirectory=$payload"
    Write-Host "Embedding SQL Server media from $payload"
}
else { Write-Warning 'Building without SQL media: setup will not offer to install the database engine.' }
& $compiler @compilerArguments (Join-Path $repoRoot "installer\EtpReportingEngine.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
$installer = Get-Item -LiteralPath (Join-Path $output "EtpReportingEngine-Setup-$version-x64.exe")
if ($signRelease) { & (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') -Paths @($installer.FullName) -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer }
Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($installer.Name)" } | Set-Content (Join-Path $installer.DirectoryName "SHA256SUMS.txt") -Encoding ascii
Write-Host "Windows installer created at $($installer.FullName)"
