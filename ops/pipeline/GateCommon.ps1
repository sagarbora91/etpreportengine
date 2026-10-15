# Shared helpers for Start-Gate.ps1, Start-FastGate.ps1, Invoke-NightlyIntegration.ps1 and Run-FlakeExperiment.ps1 (lane A2, 12 Oct 2026).
# Dot-source it:  . (Join-Path $PSScriptRoot 'GateCommon.ps1')
#
# Why it exists: on 12 Oct 2026 a gate sat idle for 30 minutes after the SqlServer.Tests step with NO child process left. The old
# loop piped `dotnet test` through Tee-Object | Select-String, and a pipeline waits for the end of the output stream, which an
# orphaned child (testhost, build server) can hold open forever. Every step here therefore (1) writes to FILES, never a pipe,
# (2) has a hard timeout after which the whole process tree is killed, and (3) reports what happened either way.

function Invoke-GateStep {
    <#
      Runs one external command with a hard timeout. Output goes to -LogPath (stdout) and -LogPath + '.stderr' (stderr, appended
      to the log at the end), never filtered. Without -LogPath the command shares this console (used for the outer wrapper).
      Returns an object: Name, ExitCode (124 = timed out and killed), TimedOut, Seconds, LogPath.
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$LogPath,
        [double]$TimeoutMinutes = 30,
        [string]$WorkingDirectory = (Get-Location).Path,
        [string]$RawArguments      # a ready-made argument string; used instead of -ArgumentList when given
    )
    $quoted = @($ArgumentList | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } })
    $start = Get-Date
    $splat = @{ FilePath = $FilePath; WorkingDirectory = $WorkingDirectory; NoNewWindow = $true; PassThru = $true }
    if ($RawArguments) { $splat.ArgumentList = $RawArguments } elseif ($quoted.Count) { $splat.ArgumentList = $quoted }
    $errPath = $null
    if ($LogPath) {
        $errPath = "$LogPath.stderr"
        $splat.RedirectStandardOutput = $LogPath
        $splat.RedirectStandardError = $errPath
    }
    $timedOut = $false
    $code = 1
    try {
        $p = Start-Process @splat
        $null = $p.Handle   # keeps the exit code readable after exit
        if ($p.WaitForExit($TimeoutMinutes * 60 * 1000)) {
            $p.WaitForExit()
            $code = $p.ExitCode
        }
        else {
            $timedOut = $true
            $code = 124
            & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null
            if ($LogPath) { Add-Content -LiteralPath $LogPath -Value "GATE-TIMEOUT: '$Name' was still running after $TimeoutMinutes minutes; its process tree was killed." }
            Write-Host "     TIMEOUT: $Name after $TimeoutMinutes minutes; process tree killed."
        }
    }
    catch {
        $code = 1
        if ($LogPath) { Add-Content -LiteralPath $LogPath -Value "GATE-STEP-ERROR: $($_.Exception.Message)" }
        Write-Host "     STEP ERROR in ${Name}: $($_.Exception.Message)"
    }
    finally {
        if ($errPath -and (Test-Path -LiteralPath $errPath)) {
            $err = Get-Content -LiteralPath $errPath -Raw -ErrorAction SilentlyContinue
            if ($err) { Add-Content -LiteralPath $LogPath -Value "`n--- stderr ---`n$err" }
            Remove-Item -LiteralPath $errPath -ErrorAction SilentlyContinue
        }
    }
    [pscustomobject]@{ Name = $Name; ExitCode = $code; TimedOut = $timedOut; Seconds = [int]((Get-Date) - $start).TotalSeconds; LogPath = $LogPath }
}

function Get-TrxCounts {
    # Totals over every *.trx in a folder. A project that wrote no trx (crash, timeout) adds nothing; the caller sees its exit code.
    param([Parameter(Mandatory)][string]$Directory)
    $t = @{ Total = 0; Passed = 0; Failed = 0; Skipped = 0; Files = 0 }
    Get-ChildItem -LiteralPath $Directory -Filter '*.trx' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $c = ([xml](Get-Content -LiteralPath $_.FullName -Raw)).TestRun.ResultSummary.Counters
            $t.Total += [int]$c.total; $t.Passed += [int]$c.passed; $t.Failed += [int]$c.failed; $t.Files++
            $t.Skipped += ([int]$c.total - [int]$c.executed)
        } catch { }
    }
    [pscustomobject]$t
}

function Get-FlakeRetryLines {
    # The ETP-FLAKE-RETRY lines written by [SqlFact]/[SqlTheory] and by the SQL fixture (tests-dotnet/TestSupport/SqlFlakeRetry.cs).
    param([Parameter(Mandatory)][string]$Directory)
    $f = Join-Path $Directory 'etp-flake-retry.log'
    if (Test-Path -LiteralPath $f) { @(Get-Content -LiteralPath $f | Where-Object { $_ -match 'ETP-FLAKE-RETRY' }) } else { @() }
}

function Write-GateResultLine {
    # The one-line verdict ("gate: ..."), written to every place a reader looks, even when the run failed or was stopped.
    param(
        [Parameter(Mandatory)][string]$Line,
        [string[]]$Path = @()
    )
    Write-Host $Line
    foreach ($p in $Path) {
        try { Add-Content -LiteralPath $p -Value $Line -ErrorAction Stop } catch { Write-Host "     (could not write the result line to $p : $($_.Exception.Message))" }
    }
}

function Get-DotnetPath {
    $c = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($c) { $c.Source } else { 'E:\dotnet\dotnet.exe' }
}

function Stop-OrphanTestHosts {
    # After a timeout kill: testhost / vstest children are normally gone with the tree; this catches stragglers started from this worktree only.
    param([Parameter(Mandatory)][string]$Worktree)
    Get-CimInstance Win32_Process -Filter "Name='testhost.exe' OR Name='dotnet.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$Worktree*" -and $_.CommandLine -match 'testhost|vstest' } |
        ForEach-Object { & taskkill.exe /PID $_.ProcessId /T /F 2>&1 | Out-Null; Write-Host "     killed straggler PID $($_.ProcessId)" }
}
