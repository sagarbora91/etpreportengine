<#
Self-test for the S1 signing scripts. Plain PowerShell (5.1), no Pester. Uses ONLY a throwaway certificate created in memory and a
temp folder; no real key, no certificate store. The real signing path always runs (offline, without a timestamp); the timestamped
case runs only if ETP_SIGNING_TEST_TIMESTAMP_URL is set (internet). Exit code 0 = all passed. Run it under BOTH shells:
  powershell -NoProfile -File scripts\release\tests\Signing.Tests.ps1
  E:\Tools\PowerShell\7\pwsh.exe -NoProfile -File scripts\release\tests\Signing.Tests.ps1
#>
$ErrorActionPreference = 'Stop'
$scripts = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$root = Join-Path ([IO.Path]::GetTempPath()) ('etp-signing-test-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root)
$failures = New-Object System.Collections.ArrayList
# R1-04: children run in the SAME PowerShell as this test (powershell.exe 5.1 or pwsh 7), so running this file under both covers both.
$hostExe = (Get-Process -Id $PID).Path
function Assert-True($condition, $message) { if ($condition) { Write-Host "PASS  $message" } else { Write-Host "FAIL  $message"; [void]$failures.Add($message) } }
function Invoke-Ps([string]$file, [string[]]$arguments, [string]$stdin) {
    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $file) + $arguments
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $script:Out = if ($stdin) { ($stdin | & $hostExe @all 2>&1 | Out-String) } else { (& $hostExe @all 2>&1 | Out-String) }
    $ErrorActionPreference = $previous
    if ($env:ETP_TEST_VERBOSE) { Write-Host $script:Out }
    return $LASTEXITCODE
}
# The passphrase goes in as a SecureString, as build-windows-installer.ps1 passes it (it asks once). Read-Host -AsSecureString cannot
# read a redirected stdin, so the prompt path is not driven here. The value is the throwaway test passphrase, never a real one.
function Invoke-Sign([string]$arguments) {
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $command = "& '$sign' $arguments -Passphrase (ConvertTo-SecureString 'throwaway-test-passphrase' -AsPlainText -Force)"
    $script:Out = (& $hostExe -NoProfile -ExecutionPolicy Bypass -Command $command 2>&1 | Out-String)
    $ErrorActionPreference = $previous
    if ($env:ETP_TEST_VERBOSE) { Write-Host $script:Out }
    return $LASTEXITCODE
}
try {
    $ceremony = Join-Path $scripts 'release\New-EtpCodeSigningCertificate.ps1'
    $sign = Join-Path $scripts 'sign-etp-artifacts.ps1'
    $trust = Join-Path $scripts 'Install-EtpPublisherCertificate.ps1'
    $installerScript = Join-Path $scripts 'build-windows-installer.ps1'
    $iss = Join-Path (Split-Path -Parent $scripts) 'installer\EtpReportingEngine.iss'

    # Static contracts
    Assert-True ((Get-Content -Raw -LiteralPath $iss) -match 'Excludes:\s*"[^"]*\*\.pdb') 'installer script excludes *.pdb from the packed files'
    foreach ($f in $ceremony, $sign, $trust, $installerScript) {
        $errors = $null; $tokens = $null
        [void][Management.Automation.Language.Parser]::ParseFile($f, [ref]$tokens, [ref]$errors)
        Assert-True ($errors.Count -eq 0) "parses: $(Split-Path -Leaf $f)"
    }
    $ceremonyText = Get-Content -Raw -LiteralPath $ceremony
    Assert-True ($ceremonyText -match 'Create\(3072\)' -and $ceremonyText -match 'AllowFixedDrive' -and $ceremonyText -match 'CreateNew') 'ceremony: RSA-3072, drive confirmation switch, never overwrites'
    Assert-True ($ceremonyText -notmatch 'Cert:\\|X509Store') 'ceremony never touches a certificate store'

    # Ceremony refuses fixed/system drives and OneDrive (nothing is written)
    $a = Join-Path $root 'a'; $b = Join-Path $root 'b'
    # -File cannot pass a string array; -Command can (two folders, as Sagar's call does).
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $Out = (& $hostExe -NoProfile -ExecutionPolicy Bypass -Command "& '$ceremony' -UsbFolder '$a','$b'" 2>&1 | Out-String)
    $code = $LASTEXITCODE; $ErrorActionPreference = $previous
    if ($env:ETP_TEST_VERBOSE) { Write-Host $Out }
    Assert-True ($code -ne 0 -and $Out -match 'not on a removable drive|system drive') 'ceremony refuses fixed/system drive folders'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $a 'etp-codesign.pfx'))) 'ceremony wrote nothing after the refusal'

    # Throwaway certificate (in memory, exported to the temp folder)
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    $req = New-Object Security.Cryptography.X509Certificates.CertificateRequest('CN=ETP Test Only', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $eku = New-Object Security.Cryptography.OidCollection; [void]$eku.Add((New-Object Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
    $req.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($eku, $true)))
    $cert = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(30))
    $pw = ConvertTo-SecureString 'throwaway-test-passphrase' -AsPlainText -Force
    $pfx = Join-Path $root 'test.pfx'; $cer = Join-Path $root 'test.cer'
    [IO.File]::WriteAllBytes($pfx, $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $pw))
    [IO.File]::WriteAllBytes($cer, $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    $victim = Join-Path $root 'victim.ps1'; [IO.File]::WriteAllText($victim, 'Write-Output 1')

    $code = Invoke-Ps $sign @('-Paths', $victim, '-PfxPath', $pfx, '-TimestampServer', 'http://127.0.0.1:9')
    Assert-True ($code -ne 0 -and $Out -match 'USB stick') 'signing refuses a PFX that is not on a USB stick'
    $code = Invoke-Ps $sign @('-Paths', $victim, '-CertificateThumbprint', 'xyz', '-TimestampServer', 'http://127.0.0.1:9')
    Assert-True ($code -ne 0) 'signing refuses a malformed thumbprint'
    Assert-True ((Get-AuthenticodeSignature -LiteralPath $victim).Status -eq 'NotSigned') 'refused signing left the file unsigned'

    # R1-04 (16 Oct): the REAL signing path (PFX loaded by sign-etp-artifacts.ps1 with EphemeralKeySet, Set-AuthenticodeSignature,
    # read-back) always runs, offline, without a timestamp (-TestOnlyNoTimestamp), in the same PowerShell as this test
    # ($hostExe: run this file under powershell.exe AND pwsh 7). R1-09: no private-key file may be left under %APPDATA%\Microsoft\Crypto.
    Write-Host "INFO  child scripts run in $hostExe (this test: $($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion))"
    $code = Invoke-Ps $sign @('-Paths', $victim, '-PfxPath', $pfx, '-TestOnlyNoTimestamp') 'throwaway-test-passphrase'
    Assert-True ($code -ne 0 -and $Out -match 'TestOnlySkipUsbCheck') '-TestOnlyNoTimestamp is refused without -TestOnlySkipUsbCheck (never on a real stick)'
    $code = Invoke-Ps $sign @('-Paths', $victim, '-PfxPath', $pfx, '-TestOnlySkipUsbCheck') 'throwaway-test-passphrase'
    Assert-True ($code -ne 0 -and $Out -match 'timestamp service') 'signing without -TimestampServer is refused (outside the test-only switch)'
    $keyFolders = @((Join-Path $env:APPDATA 'Microsoft\Crypto\Keys'), (Join-Path $env:APPDATA 'Microsoft\Crypto\RSA'))
    $keysBefore = @($keyFolders | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File -Force -ErrorAction SilentlyContinue } | ForEach-Object { $_.FullName })
    $code = Invoke-Sign "-Paths '$victim' -PfxPath '$pfx' -TestOnlySkipUsbCheck -TestOnlyNoTimestamp"
    $sig = Get-AuthenticodeSignature -LiteralPath $victim
    Assert-True ($code -eq 0 -and $Out -match 'Release signatures verified') "real signing (no timestamp) exits 0 and verifies$(if ($code -ne 0) { ': ' + $Out.Trim() })"
    Assert-True ($sig.SignerCertificate -and $sig.SignerCertificate.Thumbprint -eq $cert.Thumbprint) 'throwaway file is signed by the throwaway certificate'
    Assert-True ("$($sig.Status)" -in @('UnknownError', 'NotTrusted')) "an untrusted self-signed signature reads UnknownError/NotTrusted (got $($sig.Status)), as on Workpc before the trust step"
    $keysAfter = @($keyFolders | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File -Force -ErrorAction SilentlyContinue } | ForEach-Object { $_.FullName })
    $newKeys = @($keysAfter | Where-Object { $keysBefore -notcontains $_ })
    Assert-True ($newKeys.Count -eq 0) "no private-key file left under %APPDATA%\Microsoft\Crypto after signing (EphemeralKeySet)$(if ($newKeys.Count) { ': ' + ($newKeys -join ', ') })"
    # Change the signed content itself (text appended after the signature block is not covered the same way).
    [IO.File]::WriteAllText($victim, ([IO.File]::ReadAllText($victim).Replace('Write-Output 1', 'Write-Output 9')))
    Assert-True ((Get-AuthenticodeSignature -LiteralPath $victim).Status -eq 'HashMismatch') 'a tampered file is detected (HashMismatch)'

    $ts = $env:ETP_SIGNING_TEST_TIMESTAMP_URL
    if ($ts) {
        $stamped = Join-Path $root 'stamped.ps1'; [IO.File]::WriteAllText($stamped, 'Write-Output 2')
        $code = Invoke-Sign "-Paths '$stamped' -PfxPath '$pfx' -TimestampServer '$ts' -TestOnlySkipUsbCheck"
        $sig = Get-AuthenticodeSignature -LiteralPath $stamped
        Assert-True ($code -eq 0 -and $sig.SignerCertificate.Thumbprint -eq $cert.Thumbprint -and $null -ne $sig.TimeStamperCertificate -and "$($sig.Status)" -in @('UnknownError', 'NotTrusted')) "timestamped signing: our signer, a TimeStamperCertificate, status UnknownError/NotTrusted (got $($sig.Status))"
    }
    else { Write-Host 'SKIP  timestamped signing (set ETP_SIGNING_TEST_TIMESTAMP_URL, e.g. http://timestamp.digicert.com, internet needed); the untimestamped real signing above ran' }

    # Installer script refuses a release folder containing a .pdb
    $installerText = Get-Content -Raw -LiteralPath $installerScript
    Assert-True ($installerText -match '\*\.pdb' -and $installerText -match 'cannot be packed') 'installer script contains the PDB refusal'
    $rel = Join-Path $root 'rel'; [void](New-Item -ItemType Directory -Path $rel)
    [IO.File]::WriteAllText((Join-Path $rel 'Etp.Reporting.Reporting.pdb'), 'x')
    $fakeCompiler = Join-Path $root 'ISCC.exe'; [IO.File]::WriteAllText($fakeCompiler, 'x')
    $code = Invoke-Ps $installerScript @('-SkipReleaseBuild', '-ReleaseDirectory', $rel, '-OutputDirectory', (Join-Path $root 'out'), '-InnoSetupCompiler', $fakeCompiler)
    Assert-True ($code -ne 0 -and $Out -match 'cannot be packed') 'installer build refuses a folder containing a .pdb'

    # Trust script: refuses a PFX, needs a typed YES, changes no store in simulate mode
    $code = Invoke-Ps $trust @('-CertificatePath', $pfx, '-TestOnlySimulate')
    Assert-True ($code -ne 0 -and $Out -match '\.cer') 'trust script refuses a PFX'
    $code = Invoke-Ps $trust @('-CertificatePath', $cer, '-TestOnlySimulate') 'no'
    Assert-True ($code -ne 0 -and $Out -match 'Nothing was changed') 'trust script does nothing unless YES is typed'
    $code = Invoke-Ps $trust @('-CertificatePath', $cer, '-TestOnlySimulate') 'yes'
    Assert-True ($code -ne 0) 'lower-case yes is not accepted'
    $code = Invoke-Ps $trust @('-CertificatePath', $cer, '-TestOnlySimulate') 'YES'
    Assert-True ($code -eq 0 -and $Out -match $cert.Thumbprint -and $Out -match 'SHA-256 fingerprint') 'trust script prints the fingerprints and accepts YES'
    Assert-True ($Out -match ('File:\s+' + [regex]::Escape([IO.Path]::GetFullPath($cer)))) 'trust script names the file it is about to trust (R1-10)'
    $trustText = Get-Content -Raw -LiteralPath $trust
    Assert-True ($trustText -match 'certlm\.msc' -and $trustText -notmatch 'certmgr\.msc \(or') 'trust script names certlm.msc (Local Machine) for the undo step'
    $cert.Dispose(); $rsa.Dispose()
}
finally { Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue }
if ($failures.Count -gt 0) { Write-Host "$($failures.Count) FAILED"; exit 1 }
Write-Host 'All signing self-tests passed.'; exit 0
