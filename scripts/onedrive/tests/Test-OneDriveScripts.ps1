<#
Self-test for the OneDrive scripts. Plain PowerShell (5.1), no Pester needed. Works ONLY inside a fresh temp folder with a
fake "OneDrive"; never touches real ETP or OneDrive folders, and registers no task. Run:  powershell -File Test-OneDriveScripts.ps1
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
function Invoke-Common($expression) {
    # Runs one expression with OneDriveCommon.ps1 dot-sourced, in a child Windows PowerShell; returns its text output.
    $command = ". '{0}'; {1}" -f (Join-Path $scripts 'OneDriveCommon.ps1'), $expression
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    return ((& powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded 2>&1 | Out-String).Trim())
}

try {
    $day = (Get-Date).ToString('yyyy-MM-dd')
    $oneDrive = Join-Path $root 'OneDrive'; $source = Join-Path $root 'exports'; $inputs = Join-Path $root 'inputs'
    foreach ($d in $oneDrive, $source, $inputs) { [void](New-Item -ItemType Directory -Path $d) }
    [IO.File]::WriteAllText((Join-Path $source 'sales.xlsx'), 'fake workbook one')
    [IO.File]::WriteAllText((Join-Path $source 'stock.csv'), 'a,b')
    [IO.File]::WriteAllText((Join-Path $inputs 'ETP-Daily-Inputs-test.xlsx'), 'fake inputs')

    # ---- Shared helpers ----
    Assert-True ((Invoke-Common "ConvertTo-EtpPcNameToken `"shop pc's & co`"") -eq 'SHOP_PC_S___CO') 'PC names are reduced to A-Z 0-9 _ - (no quote or ampersand survives)'

    # ---- Send ----
    $titanKey = Join-Path $root 'shopkeys\titan.key'
    $sendConfig = Join-Path $root 'send.json'
    Write-Json $sendConfig @{ role = 'TITAN'; sourceFolder = $source; dailyInputsFolder = $inputs; oneDriveRoot = $oneDrive; stableSeconds = 0; logFolder = (Join-Path $root 'logs'); signingKeyFile = $titanKey; checkOneDriveClient = $false }
    $code = Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig, '-WhatIf')
    Assert-True ($code -eq 0 -and -not (Test-Path (Join-Path $oneDrive 'ETP-Inbound'))) 'Send -WhatIf copies nothing'
    Assert-True ((Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig)) -eq 1 -and $script:LastOutput -match 'NewSigningKey') 'Send without a signing key refuses and says how to make one'
    Assert-True ((Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig, '-NewSigningKey')) -eq 0 -and (Test-Path $titanKey) -and $script:LastOutput -match 'Fingerprint: [0-9A-F]{4} ') 'NewSigningKey creates the key and prints its fingerprint'
    Assert-True ((Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig, '-NewSigningKey')) -eq 1) 'NewSigningKey never replaces an existing key'
    $badSignConfig = Join-Path $root 'send-badkey.json'
    Write-Json $badSignConfig @{ role = 'TITAN'; sourceFolder = $source; oneDriveRoot = $oneDrive; signingKeyFile = (Join-Path $oneDrive 'k.key'); checkOneDriveClient = $false }
    Assert-True ((Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $badSignConfig, '-NewSigningKey')) -eq 1 -and -not (Test-Path (Join-Path $oneDrive 'k.key'))) 'A signing key inside OneDrive is refused'
    $acl = Get-Acl $titanKey
    Assert-True ($acl.AreAccessRulesProtected -and @($acl.Access | Where-Object { $_.IdentityReference.Value -match 'Users$|Everyone|Authenticated' }).Count -eq 0) 'Signing key ACL: inheritance off, no Users/Everyone'

    $code = Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig)
    $dayFolder = Join-Path $oneDrive "ETP-Inbound\TITAN\$day"
    Assert-True ($code -eq 0) 'Send exits 0'
    Assert-True ($script:LastOutput -match 'Sent 3 file\(s\) \(3 new, 0 already in OneDrive\), skipped 0') 'Send says "sent N, skipped M"'
    Assert-True ((Get-Content -Raw (Join-Path $root 'logs\last-send.txt')) -match 'skipped 0') 'Send writes last-send.txt for the staff'
    Assert-True ((Test-Path (Join-Path $dayFolder 'sales.xlsx')) -and (Test-Path (Join-Path $dayFolder 'ETP-Daily-Inputs-test.xlsx')) -and (Test-Path (Join-Path $dayFolder 'manifest.json'))) 'Send copies files and writes the manifest'
    $manifest = Get-Content -Raw (Join-Path $dayFolder 'manifest.json') | ConvertFrom-Json
    Assert-True (@($manifest.files).Count -eq 3 -and $manifest.role -eq 'TITAN') 'Manifest lists 3 files for TITAN'
    Assert-True ($manifest.schemaVersion -eq 2 -and ([string]$manifest.hmac) -match '^[0-9A-F]{64}$' -and ([string]$manifest.pcName) -match '^[A-Z0-9_-]+$') 'Manifest is schema 2, signed, with a tokenised PC name'
    Assert-True ((Test-Path (Join-Path $source 'sales.xlsx')) -and (Test-Path (Join-Path $inputs 'ETP-Daily-Inputs-test.xlsx'))) 'Source files are untouched'
    $before = (Get-Item (Join-Path $dayFolder 'sales.xlsx')).LastWriteTimeUtc
    $code = Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig)
    Assert-True ($code -eq 0 -and (Get-Item (Join-Path $dayFolder 'sales.xlsx')).LastWriteTimeUtc -eq $before) 'Second Send run is idempotent (no re-copy)'

    # A manifest planted in OneDrive must not be re-signed by Send (no signing oracle).
    $planted = Get-Content -Raw (Join-Path $dayFolder 'manifest.json') | ConvertFrom-Json
    $planted.files = @($planted.files) + @([pscustomobject]@{ name = 'evil.csv'; sizeBytes = 3; sha256 = ('A' * 64) })
    Write-Json (Join-Path $dayFolder 'manifest.json') $planted
    $code = Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig)
    $resigned = Get-Content -Raw (Join-Path $dayFolder 'manifest.json') | ConvertFrom-Json
    Assert-True ($code -eq 0 -and @($resigned.files | Where-Object { $_.name -eq 'evil.csv' }).Count -eq 0 -and @($resigned.files).Count -eq 3) 'Send drops entries from a manifest it did not sign'

    # ---- Receive ----
    $inbound = Join-Path $root 'Inbound'; $work = Join-Path $root 'work'; $wpKeys = Join-Path $root 'workpc-keys'
    foreach ($d in $inbound, $wpKeys) { [void](New-Item -ItemType Directory -Path $d) }
    Copy-Item $titanKey (Join-Path $wpKeys 'TITAN.key')
    $pc = Invoke-Common 'ConvertTo-EtpPcNameToken $env:COMPUTERNAME'

    # HELIOS: a forged day (signature made without the real key) is refused and nothing is taken.
    $heliosKey = Join-Path $root 'shopkeys\helios.key'
    $heliosConfig = Join-Path $root 'send-helios.json'
    Write-Json $heliosConfig @{ role = 'HELIOS'; sourceFolder = $source; oneDriveRoot = $oneDrive; signingKeyFile = $heliosKey; checkOneDriveClient = $false }
    [void](Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $heliosConfig, '-NewSigningKey'))
    Copy-Item $heliosKey (Join-Path $wpKeys 'HELIOS.key')
    $heliosDay = Join-Path $oneDrive "ETP-Inbound\HELIOS\$day"; [void](New-Item -ItemType Directory -Path $heliosDay -Force)
    [IO.File]::WriteAllText((Join-Path $heliosDay 'forged.csv'), 'x,y')
    $forgedHash = (Get-FileHash (Join-Path $heliosDay 'forged.csv') -Algorithm SHA256).Hash
    Write-Json (Join-Path $heliosDay 'manifest.json') ([ordered]@{ schemaVersion = 2; role = 'HELIOS'; day = $day; pcName = 'OTHERPC'; keyId = '0000000000000000'; createdLocal = 'x'; files = @([ordered]@{ name = 'forged.csv'; sizeBytes = 3; sha256 = $forgedHash }); hmac = ('0' * 64) })

    # TITAN from the wrong PC (config expects another name): refused, nothing taken.
    $wrongPcConfig = Join-Path $root 'recv-wrongpc.json'
    Write-Json $wrongPcConfig @{ oneDriveRoot = $oneDrive; roles = @('TITAN'); pcNames = @{ TITAN = 'SOME-OTHER-PC' }; shopKeysFolder = $wpKeys; inboundFolder = $inbound; workFolder = $work; lookbackDays = 1; stableSeconds = 0; checkOneDriveClient = $false }
    $code = Invoke-Script 'Receive-ShopDailyFiles.ps1' @('-ConfigPath', $wrongPcConfig, '-ReportDate', $day)
    Assert-True ($code -eq 2 -and -not (Test-Path (Join-Path $inbound "TITAN-$day.zip")) -and (Test-Path (Join-Path $work "Quarantine\TITAN\$day\manifest.json.reason.txt"))) 'A manifest from a PC other than the configured one is refused'
    Assert-True ((Test-Path (Join-Path $dayFolder 'sales.xlsx'))) 'Files of a refused manifest stay in OneDrive'
    $dupConfig = Join-Path $root 'recv-dup.json'
    Write-Json $dupConfig @{ oneDriveRoot = $oneDrive; roles = @('TITAN', 'HELIOS'); pcNames = @{ TITAN = 'PC1'; HELIOS = 'pc1' }; shopKeysFolder = $wpKeys; inboundFolder = $inbound; workFolder = $work; checkOneDriveClient = $false }
    Assert-True ((Invoke-Script 'Receive-ShopDailyFiles.ps1' @('-ConfigPath', $dupConfig, '-ReportDate', $day)) -eq 1) 'A config naming one PC for two roles is refused'
    [void](Invoke-Script 'Send-ShopDailyFiles.ps1' @('-ConfigPath', $sendConfig))   # the shop sends again: a fresh signed manifest

    $recvConfig = Join-Path $root 'recv.json'
    Write-Json $recvConfig @{ oneDriveRoot = $oneDrive; roles = @('TITAN', 'HELIOS'); pcNames = @{ TITAN = $pc; HELIOS = 'HELIOS-PC' }; shopKeysFolder = $wpKeys; inboundFolder = $inbound; workFolder = $work; lookbackDays = 1; stableSeconds = 0; checkOneDriveClient = $false }
    Assert-True ((Invoke-Script 'Receive-ShopDailyFiles.ps1' @('-ConfigPath', $recvConfig, '-ShowKeyFingerprints')) -eq 0 -and $script:LastOutput -match 'TITAN\s+PC .*fingerprint [0-9A-F]{4} ') 'Receive -ShowKeyFingerprints lists each role''s key'
    # tamper with one file after the manifest was written
    [IO.File]::WriteAllText((Join-Path $dayFolder 'stock.csv'), 'a,b,TAMPERED')
    $code = Invoke-Script 'Receive-ShopDailyFiles.ps1' @('-ConfigPath', $recvConfig, '-ReportDate', $day)
    Assert-True ($code -eq 2) 'Receive exits 2 (HELIOS forged, one file quarantined)'
    $zip = Join-Path $inbound "TITAN-$day.zip"
    Assert-True (Test-Path $zip) 'Receive places TITAN-<day>.zip in Inbound'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $entries = [IO.Compression.ZipFile]::OpenRead($zip); $names = @($entries.Entries | ForEach-Object { $_.FullName }); $entries.Dispose()
    Assert-True ($names -contains 'sales.xlsx' -and $names -contains 'ETP-Daily-Inputs-test.xlsx' -and $names -notcontains 'stock.csv') 'Zip holds the verified files only'
    Assert-True (Test-Path (Join-Path $work "Quarantine\TITAN\$day\stock.csv.reason.txt")) 'Tampered file is quarantined with a reason'
    Assert-True (-not (Test-Path (Join-Path $inbound "HELIOS-$day.zip")) -and (Test-Path (Join-Path $work "Quarantine\HELIOS\$day\manifest.json.reason.txt")) -and (Test-Path (Join-Path $heliosDay 'forged.csv'))) 'A forged HELIOS manifest is quarantined; its file is not taken'
    Assert-True ((Get-Content -Raw (Join-Path $work "Quarantine\HELIOS\$day\manifest.json.reason.txt")) -match 'different key') 'The refusal says the manifest was signed with a different key'
    Assert-True ((Get-Content -Raw (Join-Path $work "Logs\missing-$day.txt")) -match 'HELIOS') 'Missing role HELIOS reported'
    Assert-True (-not (Test-Path (Join-Path $dayFolder 'sales.xlsx')) -and (Test-Path (Join-Path $work "Archive\TITAN\$day\sales.xlsx"))) 'Taken files left OneDrive (checked copies archived locally)'
    Assert-True (@(Get-ChildItem (Join-Path $work 'Staging') -Directory -ErrorAction SilentlyContinue).Count -eq 0) 'Per-day staging copies are cleaned up'
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
    Write-Json $backupConfig @{ backupFolder = $backups; database = 'EtpReporting'; oneDriveRoot = $oneDrive; keyFile = $keyFile; keepLast = 2; keepMonthly = 3; syncWaitMinutes = 0; checkOneDriveClient = $false }
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-NewKey')) -eq 0 -and (Test-Path $keyFile)) 'NewKey creates the key file'
    $fingerprint = ([regex]::Match($script:LastOutput, 'Fingerprint: ([0-9A-F ]+)')).Groups[1].Value.Trim()
    Assert-True ($fingerprint.Length -eq 79) 'NewKey prints the key fingerprint'
    $usbKey = Join-Path $root 'usb\k.key'; [void](New-Item -ItemType Directory -Path (Split-Path $usbKey)); Copy-Item $keyFile $usbKey
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-VerifyKey', '-KeyFile', $usbKey, '-ConfigPath', (Join-Path $root 'none.json'))) -eq 0 -and $script:LastOutput -match [regex]::Escape($fingerprint)) 'VerifyKey on a USB copy shows the same fingerprint (no config needed)'

    # Retention: older copies already in OneDrive. keepLast 2 + first copy of each of the newest 3 months.
    $backupDir = Join-Path $oneDrive 'ETP-Backups'; [void](New-Item -ItemType Directory -Path $backupDir)
    $existing = '20260701-010101', '20260715-010101', '20260801-010101', '20260815-010101', '20260901-010101', '20261001-010101', '20261005-010101', '20261010-010101'
    foreach ($stamp in $existing) { [IO.File]::WriteAllText((Join-Path $backupDir "EtpReporting-$stamp-x.bak.enc"), 'old'); [IO.File]::WriteAllText((Join-Path $backupDir "EtpReporting-$stamp-x.bak.enc.receipt.json"), '{}') }

    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig)) -eq 0) 'Backup copy exits 0'
    $enc = Join-Path $backupDir 'EtpReporting-20261011-010101-abc.bak.enc'
    Assert-True (Test-Path $enc) 'Encrypted backup is in OneDrive\ETP-Backups'
    Assert-True ((Get-Item $enc).Length -gt $bytes.Length) 'Encrypted file is not the plain file'
    Assert-True ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($enc), 0, 8) -eq 'ETPBAK02') 'Encrypted file uses format ETPBAK02'
    $copyReceipt = Get-Content -Raw "$enc.receipt.json" | ConvertFrom-Json
    Assert-True ($copyReceipt.sha256 -eq $sha -and ($copyReceipt.oneDriveCopy.keyFingerprint -replace '(.{4})', '$1 ').Trim() -eq $fingerprint) 'The receipt copy records the key fingerprint'
    $left = @(Get-ChildItem $backupDir -Filter '*.bak.enc' | ForEach-Object { $_.Name.Substring(13, 8) } | Sort-Object)
    Assert-True (($left -join ',') -eq '20260801,20260901,20261001,20261010,20261011') "Retention keeps 2 newest + first of each of 3 months (left: $($left -join ','))"
    Assert-True (-not (Test-Path (Join-Path $backupDir 'EtpReporting-20260715-010101-x.bak.enc.receipt.json'))) 'Pruned copies lose their receipts too'

    $restored = Join-Path $root 'restored.bak'
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-Decrypt', '-InputFile', $enc, '-OutputFile', $restored)) -eq 0) 'Decrypt exits 0'
    Assert-True ((Get-FileHash $restored -Algorithm SHA256).Hash -eq $sha -and $script:LastOutput -match 'matches the receipt') 'Decrypted file equals the original and matches its receipt'
    $restored2 = Join-Path $root 'restored2.bak'
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-Decrypt', '-KeyFile', $usbKey, '-ConfigPath', (Join-Path $root 'none.json'), '-InputFile', $enc, '-OutputFile', $restored2)) -eq 0 -and (Get-FileHash $restored2 -Algorithm SHA256).Hash -eq $sha) 'Decrypt with -KeyFile needs no config file'
    $renamed = Join-Path $root 'EtpReporting-20261012-010101-zzz.bak.enc'; Copy-Item $enc $renamed
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-Decrypt', '-InputFile', $renamed, '-OutputFile', (Join-Path $root 'renamed.bak'))) -eq 1 -and -not (Test-Path (Join-Path $root 'renamed.bak'))) 'A renamed backup (possible rollback) is refused and nothing is left behind'
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-Decrypt', '-AllowRenamed', '-InputFile', $renamed, '-OutputFile', (Join-Path $root 'renamed.bak'))) -eq 0) '-AllowRenamed restores a file renamed on purpose'
    $raw = [IO.File]::ReadAllBytes($enc); $raw[100] = $raw[100] -bxor 1; [IO.File]::WriteAllBytes((Join-Path $root 'bad.enc'), $raw)
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-Decrypt', '-InputFile', (Join-Path $root 'bad.enc'), '-OutputFile', (Join-Path $root 'bad.bak'))) -eq 1) 'A damaged encrypted file is refused'
    $otherKey = Join-Path $root 'keys\other.key'
    [void](Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-NewKey', '-KeyFile', $otherKey, '-ConfigPath', (Join-Path $root 'none.json')))
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-Decrypt', '-KeyFile', $otherKey, '-ConfigPath', (Join-Path $root 'none.json'), '-InputFile', $enc, '-OutputFile', (Join-Path $root 'other.bak'))) -eq 1 -and $script:LastOutput -match 'different key') 'The wrong key is named as such (key id in the header)'
    # A backup made before 1.11.2 (ETPBAK01: magic + IV + ciphertext + MAC) must still decrypt.
    $k = [Convert]::FromBase64String((Get-Content -Raw $keyFile).Trim())
    $aes = [Security.Cryptography.Aes]::Create(); $aes.Key = [byte[]]$k[0..31]; $aes.GenerateIV()
    $cipher = $aes.CreateEncryptor().TransformFinalBlock($bytes, 0, $bytes.Length)
    $body = [byte[]]([Text.Encoding]::ASCII.GetBytes('ETPBAK01') + $aes.IV + $cipher)
    $mac = (New-Object Security.Cryptography.HMACSHA256(,[byte[]]$k[32..63])).ComputeHash($body)
    $v1 = Join-Path $root 'EtpReporting-20261001-010101-old.bak.enc'; [IO.File]::WriteAllBytes($v1, [byte[]]($body + $mac))
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $backupConfig, '-Decrypt', '-InputFile', $v1, '-OutputFile', (Join-Path $root 'v1.bak'))) -eq 0 -and (Get-FileHash (Join-Path $root 'v1.bak') -Algorithm SHA256).Hash -eq $sha) 'An older ETPBAK01 backup still decrypts'

    foreach ($file in @(Get-ChildItem $scripts -Filter '*.ps1') + @(Get-ChildItem $PSScriptRoot -Filter '*.ps1')) {
        $tokens = $null; $errors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
        Assert-True (@($errors).Count -eq 0) "$($file.Name) parses in this PowerShell ($($PSVersionTable.PSVersion))"
    }

    foreach ($example in @(Get-ChildItem $scripts -Filter '*.example.json')) {
        $parsed = $null; try { $parsed = Get-Content -Raw $example.FullName | ConvertFrom-Json } catch { }
        Assert-True ($null -ne $parsed) "$($example.Name) is valid JSON"
    }

    $badKeyConfig = Join-Path $root 'badkey.json'
    Write-Json $badKeyConfig @{ backupFolder = $backups; oneDriveRoot = $oneDrive; keyFile = (Join-Path $oneDrive 'k.key') }
    Assert-True ((Invoke-Script 'Copy-EtpBackupToOneDrive.ps1' @('-ConfigPath', $badKeyConfig, '-NewKey')) -eq 1) 'A key file inside OneDrive is refused'
    $keyAcl = Get-Acl $keyFile
    Assert-True ($keyAcl.AreAccessRulesProtected -and @($keyAcl.Access).Count -eq 3) 'Backup key ACL: inheritance off, exactly user + Administrators + SYSTEM'
}
finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }

if ($failures.Count -gt 0) { Write-Host "$($failures.Count) check(s) failed."; exit 1 }
Write-Host 'All checks passed.'; exit 0
