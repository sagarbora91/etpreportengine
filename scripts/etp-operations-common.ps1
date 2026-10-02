# Shared operational boundaries. Dot-source from signed, administrator-owned scripts.
Set-StrictMode -Version Latest

function Assert-EtpLocalSqlTarget {
    param([string]$ServerInstance,[string]$Database)
    if ($Database -notmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Choose a valid database name.' }
    if ([string]::IsNullOrWhiteSpace($ServerInstance)) { throw 'Choose a SQL Server instance on this computer.' }
    $server = $ServerInstance.Trim()
    $localHosts = @('.', '(local)', 'localhost', [Environment]::MachineName)
    if ($server.StartsWith('lpc:',[StringComparison]::OrdinalIgnoreCase)) { $server = $server.Substring(4) }
    elseif ($server.StartsWith('np:',[StringComparison]::OrdinalIgnoreCase)) {
        $server = $server.Substring(3)
        if ($server.StartsWith('\\',[StringComparison]::Ordinal)) {
            if ($server -match '^\\\\([^\\]+)\\pipe\\[A-Za-z0-9_$\\.-]+$' -and $Matches[1] -in $localHosts) { return }
            throw 'Choose a SQL Server instance on this computer.'
        }
    }
    if ($server -match '^(\.|\(local\)|localhost|\(localdb\)|[A-Za-z0-9_-]+)(\\[A-Za-z0-9_$-]+)?$') {
        if ($Matches[1] -in $localHosts -or ($Matches[1] -ieq '(localdb)' -and $Matches.ContainsKey(2))) { return }
    }
    throw 'Choose a SQL Server instance on this computer.'
}

function Assert-EtpNoLinks {
    param([Parameter(Mandatory)][string]$Path)
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked operation paths are not allowed.' }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Assert-EtpProtectedInstall {
    param([Parameter(Mandatory)][string]$Path)
    Assert-EtpNoLinks $Path
    $trusted = @('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
    $danger = [Security.AccessControl.FileSystemRights]::Write -bor [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        $acl = Get-Acl -LiteralPath $current
        if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) { throw 'Install operations in a folder owned by Administrators or SYSTEM.' }
        foreach ($rule in $acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
            if (-not ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) -and $rule.AccessControlType -eq 'Allow' -and ($rule.FileSystemRights -band $danger) -and $rule.IdentityReference.Value -notin $trusted) { throw 'The installation folder can be changed by a non-administrator.' }
        }
        $current = [IO.Path]::GetDirectoryName($current)
        $danger = [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    }
}

function New-EtpProtectedDirectory {
    # A new folder only SYSTEM and Administrators can change, created in one step with that
    # protection so nothing can be planted in it between creation and use. -ReadSid lets one
    # more account (the SQL Server service) read what is put there. Used for work that runs
    # with the administrator's full token: SQL Server's extracted setup, and the copy of a
    # backup that is about to be restored.
    param([Parameter(Mandatory)][string]$Path,[Security.Principal.SecurityIdentifier]$ReadSid)
    $full = [IO.Path]::GetFullPath($Path)
    Assert-EtpNoLinks $full
    if (Test-Path -LiteralPath $full) { throw 'A protected work folder already exists at that path.' }
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $security.SetAccessRuleProtection($true,$false)
    $security.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    }
    if ($ReadSid) { $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($ReadSid,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')) }
    $null = [IO.Directory]::CreateDirectory($full,$security)
    # Also closes the gap between Test-Path and CreateDirectory: a folder somebody else
    # planted there first is left as it was by CreateDirectory, and has the wrong owner.
    Assert-EtpProtectedInstall $full
    return $full
}

function Resolve-EtpSqlCmd {
    param([string]$ExplicitPath)
    # The ODBC client reaches a local instance over shared memory, which SQL
    # Server Express and Developer enable by default. go-sqlcmd resolves a bare
    # ".\INSTANCE" over named pipes, which they disable by default, so it is
    # preferred only when the ODBC client is absent.
    # ODBC 18 ships with SQL Server 2025 tooling, 17 with 2022. Look for the newer one
    # first: pinning a single version meant a machine with only the current tools
    # resolved nothing and fell through to go-sqlcmd.
    $candidates = @($ExplicitPath,
        (Join-Path $env:ProgramFiles 'Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE'),
        (Join-Path $env:ProgramFiles 'Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'),
        (Join-Path $env:ProgramFiles 'sqlcmd\sqlcmd.exe'))
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            Assert-EtpProtectedInstall $candidate
            return [IO.Path]::GetFullPath($candidate)
        }
    }
    throw 'Install Microsoft Sqlcmd in a protected Program Files folder.'
}

function Resolve-EtpSqlConnection {
    param([string]$SqlCmd,[string]$ServerInstance)
    # A client that cannot reach the instance fails every later call behind the
    # same masked message, which sends the operator to SQL permissions instead
    # of to the connection. Settle the protocol once, here. An instance that
    # already names its protocol is used exactly as configured.
    $attempts = @($ServerInstance)
    if ($ServerInstance -notmatch '^(?i)(lpc|np|tcp|admin):') { $attempts += 'lpc:' + $ServerInstance }
    foreach ($attempt in $attempts) {
        Assert-EtpLocalSqlTarget $attempt 'master'
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $SqlCmd -x -S $attempt -E -b -d master -Q 'SET NOCOUNT ON; SELECT 1;' 2>$null | Out-Null
            $probeExitCode = $LASTEXITCODE
        }
        catch { $probeExitCode = 1 }
        finally { $ErrorActionPreference = $previousPreference }
        if ($probeExitCode -eq 0) { return $attempt }
    }
    throw 'Could not reach the SQL Server instance with the installed command-line client. Check the instance name and that the client can connect to it.'
}

function Invoke-EtpSql {
    param([string]$SqlCmd,[string]$Server,[string]$Database='master',[string]$Query)
    Assert-EtpLocalSqlTarget $Server $Database
    # -x disables SQLCMD variable substitution in user-selected paths and values.
    # SQL sends informational RESTORE messages to stderr too. Let -b and the
    # process exit code distinguish failure; never expose stderr contents.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $result = @(& $SqlCmd -x -S $Server -E -b -r 1 -d $Database -y 0 -s '|' -Q $Query 2>$null)
        $sqlExitCode = $LASTEXITCODE
    }
    catch { throw 'The database operation failed. Check SQL permissions and operation prerequisites.' }
    finally { $ErrorActionPreference = $previousPreference }
    if ($sqlExitCode -ne 0) {
        # Say which of the two it was without ever exposing stderr: an operator
        # sent to SQL permissions for an unreachable instance looks in the
        # wrong place for as long as the backups keep failing.
        $previousPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $SqlCmd -x -S $Server -E -b -d master -Q 'SET NOCOUNT ON; SELECT 1;' 2>$null | Out-Null
            $probeExitCode = $LASTEXITCODE
        }
        catch { $probeExitCode = 1 }
        finally { $ErrorActionPreference = $previousPreference }
        if ($probeExitCode -ne 0) { throw 'Could not reach the SQL Server instance with the installed command-line client. Check the instance name and that the client can connect to it.' }
        throw 'The database operation failed. Check SQL permissions and operation prerequisites.'
    }
    return $result
}

