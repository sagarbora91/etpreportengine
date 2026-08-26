param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "artifacts/windows-release",
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "Etp.Reporting.slnx"
$desktopProject = Join-Path $repoRoot "src/Etp.Reporting.Desktop/Etp.Reporting.Desktop.csproj"
$output = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$buildProps = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props")
    $Version = $buildProps.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "Invalid semantic version: $Version" }

dotnet restore $solution
dotnet build $solution -c $Configuration --no-restore -p:Version=$Version
dotnet test $solution -c $Configuration --no-build
dotnet publish $desktopProject -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o $output

$packagedScripts = Join-Path $output "scripts"
New-Item -ItemType Directory -Path $packagedScripts -Force | Out-Null
foreach ($scriptName in @('bootstrap-etp-prerequisites.ps1','backup-etp-database.ps1','install-daily-backup-task.ps1','install-monthly-recovery-drill-task.ps1','install-etp-automation-task.ps1','remove-etp-scheduled-tasks.ps1','invoke-monthly-recovery-drill-runner.ps1','invoke-etp-recovery-drill.ps1','new-etp-support-package.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination $packagedScripts -Force
}

$executable = Join-Path $output "Etp.Reporting.Desktop.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Published executable was not produced." }
$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
"$($hash.Hash)  $($hash.Path | Split-Path -Leaf)" | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS.txt") -Encoding ascii
@{
    product = "ETP Reporting Engine"
    version = $Version
    runtime = $Runtime
    builtUtc = [DateTime]::UtcNow.ToString("o")
    commit = (git -C $repoRoot rev-parse --short=12 HEAD 2>$null)
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output "release.json") -Encoding utf8
Write-Host "Windows release created at $output"
