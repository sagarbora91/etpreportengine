<#
.SYNOPSIS
Lane V1 (code-protection plan 4.6). Times the PROTECTED and UNPROTECTED publishes of the same commit on four measures and applies the
hard rule: more than 5 % slower on any measure = drop string hiding from the slow assembly; more than 10 % = withhold obfuscation
from this release. Writes performance-<label>.json. Refuses fewer than 3 runs.

.DESCRIPTION
Run by Sagar, ELEVATED, in a desktop session (the cold-start measure needs a window), never during the gate. Same scratch-database rules
as Test-EtpProtectedBuild.ps1 (EtpAccept_* only, <Database>_U / <Database>_P, refuses if they exist, drops only what it created).

Measures, medians of -Runs interleaved runs (unprotected, protected, unprotected, ...):
  coldStartMs  new process of Etp.Reporting.Desktop.exe --connection-string <scratch> until its main window handle appears
               (title contains 'ETP Reporting Engine'); the process is killed afterwards. Not a true cold boot: the OS file cache stays
               warm after the first run; the first run of each build is reported separately in the samples as run 1.
  importMs     FolderImportService over the fixture folder into a freshly created scratch database (the probe host of Test-EtpProtectedBuild)
  reportMs     DailyReportingPackService.GenerateCombinedAsync for the business date (the DSR and sales report data, rendered by SQL + C#)
  exportMs     OpenXml workbook + PDF export of that pack (excelMs + pdfMs)
Not measured here: ReadyToRun (optional separate comparison, off by default; build a third folder and run this script with it as -Protected).

Noise rule: a difference smaller than -NoiseFloorMs (default 50 ms) counts as equal, so a 5 ms measure cannot trigger a withhold.

Exit codes: 0 within limits; 4 a measure is over 5 %; 5 a measure is over 10 %; 2 refused or bad usage; 3 incomplete.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\Measure-EtpPerformance.ps1 -Protected <dir> -Unprotected <dir>-unprotected -FixtureFolder tests-dotnet\fixtures\etp-sample -Database EtpAccept_V1perf -Label 1.13.0-rc1
#>
[CmdletBinding()]
param(
    [string]$Protected,
    [string]$Unprotected,
    [string]$FixtureFolder,
    [string]$Database,
    [string]$Server = '.\SQLEXPRESS',
    [int]$Runs = 3,
    [string]$Label,
    [string]$OutputFolder,
    [int]$NoiseFloorMs = 50,
    [int]$StartupTimeoutSeconds = 60,
    [switch]$KeepDatabases,
    [switch]$LoadFunctionsOnly
)
$ErrorActionPreference = 'Stop'
# Dot-sourcing binds the sibling's parameters in THIS scope and would overwrite ours, so save and restore them.
$savedParameters = @{
    Protected = $Protected; Unprotected = $Unprotected; FixtureFolder = $FixtureFolder; Database = $Database; Server = $Server
    OutputFolder = $OutputFolder; KeepDatabases = $KeepDatabases; LoadFunctionsOnly = $LoadFunctionsOnly
}
. (Join-Path $PSScriptRoot 'Test-EtpProtectedBuild.ps1') -LoadFunctionsOnly
$Protected = $savedParameters.Protected; $Unprotected = $savedParameters.Unprotected; $FixtureFolder = $savedParameters.FixtureFolder
$Database = $savedParameters.Database; $Server = $savedParameters.Server; $OutputFolder = $savedParameters.OutputFolder
$KeepDatabases = $savedParameters.KeepDatabases; $LoadFunctionsOnly = $savedParameters.LoadFunctionsOnly

$Script:WarnPercent = 5.0
$Script:WithholdPercent = 10.0
$Script:MinimumRuns = 3

function Assert-EtpRunCount([int]$Count) {
    if ($Count -lt $Script:MinimumRuns) { Stop-EtpRefused "At least $($Script:MinimumRuns) runs are required (got $Count): a median of fewer than three samples is noise." }
}

function Get-EtpMedian([double[]]$Values) {
    if ($null -eq $Values -or $Values.Count -eq 0) { throw 'No samples.' }
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

# 'OK', 'DROP-STRING-HIDING' (> 5 %) or 'WITHHOLD-OBFUSCATION' (> 10 %). Differences under the noise floor are equal.
function Get-EtpPerformanceVerdict([double]$UnprotectedMs, [double]$ProtectedMs, [double]$NoiseFloorMilliseconds) {
    $delta = $ProtectedMs - $UnprotectedMs
    if ($delta -le $NoiseFloorMilliseconds) { return 'OK' }
    if ($UnprotectedMs -le 0) { return 'OK' }
    $percent = $delta / $UnprotectedMs * 100.0
    if ($percent -gt $Script:WithholdPercent) { return 'WITHHOLD-OBFUSCATION' }
    if ($percent -gt $Script:WarnPercent) { return 'DROP-STRING-HIDING' }
    return 'OK'
}

function Measure-EtpColdStart([string]$BuildFolder, [string]$ConnectionArgument, [int]$TimeoutSeconds, [string]$DiagnosticsFolder) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path $BuildFolder 'Etp.Reporting.Desktop.exe'
    $info.Arguments = '--connection-string "{0}"' -f $ConnectionArgument
    $info.WorkingDirectory = $BuildFolder; $info.UseShellExecute = $false
    $info.EnvironmentVariables['ETP_DIAGNOSTICS_DIRECTORY'] = $DiagnosticsFolder
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            Start-Sleep -Milliseconds 20
            $process.Refresh()
            if ($process.HasExited) { throw "The application exited during startup with code $($process.ExitCode)." }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -like '*ETP Reporting Engine*') { return [double]$clock.ElapsedMilliseconds }
        }
        throw "No main window within $TimeoutSeconds s (a desktop session is required)."
    } finally {
        try { if (-not $process.HasExited) { $process.Kill() } } catch { }
        try { [void]$process.WaitForExit(10000) } catch { }
    }
}

