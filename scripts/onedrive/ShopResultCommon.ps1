# Shared helpers for the shop result files (1.12 lane c-shop-results; decision 44 #3). Dot-source this AFTER OneDriveCommon.ps1;
# it runs nothing by itself. Windows PowerShell 5.1 compatible.
#
# ETP writes a verdict-only JSON per shop zip (no amounts, no names). Workpc signs it with THAT shop's key and publishes it to
# OneDrive\ETP-Inbound\<ROLE>\Results\; the shop PC verifies it with its own key. The signed text starts with a domain prefix
# that no manifest has, so a manifest's HMAC can never pass as a result's (and the other way round).

Set-StrictMode -Version 2.0

$script:EtpResultDomain = "ETP-RESULT-v1`n"
$script:EtpResultMaxChars = 262144
$script:EtpResultVerdicts = @('Accepted', 'AcceptedWithWarning', 'Refused', 'Duplicate')

function Get-EtpResultHmac {
    # HMAC-SHA256 over the domain prefix plus the UTF-8 bytes of the payload text, upper-case hex.
    param([Parameter(Mandatory)][string]$PayloadText, [Parameter(Mandatory)][byte[]]$Key)
    $bytes = [Text.Encoding]::UTF8.GetBytes($script:EtpResultDomain + $PayloadText)
    $hmac = New-Object Security.Cryptography.HMACSHA256(,$Key)
    try { return ([BitConverter]::ToString($hmac.ComputeHash($bytes)) -replace '-', '') }
    finally { $hmac.Dispose() }
}

function Test-EtpHexEqual {
    # Constant-time comparison of two hex strings (case-insensitive).
    param([string]$First, [string]$Second)
    $a = $First.ToUpperInvariant(); $b = $Second.ToUpperInvariant()
    if ($a.Length -ne $b.Length) { return $false }
    $difference = 0
    for ($i = 0; $i -lt $a.Length; $i++) { $difference = $difference -bor ([int][char]$a[$i] -bxor [int][char]$b[$i]) }
    return ($difference -eq 0)
}

function New-EtpSignedResultText {
    # The published file: {format, schemaVersion, keyId, payload (the exact text that was signed), hmac}.
    param([Parameter(Mandatory)][string]$PayloadText, [Parameter(Mandatory)][byte[]]$Key)
    $envelope = [ordered]@{
        format        = 'ETP-RESULT'
        schemaVersion = 1
        keyId         = (Get-EtpKeyId -KeyBytes $Key)
        payload       = $PayloadText
        hmac          = (Get-EtpResultHmac -PayloadText $PayloadText -Key $Key)
    }
    return ($envelope | ConvertTo-Json -Depth 3)
}

function New-EtpResultCheck {
    param([bool]$Valid, [string]$Reason = '', $Payload = $null)
    return [pscustomobject]@{ Valid = $Valid; Reason = $Reason; Payload = $Payload }
}

function Test-EtpSignedResult {
    # Valid only when the envelope is well formed, the key id is this key's, the HMAC matches and the payload is schema 1
    # with known verdicts. Anything else is "could not be checked", never a verdict.
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][byte[]]$Key)
    if ($Text.Length -gt $script:EtpResultMaxChars) { return (New-EtpResultCheck $false 'too large') }
    try { $envelope = $Text | ConvertFrom-Json } catch { return (New-EtpResultCheck $false 'not valid JSON') }
    if ($null -eq $envelope -or $envelope -isnot [pscustomobject]) { return (New-EtpResultCheck $false 'not an ETP result') }
    if ([string](Get-EtpConfigValue $envelope 'format' '') -ne 'ETP-RESULT' -or [string](Get-EtpConfigValue $envelope 'schemaVersion' '') -ne '1') { return (New-EtpResultCheck $false 'not an ETP result') }
    $payloadText = Get-EtpConfigValue $envelope 'payload' $null
    if ($payloadText -isnot [string]) { return (New-EtpResultCheck $false 'no payload') }
    if ([string](Get-EtpConfigValue $envelope 'keyId' '') -ne (Get-EtpKeyId -KeyBytes $Key)) { return (New-EtpResultCheck $false 'signed with a different key') }
    if (-not (Test-EtpHexEqual -First ([string](Get-EtpConfigValue $envelope 'hmac' '')) -Second (Get-EtpResultHmac -PayloadText $payloadText -Key $Key))) { return (New-EtpResultCheck $false 'the signature does not match') }
    try { $payload = $payloadText | ConvertFrom-Json } catch { return (New-EtpResultCheck $false 'the payload is not valid JSON') }
    if ($null -eq $payload -or $payload -isnot [pscustomobject]) { return (New-EtpResultCheck $false 'the payload is not a result') }
    if ([string](Get-EtpConfigValue $payload 'schemaVersion' '') -ne '1') { return (New-EtpResultCheck $false 'unknown result version') }
    if ($script:EtpValidRoles -notcontains [string](Get-EtpConfigValue $payload 'role' '')) { return (New-EtpResultCheck $false 'unknown shop') }
    if ([string](Get-EtpConfigValue $payload 'businessDate' '') -notmatch '^\d{4}-\d{2}-\d{2}$') { return (New-EtpResultCheck $false 'no business date') }
    foreach ($f in @(Get-EtpConfigValue $payload 'files' @())) {
        if ($script:EtpResultVerdicts -notcontains [string](Get-EtpConfigValue $f 'verdict' '')) { return (New-EtpResultCheck $false 'unknown verdict') }
    }
    return (New-EtpResultCheck $true '' $payload)
}

