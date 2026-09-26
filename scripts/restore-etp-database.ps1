param(
    # Copied into a protected staging folder first, so SQL Server restores exactly the file
    # whose hash was checked, wherever the original lives (the SQL service cannot read an
    # administrator-only folder on a USB drive, for one).
    [Parameter(Mandatory)][string]$BackupPath,
    # Optional <backup>.receipt.json from the old PC; the backup must match its sha256 (and lengthBytes).
    [string]$ReceiptPath,
    [string]$SqlCmdPath
)
# Makes an ETP backup from another PC this PC's live database. Run it elevated, as a SQL
# administrator, after setup has prepared SQL Server and the protected configuration with
# "Create a new empty database" unticked, and before setup runs again: that next run takes a
# verified safety backup of the restored data and then applies the newer database updates.
# It never replaces an existing database - there is no WITH REPLACE anywhere in ETP.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')

function Get-EtpRestoreJsonValue {
    # Common sets StrictMode, under which reading a property a receipt does not have throws.
    param([object]$Object,[Parameter(Mandatory)][string]$Name)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Assert-EtpRestoreBackupPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Give the full path of the backup file.' }
    $network = $Path.StartsWith('\\') -or $Path.StartsWith('//')
    if (-not $network -and $Path -notmatch '^[A-Za-z]:[\\/]') { throw 'Give the full path of the backup file.' }
    # A share, or a device path such as \\?\, is refused as well: SQL Server reads the file as
    # its own service account, and what was checked must be what it reads.
    if ($network) { throw 'Copy the backup onto this PC or a drive attached to it first.' }
    try { $full = [IO.Path]::GetFullPath($Path) } catch { throw 'Give the full path of the backup file.' }
    if (([IO.DriveInfo]::new($full.Substring(0,1))).DriveType -eq [IO.DriveType]::Network) { throw 'Copy the backup onto this PC or a drive attached to it first.' }
    if ([IO.Path]::GetExtension($full) -ine '.bak') { throw 'Choose a SQL Server backup file, ending in .bak.' }
    Assert-EtpNoLinks $full
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw 'The backup file was not found.' }
    $etpRoot = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'EtpReporting')).TrimEnd('\') + '\'
    if ($full.StartsWith($etpRoot,[StringComparison]::OrdinalIgnoreCase)) {
        throw "Keep the old PC's backup outside this PC's ETP folders; rotation and the recovery drill treat files there as this PC's own. Move it elsewhere and run this again."
    }
    return $full
}

function Assert-EtpRestoreReceipt {
    # Read-EtpVerifiedReceipt is not used: it requires the backup to sit in this PC's backup
    # folder, and a receipt from another PC names that PC's folder. Only what identifies the
    # file is checked here; SQL Server's own verification follows.
    param([Parameter(Mandatory)][string]$ReceiptPath,[Parameter(Mandatory)][string]$Database,
          [Parameter(Mandatory)][string]$Sha256,[Parameter(Mandatory)][long]$LengthBytes)
    try { $full = [IO.Path]::GetFullPath($ReceiptPath) } catch { throw 'Give the full path of the receipt file.' }
    Assert-EtpNoLinks $full
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw 'The receipt file was not found.' }
    $receipt = $null
    try { $receipt = Get-Content -Raw -LiteralPath $full | ConvertFrom-Json } catch { $receipt = $null }
    if ($receipt -isnot [Management.Automation.PSCustomObject]) { throw 'The receipt file is not a backup receipt this helper can read.' }
    $recorded = Get-EtpRestoreJsonValue $receipt 'sha256'
    if ($recorded -isnot [string] -or $recorded -notmatch '^[A-Fa-f0-9]{64}$') { throw 'The receipt has no valid SHA-256 value.' }
    $verified = Get-EtpRestoreJsonValue $receipt 'verified'
    if ($null -ne $verified -and -not ($verified -is [bool] -and $verified)) { throw 'The receipt does not record a verified backup.' }
    $receiptDatabase = Get-EtpRestoreJsonValue $receipt 'database'
    if ($null -ne $receiptDatabase -and [string]$receiptDatabase -cne $Database) { throw "The receipt is for another database, not $Database." }
    if ([string](Get-EtpRestoreJsonValue $receipt 'encryption') -ceq 'AES_256') {
        throw 'This backup is encrypted. This helper restores unencrypted backups only (SQL Server Express and Web take no other kind); import its recovery certificate and restore it by hand, following docs\OPERATIONS.md, Second-machine recovery exercise, step 2 onwards.'
    }
    if ($recorded -ine $Sha256) { throw 'The backup file does not match its receipt (SHA-256 differs). Nothing was restored.' }
    $recordedLength = Get-EtpRestoreJsonValue $receipt 'lengthBytes'
    if ($null -ne $recordedLength) {
        $expected = [long]0
        if (-not [long]::TryParse([string]$recordedLength, [ref]$expected) -or $expected -ne $LengthBytes) { throw 'The backup file does not match its receipt (its size differs). Nothing was restored.' }
    }
}