function Invoke-EtpTimingRun([string]$Label, [string]$BuildFolder, [string]$DatabaseName, [string]$Instance, [string]$HostDll, [string]$Fixture, [string]$WorkFolder) {
    Remove-EtpScratchDatabase $Instance $DatabaseName
    $cs = Get-EtpConnectionStringArgument $Instance $DatabaseName
    $diagnostics = Join-Path $WorkFolder 'diagnostics'
    [void](New-Item -ItemType Directory -Path $diagnostics -Force)
    $exe = Join-Path $BuildFolder 'Etp.Reporting.Desktop.exe'
    $init = Start-EtpProcess $exe ('--initialize-database --connection-string "{0}"' -f $cs) @{ ETP_DIAGNOSTICS_DIRECTORY = $diagnostics } 600 $BuildFolder
    if ($init.ExitCode -ne 0) { throw "$Label --initialize-database exited $($init.ExitCode): $($init.Stderr)" }
    $cold = Measure-EtpColdStart $BuildFolder $cs $StartupTimeoutSeconds $diagnostics
    $out = Join-Path $WorkFolder ('probe-' + [Guid]::NewGuid().ToString('N'))
    $arguments = '"{0}" --build-dir "{1}" --connection-string "{2}" --fixture "{3}" --out "{4}"' -f $HostDll, $BuildFolder, $cs, $Fixture, $out
    $probe = Start-EtpProcess 'dotnet' $arguments @{ DOTNET_CLI_TELEMETRY_OPTOUT = '1' } 3600 $BuildFolder
    if ($probe.ExitCode -ne 0) { throw "$Label probe host exited $($probe.ExitCode): $($probe.Stderr)" }
    $t = Get-Content -LiteralPath (Join-Path $out 'timings.json') -Raw | ConvertFrom-Json
    return [pscustomobject]@{ coldStartMs = $cold; importMs = [double]$t.importMs; reportMs = [double]$t.packMs; exportMs = [double]$t.excelMs + [double]$t.pdfMs }
}

function New-EtpPerformanceResult($Samples, [int]$NoiseFloor) {
    $measures = @()
    foreach ($name in 'coldStartMs', 'importMs', 'reportMs', 'exportMs') {
        $u = @($Samples.Unprotected | ForEach-Object { [double]$_.$name })
        $p = @($Samples.Protected | ForEach-Object { [double]$_.$name })
        $um = Get-EtpMedian $u; $pm = Get-EtpMedian $p
        $percent = if ($um -gt 0) { [Math]::Round(($pm - $um) / $um * 100.0, 2) } else { 0.0 }
        $measures += [pscustomobject]@{
            measure = $name; unprotectedMedianMs = [Math]::Round($um, 1); protectedMedianMs = [Math]::Round($pm, 1); percentSlower = $percent
            verdict = (Get-EtpPerformanceVerdict $um $pm $NoiseFloor); unprotectedSamples = $u; protectedSamples = $p
        }
    }
    return $measures
}

if ($LoadFunctionsOnly) { return }

