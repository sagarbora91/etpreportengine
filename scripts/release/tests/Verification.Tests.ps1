<#
Self-test for the lane V1 verification scripts (Test-EtpProtectedBuild, Measure-EtpPerformance, Compare-EtpDecompilation, Resolve-EtpStackTrace).
Plain Windows PowerShell 5.1, no Pester. Works only in a fresh temp folder: no SQL Server, no ilspycmd, no ETP exe, no database, no network.
The real runs (scratch databases, the probe host, ilspycmd) happen only when Sagar runs the scripts elevated at integration.
Run:  powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\tests\Verification.Tests.ps1      Exit code 0 = all passed.
#>
$ErrorActionPreference = 'Stop'
$releaseFolder = Split-Path -Parent $PSScriptRoot
$root = Join-Path ([IO.Path]::GetTempPath()) ('etp-verification-test-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $root)
$failures = New-Object System.Collections.ArrayList
function Assert-True($condition, [string]$message) { if ($condition) { Write-Host "PASS  $message" } else { Write-Host "FAIL  $message"; [void]$failures.Add($message) } }
function Assert-Throws([scriptblock]$block, [string]$pattern, [string]$message) {
    $text = ''
    try { & $block } catch { $text = $_.Exception.Message }
    Assert-True ($text -match $pattern) ($message + $(if ($text -notmatch $pattern) { " (got: '$text')" } else { '' }))
}
function Invoke-Child([string]$file, [string[]]$arguments) {
    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $releaseFolder $file)) + $arguments
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $script:LastOutput = (& powershell.exe @all 2>&1 | Out-String)
    $ErrorActionPreference = $previous
    return $LASTEXITCODE
}