function Invoke-EtpSqlAsAutomationUser {
    # P4-15 review. The recovery drill runs as a SQL administrator, and the procedures that
    # record its result live in the application database, where any application Owner - a
    # db_owner, not necessarily a SQL administrator - can redefine them. Called directly,
    # code planted there would run with the drill's server rights. Run it instead as the
    # automation account's database user: impersonating a database user confines the batch
    # to that database, with none of the caller's server rights, and NO REVERT means code
    # further down cannot switch back. It records under the account that records backups.
    param([string]$SqlCmd,[string]$Server,[string]$Database,[string]$AutomationPrincipal,[string]$Query)
    if ($AutomationPrincipal -notmatch '^[^\\/\[\];''"]+\\[^\\/\[\];''"]+$') { throw 'Configure a dedicated local automation account.' }
    $literal = $AutomationPrincipal.Replace("'","''")
    # A database marked TRUSTWORTHY lets an impersonated user reach back out to the server,
    # which is the one thing this is for. Only a SQL administrator can set it; refuse it here.
    $scoped = "SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM sys.databases WHERE database_id=DB_ID() AND is_trustworthy_on=1) THROW 51335,'The database is marked TRUSTWORTHY.',1; " +
        "DECLARE @etpAutomationUser sysname=(SELECT name FROM sys.database_principals WHERE sid=SUSER_SID(N'$literal') AND type=N'U'); " +
        "IF @etpAutomationUser IS NULL THROW 51335,'The automation account has no user in this database.',1; " +
        "EXECUTE AS USER=@etpAutomationUser WITH NO REVERT; " + $Query
    return Invoke-EtpSql -SqlCmd $SqlCmd -Server $Server -Database $Database -Query $scoped
}

