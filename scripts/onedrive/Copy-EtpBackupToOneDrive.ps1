<#
.SYNOPSIS
  Runs on Workpc after ETP's nightly VERIFIED backup. Encrypts the newest .bak and copies it to  <OneDrive>\ETP-Backups\ ,
  verifies it by hash, waits for OneDrive to report it uploaded, and keeps the newest 3 daily copies plus one per month.

.DESCRIPTION
  What it does, in order:
   1. Refuses to start when OneDrive is not running for this Windows user (nothing would upload).
   2. Reads <backupFolder>\<database>-latest-verified.json (written by ETP's backup script only after the backup verified).
   3. Re-hashes the .bak and compares with the receipt. Mismatch = stop (never uploads a doubtful backup).
   4. Encrypts it locally (AES-256-CBC + HMAC-SHA256, "encrypt then MAC") with a key from a KEY FILE, to
      <backupFolder>\OneDriveStaging\<name>.bak.enc, then decrypts it again in memory and checks the original SHA-256.
      Format ETPBAK02: the header holds a key id byte and the file's own name, both covered by the MAC, so a renamed
      (for example older) backup is refused at restore time (AUDIT-2 S-12).
   5. Copies it into OneDrive (as .part, then renames), re-hashes the OneDrive copy and compares.
   6. Writes the receipt beside it with the key's fingerprint added (so a USB key can be matched to the backups).
   7. Waits until OneDrive reports the file uploaded (syncWaitMinutes). Fails on a OneDrive error or a stuck upload.
   8. Retention: keeps the newest keepLast copies (decision 35: 3) plus the first copy of each of the newest
      keepMonthly months (default 6) (AUDIT-3 G7). Older copies and their receipts are removed.
  The KEY FILE is never copied anywhere and MUST NOT live in OneDrive (the script refuses if it does). Keep two USB
  copies in two places and the key on paper: without the key the OneDrive backups cannot be opened. Check a USB copy
  with -VerifyKey -KeyFile <usb path>: the fingerprint must match. Nothing here holds a password.

  Windows PowerShell 5.1 compatible. Config: workpc-backup.config.json next to the script.

.PARAMETER NewKey        Create the key file (refuses to overwrite). Prints its fingerprint; never prints the key.
.PARAMETER VerifyKey     Print the fingerprint of the key file (the configured one, or -KeyFile), e.g. a USB copy.
.PARAMETER Decrypt       Restore-side helper: -InputFile x.bak.enc -OutputFile x.bak (verifies the MAC first).
.PARAMETER KeyFile       Key file to use instead of the config's (for -Decrypt and -VerifyKey on a new PC no config
                         file is needed).
.PARAMETER AllowRenamed  -Decrypt only: accept a file whose name differs from the name sealed inside it.
.PARAMETER WhatIf        Run the checks and say what would be copied; encrypts and copies nothing.

Exit codes: 0 done, 1 error.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'workpc-backup.config.json'),
    [switch]$NewKey,
    [switch]$VerifyKey,
    [switch]$Decrypt,
    [string]$InputFile,
    [string]$OutputFile,
    [string]$KeyFile,
    [switch]$AllowRenamed
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneDriveCommon.ps1')

$script:MagicV1 = 'ETPBAK01'   # 1.9.x-1.11.1: magic(8) + IV(16) + ciphertext + MAC(32). Still decrypted.
$script:MagicV2 = 'ETPBAK02'   # 1.11.2+:      magic(8) + keyId(1) + nameLength(2, LE) + name(UTF-8) + IV(16) + ciphertext + MAC(32)
$script:MacLength = 32
$script:BackupKeyBytes = 64

function Read-EtpKeyFile {
    param([string]$KeyFile)
    if (-not (Test-Path -LiteralPath $KeyFile -PathType Leaf)) { throw "Key file not found: $KeyFile. Run this script once with -NewKey, then store copies of the key off this PC." }
    $bytes = Read-EtpSecretKeyBytes -KeyFile $KeyFile -Bytes $script:BackupKeyBytes -What 'backup key'
    $fingerprint = Get-EtpKeyFingerprint -KeyBytes $bytes
    return [pscustomobject]@{
        Encryption  = [byte[]]$bytes[0..31]
        Mac         = [byte[]]$bytes[32..63]
        Fingerprint = $fingerprint
        IdByte      = [Convert]::ToByte($fingerprint.Substring(0, 2), 16)
    }
}

