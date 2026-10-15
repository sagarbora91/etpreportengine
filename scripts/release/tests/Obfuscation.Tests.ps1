<#
Self-test for the protected-release obfuscation step (code-protection plan, lane O1). Plain PowerShell (5.1 or 7), no
Pester. Never runs Obfuscar, never builds, never touches E:\Tools, E:\ETP\Reference\Symbols or a release folder: every
file it makes is in a fresh temp folder. Run:
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\release\tests\Obfuscation.Tests.ps1
Exit code 0 = all passed.
#>
$ErrorActionPreference = 'Stop'
$release = Split-Path -Parent $PSScriptRoot
$scripts = Split-Path -Parent $release
$repoRoot = Split-Path -Parent $scripts
$template = Join-Path $release 'obfuscar.xml'
$root = Join-Path ([IO.Path]::GetTempPath()) ('etp-obfuscation-test-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root)
$failures = New-Object System.Collections.ArrayList
function Assert-True($condition, $message) { if ($condition) { Write-Host "PASS  $message" } else { Write-Host "FAIL  $message"; [void]$failures.Add($message) } }
function Get-Thrown([scriptblock]$block) { try { & $block; return $null } catch { return $_.Exception.Message } }

# Functions only (the script returns after defining them when dot-sourced). Explicit values: a dot-sourced param block
# assigns its parameters in this scope.
. (Join-Path $release 'Invoke-EtpObfuscation.ps1') -ObfuscationProfile Standard -DesktopObfuscation Strings -ObfuscarRoot (Join-Path $root 'no-tools')

try {
    # ---- Configuration parses and has the settings the code needs ----
    [xml]$config = Get-Content -LiteralPath $template -Raw
    Assert-True ($null -ne $config.Obfuscator) 'obfuscar.xml parses and has an <Obfuscator> root'
    function Get-Var($name) { $n = $config.SelectSingleNode("/Obfuscator/Var[@name='$name']"); if ($n) { $n.GetAttribute('value') } }
    Assert-True ((Get-Var 'RenameProperties') -eq 'false') 'RenameProperties=false (WPF bindings, TypeDescriptor, GetProperty, System.Text.Json)'
    Assert-True ((Get-Var 'RenameFields') -eq 'false') 'RenameFields=false (enum members parsed and serialised by name)'
    Assert-True ((Get-Var 'RenameEvents') -eq 'false') 'RenameEvents=false'
    Assert-True ((Get-Var 'HideStrings') -eq 'true') 'HideStrings=true'
    Assert-True ((Get-Var 'KeepPublicApi') -eq 'true') 'Template default is the Standard profile (KeepPublicApi=true)'
    Assert-True ((Get-Var 'HidePrivateApi') -eq 'true') 'HidePrivateApi=true'
    Assert-True ((Get-Var 'SkipGenerated') -eq 'true') 'SkipGenerated=true (anonymous types serialised to JSON, state machines, generated regex)'
    Assert-True ((Get-Var 'UseUnicodeNames') -eq 'false' -and (Get-Var 'UseKoreanNames') -eq 'false') 'ASCII names only (diagnostics token CHECKs)'
    Assert-True ((Get-Var 'ReuseNames') -eq 'false') 'ReuseNames=false'
    Assert-True ($null -ne $config.SelectSingleNode('/Obfuscator/AssemblySearchPath')) 'AssemblySearchPath present'

    # ---- Every ETP assembly is listed; every product project is covered ----
    $listed = @($config.SelectNodes('/Obfuscator/Module') | ForEach-Object { Split-Path -Leaf $_.GetAttribute('file') })
    foreach ($module in (Get-EtpObfuscationModules -DesktopObfuscation Strings)) { Assert-True ($listed -contains $module) "obfuscar.xml lists $module" }
    $projects = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -Filter '*.csproj' -File | ForEach-Object { $_.BaseName + '.dll' })
    Assert-True ($projects.Count -ge 7) "found the product projects under src ($($projects.Count))"
    foreach ($project in $projects) { Assert-True ((Get-EtpObfuscationModules -DesktopObfuscation Strings) -contains $project) "product assembly $project is in the obfuscation module list (a new project must be listed or excluded on purpose)" }

    # ---- Desktop: every namespace declared in source is skipped for renaming ----
    $desktopModule = $config.SelectSingleNode("/Obfuscator/Module[contains(@file,'Etp.Reporting.Desktop.dll')]")
    $skipped = @($desktopModule.SelectNodes('SkipNamespace') | ForEach-Object { $_.GetAttribute('name') })
    Assert-True ($skipped -contains 'XamlGeneratedNamespace') 'Desktop: XamlGeneratedNamespace (GeneratedInternalTypeHelper, found by WPF via reflection) is skipped'
    $desktopSource = Join-Path $repoRoot 'src\Etp.Reporting.Desktop'
    $declared = @(Get-ChildItem -LiteralPath $desktopSource -Recurse -Filter '*.cs' -File | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
        Select-String -Pattern '^\s*namespace\s+([A-Za-z0-9_.]+)' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
    Assert-True ($declared.Count -gt 5) "found Desktop namespaces in source ($($declared.Count))"
    foreach ($ns in $declared) { Assert-True ($skipped -contains $ns) "Desktop namespace $ns is skipped for renaming (BAML refers to Desktop types by name)" }
    $renaming = @($desktopModule.SelectNodes('SkipType|SkipMethod|ForceMethod|ForceType'))
    Assert-True ($renaming.Count -eq 0) 'Desktop module has no Force*/partial skips (all-or-nothing by namespace)'

    # ---- Every excluded type still exists in source (a rename would silently make an exclusion stale) ----
    $allSource = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -Filter '*.cs' -File | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })
    $skipTypes = @($config.SelectNodes('/Obfuscator/Module/SkipType') | ForEach-Object { $_.GetAttribute('name') } | Sort-Object -Unique)
    Assert-True ($skipTypes.Count -ge 20) "obfuscar.xml has the derived SkipType list ($($skipTypes.Count) entries)"
    foreach ($fullName in $skipTypes) {
        $simple = ($fullName -split '[./+]')[-1]
        $found = @($allSource | Select-String -Pattern "\b(class|record|struct|enum)\s+$simple\b" -List)
        Assert-True ($found.Count -gt 0) "excluded type $fullName is declared in src"
    }
    $nested = @($skipTypes | Where-Object { $_ -match '/' })
    foreach ($n in $nested) { Assert-True ($skipTypes -contains ($n -replace '/', '+')) "nested exclusion $n is also written with '+'" }
    foreach ($needed in 'Etp.Reporting.Infrastructure.SqlServer.SqlServerImportHistoryQuery/StoredIssue',
                        'Etp.Reporting.Infrastructure.SqlServer.Tally.SqlServerTallyProfileService/StoreCostCentre') {
        $node = $config.SelectSingleNode("/Obfuscator/Module/SkipType[@name='$needed']")
        Assert-True ($null -ne $node -and $node.GetAttribute('skipMethods') -eq 'true') "private JSON record $needed keeps its constructor parameter names (skipMethods)"
    }

    # ---- Filling the template ----
    $in = Join-Path $root 'in'; $out = Join-Path $root 'out'
    $standard = New-EtpObfuscarConfig -Template $template -InPath $in -OutPath $out -ObfuscationProfile Standard -DesktopObfuscation Strings
    Assert-True ($standard.SelectSingleNode("/Obfuscator/Var[@name='KeepPublicApi']").GetAttribute('value') -eq 'true') 'Standard: KeepPublicApi=true'
    Assert-True ($standard.SelectSingleNode("/Obfuscator/Var[@name='InPath']").GetAttribute('value') -eq [IO.Path]::GetFullPath($in)) 'InPath filled'
    Assert-True ($standard.SelectSingleNode("/Obfuscator/Var[@name='LogFile']").GetAttribute('value') -like "$([IO.Path]::GetFullPath($out))*Mapping.txt") 'Map written into OutPath (inside the symbols folder)'
    Assert-True ($standard.SelectSingleNode('/Obfuscator/AssemblySearchPath').GetAttribute('path') -eq [IO.Path]::GetFullPath($in)) 'Search path = the publish folder'
    Assert-True (@($standard.SelectNodes('/Obfuscator/Module')).Count -eq 7) 'Standard + Strings: seven modules'
    Assert-True ($standard.OuterXml -notmatch '__FILLED_BY_') 'No placeholder left in the filled configuration'
    $none = New-EtpObfuscarConfig -Template $template -InPath $in -OutPath $out -ObfuscationProfile Standard -DesktopObfuscation None
    Assert-True (@($none.SelectNodes('/Obfuscator/Module')).Count -eq 6 -and $none.OuterXml -notmatch 'Etp\.Reporting\.Desktop\.dll') 'DesktopObfuscation None: Desktop left out, six modules'
    $strong = New-EtpObfuscarConfig -Template $template -InPath $in -OutPath $out -ObfuscationProfile Strong -DesktopObfuscation Strings
    Assert-True ($strong.SelectSingleNode("/Obfuscator/Var[@name='KeepPublicApi']").GetAttribute('value') -eq 'false') 'Strong: KeepPublicApi=false'
    $message = Get-Thrown { New-EtpObfuscarConfig -Template $template -InPath $in -OutPath $out -ObfuscationProfile Strong -DesktopObfuscation None }
    Assert-True ($message -match 'Desktop') 'Strong with Desktop left out is refused'

    # ---- Tool pin and refusal ----
    Assert-True ($script:EtpObfuscarVersion -match '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') "tool version is pinned exactly ($script:EtpObfuscarVersion)"
    $doc = Join-Path $repoRoot 'docs\security\RELEASE-PROTECTION.md'
    Assert-True ((Test-Path $doc) -and (Get-Content -Raw $doc).Contains("Obfuscar.GlobalTool $script:EtpObfuscarVersion")) 'RELEASE-PROTECTION.md names the same pinned version'
    $notices = Get-Content -Raw (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.txt')
    Assert-True ($notices -match 'Obfuscar') 'THIRD-PARTY-NOTICES.txt names Obfuscar (build tool, not shipped)'

    $tools = Join-Path $root 'tools'
    $message = Get-Thrown { Get-EtpObfuscarTool -ObfuscarRoot $tools }
    Assert-True ($message -match 'not installed' -and $message -match 'dotnet tool install Obfuscar.GlobalTool --version') 'Missing tool: refused, and the message says exactly what to install'
    $v = $script:EtpObfuscarVersion
    $toolDir = Join-Path $tools $v
    [void](New-Item -ItemType Directory -Path $toolDir -Force)
    [IO.File]::WriteAllText((Join-Path $toolDir 'obfuscar.console.exe'), 'not a real tool')
    [void](New-Item -ItemType Directory -Path (Join-Path $toolDir '.store\obfuscar.globaltool\9.9.9') -Force)
    $message = Get-Thrown { Get-EtpObfuscarTool -ObfuscarRoot $tools }
    Assert-True ($message -match 'expected exactly') 'Wrong version in the tool store: refused'
    Remove-Item -LiteralPath (Join-Path $toolDir '.store\obfuscar.globaltool\9.9.9') -Recurse -Force
    $pkgDir = Join-Path $toolDir ".store\obfuscar.globaltool\$v\obfuscar.globaltool\$v"
    [void](New-Item -ItemType Directory -Path $pkgDir -Force)
    $nupkg = Join-Path $pkgDir "obfuscar.globaltool.$v.nupkg"
    [IO.File]::WriteAllText($nupkg, 'fake package')
    $fakeHash = (Get-FileHash -LiteralPath $nupkg -Algorithm SHA256).Hash
    $message = Get-Thrown { Get-EtpObfuscarTool -ObfuscarRoot $tools -ExpectedSha256 '' }
    Assert-True ($message -match 'No SHA-256 is recorded' -and $message.Contains($fakeHash)) 'No recorded hash: refused, and the actual hash is printed for the integrator'
    $message = Get-Thrown { Get-EtpObfuscarTool -ObfuscarRoot $tools -ExpectedSha256 ('0' * 64) }
    Assert-True ($message -match 'tampered') 'Hash mismatch: refused'
    $exe = Get-EtpObfuscarTool -ObfuscarRoot $tools -ExpectedSha256 $fakeHash
    Assert-True ($exe -eq (Join-Path $toolDir 'obfuscar.console.exe')) 'Pinned version with the recorded hash: accepted'

    # ---- The installed tool (read-only check; never runs it, never installs it) ----
    $installedRoot = if ($env:ETP_OBFUSCAR_ROOT) { $env:ETP_OBFUSCAR_ROOT } else { 'E:\Tools\Obfuscar' }
    if (Test-Path -LiteralPath (Join-Path $installedRoot "$v\obfuscar.console.exe") -PathType Leaf) {
        $message = Get-Thrown { $null = Get-EtpObfuscarTool -ObfuscarRoot $installedRoot }
        Assert-True ($null -eq $message) "installed Obfuscar $v under $installedRoot matches the recorded package SHA-256 $(if ($message) { "($message)" })"
    }
    else { Write-Host "SKIP  installed-tool check: Obfuscar $v is not installed under $installedRoot (install it pinned, see docs\security\RELEASE-PROTECTION.md section 3)" }

    # ---- Folders: symbols never inside the release folder ----
    Assert-True (-not (Test-EtpPathInside -Child 'C:\a\rel-symbols' -Parent 'C:\a\rel')) '<out>-symbols is not inside <out>'
    Assert-True (-not (Test-EtpPathInside -Child 'C:\a\rel-unprotected' -Parent 'C:\a\rel')) '<out>-unprotected is not inside <out>'
    Assert-True (Test-EtpPathInside -Child 'C:\a\rel\symbols' -Parent 'C:\a\rel') 'a folder below <out> is detected'
    Assert-True (Test-EtpPathInside -Child 'C:\A\REL' -Parent 'c:\a\rel\') 'comparison ignores case and a trailing slash'
    $unprot = Join-Path $root 'r-unprotected'; $prot = Join-Path $root 'r'
    foreach ($d in $unprot, $prot) { [void](New-Item -ItemType Directory -Path $d) }
    $message = Get-Thrown { Invoke-EtpObfuscation -UnprotectedDirectory $unprot -ProtectedDirectory $prot -SymbolsDirectory (Join-Path $prot 'symbols') -ObfuscarRoot $tools -ConfigTemplate $template }
    Assert-True ($message -match 'must not be inside') 'Invoke refuses a symbols folder inside the protected folder (before running anything)'
    Assert-True (-not (Test-Path (Join-Path $prot 'symbols'))) '... and creates nothing there'
    $message = Get-Thrown { Invoke-EtpObfuscation -UnprotectedDirectory $unprot -ProtectedDirectory $prot -SymbolsDirectory (Join-Path $root 'r-symbols') -ObfuscarRoot (Join-Path $root 'empty') -ConfigTemplate $template }
    Assert-True ($message -match 'not installed') 'Invoke refuses when the tool is missing'
    Assert-True (-not (Test-Path (Join-Path $root 'r-symbols'))) '... before creating the symbols folder'

    # ---- build-windows-release.ps1 wiring (read as text; nothing is built) ----
    $build = Get-Content -Raw (Join-Path $scripts 'build-windows-release.ps1')
    Assert-True ($build -match '\[switch\]\$Protect') 'build-windows-release.ps1 has -Protect'
    Assert-True ($build -match '-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:Version=\$Version -o \$output') 'Default (no -Protect) publish is unchanged: single file, natives not self-extracted'
    Assert-True ($build -match '-p:PublishSingleFile=false -p:Version=\$Version -o \$publishDirectory') '-Protect publishes as a folder (decision D4)'
    Assert-True ($build.Contains('$unprotectedOutput = "$output-unprotected"') -and $build.Contains('$symbolsOutput = "$output-symbols"')) 'Frozen contract: <out>-unprotected and <out>-symbols'
    $iObf = $build.IndexOf('$obfuscation = Invoke-EtpObfuscation'); $iSign = $build.IndexOf("sign-etp-artifacts.ps1') @pfxArguments")
    Assert-True ($iObf -gt 0 -and $iSign -gt $iObf) 'Signing happens after obfuscation'
    $iTests = $build.IndexOf('Release test suite'); $iPublish = $build.IndexOf('Self-contained desktop folder publish')
    Assert-True ($iTests -gt 0 -and $iPublish -gt $iTests -and $iObf -gt $iPublish) 'Order: tests, then publish, then obfuscation'
    $iCheck = $build.IndexOf('Get-EtpObfuscarTool -ObfuscarRoot'); $iRestore = $build.IndexOf('dotnet restore')
    Assert-True ($iCheck -gt 0 -and $iCheck -lt $iRestore) 'The pinned tool is checked before the long build and test run'
    Assert-True ($build -match 'pdbShipped = \$false') 'release.json of a protected build records that no PDB is shipped'

    # ---- Archive-EtpSymbols.ps1 ----
    $archive = Join-Path $release 'Archive-EtpSymbols.ps1'
    $relDir = Join-Path $root 'a\rel'; $symDir = Join-Path $root 'a\rel-symbols'
    foreach ($d in $relDir, $symDir) { [void](New-Item -ItemType Directory -Path $d -Force) }
    [IO.File]::WriteAllText((Join-Path $symDir 'Mapping.txt'), 'map')
    [IO.File]::WriteAllText((Join-Path $symDir 'obfuscation-report.json'), '{}')
    $mapHash = (Get-FileHash -LiteralPath (Join-Path $symDir 'Mapping.txt') -Algorithm SHA256).Hash
    $receipt = @{ version = '1.13.0'; commit = 'abcdef1234567890abcdef1234567890abcdef12'; protection = @{ obfuscated = $true; mappingSha256 = $mapHash } } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $relDir 'release.json'), $receipt)
    $message = Get-Thrown { & $archive -SymbolsDirectory $symDir -ReleaseDirectory $relDir -ArchiveRoot $relDir }
    Assert-True ($message -match 'must not be inside') 'Archive inside the release folder: refused'
    $message = Get-Thrown { & $archive -SymbolsDirectory $symDir -ReleaseDirectory $relDir -ArchiveRoot (Join-Path $root 'OneDrive\Symbols') }
    Assert-True ($message -match 'OneDrive') 'Archive inside OneDrive: refused'
    $archiveRoot = Join-Path $root 'Symbols'
    $target = & $archive -SymbolsDirectory $symDir -ReleaseDirectory $relDir -ArchiveRoot $archiveRoot
    Assert-True ($target -eq (Join-Path $archiveRoot '1.13.0-abcdef1') -and (Test-Path (Join-Path $target 'Mapping.txt')) -and (Test-Path (Join-Path $target 'SHA256SUMS.txt'))) 'Archive written to <root>\<version>-<sha7> with SHA256SUMS.txt'
    $message = Get-Thrown { & $archive -SymbolsDirectory $symDir -ReleaseDirectory $relDir -ArchiveRoot $archiveRoot }
    Assert-True ($message -match 'never overwritten') 'An existing archive is never overwritten'
    [IO.File]::WriteAllText((Join-Path $symDir 'Mapping.txt'), 'another map')
    $message = Get-Thrown { & $archive -SymbolsDirectory $symDir -ReleaseDirectory $relDir -ArchiveRoot $archiveRoot -Label 'b2' }
    Assert-True ($message -match 'different builds') 'A map that is not the one release.json names: refused'
    Remove-Item -LiteralPath (Join-Path $symDir 'Mapping.txt')
    $message = Get-Thrown { & $archive -SymbolsDirectory $symDir -ReleaseDirectory $relDir -ArchiveRoot $archiveRoot -Label 'b3' }
    Assert-True ($message -match 'No Obfuscar map') 'Symbols without a map: refused'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host "$($failures.Count) FAILED"; exit 1 }
Write-Host 'All obfuscation checks passed.'
exit 0
