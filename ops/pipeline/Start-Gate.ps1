# Parameterised elevated FULL gate (replaces the per-version Start-Gate/Run-Gate/Rerun-Gate copies; Arch 14, 12 Oct 2026; hardened by lane A2 the same evening).
# Run ELEVATED in pwsh 7 with the ETP app closed. Example:
#   & 'E:\ETP\Migration 2026-10-04\Start-Gate.ps1' -Version 1.11.2 -Worktree 'E:\ETP\Code\Worktrees\release-1.11.2'
# For the everyday unelevated gate (6 minutes, no SQL) use Start-FastGate.ps1 instead.
#
# Outer mode (default): pauses lane builds (PAUSE-LANE-BUILDS), takes the heavy-job lock via Invoke-Serialized.ps1, runs the inner gate
#   under a hard overall timeout, writes gate-<Version>-exit.txt ("Gate exit code: N" and the 'gate:' line) beside this script, and ALWAYS
#   removes the pause file (finally). If the inner gate hangs or is killed, the outer still writes the 'gate:' line (TIMED OUT / STOPPED).
# -CleanLeftovers: first drops ONLY the gate's leftover EtpPhase0Test_* / EtpRecovery_* databases and etp_operations_test_* master
#   procedures (what Rerun-Gate did); EtpReporting and any other database are never touched.
# -Inner: the gate itself (fresh verified backup, pre-checks a-d, Release build, every tests-dotnet\*Tests.csproj with TRX + full log,
#   after-checks). Never filtered; the whole console log of each project is kept in the results folder. Each step has a timeout
#   (build 25 min, unit projects 30 min, the SQL integration project 120 min, hang detection --blame-hang-timeout 40m) and writes to
#   files, never through a pipe (12 Oct 2026: a piped `dotnet test` left the gate idle for 30 minutes with no child process).
#   More than 2 ETP-FLAKE-RETRY lines (the known transport flake, retried once by [SqlFact]/[SqlTheory]) fail the gate.
#   The last thing the inner gate does, pass or fail, is write:  gate: <Version> <PASSED|FAILED|STOPPED> exit=N tests=.. passed=.. failed=.. ...
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$Worktree,
    [string]$ToolsDirectory = 'E:\ETP\Code\Worktrees\_tools',
    [string]$ReferenceRoot = 'E:\ETP\Reference',
    [string]$PowerShellExe = 'E:\Tools\PowerShell\7\pwsh.exe',
    [string]$Instance = '.\SQLEXPRESS',
    [int]$WaitMinutes = 90,
    [int]$RunMinutes = 150,
    [string]$ResultsDirectory,
    [switch]$CleanLeftovers,
    [switch]$Inner
)
. (Join-Path $PSScriptRoot 'GateCommon.ps1')
$repo = $Worktree
$sqlcmdPath = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
if (-not (Test-Path $sqlcmdPath)) { $sqlcmdPath = 'sqlcmd' }
if (-not $Inner) {
    $ErrorActionPreference = 'Stop'
    if (-not (Test-Path -LiteralPath $Worktree)) { throw "Worktree not found: $Worktree" }
    $exitFile = Join-Path $PSScriptRoot "gate-$Version-exit.txt"
    Remove-Item $exitFile -ErrorAction SilentlyContinue
    $results = (Join-Path $ReferenceRoot ('Gate-' + $Version + '-')) + (Get-Date -Format 'yyyy-MM-dd-HHmm')
    New-Item -ItemType Directory -Force -Path $results | Out-Null
    if ($CleanLeftovers) {
        $q = @'
SET NOCOUNT ON;
DECLARE @n sysname, @s nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM sys.databases WHERE (name LIKE N'EtpPhase0Test[_]%' OR name LIKE N'EtpRecovery[_]%') AND name <> N'EtpReporting';
OPEN c; FETCH NEXT FROM c INTO @n;
WHILE @@FETCH_STATUS = 0 BEGIN SET @s = N'ALTER DATABASE ' + QUOTENAME(@n) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@n) + N';'; PRINT N'dropping database ' + @n; EXEC (@s); FETCH NEXT FROM c INTO @n; END
CLOSE c; DEALLOCATE c;
DECLARE p CURSOR LOCAL FAST_FORWARD FOR SELECT name FROM master.sys.procedures WHERE name LIKE N'etp[_]operations[_]test[_]%';
OPEN p; FETCH NEXT FROM p INTO @n;
WHILE @@FETCH_STATUS = 0 BEGIN SET @s = N'DROP PROCEDURE master.dbo.' + QUOTENAME(@n) + N';'; PRINT N'dropping master procedure ' + @n; EXEC (@s); FETCH NEXT FROM p INTO @n; END
CLOSE p; DEALLOCATE p;
'@
        & $sqlcmdPath -S $Instance -E -C -W -b -t 120 -l 30 -d master -Q $q
        if ($LASTEXITCODE) { throw "Leftover cleanup failed ($LASTEXITCODE); gate not started." }
    }
    $pause = Join-Path $ToolsDirectory 'PAUSE-LANE-BUILDS'
    $code = 1
    $resultFile = Join-Path $results 'gate-result.txt'
    try {
        "gate-$Version" | Set-Content $pause
        $self = $MyInvocation.MyCommand.Path
        $argText = "-NoProfile -File `"$(Join-Path $ToolsDirectory 'Invoke-Serialized.ps1')`" -Label gate-$Version -WaitMinutes $WaitMinutes -WorkingDirectory `"$Worktree`" " +
            "-Command `"& '$self' -Inner -Version '$Version' -Worktree '$Worktree' -ReferenceRoot '$ReferenceRoot' -Instance '$Instance' -ResultsDirectory '$results' -RunMinutes $RunMinutes`""
        # Shares this console; killed (whole tree) if it outlasts the lock wait plus the run cap plus the 45 s cool-down.
        $step = Invoke-GateStep -Name "gate-$Version" -FilePath $PowerShellExe -ArgumentList @() -TimeoutMinutes ($WaitMinutes + $RunMinutes + 5) -WorkingDirectory $Worktree -LogPath $null -RawArguments $argText
        $code = $step.ExitCode
        if ($step.TimedOut -and -not (Test-Path $resultFile)) {
            Write-GateResultLine -Line "gate: $Version TIMED-OUT exit=124 (outer cap $($WaitMinutes + $RunMinutes + 5) min; the inner gate wrote no result) results=$results" -Path $resultFile
        }
        elseif (-not (Test-Path $resultFile)) {
            Write-GateResultLine -Line "gate: $Version STOPPED exit=$code (the inner gate wrote no result; exit 75 means the heavy-job lock stayed busy) results=$results" -Path $resultFile
        }
    }
    catch {
        $code = 1
        if (-not (Test-Path $resultFile)) { Write-GateResultLine -Line "gate: $Version STOPPED exit=1 ($($_.Exception.Message)) results=$results" -Path $resultFile }
    }
    finally {
        Remove-Item $pause -ErrorAction SilentlyContinue
        try {
            "Gate exit code: $code" | Set-Content $exitFile
            if (Test-Path $resultFile) { Get-Content $resultFile | Add-Content $exitFile }
            Get-Content $exitFile | Write-Host
        } catch { Write-Host "Could not write ${exitFile}: $($_.Exception.Message)" }
    }
    exit $code
}

# ---------------------------------------------------------------- inner gate (elevated, under the heavy-job lock)
$ErrorActionPreference = 'Stop'
$r = if ($ResultsDirectory) { $ResultsDirectory } else { (Join-Path $ReferenceRoot ('Gate-' + $Version + '-')) + (Get-Date -Format 'yyyy-MM-dd-HHmm') }
New-Item -ItemType Directory -Force -Path $r | Out-Null
$resultFile = Join-Path $r 'gate-result.txt'
Start-Transcript -Path (Join-Path $r 'gate-run.log') | Out-Null
$sqlcmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
function Sql([string]$q) { & $sqlcmd -S $Instance -E -C -h -1 -W -s '|' -b -t 120 -l 30 -Q ("SET NOCOUNT ON; " + $q); if ($LASTEXITCODE) { throw "sqlcmd failed ($LASTEXITCODE)" } }
$exit = 1
$outcome = 'STOPPED'
$why = ''
$stepNotes = New-Object System.Collections.Generic.List[string]
$gateStart = Get-Date
$deadline = $gateStart.AddMinutes($RunMinutes)
try {
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Not elevated.' }
    if (Get-Process -Name 'Etp.Reporting.Desktop' -ErrorAction SilentlyContinue) { throw 'The ETP app is running; close it first.' }
    Write-Host "== Branch: $(git -C $repo rev-parse --short HEAD) $(git -C $repo log -1 --format=%s)"
    if (git -C $repo status --porcelain) { throw 'The integration worktree has uncommitted changes.' }

    Write-Host '== 1. Fresh backup of EtpReporting (daily backup task)'
    $before = (Sql "SELECT COUNT(*) FROM EtpReporting.dbo.operational_audit WHERE event_type=N'Backup' AND outcome=N'Succeeded';" | Select-Object -First 1).Trim()
    Start-ScheduledTask -TaskName 'ETP Reporting Daily Backup'
    $backupDeadline = (Get-Date).AddMinutes(20)
    do { Start-Sleep 15; $t = Get-ScheduledTask -TaskName 'ETP Reporting Daily Backup' } while ($t.State -eq 'Running' -and (Get-Date) -lt $backupDeadline)
    $info = Get-ScheduledTaskInfo -TaskName 'ETP Reporting Daily Backup'
    $after = (Sql "SELECT COUNT(*) FROM EtpReporting.dbo.operational_audit WHERE event_type=N'Backup' AND outcome=N'Succeeded';" | Select-Object -First 1).Trim()
    Write-Host "  task result $($info.LastTaskResult); succeeded backups $before -> $after"
    Sql "SELECT TOP 1 event_utc, outcome, safe_detail FROM EtpReporting.dbo.operational_audit WHERE event_type=N'Backup' ORDER BY operational_audit_id DESC;"
    if ([int]$after -le [int]$before) { throw 'No new successful backup was recorded; not running the gate.' }

    Write-Host '== 2. Pre-checks'
    $a = Sql "SELECT name FROM sys.server_principals WHERE name IN (N'NT AUTHORITY\LOCAL SERVICE', N'NT AUTHORITY\NETWORK SERVICE');"
    $b = Sql "SELECT pr.name COLLATE DATABASE_DEFAULT + N':' + p.state COLLATE DATABASE_DEFAULT FROM sys.server_permissions p JOIN sys.server_principals pr ON pr.principal_id=p.grantee_principal_id WHERE p.permission_name=N'ALTER ANY LOGIN' AND pr.name=N'NT AUTHORITY\SYSTEM';"
    $c = Sql "SELECT c.name COLLATE DATABASE_DEFAULT + N' signs=' + CAST((SELECT COUNT(*) FROM master.sys.crypt_properties cp WHERE cp.thumbprint=c.thumbprint) AS varchar(10)) FROM master.sys.certificates c WHERE c.name LIKE N'EtpOperationsModuleSigner%';"
    $d = Sql "SELECT name FROM sys.databases WHERE name LIKE N'EtpPhase0Test[_]%' OR name LIKE N'EtpRecovery[_]%' UNION ALL SELECT name FROM master.sys.procedures WHERE name LIKE N'etp[_]operations[_]test[_]%';"
    Write-Host "  (a) service logins: $(@($a) -join ', ')"; Write-Host "  (b) SYSTEM ALTER ANY LOGIN: $(@($b) -join ', ')"
    Write-Host "  (c) signer certificates: $(@($c) -join ', ')"; Write-Host "  (d) leftovers: $(@($d) -join ', ')"
    if (@($a | Where-Object { $_ }).Count -or @($b | Where-Object { $_ }).Count -or @($d | Where-Object { $_ }).Count) { throw 'A pre-check found something (see above); not running the gate.' }

    Write-Host '== 3. Gate'
    Set-Location $repo
    git rev-parse HEAD | Out-File (Join-Path $r 'sha.txt')
    $dotnet = Get-DotnetPath
    $env:ETP_FLAKE_LOG_DIRECTORY = $r
    $fail = 0
    $build = Invoke-GateStep -Name 'build' -FilePath $dotnet -ArgumentList @('build', 'Etp.Reporting.slnx', '-c', 'Release', '-m:1', '-nodeReuse:false') -LogPath (Join-Path $r 'build.log') -TimeoutMinutes 25 -WorkingDirectory $repo
    Get-Content -LiteralPath $build.LogPath -Tail 5 | ForEach-Object { Write-Host "     $_" }
    $stepNotes.Add("build=$($build.ExitCode)")
    if ($build.ExitCode) { throw "Build failed or timed out ($($build.ExitCode))" }
    $projects = ([xml](Get-Content Etp.Reporting.slnx)).SelectNodes('//Project/@Path') | ForEach-Object Value | Where-Object { $_ -like 'tests-dotnet/*Tests.csproj' }
    foreach ($p in $projects) {
        $n = [IO.Path]::GetFileNameWithoutExtension($p)
        if ((Get-Date) -gt $deadline) { $fail = 1; $stepNotes.Add("$n=SKIPPED-run-cap"); Write-Host "  -- $n SKIPPED: the $RunMinutes-minute run cap was reached"; continue }
        Write-Host "  -- $n"
        $minutes = if ($n -like '*IntegrationTests') { 120 } else { 30 }
        $log = Join-Path $r "$n.console.log"
        $step = Invoke-GateStep -Name $n -FilePath $dotnet -TimeoutMinutes $minutes -WorkingDirectory $repo -LogPath $log -ArgumentList @(
            'test', $p, '-c', 'Release', '--no-build', '-m:1', '--logger', "trx;LogFileName=$n.trx", '--logger', 'console;verbosity=normal',
            '--results-directory', $r, '--blame-hang-timeout', '40m', '--blame-hang-dump-type', 'none')
        Select-String -LiteralPath $log -Pattern '^(Passed!|Failed!|Total tests|\s+Passed:|\s+Failed:|\s+Skipped:)' | ForEach-Object { Write-Host "     $($_.Line.Trim())" }
        $stepNotes.Add("$n=$($step.ExitCode)")
        if ($step.ExitCode) { $fail = 1; Write-Host "     EXIT $($step.ExitCode)$(if ($step.TimedOut) { ' (TIMED OUT)' })"; if ($step.TimedOut) { Stop-OrphanTestHosts -Worktree $repo } }
    }
    $retries = @(Get-FlakeRetryLines -Directory $r)
    Write-Host "  flake retries (ETP-FLAKE-RETRY): $($retries.Count)"
    $retries | ForEach-Object { Write-Host "     $_" }
    if ($retries.Count -gt 2) { $fail = 1; $why = "more than 2 transport-flake retries ($($retries.Count))"; Write-Host "  GATE RULE: $why" }

    Write-Host '== 4. After-checks'
    Write-Host "  (a) service logins: $(@(Sql "SELECT name FROM sys.server_principals WHERE name IN (N'NT AUTHORITY\LOCAL SERVICE', N'NT AUTHORITY\NETWORK SERVICE');") -join ', ')"
    Write-Host "  (d) leftovers: $(@(Sql "SELECT name FROM sys.databases WHERE name LIKE N'EtpPhase0Test[_]%' OR name LIKE N'EtpRecovery[_]%' UNION ALL SELECT name FROM master.sys.procedures WHERE name LIKE N'etp[_]operations[_]test[_]%';") -join ', ')"
    Write-Host "== GATE $(if ($fail) { 'FAILED' } else { 'PASSED' }). Results: $r"
    $exit = $fail
    $outcome = if ($fail) { 'FAILED' } else { 'PASSED' }
}
catch { $why = $_.Exception.Message; Write-Host "STOPPED: $why" }
finally {
    # Always: the result line, whatever happened above.
    try {
        $counts = Get-TrxCounts -Directory $r
        $sha = try { (Get-Content (Join-Path $r 'sha.txt') -ErrorAction Stop | Select-Object -First 1).Substring(0, 7) } catch { 'unknown' }
        $line = "gate: $Version $outcome exit=$exit tests=$($counts.Total) passed=$($counts.Passed) failed=$($counts.Failed) skipped=$($counts.Skipped) trx=$($counts.Files) " +
            "flake-retries=$(@(Get-FlakeRetryLines -Directory $r).Count) sha=$sha minutes=$([int]((Get-Date) - $gateStart).TotalMinutes) steps=[$($stepNotes -join ' ')]" +
            $(if ($why) { " note=`"$why`"" } else { '' }) + " results=$r"
        Write-GateResultLine -Line $line -Path $resultFile
    }
    catch { Write-Host "gate: $Version $outcome exit=$exit (could not summarise: $($_.Exception.Message)) results=$r" }
    Stop-Transcript | Out-Null
}
exit $exit
