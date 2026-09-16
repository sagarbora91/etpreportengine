param([string]$Version,[string]$CertificateThumbprint,[uri]$TimestampServer)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
    $Version = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "Invalid semantic version: $Version" }
[xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
if ($Version -cne $buildProps.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText) {
    throw 'The installer version must match Directory.Build.props.'
}
& (Join-Path $PSScriptRoot 'build-windows-installer.ps1') -SkipReleaseBuild -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer
