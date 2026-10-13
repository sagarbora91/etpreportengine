# 1.12 lane B1 (plan section B, decisions 39 and 44 #1, AUDIT-2 S-03). Release signing for the in-app update.
#
# One signature format, two languages: this module signs, src\Etp.Reporting.Application\Updates\ReleaseSignatureVerifier.cs
# verifies (UpdateSignatureCrossLayerTests runs both). Windows PowerShell 5.1 (.NET Framework 4.8) and PowerShell 7 both work.
#
#   Key       ECDSA P-256. The public key is the 65-byte uncompressed point (0x04 | X | Y), written as base64.
#   Key id    the first 8 bytes of SHA-256 of that point, 16 lower-case hex characters.
#   Signature ECDSA-P256-SHA256 over the EXACT bytes of release.json, IEEE P1363 (r | s, 64 bytes), in release.json.sig:
#             {"schema":1,"algorithm":"ECDSA-P256-SHA256","keyId":"<id>","signature":"<base64>"}
#   Key file  the private key exists only in this file, on Sagar's two USB sticks, encrypted with a passphrase:
#             PBKDF2-HMAC-SHA256 (600,000 rounds, 16-byte salt) -> 64 bytes = AES-256 key | HMAC-SHA256 key;
#             AES-256-CBC (random IV) over the 32-byte private scalar; HMAC over the header text, the IV and the ciphertext,
#             checked (in constant time) before anything is decrypted. The public key and key id are readable without the
#             passphrase, so Publish-Release.ps1 can put the key id into release.json before it asks for the passphrase.
#             (.NET Framework has no encrypted-PKCS#8 export, so the plan's "encrypted PKCS#8" is this documented format.)
#
# Never put a key file, a passphrase or a private key in the repository, a test, a build or a log. Tests make a throwaway
# key in a temporary folder and delete it.

Set-StrictMode -Version 2.0

$script:KeyFileFormat = 'etp-release-signing-key'
$script:KeyFileVersion = 1
$script:DefaultIterations = 600000
$script:SignatureAlgorithm = 'ECDSA-P256-SHA256'

function Get-EtpReleaseKeyId {
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]]$PublicPoint)
    if ($PublicPoint.Length -ne 65 -or $PublicPoint[0] -ne 4) { throw 'A release public key is the 65-byte uncompressed P-256 point.' }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($PublicPoint) } finally { $sha.Dispose() }
    return (-join ($hash[0..7] | ForEach-Object { $_.ToString('x2') }))
}

function Get-EtpReleaseKeyFingerprint {
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]]$PublicPoint)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hex = -join ($sha.ComputeHash($PublicPoint) | ForEach-Object { $_.ToString('X2') }) } finally { $sha.Dispose() }
    return ((0..15 | ForEach-Object { $hex.Substring($_ * 4, 4) }) -join ' ')
}

function ConvertTo-EtpPublicPoint {
    param([Parameter(Mandatory)]$Parameters)
    $point = New-Object byte[] 65
    $point[0] = 4
    if ($Parameters.Q.X.Length -ne 32 -or $Parameters.Q.Y.Length -ne 32) { throw 'Unexpected P-256 coordinate length.' }
    [Array]::Copy($Parameters.Q.X, 0, $point, 1, 32)
    [Array]::Copy($Parameters.Q.Y, 0, $point, 33, 32)
    return ,$point
}

function New-EtpEcdsaFromPoint {
    param([Parameter(Mandatory)][byte[]]$PublicPoint, [byte[]]$PrivateScalar)
    if ($PublicPoint.Length -ne 65 -or $PublicPoint[0] -ne 4) { throw 'A release public key is the 65-byte uncompressed P-256 point.' }
    $parameters = New-Object System.Security.Cryptography.ECParameters
    $parameters.Curve = [System.Security.Cryptography.ECCurve+NamedCurves]::nistP256
    $q = New-Object System.Security.Cryptography.ECPoint
    $x = New-Object byte[] 32; $y = New-Object byte[] 32
    [Array]::Copy($PublicPoint, 1, $x, 0, 32); [Array]::Copy($PublicPoint, 33, $y, 0, 32)
    $q.X = $x; $q.Y = $y
    $parameters.Q = $q
    if ($PrivateScalar) { $parameters.D = $PrivateScalar }
    return [System.Security.Cryptography.ECDsa]::Create($parameters)
}