function ConvertFrom-EtpRestoreHeader {
    # RESTORE HEADERONLY: one row per backup set, fields separated by |. Only the leading
    # columns are read - BackupType (2), Position (5), DatabaseName (9) - which have not
    # moved since SQL Server 2008; the later ones differ between versions.
    param([AllowEmptyCollection()][AllowNull()][string[]]$Lines,[Parameter(Mandatory)][string]$Database)
    $rows = @($Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($rows.Count -eq 0) { throw 'SQL Server did not describe this file as a backup. Nothing was restored.' }
    if ($rows.Count -gt 1) { throw 'This file holds more than one backup. Restore it by hand as described in docs\OPERATIONS.md. Nothing was restored.' }
    $fields = $rows[0].Split('|')
    if ($fields.Count -lt 10) { throw 'SQL Server described this backup in a form this helper does not recognise. Nothing was restored.' }
    if ($fields[2].Trim() -cne '1') { throw 'This file is not a full database backup. Nothing was restored.' }
    if ($fields[5].Trim() -cne '1') { throw 'This file holds more than one backup. Restore it by hand as described in docs\OPERATIONS.md. Nothing was restored.' }
    $name = $fields[9].Trim()
    if ($name -ine $Database) { throw "This file is a backup of database '$name', not $Database. Nothing was restored." }
    return [pscustomobject]@{ DatabaseName = $name }
}

function ConvertFrom-EtpRestoreFileList {
    # RESTORE FILELISTONLY, parsed from its leading columns - LogicalName (0), Type (2),
    # Size (4), IsPresent (19) - rather than inserted into a fixed 22-column table as the
    # broker's drill does: the full column set on SQL Server 2025 is unverified, and these
    # have not moved since 2008. One data file and one log file, which is what ETP creates,
    # are restored here; any other layout is left to a person.
    param([AllowEmptyCollection()][AllowNull()][string[]]$Lines,[Parameter(Mandatory)][string]$Database)
    $layout = "This backup's file layout is not one this helper restores (it needs exactly one data file and one log file). Restore it by hand as described in docs\OPERATIONS.md. Nothing was restored."
    $data = @(); $log = @()
    foreach ($row in @($Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $fields = $row.Split('|')
        if ($fields.Count -lt 20) { throw $layout }
        $name = $fields[0]
        if ($name.Length -lt 1 -or $name.Length -gt 128 -or $name -match '[\x00-\x1F\x7F]') { throw $layout }
        $size = [long]0
        if (-not [long]::TryParse($fields[4].Trim(), [ref]$size) -or $size -lt 0) { throw $layout }
        if ($fields[19].Trim() -cne '1') { throw $layout }
        $entry = [pscustomobject]@{ LogicalName = $name; Bytes = $size }
        switch -CaseSensitive ($fields[2].Trim()) {
            'D' { $data += $entry }
            'L' { $log += $entry }
            default { throw $layout }
        }
    }
    if ($data.Count -ne 1 -or $log.Count -ne 1) { throw $layout }
    return [pscustomobject]@{ DataLogicalName = $data[0].LogicalName; LogLogicalName = $log[0].LogicalName; DataBytes = $data[0].Bytes; LogBytes = $log[0].Bytes }
}

function New-EtpRestoreDatabaseSql {
    # RESTORE takes variables for the disk and for both sides of MOVE, so the staged path and
    # the logical names only ever appear as N'' literals, with every quote doubled, and never
    # in dynamic SQL. The database name was validated as letters, digits and underscores.
    param([Parameter(Mandatory)][string]$Database,[Parameter(Mandatory)][string]$BackupFile,
          [Parameter(Mandatory)][string]$DataLogicalName,[Parameter(Mandatory)][string]$LogLogicalName)
    if ($Database -notmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Choose a valid database name.' }
    $disk = $BackupFile.Replace("'","''")
    $dataName = $DataLogicalName.Replace("'","''")
    $logName = $LogLogicalName.Replace("'","''")
    return @"
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @disk nvarchar(520)=N'$disk';
DECLARE @dataName nvarchar(128)=N'$dataName';
DECLARE @logName nvarchar(128)=N'$logName';
-- Checked in the batch that restores, so nothing can create the database in between.
-- An existing database is never overwritten; nothing in ETP restores over one.
IF DB_ID(N'$Database') IS NOT NULL THROW 51901,N'The database already exists. Nothing was restored or replaced.',1;
DECLARE @dataFolder nvarchar(260)=CONVERT(nvarchar(260),SERVERPROPERTY('InstanceDefaultDataPath'));
DECLARE @logFolder nvarchar(260)=CONVERT(nvarchar(260),SERVERPROPERTY('InstanceDefaultLogPath'));
IF NULLIF(@dataFolder,N'') IS NULL OR NULLIF(@logFolder,N'') IS NULL THROW 51902,N'SQL Server did not report its default data and log folders.',1;
IF RIGHT(@dataFolder,1)<>N'\' SET @dataFolder+=N'\';
IF RIGHT(@logFolder,1)<>N'\' SET @logFolder+=N'\';
DECLARE @dataFile nvarchar(520)=@dataFolder+N'$Database.mdf';
DECLARE @logFile nvarchar(520)=@logFolder+N'${Database}_log.ldf';
IF EXISTS(SELECT 1 FROM sys.dm_os_file_exists(@dataFile) WHERE file_exists=1) OR EXISTS(SELECT 1 FROM sys.dm_os_file_exists(@logFile) WHERE file_exists=1)
 THROW 51903,N'A database file of that name is already in SQL Server''s data or log folder. Nothing was overwritten.',1;
RESTORE DATABASE [$Database] FROM DISK=@disk WITH MOVE @dataName TO @dataFile, MOVE @logName TO @logFile, CHECKSUM, RECOVERY, STATS=10;
"@
}

function New-EtpOwnerRecoverySql {
    # The documented Owner recovery (docs\OPERATIONS.md, "Owner recovery and maintenance")
    # for the Windows account running the restore and nobody else: SUSER_SNAME() is taken
    # inside SQL Server, so no text from the command line reaches this batch. It is the
    # application's own Settings > Users change - dbo.configure_application_role, then the
    # MERGE into dbo.application_users - in one transaction, with a recorded reason. The
    # history trigger audits it and keeps its last-Owner guard. The old PC's rows are left as
    # they are. ALTER AUTHORIZATION is not used: a login that restores a backup becomes its
    # server-level owner without becoming the dbo inside it, so the procedure gives the
    # account its own user, db_owner and etp_owner. The last line proves the result either way.
    return @'
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @identity nvarchar(200)=SUSER_SNAME();
DECLARE @display nvarchar(200)=CASE WHEN CHARINDEX(N'\',@identity)>0 THEN SUBSTRING(@identity,CHARINDEX(N'\',@identity)+1,200) ELSE @identity END;
DECLARE @reason nvarchar(500)=N'Owner recovery after restoring this database onto '+CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'))+N' with restore-etp-database.ps1';
IF SUSER_SID(@identity) IS NULL THROW 51910,N'Windows could not resolve the account running the restore.',1;
BEGIN TRANSACTION;
EXEC dbo.configure_application_role @identity=@identity,@role='OWNER',@active=1;
MERGE dbo.application_users WITH(HOLDLOCK) AS target
USING(SELECT @identity windows_identity) source ON target.windows_identity=source.windows_identity
WHEN MATCHED THEN UPDATE SET role_code='OWNER',is_active=1,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
WHEN NOT MATCHED THEN INSERT(windows_identity,display_name,role_code,is_active,modified_by,change_reason)
  VALUES(@identity,@display,'OWNER',1,SUSER_SNAME(),@reason);
COMMIT TRANSACTION;
DECLARE @principal sysname=(SELECT TOP(1) name FROM sys.database_principals WHERE sid=SUSER_SID(@identity));
SELECT N'ETP_OWNER:'+CASE WHEN EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=@identity AND role_code='OWNER' AND is_active=1)
  AND (@principal=N'dbo' OR IS_ROLEMEMBER(N'etp_owner',@principal)=1) THEN N'1' ELSE N'0' END;
'@
}

function Get-EtpRestoreMarkers {
    # Every value SQL Server printed after NAME: in a marker query.
    param([AllowEmptyCollection()][AllowNull()][string[]]$Lines,[Parameter(Mandatory)][string]$Name)
    $prefix = $Name + ':'
    return @($Lines | ForEach-Object { "$_".Trim() } | Where-Object { $_.StartsWith($prefix,[StringComparison]::Ordinal) } | ForEach-Object { $_.Substring($prefix.Length) })
}

function Get-EtpRestoreMarker {
    # The one value SQL Server printed after NAME:, or a refusal.
    param([AllowEmptyCollection()][AllowNull()][string[]]$Lines,[Parameter(Mandatory)][string]$Name)
    $found = @(Get-EtpRestoreMarkers -Lines $Lines -Name $Name)
    if ($found.Count -ne 1) { throw "SQL Server did not answer a restore check as expected ($Name). Nothing was restored." }
    return $found[0]
}

function Invoke-EtpRestoreRows {
    # Like Invoke-EtpSql, but without column headers and with trailing blanks removed, for
    # the RESTORE ... ONLY listings that are parsed field by field. stderr is never shown.
    param([Parameter(Mandatory)][string]$SqlCmd,[Parameter(Mandatory)][string]$Server,[Parameter(Mandatory)][string]$Query,[Parameter(Mandatory)][string]$Failure)
    Assert-EtpLocalSqlTarget $Server 'master'
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $rows = @(& $SqlCmd -x -S $Server -E -b -r 1 -d master -h -1 -W -s '|' -Q $Query 2>$null)
        $exitCode = $LASTEXITCODE
    }
    catch { throw $Failure }
    finally { $ErrorActionPreference = $previousPreference }
    if ($exitCode -ne 0) { throw $Failure }
    return $rows
}

# Dot-sourcing exposes only the functions above, for behavioral tests.
if ($MyInvocation.InvocationName -eq '.') { return }

$script:staging = $null
$script:logPath = $null

function Write-RestoreLog([string]$Message) {
    if ($script:logPath -and (Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($script:logPath)) -PathType Container)) {
        Add-Content -LiteralPath $script:logPath -Value "$(Get-Date -Format o) $Message" -Encoding utf8
    }
    Write-Host $Message
}

function Remove-EtpRestoreStaging {
    # Only the folder this run created, recognised by its exact name inside ETP's own folder.
    if (-not $script:staging) { return }
    $folder = $script:staging
    $script:staging = $null
    $root = [IO.Path]::GetFullPath((Join-Path $env:ProgramData 'EtpReporting')).TrimEnd('\')
    if ([IO.Path]::GetDirectoryName($folder) -ine $root -or [IO.Path]::GetFileName($folder) -cnotmatch '^RestoreStaging-[a-f0-9]{32}$') { return }
    if (-not (Test-Path -LiteralPath $folder -PathType Container)) { return }
    Assert-EtpNoLinks $folder
    Remove-Item -LiteralPath $folder -Recurse -Force
}

trap {
    Write-RestoreLog "Restore stopped: $($_.Exception.Message)"
    try { Remove-EtpRestoreStaging }
    catch { Write-RestoreLog 'The protected copy of the backup could not be removed. Delete the RestoreStaging folder under %ProgramData%\EtpReporting by hand.' }
    exit 1
}

# 1. Elevation, before anything else.
$current = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($current)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run the restore from an elevated (administrator) PowerShell window.'
}

# 2. The protected machine configuration names the instance and the database, and brings
#    its own checks: administrator-owned, local instance, dedicated non-admin automation account.
$configurationPath = Join-Path $env:ProgramData 'EtpReporting\Operations\operations.json'
Assert-EtpNoLinks $configurationPath
if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
    throw 'This PC has no ETP machine configuration (operations.json) yet. Run the ETP setup first, with "Create a new empty database" unticked, then run this again.'
}
$configuration = Get-EtpOperationsConfiguration
$ServerInstance = [string]$configuration.serverInstance
$Database = [string]$configuration.database
$script:logPath = Join-Path $env:ProgramData ("EtpReporting\SetupLogs\restore-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff').log")
Write-RestoreLog "Restoring $Database on $ServerInstance, run by $($current.Name)."