try {
    # The scripts under test are dot-sourced for their functions (-LoadFunctionsOnly: nothing else runs).
    . (Join-Path $releaseFolder 'Test-EtpProtectedBuild.ps1') -LoadFunctionsOnly
    . (Join-Path $releaseFolder 'Measure-EtpPerformance.ps1') -LoadFunctionsOnly
    . (Join-Path $releaseFolder 'Compare-EtpDecompilation.ps1') -LoadFunctionsOnly
    . (Join-Path $releaseFolder 'Resolve-EtpStackTrace.ps1') -LoadFunctionsOnly

    # ---------------------------------------------------------------- Test-EtpProtectedBuild: guards
    Assert-True ($null -eq (Assert-EtpScratchDatabase 'EtpAccept_V1')) 'EtpAccept_V1 is an accepted scratch name'
    foreach ($bad in 'EtpReporting', 'EtpReportingHelios', 'EtpAccept_', 'etpaccept_v1', 'EtpAccept_v1;DROP DATABASE x', 'EtpAccept_a b', 'master', '', 'EtpPhase1Test_x') {
        Assert-Throws { Assert-EtpScratchDatabase $bad } 'ETP-REFUSED' "scratch name '$bad' is refused"
    }
    Assert-Throws { Assert-EtpLocalServer 'remote-sql\PROD' } 'not a SQL Server instance on this computer' 'a remote SQL Server is refused'
    Assert-True ($null -eq (Assert-EtpLocalServer '.\SQLEXPRESS')) '.\SQLEXPRESS is accepted'
    $names = Get-EtpScratchNames 'EtpAccept_V1'
    Assert-True ($names.Unprotected -ceq 'EtpAccept_V1_U' -and $names.Protected -ceq 'EtpAccept_V1_P') 'scratch names are <base>_U and <base>_P'
    Assert-True (($names.Unprotected -cmatch '^EtpAccept_') -and ($names.Protected -cmatch '^EtpAccept_')) 'derived names still match EtpAccept_*'

    function New-FakeBuild([string]$folder, [string]$content, [bool]$withPdb) {
        [void](New-Item -ItemType Directory -Path $folder -Force)
        [IO.File]::WriteAllText((Join-Path $folder 'Etp.Reporting.Desktop.exe'), 'exe')
        foreach ($name in $Script:EtpAssemblies) { [IO.File]::WriteAllText((Join-Path $folder ($name + '.dll')), "$content $name") }
        foreach ($sub in 'database\migrations', 'database\objects') { [void](New-Item -ItemType Directory -Path (Join-Path $folder $sub) -Force) }
        if ($withPdb) { [IO.File]::WriteAllText((Join-Path $folder 'Etp.Reporting.Reporting.pdb'), 'pdb') }
    }
    New-FakeBuild (Join-Path $root 'u') 'plain' $false
    New-FakeBuild (Join-Path $root 'p') 'obfuscated' $false
    New-FakeBuild (Join-Path $root 'same') 'plain' $false
    New-FakeBuild (Join-Path $root 'withpdb') 'obfuscated' $true
    Assert-True ($null -eq (Assert-EtpBuildFolder (Join-Path $root 'u') 'Unprotected')) 'a complete folder publish is accepted'
    Remove-Item -LiteralPath (Join-Path $root 'u\Etp.Reporting.Reporting.dll')
    Assert-Throws { Assert-EtpBuildFolder (Join-Path $root 'u') 'Unprotected' } 'single-file bundle cannot be probed' 'a folder without Etp.Reporting.Reporting.dll (single-file bundle) is refused'
    [IO.File]::WriteAllText((Join-Path $root 'u\Etp.Reporting.Reporting.dll'), 'plain Etp.Reporting.Reporting')
    Assert-True (@(Assert-EtpIsReallyProtected (Join-Path $root 'p') (Join-Path $root 'u')).Count -ge 0) 'differing assemblies count as protected'
    Assert-Throws { Assert-EtpIsReallyProtected (Join-Path $root 'same') (Join-Path $root 'u') } 'not protected' 'a "protected" folder identical to the unprotected one is refused (no mislabelled comparison)'
    Assert-Throws { Assert-EtpIsReallyProtected (Join-Path $root 'withpdb') (Join-Path $root 'u') } 'debug symbols' 'a protected folder that contains a .pdb is refused'

    # ---------------------------------------------------------------- the comparer must FAIL when one thing differs
    $a = [ordered]@{ 'dbo.sales' = 'rows=10;chk=1;sum=2;cols=5/6'; 'dbo.stock' = 'rows=3;chk=7;sum=9;cols=4/4' }
    $b = [ordered]@{ 'dbo.sales' = 'rows=10;chk=1;sum=2;cols=5/6'; 'dbo.stock' = 'rows=3;chk=7;sum=9;cols=4/4' }
    Assert-True (@(Compare-EtpMaps $a $b 'fact-table checksum').Count -eq 0) 'identical maps compare as identical'
    $b['dbo.stock'] = 'rows=3;chk=7;sum=10;cols=4/4'
    $diff = @(Compare-EtpMaps $a $b 'fact-table checksum')
    Assert-True ($diff.Count -eq 1 -and $diff[0] -match 'dbo\.stock') 'a single changed checksum is reported'
    $b.Remove('dbo.stock'); $b['dbo.extra'] = 'rows=0'
    Assert-True (@(Compare-EtpMaps $a $b 'x').Count -eq 2) 'a table present on one side only is reported on both counts'

    # xlsx: build two tiny workbooks that differ in ONE cell, plus different docProps (which must be ignored)
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    function New-TinyXlsx([string]$path, [string]$cellB2, [string]$created) {
        $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
        try {
            function Add-Part($z, $name, $text) { $e = $z.CreateEntry($name); $w = New-Object IO.StreamWriter($e.Open(), (New-Object Text.UTF8Encoding($false))); $w.Write($text); $w.Dispose() }
            Add-Part $zip 'xl/workbook.xml' '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="DSR" sheetId="1" r:id="rId1"/></sheets></workbook>'
            Add-Part $zip 'xl/_rels/workbook.xml.rels' '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="x" Target="worksheets/sheet1.xml"/></Relationships>'
            Add-Part $zip 'xl/sharedStrings.xml' ('<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Net sales</t></si><si><t>' + $cellB2 + '</t></si></sst>')
            Add-Part $zip 'xl/worksheets/sheet1.xml' '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1"><v>1234.5</v></c></row><row r="2"><c r="B2" t="s"><v>1</v></c></row></sheetData></worksheet>'
            Add-Part $zip 'docProps/core.xml' ('<cp:coreProperties xmlns:cp="x"><created>' + $created + '</created></cp:coreProperties>')
        } finally { $zip.Dispose() }
    }
    New-TinyXlsx (Join-Path $root 'one.xlsx') 'Total' '2026-10-15T10:00:00Z'
    New-TinyXlsx (Join-Path $root 'two.xlsx') 'Total' '2027-01-01T00:00:00Z'
    New-TinyXlsx (Join-Path $root 'three.xlsx') 'Totaa' '2026-10-15T10:00:00Z'
    $c1 = Get-EtpXlsxCells (Join-Path $root 'one.xlsx')
    Assert-True ($c1['DSR!A1'] -ceq 'Net sales' -and $c1['DSR!B1'] -ceq '1234.5' -and $c1['DSR!B2'] -ceq 'Total') 'xlsx cells are read with shared strings resolved'
    Assert-True (@(Compare-EtpMaps $c1 (Get-EtpXlsxCells (Join-Path $root 'two.xlsx')) 'DSR Excel cell').Count -eq 0) 'docProps (created time) differences are ignored'
    $cellDiff = @(Compare-EtpMaps $c1 (Get-EtpXlsxCells (Join-Path $root 'three.xlsx')) 'DSR Excel cell')
    Assert-True ($cellDiff.Count -eq 1 -and $cellDiff[0] -match 'DSR!B2') 'ONE differing Excel cell makes the comparison fail'

    # pdf: Flate stream with a zlib header, metadata that differs, content that differs
    function New-TinyPdf([string]$path, [string]$content, [string]$date) {
        $latin1 = [Text.Encoding]::GetEncoding(28591)
        $raw = $latin1.GetBytes($content)
        $ms = New-Object IO.MemoryStream
        $ms.WriteByte(0x78); $ms.WriteByte(0x9C)
        $deflate = New-Object IO.Compression.DeflateStream($ms, [IO.Compression.CompressionMode]::Compress, $true)
        $deflate.Write($raw, 0, $raw.Length); $deflate.Dispose()
        $body = $latin1.GetString($ms.ToArray())
        $text = "%PDF-1.4`n1 0 obj<</Type/Page/Parent 2 0 R>>endobj`n2 0 obj<</Length $($body.Length)/Filter/FlateDecode>>`nstream`n$body`nendstream`nendobj`n3 0 obj<</Producer(PDFsharp)/CreationDate(D:$date)>>endobj`nxref`n0 4`ntrailer<</ID[<AA><BB>]>>`nstartxref`n99`n%%EOF"
        [IO.File]::WriteAllBytes($path, $latin1.GetBytes($text))
    }
    New-TinyPdf (Join-Path $root 'a.pdf') 'BT (Net sales 1234.50) Tj ET' '20261015100000'
    New-TinyPdf (Join-Path $root 'b.pdf') 'BT (Net sales 1234.50) Tj ET' '20270101000000'
    New-TinyPdf (Join-Path $root 'c.pdf') 'BT (Net sales 1234.51) Tj ET' '20261015100000'
    $pa = Get-EtpPdfFingerprint (Join-Path $root 'a.pdf'); $pb = Get-EtpPdfFingerprint (Join-Path $root 'b.pdf'); $pc = Get-EtpPdfFingerprint (Join-Path $root 'c.pdf')
    Assert-True ($pa.Pages -eq 1 -and $pa.Streams -eq 1) 'pdf: one page and one stream are counted'
    Assert-True ($pa.Hash -ceq $pb.Hash) 'pdf: creation date and offsets are excluded from the comparison'
    Assert-True ($pa.Hash -cne $pc.Hash) 'pdf: a changed digit inside a compressed content stream is detected'

    # ---------------------------------------------------------------- R1-06: identical FAILURES must not pass; R1-02: reflection probes compared
    function New-FakeProbeResult([string]$label, [int]$failed, [int]$cellCount, [int]$pages, [int]$tables, [bool]$withReflection) {
        $sums = [ordered]@{}; for ($i = 1; $i -le $tables; $i++) { $sums["dbo.t$i"] = "rows=$i;chk=$i;sum=$i;cols=2/3" }
        $sums['dbo.import_log'] = 'rows=4;listed-only'; $sums['dbo.empty'] = 'rows=0;chk=;sum=;cols=1/1'
        $cellMap = [ordered]@{}; for ($i = 1; $i -le $cellCount; $i++) { $cellMap["DSR!A$i"] = "$i" }
        $refl = [ordered]@{}
        if ($withReflection) { $refl['importHistory.issues'] = 'REQUIRED_COLUMN_MISSING|Blocker|row=4'; $refl['tally.costCentres'] = 'HEMW=Smoke Centre'; $refl['sec01.matrix'] = 'entries=10'; $refl['families'] = '31:...' }
        return [pscustomobject]@{ Label = $label; Checksums = $sums; Import = [ordered]@{ '001' = 'R025.xlsx|R025'; 'failed-files' = [string]$failed }; Cells = $cellMap; Pdf = [pscustomobject]@{ Pages = $pages; Streams = 1; Hash = 'H' }; Reflection = $refl; Health = [ordered]@{}; PackJsonHash = 'J' }
    }
    $good = New-FakeProbeResult 'unprotected' 0 60 2 6 $true
    Assert-True (@(Test-EtpProbeCompleteness $good).Count -eq 0) 'a complete probe result has no completeness problem'
    Assert-True (@(@(Test-EtpProbeCompleteness (New-FakeProbeResult 'p' 2 60 2 6 $true)) -match 'failed to import').Count -eq 1) 'failed fixture files make the run INCOMPLETE even when both builds fail alike'
    Assert-True (@(@(Test-EtpProbeCompleteness (New-FakeProbeResult 'p' 0 0 2 6 $true)) -match 'Excel cells').Count -eq 1) 'an empty Excel cell map is INCOMPLETE (empty equals empty is not a pass)'
    Assert-True (@(@(Test-EtpProbeCompleteness (New-FakeProbeResult 'p' 0 60 0 6 $true)) -match 'no pages').Count -eq 1) 'a PDF without pages is INCOMPLETE'
    Assert-True (@(@(Test-EtpProbeCompleteness (New-FakeProbeResult 'p' 0 60 2 2 $true)) -match 'checksummed tables').Count -eq 1) 'too few checksummed tables with rows (listed-only and empty ones do not count) is INCOMPLETE'
    Assert-True (@(@(Test-EtpProbeCompleteness (New-FakeProbeResult 'p' 0 60 2 6 $false)) -match 'reflection probe').Count -eq 4) 'a missing reflection probe result is INCOMPLETE'
    $other = New-FakeProbeResult 'protected' 0 60 2 6 $true
    Assert-True (@(Compare-EtpProbeResults $good $other).Count -eq 0) 'two complete, equal probe results compare as identical'
    $other.Reflection['tally.costCentres'] = 'HEMW='
    Assert-True (@(@(Compare-EtpProbeResults $good $other) -match 'reflection probe \[tally\.costCentres\]').Count -eq 1) 'a differing reflection probe result is a difference'
    $probeSource = $Script:ProbeProgram
    foreach ($needle in 'SqlServerImportHistoryQuery(cs).LoadAsync', 'SqlServerTallyProfileService(cs)', 'ExpectedRolePermissionMatrix.LoadEmbedded()', 'EtpReportFamilyRegistry.Families', 'reflection.json') {
        Assert-True ($probeSource.Contains($needle)) "the probe host runs the reflection path: $needle"
    }

    # ---------------------------------------------------------------- Test-EtpProtectedBuild as a program: refusals exit 2 and touch nothing
    Assert-True ((Invoke-Child 'Test-EtpProtectedBuild.ps1' @()) -eq 2 -and $script:LastOutput -match 'REFUSED') 'no arguments: refused, exit 2'
    Assert-True ((Invoke-Child 'Test-EtpProtectedBuild.ps1' @('-Protected', (Join-Path $root 'p'), '-Unprotected', (Join-Path $root 'u'), '-FixtureFolder', $root, '-Database', 'EtpReporting')) -eq 2 -and $script:LastOutput -match 'EtpAccept_') 'EtpReporting as the database: refused before anything else, exit 2'
    Assert-True ((Invoke-Child 'Test-EtpProtectedBuild.ps1' @('-Protected', (Join-Path $root 'p'), '-Unprotected', (Join-Path $root 'u'), '-FixtureFolder', $root, '-Database', 'EtpAccept_X', '-Server', 'prod-sql\LIVE')) -eq 2 -and $script:LastOutput -match 'this computer') 'a remote server: refused, exit 2'

    # ---------------------------------------------------------------- Measure-EtpPerformance
    Assert-Throws { Assert-EtpRunCount 2 } 'At least 3 runs' 'fewer than 3 runs are refused'
    Assert-True ($null -eq (Assert-EtpRunCount 3)) 'three runs are accepted'
    Assert-True ((Get-EtpMedian @(5, 1, 3)) -eq 3 -and (Get-EtpMedian @(4, 1, 3, 2)) -eq 2.5) 'median of odd and even sample counts'
    Assert-True ((Get-EtpPerformanceVerdict 1000 1040 50) -eq 'OK') 'within 5 % is OK'
    Assert-True ((Get-EtpPerformanceVerdict 1000 1070 50) -eq 'DROP-STRING-HIDING') '7 % slower: drop string hiding'
    Assert-True ((Get-EtpPerformanceVerdict 1000 1120 50) -eq 'WITHHOLD-OBFUSCATION') '12 % slower: withhold obfuscation'
    Assert-True ((Get-EtpPerformanceVerdict 100 140 50) -eq 'OK') 'a 40 ms difference is below the noise floor'
    Assert-True ((Get-EtpPerformanceVerdict 1000 900 50) -eq 'OK') 'faster is OK'
    Assert-True ((Invoke-Child 'Measure-EtpPerformance.ps1' @('-Protected', (Join-Path $root 'p'), '-Unprotected', (Join-Path $root 'u'), '-FixtureFolder', $root, '-Database', 'EtpAccept_X', '-Runs', '2')) -eq 2 -and $script:LastOutput -match 'At least 3 runs') 'the program refuses -Runs 2 with exit 2'
    $samples = [pscustomobject]@{
        Unprotected = @(1..3 | ForEach-Object { [pscustomobject]@{ coldStartMs = 1000; importMs = 2000; reportMs = 500; exportMs = 800 } })
        Protected   = @(1..3 | ForEach-Object { [pscustomobject]@{ coldStartMs = 1030; importMs = 2300; reportMs = 505; exportMs = 800 } })
    }
    $measures = @(New-EtpPerformanceResult $samples 50)
    Assert-True (($measures | Where-Object { $_.measure -eq 'importMs' }).verdict -eq 'WITHHOLD-OBFUSCATION' -and ($measures | Where-Object { $_.measure -eq 'coldStartMs' }).verdict -eq 'OK') 'the result table applies the rule per measure'

    # ---------------------------------------------------------------- Compare-EtpDecompilation
    $plain = @'
namespace Etp.Reporting.Reporting
{
    public sealed class SalesReportingService
    {
        public string Summarize(int lines) { return "x"; }
        private string BuildQuery() { return "SELECT sku, qty FROM dbo.sales_lines WHERE business_date = @d"; }
        internal static string Describe() { return @"EXEC dbo.usp_close_day @date"; }
    }
    internal sealed class InvoiceTenderReconciliationService
    {
        private void Reconcile() { var s = """
            UPDATE dbo.invoices SET status = 'X'
            """; }
    }
}
'@
    $hidden = @'
namespace A
{
    public sealed class SalesReportingService
    {
        public string Summarize(int lines) { return a(1); }
        private string a() { return b.c(2); }
        internal static string b() { return d.e(3); }
    }
    internal sealed class A { private void a() { } }
}
'@
    $m1 = Get-EtpDecompilationMetrics $plain
    $m2 = Get-EtpDecompilationMetrics $hidden
    Assert-True ($m1.Types -eq 2 -and $m1.PublicTypes -eq 1 -and $m1.NonPublicTypes -eq 1) 'metrics count public and non-public types'
    Assert-True ($m1.ReadableNonPublicTypes -eq 1 -and $m2.ReadableNonPublicTypes -eq 0) 'readable non-public types drop from 1 to 0 when renamed'
    Assert-True ($m1.ReadableNonPublicMethods -ge 2 -and $m2.ReadableNonPublicMethods -eq 0) 'readable non-public methods drop to 0 when renamed'
    Assert-True ($m1.SqlStrings -eq 3 -and $m2.SqlStrings -eq 0) 'plain, verbatim and raw SQL string literals are counted (3 before, 0 after)'
    Assert-True ((@($m2.KnownServices | Where-Object { $_.Name -eq 'SalesReportingService' -and $_.Found -and $_.Visibility -eq 'public' })).Count -eq 1) 'a public service stays findable and is reported as public'
    Assert-True ((Test-EtpReadableName 'SalesReportingService') -and -not (Test-EtpReadableName 'a') -and -not (Test-EtpReadableName 'A_0') -and -not (Test-EtpReadableName 'b1')) 'readable-name heuristic'
    Assert-True ((Test-EtpWithinPercent 5 100 10) -and -not (Test-EtpWithinPercent 20 100 10) -and (Test-EtpWithinPercent 0 0 10)) 'the 10 percent rule'
    Assert-True ((Invoke-Child 'Compare-EtpDecompilation.ps1' @('-Protected', (Join-Path $root 'p'), '-Unprotected', (Join-Path $root 'u'), '-IlSpyCmd', (Join-Path $root 'missing\ilspycmd.exe'))) -eq 2 -and $script:LastOutput -match 'never downloaded') 'a missing ilspycmd is refused and never downloaded'
    $fakeTool = Join-Path $root 'tools\ilspycmd.exe'
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $fakeTool) -Force); [IO.File]::WriteAllText($fakeTool, 'not a tool')
    Assert-True ((Invoke-Child 'Compare-EtpDecompilation.ps1' @('-Protected', (Join-Path $root 'p'), '-Unprotected', (Join-Path $root 'u'), '-IlSpyCmd', $fakeTool)) -eq 2 -and $script:LastOutput -match 'not the pinned version') 'an ilspycmd that is not the pinned version is refused'
    # The installed tool (read-only pin check; never installs anything).
    $installedIlSpy = if ($env:ETP_ILSPYCMD) { $env:ETP_ILSPYCMD } else { 'E:\Tools\ilspycmd\ilspycmd.exe' }
    if (Test-Path -LiteralPath $installedIlSpy -PathType Leaf) {
        $pinnedIlSpy = '11.1.0.9782'
        $ilspyError = $null
        try { Assert-EtpIlSpy $installedIlSpy $pinnedIlSpy } catch { $ilspyError = $_.Exception.Message }
        Assert-True ($null -eq $ilspyError) "installed ilspycmd at $installedIlSpy is the pinned $pinnedIlSpy $(if ($ilspyError) { "($ilspyError)" })"
        Assert-True ((Get-Content -Raw -LiteralPath (Join-Path $releaseFolder 'Compare-EtpDecompilation.ps1')).Contains("'$pinnedIlSpy'")) 'Compare-EtpDecompilation.ps1 pins the same ilspycmd version'
    }
    else { Write-Host "SKIP  installed-tool check: ilspycmd is not installed at $installedIlSpy (install it pinned, see docs\security\VERIFICATION.md)" }

    # ---------------------------------------------------------------- Resolve-EtpStackTrace
    $mapping = @'
