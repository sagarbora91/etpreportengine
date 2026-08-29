[CmdletBinding()]
param(
    [string]$AuditPath = "verification/function-audit/current",
    [string]$OutputPath = "verification/function-audit/current/external-acceptance"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$auditRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $AuditPath))
$outputRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $auditRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $outputRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Acceptance inputs and output must remain inside the repository."
}

$summaryPath = Join-Path $auditRoot "audit-summary.json"
$coveragePath = Join-Path $auditRoot "function-coverage.json"
$totalsPath = Join-Path $auditRoot "expected-control-totals.json"
foreach ($required in @($summaryPath, $coveragePath, $totalsPath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Run scripts/Invoke-EtpFunctionAudit.ps1 first. Missing evidence: $required"
    }
}

$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
$coverage = Get-Content -LiteralPath $coveragePath -Raw | ConvertFrom-Json
$totals = Get-Content -LiteralPath $totalsPath -Raw | ConvertFrom-Json
if ($summary.status -ne "PASS" -or $summary.uncoveredFunctions -ne 0) {
    throw "External acceptance cannot be prepared from a failed or incomplete automated audit."
}

$git = (Get-Command git -ErrorAction Stop).Source
$sourceCommit = (& $git -C $repositoryRoot rev-parse HEAD).Trim().ToLowerInvariant()
$branch = (& $git -C $repositoryRoot branch --show-current).Trim()
$sourceClean = @(& $git -C $repositoryRoot status --porcelain --untracked-files=all).Count -eq 0
$createdUtc = [DateTimeOffset]::UtcNow
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$templates = Join-Path $repositoryRoot "verification/templates"
foreach ($name in @("ETP-1.8.5-ROLE-UAT-REGISTER.csv", "ETP-1.8.5-DEFECT-REGISTER.csv", "ETP-1.8.5-RELEASE-GATE-DASHBOARD.csv")) {
    Copy-Item -LiteralPath (Join-Path $templates $name) -Destination (Join-Path $outputRoot $name) -Force
}

$roleRows = @(Import-Csv -LiteralPath (Join-Path $outputRoot "ETP-1.8.5-ROLE-UAT-REGISTER.csv"))
$gateRows = @(Import-Csv -LiteralPath (Join-Path $outputRoot "ETP-1.8.5-RELEASE-GATE-DASHBOARD.csv"))
if (($roleRows.ScenarioId | Sort-Object -Unique).Count -ne $roleRows.Count) { throw "The role UAT register contains duplicate scenario IDs." }
if (($gateRows.GateId | Sort-Object -Unique).Count -ne $gateRows.Count) { throw "The release dashboard contains duplicate gate IDs." }
if (@($roleRows | Where-Object Area -eq "Reports").Count -ne $coverage.reportCount) {
    throw "The role UAT register does not contain exactly one scenario for every product report."
}
if (($gateRows | Where-Object GateId -eq "SIGN-003").CurrentResult -eq "PASS") {
    throw "The template cannot pre-approve push, tag or release promotion."
}

$functionRows = foreach ($entry in $coverage.entries) {
    [pscustomobject][ordered]@{
        FunctionId = $entry.Id
        Kind = $entry.Kind
        Function = $entry.Name
        Disposition = $entry.Disposition
        AutomatedBaseline = if ($entry.Disposition -eq "DEFERRED_UNAVAILABLE") { "DECLARED UNAVAILABLE" } else { "PASS" }
        AutomatedEvidence = ($entry.Evidence -join "; ")
        ExternalResult = if ($entry.Disposition -eq "DEFERRED_UNAVAILABLE") { "DEFERRED" } else { "NOT RUN" }
        Role = ""
        Tester = ""
        ExecutedUtc = ""
        EvidenceReference = ""
        DefectId = ""
        Notes = $entry.Note
    }
}
$functionRows | Export-Csv -LiteralPath (Join-Path $outputRoot "ETP-1.8.5-FUNCTION-EXECUTION-REGISTER.csv") -NoTypeInformation -Encoding utf8
if (@($functionRows).Count -ne @($coverage.entries).Count) { throw "The generated function register is incomplete." }

