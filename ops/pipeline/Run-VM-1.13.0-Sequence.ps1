# 1.13.0 COPY (code-protection lane G1, 15 Oct 2026) of Run-VM-1.12.0-Sequence.ps1; NOT RUN YET, parse-checked only. The original is untouched.
# Elevated (Windows PowerShell 5.1), interactive: waits for Chain-Gate-Then-Installer-1.13.0.ps1 (chain-1.13.0.log must end DONE), checks the installer is the
# build of the sha the gate passed (installer-build-1.13.0-<sha>-b1.log, SHA256SUMS.txt) and that it is the PROTECTED build (release.json notes name obfuscation
# and a signer thumbprint, no .pdb files beside it), lists the checkpoints, then runs the VM stages in order. Sagar types the ETPTest password (once per stage)
# and answers the YES prompts in this window. Log: E:\ETP\Migration 2026-10-04m-1.13.0-sequence.log
#   -Path From112x (default)   upgrade from the 1.12.x checkpoint (the VM state after the 1.12.0 acceptance; give the exact name with -CheckpointName)
#   -CheckpointName '<pattern>' overrides the checkpoint (wildcards allowed, newest match wins). Default pattern is a PLACEHOLDER: the stop below
#                              tells Claude/Sagar which checkpoints exist if it matches nothing.
#   -SkipWait                  do not wait for the chain log.
# Stages: 1 Upgrade (restores the checkpoint, asks YES) -> 2 ServiceUi -> 3 HealthNightOne -> 4 Release113 (read-only checks + manual 1130 checklist).
# The acceptance helper (Run-VM-Acceptance-1.13.0.ps1) is produced by the integrator beside the 1.12.0 one; until then -Helper points at the 1.12.0 helper.
[CmdletBinding()]
param(
    [ValidateSet('From112x')][string]$Path = 'From112x',
    [string]$CheckpointName,
    [switch]$SkipWait,
    [string]$Helper = 'E:\ETP\Reference\Work in progress 2026-10-04 (CONTAINS BUSINESS DATA)\Run-VM-Acceptance-1.13.0.ps1'
)
$log = 'E:\ETP\Migration 2026-10-04\vm-1.13.0-sequence.log'
Start-Transcript -Path $log -Force | Out-Null
$inst = 'E:\ETP\Installers\ETP Reporting Engine 1.13.0\EtpReportingEngine-Setup-1.13.0-x64.exe'
$chain = 'E:\ETP\Migration 2026-10-04\chain-1.13.0.log'
$instDir = Split-Path $inst -Parent
$stagesFile = 'E:\ETP\Migration 2026-10-04\vm-1.13.0-stages.txt'
$h = $Helper
if (-not (Test-Path -LiteralPath $h)) { $h = 'E:\ETP\Reference\Work in progress 2026-10-04 (CONTAINS BUSINESS DATA)\Run-VM-Acceptance-1.12.0.ps1' }
$vmName = 'ETP-Acceptance-186'
if (-not $SkipWait) {
  Write-Host "Waiting for the 1.13.0 gate and installer ($chain)..." -ForegroundColor Cyan
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
# Review M-2: the installer must be the protected build of the commit the 1.13.0 gate passed.
function Stop-NotInstaller([string]$Why) {
  Write-Host "Installer check FAILED - VM run not started: $Why" -ForegroundColor Red
  "ABORT installer check: $Why $(Get-Date -Format s)" | Add-Content $stagesFile
  Stop-Transcript | Out-Null; Read-Host 'Press Enter to close'; exit 1
}
$c = Get-Content $chain -Raw -ErrorAction SilentlyContinue
if (-not $c) { Stop-NotInstaller "no 1.13.0 chain log $chain (run Chain-Gate-Then-Installer-1.13.0.ps1 first)" }
if ($c -notmatch 'DONE 20') { Stop-NotInstaller "$chain does not end DONE (gate, build or publish not finished)" }
$gateLine = @($c -split "`r?`n" | Where-Object { $_ -match '^gate: .*PASSED.* sha=([0-9a-f]{7})' })
if ($gateLine.Count -eq 0) { Stop-NotInstaller "no PASSED gate line with sha= in $chain" }
$null = $gateLine[-1] -match ' sha=([0-9a-f]{7})'; $gateSha = $Matches[1]
$buildLog = @(Get-ChildItem -LiteralPath $instDir -File -Filter 'installer-build-1.13.0-*-b1.log' -ErrorAction SilentlyContinue)
if ($buildLog.Count -ne 1) { Stop-NotInstaller "expected one installer-build-1.13.0-*-b1.log in $instDir, found $($buildLog.Count)" }
if ($buildLog[0].Name -notmatch '^installer-build-1\.13\.0-([0-9a-f]{7})-b1\.log$') { Stop-NotInstaller "unexpected build log name $($buildLog[0].Name)" }
$buildSha = $Matches[1]
if ($buildSha -ne $gateSha) { Stop-NotInstaller "installer built from $buildSha but the 1.13.0 gate passed $gateSha" }
$sumLine = @(Get-Content -LiteralPath (Join-Path $instDir 'SHA256SUMS.txt') -ErrorAction SilentlyContinue | Where-Object { $_ -match ('\s' + [regex]::Escape((Split-Path $inst -Leaf)) + '$') })
if ($sumLine.Count -ne 1) { Stop-NotInstaller "no SHA256SUMS.txt line for the installer in $instDir" }
$expected = ($sumLine[0] -split '\s+')[0]
$actual = (Get-FileHash -LiteralPath $inst -Algorithm SHA256).Hash
if ($actual -ne $expected) { Stop-NotInstaller "installer SHA-256 $actual does not match SHA256SUMS.txt ($expected)" }
Write-Host "1.13.0 installer: build sha $buildSha = gate sha $gateSha; SHA-256 $actual matches SHA256SUMS.txt." -ForegroundColor Green
"INSTALLER 1.13.0 build=$buildSha gate=$gateSha sha256=$actual $(Get-Date -Format s)" | Add-Content $stagesFile
# G1: the installer folder must be the PROTECTED build: no symbol/map files beside it and release.json notes naming obfuscation and a signer.
$leak = @(Get-ChildItem -LiteralPath $instDir -Recurse -File -Force | Where-Object { $_.Extension -in '.pdb', '.map' -or $_.Name -in 'Mapping.txt', 'obfuscation-report.json' })
if ($leak.Count) { Stop-NotInstaller "symbol or map files in $instDir (e.g. $($leak[0].Name)); symbols must stay under Reference\Symbols" }
$relJson = Join-Path $instDir 'release.json'
if (-not (Test-Path -LiteralPath $relJson)) { Stop-NotInstaller "no release.json in $instDir" }
$relNotes = [string]((Get-Content -LiteralPath $relJson -Raw | ConvertFrom-Json).notes)
if ($relNotes -notmatch 'obfuscation=Obfuscar' -or $relNotes -notmatch 'Authenticode=signed') { Stop-NotInstaller "release.json notes do not record an obfuscated, signed build: [$relNotes]" }
Write-Host 'release.json records obfuscation and Authenticode signing; no symbol files beside the installer.' -ForegroundColor Green
Write-Host "Installer ready. Checkpoints on ${vmName}:" -ForegroundColor Green
Get-VMSnapshot -VMName $vmName | Sort-Object CreationTime | Format-Table Name, CreationTime -AutoSize | Out-String | Write-Host
$pattern = $CheckpointName
if (-not $pattern) {
  $pattern = 'VM112x*'   # PLACEHOLDER: the checkpoint taken after the 1.12.x acceptance; pass -CheckpointName with the real name
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
  @{ Name = '1/4 Upgrade to 1.13.0 from the 1.12.x checkpoint';  Args = @('-Scenario','Upgrade','-Stage','Install','-UpgradeFrom','Checkpoint','-CheckpointName',$pattern) },
  @{ Name = '2/4 Service UI checks and timings';          Args = @('-Scenario','Rehearsal','-Stage','ServiceUi') },
  @{ Name = '3/4 Night-one health check';                 Args = @('-Scenario','Rehearsal','-Stage','HealthNightOne') },
  @{ Name = '4/4 1.13.0 read-only checks and checklist';   Args = @('-Scenario','Upgrade','-UpgradeFrom','Checkpoint','-Stage','Release113') }
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
