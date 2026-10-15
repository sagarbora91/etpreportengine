param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "artifacts/windows-release",
    [string]$Version,
    [string]$CertificateThumbprint,
    [uri]$TimestampServer,
    # Code-protection plan 4.3 (lane O1). -Protect = the installer build only (Build-Installer.ps1 passes it; nothing else
    # does): folder publish (decision D4) to <OutputDirectory>-unprotected, a copy of it obfuscated with the pinned Obfuscar
    # into <OutputDirectory>, PDBs + Mapping.txt + obfuscation-report.json into <OutputDirectory>-symbols (private, never
    # shipped). Without -Protect this script behaves exactly as before (single-file publish, PDBs beside the exe).
    [switch]$Protect,
    [ValidateSet('Standard', 'Strong')][string]$ObfuscationProfile = 'Standard',
    [ValidateSet('Strings', 'None')][string]$DesktopObfuscation = 'Strings',
    [string]$ObfuscarRoot = 'E:\Tools\Obfuscar',
    # Frozen contract 5.1 (lane S1 implements it in sign-etp-artifacts.ps1): a PFX on the USB stick; the passphrase is asked for.
    [string]$PfxPath
)

$ErrorActionPreference = "Stop"

function Assert-NativeSuccess {
    param([Parameter(Mandatory)][string]$Step)

    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "Etp.Reporting.slnx"
$desktopProject = Join-Path $repoRoot "src/Etp.Reporting.Desktop/Etp.Reporting.Desktop.csproj"
$output = [System.IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }))
if (Test-Path -LiteralPath $output) {
    throw "Release output already exists. Choose a new OutputDirectory to preserve previous candidates: $output"
}
# Frozen contract 5.1: the unprotected publish and the symbols sit BESIDE the release folder, never inside it, so Inno's
# "{#ReleaseDirectory}\*" can never pack them.
$unprotectedOutput = "$output-unprotected"
$symbolsOutput = "$output-symbols"
if ($Protect) {
    foreach ($sibling in $unprotectedOutput, $symbolsOutput) {
        if (Test-Path -LiteralPath $sibling) { throw "Protected build folder already exists. Choose a new OutputDirectory: $sibling" }
    }
    # Refuse now, not after the 45-minute build and test run, when the pinned tool is missing or not verified or the
    # configuration does not load.
    # Dot-sourced WITH this script's values: a dot-sourced param block assigns its parameters in this scope.
    . (Join-Path $PSScriptRoot 'release\Invoke-EtpObfuscation.ps1') -ObfuscationProfile $ObfuscationProfile -DesktopObfuscation $DesktopObfuscation -ObfuscarRoot $ObfuscarRoot
    $null = Get-EtpObfuscarTool -ObfuscarRoot $ObfuscarRoot
    $null = New-EtpObfuscarConfig -Template (Join-Path $PSScriptRoot 'release\obfuscar.xml') -InPath $unprotectedOutput -OutPath $symbolsOutput -ObfuscationProfile $ObfuscationProfile -DesktopObfuscation $DesktopObfuscation
}
elseif ($PSBoundParameters.ContainsKey('ObfuscationProfile') -or $PSBoundParameters.ContainsKey('DesktopObfuscation')) {
    throw '-ObfuscationProfile and -DesktopObfuscation apply only with -Protect.'
}
if ($PfxPath -and $CertificateThumbprint) { throw 'Supply a signing certificate thumbprint or a PFX file, not both.' }
# A4.3 accepted unsigned: signing is applied when a certificate is supplied and
# skipped when it is not. An unsigned build is a deliberate, recorded choice, so the
# release still builds; what it loses is the guarantee of who produced it.
# Partial credentials are a mistake, not a choice: supplying one and not the other
# used to throw, and must not now degrade silently into an unsigned release.
if (-not $PfxPath -and [bool]$CertificateThumbprint -ne [bool]$TimestampServer) { throw 'Supply both a signing certificate and a timestamp service, or neither.' }
$signRelease = ([bool]$CertificateThumbprint -and [bool]$TimestampServer) -or [bool]$PfxPath
if ($PfxPath -and -not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) { throw "Signing PFX not found: $PfxPath" }
$sourceCommit = (git -C $repoRoot rev-parse HEAD).Trim()
Assert-NativeSuccess "Source commit lookup"
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
    $Version = $buildProps.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "Invalid semantic version: $Version" }