function Write-EtpJsonAtomically {
    param([string]$Path,[object]$Value,[switch]$Replace)
    Assert-EtpNoLinks $Path
    $full = [IO.Path]::GetFullPath($Path)
    $temporary = "$full.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary -Encoding utf8
        if ((Test-Path -LiteralPath $full) -and $Replace) { [IO.File]::Replace($temporary,$full,[System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporary,$full) }
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
}

function Read-EtpVerifiedReceipt {
    param([string]$ReceiptPath,[string]$BackupDirectory,[string]$Database,[switch]$SkipCertificateCheck)
    Assert-EtpNoLinks $ReceiptPath
    $receipt = Get-Content -Raw -LiteralPath $ReceiptPath | ConvertFrom-Json
    # D9 revised: AES_256 where the edition can encrypt, NONE where it cannot. Anything
    # else is a receipt this build did not write and is not trusted.
    if ($receipt.schemaVersion -ne 2 -or $receipt.verified -ne $true -or $receipt.database -cne $Database -or $receipt.encryption -cnotin @('AES_256','NONE')) { throw 'A verified backup receipt is required.' }
    $root = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\') + '\'
    $backup = [IO.Path]::GetFullPath($receipt.backupPath)
    if (-not $backup.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetDirectoryName($backup)+'\' -ine $root) { throw 'The receipt backup is outside the backup folder.' }
    Assert-EtpNoLinks $backup
    $file = Get-Item -LiteralPath $backup
    if ($receipt.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $file.Length -ne $receipt.lengthBytes -or (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ine $receipt.sha256) { throw 'Backup verification failed: the receipt and file differ.' }
    if ($receipt.encryption -ceq 'NONE') {
        # A receipt must not be able to opt out of custody merely by saying so. An
        # unencrypted backup has no certificate, so custody details appearing here mean
        # the field was altered on a receipt that WAS encrypted. Refuse it rather than
        # skip the chain, otherwise editing one word downgrades every check below.
        $thumb = [string]$receipt.certificateThumbprint
        $custody = [string]$receipt.certificateReceipt
        if (-not [string]::IsNullOrWhiteSpace($thumb) -or -not [string]::IsNullOrWhiteSpace($custody)) {
            throw 'An unencrypted backup receipt must not carry certificate custody details.'
        }
        return $receipt
    }
    if ($null -eq $receipt.PSObject.Properties['certificateThumbprint'] -or $receipt.certificateThumbprint -notmatch '^(?:[A-Fa-f0-9]{2}){20,64}$') { throw 'The backup certificate thumbprint is invalid.' }
    # Rotation may move the latest pointer to another certificate. Every backup
    # remains bound to the immutable custody receipt for its own encryption key.
    # Retention can skip reconnecting recovery media, never the identity binding.
    Assert-EtpCertificateCustody -ReceiptPath $receipt.certificateReceipt -ExpectedThumbprint $receipt.certificateThumbprint -RequireAvailable:(-not $SkipCertificateCheck)
    return $receipt
}

function Assert-EtpCertificateCustody {
    param([string]$ReceiptPath,[switch]$RequireAvailable,[string]$ExpectedThumbprint)
    Assert-EtpNoLinks $ReceiptPath
    if (-not (Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) { throw 'Export the backup certificate and private key to two recovery locations first.' }
    $certificate = Get-Content -Raw -LiteralPath $ReceiptPath | ConvertFrom-Json
    if ($null -eq $certificate.PSObject.Properties['schemaVersion'] -or $certificate.schemaVersion -ne 2 -or
        $null -eq $certificate.PSObject.Properties['exportId'] -or $certificate.exportId -notmatch '^[A-Fa-f0-9]{32}$' -or
        $null -eq $certificate.PSObject.Properties['certificateThumbprint'] -or $certificate.certificateThumbprint -notmatch '^(?:[A-Fa-f0-9]{2}){20,64}$') {
        throw 'Use an immutable certificate-specific custody receipt.'
    }
    $expectedFileName = 'certificate-custody-' + $certificate.certificateThumbprint + '-' + $certificate.exportId + '.json'
    if ([IO.Path]::GetFileName($ReceiptPath) -ine $expectedFileName) { throw 'Use an immutable certificate-specific custody receipt.' }
    if ($PSBoundParameters.ContainsKey('ExpectedThumbprint')) {
        if ($ExpectedThumbprint -notmatch '^(?:[A-Fa-f0-9]{2}){20,64}$') { throw 'The backup certificate thumbprint is invalid.' }
        if ($certificate.certificateThumbprint -ine $ExpectedThumbprint) { throw 'The backup certificate does not match its custody receipt.' }
    }
    if ($certificate.certificateName -cne 'EtpBackupCert' -or @($certificate.copies).Count -ne 2) { throw 'Two certificate recovery copies must be recorded.' }
    $certificatePaths = @($certificate.copies | ForEach-Object { [IO.Path]::GetFullPath($_.certificatePath) })
    $privateKeyPaths = @($certificate.copies | ForEach-Object { [IO.Path]::GetFullPath($_.privateKeyPath) })
    if ($certificatePaths[0] -ieq $certificatePaths[1] -or $privateKeyPaths[0] -ieq $privateKeyPaths[1]) { throw 'Choose two distinct certificate recovery locations.' }
    foreach ($copy in $certificate.copies) {
        if ($copy.certificateSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $copy.privateKeySha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Certificate export hashes are invalid.' }
        if (-not $RequireAvailable) { continue }
        foreach ($entry in @(@($copy.certificatePath,$copy.certificateSha256),@($copy.privateKeyPath,$copy.privateKeySha256))) {
            Assert-EtpNoLinks $entry[0]
            if (-not (Test-Path -LiteralPath $entry[0] -PathType Leaf) -or (Get-FileHash -LiteralPath $entry[0] -Algorithm SHA256).Hash -ine $entry[1]) { throw 'A certificate recovery copy is missing or has changed. Reconnect the recovery storage and verify custody.' }
        }
    }
}

function Test-EtpRotatableBackupReceipt {
    # Only an ordinary scheduled backup may ever be deleted by rotation. A receipt written
    # before the purpose field existed has none, and is scheduled. Anything else - a
    # pre-migration backup, or a purpose this build cannot interpret - is kept, because the
    # cost of keeping a file is disk and the cost of deleting one is the database.
    param([Parameter(Mandatory)][object]$Receipt)
    if ($null -eq $Receipt.PSObject.Properties['purpose']) { return $true }
    return ([string]$Receipt.purpose) -ceq 'SCHEDULED'
}

function Invoke-EtpBackupRotation {
    # Deletes the scheduled backups retention no longer needs and returns the bytes that
    # reclaimed. Only verified receipt/file pairs for this database are considered: a
    # receipt that cannot be read, and any .bak without one, is left for an administrator.
    # Extracted from backup-etp-database.ps1 so it can also run on the refusal path there.
    param([Parameter(Mandatory)][string]$Directory,[Parameter(Mandatory)][string]$Database)
    $receipts = @()
    foreach ($candidate in Get-ChildItem -LiteralPath $Directory -Filter "$Database-*.bak.receipt.json" -File) {
        try { $receipts += Read-EtpVerifiedReceipt -ReceiptPath $candidate.FullName -BackupDirectory $Directory -Database $Database -SkipCertificateCheck }
        catch { Write-Warning 'An older backup receipt needs review; its files were retained.' }
    }
    $keep = @(Get-EtpRetainedBackupReceipts $receipts | ForEach-Object backupPath)
    $reclaimed = [int64]0
    foreach ($old in $receipts) {
        if ($old.backupPath -in $keep) { continue }
        # Read-EtpVerifiedReceipt already checked absolute containment and rejected junctions.
        $reclaimed += [int64]$old.lengthBytes
        Remove-Item -LiteralPath $old.backupPath -Force
        Remove-Item -LiteralPath "$($old.backupPath).receipt.json" -Force
    }
    return $reclaimed
}

function Get-EtpRetainedBackupReceipts {
    param([object[]]$Receipts)
    # Latest successful backup per UTC day, plus latest per calendar month.
    # A pre-migration backup is the only copy of the database as it was before a schema
    # change. It is kept whatever its date, and it does not occupy a day or a month slot,
    # so taking one never shortens the ordinary history. Before 25 September 2026 it had
    # neither protection: setup's own backup was deleted by that same day's rotation.
    $ordered = @($Receipts | Sort-Object { ([datetimeoffset]$_.verifiedAtUtc).UtcDateTime } -Descending)
    $days = @{}; $months = @{}; $keep = @{}
    foreach ($receipt in $ordered) {
        if (-not (Test-EtpRotatableBackupReceipt -Receipt $receipt)) { $keep[$receipt.backupPath]=$receipt; continue }
        $date = ([datetimeoffset]$receipt.verifiedAtUtc).UtcDateTime
        $day = $date.ToString('yyyy-MM-dd'); $month = $date.ToString('yyyy-MM')
        if (-not $days.ContainsKey($day) -and $days.Count -lt 14) { $days[$day]=$true; $keep[$receipt.backupPath]=$receipt }
        if (-not $months.ContainsKey($month) -and $months.Count -lt 12) { $months[$month]=$true; $keep[$receipt.backupPath]=$receipt }
    }
    return @($keep.Values)
}
function Get-EtpOperationsConfiguration {
    $path = Join-Path $env:ProgramData 'EtpReporting\Operations\operations.json'
    Assert-EtpProtectedInstall $path
    $configuration = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    Assert-EtpLocalSqlTarget $configuration.serverInstance $configuration.database
    if ($configuration.automationPrincipal -notmatch ('^'+[regex]::Escape([Environment]::MachineName)+'\\[^\\]+$')) { throw 'Configure a dedicated local automation account.' }
    $sid = ([Security.Principal.NTAccount]::new($configuration.automationPrincipal)).Translate([Security.Principal.SecurityIdentifier])
    if ($sid.Value -in @('S-1-5-18','S-1-5-19','S-1-5-20') -or $sid.Value -match '-500$') { throw 'Automation cannot run as a built-in service or administrator account.' }
    $localUser = Get-LocalUser -SID $sid -ErrorAction Stop
    if (-not $localUser.Enabled) { throw 'Enable the dedicated automation account first.' }
    $administrators = @(Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop | ForEach-Object { $_.SID.Value })
    if ($sid.Value -in $administrators) { throw 'The automation account must not belong to Administrators.' }
    return $configuration
}

function Add-EtpBatchLogonRight {
    # A task that runs whether or not anyone is signed in logs on as a batch job, so the
    # account needs that right or the task registers and then fails to start. Task Scheduler
    # usually adds it when a task is registered; granting it here makes it certain and
    # visible in the local policy. Only an administrator can grant it, and granting it twice
    # is harmless. It is NOT what made registration fail on 24 September 2026: a missing
    # batch right returns SCHED_S_BATCH_LOGON_PROBLEM, which still registers the task.
    param([Parameter(Mandatory)][Security.Principal.SecurityIdentifier]$Sid)
    if (-not ('Etp.LocalSecurityPolicy' -as [type])) {
        Add-Type -Namespace Etp -Name LocalSecurityPolicy -UsingNamespace System.Text -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)]
private struct LsaUnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
[StructLayout(LayoutKind.Sequential)]
private struct LsaObjectAttributes { public int Length; public IntPtr RootDirectory; public IntPtr ObjectName; public int Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService; }
[DllImport("advapi32.dll", SetLastError = true)]
private static extern uint LsaOpenPolicy(IntPtr systemName, ref LsaObjectAttributes objectAttributes, int desiredAccess, out IntPtr policyHandle);
[DllImport("advapi32.dll", SetLastError = true)]
private static extern uint LsaAddAccountRights(IntPtr policyHandle, byte[] accountSid, LsaUnicodeString[] userRights, int countOfRights);
[DllImport("advapi32.dll", SetLastError = true)]
private static extern uint LsaEnumerateAccountRights(IntPtr policyHandle, byte[] accountSid, out IntPtr userRights, out int countOfRights);
[DllImport("advapi32.dll")] private static extern uint LsaClose(IntPtr objectHandle);
[DllImport("advapi32.dll")] private static extern uint LsaFreeMemory(IntPtr buffer);
[DllImport("advapi32.dll")] private static extern int LsaNtStatusToWinError(uint status);

private static IntPtr OpenPolicy(int access)
{
    LsaObjectAttributes attributes = new LsaObjectAttributes();
    attributes.Length = Marshal.SizeOf(typeof(LsaObjectAttributes));
    IntPtr handle;
    uint status = LsaOpenPolicy(IntPtr.Zero, ref attributes, access, out handle);
    if (status != 0) throw new System.ComponentModel.Win32Exception(LsaNtStatusToWinError(status));
    return handle;
}

private static LsaUnicodeString Text(string value)
{
    LsaUnicodeString text = new LsaUnicodeString();
    text.Buffer = Marshal.StringToHGlobalUni(value);
    text.Length = (ushort)(value.Length * 2);
    text.MaximumLength = (ushort)(text.Length + 2);
    return text;
}

public static void Grant(byte[] sid, string right)
{
    IntPtr policy = OpenPolicy(0x00000800 | 0x00000010 | 0x00000004); // create account, lookup names, view local information
    LsaUnicodeString[] rights = new LsaUnicodeString[] { Text(right) };
    try
    {
        uint status = LsaAddAccountRights(policy, sid, rights, 1);
        if (status != 0) throw new System.ComponentModel.Win32Exception(LsaNtStatusToWinError(status));
    }
    finally { Marshal.FreeHGlobal(rights[0].Buffer); LsaClose(policy); }
}

public static string[] Rights(byte[] sid)
{
    IntPtr policy = OpenPolicy(0x00000010 | 0x00000004);
    IntPtr buffer = IntPtr.Zero;
    try
    {
        int count;
        uint status = LsaEnumerateAccountRights(policy, sid, out buffer, out count);
        if (status == 0xC0000034) return new string[0]; // the account holds none
        if (status != 0) throw new System.ComponentModel.Win32Exception(LsaNtStatusToWinError(status));
        string[] found = new string[count];
        int size = Marshal.SizeOf(typeof(LsaUnicodeString));
        for (int index = 0; index < count; index++)
        {
            LsaUnicodeString item = (LsaUnicodeString)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + index * size), typeof(LsaUnicodeString));
            found[index] = Marshal.PtrToStringUni(item.Buffer, item.Length / 2);
        }
        return found;
    }
    finally { if (buffer != IntPtr.Zero) LsaFreeMemory(buffer); LsaClose(policy); }
}
'@
    }
    $bytes = New-Object byte[] $Sid.BinaryLength
    $Sid.GetBinaryForm($bytes, 0)
    [Etp.LocalSecurityPolicy]::Grant($bytes, 'SeBatchLogonRight')
    if ('SeBatchLogonRight' -notin [Etp.LocalSecurityPolicy]::Rights($bytes)) {
        throw 'The automation account still cannot log on as a batch job. Check the local security policy.'
    }
}

function Register-EtpScheduledOperation {
    # Every operation runs as the least-privileged automation account unless told otherwise.
    # The one exception is the recovery drill, which only a SQL administrator can perform
    # (P4-15) and is registered under the Owner with -Principal.
    param([string]$TaskName,[object]$Action,[object]$Trigger,[string]$Description,
          [string]$Principal,[ValidateSet('Limited','Highest')][string]$RunLevel='Limited')
    $configuration = Get-EtpOperationsConfiguration
    $userId = if ([string]::IsNullOrWhiteSpace($Principal)) { $configuration.automationPrincipal } else { $Principal }
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)
    $mine = (Resolve-EtpAccountSid ([Security.Principal.WindowsIdentity]::GetCurrent().Name)) -eq (Resolve-EtpAccountSid $userId)
    if ($mine) {
        # Registering an S4U task for the account doing the registering needs no credential.
        $principalObject = New-ScheduledTaskPrincipal -UserId $userId -LogonType S4U -RunLevel $RunLevel
        Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Settings $settings -Principal $principalObject -Description $Description -Force | Out-Null
    }
    else {
        # For anyone else's account Windows demands that account's password once, to prove the
        # principal is real. That is documented for TASK_LOGON_S4U, and measured here on
        # 24 September 2026: an elevated administrator, and even SYSTEM (which does hold
        # SeTcbPrivilege), are both refused with a bare "Access is denied" without it. The
        # password is used for this one call and never stored - S4U means Task Scheduler keeps
        # no credential and asks Windows for a token when the task runs. Nobody keeps a copy,
        # so the account's password is reset to a fresh random value and discarded again.
        Register-EtpAutomationTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Settings $settings -Description $Description -UserId $userId -RunLevel $RunLevel
    }
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    # Compare accounts, not spellings. Task Scheduler reports a local account registered as
    # COMPUTER\User by its bare name, so comparing the text failed every registration.
    $expectedSid = Resolve-EtpAccountSid $userId
    if (-not $expectedSid -or $task.State -eq 'Disabled' -or (Resolve-EtpAccountSid $task.Principal.UserId) -ne $expectedSid -or $task.Principal.RunLevel -ne $RunLevel) { throw 'The scheduled task did not retain the required principal and settings.' }
    if ($task.Principal.LogonType -ne 'S4U') { throw 'The scheduled task must run without a stored password.' }
}

