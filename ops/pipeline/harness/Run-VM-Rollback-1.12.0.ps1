<#
Run-VM-Rollback-1.12.0.ps1

G16 for the 1.12.0 go-live (decision 63, 13 Oct 2026: Workpc goes from 1.9.6 straight to the complete 1.12.0): rehearse the go-live
rollback on the Hyper-V VM "ETP-Acceptance-186": 1.12.0 (69 migrations, latest 0069_import_source_locations) back to 1.9.6
(48 migrations) with the PRE_MIGRATION backup the 1.12.0 setup took when it upgraded the 1.9.6 VM.
Written 13 Oct 2026. NOT RUN YET (parse-checked only). Plain-English notes: Reference\Work in progress 2026-10-13\VM-ROLLBACK-REHEARSAL-1.12.0-NOTES.md.
Launcher (asks YES first): E:\ETP\Migration 2026-10-04\Run-VM-Rollback-1.12.0-Sequence.ps1.

New file, copied from Run-VM-Rollback-1.11.4.ps1 (unchanged, still valid for 1.11.4). Helpers are copied from the acceptance harness, which
is not edited or dot-sourced (it runs its own main on load).

Run it ELEVATED (Windows PowerShell 5.1) on Workpc. Sagar types the ETPTest password at the prompt (never stored) and YES before every step
that changes the guest. -DryRun (alias -WhatIf) prints the plan and the checks and changes nothing (no elevation needed).

Needs first: the 1.12.0 acceptance upgrade from the 1.9.6 VM, i.e. Run-VM-1.12.0-Sequence.ps1 -Path From196. Its stage 1 restores
'VM196 delta*', runs the 1.12.0 setup (which takes the PRE_MIGRATION backup) and ends with the checkpoint
"VM195 Upgrade after automated steps, <yyyy-MM-dd HH:mm>" (Run-VM-Acceptance-1.12.0.ps1, Invoke-InstallStage). The 1.11.4 checkpoint of
13 Oct 13:25 has the same prefix, so the start checkpoint is the newest one that matches -StartCheckpoint, does NOT match
-ExcludeCheckpoint, and whose parent checkpoint matches -StartCheckpointParent ('VM196 delta*'). Step 3 then proves it: app 1.12.0, 69 migrations.

