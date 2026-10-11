<#
.SYNOPSIS
  Runs on Workpc after ETP's nightly VERIFIED backup. Encrypts the newest .bak and copies it to  <OneDrive>\ETP-Backups\ ,
  verifies it by hash, and keeps only the newest N copies.

.DESCRIPTION
  What it does, in order:
   1. Reads <backupFolder>\<database>-latest-verified.json (written by ETP's backup script only after the backup verified).
   2. Re-hashes the .bak and compares with the receipt. Mismatch = stop (never uploads a doubtful backup).
   3. Encrypts it locally (AES-256-CBC + HMAC-SHA256, "encrypt then MAC") with a key from a KEY FILE, to
      <backupFolder>\OneDriveStaging\<name>.bak.enc, then decrypts it again in memory and checks the original SHA-256.
   4. Copies it into OneDrive (as .part, then renames), re-hashes the OneDrive copy and compares.
   5. Copies the small receipt json beside it, then removes older copies beyond keepLast (default 3).
  The KEY FILE is never copied anywhere and MUST NOT live in OneDrive (the script refuses if it does). Keep a copy of
  the key on a USB stick in a safe place: without the key the OneDrive backups cannot be opened. Nothing here holds a password.

  Windows PowerShell 5.1 compatible. Config: workpc-backup.config.json next to the script.

.PARAMETER NewKey   Create the key file (refuses to overwrite). Prints where it is; does not print the key.
.PARAMETER Decrypt  Restore-side helper: -InputFile x.bak.enc -OutputFile x.bak (verifies the MAC first).
.PARAMETER WhatIf   Run the checks and say what would be copied; encrypts and copies nothing.

Exit codes: 0 done, 1 error.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'workpc-backup.config.json'),
    [switch]$NewKey,
    [switch]$Decrypt,
    [string]$InputFile,
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneDriveCommon.ps1')

$script:Magic = [Text.Encoding]::ASCII.GetBytes('ETPBAK01')   # 8 bytes
$script:MacLength = 32

function Get-EtpOneDriveLocalRoots {
    $roots = @()
    foreach ($name in 'OneDrive', 'OneDriveCommercial', 'OneDriveConsumer') {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $roots += $value }
    }
    return $roots
}

function Assert-EtpKeyOutsideOneDrive {
    param([string]$KeyFile, [string]$ConfiguredRoot)
    $roots = @(@(Get-EtpOneDriveLocalRoots) + @($ConfiguredRoot) | Where-Object { $_ })
    foreach ($root in $roots) {
        if (Test-EtpPathInside -Path $KeyFile -Folder $root) { throw "The key file '$KeyFile' is inside OneDrive ($root). Move it to a local folder that OneDrive does not sync." }
    }
    if ($KeyFile -match '\\OneDrive[^\\]*\\') { throw "The key file path '$KeyFile' looks like a OneDrive folder. Use a local folder that OneDrive does not sync." }
}

function Read-EtpKeyFile {
    param([string]$KeyFile)
    if (-not (Test-Path -LiteralPath $KeyFile -PathType Leaf)) { throw "Key file not found: $KeyFile. Run this script once with -NewKey, then store a copy of the key off this PC." }
    $bytes = [Convert]::FromBase64String(((Get-Content -Raw -LiteralPath $KeyFile -Encoding ASCII).Trim()))
    if ($bytes.Length -ne 64) { throw 'Key file is not a valid ETP backup key (expected 64 bytes).' }
    return [pscustomobject]@{ Encryption = $bytes[0..31]; Mac = $bytes[32..63] }
}

