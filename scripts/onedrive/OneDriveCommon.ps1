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

# ---------------------------------------------------------------------------------------------------------------------
# 1.11.2 hardening (AUDIT-2 S-02/S-12, AUDIT-3 G2/G4). Keys are created here and never printed; only their SHA-256
# fingerprint is shown, so a USB copy can be matched with the original.
# ---------------------------------------------------------------------------------------------------------------------

function ConvertTo-EtpPcNameToken {
    # A PC name reduced to A-Z 0-9 _ - (upper case, at most 63 characters). Used in the manifest and compared with the
    # Workpc config, so a name holding ' or & (or anything else) never reaches a log, a health check or SQL raw.
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return '' }
    $token = ($Name.Trim().ToUpperInvariant() -replace '[^A-Z0-9_-]', '_')
    if ($token.Length -gt 63) { $token = $token.Substring(0, 63) }
    return $token
}

function Get-EtpOneDriveLocalRoots {
    $roots = @()
    foreach ($name in 'OneDrive', 'OneDriveCommercial', 'OneDriveConsumer') {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $roots += $value }
    }
    return $roots
}

function Assert-EtpKeyOutsideOneDrive {
    # Every ETP key (the backup key, the shop signing keys) must stay out of anything OneDrive syncs.
    param([string]$KeyFile, [string]$ConfiguredRoot)
    $roots = @(@(Get-EtpOneDriveLocalRoots) + @($ConfiguredRoot) | Where-Object { $_ })
    foreach ($root in $roots) {
        if (Test-EtpPathInside -Path $KeyFile -Folder $root) { throw "The key file '$KeyFile' is inside OneDrive ($root). Move it to a local folder that OneDrive does not sync." }
    }
    if ($KeyFile -match '\\OneDrive[^\\]*\\') { throw "The key file path '$KeyFile' looks like a OneDrive folder. Use a local folder that OneDrive does not sync." }
}

function Get-EtpKeyFingerprint {
    # SHA-256 of the raw key bytes, upper-case hex. Safe to show and to write down; it does not reveal the key.
    param([Parameter(Mandatory)][byte[]]$KeyBytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($KeyBytes)) -replace '-', '') }
    finally { $sha.Dispose() }
}

function Format-EtpFingerprint {
    # 64 hex characters in groups of 4: easier to compare by eye or to read out on the phone.
    param([string]$Fingerprint)
    return (($Fingerprint -split '(.{4})' | Where-Object { $_ }) -join ' ')
}

function Get-EtpKeyId {
    # Short public id of a key: the first 16 hex characters of its fingerprint.
    param([Parameter(Mandatory)][byte[]]$KeyBytes)
    return (Get-EtpKeyFingerprint -KeyBytes $KeyBytes).Substring(0, 16)
}

function New-EtpSecretKeyFile {
    # Creates a random key file (base64 text) that only the current user, Administrators and SYSTEM can read.
    # The permissions are set on the EMPTY file before the key is written. If they cannot be set, the file is deleted
    # and this throws (S-12a: a key must never be left readable by the SQL service or the automation account).
    # Returns the key's fingerprint.
    param([Parameter(Mandatory)][string]$KeyFile, [Parameter(Mandatory)][int]$Bytes)
    if (Test-Path -LiteralPath $KeyFile) { throw "Key file already exists: $KeyFile. It is not replaced." }
    $directory = Split-Path -Parent $KeyFile
    if (-not (Test-Path -LiteralPath $directory)) { [void](New-Item -ItemType Directory -Path $directory -Force) }
    $stream = [IO.File]::Open($KeyFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $stream.Dispose()
    try {
        $acl = Get-Acl -LiteralPath $KeyFile
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }
        foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-32-544', 'S-1-5-18')) {
            $rule = New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'Allow')
            $acl.AddAccessRule($rule)
        }
        Set-Acl -LiteralPath $KeyFile -AclObject $acl
    }
    catch {
        Remove-Item -LiteralPath $KeyFile -Force -ErrorAction SilentlyContinue
        throw "The key file's permissions could not be restricted, so no key was created: $($_.Exception.Message)"
    }
    $key = New-Object byte[] $Bytes
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($key) } finally { $rng.Dispose() }
    [IO.File]::WriteAllText($KeyFile, [Convert]::ToBase64String($key), [Text.Encoding]::ASCII)
    return (Get-EtpKeyFingerprint -KeyBytes $key)
}

function Read-EtpSecretKeyBytes {
    param([Parameter(Mandatory)][string]$KeyFile, [Parameter(Mandatory)][int]$Bytes, [string]$What = 'key')
    if (-not (Test-Path -LiteralPath $KeyFile -PathType Leaf)) { throw "The $What file was not found: $KeyFile" }
    try { $key = [Convert]::FromBase64String(((Get-Content -Raw -LiteralPath $KeyFile -Encoding ASCII).Trim())) }
    catch { throw "The $What file '$KeyFile' is not a valid ETP key." }
    if ($key.Length -ne $Bytes) { throw "The $What file '$KeyFile' is not a valid ETP key (expected $Bytes bytes)." }
    return ,$key
}

