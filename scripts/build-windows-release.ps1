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
if (-not $CertificateThumbprint -or -not $TimestampServer) { throw 'A signing certificate and timestamp service are required to build a release.' }
$sourceCommit = (git -C $repoRoot rev-parse HEAD).Trim()
Assert-NativeSuccess "Source commit lookup"
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
    $Version = $buildProps.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "Invalid semantic version: $Version" }

dotnet restore $solution
Assert-NativeSuccess "Solution restore"
dotnet build $solution -c $Configuration --no-restore -p:Version=$Version
Assert-NativeSuccess "Release build"
dotnet test $solution -c $Configuration --no-build
Assert-NativeSuccess "Release test suite"
dotnet publish $desktopProject -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o $output
Assert-NativeSuccess "Self-contained desktop publish"

$packagedScripts = Join-Path $output "scripts"
New-Item -ItemType Directory -Path $packagedScripts -Force | Out-Null
foreach ($scriptName in @('bootstrap-etp-prerequisites.ps1','backup-etp-database.ps1','install-daily-backup-task.ps1','install-monthly-recovery-drill-task.ps1','install-etp-automation-task.ps1','remove-etp-scheduled-tasks.ps1','invoke-monthly-recovery-drill-runner.ps1','invoke-etp-recovery-drill.ps1','new-etp-support-package.ps1','etp-operations-common.ps1','invoke-database-maintenance.ps1','initialize-etp-operation-folders.ps1','install-etp-sql-operations.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination $packagedScripts -Force
}

New-Item -ItemType Directory -Path (Join-Path $packagedScripts 'sql') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'sql\etp-operations-broker.sql') -Destination (Join-Path $packagedScripts 'sql')

$executable = Join-Path $output "Etp.Reporting.Desktop.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Published executable was not produced." }
$signingInputs = @($executable) + @(Get-ChildItem -LiteralPath $packagedScripts -Filter '*.ps1' -File | ForEach-Object FullName)
& (Join-Path $PSScriptRoot 'sign-etp-artifacts.ps1') -Paths $signingInputs -CertificateThumbprint $CertificateThumbprint -TimestampServer $TimestampServer
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
