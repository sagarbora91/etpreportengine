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

function Get-EtpProtectedPathFindings {
    # One step of Assert-EtpProtectedInstall: what on this one path would let somebody who is
    # not an administrator change what runs. Role is Target for the file or folder being
    # checked, Ancestor for each folder above it and Root for the volume root at the top.
    # Returns one line per problem naming the path, the identity, the rights and the fix;
    # nothing when the path is safe. Kept in step with ProtectedOperationPath in the desktop
    # application.
    #
    # Why the rights differ by role (2 Oct 2026, a second drive on Workpc, where the old
    # all-or-nothing rule refused a sound installation until E:\ was re-permissioned by hand):
    # - Target: anything that changes it. Write (create, change or append), Delete,
    #   delete-child, change permissions, take ownership.
    # - Ancestor: anything that can swap the path out from under the target. Renaming a
    #   folder needs Delete on it or delete-child on its parent; change permissions and take
    #   ownership lead to either. Write on an ancestor only creates NEW names beside the path,
    #   which cannot replace or redirect any existing component of it.
    # - Root: the same, less Delete, because a volume root cannot be renamed or deleted. This
    #   is the only right relaxed. A Windows-formatted data drive gives Authenticated Users
    #   Modify (which includes Delete, but not delete-child or change permissions) on its root.
    #   Inheritable copies of root rights still reach folders under it, and are checked there.
    # Ignored, as before: Deny entries, and inherit-only entries, which apply only to children
    # and are checked on them. Also ignored: application package and capability SIDs
    # (S-1-15-2-*, S-1-15-3-*, e.g. ALL APPLICATION PACKAGES). Windows only consults them in a
    # second access check for an AppContainer process, whose access is the intersection of
    # both checks and which runs at low integrity, so they never give anyone more than that
    # user already has. Installers often grant them Full Control on a data drive's Program Files.
    # Owner: an owner can always rewrite the permissions, so it must be Administrators, SYSTEM
    # or TrustedInstaller at every level. That stays, including for an administrator's own
    # account: its unelevated programs would then be able to change what setup runs elevated.
    param([Parameter(Mandatory)][string]$Path,
          [Parameter(Mandatory)][Security.AccessControl.FileSystemSecurity]$Security,
          [Parameter(Mandatory)][ValidateSet('Target','Ancestor','Root')][string]$Role)
    $trusted = @('S-1-5-18','S-1-5-32-544','S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
    $isFolder = $Security -is [Security.AccessControl.DirectorySecurity]
    $describeSid = {
        param([Security.Principal.SecurityIdentifier]$Sid)
        try { return '{0} ({1})' -f $Sid.Translate([Security.Principal.NTAccount]).Value, $Sid.Value } catch { return $Sid.Value }
    }
    $findings = @()
    $owner = $Security.GetOwner([Security.Principal.SecurityIdentifier])
    if ($null -eq $owner -or $owner.Value -notin $trusted) {
        $ownerText = if ($owner) { & $describeSid $owner } else { 'nobody' }
        $findings += "'$Path' is owned by $ownerText, and an owner can always change its permissions. Install operations in a folder owned by Administrators or SYSTEM. Fix: icacls `"$Path`" /setowner `"*S-1-5-32-544`""
    }
    # Raw access-mask bits, so generic rights (which FileSystemRights does not name) are seen too.
    $write = 0x116; $delete = 0x10000; $deleteChild = 0x40; $changePermissions = 0x40000; $takeOwnership = 0x80000
    $mask = $deleteChild -bor $changePermissions -bor $takeOwnership
    if ($Role -ne 'Root') { $mask = $mask -bor $delete }
    if ($Role -eq 'Target') { $mask = $mask -bor $write }
    $names = @(
        @($write, $(if ($isFolder) { 'write (create or change items in it)' } else { 'write (change it)' })),
        @($delete, 'delete (rename or delete it)'),
        @($deleteChild, 'delete subfolders and files (rename or delete anything in it)'),
        @($changePermissions, 'change permissions'),
        @($takeOwnership, 'take ownership'))
    foreach ($rule in $Security.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow) { continue }
        if ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly) { continue }
        $sid = $rule.IdentityReference.Value
        if ($sid -in $trusted -or $sid -like 'S-1-15-2-*' -or $sid -like 'S-1-15-3-*') { continue }
        $rights = [int64][int]$rule.FileSystemRights
        if ($rights -lt 0) { $rights += 4294967296 }
        # GENERIC_ALL and MAXIMUM_ALLOWED as everything, GENERIC_WRITE as FILE_GENERIC_WRITE.
        if ($rights -band 0x12000000) { $rights = $rights -bor 0x1F01FF }
        if ($rights -band 0x40000000) { $rights = $rights -bor $write }
        $granted = $rights -band $mask
        if (-not $granted) { continue }
        $what = @($names | Where-Object { $granted -band $_[0] } | ForEach-Object { $_[1] }) -join ', '
        $readOnly = if ($isFolder) { '(OI)(CI)RX' } else { 'RX' }
        $fix = "icacls `"$Path`" /grant:r `"*${sid}:$readOnly`""
        if ($rule.IsInherited) { $fix = "icacls `"$Path`" /inheritance:d, then $fix" }
        $source = if ($rule.IsInherited) { ', inherited from the folder above' } else { '' }
        $findings += "'$Path' gives $(& $describeSid $rule.IdentityReference) $what$source. Fix (keeps read access only): $fix"
    }
    return $findings
}

function Get-EtpProtectedInstallFindings {
    # Every problem on the way from Path up to its root, so one message lists all there is to
    # fix. ReadSecurity is replaceable so tests can describe a whole drive layout without
    # touching real permissions.
    param([Parameter(Mandatory)][string]$Path,[scriptblock]$ReadSecurity = { param($Item) Get-Acl -LiteralPath $Item })
    $findings = @()
    $current = [IO.Path]::GetFullPath($Path)
    $role = 'Target'
    while ($current) {
        $parent = [IO.Path]::GetDirectoryName($current)
        if (-not $parent -and $role -eq 'Ancestor') { $role = 'Root' }
        $findings += @(Get-EtpProtectedPathFindings -Path $current -Security (& $ReadSecurity $current) -Role $role)
        $current = $parent
        $role = 'Ancestor'
    }
    return $findings
}

function Format-EtpProtectedInstallRefusal {
    # Setup does not repair these itself. Re-permissioning the folder after its files were
    # copied would also bless anything a non-administrator changed in them meanwhile, and the
    # folders above belong to Windows and other software. Say exactly what to change instead,
    # except inside a user profile or the Windows folder, where following that advice would
    # lock a user out of their own files or loosen Windows; there the answer is to move.
    param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string[]]$Findings,[string[]]$UnfixableRoots = @())
    $headline = if (@($Findings | Where-Object { $_ -notmatch 'Install operations in a folder owned by' }).Count -gt 0) { 'The installation folder can be changed by a non-administrator.' } else { 'Install operations in a folder owned by Administrators or SYSTEM.' }
    $full = [IO.Path]::GetFullPath($Path)
    foreach ($root in @($UnfixableRoots | Where-Object { $_ })) {
        if ($full.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            return "$headline '$full' is inside '$root', whose permissions ETP will not ask you to change. Install it under Program Files instead, in a folder owned by Administrators or SYSTEM."
        }
    }
    return ($headline + ' Nothing was changed. Fix each item below from an administrator PowerShell window, then try again:' + [Environment]::NewLine + (($Findings | ForEach-Object { '- ' + $_ }) -join [Environment]::NewLine))
}

function Assert-EtpProtectedInstall {
    param([Parameter(Mandatory)][string]$Path)
    Assert-EtpNoLinks $Path
    $findings = @(Get-EtpProtectedInstallFindings $Path)
    if ($findings.Count -eq 0) { return }
    $profiles = $null
    try { $profiles = [Environment]::ExpandEnvironmentVariables((Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList' -Name ProfilesDirectory -ErrorAction Stop).ProfilesDirectory) } catch { $profiles = $null }
    throw (Format-EtpProtectedInstallRefusal -Path $Path -Findings $findings -UnfixableRoots @($profiles, $env:SystemRoot))
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

function Test-EtpOdbc17SqlCmdPath {
    # The Sqlcmd of Command Line Utilities 15, on ODBC Driver 17, wherever it was installed.
    param([string]$Path)
    return ($Path -match '(?i)\\Client SDK\\ODBC\\170\\Tools\\Binn\\SQLCMD\.EXE$')
}

function Get-EtpRegisteredSqlCmdFolders {
    # Where the SQL Server client installers recorded their command-line tools. The Command
    # Line Utilities MSI and SQL Server setup both write ODBCToolsPath under
    # HKLM\SOFTWARE\Microsoft\Microsoft SQL Server\<version>\Tools\ClientSetup, wherever they
    # were installed - which is not always %ProgramFiles%. On Workpc (2 October 2026) SQL
    # Server and the bundled Sqlcmd went to E:\Program Files, and a resolver that looked only
    # in %ProgramFiles% (C:) failed setup. Newest version first. Only administrators can write
    # HKLM; every folder named here is still checked like any other before it is used.
    $root = 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server'
    if (-not (Test-Path -LiteralPath $root)) { return @() }
    $folders = @()
    $versions = @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^\d{2,4}$' } | Sort-Object { [int]$_.PSChildName } -Descending)
    foreach ($version in $versions) {
        $key = $null
        try {
            $key = $version.OpenSubKey('Tools\ClientSetup')
            if ($null -eq $key) { continue }
            $value = $key.GetValue('ODBCToolsPath')
            if ($value -is [string] -and -not [string]::IsNullOrWhiteSpace($value)) { $folders += $value }
        }
        finally { if ($key) { $key.Close() } }
    }
    return $folders
}

function Get-EtpProgramFilesFolders {
    # %ProgramFiles%, then the Program Files folder on the drive ETP itself is installed on
    # (these scripts live in the application's scripts folder). A PC set up to install
    # programs on another drive puts SQL Server's tools there too.
    param([string]$ApplicationDirectory = $PSScriptRoot)
    $folders = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) { $folders += $env:ProgramFiles }
    if (-not [string]::IsNullOrWhiteSpace($ApplicationDirectory)) {
        try { $drive = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($ApplicationDirectory)) } catch { $drive = $null }
        if ($drive -match '^[A-Za-z]:\\$') { $folders += [IO.Path]::Combine($drive, 'Program Files') }
    }
    return $folders
}

function Get-EtpSqlCmdCandidatePaths {
    # The places Resolve-EtpSqlCmd looks, in order. Pure, so it can be tested without the
    # registry or a Program Files folder. Only absolute local paths (drive letter, no UNC)
    # are ever considered. Every ODBC 17 Sqlcmd comes first, wherever it was found; then
    # any other ODBC Sqlcmd (ODBC 18); go-sqlcmd last.
    param([string[]]$RegisteredToolsFolders,[string[]]$ProgramFilesFolders)
    $odbc = @(); $go = @()
    foreach ($folder in @($RegisteredToolsFolders | Where-Object { $_ })) {
        if ($folder -match '^[A-Za-z]:\\') { $odbc += [IO.Path]::Combine($folder, 'SQLCMD.EXE') }
    }
    foreach ($programFiles in @($ProgramFilesFolders | Where-Object { $_ })) {
        if ($programFiles -notmatch '^[A-Za-z]:\\') { continue }
        $odbc += [IO.Path]::Combine($programFiles, 'Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE')
        $odbc += [IO.Path]::Combine($programFiles, 'Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE')
        $go += [IO.Path]::Combine($programFiles, 'sqlcmd\sqlcmd.exe')
    }
    $seen = @{}
    $ordered = @()
    foreach ($candidate in @(@($odbc | Where-Object { Test-EtpOdbc17SqlCmdPath $_ }) + @($odbc | Where-Object { -not (Test-EtpOdbc17SqlCmdPath $_) }) + $go)) {
        try { $full = [IO.Path]::GetFullPath($candidate) } catch { continue }
        if ($seen.ContainsKey($full.ToUpperInvariant())) { continue }
        $seen[$full.ToUpperInvariant()] = $true
        $ordered += $full
    }
    return $ordered
}

function Resolve-EtpSqlCmd {
    param([string]$ExplicitPath,[switch]$Odbc17Only)
    # The ODBC client reaches a local instance over shared memory, which SQL
    # Server Express and Developer enable by default. go-sqlcmd resolves a bare
    # ".\INSTANCE" over named pipes, which they disable by default, so it is
    # preferred only when the ODBC client is absent.
    # The ODBC 17 Sqlcmd comes first. The ODBC 18 one, which SQL Server 2025's own setup
    # installs, encrypts by default and refuses the instance's self-signed certificate,
    # so on a new PC (1 October 2026) every call setup made failed with "The certificate
    # chain was issued by an authority that is not trusted". It stays as the fallback:
    # pinning a single version meant a machine with only the current tools resolved
    # nothing and fell through to go-sqlcmd. -Odbc17Only is setup's "is the bundled
    # Sqlcmd installed" question.
    # A path the operator named is used only if it passes the protection check.
    if ($ExplicitPath -and (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
        Assert-EtpProtectedInstall $ExplicitPath
        return [IO.Path]::GetFullPath($ExplicitPath)
    }
    # Found ones are tried in order, and only one in an Administrators/SYSTEM-protected
    # folder is ever returned. One that fails the check is passed over for the next, and
    # its refusal is what is reported if none passes.
    $refusal = $null
    $candidates = @(Get-EtpSqlCmdCandidatePaths -RegisteredToolsFolders @(Get-EtpRegisteredSqlCmdFolders) -ProgramFilesFolders @(Get-EtpProgramFilesFolders))
    if ($Odbc17Only) { $candidates = @($candidates | Where-Object { Test-EtpOdbc17SqlCmdPath $_ }) }
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        try {
            Assert-EtpProtectedInstall $candidate
            return [IO.Path]::GetFullPath($candidate)
        }
        catch { if ($null -eq $refusal) { $refusal = $_.Exception.Message } }
    }
    if ($null -ne $refusal) { throw $refusal }
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

# 1.9.3. Setup installs the operations broker alone. The automation account gets its rights
# (etp_automation, db_backupoperator, EXECUTE on the signed broker) only from a full
# install-etp-sql-operations.ps1 run, which needs the account to be an active Store Manager
# first. Until then the daily backup and the drill's recording failed with only the masked
# "The database operation failed", as on Workpc on 2 October 2026. These functions say which
# right is missing and what to run, without ever showing SQL Server's own error text.
$EtpMaskedSqlFailure = 'The database operation failed. Check SQL permissions and operation prerequisites.'

function Get-EtpAutomationGrantQuery {
    param([Parameter(Mandatory)][string]$Database,[Parameter(Mandatory)][string]$AutomationPrincipal)
    if ($AutomationPrincipal -notmatch '^[^\\/\[\];''"]+\\[^\\/\[\];''"]+$') { throw 'Configure a dedicated local automation account.' }
    $identity = $AutomationPrincipal.Replace("'","''")
    $databaseLiteral = $Database.Replace("'","''")
    $procedure = Get-EtpOperationsProcedureName $Database
    # Read-only. A SQL administrator (setup, the drill) sees everything. The automation
    # account itself (the daily backup) sees only its own roles and its own EXECUTE right,
    # so the signature is not judged from there. Anyone else gets UNKNOWN.
    return @"
SET NOCOUNT ON;
DECLARE @identity sysname=N'$identity', @database sysname=N'$databaseLiteral', @procedure sysname=N'$procedure';
DECLARE @admin bit=CASE WHEN IS_SRVROLEMEMBER(N'sysadmin')=1 THEN 1 ELSE 0 END;
DECLARE @self bit=CASE WHEN SUSER_SID()=SUSER_SID(@identity) THEN 1 ELSE 0 END;
DECLARE @found TABLE(item nvarchar(100));
IF (@admin=0 AND @self=0) OR DB_ID(@database) IS NULL BEGIN SELECT N'ETP_AUTOMATION:UNKNOWN'; RETURN; END;
INSERT @found VALUES(CASE WHEN @admin=1 THEN N'CALLER_ADMIN' ELSE N'CALLER_SELF' END);
IF SUSER_ID(@identity) IS NOT NULL
BEGIN
    INSERT @found VALUES(N'LOGIN');
    DECLARE @sql nvarchar(max)=N'USE '+QUOTENAME(@database)+N';
        DECLARE @u sysname=(SELECT name FROM sys.database_principals WHERE sid=SUSER_SID(@identity));
        SELECT N''USER'' WHERE @u IS NOT NULL
        UNION ALL SELECT N''STORE_MANAGER'' WHERE IS_ROLEMEMBER(N''etp_store_manager'',@u)=1
        UNION ALL SELECT N''ACTIVE'' WHERE EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=@identity AND role_code=''STORE_MANAGER'' AND is_active=1)
        UNION ALL SELECT N''ROLE:etp_automation'' WHERE IS_ROLEMEMBER(N''etp_automation'',@u)=1
        UNION ALL SELECT N''ROLE:db_backupoperator'' WHERE IS_ROLEMEMBER(N''db_backupoperator'',@u)=1;';
    INSERT @found EXEC sys.sp_executesql @sql, N'@identity sysname', @identity=@identity;
    IF @admin=1
    BEGIN
        IF OBJECT_ID(N'dbo.'+QUOTENAME(@procedure),N'P') IS NOT NULL INSERT @found VALUES(N'BROKER');
        IF EXISTS(SELECT 1 FROM sys.database_permissions p JOIN sys.database_principals dp ON dp.principal_id=p.grantee_principal_id
                  WHERE dp.sid=SUSER_SID(@identity) AND p.major_id=OBJECT_ID(N'dbo.'+QUOTENAME(@procedure))
                    AND p.permission_name=N'EXECUTE' AND p.state IN ('G','W'))
            INSERT @found VALUES(N'BROKER_EXECUTE');
        IF EXISTS(SELECT 1 FROM sys.crypt_properties WHERE major_id=OBJECT_ID(N'dbo.'+QUOTENAME(@procedure)))
            INSERT @found VALUES(N'BROKER_SIGNED');
    END
    ELSE IF HAS_PERMS_BY_NAME(N'dbo.'+QUOTENAME(@procedure),N'OBJECT',N'EXECUTE')=1
        INSERT @found VALUES(N'BROKER_EXECUTE');
END;
SELECT N'ETP_AUTOMATION:'+item FROM @found;
"@
}

function ConvertFrom-EtpAutomationGrantResult {
    # Pure: turns the query's lines into a state. Kept apart from SQL so it can be tested.
    param([AllowEmptyCollection()][string[]]$Lines)
    $items = @($Lines | ForEach-Object { "$_".Trim() } | Where-Object { $_.StartsWith('ETP_AUTOMATION:') } | ForEach-Object { $_.Substring(15) })
    if ($items.Count -eq 0 -or $items -ccontains 'UNKNOWN') { return [pscustomobject]@{ State = 'UNKNOWN'; Missing = @() } }
    $admin = $items -ccontains 'CALLER_ADMIN'
    if (-not ($items -ccontains 'LOGIN' -and $items -ccontains 'USER' -and $items -ccontains 'STORE_MANAGER' -and $items -ccontains 'ACTIVE')) {
        return [pscustomobject]@{ State = 'NOT_STORE_MANAGER'; Missing = @() }
    }
    $missing = [Collections.Generic.List[string]]::new()
    foreach ($role in @('etp_automation','db_backupoperator')) { if (-not ($items -ccontains "ROLE:$role")) { $missing.Add("the $role database role") } }
    if ($admin -and -not ($items -ccontains 'BROKER')) { $missing.Add('the operations broker in master') }
    if (-not ($items -ccontains 'BROKER_EXECUTE')) { $missing.Add('EXECUTE on the operations broker') }
    if ($admin -and -not ($items -ccontains 'BROKER_SIGNED')) { $missing.Add('the broker''s module signature') }
    if ($missing.Count -gt 0) { return [pscustomobject]@{ State = 'GRANTS_MISSING'; Missing = @($missing) } }
    return [pscustomobject]@{ State = 'READY'; Missing = @() }
}

function Get-EtpAutomationGrantState {
    param([string]$SqlCmd,[string]$Server,[string]$Database,[string]$AutomationPrincipal)
    $lines = @(Invoke-EtpSql -SqlCmd $SqlCmd -Server $Server -Query (Get-EtpAutomationGrantQuery -Database $Database -AutomationPrincipal $AutomationPrincipal))
    return ConvertFrom-EtpAutomationGrantResult -Lines $lines
}

function Get-EtpAutomationGrantCommand {
    param([string]$ServerInstance,[string]$Database,[string]$AutomationPrincipal,[string]$ScriptsDirectory=$PSScriptRoot)
    $script = Join-Path $ScriptsDirectory 'install-etp-sql-operations.ps1'
    return "powershell.exe -ExecutionPolicy Bypass -File '$script' -ServerInstance '$ServerInstance' -Database '$Database' -AutomationPrincipal '$AutomationPrincipal'"
}

function Get-EtpAutomationGrantGuidance {
    # The sentence an operator acts on, or $null when the account is ready or its state
    # cannot be read (the caller then keeps its own message).
    param([Parameter(Mandatory)]$GrantState,[string]$ServerInstance,[string]$Database,[string]$AutomationPrincipal,[string]$ScriptsDirectory=$PSScriptRoot)
    $command = Get-EtpAutomationGrantCommand -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $AutomationPrincipal -ScriptsDirectory $ScriptsDirectory
    switch ($GrantState.State) {
        'NOT_STORE_MANAGER' {
            return "The automation account $AutomationPrincipal is not an active Store Manager of $Database, so the scheduled backup and the recovery drill cannot run under it. In ETP as Owner, started with 'Run as administrator', add $AutomationPrincipal as an active Store Manager in Settings > Users. Then run ETP setup again, which completes its backup rights, or run this in an administrator PowerShell window: $command (docs\OPERATIONS.md, step 7)."
        }
        'GRANTS_MISSING' {
            return "The automation account $AutomationPrincipal is an active Store Manager but does not yet have the operations module's rights (missing: $($GrantState.Missing -join ', ')). Run ETP setup again, which completes them, or run this in an administrator PowerShell window: $command (docs\OPERATIONS.md, step 7)."
        }
        default { return $null }
    }
}

function Get-EtpAutomationFailureMessage {
    # Called when a backup or drill step has failed. Only the masked failure is explained,
    # and only when the account's rights really are incomplete; any other failure, or a
    # failure to read the state, returns $null and the original error stands.
    param([string]$Message,[string]$SqlCmd,[string]$Server,[string]$ServerInstance,[string]$Database,[string]$AutomationPrincipal)
    if ($Message -cne $EtpMaskedSqlFailure -or [string]::IsNullOrWhiteSpace($SqlCmd) -or [string]::IsNullOrWhiteSpace($AutomationPrincipal)) { return $null }
    try { $state = Get-EtpAutomationGrantState -SqlCmd $SqlCmd -Server $Server -Database $Database -AutomationPrincipal $AutomationPrincipal }
    catch { return $null }
    return Get-EtpAutomationGrantGuidance -GrantState $state -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $AutomationPrincipal
}

function Get-EtpWatchFolderNames {
    # The automatic-import folders under %ProgramData%\EtpReporting that
    # dbo.watch_folder_settings names by default (migration 0011): inbound, processed,
    # failed and report output. The service also files duplicates in Processed\Duplicate.
    return @('Inbound','Processed','Failed','ReportPacks')
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