function Protect-EtpFile {
    # ETPBAK02 header + AES-256-CBC ciphertext + HMAC-SHA256(32) over everything before it.
    param([string]$Source, [string]$Destination, $Key, [Parameter(Mandatory)][string]$SealedName)
    $nameBytes = [Text.Encoding]::UTF8.GetBytes($SealedName)
    if ($nameBytes.Length -gt 1024) { throw 'Backup file name is too long.' }
    $aes = [Security.Cryptography.Aes]::Create()
    $aes.KeySize = 256; $aes.Mode = [Security.Cryptography.CipherMode]::CBC; $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
    $aes.Key = [byte[]]$Key.Encryption; $aes.GenerateIV()
    try {
        $out = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $in = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            $magic = [Text.Encoding]::ASCII.GetBytes($script:MagicV2)
            $out.Write($magic, 0, $magic.Length)
            $out.WriteByte([byte]$Key.IdByte)
            $out.WriteByte([byte]($nameBytes.Length -band 0xFF)); $out.WriteByte([byte](($nameBytes.Length -shr 8) -band 0xFF))
            $out.Write($nameBytes, 0, $nameBytes.Length)
            $out.Write($aes.IV, 0, 16)
            $encryptor = $aes.CreateEncryptor()
            $crypto = New-Object Security.Cryptography.CryptoStream($out, $encryptor, [Security.Cryptography.CryptoStreamMode]::Write)
            $in.CopyTo($crypto, 1MB)
            $crypto.FlushFinalBlock()
        }
        finally { $in.Dispose(); $out.Dispose() }
    }
    finally { $aes.Dispose() }

    $hmac = New-Object Security.Cryptography.HMACSHA256(,[byte[]]$Key.Mac)
    try {
        $stream = [IO.File]::Open($Destination, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { $mac = $hmac.ComputeHash($stream) } finally { $stream.Dispose() }
    }
    finally { $hmac.Dispose() }
    $append = [IO.File]::Open($Destination, [IO.FileMode]::Append, [IO.FileAccess]::Write)
    try { $append.Write($mac, 0, $mac.Length) } finally { $append.Dispose() }
}

function Read-EtpExactly {
    param([IO.Stream]$Stream, [int]$Count)
    $buffer = New-Object byte[] $Count
    $read = 0
    while ($read -lt $Count) {
        $got = $Stream.Read($buffer, $read, $Count - $read)
        if ($got -le 0) { throw 'Encrypted file is damaged (header cut short).' }
        $read += $got
    }
    return ,$buffer
}

