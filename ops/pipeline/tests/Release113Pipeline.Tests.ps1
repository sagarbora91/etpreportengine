<#
Self-test for the 1.13.0 publishability rules (R1-01, R1-05; review 16 Oct 2026). Plain PowerShell, no Pester, temp folders only:
no real build, no SQL, no real key, nothing under E:\ETP\Installers or OneDrive.
  1. Build-Installer-1.13.0.ps1 with a fake worktree, a fake build-windows-installer.ps1 and a fake build lock: a failed build and an
     unsigned setup exe both leave NO installer-*/windows-release* folder (all FAILED-*, NOT-PUBLISHABLE.txt) and no evidence; a repeat
     run refuses before touching an earlier build's folders.
  2. Publish-Release-1.13.0.ps1 against a fake artifacts folder: unsigned and untimestamped installers are refused (unless
     -AllowUnprotected for a build that does not claim protection); a protected build needs publish-evidence-<tag>.json that matches
     the installer bytes, HEAD, signer and the archived map. The signed cases need ETP_SIGNING_TEST_TIMESTAMP_URL (internet) and an
     unsigned PE to sign (default: the Release build's Etp.Reporting.Desktop.exe); without them they print SKIP.
Run (pwsh 7, the chain's shell):  E:\Tools\PowerShell\7\pwsh.exe -NoProfile -File ops\pipeline\tests\Release113Pipeline.Tests.ps1
Exit code 0 = all passed.
#>
param([string]$SampleExe)
$ErrorActionPreference = 'Stop'
$pipeline = Split-Path -Parent $PSScriptRoot
$repo = Split-Path -Parent (Split-Path -Parent $pipeline)
if (-not $SampleExe) { $SampleExe = Join-Path $repo 'src\Etp.Reporting.Desktop\bin\Release\net10.0-windows\Etp.Reporting.Desktop.exe' }
$sign = Join-Path $repo 'scripts\sign-etp-artifacts.ps1'
$pwsh = (Get-Process -Id $PID).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('etp-pipeline113-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$failures = New-Object System.Collections.ArrayList
function Check($c, [string]$m) { if ($c) { Write-Host "PASS  $m" } else { Write-Host "FAIL  $m"; [void]$failures.Add($m) } }
function New-FakeRepo([string]$Path) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Push-Location $Path
    try { git init -q; git config user.email test@example.invalid; git config user.name test; Set-Content x.txt 'x'; git add x.txt; git commit -q -m init }
    finally { Pop-Location }
}
try {
    # ------------------------------------------------------------------ 1. Build-Installer-1.13.0: quarantine
    $wt = Join-Path $root 'bi\wt'; $tools = Join-Path $root 'bi\tools'; $pipe = Join-Path $root 'bi\pipe'
    New-Item -ItemType Directory -Path $tools, $pipe, (Join-Path $wt 'scripts\release') -Force | Out-Null
    New-FakeRepo $wt
    $tag = (git -C $wt rev-parse --short=7 HEAD).Trim() + '-b1'
    @'
param($ReleaseDirectory, $OutputDirectory, [switch]$Protect, $PfxPath, $TimestampServer, $InnoSetupCompiler, $SqlPayloadDirectory)
New-Item -ItemType Directory -Path $ReleaseDirectory, "$ReleaseDirectory-unprotected", "$ReleaseDirectory-symbols", $OutputDirectory -Force | Out-Null
Set-Content (Join-Path "$ReleaseDirectory-symbols" 'Mapping.txt') 'map'
'{"version":"1.13.0","commit":"x","protection":{"obfuscated":true}}' | Set-Content (Join-Path $ReleaseDirectory 'release.json')
Set-Content (Join-Path $OutputDirectory 'EtpReportingEngine-Setup-1.13.0-x64.exe') 'not signed'
if ($env:ETP_FAKE_BUILD_MODE -eq 'fail') { Write-Output 'fake build failed'; exit 1 }
Write-Output 'fake build ok'
'@ | Set-Content (Join-Path $wt 'scripts\build-windows-installer.ps1')
    @'
param([string]$Command, [string]$WorkingDirectory, [int]$WaitMinutes, [string]$Label)
$errors = $null; [void][Management.Automation.Language.Parser]::ParseInput($Command, [ref]$null, [ref]$errors)
if ($errors.Count) { Write-Output "COMMAND PARSE ERRORS: $($errors | ForEach-Object { $_.Message })"; exit 99 }
Set-Location $WorkingDirectory
& (Get-Process -Id $PID).Path -NoProfile -Command $Command
exit $LASTEXITCODE
'@ | Set-Content (Join-Path $tools 'Invoke-Serialized.ps1')
    # A copy without the "PFX must be on the USB stick" drive check: the test PFX is a temp file.
    $text = (Get-Content -Raw -LiteralPath (Join-Path $pipeline 'Build-Installer-1.13.0.ps1')) -replace "(?m)^\s*if \(\(Split-Path -Qualifier \`$pfxFull\).*\r?\n", ''
    $bi = Join-Path $pipe 'Build-Installer-1.13.0.ps1'; Set-Content -LiteralPath $bi -Value $text
    $fakePfx = Join-Path $root 'bi\fake.pfx'; Set-Content $fakePfx 'x'
    $art = Join-Path $wt 'artifacts'
    foreach ($mode in 'fail', 'unsigned') {
        $env:ETP_FAKE_BUILD_MODE = $mode
        Get-ChildItem $art -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
        $out = & $pwsh -NoProfile -File $bi -Version 1.13.0 -Worktree $wt -NoSqlPayload -Protect -PfxPath $fakePfx -ToolsDirectory $tools -PowerShellExe $pwsh 2>&1 | Out-String
        $code = $LASTEXITCODE
        $names = @(Get-ChildItem $art -Directory | ForEach-Object Name)
        Check ($code -ne 0 -and $out -notmatch 'COMMAND PARSE ERRORS') "build ${mode}: non-zero exit and the serialised command parses"
        Check (@($names | Where-Object { $_ -like 'installer-*' -or $_ -like 'windows-release*' }).Count -eq 0 -and @($names | Where-Object { $_ -like 'FAILED-*' }).Count -eq 4) "build ${mode}: all four folders renamed FAILED-* ($($names -join ', '))"
        Check (@(Get-ChildItem $art -Directory -Filter 'FAILED-*installer-*' | Where-Object { Test-Path (Join-Path $_.FullName 'NOT-PUBLISHABLE.txt') }).Count -eq 1 -and -not (Test-Path (Join-Path $art "publish-evidence-$tag.json"))) "build ${mode}: NOT-PUBLISHABLE.txt and no publish evidence"
        Check ((Get-Content (Join-Path $pipe 'installer-build-1.13.0-exit.txt')) -match 'Exit code: [1-9]') "build ${mode}: the exit file says non-zero (the chain writes 'Installer build failed.')"
    }
    Check ($out -match 'not signed with a timestamp') 'build unsigned: the unsigned setup exe is named as the reason'
    Get-ChildItem $art | Remove-Item -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $art "installer-$tag") | Out-Null
    $out = & $pwsh -NoProfile -File $bi -Version 1.13.0 -Worktree $wt -NoSqlPayload -Protect -PfxPath $fakePfx -ToolsDirectory $tools -PowerShellExe $pwsh 2>&1 | Out-String
    Check ($LASTEXITCODE -ne 0 -and $out -match 'already exists from an earlier build' -and -not (Test-Path (Join-Path $art "installer-$tag\NOT-PUBLISHABLE.txt"))) 'a repeat run refuses and leaves the earlier build untouched'
    Remove-Item Env:ETP_FAKE_BUILD_MODE -ErrorAction SilentlyContinue

    # ------------------------------------------------------------------ 2. Publish-Release-1.13.0: evidence and signature rules
    $wt = Join-Path $root 'pub\wt'; $inst = Join-Path $root 'pub\Installers'; $sym = Join-Path $root 'pub\Symbols\1.13.0-abcdef1'
    New-Item -ItemType Directory -Path $inst, $sym, (Join-Path $wt 'database\migrations') -Force | Out-Null
    New-FakeRepo $wt
    $commit = (git -C $wt rev-parse HEAD).Trim(); $tag = 'abcdef1-b1'
    $pub = Join-Path $pipeline 'Publish-Release-1.13.0.ps1'
    $art = Join-Path $wt 'artifacts'; $instDir = Join-Path $art "installer-$tag"; $relDir = Join-Path $art "windows-release-$tag"
    New-Item -ItemType Directory -Path $instDir, $relDir | Out-Null
    $setup = Join-Path $instDir 'EtpReportingEngine-Setup-1.13.0-x64.exe'
    Set-Content (Join-Path $sym 'Mapping.txt') 'the map'
    $mapHash = (Get-FileHash (Join-Path $sym 'Mapping.txt') -Algorithm SHA256).Hash
    function Set-Receipt([bool]$Protected) {
        $r = [ordered]@{ version = '1.13.0'; commit = $commit }
        if ($Protected) { $r.protection = [ordered]@{ obfuscated = $true; profile = 'Standard'; mappingSha256 = $mapHash } }
        $r | ConvertTo-Json | Set-Content (Join-Path $relDir 'release.json')
    }
    function Invoke-Pub([string[]]$Extra) {
        Get-ChildItem $inst -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
        $script:Out = & $pwsh -NoProfile -File $pub -Version 1.13.0 -Worktree $wt -InstallersRoot $inst @Extra 2>&1 | Out-String
        return $LASTEXITCODE
    }
    Set-Content $setup 'not signed'
    Set-Receipt $true
    $c = Invoke-Pub @(); Check ($c -ne 0 -and $Out -match 'NOT SIGNED') 'publish: protected claim + unsigned installer refused'
    $c = Invoke-Pub @('-AllowUnprotected'); Check ($c -ne 0 -and $Out -match 'NOT SIGNED') 'publish: -AllowUnprotected does not let an unsigned build that claims protection through'
    Set-Receipt $false
    $c = Invoke-Pub @(); Check ($c -ne 0 -and $Out -match 'NOT SIGNED') 'publish: unsigned unprotected build refused without -AllowUnprotected (R1-05)'
    $c = Invoke-Pub @('-AllowUnprotected'); Check ($c -eq 0 -and $Out -match 'obfuscation=NONE') 'publish: unsigned unprotected build published with -AllowUnprotected, labelled NONE / NOT SIGNED'
    $ts = $env:ETP_SIGNING_TEST_TIMESTAMP_URL
    if (-not $ts -or -not (Test-Path -LiteralPath $SampleExe -PathType Leaf)) { Write-Host "SKIP  signed publish cases (need ETP_SIGNING_TEST_TIMESTAMP_URL and an unsigned exe at $SampleExe)" }
    else {
        Copy-Item -LiteralPath $SampleExe -Destination $setup -Force
        $rsa = [Security.Cryptography.RSA]::Create(2048)
        $req = New-Object Security.Cryptography.X509Certificates.CertificateRequest('CN=ETP Test Only', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $eku = New-Object Security.Cryptography.OidCollection; [void]$eku.Add((New-Object Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
        $req.CertificateExtensions.Add((New-Object Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($eku, $true)))
        $cert = $req.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(30))
        $pw = ConvertTo-SecureString 'throwaway-test-passphrase' -AsPlainText -Force
        $pfx = Join-Path $root 'pub\test.pfx'; [IO.File]::WriteAllBytes($pfx, $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $pw))
        $unstamped = Join-Path $root 'pub\unstamped.exe'; Copy-Item -LiteralPath $SampleExe -Destination $unstamped
        & $sign -Paths @($unstamped) -PfxPath $pfx -Passphrase $pw -TestOnlySkipUsbCheck -TestOnlyNoTimestamp 3>$null | Out-Null
        Copy-Item -LiteralPath $unstamped -Destination $setup -Force
        Set-Receipt $false
        $c = Invoke-Pub @(); Check ($c -ne 0 -and $Out -match 'WITHOUT a timestamp') 'publish: an installer signed without a timestamp is refused'
        Copy-Item -LiteralPath $SampleExe -Destination $setup -Force
        & $sign -Paths @($setup) -PfxPath $pfx -Passphrase $pw -TestOnlySkipUsbCheck -TimestampServer $ts 3>$null | Out-Null
        $hash = (Get-FileHash $setup -Algorithm SHA256).Hash
        Set-Receipt $true
        $c = Invoke-Pub @('-AllowUnprotected'); Check ($c -ne 0 -and $Out -match 'no smoke evidence') 'publish: a signed protected build without smoke evidence is refused, -AllowUnprotected or not (R1-01)'
        $ev = [ordered]@{ schema = 1; result = 'SMOKE-PASSED'; smoke = 'IDENTICAL'; tag = $tag; version = '1.13.0'; commit = $commit; installer = (Split-Path -Leaf $setup); installerSha256 = $hash
            signerThumbprint = $cert.Thumbprint; timestamped = $true; mappingSha256 = $mapHash; profile = 'Standard'; symbolsArchive = $sym; smokeReport = 'x'; writtenUtc = 'x' }
        $evPath = Join-Path $art "publish-evidence-$tag.json"
        foreach ($case in @(
                @{ Key = 'installerSha256'; Value = ('00' * 32); Expect = 'changed or rebuilt after the smoke'; Name = 'other installer bytes' },
                @{ Key = 'commit'; Value = ('1' * 40); Expect = 'HEAD'; Name = 'another commit' },
                @{ Key = 'result'; Value = 'SMOKE-FAILED'; Expect = 'not SMOKE-PASSED'; Name = 'a failed smoke' },
                @{ Key = 'signerThumbprint'; Value = ('A' * 40); Expect = 'not the evidence signer'; Name = 'another signer' })) {
            $bad = [ordered]@{} + $ev; $bad[$case.Key] = $case.Value; $bad | ConvertTo-Json | Set-Content $evPath
            $c = Invoke-Pub @(); Check ($c -ne 0 -and $Out -match $case.Expect) "publish: evidence for $($case.Name) is refused"
        }
        $ev | ConvertTo-Json | Set-Content $evPath
        Set-Content (Join-Path $sym 'Mapping.txt') 'another map'
        $c = Invoke-Pub @(); Check ($c -ne 0 -and $Out -match 'archived map') 'publish: an archived map that is not this build''s is refused'
        Set-Content (Join-Path $sym 'Mapping.txt') 'the map'
        $c = Invoke-Pub @(); Check ($c -eq 0 -and $Out -match 'smoke IDENTICAL') 'publish: matching evidence publishes'
        $notes = (Get-Content -Raw (Join-Path $inst 'ETP Reporting Engine 1.13.0\release.json') | ConvertFrom-Json).notes
        Check ($notes -match 'obfuscation=Obfuscar' -and $notes -match 'Authenticode=signed') 'publish: the notes keep the strings Run-VM-1.13.0-Sequence checks'
        Set-Content (Join-Path $instDir 'NOT-PUBLISHABLE.txt') 'failed'
        $c = Invoke-Pub @(); Check ($c -ne 0 -and $Out -match 'NOT-PUBLISHABLE') 'publish: a folder marked NOT-PUBLISHABLE is refused'
        $cert.Dispose(); $rsa.Dispose()
    }
}
finally {
    Remove-Item Env:ETP_FAKE_BUILD_MODE -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failures.Count -gt 0) { Write-Host "$($failures.Count) FAILED"; exit 1 }
Write-Host 'All 1.13.0 pipeline self-tests passed.'; exit 0