function Get-EtpResultFileInfo {
    # <yyyy-MM-dd>.result.json (sequence 1) or <yyyy-MM-dd>-<n>.result.json. Returns $null for any other name.
    param([string]$Name)
    if ($Name -match '^(\d{4}-\d{2}-\d{2})(?:-(\d{1,3}))?\.result\.json$') {
        $sequence = 1
        if ($Matches[2]) { $sequence = [int]$Matches[2] }
        return [pscustomobject]@{ Day = $Matches[1]; Sequence = $sequence }
    }
    return $null
}

function Get-EtpPendingResultInfo {
    # <ROLE>-<yyyy-MM-dd>-<seq>.json as ETP writes it. Returns $null for any other name.
    param([string]$Name)
    if ($Name -match '^(TITAN|HELIOS|SERVICE)-(\d{4}-\d{2}-\d{2})-(\d{1,3})\.json$') {
        return [pscustomobject]@{ Role = $Matches[1]; Day = $Matches[2]; Sequence = [int]$Matches[3] }
    }
    return $null
}

function Format-EtpShopText {
    # Text from a (verified) result for a window or file: no control characters, at most 300 characters.
    param($Text)
    $clean = ([string]$Text) -replace '[\x00-\x1F\x7F]', ' '
    if ($clean.Length -gt 300) { $clean = $clean.Substring(0, 300) }
    return $clean.Trim()
}

function New-EtpShopResultSummary {
    param([string]$State, [string]$Headline, $Lines = @(), [string]$Day = '', [int]$Sequence = 0, [string]$File = '')
    return [pscustomobject]@{ State = $State; Headline = $Headline; Lines = @($Lines); Day = $Day; Sequence = $Sequence; File = $File }
}

function Get-EtpShopResultSummary {
    # Reads the newest result in the shop's Results folder and describes it for the screen. State is one of:
    # None (no result yet), Unchecked (a file is there but could not be verified: NEVER shown as a verdict), Accepted,
    # Warning, Refused (verdicts of a verified result).
    param([Parameter(Mandatory)][string]$ResultsFolder, $Key, [Parameter(Mandatory)][string]$Role)
    if (-not (Test-Path -LiteralPath $ResultsFolder -PathType Container)) { return (New-EtpShopResultSummary 'None' 'No result from ETP yet.') }
    $candidates = @(Get-ChildItem -LiteralPath $ResultsFolder -File -ErrorAction SilentlyContinue | ForEach-Object {
        $info = Get-EtpResultFileInfo -Name $_.Name
        if ($info) { [pscustomobject]@{ Item = $_; Day = $info.Day; Sequence = $info.Sequence } }
    })
    if ($candidates.Count -eq 0) { return (New-EtpShopResultSummary 'None' 'No result from ETP yet.') }
    $latest = @($candidates | Sort-Object -Property @{ Expression = 'Day'; Descending = $true }, @{ Expression = 'Sequence'; Descending = $true })[0]
    $why = ''
    $text = $null
    if ($null -eq $Key) { $why = 'this PC has no signing key' }
    elseif (Test-EtpOneDrivePlaceholder -Path $latest.Item.FullName) { $why = 'it is not downloaded from the cloud yet' }
    else {
        try { $text = [IO.File]::ReadAllText($latest.Item.FullName, [Text.Encoding]::UTF8) } catch { $why = 'the file could not be read' }
    }
    $payload = $null
    if (-not $why) {
        $check = Test-EtpSignedResult -Text $text -Key ([byte[]]$Key)
        if (-not $check.Valid) { $why = $check.Reason }
        else {
            $payload = $check.Payload
            # Review m-1 (decision 53): the sequence too, so a genuine earlier verdict copied to a higher -N name cannot
            # override a later one.
            if ([string]$payload.role -ne $Role -or [string]$payload.businessDate -ne $latest.Day -or [string]$payload.sequence -ne [string]$latest.Sequence) {
                $why = 'it belongs to another shop, day or send'
            }
        }
    }
    if ($why) {
        return (New-EtpShopResultSummary 'Unchecked' "The result for $($latest.Day) could not be checked ($why). Do not rely on it; ask the office." @() $latest.Day $latest.Sequence $latest.Item.Name)
    }

    $worst = 'Accepted'
    $lines = New-Object System.Collections.ArrayList
    foreach ($f in @(Get-EtpConfigValue $payload 'files' @())) {
        $name = Format-EtpShopText (Get-EtpConfigValue $f 'name' '')
        $message = Format-EtpShopText (Get-EtpConfigValue $f 'message' '')
        $todo = Format-EtpShopText (Get-EtpConfigValue $f 'whatToDo' '')
        switch ([string]$f.verdict) {
            'Refused' {
                $worst = 'Refused'
                $line = "REFUSED  ${name}: $message"
                if ($todo) { $line += " Fix: $todo" }
                [void]$lines.Add($line)
            }
            'AcceptedWithWarning' {
                if ($worst -ne 'Refused') { $worst = 'Warning' }
                $line = "WARNING  ${name}: $message"
                if ($todo) { $line += " $todo" }
                [void]$lines.Add($line)
            }
            'Duplicate' { [void]$lines.Add("ALREADY HAD  ${name}: ETP already has this exact file.") }
            default { [void]$lines.Add("OK  $name") }
        }
    }
    $headline = "Sheet for $($latest.Day): accepted."
    if ($worst -eq 'Refused') { $headline = "Sheet for $($latest.Day): REFUSED. ETP did not take a file." }
    elseif ($worst -eq 'Warning') { $headline = "Sheet for $($latest.Day): accepted, with a warning." }
    return (New-EtpShopResultSummary $worst $headline @($lines) $latest.Day $latest.Sequence $latest.Item.Name)
}