# 3. The file, and what the old PC recorded about it.
$source = Assert-EtpRestoreBackupPath $BackupPath
$length = [long](Get-Item -LiteralPath $source).Length
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
Write-RestoreLog "Backup file: $source ($length bytes, SHA-256 $sourceHash)."
if (-not [string]::IsNullOrWhiteSpace($ReceiptPath)) {
    Assert-EtpRestoreReceipt -ReceiptPath $ReceiptPath -Database $Database -Sha256 $sourceHash -LengthBytes $length
    Write-RestoreLog 'The backup matches its receipt.'
}
else { Write-RestoreLog 'No receipt was given. Compare the SHA-256 above with any hash recorded when the backup was taken.' }

# 4. The client and the instance.
$sqlcmd = Resolve-EtpSqlCmd $SqlCmdPath
$ServerInstance = Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $ServerInstance

# 5. Preconditions, in one read-only batch.
$checks = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query ("SET NOCOUNT ON; " +
    "SELECT N'ETP_SYSADMIN:'+CONVERT(nvarchar(1),COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)); " +
    "SELECT N'ETP_IDENTITY:'+SUSER_SNAME(); " +
    "SELECT N'ETP_DATABASE:'+CASE WHEN DB_ID(N'$Database') IS NULL THEN N'MISSING' ELSE N'EXISTS' END; " +
    "SELECT N'ETP_SERVICE:'+service_account FROM sys.dm_server_services WHERE servicename LIKE N'SQL Server (%';"))