dotnet restore $solution
Assert-NativeSuccess "Solution restore"
# One build process: on the 20-thread owner PC an unlimited build started 19 compiler
# processes, and the PC powered off twice under that load on 1 October 2026.
dotnet build $solution -c $Configuration --no-restore -p:Version=$Version -m:1 -nodeReuse:false
Assert-NativeSuccess "Release build"
# One test project at a time. Run together, the CPU-heavy UI and import suites slowed SQL
# Server Express until its query-memory queue backed up: on 22 September 2026 the recovery
# drill's DBCC CHECKDB waited 57 s on RESOURCE_SEMAPHORE behind ten other sessions and the
# gate failed four runs out of four, while the same suite run project by project passed.
# `dotnet test` on the solution with -m:1 did not keep the test hosts apart, so each test
# project listed in the solution is run on its own, in order.
[xml]$solutionXml = Get-Content -LiteralPath $solution
$testProjects = @($solutionXml.SelectNodes('//Project/@Path') | ForEach-Object Value | Where-Object { $_ -like 'tests-dotnet/*Tests.csproj' })
if ($testProjects.Count -eq 0) { throw 'No test projects were found in the solution.' }
foreach ($testProject in $testProjects) {
    dotnet test (Join-Path $repoRoot $testProject) -c $Configuration --no-build -m:1
    Assert-NativeSuccess "Release test suite ($([IO.Path]::GetFileNameWithoutExtension($testProject)))"
}
if ($Protect) {
    # Decision D4: folder publish, so Obfuscar can rewrite the ETP assemblies before anything is packed. Everything up to
    # the obfuscation step below is built into the unprotected folder, which the protected-build smoke compares against.
    $publishDirectory = $unprotectedOutput
    dotnet publish $desktopProject -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=false -p:Version=$Version -o $publishDirectory
    Assert-NativeSuccess "Self-contained desktop folder publish"
}
else {
    $publishDirectory = $output
    dotnet publish $desktopProject -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:Version=$Version -o $output
    Assert-NativeSuccess "Self-contained desktop publish"
}

# S-13. The natives must sit next to the exe in the (administrator-only) installation folder, never be
# unpacked at run time under the user's %TEMP%, where an unelevated process could plant a DLL that an
# elevated ETP then loads. Gate: the WPF/runtime natives are on disk beside the exe, and the exe was not
# published for self-extraction (a self-extracting exe carries them inside and leaves none beside it).
$nativeNames = @('PresentationNative_cor3.dll','wpfgfx_cor3.dll','vcruntime140_cor3.dll','D3DCompiler_47_cor3.dll','PenImc_cor3.dll')
$missingNatives = @($nativeNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $publishDirectory $_)) })
if ($missingNatives.Count -gt 0) { throw "Native libraries are not beside the executable (self-extract may be on): $($missingNatives -join ', '). Release refused (audit S-13)." }
$sqlClientNative = @(Get-ChildItem -LiteralPath $publishDirectory -Filter 'Microsoft.Data.SqlClient.SNI*.dll' -File -Recurse -ErrorAction SilentlyContinue)
if ($sqlClientNative.Count -eq 0) { Write-Warning 'No Microsoft.Data.SqlClient.SNI native DLL found beside the executable; confirm SqlClient natives are not being extracted at run time.' }