function Get-EtpPassphraseBytes {
    param([Parameter(Mandatory)][System.Security.SecureString]$Passphrase)
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Passphrase)
    try {
        $text = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        return ,([System.Text.Encoding]::UTF8.GetBytes($text))
    }
    finally { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Get-EtpKeyFileHeaderBytes {
    param([Parameter(Mandatory)]$File)
    # Everything the MAC protects besides the IV and ciphertext: a changed key id, public key, salt or round count fails.
    $text = '{0}|{1}|nistP256|{2}|{3}|{4}|{5}' -f $script:KeyFileFormat, $script:KeyFileVersion, $File.keyId, $File.publicKey, $File.iterations, $File.salt
    return ,([System.Text.Encoding]::UTF8.GetBytes($text))
}

function Get-EtpDerivedKeys {
    param([Parameter(Mandatory)][byte[]]$PassphraseBytes, [Parameter(Mandatory)][byte[]]$Salt, [Parameter(Mandatory)][int]$Iterations)
    $kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes($PassphraseBytes, $Salt, $Iterations, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $bytes = $kdf.GetBytes(64) } finally { $kdf.Dispose() }
    $encryption = New-Object byte[] 32; $mac = New-Object byte[] 32
    [Array]::Copy($bytes, 0, $encryption, 0, 32); [Array]::Copy($bytes, 32, $mac, 0, 32)
    [Array]::Clear($bytes, 0, $bytes.Length)
    return @{ Encryption = $encryption; Mac = $mac }
}

function Get-EtpKeyFileMac {
    param([Parameter(Mandatory)][byte[]]$MacKey, [Parameter(Mandatory)][byte[]]$Header, [Parameter(Mandatory)][byte[]]$InitVector, [Parameter(Mandatory)][byte[]]$Ciphertext)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256 -ArgumentList (, $MacKey)
    try {
        $all = New-Object byte[] ($Header.Length + $InitVector.Length + $Ciphertext.Length)
        [Array]::Copy($Header, 0, $all, 0, $Header.Length)
        [Array]::Copy($InitVector, 0, $all, $Header.Length, $InitVector.Length)
        [Array]::Copy($Ciphertext, 0, $all, $Header.Length + $InitVector.Length, $Ciphertext.Length)
        return ,($hmac.ComputeHash($all))
    }
    finally { $hmac.Dispose() }
}

function Test-EtpBytesEqualConstantTime {
    param([byte[]]$Left, [byte[]]$Right)
    if ($null -eq $Left -or $null -eq $Right -or $Left.Length -ne $Right.Length) { return $false }
    $difference = 0
    for ($i = 0; $i -lt $Left.Length; $i++) { $difference = $difference -bor ($Left[$i] -bxor $Right[$i]) }
    return ($difference -eq 0)
}

function New-EtpReleaseSigningKeyFileContent {
    <#
    .SYNOPSIS
    Makes a new P-256 key pair and returns the encrypted key file text plus the public facts. Writes nothing.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][System.Security.SecureString]$Passphrase,
        [ValidateRange(10000, 10000000)][int]$Iterations = $script:DefaultIterations
    )
    $ecdsa = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    $passphraseBytes = $null; $keys = $null; $parameters = $null
    try {
        $parameters = $ecdsa.ExportParameters($true)
        if ($parameters.D.Length -ne 32) { throw 'Unexpected P-256 private key length.' }
        $point = ConvertTo-EtpPublicPoint -Parameters $parameters
        $keyId = Get-EtpReleaseKeyId -PublicPoint $point
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        $salt = New-Object byte[] 16; $iv = New-Object byte[] 16
        try { $rng.GetBytes($salt); $rng.GetBytes($iv) } finally { $rng.Dispose() }
        $passphraseBytes = Get-EtpPassphraseBytes -Passphrase $Passphrase
        $keys = Get-EtpDerivedKeys -PassphraseBytes $passphraseBytes -Salt $salt -Iterations $Iterations
        $aes = [System.Security.Cryptography.Aes]::Create()
        try {
            $aes.KeySize = 256; $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC; $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
            $aes.Key = $keys.Encryption; $aes.IV = $iv
            $encryptor = $aes.CreateEncryptor()
            try { $ciphertext = $encryptor.TransformFinalBlock($parameters.D, 0, $parameters.D.Length) } finally { $encryptor.Dispose() }
        }
        finally { $aes.Dispose() }
        $file = [ordered]@{
            format     = $script:KeyFileFormat
            version    = $script:KeyFileVersion
            curve      = 'nistP256'
            keyId      = $keyId
            publicKey  = [Convert]::ToBase64String($point)
            kdf        = 'PBKDF2-HMAC-SHA256'
            iterations = $Iterations
            salt       = [Convert]::ToBase64String($salt)
            cipher     = 'AES-256-CBC+HMAC-SHA256'
            iv         = [Convert]::ToBase64String($iv)
            ciphertext = [Convert]::ToBase64String($ciphertext)
            mac        = $null
        }
        $mac = Get-EtpKeyFileMac -MacKey $keys.Mac -Header (Get-EtpKeyFileHeaderBytes -File ([pscustomobject]$file)) -InitVector $iv -Ciphertext $ciphertext
        $file.mac = [Convert]::ToBase64String($mac)
        return [pscustomobject]@{
            Content     = ([pscustomobject]$file | ConvertTo-Json -Compress)
            KeyId       = $keyId
            PublicKey   = $file.publicKey
            Fingerprint = Get-EtpReleaseKeyFingerprint -PublicPoint $point
            CSharpLine  = ('new("{0}", "{1}"),' -f $keyId, $file.publicKey)
        }
    }
    finally {
        if ($parameters -and $parameters.D) { [Array]::Clear($parameters.D, 0, $parameters.D.Length) }
        if ($passphraseBytes) { [Array]::Clear($passphraseBytes, 0, $passphraseBytes.Length) }
        if ($keys) { [Array]::Clear($keys.Encryption, 0, 32); [Array]::Clear($keys.Mac, 0, 32) }
        $ecdsa.Dispose()
    }
}

