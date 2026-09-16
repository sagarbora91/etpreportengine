param(
    [string]$Configuration = "Release",
    [switch]$SkipReleaseBuild,
    [string]$ReleaseDirectory = "artifacts/windows-release",
    [string]$OutputDirectory = "artifacts/installer",
    [string]$CertificateThumbprint,
    [uri]$TimestampServer
)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$release = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($ReleaseDirectory)) { $ReleaseDirectory } else { Join-Path $repoRoot $ReleaseDirectory }))
$output = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }))
if (Test-Path -LiteralPath $output) { throw "Installer output already exists. Choose a new OutputDirectory to preserve previous candidates: $output" }
if (-not $CertificateThumbprint -or -not $TimestampServer) { throw 'A signing certificate and timestamp service are required to build a release.' }
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
foreach ($payload in $payloads) {
    $signature = Get-AuthenticodeSignature -LiteralPath $payload
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ine $CertificateThumbprint) {
        throw 'Every executable and PowerShell payload must have the selected valid release signature before packaging.'
    }
}
& $compiler "/DAppVersion=$version" "/DReleaseDirectory=$release" "/DInstallerOutputDirectory=$output" (Join-Path $repoRoot "installer\EtpReportingEngine.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
$installer = Get-Item -LiteralPath (Join-Path $output "EtpReportingEngine-Setup-$version-x64.exe")
& (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') -Paths @($installer.FullName) -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer
Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($installer.Name)" } | Set-Content (Join-Path $installer.DirectoryName "SHA256SUMS.txt") -Encoding ascii
Write-Host "Windows installer created at $($installer.FullName)"
