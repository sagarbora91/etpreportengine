<#
Self-test for the OneDrive scripts. Plain PowerShell (5.1), no Pester needed. Works ONLY inside a fresh temp folder with a
fake "OneDrive"; never touches real ETP or OneDrive folders. Run:  powershell -File Test-OneDriveScripts.ps1
Exit code 0 = all passed.
#>
$ErrorActionPreference = 'Stop'
$scripts = Split-Path -Parent $PSScriptRoot
$root = Join-Path ([IO.Path]::GetTempPath()) ("etp-onedrive-test-" + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root)
$failures = New-Object System.Collections.ArrayList
function Assert-True($condition, $message) { if ($condition) { Write-Host "PASS  $message" } else { Write-Host "FAIL  $message"; [void]$failures.Add($message) } }
function Write-Json($path, $object) { [IO.File]::WriteAllText($path, ($object | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false))) }
function Invoke-Script($file, $arguments) {
    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $scripts $file)) + $arguments
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $script:LastOutput = (& powershell.exe @all 2>&1 | Out-String)
    $ErrorActionPreference = $previous
    if ($env:ETP_TEST_VERBOSE) { Write-Host $script:LastOutput }
    return $LASTEXITCODE
}

try {
    $day = (Get-Date).ToString('yyyy-MM-dd')
    $oneDrive = Join-Path $root 'OneDrive'; $source = Join-Path $root 'exports'; $inputs = Join-Path $root 'inputs'
    foreach ($d in $oneDrive, $source, $inputs) { [void](New-Item -ItemType Directory -Path $d) }
    [IO.File]::WriteAllText((Join-Path $source 'sales.xlsx'), 'fake workbook one')
    [IO.File]::WriteAllText((Join-Path $source 'stock.csv'), 'a,b')
    [IO.File]::WriteAllText((Join-Path $inputs 'ETP-Daily-Inputs-test.xlsx'), 'fake inputs')

    # ---- Send ----
    $sendConfig = Join-Path $root 'send.json'
    Write-Json $sendConfig @{ role = 'TITAN'; sourceFolder = $source; dailyInputsFolder = $inputs; oneDriveRoot = $oneDrive; stableSeconds = 0; logFolder = (Join-Path $root 'logs') }
    $code = Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig, '-WhatIf')
    Assert-True ($code -eq 0 -and -not (Test-Path (Join-Path $oneDrive 'ETP-Inbound'))) 'Send -WhatIf copies nothing'
    $code = Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig)
    $dayFolder = Join-Path $oneDrive "ETP-Inbound\TITAN\$day"
    Assert-True ($code -eq 0) 'Send exits 0'
    Assert-True ((Test-Path (Join-Path $dayFolder 'sales.xlsx')) -and (Test-Path (Join-Path $dayFolder 'ETP-Daily-Inputs-test.xlsx')) -and (Test-Path (Join-Path $dayFolder 'manifest.json'))) 'Send copies files and writes the manifest'
    $manifest = Get-Content -Raw (Join-Path $dayFolder 'manifest.json') | ConvertFrom-Json
    Assert-True (@($manifest.files).Count -eq 3 -and $manifest.role -eq 'TITAN') 'Manifest lists 3 files for TITAN'
    Assert-True ((Test-Path (Join-Path $source 'sales.xlsx')) -and (Test-Path (Join-Path $inputs 'ETP-Daily-Inputs-test.xlsx'))) 'Source files are untouched'
    $before = (Get-Item (Join-Path $dayFolder 'sales.xlsx')).LastWriteTimeUtc
    $code = Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig)
    Assert-True ($code -eq 0 -and (Get-Item (Join-Path $dayFolder 'sales.xlsx')).LastWriteTimeUtc -eq $before) 'Second Send run is idempotent (no re-copy)'

    # ---- Receive ----
    $inbound = Join-Path $root 'Inbound'; $work = Join-Path $root 'work'
    [void](New-Item -ItemType Directory -Path $inbound)
    $recvConfig = Join-Path $root 'recv.json'
    Write-Json $recvConfig @{ oneDriveRoot = $oneDrive; roles = @('TITAN', 'HELIOS'); inboundFolder = $inbound; workFolder = $work; lookbackDays = 1; stableSeconds = 0 }
    # tamper with one file after the manifest was written
    [IO.File]::WriteAllText((Join-Path $dayFolder 'stock.csv'), 'a,b,TAMPERED')
    $code = Invoke-Script 'Receive-ShopDailyFiles.ps1' @('-ConfigPath', $recvConfig, '-ReportDate', $day)
    Assert-True ($code -eq 2) 'Receive exits 2 (HELIOS missing, one file quarantined)'
    $zip = Join-Path $inbound "TITAN-$day.zip"
    Assert-True (Test-Path $zip) 'Receive places TITAN-<day>.zip in Inbound'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $entries = [IO.Compression.ZipFile]::OpenRead($zip); $names = @($entries.Entries | ForEach-Object { $_.FullName }); $entries.Dispose()
    Assert-True ($names -contains 'sales.xlsx' -and $names -contains 'ETP-Daily-Inputs-test.xlsx' -and $names -notcontains 'stock.csv') 'Zip holds the verified files only'
    Assert-True (@(Get-ChildItem (Join-Path $work "Quarantine\TITAN\$day") -Filter '*.reason.txt' -ErrorAction SilentlyContinue).Count -eq 1) 'Tampered file is quarantined with a reason'
    Assert-True ((Get-Content -Raw (Join-Path $work "Logs\missing-$day.txt")) -match 'HELIOS') 'Missing role HELIOS reported'
    Assert-True (-not (Test-Path (Join-Path $dayFolder 'sales.xlsx'))) 'Taken files left OneDrive (archived locally)'
    Remove-Item $zip -Force
    $code = Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig)
    $code = Invoke-Script 'Receive-ShopDailyFiles.ps1' @('-ConfigPath', $recvConfig, '-ReportDate', $day)
    Assert-True (-not (Test-Path $zip)) 'A re-sent, already-taken file is not imported twice (no new r1 zip)'
    $zip2 = Join-Path $inbound "TITAN-$day-r2.zip"
    $entries = [IO.Compression.ZipFile]::OpenRead($zip2); $names2 = @($entries.Entries | ForEach-Object { $_.FullName }); $entries.Dispose()
    Assert-True ($names2.Count -eq 1 -and $names2[0] -eq 'stock.csv') 'The not-yet-taken file goes in a second zip (-r2)'

    # ---- Backup encryption round trip ----
    $backups = Join-Path $root 'Backups'; [void](New-Item -ItemType Directory -Path $backups)
    $bak = Join-Path $backups 'EtpReporting-20261011-010101-abc.bak'
    $bytes = New-Object byte[] (3MB + 123); (New-Object Random 7).NextBytes($bytes); [IO.File]::WriteAllBytes($bak, $bytes)
    $sha = ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)) -replace '-', '')
    Write-Json (Join-Path $backups 'EtpReporting-latest-verified.json') @{ verified = $true; backupPath = $bak; sha256 = $sha }
    $keyFile = Join-Path $root 'keys\k.key'
    $backupConfig = Join-Path $root 'backup.json'
    Write-Json $backupConfig @{ backupFolder = $backups; database = 'EtpReporting'; oneDriveRoot = $oneDrive; keyFile = $keyFile; keepLast = 2 }
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-NewKey')) -eq 0 -and (Test-Path $keyFile)) 'NewKey creates the key file'
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig)) -eq 0) 'Backup copy exits 0'
    $enc = Join-Path $oneDrive 'ETP-Backups\EtpReporting-20261011-010101-abc.bak.enc'
    Assert-True (Test-Path $enc) 'Encrypted backup is in OneDrive\ETP-Backups'
    Assert-True ((Get-Item $enc).Length -gt $bytes.Length) 'Encrypted file is not the plain file'
    $restored = Join-Path $root 'restored.bak'
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-Decrypt', '-InputFile', $enc, '-OutputFile', $restored)) -eq 0) 'Decrypt exits 0'
    Assert-True ((Get-FileHash $restored -Algorithm SHA256).Hash -eq $sha) 'Decrypted file equals the original'
    $raw = [IO.File]::ReadAllBytes($enc); $raw[100] = $raw[100] -bxor 1; [IO.File]::WriteAllBytes((Join-Path $root 'bad.enc'), $raw)
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-Decrypt', '-InputFile', (Join-Path $root 'bad.enc'), '-OutputFile', (Join-Path $root 'bad.bak'))) -eq 1) 'A damaged encrypted file is refused'
    $badKeyConfig = Join-Path $root 'badkey.json'
    Write-Json $badKeyConfig @{ backupFolder = $backups; oneDriveRoot = $oneDrive; keyFile = (Join-Path $oneDrive 'k.key') }
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $badKeyConfig, '-NewKey')) -eq 1) 'A key file inside OneDrive is refused'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }

if ($failures.Count -gt 0) { Write-Host "$($failures.Count) check(s) failed."; exit 1 }
Write-Host 'All checks passed.'; exit 0