Renamed Types:

[Etp.Reporting.Reporting]Etp.Reporting.Reporting.StockReconciliationService -> [Etp.Reporting.Reporting]Etp.Reporting.Reporting.A
{
	Renamed Methods:
	Etp.Reporting.Reporting.StockReconciliationService::Reconcile(System.DateTime, System.String) -> a
	Etp.Reporting.Reporting.StockReconciliationService::Reconcile(System.DateTime) -> b
	Etp.Reporting.Reporting.StockReconciliationService::LoadOpening() -> c
}

[Etp.Reporting.Reporting]Etp.Reporting.Reporting.Helpers/Inner -> [Etp.Reporting.Reporting]Etp.Reporting.Reporting.B/a

Skipped Types:
Etp.Reporting.Reporting.PublicThing -> Etp.Reporting.Reporting.PublicThing
'@
    $mapFile = Join-Path $root 'Mapping.txt'
    [IO.File]::WriteAllText($mapFile, $mapping)
    $map = ConvertFrom-EtpObfuscarMapping ([IO.File]::ReadAllLines($mapFile))
    Assert-True ($map.Types['Etp.Reporting.Reporting.A'] -ceq 'Etp.Reporting.Reporting.StockReconciliationService') 'mapping: a renamed type is read'
    Assert-True ($map.Types['Etp.Reporting.Reporting.B+a'] -ceq 'Etp.Reporting.Reporting.Helpers+Inner') 'mapping: nested types use + like stack traces'
    Assert-True (-not $map.Types.ContainsKey('Etp.Reporting.Reporting.PublicThing')) 'mapping: skipped (unrenamed) sections are ignored'

    # The shape real Obfuscar 2.2.50 writes (captured from a probe assembly at integration, 15 Oct 2026): members inline in
    # the type block, [assembly] prefixes, Name[arity], renamed namespaces, 'skipped:' lines without an arrow.
    $realShape = @'