if ((Get-EtpRestoreMarker $checks 'ETP_SYSADMIN') -cne '1') {
    throw 'Your Windows account is not a SQL Server administrator on this instance. On an instance ETP setup installed, members of Administrators are, from an elevated window. Nothing was restored.'
}
# The Owner recovery below is for exactly this account, so it must be the account SQL Server
# sees, a plain COMPUTER\User or DOMAIN\User, and not the automation or a service account.
$sqlIdentity = Get-EtpRestoreMarker $checks 'ETP_IDENTITY'
$automationSid = Resolve-EtpAccountSid $configuration.automationPrincipal
if ($sqlIdentity -ine $current.Name -or $sqlIdentity.Length -gt 200 -or $sqlIdentity -notmatch '^[^\\/\[\];''"]+\\[^\\/\[\];''"]+$' -or
    $current.User.Value -in @('S-1-5-18','S-1-5-19','S-1-5-20') -or $current.User.Value -eq $automationSid) {
    throw 'Run the restore signed in as the person who will own ETP on this PC, not as a service or the automation account. Nothing was restored.'
}
$databaseState = Get-EtpRestoreMarker $checks 'ETP_DATABASE'
if ($databaseState -ceq 'EXISTS') {
    throw "$Database already exists on $ServerInstance. This helper never restores over an existing database. If it holds nothing you need (for example setup created it empty), a SQL administrator must remove it deliberately first; otherwise stop here."
}
if ($databaseState -cne 'MISSING') { throw 'SQL Server returned an unexpected database-existence result. Nothing was restored.' }
$serviceAccount = Get-EtpRestoreMarker $checks 'ETP_SERVICE'
$serviceSid = $null
try {
    if ($serviceAccount -ieq 'LocalSystem') { $serviceSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18') }
    else { $serviceSid = ([Security.Principal.NTAccount]::new($serviceAccount)).Translate([Security.Principal.SecurityIdentifier]) }
}
catch { $serviceSid = $null }
if (-not $serviceSid) { throw 'Windows could not identify the account SQL Server runs as, so it cannot be given read access to the backup. Nothing was restored.' }

# 6. A protected copy the SQL Server service can read and nobody else can change, checked
#    against the hash of the original.
$programDataRoot = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($env:ProgramData))
if (([IO.DriveInfo]::new($programDataRoot)).AvailableFreeSpace -lt ($length + 1GB)) {
    throw "Drive $programDataRoot needs room for a protected copy of the backup ($([math]::Ceiling(($length + 1GB) / 1GB)) GB free). Nothing was restored."
}
$script:staging = New-EtpProtectedDirectory -Path (Join-Path $env:ProgramData ('EtpReporting\RestoreStaging-' + [Guid]::NewGuid().ToString('N'))) -ReadSid $serviceSid
$staged = Join-Path $script:staging 'restore-source.bak'
Copy-Item -LiteralPath $source -Destination $staged
if ((Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash -ine $sourceHash) { throw 'The copy of the backup differs from the original. Nothing was restored.' }
$stagedLiteral = $staged.Replace("'","''")
$unreadable = 'SQL Server could not verify this backup (RESTORE VERIFYONLY WITH CHECKSUM). It may be damaged, encrypted, taken without CHECKSUM, from a newer SQL Server, or not a database backup. Nothing was changed.'

# 7-9. What the file holds, whether SQL Server can read all of it, and where it will go.
$null = ConvertFrom-EtpRestoreHeader -Database $Database -Lines (Invoke-EtpRestoreRows -SqlCmd $sqlcmd -Server $ServerInstance -Failure $unreadable -Query "SET NOCOUNT ON; RESTORE HEADERONLY FROM DISK=N'$stagedLiteral';")
try { Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "RESTORE VERIFYONLY FROM DISK=N'$stagedLiteral' WITH CHECKSUM;" | Out-Null }
catch { throw $unreadable }
$files = ConvertFrom-EtpRestoreFileList -Database $Database -Lines (Invoke-EtpRestoreRows -SqlCmd $sqlcmd -Server $ServerInstance -Failure $unreadable -Query "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK=N'$stagedLiteral';")
Write-RestoreLog 'SQL Server verified the backup: one full backup of the configured database, with one data file and one log file.'
$folders = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "SET NOCOUNT ON; SELECT N'ETP_DATA_PATH:'+CONVERT(nvarchar(260),SERVERPROPERTY('InstanceDefaultDataPath')); SELECT N'ETP_LOG_PATH:'+CONVERT(nvarchar(260),SERVERPROPERTY('InstanceDefaultLogPath'));")
$needed = @{}
foreach ($target in @([pscustomobject]@{ Folder = (Get-EtpRestoreMarker $folders 'ETP_DATA_PATH'); Bytes = $files.DataBytes },
                      [pscustomobject]@{ Folder = (Get-EtpRestoreMarker $folders 'ETP_LOG_PATH'); Bytes = $files.LogBytes })) {
    if ([string]::IsNullOrWhiteSpace($target.Folder) -or $target.Folder -notmatch '^[A-Za-z]:\\') { throw 'SQL Server did not report its default data and log folders. Nothing was restored.' }
    $drive = [IO.Path]::GetPathRoot($target.Folder)
    if (-not $needed.ContainsKey($drive)) { $needed[$drive] = [long]0 }
    $needed[$drive] += [long]$target.Bytes
}
foreach ($drive in @($needed.Keys)) {
    if (([IO.DriveInfo]::new($drive)).AvailableFreeSpace -lt $needed[$drive]) {
        throw "Drive $drive does not have room for the restored database ($([math]::Ceiling($needed[$drive] / 1GB)) GB needed). Nothing was restored."
    }
}

