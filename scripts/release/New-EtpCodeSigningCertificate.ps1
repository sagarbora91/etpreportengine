<#
.SYNOPSIS
1.13 (code protection, S1, decision D5). The Authenticode certificate ceremony. Run ONCE, by Sagar, on Workpc, with the two USB sticks in.

.DESCRIPTION
Makes a self-signed code-signing certificate (RSA-3072, SHA-256, valid 10 years) entirely in memory and writes the SAME
passphrase-protected PFX (etp-codesign.pfx) to both USB sticks, plus the public certificate (etp-codesign.cer) and a small
fingerprint card (etp-codesign-fingerprint.txt) beside it. Then it prints the SHA-1 thumbprint and the SHA-256 fingerprint:
write them on the paper with the passphrase. Later you copy etp-codesign.cer into installer\certs\ in the repository (the
public certificate is safe to commit; the PFX never is).

The private key is never put in the Windows certificate store, the repository, OneDrive, the user profile, a log or a command
line. The passphrase is typed twice at a hidden prompt (SecureString). Each stick is read back, opened with the passphrase and
used to sign and verify a test value before the script ends. The script never replaces an existing PFX.

Losing both sticks (or the passphrase) means a new ceremony and the trust script again on every PC.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\New-EtpCodeSigningCertificate.ps1 -UsbFolder 'F:\ETP-Key','G:\ETP-Key'

.PARAMETER AllowFixedDrive
Same meaning as in New-EtpReleaseSigningKey.ps1: a USB drive that Windows reports as Fixed is accepted only after you type its
letter to confirm it is a USB stick you will unplug. The system drive, network drives and OneDrive stay refused.

.PARAMETER Subject
Certificate subject. Default: CN=Saagar Traders - ETP Reporting Engine, O=Saagar Traders.

.PARAMETER Years
Validity in years (default 10).
#>
[CmdletBinding()]
param(
    # Two folders, each on a different removable drive.
    [Parameter(Mandatory)][ValidateCount(2, 2)][string[]]$UsbFolder,
    [string]$Subject = 'CN=Saagar Traders - ETP Reporting Engine, O=Saagar Traders',
    [ValidateRange(1, 10)][int]$Years = 10,
    [switch]$AllowFixedDrive
)
$ErrorActionPreference = 'Stop'

$PfxFileName = 'etp-codesign.pfx'
$CerFileName = 'etp-codesign.cer'
$CardFileName = 'etp-codesign-fingerprint.txt'

