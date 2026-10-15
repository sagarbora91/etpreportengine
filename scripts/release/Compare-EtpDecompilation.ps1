<#
.SYNOPSIS
Lane V1 (code-protection plan 5.4). Decompiles OUR OWN protected and unprotected assemblies with a pinned ilspycmd and writes a
markdown report of what a reader can still see: readable type and method names, the named report services, plain SQL strings, debug symbols.

.DESCRIPTION
Findings, not claims: the report states numbers and the honest limits. With -Strict the script also exits 1 when the protected build
fails the thresholds below; without it, exit 0 means "the report was written".

ilspycmd is NOT downloaded here. Install it once by hand, pinned (plan 4.3.4 pattern, like Inno Setup):
    dotnet tool install ilspycmd --version 11.1.0 --tool-path E:\Tools\ilspycmd
and the script refuses if E:\Tools\ilspycmd\ilspycmd.exe is missing or is not the pinned version.

Counted per assembly, before (unprotected) and after (protected):
  - types declared, split into public and non-public (KeepPublicApi keeps public names by design: only non-public ones are renamed)
  - readable non-public type names and readable non-public method names (a name is readable when it holds a run of 4+ letters with a vowel)
  - the four named report services (SalesReportingService, StockReconciliationService, InvoiceTenderReconciliationService,
    DailyReportingPackService): found by name, and with which visibility
  - string literals that look like SQL (SELECT..FROM, EXEC, INSERT INTO, UPDATE..SET, DELETE FROM, MERGE, CREATE VIEW/PROC/FUNCTION, DBCC)
  - debug symbols: *.pdb files shipped beside the assemblies (must be none), and whether the DLL still names a .pdb path

-Strict thresholds (defaults): protected readable non-public identifiers <= 10 % of unprotected, protected plain SQL strings <= 10 % of
unprotected, no .pdb file, for Etp.Reporting.Reporting and Etp.Reporting.Infrastructure.SqlServer (the two the plan names).

Exit codes: 0 report written (and thresholds met with -Strict); 1 -Strict threshold missed; 2 refused (tool missing, folders wrong); 3 incomplete.

.EXAMPLE
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\release\Compare-EtpDecompilation.ps1 -Protected <dir> -Unprotected <dir>-unprotected -OutputFile E:\ETP\Reference\Symbols\1.13.0-abc1234\decompilation.md
#>
[CmdletBinding()]
param(
    [string]$Protected,
    [string]$Unprotected,
    [string]$OutputFile,
    [string]$IlSpyCmd = 'E:\Tools\ilspycmd\ilspycmd.exe',
    [string]$PinnedVersion = '11.1.0',
    [string[]]$Assemblies = @('Etp.Reporting.Domain', 'Etp.Reporting.Application', 'Etp.Reporting.Import', 'Etp.Reporting.Infrastructure.SqlServer', 'Etp.Reporting.Infrastructure.Http', 'Etp.Reporting.Reporting', 'Etp.Reporting.Desktop'),
    [string[]]$StrictAssemblies = @('Etp.Reporting.Reporting', 'Etp.Reporting.Infrastructure.SqlServer'),
    [int]$MaxReadablePercent = 10,
    [int]$MaxSqlStringPercent = 10,
    [int]$TimeoutMinutesPerAssembly = 15,
    [string]$WorkFolder,
    [switch]$Strict,
    [switch]$LoadFunctionsOnly
)
$ErrorActionPreference = 'Stop'

$Script:KnownServices = @('SalesReportingService', 'StockReconciliationService', 'InvoiceTenderReconciliationService', 'DailyReportingPackService')

function Stop-EtpRefused([string]$Message) { throw (New-Object System.InvalidOperationException("ETP-REFUSED: $Message")) }