# 10. The restore itself.
Write-RestoreLog "Restoring $Database from the verified copy into SQL Server's own data and log folders. This can take several minutes."
try { Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query (New-EtpRestoreDatabaseSql -Database $Database -BackupFile $staged -DataLogicalName $files.DataLogicalName -LogLogicalName $files.LogLogicalName) | Out-Null }
catch { throw "SQL Server could not restore the backup. Nothing was replaced. If SQL Server now lists $Database as restoring, a SQL administrator must examine it before anything else is tried." }
Remove-EtpRestoreStaging

# 11. No cross-database trust, and a state setup accepts.
$state = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query ("SET NOCOUNT ON; ALTER DATABASE [$Database] SET TRUSTWORTHY OFF; ALTER DATABASE [$Database] SET DB_CHAINING OFF; " +
    "SELECT N'ETP_STATE:'+state_desc+N'|'+user_access_desc+N'|'+CONVERT(nvarchar(1),is_read_only)+N'|'+CONVERT(nvarchar(1),is_trustworthy_on)+N'|'+CONVERT(nvarchar(1),is_db_chaining_on)+N'|'+CONVERT(nvarchar(10),compatibility_level) FROM sys.databases WHERE name=N'$Database'; " +
    "SELECT N'ETP_COLLATION:'+CONVERT(nvarchar(128),SERVERPROPERTY('Collation'))+N'|'+CONVERT(nvarchar(128),DATABASEPROPERTYEX(N'$Database','Collation'));"))
