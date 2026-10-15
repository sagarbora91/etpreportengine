<#
.SYNOPSIS
Authenticode-signs ETP release files (exe, dll, ps1, psm1) and verifies each signature.

.DESCRIPTION
Two ways to name the certificate:
  -CertificateThumbprint  a certificate (with its private key) already in Cert:\CurrentUser\My (the original path).
  -PfxPath                the ETP code-signing PFX on a USB stick (code protection S1, decision D5). The passphrase is asked at a
                          hidden prompt (or passed as a SecureString by build-windows-installer.ps1, which asks once). The key is
                          loaded for this run only and is never imported into the Windows certificate store.
A timestamp service is mandatory. With the self-signed ETP certificate, Windows reports a signature as valid only on a PC that
trusts the certificate (scripts\Install-EtpPublisherCertificate.ps1); on Workpc before that, the status is UnknownError / NotTrusted.
This script therefore accepts, for the PFX path, a status of Valid, UnknownError or NotTrusted ONLY when the signer thumbprint is
ours and a timestamp is present. HashMismatch, NotSigned and every other status fail.
#>
[CmdletBinding(DefaultParameterSetName = 'Store')]
param(
    [Parameter(Mandatory)][string[]]$Paths,
    [Parameter(Mandatory, ParameterSetName = 'Store')][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [Parameter(Mandatory, ParameterSetName = 'Pfx')][string]$PfxPath,
    [Parameter(ParameterSetName = 'Pfx')][securestring]$Passphrase,
    # Pfx only: accept a USB stick that Windows reports as Fixed after the operator types its letter.
    [Parameter(ParameterSetName = 'Pfx')][switch]$AllowFixedDrive,
    # Pfx only, for the self-tests with a throwaway certificate. Never used for a real release; prints a warning.
    [Parameter(ParameterSetName = 'Pfx')][switch]$TestOnlySkipUsbCheck,
    [Parameter(Mandatory)][uri]$TimestampServer
)
$ErrorActionPreference = 'Stop'
if ($TimestampServer.Scheme -notin @('http', 'https')) { throw 'Choose the certificate provider timestamp service.' }

function Assert-EtpPfxOnUsb([string]$Path, [switch]$AllowFixed) {
    # Same drive rules as the release-key ceremony: removable, not the system drive, not OneDrive, not a share.
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "The signing certificate file was not found: $full" }
    $root = [IO.Path]::GetPathRoot($full)
    if ($root -notmatch '^[A-Za-z]:\\$') { throw "$full is not on a local drive letter." }
    $drive = New-Object IO.DriveInfo($root)
    if ($drive.DriveType -eq [IO.DriveType]::Fixed -and $AllowFixed) {
        if ($root.TrimEnd('\') -ieq $env:SystemDrive) { throw "$full is on the system drive." }
        $letter = $root.Substring(0, 1).ToUpperInvariant()
        $typed = Read-Host "$root reports itself as a fixed drive. If it is the USB stick holding the key, type FIXED $letter to use it"
        if ("$typed".Trim() -cne "FIXED $letter") { throw "$root was not confirmed as a USB stick." }
    }
    elseif ($drive.DriveType -ne [IO.DriveType]::Removable) {
        throw "The signing key must be used from a USB stick, not from $($drive.DriveType) drive $root$(if ($drive.DriveType -eq [IO.DriveType]::Fixed) { ' (use -AllowFixedDrive for a USB stick that Windows reports as fixed)' })."
    }
    if ($root.TrimEnd('\') -ieq $env:SystemDrive) { throw "$full is on the system drive." }
    foreach ($name in 'OneDrive', 'OneDriveCommercial', 'OneDriveConsumer') {
        $sync = [Environment]::GetEnvironmentVariable($name)
        if ($sync -and $full.StartsWith([IO.Path]::GetFullPath($sync), [StringComparison]::OrdinalIgnoreCase)) { throw "$full is inside OneDrive." }
    }
}

$loaded = $null
if ($PSCmdlet.ParameterSetName -eq 'Pfx') {
    if ($TestOnlySkipUsbCheck) {
        Write-Warning 'TEST ONLY: the USB-stick check is skipped. Never do this for a real release.'
        if (-not (Test-Path -LiteralPath $PfxPath -PathType Leaf)) { throw "The signing certificate file was not found: $PfxPath" }
    }
    else { Assert-EtpPfxOnUsb -Path $PfxPath -AllowFixed:$AllowFixedDrive }
    if (-not $Passphrase) { $Passphrase = Read-Host -AsSecureString 'Code-signing key passphrase' }
    try { $loaded = New-Object Security.Cryptography.X509Certificates.X509Certificate2([IO.Path]::GetFullPath($PfxPath), $Passphrase) }
    catch { throw 'The signing key could not be opened (wrong passphrase or damaged file).' }
    $certificate = $loaded
}
else {
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
}
$expected = $certificate.Thumbprint
$isPfx = $PSCmdlet.ParameterSetName -eq 'Pfx'
try {
    if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date) -or '1.3.6.1.5.5.7.3.3' -notin @($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })) { throw 'A current code-signing certificate with its private key is required.' }
    foreach ($path in $Paths) {
        $full = [IO.Path]::GetFullPath($path)
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw 'A signing input is missing.' }
        if ([IO.Path]::GetExtension($full) -notin @('.exe', '.dll', '.ps1', '.psm1')) { throw 'Unsupported signing input.' }
        $result = Set-AuthenticodeSignature -LiteralPath $full -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer.AbsoluteUri
        $verified = Get-AuthenticodeSignature -LiteralPath $full
        $statusOk = if ($isPfx) { "$($verified.Status)" -in @('Valid', 'UnknownError', 'NotTrusted') } else { $verified.Status -eq 'Valid' }
        if (-not $statusOk -or $null -eq $verified.SignerCertificate -or $verified.SignerCertificate.Thumbprint -ine $expected -or $null -eq $verified.TimeStamperCertificate) {
            throw "Signing failed or could not be verified for $([IO.Path]::GetFileName($full)) (status $($verified.Status)); no release can be published."
        }
    }
}
finally { if ($loaded) { $loaded.Dispose() } }
Write-Output 'Release signatures verified.'