function Write-EtpShopResultStatus {
    # last-result.txt in the shop's log folder (the same place as last-send.txt).
    param([Parameter(Mandatory)][string]$LogFolder, [Parameter(Mandatory)]$Summary)
    $text = New-Object System.Collections.ArrayList
    [void]$text.Add("$((Get-Date).ToString('yyyy-MM-dd HH:mm'))  $($Summary.Headline)")
    foreach ($line in @($Summary.Lines)) { [void]$text.Add("  $line") }
    try { Write-EtpTextAtomically -Path (Join-Path $LogFolder 'last-result.txt') -Text (($text -join "`r`n") + "`r`n") }
    catch { Write-Warning "Could not write last-result.txt: $($_.Exception.Message)" }
}

function Get-EtpShopResultsContext {
    # Everything Send and Show both need: role, key (or $null), results folder, log folder.
    param([Parameter(Mandatory)][string]$ConfigPath)
    $config = Read-EtpOneDriveConfig -Path $ConfigPath
    $role = ([string](Get-EtpConfigValue $config 'role' '')).ToUpperInvariant()
    if ($script:EtpValidRoles -notcontains $role) { throw "Config 'role' must be one of: $($script:EtpValidRoles -join ', ')." }
    $oneDrive = Resolve-EtpOneDriveRoot -Override ([string](Get-EtpConfigValue $config 'oneDriveRoot' ''))
    $keyPath = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'signingKeyFile' '%LOCALAPPDATA%\EtpOneDrive\shop-signing.key'))
    $key = $null
    if (Test-Path -LiteralPath $keyPath -PathType Leaf) {
        try { $key = Read-EtpSecretKeyBytes -KeyFile $keyPath -Bytes $script:EtpSigningKeyBytes -What 'signing key' } catch { $key = $null }
    }
    return [pscustomobject]@{
        Role          = $role
        Key           = $key
        ResultsFolder = (Join-Path $oneDrive (Join-Path 'ETP-Inbound' (Join-Path $role 'Results')))
        LogFolder     = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'logFolder' '%LOCALAPPDATA%\EtpOneDrive'))
    }
}

function Update-EtpShopResult {
    # Reads and checks the newest result, writes last-result.txt and prints it. Used by Show-ShopResults.ps1 (which adds the
    # window) and by Send-ShopDailyFiles.ps1 (console only). Returns the summary.
    param([Parameter(Mandatory)]$Context)
    $summary = Get-EtpShopResultSummary -ResultsFolder $Context.ResultsFolder -Key $Context.Key -Role $Context.Role
    Write-EtpShopResultStatus -LogFolder $Context.LogFolder -Summary $summary
    $colour = 'Green'
    if ($summary.State -eq 'Warning') { $colour = 'Yellow' } elseif (@('Refused', 'Unchecked') -contains $summary.State) { $colour = 'Red' }
    Write-Host $summary.Headline -ForegroundColor $colour
    foreach ($line in $summary.Lines) { Write-Host "  $line" -ForegroundColor $colour }
    return $summary
}
