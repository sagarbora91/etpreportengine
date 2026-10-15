# Elevated (Windows PowerShell 5.1), interactive: waits for the 1.12.0 gate + installer (Chain-Gate-Then-Installer-1.12.0.ps1 must pass), lists the checkpoints, then runs the VM stages in order.
# Sagar types the ETPTest password (once per stage) and answers the YES prompts in this window. Log: E:\ETP\Migration 2026-10-04\vm-1.12.0-sequence.log
# NOT RUN YET (written 13 Oct 2026, parse-checked only). Copy of Run-VM-1.11.4-Sequence.ps1 (unchanged).
# WAVE 2 (review M-2, 15 Oct 2026; wave-1 version kept as .bak-before-w2-review): waits on chain-1.12.0-w2.log (never the wave-1
# chain-1.12.0.log, which already says DONE), takes the gate sha from its 'gate:' line, and before stage 1 requires the installer
# folder to hold installer-build-1.12.0-<that sha>-w2b1.log and the exe to match its SHA256SUMS.txt line. Otherwise it stops.
#   -Path From1114  (default)  start from the VM at 1.11.4:  checkpoint 'VM195 Upgrade after automated steps, 2026-10-13 13:25'   (61 -> 66 migrations)
#   -Path From196              start from the 1.9.6 VM:      checkpoint 'VM196 delta*'                                              (48 -> 66 migrations)
#   -CheckpointName '<pattern>' overrides the checkpoint of either path (wildcards allowed, newest match wins).
#   -SkipWait                  do not wait for the chain log (installer already built and the gate known good).
# Stages: 1 Upgrade (restores the checkpoint, asks YES) -> 2 ServiceUi -> 3 HealthNightOne (needs VM local time 02:30 or later, else it runs nothing and says so)
#         -> 4 Release112 (read-only 1.12 checks + the manual checklist). Stages 2-4 write into the evidence folder stage 1 made (VM195-<time>-Upgrade-Checkpoint).
[CmdletBinding()]
param(
    [ValidateSet('From1114', 'From196')][string]$Path = 'From1114',
    [string]$CheckpointName,
    [switch]$SkipWait
)
$log = 'E:\ETP\Migration 2026-10-04\vm-1.12.0-sequence.log'
Start-Transcript -Path $log -Force | Out-Null
$inst = 'E:\ETP\Installers\ETP Reporting Engine 1.12.0\EtpReportingEngine-Setup-1.12.0-x64.exe'
$chain = 'E:\ETP\Migration 2026-10-04\chain-1.12.0-w2.log'
$instDir = Split-Path $inst -Parent
$stagesFile = 'E:\ETP\Migration 2026-10-04\vm-1.12.0-stages.txt'
$h = 'E:\ETP\Reference\Work in progress 2026-10-04 (CONTAINS BUSINESS DATA)\Run-VM-Acceptance-1.12.0.ps1'
$vmName = 'ETP-Acceptance-186'
if (-not $SkipWait) {
  Write-Host "Waiting for the 1.12.0 wave-2 gate and installer ($chain)..." -ForegroundColor Cyan
  $lastBeat = Get-Date
  while ($true) {
    $c = Get-Content $chain -Raw -ErrorAction SilentlyContinue
    if ($c -match 'did not pass|Installer build failed|Publish failed') {
      Write-Host "Gate or installer FAILED - VM run not started. See $chain" -ForegroundColor Red
      "ABORT gate/installer failed $(Get-Date -Format s)" | Add-Content $stagesFile
      Stop-Transcript | Out-Null; Read-Host 'Press Enter to close'; exit 1
    }
    if (($c -match 'DONE 20') -and (Test-Path $inst)) { break }
    if (((Get-Date) - $lastBeat).TotalMinutes -ge 2) { Write-Host ("$(Get-Date -Format 'HH:mm:ss') still waiting, gate/installer not done") -ForegroundColor DarkYellow; $lastBeat = Get-Date }
    Start-Sleep 30
  }
}
if (-not (Test-Path $inst)) {
  Write-Host "Installer not found: $inst" -ForegroundColor Red
  "ABORT no installer $(Get-Date -Format s)" | Add-Content $stagesFile
  Stop-Transcript | Out-Null; Read-Host 'Press Enter to close'; exit 1
}
# Review M-2: the installer must be the wave-2 build of the commit the w2 gate passed (same file name and version as wave 1).
function Stop-NotW2([string]$Why) {
  Write-Host "Installer check FAILED - VM run not started: $Why" -ForegroundColor Red
  "ABORT installer check: $Why $(Get-Date -Format s)" | Add-Content $stagesFile
  Stop-Transcript | Out-Null; Read-Host 'Press Enter to close'; exit 1
}
$c = Get-Content $chain -Raw -ErrorAction SilentlyContinue
if (-not $c) { Stop-NotW2 "no wave-2 chain log $chain (run Chain-Gate-Then-Installer-1.12.0-w2.ps1 first)" }
if ($c -notmatch 'DONE 20') { Stop-NotW2 "$chain does not end DONE (gate, build or publish not finished)" }
$gateLine = @($c -split "`r?`n" | Where-Object { $_ -match '^gate: .*PASSED.* sha=([0-9a-f]{7})' })
if ($gateLine.Count -eq 0) { Stop-NotW2 "no PASSED gate line with sha= in $chain" }
$null = $gateLine[-1] -match ' sha=([0-9a-f]{7})'; $gateSha = $Matches[1]
$buildLog = @(Get-ChildItem -LiteralPath $instDir -File -Filter 'installer-build-1.12.0-*-w2b1.log' -ErrorAction SilentlyContinue)
if ($buildLog.Count -ne 1) { Stop-NotW2 "expected one installer-build-1.12.0-*-w2b1.log in $instDir, found $($buildLog.Count)" }
if ($buildLog[0].Name -notmatch '^installer-build-1\.12\.0-([0-9a-f]{7})-w2b1\.log$') { Stop-NotW2 "unexpected build log name $($buildLog[0].Name)" }
$buildSha = $Matches[1]
if ($buildSha -ne $gateSha) { Stop-NotW2 "installer built from $buildSha but the wave-2 gate passed $gateSha" }
$sumLine = @(Get-Content -LiteralPath (Join-Path $instDir 'SHA256SUMS.txt') -ErrorAction SilentlyContinue | Where-Object { $_ -match ('\s' + [regex]::Escape((Split-Path $inst -Leaf)) + '$') })
if ($sumLine.Count -ne 1) { Stop-NotW2 "no SHA256SUMS.txt line for the installer in $instDir" }
$expected = ($sumLine[0] -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $inst -Algorithm SHA256).Hash
if ($actual -ne $expected) { Stop-NotW2 "installer SHA-256 $actual does not match SHA256SUMS.txt ($expected)" }
Write-Host "Wave-2 installer: build sha $buildSha = gate sha $gateSha; SHA-256 $actual matches SHA256SUMS.txt." -ForegroundColor Green
"INSTALLER w2 build=$buildSha gate=$gateSha sha256=$actual $(Get-Date -Format s)" | Add-Content $stagesFile
Write-Host "Installer ready. Checkpoints on ${vmName}:" -ForegroundColor Green
Get-VMSnapshot -VMName $vmName | Sort-Object CreationTime | Format-Table Name, CreationTime -AutoSize | Out-String | Write-Host
$pattern = $CheckpointName
if (-not $pattern) {
  if ($Path -eq 'From196') { $pattern = 'VM196 delta*' } else { $pattern = 'VM195 Upgrade after automated steps, 2026-10-13 13:25' }
}
if (-not (Get-VMSnapshot -VMName $vmName | Where-Object Name -like $pattern)) {
  Write-Host "No checkpoint matching '$pattern' - stopping. Tell Claude which checkpoint to use (or pass -CheckpointName)." -ForegroundColor Red
  "ABORT no checkpoint '$pattern' $(Get-Date -Format s)" | Add-Content $stagesFile
  Stop-Transcript | Out-Null; Read-Host 'Press Enter to close'; exit 1
}
Write-Host "Path: $Path; checkpoint: $pattern" -ForegroundColor Cyan
"START path=$Path checkpoint='$pattern' $(Get-Date -Format s)" | Add-Content $stagesFile
$ref = 'E:\ETP\Reference'
$before = @(Get-ChildItem -LiteralPath $ref -Directory -Filter 'VM195-*-Upgrade-Checkpoint*' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
$outFolder = $null
$stages = @(
  @{ Name = '1/4 Upgrade to 1.12.0 from the checkpoint';  Args = @('-Scenario','Upgrade','-Stage','Install','-UpgradeFrom','Checkpoint','-CheckpointName',$pattern) },
  @{ Name = '2/4 Service UI checks and timings';          Args = @('-Scenario','Rehearsal','-Stage','ServiceUi') },
  @{ Name = '3/4 Night-one health check';                 Args = @('-Scenario','Rehearsal','-Stage','HealthNightOne') },
  @{ Name = '4/4 1.12.0 read-only checks and checklist';   Args = @('-Scenario','Upgrade','-UpgradeFrom','Checkpoint','-Stage','Release112') }
)
foreach ($s in $stages) {
  Write-Host "`n===== $($s.Name) =====" -ForegroundColor Cyan
  $a = @($s.Args)
  if ($outFolder -and $s.Name -notlike '1/4*') { $a += @('-OutputFolder', $outFolder) }
  & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $h @a
  $rc = $LASTEXITCODE
  Write-Host "===== $($s.Name): exit $rc =====" -ForegroundColor ($(if ($rc) {'Red'} else {'Green'}))
  "STAGE $($s.Name) exit $rc $(Get-Date -Format s)" | Add-Content $stagesFile
  if ($s.Name -like '1/4*') {
    # The evidence folder stage 1 created: stages 2-4 write beside it (VM195-<time>-Upgrade-Checkpoint), not into the old Rehearsal folder.
    $now = @(Get-ChildItem -LiteralPath $ref -Directory -Filter 'VM195-*-Upgrade-Checkpoint*' -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.FullName } | Sort-Object LastWriteTime)
    if ($now.Count -gt 0) { $outFolder = $now[-1].FullName; "EVIDENCE $outFolder" | Add-Content $stagesFile; Write-Host "Evidence folder: $outFolder" }
  }
  if ($rc) { Write-Host 'Stopping at the first failed stage.' -ForegroundColor Red; break }
}
"SEQUENCE END $(Get-Date -Format s)" | Add-Content $stagesFile
Stop-Transcript | Out-Null
Read-Host 'VM sequence finished - press Enter to close'