# ---- Shop manifest signature (S-02) ---------------------------------------------------------------------------------
$script:EtpManifestSchemaVersion = 2
$script:EtpSigningKeyBytes = 32

function Get-EtpManifestCanonicalText {
    # The exact text the HMAC covers: schema, role, day, pcName, keyId, createdLocal and every file as name|size|sha256,
    # files in ordinal order. Send and Receive both build it from the parsed manifest, so JSON spacing and property
    # order never matter.
    param([Parameter(Mandatory)]$Manifest)
    $files = New-Object 'System.Collections.Generic.List[string]'
    foreach ($f in @(Get-EtpConfigValue $Manifest 'files' @())) {
        if ($null -eq $f) { continue }
        $size = [int64](Get-EtpConfigValue $f 'sizeBytes' 0)
        $files.Add(('file={0}|{1}|{2}' -f [string](Get-EtpConfigValue $f 'name' ''), $size.ToString([Globalization.CultureInfo]::InvariantCulture), ([string](Get-EtpConfigValue $f 'sha256' '')).ToUpperInvariant()))
    }
    $sorted = $files.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $lines = @(
        'ETP-SHOP-MANIFEST',
        ('schemaVersion={0}' -f [string](Get-EtpConfigValue $Manifest 'schemaVersion' '')),
        ('role={0}' -f [string](Get-EtpConfigValue $Manifest 'role' '')),
        ('day={0}' -f [string](Get-EtpConfigValue $Manifest 'day' '')),
        ('pcName={0}' -f [string](Get-EtpConfigValue $Manifest 'pcName' '')),
        ('keyId={0}' -f [string](Get-EtpConfigValue $Manifest 'keyId' '')),
        ('createdLocal={0}' -f [string](Get-EtpConfigValue $Manifest 'createdLocal' ''))
    ) + @($sorted)
    return ($lines -join "`n")
}

function Get-EtpManifestHmac {
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][byte[]]$Key)
    $hmac = New-Object Security.Cryptography.HMACSHA256(,$Key)
    try { return ([BitConverter]::ToString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes((Get-EtpManifestCanonicalText -Manifest $Manifest)))) -replace '-', '') }
    finally { $hmac.Dispose() }
}

function Test-EtpManifestHmac {
    # Constant-time comparison of the manifest's "hmac" with the one computed with the key.
    param([Parameter(Mandatory)]$Manifest, [Parameter(Mandatory)][byte[]]$Key)
    $claimed = ([string](Get-EtpConfigValue $Manifest 'hmac' '')).ToUpperInvariant()
    $expected = Get-EtpManifestHmac -Manifest $Manifest -Key $Key
    if ($claimed.Length -ne $expected.Length) { return $false }
    $difference = 0
    for ($i = 0; $i -lt $expected.Length; $i++) { $difference = $difference -bor ([int][char]$claimed[$i] -bxor [int][char]$expected[$i]) }
    return ($difference -eq 0)
}

# ---- OneDrive client checks (G4) ------------------------------------------------------------------------------------

function Test-EtpOneDriveClientRunning {
    # True when OneDrive.exe runs for the current Windows user, in any session (a scheduled task runs in its own one).
    $me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    try {
        $processes = @(Get-Process -Name 'OneDrive' -IncludeUserName -ErrorAction Stop)
        return (@($processes | Where-Object { $_.UserName -and ([string]$_.UserName) -ieq $me }).Count -gt 0)
    }
    catch {
        # -IncludeUserName needs elevation. Unelevated, a visible OneDrive process is the best evidence available.
        return (@(Get-Process -Name 'OneDrive' -ErrorAction SilentlyContinue).Count -gt 0)
    }
}

function Assert-EtpOneDriveClientRunning {
    param([string]$LogFile)
    if (Test-EtpOneDriveClientRunning) { return }
    $me = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    if ($LogFile) { Write-EtpLogLine -LogFile $LogFile -Event 'OneDriveNotRunning' -Data @{ user = $me } }
    throw "OneDrive is not running for $me, so nothing would reach the cloud. Sign in to Windows as this user and start OneDrive (cloud icon by the clock), then run this again."
}

function Get-EtpOneDriveSyncState {
    # Windows' own "Status" column for a file in a cloud-synced folder (System.StorageProviderState), or $null when it
    # cannot be read (not a synced folder, or no shell in this session). Values per the Windows SDK (propkey.h):
    # 1 sparse, 2 in sync, 3 pinned, 4 pending upload, 5 pending download, 6 transferring, 7 error.
    param([Parameter(Mandatory)][string]$Path)
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.Namespace((Split-Path -Parent $Path))
        if ($null -eq $folder) { return $null }
        $item = $folder.ParseName((Split-Path -Leaf $Path))
        if ($null -eq $item) { return $null }
        $value = $item.ExtendedProperty('System.StorageProviderState')
        if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { return $null }
        return [int]$value
    }
    catch { return $null }
}

