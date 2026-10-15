# 1.12.0 WAVE 2 copy (integrator, 13 Oct 2026): same as the wave-1 script, pointed at the integrate/112-w2 worktree.
# 1.12.0 gate preparation (wave-1 integrator, 13 Oct 2026). Runs ELEVATED (CREATE DATABASE), under the heavy-job lock, called by
# Run-Gate-1.12.0.ps1 - do not start it on its own. Never touches EtpReporting: every database here is a throwaway EtpPhase0Test_*.
#   1. Release build.
#   2. Objects baseline (lane A3): ETP_EXPORT_OBJECTS_BASELINE=<worktree>\database\objects and ONLY the test
#      SchemaObjectRunnerSqlTests.Objects_baseline_export_runs_only_when_the_integrator_asks_for_it: a migrations-only scratch
#      database, Export-EtpSchemaObjects.ps1 into the repository folder (existing files, e.g. D2's six, are never overwritten),
#      database dropped. Committed as its own commit "objects baseline".
#   3. Rebuild (the integration tests' bin now carries the objects), then ExpectedRolePermissionsTests with
#      ETP_REGENERATE_EXPECTED_PERMISSIONS=1: the JSON is rewritten from a fresh fixture database (decision-tagged entries kept).
#      Committed as "Regenerate ExpectedRolePermissions.json".
#   4. Rebuild (the JSON is an embedded resource) and prove it: ExpectedRolePermissionsTests without the variable, and the
#      unit guards SchemaObjectGuardTests (each GRANTS block equals its JSON rows) must pass.
# Every step writes to files (GateCommon.ps1 Invoke-GateStep: timeout, whole process tree killed), never through a pipe.
# Results: E:\ETP\Reference\Gate-1.12.0-prep-<time>\ ; last line in prepare-result.txt:  prepare: 1.12.0 <PASSED|FAILED> ...
[CmdletBinding()]
param(
    [string]$Worktree = 'E:\ETP\Code\Worktrees\integrate-112-w2',
    [string]$ReferenceRoot = 'E:\ETP\Reference'
)
. (Join-Path $PSScriptRoot 'GateCommon.ps1')
$ErrorActionPreference = 'Stop'
$r = Join-Path $ReferenceRoot ('Gate-1.12.0-w2-prep-' + (Get-Date -Format 'yyyy-MM-dd-HHmm'))
New-Item -ItemType Directory -Force -Path $r | Out-Null
$resultFile = Join-Path $r 'prepare-result.txt'
Start-Transcript -Path (Join-Path $r 'prepare-run.log') | Out-Null
$exit = 1; $outcome = 'STOPPED'; $why = ''
$notes = New-Object System.Collections.Generic.List[string]
$coAuthor = 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
$integration = 'tests-dotnet/Etp.Reporting.SqlServer.IntegrationTests/Etp.Reporting.SqlServer.IntegrationTests.csproj'
$unit = 'tests-dotnet/Etp.Reporting.SqlServer.Tests/Etp.Reporting.SqlServer.Tests.csproj'

