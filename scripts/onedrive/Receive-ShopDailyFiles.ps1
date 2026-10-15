<#
.SYNOPSIS
  Runs on Workpc. Reads ETP-Inbound\<ROLE>\<yyyy-MM-dd>\ from the synced OneDrive, checks the shop PC's SIGNED
  manifest.json and every file in it (size + SHA-256), and hands verified files to ETP's automatic import by placing ONE
  zip per role and day in ETP's Inbound watch folder:  <Inbound>\<ROLE>-<yyyy-MM-dd>.zip  (ETP names the extraction
  folder after the zip, so the zip name carries the business day - the same convention as a hand-made Inbound zip).

.DESCRIPTION
  - Proof of origin (AUDIT-2 S-02): the manifest must carry a valid HMAC made with that role's shop signing key
    (<shopKeysFolder>\<ROLE>.key, copied from the shop PC by USB) and the PC name configured for the role (pcNames).
    A manifest that fails either check is quarantined and none of its files are taken.
  - No gap between check and zip (S-06): each file is first copied out of OneDrive into a local staging folder; the
    LOCAL copy is size- and hash-checked and is what goes into the zip and the archive. A file OneDrive replaces after
    the check can never reach Inbound.
  - Bad files (wrong size/hash, not listed in the manifest, not .xlsx/.csv) go to <work>\Quarantine\<ROLE>\<day>\
    with a .reason.txt beside them. Nothing bad reaches Inbound.
  - Good files leave OneDrive; the checked copies are kept in <work>\Archive\<ROLE>\<day>\ after the zip is in Inbound.
  - A file whose SHA-256 was already taken is not imported twice (the shop PC re-running its script is harmless).
  - Roles with no manifest for the report day are reported as MISSING (file <work>\Logs\missing-<day>.txt, exit code 2),
    unless the shop is not live yet for that day: shop-live.json (1.12.1, decision 51) gives each shop's "sendingFrom" day.
    A not-live shop is never reported missing; if it sends anyway its files are still checked and taken.
  - Run it from the scheduled task that Register-WorkpcTasks.ps1 creates (Sagar's account, highest privileges): the
    Inbound folder is protected by ETP setup, and the OneDrive client runs only for Sagar. It refuses to run when
    OneDrive is not running (checkOneDriveClient). It never runs the import itself; ETP's scheduled import picks the
    zip up.
  - Windows PowerShell 5.1 compatible. Handles no passwords.

.PARAMETER ReportDate          The business day that must be complete (default: yesterday). Earlier days inside
                               lookbackDays are still processed if they arrive late.
.PARAMETER ShowKeyFingerprints Print the fingerprint of each role's signing key on Workpc (compare with the shop PC's
                               "Send-ShopDailyFiles.ps1 -ShowSigningKey").
.PARAMETER WhatIf              Verify and report only; moves and writes nothing except the log of the dry run.

Exit codes: 0 all roles received, 2 something missing or quarantined (read the log), 1 error.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [datetime]$ReportDate = (Get-Date).Date.AddDays(-1),
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'workpc-receive.config.json'),
    [switch]$ShowKeyFingerprints
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneDriveCommon.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:ImportableExtensions = @('.xlsx', '.csv')

function Move-EtpToQuarantine {
    param([string]$Source, [string]$Folder, [string]$Reason, [bool]$DryRun)
    if ($DryRun) { return }
    if (-not (Test-Path -LiteralPath $Folder)) { [void](New-Item -ItemType Directory -Path $Folder -Force) }
    $name = Split-Path -Leaf $Source
    $target = Join-Path $Folder $name
    if (Test-Path -LiteralPath $target) { $target = Join-Path $Folder ("{0}-{1}{2}" -f [IO.Path]::GetFileNameWithoutExtension($name), [Guid]::NewGuid().ToString('N').Substring(0, 8), [IO.Path]::GetExtension($name)) }
    Move-Item -LiteralPath $Source -Destination $target
    [IO.File]::WriteAllText("$target.reason.txt", "$Reason`r`nQuarantined (local time): $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))`r`n", (New-Object Text.UTF8Encoding($false)))
}