function Resolve-EtpAccountSid {
    # A Windows account named as DOMAIN\User, a bare name, or a SID string; $null if unknown.
    param([string]$Account)
    if ([string]::IsNullOrWhiteSpace($Account)) { return $null }
    try {
        if ($Account -match '^S-1-\d+(-\d+)+$') { return ([Security.Principal.SecurityIdentifier]::new($Account)).Value }
        return ([Security.Principal.NTAccount]::new($Account)).Translate([Security.Principal.SecurityIdentifier]).Value
    }
    catch { return $null }
}

function Register-EtpAutomationTask {
    # Registers one task for the dedicated automation account through the Task Scheduler COM
    # API, the only way to pass a password together with S4U: Register-ScheduledTask's
    # -Principal parameter set accepts no password, and its -User/-Password set registers a
    # Password-logon task, which would store the credential and defeat the point.
    param([string]$TaskName,[object]$Action,[object]$Trigger,[object]$Settings,[string]$Description,
          [string]$UserId,[string]$RunLevel)
    $sid = Resolve-EtpAccountSid $UserId
    if (-not $sid) { throw 'The automation account could not be resolved.' }
    $account = Get-LocalUser -SID $sid -ErrorAction Stop
    if (-not $account.Enabled) { throw 'Enable the dedicated automation account first.' }
    $bytes = New-Object byte[] 48
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    $secret = $null
    $service = $null
    try {
        $random.GetBytes($bytes)
        $secret = 'Etp!' + [Convert]::ToBase64String($bytes)
        $secure = ConvertTo-SecureString $secret -AsPlainText -Force
        try {
            # The account never signs in and nothing is encrypted under it, so replacing its
            # password costs nothing; it only has to be known for the call below.
            Set-LocalUser -SID $sid -Password $secure -ErrorAction Stop
        }
        finally { $secure.Dispose() }

        $service = New-Object -ComObject 'Schedule.Service'
        $service.Connect()
        $definition = $service.NewTask(0)
        $definition.RegistrationInfo.Description = $Description
        $definition.Settings.StartWhenAvailable = $true
        $definition.Settings.MultipleInstances = 2          # TASK_INSTANCES_IGNORE_NEW
        $definition.Settings.ExecutionTimeLimit = 'PT2H'
        $definition.Settings.Enabled = $true
        $definition.Principal.LogonType = 2                 # TASK_LOGON_S4U
        $definition.Principal.UserId = $UserId
        $definition.Principal.RunLevel = if ($RunLevel -eq 'Highest') { 1 } else { 0 }

        $exec = $definition.Actions.Create(0)               # TASK_ACTION_EXEC
        $exec.Path = $Action.Execute
        if ($Action.Arguments) { $exec.Arguments = $Action.Arguments }
        if ($Action.WorkingDirectory) { $exec.WorkingDirectory = $Action.WorkingDirectory }

        $start = ([datetime]$Trigger.StartBoundary).ToString('yyyy-MM-ddTHH:mm:ss')
        switch ($Trigger.CimClass.CimClassName) {
            'MSFT_TaskDailyTrigger' {
                $created = $definition.Triggers.Create(2)   # TASK_TRIGGER_DAILY
                $created.DaysInterval = [int]$Trigger.DaysInterval
            }
            'MSFT_TaskTimeTrigger' {
                $created = $definition.Triggers.Create(1)   # TASK_TRIGGER_TIME
                if ($Trigger.Repetition -and $Trigger.Repetition.Interval) {
                    $created.Repetition.Interval = $Trigger.Repetition.Interval
                    if ($Trigger.Repetition.Duration) { $created.Repetition.Duration = $Trigger.Repetition.Duration }
                    if ($null -ne $Trigger.Repetition.StopAtDurationEnd) { $created.Repetition.StopAtDurationEnd = [bool]$Trigger.Repetition.StopAtDurationEnd }
                }
            }
            default { throw ('This scheduled operation uses a trigger this installer cannot register for the automation account: ' + $Trigger.CimClass.CimClassName) }
        }
        $created.StartBoundary = $start
        $created.Enabled = $true

        try { $definition.Principal.Id = 'Author'; $folder = $service.GetFolder('\'); $folder.RegisterTaskDefinition($TaskName, $definition, 6, $UserId, $secret, 2) | Out-Null }  # TASK_CREATE_OR_UPDATE, TASK_LOGON_S4U
        catch {
            throw ("Windows refused to register '" + $TaskName + "' to run as " + $UserId +
                ' without a stored password. Windows said: ' + $_.Exception.Message)
        }
    }
    finally {
        $random.Dispose()
        [Array]::Clear($bytes, 0, $bytes.Length)
        $secret = $null
        if ($service) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($service) }
    }
}

function Get-EtpOperationsProcedureName {
    param([string]$Database)
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $hash=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Database.ToUpperInvariant())))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
    return 'etp_operations_'+$hash.Substring(0,16)
}