function Read-EtpReleaseKeyFilePublic {
    <#
    .SYNOPSIS
    The key id and public key of a key file, without the passphrase (the MAC is not checked here; signing checks it).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $file = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($file.format -ne $script:KeyFileFormat -or $file.version -ne $script:KeyFileVersion -or $file.curve -ne 'nistP256') { throw 'This is not an ETP release signing key file.' }
    $point = [Convert]::FromBase64String([string]$file.publicKey)
    if ((Get-EtpReleaseKeyId -PublicPoint $point) -cne [string]$file.keyId) { throw 'The key file''s key id does not match its public key.' }
    return [pscustomobject]@{ KeyId = [string]$file.keyId; PublicKey = [string]$file.publicKey; Fingerprint = Get-EtpReleaseKeyFingerprint -PublicPoint $point }
}

function Open-EtpReleaseSigningKey {
    # Returns @{ Ecdsa; KeyId; PublicPoint }. The caller disposes Ecdsa. A wrong passphrase or a changed file fails the MAC.
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][System.Security.SecureString]$Passphrase)
    $public = Read-EtpReleaseKeyFilePublic -Path $Path
    $file = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $iterations = [int]$file.iterations
    if ($iterations -lt 10000) { throw 'The key file''s round count is too low.' }
    $salt = [Convert]::FromBase64String([string]$file.salt)
    $iv = [Convert]::FromBase64String([string]$file.iv)
    $ciphertext = [Convert]::FromBase64String([string]$file.ciphertext)
    $storedMac = [Convert]::FromBase64String([string]$file.mac)
    $passphraseBytes = Get-EtpPassphraseBytes -Passphrase $Passphrase
    $keys = $null; $scalar = $null
    try {
        $keys = Get-EtpDerivedKeys -PassphraseBytes $passphraseBytes -Salt $salt -Iterations $iterations
        $mac = Get-EtpKeyFileMac -MacKey $keys.Mac -Header (Get-EtpKeyFileHeaderBytes -File $file) -InitVector $iv -Ciphertext $ciphertext
        if (-not (Test-EtpBytesEqualConstantTime -Left $mac -Right $storedMac)) { throw 'Wrong passphrase, or the key file was changed.' }
        $aes = [System.Security.Cryptography.Aes]::Create()
        try {
            $aes.KeySize = 256; $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC; $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
            $aes.Key = $keys.Encryption; $aes.IV = $iv
            $decryptor = $aes.CreateDecryptor()
            try { $scalar = $decryptor.TransformFinalBlock($ciphertext, 0, $ciphertext.Length) } finally { $decryptor.Dispose() }
        }
        finally { $aes.Dispose() }
        if ($scalar.Length -ne 32) { throw 'The key file does not hold a P-256 private key.' }
        $point = [Convert]::FromBase64String($public.PublicKey)
        $ecdsa = New-EtpEcdsaFromPoint -PublicPoint $point -PrivateScalar $scalar
        return @{ Ecdsa = $ecdsa; KeyId = $public.KeyId; PublicPoint = $point }
    }
    finally {
        [Array]::Clear($passphraseBytes, 0, $passphraseBytes.Length)
        if ($keys) { [Array]::Clear($keys.Encryption, 0, 32); [Array]::Clear($keys.Mac, 0, 32) }
        if ($scalar) { [Array]::Clear($scalar, 0, $scalar.Length) }
    }
}

