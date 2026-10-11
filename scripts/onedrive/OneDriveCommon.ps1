# Shared helpers for the ETP OneDrive daily-file scripts. Dot-source this file; it runs nothing by itself.
# Windows PowerShell 5.1 compatible (no pwsh-only syntax). No secrets are read or written here.

Set-StrictMode -Version 2.0

$script:EtpValidRoles = @('TITAN', 'HELIOS', 'SERVICE')

function Read-EtpOneDriveConfig {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Config file not found: $Path. Copy the matching .example.json next to the script, rename it (remove '.example') and edit it."
    }
    try { return (Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json) }
    catch { throw "Config file '$Path' is not valid JSON: $($_.Exception.Message)" }
}

function Get-EtpConfigValue {
    # Reads an optional property of a parsed config object (strict mode safe).
    param($Config, [string]$Name, $Default = $null)
    if ($null -ne $Config -and $Config.PSObject.Properties.Name -contains $Name) {
        $value = $Config.$Name
        if ($null -ne $value -and -not ($value -is [string] -and [string]::IsNullOrWhiteSpace($value))) { return $value }
    }
    return $Default
}

function Resolve-EtpOneDriveRoot {
    # Override from config wins; otherwise the folder Windows says OneDrive syncs ($env:OneDrive, then OneDriveCommercial/Consumer).
    param([string]$Override)
    $candidate = $Override
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        foreach ($name in 'OneDrive', 'OneDriveCommercial', 'OneDriveConsumer') {
            $value = [Environment]::GetEnvironmentVariable($name)
            if (-not [string]::IsNullOrWhiteSpace($value)) { $candidate = $value; break }
        }
    }
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw 'OneDrive folder not found. Sign in to OneDrive on this PC, or set "oneDriveRoot" in the config file.'
    }
    $candidate = [Environment]::ExpandEnvironmentVariables($candidate).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { throw "OneDrive folder does not exist: $candidate" }
    return $candidate
}

function Test-EtpPathInside {
    param([string]$Path, [string]$Folder)
    $p = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $f = [IO.Path]::GetFullPath($Folder).TrimEnd('\') + '\'
    return $p.StartsWith($f, [StringComparison]::OrdinalIgnoreCase)
}

function Get-EtpFileSha256 {
    param([Parameter(Mandatory)][string]$Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '') }
        finally { $sha.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Test-EtpFileLocked {
    # True when another program has the file open for writing (cannot be opened exclusively for reading).
    param([string]$Path)
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        $stream.Dispose()
        return $false
    }
    catch [IO.IOException] { return $true }
    catch [UnauthorizedAccessException] { return $true }
}

function Test-EtpFileStable {
    # Stable = size and last-write time unchanged across the wait, and the file can be opened exclusively.
    param([string]$Path, [int]$StableSeconds = 10)
    $first = Get-Item -LiteralPath $Path -Force
    if ($StableSeconds -gt 0) { Start-Sleep -Seconds $StableSeconds }
    $second = Get-Item -LiteralPath $Path -Force
    if ($first.Length -ne $second.Length -or $first.LastWriteTimeUtc -ne $second.LastWriteTimeUtc) { return $false }
    return (-not (Test-EtpFileLocked -Path $Path))
}

function Test-EtpOneDrivePlaceholder {
    # A "Files On-Demand" placeholder is not downloaded yet: reading it would trigger a download or fail.
    param([string]$Path)
    $attributes = [int](Get-Item -LiteralPath $Path -Force).Attributes
    $offline = 0x1000          # FILE_ATTRIBUTE_OFFLINE
    $recallOnOpen = 0x40000    # FILE_ATTRIBUTE_RECALL_ON_OPEN
    $recallOnData = 0x400000   # FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    return (($attributes -band ($offline -bor $recallOnOpen -bor $recallOnData)) -ne 0)
}

function Write-EtpTextAtomically {
    # Writes UTF-8 (no BOM) to a temp name beside the target, then renames. Readers never see a half file.
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Text)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) { [void](New-Item -ItemType Directory -Path $directory -Force) }
    $temporary = "$Path.$([Guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllText($temporary, $Text, (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    Move-Item -LiteralPath $temporary -Destination $Path
}

function Write-EtpLogLine {
    # One JSON object per line. Never log file contents; names, sizes, hashes and reasons only.
    param([Parameter(Mandatory)][string]$LogFile, [Parameter(Mandatory)][string]$Event, [hashtable]$Data = @{})
    $directory = Split-Path -Parent $LogFile
    # -WhatIf:$false - a dry run still keeps its log (the caller's -WhatIf must not stop the log folder being created).
    if (-not (Test-Path -LiteralPath $directory)) { [void](New-Item -ItemType Directory -Path $directory -Force -WhatIf:$false) }
    $entry = [ordered]@{ timeUtc = [DateTime]::UtcNow.ToString('o'); event = $Event }
    foreach ($key in $Data.Keys) { $entry[$key] = $Data[$key] }
    $line = ($entry | ConvertTo-Json -Compress -Depth 6)
    [IO.File]::AppendAllText($LogFile, $line + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

function ConvertTo-EtpSafeFileName {
    param([string]$Name)
    return ($Name -replace '[\/:*?"<>|]', '_')
}
