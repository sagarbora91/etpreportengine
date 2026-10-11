<#
.SYNOPSIS
  Runs on each shop PC. Copies the day's ETP export files and the staff daily-inputs workbook into this PC's
  OneDrive folder  ETP-Inbound\<ROLE>\<yyyy-MM-dd>\  and writes manifest.json (written last = "this day is complete").

.DESCRIPTION
  - Source files are only read, never changed or deleted.
  - Files still being written (size/time still changing, or locked) are skipped; run again later (safe).
  - Safe to run any number of times for the same day: identical files are not copied again.
  - Config: shop-daily-files.config.json next to this script (copy shop-daily-files.config.example.json).
  - Windows PowerShell 5.1 compatible. Handles no passwords.

.PARAMETER Date     The business day to send (default: today).
.PARAMETER ConfigPath  Override the config location.
.PARAMETER WhatIf   Show what would be copied; copies nothing.

Exit codes: 0 done (or nothing for that day), 2 done but some files were skipped (in use / not stable), 1 error.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [datetime]$Date = (Get-Date).Date,
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'shop-daily-files.config.json')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneDriveCommon.ps1')

function Send-EtpShopDailyFiles {
    param([datetime]$Date, [string]$ConfigPath, [bool]$DryRun)

    $config = Read-EtpOneDriveConfig -Path $ConfigPath
    $role = ([string](Get-EtpConfigValue $config 'role' '')).ToUpperInvariant()
    if ($script:EtpValidRoles -notcontains $role) { throw "Config 'role' must be one of: $($script:EtpValidRoles -join ', ')." }

    $sourceFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'sourceFolder' ''))
    if ([string]::IsNullOrWhiteSpace($sourceFolder) -or -not (Test-Path -LiteralPath $sourceFolder -PathType Container)) {
        throw "Source folder not found: '$sourceFolder'. Set 'sourceFolder' in the config."
    }
    $patterns = @(Get-EtpConfigValue $config 'patterns' @('*.xlsx', '*.csv'))
    $inputsFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'dailyInputsFolder' ''))
    $inputsPattern = [string](Get-EtpConfigValue $config 'dailyInputsPattern' 'ETP-Daily-Inputs*.xlsx')
    $stableSeconds = [int](Get-EtpConfigValue $config 'stableSeconds' 10)
    $logFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'logFolder' '%LOCALAPPDATA%\EtpOneDrive'))
    $logFile = Join-Path $logFolder 'send.log.jsonl'

    $oneDrive = Resolve-EtpOneDriveRoot -Override ([string](Get-EtpConfigValue $config 'oneDriveRoot' ''))
    foreach ($folder in @($sourceFolder, $inputsFolder)) {
        if (-not [string]::IsNullOrWhiteSpace($folder) -and (Test-EtpPathInside -Path $folder -Folder $oneDrive)) {
            throw "Source folder '$folder' is inside OneDrive. Keep exports in a normal local folder; this script copies them."
        }
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
        Write-Host "No $role files dated $day were found in the source folder(s). Nothing sent."
        Write-EtpLogLine -LogFile $logFile -Event 'NothingToSend' -Data @{ role = $role; day = $day }
        return 0
    }

    # Merge with an earlier manifest for this day so a later partial run never drops files already sent.
    $manifestPath = Join-Path $destination 'manifest.json'
    $entries = @{}
    if ((Test-Path -LiteralPath $manifestPath -PathType Leaf) -and -not $DryRun) {
        try {
            $old = Get-Content -Raw -LiteralPath $manifestPath -Encoding UTF8 | ConvertFrom-Json
            foreach ($f in @($old.files)) { $entries[[string]$f.name] = $f }
        } catch { Write-Warning "Earlier manifest unreadable and will be rewritten: $($_.Exception.Message)" }
    }

    $skipped = 0; $copied = 0; $unchanged = 0
    foreach ($file in $todays) {
        $name = ConvertTo-EtpSafeFileName $file.Name
        if (Test-EtpOneDrivePlaceholder -Path $file.FullName) {
            Write-Warning "Skipped (not downloaded from the cloud yet): $($file.Name)"; $skipped++; continue
        }
        if (-not (Test-EtpFileStable -Path $file.FullName -StableSeconds $stableSeconds)) {
            Write-Warning "Skipped (still being written or open in another program): $($file.Name). Close it and run this again."
            Write-EtpLogLine -LogFile $logFile -Event 'SkippedInUse' -Data @{ role = $role; day = $day; file = $file.Name }
            $skipped++; continue
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
        $manifest = [ordered]@{
            schemaVersion = 1
            role          = $role
            day           = $day
            pcName        = $env:COMPUTERNAME
            createdLocal  = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
            files         = @($entries.Values | Sort-Object name | ForEach-Object { [ordered]@{ name = $_.name; sizeBytes = [int64]$_.sizeBytes; sha256 = $_.sha256 } })
        }
        Write-EtpTextAtomically -Path $manifestPath -Text ($manifest | ConvertTo-Json -Depth 5)
    }

    Write-Host "$role $day : $copied copied, $unchanged already there, $skipped skipped."
    Write-EtpLogLine -LogFile $logFile -Event 'Done' -Data @{ role = $role; day = $day; copied = $copied; unchanged = $unchanged; skipped = $skipped; dryRun = $DryRun }
    if ($skipped -gt 0) { return 2 }
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    try { exit (Send-EtpShopDailyFiles -Date $Date -ConfigPath $ConfigPath -DryRun ([bool]$WhatIfPreference)) }
    catch { Write-Error $_.Exception.Message; exit 1 }
}
