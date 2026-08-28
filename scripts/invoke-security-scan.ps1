param([string]$OutputPath = "artifacts/security-scan.json")

$ErrorActionPreference = "Stop"

function Invoke-CapturedCommand {
    param([Parameter(Mandatory)][scriptblock]$Command)

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # Native tools commonly write diagnostics to stderr for non-zero exits.
        # Capture that text so callers can make an evidence-based retry decision
        # instead of letting the script-wide Stop policy discard the payload.
        $ErrorActionPreference = "Continue"
        $raw = & $Command 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Raw = $raw; Error = $null }
    }
    catch {
        return [pscustomobject]@{ ExitCode = -1; Raw = ""; Error = $_.Exception.Message }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function ConvertFrom-JsonPayload {
    param([AllowEmptyString()][string]$Raw)

    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    $start = $Raw.IndexOf('{')
    $end = $Raw.LastIndexOf('}')
    if ($start -lt 0 -or $end -lt $start) { return $null }
    try { return $Raw.Substring($start, $end - $start + 1) | ConvertFrom-Json }
    catch { return $null }
}

function Get-OptionalProperty {
    param([AllowNull()][object]$Value, [Parameter(Mandatory)][string]$Name)

    if ($null -eq $Value) { return $null }
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-DotnetPackageRows {
    param([AllowNull()][object]$Document)

    $rows = @()
    foreach ($project in @(Get-OptionalProperty $Document "projects")) {
        foreach ($framework in @(Get-OptionalProperty $project "frameworks")) {
            $packages = @(Get-OptionalProperty $framework "topLevelPackages") + @(Get-OptionalProperty $framework "transitivePackages")
            foreach ($package in $packages) {
                if ($null -eq $package) { continue }
                $alternative = Get-OptionalProperty $package "alternativePackage"
                $rows += [pscustomobject]@{
                    Project = Get-OptionalProperty $project "path"
                    Id = Get-OptionalProperty $package "id"
                    ResolvedVersion = Get-OptionalProperty $package "resolvedVersion"
                    Reasons = @(Get-OptionalProperty $package "deprecationReasons")
                    AlternativePackage = Get-OptionalProperty $alternative "id"
                    Vulnerabilities = @(Get-OptionalProperty $package "vulnerabilities")
                }
            }
        }
    }
    return $rows
}

function Get-DeprecatedPackageSummary {
    param([object[]]$Rows)

    return @($Rows | Group-Object Id, ResolvedVersion | ForEach-Object {
        $first = $_.Group[0]
        [ordered]@{
            id = $first.Id
            resolvedVersion = $first.ResolvedVersion
            reasons = @($_.Group.Reasons | ForEach-Object { $_ } | Where-Object { $_ } | Sort-Object -Unique)
            alternativePackage = $first.AlternativePackage
            affectedProjectCount = @($_.Group.Project | Sort-Object -Unique).Count
        }
    })
}

function Get-VulnerablePackageSummary {
    param([object[]]$Rows)

    return @($Rows | Group-Object Id, ResolvedVersion | ForEach-Object {
        $first = $_.Group[0]
        $vulnerabilities = @($_.Group.Vulnerabilities | ForEach-Object { $_ })
        [ordered]@{
            id = $first.Id
            resolvedVersion = $first.ResolvedVersion
            severities = @($vulnerabilities | ForEach-Object { Get-OptionalProperty $_ "severity" } | Where-Object { $_ } | Sort-Object -Unique)
            affectedProjectCount = @($_.Group.Project | Sort-Object -Unique).Count
        }
    })
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
[IO.Directory]::CreateDirectory((Split-Path -Parent $output)) | Out-Null
$solution = Join-Path $repoRoot "Etp.Reporting.slnx"

$dotnetVulnerableCommand = Invoke-CapturedCommand { dotnet list $solution package --vulnerable --include-transitive --format json }
$dotnetDeprecatedCommand = Invoke-CapturedCommand { dotnet list $solution package --deprecated --format json }
$dotnetVulnerableJson = ConvertFrom-JsonPayload $dotnetVulnerableCommand.Raw
$dotnetDeprecatedJson = ConvertFrom-JsonPayload $dotnetDeprecatedCommand.Raw
$dotnetVulnerableRows = @(Get-DotnetPackageRows $dotnetVulnerableJson)
$dotnetDeprecatedRows = @(Get-DotnetPackageRows $dotnetDeprecatedJson)
$dotnetVulnerableSucceeded = $dotnetVulnerableCommand.ExitCode -eq 0 -and $null -ne (Get-OptionalProperty $dotnetVulnerableJson "projects")
$dotnetDeprecatedSucceeded = $dotnetDeprecatedCommand.ExitCode -eq 0 -and $null -ne (Get-OptionalProperty $dotnetDeprecatedJson "projects")

$npmCommand = Invoke-CapturedCommand { npm --prefix $repoRoot audit --json }
$npmAudit = ConvertFrom-JsonPayload $npmCommand.Raw
$npmCounts = Get-OptionalProperty (Get-OptionalProperty $npmAudit "metadata") "vulnerabilities"
$npmRetriedWithSystemCa = $false
if ($null -eq $npmCounts -and $npmCommand.Raw -match "unable to verify the first certificate") {
    $npmRetriedWithSystemCa = $true
    $previousNodeOptions = $env:NODE_OPTIONS
    try {
        if ($previousNodeOptions -notmatch "(?:^|\s)--use-system-ca(?:\s|$)") {
            $env:NODE_OPTIONS = (($previousNodeOptions, "--use-system-ca") | Where-Object { $_ }) -join " "
        }
        $npmCommand = Invoke-CapturedCommand { npm --prefix $repoRoot audit --json }
        $npmAudit = ConvertFrom-JsonPayload $npmCommand.Raw
        $npmCounts = Get-OptionalProperty (Get-OptionalProperty $npmAudit "metadata") "vulnerabilities"
    }
    finally {
        if ($null -eq $previousNodeOptions) { Remove-Item Env:NODE_OPTIONS -ErrorAction SilentlyContinue }
        else { $env:NODE_OPTIONS = $previousNodeOptions }
    }
}
$npmTotalValue = Get-OptionalProperty $npmCounts "total"
$npmSucceeded = $null -ne $npmAudit -and $null -ne $npmCounts -and $null -ne $npmTotalValue

$scanErrors = @()
if (-not $dotnetVulnerableSucceeded) { $scanErrors += ".NET vulnerability scan did not return valid JSON (exit $($dotnetVulnerableCommand.ExitCode))." }
if (-not $dotnetDeprecatedSucceeded) { $scanErrors += ".NET deprecation scan did not return valid JSON (exit $($dotnetDeprecatedCommand.ExitCode))." }
if (-not $npmSucceeded) { $scanErrors += "npm audit did not return a valid vulnerability summary (exit $($npmCommand.ExitCode))." }

$vulnerablePackages = @(Get-VulnerablePackageSummary $dotnetVulnerableRows)
$deprecatedPackages = @(Get-DeprecatedPackageSummary $dotnetDeprecatedRows)
$npmTotal = if ($npmSucceeded) { [int]$npmTotalValue } else { $null }
$scanStatus = if ($scanErrors.Count -gt 0) {
    "error"
}
elseif ($vulnerablePackages.Count -gt 0 -or $deprecatedPackages.Count -gt 0 -or $npmTotal -gt 0) {
    "findings"
}
else {
    "clean"
}
$result = [ordered]@{
    scannedUtc = [DateTime]::UtcNow.ToString("o")
    status = $scanStatus
    scanSucceeded = $scanErrors.Count -eq 0
    scanErrors = $scanErrors
    dotnetVulnerabilitiesFound = $vulnerablePackages.Count -gt 0
    dotnetDeprecatedFound = $deprecatedPackages.Count -gt 0
    npmExitCode = $npmCommand.ExitCode
    npmVulnerabilityCounts = if ($npmSucceeded) { $npmCounts } else { $null }
    dotnet = [ordered]@{
        vulnerabilityScanSucceeded = $dotnetVulnerableSucceeded
        vulnerabilityScanExitCode = $dotnetVulnerableCommand.ExitCode
        vulnerablePackages = $vulnerablePackages
        deprecationScanSucceeded = $dotnetDeprecatedSucceeded
        deprecationScanExitCode = $dotnetDeprecatedCommand.ExitCode
        deprecatedPackages = $deprecatedPackages
    }
    npm = [ordered]@{
        status = if (-not $npmSucceeded) { "error" } elseif ($npmTotal -gt 0) { "findings" } else { "clean" }
        scanSucceeded = $npmSucceeded
        exitCode = $npmCommand.ExitCode
        retriedWithSystemCa = $npmRetriedWithSystemCa
        vulnerabilityCounts = if ($npmSucceeded) { $npmCounts } else { $null }
    }
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $output -Encoding utf8
$result | Format-List

if ($scanErrors.Count -gt 0) { throw "One or more dependency scans failed. No clean result was claimed. See $output." }
if ($result.dotnetVulnerabilitiesFound -or $npmTotal -gt 0) { throw "Dependency vulnerabilities were detected. See $output." }
