# Rebuild the installer's SQL payload with a current cumulative update (Dependency 4, 12 Oct 2026). Downloads nothing.
# SQL Server 2025 RTM crashed once (error 3449); bootstrap now applies SQLServer2025-KB*-x64.exe from the payload right after the engine
# install (Install-EtpSqlCumulativeUpdateFromPayload). This script builds the NEW payload folder beside the old one; the old folder is untouched.
#
# SAGAR - the media step (no code can do this for you):
#   1. Download the SQL Server 2025 cumulative update installer (CU9 = SQLServer2025-KB5122048-x64.exe, the one already used on Workpc) from the
#      Microsoft KB page / Update Catalog, and note the SHA-256 printed on the KB page.
#   2. Run (any PC, no elevation needed):
#        & 'E:\ETP\Migration 2026-10-04\Prepare-SqlPayload.ps1' -CuPath '<downloaded CU .exe>' -ExpectedSha256 '<hash from the KB page>'
#   3. Build the installer with  Build-Installer.ps1 -Version <v> -Worktree <wt> -SqlPayloadDirectory '<the new folder this script prints>'
#   4. VM install gate (required before shipping): install the new installer on a clean VM with the SQL option ticked, then check
#        SELECT @@VERSION  (expect the CU build, 17.0.5xxx, not 17.0.1000.7)  and run scripts\Test-EtpRestoredPc.ps1.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CuPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ExpectedSha256,
    [string]$SourcePayload = 'E:\ETP\Installers\SQL payload for the ETP installer (2025 + ODBC 17)',
    [string]$TargetPayload = 'E:\ETP\Installers\SQL payload for the ETP installer (2025 CU + ODBC 17)'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $CuPath -PathType Leaf)) { throw "CU file not found: $CuPath" }
if ((Split-Path $CuPath -Leaf) -notmatch '^SQLServer2025-KB\d+-x64\.exe$') { throw 'Expected a file named like SQLServer2025-KB5122048-x64.exe (bootstrap looks for that pattern).' }
$hash = (Get-FileHash -LiteralPath $CuPath -Algorithm SHA256).Hash
if ($hash -ne $ExpectedSha256.ToUpperInvariant()) { throw "SHA-256 mismatch. File: $hash  Expected: $ExpectedSha256. Not used." }
$sig = Get-AuthenticodeSignature -LiteralPath $CuPath
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw "Signature is not a valid Microsoft one (status $($sig.Status)). Not used." }
if (-not (Test-Path -LiteralPath $SourcePayload -PathType Container)) { throw "Source payload not found: $SourcePayload" }
if (Test-Path -LiteralPath $TargetPayload) { throw "Target already exists, choose another or remove it yourself: $TargetPayload" }
New-Item -ItemType Directory -Path $TargetPayload | Out-Null
Get-ChildItem -LiteralPath $SourcePayload -File | Where-Object Name -ne 'README.txt' | Copy-Item -Destination $TargetPayload
Copy-Item -LiteralPath $CuPath -Destination $TargetPayload
foreach ($need in 'SQLEXPR_x64_ENU.exe', 'MsSqlCmdLnUtils.msi', 'msodbcsql17.msi', 'msodbcsql.msi') {
    if (-not (Test-Path (Join-Path $TargetPayload $need))) { throw "Payload is missing $need." }
}
@"
SQL payload with cumulative update, prepared $(Get-Date -Format 'yyyy-MM-dd') by Prepare-SqlPayload.ps1.
Base files copied unchanged from: $SourcePayload (SQLEXPR RTM 17.0.1000.7, ODBC 17/18, Sqlcmd 15, LocalDB).
Cumulative update: $(Split-Path $CuPath -Leaf), SHA-256 $hash, valid Microsoft signature. Setup applies it after the engine install.
"@ | Set-Content (Join-Path $TargetPayload 'README.txt')
Write-Host "New payload folder: $TargetPayload"