function ConvertTo-EtpSignatureFileText {
    param([Parameter(Mandatory)][string]$KeyId, [Parameter(Mandatory)][byte[]]$Signature)
    # Fixed text, not ConvertTo-Json: the same bytes from Windows PowerShell 5.1 and PowerShell 7.
    return ('{{"schema":1,"algorithm":"{0}","keyId":"{1}","signature":"{2}"}}' -f $script:SignatureAlgorithm, $KeyId, [Convert]::ToBase64String($Signature))
}

function New-EtpReleaseSignature {
    <#
    .SYNOPSIS
    Signs the exact bytes of a release.json with the key file and writes <ManifestPath>.sig (ASCII). Returns the key id.
    The signature is verified against the public key before the file is written.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][string]$KeyPath,
        [Parameter(Mandatory)][System.Security.SecureString]$Passphrase,
        [string]$SignaturePath
    )
    if (-not $SignaturePath) { $SignaturePath = "$ManifestPath.sig" }
    $bytes = [System.IO.File]::ReadAllBytes($ManifestPath)
    $key = Open-EtpReleaseSigningKey -Path $KeyPath -Passphrase $Passphrase
    try {
        $signature = $key.Ecdsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
        if ($signature.Length -ne 64) { throw 'Unexpected ECDSA signature length (expected IEEE P1363, 64 bytes).' }
    }
    finally { $key.Ecdsa.Dispose() }
    $text = ConvertTo-EtpSignatureFileText -KeyId $key.KeyId -Signature $signature
    if (-not (Test-EtpReleaseSignatureBytes -ManifestBytes $bytes -SignatureText $text -PublicKey ([Convert]::ToBase64String($key.PublicPoint)))) {
        throw 'The new signature did not verify against the key file''s public key. Nothing was written.'
    }
    [System.IO.File]::WriteAllText($SignaturePath, $text, (New-Object System.Text.ASCIIEncoding))
    return $key.KeyId
}

function Test-EtpReleaseSignatureBytes {
    param([Parameter(Mandatory)][byte[]]$ManifestBytes, [Parameter(Mandatory)][string]$SignatureText, [Parameter(Mandatory)][string]$PublicKey)
    # Any malformed part (not JSON, a missing field, bad base64) is "not verified", never an error.
    try {
        $signatureFile = $SignatureText | ConvertFrom-Json
        if ($signatureFile.schema -ne 1 -or $signatureFile.algorithm -cne $script:SignatureAlgorithm) { return $false }
        $point = [Convert]::FromBase64String($PublicKey)
        if ((Get-EtpReleaseKeyId -PublicPoint $point) -cne [string]$signatureFile.keyId) { return $false }
        $signature = [Convert]::FromBase64String([string]$signatureFile.signature)
    }
    catch { return $false }
    if ($signature.Length -ne 64) { return $false }
    $ecdsa = New-EtpEcdsaFromPoint -PublicPoint $point
    try { return $ecdsa.VerifyData($ManifestBytes, $signature, [System.Security.Cryptography.HashAlgorithmName]::SHA256) }
    finally { $ecdsa.Dispose() }
}