Order (each "YES" is a prompt; the run stops at the first failed step with a plain message):
  0  Host checks: elevated, heavy-job lock, 1.9.6 installer SHA-256 = SHA256SUMS.txt, the checkpoints exist, memory and disk.
  1  Safety checkpoint of the VM as it is now ("VM195 before 1.12.0 rollback rehearsal, <stamp>").
  2  YES: restore 'VM196 delta*' (1.9.6, 48 migrations, the state the 1.12.0 upgrade started from) and READ the 1.9.6 baseline:
     DSR VALUE / VOL / INVOICE (1.9.6 rule), users, migration journal, row counts, and the list of backups already there. Read-only.
  3  YES: restore the 1.12.0 start checkpoint (above). Read the same figures (1.9.6 rule and 1.12.0 rule) and the 1.12.0 leftovers
     (tasks, ProgramData folders, settings.json). Find the PRE_MIGRATION backup the 1.12.0 setup took: the one PRE_MIGRATION receipt that
     was NOT in the baseline's backup list. Check the receipt (purpose, database, verified, SHA-256, size; its row counts = the baseline),
     copy both to C:\ETP-Restore (the restore helper refuses a backup inside ProgramData\EtpReporting), copy the 1.9.6 installer in.
  -- ROLLBACK CLOCK STARTS --
  4  Disable (and stop) every ETP* task (incl. the 1.12 ones: shop results, disk copy); wait until ETP is closed.
  5  YES: rename EtpReporting -> EtpReporting_Failed_<yyyyMMdd> (never dropped) AND its files (restore THROW 51903 otherwise).
  6  YES: restore-etp-database.ps1 (the installed 1.12.0 copy; byte-identical to 1.9.6's, as are the broker scripts) with -BackupPath and
     -ReceiptPath. Its "Next: run the ETP setup again" (21 waiting updates, 0049-0069) is ignored on purpose.
  7  YES: uninstall 1.12.0 (silent; remove-etp-scheduled-tasks.ps1 removes the ETP tasks whose action points into the app folder:
     the 3 ETP Reporting tasks, the OneDrive tasks, 'ETP Publish Shop Results', 'ETP Show Shop Results', 'ETP Copy Backup To Disk').
  8  YES: install 1.9.6 (tasks sqlprerequisites,desktopicon; no newdatabase), as the harness does.
  9  Checks: 1.9.6 installed, 48 migrations with latest 0048_service_centre_interim, same journal as the baseline, no 1.12 schema left in
     EtpReporting (schema_objects, update/Tally columns, no-sale days, import locations, unit_kind), DSR = baseline, users and roles =
     baseline, fact-table counts = baseline and receipt, the 3 tasks back and enabled, no other ETP task left enabled, EtpReporting_Failed_*
     still there and ONLINE with 69 migrations. 1.12 ProgramData folders and settings.json: recorded (kept on purpose, harmless to 1.9.6).
  -- ROLLBACK CLOCK STOPS -- (minutes in SUMMARY.md)
  10 Checkpoint "VM195 Rollback 1.12.0 rehearsal done, <stamp>" (or "... STOPPED at <step>, <stamp>" after a failure).
  11 Optional, -NegativeCheck, only after a PASS: YES: restore the 1.12.0 start checkpoint again and run the 1.9.6 setup straight over
     the 69-migration database. Expected: setup fails, journal still 69, rows unchanged, "upgraded by a newer release". Then back to "done".

Why the baseline comes from 'VM196 delta': decision 41 (1.11.2+, kept in 1.12.0) leaves same-day-cancelled bills out of INVOICE, so 1.12.0
and 1.9.6 may differ by design. The comparison that proves the rollback is 1.9.6-before-upgrade vs 1.9.6-after-rollback, same rule both
times, with the SQL of OperationalReportRepository.DsrFactsSql (1.9.6 = commit 7687873; the newer rule adds the v_sales_cancelled_same_day
join, a database\objects file in 1.12.0). The VM's sales end 28 Sep (HEMW) / 29 Sep (WLMHW), so the script also compares each store's last
sales day and August 2026 (WLMHW INVOICE 254 under 1.9.6, 253 under the newer rule).

Evidence: E:\ETP\Reference\VM195-<yyyyMMdd-HHmm>-Rollback112\ (SUMMARY.md, checks.csv, timings.csv, dsr-figures.csv, facts-*.json,
setup and restore logs, run-*.log). Store-level DSR totals, counts, account names, file names, hashes. No customer names or phones.
Exit codes: 0 = passed (maybe with REVIEW items), 1 = failed or stopped, 75 = heavy-job lock busy.
#>
[CmdletBinding()]
param(
    # Print the plan and the checks only; nothing is changed, no password is asked.
    [Alias('WhatIf')][switch]$DryRun,
    # Optional step 11 (see above).
    [switch]$NegativeCheck,
    # The 1.12.0 start state: the checkpoint the 1.12.0 harness takes after the upgrade from the 1.9.6 VM (-Path From196).
    [string]$StartCheckpoint = 'VM195 Upgrade after automated steps, *',
    # Must NOT match: the 1.11.4 checkpoint with the same prefix. '' = no exclusion.
    [string]$ExcludeCheckpoint = 'VM195 Upgrade after automated steps, 2026-10-13 13:25*',
    # The start checkpoint must have been taken on top of this one (Hyper-V parent). '' = any parent.
    [string]$StartCheckpointParent = 'VM196 delta*',
    [string]$BaselineCheckpoint = 'VM196 delta*',
    # Skip step 2 (the baseline restore). The 1.9.6-rule figures read from the 1.12.0 database in step 3 then become the baseline, and the
    # PRE_MIGRATION backup must then be the only PRE_MIGRATION backup matching -PreMigrationPattern (pass the exact file name if not).
    [switch]$SkipBaseline,
    # Skip the safety checkpoint of the VM's current state (only if it holds nothing worth keeping).
    [switch]$NoSafetyCheckpoint,
    [string]$Installer196Path = 'E:\ETP\Installers\ETP Reporting Engine 1.9.6\EtpReportingEngine-Setup-1.9.6-x64.exe',
    # The PRE_MIGRATION backups in the guest's C:\ProgramData\EtpReporting\Backups (backup-etp-database.ps1 names them
    # EtpReporting-<UTC yyyyMMdd-HHmmss>-<guid>.bak). The one taken by the 1.12.0 setup is the PRE_MIGRATION one not in the baseline.
    [string]$PreMigrationPattern = 'EtpReporting-*.bak',
    # Go on although the receipt's row counts differ from the 1.9.6 baseline (only if you know why).
    [switch]$AllowReceiptBaselineDifference,
    # The release being rolled back. Wave 2 adds 0067-0069; if the final 1.12.0 ends elsewhere, pass the count and the latest name.
    [string]$NewVersion = '1.12.0',
    [ValidateRange(49, 200)][int]$NewMigrationCount = 69,
    [string]$NewLatestMigration = '0069_import_source_locations',
    [string]$DsrDate = '2026-10-02',
    [ValidateRange(10, 240)][int]$SetupTimeoutMinutes = 90,
    [ValidateRange(5, 120)][int]$RestoreTimeoutMinutes = 45,
    [ValidateRange(0, 240)][int]$LockWaitMinutes = 30
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- constants
$VmName = 'ETP-Acceptance-186'
$GuestComputer = 'DESKTOP-IPT0J6H'
$GuestUser = 'DESKTOP-IPT0J6H\ETPTest'
$ReferenceRoot = 'E:\ETP\Reference'
$GuestStageDir = 'C:\EtpStaging'
$GuestRestoreDir = 'C:\ETP-Restore'
$GuestEvidenceRoot = 'C:\EtpEvidence\VM195'
$Database = 'EtpReporting'
$FailedBase = 'EtpReporting_Failed_' + (Get-Date -Format 'yyyyMMdd')
$OldVersion = '1.9.6'
$OldMigrationCount = 48
$OldLatestMigration = '0048_service_centre_interim'
$Stores = @('WLMHW', 'HEMW')
$SetupTasks196 = 'sqlprerequisites,desktopicon'   # as the harness upgrades; no newdatabase: the database is restored first
$ExpectedTasks = @('ETP Reporting Daily Backup', 'ETP Reporting Monthly Recovery Drill', 'ETP Reporting Automated Operations')
# remove-etp-scheduled-tasks.ps1 of 1.12.0 (approved list): removed by the 1.12.0 uninstall when their action points into the app folder.
$Tasks112 = @('ETP Send Shop Daily Files', 'ETP Receive Shop Daily Files', 'ETP Copy Backup To OneDrive', 'ETP Publish Shop Results',
    'ETP Show Shop Results', 'ETP Copy Backup To Disk')
# Tables whose counts must not move between the 1.9.6 baseline and the rolled-back database.
$FactTables = @('dbo.sales_invoices', 'dbo.sales_lines', 'dbo.sales_tenders', 'dbo.import_files', 'dbo.daily_reporting_days',
    'dbo.stock_snapshots', 'dbo.stock_movements', 'dbo.source_lineage', 'dbo.manual_operational_inputs')
# Data checks on the VM copy (GO-LIVE-CHECKLIST.md section 1): WLMHW August INVOICE 253 under rule 41, 254 before it.
$AugustWlmhwInvoices196 = 254
$AugustWlmhwInvoicesNew = 253

$RunId = Get-Date -Format 'yyyyMMdd-HHmmss'
$Stamp = Get-Date -Format 'yyyy-MM-dd HH:mm'
$DoneCheckpointPrefix = 'VM195 Rollback 1.12.0 rehearsal done'
$script:Checks = New-Object System.Collections.Generic.List[object]
$script:CheckNo = 0
$script:Timings = New-Object System.Collections.Generic.List[object]
$script:Notes = New-Object System.Collections.Generic.List[string]
$script:RunInfo = [ordered]@{}
$script:Dsr = New-Object System.Collections.Generic.List[object]
$script:Session = $null
$script:Credential = $null
$script:Out = $null
$script:GuestRunDir = "$GuestEvidenceRoot\$RunId-Rollback112"
$script:InstallerHash = $null
$script:LockBusy = $false
$script:Step = 'host checks'
$script:GuestChanged = $false      # true from the rename on: a failure then gets a STOPPED checkpoint
$script:FinalCheckpoint = ''
$script:RollbackClock = $null
$script:RollbackMinutes = $null
$script:FailedName = ''
$script:Periods = @()
$script:Baseline = $null
$script:BaselineSource = ''
$script:PreMigration = $null
$script:GuestInstaller = ''
$script:StartFacts = $null

# ---------------------------------------------------------------- host helpers (copied from Run-VM-Acceptance-1.11.4.ps1 via Run-VM-Rollback-1.11.4.ps1)
function Write-Step([string]$Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }

function Write-Utf8([string]$Path, [string]$Text) {
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-Result($Ok, [string]$Otherwise = 'FAIL') { if ($Ok -eq $true) { return 'PASS' } return $Otherwise }

function Add-Check {
    param([string]$Area, [string]$Check, [string]$Expected, [string]$Actual, [ValidateSet('PASS', 'FAIL', 'REVIEW', 'INFO')][string]$Result)
    $script:CheckNo++
    $script:Checks.Add([pscustomobject]@{ No = $script:CheckNo; Area = $Area; Check = $Check; Expected = $Expected; Actual = $Actual; Result = $Result })
    $color = 'Gray'
    if ($Result -eq 'PASS') { $color = 'Green' } elseif ($Result -eq 'FAIL') { $color = 'Red' } elseif ($Result -eq 'REVIEW') { $color = 'Yellow' }
    Write-Host ("  [{0}] {1}: {2}" -f $Result, $Check, $Actual) -ForegroundColor $color
}

# A check that must pass for the next step to make sense: FAIL stops the run with a plain message.
function Assert-Check {
    param([string]$Area, [string]$Check, [string]$Expected, [string]$Actual, $Ok, [string]$StopMessage)
    Add-Check $Area $Check $Expected $Actual (Get-Result $Ok)
    if ($Ok -ne $true) { throw $StopMessage }
}

function Save-Json([string]$Name, $Object) { Write-Utf8 (Join-Path $script:Out $Name) ($Object | ConvertTo-Json -Depth 10) }

function Save-Csv([string]$Name, $Rows) {
    $path = Join-Path $script:Out $Name
    $list = @($Rows | Where-Object { $null -ne $_ })
    if ($list.Count -gt 0) { $list | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding UTF8 } else { Write-Utf8 $path '' }
}

function Format-Cell($Value) { return ((([string]$Value) -replace '\|', '\|') -replace "`r?`n", ' ') }

function Get-LogMessage([string]$Line) { return ($Line -replace '^\d{4}-\d{2}-\d{2}T\S+\s+', '') }

function ConvertFrom-RoundTrip([string]$Text) {
    return [datetime]::Parse($Text, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}

function Confirm-Yes([string]$What) {
    Write-Host ''
    Write-Host "ABOUT TO: $What" -ForegroundColor Yellow
    $answer = Read-Host 'Type YES (capitals) to go on; anything else stops here'
    if ([string]$answer -cne 'YES') { throw "Stopped by Sagar before: $What. Nothing more was changed." }
}

function Start-Timing([string]$Name) {
    $script:Step = $Name
    $t = [pscustomobject]@{ Step = $Name; Started = (Get-Date).ToString('HH:mm:ss'); Ended = ''; Minutes = ''; InRollbackClock = [bool]$script:RollbackClock; Watch = [Diagnostics.Stopwatch]::StartNew() }
    $script:Timings.Add($t)
    Write-Step $Name
    return $t
}

function Stop-Timing($T) {
    if (-not $T -or $T.Ended) { return }
    $T.Watch.Stop()
    $T.Ended = (Get-Date).ToString('HH:mm:ss')
    $T.Minutes = [math]::Round($T.Watch.Elapsed.TotalMinutes, 1)
    Write-Host "  ($($T.Minutes) min)" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- Hyper-V and session helpers (copied)
function Wait-Heartbeat([int]$Minutes = 10) {
    $deadline = (Get-Date).AddMinutes($Minutes)
    while ((Get-VMIntegrationService -VMName $VmName -Name 'Heartbeat').PrimaryStatusDescription -ne 'OK') {
        if ((Get-Date) -gt $deadline) { throw "The VM did not report a heartbeat in $Minutes minutes. Open the VM console (vmconnect.exe localhost $VmName) and look." }
        Start-Sleep -Seconds 10
    }
}

function Connect-Guest {
    if ($script:Session) { Remove-PSSession -Session $script:Session -ErrorAction SilentlyContinue; $script:Session = $null }
    $deadline = (Get-Date).AddMinutes(10)
    while (-not $script:Session) {
        try { $script:Session = New-PSSession -VMName $VmName -Credential $script:Credential -ErrorAction Stop }
        catch {
            # Never retry a wrong password: the account could lock.
            if ($_.Exception.Message -match '(?i)credential|password|user name|logon failure') {
                throw "PowerShell Direct refused the account or password ($($_.Exception.Message)). Run the script again and retype the ETPTest password."
            }
            if ((Get-Date) -gt $deadline) { throw "Could not open a PowerShell Direct session in 10 minutes: $($_.Exception.Message)" }
            Start-Sleep -Seconds 15
        }
    }
    Invoke-Command -Session $script:Session -ScriptBlock $GuestHelpers -ErrorAction Stop
}

function Invoke-Guest([scriptblock]$Block, [object[]]$Arguments = @()) {
    return Invoke-Command -Session $script:Session -ScriptBlock $Block -ArgumentList $Arguments -ErrorAction Stop
}

function Invoke-GuestJson([scriptblock]$Block, [object[]]$Arguments = @()) {
    $output = @(Invoke-Guest $Block $Arguments)
    if ($output.Count -eq 0) { return $null }
    return ([string]$output[-1] | ConvertFrom-Json)
}

function Find-Checkpoint([string]$Pattern) {
    $all = @(Get-VMSnapshot -VMName $VmName | Where-Object { $_.Name -like $Pattern } | Sort-Object CreationTime)
    if ($all.Count -eq 0) {
        $names = (@(Get-VMSnapshot -VMName $VmName | ForEach-Object { $_.Name }) -join '; ')
        throw "No checkpoint of $VmName matches '$Pattern'. Checkpoints: $names"
    }
    if ($all.Count -gt 1) { Write-Host "  $($all.Count) checkpoints match '$Pattern'; using the newest." -ForegroundColor Yellow }
    return $all[-1]
}

# The 1.12.0 start checkpoint: matches -StartCheckpoint, not -ExcludeCheckpoint (the 1.11.4 one has the same prefix), and was taken on
# top of -StartCheckpointParent (the 1.12.0 harness restores 'VM196 delta*' and checkpoints after the upgrade, so that is its parent).
function Select-StartCheckpoint($Snapshots) {
    $list = @($Snapshots | Where-Object {
            $_.Name -like $StartCheckpoint -and
            (-not $ExcludeCheckpoint -or $_.Name -notlike $ExcludeCheckpoint) -and
            (-not $StartCheckpointParent -or ([string]$_.ParentSnapshotName) -like $StartCheckpointParent) } | Sort-Object CreationTime)
    return , $list
}

function Find-StartCheckpoint {
    $all = @(Select-StartCheckpoint @(Get-VMSnapshot -VMName $VmName))
    if ($all.Count -eq 0) {
        $names = (@(Get-VMSnapshot -VMName $VmName | ForEach-Object { "$($_.Name) [parent: $($_.ParentSnapshotName)]" }) -join '; ')
        throw "No 1.12.0 start checkpoint: none matches '$StartCheckpoint', excluding '$ExcludeCheckpoint', with a parent like '$StartCheckpointParent'. Run Run-VM-1.12.0-Sequence.ps1 -Path From196 first, or pass -StartCheckpoint '<exact name>' -StartCheckpointParent ''. Checkpoints: $names"
    }
    if ($all.Count -gt 1) { Write-Host "  $($all.Count) checkpoints qualify as the 1.12.0 start; using the newest ($($all[-1].Name))." -ForegroundColor Yellow }
    return $all[-1]
}

function New-Checkpoint([string]$Name) {
    Checkpoint-VM -Name $VmName -SnapshotName $Name
    Write-Host "  checkpoint taken: $Name"
    $script:Notes.Add("Checkpoint taken: $Name")
}

function Restore-Checkpoint($Snapshot) {
    if (@('Running', 'Paused') -contains [string](Get-VM -Name $VmName).State) {
        Write-Host '  turning the VM off (its current state is discarded; no checkpoint is deleted)'
        Stop-VM -Name $VmName -TurnOff -Force
    }
    Restore-VMSnapshot -VMSnapshot $Snapshot -Confirm:$false
    if ((Get-VM -Name $VmName).State -ne 'Running') { Start-VM -Name $VmName }
    Wait-Heartbeat
}

function Copy-ToGuest([string]$HostFile, [string]$GuestFile, [string]$ExpectedHash) {
    if (-not $ExpectedHash) { $ExpectedHash = (Get-FileHash -LiteralPath $HostFile -Algorithm SHA256).Hash }
    $sizeMb = [math]::Round((Get-Item -LiteralPath $HostFile).Length / 1MB, 1)
    Write-Host "  copying $(Split-Path -Leaf $HostFile) ($sizeMb MB) to $GuestFile"
    $copied = $false
    try {
        Copy-VMFile -Name $VmName -SourcePath $HostFile -DestinationPath $GuestFile -FileSource Host -CreateFullPath -Force -ErrorAction Stop
        $copied = $true
    }
    catch { Write-Host "  Copy-VMFile failed ($($_.Exception.Message)); copying over the PowerShell Direct session instead." -ForegroundColor Yellow }
    if (-not $copied) { Copy-Item -ToSession $script:Session -LiteralPath $HostFile -Destination $GuestFile -Force }
    $actual = [string](Invoke-Guest { param($p) (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash } @($GuestFile))
    if ($actual -ne $ExpectedHash) { throw "Hash mismatch after copying $HostFile into the guest ($actual, expected $ExpectedHash)." }
}

function Copy-FromGuest([string]$Label) {
    $dest = Join-Path $script:Out $Label
    if (-not (Test-Path -LiteralPath $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
    try { Copy-Item -FromSession $script:Session -Path "$($script:GuestRunDir)\$Label\*" -Destination $dest -Recurse -Force -ErrorAction Stop }
    catch { Write-Host "  nothing copied from the guest for $Label ($($_.Exception.Message))" -ForegroundColor Yellow }
}

# ---------------------------------------------------------------- guest script blocks
# Copied from the harness: SQL through System.Data.SqlClient, Windows authentication over shared memory.
$GuestHelpers = {
    function global:Invoke-P6Sql {
        param([string]$Query, [string]$Database = 'master', [hashtable]$Parameters = @{}, [int]$Timeout = 900)
        $connection = $null
        $lastError = $null
        foreach ($source in @('lpc:.\SQLEXPRESS', '.\SQLEXPRESS')) {
            $candidate = New-Object System.Data.SqlClient.SqlConnection ('Data Source=' + $source + ';Initial Catalog=' + $Database + ';Integrated Security=SSPI;Connect Timeout=30;Pooling=false;Application Name=ETP VM rollback rehearsal')
            try { $candidate.Open(); $connection = $candidate; break }
            catch { $lastError = $_.Exception; $candidate.Dispose() }
        }
        if (-not $connection) { throw $lastError }
        $rows = New-Object System.Collections.Generic.List[object]
        try {
            $command = $connection.CreateCommand()
            $command.CommandText = $Query
            $command.CommandTimeout = $Timeout
            foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue('@' + $key, [string]$Parameters[$key]) }
            $reader = $command.ExecuteReader()
            do {
                while ($reader.Read()) {
                    $row = [ordered]@{}
                    for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                        $value = $reader.GetValue($i)
                        if ($value -is [DBNull]) { $value = $null }
                        elseif ($value -is [datetime]) { $value = $value.ToString('yyyy-MM-ddTHH:mm:ss.fff') }
                        elseif ($value -is [byte[]]) { $value = [BitConverter]::ToString($value) }
                        $row[$reader.GetName($i)] = $value
                    }
                    $rows.Add([pscustomobject]$row)
                }
            } while ($reader.NextResult())
            $reader.Close()
        }
        finally { $connection.Dispose() }
        # One array object (also for 0 or 1 rows): assign it, never wrap the call in @( ).
        return , $rows.ToArray()
    }

    function global:Get-P6RowCounts {
        $sql = @'
SET NOCOUNT ON;
DECLARE @sql nvarchar(max);
SELECT @sql = STRING_AGG(CONVERT(nvarchar(max), N'SELECT N' + QUOTENAME(s.name + N'.' + t.name, N'''') + N' AS table_name, COUNT_BIG(*) AS row_count FROM ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)), N' UNION ALL ')
FROM sys.tables AS t JOIN sys.schemas AS s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0;
IF @sql IS NOT NULL BEGIN SET @sql = @sql + N' ORDER BY table_name;'; EXEC sys.sp_executesql @sql; END;
'@
        return (Invoke-P6Sql -Database 'EtpReporting' -Query $sql)
    }

    function global:Get-P6AppDir {
        $roots = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
        $entry = @(Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'ETP Reporting' -and $_.InstallLocation })
        if ($entry.Count -gt 0) { return ([string]$entry[0].InstallLocation).TrimEnd('\') }
        return 'C:\Program Files\Saagar Traders\ETP Reporting Engine'
    }
}

$PrepGuestBlock = {
    param([string]$StageDir, [string]$RunDir, [string]$RestoreDir)
    foreach ($d in @($StageDir, (Split-Path -Parent $RunDir), $RestoreDir)) {
        if (-not (Test-Path -LiteralPath $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
        # Administrators and SYSTEM only, like the harness (runbook step A11).
        $null = & icacls.exe $d /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' 2>&1
    }
    New-Item -ItemType Directory -Path $RunDir -Force | Out-Null
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $elevated = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    [ordered]@{ computer = $env:COMPUTERNAME; account = $identity.Name; elevated = $elevated; clockUtc = (Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json -Compress
}

$SqlReadyBlock = {
    Start-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddMinutes(3)
    $ok = $false; $last = ''
    while (-not $ok -and (Get-Date) -lt $deadline) {
        try { $null = Invoke-P6Sql -Query 'SELECT 1 AS one'; $ok = $true } catch { $last = $_.Exception.Message; Start-Sleep -Seconds 10 }
    }
    [ordered]@{ ok = $ok; service = [string](Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue).Status; error = $last } | ConvertTo-Json -Compress
}

# The guest state this rehearsal judges: app, tasks, databases and their files, journal, users, row counts. No business values.
$RbFactsBlock = {
    param([string]$FailedName)
    $ErrorActionPreference = 'Continue'
    $errors = New-Object System.Collections.Generic.List[string]
    $f = [ordered]@{}
    $f.collectedUtc = (Get-Date).ToUniversalTime().ToString('o')
    $appDir = Get-P6AppDir
    $exe = Join-Path $appDir 'Etp.Reporting.Desktop.exe'
    $f.appDir = $appDir
    $f.exeExists = Test-Path -LiteralPath $exe
    $f.exeProductVersion = ''
    if ($f.exeExists) { $f.exeProductVersion = [string](Get-Item -LiteralPath $exe).VersionInfo.ProductVersion }
    $roots = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
    $f.etpUninstallEntries = @(Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'ETP Reporting' } | ForEach-Object {
            [ordered]@{ name = [string]$_.DisplayName; version = [string]$_.DisplayVersion; location = [string]$_.InstallLocation; quietUninstall = [string]$_.QuietUninstallString; uninstall = [string]$_.UninstallString } })
    $f.markers = [ordered]@{ setupIncomplete = (Test-Path -LiteralPath (Join-Path $appDir 'SETUP-INCOMPLETE.txt')); restorePending = (Test-Path -LiteralPath (Join-Path $appDir 'DATABASE-RESTORE-PENDING.txt')) }
    $f.bundledMigrations = @(Get-ChildItem -LiteralPath (Join-Path $appDir 'database\migrations') -Filter '*.sql' -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { $_.BaseName })
    $f.etpProcesses = @(Get-Process -Name 'Etp.Reporting.Desktop' -ErrorAction SilentlyContinue).Count
    $f.tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like 'ETP*' } | ForEach-Object {
            [ordered]@{ name = [string]$_.TaskName; path = [string]$_.TaskPath; state = [string]$_.State; user = [string]$_.Principal.UserId
                inAppFolder = [bool](@($_.Actions | Where-Object { ("$($_.Execute) $($_.Arguments)").IndexOf($appDir, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count) } })
    # Backups already in the folder (name and receipt purpose), so step 3 can tell which PRE_MIGRATION backup the 1.12.0 setup added.
    $f.backups = @(Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\Backups' -Filter '*.bak' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc | ForEach-Object {
            $purpose = ''
            $rp = $_.FullName + '.receipt.json'
            if (Test-Path -LiteralPath $rp) { try { $purpose = [string](Get-Content -Raw -LiteralPath $rp | ConvertFrom-Json).purpose } catch { $purpose = 'UNREADABLE' } }
            [ordered]@{ name = $_.Name; purpose = $purpose } })
    # What 1.12.0 adds outside the database. Kept on purpose by the rollback; recorded here (names and file counts only).
    $pd = 'C:\ProgramData\EtpReporting'
    $f.programData = @(foreach ($rel in @('Operations\ShopResults', 'CollectionsEvidence', 'TallyEvidence', 'Updates', 'Operations\disk-copy.json')) {
            $p = Join-Path $pd $rel
            $exists = Test-Path -LiteralPath $p
            $files = $null
            if ($exists -and (Test-Path -LiteralPath $p -PathType Container)) { $files = @(Get-ChildItem -LiteralPath $p -Recurse -File -Force -ErrorAction SilentlyContinue).Count }
            [ordered]@{ item = $rel; exists = $exists; files = $files } })
    # settings.json of this account (1.12 adds "database": {"endpointPolicy": ...}; 1.9.6's System.Text.Json reader ignores unknown members).
    $sj = Join-Path $env:LOCALAPPDATA 'EtpReporting\settings.json'
    $f.settingsJson = [ordered]@{ exists = (Test-Path -LiteralPath $sj); databaseSection = $false; parses = $null }
    if ($f.settingsJson.exists) {
        try { $sjv = Get-Content -Raw -LiteralPath $sj | ConvertFrom-Json; $f.settingsJson.parses = $true; $f.settingsJson.databaseSection = [bool]($sjv.PSObject.Properties['database']) }
        catch { $f.settingsJson.parses = $false }
    }
    $svc = Get-Service -Name 'MSSQL$SQLEXPRESS' -ErrorAction SilentlyContinue
    $f.sqlService = ''
    if ($svc) { $f.sqlService = [string]$svc.Status }
    $f.databases = @(); $f.files = @(); $f.journal = @(); $f.users = @(); $f.rowCounts = @(); $f.failedJournal = @(); $f.schema112 = @(); $f.schemaObjectsRows = $null
    if ($svc -and [string]$svc.Status -eq 'Running') {
        try { $rows = Invoke-P6Sql -Query "SELECT name, state_desc, user_access_desc FROM sys.databases WHERE name LIKE N'Etp%' ORDER BY name"; $f.databases = @($rows) } catch { $errors.Add("databases: $($_.Exception.Message)") }
        try { $rows = Invoke-P6Sql -Query "SELECT DB_NAME(database_id) AS db, type_desc, name AS logical_name, physical_name FROM sys.master_files WHERE DB_NAME(database_id) LIKE N'EtpReporting%' ORDER BY 1, 2"; $f.files = @($rows) } catch { $errors.Add("files: $($_.Exception.Message)") }
        $main = @($f.databases | Where-Object { $_.name -eq 'EtpReporting' -and $_.state_desc -eq 'ONLINE' })
        if ($main.Count -eq 1) {
            try { $rows = Invoke-P6Sql -Database 'EtpReporting' -Query 'SELECT migration_id FROM dbo.schema_migrations ORDER BY migration_id'; $f.journal = @($rows | ForEach-Object { ([string]$_.migration_id).Trim() }) } catch { $errors.Add("journal: $($_.Exception.Message)") }
            try { $rows = Invoke-P6Sql -Database 'EtpReporting' -Query 'SELECT windows_identity, role_code, CONVERT(int, is_active) AS is_active FROM dbo.application_users ORDER BY windows_identity'; $f.users = @($rows) } catch { $errors.Add("users: $($_.Exception.Message)") }
            try { $rows = Get-P6RowCounts; $f.rowCounts = @($rows) } catch { $errors.Add("row counts: $($_.Exception.Message)") }
            # 1.12.0 schema (0062-0069) present in EtpReporting: must be empty after the rollback.
            $probe = @"
SET NOCOUNT ON;
SELECT x.item FROM (VALUES
 (N'table dbo.schema_objects (0062)', CASE WHEN OBJECT_ID(N'dbo.schema_objects', N'U') IS NULL THEN 0 ELSE 1 END),
 (N'table dbo.bank_statement_lines (0063)', CASE WHEN OBJECT_ID(N'dbo.bank_statement_lines', N'U') IS NULL THEN 0 ELSE 1 END),
 (N'table dbo.cash_deposit_confirmations (0064)', CASE WHEN OBJECT_ID(N'dbo.cash_deposit_confirmations', N'U') IS NULL THEN 0 ELSE 1 END),
 (N'column product_settings.update_feed_path (0065)', CASE WHEN COL_LENGTH(N'dbo.product_settings', N'update_feed_path') IS NULL THEN 0 ELSE 1 END),
 (N'column tally_profiles.test_machine_name (0066)', CASE WHEN COL_LENGTH(N'dbo.tally_profiles', N'test_machine_name') IS NULL THEN 0 ELSE 1 END),
 (N'column business_units.unit_kind (0067)', CASE WHEN COL_LENGTH(N'dbo.business_units', N'unit_kind') IS NULL THEN 0 ELSE 1 END),
 (N'table dbo.no_sale_days (0068)', CASE WHEN OBJECT_ID(N'dbo.no_sale_days', N'U') IS NULL THEN 0 ELSE 1 END),
 (N'table dbo.import_source_locations (0069)', CASE WHEN OBJECT_ID(N'dbo.import_source_locations', N'U') IS NULL THEN 0 ELSE 1 END)
) AS x(item, present) WHERE x.present = 1;
IF OBJECT_ID(N'dbo.schema_objects', N'U') IS NOT NULL EXEC sys.sp_executesql N'SELECT COUNT_BIG(*) AS schema_objects_rows FROM dbo.schema_objects';
"@
            try { $rows = Invoke-P6Sql -Database 'EtpReporting' -Query $probe; $f.schema112 = @($rows | Where-Object { $_.PSObject.Properties['item'] } | ForEach-Object { [string]$_.item }); $so = @($rows | Where-Object { $_.PSObject.Properties['schema_objects_rows'] }); if ($so.Count) { $f.schemaObjectsRows = [int64]$so[0].schema_objects_rows } }
            catch { $errors.Add("1.12 schema probe: $($_.Exception.Message)") }
        }
        if ($FailedName -and @($f.databases | Where-Object { $_.name -eq $FailedName -and $_.state_desc -eq 'ONLINE' }).Count -eq 1) {
            try { $rows = Invoke-P6Sql -Database $FailedName -Query 'SELECT migration_id FROM dbo.schema_migrations ORDER BY migration_id'; $f.failedJournal = @($rows | ForEach-Object { ([string]$_.migration_id).Trim() }) } catch { $errors.Add("failed journal: $($_.Exception.Message)") }
        }
    }
    $f.errors = $errors.ToArray()
    $f | ConvertTo-Json -Depth 6 -Compress
}

# DSR VALUE / VOL / INVOICE per store and period, read-only. The SQL of OperationalReportRepository.DsrFactsSql for the current period
# (1.9.6 = commit 7687873; -WithCancelled adds the v_sales_cancelled_same_day join of 1.11.2+ and 1.12.0, decision 41). Gift cards out of all three.
$DsrBlock = {
    param([string]$PeriodsJson, [string]$StoresCsv, [bool]$WithCancelled)
    $r = [ordered]@{ rule = $(if ($WithCancelled) { 'newer (rule 41)' } else { '1.9.6' }); viewExists = $false; rows = @(); coverage = @(); errors = @() }
    $periods = ConvertFrom-Json -InputObject $PeriodsJson   # assigned, then foreach: works whether 5.1 returns the array as one object or not
    $stores = @($StoresCsv -split ',' | Where-Object { $_ })
    try {
        $v = Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT CASE WHEN OBJECT_ID(N'dbo.v_sales_cancelled_same_day', N'V') IS NULL THEN 0 ELSE 1 END AS v"
        $r.viewExists = ([int]$v[0].v -eq 1)
    }
    catch { $r.errors += "view probe: $($_.Exception.Message)" }
    if ($WithCancelled -and -not $r.viewExists) { $r.errors += 'dbo.v_sales_cancelled_same_day is missing: the newer rule cannot be read on this database.'; return ($r | ConvertTo-Json -Depth 5 -Compress) }
    $gc = "(UPPER(LTRIM(RTRIM(COALESCE(l.product_code,N''))))=N'GIFT CARD' OR UPPER(LTRIM(RTRIM(COALESCE(l.source_brand_code,N''))))=N'GC')"
    $join = ''; $notCancelled = ''
    if ($WithCancelled) {
        $join = 'LEFT JOIN dbo.v_sales_cancelled_same_day cancelled ON cancelled.store_code=i.store_code AND cancelled.document_number=i.document_number AND cancelled.transaction_date=i.transaction_date'
        $notCancelled = ' AND cancelled.document_number IS NULL'
    }
    $storeList = (@($stores | ForEach-Object { "N'" + ($_ -replace "'", "''") + "'" }) -join ',')
    $sql = @"
SET NOCOUNT ON;
SELECT i.store_code,
  CONVERT(varchar(40), SUM(CASE WHEN $gc THEN 0 ELSE l.source_gross_amount END)) AS value_amount,
  CONVERT(varchar(40), SUM(CASE WHEN $gc THEN 0 ELSE l.source_quantity END)) AS vol,
  COUNT(DISTINCT CASE WHEN UPPER(l.source_transaction_type)='INV' AND NOT $gc$notCancelled THEN i.sales_invoice_id END) AS invoices,
  CONVERT(varchar(40), SUM(CASE WHEN $gc THEN l.source_gross_amount END)) AS gift_cards
FROM dbo.sales_invoices i JOIN dbo.sales_lines l ON l.sales_invoice_id=i.sales_invoice_id
$join
WHERE i.transaction_date BETWEEN @from AND @to
  AND i.store_code IN ($storeList)
  AND UPPER(COALESCE(l.source_transaction_type,'')) IN ('INV','SR','BC')
GROUP BY i.store_code;
"@
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($p in $periods) {
        $found = @()
        try { $res = Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ from = [string]$p.from; to = [string]$p.to } -Query $sql; $found = @($res) }
        catch { $r.errors += "$($p.label): $($_.Exception.Message)" }
        foreach ($s in $stores) {
            $hit = @($found | Where-Object { [string]$_.store_code -eq $s }) | Select-Object -First 1
            if ($hit) { $rows.Add([ordered]@{ period = [string]$p.label; store = $s; value = [string]$hit.value_amount; vol = [string]$hit.vol; invoices = [string]$hit.invoices; giftCards = [string]$hit.gift_cards }) }
            else { $rows.Add([ordered]@{ period = [string]$p.label; store = $s; value = ''; vol = ''; invoices = ''; giftCards = '' }) }
        }
    }
    $r.rows = $rows.ToArray()
    try {
        $cov = Invoke-P6Sql -Database 'EtpReporting' -Query "SELECT store_code, CONVERT(varchar(10), MAX(COALESCE(period_end, business_date)), 23) AS r025_covers_to, (SELECT CONVERT(varchar(10), MAX(i.transaction_date), 23) FROM dbo.sales_invoices i WHERE i.store_code = f.store_code) AS last_sales_date FROM dbo.import_files f WHERE report_code = 'R025' AND data_truth_version = 1 AND is_superseded = 0 GROUP BY store_code ORDER BY store_code"
        $r.coverage = @($cov)
    }
    catch { $r.errors += "coverage: $($_.Exception.Message)" }
    $r | ConvertTo-Json -Depth 5 -Compress
}

$LastSalesDatesBlock = {
    param([string]$StoresCsv)
    $stores = @($StoresCsv -split ',' | Where-Object { $_ })
    $out = @()
    foreach ($s in $stores) {
        $res = Invoke-P6Sql -Database 'EtpReporting' -Parameters @{ s = $s } -Query 'SELECT CONVERT(varchar(10), MAX(transaction_date), 23) AS d FROM dbo.sales_invoices WHERE store_code = @s'
        $out += [ordered]@{ store = $s; last = [string]$res[0].d }
    }
    ConvertTo-Json -InputObject @($out) -Compress
}

# Finds the PRE_MIGRATION backup the 1.12.0 setup took (a PRE_MIGRATION receipt whose .bak was not in the baseline's list), checks the
# receipt, copies both to the restore folder and hashes the copy.
$PreMigrationBlock = {
    param([string]$Pattern, [string]$RestoreDir, [string]$KnownJson)
    $r = [ordered]@{ ok = $false; bak = ''; bytes = 0; sha256 = ''; copySha256 = ''; receipt = ''; purpose = ''; database = ''; verified = $null
        receiptSha256 = ''; receiptBytes = $null; encryption = ''; rowCounts = $null; guestBak = ''; guestReceipt = ''; candidates = @(); error = '' }
    try {
        $known = @()
        if ($KnownJson) { $known = @((ConvertFrom-Json -InputObject $KnownJson) | ForEach-Object { [string]$_ }) }
        $dir = 'C:\ProgramData\EtpReporting\Backups'
        $baks = @(Get-ChildItem -LiteralPath $dir -Filter $Pattern -File -ErrorAction Stop | Where-Object { $_.Extension -eq '.bak' } | Sort-Object LastWriteTimeUtc)
        $pre = @()
        foreach ($b in $baks) {
            $rp = $b.FullName + '.receipt.json'
            $purpose = ''
            if (Test-Path -LiteralPath $rp -PathType Leaf) { try { $purpose = [string](Get-Content -Raw -LiteralPath $rp | ConvertFrom-Json).purpose } catch { $purpose = 'UNREADABLE' } }
            $isNew = ($known -notcontains $b.Name)
            $r.candidates += "$($b.Name) [$purpose$(if (-not $isNew) { ', in the baseline' })]"
            if ($purpose -eq 'PRE_MIGRATION' -and $isNew) { $pre += $b }
        }
        if ($pre.Count -ne 1) { throw "$($pre.Count) PRE_MIGRATION backups matching $Pattern in $dir are not in the baseline (expected exactly 1). Backups seen: $($r.candidates -join '; '). Pass -PreMigrationPattern with the exact file name." }
        $bak = $pre[0]
        $receiptPath = $bak.FullName + '.receipt.json'
        $j = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
        $r.bak = $bak.Name; $r.bytes = $bak.Length; $r.receipt = Split-Path -Leaf $receiptPath
        $r.purpose = [string]$j.purpose; $r.database = [string]$j.database; $r.verified = $j.verified
        $r.receiptSha256 = [string]$j.sha256; $r.receiptBytes = $j.lengthBytes; $r.encryption = [string]$j.encryption; $r.rowCounts = $j.rowCounts
        $r.sha256 = (Get-FileHash -LiteralPath $bak.FullName -Algorithm SHA256).Hash
        New-Item -ItemType Directory -Path $RestoreDir -Force | Out-Null
        $r.guestBak = Join-Path $RestoreDir $bak.Name
        $r.guestReceipt = Join-Path $RestoreDir (Split-Path -Leaf $receiptPath)
        Copy-Item -LiteralPath $bak.FullName -Destination $r.guestBak -Force
        Copy-Item -LiteralPath $receiptPath -Destination $r.guestReceipt -Force
        $r.copySha256 = (Get-FileHash -LiteralPath $r.guestBak -Algorithm SHA256).Hash
        $r.ok = $true
    }
    catch { $r.error = $_.Exception.Message }
    $r | ConvertTo-Json -Depth 5 -Compress
}

# Stops (if running) and disables every ETP task. Re-enabling is setup's job (uninstall removes them; 1.9.6 setup registers them again).
$DisableTasksBlock = {
    $out = @()
    foreach ($t in @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like 'ETP*' })) {
        $before = [string]$t.State
        if ($before -eq 'Running') { Stop-ScheduledTask -TaskName $t.TaskName -TaskPath $t.TaskPath -ErrorAction SilentlyContinue }
        Disable-ScheduledTask -TaskName $t.TaskName -TaskPath $t.TaskPath -ErrorAction SilentlyContinue | Out-Null
        $after = [string](Get-ScheduledTask -TaskName $t.TaskName -TaskPath $t.TaskPath).State
        $out += [ordered]@{ name = [string]$t.TaskName; before = $before; after = $after }
    }
    ConvertTo-Json -InputObject @($out) -Compress
}

$NoEtpProcessBlock = { @(Get-Process -Name 'Etp.Reporting.Desktop' -ErrorAction SilentlyContinue).Count }

# Rename the database (never drop it), then rename its files when they hold the names the restore will need.
$RenameDatabaseBlock = {
    param([string]$Source, [string]$TargetBase)
    $ErrorActionPreference = 'Stop'
    $r = [ordered]@{ ok = $false; target = ''; renamed = $false; collision = @(); moves = @(); filesMoved = $false; state = ''; error = ''; recovery = '' }
    $modified = New-Object System.Collections.Generic.List[object]
    $moved = New-Object System.Collections.Generic.List[object]
    $offline = $false
    try {
        if ($Source -notmatch '^[A-Za-z0-9_]{1,100}$' -or $TargetBase -notmatch '^[A-Za-z0-9_]{1,100}$') { throw 'Invalid database name.' }
        $x = Invoke-P6Sql -Parameters @{ n = $Source } -Query 'SELECT DB_ID(@n) AS id'
        if ($null -eq $x[0].id) { throw "$Source does not exist, so there is nothing to rename." }
        $target = $TargetBase; $n = 2
        while ($true) {
            $y = Invoke-P6Sql -Parameters @{ n = $target } -Query 'SELECT DB_ID(@n) AS id'
            if ($null -eq $y[0].id) { break }
            if ($n -gt 9) { throw "No free name like $TargetBase." }
            $target = "${TargetBase}_$n"; $n++
        }
        $r.target = $target
        $null = Invoke-P6Sql -Query "BEGIN TRY ALTER DATABASE [$Source] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ALTER DATABASE [$Source] MODIFY NAME = [$target]; END TRY BEGIN CATCH IF DB_ID(N'$Source') IS NOT NULL ALTER DATABASE [$Source] SET MULTI_USER; THROW; END CATCH; ALTER DATABASE [$target] SET MULTI_USER;"
        $r.renamed = $true
        # restore-etp-database.ps1 puts the restored files at <default data path>\<db>.mdf and <default log path>\<db>_log.ldf and
        # refuses (51903) when either name is already there.
        $paths = Invoke-P6Sql -Query "SELECT CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultDataPath')) AS d, CONVERT(nvarchar(260), SERVERPROPERTY('InstanceDefaultLogPath')) AS l"
        $restoreData = (([string]$paths[0].d).TrimEnd('\')) + "\$Source.mdf"
        $restoreLog = (([string]$paths[0].l).TrimEnd('\')) + "\${Source}_log.ldf"
        $files = Invoke-P6Sql -Parameters @{ n = $target } -Query 'SELECT file_id, type_desc, name AS logical_name, physical_name FROM sys.master_files WHERE database_id = DB_ID(@n) ORDER BY file_id'
        $files = @($files)
        $foreign = @()
        foreach ($candidate in @($restoreData, $restoreLog)) {
            if ((Test-Path -LiteralPath $candidate) -and (@($files | Where-Object { [string]$_.physical_name -ieq $candidate }).Count -eq 0)) { $foreign += $candidate }
        }
        if ($foreign.Count -gt 0) { throw "A file that does not belong to $target is at $($foreign -join ', '); the restore would refuse it. Nothing was moved; look at it before going on." }
        $r.collision = @($files | Where-Object { $_.physical_name -ieq $restoreData -or $_.physical_name -ieq $restoreLog } | ForEach-Object { [string]$_.physical_name })
        if ($r.collision.Count -gt 0) {
            $plan = @()
            foreach ($f in $files) {
                $dir = Split-Path -Parent ([string]$f.physical_name)
                if ([string]$f.type_desc -eq 'LOG') { $leaf = "${target}_log.ldf"; if ([int]$f.file_id -ne 2) { $leaf = "${target}_log_$($f.file_id).ldf" } }
                elseif ([int]$f.file_id -eq 1) { $leaf = "$target.mdf" }
                else { $leaf = "${target}_$($f.file_id).ndf" }
                $new = Join-Path $dir $leaf
                if (Test-Path -LiteralPath $new) { throw "Cannot rename $($f.physical_name): $new already exists. Nothing was moved." }
                $plan += [pscustomobject]@{ logical = [string]$f.logical_name; old = [string]$f.physical_name; new = $new }
            }
            foreach ($p in $plan) {
                $null = Invoke-P6Sql -Query ("ALTER DATABASE [$target] MODIFY FILE (NAME = [" + $p.logical.Replace(']', ']]') + "], FILENAME = N'" + $p.new.Replace("'", "''") + "');")
                $modified.Add($p)
            }
            $null = Invoke-P6Sql -Query "ALTER DATABASE [$target] SET OFFLINE WITH ROLLBACK IMMEDIATE;"
            $offline = $true
            foreach ($p in $plan) { Move-Item -LiteralPath $p.old -Destination $p.new; $moved.Add($p) }
            $null = Invoke-P6Sql -Query "ALTER DATABASE [$target] SET ONLINE;"
            $offline = $false
            $r.filesMoved = $true
            $r.moves = @($plan | ForEach-Object { "$($_.old) -> $($_.new)" })
        }
        $s = Invoke-P6Sql -Parameters @{ n = $target } -Query 'SELECT state_desc FROM sys.databases WHERE name = @n'
        $r.state = [string]$s[0].state_desc
        $r.ok = ($r.state -eq 'ONLINE')
        if (-not $r.ok) { $r.error = "$target is $($r.state), not ONLINE." }
    }
    catch {
        $r.error = $_.Exception.Message
        # Put the files back the way they were, so the renamed database comes online again.
        if ($modified.Count -gt 0) {
            $log = @()
            try { if (-not $offline) { $null = Invoke-P6Sql -Query "ALTER DATABASE [$($r.target)] SET OFFLINE WITH ROLLBACK IMMEDIATE;"; $offline = $true } } catch { $log += "offline: $($_.Exception.Message)" }
            foreach ($p in $moved) { try { Move-Item -LiteralPath $p.new -Destination $p.old } catch { $log += "move back $($p.new): $($_.Exception.Message)" } }
            foreach ($p in $modified) { try { $null = Invoke-P6Sql -Query ("ALTER DATABASE [$($r.target)] MODIFY FILE (NAME = [" + $p.logical.Replace(']', ']]') + "], FILENAME = N'" + $p.old.Replace("'", "''") + "');") } catch { $log += "modify back $($p.logical): $($_.Exception.Message)" } }
            try { $null = Invoke-P6Sql -Query "ALTER DATABASE [$($r.target)] SET ONLINE;" } catch { $log += "online: $($_.Exception.Message)" }
            $r.recovery = $(if ($log.Count) { $log -join ' | ' } else { 'file names put back; database brought online again' })
        }
        try { $s = Invoke-P6Sql -Parameters @{ n = $r.target } -Query 'SELECT state_desc FROM sys.databases WHERE name = @n'; $r.state = [string]$s[0].state_desc } catch { }
    }
    $r | ConvertTo-Json -Depth 5 -Compress
}

$StartProcBlock = {
    param([string]$FilePath, [string]$Arguments, [string]$StdOut, [string]$StdErr)
    $global:P6Proc = $null
    $global:P6ProcStartUtc = (Get-Date).ToUniversalTime()
    $start = @{ FilePath = $FilePath; ArgumentList = $Arguments; PassThru = $true }
    if ($StdOut) { $start.RedirectStandardOutput = $StdOut; $start.RedirectStandardError = $StdErr }
    $global:P6Proc = Start-Process @start
    $null = $global:P6Proc.Handle   # keeps ExitCode readable after the process ends
    [string]$global:P6Proc.Id
}

$PollBlock = {
    param([bool]$WatchLog)
    $r = [ordered]@{ status = 'NOPROCESS'; exitCode = $null; lastLine = '' }
    $p = $global:P6Proc
    if ($null -ne $p) { if ($p.HasExited) { $r.status = 'EXIT'; $r.exitCode = $p.ExitCode } else { $r.status = 'RUNNING' } }
    if ($WatchLog -and $global:P6ProcStartUtc) {
        $log = Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\SetupLogs' -Filter '*.log' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTimeUtc -ge $global:P6ProcStartUtc } | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
        if ($log) { $r.lastLine = [string](Get-Content -LiteralPath $log.FullName -Tail 1 -ErrorAction SilentlyContinue) }
    }
    [pscustomobject]$r
}

$PollByNameBlock = {
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'EtpReportingEngine-Setup*' -or $_.ProcessName -like 'unins*' -or $_.ProcessName -like '_iu*' })
    if ($running.Count -gt 0) { [pscustomobject]@{ status = 'RUNNING'; exitCode = $null; lastLine = '' } }
    else { [pscustomobject]@{ status = 'NOPROCESS'; exitCode = $null; lastLine = '' } }
}

# Setup outcome (copied from the harness, without the 1.9.7-only checks, plus the newer-database refusal of the negative check).
$CollectSetupBlock = {
    param([string]$GuestDir, [string]$SinceUtc)
    $since = [datetime]::Parse($SinceUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    New-Item -ItemType Directory -Path $GuestDir -Force | Out-Null
    $r = [ordered]@{ bootstrapLogs = @(); keyLines = @(); completed = $false; dbccPassed = $false; preMigrationBackup = $false; failedLine = ''
        newerRefusal = $false; markers = [ordered]@{}; innoTail = @(); innoRefusedAtStart = $false }
    $logs = @(Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\SetupLogs' -Filter 'bootstrap-*.log' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -ge $since } | Sort-Object LastWriteTimeUtc)
    foreach ($l in $logs) { Copy-Item -LiteralPath $l.FullName -Destination $GuestDir -Force }
    $r.bootstrapLogs = @($logs | ForEach-Object { $_.Name })
    if ($logs.Count -gt 0) {
        $lines = @(Get-Content -LiteralPath $logs[-1].FullName -ErrorAction SilentlyContinue | Where-Object { $_ -and $_.Trim() })
        $text = $lines -join "`n"
        $r.keyLines = @($lines | Where-Object { $_ -match 'FAILED:|WARNING|NEXT STEP|NOTE:|completed successfully|DBCC|pre-migration|newer release|operations module|Owner|tasks are installed' })
        $r.completed = ($lines.Count -gt 0) -and ($lines[-1] -match 'ETP prerequisite bootstrap completed successfully')
        $r.dbccPassed = $text -match 'DBCC integrity checks passed'
        $r.preMigrationBackup = $text -match 'Verified pre-migration backup is retained at'
        $r.newerRefusal = $text -match 'upgraded by a newer release of ETP'
        $failed = @($lines | Where-Object { $_ -match 'FAILED:' } | Select-Object -First 1)
        if ($failed.Count -gt 0) { $r.failedLine = [string]$failed[0] }
    }
    $appDir = Get-P6AppDir
    $r.markers = [ordered]@{ setupIncomplete = (Test-Path -LiteralPath (Join-Path $appDir 'SETUP-INCOMPLETE.txt')); restorePending = (Test-Path -LiteralPath (Join-Path $appDir 'DATABASE-RESTORE-PENDING.txt')) }
    $inno = Join-Path $GuestDir 'setup-inno.log'
    if (Test-Path -LiteralPath $inno) {
        $innoLines = @(Get-Content -LiteralPath $inno -ErrorAction SilentlyContinue)
        $r.innoTail = @($innoLines | Select-Object -Last 25)
        $r.innoRefusedAtStart = [bool](@($innoLines | Where-Object { $_ -match 'InitializeSetup returned False' }).Count)
        if (-not $r.newerRefusal) { $r.newerRefusal = [bool](@($innoLines | Where-Object { $_ -match 'upgraded by a newer release' }).Count) }
    }
    $r | ConvertTo-Json -Depth 6 -Compress
}

# restore-etp-database.ps1's own log (SetupLogs\restore-*.log) written since the start, copied to the evidence folder.
$CollectRestoreBlock = {
    param([string]$GuestDir, [string]$SinceUtc)
    $since = [datetime]::Parse($SinceUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    $r = [ordered]@{ logs = @(); lines = @(); stopped = ''; matchesReceipt = $false; checkdbClean = $false; ownerLine = $false; brokerFailed = $false; waiting = '' }
    $logs = @(Get-ChildItem -LiteralPath 'C:\ProgramData\EtpReporting\SetupLogs' -Filter 'restore-*.log' -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTimeUtc -ge $since } | Sort-Object LastWriteTimeUtc)
    foreach ($l in $logs) { Copy-Item -LiteralPath $l.FullName -Destination $GuestDir -Force }
    $r.logs = @($logs | ForEach-Object { $_.Name })
    $lines = @()
    if ($logs.Count -gt 0) { $lines = @(Get-Content -LiteralPath $logs[-1].FullName -ErrorAction SilentlyContinue | Where-Object { $_ }) }
    $r.lines = @($lines | Select-Object -Last 30)
    $text = $lines -join "`n"
    $stop = @($lines | Where-Object { $_ -match 'Restore stopped:' } | Select-Object -First 1)
    if ($stop.Count) { $r.stopped = [string]$stop[0] }
    $r.matchesReceipt = $text -match 'The backup matches its receipt'
    $r.checkdbClean = $text -match 'DBCC CHECKDB found no damage'
    $r.ownerLine = $text -match 'is now an active Owner of'
    $r.brokerFailed = $text -match 'operations broker could not be installed'
    $w = @($lines | Where-Object { $_ -match 'database update\(s\) are waiting|No database updates are waiting' } | Select-Object -First 1)
    if ($w.Count) { $r.waiting = [string]$w[0] }
    $r | ConvertTo-Json -Depth 4 -Compress
}

$UninstallerBlock = {
    $roots = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
    $e = @(Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'ETP Reporting' })
    $r = [ordered]@{ count = $e.Count; version = ''; exe = '' }
    if ($e.Count -eq 1) {
        $r.version = [string]$e[0].DisplayVersion
        $cmd = [string]$e[0].UninstallString
        if ($cmd -match '^\s*"([^"]+)"') { $r.exe = $Matches[1] } else { $r.exe = ($cmd -split '\s+/')[0].Trim() }
    }
    $r | ConvertTo-Json -Compress
}

$WaitUninstalledBlock = {
    param([int]$Minutes, [string]$AppDir)
    $roots = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*')
    $deadline = (Get-Date).AddMinutes($Minutes)
    # Inno's uninstaller hands over to a copy of itself in %TEMP%; wait for the entry to go and for that copy to end.
    while ($true) {
        $left = @(Get-ItemProperty -Path $roots -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'ETP Reporting' }).Count
        $busy = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'unins*' -or $_.ProcessName -like '_iu*' }).Count
        if ($left -eq 0 -and $busy -eq 0) { break }
        if ((Get-Date) -gt $deadline) { break }
        Start-Sleep -Seconds 10
    }
    [ordered]@{ entriesLeft = $left; uninstallerRunning = $busy; exeLeft = (Test-Path -LiteralPath (Join-Path $AppDir 'Etp.Reporting.Desktop.exe')) } | ConvertTo-Json -Compress
}

$EnableTasksBlock = {
    param([string[]]$Names)
    $out = @()
    foreach ($n in $Names) {
        $t = Get-ScheduledTask -TaskName $n -ErrorAction SilentlyContinue
        if ($t -and [string]$t.State -eq 'Disabled') { Enable-ScheduledTask -TaskName $t.TaskName -TaskPath $t.TaskPath | Out-Null; $out += $n }
    }
    ConvertTo-Json -InputObject @($out) -Compress
}

# ---------------------------------------------------------------- step helpers
function Get-Facts([string]$Phase) {
    Write-Host "  reading the guest state ($Phase)"
    $f = Invoke-GuestJson $RbFactsBlock @($script:FailedName)
    Save-Json "facts-$Phase.json" $f
    foreach ($e in @($f.errors)) { if ($e) { Write-Host "    guest note: $e" -ForegroundColor Yellow } }
    return $f
}

function Get-CountMap($Facts) {
    $m = @{}
    foreach ($row in @($Facts.rowCounts)) { if ($row) { $m[[string]$row.table_name] = [int64]$row.row_count } }
    return $m
}

function Get-UserKeys($Facts) {
    return @(@($Facts.users) | Where-Object { $_ } | ForEach-Object { "$($_.windows_identity)|$($_.role_code)|$([int]$_.is_active)" } | Sort-Object)
}

function Wait-NoEtpProcess([int]$Minutes = 10, [switch]$AskSagar) {
    $deadline = (Get-Date).AddMinutes($Minutes)
    while ($true) {
        $n = [int](Invoke-Guest $NoEtpProcessBlock)
        if ($n -eq 0) { return }
        if ($AskSagar) {
            Read-Host "  ETP is running in the VM ($n process). Close it in the VM console (vmconnect.exe localhost $VmName; do not kill it), then press Enter" | Out-Null
            continue
        }
        if ((Get-Date) -gt $deadline) { throw "ETP is still running in the VM after $Minutes minutes. Close it in the VM console and run again." }
        Start-Sleep -Seconds 15
    }
}

function Wait-GuestProcess {
    param([string]$What, [int]$TimeoutMinutes, [switch]$WatchLog)
    $start = Get-Date
    $deadline = $start.AddMinutes($TimeoutMinutes)
    $lastLine = ''
    $lastPrint = $start
    $byName = $false
    while ($true) {
        Start-Sleep -Seconds 15
        $state = $null
        try {
            if ($byName) { $state = Invoke-Guest $PollByNameBlock } else { $state = Invoke-Guest $PollBlock @([bool]$WatchLog) }
        }
        catch {
            Write-Host "  the guest session dropped ($($_.Exception.Message)); reconnecting" -ForegroundColor Yellow
            Wait-Heartbeat
            Connect-Guest
            $byName = $true
            $state = Invoke-Guest $PollByNameBlock
        }
        $elapsed = [int]((Get-Date) - $start).TotalMinutes
        if ($state.lastLine -and $state.lastLine -ne $lastLine) { $lastLine = [string]$state.lastLine; $lastPrint = Get-Date; Write-Host "  [$elapsed min] $(Get-LogMessage $lastLine)" }
        elseif (((Get-Date) - $lastPrint).TotalMinutes -ge 3) { $lastPrint = Get-Date; Write-Host "  [$elapsed min] $What still running" }
        if ($state.status -eq 'EXIT') { Write-Host "  $What finished after $elapsed min, exit code $($state.exitCode)"; return $state.exitCode }
        if ($state.status -eq 'NOPROCESS') { Write-Host "  $What : no process left to watch (the session was reset); exit code unknown" -ForegroundColor Yellow; return $null }
        if ((Get-Date) -gt $deadline) { throw "$What was still running after $TimeoutMinutes minutes. It was left running. Look at the VM console before doing anything else." }
    }
}

function Invoke-Setup {
    param([string]$Label, [string]$GuestInstaller, [string]$Tasks, [switch]$NoRetry)
    Write-Host "  setup: $(Split-Path -Leaf $GuestInstaller), tasks '$Tasks' ($Label)"
    # The five-minute automation task can start ETP in the same second setup begins (4 Oct, exit 5): pause the ETP tasks for the run.
    $pausedTasks = @(Invoke-Guest { Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -like 'ETP Reporting*' -and $_.State -ne 'Disabled' } | ForEach-Object { Disable-ScheduledTask -TaskName $_.TaskName -TaskPath $_.TaskPath | Out-Null; $_.TaskPath + '|' + $_.TaskName } })
    if ($pausedTasks.Count -gt 0) { Write-Host "  paused guest tasks: $($pausedTasks -join ', ')" }
    Wait-NoEtpProcess -AskSagar
    $guestDir = "$($script:GuestRunDir)\$Label"
    $innoLog = "$guestDir\setup-inno.log"
    $argString = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG="{0}" /TASKS="{1}"' -f $innoLog, $Tasks
    $sinceUtc = [string](Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null; (Get-Date).ToUniversalTime().ToString('o') } @($guestDir))
    Write-Host "  $GuestInstaller $argString"
    [void](Invoke-Guest $StartProcBlock @($GuestInstaller, $argString, '', ''))
    $exitCode = Wait-GuestProcess -What "setup ($Label)" -TimeoutMinutes $SetupTimeoutMinutes -WatchLog
    $collected = Invoke-GuestJson $CollectSetupBlock @($guestDir, $sinceUtc)
    Copy-FromGuest $Label
    $run = [pscustomobject]@{ Label = $Label; Tasks = $Tasks; ExitCode = $exitCode; SinceUtc = $sinceUtc; Collected = $collected }
    Save-Json "$Label\setup-result.json" $run
    if ($pausedTasks.Count -gt 0) {
        Invoke-Guest { param($list) foreach ($t in $list) { $p, $n = $t -split '\|', 2; Enable-ScheduledTask -TaskName $n -TaskPath $p -ErrorAction SilentlyContinue | Out-Null } } @(, $pausedTasks) | Out-Null
        Write-Host "  re-enabled the tasks paused for setup: $($pausedTasks -join ', ')"
    }
    $refusedAtStart = [bool]$collected.innoRefusedAtStart -or [bool](@($collected.innoTail | Where-Object { $_ -match '(?i)mutex|is running|unable to automatically close|could not be shut down' }).Count)
    if (-not $NoRetry -and $null -ne $exitCode -and $exitCode -ne 0 -and @($collected.bootstrapLogs).Count -eq 0 -and $refusedAtStart) {
        $script:Notes.Add("$Label`: setup refused at start (ETP was running). Retried once.")
        Write-Host '  setup refused to start because ETP was running; waiting and retrying once' -ForegroundColor Yellow
        Start-Sleep -Seconds 30
        return (Invoke-Setup -Label "$Label-retry" -GuestInstaller $GuestInstaller -Tasks $Tasks -NoRetry)
    }
    return $run
}

function Open-Guest([string]$Area) {
    Wait-Heartbeat
    Connect-Guest
    $skew = 0
    $who = $null
    for ($i = 0; $i -lt 3; $i++) {
        $who = Invoke-GuestJson $PrepGuestBlock @($GuestStageDir, $script:GuestRunDir, $GuestRestoreDir)
        $guestUtc = if ($who.clockUtc -is [datetime]) { $who.clockUtc.ToUniversalTime() } else { ConvertFrom-RoundTrip ([string]$who.clockUtc) }
        $skew = [math]::Abs(((Get-Date).ToUniversalTime() - $guestUtc).TotalSeconds)
        if ($skew -le 120) { break }
        # 4 Oct: the skew after a checkpoint restore was the guest's time zone; match the host's (as the harness does).
        $tz = (Get-TimeZone).Id
        Write-Host "  guest clock is $([int]$skew) s away; setting the guest time zone to '$tz'"
        Invoke-Guest { param($id) Set-TimeZone -Id $id } @($tz) | Out-Null
        Start-Sleep -Seconds 5
    }
    Add-Check $Area 'Guest identity' "$GuestComputer, $GuestUser, elevated" "$($who.computer), $($who.account), elevated=$($who.elevated)" (Get-Result ((([string]$who.computer) -eq $GuestComputer) -and (([string]$who.account) -eq $GuestUser) -and [bool]$who.elevated))
    Add-Check $Area 'Guest clock' 'within 120 s of the host' "$([int]$skew) s" (Get-Result ($skew -le 120) 'REVIEW')
    if (-not $who.elevated) { throw 'The PowerShell Direct session is not elevated. ETPTest must be a local administrator in the VM.' }
    $sql = Invoke-GuestJson $SqlReadyBlock
    Assert-Check $Area 'SQL Server answers' 'service Running, SELECT 1 works' "service $($sql.service); ok=$($sql.ok) $($sql.error)" ([bool]$sql.ok) 'SQL Server in the VM did not answer within 3 minutes. Look at the VM console; nothing was changed.'
}

function Read-Dsr([string]$Phase, [string]$Source, [switch]$RuleNew) {
    $periodsJson = ConvertTo-Json -InputObject @($script:Periods) -Compress
    $res = Invoke-GuestJson $DsrBlock @($periodsJson, ($Stores -join ','), [bool]$RuleNew)
    Save-Json "dsr-$Phase.json" $res
    foreach ($e in @($res.errors)) { if ($e) { Write-Host "    DSR note: $e" -ForegroundColor Yellow } }
    foreach ($row in @($res.rows)) {
        $script:Dsr.Add([pscustomobject]@{ Phase = $Phase; Source = $Source; Rule = [string]$res.rule; Period = [string]$row.period; Store = [string]$row.store
                Value = [string]$row.value; Vol = [string]$row.vol; Invoices = [string]$row.invoices; GiftCards = [string]$row.giftCards })
    }
    return $res
}

function Format-Dsr($Row) {
    if (-not $Row) { return 'not read' }
    if (-not $Row.Value -and -not $Row.Invoices) { return 'no sales rows' }
    return "$($Row.Value) / $($Row.Vol) / $($Row.Invoices)"
}

# Set difference both ways (Compare-Object refuses an empty list).
function Get-ListDiff($A, $B, [string]$OnlyA, [string]$OnlyB) {
    $a = @($A | Where-Object { $null -ne $_ }); $b = @($B | Where-Object { $null -ne $_ })
    $out = @()
    foreach ($x in $a) { if ($b -notcontains $x) { $out += "$x ($OnlyA)" } }
    foreach ($x in $b) { if ($a -notcontains $x) { $out += "$x ($OnlyB)" } }
    return , $out
}

function Get-DsrRows([string]$Phase) { return @($script:Dsr | Where-Object { $_.Phase -eq $Phase }) }

function Compare-Dsr([string]$PhaseA, [string]$PhaseB) {
    $diff = @()
    foreach ($a in (Get-DsrRows $PhaseA)) {
        $b = @($script:Dsr | Where-Object { $_.Phase -eq $PhaseB -and $_.Period -eq $a.Period -and $_.Store -eq $a.Store }) | Select-Object -First 1
        if (-not $b -or $a.Value -ne $b.Value -or $a.Vol -ne $b.Vol -or $a.Invoices -ne $b.Invoices -or $a.GiftCards -ne $b.GiftCards) { $diff += "$($a.Period) $($a.Store): $(Format-Dsr $a) vs $(Format-Dsr $b)" }
    }
    return , $diff
}

function Set-Periods($Facts) {
    $last = Invoke-GuestJson $LastSalesDatesBlock @(($Stores -join ','))
    $list = New-Object System.Collections.Generic.List[object]
    $list.Add([pscustomobject]@{ label = "$DsrDate (day)"; from = $DsrDate; to = $DsrDate })
    foreach ($d in @(@($last) | ForEach-Object { [string]$_.last } | Where-Object { $_ -and $_ -ne $DsrDate } | Sort-Object -Unique)) {
        $which = (@(@($last) | Where-Object { [string]$_.last -eq $d } | ForEach-Object { $_.store }) -join ', ')
        $list.Add([pscustomobject]@{ label = "$d (day; last sales day of $which)"; from = $d; to = $d })
    }
    $list.Add([pscustomobject]@{ label = 'Aug 2026 (month)'; from = '2026-08-01'; to = '2026-08-31' })
    $script:Periods = $list.ToArray()
    Add-Check 'DSR' 'Periods compared' "$DsrDate as asked, plus each store's last sales day and Aug 2026" (@($script:Periods | ForEach-Object { $_.label }) -join '; ') 'INFO'
}

# ---------------------------------------------------------------- plan (printed by -DryRun and at the start of a real run)
function Show-Plan {
    $lines = @(
        "G16 rollback rehearsal on $VmName ($NewVersion -> $OldVersion). Evidence: $ReferenceRoot\VM195-<yyyyMMdd-HHmm>-Rollback112\",
        '',
        "  0  Host checks: elevated window, heavy-job lock (waits up to $LockWaitMinutes min while a gate runs), 1.9.6 installer hash = SHA256SUMS.txt,",
        "     the 1.12.0 start checkpoint ('$StartCheckpoint', not '$ExcludeCheckpoint', parent like '$StartCheckpointParent')",
        "     and '$BaselineCheckpoint' exist, memory and disk. Then the ETPTest password prompt.",
        "  1  Safety checkpoint 'VM195 before 1.12.0 rollback rehearsal, <stamp>' of the VM as it is now$(if ($NoSafetyCheckpoint) { ' (SKIPPED: -NoSafetyCheckpoint)' })",
        "  2  YES -> restore '$BaselineCheckpoint' and read the 1.9.6 baseline (DSR, users, journal, row counts, backups present). Read-only.$(if ($SkipBaseline) { ' (SKIPPED: -SkipBaseline)' })",
        "  3  YES -> restore the 1.12.0 start checkpoint ($NewVersion, $NewMigrationCount migrations, latest $NewLatestMigration); read the same figures and the",
        "     1.12.0 leftovers (tasks, ProgramData folders, settings.json); find the PRE_MIGRATION backup ($PreMigrationPattern) the 1.12.0 setup added",
        "     (not in the baseline's list), check its receipt, copy it + receipt to $GuestRestoreDir; copy the 1.9.6 installer into $GuestStageDir.",
        '     ---- rollback clock starts ----',
        '  4  Disable and stop every ETP* task in the VM (incl. 1.12 shop results / disk copy); wait until ETP is closed (asks Sagar if it is open).',
        "  5  YES -> rename $Database -> $FailedBase (never dropped); rename its files too if they hold the names the restore needs.",
        "  6  YES -> restore-etp-database.ps1 -BackupPath $GuestRestoreDir\<bak> -ReceiptPath $GuestRestoreDir\<bak>.receipt.json (its 'run setup again' is ignored).",
        "  7  YES -> uninstall $NewVersion (silent; its uninstaller removes the ETP tasks that point into the app folder).",
        "  8  YES -> install $OldVersion (silent, tasks '$SetupTasks196').",
        '  9  Checks (below); tasks re-enabled if setup left one disabled.',
        '     ---- rollback clock stops ----',
        "  10 Checkpoint '$DoneCheckpointPrefix, <stamp>' (or 'VM195 Rollback 1.12.0 rehearsal STOPPED at <step>, <stamp>').",
        "  11 $(if ($NegativeCheck) { 'YES -> ' } else { '(not chosen) ' })negative check: restore the 1.12.0 start checkpoint, run 1.9.6 setup over the $NewMigrationCount-migration database, expect a refusal; then back to the 'done' checkpoint.",
        '',
        'Checks after the rollback (PASS needed):',
        "  - $OldVersion installed (exe version and one Programs and Features entry), no SETUP-INCOMPLETE / DATABASE-RESTORE-PENDING",
        "  - $OldMigrationCount migrations, latest $OldLatestMigration, the same journal as the baseline; the app bundles $OldMigrationCount",
        '  - no 1.12.0 schema left in EtpReporting (schema_objects, collections tables, update/Tally columns, unit_kind, no-sale days, import locations)',
        "  - DSR VALUE / VOL / INVOICE (1.9.6 rule) for every period and store = the baseline ($DsrDate, last sales days, Aug 2026)",
        '  - users and roles (identity, role, active) = the baseline; fact-table row counts = the baseline and the backup receipt',
        "  - tasks present and not disabled: $($ExpectedTasks -join ', '); no other ETP* task enabled",
        "  - $FailedBase still present and ONLINE with $NewMigrationCount migrations",
        '  - recorded, kept on purpose: ShopResults, CollectionsEvidence, TallyEvidence, Updates, disk-copy.json, settings.json',
        '  - rollback minutes recorded (GO-LIVE-CHECKLIST.md expects about 45)',
        '',
        "To get back to $NewVersion afterwards: restore the 1.12.0 start checkpoint (elevated; commands in SUMMARY.md and the notes)."
    )
    foreach ($l in $lines) { Write-Host $l }
}

function Invoke-DryRun {
    Write-Step 'DRY RUN: the plan below; nothing is changed and no password is asked'
    Show-Plan
    Write-Step 'Dry-run host checks (read-only)'
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    Write-Host "  elevated window: $($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) (the real run needs True)"
    if (Test-Path -LiteralPath $Installer196Path) {
        $sums = Join-Path (Split-Path -Parent $Installer196Path) 'SHA256SUMS.txt'
        $expected = ''
        if (Test-Path -LiteralPath $sums) { $line = @(Get-Content -LiteralPath $sums | Where-Object { $_ -like "*$(Split-Path -Leaf $Installer196Path)*" }) | Select-Object -First 1; $expected = (([string]$line).Trim() -split '\s+')[0] }
        Write-Host '  hashing the 1.9.6 installer (about 830 MB)'
        $hash = (Get-FileHash -LiteralPath $Installer196Path -Algorithm SHA256).Hash
        Write-Host "  1.9.6 installer SHA-256 $hash; SHA256SUMS.txt $expected; match=$($hash -ieq $expected)"
    }
    else { Write-Host "  1.9.6 installer NOT FOUND: $Installer196Path" -ForegroundColor Red }
    try {
        Import-Module Hyper-V -ErrorAction Stop
        $snaps = @(Get-VMSnapshot -VMName $VmName -ErrorAction Stop)
        $m = @($snaps | Where-Object { $_.Name -like $BaselineCheckpoint } | Sort-Object CreationTime)
        if ($m.Count) { Write-Host "  baseline checkpoint '$BaselineCheckpoint': found ($($m[-1].Name))" } else { Write-Host "  baseline checkpoint '$BaselineCheckpoint': NOT FOUND" -ForegroundColor Red }
        $s = @(Select-StartCheckpoint $snaps)
        if ($s.Count) { Write-Host "  1.12.0 start checkpoint: found ($($s[-1].Name), parent '$($s[-1].ParentSnapshotName)')$(if ($s.Count -gt 1) { "; $($s.Count) qualify, the newest is used" })" }
        else { Write-Host "  1.12.0 start checkpoint: NOT FOUND (run Run-VM-1.12.0-Sequence.ps1 -Path From196 first)" -ForegroundColor Red }
        Write-Host "  VM state: $((Get-VM -Name $VmName).State)"
    }
    catch { Write-Host "  Hyper-V not readable from this window ($($_.Exception.Message)); the real run checks it elevated." -ForegroundColor Yellow }
    Write-Host ''
    Write-Host 'DRY RUN finished: nothing was changed.' -ForegroundColor Green
}

# ---------------------------------------------------------------- the rehearsal
function Invoke-Preflight {
    $t = Start-Timing '0 Host checks'
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this from an elevated Windows PowerShell window (Run as administrator). Hyper-V and PowerShell Direct need it.' }
    Import-Module Hyper-V -ErrorAction Stop
    $vm = Get-VM -Name $VmName -ErrorAction Stop
    $script:RunInfo['VM'] = "$VmName (state $($vm.State), files at $($vm.Path))"
    $script:RunInfo['Checkpoints before the run'] = (@(Get-VMSnapshot -VMName $VmName | Sort-Object CreationTime | ForEach-Object { $_.Name }) -join '; ')
    $start = Find-StartCheckpoint
    Add-Check 'Host' "Start checkpoint ($NewVersion after the upgrade from the 1.9.6 VM)" "'$StartCheckpoint', not '$ExcludeCheckpoint', parent like '$StartCheckpointParent'" "$($start.Name) (parent '$($start.ParentSnapshotName)', created $($start.CreationTime.ToString('yyyy-MM-dd HH:mm')))" 'PASS'
    if (-not $SkipBaseline) { $base = Find-Checkpoint $BaselineCheckpoint; Add-Check 'Host' 'Baseline checkpoint (1.9.6, where that upgrade started)' $BaselineCheckpoint $base.Name 'PASS' }
    $gsi = Get-VMIntegrationService -VMName $VmName -Name 'Guest Service Interface'
    Add-Check 'Host' 'Guest Service Interface (for Copy-VMFile)' 'enabled' ([string]$gsi.Enabled) (Get-Result ([bool]$gsi.Enabled) 'REVIEW')
    $freeGb = [math]::Round((Get-CimInstance -ClassName Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
    $needGb = [math]::Round($vm.MemoryStartup / 1GB + 1, 1)
    Assert-Check 'Host' 'Free memory on Workpc' "at least $needGb GB" "$freeGb GB" ($freeGb -ge $needGb) 'Not enough free memory for the VM. Close large programs and make sure no build or test is running.'
    $qualifier = (Split-Path -Qualifier $vm.Path).TrimEnd(':')
    $diskGb = [math]::Round((Get-PSDrive -Name $qualifier).Free / 1GB, 1)
    Assert-Check 'Host' "Free space on $qualifier`: (checkpoints)" 'at least 20 GB' "$diskGb GB" ($diskGb -ge 20) "Not enough free space on $qualifier`: for the checkpoints this run takes."
    if (-not (Test-Path -LiteralPath $Installer196Path -PathType Leaf)) { throw "The 1.9.6 installer is not at $Installer196Path." }
    $sums = Join-Path (Split-Path -Parent $Installer196Path) 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $sums)) { throw "SHA256SUMS.txt is missing beside the 1.9.6 installer, so it cannot be verified. Do not run an installer you cannot verify." }
    $line = @(Get-Content -LiteralPath $sums | Where-Object { $_ -like "*$(Split-Path -Leaf $Installer196Path)*" }) | Select-Object -First 1
    $expected = (([string]$line).Trim() -split '\s+')[0]
    Write-Host '  hashing the 1.9.6 installer'
    $script:InstallerHash = (Get-FileHash -LiteralPath $Installer196Path -Algorithm SHA256).Hash
    Assert-Check 'Host' '1.9.6 installer SHA-256 = SHA256SUMS.txt' $expected $script:InstallerHash ($script:InstallerHash -ieq $expected) 'The 1.9.6 installer does not match SHA256SUMS.txt. Stop: do not use it.'
    $script:RunInfo['Installer 1.9.6'] = "$Installer196Path; SHA-256 $($script:InstallerHash)"
    Stop-Timing $t
}

function Invoke-SafetyCheckpoint {
    $t = Start-Timing '1 Safety checkpoint of the current VM state'
    if ($NoSafetyCheckpoint) { Add-Check 'Start' 'Safety checkpoint' 'taken' 'skipped (-NoSafetyCheckpoint)' 'REVIEW' }
    else {
        $name = "VM195 before 1.12.0 rollback rehearsal, $Stamp"
        New-Checkpoint $name
        Add-Check 'Start' 'Safety checkpoint of the VM as it was' 'taken' $name 'PASS'
    }
    Stop-Timing $t
}

function Invoke-Baseline {
    $t = Start-Timing "2 Baseline: $BaselineCheckpoint (1.9.6)"
    $snap = Find-Checkpoint $BaselineCheckpoint
    Confirm-Yes "restore checkpoint '$($snap.Name)' (the VM's current state is discarded$(if (-not $NoSafetyCheckpoint) { '; the safety checkpoint keeps it' })), then READ the 1.9.6 figures"
    Restore-Checkpoint $snap
    Open-Guest 'Baseline 1.9.6'
    $f = Get-Facts 'baseline-1.9.6'
    Assert-Check 'Baseline 1.9.6' 'Installed version' "$OldVersion*" $f.exeProductVersion ([string]$f.exeProductVersion -like "$OldVersion*") "The baseline checkpoint is not on $OldVersion. Tell Claude which checkpoint holds 1.9.6 + the live copy."
    $j = @($f.journal)
    Assert-Check 'Baseline 1.9.6' 'Migration journal' "$OldMigrationCount, latest $OldLatestMigration" "$($j.Count), latest $($j[-1])" (($j.Count -eq $OldMigrationCount) -and ($j[-1] -eq $OldLatestMigration)) 'The baseline database is not at 48 migrations.'
    Add-Check 'Baseline 1.9.6' 'Backups already in the backup folder (the 1.12.0 PRE_MIGRATION backup is the one added after this)' 'recorded' "$(@($f.backups).Count): $(@($f.backups | ForEach-Object { "$($_.name) [$($_.purpose)]" }) -join '; ')" 'INFO'
    Add-Check 'Baseline 1.9.6' 'No 1.12.0 schema in the baseline database (probe sanity)' 'none' $(if (@($f.schema112).Count) { @($f.schema112) -join '; ' } else { 'none' }) (Get-Result (@($f.schema112).Count -eq 0) 'REVIEW')
    Set-Periods $f
    [void](Read-Dsr 'baseline' "$($snap.Name), 1.9.6 rule")
    $script:Baseline = $f
    $script:BaselineSource = "checkpoint '$($snap.Name)' (1.9.6, before the $NewVersion upgrade)"
    Stop-Timing $t
}

function Invoke-StartState {
    $area = "Start $NewVersion"
    $t = Start-Timing "3 Start state: $NewVersion"
    $snap = Find-StartCheckpoint
    Confirm-Yes "restore checkpoint '$($snap.Name)' ($NewVersion; the VM's current state is discarded)"
    Restore-Checkpoint $snap
    $script:RunInfo['Start checkpoint'] = "$($snap.Name) (parent '$($snap.ParentSnapshotName)', created $($snap.CreationTime.ToString('yyyy-MM-dd HH:mm')))"
    Open-Guest $area
    $f = Get-Facts "before-rollback-$NewVersion"
    Assert-Check $area 'Installed version' "$NewVersion*" $f.exeProductVersion ([string]$f.exeProductVersion -like "$NewVersion*") "The start checkpoint is not on $NewVersion. Pass -StartCheckpoint '<exact name>' (Run-VM-1.12.0-Sequence.ps1 -Path From196 makes it)."
    $j = @($f.journal)
    Assert-Check $area 'Migration journal' "$NewMigrationCount, latest $NewLatestMigration" "$($j.Count), latest $($j[-1])" (($j.Count -eq $NewMigrationCount) -and ($j[-1] -eq $NewLatestMigration)) "The start database is not at $NewMigrationCount migrations (latest $NewLatestMigration). If the final 1.12.0 has another count, pass -NewMigrationCount and -NewLatestMigration."
    Add-Check $area '1.12.0 schema present (probe sanity)' 'all 8 items; dbo.schema_objects has rows' "$(@($f.schema112).Count): $(@($f.schema112) -join '; '); dbo.schema_objects rows $($f.schemaObjectsRows)" (Get-Result ((@($f.schema112).Count -eq 8) -and ([int64]$f.schemaObjectsRows -gt 0)) 'REVIEW')
    Add-Check $area 'Users (identity|role|active)' 'recorded' ((Get-UserKeys $f) -join '; ') 'INFO'
    Add-Check $area 'ETP tasks' 'recorded' (@($f.tasks | ForEach-Object { "$($_.name)=$($_.state)$(if (-not $_.inAppFolder) { ' (action outside the app folder)' })" }) -join '; ') 'INFO'
    Add-Check $area '1.12.0 items outside the database' 'recorded (the rollback keeps them)' (@($f.programData | ForEach-Object { "$($_.item): $(if ($_.exists) { "present$(if ($null -ne $_.files) { ", $($_.files) files" })" } else { 'absent' })" }) -join '; ') 'INFO'
    Add-Check $area 'settings.json of ETPTest' 'recorded' "exists=$($f.settingsJson.exists); database section (1.12 endpointPolicy)=$($f.settingsJson.databaseSection); parses=$($f.settingsJson.parses)" 'INFO'
    if ($SkipBaseline) { Set-Periods $f }
    [void](Read-Dsr 'start-196rule' "$($snap.Name), 1.9.6 rule")
    [void](Read-Dsr 'start-newrule' "$($snap.Name), $NewVersion rule" -RuleNew)
    if ($SkipBaseline) {
        $script:Baseline = $f
        $script:BaselineSource = "the $NewVersion database at '$($snap.Name)', read with the 1.9.6 rule (-SkipBaseline; the upgrade changes no sales row)"
        foreach ($row in @(Get-DsrRows 'start-196rule')) { $c = $row.PSObject.Copy(); $c.Phase = 'baseline'; $script:Dsr.Add($c) }
        $script:Notes.Add("-SkipBaseline: the baseline is the $NewVersion database read with the 1.9.6 rule, not a 1.9.6 screen. Users and row counts are compared with the $NewVersion database, so rows written after its upgrade show as differences.")
    }
    else {
        $d = Compare-Dsr 'baseline' 'start-196rule'
        Add-Check 'DSR' "1.9.6 rule: baseline (VM196 delta) vs the $NewVersion database" 'same figures (the upgrade changes no sales row)' $(if ($d.Count) { $d -join '; ' } else { 'same' }) (Get-Result ($d.Count -eq 0) 'REVIEW')
        $bu = Get-UserKeys $script:Baseline; $su = Get-UserKeys $f
        $lost = @($su | Where-Object { $bu -notcontains $_ })
        Add-Check $area "Users in the $NewVersion database that are not in the 1.9.6 baseline (a rollback drops them; re-add by hand)" 'for the record' $(if ($lost.Count) { $lost -join '; ' } else { 'none' }) 'INFO'
    }
    $aug196 = @(Get-DsrRows 'start-196rule' | Where-Object { $_.Period -like 'Aug 2026*' -and $_.Store -eq 'WLMHW' }) | Select-Object -First 1
    $augNew = @(Get-DsrRows 'start-newrule' | Where-Object { $_.Period -like 'Aug 2026*' -and $_.Store -eq 'WLMHW' }) | Select-Object -First 1
    Add-Check 'DSR' "WLMHW Aug 2026 INVOICE, 1.9.6 rule vs $NewVersion rule (decision 41)" "$AugustWlmhwInvoices196 vs $AugustWlmhwInvoicesNew" "$($aug196.Invoices) vs $($augNew.Invoices)" (Get-Result (([string]$aug196.Invoices -eq [string]$AugustWlmhwInvoices196) -and ([string]$augNew.Invoices -eq [string]$AugustWlmhwInvoicesNew)) 'REVIEW')

    # The PRE_MIGRATION backup the 1.12.0 setup took: the PRE_MIGRATION receipt that the baseline did not have.
    $known = @()
    if (-not $SkipBaseline) { $known = @(@($script:Baseline.backups) | Where-Object { $_ } | ForEach-Object { [string]$_.name }) }
    $pm = Invoke-GuestJson $PreMigrationBlock @($PreMigrationPattern, $GuestRestoreDir, (ConvertTo-Json -InputObject @($known) -Compress))
    Save-Json 'pre-migration-backup.json' $pm
    Assert-Check 'Backup' "PRE_MIGRATION backup of the $NewVersion upgrade found and copied" "exactly one PRE_MIGRATION $PreMigrationPattern with .receipt.json, not in the baseline ($($known.Count) known)" $(if ($pm.ok) { "$($pm.bak) ($($pm.bytes) bytes), $($pm.receipt)" } else { $pm.error }) ([bool]$pm.ok) "The PRE_MIGRATION backup was not found or not copied: $($pm.error)"
    $receiptOk = ([string]$pm.purpose -eq 'PRE_MIGRATION') -and ([string]$pm.database -eq $Database) -and ($pm.verified -eq $true) -and ([string]$pm.encryption -ne 'AES_256')
    Assert-Check 'Backup' 'Receipt: purpose, database, verified, not encrypted' "PRE_MIGRATION, $Database, verified, not AES_256" "$($pm.purpose), $($pm.database), verified=$($pm.verified), encryption=$($pm.encryption)" $receiptOk 'The receipt does not describe a verified PRE_MIGRATION backup of EtpReporting.'
    $hashOk = ([string]$pm.sha256 -ieq [string]$pm.receiptSha256) -and ([string]$pm.copySha256 -ieq [string]$pm.sha256) -and ([string]$pm.receiptBytes -eq [string]$pm.bytes)
    Assert-Check 'Backup' 'Receipt hash = backup file = the copy in C:\ETP-Restore' $pm.receiptSha256 "file $($pm.sha256); copy $($pm.copySha256); size $($pm.bytes) vs receipt $($pm.receiptBytes)" $hashOk 'The backup does not match its receipt. Stop: do not restore it.'
    # Before anything is changed: does the backup hold the 1.9.6 baseline rows? (receipt rowCounts = the broker's 4 tables)
    if (-not $SkipBaseline) {
        $bc = Get-CountMap $script:Baseline
        $rDiff = @()
        if (-not $pm.rowCounts) { $rDiff += 'the receipt has no rowCounts' }
        else { foreach ($p in @($pm.rowCounts.PSObject.Properties)) { $k = "dbo.$($p.Name)"; if ([string]$bc[$k] -ne [string]$p.Value) { $rDiff += "$k receipt $($p.Value), baseline $($bc[$k])" } } }
        $rText = 'same'
        if ($rDiff.Count) { $rText = $rDiff -join '; ' } elseif ($pm.rowCounts) { $rText = 'same: ' + (@($pm.rowCounts.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', ') }
        if ($AllowReceiptBaselineDifference) { Add-Check 'Backup' 'Receipt row counts = the 1.9.6 baseline' 'same' $rText (Get-Result ($rDiff.Count -eq 0) 'REVIEW') }
        else { Assert-Check 'Backup' 'Receipt row counts = the 1.9.6 baseline' 'same' $rText ($rDiff.Count -eq 0) 'The PRE_MIGRATION backup does not hold the 1.9.6 baseline rows, so it may be the wrong backup. Nothing was changed. Pass -PreMigrationPattern with the exact file name, or -AllowReceiptBaselineDifference if you know why.' }
    }
    $script:RunInfo['PRE_MIGRATION backup'] = "$($pm.bak); SHA-256 $($pm.sha256); receipt $($pm.receipt)"
    $script:PreMigration = $pm
    $script:GuestInstaller = "$GuestStageDir\" + (Split-Path -Leaf $Installer196Path)
    Copy-ToGuest $Installer196Path $script:GuestInstaller $script:InstallerHash
    Add-Check $area '1.9.6 installer copied into the guest, hash checked' $script:InstallerHash $script:GuestInstaller 'PASS'
    $script:StartFacts = $f
    Stop-Timing $t
}

function Invoke-Rollback {
    $script:RollbackClock = [Diagnostics.Stopwatch]::StartNew()
    $script:RunInfo['Rollback clock started'] = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')

    # 4. Tasks off, ETP closed.
    $t = Start-Timing '4 Disable the ETP tasks, ETP closed'
    $tasks = @(Invoke-GuestJson $DisableTasksBlock)
    Save-Json 'tasks-disabled.json' $tasks
    $notDisabled = @($tasks | Where-Object { $_.after -ne 'Disabled' })
    Assert-Check 'Rollback' 'ETP tasks disabled' 'every ETP* task Disabled' (@($tasks | ForEach-Object { "$($_.name): $($_.before) -> $($_.after)" }) -join '; ') ($notDisabled.Count -eq 0) 'Not every ETP task could be disabled. Disable it in Task Scheduler in the VM and run again (it restores the 1.12.0 start checkpoint first).'
    # A stopped automation run can take a moment to exit; ask Sagar only if ETP is still open after 2 minutes (then it is the app itself).
    try { Wait-NoEtpProcess -Minutes 2 } catch { Wait-NoEtpProcess -AskSagar }
    Add-Check 'Rollback' 'ETP closed' '0 Etp.Reporting.Desktop processes' '0' 'PASS'
    Stop-Timing $t

    # 5. Rename (never drop).
    $t = Start-Timing "5 Rename $Database -> $FailedBase"
    Confirm-Yes "rename the database $Database to $FailedBase (kept, never dropped), and rename its files if the restore needs their names"
    $script:GuestChanged = $true
    $rn = Invoke-GuestJson $RenameDatabaseBlock @($Database, $FailedBase)
    Save-Json 'rename.json' $rn
    if ($rn.target) { $script:FailedName = [string]$rn.target }
    $fileText = 'not needed (no file name in the way)'
    if ($rn.filesMoved) { $fileText = "renamed: $(@($rn.moves) -join '; ')" }
    if ($rn.recovery) { $fileText += " | recovery: $($rn.recovery)" }
    Add-Check 'Rollback' 'Files of the renamed database' 'renamed when they hold EtpReporting.mdf / EtpReporting_log.ldf (else restore THROW 51903)' $fileText $(if ($rn.ok) { 'PASS' } else { 'FAIL' })
    if ($rn.filesMoved) { $script:Notes.Add("Step 5 had to rename the database files ($(@($rn.collision) -join ', ')): the checklist's rename alone would have made restore-etp-database.ps1 refuse. Add the file rename to GO-LIVE-CHECKLIST.md section 3 step 2 (T-SQL in VM-ROLLBACK-REHEARSAL-NOTES.md).") }
    Assert-Check 'Rollback' "Rename $Database -> $($rn.target)" "$Database gone, $FailedBase ONLINE" "renamed=$($rn.renamed); state=$($rn.state); $($rn.error)" ([bool]$rn.ok) "The rename did not finish: $($rn.error). Look at SQL Server in the VM before anything else; nothing was dropped."
    $f = Get-Facts 'after-rename'
    $failedJ = @($f.failedJournal)
    Assert-Check 'Rollback' "$($script:FailedName) holds the $NewVersion data" "$NewMigrationCount migrations; $Database absent" "$($failedJ.Count) migrations; $Database present=$(@($f.databases | Where-Object { $_.name -eq $Database }).Count)" (($failedJ.Count -eq $NewMigrationCount) -and (@($f.databases | Where-Object { $_.name -eq $Database }).Count -eq 0)) 'After the rename the databases are not as expected.'
    Stop-Timing $t

    # 6. Restore the PRE_MIGRATION backup as EtpReporting.
    $t = Start-Timing '6 Restore the PRE_MIGRATION backup'
    Confirm-Yes "restore $($script:PreMigration.bak) as $Database with restore-etp-database.ps1 (it never overwrites an existing database)"
    $label = 'restore'
    $guestDir = "$($script:GuestRunDir)\$label"
    $sinceUtc = [string](Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null; (Get-Date).ToUniversalTime().ToString('o') } @($guestDir))
    $restoreScript = "$($script:StartFacts.appDir)\scripts\restore-etp-database.ps1"
    $restoreArgs = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -BackupPath "{1}" -ReceiptPath "{2}"' -f $restoreScript, $script:PreMigration.guestBak, $script:PreMigration.guestReceipt
    Write-Host "  powershell.exe $restoreArgs"
    [void](Invoke-Guest $StartProcBlock @('C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe', $restoreArgs, "$guestDir\stdout.txt", "$guestDir\stderr.txt"))
    $rc = Wait-GuestProcess -What 'restore-etp-database.ps1' -TimeoutMinutes $RestoreTimeoutMinutes -WatchLog
    $rl = Invoke-GuestJson $CollectRestoreBlock @($guestDir, $sinceUtc)
    Copy-FromGuest $label
    Save-Json "$label\restore-result.json" ([pscustomobject]@{ ExitCode = $rc; Collected = $rl })
    $f = Get-Facts 'after-restore'
    $db = @($f.databases | Where-Object { $_.name -eq $Database }) | Select-Object -First 1
    $j = @($f.journal)
    $restoredOk = $db -and ([string]$db.state_desc -eq 'ONLINE') -and ($j.Count -eq $OldMigrationCount) -and ($j[-1] -eq $OldLatestMigration)
    Add-Check 'Rollback' 'restore-etp-database.ps1 exit code' '0' "$rc$(if ($rl.stopped) { '; ' + (Get-LogMessage $rl.stopped) })" $(if ($rc -eq 0) { 'PASS' } elseif ($restoredOk -and ($rl.brokerFailed -or $null -eq $rc)) { 'REVIEW' } else { 'FAIL' })
    Add-Check 'Rollback' 'Restore log' 'matches its receipt; CHECKDB clean; Owner recovered' "receipt=$($rl.matchesReceipt); CHECKDB=$($rl.checkdbClean); owner=$($rl.ownerLine); $(Get-LogMessage $rl.waiting)" (Get-Result ([bool]$rl.matchesReceipt -and [bool]$rl.checkdbClean -and [bool]$rl.ownerLine) 'REVIEW')
    if ($rl.waiting -match 'are waiting') { $script:Notes.Add("The restore helper (the installed $NewVersion copy) ends with 'Next: run the ETP setup again' and lists the waiting updates (0049-$($NewLatestMigration.Substring(0, 4))). On Workpc that next step must be the 1.9.6 installer AFTER uninstalling $NewVersion, never $NewVersion setup again (that would re-apply them).") }
    Assert-Check 'Rollback' "$Database restored" "ONLINE, $OldMigrationCount migrations, latest $OldLatestMigration" "state $($db.state_desc); $($j.Count) migrations, latest $($j[-1])" ([bool]$restoredOk) "The restore did not leave a 48-migration $Database online. Read restore\ in the evidence folder; $($script:FailedName) is untouched."
    $counts = Get-CountMap $f
    $rcDiff = @()
    if (-not $script:PreMigration.rowCounts) { $rcDiff += 'the receipt has no rowCounts' }
    else { foreach ($p in @($script:PreMigration.rowCounts.PSObject.Properties)) { $k = "dbo.$($p.Name)"; if ([string]$counts[$k] -ne [string]$p.Value) { $rcDiff += "$k $($counts[$k]) (receipt $($p.Value))" } } }
    Assert-Check 'Rollback' 'Row counts = the backup receipt' $(if ($script:PreMigration.rowCounts) { @($script:PreMigration.rowCounts.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', ' } else { 'receipt row counts' }) $(if ($rcDiff.Count) { $rcDiff -join '; ' } else { 'same' }) ($rcDiff.Count -eq 0) 'The restored row counts differ from the backup receipt.'
    Stop-Timing $t

    # 7. Uninstall 1.12.0.
    $t = Start-Timing "7 Uninstall $NewVersion"
    $u = Invoke-GuestJson $UninstallerBlock
    Assert-Check 'Rollback' 'Uninstaller found' "one entry, $NewVersion" "$($u.count) entry, $($u.version), $($u.exe)" (($u.count -eq 1) -and ([string]$u.version -eq $NewVersion) -and $u.exe) "The $NewVersion uninstaller was not found in Programs and Features."
    Confirm-Yes "uninstall ETP $NewVersion silently (database, backups and ProgramData stay; its uninstaller removes the ETP tasks)"
    Wait-NoEtpProcess -AskSagar
    $uDir = "$($script:GuestRunDir)\uninstall-$NewVersion"
    Invoke-Guest { param($d) New-Item -ItemType Directory -Path $d -Force | Out-Null } @($uDir) | Out-Null
    [void](Invoke-Guest $StartProcBlock @([string]$u.exe, ('/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="{0}\uninstall.log"' -f $uDir), '', ''))
    $urc = Wait-GuestProcess -What 'uninstall' -TimeoutMinutes 20
    $w = Invoke-GuestJson $WaitUninstalledBlock @(15, [string]$script:StartFacts.appDir)
    Copy-FromGuest "uninstall-$NewVersion"
    $f = Get-Facts 'after-uninstall'
    # remove-etp-scheduled-tasks.ps1 (1.12.0) removes its approved tasks only when their action points into the app folder.
    $appDirTasks = @(@($script:StartFacts.tasks) | Where-Object { $_.inAppFolder } | ForEach-Object { [string]$_.name })
    $leftInApp = @(@($f.tasks) | Where-Object { $appDirTasks -contains $_.name } | ForEach-Object { [string]$_.name })
    $leftOther = @(@($f.tasks) | Where-Object { $appDirTasks -notcontains $_.name } | ForEach-Object { "$($_.name)=$($_.state)" })
    Add-Check 'Rollback' 'ETP tasks the uninstall had to remove (action in the app folder)' "gone: $(if ($appDirTasks.Count) { $appDirTasks -join ', ' } else { 'none were registered' })" $(if ($leftInApp.Count) { 'STILL THERE: ' + ($leftInApp -join ', ') } else { 'all gone' }) (Get-Result ($leftInApp.Count -eq 0) 'REVIEW')
    Add-Check 'Rollback' 'ETP tasks the uninstall leaves (action outside the app folder; disabled in step 4)' 'for the record' $(if ($leftOther.Count) { $leftOther -join '; ' } else { 'none' }) $(if ($leftOther.Count) { 'REVIEW' } else { 'INFO' })
    Assert-Check 'Rollback' "$NewVersion uninstalled" 'exit 0; no Programs and Features entry; exe gone' "exit $urc; entries left $($w.entriesLeft); exe left $($w.exeLeft)" ((($urc -eq 0) -or ($null -eq $urc)) -and ([int]$w.entriesLeft -eq 0) -and (-not $w.exeLeft)) "The $NewVersion uninstall did not finish. Look at uninstall-$NewVersion\uninstall.log in the evidence folder."
    Assert-Check 'Rollback' "$Database untouched by the uninstall" "ONLINE, $OldMigrationCount migrations" "$(@($f.journal).Count) migrations" (@($f.journal).Count -eq $OldMigrationCount) 'The database changed during the uninstall.'
    Stop-Timing $t

    # 8. Install 1.9.6.
    $t = Start-Timing "8 Install $OldVersion"
    Confirm-Yes "install ETP $OldVersion silently (tasks '$SetupTasks196'; it takes a safety backup and finds no update to apply)"
    $run = Invoke-Setup -Label "setup-$OldVersion-rollback" -GuestInstaller $script:GuestInstaller -Tasks $SetupTasks196
    $c = $run.Collected
    foreach ($line in @($c.keyLines | Where-Object { $_ -match 'WARNING|NEXT STEP|NOTE:' })) { Add-Check 'Rollback' 'Setup advice line' 'read it' (Get-LogMessage $line) $(if ($line -match 'WARNING') { 'REVIEW' } else { 'INFO' }) }
    Assert-Check 'Rollback' "$OldVersion setup" 'exit 0; bootstrap "completed successfully"; no FAILED line; no SETUP-INCOMPLETE' "exit $($run.ExitCode); completed=$($c.completed); $(Get-LogMessage $c.failedLine); incomplete=$($c.markers.setupIncomplete)" ((($run.ExitCode -eq 0)) -and [bool]$c.completed -and (-not $c.failedLine) -and (-not $c.markers.setupIncomplete)) "The 1.9.6 setup did not finish cleanly. Read setup-$OldVersion-rollback\ in the evidence folder."
    Stop-Timing $t

    # 9. Checks.
    $t = Start-Timing '9 Checks after the rollback'
    $f = Get-Facts 'after-rollback-1.9.6'
    $area = 'After rollback'
    $entries = @($f.etpUninstallEntries)
    Add-Check $area 'Application version' "$OldVersion* (exe), one entry $OldVersion" "$($f.exeProductVersion); entries: $(@($entries | ForEach-Object { "$($_.name) [$($_.version)]" }) -join '; ')" (Get-Result (([string]$f.exeProductVersion -like "$OldVersion*") -and ($entries.Count -eq 1) -and ([string]$entries[0].version -eq $OldVersion)))
    Add-Check $area 'Setup markers' 'no SETUP-INCOMPLETE.txt, no DATABASE-RESTORE-PENDING.txt' "incomplete=$($f.markers.setupIncomplete); restorePending=$($f.markers.restorePending)" (Get-Result ((-not $f.markers.setupIncomplete) -and (-not $f.markers.restorePending)))
    $j = @($f.journal); $bj = @($script:Baseline.journal)
    $jDiff = Get-ListDiff $bj $j 'only in the baseline' 'only after'
    Add-Check $area 'Migrations' "$OldMigrationCount, latest $OldLatestMigration, same journal as the baseline" "$($j.Count), latest $($j[-1]); differences: $(if ($jDiff.Count) { $jDiff -join ', ' } else { 'none' })" (Get-Result (($j.Count -eq $OldMigrationCount) -and ($j[-1] -eq $OldLatestMigration) -and ($jDiff.Count -eq 0)))
    $bundled = @($f.bundledMigrations)
    Add-Check $area 'Migrations bundled with the installed app' "$OldMigrationCount, latest = the journal's latest" "$($bundled.Count), latest $($bundled[-1])" (Get-Result (($bundled.Count -eq $OldMigrationCount) -and ($bundled[-1] -eq $j[-1])))
    [void](Read-Dsr 'after-rollback' 'restored database under 1.9.6, 1.9.6 rule')
    $d = Compare-Dsr 'baseline' 'after-rollback'
    $dsrText = @(Get-DsrRows 'after-rollback' | ForEach-Object { "$($_.Period) $($_.Store): $(Format-Dsr $_)" }) -join '; '
    Add-Check $area 'DSR VALUE / VOL / INVOICE = the 1.9.6 baseline' "same for every period and store (baseline: $($script:BaselineSource))" $(if ($d.Count) { 'DIFFERENT: ' + ($d -join '; ') } else { "same: $dsrText" }) (Get-Result ($d.Count -eq 0))
    $bu = Get-UserKeys $script:Baseline; $au = Get-UserKeys $f
    $uDiff = Get-ListDiff $bu $au 'only in the baseline' 'only after'
    Add-Check $area 'Users and roles (identity|role|active)' 'same as the baseline' $(if ($uDiff.Count) { $uDiff -join '; ' } else { "same ($($au.Count) rows; active: $(@($f.users | Where-Object { [int]$_.is_active -eq 1 } | ForEach-Object { "$($_.windows_identity) $($_.role_code)" }) -join ', '))" }) (Get-Result ($uDiff.Count -eq 0))
    $bc = Get-CountMap $script:Baseline; $ac = Get-CountMap $f
    $cDiff = @($FactTables | Where-Object { $bc.ContainsKey($_) -and ([string]$bc[$_] -ne [string]$ac[$_]) } | ForEach-Object { "$_ $($ac[$_]) (baseline $($bc[$_]))" })
    Add-Check $area 'Fact-table row counts' "same as the baseline: $($FactTables -join ', ')" $(if ($cDiff.Count) { $cDiff -join '; ' } else { 'same' }) (Get-Result ($cDiff.Count -eq 0))
    $otherDiff = @($bc.Keys | Where-Object { $FactTables -notcontains $_ -and ([string]$bc[$_] -ne [string]$ac[$_]) } | Sort-Object | ForEach-Object { "$_ $($bc[$_]) -> $($ac[$_])" })
    Add-Check $area 'Other tables that changed (setup, restore and Owner-recovery bookkeeping expected)' 'for the record' $(if ($otherDiff.Count) { $otherDiff -join '; ' } else { 'none' }) 'INFO'
    $missing = @($ExpectedTasks | Where-Object { $n = $_; @($f.tasks | Where-Object { $_.name -eq $n }).Count -eq 0 })
    $disabled = @($f.tasks | Where-Object { $ExpectedTasks -contains $_.name -and $_.state -eq 'Disabled' } | ForEach-Object { $_.name })
    if ($disabled.Count -gt 0) {
        $en = @(Invoke-GuestJson $EnableTasksBlock @(, [string[]]$disabled))
        Add-Check $area 'Tasks left disabled by setup, re-enabled here (checklist step 5)' 'none' ($en -join ', ') 'REVIEW'
        $f = Get-Facts 'after-rollback-tasks'
    }
    $taskText = @($f.tasks | ForEach-Object { "$($_.name)=$($_.state) ($($_.user))" }) -join '; '
    Add-Check $area 'ETP tasks registered again and enabled' "$($ExpectedTasks -join ', ') present, none Disabled" $(if ($missing.Count) { "MISSING: $($missing -join ', '); $taskText" } else { $taskText }) (Get-Result (($missing.Count -eq 0) -and (@($f.tasks | Where-Object { $ExpectedTasks -contains $_.name -and $_.state -eq 'Disabled' }).Count -eq 0)))
    $failedDb = @($f.databases | Where-Object { $_.name -eq $script:FailedName }) | Select-Object -First 1
    Add-Check $area "$($script:FailedName) kept" "present, ONLINE, $NewMigrationCount migrations" "$(if ($failedDb) { $failedDb.state_desc } else { 'MISSING' }); $(@($f.failedJournal).Count) migrations" (Get-Result ($failedDb -and ([string]$failedDb.state_desc -eq 'ONLINE') -and (@($f.failedJournal).Count -eq $NewMigrationCount)))
    Add-Check $area 'Database files' 'for the record' (@($f.files | ForEach-Object { "$($_.db) $($_.type_desc): $($_.physical_name)" }) -join '; ') 'INFO'
    Add-Check $area "No $NewVersion schema left in $Database" 'none of: schema_objects, collections tables, update/Tally columns, unit_kind, no-sale days, import locations' $(if (@($f.schema112).Count) { @($f.schema112) -join '; ' } else { 'none' }) (Get-Result (@($f.schema112).Count -eq 0))
    $otherOn = @(@($f.tasks) | Where-Object { $ExpectedTasks -notcontains $_.name -and $_.state -ne 'Disabled' } | ForEach-Object { "$($_.name)=$($_.state)" })
    Add-Check $area 'No other ETP task enabled (the 1.12 tasks need 1.12 scripts)' 'none' $(if ($otherOn.Count) { $otherOn -join '; ' } else { 'none' }) (Get-Result ($otherOn.Count -eq 0))
    Add-Check $area "$NewVersion items outside the database (kept on purpose; 1.9.6 never reads them)" 'for the record' (@($f.programData | ForEach-Object { "$($_.item): $(if ($_.exists) { "present$(if ($null -ne $_.files) { ", $($_.files) files" })" } else { 'absent' })" }) -join '; ') 'INFO'
    Add-Check $area 'settings.json of ETPTest' "kept; the 1.12 'database' section is ignored by 1.9.6 (System.Text.Json skips unknown members)" "exists=$($f.settingsJson.exists); database section=$($f.settingsJson.databaseSection); parses=$($f.settingsJson.parses)" (Get-Result ((-not $f.settingsJson.exists) -or ($f.settingsJson.parses -eq $true)) 'REVIEW')
    Stop-Timing $t
    $script:RollbackClock.Stop()
    $script:RollbackMinutes = [math]::Round($script:RollbackClock.Elapsed.TotalMinutes, 1)
    Add-Check 'Timing' 'Rollback clock (tasks off -> checks done)' 'about 45 min (GO-LIVE-CHECKLIST.md section 3)' "$($script:RollbackMinutes) min" (Get-Result ($script:RollbackMinutes -le 60) 'REVIEW')
}

function Invoke-NegativeCheck {
    $t = Start-Timing "11 Negative check: $OldVersion setup over the $NewMigrationCount-migration database"
    $snap = Find-StartCheckpoint
    Confirm-Yes "restore '$($snap.Name)' again (the rolled-back state is kept in the 'done' checkpoint) and run the $OldVersion setup straight over the $NewMigrationCount-migration database, as a mistake would"
    Restore-Checkpoint $snap
    Open-Guest 'Negative check'
    Copy-ToGuest $Installer196Path $script:GuestInstaller $script:InstallerHash
    $before = Get-Facts 'negative-before'
    [void](Invoke-GuestJson $DisableTasksBlock)
    Wait-NoEtpProcess -AskSagar
    $run = Invoke-Setup -Label "setup-$OldVersion-over-$NewVersion" -GuestInstaller $script:GuestInstaller -Tasks $SetupTasks196 -NoRetry
    $after = Get-Facts 'negative-after'
    $c = $run.Collected
    $area = 'Negative check'
    Add-Check $area "$OldVersion setup over a $NewMigrationCount-migration database" 'refused: exit not 0' "exit $($run.ExitCode); $(Get-LogMessage $c.failedLine)" (Get-Result (($null -ne $run.ExitCode) -and ($run.ExitCode -ne 0)))
    Add-Check $area 'Refusal message' '"This database was upgraded by a newer release of ETP ... Nothing was changed."' "found=$($c.newerRefusal)" (Get-Result ([bool]$c.newerRefusal) 'REVIEW')
    $jb = @($before.journal); $ja = @($after.journal)
    Add-Check $area 'Journal unchanged' "$NewMigrationCount, latest $NewLatestMigration" "$($ja.Count), latest $($ja[-1])" (Get-Result (($ja.Count -eq $jb.Count) -and ($ja.Count -eq $NewMigrationCount) -and ($ja[-1] -eq $NewLatestMigration)))
    $bc = Get-CountMap $before; $ac = Get-CountMap $after
    $cDiff = @($FactTables | Where-Object { $bc.ContainsKey($_) -and ([string]$bc[$_] -ne [string]$ac[$_]) } | ForEach-Object { "$_ $($bc[$_]) -> $($ac[$_])" })
    Add-Check $area 'Fact-table rows unchanged' 'same' $(if ($cDiff.Count) { $cDiff -join '; ' } else { 'same' }) (Get-Result ($cDiff.Count -eq 0))
    Add-Check $area 'App left behind' 'for the record (a refused setup leaves SETUP-INCOMPLETE.txt)' "$($after.exeProductVersion); incomplete=$($after.markers.setupIncomplete)" 'INFO'
    $script:Notes.Add("Not tried: attaching or opening $FailedBase with 1.9.6. ETP opens only the database named in operations.json / settings.json, so that test would need a config change that the real rollback never makes; the setup-over-$NewMigrationCount test above is the realistic mistake and needs no config change.")
    Stop-Timing $t
    $t = Start-Timing "Back to the 'done' checkpoint"
    $done = Find-Checkpoint "$DoneCheckpointPrefix, $Stamp*"
    Write-Host "  the negative-check state is throwaway; restoring '$($done.Name)'"
    Restore-Checkpoint $done
    Add-Check $area "VM left at '$($done.Name)'" 'restored' 'restored' 'PASS'
    Stop-Timing $t
}

# ---------------------------------------------------------------- summary
function Write-Summary {
    $fails = @($script:Checks | Where-Object { $_.Result -eq 'FAIL' }).Count
    $reviews = @($script:Checks | Where-Object { $_.Result -eq 'REVIEW' }).Count
    $verdict = 'PASS'
    if ($script:LockBusy) { $verdict = 'NOT RUN: the heavy-job lock was busy (a gate or build is running)' }
    elseif ($fails -gt 0) { $verdict = "FAIL: $fails check(s) failed (stopped at: $($script:Step))" }
    elseif ($reviews -gt 0) { $verdict = "PASS with $reviews item(s) to review" }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# G16 rollback rehearsal on the VM: $NewVersion -> $OldVersion (decision 63 go-live)")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("**Verdict: $verdict.**  Rollback clock: **$(if ($null -ne $script:RollbackMinutes) { "$($script:RollbackMinutes) min" } else { 'not completed' })** (checklist expects about 45 min).")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Item | Value |')
    [void]$sb.AppendLine('|---|---|')
    [void]$sb.AppendLine("| Run | $RunId on $env:COMPUTERNAME |")
    foreach ($k in @($script:RunInfo.Keys)) { [void]$sb.AppendLine("| $(Format-Cell $k) | $(Format-Cell $script:RunInfo[$k]) |") }
    [void]$sb.AppendLine("| Baseline | $(Format-Cell $script:BaselineSource) |")
    [void]$sb.AppendLine("| Renamed database | $(Format-Cell $script:FailedName) |")
    [void]$sb.AppendLine("| Final checkpoint | $(Format-Cell $script:FinalCheckpoint) |")
    [void]$sb.AppendLine("| Evidence in the VM | $($script:GuestRunDir) |")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Checks')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| # | Area | Check | Expected | Actual | Result |')
    [void]$sb.AppendLine('|---|---|---|---|---|---|')
    foreach ($c in $script:Checks) { [void]$sb.AppendLine("| $($c.No) | $(Format-Cell $c.Area) | $(Format-Cell $c.Check) | $(Format-Cell $c.Expected) | $(Format-Cell $c.Actual) | **$($c.Result)** |") }
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## Timings')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| Step | Started | Ended | Minutes | On the rollback clock |')
    [void]$sb.AppendLine('|---|---|---|---|---|')
    foreach ($tm in $script:Timings) { [void]$sb.AppendLine("| $(Format-Cell $tm.Step) | $($tm.Started) | $($tm.Ended) | $($tm.Minutes) | $(if ($tm.InRollbackClock) { 'yes' } else { '' }) |") }
    [void]$sb.AppendLine('')
    if ($script:Dsr.Count -gt 0) {
        [void]$sb.AppendLine('## DSR VALUE / VOL / INVOICE (gift cards excluded; store totals, no customer data)')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine("| Period | Store | Baseline 1.9.6 (before upgrade) | $NewVersion database, 1.9.6 rule | $NewVersion database, $NewVersion rule (decision 41) | After rollback, 1.9.6 | Same as baseline |")
        [void]$sb.AppendLine('|---|---|---|---|---|---|---|')
        foreach ($b in (Get-DsrRows 'baseline')) {
            $pick = { param($ph) @($script:Dsr | Where-Object { $_.Phase -eq $ph -and $_.Period -eq $b.Period -and $_.Store -eq $b.Store }) | Select-Object -First 1 }
            $s196 = & $pick 'start-196rule'; $sNew = & $pick 'start-newrule'; $a = & $pick 'after-rollback'
            $same = ''
            if ($a) { $same = $(if ($a.Value -eq $b.Value -and $a.Vol -eq $b.Vol -and $a.Invoices -eq $b.Invoices -and $a.GiftCards -eq $b.GiftCards) { 'yes' } else { '**NO**' }) }
            [void]$sb.AppendLine("| $(Format-Cell $b.Period) | $($b.Store) | $(Format-Dsr $b) | $(Format-Dsr $s196) | $(Format-Dsr $sNew) | $(Format-Dsr $a) | $same |")
        }
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine("The VM's sales end 28 Sep (HEMW) / 29 Sep (WLMHW), so $DsrDate reads 'no sales rows' there (the app shows it as not imported). The comparison that proves the rollback is the last two columns' source: same rule, before and after.")
        [void]$sb.AppendLine('')
    }
    if ($script:Notes.Count -gt 0) {
        [void]$sb.AppendLine('## Notes')
        [void]$sb.AppendLine('')
        foreach ($n in $script:Notes) { [void]$sb.AppendLine("- $n") }
        [void]$sb.AppendLine('')
    }
    [void]$sb.AppendLine("## Back to $NewVersion")
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('In an elevated PowerShell on Workpc (the current VM state is discarded; no checkpoint is deleted):')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('```powershell')
    [void]$sb.AppendLine("Stop-VM -Name $VmName -TurnOff -Force")
    $startName = $StartCheckpoint
    if ($script:RunInfo.Contains('Start checkpoint')) { $startName = ([string]$script:RunInfo['Start checkpoint'] -split ' \(parent ')[0] }
    [void]$sb.AppendLine("Get-VMSnapshot -VMName $VmName | Where-Object Name -eq '$($startName -replace "'", "''")' | Restore-VMSnapshot -Confirm:`$false")
    [void]$sb.AppendLine("Start-VM -Name $VmName")
    [void]$sb.AppendLine('```')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine("Files here: checks.csv, timings.csv, dsr-figures.csv, facts-*.json, dsr-*.json, rename.json, pre-migration-backup.json, restore\ (helper log), uninstall-$NewVersion\, setup-*\ (Inno and bootstrap logs), run-$RunId.log (transcript).")
    Write-Utf8 (Join-Path $script:Out 'SUMMARY.md') $sb.ToString()
    Save-Csv 'checks.csv' $script:Checks
    Save-Csv 'timings.csv' @($script:Timings | Select-Object Step, Started, Ended, Minutes, InRollbackClock)
    Save-Csv 'dsr-figures.csv' $script:Dsr
    Write-Host ''
    Write-Host "VERDICT: $verdict" -ForegroundColor $(if ($fails -gt 0 -or $script:LockBusy) { 'Red' } elseif ($reviews -gt 0) { 'Yellow' } else { 'Green' })
    if ($null -ne $script:RollbackMinutes) { Write-Host "Rollback took $($script:RollbackMinutes) minutes." }
    Write-Host "Summary: $(Join-Path $script:Out 'SUMMARY.md')"
}

# ---------------------------------------------------------------- main
if ($DryRun) { Invoke-DryRun; exit 0 }

$exitCode = 1
$lock = $null
$lockHeld = $false
$transcribing = $false
$currentTiming = $null
try {
    $base = Join-Path $ReferenceRoot ('VM195-{0}-Rollback112' -f (Get-Date -Format 'yyyyMMdd-HHmm'))
    $script:Out = $base; $n = 2
    while (Test-Path -LiteralPath $script:Out) { $script:Out = "$base-$n"; $n++ }
    New-Item -ItemType Directory -Path $script:Out | Out-Null
    Start-Transcript -Path (Join-Path $script:Out "run-$RunId.log") -Force | Out-Null
    $transcribing = $true
    $script:RunInfo['Output folder'] = $script:Out
    $script:RunInfo['Switches'] = "NegativeCheck=$NegativeCheck; SkipBaseline=$SkipBaseline; NoSafetyCheckpoint=$NoSafetyCheckpoint"
    Write-Step "G16 rollback rehearsal ($NewVersion -> $OldVersion). Evidence: $($script:Out)"
    Show-Plan

    Write-Host ''
    Write-Host "  waiting for the heavy-job lock (up to $LockWaitMinutes min)"
    $lock = New-Object System.Threading.Mutex($false, 'Global\EtpHeavyJobLock')
    try { $lockHeld = $lock.WaitOne([TimeSpan]::FromMinutes($LockWaitMinutes)) }
    catch [System.Threading.AbandonedMutexException] { $lockHeld = $true }
    if (-not $lockHeld) {
        $script:LockBusy = $true
        throw "The heavy-job lock (Global\EtpHeavyJobLock) stayed busy for $LockWaitMinutes minutes: a gate, build or test is running. Run the same command again when it has finished."
    }

    Invoke-Preflight
    $script:Credential = Get-Credential -UserName $GuestUser -Message "Password for $GuestUser in $VmName (typed by Sagar; never stored)"
    if (-not $script:Credential) { throw 'No password was entered.' }

    Invoke-SafetyCheckpoint
    if (-not $SkipBaseline) { Invoke-Baseline }
    Invoke-StartState
    Invoke-Rollback

    $fails = @($script:Checks | Where-Object { $_.Result -eq 'FAIL' }).Count
    $script:Step = '10 Checkpoint'
    if ($fails -eq 0) {
        $script:FinalCheckpoint = "$DoneCheckpointPrefix, $Stamp"
        New-Checkpoint $script:FinalCheckpoint
        if ($NegativeCheck) { Invoke-NegativeCheck }
    }
    else {
        $script:FinalCheckpoint = "VM195 Rollback 1.12.0 rehearsal FAILED at checks, $Stamp"
        New-Checkpoint $script:FinalCheckpoint
        if ($NegativeCheck) { $script:Notes.Add('Negative check not run: the rollback itself has a FAIL.') }
    }
    $exitCode = 0
    if (@($script:Checks | Where-Object { $_.Result -eq 'FAIL' }).Count -gt 0) { $exitCode = 1 }
}
catch {
    Write-Host ''
    Write-Host "STOPPED at step '$($script:Step)': $($_.Exception.Message)" -ForegroundColor Red
    if (-not $script:LockBusy) { Add-Check 'Run' "Step '$($script:Step)'" 'finished' $_.Exception.Message 'FAIL' }
    $exitCode = 1
    if ($script:LockBusy) { $exitCode = 75 }
    # Keep the failed state for diagnosis (a checkpoint deletes nothing).
    if ($script:GuestChanged -and -not $script:FinalCheckpoint) {
        try {
            $script:FinalCheckpoint = "VM195 Rollback 1.12.0 rehearsal STOPPED at $(($script:Step -split ' ')[0]), $Stamp"
            New-Checkpoint $script:FinalCheckpoint
        }
        catch { Write-Host "  could not take the STOPPED checkpoint: $($_.Exception.Message)" -ForegroundColor Yellow }
    }
}
finally {
    foreach ($tm in $script:Timings) { if (-not $tm.Ended) { Stop-Timing $tm } }
    if ($script:RollbackClock -and $script:RollbackClock.IsRunning) { $script:RollbackClock.Stop() }
    if ($script:Out) { try { Write-Summary } catch { Write-Host "Could not write the summary: $($_.Exception.Message)" -ForegroundColor Red } }
    if ($script:Session) { Remove-PSSession -Session $script:Session -ErrorAction SilentlyContinue }
    if ($lockHeld) { try { $lock.ReleaseMutex() } catch { } }
    if ($lock) { $lock.Dispose() }
    if ($transcribing) { Stop-Transcript | Out-Null }
}
exit $exitCode