# A name is readable when it holds a run of four or more letters that contains a vowel ("Summarize", "SalesReportingService").
# Obfuscar-style names ("A", "a", "b_0", "a1") and compiler names ("<Run>d__3") are not.
function Test-EtpReadableName([string]$Name) {
    if ([string]::IsNullOrEmpty($Name)) { return $false }
    foreach ($m in [regex]::Matches($Name, '[A-Za-z]{4,}')) { if ($m.Value -match '[AEIOUaeiou]') { return $true } }
    return $false
}

# Metrics of one decompiled C# text. Pure function: no file access (self-tested on sample text).
function Get-EtpDecompilationMetrics([string]$Source) {
    $types = @()
    foreach ($m in [regex]::Matches($Source, '(?m)^[ \t]*(?:\[[^\]\r\n]*\][ \t]*)*(?<vis>public|internal|private|protected)?[ \t]*(?:(?:static|sealed|abstract|partial|readonly|unsafe|ref)[ \t]+)*(?<kind>class|struct|enum|interface|record)[ \t]+(?<name>[A-Za-z_]\w*)')) {
        $vis = if ($m.Groups['vis'].Success) { $m.Groups['vis'].Value } else { 'internal' }
        $types += [pscustomobject]@{ Name = $m.Groups['name'].Value; Visibility = $vis }
    }
    $methods = @()
    foreach ($m in [regex]::Matches($Source, '(?m)^[ \t]*(?<vis>public|internal|private|protected)[ \t]+(?:(?:static|virtual|override|async|sealed|abstract|readonly|extern|unsafe|new)[ \t]+)*[\w<>\[\],.?]+(?:[ \t]*<[^>\r\n]*>)?[ \t]+(?<name>[A-Za-z_]\w*)[ \t]*(?:<[^>\r\n]*>)?\(')) {
        $methods += [pscustomobject]@{ Name = $m.Groups['name'].Value; Visibility = $m.Groups['vis'].Value }
    }
    $literals = New-Object System.Collections.ArrayList
    foreach ($m in [regex]::Matches($Source, '(?s)"""(.*?)"""')) { [void]$literals.Add($m.Groups[1].Value) }
    $withoutRaw = [regex]::Replace($Source, '(?s)"""(.*?)"""', '')
    foreach ($m in [regex]::Matches($withoutRaw, '@"((?:[^"]|"")*)"')) { [void]$literals.Add($m.Groups[1].Value) }
    $withoutVerbatim = [regex]::Replace($withoutRaw, '@"((?:[^"]|"")*)"', '')
    foreach ($m in [regex]::Matches($withoutVerbatim, '"((?:[^"\\\r\n]|\\.)*)"')) { [void]$literals.Add($m.Groups[1].Value) }
    $sqlPattern = '(?is)\b(select\s.+?\sfrom\s|exec(?:ute)?\s+\w|insert\s+into\s|update\s+\S+\s+set\s|delete\s+from\s|merge\s+(?:into\s)?\S+|create\s+(?:or\s+alter\s+)?(?:view|procedure|proc|function)\s|dbcc\s+\w)'
    $sqlStrings = @($literals | Where-Object { $_ -match $sqlPattern })
    $nonPublicTypes = @($types | Where-Object { $_.Visibility -ne 'public' })
    $nonPublicMethods = @($methods | Where-Object { $_.Visibility -ne 'public' })
    return [pscustomobject]@{
        Types                  = $types.Count
        PublicTypes            = @($types | Where-Object { $_.Visibility -eq 'public' }).Count
        NonPublicTypes         = $nonPublicTypes.Count
        ReadableNonPublicTypes = @($nonPublicTypes | Where-Object { Test-EtpReadableName $_.Name }).Count
        NonPublicMethods       = $nonPublicMethods.Count
        ReadableNonPublicMethods = @($nonPublicMethods | Where-Object { Test-EtpReadableName $_.Name }).Count
        SqlStrings             = $sqlStrings.Count
        KnownServices          = @($Script:KnownServices | ForEach-Object {
                $service = $_
                $hit = $types | Where-Object { $_.Name -ceq $service } | Select-Object -First 1
                [pscustomobject]@{ Name = $service; Found = [bool]$hit; Visibility = $(if ($hit) { $hit.Visibility } else { '' }) }
            })
    }
}