function Test-EtpReleaseSignature {
    <#
    .SYNOPSIS
    True when <ManifestPath>.sig is a valid signature of release.json by the given public key (base64 of the 65-byte point).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ManifestPath, [Parameter(Mandatory)][string]$PublicKey, [string]$SignaturePath)
    if (-not $SignaturePath) { $SignaturePath = "$ManifestPath.sig" }
    return (Test-EtpReleaseSignatureBytes -ManifestBytes ([System.IO.File]::ReadAllBytes($ManifestPath)) `
        -SignatureText ([System.IO.File]::ReadAllText($SignaturePath)) -PublicKey $PublicKey)
}

function ConvertTo-EtpReleaseManifestText {
    <#
    .SYNOPSIS
    release.json schema 2 as text (UTF-8 is written by the caller, without a byte order mark). The 1.11.2 L3 fields plus
    schema, keyId, hasSchemaChange, minimumFromVersion and notes. The C# reader is ReleaseManifestParser (it refuses unknown
    schemas, a different product, an installer name that is not EtpReportingEngine-Setup-<version>-x64.exe, a minimum that is
    not below the version, and notes over 4000 characters).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
        [Parameter(Mandatory)][long]$InstallerBytes,
        [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$Sha256,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{7,40}$')][string]$Commit,
        [Parameter(Mandatory)][int]$SchemaCompatLevel,
        [Parameter(Mandatory)][bool]$HasSchemaChange,
        [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$MinimumFromVersion,
        [AllowEmptyString()][string]$Notes = '',
        [bool]$SqlPayloadIncluded = $false,
        [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{16}$')][string]$KeyId,
        [datetime]$PublishedUtc = [datetime]::UtcNow
    )
    if ([version]$MinimumFromVersion -ge [version]$Version) { throw "minimumFromVersion ($MinimumFromVersion) must be below the version ($Version)." }
    if ($Notes.Length -gt 4000) { throw 'Release notes are limited to 4000 characters.' }
    [ordered]@{
        schema             = 2
        product            = 'ETP Reporting Engine'
        version            = $Version
        installer          = "EtpReportingEngine-Setup-$Version-x64.exe"
        installerBytes     = $InstallerBytes
        sha256             = $Sha256.ToUpperInvariant()
        commit             = $Commit.ToLowerInvariant()
        publishedUtc       = $PublishedUtc.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
        schemaCompatLevel  = $SchemaCompatLevel
        hasSchemaChange    = $HasSchemaChange
        # 1.11.2 L3's name for the same fact, kept for anything that already reads it.
        hasMigration       = $HasSchemaChange
        minimumFromVersion = $MinimumFromVersion
        notes              = $Notes
        sqlPayloadIncluded = $SqlPayloadIncluded
        keyId              = $KeyId
    } | ConvertTo-Json -Compress
}

function Copy-EtpReleaseToFeed {
    <#
    .SYNOPSIS
    Puts a signed release into the update feed: <FeedRoot>\<Version>\ gets the installer, then release.json.sig, then
    release.json LAST, so a PC that sees release.json sees a complete folder. Refuses to replace a folder that already holds
    a release.json (publish a new version instead). Checks the copied installer's hash and the copied signature.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceFolder,
        [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
        [Parameter(Mandatory)][string]$FeedRoot,
        [Parameter(Mandatory)][string]$PublicKey
    )
    $manifest = Join-Path $SourceFolder 'release.json'
    $signature = Join-Path $SourceFolder 'release.json.sig'
    if (-not (Test-EtpReleaseSignature -ManifestPath $manifest -PublicKey $PublicKey -SignaturePath $signature)) { throw 'release.json in the source folder is not signed by this key.' }
    $release = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    if ($release.version -cne $Version) { throw "release.json says $($release.version), not $Version." }
    $installer = Join-Path $SourceFolder ([string]$release.installer)
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "The installer $($release.installer) is not in $SourceFolder." }
    if (-not (Test-Path -LiteralPath $FeedRoot -PathType Container)) { throw "The update feed folder $FeedRoot does not exist. Create it in OneDrive first." }
    $target = Join-Path $FeedRoot $Version
    if (Test-Path -LiteralPath (Join-Path $target 'release.json')) { throw "$target already holds a published release. Publish a new version instead of replacing it." }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $targetInstaller = Join-Path $target ([string]$release.installer)
    Copy-Item -LiteralPath $installer -Destination $targetInstaller -Force
    $copiedHash = (Get-FileHash -LiteralPath $targetInstaller -Algorithm SHA256).Hash
    if ($copiedHash -ne ([string]$release.sha256).ToUpperInvariant()) { throw 'The copied installer does not match release.json. Nothing more was copied.' }
    Copy-Item -LiteralPath $signature -Destination (Join-Path $target 'release.json.sig') -Force
    Copy-Item -LiteralPath $manifest -Destination (Join-Path $target 'release.json') -Force
    if (-not (Test-EtpReleaseSignature -ManifestPath (Join-Path $target 'release.json') -PublicKey $PublicKey)) { throw "The copied release in $target does not verify. Remove the folder and publish again." }
    return $target
}

Export-ModuleMember -Function Get-EtpReleaseKeyId, Get-EtpReleaseKeyFingerprint, New-EtpReleaseSigningKeyFileContent,
    Read-EtpReleaseKeyFilePublic, New-EtpReleaseSignature, Test-EtpReleaseSignature, ConvertTo-EtpReleaseManifestText, Copy-EtpReleaseToFeed