$packagedScripts = Join-Path $publishDirectory "scripts"
New-Item -ItemType Directory -Path $packagedScripts -Force | Out-Null
foreach ($scriptName in @('bootstrap-etp-prerequisites.ps1','backup-etp-database.ps1','install-daily-backup-task.ps1','copy-etp-backup-to-disk.ps1','install-monthly-recovery-drill-task.ps1','install-etp-automation-task.ps1','remove-etp-scheduled-tasks.ps1','invoke-monthly-recovery-drill-runner.ps1','invoke-etp-recovery-drill.ps1','new-etp-support-package.ps1','etp-operations-common.ps1','invoke-database-maintenance.ps1','initialize-etp-operation-folders.ps1','install-etp-sql-operations.ps1','restore-etp-database.ps1','Test-EtpRestoredPc.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination $packagedScripts -Force
}

# S-05. The OneDrive shop/Workpc scripts ship inside the installer, in {app}\scripts\onedrive (Program Files:
# Users read-only), so the elevated tasks never run code from a folder an ordinary user can edit. Real
# configuration files are not shipped (they live in ProgramData\EtpReporting\Operations); the *.example.json
# templates and the scripts are. tests\ is development-only.
$oneDriveSource = Join-Path $PSScriptRoot 'onedrive'
if (-not (Test-Path -LiteralPath $oneDriveSource -PathType Container)) { throw "scripts\onedrive is missing: $oneDriveSource" }
$oneDriveTarget = Join-Path $packagedScripts 'onedrive'
New-Item -ItemType Directory -Path $oneDriveTarget -Force | Out-Null
foreach ($item in Get-ChildItem -LiteralPath $oneDriveSource -File) {
    if ($item.Extension -in '.ps1','.psm1','.psd1') { Copy-Item -LiteralPath $item.FullName -Destination $oneDriveTarget -Force }
    elseif ($item.Name -like '*.example.json') { Copy-Item -LiteralPath $item.FullName -Destination $oneDriveTarget -Force }
}
# 1.11.2 integration: L2's final file list; each must reach the release folder.
foreach ($required in 'OneDriveCommon.ps1','Send-ShopDailyFiles.ps1','Register-ShopDailyTask.ps1','Receive-ShopDailyFiles.ps1',
                      'Copy-EtpBackupToOneDrive.ps1','Register-WorkpcTasks.ps1','shop-daily-files.config.example.json',
                      'workpc-receive.config.example.json','workpc-backup.config.example.json',
                      # 1.12 C1 (review M-6): shop results.
                      'ShopResultCommon.ps1','Publish-ShopResults.ps1','Show-ShopResults.ps1') {
    if (-not (Test-Path -LiteralPath (Join-Path $oneDriveTarget $required))) { throw "scripts\onedrive\$required did not reach the release folder." }
}

# Dependency 3. Third-party licence notices travel with every release.
$notices = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.txt'
if (-not (Test-Path -LiteralPath $notices)) { throw 'THIRD-PARTY-NOTICES.txt is missing from the repository root.' }
Copy-Item -LiteralPath $notices -Destination $publishDirectory -Force

New-Item -ItemType Directory -Path (Join-Path $packagedScripts 'sql') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sql\etp-operations-broker.sql') -Destination (Join-Path $packagedScripts 'sql')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sql\etp-operations-grants.sql') -Destination (Join-Path $packagedScripts 'sql')

# 1.12 A3: setup refuses to run without database\objects (views, procedures and functions as files), so the release
# folder must carry it beside database\migrations (Desktop csproj Content items).
foreach ($databaseFolder in 'database\migrations','database\objects') {
    if (@(Get-ChildItem -LiteralPath (Join-Path $publishDirectory $databaseFolder) -Filter '*.sql' -File -ErrorAction SilentlyContinue).Count -eq 0) {
        throw "$databaseFolder did not reach the release folder."
    }
}

