<#
.SYNOPSIS
1.12 (B1, decision 44 #1). The release signing key ceremony. Run ONCE, by Sagar, on Workpc, with the two USB sticks in.

.DESCRIPTION
Makes a new ECDSA P-256 key pair and writes the SAME passphrase-encrypted key file to both USB sticks
(etp-release-signing-key.json), plus the public key as text beside it. Then it prints, for paper and for the code:
  - the key id and the SHA-256 fingerprint of the public key (write the fingerprint on the paper with the passphrase);
  - the line to paste into src\Etp.Reporting.Application\Updates\ReleaseSigningKeys.cs (Trusted).

Nothing is written anywhere else: not the repository, not OneDrive, not the user profile, not a log. The private key never
leaves this PowerShell process unencrypted. Each stick is read back and opened with the passphrase before the script ends.

Losing both sticks (or the passphrase) means a new key: the next release carrying the new public key is installed once by
hand, as today. Keep the sticks in two places and the passphrase on paper, apart from the sticks.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\New-EtpReleaseSigningKey.ps1 -UsbFolder 'F:\ETP-Key','G:\ETP-Key'

.PARAMETER AllowFixedDrive
Review m-9 (decision 53): many USB SSDs and large sticks report themselves to Windows as Fixed, not Removable. With this
switch such a drive is accepted after you type its letter to confirm it is a USB stick you will unplug (never an internal
disk). The system drive, network drives and OneDrive stay refused.
#>
[CmdletBinding()]
param(
    # Two folders, each on a different removable drive.
    [Parameter(Mandatory)][ValidateCount(2, 2)][string[]]$UsbFolder,
    [ValidateRange(100000, 10000000)][int]$Iterations = 600000,
    [switch]$AllowFixedDrive
)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'EtpReleaseSigning.psm1') -Force

$KeyFileName = 'etp-release-signing-key.json'
$PublicFileName = 'etp-release-public-key.txt'

# The two folders must be on two different removable drives, never on the system drive, OneDrive or a network share.
$roots = @()
foreach ($folder in $UsbFolder) {
    $full = [IO.Path]::GetFullPath($folder)
    $root = [IO.Path]::GetPathRoot($full)
    if ($root -notmatch '^[A-Za-z]:\\$') { throw "$folder is not on a local drive letter." }
    $drive = New-Object IO.DriveInfo($root)
    if ($drive.DriveType -eq [IO.DriveType]::Fixed -and $AllowFixedDrive) {
        if ($root.TrimEnd('\') -ieq $env:SystemDrive) { throw "$folder is on the system drive." }
        $letter = $root.Substring(0, 1).ToUpperInvariant()
        $typed = Read-Host "$root reports itself as a fixed drive. If it is a USB stick you will unplug and keep apart, type FIXED $letter to use it"
        if ("$typed".Trim() -cne "FIXED $letter") { throw "$folder was not confirmed as a USB stick. Nothing was written." }
    }
    elseif ($drive.DriveType -ne [IO.DriveType]::Removable) {
        throw "$folder is not on a removable drive (it is $($drive.DriveType)). Use the USB sticks$(if ($drive.DriveType -eq [IO.DriveType]::Fixed) { ', or -AllowFixedDrive for a USB stick that Windows reports as fixed' })."
    }
    if ($root.TrimEnd('\') -ieq $env:SystemDrive) { throw "$folder is on the system drive." }
    foreach ($name in 'OneDrive', 'OneDriveCommercial', 'OneDriveConsumer') {
        $sync = [Environment]::GetEnvironmentVariable($name)
        if ($sync -and $full.StartsWith([IO.Path]::GetFullPath($sync), [StringComparison]::OrdinalIgnoreCase)) { throw "$folder is inside OneDrive." }
    }
    if (Test-Path -LiteralPath (Join-Path $full $KeyFileName)) { throw "$full already holds a key file. This script never replaces a key; move the old one away first if you really mean to make a new key." }
    $roots += $root.ToUpperInvariant()
}
if ($roots[0] -eq $roots[1]) { throw 'Both folders are on the same drive. Use two USB sticks.' }

Write-Host 'Choose a passphrase of at least 16 characters. Write it on paper; keep the paper apart from the sticks.'
$first = Read-Host -AsSecureString 'Passphrase'
$second = Read-Host -AsSecureString 'Passphrase again'
$a = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($first)
$b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($second)
try {
    $same = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($a) -ceq [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b)
    $length = $first.Length
}
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($a); [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }
if (-not $same) { throw 'The two passphrases differ. Nothing was written.' }
if ($length -lt 16) { throw 'The passphrase is shorter than 16 characters. Nothing was written.' }

$key = New-EtpReleaseSigningKeyFileContent -Passphrase $first -Iterations $Iterations
$publicText = "ETP release signing public key`r`nKey id: $($key.KeyId)`r`nFingerprint (SHA-256): $($key.Fingerprint)`r`nPublic key (base64, 65-byte P-256 point): $($key.PublicKey)`r`nMade: $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm'))`r`n"

foreach ($folder in $UsbFolder) {
    $full = [IO.Path]::GetFullPath($folder)
    New-Item -ItemType Directory -Force -Path $full | Out-Null
    $path = Join-Path $full $KeyFileName
    # CreateNew: never overwrite a key file.
    $stream = New-Object IO.FileStream($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($key.Content); $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    [IO.File]::WriteAllText((Join-Path $full $PublicFileName), $publicText, (New-Object Text.UTF8Encoding($false)))
}

# Read each stick back and sign a test text with it: proves the passphrase opens both copies.
$probe = Join-Path ([IO.Path]::GetTempPath()) ('etp-key-check-' + [Guid]::NewGuid().ToString('N') + '.txt')
try {
    [IO.File]::WriteAllText($probe, 'ETP release key check')
    foreach ($folder in $UsbFolder) {
        $path = Join-Path ([IO.Path]::GetFullPath($folder)) $KeyFileName
        $id = New-EtpReleaseSignature -ManifestPath $probe -KeyPath $path -Passphrase $first -SignaturePath "$probe.sig"
        if ($id -cne $key.KeyId -or -not (Test-EtpReleaseSignature -ManifestPath $probe -PublicKey $key.PublicKey -SignaturePath "$probe.sig")) { throw "The key on $folder did not read back correctly." }
        Remove-Item -LiteralPath "$probe.sig" -Force
    }
}
finally { Remove-Item -LiteralPath $probe, "$probe.sig" -Force -ErrorAction SilentlyContinue }

Write-Host ''
Write-Host "Key written to both sticks and checked."
Write-Host "Key id:       $($key.KeyId)"
Write-Host "Fingerprint:  $($key.Fingerprint)"
Write-Host ''
Write-Host 'Paste this line into src\Etp.Reporting.Application\Updates\ReleaseSigningKeys.cs (inside Trusted):'
Write-Host "        $($key.CSharpLine)"
Write-Host ''
Write-Host 'Write the fingerprint on the passphrase paper. Eject both sticks and store them in two places.'
