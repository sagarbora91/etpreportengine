<#
.SYNOPSIS
  Runs on Workpc (task 'ETP Publish Shop Results', 07:40, or by hand). Signs each shop result file that ETP wrote and puts it
  in that shop's own OneDrive folder, so the shop PC can show "accepted" or "refused, and why".

.DESCRIPTION
  - ETP writes  <resultsFolder>\pending\<ROLE>-<yyyy-MM-dd>-<seq>.json  after it imports a shop's zip. A result holds verdicts
    only (accepted / refused plus a plain reason), never amounts or names.
  - Each file is signed with THAT shop's key (<shopKeysFolder>\<ROLE>.key, the same key that signs the shop's manifest):
    HMAC-SHA256 over "ETP-RESULT-v1<LF>" plus the file's text. The prefix means a manifest signature can never pass as a result.
  - Published as  <OneDrive>\ETP-Inbound\<ROLE>\Results\<day>.result.json  (sequence 1) or  <day>-<seq>.result.json  (a
    re-import after a fix). Written to a temp name and renamed, so the shop never reads half a file. The local file then moves
    to  <resultsFolder>\published\.
  - A role without a key here is left in pending and reported (exit 2); nothing is lost and nothing is published unsigned.
  - A pending file that is not a schema-1 result for the role and day in its name is moved to  rejected\  and reported.
  - Published results older than keepDays (default 30) are removed from OneDrive.
  - Config: workpc-receive.config.json (the same file as Receive-ShopDailyFiles.ps1). Optional keys: resultsFolder
    (default %ProgramData%\EtpReporting\Operations\ShopResults), keepDays. Windows PowerShell 5.1 compatible.

.PARAMETER ConfigPath  Override the config location.
.PARAMETER WhatIf      Say what would be published; change nothing.

Exit codes: 0 done (or nothing pending), 2 done but a result could not be published or was rejected, 1 error.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'workpc-receive.config.json')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneDriveCommon.ps1')
. (Join-Path $PSScriptRoot 'ShopResultCommon.ps1')

function Test-EtpPendingPayload {
    # A pending file must be a schema-1 result whose role, day and sequence equal the ones in its name.
    param([string]$Text, $Info)
    if ($Text.Length -gt $script:EtpResultMaxChars) { return 'too large' }
    try { $payload = $Text | ConvertFrom-Json } catch { return 'not valid JSON' }
    if ($null -eq $payload -or $payload -isnot [pscustomobject]) { return 'not a result' }
    if ([string](Get-EtpConfigValue $payload 'schemaVersion' '') -ne '1') { return 'unknown result version' }
    if ([string](Get-EtpConfigValue $payload 'role' '') -ne $Info.Role) { return 'the role differs from the file name' }
    if ([string](Get-EtpConfigValue $payload 'businessDate' '') -ne $Info.Day) { return 'the business date differs from the file name' }
    if ([string](Get-EtpConfigValue $payload 'sequence' '') -ne [string]$Info.Sequence) { return 'the sequence differs from the file name' }
    foreach ($f in @(Get-EtpConfigValue $payload 'files' @())) {
        if ($script:EtpResultVerdicts -notcontains [string](Get-EtpConfigValue $f 'verdict' '')) { return 'unknown verdict' }
    }
    return ''
}