function Invoke-EtpOperationsBroker {
    param([string]$SqlCmd,[string]$Server,[string]$Database,[string]$BackupPath,[ValidateSet('BACKUP','METADATA','DRILL')][string]$Operation)
    Assert-EtpLocalSqlTarget $Server $Database
    $file=[IO.Path]::GetFileName($BackupPath)
    if ($file -notmatch '^[A-Za-z0-9_.-]+\.bak$' -or $file.Contains('..') -or -not $file.StartsWith($Database+'-',[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a backup belonging to the configured database.' }
    $procedure=Get-EtpOperationsProcedureName $Database
    $output=@(Invoke-EtpSql -SqlCmd $SqlCmd -Server $Server -Query "EXEC dbo.[$procedure] '$Operation',N'$file';")
    $metadata=@($output | Where-Object { $_.StartsWith('ETP_METADATA:') })
    if ($metadata.Count -ne 1) { throw 'The restricted SQL operation did not return verified backup metadata.' }
    return @($metadata[0].Substring(13) | ConvertFrom-Json)
}

# ---------------------------------------------------------------------------------------------
# 1.9.3, Sagar's decision of 2 October 2026: setup gives the Owner ALTER ANY LOGIN WITH GRANT
# OPTION itself, so Settings > Users works without "Run as administrator" right after install.
# Migration 0043 gives it to every active Owner except the account running the migration, and
# SQL Server never lets a login grant a permission to itself (error 4627): setup and the
# restore helper run as the very Owner they provision. So the grant is made by a different SQL
# administrator, NT AUTHORITY\SYSTEM, which on an instance ETP's setup installed is one through
# BUILTIN\Administrators: a one-off scheduled task, registered under a unique name, started,
# waited for with a timeout, read, and always unregistered - the method proven by hand on
# Workpc (Migration 2026-10-02\grant-alter-any-login.cmd). Its files live in a new folder only
# SYSTEM and Administrators can change, which is deleted afterwards. Nothing here ever stops
# setup or the restore: every outcome is returned for the caller to log, and the caller
# re-checks with its own read-only probe.

function New-EtpOwnerGrantOptionSql {
    # Fixed text with no parameters, run by the SYSTEM task connected to the ETP database. The
    # logins that get the grant are chosen inside SQL Server from dbo.application_users - every
    # active OWNER that has a login and does not hold the grant option yet, as migration 0043
    # upgrades - so no name from setup, a command line or a user reaches it, and every login
    # name goes through QUOTENAME. Narrower than 0043, because the restore helper runs it
    # against rows that came from another PC: Windows user logins only, never a group (an
    # Owner row naming BUILTIN\Users must not hand the right to every user of the PC), and
    # never SYSTEM, LOCAL SERVICE, NETWORK SERVICE or an NT SERVICE\ account (S-1-5-80-).
    # Nothing is granted unless the connection is a SQL administrator, and a login that
    # already holds the right is skipped, so running it again changes nothing. Only ALTER ANY
    # LOGIN is ever granted.
    return @'
SET NOCOUNT ON; SET XACT_ABORT ON;
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1
BEGIN
  SELECT N'ETP_GRANT_SYSADMIN:0';
  RETURN;
END;
SELECT N'ETP_GRANT_SYSADMIN:1';
IF OBJECT_ID(N'dbo.application_users',N'U') IS NULL
BEGIN
  SELECT N'ETP_GRANT_USERS_TABLE:0';
  RETURN;
END;
DECLARE @owners TABLE(login_name sysname NOT NULL PRIMARY KEY);
INSERT @owners(login_name)
SELECT sp.name FROM sys.server_principals sp
WHERE sp.type='U' AND sp.sid<>SUSER_SID()
  AND sp.sid NOT IN(SID_BINARY(N'S-1-5-18'),SID_BINARY(N'S-1-5-19'),SID_BINARY(N'S-1-5-20'))
  AND SUBSTRING(sp.sid,3,10)<>SUBSTRING(SID_BINARY(N'S-1-5-80-0'),3,10)
  AND sp.principal_id IN(SELECT SUSER_ID(u.windows_identity) FROM dbo.application_users u WHERE u.role_code='OWNER' AND u.is_active=1)
  AND NOT EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=sp.principal_id
                 AND p.permission_name=N'ALTER ANY LOGIN' AND p.state='W');
DECLARE @grants nvarchar(max)=N'';
SELECT @grants+=N'GRANT ALTER ANY LOGIN TO '+QUOTENAME(login_name)+N' WITH GRANT OPTION;' FROM @owners;
IF LEN(@grants)>0
BEGIN
  SET @grants=N'USE [master]; '+@grants;
  EXEC(@grants);
END;
SELECT N'ETP_GRANT_GRANTED:'+CONVERT(nvarchar(10),COUNT(*)) FROM @owners;
SELECT N'ETP_GRANT_MISSING:'+CONVERT(nvarchar(10),COUNT(*)) FROM sys.server_principals sp
WHERE sp.type='U' AND sp.sid<>SUSER_SID()
  AND sp.sid NOT IN(SID_BINARY(N'S-1-5-18'),SID_BINARY(N'S-1-5-19'),SID_BINARY(N'S-1-5-20'))
  AND SUBSTRING(sp.sid,3,10)<>SUBSTRING(SID_BINARY(N'S-1-5-80-0'),3,10)
  AND sp.principal_id IN(SELECT SUSER_ID(u.windows_identity) FROM dbo.application_users u WHERE u.role_code='OWNER' AND u.is_active=1)
  AND NOT EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=sp.principal_id
                 AND p.permission_name=N'ALTER ANY LOGIN' AND p.state='W');
'@
}

function New-EtpOwnersWithoutGrantOptionSql {
    # Read-only, run in the ETP database by the account running setup: how many of the logins
    # the SYSTEM task would grant to (see New-EtpOwnerGrantOptionSql) still lack the grant
    # option. Anything but 0 is a reason to run the task.
    return @'
SET NOCOUNT ON;
SELECT COUNT(*) FROM sys.server_principals sp
WHERE sp.type='U'
  AND sp.sid NOT IN(SID_BINARY(N'S-1-5-18'),SID_BINARY(N'S-1-5-19'),SID_BINARY(N'S-1-5-20'))
  AND SUBSTRING(sp.sid,3,10)<>SUBSTRING(SID_BINARY(N'S-1-5-80-0'),3,10)
  AND sp.principal_id IN(SELECT SUSER_ID(u.windows_identity) FROM dbo.application_users u WHERE u.role_code='OWNER' AND u.is_active=1)
  AND NOT EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=sp.principal_id
                 AND p.permission_name=N'ALTER ANY LOGIN' AND p.state='W');
'@
}

