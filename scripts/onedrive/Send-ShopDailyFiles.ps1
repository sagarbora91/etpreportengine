<#
.SYNOPSIS
  Runs on each shop PC. Copies the day's ETP export files and the staff daily-inputs workbook into this PC's
  OneDrive folder  ETP-Inbound\<ROLE>\<yyyy-MM-dd>\  and writes a SIGNED manifest.json (written last = "this day is
  complete").

.DESCRIPTION
  - Source files are only read, never changed or deleted.
  - Files still being written (size/time still changing, or locked) are skipped; run again later (safe).
  - Safe to run any number of times for the same day: identical files are not copied again.
  - The manifest carries an HMAC-SHA256 made with this PC's own signing key, so Workpc can tell the files really came
    from this PC (AUDIT-2 S-02). The key stays on this PC, outside OneDrive; a copy goes to Workpc by USB only.
  - Each run says how many files were sent and how many were skipped, on screen and in
    <logFolder>\last-send.txt (the task runs hidden, so that file is where staff can look).
  - Config: shop-daily-files.config.json next to this script (copy shop-daily-files.config.example.json).
  - Windows PowerShell 5.1 compatible. Handles no passwords.

.PARAMETER Date            The business day to send (default: today).
.PARAMETER ConfigPath      Override the config location.
.PARAMETER NewSigningKey   Create this PC's signing key (once). Prints its fingerprint and what to copy to Workpc.
.PARAMETER ShowSigningKey  Print the fingerprint of this PC's signing key (to compare with Workpc's copy).
.PARAMETER WhatIf          Show what would be copied; copies nothing.

Exit codes: 0 done (or nothing for that day), 2 done but some files were skipped (in use / not stable), 1 error.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [datetime]$Date = (Get-Date).Date,
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'shop-daily-files.config.json'),
    [switch]$NewSigningKey,
    [switch]$ShowSigningKey
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneDriveCommon.ps1')

function Get-EtpShopRole {
    param($Config)
    $role = ([string](Get-EtpConfigValue $Config 'role' '')).ToUpperInvariant()
    if ($script:EtpValidRoles -notcontains $role) { throw "Config 'role' must be one of: $($script:EtpValidRoles -join ', ')." }
    return $role
}

function Get-EtpShopSigningKeyPath {
    param($Config)
    $path = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $Config 'signingKeyFile' '%LOCALAPPDATA%\EtpOneDrive\shop-signing.key'))
    Assert-EtpKeyOutsideOneDrive -KeyFile $path -ConfiguredRoot ([string](Get-EtpConfigValue $Config 'oneDriveRoot' ''))
    return $path
}

function Write-EtpSendStatus {
    # Screen + last-send.txt. Red lines when anything was skipped (AUDIT-6 item 6: "sent N, skipped M").
    param([string]$LogFolder, [string]$Headline, [string[]]$SkippedLines, [string[]]$Warnings)
    $text = New-Object System.Collections.ArrayList
    [void]$text.Add("$((Get-Date).ToString('yyyy-MM-dd HH:mm'))  $Headline")
    if ($SkippedLines.Count -gt 0) {
        Write-Host $Headline -ForegroundColor Red
        foreach ($line in $SkippedLines) { Write-Host "  NOT SENT: $line" -ForegroundColor Red; [void]$text.Add("  NOT SENT: $line") }
        $advice = 'Close Excel (and any program using these files), then run this again. Files already sent are not sent twice.'
        Write-Host $advice -ForegroundColor Red; [void]$text.Add($advice)
    }
    else { Write-Host $Headline -ForegroundColor Green }
    foreach ($w in $Warnings) { Write-Host $w -ForegroundColor Red; [void]$text.Add($w) }
    try { Write-EtpTextAtomically -Path (Join-Path $LogFolder 'last-send.txt') -Text (($text -join "`r`n") + "`r`n") } catch { Write-Warning "Could not write last-send.txt: $($_.Exception.Message)" }
}