$stateParts = @((Get-EtpRestoreMarker $state 'ETP_STATE').Split('|'))
$compatibility = 0
if ($stateParts.Count -ne 6 -or ($stateParts[0..4] -join '|') -cne 'ONLINE|MULTI_USER|0|0|0' -or -not [int]::TryParse($stateParts[5], [ref]$compatibility)) {
    throw "$Database was restored but is not online, open to all users, writable and closed to cross-database trust. It has been left in place for a SQL administrator to examine. Do not run setup against it."
}
if ($compatibility -lt 150) {
    Write-RestoreLog "WARNING: the restored database's compatibility level is $compatibility. Setup refuses anything below 150, so a SQL administrator must raise it deliberately before setup runs again."
}
$collations = @((Get-EtpRestoreMarker $state 'ETP_COLLATION').Split('|'))
if ($collations.Count -ne 2 -or $collations[0] -cne $collations[1]) {
    Write-RestoreLog "WARNING: SQL Server's collation ($($collations[0])) differs from the restored database's ($($collations[-1])). The monthly recovery drill compares the two and will fail with a collation conflict on this instance."
}

# 12. Every page, before anything is written to it.
try { Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Query "DBCC CHECKDB ([$Database]) WITH NO_INFOMSGS, ALL_ERRORMSGS;" | Out-Null }
catch { throw 'The database was restored but DBCC CHECKDB reported damage. It has been left in place for a SQL administrator to examine. Do not run setup against it.' }
Write-RestoreLog 'DBCC CHECKDB found no damage.'