function Invoke-Build([string]$name) {
    $step = Invoke-GateStep -Name $name -FilePath $script:dotnet -ArgumentList @('build', 'Etp.Reporting.slnx', '-c', 'Release', '-m:1', '-nodeReuse:false') `
        -LogPath (Join-Path $r "$name.log") -TimeoutMinutes 25 -WorkingDirectory $Worktree
    Get-Content -LiteralPath $step.LogPath -Tail 3 | ForEach-Object { Write-Host "     $_" }
    $notes.Add("$name=$($step.ExitCode)")
    if ($step.ExitCode) { throw "$name failed ($($step.ExitCode))" }
}
function Invoke-Filtered([string]$name, [string]$project, [string]$filter, [int]$minutes) {
    $step = Invoke-GateStep -Name $name -FilePath $script:dotnet -TimeoutMinutes $minutes -WorkingDirectory $Worktree -LogPath (Join-Path $r "$name.console.log") -ArgumentList @(
        'test', $project, '-c', 'Release', '--no-build', '-m:1', '--filter', $filter, '--logger', "trx;LogFileName=$name.trx",
        '--logger', 'console;verbosity=normal', '--results-directory', $r, '--blame-hang-timeout', '20m', '--blame-hang-dump-type', 'none')
    Select-String -LiteralPath $step.LogPath -Pattern '^(Passed!|Failed!|Total tests|\s+Passed:|\s+Failed:|\s+Skipped:)' | ForEach-Object { Write-Host "     $($_.Line.Trim())" }
    $notes.Add("$name=$($step.ExitCode)")
    return $step.ExitCode
}
function Invoke-Git([string[]]$arguments) {
    $out = & git -C $Worktree @arguments 2>&1
    if ($LASTEXITCODE) { throw "git $($arguments -join ' ') failed: $out" }
    return $out
}

try {
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Not elevated.' }
    if (Get-Process -Name 'Etp.Reporting.Desktop' -ErrorAction SilentlyContinue) { throw 'The ETP app is running; close it first.' }
    if ((Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')) -ne 'integrate/112-w2') { throw "The worktree is not on integrate/112-w2." }
    if (Invoke-Git @('status', '--porcelain')) { throw 'The integration worktree has uncommitted changes.' }
    Write-Host "== Branch: $(Invoke-Git @('rev-parse', '--short', 'HEAD')) $(Invoke-Git @('log', '-1', '--format=%s'))"
    $script:dotnet = Get-DotnetPath
    $env:ETP_FLAKE_LOG_DIRECTORY = $r
    $env:ETP_DIAGNOSTICS_DIRECTORY = Join-Path $r 'diagnostics'
    Remove-Item Env:ETP_EXPORT_OBJECTS_BASELINE, Env:ETP_REGENERATE_EXPECTED_PERMISSIONS -ErrorAction SilentlyContinue

    Write-Host '== 1. Build'
    Invoke-Build 'build-1'

    Write-Host '== 2. Objects baseline'
    $objects = Join-Path $Worktree 'database\objects'
    $before = @(Get-ChildItem -LiteralPath $objects -Filter '*.sql' -File -Recurse).Count
    $env:ETP_EXPORT_OBJECTS_BASELINE = $objects
    try { $code = Invoke-Filtered 'objects-baseline' $integration 'FullyQualifiedName~SchemaObjectRunnerSqlTests.Objects_baseline_export_runs_only_when_the_integrator_asks_for_it' 30 }
    finally { Remove-Item Env:ETP_EXPORT_OBJECTS_BASELINE -ErrorAction SilentlyContinue }
    if ($code) { throw "The objects baseline export failed ($code); see objects-baseline.console.log." }
    $after = @(Get-ChildItem -LiteralPath $objects -Filter '*.sql' -File -Recurse).Count
    Write-Host "     object files: $before -> $after"
    $changed = @(Invoke-Git @('status', '--porcelain', '--', 'database/objects'))
    if (@(Invoke-Git @('diff', '--name-only', '--', 'database/objects/*.sql')).Count) { throw 'The export changed an existing object file (it must never overwrite); nothing committed.' }
    if ($changed.Count) {
        Invoke-Git @('add', '--', 'database/objects') | Out-Null
        Invoke-Git @('commit', '-q', '-m', "objects baseline`n`nScratch database migrated 0001-0069 (objects step off), Export-EtpSchemaObjects.ps1 into database/objects ($before -> $after files; existing files kept), plus the generated EXCLUDED.md section.`n`n$coAuthor") | Out-Null
        Write-Host "     committed: $(Invoke-Git @('log', '-1', '--format=%h %s'))"
        $notes.Add("baseline-files=$after")
    }
    else { Write-Host '     nothing new (baseline already committed)'; $notes.Add('baseline=unchanged') }

    Write-Host '== 3. Regenerate ExpectedRolePermissions.json'
    Invoke-Build 'build-2'
    $env:ETP_REGENERATE_EXPECTED_PERMISSIONS = '1'
    try { $code = Invoke-Filtered 'permissions-regenerate' $integration 'FullyQualifiedName~ExpectedRolePermissionsTests' 30 }
    finally { Remove-Item Env:ETP_REGENERATE_EXPECTED_PERMISSIONS -ErrorAction SilentlyContinue }
    $json = 'src/Etp.Reporting.Infrastructure.SqlServer/HealthCheck/ExpectedRolePermissions.json'
    if (@(Invoke-Git @('status', '--porcelain', '--', $json)).Count) {
        Invoke-Git @('diff', '--stat', '--', $json) | ForEach-Object { Write-Host "     $_" }
        Invoke-Git @('diff', '--', $json) | Set-Content -LiteralPath (Join-Path $r 'ExpectedRolePermissions.diff')
        Invoke-Git @('add', '--', $json) | Out-Null
        Invoke-Git @('commit', '-q', '-m', "Regenerate ExpectedRolePermissions.json (1.12.0 wave 2: 0068 no-sale days, 0069 import_source_locations, no-sale object GRANTS)`n`n$coAuthor") | Out-Null
        Write-Host "     committed: $(Invoke-Git @('log', '-1', '--format=%h %s'))"
        $notes.Add('permissions=regenerated')
    }
    elseif ($code) { throw "ExpectedRolePermissionsTests failed ($code) without regenerating the file; see permissions-regenerate.console.log." }
    else { Write-Host '     already up to date'; $notes.Add('permissions=unchanged') }
    if (Invoke-Git @('status', '--porcelain')) { throw 'Something other than the baseline and the JSON changed in the worktree; see git status.' }

    Write-Host '== 4. Prove it'
    Invoke-Build 'build-3'
    $failed = 0
    if (Invoke-Filtered 'permissions-check' $integration 'FullyQualifiedName~ExpectedRolePermissionsTests' 30) { $failed = 1 }
    if (Invoke-Filtered 'object-guards' $unit 'FullyQualifiedName~SchemaObjectGuardTests' 15) { $failed = 1 }
    if ($failed) { throw 'The permission check or the object guards still fail after regeneration; see the logs.' }

    $exit = 0; $outcome = 'PASSED'
}
catch { $why = $_.Exception.Message; Write-Host "STOPPED: $why"; $outcome = 'FAILED' }
finally {
    $sha = try { (& git -C $Worktree rev-parse --short HEAD) } catch { 'unknown' }
    Write-GateResultLine -Line ("prepare: 1.12.0-w2 $outcome exit=$exit sha=$sha steps=[$($notes -join ' ')]" + $(if ($why) { " note=`"$why`"" } else { '' }) + " results=$r") -Path $resultFile
    Stop-Transcript | Out-Null
}
exit $exit