function Unprotect-EtpFile {
    # Verifies the MAC FIRST, then decrypts to OutputPath, or only hashes the plaintext when OutputPath is empty.
    # Returns Sha256 (hex of the plaintext), Format, SealedName (ETPBAK02 only) and KeyIdByte.
    param([string]$Source, [string]$OutputPath, $Key)
    $info = Get-Item -LiteralPath $Source
    $bodyLength = $info.Length - $script:MacLength
    if ($bodyLength -lt (8 + 16 + 16)) { throw 'Encrypted file is too short or damaged.' }
    $stream = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $hmac = New-Object Security.Cryptography.HMACSHA256(,[byte[]]$Key.Mac)
        try {
            $buffer = New-Object byte[] (1MB); $remaining = $bodyLength
            while ($remaining -gt 0) {
                $read = $stream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
                if ($read -le 0) { throw 'Unexpected end of encrypted file.' }
                [void]$hmac.TransformBlock($buffer, 0, $read, $null, 0); $remaining -= $read
            }
            [void]$hmac.TransformFinalBlock((New-Object byte[] 0), 0, 0)
            $expected = $hmac.Hash
        }
        finally { $hmac.Dispose() }
        $actual = New-Object byte[] $script:MacLength
        if ($stream.Read($actual, 0, $script:MacLength) -ne $script:MacLength) { throw 'Encrypted file is damaged (no MAC).' }
        $difference = 0
        for ($i = 0; $i -lt $script:MacLength; $i++) { $difference = $difference -bor ($expected[$i] -bxor $actual[$i]) }

        $stream.Position = 0
        $magic = [Text.Encoding]::ASCII.GetString((Read-EtpExactly $stream 8))
        if ($difference -ne 0) {
            # Unauthenticated header bytes are read only to word the error better; nothing else trusts them.
            if ($magic -eq $script:MagicV2) {
                $id = $stream.ReadByte()
                if ($id -ge 0 -and $id -ne $Key.IdByte) { throw ("Integrity check failed: this backup was made with a different key (key id {0:X2}; the key you gave has id {1:X2}). Use the key file whose fingerprint starts with {0:X2}." -f $id, $Key.IdByte) }
            }
            throw 'Integrity check failed: wrong key, or the encrypted file is damaged or was changed.'
        }

        $sealedName = $null; $keyIdByte = $null
        if ($magic -eq $script:MagicV2) {
            $keyIdByte = $stream.ReadByte()
            $lengthBytes = Read-EtpExactly $stream 2
            $nameLength = [int]$lengthBytes[0] + ([int]$lengthBytes[1] -shl 8)
            $sealedName = [Text.Encoding]::UTF8.GetString((Read-EtpExactly $stream $nameLength))
        }
        elseif ($magic -ne $script:MagicV1) { throw 'Not an ETP encrypted backup (header).' }
        $iv = Read-EtpExactly $stream 16
        $headerLength = $stream.Position

        $aes = [Security.Cryptography.Aes]::Create()
        $aes.KeySize = 256; $aes.Mode = [Security.Cryptography.CipherMode]::CBC; $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = [byte[]]$Key.Encryption; $aes.IV = $iv
        $sha = [Security.Cryptography.SHA256]::Create()
        $output = $null
        try {
            if ($OutputPath) { $output = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None) }
            $decryptor = $aes.CreateDecryptor()
            $remaining = $bodyLength - $headerLength
            if ($remaining -lt 16) { throw 'Encrypted file is too short or damaged.' }
            $block = New-Object byte[] (1MB); $plain = New-Object byte[] (1MB + 32)
            while ($remaining -gt 0) {
                $want = [int][Math]::Min($block.Length, $remaining)
                $read = 0
                while ($read -lt $want) {   # fill the block fully: the decryptor needs whole 16-byte blocks
                    $got = $stream.Read($block, $read, $want - $read)
                    if ($got -le 0) { throw 'Unexpected end of encrypted file.' }
                    $read += $got
                }
                $remaining -= $read
                if ($remaining -eq 0) {
                    $final = $decryptor.TransformFinalBlock($block, 0, $read)
                    if ($final.Length -gt 0) { [void]$sha.TransformBlock($final, 0, $final.Length, $null, 0); if ($output) { $output.Write($final, 0, $final.Length) } }
                }
                else {
                    $count = $decryptor.TransformBlock($block, 0, $read, $plain, 0)
                    if ($count -gt 0) { [void]$sha.TransformBlock($plain, 0, $count, $null, 0); if ($output) { $output.Write($plain, 0, $count) } }
                }
            }
            [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
            return [pscustomobject]@{
                Sha256     = ([BitConverter]::ToString($sha.Hash) -replace '-', '')
                Format     = $magic
                SealedName = $sealedName
                KeyIdByte  = $keyIdByte
            }
        }
        finally { if ($output) { $output.Dispose() }; $sha.Dispose(); $aes.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-EtpBackupsToPrune {
    # Pure: from the encrypted backup names in ETP-Backups, the ones to delete. Keeps the newest $KeepLast and, for each
    # of the newest $KeepMonthly calendar months, that month's FIRST copy (a stable monthly restore point).
    # Names start <database>-<yyyyMMdd>-<HHmmss>, so name order is time order.
    param([string[]]$Names, [string]$Database, [int]$KeepLast, [int]$KeepMonthly)
    $sorted = @($Names | Sort-Object -Descending)
    $keep = @{}
    foreach ($n in @($sorted | Select-Object -First $KeepLast)) { $keep[$n] = $true }
    if ($KeepMonthly -gt 0) {
        $pattern = '^' + [regex]::Escape($Database) + '-(\d{6})\d{2}-'
        $firstOfMonth = @{}
        foreach ($n in $sorted) {   # newest first, so the last one seen for a month is its first copy
            $m = [regex]::Match($n, $pattern)
            if ($m.Success) { $firstOfMonth[$m.Groups[1].Value] = $n }
        }
        foreach ($month in @($firstOfMonth.Keys | Sort-Object -Descending | Select-Object -First $KeepMonthly)) { $keep[$firstOfMonth[$month]] = $true }
    }
    return @($sorted | Where-Object { -not $keep.ContainsKey($_) })
}

function Copy-EtpBackupToOneDrive {
    param([string]$ConfigPath, [bool]$DryRun)
    $config = Read-EtpOneDriveConfig -Path $ConfigPath
    $backupFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'backupFolder' "$env:ProgramData\EtpReporting\Backups"))
    $database = [string](Get-EtpConfigValue $config 'database' 'EtpReporting')
    $keyFile = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'keyFile' ''))
    $keepLast = [int](Get-EtpConfigValue $config 'keepLast' 3)
    $keepMonthly = [int](Get-EtpConfigValue $config 'keepMonthly' 6)
    $checkClient = [bool](Get-EtpConfigValue $config 'checkOneDriveClient' $true)
    $syncWaitMinutes = [int](Get-EtpConfigValue $config 'syncWaitMinutes' 30)
    if ($keepLast -lt 1) { throw 'keepLast must be at least 1.' }
    if ($keepMonthly -lt 0) { throw 'keepMonthly cannot be negative.' }
    if ([string]::IsNullOrWhiteSpace($keyFile)) { throw "Config 'keyFile' is required (a local path outside OneDrive)." }
    $configuredRoot = [string](Get-EtpConfigValue $config 'oneDriveRoot' '')
    $oneDrive = Resolve-EtpOneDriveRoot -Override $configuredRoot
    Assert-EtpKeyOutsideOneDrive -KeyFile $keyFile -ConfiguredRoot $oneDrive
    $key = Read-EtpKeyFile -KeyFile $keyFile

    $staging = Join-Path $backupFolder 'OneDriveStaging'
    $logFile = Join-Path $staging 'copy.log.jsonl'
    if ($checkClient) { Assert-EtpOneDriveClientRunning -LogFile $logFile }

    $pointer = Join-Path $backupFolder "$database-latest-verified.json"
    if (-not (Test-Path -LiteralPath $pointer -PathType Leaf)) { throw "No verified backup receipt at $pointer. Run ETP's backup first." }
    $receipt = Get-Content -Raw -LiteralPath $pointer -Encoding UTF8 | ConvertFrom-Json
    if (-not $receipt.verified) { throw 'The latest backup receipt is not marked verified.' }
    $bak = [string]$receipt.backupPath
    if (-not (Test-EtpPathInside -Path $bak -Folder $backupFolder)) { throw 'The receipt points outside the backup folder; refusing.' }
    if (-not (Test-Path -LiteralPath $bak -PathType Leaf)) { throw "Backup file named in the receipt is missing: $bak" }
    $expected = ([string]$receipt.sha256).ToUpperInvariant()
    $sourceHash = Get-EtpFileSha256 -Path $bak
    if ($sourceHash -ne $expected) { throw 'The .bak no longer matches its verified receipt (SHA-256). Not copying it.' }

    $destFolder = Join-Path $oneDrive 'ETP-Backups'
    $name = (Split-Path -Leaf $bak) + '.enc'
    $destination = Join-Path $destFolder $name
    if ((Test-Path -LiteralPath $destination) -and -not $DryRun) { Write-Host "Already in OneDrive: $name"; return }
    if ($DryRun) { Write-Host "WhatIf: would encrypt $bak and copy it to $destination, then keep the newest $keepLast plus $keepMonthly monthly."; return }

    if (-not (Test-Path -LiteralPath $staging)) { [void](New-Item -ItemType Directory -Path $staging -Force) }
    $stagedFile = Join-Path $staging $name
    try {
        Protect-EtpFile -Source $bak -Destination $stagedFile -Key $key -SealedName $name
        $check = Unprotect-EtpFile -Source $stagedFile -OutputPath $null -Key $key
        if ($check.Sha256 -ne $expected -or $check.SealedName -ne $name) { throw 'Round-trip check failed: the encrypted file does not decrypt back to the original backup.' }
        $encryptedHash = Get-EtpFileSha256 -Path $stagedFile

        if (-not (Test-Path -LiteralPath $destFolder)) { [void](New-Item -ItemType Directory -Path $destFolder -Force) }
        $part = "$destination.part"
        Copy-Item -LiteralPath $stagedFile -Destination $part -Force
        if ((Get-EtpFileSha256 -Path $part) -ne $encryptedHash) { Remove-Item -LiteralPath $part -Force; throw 'The OneDrive copy did not match the encrypted file (SHA-256). Removed; run again.' }
        Move-Item -LiteralPath $part -Destination $destination

        # The receipt copy also says which key opens the file (G2): match it with a USB key via -VerifyKey.
        $receipt | Add-Member -NotePropertyName oneDriveCopy -NotePropertyValue ([pscustomobject][ordered]@{
            encryptedFile   = $name
            encryptedSha256 = $encryptedHash
            format          = $script:MagicV2
            keyFingerprint  = $key.Fingerprint
            copiedLocal     = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        }) -Force
        Write-EtpTextAtomically -Path (Join-Path $destFolder ("$name.receipt.json")) -Text ($receipt | ConvertTo-Json -Depth 10)
        Write-EtpLogLine -LogFile $logFile -Event 'Copied' -Data @{ file = $name; plainSha256 = $expected; encryptedSha256 = $encryptedHash; bytes = (Get-Item -LiteralPath $destination).Length; keyFingerprint = $key.Fingerprint }
        Write-Host "Copied and verified: $name"
    }
    finally { if (Test-Path -LiteralPath $stagedFile) { Remove-Item -LiteralPath $stagedFile -Force } }

    # Success only once OneDrive has the file (G4). No status from Windows = warning in the log, not a failure.
    $sync = Wait-EtpOneDriveUploaded -Path $destination -TimeoutMinutes $syncWaitMinutes
    Write-EtpLogLine -LogFile $logFile -Event ("Sync$sync") -Data @{ file = $name }
    if ($sync -eq 'Synced') { Write-Host 'OneDrive reports the file uploaded.' }
    else { Write-Warning 'Windows did not report a OneDrive status for the file; check the green tick in File Explorer.' }

    $all = @(Get-ChildItem -LiteralPath $destFolder -Filter "$database-*.bak.enc" -File | ForEach-Object { $_.Name })
    foreach ($oldName in @(Get-EtpBackupsToPrune -Names $all -Database $database -KeepLast $keepLast -KeepMonthly $keepMonthly)) {
        $old = Join-Path $destFolder $oldName
        Remove-Item -LiteralPath $old -Force
        if (Test-Path -LiteralPath "$old.receipt.json") { Remove-Item -LiteralPath "$old.receipt.json" -Force }
        Write-EtpLogLine -LogFile $logFile -Event 'Pruned' -Data @{ file = $oldName }
    }
}

