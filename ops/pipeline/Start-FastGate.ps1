# Fast gate (architecture A2, 12 Oct 2026): the everyday check a lane or the integrator runs before a note or a merge. Target: 6 minutes.
# UNELEVATED, touches no SQL Server, does NOT pause lane builds. It runs under the heavy-job lock (Invoke-Serialized.ps1) so it never overlaps
# a full gate or another build. The full elevated gate (Start-Gate.ps1) is for release candidates only.
#
#   & 'E:\ETP\Migration 2026-10-04\Start-FastGate.ps1' -Worktree 'E:\ETP\Code\Worktrees\w112-a2-gate'
#
# Steps: 1 Release build of the solution; 2 every unit test project (tests-dotnet\*Tests.csproj except *IntegrationTests) IN PARALLEL with
# --filter "Category!=Sql" (a unit test that needs SQL carries [Trait("Category","Sql")] and belongs to the full gate) and trx + full logs;
# 3 PowerShell parse check of scripts\**\*.ps1 and scripts\onedrive\tests\Test-OneDriveScripts.ps1. Every step has a hard timeout and writes
# files, never a pipe (GateCommon.ps1). The last line, pass or fail, is:  gate: fast <label> <PASSED|FAILED|STOPPED> ...
# Results: E:\ETP\Reference\FastGate\<label>-<timestamp>\
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Worktree,
    [string]$Label = '',
    [string]$ToolsDirectory = 'E:\ETP\Code\Worktrees\_tools',
    [string]$ReferenceRoot = 'E:\ETP\Reference',
    [string]$PowerShellExe = 'E:\Tools\PowerShell\7\pwsh.exe',
    [int]$WaitMinutes = 30,
    [int]$BuildMinutes = 15,
    [int]$TestMinutes = 15,
    [string]$ResultsDirectory,
    [switch]$Inner
)
. (Join-Path $PSScriptRoot 'GateCommon.ps1')
if (-not $Label) { $Label = Split-Path -Leaf $Worktree }
if (-not $Inner) {
    $ErrorActionPreference = 'Stop'
    if (-not (Test-Path -LiteralPath $Worktree)) { throw "Worktree not found: $Worktree" }
    $results = Join-Path $ReferenceRoot ("FastGate\$Label-" + (Get-Date -Format 'yyyy-MM-dd-HHmmss'))
    New-Item -ItemType Directory -Force -Path $results | Out-Null
    $self = $MyInvocation.MyCommand.Path
    $argText = "-NoProfile -File `"$(Join-Path $ToolsDirectory 'Invoke-Serialized.ps1')`" -Label fastgate-$Label -WaitMinutes $WaitMinutes -WorkingDirectory `"$Worktree`" " +
        "-Command `"& '$self' -Inner -Worktree '$Worktree' -Label '$Label' -ReferenceRoot '$ReferenceRoot' -ResultsDirectory '$results' -BuildMinutes $BuildMinutes -TestMinutes $TestMinutes`""
    $step = Invoke-GateStep -Name "fastgate-$Label" -FilePath $PowerShellExe -ArgumentList @() -RawArguments $argText -TimeoutMinutes ($WaitMinutes + $BuildMinutes + $TestMinutes + 25) -WorkingDirectory $Worktree
    $resultFile = Join-Path $results 'gate-result.txt'
    if (-not (Test-Path $resultFile)) {
        Write-GateResultLine -Line "gate: fast $Label $(if ($step.TimedOut) { 'TIMED-OUT' } else { 'STOPPED' }) exit=$($step.ExitCode) (the inner gate wrote no result; exit 75 means the heavy-job lock stayed busy) results=$results" -Path $resultFile
    }
    exit $step.ExitCode
}

# ---------------------------------------------------------------- inner (holds the heavy-job lock)
$ErrorActionPreference = 'Stop'
$r = $ResultsDirectory
New-Item -ItemType Directory -Force -Path $r | Out-Null
$resultFile = Join-Path $r 'gate-result.txt'
Start-Transcript -Path (Join-Path $r 'fastgate-run.log') | Out-Null
$start = Get-Date
$exit = 1; $outcome = 'STOPPED'; $why = ''
$notes = New-Object System.Collections.Generic.List[string]
try {
    Set-Location $Worktree
    git rev-parse HEAD | Out-File (Join-Path $r 'sha.txt')
    Write-Host "== Branch: $(git rev-parse --short HEAD) $(git log -1 --format=%s)"
    $dotnet = Get-DotnetPath
    $env:ETP_FLAKE_LOG_DIRECTORY = $r
    $env:ETP_DIAGNOSTICS_DIRECTORY = Join-Path $r 'diagnostics'   # test runs must never write to a real diagnostics log

    Write-Host '== 1. Build (Release)'
    $build = Invoke-GateStep -Name 'build' -FilePath $dotnet -ArgumentList @('build', 'Etp.Reporting.slnx', '-c', 'Release', '-m:2', '-nodeReuse:false') -LogPath (Join-Path $r 'build.log') -TimeoutMinutes $BuildMinutes -WorkingDirectory $Worktree
    Get-Content -LiteralPath $build.LogPath -Tail 4 | ForEach-Object { Write-Host "     $_" }
    $notes.Add("build=$($build.ExitCode)")
    if ($build.ExitCode) { throw "Build failed or timed out ($($build.ExitCode))" }

    Write-Host '== 2. Unit tests in parallel (no SQL)'
    $projects = ([xml](Get-Content Etp.Reporting.slnx)).SelectNodes('//Project/@Path') | ForEach-Object Value |
        Where-Object { $_ -like 'tests-dotnet/*Tests.csproj' -and $_ -notlike '*IntegrationTests*' }
    $running = @()
    foreach ($p in $projects) {
        $n = [IO.Path]::GetFileNameWithoutExtension($p)
        $log = Join-Path $r "$n.console.log"
        $args2 = @('test', $p, '-c', 'Release', '--no-build', '--filter', 'Category!=Sql', '--logger', "trx;LogFileName=$n.trx", '--logger', 'console;verbosity=normal',
            '--results-directory', $r, '--blame-hang-timeout', '10m', '--blame-hang-dump-type', 'none')
        $running += [pscustomobject]@{ Name = $n; Log = $log; Job = Start-ThreadJob -Name $n -ScriptBlock {
                param($common, $dotnet, $n, $args2, $log, $minutes, $wd)
                . $common
                Invoke-GateStep -Name $n -FilePath $dotnet -ArgumentList $args2 -LogPath $log -TimeoutMinutes $minutes -WorkingDirectory $wd
            } -ArgumentList (Join-Path $PSScriptRoot 'GateCommon.ps1'), $dotnet, $n, $args2, $log, $TestMinutes, $Worktree }
    }
    $fail = 0
    foreach ($j in $running) {
        $step = Receive-Job -Job $j.Job -Wait -AutoRemoveJob | Where-Object { $_ -is [psobject] -and $_.PSObject.Properties['ExitCode'] } | Select-Object -Last 1
        $code = if ($step) { $step.ExitCode } else { 1 }
        Write-Host "  -- $($j.Name): exit $code$(if ($step -and $step.TimedOut) { ' (TIMED OUT)' })"
        Select-String -LiteralPath $j.Log -Pattern '^(Passed!|Failed!|Total tests|\s+Passed:|\s+Failed:|\s+Skipped:)' -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "     $($_.Line.Trim())" }
        $notes.Add("$($j.Name)=$code")
        if ($code) { $fail = 1 }
    }

    Write-Host '== 3. PowerShell parse check and OneDrive script self-test'
    $parseErrors = @()
    Get-ChildItem -LiteralPath (Join-Path $Worktree 'scripts') -Filter '*.ps1' -File -Recurse | ForEach-Object {
        $tokens = $null; $errs = $null
        [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errs) | Out-Null
        foreach ($e in $errs) { $parseErrors += "$($_.FullName): $($e.Message)" }
    }
    $parseErrors | ForEach-Object { Write-Host "     PARSE ERROR $_" }
    $notes.Add("parse=$($parseErrors.Count)")
    if ($parseErrors.Count) { $fail = 1 }
    $od = Join-Path $Worktree 'scripts\onedrive\tests\Test-OneDriveScripts.ps1'
    if (Test-Path -LiteralPath $od) {
        # The script's own header says plain Windows PowerShell 5.1. PowerShell 7's PSModulePath must not reach it, or 5.1 loads
        # the 7.x Microsoft.PowerShell.Security and Get-Acl fails to autoload (integration 1.12, 13 Oct 2026).
        $savedModulePath = $env:PSModulePath
        Remove-Item Env:PSModulePath -ErrorAction SilentlyContinue
        try {
            $odStep = Invoke-GateStep -Name 'onedrive-scripts' -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $od) -LogPath (Join-Path $r 'Test-OneDriveScripts.console.log') -TimeoutMinutes 10 -WorkingDirectory $Worktree
        }
        finally { $env:PSModulePath = $savedModulePath }
        Write-Host "  -- onedrive scripts: exit $($odStep.ExitCode)"
        $notes.Add("onedrive=$($odStep.ExitCode)")
        if ($odStep.ExitCode) { $fail = 1 }
    }
    $exit = $fail
    $outcome = if ($fail) { 'FAILED' } else { 'PASSED' }
}
catch { $why = $_.Exception.Message; Write-Host "STOPPED: $why" }
finally {
    try {
        $counts = Get-TrxCounts -Directory $r
        $sha = try { (Get-Content (Join-Path $r 'sha.txt') -ErrorAction Stop | Select-Object -First 1).Substring(0, 7) } catch { 'unknown' }
        $line = "gate: fast $Label $outcome exit=$exit tests=$($counts.Total) passed=$($counts.Passed) failed=$($counts.Failed) skipped=$($counts.Skipped) trx=$($counts.Files) sha=$sha " +
            "minutes=$([math]::Round(((Get-Date) - $start).TotalMinutes, 1)) steps=[$($notes -join ' ')]" + $(if ($why) { " note=`"$why`"" } else { '' }) + " results=$r"
        Write-GateResultLine -Line $line -Path $resultFile
    }
    catch { Write-Host "gate: fast $Label $outcome exit=$exit (could not summarise: $($_.Exception.Message)) results=$r" }
    Stop-Transcript | Out-Null
}
exit $exit