function Send-EtpShopDailyFiles {
    param([datetime]$Date, [string]$ConfigPath, [bool]$DryRun)

    $config = Read-EtpOneDriveConfig -Path $ConfigPath
    $role = Get-EtpShopRole -Config $config

    $sourceFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'sourceFolder' ''))
    if ([string]::IsNullOrWhiteSpace($sourceFolder) -or -not (Test-Path -LiteralPath $sourceFolder -PathType Container)) {
        throw "Source folder not found: '$sourceFolder'. Set 'sourceFolder' in the config."
    }
    $patterns = @(Get-EtpConfigValue $config 'patterns' @('*.xlsx', '*.csv'))
    $inputsFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'dailyInputsFolder' ''))
    $inputsPattern = [string](Get-EtpConfigValue $config 'dailyInputsPattern' 'ETP-Daily-Inputs*.xlsx')
    $stableSeconds = [int](Get-EtpConfigValue $config 'stableSeconds' 10)
    $checkClient = [bool](Get-EtpConfigValue $config 'checkOneDriveClient' $true)
    $logFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'logFolder' '%LOCALAPPDATA%\EtpOneDrive'))
    $logFile = Join-Path $logFolder 'send.log.jsonl'

    $oneDrive = Resolve-EtpOneDriveRoot -Override ([string](Get-EtpConfigValue $config 'oneDriveRoot' ''))
    foreach ($folder in @($sourceFolder, $inputsFolder)) {
        if (-not [string]::IsNullOrWhiteSpace($folder) -and (Test-EtpPathInside -Path $folder -Folder $oneDrive)) {
            throw "Source folder '$folder' is inside OneDrive. Keep exports in a normal local folder; this script copies them."
        }
    }
    $keyPath = Get-EtpShopSigningKeyPath -Config $config
    $key = $null
    if (-not $DryRun) {
        if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) { throw "This PC has no signing key yet ($keyPath). Run once: Send-ShopDailyFiles.ps1 -NewSigningKey, and copy the key to Workpc by USB." }
        $key = Read-EtpSecretKeyBytes -KeyFile $keyPath -Bytes $script:EtpSigningKeyBytes -What 'signing key'
    }
    $pcName = ConvertTo-EtpPcNameToken $env:COMPUTERNAME
    $warnings = @()
    if ($checkClient -and -not (Test-EtpOneDriveClientRunning)) {
        $warnings += 'OneDrive is NOT running on this PC: the files are in the OneDrive folder but will not reach Workpc until OneDrive is started and signed in.'
        Write-EtpLogLine -LogFile $logFile -Event 'OneDriveNotRunning' -Data @{ role = $role }
    }

    $day = $Date.ToString('yyyy-MM-dd')
    $destination = Join-Path $oneDrive (Join-Path 'ETP-Inbound' (Join-Path $role $day))

    # Candidate files: written on that business day (local time), from the export folder and the daily-inputs folder.
    $candidates = New-Object System.Collections.ArrayList
    foreach ($pattern in $patterns) {
        foreach ($file in @(Get-ChildItem -LiteralPath $sourceFolder -Filter $pattern -File -ErrorAction SilentlyContinue)) { [void]$candidates.Add($file) }
    }
    if (-not [string]::IsNullOrWhiteSpace($inputsFolder) -and (Test-Path -LiteralPath $inputsFolder -PathType Container)) {
        foreach ($file in @(Get-ChildItem -LiteralPath $inputsFolder -Filter $inputsPattern -File -ErrorAction SilentlyContinue)) { [void]$candidates.Add($file) }
    }
    $todays = @($candidates | Where-Object { $_.Name -notlike '~$*' -and $_.LastWriteTime.Date -eq $Date.Date } | Sort-Object FullName -Unique)

    if ($todays.Count -eq 0) {
        Write-EtpSendStatus -LogFolder $logFolder -Headline "No $role files dated $day were found in the source folder(s). Nothing sent." -SkippedLines @() -Warnings $warnings
        Write-EtpLogLine -LogFile $logFile -Event 'NothingToSend' -Data @{ role = $role; day = $day }
        return 0
    }

    # Merge with an earlier manifest for this day so a later partial run never drops files already sent - but ONLY
    # when that manifest carries this PC's own valid signature. Anyone with the shared account can write the OneDrive
    # folder; re-signing their entries would turn this script into a signing machine for forged files.
    $manifestPath = Join-Path $destination 'manifest.json'
    $entries = @{}
    if ((Test-Path -LiteralPath $manifestPath -PathType Leaf) -and -not $DryRun) {
        try {
            $old = Get-Content -Raw -LiteralPath $manifestPath -Encoding UTF8 | ConvertFrom-Json
            if ([string](Get-EtpConfigValue $old 'role' '') -eq $role -and [string](Get-EtpConfigValue $old 'day' '') -eq $day -and (Test-EtpManifestHmac -Manifest $old -Key $key)) {
                foreach ($f in @(Get-EtpConfigValue $old 'files' @())) { $entries[[string]$f.name] = $f }
            }
            else {
                Write-Warning 'The manifest already in OneDrive was not signed by this PC; it is replaced by a new one listing only files this run checked.'
                Write-EtpLogLine -LogFile $logFile -Event 'ForeignManifestReplaced' -Data @{ role = $role; day = $day }
            }
        } catch { Write-Warning "Earlier manifest unreadable and will be rewritten: $($_.Exception.Message)" }
    }

    $skippedLines = New-Object System.Collections.ArrayList
    $copied = 0; $unchanged = 0
    foreach ($file in $todays) {
        $name = ConvertTo-EtpSafeFileName $file.Name
        if (Test-EtpOneDrivePlaceholder -Path $file.FullName) {
            [void]$skippedLines.Add("$($file.Name) (not downloaded from the cloud yet)")
            Write-EtpLogLine -LogFile $logFile -Event 'SkippedPlaceholder' -Data @{ role = $role; day = $day; file = $file.Name }
            continue
        }
        if (-not (Test-EtpFileStable -Path $file.FullName -StableSeconds $stableSeconds)) {
            [void]$skippedLines.Add("$($file.Name) (still open in Excel or another program, or still being written)")
            Write-EtpLogLine -LogFile $logFile -Event 'SkippedInUse' -Data @{ role = $role; day = $day; file = $file.Name }
            continue
        }
        $hash = Get-EtpFileSha256 -Path $file.FullName
        $target = Join-Path $destination $name
        if ((Test-Path -LiteralPath $target -PathType Leaf) -and ((Get-Item -LiteralPath $target).Length -eq $file.Length) -and ((Get-EtpFileSha256 -Path $target) -eq $hash)) {
            $unchanged++
        }
        elseif ($DryRun) {
            Write-Host "WhatIf: would copy $($file.FullName) -> $target ($($file.Length) bytes)"; $copied++; continue
        }
        else {
            if (-not (Test-Path -LiteralPath $destination)) { [void](New-Item -ItemType Directory -Path $destination -Force) }
            $temporary = "$target.$([Guid]::NewGuid().ToString('N')).part"
            Copy-Item -LiteralPath $file.FullName -Destination $temporary -Force
            if ((Get-EtpFileSha256 -Path $temporary) -ne $hash) { Remove-Item -LiteralPath $temporary -Force; throw "Copy of '$($file.Name)' did not verify. Nothing was kept for it." }
            if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
            Move-Item -LiteralPath $temporary -Destination $target
            $copied++
            Write-EtpLogLine -LogFile $logFile -Event 'Copied' -Data @{ role = $role; day = $day; file = $name; bytes = $file.Length; sha256 = $hash }
        }
        $entries[$name] = [pscustomobject]@{ name = $name; sizeBytes = $file.Length; sha256 = $hash }
    }

    if (-not $DryRun -and $entries.Count -gt 0) {
        $manifest = [pscustomobject][ordered]@{
            schemaVersion = $script:EtpManifestSchemaVersion
            role          = $role
            day           = $day
            pcName        = $pcName
            keyId         = (Get-EtpKeyId -KeyBytes $key)
            createdLocal  = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
            files         = @($entries.Values | Sort-Object name | ForEach-Object { [pscustomobject][ordered]@{ name = [string]$_.name; sizeBytes = [int64]$_.sizeBytes; sha256 = ([string]$_.sha256).ToUpperInvariant() } })
        }
        $manifest | Add-Member -NotePropertyName hmac -NotePropertyValue (Get-EtpManifestHmac -Manifest $manifest -Key $key)
        Write-EtpTextAtomically -Path $manifestPath -Text ($manifest | ConvertTo-Json -Depth 5)
    }

    $skipped = $skippedLines.Count
    $sent = $copied + $unchanged
    $verb = if ($DryRun) { 'Would send' } else { 'Sent' }
    $headline = "$role $day : $verb $sent file(s) ($copied new, $unchanged already in OneDrive), skipped $skipped."
    Write-EtpSendStatus -LogFolder $logFolder -Headline $headline -SkippedLines @($skippedLines) -Warnings $warnings
    Write-EtpLogLine -LogFile $logFile -Event 'Done' -Data @{ role = $role; day = $day; sent = $sent; copied = $copied; unchanged = $unchanged; skipped = $skipped; dryRun = $DryRun }
    if ($skipped -gt 0) { return 2 }
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($NewSigningKey -or $ShowSigningKey) {
            $config = Read-EtpOneDriveConfig -Path $ConfigPath
            $role = Get-EtpShopRole -Config $config
            $keyPath = Get-EtpShopSigningKeyPath -Config $config
            if ($NewSigningKey) {
                if ($PSCmdlet.ShouldProcess($keyPath, 'Create this PC''s signing key')) {
                    $fingerprint = New-EtpSecretKeyFile -KeyFile $keyPath -Bytes $script:EtpSigningKeyBytes
                    Write-Host "Signing key created: $keyPath"
                    Write-Host "Fingerprint: $(Format-EtpFingerprint $fingerprint)"
                    Write-Host "PC name sent in each manifest: $(ConvertTo-EtpPcNameToken $env:COMPUTERNAME)"
                    Write-Host "NOW copy the key file by USB to Workpc as  C:\ProgramData\EtpReporting\Operations\shop-keys\$role.key  and put the PC name above"
                    Write-Host "under pcNames.$role in workpc-receive.config.json. Never put the key in OneDrive, e-mail or chat. Wipe it from the USB stick afterwards."
                }
            }
            else {
                $key = Read-EtpSecretKeyBytes -KeyFile $keyPath -Bytes $script:EtpSigningKeyBytes -What 'signing key'
                Write-Host "$role signing key $keyPath"
                Write-Host "Fingerprint: $(Format-EtpFingerprint (Get-EtpKeyFingerprint -KeyBytes $key))"
                Write-Host "PC name sent in each manifest: $(ConvertTo-EtpPcNameToken $env:COMPUTERNAME)"
            }
            exit 0
        }
        exit (Send-EtpShopDailyFiles -Date $Date -ConfigPath $ConfigPath -DryRun ([bool]$WhatIfPreference))
    }
    catch { Write-Error $_.Exception.Message; exit 1 }
}