$identity = [ordered]@{
    format = "etp-external-acceptance-workspace"
    version = 1
    createdUtc = $createdUtc
    productVersion = "1.8.5"
    sourceCommit = $sourceCommit
    branch = $branch
    sourceTreeCleanAtPreparation = $sourceClean
    automatedAuditStatus = $summary.status
    automatedAuditCompletedUtc = $summary.completedUtc
    activeFunctionsMapped = $summary.activeFunctionsMapped
    deferredUnavailableFunctions = $summary.deferredUnavailableFunctions
    productReports = $summary.productReports
    uncoveredFunctions = $summary.uncoveredFunctions
    candidateArtifactState = "NOT_BUILT"
    candidateApplicationSha256 = $null
    candidateInstallerSha256 = $null
    signatureState = "NOT_SIGNED"
    releaseState = "BLOCKED_PENDING_EXTERNAL_ACCEPTANCE"
}
$identity | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $outputRoot "candidate-identity.json") -Encoding utf8

$readme = @(
    "# ETP 1.8.5 external acceptance workspace",
    "",
    "> PRE-CANDIDATE WORKSPACE — NOT A RELEASE APPROVAL",
    "",
    "Prepared UTC: $($createdUtc.ToString('o'))  ",
    "Source commit: ``$sourceCommit``  ",
    "Branch: ``$branch``  ",
    "Source clean when prepared: **$sourceClean**  ",
    "Candidate artifacts: **NOT BUILT / NOT HASH-BOUND / NOT SIGNED**",
    "",
    "## Automated baseline",
    "",
    "- Overall result: **$($summary.status)**",
    "- Active functions mapped: **$($summary.activeFunctionsMapped)**",
    "- Deferred/unavailable functions: **$($summary.deferredUnavailableFunctions)**",
    "- Product reports: **$($summary.productReports)**",
    "- Active functions without evidence: **$($summary.uncoveredFunctions)**",
    "- Disposable SQL database removed: **$($summary.disposableDatabaseRemoved)**",
    "",
    "## Synthetic control oracle",
    "",
    "- Workbooks: **$($totals.workbookCount)** across stores **$($totals.stores -join ', ')**",
    "- Canonical sales rows: **$($totals.totals.canonicalSalesRows)**",
    "- Net sales: **$($totals.totals.netSales)**",
    "- Signed units: **$($totals.totals.signedUnits)**",
    "- Eligible tender total: **$($totals.totals.tenderTotal)**",
    "- Closing stock quantity: **$($totals.totals.closingStockQuantity)**",
    "- Quarantined tender rows: **$($totals.totals.quarantinedTenderRows)**",
    "",
    "## Execution order",
    "",
    "1. Build a clean 1.8.5 candidate outside the repository and record exact SHA-256 values in a new hash-bound evidence copy.",
    "2. Complete the release-gate dashboard. A blank, NOT RUN, FAIL or BLOCKED release gate is not approval.",
    "3. Execute the role UAT register separately as Owner, Store Manager and Viewer.",
    "4. Disposition every active row in the function execution register; attach privacy-safe evidence and defect IDs.",
    "5. Record every finding in the defect register. Severity 1 or 2 and unresolved control, privacy, security, data-loss or accessibility blockers prevent release.",
    "6. Repeat affected automated and external checks after every fix. Never edit expected totals to obtain a pass.",
    "7. Obtain named business, operational and release sign-off before tag, push or release promotion.",
    "",
    "The canonical instructions are in ``docs/audit/ETP-1.8.5-UAT-AND-RELEASE-READINESS-PACK.md``. The historical 1.8.4 evidence pack is rejected and must not be reused."
)
$readme -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $outputRoot "00-READ-ME-FIRST.md") -Encoding utf8

$manifestRows = Get-ChildItem -LiteralPath $outputRoot -File | Where-Object Name -ne "workspace-sha256.csv" | Sort-Object Name | ForEach-Object {
    [pscustomobject][ordered]@{ File = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant() }
}
$manifestRows | Export-Csv -LiteralPath (Join-Path $outputRoot "workspace-sha256.csv") -NoTypeInformation -Encoding utf8
Write-Host "External acceptance workspace prepared: $outputRoot"
Write-Host "Functions awaiting external disposition: $(@($functionRows | Where-Object ExternalResult -eq 'NOT RUN').Count)"
Write-Host "Release state: BLOCKED_PENDING_EXTERNAL_ACCEPTANCE"
