param([string]$Configuration = "Release", [switch]$SkipReleaseBuild)
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipReleaseBuild) { & (Join-Path $PSScriptRoot "build-windows-release.ps1") -Configuration $Configuration }
$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compiler) { throw "Inno Setup 6 is required. Install it with: winget install JRSoftware.InnoSetup" }
[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
$version = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
& $compiler "/DAppVersion=$version" (Join-Path $repoRoot "installer\EtpReportingEngine.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
$installer = Get-ChildItem (Join-Path $repoRoot "artifacts\installer") -Filter "*.exe" | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $($installer.Name)" } | Set-Content (Join-Path $installer.DirectoryName "SHA256SUMS.txt") -Encoding ascii
Write-Host "Windows installer created at $($installer.FullName)"