Renamed Types:

[Etp.Probe]Etp.Probe.Helper -> [Etp.Probe]A.A
{
	[Etp.Probe]Etp.Probe.Helper::Describe[1]( [System.Runtime]System.Int32 ) -> A
}

[Etp.Probe]Etp.Probe.Outer/Inner -> [Etp.Probe]A.a/A
{
	[Etp.Probe]Etp.Probe.Outer/Inner::Twice[1]( [System.Runtime]System.Int32 ) -> A
	[Etp.Probe]Etp.Probe.Outer/Inner::ToString[0]( ) skipped:  external base class or interface
}

[Etp.Probe]Etp.Probe.Outer -> [Etp.Probe]A.a
{
}

Skipped Types:

[Etp.Probe]Etp.Probe.PublicService skipped:  KeepPublicApi option in configuration
{
	[Etp.Probe]Etp.Probe.PublicService::Run[1]( [System.Runtime]System.Int32 ) skipped:  KeepPublicApi option in configuration
}
'@
    $real = ConvertFrom-EtpObfuscarMapping ($realShape -split "\r?\n")
    Assert-True ($real.Types['A.A'] -ceq 'Etp.Probe.Helper' -and $real.Types['A.a+A'] -ceq 'Etp.Probe.Outer+Inner' -and $real.Types['A.a'] -ceq 'Etp.Probe.Outer') 'real map: renamed types (namespace renamed too, nested with +, A and a distinct)'
    Assert-True ($real.Types.Count -eq 3) 'real map: member lines are not read as types'
    Assert-True ((Resolve-EtpMethodName $real 'Etp.Probe.Helper' 'A' 1) -ceq 'Describe' -and (Resolve-EtpMethodName $real 'Etp.Probe.Outer+Inner' 'A' 1) -ceq 'Twice') 'real map: inline members resolve, [arity] stripped'
    $realTrace = Resolve-EtpTraceText $real "   at A.a+A.A(Int32 n)`r`n   at A.A.A(Int32 n)"
    Assert-True ($realTrace.Text -match 'Etp\.Probe\.Outer\+Inner\.Twice' -and $realTrace.Text -match 'Etp\.Probe\.Helper\.Describe') 'real map: a protected-build trace is translated back'
    $trace = @'
