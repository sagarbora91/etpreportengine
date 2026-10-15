<#
.SYNOPSIS
Code-protection plan 4.2 (lane O1). Archives the private symbols of one protected release: PDBs, the Obfuscar map
(Mapping.txt), the filled Obfuscar configuration, the Obfuscar log and obfuscation-report.json.

.DESCRIPTION
Copies <OutputDirectory>-symbols (written by build-windows-release.ps1 -Protect) to
<ArchiveRoot>\<version>-<sha7>\ (default E:\ETP\Reference\Symbols\1.13.0-abc1234\), writes SHA256SUMS.txt there, reads
every file back against the source, and leaves the source folder in place (the caller deletes it).

The map turns renamed names in diagnostics logs and the error register back into source names (Resolve-EtpStackTrace.ps1,
lane V1). Lose it and crash reports from that release cannot be read. It is PRIVATE: the archive is refused under
E:\ETP\Installers (copied to the USB-KIT and the OneDrive update feed), inside any OneDrive folder, and inside the release
folder. The nightly copy to G: is the existing backup routine's job, not this script's.

Called by E:\ETP\Migration 2026-10-04\Build-Installer.ps1 (lane G1) after the installer is built. Never overwrites an
existing archive: a rebuilt release with the same commit gets a new -Label.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\Archive-EtpSymbols.ps1 `
    -SymbolsDirectory artifacts\installer-1.13.0-symbols -ReleaseDirectory artifacts\installer-1.13.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SymbolsDirectory,
    # The protected release folder; its release.json gives the version and commit, and the archive must not be inside it.
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [string]$ArchiveRoot = 'E:\ETP\Reference\Symbols',
    # Optional suffix for a second archive of the same version and commit (for example "b2").
    [ValidatePattern('^[A-Za-z0-9-]{1,20}$')][string]$Label
)

$ErrorActionPreference = 'Stop'

function Get-EtpFullPathForArchive([string]$Path) { return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }
function Test-EtpInside([string]$Child, [string]$Parent) {
    return ((Get-EtpFullPathForArchive $Child) + '\').StartsWith((Get-EtpFullPathForArchive $Parent) + '\', [StringComparison]::OrdinalIgnoreCase)
}

$symbols = Get-EtpFullPathForArchive $SymbolsDirectory
$release = Get-EtpFullPathForArchive $ReleaseDirectory
if (-not (Test-Path -LiteralPath $symbols -PathType Container)) { throw "Symbols folder not found: $symbols" }
if (-not (Test-Path -LiteralPath $release -PathType Container)) { throw "Release folder not found: $release" }

$report = Join-Path $symbols 'obfuscation-report.json'
if (-not (Test-Path -LiteralPath $report)) { throw "obfuscation-report.json is missing in $symbols; this is not the symbols folder of a protected build." }
$maps = @(Get-ChildItem -LiteralPath $symbols -File | Where-Object { $_.Name -in 'Mapping.txt', 'Mapping.xml' })
if ($maps.Count -eq 0) { throw "No Obfuscar map (Mapping.txt) in $symbols. Refused: without it, crash reports of this release cannot be read." }

$receipt = Get-Content -Raw -LiteralPath (Join-Path $release 'release.json') | ConvertFrom-Json
if (-not $receipt.protection -or -not $receipt.protection.obfuscated) { throw "release.json in $release does not describe a protected build." }
$version = [string]$receipt.version
$commit = [string]$receipt.commit
if ($version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "release.json has an invalid version: $version" }
if ($commit -notmatch '^[0-9a-f]{7,40}$') { throw "release.json has an invalid commit: $commit" }
$mapHash = (Get-FileHash -LiteralPath $maps[0].FullName -Algorithm SHA256).Hash
if ($receipt.protection.mappingSha256 -and $receipt.protection.mappingSha256 -ine $mapHash) {
    throw "The map in $symbols (SHA-256 $mapHash) is not the one release.json names ($($receipt.protection.mappingSha256)): symbols and release are from different builds."
}

$name = "$version-$($commit.Substring(0, 7))"
if ($Label) { $name = "$name-$Label" }
$root = Get-EtpFullPathForArchive $ArchiveRoot
$target = Join-Path $root $name

foreach ($forbidden in $release, $symbols, 'E:\ETP\Installers') {
    if (Test-EtpInside $target $forbidden) { throw "The symbols archive must not be inside $forbidden : $target" }
}
if ($target -match '\\OneDrive( - [^\\]+)?(\\|$)' -or ($env:OneDrive -and (Test-EtpInside $target $env:OneDrive))) { throw "The symbols archive must not be inside OneDrive: $target" }
if (Test-Path -LiteralPath $target) { throw "Archive already exists (never overwritten): $target. Use -Label for a second build of the same commit." }

[void](New-Item -ItemType Directory -Path $target -Force)
Copy-Item -Path (Join-Path $symbols '*') -Destination $target -Recurse
$files = @(Get-ChildItem -LiteralPath $target -Recurse -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName)
$lines = foreach ($file in $files) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash, $file.FullName.Substring($target.Length + 1)
}
[IO.File]::WriteAllLines((Join-Path $target 'SHA256SUMS.txt'), [string[]]$lines, (New-Object Text.UTF8Encoding($false)))

# Read back against the source.
foreach ($file in $files) {
    $relative = $file.FullName.Substring($target.Length + 1)
    $source = Join-Path $symbols $relative
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
        throw "Archive copy differs from the source: $relative"
    }
}
Write-Host "Symbols of $version ($commit) archived to $target ($($files.Count) files). Private: never copy to the USB-KIT, OneDrive or an installer folder."
return $target