function New-EtpSystemSqlAdministratorCheckSql {
    # Read-only, before any task is registered: is NT AUTHORITY\SYSTEM a SQL administrator,
    # directly or through BUILTIN\Administrators (how setup installs SQL Server Express)? YES
    # or NO. The task's own batch checks again, as SYSTEM, before it grants anything.
    return @'
SET NOCOUNT ON;
SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.server_role_members rm
  JOIN sys.server_principals r ON r.principal_id=rm.role_principal_id
  JOIN sys.server_principals m ON m.principal_id=rm.member_principal_id
  WHERE r.name=N'sysadmin' AND m.is_disabled=0 AND m.sid IN(SID_BINARY(N'S-1-5-18'),SID_BINARY(N'S-1-5-32-544')))
  THEN 'YES' ELSE 'NO' END;
'@
}

function Test-EtpRunningElevated {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-EtpOwnerGrantPreflight {
    # The one SQL call made as the account running setup; YES, NO or UNKNOWN.
    param([Parameter(Mandatory)][string]$SqlCmdPath,[Parameter(Mandatory)][string]$ServerInstance)
    Assert-EtpLocalSqlTarget $ServerInstance 'master'
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $SqlCmdPath -x -S $ServerInstance -E -b -h -1 -W -d master -Q (New-EtpSystemSqlAdministratorCheckSql) 2>$null)
        $exitCode = $LASTEXITCODE
    }
    catch { return 'UNKNOWN' }
    finally { $ErrorActionPreference = $previousPreference }
    $values = @($lines | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
    if ($exitCode -ne 0 -or $values.Count -ne 1 -or $values[0] -cnotin @('YES','NO')) { return 'UNKNOWN' }
    return $values[0]
}

function Assert-EtpOwnerGrantPlainPath {
    # Every path the task's command file names: a full local path in printable ASCII, with none
    # of the characters cmd.exe treats specially inside or around quotes.
    param([Parameter(Mandatory)][string]$Path)
    if ($Path -cnotmatch '^[A-Za-z]:\\[\x20-\x7E]+$' -or $Path -match '["%!^&|<>]') {
        throw 'The one-off grant task needs plain local folder and program paths.'
    }
}

