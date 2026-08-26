param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [switch]$UpdateChangelog
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot "Directory.Build.props"
[xml]$props = Get-Content -LiteralPath $propsPath
$node = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
if ($null -eq $node) { throw "VersionPrefix is missing from Directory.Build.props." }
$node.InnerText = $Version
$settings = [System.Xml.XmlWriterSettings]::new()
$settings.Indent = $true
$settings.OmitXmlDeclaration = $true
$writer = [System.Xml.XmlWriter]::Create($propsPath, $settings)
try { $props.Save($writer) } finally { $writer.Dispose() }

if ($UpdateChangelog) {
    $path = Join-Path $repoRoot "CHANGELOG.md"
    $content = Get-Content -LiteralPath $path -Raw
    $date = Get-Date -Format yyyy-MM-dd
    $content = $content.Replace("## [Unreleased]", "## [Unreleased]`r`n`r`n## [$Version] - $date")
    Set-Content -LiteralPath $path -Value $content -Encoding utf8
}

Write-Host "Release version set to $Version"