function Publish-EtpShopResults {
    param([string]$ConfigPath, [bool]$DryRun)

    $config = Read-EtpOneDriveConfig -Path $ConfigPath
    $roles = @(Get-EtpConfigValue $config 'roles' $script:EtpValidRoles | ForEach-Object { ([string]$_).ToUpperInvariant() } | Where-Object { $script:EtpValidRoles -contains $_ })
    $keysFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'shopKeysFolder' "$env:ProgramData\EtpReporting\Operations\shop-keys"))
    $resultsFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'resultsFolder' "$env:ProgramData\EtpReporting\Operations\ShopResults"))
    $work = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'workFolder' "$env:ProgramData\EtpReporting\OneDriveReceive"))
    $keepDays = [int](Get-EtpConfigValue $config 'keepDays' 30)
    $checkClient = [bool](Get-EtpConfigValue $config 'checkOneDriveClient' $true)
    $logFile = Join-Path $work 'publish-results.log.jsonl'

    $pending = Join-Path $resultsFolder 'pending'
    if (-not (Test-Path -LiteralPath $pending -PathType Container)) { Write-Host 'No pending shop results.'; return 0 }
    $oneDrive = Resolve-EtpOneDriveRoot -Override ([string](Get-EtpConfigValue $config 'oneDriveRoot' ''))
    if (Test-EtpPathInside -Path $resultsFolder -Folder $oneDrive) { throw "resultsFolder '$resultsFolder' must not be inside OneDrive." }
    Assert-EtpKeyOutsideOneDrive -KeyFile (Join-Path $keysFolder 'x.key') -ConfiguredRoot ([string](Get-EtpConfigValue $config 'oneDriveRoot' ''))
    if (-not $DryRun) {
        if ($checkClient) { Assert-EtpOneDriveClientRunning -LogFile $logFile }
        foreach ($sub in 'published', 'rejected') { $p = Join-Path $resultsFolder $sub; if (-not (Test-Path -LiteralPath $p)) { [void](New-Item -ItemType Directory -Path $p -Force) } }
    }

    $problems = 0; $published = 0
    $files = @(Get-ChildItem -LiteralPath $pending -File -Filter '*.json' -ErrorAction SilentlyContinue | Sort-Object Name)
    foreach ($file in $files) {
        $info = Get-EtpPendingResultInfo -Name $file.Name
        if ($null -eq $info -or $roles -notcontains $info.Role) {
            Write-EtpLogLine -LogFile $logFile -Event 'PendingIgnored' -Data @{ file = $file.Name; reason = 'name is not ROLE-yyyy-MM-dd-seq.json for a configured role' }
            continue
        }
        $role = $info.Role
        $text = $null
        try { $text = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8) } catch { }
        $reason = if ($null -eq $text) { 'unreadable' } else { Test-EtpPendingPayload -Text $text -Info $info }
        if ($reason) {
            $problems++
            Write-Host "$($file.Name): rejected ($reason)." -ForegroundColor Red
            Write-EtpLogLine -LogFile $logFile -Event 'PendingRejected' -Data @{ file = $file.Name; role = $role; reason = $reason }
            if (-not $DryRun) { Move-Item -LiteralPath $file.FullName -Destination (Join-Path $resultsFolder (Join-Path 'rejected' $file.Name)) -Force }
            continue
        }
        $keyFile = Join-Path $keysFolder "$role.key"
        if (-not (Test-Path -LiteralPath $keyFile -PathType Leaf)) {
            $problems++
            Write-Host "$($file.Name): no key for $role in $keysFolder, so nothing can be signed. It stays in pending." -ForegroundColor Red
            Write-EtpLogLine -LogFile $logFile -Event 'NoKey' -Data @{ file = $file.Name; role = $role }
            continue
        }
        $key = Read-EtpSecretKeyBytes -KeyFile $keyFile -Bytes $script:EtpSigningKeyBytes -What "$role signing key"
        $name = if ($info.Sequence -le 1) { "$($info.Day).result.json" } else { "$($info.Day)-$($info.Sequence).result.json" }
        $target = Join-Path $oneDrive (Join-Path 'ETP-Inbound' (Join-Path $role (Join-Path 'Results' $name)))
        if ($DryRun) { Write-Host "WhatIf: would publish $($file.Name) -> $target"; continue }
        # The sequence is in the published name, so a late re-run of an older file never replaces a newer result.
        Write-EtpTextAtomically -Path $target -Text (New-EtpSignedResultText -PayloadText $text -Key $key)
        Move-Item -LiteralPath $file.FullName -Destination (Join-Path $resultsFolder (Join-Path 'published' $file.Name)) -Force
        $published++
        Write-Host "$role $($info.Day) (seq $($info.Sequence)) published."
        Write-EtpLogLine -LogFile $logFile -Event 'Published' -Data @{ role = $role; day = $info.Day; sequence = $info.Sequence; target = $name }
    }

    # Retention: results older than keepDays leave OneDrive (the signed copy is regenerable from published\).
    if (-not $DryRun -and $keepDays -gt 0) {
        $cutoff = (Get-Date).Date.AddDays(-$keepDays)
        foreach ($role in $roles) {
            $folder = Join-Path $oneDrive (Join-Path 'ETP-Inbound' (Join-Path $role 'Results'))
            foreach ($old in @(Get-ChildItem -LiteralPath $folder -File -ErrorAction SilentlyContinue)) {
                $oldInfo = Get-EtpResultFileInfo -Name $old.Name
                if ($oldInfo -and [datetime]::ParseExact($oldInfo.Day, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture) -lt $cutoff) { Remove-Item -LiteralPath $old.FullName -Force }
            }
        }
    }
    Write-EtpLogLine -LogFile $logFile -Event 'Done' -Data @{ published = $published; problems = $problems; dryRun = $DryRun }
    Write-Host "Published $published shop result(s); $problems problem(s)."
    if ($problems -gt 0) { return 2 }
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    try { exit (Publish-EtpShopResults -ConfigPath $ConfigPath -DryRun ([bool]$WhatIfPreference)) }
    catch { Write-Error $_.Exception.Message; exit 1 }
}