function New-EtpOwnerGrantCommandScript {
    # The command file the SYSTEM task runs: sqlcmd with the fixed batch above, its output and
    # exit code into a file the caller reads. Every value is checked first, so nothing in it
    # can turn into a second command.
    param([Parameter(Mandatory)][string]$SqlCmdPath,[Parameter(Mandatory)][string]$ServerInstance,[Parameter(Mandatory)][string]$Database,
          [Parameter(Mandatory)][string]$SqlPath,[Parameter(Mandatory)][string]$OutputPath)
    Assert-EtpLocalSqlTarget $ServerInstance $Database
    if ($ServerInstance -match '["%!^&|<>\s]') { throw 'Choose a SQL Server instance on this computer.' }
    foreach ($path in @($SqlCmdPath, $SqlPath, $OutputPath)) { Assert-EtpOwnerGrantPlainPath $path }
    $lines = @(
        '@echo off',
        ('"{0}" -x -S "{1}" -E -b -h -1 -W -d {2} -i "{3}" > "{4}" 2>&1' -f $SqlCmdPath, $ServerInstance, $Database, $SqlPath, $OutputPath),
        'set ETPEXIT=%ERRORLEVEL%',
        # Redirection first: in "echo ...:0>> file" cmd.exe reads the 0 as a handle number.
        ('>> "{0}" echo ETP_GRANT_EXIT:%ETPEXIT%' -f $OutputPath),
        'exit /b %ETPEXIT%')
    return ($lines -join "`r`n") + "`r`n"
}

function ConvertFrom-EtpOwnerGrantOutput {
    # What the task's output file says. Outcome: Granted, AlreadyHeld, Incomplete, NotSysadmin
    # or Failed. Only the ETP_GRANT_ markers are read; sqlcmd's own messages, which can name
    # accounts, are never returned. A marker that appears twice counts as unreadable.
    param([AllowEmptyCollection()][AllowNull()][string[]]$Lines)
    $values = @{}
    foreach ($line in @($Lines | ForEach-Object { "$_".Trim() })) {
        if ($line -cmatch '^ETP_GRANT_([A-Z_]+):(-?\d{1,10})$') {
            if ($values.ContainsKey($Matches[1])) { $values[$Matches[1]] = $null } else { $values[$Matches[1]] = [long]$Matches[2] }
        }
    }
    $result = [pscustomobject]@{ Outcome = 'Failed'; Granted = 0; Missing = -1; ExitCode = $null; Detail = '' }
    if (-not $values.ContainsKey('EXIT') -or $null -eq $values['EXIT']) { $result.Detail = 'the task did not report an exit code'; return $result }
    $result.ExitCode = $values['EXIT']
    if ($values.ContainsKey('SYSADMIN') -and $values['SYSADMIN'] -eq 0) { $result.Outcome = 'NotSysadmin'; return $result }
    if ($result.ExitCode -ne 0) { $result.Detail = "sqlcmd ended with exit code $($result.ExitCode)"; return $result }
    if (-not $values.ContainsKey('SYSADMIN') -or $values['SYSADMIN'] -ne 1) { $result.Detail = 'SQL Server did not confirm that SYSTEM is a SQL administrator'; return $result }
    if ($values.ContainsKey('USERS_TABLE')) { $result.Detail = 'the database has no ETP user administration'; return $result }
    if (-not $values.ContainsKey('GRANTED') -or -not $values.ContainsKey('MISSING') -or $null -eq $values['GRANTED'] -or $null -eq $values['MISSING']) {
        $result.Detail = 'SQL Server did not report the result'; return $result
    }
    $result.Granted = [int]$values['GRANTED']
    $result.Missing = [int]$values['MISSING']
    $result.Outcome = if ($result.Missing -gt 0) { 'Incomplete' } elseif ($result.Granted -gt 0) { 'Granted' } else { 'AlreadyHeld' }
    return $result
}

function Get-EtpOwnerGrantManualCommand {
    # The documented fallback (docs\OPERATIONS.md, Owners and SQL Server logins) as one line for
    # an elevated PowerShell, for exactly this Owner and instance; $null when a value is not one
    # the line can carry safely, and the log then points at the document instead.
    param([string]$Identity,[string]$ServerInstance,[string]$SqlCmdPath)
    if ([string]::IsNullOrWhiteSpace($Identity) -or $Identity.Length -gt 128 -or $Identity -notmatch '^[^\\/\[\];''"%`$]+\\[^\\/\[\];''"%`$]+$') { return $null }
    if ([string]::IsNullOrWhiteSpace($ServerInstance) -or $ServerInstance -notmatch '^[A-Za-z0-9_.:()\\-]+$') { return $null }
    if ([string]::IsNullOrWhiteSpace($SqlCmdPath) -or $SqlCmdPath.Contains("'")) { return $null }
    try { Assert-EtpOwnerGrantPlainPath $SqlCmdPath } catch { return $null }
    $grant = "GRANT ALTER ANY LOGIN TO [$Identity] WITH GRANT OPTION"
    return ("`$a = New-ScheduledTaskAction -Execute '{0}' -Argument '-S {1} -E -b -d master -Q ""{2}""'; " +
        "Register-ScheduledTask -TaskName EtpOneOffOwnerGrant -Action `$a -User 'NT AUTHORITY\SYSTEM' -RunLevel Highest -Force | Out-Null; " +
        "Start-ScheduledTask -TaskName EtpOneOffOwnerGrant; Start-Sleep -Seconds 15; (Get-ScheduledTaskInfo -TaskName EtpOneOffOwnerGrant).LastTaskResult; " +
        "Unregister-ScheduledTask -TaskName EtpOneOffOwnerGrant -Confirm:`$false") -f $SqlCmdPath, $ServerInstance, $grant
}

function Test-EtpOwnerGrantTaskFinished {
    # Finished when the command file wrote its last line, or when the task is no longer running
    # and has a result (cmd.exe ended before it could write one).
    param([Parameter(Mandatory)][string]$TaskName,[Parameter(Mandatory)][string]$OutputPath)
    if (Test-Path -LiteralPath $OutputPath -PathType Leaf) {
        try {
            $done = @(Get-Content -LiteralPath $OutputPath -ErrorAction Stop | Where-Object { "$_".Trim() -cmatch '^ETP_GRANT_EXIT:-?\d+$' })
            if ($done.Count -gt 0) { return $true }
        }
        catch { }
    }
    $task = Get-ScheduledTask -TaskName $TaskName -TaskPath '\' -ErrorAction Stop
    if ("$($task.State)" -in @('Running', 'Queued')) { return $false }
    $info = Get-ScheduledTaskInfo -TaskName $TaskName -TaskPath '\' -ErrorAction Stop
    # 0x41301 is "currently running", 0x41303 "has not yet run".
    return ([long]$info.LastTaskResult -notin @(267009, 267011))
}