# Same checks as the release-key ceremony (New-EtpReleaseSigningKey.ps1): removable, not system drive, not OneDrive.
function Assert-EtpUsbFolder([string]$Folder, [switch]$AllowFixed) {
    $full = [IO.Path]::GetFullPath($Folder)
    $root = [IO.Path]::GetPathRoot($full)
    if ($root -notmatch '^[A-Za-z]:\\$') { throw "$Folder is not on a local drive letter." }
    $drive = New-Object IO.DriveInfo($root)
    if ($drive.DriveType -eq [IO.DriveType]::Fixed -and $AllowFixed) {
        if ($root.TrimEnd('\') -ieq $env:SystemDrive) { throw "$Folder is on the system drive." }
        $letter = $root.Substring(0, 1).ToUpperInvariant()
        $typed = Read-Host "$root reports itself as a fixed drive. If it is a USB stick you will unplug and keep apart, type FIXED $letter to use it"
        if ("$typed".Trim() -cne "FIXED $letter") { throw "$Folder was not confirmed as a USB stick. Nothing was written." }
    }
    elseif ($drive.DriveType -ne [IO.DriveType]::Removable) {
        throw "$Folder is not on a removable drive (it is $($drive.DriveType)). Use the USB sticks$(if ($drive.DriveType -eq [IO.DriveType]::Fixed) { ', or -AllowFixedDrive for a USB stick that Windows reports as fixed' })."
    }
    if ($root.TrimEnd('\') -ieq $env:SystemDrive) { throw "$Folder is on the system drive." }
    foreach ($name in 'OneDrive', 'OneDriveCommercial', 'OneDriveConsumer') {
        $sync = [Environment]::GetEnvironmentVariable($name)
        if ($sync -and $full.StartsWith([IO.Path]::GetFullPath($sync), [StringComparison]::OrdinalIgnoreCase)) { throw "$Folder is inside OneDrive." }
    }
    return $root.ToUpperInvariant()
}

$roots = @()
foreach ($folder in $UsbFolder) {
    $roots += Assert-EtpUsbFolder -Folder $folder -AllowFixed:$AllowFixedDrive
    $full = [IO.Path]::GetFullPath($folder)
    foreach ($name in $PfxFileName, $CerFileName, $CardFileName) {
        if (Test-Path -LiteralPath (Join-Path $full $name)) { throw "$full already holds $name. This script never replaces a certificate; move the old files away first if you really mean to make a new one." }
    }
}
if ($roots[0] -eq $roots[1]) { throw 'Both folders are on the same drive. Use two USB sticks.' }

Write-Host 'Choose a passphrase of at least 16 characters (different from the release-key passphrase). Write it on paper; keep the paper apart from the sticks.'
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

# Build the certificate in memory (no certificate store involved).
Add-Type -AssemblyName System.Security
$rsa = [Security.Cryptography.RSA]::Create(3072)
$certificate = $null; $pfxBytes = $null; $cerBytes = $null
try {
    $request = New-Object Security.Cryptography.X509Certificates.CertificateRequest(
        (New-Object Security.Cryptography.X509Certificates.X500DistinguishedName($Subject)),
        $rsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($false, $false, 0, $true)))
    $request.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509KeyUsageExtension([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature, $true)))
    $eku = New-Object Security.Cryptography.OidCollection
    [void]$eku.Add((New-Object Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3', 'Code Signing')))
    $request.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($eku, $true)))
    $request.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension($request.PublicKey, $false)))
    $notBefore = [DateTimeOffset]::UtcNow.AddDays(-1)
    $certificate = $request.CreateSelfSigned($notBefore, $notBefore.AddYears($Years))

    # PFX protected by the passphrase. Windows PowerShell 5.1 (.NET Framework) takes the SecureString directly.
    $pfxBytes = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $first)
    $cerBytes = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    $sha1 = $certificate.Thumbprint
    $sha256 = (([Security.Cryptography.SHA256]::Create()).ComputeHash($cerBytes) | ForEach-Object { $_.ToString('X2') }) -join ''
    $sha256Spaced = ($sha256 -split '(.{2})' | Where-Object { $_ }) -join ':'
    $notAfter = $certificate.NotAfter
    $card = "ETP code-signing certificate`r`nSubject: $($certificate.Subject)`r`nSHA-1 thumbprint: $sha1`r`nSHA-256 fingerprint: $sha256Spaced`r`nValid until: $($notAfter.ToString('yyyy-MM-dd'))`r`nMade: $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm'))`r`n"
}
finally {
    if ($certificate) { $certificate.Dispose() }
    $rsa.Dispose()
}

foreach ($folder in $UsbFolder) {
    $full = [IO.Path]::GetFullPath($folder)
    New-Item -ItemType Directory -Force -Path $full | Out-Null
    # CreateNew: never overwrite.
    $stream = New-Object IO.FileStream((Join-Path $full $PfxFileName), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($pfxBytes, 0, $pfxBytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    [IO.File]::WriteAllBytes((Join-Path $full $CerFileName), $cerBytes)
    [IO.File]::WriteAllText((Join-Path $full $CardFileName), $card, (New-Object Text.UTF8Encoding($false)))
}
[Array]::Clear($pfxBytes, 0, $pfxBytes.Length)

# Read each stick back with the passphrase and sign + verify a test value.
foreach ($folder in $UsbFolder) {
    $path = Join-Path ([IO.Path]::GetFullPath($folder)) $PfxFileName
    $check = New-Object Security.Cryptography.X509Certificates.X509Certificate2($path, $first)
    try {
        if (-not $check.HasPrivateKey -or $check.Thumbprint -cne $sha1) { throw "The certificate on $folder did not read back correctly." }
        $data = [Text.Encoding]::UTF8.GetBytes('ETP code-signing check')
        $signer = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($check)
        $signature = $signer.SignData($data, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $verifier = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($check)
        if (-not $verifier.VerifyData($data, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw "The key on $folder could not sign a test value." }
    }
    finally { $check.Dispose() }
}

Write-Host ''
Write-Host 'Certificate written to both sticks and checked.'
Write-Host "SHA-1 thumbprint:     $sha1"
Write-Host "SHA-256 fingerprint:  $sha256Spaced"
Write-Host "Valid until:          $($notAfter.ToString('yyyy-MM-dd'))"
Write-Host ''
Write-Host 'Write BOTH numbers on the passphrase paper. Then:'
Write-Host "  1. Copy $CerFileName (public, safe) from a stick into the repository folder installer\certs\ and tell Claude it is there."
Write-Host '  2. Eject both sticks and store them in two places. The PFX never goes anywhere else.'