function Wait-EtpOneDriveUploaded {
    # Returns 'Synced', or 'Unknown' when Windows gives no status (the caller logs a warning; not a failure).
    # Throws when OneDrive reports an error, or when the file is still waiting to upload once the time is up.
    param([Parameter(Mandatory)][string]$Path, [int]$TimeoutMinutes = 30, [int]$PollSeconds = 15)
    $deadline = [DateTime]::UtcNow.AddMinutes([Math]::Max(0, $TimeoutMinutes))
    $state = $null
    while ($true) {
        $state = Get-EtpOneDriveSyncState -Path $Path
        if ($null -ne $state -and @(1, 2, 3) -contains $state) { return 'Synced' }
        if ($state -eq 7) { throw "OneDrive reports a sync error for '$Path'. Open OneDrive (cloud icon by the clock) and fix it, then run this again." }
        if ([DateTime]::UtcNow -ge $deadline) { break }
        Start-Sleep -Seconds $PollSeconds
    }
    if ($null -ne $state -and @(4, 5, 6) -contains $state) { throw "OneDrive had not finished uploading '$Path' after $TimeoutMinutes minute(s) (status $state). Check that OneDrive is signed in and online, then run this again." }
    return 'Unknown'
}

# ---- Live shops (1.12.1 lane P7, decision 51) ----------------------------------------------------------------------
# One small file says which shops are LIVE and from which business day, so a shop that is not sending yet is never reported
# missing. ETP reads the same file (ShopLiveFile in the Application layer), so the receive script, the Today checklist,
# DATA-01 and Morning agree. Format (all dates yyyy-MM-dd):
#   { "shops": [ { "role": "TITAN",  "storeCodes": ["WLMHW"], "sendingFrom": "2026-10-20" },
#                { "role": "HELIOS", "storeCodes": ["HEMW"],  "sendingFrom": null } ] }
# A shop with no sendingFrom is not live yet. A role or store the file does not list is expected, and so is everything
# when the file does not exist (the behaviour before 1.12.1). Environment variable ETP_SHOP_LIVE_FILE overrides the path.

function Get-EtpShopLivePath {
    param($Config)
    $override = $env:ETP_SHOP_LIVE_FILE
    if (-not [string]::IsNullOrWhiteSpace($override)) { return $override }
    $fromConfig = [string](Get-EtpConfigValue $Config 'shopLiveFile' '')
    if ($fromConfig) { return [Environment]::ExpandEnvironmentVariables($fromConfig) }
    return (Join-Path $env:ProgramData 'EtpReporting\Operations\shop-live.json')
}

function ConvertTo-EtpLiveDay {
    # ConvertFrom-Json in PowerShell 7 turns an ISO date string into a [datetime]; 5.1 keeps the string. Accept both.
    param($Value, [string]$Role)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return $Value.Date }
    $text = ([string]$Value).Trim()
    if (-not $text) { return $null }
    $parsed = [datetime]::MinValue
    if (-not [datetime]::TryParseExact($text, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) {
        throw "Shop live file: sendingFrom '$text' for $Role is not a date (use yyyy-MM-dd)."
    }
    return $parsed.Date
}

function Read-EtpShopLive {
    # Returns @{ ROLE = [datetime] or $null } for the roles the file lists ($null = listed, not live yet). A missing file
    # returns an empty table (every role expected). An unreadable or invalid file THROWS: guessing could hide a real gap.
    param([Parameter(Mandatory)][string]$Path)
    $table = @{}
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $table }
    try { $doc = Get-Content -Raw -LiteralPath $Path -Encoding UTF8 | ConvertFrom-Json }
    catch { throw "Shop live file '$Path' is not valid JSON: $($_.Exception.Message)" }
    foreach ($shop in @(Get-EtpConfigValue $doc 'shops' @())) {
        $role = ([string](Get-EtpConfigValue $shop 'role' '')).ToUpperInvariant()
        if ($script:EtpValidRoles -notcontains $role) { throw "Shop live file: unknown role '$role' (use TITAN, HELIOS or SERVICE)." }
        if ($table.ContainsKey($role)) { throw "Shop live file: role $role is listed twice." }
        $table[$role] = ConvertTo-EtpLiveDay -Value (Get-EtpConfigValue $shop 'sendingFrom' $null) -Role $role
    }
    return $table
}

function Test-EtpRoleExpected {
    # True when files from the role are expected for the business day.
    param([hashtable]$Live, [string]$Role, [datetime]$Day)
    if (-not $Live.ContainsKey($Role)) { return $true }
    $from = $Live[$Role]
    if ($null -eq $from) { return $false }
    return ($Day.Date -ge ([datetime]$from).Date)
}