$obfuscation = $null
if ($Protect) {
    # Obfuscation runs on a copy, so the unprotected folder stays the smoke's reference. A failure here fails the build:
    # there is no fallback to an unprotected release labelled protected (plan 4.3.2).
    Copy-Item -LiteralPath $publishDirectory -Destination $output -Recurse
    $obfuscation = Invoke-EtpObfuscation -UnprotectedDirectory $publishDirectory -ProtectedDirectory $output -SymbolsDirectory $symbolsOutput `
        -ObfuscationProfile $ObfuscationProfile -DesktopObfuscation $DesktopObfuscation -ObfuscarRoot $ObfuscarRoot `
        -ConfigTemplate (Join-Path $PSScriptRoot 'release\obfuscar.xml') -Commit $sourceCommit
    [IO.File]::WriteAllText((Join-Path $publishDirectory 'NOT-FOR-RELEASE.txt'),
        "Unprotected comparison build of $Version ($sourceCommit), used only by the protected-build smoke. Never package it, copy it to a PC or share it.`r`n",
        (New-Object Text.UTF8Encoding($false)))
    @{
        product = "ETP Reporting Engine"
        version = $Version
        runtime = $Runtime
        builtUtc = [DateTime]::UtcNow.ToString("o")
        commit = $sourceCommit
        protection = @{ obfuscated = $false; purpose = 'unprotected comparison for the protected-build smoke; not for release' }
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publishDirectory "release.json") -Encoding utf8
}

$packagedScripts = Join-Path $output "scripts"
$executable = Join-Path $output "Etp.Reporting.Desktop.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Published executable was not produced." }
# Signing comes after obfuscation: rewriting a signed DLL would break its signature. In the folder layout the ETP code is
# in the DLLs, so they are signed too.
$etpAssemblies = @()
if ($Protect) { $etpAssemblies = @(Get-ChildItem -LiteralPath $output -Filter 'Etp.Reporting.*.dll' -File | Sort-Object Name | ForEach-Object FullName) }
$signingInputs = @($executable) + $etpAssemblies + @(Get-ChildItem -LiteralPath $packagedScripts -Filter '*.ps1' -File -Recurse | ForEach-Object FullName)
if ($PfxPath) {
    $pfxArguments = @{ Paths = $signingInputs; PfxPath = $PfxPath }
    if ($TimestampServer) { $pfxArguments.TimestampServer = $TimestampServer }
    & (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') @pfxArguments
}
elseif ($signRelease) { & (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') -Paths $signingInputs -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer }
else { Write-Warning 'Building an UNSIGNED release. The executable and scripts carry no publisher signature.' }
$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
if ($Protect) {
    @(@($executable) + $etpAssemblies | ForEach-Object { $h = Get-FileHash -LiteralPath $_ -Algorithm SHA256; "$($h.Hash)  $(Split-Path -Leaf $h.Path)" }) |
        Set-Content -LiteralPath (Join-Path $output "SHA256SUMS.txt") -Encoding ascii
}
else {
    "$($hash.Hash)  $($hash.Path | Split-Path -Leaf)" | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS.txt") -Encoding ascii
}
$receipt = @{
    product = "ETP Reporting Engine"
    version = $Version
    runtime = $Runtime
    builtUtc = [DateTime]::UtcNow.ToString("o")
    commit = $sourceCommit
    sourceTreeClean = (@(git -C $repoRoot status --porcelain --untracked-files=all).Count -eq 0)
}
if ($Protect) {
    $receipt.protection = @{
        obfuscated = $true
        tool = "Obfuscar.GlobalTool $($obfuscation.toolVersion)"
        profile = $obfuscation.profile
        desktop = $obfuscation.desktop
        layout = 'folder'
        pdbShipped = $false
        mappingSha256 = $obfuscation.mappingSha256
    }
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output "release.json") -Encoding utf8
if ($Protect) {
    Write-Host "Protected Windows release created at $output"
    Write-Host "Unprotected comparison build (never ship): $unprotectedOutput"
    Write-Host "Symbols and obfuscation map (private; archive with scripts\release\Archive-EtpSymbols.ps1): $symbolsOutput"
}
else { Write-Host "Windows release created at $output" }
