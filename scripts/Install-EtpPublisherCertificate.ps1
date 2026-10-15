<#
.SYNOPSIS
Trusts the ETP code-signing certificate on THIS PC. Run by Sagar, elevated, by hand, on each PC. Never run by setup. Never silent.

.DESCRIPTION
Shows the certificate's subject, validity, SHA-1 thumbprint and SHA-256 fingerprint. Compare them with the numbers on the paper
card made at the ceremony. Only if they are identical, type YES (capital letters). The script then adds the PUBLIC certificate
to this PC's Trusted Root Certification Authorities and Trusted Publishers stores (Local Machine). After that, files signed with
the ETP key show as valid here and UAC shows the publisher name.

Trusting a self-signed certificate means: anything signed with that key is trusted on this PC. That is why the key lives only on
the two USB sticks. To undo it later: certmgr.msc (or `certlm.msc`) > Trusted Root Certification Authorities and Trusted
Publishers > remove "Saagar Traders - ETP Reporting Engine".

.PARAMETER CertificatePath
The public certificate (.cer). Default: installer\certs\etp-codesign.cer in the repository, or etp-codesign.cer beside this script.
A file that contains a private key (a PFX) is refused.

.PARAMETER TestOnlySimulate
For the self-tests: no administrator check and no store is touched. Never used for real.
#>
[CmdletBinding()]
param(
    [string]$CertificatePath,
    [switch]$TestOnlySimulate
)
$ErrorActionPreference = 'Stop'

if (-not $CertificatePath) {
    foreach ($candidate in (Join-Path $PSScriptRoot '..\installer\certs\etp-codesign.cer'), (Join-Path $PSScriptRoot 'etp-codesign.cer')) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $CertificatePath = $candidate; break }
    }
    if (-not $CertificatePath) { throw 'No certificate file found. Pass -CertificatePath with the etp-codesign.cer from the USB stick.' }
}
$full = [IO.Path]::GetFullPath($CertificatePath)
if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "The certificate file was not found: $full" }
if ([IO.Path]::GetExtension($full) -ine '.cer') { throw 'Give the public certificate (.cer), never the PFX.' }

if (-not $TestOnlySimulate) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this from an elevated (Run as administrator) PowerShell window.' }
}

$certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2($full)
try {
    if ($certificate.HasPrivateKey) { throw 'This file contains a private key. Refusing. Use the public .cer only.' }
    if ('1.3.6.1.5.5.7.3.3' -notin @($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })) { throw 'This certificate is not a code-signing certificate.' }
    if ($certificate.Subject -cne $certificate.Issuer) { throw 'This certificate is not self-signed; refusing to add it as a trusted root.' }
    if ($certificate.NotAfter -le (Get-Date)) { throw 'This certificate has expired.' }
    $sha256 = (([Security.Cryptography.SHA256]::Create()).ComputeHash($certificate.RawData) | ForEach-Object { $_.ToString('X2') }) -join ':'

    Write-Host ''
    Write-Host 'You are about to trust this publisher on this PC:'
    Write-Host "  Subject:             $($certificate.Subject)"
    Write-Host "  Valid:               $($certificate.NotBefore.ToString('yyyy-MM-dd')) to $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
    Write-Host "  SHA-1 thumbprint:    $($certificate.Thumbprint)"
    Write-Host "  SHA-256 fingerprint: $sha256"
    Write-Host ''
    Write-Host 'Compare BOTH numbers with the paper card from the ceremony. If either differs by even one character, stop: type anything else.'
    $answer = Read-Host 'Type YES to trust this certificate on this PC'
    if ("$answer".Trim() -cne 'YES') { Write-Host 'Not confirmed. Nothing was changed.'; exit 1 }

    if ($TestOnlySimulate) {
        Write-Host 'TEST ONLY: no certificate store was changed.'
        exit 0
    }
    foreach ($storeName in 'Root', 'TrustedPublisher') {
        $store = New-Object Security.Cryptography.X509Certificates.X509Store($storeName, [Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try {
            if ($store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $certificate.Thumbprint, $false).Count -eq 0) { $store.Add($certificate) }
        }
        finally { $store.Close() }
    }
    Write-Host 'Done. The ETP publisher certificate is now trusted on this PC (Trusted Root and Trusted Publishers).'
}
finally { $certificate.Dispose() }
