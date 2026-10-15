<#
.SYNOPSIS
Lane V1 (code-protection plan 4.2). Turns a stack trace or diagnostics-log excerpt from a PROTECTED ETP build back into the original type
and method names, using the Obfuscar Mapping.txt archived with that release.

.DESCRIPTION
Protected builds write renamed names into the diagnostics log and the error register (DesktopDiagnostics records DeclaringType.FullName.Method).
The release archives `Mapping.txt` beside its symbols: `E:\ETP\Reference\Symbols\<version>-<sha7>\`. This script reads it and rewrites the frames.

    Resolve-EtpStackTrace.ps1 -SymbolsFolder E:\ETP\Reference\Symbols\1.13.0-abc1234 -TraceFile crash.txt
    Get-Content crash.txt | Resolve-EtpStackTrace.ps1 -MappingFile D:\Mapping.txt -OutputFile resolved.txt

What it resolves: lines of the form `   at New.Type.method(Arg.Type a)` and, anywhere in the text, any renamed type full name that contains a
dot. Overloads: when several original methods share the renamed name on one type, all candidates are shown, narrowed by parameter count
when the map carries signatures. Public API names were never renamed and pass through unchanged. Names the map does not know are left as
they are and counted.

The Obfuscar text-mapping format was read from its documented shape, not from a real file (lane O1 produces the first one): the parser
is deliberately tolerant (a section heading per member kind, `old -> new` lines, optional [assembly] prefixes and `Type::member(sig)`), and
Verification.Tests.ps1 pins the shapes it accepts. If a real Mapping.txt resolves nothing, adjust ConvertFrom-EtpObfuscarMapping.

Exit codes: 0 done (summary printed to stderr); 1 the trace had frames but none could be resolved; 2 refused (no map, bad paths).
#>
[CmdletBinding()]
param(
    [string]$MappingFile,
    [string]$SymbolsFolder,
    [string]$TraceFile,
    [string]$TraceText,
    [string]$OutputFile,
    [Parameter(ValueFromPipeline = $true)][string[]]$InputObject,
    [switch]$LoadFunctionsOnly
)
begin {
    $ErrorActionPreference = 'Stop'
    $pipelineLines = New-Object System.Collections.ArrayList

    function Stop-EtpRefused([string]$Message) { throw (New-Object System.InvalidOperationException("ETP-REFUSED: $Message")) }

    function ConvertTo-EtpTypeKey([string]$Name) {
        # Mapping uses Outer/Inner, stack traces use Outer+Inner; also drop an [assembly] prefix and generic arity noise.
        $name = $Name.Trim()
        $name = [regex]::Replace($name, '^\[[^\]]*\]', '')
        return $name.Replace('/', '+')
    }

    # Parses Obfuscar's text mapping into: Types (new full name -> old full name), and Methods ("oldType|newName" -> list of @{Name;Parameters}).
    function ConvertFrom-EtpObfuscarMapping([string[]]$Lines) {
        # Ordinal: obfuscated names differ only by case (A and a are different types).
        $types = New-Object System.Collections.Hashtable([StringComparer]::Ordinal)
        $methods = New-Object System.Collections.Hashtable([StringComparer]::Ordinal)
        $section = ''
        $sectionOutsideBlock = ''
        $currentOldType = ''
        foreach ($raw in $Lines) {
            $line = $raw.Trim()
            if ($line -eq '') { continue }
            # A type's member sections sit in { } after the type line; after the block, the enclosing section (types) resumes.
            if ($line -eq '{') { $sectionOutsideBlock = $section; continue }
            if ($line -eq '}') { $section = $sectionOutsideBlock; continue }
            if ($line -match '^(Renamed|Skipped|Retained|Not renamed)\s+(?<kind>[A-Za-z ]+?)\s*:?\s*$') {
                $section = $Matches['kind'].Trim().ToLowerInvariant()
                if ($line -match '^(Skipped|Retained|Not renamed)') { $section = 'ignore' }
                continue
            }
            if ($line -notmatch '^(?<left>.+?)\s+->\s+(?<right>\S.*)$') { continue }
            $left = $Matches['left'].Trim(); $right = $Matches['right'].Trim()
            if ($section -eq 'ignore') { continue }
            if ($section -eq 'types' -or ($section -eq '' -and $left -notmatch '::')) {
                $old = ConvertTo-EtpTypeKey $left
                $new = ConvertTo-EtpTypeKey $right
                if ($old -and $new) { $types[$new] = $old; $currentOldType = $old }
                continue
            }
            if ($section -eq 'methods' -or $section -eq 'properties' -or $section -eq 'events') {
                $leftNoAsm = [regex]::Replace($left, '^\[[^\]]*\]', '')
                $typePart = $currentOldType; $memberPart = $leftNoAsm
                $split = $leftNoAsm.IndexOf('::')
                if ($split -ge 0) { $typePart = ConvertTo-EtpTypeKey $leftNoAsm.Substring(0, $split); $memberPart = $leftNoAsm.Substring($split + 2) }
                $parameters = -1
                $name = $memberPart
                $paren = $memberPart.IndexOf('(')
                if ($paren -ge 0) {
                    $name = $memberPart.Substring(0, $paren)
                    $inside = $memberPart.Substring($paren + 1).TrimEnd(')').Trim()
                    $parameters = if ($inside -eq '') { 0 } else { ($inside -split ',').Count }
                }
                $newName = [regex]::Replace($right, '^\[[^\]]*\]', '')
                $newName = ($newName -split '[\s(]')[0]
                if (-not $typePart -or -not $name -or -not $newName) { continue }
                $key = $typePart + '|' + $newName
                if (-not $methods.ContainsKey($key)) { $methods[$key] = New-Object System.Collections.ArrayList }
                [void]$methods[$key].Add([pscustomobject]@{ Name = $name; Parameters = $parameters })
            }
        }
        return [pscustomobject]@{ Types = $types; Methods = $methods }
    }

    function Resolve-EtpMethodName($Map, [string]$OldType, [string]$NewName, [int]$ParameterCount) {
        $key = $OldType + '|' + $NewName
        if (-not $Map.Methods.ContainsKey($key)) { return $null }
        $candidates = @($Map.Methods[$key])
        if ($ParameterCount -ge 0) {
            $narrow = @($candidates | Where-Object { $_.Parameters -eq $ParameterCount })
            if ($narrow.Count -gt 0) { $candidates = $narrow }
        }
        return (@($candidates | ForEach-Object { $_.Name } | Select-Object -Unique)) -join ' | '
    }

    # Rewrites a trace. Returns Text plus counts (frames seen, frames resolved).
    function Resolve-EtpTraceText($Map, [string]$Text) {
        $typeNamesLongestFirst = @($Map.Types.Keys | Sort-Object { $_.Length } -Descending)
        $frames = 0; $resolved = 0
        $out = New-Object System.Collections.ArrayList
        foreach ($line in ($Text -split "\r?\n")) {
            $result = $line
            if ($line -match '^(?<indent>\s*)at\s+(?<call>[^\s(]+)\((?<args>[^)]*)\)(?<rest>.*)$') {
                $frames++
                $call = $Matches['call']
                $argText = $Matches['args']
                $oldType = $null; $newType = $null
                foreach ($candidate in $typeNamesLongestFirst) {
                    if ($call.StartsWith($candidate + '.', [StringComparison]::Ordinal)) { $newType = $candidate; $oldType = $Map.Types[$candidate]; break }
                }
                if ($newType) {
                    $newMethod = $call.Substring($newType.Length + 1)
                    $parameterCount = if ($argText.Trim() -eq '') { 0 } else { ($argText -split ',').Count }
                    $oldMethod = Resolve-EtpMethodName $Map $oldType $newMethod $parameterCount
                    $methodShown = if ($oldMethod) { $oldMethod } else { $newMethod }
                    foreach ($candidate in $typeNamesLongestFirst) {
                        $argText = [regex]::Replace($argText, '(?<![\w.+])' + [regex]::Escape($candidate) + '(?![\w])', { param($m) $Map.Types[$m.Value] })
                    }
                    $result = '{0}at {1}.{2}({3}){4}   [obfuscated: {5}.{6}]' -f $Matches['indent'], $oldType, $methodShown, $argText, $Matches['rest'], $newType, $newMethod
                    if ($oldMethod) { $resolved++ }
                }
            } else {
                foreach ($candidate in $typeNamesLongestFirst) {
                    if ($candidate.Contains('.') -and $result.Contains($candidate)) {
                        $result = [regex]::Replace($result, '(?<![\w.+])' + [regex]::Escape($candidate) + '(?![\w])', { param($m) $Map.Types[$m.Value] })
                    }
                }
            }
            [void]$out.Add($result)
        }
        return [pscustomobject]@{ Text = ($out -join "`r`n"); Frames = $frames; Resolved = $resolved }
    }
}
process {
    if ($InputObject) { foreach ($i in $InputObject) { [void]$pipelineLines.Add($i) } }
}
end {
    if ($LoadFunctionsOnly) { return }
    $exitCode = 2
    try {
        if (-not $MappingFile) {
            if (-not $SymbolsFolder) { Stop-EtpRefused 'Provide -MappingFile or -SymbolsFolder (E:\ETP\Reference\Symbols\<version>-<sha7>).' }
            if (-not (Test-Path -LiteralPath $SymbolsFolder -PathType Container)) { Stop-EtpRefused "Symbols folder not found: $SymbolsFolder" }
            $found = @(Get-ChildItem -LiteralPath $SymbolsFolder -Filter 'Mapping.txt' -File -Recurse)
            if ($found.Count -ne 1) { Stop-EtpRefused "Expected exactly one Mapping.txt under $SymbolsFolder, found $($found.Count)." }
            $MappingFile = $found[0].FullName
        }
        if (-not (Test-Path -LiteralPath $MappingFile -PathType Leaf)) { Stop-EtpRefused "Mapping file not found: $MappingFile" }
        $text = $null
        if ($TraceText) { $text = $TraceText }
        elseif ($TraceFile) {
            if (-not (Test-Path -LiteralPath $TraceFile -PathType Leaf)) { Stop-EtpRefused "Trace file not found: $TraceFile" }
            $text = [IO.File]::ReadAllText($TraceFile)
        }
        elseif ($pipelineLines.Count -gt 0) { $text = $pipelineLines -join "`r`n" }
        else { Stop-EtpRefused 'Provide -TraceFile, -TraceText or pipe the trace in.' }

        $map = ConvertFrom-EtpObfuscarMapping ([IO.File]::ReadAllLines($MappingFile))
        if ($map.Types.Count -eq 0) { Stop-EtpRefused "No renamed types were read from $MappingFile. Is it an Obfuscar Mapping.txt?" }
        $result = Resolve-EtpTraceText $map $text
        if ($OutputFile) { [IO.File]::WriteAllText($OutputFile, $result.Text, (New-Object Text.UTF8Encoding($false))) } else { Write-Output $result.Text }
        [Console]::Error.WriteLine(('Resolved {0} of {1} stack frames ({2} renamed types in the map).' -f $result.Resolved, $result.Frames, $map.Types.Count))
        $exitCode = if ($result.Frames -gt 0 -and $result.Resolved -eq 0) { 1 } else { 0 }
    }
    catch {
        $message = $_.Exception.Message
        if ($message -like 'ETP-REFUSED:*') { [Console]::Error.WriteLine('REFUSED: ' + $message.Substring('ETP-REFUSED:'.Length).Trim()); $exitCode = 2 }
        else { [Console]::Error.WriteLine("FAILED: $message"); $exitCode = 2 }
    }
    exit $exitCode
}
