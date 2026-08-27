[CmdletBinding()]
param(
    [string]$VaultPath,
    [int]$StaleAfterDays = 180
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($VaultPath)) {
    $VaultPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'knowledge'
}

if (-not (Test-Path -LiteralPath $VaultPath -PathType Container)) {
    throw "Knowledge vault not found: $VaultPath"
}

$notes = @(Get-ChildItem -LiteralPath $VaultPath -Recurse -File -Filter '*.md')
$names = @{}
foreach ($note in $notes) {
    $names[$note.BaseName.ToLowerInvariant()] = $note.FullName
}

$broken = [System.Collections.Generic.List[string]]::new()
$stale = [System.Collections.Generic.List[string]]::new()
$today = [DateTime]::Today

foreach ($note in $notes) {
    $content = Get-Content -LiteralPath $note.FullName -Raw
    foreach ($match in [regex]::Matches($content, '\[\[([^\]|#]+)(?:#[^\]|]+)?(?:\|[^\]]+)?\]\]')) {
        $target = $match.Groups[1].Value.Trim()
        if (-not $names.ContainsKey($target.ToLowerInvariant())) {
            $broken.Add("$($note.FullName): [[$target]]")
        }
    }

    $verifiedMatch = [regex]::Match($content, '(?m)^last_verified:\s*(\d{4}-\d{2}-\d{2})\s*$')
    if ($verifiedMatch.Success) {
        $verified = [DateTime]::ParseExact($verifiedMatch.Groups[1].Value, 'yyyy-MM-dd', $null)
        if (($today - $verified).TotalDays -gt $StaleAfterDays) {
            $stale.Add("$($note.FullName): last verified $($verified.ToString('yyyy-MM-dd'))")
        }
    }
}

if ($stale.Count -gt 0) {
    Write-Warning ("Potentially stale notes:`n" + ($stale -join "`n"))
}

if ($broken.Count -gt 0) {
    throw ("Broken Wiki links:`n" + ($broken -join "`n"))
}

Write-Output "Knowledge vault valid: $($notes.Count) notes, 0 broken Wiki links, $($stale.Count) stale-note warnings."