$exitCode = 2
$created = New-Object System.Collections.ArrayList
try {
    if ([string]::IsNullOrWhiteSpace($Protected) -or [string]::IsNullOrWhiteSpace($Unprotected) -or [string]::IsNullOrWhiteSpace($FixtureFolder)) {
        Stop-EtpRefused 'Provide -Protected, -Unprotected, -FixtureFolder and -Database (see Get-Help).'
    }
    Assert-EtpRunCount $Runs
    Assert-EtpScratchDatabase $Database
    Assert-EtpLocalServer $Server
    if (-not (Test-EtpElevated)) { Stop-EtpRefused 'Run this elevated (it creates and drops the EtpAccept_ scratch databases).' }
    Assert-EtpBuildFolder $Protected 'Protected'; Assert-EtpBuildFolder $Unprotected 'Unprotected'
    $Protected = (Resolve-Path -LiteralPath $Protected).Path; $Unprotected = (Resolve-Path -LiteralPath $Unprotected).Path
    if ($Protected -ieq $Unprotected) { Stop-EtpRefused '-Protected and -Unprotected are the same folder.' }
    if (-not (Test-Path -LiteralPath $FixtureFolder -PathType Container)) { Stop-EtpRefused "Fixture folder not found: $FixtureFolder" }
    $FixtureFolder = (Resolve-Path -LiteralPath $FixtureFolder).Path
    [void](Assert-EtpIsReallyProtected $Protected $Unprotected)
    if (-not $Label) { $Label = (Get-Date).ToString('yyyyMMdd-HHmmss') }
    if ($Label -notmatch '^[A-Za-z0-9._-]{1,60}$') { Stop-EtpRefused 'Label may hold letters, digits, dot, underscore and hyphen only.' }
    $scratch = Get-EtpScratchNames $Database
    foreach ($n in $scratch.Unprotected, $scratch.Protected) {
        Assert-EtpScratchDatabase $n
        if (Test-EtpDatabaseExists $Server $n) { Stop-EtpRefused "Database $n already exists. This script drops only databases it created itself." }
    }
    if (-not $OutputFolder) { $OutputFolder = Join-Path ([IO.Path]::GetTempPath()) ('EtpPerformance-' + $Label) }
    [void](New-Item -ItemType Directory -Path $OutputFolder -Force)

    $exitCode = 3
    $hostDll = New-EtpProbeHost $Unprotected (Join-Path $OutputFolder 'probe-host')
    [void]$created.Add($scratch.Unprotected); [void]$created.Add($scratch.Protected)
    $samples = [pscustomobject]@{ Unprotected = (New-Object System.Collections.ArrayList); Protected = (New-Object System.Collections.ArrayList) }
    for ($run = 1; $run -le $Runs; $run++) {
        Write-Host "Run $run of $Runs"
        [void]$samples.Unprotected.Add((Invoke-EtpTimingRun 'unprotected' $Unprotected $scratch.Unprotected $Server $hostDll $FixtureFolder (Join-Path $OutputFolder 'unprotected')))
        [void]$samples.Protected.Add((Invoke-EtpTimingRun 'protected' $Protected $scratch.Protected $Server $hostDll $FixtureFolder (Join-Path $OutputFolder 'protected')))
    }
    $measures = @(New-EtpPerformanceResult $samples $NoiseFloorMs)
    $worst = 'OK'
    if (@($measures | Where-Object { $_.verdict -eq 'DROP-STRING-HIDING' }).Count -gt 0) { $worst = 'DROP-STRING-HIDING' }
    if (@($measures | Where-Object { $_.verdict -eq 'WITHHOLD-OBFUSCATION' }).Count -gt 0) { $worst = 'WITHHOLD-OBFUSCATION' }
    $result = [ordered]@{
        label = $Label; measuredUtc = (Get-Date).ToUniversalTime().ToString('o'); computer = $env:COMPUTERNAME; runs = $Runs; noiseFloorMs = $NoiseFloorMs
        rule = 'over 5 % slower on any measure: drop string hiding from the slow assembly; over 10 %: withhold obfuscation from this release'
        overall = $worst; measures = $measures
    }
    $path = Join-Path $OutputFolder ("performance-$Label.json")
    [IO.File]::WriteAllText($path, ($result | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    foreach ($m in $measures) { Write-Host ('{0,-12} unprotected {1,9} ms  protected {2,9} ms  {3,7} %  {4}' -f $m.measure, $m.unprotectedMedianMs, $m.protectedMedianMs, $m.percentSlower, $m.verdict) }
    Write-Host "Overall: $worst. Written: $path"
    if ($worst -eq 'OK') { $exitCode = 0 } elseif ($worst -eq 'DROP-STRING-HIDING') { $exitCode = 4 } else { $exitCode = 5 }
}
catch {
    $message = $_.Exception.Message
    if ($message -like 'ETP-REFUSED:*') { Write-Host ('REFUSED: ' + $message.Substring('ETP-REFUSED:'.Length).Trim()); $exitCode = 2 }
    else { Write-Host "INCOMPLETE: $message"; $exitCode = 3 }
}
finally {
    if (-not $KeepDatabases) {
        foreach ($n in $created) { try { Remove-EtpScratchDatabase $Server $n } catch { Write-Host "WARNING: could not drop $n : $($_.Exception.Message)" } }
    }
}
exit $exitCode