# 13. What setup will find when it runs again.
$schema = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -Query ("SET NOCOUNT ON; " +
    "SELECT N'ETP_SCHEMA:'+CASE WHEN OBJECT_ID(N'dbo.schema_migrations',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.application_users',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.configure_application_role',N'P') IS NOT NULL THEN N'1' ELSE N'0' END; " +
    "IF OBJECT_ID(N'dbo.schema_migrations',N'U') IS NOT NULL EXEC(N'SELECT N''ETP_MIGRATION:''+migration_id FROM dbo.schema_migrations;'); " +
    "IF OBJECT_ID(N'dbo.accounting_batches',N'U') IS NOT NULL EXEC(N'SELECT N''ETP_DUPLICATE_BATCH_DAYS:''+CONVERT(nvarchar(20),COUNT_BIG(*)) FROM (SELECT store_code,business_date FROM dbo.accounting_batches WHERE status<>''REJECTED'' GROUP BY store_code,business_date HAVING COUNT(*)>1) d;');"))
if ((Get-EtpRestoreMarker $schema 'ETP_SCHEMA') -cne '1') {
    throw "$Database was restored and checked, but it is not an ETP database with user administration (migration 0022 or later), so this helper cannot make you its Owner. Follow docs\OPERATIONS.md, Owner recovery and maintenance, by hand before running setup."
}
$applied = @(Get-EtpRestoreMarkers $schema 'ETP_MIGRATION')
$migrationDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'database\migrations'
$bundled = @()
if (Test-Path -LiteralPath $migrationDirectory -PathType Container) { $bundled = @(Get-ChildItem -LiteralPath $migrationDirectory -Filter '*.sql' -File | ForEach-Object { $_.BaseName } | Sort-Object) }
$pending = @($bundled | Where-Object { $_ -notin $applied })
$unknown = @($applied | Where-Object { $bundled.Count -gt 0 -and $_ -notin $bundled })
if ($unknown.Count -gt 0) {
    Write-RestoreLog "WARNING: the backup records database updates this ETP release does not have ($($unknown -join ', ')). It comes from a newer ETP release; install that release or a later one before running setup."
}
$duplicateDays = @(Get-EtpRestoreMarkers $schema 'ETP_DUPLICATE_BATCH_DAYS')
$duplicateCount = [long]0
if ($duplicateDays.Count -eq 1 -and [long]::TryParse($duplicateDays[0], [ref]$duplicateCount) -and $duplicateCount -gt 0) {
    Write-RestoreLog "WARNING: $duplicateCount store-day(s) have more than one accounting batch that is not rejected. Setup's database updates 0033 and 0037 refuse that until the Owner or the accountant has reviewed those batches (docs\OPERATIONS.md, Phase 5 upgrade and operating checks)."
}

