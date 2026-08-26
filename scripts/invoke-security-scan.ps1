param([string]$OutputPath = "artifacts/security-scan.json")

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
[IO.Directory]::CreateDirectory((Split-Path -Parent $output)) | Out-Null
$dotnetVulnerable = & dotnet list (Join-Path $repoRoot "Etp.Reporting.slnx") package --vulnerable --include-transitive 2>&1 | Out-String
$dotnetDeprecated = & dotnet list (Join-Path $repoRoot "Etp.Reporting.slnx") package --deprecated 2>&1 | Out-String
$npmAuditRaw = & npm --prefix $repoRoot audit --json 2>&1 | Out-String
$npmExitCode = $LASTEXITCODE
try { $npmAudit = $npmAuditRaw | ConvertFrom-Json } catch { $npmAudit = $null }
$result = [ordered]@{
    scannedUtc = [DateTime]::UtcNow.ToString("o")
    dotnetVulnerabilitiesFound = $dotnetVulnerable -notmatch 'has no vulnerable packages'
    dotnetDeprecatedFound = $dotnetDeprecated -notmatch 'has no deprecated packages'
    npmExitCode = $npmExitCode
    npmVulnerabilityCounts = if ($npmAudit.metadata.vulnerabilities) { $npmAudit.metadata.vulnerabilities } else { $null }
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8
$result | Format-List
if ($result.dotnetVulnerabilitiesFound -or ($npmAudit.metadata.vulnerabilities.total -gt 0)) { throw "Dependency vulnerabilities were detected. See $output." }