# The ratio rule used by -Strict: protected must be at most MaxPercent of unprotected (0 unprotected = nothing to hide = pass).
function Test-EtpWithinPercent([double]$Protected, [double]$Unprotected, [int]$MaxPercent) {
    if ($Unprotected -le 0) { return $true }
    return ($Protected / $Unprotected * 100.0) -le $MaxPercent
}

function Get-EtpReadableIdentifierCount($Metrics) { return [int]($Metrics.ReadableNonPublicTypes + $Metrics.ReadableNonPublicMethods) }

function Test-EtpDllNamesPdb([string]$DllPath) {
    $bytes = [IO.File]::ReadAllBytes($DllPath)
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    $utf16 = [Text.Encoding]::Unicode.GetString($bytes)
    return ($ascii -match '(?i)\.pdb') -or ($utf16 -match '(?i)\.pdb')
}

function Assert-EtpIlSpy([string]$Path, [string]$Version) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Stop-EtpRefused "ilspycmd is not installed at $Path. It is never downloaded by this script. Install once, pinned: dotnet tool install ilspycmd --version $Version --tool-path $(Split-Path -Parent $Path)"
    }
    $store = Join-Path (Split-Path -Parent $Path) ('.store\ilspycmd\' + $Version)
    $pinned = Test-Path -LiteralPath $store -PathType Container
    if (-not $pinned) {
        $reported = ''
        try { $reported = (& $Path --version 2>&1 | Out-String) } catch { $reported = '' }
        $pinned = $reported -match [regex]::Escape($Version)
    }
    if (-not $pinned) { Stop-EtpRefused "ilspycmd at $Path is not the pinned version $Version (no .store\ilspycmd\$Version folder and --version does not say $Version). Reinstall it pinned." }
}

