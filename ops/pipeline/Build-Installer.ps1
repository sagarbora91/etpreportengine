# Parameterised installer build (replaces Build-Installer-<ver>.ps1; Arch 14, 12 Oct 2026). Run ELEVATED in pwsh 7: the release build
# runs every test project, including the SQL integration tests, which need CREATE DATABASE. Unsigned on purpose (decision 39).
#   & 'E:\ETP\Migration 2026-10-04\Build-Installer.ps1' -Version 1.11.2 -Worktree 'E:\ETP\Code\Worktrees\release-1.11.2'
# The SQL payload folder must hold the current media (see Prepare-SqlPayload.ps1); without it the build refuses unless -NoSqlPayload.
# Writes installer-build-<Version>-exit.txt ("Exit code: N") beside this script; the build log is installer-build-<Version>-<tag>.log.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Worktree,
    [string]$SqlPayloadDirectory = 'E:\ETP\Installers\SQL payload for the ETP installer (2025 + ODBC 17)',
    [switch]$NoSqlPayload,
    [string]$InnoSetupCompiler = 'E:\Tools\Inno Setup 6',
    [string]$ToolsDirectory = 'E:\ETP\Code\Worktrees\_tools',
    [string]$PowerShellExe = 'E:\Tools\PowerShell\7\pwsh.exe',
    [int]$WaitMinutes = 90
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Worktree)) { throw "Worktree not found: $Worktree" }
$exitFile = Join-Path $PSScriptRoot "installer-build-$Version-exit.txt"
Remove-Item $exitFile -ErrorAction SilentlyContinue
$payloadArg = ''
if (-not $NoSqlPayload) {
    if (-not (Test-Path -LiteralPath $SqlPayloadDirectory -PathType Container)) { throw "SQL payload folder not found: $SqlPayloadDirectory (run Prepare-SqlPayload.ps1, or pass -NoSqlPayload)." }
    $payloadArg = "-SqlPayloadDirectory '$SqlPayloadDirectory'"
}
$log = Join-Path $PSScriptRoot "installer-build-$Version"
$cmd = @"
`$sha = (git rev-parse --short=7 HEAD).Trim()
`$tag = "`$sha-b1"
`$log = "$log-`$tag.log"
try {
    & .\scripts\build-windows-installer.ps1 ``
        -ReleaseDirectory "artifacts\windows-release-`$tag" ``
        -OutputDirectory  "artifacts\installer-`$tag" ``
        $payloadArg ``
        -InnoSetupCompiler '$InnoSetupCompiler' *>&1 | Tee-Object -FilePath `$log
    if (`$LASTEXITCODE) { exit `$LASTEXITCODE }
} catch { ('FAILED: ' + `$_.Exception.Message) | Tee-Object -FilePath `$log -Append; exit 1 }
"@
& $PowerShellExe -NoProfile -File (Join-Path $ToolsDirectory 'Invoke-Serialized.ps1') -WorkingDirectory $Worktree -Label "installer-$Version" -WaitMinutes $WaitMinutes -Command $cmd
"Exit code: $LASTEXITCODE" | Tee-Object -FilePath $exitFile
exit $LASTEXITCODE
