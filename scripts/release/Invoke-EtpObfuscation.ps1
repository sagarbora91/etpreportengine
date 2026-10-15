<#
.SYNOPSIS
Code-protection plan 4.3 (lane O1). Obfuscates the ETP assemblies of a folder publish with a pinned Obfuscar, for the
PROTECTED release build only.

.DESCRIPTION
Called only by scripts\build-windows-release.ps1 -Protect, after every test project has passed and after dotnet publish.
Never called by dev builds, dotnet test, the fast gate, the elevated gate's test steps or CI.

  1. Finds Obfuscar at <ObfuscarRoot>\<PinnedVersion>\obfuscar.console.exe (a `dotnet tool install --tool-path` folder)
     and checks the tool store holds exactly that version and that its .nupkg has the recorded SHA-256. Refuses otherwise.
     Nothing is ever downloaded.
  2. Fills scripts\release\obfuscar.xml (InPath = the unprotected publish, OutPath = <SymbolsDirectory>\obfuscar-out).
  3. Runs Obfuscar; a non-zero exit, a missing module or a missing Mapping.txt is a failure (no silent fallback).
  4. Copies the rewritten DLLs over the copies in -ProtectedDirectory; moves every *.pdb out of it into
     <SymbolsDirectory>\original-pdb (the unprotected build's symbols); regenerated PDBs, Mapping.txt, the filled
     configuration, the Obfuscar log and obfuscation-report.json stay in <SymbolsDirectory>.
  5. Proves: every listed module changed, no *.pdb is left in -ProtectedDirectory, the symbols folder is not inside it.

The map file turns renamed names in diagnostics logs and the error register back into source names. It is PRIVATE: it is
archived with the symbols (Archive-EtpSymbols.ps1) and never shipped, never put in E:\ETP\Installers, USB-KIT or OneDrive.

Dot-sourcing this file (". .\Invoke-EtpObfuscation.ps1") only defines its functions (used by tests\Obfuscation.Tests.ps1).

.PARAMETER ObfuscationProfile
Standard (default): KeepPublicApi=true - private and internal names renamed, public API names kept, strings hidden.
Strong: KeepPublicApi=false - public types and methods of the six non-UI assemblies renamed too (the exclusion list in
obfuscar.xml already covers the reflection and JSON cases found in the code); needs the Desktop module in the project.
Use Strong only after the protected-build smoke has passed with it.

.PARAMETER DesktopObfuscation
Strings (default): Desktop is in the Obfuscar project with every namespace skipped for renaming; only string hiding may
apply. None: Desktop is left exactly as published (Standard profile only).
#>
[CmdletBinding()]
param(
    [string]$UnprotectedDirectory,
    [string]$ProtectedDirectory,
    [string]$SymbolsDirectory,
    [ValidateSet('Standard', 'Strong')][string]$ObfuscationProfile = 'Standard',
    [ValidateSet('Strings', 'None')][string]$DesktopObfuscation = 'Strings',
    [string]$ObfuscarRoot = 'E:\Tools\Obfuscar',
    [string]$ConfigTemplate = (Join-Path $PSScriptRoot 'obfuscar.xml'),
    [string]$Commit
)

$ErrorActionPreference = 'Stop'

# ---- Pin (code-protection plan 4.3.4). Change only together with RELEASE-PROTECTION.md and a protected-build smoke. ----
# Stable line first (plan section 6); 3.0.0-beta.x only if 2.2.50 fails the smoke.
$script:EtpObfuscarPackageId = 'obfuscar.globaltool'
$script:EtpObfuscarVersion = '2.2.50'
# SHA-256 of obfuscar.globaltool.<version>.nupkg as installed under <ObfuscarRoot>\<version>\.store. Lanes have no internet,
# so the integrator records it at install time (RELEASE-PROTECTION.md "Installing the tool"). Empty = refuse.
$script:EtpObfuscarPackageSha256 = ''

# The seven ETP assemblies. The six non-UI ones get the full treatment; Desktop is handled by -DesktopObfuscation.
$script:EtpLibraryModules = @(
    'Etp.Reporting.Domain.dll',
    'Etp.Reporting.Application.dll',
    'Etp.Reporting.Import.dll',
    'Etp.Reporting.Infrastructure.Http.dll',
    'Etp.Reporting.Infrastructure.SqlServer.dll',
    'Etp.Reporting.Reporting.dll')
$script:EtpDesktopModule = 'Etp.Reporting.Desktop.dll'

function Get-EtpObfuscationModules {
    param([ValidateSet('Strings', 'None')][string]$DesktopObfuscation = 'Strings')
    if ($DesktopObfuscation -eq 'None') { return @($script:EtpLibraryModules) }
    return @($script:EtpLibraryModules) + $script:EtpDesktopModule
}

function Get-EtpFullPath {
    param([Parameter(Mandatory)][string]$Path)
    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-EtpPathInside {
    # True when $Child is $Parent or anywhere below it (case-insensitive, Windows paths).
    param([Parameter(Mandatory)][string]$Child, [Parameter(Mandatory)][string]$Parent)
    $c = (Get-EtpFullPath $Child) + '\'
    $p = (Get-EtpFullPath $Parent) + '\'
    return $c.StartsWith($p, [StringComparison]::OrdinalIgnoreCase)
}

function Get-EtpObfuscarTool {
    # Returns the path of obfuscar.console.exe for the pinned version, or throws a message that says what to install.
    param(
        [Parameter(Mandatory)][string]$ObfuscarRoot,
        [string]$Version = $script:EtpObfuscarVersion,
        [string]$ExpectedSha256 = $script:EtpObfuscarPackageSha256
    )
    $install = "dotnet tool install Obfuscar.GlobalTool --version $Version --tool-path `"$(Join-Path $ObfuscarRoot $Version)`""
    $toolDirectory = Join-Path $ObfuscarRoot $Version
    $exe = Join-Path $toolDirectory 'obfuscar.console.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "Obfuscar $Version is not installed at $toolDirectory (obfuscar.console.exe missing). The protected build never downloads tools. The integrator installs it once, elevated or as Sagar: $install  - then records the package SHA-256 (docs\security\RELEASE-PROTECTION.md)."
    }
    $store = Join-Path $toolDirectory ".store\$($script:EtpObfuscarPackageId)"
    $versions = @(Get-ChildItem -LiteralPath $store -Directory -ErrorAction SilentlyContinue | ForEach-Object Name)
    if ($versions.Count -ne 1 -or $versions[0] -ne $Version) {
        throw "Obfuscar tool store $store holds version(s) '$($versions -join ', ')', expected exactly $Version. Reinstall into an empty folder: $install"
    }
    $nupkg = @(Get-ChildItem -LiteralPath (Join-Path $store $Version) -Recurse -File -Filter "$($script:EtpObfuscarPackageId).$Version.nupkg" -ErrorAction SilentlyContinue)
    if ($nupkg.Count -ne 1) { throw "The Obfuscar $Version package file ($($script:EtpObfuscarPackageId).$Version.nupkg) was not found exactly once under $store; cannot check its SHA-256." }
    if ([string]::IsNullOrWhiteSpace($ExpectedSha256)) {
        $actual = (Get-FileHash -LiteralPath $nupkg[0].FullName -Algorithm SHA256).Hash
        throw "No SHA-256 is recorded for Obfuscar $Version. Compare $actual with the value nuget.org shows for Obfuscar.GlobalTool $Version (or 'dotnet nuget verify'), then record it in `$script:EtpObfuscarPackageSha256 in scripts\release\Invoke-EtpObfuscation.ps1 and in docs\security\RELEASE-PROTECTION.md. Protected build refused."
    }
    $hash = (Get-FileHash -LiteralPath $nupkg[0].FullName -Algorithm SHA256).Hash
    if ($hash -ine $ExpectedSha256) {
        throw "Obfuscar package $($nupkg[0].FullName) has SHA-256 $hash; the recorded value is $ExpectedSha256. Tool tampered with or a different build installed. Protected build refused."
    }
    return $exe
}

function New-EtpObfuscarConfig {
    # Fills the template; returns the [xml] document (not saved).
    param(
        [Parameter(Mandatory)][string]$Template,
        [Parameter(Mandatory)][string]$InPath,
        [Parameter(Mandatory)][string]$OutPath,
        [ValidateSet('Standard', 'Strong')][string]$ObfuscationProfile = 'Standard',
        [ValidateSet('Strings', 'None')][string]$DesktopObfuscation = 'Strings'
    )
    if ($ObfuscationProfile -eq 'Strong' -and $DesktopObfuscation -eq 'None') {
        throw 'The Strong profile renames public members of the six non-UI assemblies, so Etp.Reporting.Desktop.dll must be in the Obfuscar project for its references to be rewritten. Use -DesktopObfuscation Strings with -ObfuscationProfile Strong.'
    }
    [xml]$config = Get-Content -LiteralPath $Template -Raw
    $vars = @{ InPath = (Get-EtpFullPath $InPath); OutPath = (Get-EtpFullPath $OutPath); LogFile = (Join-Path (Get-EtpFullPath $OutPath) 'Mapping.txt') }
    $vars.KeepPublicApi = $(if ($ObfuscationProfile -eq 'Standard') { 'true' } else { 'false' })
    foreach ($name in $vars.Keys) {
        $node = $config.SelectSingleNode("/Obfuscator/Var[@name='$name']")
        if ($null -eq $node) { throw "obfuscar.xml has no Var '$name'." }
        $node.SetAttribute('value', $vars[$name])
    }
    $search = $config.SelectSingleNode('/Obfuscator/AssemblySearchPath')
    if ($null -eq $search) { throw 'obfuscar.xml has no AssemblySearchPath.' }
    $search.SetAttribute('path', $vars.InPath)
    $wanted = Get-EtpObfuscationModules -DesktopObfuscation $DesktopObfuscation
    foreach ($module in @($config.SelectNodes('/Obfuscator/Module'))) {
        $file = Split-Path -Leaf $module.GetAttribute('file')
        if ($wanted -notcontains $file) { [void]$module.ParentNode.RemoveChild($module) }
    }
    $present = @($config.SelectNodes('/Obfuscator/Module') | ForEach-Object { Split-Path -Leaf $_.GetAttribute('file') })
    $missing = @($wanted | Where-Object { $present -notcontains $_ })
    if ($missing.Count -gt 0) { throw "obfuscar.xml does not list: $($missing -join ', ')." }
    return $config
}

function Invoke-EtpObfuscation {
    param(
        [Parameter(Mandatory)][string]$UnprotectedDirectory,
        [Parameter(Mandatory)][string]$ProtectedDirectory,
        [Parameter(Mandatory)][string]$SymbolsDirectory,
        [string]$ObfuscationProfile = 'Standard',
        [string]$DesktopObfuscation = 'Strings',
        [string]$ObfuscarRoot = 'E:\Tools\Obfuscar',
        [string]$ConfigTemplate,
        [string]$Commit
    )
    $unprotected = Get-EtpFullPath $UnprotectedDirectory
    $protected = Get-EtpFullPath $ProtectedDirectory
    $symbols = Get-EtpFullPath $SymbolsDirectory
    foreach ($pair in @(@($symbols, $protected), @($symbols, $unprotected), @($protected, $unprotected), @($unprotected, $protected))) {
        if (Test-EtpPathInside -Child $pair[0] -Parent $pair[1]) { throw "Folder $($pair[0]) must not be inside (or equal to) $($pair[1]): the symbols and the map must never be packed with a release." }
    }
    if (Test-EtpPathInside -Child $symbols -Parent 'E:\ETP\Installers') { throw "The symbols folder must not be under E:\ETP\Installers (copied to the USB-KIT and the update feed): $symbols" }
    if ($symbols -match '\\OneDrive( - [^\\]+)?(\\|$)') { throw "The symbols folder must not be inside OneDrive: $symbols" }
    foreach ($folder in $unprotected, $protected) { if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw "Folder missing: $folder" } }
    if (Test-Path -LiteralPath $symbols) { throw "Symbols folder already exists; choose a new OutputDirectory: $symbols" }

    $exe = Get-EtpObfuscarTool -ObfuscarRoot $ObfuscarRoot
    $modules = Get-EtpObfuscationModules -DesktopObfuscation $DesktopObfuscation
    foreach ($module in $modules) {
        foreach ($folder in $unprotected, $protected) {
            if (-not (Test-Path -LiteralPath (Join-Path $folder $module) -PathType Leaf)) { throw "$module is not in $folder (folder publish expected; single-file bundles cannot be obfuscated)." }
        }
    }

    [void](New-Item -ItemType Directory -Path $symbols)
    $outPath = Join-Path $symbols 'obfuscar-out'
    [void](New-Item -ItemType Directory -Path $outPath)
    if (-not $ConfigTemplate) { $ConfigTemplate = Join-Path $PSScriptRoot 'obfuscar.xml' }
    $config = New-EtpObfuscarConfig -Template $ConfigTemplate -InPath $unprotected -OutPath $outPath -ObfuscationProfile $ObfuscationProfile -DesktopObfuscation $DesktopObfuscation
    $filled = Join-Path $symbols 'obfuscar.filled.xml'
    $config.Save($filled)

    # Obfuscar.GlobalTool 2.2.x targets an older runtime than the .NET 10 SDK on Workpc; roll forward instead of failing.
    $previousRollForward = $env:DOTNET_ROLL_FORWARD
    $env:DOTNET_ROLL_FORWARD = 'Major'
    $log = Join-Path $symbols 'obfuscar.log'
    try {
        $previousPreference = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & $exe $filled *>&1 | Tee-Object -FilePath $log | Out-Host
        $exit = $LASTEXITCODE
        $ErrorActionPreference = $previousPreference
    }
    finally { $env:DOTNET_ROLL_FORWARD = $previousRollForward }
    if ($exit -ne 0) { throw "Obfuscar failed with exit code $exit (log: $log). No protected release is produced." }

    $mapping = @(Get-ChildItem -LiteralPath $outPath -File | Where-Object { $_.Name -in 'Mapping.txt', 'Mapping.xml' })
    if ($mapping.Count -eq 0 -or $mapping[0].Length -eq 0) { throw "Obfuscar produced no Mapping.txt in $outPath; without the map, crash reports from this build cannot be read. Refused." }

    $results = @()
    foreach ($module in $modules) {
        $rewritten = Join-Path $outPath $module
        if (-not (Test-Path -LiteralPath $rewritten -PathType Leaf)) { throw "Obfuscar did not write $module (log: $log)." }
        $before = (Get-FileHash -LiteralPath (Join-Path $unprotected $module) -Algorithm SHA256).Hash
        $after = (Get-FileHash -LiteralPath $rewritten -Algorithm SHA256).Hash
        if ($before -eq $after) { throw "$module is byte-identical after Obfuscar: the configuration did nothing. Refused rather than labelling an unprotected build protected." }
        Copy-Item -LiteralPath $rewritten -Destination (Join-Path $protected $module) -Force
        $results += [ordered]@{ module = $module; unprotectedSha256 = $before; protectedSha256 = $after }
    }

    # Symbols: the publish's PDBs describe the UNPROTECTED assemblies (use them with the map); Obfuscar's regenerated
    # PDBs (if written) describe the protected ones. Neither is ever shipped.
    $originalPdb = Join-Path $symbols 'original-pdb'
    [void](New-Item -ItemType Directory -Path $originalPdb)
    foreach ($pdb in @(Get-ChildItem -LiteralPath $protected -Recurse -File -Filter '*.pdb')) {
        $relative = $pdb.FullName.Substring($protected.Length + 1)
        $target = Join-Path $originalPdb $relative
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force)
        Move-Item -LiteralPath $pdb.FullName -Destination $target
    }
    $protectedPdb = Join-Path $symbols 'protected-pdb'
    [void](New-Item -ItemType Directory -Path $protectedPdb)
    foreach ($pdb in @(Get-ChildItem -LiteralPath $outPath -File -Filter '*.pdb')) { Move-Item -LiteralPath $pdb.FullName -Destination $protectedPdb }
    foreach ($map in $mapping) { Move-Item -LiteralPath $map.FullName -Destination $symbols }

    $leftPdb = @(Get-ChildItem -LiteralPath $protected -Recurse -File -Filter '*.pdb')
    if ($leftPdb.Count -gt 0) { throw "PDB files are still in the protected folder: $($leftPdb.FullName -join ', ')" }
    $leftMap = @(Get-ChildItem -LiteralPath $protected -Recurse -File | Where-Object { $_.Name -in 'Mapping.txt', 'Mapping.xml', 'obfuscar.filled.xml' })
    if ($leftMap.Count -gt 0) { throw "Obfuscation map or configuration found in the protected folder: $($leftMap.FullName -join ', ')" }

    $mapFile = Join-Path $symbols $mapping[0].Name
    $report = [ordered]@{
        schema = 1
        tool = 'Obfuscar.GlobalTool'
        toolVersion = $script:EtpObfuscarVersion
        toolPackageSha256 = $script:EtpObfuscarPackageSha256
        profile = $ObfuscationProfile
        keepPublicApi = ($ObfuscationProfile -eq 'Standard')
        desktop = $DesktopObfuscation
        commit = $Commit
        obfuscatedUtc = [DateTime]::UtcNow.ToString('o')
        mapping = $mapping[0].Name
        mappingSha256 = (Get-FileHash -LiteralPath $mapFile -Algorithm SHA256).Hash
        regeneratedPdbs = @(Get-ChildItem -LiteralPath $protectedPdb -File | ForEach-Object Name)
        modules = $results
    }
    $reportPath = Join-Path $symbols 'obfuscation-report.json'
    [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Obfuscated $($modules.Count) ETP assemblies ($ObfuscationProfile profile, Desktop: $DesktopObfuscation). Map and symbols: $symbols (private, never ship)."
    return $report
}

if ($MyInvocation.InvocationName -eq '.') { return }
if (-not $UnprotectedDirectory -or -not $ProtectedDirectory -or -not $SymbolsDirectory) {
    throw 'Supply -UnprotectedDirectory, -ProtectedDirectory and -SymbolsDirectory (normally only build-windows-release.ps1 -Protect calls this script).'
}
Invoke-EtpObfuscation -UnprotectedDirectory $UnprotectedDirectory -ProtectedDirectory $ProtectedDirectory -SymbolsDirectory $SymbolsDirectory `
    -ObfuscationProfile $ObfuscationProfile -DesktopObfuscation $DesktopObfuscation -ObfuscarRoot $ObfuscarRoot -ConfigTemplate $ConfigTemplate -Commit $Commit