function Get-EtpReceiveSettings {
    # Parses the config once: roles, folders, the per-role signing keys and the expected PC names (both required).
    param([string]$ConfigPath)
    $config = Read-EtpOneDriveConfig -Path $ConfigPath
    $roles = @(Get-EtpConfigValue $config 'roles' $script:EtpValidRoles | ForEach-Object { ([string]$_).ToUpperInvariant() })
    foreach ($r in $roles) { if ($script:EtpValidRoles -notcontains $r) { throw "Unknown role '$r' in config." } }
    $configuredRoot = [string](Get-EtpConfigValue $config 'oneDriveRoot' '')
    $keysFolder = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'shopKeysFolder' "$env:ProgramData\EtpReporting\Operations\shop-keys"))
    Assert-EtpKeyOutsideOneDrive -KeyFile (Join-Path $keysFolder 'x.key') -ConfiguredRoot $configuredRoot

    $pcNames = @{}
    $configured = Get-EtpConfigValue $config 'pcNames' $null
    if ($null -ne $configured) {
        foreach ($p in $configured.PSObject.Properties) {
            $token = ConvertTo-EtpPcNameToken ([string]$p.Value)
            if ($token) { $pcNames[([string]$p.Name).ToUpperInvariant()] = $token }
        }
    }
    # One PC sending for two roles defeats the per-role keys: refuse such a config outright.
    $seen = @{}
    foreach ($r in $roles) {
        if (-not $pcNames.ContainsKey($r)) { continue }
        if ($seen.ContainsKey($pcNames[$r])) { throw "pcNames gives the same PC ($($pcNames[$r])) for $($seen[$pcNames[$r]]) and $r. Each role must come from its own shop PC." }
        $seen[$pcNames[$r]] = $r
    }
    # 1.12.1 P7 (decision 51): a shop that is not live yet is never reported missing (see Read-EtpShopLive).
    $shopLivePath = Get-EtpShopLivePath -Config $config
    $live = Read-EtpShopLive -Path $shopLivePath
    return [pscustomobject]@{
        Config        = $config
        Roles         = $roles
        ConfiguredRoot = $configuredRoot
        KeysFolder    = $keysFolder
        PcNames       = $pcNames
        ShopLivePath  = $shopLivePath
        Live          = $live
    }
}

