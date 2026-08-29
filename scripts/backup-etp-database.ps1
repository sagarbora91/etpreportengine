param(
    [string]$ServerInstance = ".\SQLEXPRESS",
    [string]$Database = "EtpReporting",
    [string]$BackupDirectory = "$env:ProgramData\EtpReporting\Backups",
    [ValidateRange(0, 1048576)][double]$MinimumFreeSpaceGb = 0,
    [string]$ResultPath,
    [string]$SqlCmdPath
)

$ErrorActionPreference = "Stop"
$sqlcmd = $SqlCmdPath
if ([string]::IsNullOrWhiteSpace($sqlcmd)) { $sqlcmd = (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue).Source }
if ([string]::IsNullOrWhiteSpace($sqlcmd)) {
    $sqlcmd = @(
        (Join-Path $env:ProgramFiles 'sqlcmd\sqlcmd.exe'),
        'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not (Test-Path -LiteralPath $sqlcmd)) { throw "SQLCMD is not installed at the expected SQL Server tools path." }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Database must contain only letters, numbers, or underscore." }
$resolvedDirectory = [System.IO.Path]::GetFullPath($BackupDirectory)
New-Item -ItemType Directory -Path $resolvedDirectory -Force | Out-Null
$drive = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($resolvedDirectory))
$freeGbExact = $drive.AvailableFreeSpace / 1GB
$freeGb = [math]::Round($freeGbExact, 2)
if ($MinimumFreeSpaceGb -gt 0 -and $freeGbExact -lt $MinimumFreeSpaceGb) {
    throw "Backup storage has $freeGb GB free; at least $MinimumFreeSpaceGb GB is required before this backup can start."
}
if ($freeGb -lt 5) { Write-Warning "BACKUP_SPACE_CRITICAL: Backup storage has only $freeGb GB free. Add storage immediately." }
elseif ($freeGb -lt 20) { Write-Warning "BACKUP_SPACE_LOW: Backup storage has only $freeGb GB free. Plan additional storage." }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
$uniqueSuffix = [Guid]::NewGuid().ToString("N")
$backupPath = Join-Path $resolvedDirectory "$Database-$stamp-$uniqueSuffix.bak"
if (Test-Path -LiteralPath $backupPath) { throw "Refusing to overwrite an existing database backup." }
$escapedPath = $backupPath.Replace("'", "''")
& $sqlcmd -S $ServerInstance -E -b -Q "BACKUP DATABASE [$Database] TO DISK=N'$escapedPath' WITH COPY_ONLY, CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK=N'$escapedPath' WITH CHECKSUM;"
if ($LASTEXITCODE -ne 0) { throw "Database backup or verification failed." }
$auditActor = ([Environment]::UserName).Replace("'", "''")
& $sqlcmd -S $ServerInstance -E -b -d $Database -Q "IF COL_LENGTH('dbo.operational_audit','actor_name') IS NOT NULL INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name) VALUES('Backup','Succeeded','Checksum backup verified','operations',N'$auditActor');"
if ($LASTEXITCODE -ne 0) { throw "Database backup succeeded but its audit event could not be recorded." }
$backupFile = Get-Item -LiteralPath $backupPath
$backupHash = Get-FileHash -LiteralPath $backupPath -Algorithm SHA256
$receipt = [ordered]@{
    schemaVersion = 1
    verified = $true
    serverInstance = $ServerInstance
    database = $Database
    backupPath = $backupFile.FullName
    sha256 = $backupHash.Hash
    lengthBytes = $backupFile.Length
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
}
if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
    $resolvedResultPath = [System.IO.Path]::GetFullPath($ResultPath)
    if (Test-Path -LiteralPath $resolvedResultPath) { throw "Refusing to overwrite an existing backup verification receipt." }
    $resultDirectory = Split-Path -Parent $resolvedResultPath
    if ([string]::IsNullOrWhiteSpace($resultDirectory)) { throw "The backup verification receipt requires a parent directory." }
    New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
    $temporaryResultPath = "$resolvedResultPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $receipt | ConvertTo-Json | Set-Content -LiteralPath $temporaryResultPath -Encoding utf8
        Move-Item -LiteralPath $temporaryResultPath -Destination $resolvedResultPath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryResultPath) { Remove-Item -LiteralPath $temporaryResultPath -Force }
    }
}
$backupHash | Format-List