# 14. Make the account running this the Owner, the documented way.
$owner = @()
try { $owner = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $ServerInstance -Database $Database -Query (New-EtpOwnerRecoverySql)) }
catch { $owner = @() }
$ownerResult = @(Get-EtpRestoreMarkers $owner 'ETP_OWNER')
if ($ownerResult.Count -ne 1 -or $ownerResult[0] -cne '1') {
    throw 'The database was restored and checked, but making you its Owner failed. Follow docs\OPERATIONS.md, Owner recovery and maintenance, by hand, then run setup again.'
}
Write-RestoreLog "$sqlIdentity is now an active Owner of $Database, with the reason recorded in its user history."

# 15. The broker setup's safety backup goes through. Left alone if it is already there.
try {
    foreach ($line in @(& (Join-Path $PSScriptRoot 'install-etp-sql-operations.ps1') -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $configuration.automationPrincipal -SqlCmdPath $sqlcmd -BrokerOnly)) { Write-RestoreLog "$line" }
}
catch {
    throw "The database was restored and checked, and you are its Owner, but the operations broker could not be installed ($($_.Exception.Message)). Setup's safety backup needs it: run install-etp-sql-operations.ps1 with -BrokerOnly from an elevated window, then run setup again."
}

# 16. What happens next.
$waiting = if ($bundled.Count -eq 0) { 'The waiting database updates could not be counted, because this copy of ETP has no migrations folder.' }
    elseif ($pending.Count -eq 0) { 'No database updates are waiting.' }
    else { "$($pending.Count) database update(s) are waiting ($($pending -join ', '))." }
Write-RestoreLog "$Database was restored from $source, verified and checked, and $sqlIdentity is its Owner. $waiting"
Write-RestoreLog 'Next:'
Write-RestoreLog '1. Run the ETP setup again (you can leave "Install SQL Server" unticked). It takes a verified safety backup of this data before it applies the waiting updates.'
Write-RestoreLog "2. Then, in ETP as Owner, add $($configuration.automationPrincipal) as an active Store Manager in Settings > Users and run install-etp-sql-operations.ps1 (docs\OPERATIONS.md, step 7)."
Write-RestoreLog "The old PC's accounts stay listed in Settings > Users; they cannot sign in here. A database restored onto a newer SQL Server than the one it was backed up on is upgraded, and its backups can no longer be restored onto the older version."
exit 0