System.InvalidOperationException: boom
   at Etp.Reporting.Reporting.A.a(DateTime d, String s) in x
   at Etp.Reporting.Reporting.A.b(DateTime d)
   at Etp.Reporting.Reporting.A.c()
   at Etp.Reporting.Reporting.A.zz()
   at System.Threading.Tasks.Task.Run()
'@
    $resolved = Resolve-EtpTraceText $map $trace
    Assert-True ($resolved.Frames -eq 5 -and $resolved.Resolved -eq 3) 'trace: 3 of 5 frames resolved (one unknown method, one framework frame)'
    Assert-True ($resolved.Text -match 'StockReconciliationService\.Reconcile\(DateTime d, String s\)' -and $resolved.Text -match 'StockReconciliationService\.LoadOpening\(\)') 'trace: type and method names are restored'
    Assert-True ($resolved.Text -match 'StockReconciliationService\.Reconcile\(DateTime d\)' -and $resolved.Text -match 'obfuscated: Etp\.Reporting\.Reporting\.A\.b') 'trace: overloads are told apart by parameter count and the obfuscated name is kept'
    Assert-True ($resolved.Text -match 'System\.Threading\.Tasks\.Task\.Run') 'trace: framework frames pass through unchanged'
    $traceFile = Join-Path $root 'trace.txt'; [IO.File]::WriteAllText($traceFile, $trace)
    Assert-True ((Invoke-Child 'Resolve-EtpStackTrace.ps1' @('-MappingFile', $mapFile, '-TraceFile', $traceFile, '-OutputFile', (Join-Path $root 'resolved.txt'))) -eq 0 -and (Get-Content -LiteralPath (Join-Path $root 'resolved.txt') -Raw) -match 'LoadOpening') 'the program writes the resolved trace and exits 0'
    Assert-True ((Invoke-Child 'Resolve-EtpStackTrace.ps1' @('-TraceFile', $traceFile)) -eq 2 -and $script:LastOutput -match 'MappingFile') 'no map: refused, exit 2'
    $unknown = Join-Path $root 'unknown.txt'; [IO.File]::WriteAllText($unknown, "   at Some.Other.Type.m()`n")
    Assert-True ((Invoke-Child 'Resolve-EtpStackTrace.ps1' @('-MappingFile', $mapFile, '-TraceFile', $unknown)) -eq 1) 'a trace whose frames the map cannot resolve exits 1'
}
finally {
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host ("{0} FAILED" -f $failures.Count); exit 1 }
Write-Host 'ALL PASSED'
exit 0
