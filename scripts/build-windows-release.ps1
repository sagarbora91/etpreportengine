param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "artifacts/windows-release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "Etp.Reporting.slnx"
$desktopProject = Join-Path $repoRoot "src/Etp.Reporting.Desktop/Etp.Reporting.Desktop.csproj"
$output = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))

dotnet restore $solution
dotnet build $solution -c $Configuration --no-restore
dotnet test $solution -c $Configuration --no-build
dotnet publish $desktopProject -c $Configuration -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $output

$executable = Join-Path $output "Etp.Reporting.Desktop.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Published executable was not produced." }
$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
"$($hash.Hash)  $($hash.Path | Split-Path -Leaf)" | Set-Content -LiteralPath (Join-Path $output "SHA256SUMS.txt") -Encoding ascii
Write-Host "Windows release created at $output"
