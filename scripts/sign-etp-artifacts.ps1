param(
    [Parameter(Mandatory)][string[]]$Paths,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [Parameter(Mandatory)][uri]$TimestampServer
)
$ErrorActionPreference = 'Stop'
if ($TimestampServer.Scheme -notin @('http','https')) { throw 'Choose the certificate provider timestamp service.' }
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
if (-not $certificate.HasPrivateKey -or $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date) -or '1.3.6.1.5.5.7.3.3' -notin @($certificate.EnhancedKeyUsageList.ObjectId.Value)) { throw 'A current code-signing certificate with its private key is required.' }
foreach ($path in $Paths) {
    $full = [IO.Path]::GetFullPath($path)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw 'A signing input is missing.' }
    if ([IO.Path]::GetExtension($full) -notin @('.exe','.dll','.ps1','.psm1')) { throw 'Unsupported signing input.' }
    $result = Set-AuthenticodeSignature -LiteralPath $full -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer.AbsoluteUri
    if ($result.Status -ne 'Valid') { throw 'Signing failed; no release can be published.' }
    $verified = Get-AuthenticodeSignature -LiteralPath $full
    if ($verified.Status -ne 'Valid' -or $verified.SignerCertificate.Thumbprint -ine $CertificateThumbprint -or $null -eq $verified.TimeStamperCertificate) { throw 'Signed artifact verification failed.' }
}
Write-Output 'Release signatures verified.'
