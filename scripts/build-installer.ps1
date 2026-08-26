param([string]$Version)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
    $Version = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "Invalid semantic version: $Version" }
$candidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
if (-not $candidates) { throw "Inno Setup 6 compiler was not found." }
& $candidates[0] "/DAppVersion=$Version" (Join-Path $repoRoot "installer\EtpReportingEngine.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed." }
