param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "artifacts/windows-release",
    [string]$Version,
    [string]$CertificateThumbprint,
    [uri]$TimestampServer
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
# A4.3 accepted unsigned: signing is applied when a certificate is supplied and
# skipped when it is not. An unsigned build is a deliberate, recorded choice, so the
# release still builds; what it loses is the guarantee of who produced it.
# Partial credentials are a mistake, not a choice: supplying one and not the other
# used to throw, and must not now degrade silently into an unsigned release.
if ([bool]$CertificateThumbprint -ne [bool]$TimestampServer) { throw 'Supply both a signing certificate and a timestamp service, or neither.' }
$signRelease = [bool]$CertificateThumbprint -and [bool]$TimestampServer
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
dotnet publish $desktopProject -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:Version=$Version -o $output
Assert-NativeSuccess "Self-contained desktop publish"

# S-13. The natives must sit next to the exe in the (administrator-only) installation folder, never be
# unpacked at run time under the user's %TEMP%, where an unelevated process could plant a DLL that an
# elevated ETP then loads. Gate: the WPF/runtime natives are on disk beside the exe, and the exe was not
# published for self-extraction (a self-extracting exe carries them inside and leaves none beside it).
$nativeNames = @('PresentationNative_cor3.dll','wpfgfx_cor3.dll','vcruntime140_cor3.dll','D3DCompiler_47_cor3.dll','PenImc_cor3.dll')
$missingNatives = @($nativeNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $output $_)) })
if ($missingNatives.Count -gt 0) { throw "Native libraries are not beside the executable (self-extract may be on): $($missingNatives -join ', '). Release refused (audit S-13)." }
$sqlClientNative = @(Get-ChildItem -LiteralPath $output -Filter 'Microsoft.Data.SqlClient.SNI*.dll' -File -Recurse -ErrorAction SilentlyContinue)
if ($sqlClientNative.Count -eq 0) { Write-Warning 'No Microsoft.Data.SqlClient.SNI native DLL found beside the executable; confirm SqlClient natives are not being extracted at run time.' }

$packagedScripts = Join-Path $output "scripts"
New-Item -ItemType Directory -Path $packagedScripts -Force | Out-Null
foreach ($scriptName in @('bootstrap-etp-prerequisites.ps1','backup-etp-database.ps1','install-daily-backup-task.ps1','install-monthly-recovery-drill-task.ps1','install-etp-automation-task.ps1','remove-etp-scheduled-tasks.ps1','invoke-monthly-recovery-drill-runner.ps1','invoke-etp-recovery-drill.ps1','new-etp-support-package.ps1','etp-operations-common.ps1','invoke-database-maintenance.ps1','initialize-etp-operation-folders.ps1','install-etp-sql-operations.ps1','restore-etp-database.ps1','Test-EtpRestoredPc.ps1')) {
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
                      'workpc-receive.config.example.json','workpc-backup.config.example.json') {
    if (-not (Test-Path -LiteralPath (Join-Path $oneDriveTarget $required))) { throw "scripts\onedrive\$required did not reach the release folder." }
}

# Dependency 3. Third-party licence notices travel with every release.
$notices = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.txt'
if (-not (Test-Path -LiteralPath $notices)) { throw 'THIRD-PARTY-NOTICES.txt is missing from the repository root.' }
Copy-Item -LiteralPath $notices -Destination $output -Force

New-Item -ItemType Directory -Path (Join-Path $packagedScripts 'sql') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sql\etp-operations-broker.sql') -Destination (Join-Path $packagedScripts 'sql')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sql\etp-operations-grants.sql') -Destination (Join-Path $packagedScripts 'sql')

$executable = Join-Path $output "Etp.Reporting.Desktop.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Published executable was not produced." }
$signingInputs = @($executable) + @(Get-ChildItem -LiteralPath $packagedScripts -Filter '*.ps1' -File -Recurse | ForEach-Object FullName)
if ($signRelease) { & (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') -Paths $signingInputs -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer }
else { Write-Warning 'Building an UNSIGNED release. The executable and scripts carry no publisher signature.' }
$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
"$($hash.Hash)  $($hash.Path | Split-Path -Leaf)" | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS.txt") -Encoding ascii
@{
    product = "ETP Reporting Engine"
    version = $Version
    runtime = $Runtime
    builtUtc = [DateTime]::UtcNow.ToString("o")
    commit = $sourceCommit
    sourceTreeClean = (@(git -C $repoRoot status --porcelain --untracked-files=all).Count -eq 0)
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output "release.json") -Encoding utf8
Write-Host "Windows release created at $output"
