param([string]$Version, [string]$OutputDirectory = "artifacts\offline-deployment")
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Version)) { [xml]$p=Get-Content (Join-Path $repoRoot 'Directory.Build.props'); $Version=$p.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText }
$installer = Join-Path $repoRoot "artifacts\installer\EtpReportingEngine-Setup-$Version-x64.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw "Build the versioned installer first." }
$resolved = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory)); New-Item -ItemType Directory -Path $resolved -Force | Out-Null
$stage = Join-Path $resolved "stage-$([guid]::NewGuid().ToString('N'))"; New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item -LiteralPath $installer -Destination $stage
    foreach($file in @('docs\14_WINDOWS_QUICK_START.md','docs\17_ADMINISTRATOR_HANDBOOK.md','docs\18_USER_MANUAL.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $stage }
    Get-ChildItem -LiteralPath $stage -File | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash)  $([IO.Path]::GetFileName($_.Path))" } | Set-Content (Join-Path $stage 'SHA256SUMS.txt') -Encoding ascii
    $zip=Join-Path $resolved "EtpReportingEngine-Offline-$Version.zip"; if(Test-Path $zip){Remove-Item -LiteralPath $zip -Force}; Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Format-List
} finally { if(Test-Path $stage){Remove-Item -LiteralPath $stage -Recurse -Force} }