function Resolve-EtpRequestedKeyFile {
    # -KeyFile wins (a new PC needs no config file); otherwise the config's keyFile.
    param([string]$KeyFile, [string]$ConfigPath)
    if (-not [string]::IsNullOrWhiteSpace($KeyFile)) { return [Environment]::ExpandEnvironmentVariables($KeyFile) }
    $config = Read-EtpOneDriveConfig -Path $ConfigPath
    $path = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'keyFile' ''))
    if ([string]::IsNullOrWhiteSpace($path)) { throw "Config 'keyFile' is required, or pass -KeyFile." }
    return $path
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($NewKey) {
            $path = Resolve-EtpRequestedKeyFile -KeyFile $KeyFile -ConfigPath $ConfigPath
            $root = ''
            if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) { $root = [string](Get-EtpConfigValue (Read-EtpOneDriveConfig -Path $ConfigPath) 'oneDriveRoot' '') }
            Assert-EtpKeyOutsideOneDrive -KeyFile $path -ConfiguredRoot $root
            if ($PSCmdlet.ShouldProcess($path, 'Create backup key file')) {
                if (Test-Path -LiteralPath $path) { throw "Key file already exists: $path. It is not replaced (older backups would become unreadable)." }
                $fingerprint = New-EtpSecretKeyFile -KeyFile $path -Bytes $script:BackupKeyBytes
                Write-Host "Key file created: $path"
                Write-Host "Fingerprint: $(Format-EtpFingerprint $fingerprint)"
                Write-Host 'NOW copy it to TWO USB sticks kept in two places, and write the key on paper for the safe. After copying, run'
                Write-Host '  Copy-EtpBackupToOneDrive.ps1 -VerifyKey -KeyFile <USB path>  - the fingerprint must be the same as above.'
                Write-Host 'It must never go into OneDrive, e-mail or chat. Without it the OneDrive backups cannot be opened.'
            }
            exit 0
        }
        if ($VerifyKey) {
            $path = Resolve-EtpRequestedKeyFile -KeyFile $KeyFile -ConfigPath $ConfigPath
            $key = Read-EtpKeyFile -KeyFile $path
            Write-Host "Key file: $path"
            Write-Host "Fingerprint: $(Format-EtpFingerprint $key.Fingerprint)"
            Write-Host 'Compare it with the fingerprint shown when the key was made, or with keyFingerprint in any .receipt.json in OneDrive\ETP-Backups.'
            exit 0
        }
        if ($Decrypt) {
            if (-not $InputFile -or -not $OutputFile) { throw '-Decrypt needs -InputFile and -OutputFile.' }
            if (Test-Path -LiteralPath $OutputFile) { throw "Output file already exists: $OutputFile" }
            $key = Read-EtpKeyFile -KeyFile (Resolve-EtpRequestedKeyFile -KeyFile $KeyFile -ConfigPath $ConfigPath)
            $result = Unprotect-EtpFile -Source $InputFile -OutputPath $OutputFile -Key $key
            $actualName = Split-Path -Leaf $InputFile
            if ($null -ne $result.SealedName -and $result.SealedName -ne $actualName -and -not $AllowRenamed) {
                Remove-Item -LiteralPath $OutputFile -Force
                throw "This file was sealed as '$($result.SealedName)' but is named '$actualName'. A renamed file can be an OLDER backup passed off as a newer one. Nothing was restored. If you renamed it yourself, run again with -AllowRenamed."
            }
            if ($result.Format -eq $script:MagicV1) { Write-Warning 'Old format (ETPBAK01): the file name is not sealed inside it; compare the SHA-256 below with its receipt.' }
            Write-Host "Decrypted. SHA-256 of the restored .bak: $($result.Sha256)"
            $receiptPath = "$InputFile.receipt.json"
            if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
                $receipt = Get-Content -Raw -LiteralPath $receiptPath -Encoding UTF8 | ConvertFrom-Json
                if (([string](Get-EtpConfigValue $receipt 'sha256' '')).ToUpperInvariant() -eq $result.Sha256) { Write-Host 'It matches the receipt beside the encrypted file.' }
                else { Remove-Item -LiteralPath $OutputFile -Force; throw 'The restored .bak does NOT match the receipt beside the encrypted file. Nothing was restored; check you have the right pair of files.' }
            }
            else { Write-Warning 'No .receipt.json beside the encrypted file: compare the SHA-256 above with the receipt by hand.' }
            exit 0
        }
        Copy-EtpBackupToOneDrive -ConfigPath $ConfigPath -DryRun ([bool]$WhatIfPreference)
        exit 0
    }
    catch { Write-Error $_.Exception.Message; exit 1 }
}
