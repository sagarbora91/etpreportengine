<#
.SYNOPSIS
  Runs on Workpc after ETP's nightly VERIFIED backup. Copies the newest verified .bak, as it is, to a second disk inside
  the same PC (decision 54: G:, protected by BitLocker), checks the copy by hash, and keeps the newest 30 daily copies
  plus the first copy of each of the newest 12 months.

.DESCRIPTION
  In order:
   1. Reads <BackupFolder>\<Database>-latest-verified.json (written by ETP's backup script only after the backup verified).
   2. Re-hashes the .bak and compares it with the receipt. Mismatch = stop (a doubtful backup is never copied).
   3. Refuses (exit 2, one plain line, no stack) when the disk is not connected, when -DiskCopyRoot is on the system
      drive, on a network share or inside OneDrive, or when the disk has less than 3 times the file's size free.
   4. Copies to <DiskCopyRoot>\<yyyy-MM>\<file> as .part, renames it, re-hashes the copy and compares.
   5. Writes <file>.receipt.json beside it and <DiskCopyRoot>\latest-disk-copy.json (what health check OPS-11 reads).
   6. Retention: keeps the newest KeepDaily copies plus the first copy of each of the newest KeepMonthly months.
      Older copies and their receipts are removed; nothing else in the folder is touched.
  No encryption: the disk is inside the PC and BitLocker covers it (decisions 51 and 56). Restoring is one file with
  no key to find. Nothing here holds a password or a key.

  Windows PowerShell 5.1 compatible. Run by the scheduled task 'ETP Copy Backup To Disk' (install-daily-backup-task.ps1
  -DiskCopyRoot G:\ETP-Backups).

.PARAMETER DiskCopyRoot  Folder on the second disk, for example G:\ETP-Backups.
.PARAMETER BackupFolder  ETP's backup folder (default C:\ProgramData\EtpReporting\Backups).
.PARAMETER Database      Database name in the backup file names (default EtpReporting).
.PARAMETER KeepDaily     Newest copies to keep (default 30).
.PARAMETER KeepMonthly   Months whose first copy is kept (default 12).
.PARAMETER FreeSpaceFactor  Free space needed, as a multiple of the file size (default 3).
.PARAMETER TestAllowSystemDrive  Tests only: lets a temp folder on the system drive stand in for the second disk.

Exit codes: 0 done, 1 error (for example a hash that does not match), 2 refused or disk missing.
#>
[CmdletBinding()]
param(
    [string]$DiskCopyRoot,
    [string]$BackupFolder = (Join-Path $env:ProgramData 'EtpReporting\Backups'),
    [string]$Database = 'EtpReporting',
    [int]$KeepDaily = 30,
    [int]$KeepMonthly = 12,
    [double]$FreeSpaceFactor = 3,
    [switch]$TestAllowSystemDrive
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Get-EtpDiskFileSha256 {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '') }
        finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Test-EtpDiskPathInside {
    param([string]$Path, [string]$Folder)
    $p = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $f = [IO.Path]::GetFullPath($Folder).TrimEnd('\') + '\'
    return $p.StartsWith($f, [StringComparison]::OrdinalIgnoreCase)
}

function Write-EtpDiskTextAtomically {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Text)
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllText($temporary, $Text, (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    Move-Item -LiteralPath $temporary -Destination $Path
}

function Write-EtpDiskLogLine {
    # One JSON object per line: names, sizes, hashes and reasons only.
    param([string]$LogFile, [string]$Event, [hashtable]$Data = @{})
    $entry = [ordered]@{ timeUtc = [DateTime]::UtcNow.ToString('o'); event = $Event }
    foreach ($key in $Data.Keys) { $entry[$key] = $Data[$key] }
    [IO.File]::AppendAllText($LogFile, (($entry | ConvertTo-Json -Compress -Depth 6) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
}

function Get-EtpDiskCopyMonth {
    # yyyy-MM from <database>-<yyyyMMdd>-<HHmmss>-..., else the current UTC month.
    param([string]$Name, [string]$Database)
    $m = [regex]::Match($Name, '^' + [regex]::Escape($Database) + '-(\d{4})(\d{2})\d{2}-')
    if ($m.Success -and [int]$m.Groups[2].Value -ge 1 -and [int]$m.Groups[2].Value -le 12) { return ('{0}-{1}' -f $m.Groups[1].Value, $m.Groups[2].Value) }
    return [DateTime]::UtcNow.ToString('yyyy-MM', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-EtpDiskCopiesToPrune {
    # Pure: from the copy names, the ones to delete. Keeps the newest $KeepDaily and, for each of the newest
    # $KeepMonthly calendar months, that month's FIRST copy. Names start <database>-<yyyyMMdd>-<HHmmss>, so name order
    # is time order.
    param([string[]]$Names, [string]$Database, [int]$KeepDaily, [int]$KeepMonthly)
    $sorted = @($Names | Sort-Object -Descending)
    $keep = @{}
    foreach ($n in @($sorted | Select-Object -First $KeepDaily)) { $keep[$n] = $true }
    if ($KeepMonthly -gt 0) {
        $pattern = '^' + [regex]::Escape($Database) + '-(\d{6})\d{2}-'
        $firstOfMonth = @{}
        foreach ($n in $sorted) {   # newest first, so the last one seen for a month is its first copy
            $m = [regex]::Match($n, $pattern)
            if ($m.Success) { $firstOfMonth[$m.Groups[1].Value] = $n }
        }
        foreach ($month in @($firstOfMonth.Keys | Sort-Object -Descending | Select-Object -First $KeepMonthly)) { $keep[$firstOfMonth[$month]] = $true }
    }
    return @($sorted | Where-Object { -not $keep.ContainsKey($_) })
}

function New-EtpDiskRefusal {
    # A refusal is a plain sentence and exit code 2, never a stack.
    param([string]$Message)
    $exception = New-Object System.InvalidOperationException($Message)
    $exception.Data['EtpRefusal'] = $true
    return $exception
}

function Assert-EtpDiskCopyRoot {
    param([string]$Root, [bool]$AllowSystemDrive)
    if ([string]::IsNullOrWhiteSpace($Root)) { throw (New-EtpDiskRefusal 'Say where to copy: -DiskCopyRoot G:\ETP-Backups.') }
    $full = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Root)).TrimEnd('\')
    $driveRoot = [IO.Path]::GetPathRoot($full)
    if ($full.StartsWith('\\')) { throw (New-EtpDiskRefusal 'The disk copy must go to a disk inside this PC, not a network share.') }
    if (-not (Test-Path -LiteralPath $driveRoot)) {
        throw (New-EtpDiskRefusal ("The backup disk {0} is not connected or not unlocked, so tonight's disk copy was not made." -f $driveRoot.TrimEnd('\')))
    }
    foreach ($name in 'OneDrive', 'OneDriveCommercial', 'OneDriveConsumer') {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value) -and (Test-EtpDiskPathInside -Path $full -Folder $value)) {
            throw (New-EtpDiskRefusal 'The disk copy folder is inside OneDrive. Choose a folder on the second disk that OneDrive does not sync.')
        }
    }
    if ($full -match '\\OneDrive[^\\]*(\\|$)') { throw (New-EtpDiskRefusal 'The disk copy folder looks like a OneDrive folder. Choose a folder on the second disk.') }
    $systemRoot = [IO.Path]::GetPathRoot($env:SystemRoot)
    if (-not $AllowSystemDrive -and [string]::Equals($driveRoot, $systemRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw (New-EtpDiskRefusal ("The disk copy folder is on the system drive {0}. It must be on the second disk, otherwise one failed disk loses both copies." -f $systemRoot.TrimEnd('\')))
    }
    return $full
}

function Copy-EtpBackupToDisk {
    param([string]$DiskCopyRoot, [string]$BackupFolder, [string]$Database, [int]$KeepDaily, [int]$KeepMonthly, [double]$FreeSpaceFactor, [bool]$AllowSystemDrive)
    if ($Database -notmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Choose a valid database name.' }
    if ($KeepDaily -lt 1) { throw 'KeepDaily must be at least 1.' }
    if ($KeepMonthly -lt 0) { throw 'KeepMonthly cannot be negative.' }
    $root = Assert-EtpDiskCopyRoot -Root $DiskCopyRoot -AllowSystemDrive $AllowSystemDrive

    $pointer = Join-Path $BackupFolder "$Database-latest-verified.json"
    if (-not (Test-Path -LiteralPath $pointer -PathType Leaf)) { throw "No verified backup receipt at $pointer. Run ETP's backup first." }
    $receipt = Get-Content -Raw -LiteralPath $pointer -Encoding UTF8 | ConvertFrom-Json
    if (-not $receipt.verified) { throw 'The latest backup receipt is not marked verified.' }
    $bak = [string]$receipt.backupPath
    if (-not (Test-EtpDiskPathInside -Path $bak -Folder $BackupFolder)) { throw 'The receipt points outside the backup folder; refusing.' }
    if (-not (Test-Path -LiteralPath $bak -PathType Leaf)) { throw "Backup file named in the receipt is missing: $bak" }
    $name = Split-Path -Leaf $bak
    if ($name -notmatch ('^' + [regex]::Escape($Database) + '-[A-Za-z0-9._-]+\.bak$')) { throw 'The backup file name is not one of ETP''s; refusing.' }
    $expected = ([string]$receipt.sha256).ToUpperInvariant()
    if ((Get-EtpDiskFileSha256 -Path $bak) -ne $expected) { throw 'The .bak no longer matches its verified receipt (SHA-256). Not copying it.' }
    $size = (Get-Item -LiteralPath $bak).Length

    if (-not (Test-Path -LiteralPath $root)) { [void](New-Item -ItemType Directory -Path $root -Force) }
    $free = (New-Object IO.DriveInfo([IO.Path]::GetPathRoot($root))).AvailableFreeSpace
    if ($free -lt ($size * $FreeSpaceFactor)) {
        throw (New-EtpDiskRefusal ("The backup disk has {0:N1} GB free; it needs at least {1:N1} GB ({2:0.#} times the backup file). Delete old copies or use a bigger disk." -f ($free / 1GB), ($size * $FreeSpaceFactor / 1GB), $FreeSpaceFactor))
    }

    $logFile = Join-Path $root 'disk-copy.log.jsonl'
    $month = Get-EtpDiskCopyMonth -Name $name -Database $Database
    $folder = Join-Path $root $month
    if (-not (Test-Path -LiteralPath $folder)) { [void](New-Item -ItemType Directory -Path $folder -Force) }
    foreach ($stale in @(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.part' -File -ErrorAction SilentlyContinue)) { Remove-Item -LiteralPath $stale.FullName -Force }
    $destination = Join-Path $folder $name
    $relative = "$month\$name"

    if (Test-Path -LiteralPath $destination -PathType Leaf) {
        if ((Get-EtpDiskFileSha256 -Path $destination) -ne $expected) { throw "A different file already has the name $relative on the backup disk. It was not replaced; check the disk." }
        Write-Host "Already on the backup disk: $relative"
    }
    else {
        $part = "$destination.part"
        Copy-Item -LiteralPath $bak -Destination $part -Force
        if ((Get-EtpDiskFileSha256 -Path $part) -ne $expected) {
            Remove-Item -LiteralPath $part -Force
            throw 'The disk copy did not match the verified backup (SHA-256). It was removed; run again.'
        }
        Move-Item -LiteralPath $part -Destination $destination
        if ((Get-EtpDiskFileSha256 -Path $destination) -ne $expected) {
            Remove-Item -LiteralPath $destination -Force
            throw 'The disk copy did not match the verified backup after renaming (SHA-256). It was removed; run again.'
        }
        Write-Host "Copied and verified: $relative"
    }

    $diskReceipt = [ordered]@{
        schemaVersion       = 1
        database            = $Database
        file                = $name
        relativePath        = $relative
        sha256              = $expected
        lengthBytes         = $size
        sourceVerifiedAtUtc = [string]$receipt.verifiedAtUtc
        sourceEncryption    = [string]$receipt.encryption
        copiedAtUtc         = [DateTime]::UtcNow.ToString('o')
        diskCopy            = 'PLAIN_BITLOCKER_DISK'
    }
    $text = $diskReceipt | ConvertTo-Json -Depth 5
    Write-EtpDiskTextAtomically -Path "$destination.receipt.json" -Text $text
    Write-EtpDiskTextAtomically -Path (Join-Path $root 'latest-disk-copy.json') -Text $text
    Write-EtpDiskLogLine -LogFile $logFile -Event 'Copied' -Data @{ file = $relative; sha256 = $expected; bytes = $size }

    $names = @(Get-ChildItem -LiteralPath $root -Recurse -Filter "$Database-*.bak" -File | Where-Object { $_.Directory.Name -match '^\d{4}-\d{2}$' } | ForEach-Object { $_.Name })
    foreach ($oldName in @(Get-EtpDiskCopiesToPrune -Names $names -Database $Database -KeepDaily $KeepDaily -KeepMonthly $KeepMonthly)) {
        foreach ($old in @(Get-ChildItem -LiteralPath $root -Recurse -Filter $oldName -File | Where-Object { $_.Directory.Name -match '^\d{4}-\d{2}$' })) {
            Remove-Item -LiteralPath $old.FullName -Force
            if (Test-Path -LiteralPath "$($old.FullName).receipt.json") { Remove-Item -LiteralPath "$($old.FullName).receipt.json" -Force }
            Write-EtpDiskLogLine -LogFile $logFile -Event 'Pruned' -Data @{ file = $oldName }
        }
    }
    foreach ($dir in @(Get-ChildItem -LiteralPath $root -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}$' })) {
        if (@(Get-ChildItem -LiteralPath $dir.FullName -Force).Count -eq 0) { Remove-Item -LiteralPath $dir.FullName -Force }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    try {
        Copy-EtpBackupToDisk -DiskCopyRoot $DiskCopyRoot -BackupFolder $BackupFolder -Database $Database -KeepDaily $KeepDaily -KeepMonthly $KeepMonthly `
            -FreeSpaceFactor $FreeSpaceFactor -AllowSystemDrive ([bool]$TestAllowSystemDrive)
        exit 0
    }
    catch {
        $exception = $_.Exception
        [Console]::Error.WriteLine($exception.Message)
        if ($exception.Data.Contains('EtpRefusal')) { exit 2 }
        exit 1
    }
}
