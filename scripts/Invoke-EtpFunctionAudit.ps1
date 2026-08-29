[CmdletBinding()]
param(
    [string]$OutputPath = "verification/function-audit/current",
    [string]$SqlServer = "localhost\SQLEXPRESS"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedOutput = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
if (-not $resolvedOutput.StartsWith($repositoryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Audit output must remain inside the repository."
}

$databaseName = "EtpReportingFunctionAudit_{0}_{1}" -f (Get-Date -Format "yyyyMMddHHmmss"), $PID
if ($databaseName -notmatch '^EtpReportingFunctionAudit_[0-9]{14}_[0-9]+$') {
    throw "Generated validation database name failed the safety check."
}

$connectionString = "Server=$SqlServer;Database=$databaseName;Integrated Security=true;Encrypt=true;TrustServerCertificate=true;Connect Timeout=30"
$migrationPath = Join-Path $repositoryRoot "database/migrations"
$logPath = Join-Path $resolvedOutput "logs"
New-Item -ItemType Directory -Force -Path $resolvedOutput, $logPath | Out-Null
$steps = [System.Collections.Generic.List[object]]::new()
$overallStatus = "PASS"
$failureMessage = $null
$databaseRemoved = $false
$started = [DateTimeOffset]::UtcNow

function Invoke-AuditStep {
    param([string]$Name, [string]$Project, [string[]]$Arguments)
    $stepStarted = [DateTimeOffset]::UtcNow
    $logFile = Join-Path $logPath "$Name.log"
    & dotnet $Project @Arguments 2>&1 | Tee-Object -FilePath $logFile
    $exitCode = $LASTEXITCODE
    $step = [ordered]@{
        name = $Name
        status = if ($exitCode -eq 0) { "PASS" } else { "FAIL" }
        exitCode = $exitCode
        durationSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $stepStarted).TotalSeconds, 3)
        log = [System.IO.Path]::GetRelativePath($resolvedOutput, $logFile).Replace('\', '/')
    }
    $steps.Add([pscustomobject]$step)
    if ($exitCode -ne 0) { throw "Audit step '$Name' failed with exit code $exitCode." }
}

function Write-AuditSummary {
    $completed = [DateTimeOffset]::UtcNow
    $coverageFile = Join-Path $resolvedOutput "function-coverage.json"
    $coverage = if (Test-Path $coverageFile) { Get-Content -Raw $coverageFile | ConvertFrom-Json } else { $null }
    $summary = [ordered]@{
        audit = "ETP_FUNCTION_FIRST_SYNTHETIC_ACCEPTANCE_V1"
        status = $overallStatus
        startedUtc = $started
        completedUtc = $completed
        durationSeconds = [Math]::Round(($completed - $started).TotalSeconds, 3)
        disposableDatabaseRemoved = $databaseRemoved
        activeFunctionsMapped = if ($coverage) { $coverage.activeFunctionCount } else { $null }
        deferredUnavailableFunctions = if ($coverage) { $coverage.deferredUnavailableCount } else { $null }
        productReports = if ($coverage) { $coverage.reportCount } else { $null }
        uncoveredFunctions = if ($coverage) { $coverage.uncoveredCount } else { $null }
        failure = $failureMessage
        steps = $steps
        externalGates = @(
            "Signed installer installation and upgrade on a clean Windows workstation",
            "Observed keyboard-only, screen-reader and display-scaling accessibility session",
            "Installed email-client sharing behavior and corporate policy integration",
            "Production-like backup retention and restore drill outside the developer machine",
            "Business-owner control-total review and user acceptance sign-off"
        )
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $resolvedOutput "audit-summary.json")

    $markdown = @(
        "# ETP function-first synthetic acceptance audit",
        "",
        "- Overall result: **$overallStatus**",
        "- Active functions mapped: **$($summary.activeFunctionsMapped)**",
        "- Product reports mapped: **$($summary.productReports)**",
        "- Active functions without evidence: **$($summary.uncoveredFunctions)**",
        "- Disposable SQL database removed: **$(if ($databaseRemoved) { 'Yes' } else { 'No' })**",
        "",
        "## Automated evidence",
        "",
        "| Check | Result | Seconds | Evidence |",
        "|---|---:|---:|---|"
    )
    foreach ($step in $steps) { $markdown += "| $($step.name) | $($step.status) | $($step.durationSeconds) | ``$($step.log)`` |" }
    $markdown += ""
    $markdown += "## External acceptance gates"
    $markdown += ""
    foreach ($gate in $summary.externalGates) { $markdown += "- $gate" }
    if ($failureMessage) { $markdown += ""; $markdown += "## Failure"; $markdown += ""; $markdown += $failureMessage }
    $markdown -join [Environment]::NewLine | Set-Content -Encoding UTF8 (Join-Path $resolvedOutput "audit-summary.md")
}

Push-Location $repositoryRoot
try {
    Invoke-AuditStep "01-build" "build" @("Etp.Reporting.slnx", "-c", "Release")
    Invoke-AuditStep "02-inventory-and-fixtures" "run" @("--project", "tools/Etp.Reporting.FunctionAudit/Etp.Reporting.FunctionAudit.csproj", "-c", "Release", "--no-build", "--", $resolvedOutput)
    Invoke-AuditStep "03-offline-import" "run" @("--project", "tools/Etp.Reporting.Smoke/Etp.Reporting.Smoke.csproj", "-c", "Release", "--no-build", "--", (Join-Path $resolvedOutput "demo-data"))
    Invoke-AuditStep "04-dotnet-tests" "test" @("Etp.Reporting.slnx", "-c", "Release", "--no-build")
    Invoke-AuditStep "05-excel-export" "run" @("--project", "tools/Etp.Reporting.ExportSmoke/Etp.Reporting.ExportSmoke.csproj", "-c", "Release", "--no-build", "--", (Join-Path $resolvedOutput "exports/synthetic-report.xlsx"))
    Invoke-AuditStep "06-pdf-export" "run" @("--project", "tools/Etp.Reporting.ExportSmoke/Etp.Reporting.ExportSmoke.csproj", "-c", "Release", "--no-build", "--", (Join-Path $resolvedOutput "exports/synthetic-report.pdf"))
    Invoke-AuditStep "07-dsr-pdf" "run" @("--project", "tools/Etp.Reporting.DsrSmoke/Etp.Reporting.DsrSmoke.csproj", "-c", "Release", "--no-build", "--", (Join-Path $resolvedOutput "exports/synthetic-dsr.pdf"))
    Invoke-AuditStep "08-performance" "run" @("--project", "tools/Etp.Reporting.PerformanceSmoke/Etp.Reporting.PerformanceSmoke.csproj", "-c", "Release", "--no-build", "--", (Join-Path $resolvedOutput "performance.json"))
    Invoke-AuditStep "09-ui-all-routes" "run" @("--project", "tools/Etp.Reporting.UiSmoke/Etp.Reporting.UiSmoke.csproj", "-c", "Release", "--no-build", "--", (Join-Path $resolvedOutput "ui"))
    Invoke-AuditStep "10-live-sql" "run" @("--project", "tools/Etp.Reporting.LiveSmoke/Etp.Reporting.LiveSmoke.csproj", "-c", "Release", "--no-build", "--", (Join-Path $resolvedOutput "demo-data"), $connectionString, $migrationPath)
}
catch {
    $overallStatus = "FAIL"
    $failureMessage = $_.Exception.Message
}
finally {
    try {
        $dropSql = "IF DB_ID(N'$databaseName') IS NOT NULL BEGIN ALTER DATABASE [$databaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$databaseName]; END"
        & sqlcmd -S $SqlServer -E -C -b -Q $dropSql 2>&1 | Set-Content -Encoding UTF8 (Join-Path $logPath "11-database-cleanup.log")
        if ($LASTEXITCODE -ne 0) { throw "Disposable database cleanup failed." }
        $databaseRemoved = $true
    }
    catch {
        $overallStatus = "FAIL"
        if (-not $failureMessage) { $failureMessage = $_.Exception.Message }
    }
    Write-AuditSummary
    Pop-Location
}

if ($overallStatus -ne "PASS") { Write-Error $failureMessage; exit 1 }
Write-Host "ETP function-first audit passed. Evidence: $resolvedOutput"