function Receive-EtpShopDailyFiles {
    param([datetime]$ReportDate, [string]$ConfigPath, [bool]$DryRun)

    $settings = Get-EtpReceiveSettings -ConfigPath $ConfigPath
    $config = $settings.Config
    $roles = $settings.Roles
    $inbound = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'inboundFolder' "$env:ProgramData\EtpReporting\Inbound"))
    $work = [Environment]::ExpandEnvironmentVariables([string](Get-EtpConfigValue $config 'workFolder' "$env:ProgramData\EtpReporting\OneDriveReceive"))
    $lookback = [int](Get-EtpConfigValue $config 'lookbackDays' 3)
    $stableSeconds = [int](Get-EtpConfigValue $config 'stableSeconds' 10)
    $checkClient = [bool](Get-EtpConfigValue $config 'checkOneDriveClient' $true)
    $oneDrive = Resolve-EtpOneDriveRoot -Override $settings.ConfiguredRoot
    if (-not (Test-Path -LiteralPath $inbound -PathType Container)) { throw "ETP Inbound folder not found: $inbound. Run ETP's operation-folder setup first." }
    if (Test-EtpPathInside -Path $work -Folder $oneDrive) { throw "workFolder '$work' must not be inside OneDrive (quarantined files and the archive stay local)." }

    $logFile = Join-Path $work 'Logs\receive.log.jsonl'
    if ($checkClient) { Assert-EtpOneDriveClientRunning -LogFile $logFile }
    $statePath = Join-Path $work 'State\taken.json'
    $taken = @{}
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        $state = Get-Content -Raw -LiteralPath $statePath -Encoding UTF8 | ConvertFrom-Json
        foreach ($p in $state.PSObject.Properties) { $taken[$p.Name] = $p.Value }
    }

    $reportDay = $ReportDate.ToString('yyyy-MM-dd')
    $problems = New-Object System.Collections.ArrayList
    $received = @{}   # "ROLE|day" -> $true when at least one manifest was handled or already taken

    foreach ($role in $roles) {
        $roleRoot = Join-Path $oneDrive (Join-Path 'ETP-Inbound' $role)
        $keyPath = Join-Path $settings.KeysFolder "$role.key"
        $roleKey = $null
        for ($offset = 0; $offset -le $lookback; $offset++) {
            $day = $ReportDate.AddDays(-$offset).ToString('yyyy-MM-dd')
            $dayFolder = Join-Path $roleRoot $day
            if (-not (Test-Path -LiteralPath $dayFolder -PathType Container)) { continue }
            $manifestPath = Join-Path $dayFolder 'manifest.json'
            if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                if ($offset -eq 0) { [void]$problems.Add("$role $day : files are in OneDrive but there is no manifest yet (shop PC still sending or OneDrive still syncing).") }
                continue
            }
            if (Test-EtpOneDrivePlaceholder -Path $manifestPath) { [void]$problems.Add("$role $day : manifest not downloaded from the cloud yet."); continue }

            # Workpc set-up problems leave everything in OneDrive (nothing is wrong with the files themselves).
            if (-not $settings.PcNames.ContainsKey($role)) {
                [void]$problems.Add("$role $day : no PC name configured for $role (pcNames in $ConfigPath). Files left in OneDrive.")
                continue
            }
            if ($null -eq $roleKey) {
                if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
                    [void]$problems.Add("$role $day : Workpc has no signing key for $role ($keyPath). Copy it from the shop PC by USB. Files left in OneDrive.")
                    continue
                }
                $roleKey = Read-EtpSecretKeyBytes -KeyFile $keyPath -Bytes $script:EtpSigningKeyBytes -What "$role signing key"
            }

            $quarantineFolder = Join-Path $work (Join-Path 'Quarantine' (Join-Path $role $day))
            $archiveFolder = Join-Path $work (Join-Path 'Archive' (Join-Path $role $day))
            try { $manifest = Get-Content -Raw -LiteralPath $manifestPath -Encoding UTF8 | ConvertFrom-Json }
            catch {
                [void]$problems.Add("$role $day : manifest unreadable ($($_.Exception.Message)). Files left in OneDrive.")
                continue
            }
            if ([string](Get-EtpConfigValue $manifest 'role' '') -ne $role -or [string](Get-EtpConfigValue $manifest 'day' '') -ne $day) {
                [void]$problems.Add("$role $day : manifest does not match its folder (role/day). Files left in OneDrive.")
                continue
            }

            # Signature, then sender. Either failing quarantines the manifest; its files stay until a valid one arrives.
            $reason = $null
            if ([int](Get-EtpConfigValue $manifest 'schemaVersion' 0) -ne $script:EtpManifestSchemaVersion) {
                $reason = "Unsigned or old manifest (schemaVersion $(Get-EtpConfigValue $manifest 'schemaVersion' 'none')). Update the shop PC's scripts to 1.11.2 or later and send again."
            }
            elseif (-not (Test-EtpManifestHmac -Manifest $manifest -Key $roleKey)) {
                $claimedKey = [string](Get-EtpConfigValue $manifest 'keyId' '')
                if ($claimedKey -and $claimedKey -ne (Get-EtpKeyId -KeyBytes $roleKey)) { $reason = "Signature does not verify: the manifest was signed with a different key (key id $claimedKey, Workpc has $(Get-EtpKeyId -KeyBytes $roleKey) for $role)." }
                else { $reason = 'Signature does not verify: the manifest was changed after the shop PC signed it, or was not written by the shop PC.' }
            }
            else {
                $sender = ConvertTo-EtpPcNameToken ([string](Get-EtpConfigValue $manifest 'pcName' ''))
                if ($sender -ne $settings.PcNames[$role]) { $reason = "Sent by PC '$sender', but $role is configured to come from '$($settings.PcNames[$role])'." }
            }
            if ($reason) {
                Move-EtpToQuarantine -Source $manifestPath -Folder $quarantineFolder -Reason $reason -DryRun $DryRun
                [void]$problems.Add("$role $day : manifest refused - $reason Files left in OneDrive.")
                Write-EtpLogLine -LogFile $logFile -Event 'ManifestRefused' -Data @{ role = $role; day = $day; reason = $reason; dryRun = $DryRun }
                continue
            }

            $dayStaging = Join-Path $work (Join-Path 'Staging' ([Guid]::NewGuid().ToString('N')))
            try {
                $good = New-Object System.Collections.ArrayList
                $alreadyTaken = New-Object System.Collections.ArrayList
                $listed = @{}
                foreach ($entry in @(Get-EtpConfigValue $manifest 'files' @())) {
                    $name = ConvertTo-EtpSafeFileName ([string]$entry.name)
                    if ($listed.ContainsKey($name.ToLowerInvariant())) { continue }   # a name listed twice is checked once
                    $listed[$name.ToLowerInvariant()] = $true
                    $path = Join-Path $dayFolder $name
                    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                        [void]$problems.Add("$role $day : '$name' is in the manifest but not in OneDrive yet (may still be syncing).")
                        Write-EtpLogLine -LogFile $logFile -Event 'MissingFile' -Data @{ role = $role; day = $day; file = $name }
                        continue
                    }
                    if (Test-EtpOneDrivePlaceholder -Path $path) { [void]$problems.Add("$role $day : '$name' not downloaded from the cloud yet."); continue }
                    if (-not (Test-EtpFileStable -Path $path -StableSeconds $stableSeconds)) { [void]$problems.Add("$role $day : '$name' is still syncing."); continue }
                    $extension = [IO.Path]::GetExtension($name).ToLowerInvariant()

                    # Hash the LOCAL copy (S-06). A dry run writes nothing, so it hashes in place.
                    $checked = $path
                    if (-not $DryRun -and $script:ImportableExtensions -contains $extension) {
                        if (-not (Test-Path -LiteralPath $dayStaging)) { [void](New-Item -ItemType Directory -Path $dayStaging -Force) }
                        $checked = Join-Path $dayStaging $name
                        Copy-Item -LiteralPath $path -Destination $checked -Force
                    }
                    $size = (Get-Item -LiteralPath $checked).Length
                    $actualHash = Get-EtpFileSha256 -Path $checked
                    $reason = $null
                    if ($script:ImportableExtensions -notcontains $extension) { $reason = "File type '$extension' is not importable by ETP (only .xlsx and .csv)." }
                    elseif ($size -ne [int64]$entry.sizeBytes) { $reason = "Size differs from the manifest (manifest $($entry.sizeBytes), found $size)." }
                    elseif ($actualHash -ne ([string]$entry.sha256).ToUpperInvariant()) { $reason = 'SHA-256 differs from the manifest; the file changed or was damaged in transit.' }
                    if ($reason) {
                        if ($checked -ne $path) { Remove-Item -LiteralPath $checked -Force }
                        Move-EtpToQuarantine -Source $path -Folder $quarantineFolder -Reason $reason -DryRun $DryRun
                        [void]$problems.Add("$role $day : '$name' quarantined - $reason")
                        Write-EtpLogLine -LogFile $logFile -Event 'Quarantined' -Data @{ role = $role; day = $day; file = $name; reason = $reason; dryRun = $DryRun }
                        continue
                    }
                    if ($taken.ContainsKey($actualHash)) { [void]$alreadyTaken.Add($path); continue }
                    [void]$good.Add([pscustomobject]@{ Name = $name; Path = $path; Checked = $checked; Hash = $actualHash; Size = $size })
                }

                # Files in the folder that the manifest does not list are not trusted.
                foreach ($stray in @(Get-ChildItem -LiteralPath $dayFolder -File -Force | Where-Object { $_.Name -ne 'manifest.json' -and $_.Name -notlike '*.part' -and $_.Name -notlike '*.tmp' -and -not $listed.ContainsKey($_.Name.ToLowerInvariant()) })) {
                    Move-EtpToQuarantine -Source $stray.FullName -Folder $quarantineFolder -Reason 'Not listed in the manifest.' -DryRun $DryRun
                    [void]$problems.Add("$role $day : '$($stray.Name)' quarantined - not listed in the manifest.")
                    Write-EtpLogLine -LogFile $logFile -Event 'Quarantined' -Data @{ role = $role; day = $day; file = $stray.Name; reason = 'Not listed in manifest'; dryRun = $DryRun }
                }

                if ($good.Count -gt 0) {
                    $sequence = 1 + @($taken.Values | Where-Object { $_.role -eq $role -and $_.day -eq $day } | ForEach-Object { $_.zip } | Sort-Object -Unique).Count
                    $zipName = if ($sequence -eq 1) { "$role-$day.zip" } else { "$role-$day-r$sequence.zip" }
                    if ($DryRun) {
                        Write-Host "WhatIf: would place $zipName ($($good.Count) file(s)) in $inbound"
                    }
                    else {
                        $staging = Join-Path $work 'Staging'
                        if (-not (Test-Path -LiteralPath $staging)) { [void](New-Item -ItemType Directory -Path $staging -Force) }
                        $stagedZip = Join-Path $staging ("{0}.{1}.tmp" -f $zipName, [Guid]::NewGuid().ToString('N'))
                        $archive = [IO.Compression.ZipFile]::Open($stagedZip, [IO.Compression.ZipArchiveMode]::Create)
                        # The zip is built from the checked LOCAL copies, never from the OneDrive path.
                        try { foreach ($g in $good) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $g.Checked, $g.Name, [IO.Compression.CompressionLevel]::Optimal) } }
                        finally { $archive.Dispose() }
                        $finalZip = Join-Path $inbound $zipName
                        if (Test-Path -LiteralPath $finalZip) { Remove-Item -LiteralPath $stagedZip -Force; throw "$zipName already exists in Inbound (ETP has not imported it yet). Run again after ETP's import has run." }
                        # Same-volume rename: ETP never sees a half-written zip. The temp name has no .zip extension.
                        try { Move-Item -LiteralPath $stagedZip -Destination $finalZip }
                        catch [UnauthorizedAccessException] { throw "Cannot write to '$inbound'. Run this from the task Register-WorkpcTasks.ps1 creates (highest privileges), or as an administrator." }

                        if (-not (Test-Path -LiteralPath $archiveFolder)) { [void](New-Item -ItemType Directory -Path $archiveFolder -Force) }
                        foreach ($g in $good) {
                            $taken[$g.Hash] = [pscustomobject]@{ role = $role; day = $day; file = $g.Name; zip = $zipName; takenLocal = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') }
                            Write-EtpLogLine -LogFile $logFile -Event 'Taken' -Data @{ role = $role; day = $day; file = $g.Name; bytes = $g.Size; sha256 = $g.Hash; zip = $zipName }
                            Move-Item -LiteralPath $g.Checked -Destination (Join-Path $archiveFolder $g.Name) -Force
                            Remove-Item -LiteralPath $g.Path -Force
                        }
                        Write-EtpTextAtomically -Path $statePath -Text ($taken | ConvertTo-Json -Depth 4)
                        Write-Host "$role $day : $($good.Count) file(s) -> $zipName"
                    }
                }
                if (-not $DryRun) {
                    # Already-taken re-sends are simply cleared out of OneDrive (the archive already has them).
                    foreach ($dup in $alreadyTaken) { Remove-Item -LiteralPath $dup -Force; Write-EtpLogLine -LogFile $logFile -Event 'AlreadyTaken' -Data @{ role = $role; day = $day; file = (Split-Path -Leaf $dup) } }
                }
                if ($good.Count -gt 0 -or $alreadyTaken.Count -gt 0) { $received["$role|$day"] = $true }
                if (-not $DryRun) {
                    $left = @(Get-ChildItem -LiteralPath $dayFolder -File -Force | Where-Object { $_.Name -ne 'manifest.json' })
                    if ($left.Count -eq 0) {
                        if (-not (Test-Path -LiteralPath $archiveFolder)) { [void](New-Item -ItemType Directory -Path $archiveFolder -Force) }
                        Move-Item -LiteralPath $manifestPath -Destination (Join-Path $archiveFolder ("manifest-{0}.json" -f (Get-Date).ToString('yyyyMMdd-HHmmss'))) -Force
                        Remove-Item -LiteralPath $dayFolder -Force
                    }
                }
            }
            finally {
                if (Test-Path -LiteralPath $dayStaging) { Remove-Item -LiteralPath $dayStaging -Recurse -Force -ErrorAction SilentlyContinue }
            }
        }
    }

    # Missing-roles report for the report day: nothing received for it, today or on an earlier run.
    $missing = @()
    $notYet = @()
    foreach ($role in $roles) {
        if (-not (Test-EtpRoleExpected -Live $settings.Live -Role $role -Day $ReportDate.Date)) { $notYet += $role; continue }
        $has = $received.ContainsKey("$role|$reportDay") -or (@($taken.Values | Where-Object { $_.role -eq $role -and $_.day -eq $reportDay }).Count -gt 0)
        if (-not $has) { $missing += $role }
    }
    if ($missing.Count -gt 0) {
        $line = "MISSING for ${reportDay}: $($missing -join ', ') - nothing received from the shop PC(s)."
        [void]$problems.Add($line)
        Write-EtpLogLine -LogFile $logFile -Event 'MissingRoles' -Data @{ day = $reportDay; roles = ($missing -join ',') }
    }
    if (-not $DryRun) {
        $summary = Join-Path $work ("Logs\missing-{0}.txt" -f $reportDay)
        if ($problems.Count -gt 0) { Write-EtpTextAtomically -Path $summary -Text (($problems -join "`r`n") + "`r`n") }
        elseif (Test-Path -LiteralPath $summary) { Remove-Item -LiteralPath $summary -Force }
    }
    foreach ($p in $problems) { Write-Warning $p }
    if ($notYet.Count -gt 0) { Write-Host "Not expected yet for ${reportDay} (not live, see $($settings.ShopLivePath)): $($notYet -join ', ')." }
    if ($problems.Count -eq 0) { Write-Host "All roles received for $reportDay." }
    if ($problems.Count -gt 0) { return 2 }
    return 0
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        if ($ShowKeyFingerprints) {
            $settings = Get-EtpReceiveSettings -ConfigPath $ConfigPath
            foreach ($role in $settings.Roles) {
                $keyPath = Join-Path $settings.KeysFolder "$role.key"
                $pc = if ($settings.PcNames.ContainsKey($role)) { $settings.PcNames[$role] } else { '(not set in pcNames)' }
                if (Test-Path -LiteralPath $keyPath -PathType Leaf) {
                    $key = Read-EtpSecretKeyBytes -KeyFile $keyPath -Bytes $script:EtpSigningKeyBytes -What "$role signing key"
                    Write-Host ("{0,-8} PC {1}  fingerprint {2}" -f $role, $pc, (Format-EtpFingerprint (Get-EtpKeyFingerprint -KeyBytes $key)))
                }
                else { Write-Host ("{0,-8} PC {1}  NO KEY at {2}" -f $role, $pc, $keyPath) }
            }
            exit 0
        }
        exit (Receive-EtpShopDailyFiles -ReportDate $ReportDate -ConfigPath $ConfigPath -DryRun ([bool]$WhatIfPreference))
    }
    catch { Write-Error $_.Exception.Message; exit 1 }
}
