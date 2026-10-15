# 1.12.0 WAVE 2 copy (integrator, 13 Oct 2026): same as the wave-1 script, pointed at the integrate/112-w2 worktree.
# Build the ETP 1.12.0 installer (runbook: INSTALLER-BUILD-RUNBOOK.md). Run ELEVATED in pwsh 7: the release build runs every
# test project, including the SQL integration tests, which need CREATE DATABASE. Unsigned on purpose (A4.7 -> Phase 8).
$worktree = 'E:\ETP\Code\Worktrees\integrate-112-w2'
$cmd = @'
$sha = (git rev-parse --short=7 HEAD).Trim()
$tag = "$sha-w2b1"
$log = "E:\ETP\Migration 2026-10-04\installer-build-1.12.0-$tag.log"
try {
    & .\scripts\build-windows-installer.ps1 `
        -ReleaseDirectory "artifacts\windows-release-$tag" `
        -OutputDirectory  "artifacts\installer-$tag" `
        -SqlPayloadDirectory 'E:\ETP\Installers\SQL payload for the ETP installer (2025 + ODBC 17)' `
        -InnoSetupCompiler   'E:\Tools\Inno Setup 6' *>&1 | Tee-Object -FilePath $log
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
} catch { ('FAILED: ' + $_.Exception.Message) | Tee-Object -FilePath $log -Append; exit 1 }
'@
& E:\Tools\PowerShell\7\pwsh.exe -NoProfile -File E:\ETP\Code\Worktrees\_tools\Invoke-Serialized.ps1 -WorkingDirectory $worktree -Label installer-1.12.0-w2 -WaitMinutes 90 -Command $cmd
"Exit code: $LASTEXITCODE" | Tee-Object -FilePath 'E:\ETP\Migration 2026-10-04\installer-build-1.12.0-w2-exit.txt'