function New-EtpKeyFile {
    param([string]$KeyFile)
    if (Test-Path -LiteralPath $KeyFile) { throw "Key file already exists: $KeyFile. It is not replaced (older backups would become unreadable)." }
    $directory = Split-Path -Parent $KeyFile
    if (-not (Test-Path -LiteralPath $directory)) { [void](New-Item -ItemType Directory -Path $directory -Force) }
    $bytes = New-Object byte[] 64
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    [IO.File]::WriteAllText($KeyFile, [Convert]::ToBase64String($bytes), [Text.Encoding]::ASCII)
    try {
        # Only the current user, Administrators and SYSTEM may read it.
        $acl = Get-Acl -LiteralPath $KeyFile
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-32-544', 'S-1-5-18')) {
            $rule = New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'Allow')
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $KeyFile -AclObject $acl
    }
    catch { Write-Warning "Key file created but its permissions could not be restricted: $($_.Exception.Message)" }
}

function Protect-EtpFile {
    # header(8) + IV(16) + AES-256-CBC ciphertext + HMAC-SHA256(32) over everything before it.
    param([string]$Source, [string]$Destination, $Key)
    $aes = [Security.Cryptography.Aes]::Create()
    $aes.KeySize = 256; $aes.Mode = [Security.Cryptography.CipherMode]::CBC; $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
    $aes.Key = [byte[]]$Key.Encryption; $aes.GenerateIV()
    try {
        $out = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $in = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            $out.Write($script:Magic, 0, $script:Magic.Length)
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

function Unprotect-EtpFile {
    # Verifies the MAC FIRST, then decrypts to OutputPath, or only hashes the plaintext when OutputPath is empty.
    # Returns the SHA-256 (hex) of the plaintext.
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
        if ($difference -ne 0) { throw 'Integrity check failed: wrong key, or the encrypted file is damaged or was changed.' }

        $stream.Position = 0
        $magic = New-Object byte[] 8; [void]$stream.Read($magic, 0, 8)
        if ([Text.Encoding]::ASCII.GetString($magic) -ne [Text.Encoding]::ASCII.GetString($script:Magic)) { throw 'Not an ETP encrypted backup (header).' }
        $iv = New-Object byte[] 16; [void]$stream.Read($iv, 0, 16)

        $aes = [Security.Cryptography.Aes]::Create()
        $aes.KeySize = 256; $aes.Mode = [Security.Cryptography.CipherMode]::CBC; $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $aes.Key = [byte[]]$Key.Encryption; $aes.IV = $iv
        $sha = [Security.Cryptography.SHA256]::Create()
        $output = $null
        try {
            if ($OutputPath) { $output = [IO.File]::Open($OutputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None) }
            $decryptor = $aes.CreateDecryptor()
            $remaining = $bodyLength - 8 - 16
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
            return ([BitConverter]::ToString($sha.Hash) -replace '-', '')
        }
        finally { if ($output) { $output.Dispose() }; $sha.Dispose(); $aes.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Copy-EtpBackupToOneDrive {
    param([string]$ConfigPath, [bool]$DryRun)
    $config = Read-EtpOneDriveConfig -Path $ConfigPath
    $backupFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'backupFolder' "$env:ProgramData\EtpReporting\Backups"))
    $database = [string](Get-EtpConfigValue $config 'database' 'EtpReporting')
    $keyFile = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'keyFile' ''))
    $keepLast = [int](Get-EtpConfigValue $config 'keepLast' 3)
    if ($keepLast -lt 1) { throw 'keepLast must be at least 1.' }
    if ([string]::IsNullOrWhiteSpace($keyFile)) { throw "Config 'keyFile' is required (a local path outside OneDrive)." }
    $configuredRoot = [string](Get-EtpConfigValue $config 'oneDriveRoot' '')
    $oneDrive = Resolve-EtpOneDriveRoot -Override $configuredRoot
    Assert-EtpKeyOutsideOneDrive -KeyFile $keyFile -ConfiguredRoot $oneDrive
    $key = Read-EtpKeyFile -KeyFile $keyFile

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
    if ($DryRun) { Write-Host "WhatIf: would encrypt $bak and copy it to $destination, then keep the newest $keepLast."; return }

    $staging = Join-Path $backupFolder 'OneDriveStaging'
    if (-not (Test-Path -LiteralPath $staging)) { [void](New-Item -ItemType Directory -Path $staging -Force) }
    $stagedFile = Join-Path $staging $name
    $logFile = Join-Path $staging 'copy.log.jsonl'
    try {
        Protect-EtpFile -Source $bak -Destination $stagedFile -Key $key
        if ((Unprotect-EtpFile -Source $stagedFile -OutputPath $null -Key $key) -ne $expected) { throw 'Round-trip check failed: the encrypted file does not decrypt back to the original backup.' }
        $encryptedHash = Get-EtpFileSha256 -Path $stagedFile

        if (-not (Test-Path -LiteralPath $destFolder)) { [void](New-Item -ItemType Directory -Path $destFolder -Force) }
        $part = "$destination.part"
        Copy-Item -LiteralPath $stagedFile -Destination $part -Force
        if ((Get-EtpFileSha256 -Path $part) -ne $encryptedHash) { Remove-Item -LiteralPath $part -Force; throw 'The OneDrive copy did not match the encrypted file (SHA-256). Removed; run again.' }
        Move-Item -LiteralPath $part -Destination $destination
        Copy-Item -LiteralPath $pointer -Destination (Join-Path $destFolder ("$name.receipt.json")) -Force
        Write-EtpLogLine -LogFile $logFile -Event 'Copied' -Data @{ file = $name; plainSha256 = $expected; encryptedSha256 = $encryptedHash; bytes = (Get-Item -LiteralPath $destination).Length }
        Write-Host "Copied and verified: $name"
    }
    finally { if (Test-Path -LiteralPath $stagedFile) { Remove-Item -LiteralPath $stagedFile -Force } }

    # Retention: names start with <database>-<yyyyMMdd-HHmmss>, so name order is time order.
    $all = @(Get-ChildItem -LiteralPath $destFolder -Filter "$database-*.bak.enc" -File | Sort-Object Name -Descending)
    foreach ($old in @($all | Select-Object -Skip $keepLast)) {
        Remove-Item -LiteralPath $old.FullName -Force
        $oldReceipt = "$($old.FullName).receipt.json"
        if (Test-Path -LiteralPath $oldReceipt) { Remove-Item -LiteralPath $oldReceipt -Force }
        Write-EtpLogLine -LogFile $logFile -Event 'Pruned' -Data @{ file = $old.Name }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($NewKey) {
            $config = Read-EtpOneDriveConfig -Path $ConfigPath
            $keyFile = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'keyFile' ''))
            if ([string]::IsNullOrWhiteSpace($keyFile)) { throw "Config 'keyFile' is required." }
            Assert-EtpKeyOutsideOneDrive -KeyFile $keyFile -ConfiguredRoot ([string](Get-EtpConfigValue $config 'oneDriveRoot' ''))
            if ($PSCmdlet.ShouldProcess($keyFile, 'Create backup key file')) {
                New-EtpKeyFile -KeyFile $keyFile
                Write-Host "Key file created: $keyFile"
                Write-Host 'NOW copy it to a USB stick kept somewhere safe. It must never go into OneDrive, email or chat. Without it the OneDrive backups cannot be opened.'
            }
            exit 0
        }
        if ($Decrypt) {
            if (-not $InputFile -or -not $OutputFile) { throw '-Decrypt needs -InputFile and -OutputFile.' }
            if (Test-Path -LiteralPath $OutputFile) { throw "Output file already exists: $OutputFile" }
            $config = Read-EtpOneDriveConfig -Path $ConfigPath
            $key = Read-EtpKeyFile -KeyFile ([Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'keyFile' '')))
            $hash = Unprotect-EtpFile -Source $InputFile -OutputPath $OutputFile -Key $key
            Write-Host "Decrypted. SHA-256 of the restored .bak: $hash (compare with the .receipt.json beside the encrypted file)."
            exit 0
        }
        Copy-EtpBackupToOneDrive -ConfigPath $ConfigPath -DryRun ([bool]$WhatIfPreference)
        exit 0
    }
    catch { Write-Error $_.Exception.Message; exit 1 }
}