function Remove-EtpOwnerGrantWorkFolder {
    # Only the folder this run created, recognised by its exact name inside the work root.
    param([Parameter(Mandatory)][string]$Folder,[Parameter(Mandatory)][string]$WorkRoot)
    $root = [IO.Path]::GetFullPath($WorkRoot).TrimEnd('\')
    $full = [IO.Path]::GetFullPath($Folder)
    if ([IO.Path]::GetDirectoryName($full) -ine $root -or [IO.Path]::GetFileName($full) -cnotmatch '^OwnerGrant-[a-f0-9]{32}$') { throw 'Not a one-off grant work folder.' }
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { return }
    Assert-EtpNoLinks $full
    Remove-Item -LiteralPath $full -Recurse -Force
}

function Format-EtpOwnerGrantMessage {
    # One log line for the attempt. The caller's read-only re-check decides whether the NOTE
    # with the manual command follows.
    param([Parameter(Mandatory)][object]$Result)
    $task = 'a one-off scheduled task run as SYSTEM'
    $message = switch -CaseSensitive ($Result.Outcome) {
        'Granted' { "$($Result.Granted) active Owner login(s) were given ALTER ANY LOGIN WITH GRANT OPTION through $task, so Owners can change users in Settings > Users without 'Run as administrator'." }
        'AlreadyHeld' { "Every active Owner with a SQL Server login already holds ALTER ANY LOGIN WITH GRANT OPTION; $task found nothing to grant." }
        'NotSysadmin' { "NOTE: SYSTEM is not a SQL Server administrator on this instance, so ALTER ANY LOGIN WITH GRANT OPTION could not be given to the Owner through $task. Nothing was changed." }
        'Skipped' { "NOTE: ALTER ANY LOGIN WITH GRANT OPTION was not given to the Owner: $($Result.Detail). Nothing was changed." }
        'Incomplete' { "WARNING: $task gave ALTER ANY LOGIN WITH GRANT OPTION to $($Result.Granted) Owner login(s), but $($Result.Missing) active Owner login(s) still lack it." }
        'TimedOut' { "WARNING: $task meant to give the Owner ALTER ANY LOGIN WITH GRANT OPTION did not finish in time and was stopped. Nothing else depends on it." }
        default { "WARNING: $task could not give the Owner ALTER ANY LOGIN WITH GRANT OPTION ($($Result.Detail)). Nothing else depends on it." }
    }
    if ($Result.PSObject.Properties['Leftovers']) {
        foreach ($leftover in @($Result.Leftovers)) { if ($leftover) { $message += " WARNING: $leftover" } }
    }
    return $message
}

function Invoke-EtpOwnerGrantOptionAsSystem {
    # Never throws: the result says what happened (Outcome, Granted, Missing, Detail, Message).
    param([Parameter(Mandatory)][string]$SqlCmdPath,[Parameter(Mandatory)][string]$ServerInstance,[Parameter(Mandatory)][string]$Database,
          [string]$WorkRoot,[ValidateRange(1, 3600)][int]$TimeoutSeconds = 120)
    $result = [pscustomobject]@{ Outcome = 'Failed'; Granted = 0; Missing = -1; ExitCode = $null; Detail = ''; TaskName = $null; Leftovers = @(); Message = '' }
    $taskName = $null
    $registered = $false
    $work = $null
    try {
        if ([string]::IsNullOrWhiteSpace($WorkRoot)) { $WorkRoot = Join-Path $env:ProgramData 'EtpReporting' }
        if (-not (Test-EtpRunningElevated)) { $result.Outcome = 'Skipped'; $result.Detail = 'this was not run as administrator' }
        else {
            $preflight = Invoke-EtpOwnerGrantPreflight -SqlCmdPath $SqlCmdPath -ServerInstance $ServerInstance
            if ($preflight -ceq 'NO') { $result.Outcome = 'NotSysadmin' }
            else {
                # UNKNOWN goes on: the batch checks again, as SYSTEM, before it grants anything.
                $work = New-EtpProtectedDirectory -Path (Join-Path $WorkRoot ('OwnerGrant-' + [Guid]::NewGuid().ToString('N')))
                $sqlPath = Join-Path $work 'grant-owner-option.sql'
                $commandPath = Join-Path $work 'grant-owner-option.cmd'
                $outputPath = Join-Path $work 'grant-owner-option.out'
                $commandText = New-EtpOwnerGrantCommandScript -SqlCmdPath $SqlCmdPath -ServerInstance $ServerInstance -Database $Database -SqlPath $sqlPath -OutputPath $outputPath
                [IO.File]::WriteAllText($sqlPath, (New-EtpOwnerGrantOptionSql), [Text.Encoding]::ASCII)
                [IO.File]::WriteAllText($commandPath, $commandText, [Text.Encoding]::ASCII)
                $taskName = 'ETP Reporting Owner Grant ' + [Guid]::NewGuid().ToString('N')
                $result.TaskName = $taskName
                $action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\cmd.exe') -Argument ('/d /v:off /s /c ""' + $commandPath + '""')
                $principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
                $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 10) -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
                Register-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $action -Principal $principal -Settings $settings `
                    -Description 'One-off: gives active ETP Owners ALTER ANY LOGIN WITH GRANT OPTION. ETP setup removes it when it ends.' -ErrorAction Stop | Out-Null
                $registered = $true
                Start-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction Stop
                $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
                $finished = $false
                while (-not $finished) {
                    Start-Sleep -Milliseconds 250
                    $finished = [bool](Test-EtpOwnerGrantTaskFinished -TaskName $taskName -OutputPath $outputPath)
                    if (-not $finished -and [DateTime]::UtcNow -ge $deadline) { break }
                }
                if (-not $finished) {
                    try { Stop-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction Stop } catch { }
                    $result.Outcome = 'TimedOut'
                }
                else {
                    $lines = @()
                    if (Test-Path -LiteralPath $outputPath -PathType Leaf) { $lines = @(Get-Content -LiteralPath $outputPath -ErrorAction Stop) }
                    $parsed = ConvertFrom-EtpOwnerGrantOutput -Lines $lines
                    foreach ($name in @('Outcome', 'Granted', 'Missing', 'ExitCode', 'Detail')) { $result.$name = $parsed.$name }
                }
            }
        }
    }
    catch {
        $result.Outcome = 'Failed'
        $result.Detail = ($_.Exception.Message -replace '[\x00-\x1F\x7F]', ' ').Trim()
    }
    finally {
        # Always, whatever happened above: no task and no file outlives this call.
        if ($taskName) {
            try { Unregister-ScheduledTask -TaskName $taskName -TaskPath '\' -Confirm:$false -ErrorAction Stop }
            catch { if ($registered) { $result.Leftovers += "the scheduled task '$taskName' could not be removed; delete it in Task Scheduler." } }
        }
        if ($work) {
            try { Remove-EtpOwnerGrantWorkFolder -Folder $work -WorkRoot $WorkRoot }
            catch { $result.Leftovers += "the folder $work could not be removed; delete it by hand." }
        }
    }
    $result.Message = Format-EtpOwnerGrantMessage -Result $result
    return $result
}