function Invoke-EtpDecompile([string]$IlSpy, [string]$Dll, [string]$OutFolder, [int]$TimeoutMinutes) {
    [void](New-Item -ItemType Directory -Path $OutFolder -Force)
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $IlSpy; $info.Arguments = '-o "{0}" "{1}"' -f $OutFolder, $Dll
    $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true; $info.CreateNoWindow = $true
    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutMinutes * 60000)) { try { $process.Kill() } catch { }; throw "ilspycmd timed out on $Dll" }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "ilspycmd exited $($process.ExitCode) on ${Dll}: $($stderr.Result)" }
    $files = @(Get-ChildItem -LiteralPath $OutFolder -Filter '*.cs' -File -Recurse)
    if ($files.Count -eq 0) { throw "ilspycmd produced no .cs output for $Dll" }
    return (($files | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`r`n")
}

function Get-EtpAssemblyReport([string]$IlSpy, [string]$Folder, [string]$Name, [string]$Work, [int]$TimeoutMinutes) {
    $dll = Join-Path $Folder ($Name + '.dll')
    if (-not (Test-Path -LiteralPath $dll)) { throw "$dll not found" }
    $text = Invoke-EtpDecompile $IlSpy $dll (Join-Path $Work ($Name)) $TimeoutMinutes
    $metrics = Get-EtpDecompilationMetrics $text
    $metrics | Add-Member -NotePropertyName Assembly -NotePropertyValue $Name
    $metrics | Add-Member -NotePropertyName NamesPdb -NotePropertyValue (Test-EtpDllNamesPdb $dll)
    return $metrics
}

function Format-EtpDecompilationReport($Before, $After, [string[]]$PdbFiles, $Failures, [string]$ToolVersion) {
    $l = New-Object System.Collections.ArrayList
    [void]$l.Add('# Decompilation comparison (own binaries only)')
    [void]$l.Add('')
    [void]$l.Add("Run $((Get-Date).ToString('yyyy-MM-dd HH:mm')) on $env:COMPUTERNAME with ilspycmd $ToolVersion. Before = unprotected publish, after = protected publish.")
    [void]$l.Add('')
    [void]$l.Add('| Assembly | Non-public types (readable before / after) | Non-public methods (readable before / after) | Plain SQL strings (before / after) | Public types kept |')
    [void]$l.Add('|---|---|---|---|---|')
    foreach ($b in $Before) {
        $a = $After | Where-Object { $_.Assembly -eq $b.Assembly } | Select-Object -First 1
        if (-not $a) { continue }
        [void]$l.Add(('| {0} | {1} / {2} of {3} | {4} / {5} of {6} | {7} / {8} | {9} / {10} |' -f $b.Assembly, $b.ReadableNonPublicTypes, $a.ReadableNonPublicTypes, $b.NonPublicTypes, $b.ReadableNonPublicMethods, $a.ReadableNonPublicMethods, $b.NonPublicMethods, $b.SqlStrings, $a.SqlStrings, $b.PublicTypes, $a.PublicTypes))
    }
    [void]$l.Add('')
    [void]$l.Add('## Named report services')
    [void]$l.Add('| Service | Unprotected | Protected |')
    [void]$l.Add('|---|---|---|')
    foreach ($service in $Script:KnownServices) {
        $cells = foreach ($set in $Before, $After) {
            $hit = $null
            foreach ($metric in $set) { $k = $metric.KnownServices | Where-Object { $_.Name -eq $service -and $_.Found } | Select-Object -First 1; if ($k) { $hit = $k; break } }
            if ($hit) { "findable by name ($($hit.Visibility))" } else { 'not findable by name' }
        }
        [void]$l.Add("| $service | $($cells[0]) | $($cells[1]) |")
    }
    [void]$l.Add('')
    [void]$l.Add('## Debug symbols')
    if ($PdbFiles.Count -eq 0) { [void]$l.Add('No .pdb file ships beside the protected assemblies, so a decompiler shows no source file names or line numbers.') }
    else { [void]$l.Add('**FAIL**: the protected folder ships ' + $PdbFiles.Count + ' .pdb file(s): ' + (($PdbFiles | Select-Object -First 5) -join ', ')) }
    $naming = @($After | Where-Object { $_.NamesPdb } | ForEach-Object { $_.Assembly })
    if ($naming.Count -gt 0) { [void]$l.Add('The DLLs still name a .pdb path in their debug directory (normal for a portable PDB; the file itself is not shipped): ' + ($naming -join ', ') + '.') }
    if ($Failures.Count -gt 0) { [void]$l.Add(''); [void]$l.Add('## Not decompiled'); foreach ($f in $Failures) { [void]$l.Add("- $f") } }
    [void]$l.Add('')
    [void]$l.Add('## What this does not show')
    [void]$l.Add('- Public types, public methods and public members keep their names by design (KeepPublicApi: XAML, tests and scripts bind to them). Only non-public names are renamed.')
    [void]$l.Add('- Method bodies, control flow and arithmetic are NOT obfuscated: the structure of the C# half stays readable.')
    [void]$l.Add('- The SQL half (database\objects views, functions and procedures, and database\migrations) ships as plain text by design and is untouched.')
    [void]$l.Add('- A string-hiding pass makes SQL text invisible in a listing, not unrecoverable at run time.')
    return ($l -join "`r`n")
}

if ($LoadFunctionsOnly) { return }

$exitCode = 2
try {
    if ([string]::IsNullOrWhiteSpace($Protected) -or [string]::IsNullOrWhiteSpace($Unprotected)) { Stop-EtpRefused 'Provide -Protected and -Unprotected.' }
    foreach ($pair in @(@('Protected', $Protected), @('Unprotected', $Unprotected))) {
        if (-not (Test-Path -LiteralPath $pair[1] -PathType Container)) { Stop-EtpRefused "$($pair[0]) folder does not exist: $($pair[1])" }
    }
    $Protected = (Resolve-Path -LiteralPath $Protected).Path; $Unprotected = (Resolve-Path -LiteralPath $Unprotected).Path
    if ($Protected -ieq $Unprotected) { Stop-EtpRefused '-Protected and -Unprotected are the same folder.' }
    Assert-EtpIlSpy $IlSpyCmd $PinnedVersion
    if (-not $OutputFile) { $OutputFile = Join-Path ([IO.Path]::GetTempPath()) ('EtpDecompilation-' + (Get-Date).ToString('yyyyMMdd-HHmmss') + '.md') }
    if (-not $WorkFolder) { $WorkFolder = Join-Path ([IO.Path]::GetTempPath()) ('EtpDecompile-' + [Guid]::NewGuid().ToString('N')) }

    $exitCode = 3
    $before = @(); $after = @(); $failures = @()
    foreach ($name in $Assemblies) {
        foreach ($side in @(@('before', $Unprotected), @('after', $Protected))) {
            try {
                $report = Get-EtpAssemblyReport $IlSpyCmd $side[1] $name (Join-Path $WorkFolder $side[0]) $TimeoutMinutesPerAssembly
                if ($side[0] -eq 'before') { $before += $report } else { $after += $report }
            } catch { $failures += "$name ($($side[0])): $($_.Exception.Message)" }
        }
    }
    $pdbFiles = @(Get-ChildItem -LiteralPath $Protected -Recurse -Filter '*.pdb' -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    $markdown = Format-EtpDecompilationReport $before $after $pdbFiles $failures $PinnedVersion
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent ([IO.Path]::GetFullPath($OutputFile))) -Force)
    [IO.File]::WriteAllText($OutputFile, $markdown, (New-Object Text.UTF8Encoding($false)))
    Write-Host "Report written: $OutputFile"
    if ($failures.Count -gt 0) { Write-Host "INCOMPLETE: $($failures.Count) assembly decompilations failed (listed in the report)."; $exitCode = 3 }
    else { $exitCode = 0 }

    if ($Strict -and $exitCode -eq 0) {
        $problems = @()
        if ($pdbFiles.Count -gt 0) { $problems += "$($pdbFiles.Count) .pdb files ship in the protected folder" }
        foreach ($name in $StrictAssemblies) {
            $b = $before | Where-Object { $_.Assembly -eq $name } | Select-Object -First 1
            $a = $after | Where-Object { $_.Assembly -eq $name } | Select-Object -First 1
            if (-not $b -or -not $a) { $problems += "$name was not analysed"; continue }
            if (-not (Test-EtpWithinPercent (Get-EtpReadableIdentifierCount $a) (Get-EtpReadableIdentifierCount $b) $MaxReadablePercent)) { $problems += "$name still has $(Get-EtpReadableIdentifierCount $a) of $(Get-EtpReadableIdentifierCount $b) readable non-public identifiers (limit $MaxReadablePercent %)" }
            if (-not (Test-EtpWithinPercent $a.SqlStrings $b.SqlStrings $MaxSqlStringPercent)) { $problems += "$name still shows $($a.SqlStrings) of $($b.SqlStrings) plain SQL strings (limit $MaxSqlStringPercent %)" }
        }
        foreach ($p in $problems) { Write-Host "STRICT  $p" }
        if ($problems.Count -gt 0) { $exitCode = 1 }
    }
}
catch {
    $message = $_.Exception.Message
    if ($message -like 'ETP-REFUSED:*') { Write-Host ('REFUSED: ' + $message.Substring('ETP-REFUSED:'.Length).Trim()); $exitCode = 2 }
    else { Write-Host "INCOMPLETE: $message"; $exitCode = 3 }
}
exit $exitCode
