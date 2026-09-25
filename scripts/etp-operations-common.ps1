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
