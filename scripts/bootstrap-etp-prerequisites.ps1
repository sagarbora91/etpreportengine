param(
    [Parameter(Mandatory)][string]$ApplicationDirectory,
    # P4-11. Setup passes this only when it actually carries the SQL media, so the
    # option and the licence prompt cannot appear for an install that cannot happen.
    [switch]$SkipSqlInstallation,
    [string]$SqlPayloadDirectory,
    [ValidateRange(0.1, 1048576)][double]$MinimumBackupFreeSpaceGb = 5,
    # Setup's "Create a new empty database" option, unticked. Only a MISSING database is
    # affected: setup prepares SQL Server and the configuration and stops without creating
    # it, so an existing backup can be restored first with restore-etp-database.ps1. A
    # database created now would have to be replaced by that restore, which the recovery
    # procedures never do (no WITH REPLACE).
    [switch]$DeferDatabaseCreation
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')

# Setup reads this exit code as "ready for a restore", not as a failure. It must equal
# DatabaseRestorePendingExitCode in installer\EtpReportingEngine.iss.
$DatabaseRestorePendingExitCode = 2
# What setup provisions on a PC that has no ETP configuration yet (the documented defaults).
$EtpNewMachineServerInstance = '.\SQLEXPRESS'
$EtpNewMachineDatabase = 'EtpReporting'

function Resolve-EtpBootstrapServiceName {
    param([Parameter(Mandatory)][string]$ServerInstance,[Parameter(Mandatory)][string]$Database)
    Assert-EtpLocalSqlTarget $ServerInstance $Database
    $server = $ServerInstance.Trim()
    if ($server -match '^(?i)(lpc|np):') { $server = $server.Substring($server.IndexOf(':')+1) }
    if ($server.StartsWith('\\') -or $server.StartsWith('(localdb)',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Bootstrap requires a standard local SQL Server service endpoint. Configure a supported default or named instance manually first.'
    }
    $parts = $server.Split('\')
    if ($parts.Count -eq 1) { return 'MSSQLSERVER' }
    return 'MSSQL$'+$parts[1]
}

function Assert-EtpBootstrapSqlEdition {
    param([int]$MajorVersion,[int]$EngineEdition,[string]$Edition)
    if ($MajorVersion -lt 16) { throw 'SQL Server 2022 or newer is required. Install a supported edition manually before bootstrap.' }
    # D9 revised: Express and Web are accepted. They cannot encrypt a backup, so the
    # backup runs unencrypted and its receipt says so; protecting the backup folder at
    # rest is a separate, deferred control rather than a reason to refuse the edition.
    if ($EngineEdition -notin @(2,3,4)) {
        throw 'This SQL Server edition is not supported. Install SQL Server Express or a fuller edition before bootstrap.'
    }
    if ($Edition -match '(?i)Express|Web') {
        Write-Warning 'This SQL Server edition cannot encrypt backups. Backups will be unencrypted; protect the backup folder at rest.'
    }
}

function Assert-EtpBootstrapAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'ETP prerequisite configuration requires administrator rights.'
    }
}

function Get-EtpSqlEngineSetupArguments {
    param([Parameter(Mandatory)][string]$InstanceName)
    if ($InstanceName -notmatch '^[A-Za-z0-9_$-]{1,16}$') { throw 'The configured SQL Server instance name could not be determined.' }
    # Shared memory only: the engine is local to this machine and must not listen
    # on the network. Administrators become sysadmin so the owner can administer it.
    # SECURITYMODE is omitted deliberately: its only supported value is SQL, and
    # omitting it is the documented way to get Windows-only authentication.
    # The collation is the one the owner's database has always had. Left to SQL setup it
    # follows the Windows locale, and an en-US machine gets SQL_Latin1_General_CP1_CI_AS
    # (the acceptance VM did, 24 September 2026). The monthly recovery drill compares a
    # temporary table, which takes the server's collation, with the restored copy's file
    # list, which takes the database's, and has no COLLATE clause: on such an instance a
    # restored Latin1_General_CI_AS database fails its drill with a collation conflict.
    # A new database simply inherits it. Only an instance setup installs is affected.
    # Express setup also makes the account running it a SQL administrator unless told not
    # to (ADDCURRENTUSERASSQLADMIN defaults to True for Express). On the first real new PC
    # (1 October 2026) that left the Owner's own account in sysadmin, beside Administrators.
    return @('/ACTION=Install','/QUIET','/IACCEPTSQLSERVERLICENSETERMS','/FEATURES=SQLENGINE',
        "/INSTANCENAME=$InstanceName",'/SQLSYSADMINACCOUNTS=BUILTIN\Administrators','/ADDCURRENTUSERASSQLADMIN=False',
        '/SQLCOLLATION=Latin1_General_CI_AS','/TCPENABLED=0','/NPENABLED=0','/UPDATEENABLED=0')
}

function Install-EtpSqlEngineFromPayload {
    param([Parameter(Mandatory)][string]$PayloadDirectory,[Parameter(Mandatory)][string]$ServiceName)
    $engine = Get-ChildItem -LiteralPath $PayloadDirectory -Filter 'SQLEXPR*_x64_*.exe' -File | Select-Object -First 1
    if (-not $engine) { throw 'The bundled SQL Server Express package was not found in the installer media.' }
    # Install the instance the configuration actually asks for. Hard-coding
    # SQLEXPRESS would install an instance, then fail looking for the configured
    # service, and leave SQL Server behind on the machine.
    $instance = if ($ServiceName -ceq 'MSSQLSERVER') { 'MSSQLSERVER' } else { $ServiceName.Substring($ServiceName.IndexOf('$') + 1) }
    $arguments = Get-EtpSqlEngineSetupArguments $instance

    # The package is a self-extractor; extract, then run its own setup unattended. The
    # extracted setup.exe runs with the administrator's full token, so it must not sit in a
    # folder the user's unelevated processes can write - which the user's Temp folder, where
    # this used to extract, is. The path has no spaces: /X: with a quoted path is unverified.
    $extract = $null
    try {
        $extract = New-EtpProtectedDirectory -Path (Join-Path $env:ProgramData ('EtpSqlSetup-' + [Guid]::NewGuid().ToString('N')))
        Start-EtpProcess -FilePath $engine.FullName -Arguments @('/Q', "/X:$extract") -Description 'extract the SQL Server media'
        $setup = Join-Path $extract 'setup.exe'
        if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
            # Where SQL Server 2025's extractor puts setup.exe is unverified. Accept it one
            # level down, but only if there is exactly one.
            $found = @(Get-ChildItem -LiteralPath $extract -Filter 'setup.exe' -File -Recurse -Depth 1)
            if ($found.Count -ne 1) { throw 'The bundled SQL Server media did not extract a setup program.' }
            $setup = $found[0].FullName
        }
        Start-EtpProcess -FilePath $setup -Description 'install SQL Server Express' -Arguments $arguments
    }
    # Only a folder this function created is ever removed.
    finally { if ($extract -and (Test-Path -LiteralPath $extract)) { Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue } }

    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) { throw 'SQL Server Express was installed but its service did not appear.' }
}

function Get-EtpInstalledOdbcDrivers {
    # Setup starts the 64-bit powershell.exe from {sys}, so this is the 64-bit driver list.
    $key = 'HKLM:\SOFTWARE\ODBC\ODBCINST.INI\ODBC Drivers'
    if (-not (Test-Path -LiteralPath $key)) { return @() }
    $item = Get-Item -LiteralPath $key
    return @($item.GetValueNames() | Where-Object { [string]$item.GetValue($_) -ceq 'Installed' })
}

function Get-EtpSqlClientInstallPlan {
    # The client packages, chosen by exact file name, in the order they have to install.
    # The bundled Sqlcmd (Microsoft Command Line Utilities 15) stops with error 26010 unless
    # ODBC Driver 17 is already there - its custom action MSSQLODBC17_64 - and the SQL Server
    # 2025 media carries only Driver 18, as msodbcsql.msi. Until 27 September 2026 setup took
    # the first msodbcsql*.msi it found, which was Driver 18, so Sqlcmd could never install.
    # Media that cannot finish the job is refused here, before anything has been changed.
    # LocalDB, and anything else in the media, is never installed.
    param([Parameter(Mandatory)][string]$PayloadDirectory,[string[]]$InstalledOdbcDrivers)
    $installed = @($InstalledOdbcDrivers | Where-Object { $_ })
    $packages = @(
        [pscustomobject]@{ File='msodbcsql17.msi'; Driver='ODBC Driver 17 for SQL Server'; Terms='IACCEPTMSODBCSQLLICENSETERMS=YES'; What='ODBC Driver 17 for SQL Server' },
        [pscustomobject]@{ File='msodbcsql.msi'; Driver='ODBC Driver 18 for SQL Server'; Terms='IACCEPTMSODBCSQLLICENSETERMS=YES'; What='ODBC Driver 18 for SQL Server' },
        [pscustomobject]@{ File='MsSqlCmdLnUtils.msi'; Driver=''; Terms='IACCEPTMSSQLCMDLNUTILSLICENSETERMS=YES'; What='Sqlcmd' })
    $present = @{}
    foreach ($package in $packages) { $present[$package.File] = Test-Path -LiteralPath (Join-Path $PayloadDirectory $package.File) -PathType Leaf }
    if (-not $present['MsSqlCmdLnUtils.msi']) {
        throw 'The SQL Server media included with this installer has no Sqlcmd package (MsSqlCmdLnUtils.msi), and this PC has no Sqlcmd. Nothing was installed.'
    }
    if ('ODBC Driver 17 for SQL Server' -notin $installed -and -not $present['msodbcsql17.msi']) {
        throw 'The bundled Sqlcmd needs Microsoft ODBC Driver 17 for SQL Server, which is not installed and is not included with this installer (msodbcsql17.msi). Nothing was installed.'
    }
    $plan = @()
    foreach ($package in $packages) {
        if (-not $present[$package.File]) { continue }
        if ($package.Driver -and $package.Driver -in $installed) { continue }
        $plan += [pscustomobject]@{ Path=(Join-Path $PayloadDirectory $package.File); Terms=$package.Terms; What=$package.What }
    }
    return $plan
}

function Test-EtpSqlCmdInstalled {
    # Only the ODBC 17 Sqlcmd counts. SQL Server 2025's setup brings an ODBC 18 Sqlcmd that
    # cannot connect to the instance it just installed (see Resolve-EtpSqlCmd); counting it
    # made setup skip the bundled Sqlcmd and then fail its first query. It counts wherever
    # it was installed (Workpc, 2 October 2026: E:\Program Files), if it is protected.
    try { $null = Resolve-EtpSqlCmd -Odbc17Only; return $true } catch { return $false }
}

function Test-EtpSqlEngineInstalled {
    param([Parameter(Mandatory)][string]$ServiceName)
    return [bool](Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)
}

function Get-EtpAdoptedSqlInstanceQuery {
    # Read-only. Everything after the first line needs a SQL administrator, which setup's
    # elevated account is on an instance this installer set up (BUILTIN\Administrators).
    return "SET NOCOUNT ON; " +
        "SELECT N'ETP_ADOPT_SYSADMIN:'+CONVERT(nvarchar(1),COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)); " +
        "IF COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)=1 BEGIN " +
        "SELECT N'ETP_ADOPT_WINDOWS_ONLY:'+CONVERT(nvarchar(10),SERVERPROPERTY('IsIntegratedSecurityOnly')); " +
        "SELECT N'ETP_ADOPT_PROTOCOL:'+CASE WHEN registry_key LIKE N'%\SuperSocketNetLib\Tcp' THEN N'TCP' ELSE N'NP' END+N'='+CONVERT(nvarchar(10),value_data) FROM sys.dm_server_registry " +
        "WHERE value_name=N'Enabled' AND (registry_key LIKE N'%\SuperSocketNetLib\Tcp' OR registry_key LIKE N'%\SuperSocketNetLib\Np'); " +
        "SELECT N'ETP_ADOPT_ADMIN:'+CONVERT(nvarchar(200),m.sid,1)+N'|'+m.name FROM sys.server_role_members rm " +
        "JOIN sys.server_principals r ON r.principal_id=rm.role_principal_id JOIN sys.server_principals m ON m.principal_id=rm.member_principal_id " +
        "WHERE r.name=N'sysadmin' AND m.type IN ('U','G') AND m.is_disabled=0; END;"
}

function Get-EtpAdoptedSqlInstanceProblems {
    # What stops setup from using an instance it did not install, in plain words; nothing
    # when it matches what this installer's own SQL Server setup produces: Windows accounts
    # only, TCP/IP and named pipes off, and no enabled Windows account or group among the SQL
    # administrators except Administrators and SQL Server's own service accounts (NT SERVICE\,
    # S-1-5-80-). SQL logins are not listed: with Windows-only authentication they cannot sign in.
    param([AllowEmptyCollection()][AllowNull()][string[]]$Lines)
    $values = @{}
    foreach ($line in @($Lines | ForEach-Object { "$_".Trim() })) {
        if ($line -cmatch '^ETP_ADOPT_([A-Z_]+):(.*)$') {
            if (-not $values.ContainsKey($Matches[1])) { $values[$Matches[1]] = @() }
            $values[$Matches[1]] += $Matches[2]
        }
    }
    foreach ($key in @('SYSADMIN', 'WINDOWS_ONLY', 'PROTOCOL', 'ADMIN')) { if (-not $values.ContainsKey($key)) { $values[$key] = @() } }
    $sysadmin = @($values['SYSADMIN'])
    if ($sysadmin.Count -ne 1 -or $sysadmin[0] -cne '1') { return @('the account running setup is not a SQL Server administrator on it, so its settings cannot be checked') }
    $problems = @()
    $windowsOnly = @($values['WINDOWS_ONLY'])
    if ($windowsOnly.Count -ne 1 -or $windowsOnly[0] -cne '1') { $problems += 'it accepts SQL Server logins as well as Windows accounts' }
    foreach ($protocol in @(@('TCP', 'TCP/IP', 'it can be reached over the network (TCP/IP is on)'), @('NP', 'named pipes', 'it accepts named-pipe connections'))) {
        $settings = @($values['PROTOCOL'] | Where-Object { $_.StartsWith($protocol[0] + '=', [StringComparison]::Ordinal) })
        if ($settings.Count -ne 1) { $problems += "setup could not read whether $($protocol[1]) is on" }
        elseif ($settings[0] -cne ($protocol[0] + '=0')) { $problems += $protocol[2] }
    }
    $others = @()
    foreach ($admin in @($values['ADMIN'])) {
        $parts = $admin.Split([char[]]'|', 2)
        $sid = $null
        try {
            if ($parts[0] -notmatch '^0x([0-9A-Fa-f]{2}){8,68}$') { throw 'unreadable' }
            $hex = $parts[0].Substring(2)
            $bytes = [byte[]]::new($hex.Length / 2)
            for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = [Convert]::ToByte($hex.Substring($i * 2, 2), 16) }
            $sid = [Security.Principal.SecurityIdentifier]::new($bytes, 0).Value
        }
        catch { $sid = $null }
        $name = if ($parts.Count -eq 2 -and $parts[1]) { $parts[1] -replace '[\x00-\x1F\x7F]', '?' } else { '(unnamed)' }
        if (-not $sid) { $others += $name; continue }
        if ($sid -ceq 'S-1-5-32-544' -or $sid.StartsWith('S-1-5-80-', [StringComparison]::Ordinal)) { continue }
        $others += $name
    }
    if ($others.Count -gt 0) { $problems += "other Windows accounts or groups are SQL Server administrators on it ($($others -join ', '))" }
    return $problems
}

function Assert-EtpAdoptedSqlInstance {
    # Setup installs SQL Server hardened: Windows accounts only, no TCP/IP or named pipes, and
    # only Administrators as SQL administrators. An instance of the same name that was already
    # on the PC - another program's, say - was not set up that way by anyone ETP knows of. On
    # an existing ETP machine an administrator chose the instance by hand; a new PC's setup
    # would otherwise adopt it silently and put the shop's data in it. So it is checked before
    # any ETP folder, account or configuration is created, and refused unless it matches. A
    # second run after this installer's own SQL Server setup succeeded passes, because that
    # instance matches.
    param([Parameter(Mandatory)][string]$ServiceName,[Parameter(Mandatory)][string]$ServerInstance)
    $refusal = 'SQL Server ({0}) was already installed on this PC, so setup did not install or configure it, and it will not put ETP in it as it is: {1}. Setup created no ETP folder, account or configuration. To use this instance deliberately, prepare it and the protected folders by hand as described in docs\OPERATIONS.md (deployment steps 1 and 3), then run setup again.'
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service -or $service.Status -ne 'Running') { throw ($refusal -f $ServiceName, 'it is not running, so its settings cannot be checked') }
    $lines = @()
    try {
        $sqlcmd = Resolve-EtpSqlCmd
        $server = Resolve-EtpSqlConnection -SqlCmd $sqlcmd -ServerInstance $ServerInstance
        $lines = @(Invoke-EtpSql -SqlCmd $sqlcmd -Server $server -Query (Get-EtpAdoptedSqlInstanceQuery))
    }
    catch { throw ($refusal -f $ServiceName, 'setup could not connect to it and read its settings') }
    $problems = @(Get-EtpAdoptedSqlInstanceProblems -Lines $lines)
    if ($problems.Count -gt 0) { throw ($refusal -f $ServiceName, ($problems -join '; ')) }
    Write-Host "SQL Server ($ServiceName) was already installed; its settings match what setup would have installed, so ETP will use it."
}

function Test-EtpNewMachine {
    # A PC counts as new only when ETP's protected Operations folder does not exist at all.
    # Strict folder setup (-GrantAutomationFolderAccess:$false) creates that folder and
    # deliberately writes no operations.json. Taking the missing file alone as "new PC" made
    # setup create EtpAutomation and grant it the ETP folders over that recorded decision.
    param([Parameter(Mandatory)][string]$OperationsDirectory)
    Assert-EtpNoLinks $OperationsDirectory
    if (Test-Path -LiteralPath (Join-Path $OperationsDirectory 'operations.json')) { return $false }
    if (Test-Path -LiteralPath $OperationsDirectory) {
        throw 'This PC already has ETP''s protected folders but no automation configuration (operations.json), which is how strict folder setup leaves them, and setup does not override that decision. Resolve automation folder access in the protected machine configuration (docs\OPERATIONS.md, Folder policy decision), then run setup again. Nothing was changed.'
    }
    return $true
}

function Install-EtpSqlPrerequisitesFromPayload {
    param([Parameter(Mandatory)][string]$PayloadDirectory,[Parameter(Mandatory)][string]$ServiceName)
    # The media is whatever the build packaged. Say exactly what is missing rather
    # than failing halfway through an unattended setup.
    Assert-EtpNoLinks $PayloadDirectory
    if (-not (Test-Path -LiteralPath $PayloadDirectory -PathType Container)) { throw 'The bundled SQL Server media is missing from this installer.' }
    # These binaries run with the full administrator token. Every other elevated payload
    # in this script passes the ownership and ACL gate; this one must too, or a folder a
    # non-administrator can write becomes an elevation path.
    Assert-EtpProtectedInstall $PayloadDirectory
    $needEngine = -not (Test-EtpSqlEngineInstalled $ServiceName)
    if ($needEngine -and -not (Get-ChildItem -LiteralPath $PayloadDirectory -Filter 'SQLEXPR*_x64_*.exe' -File)) { throw 'The bundled SQL Server Express package was not found in the installer media.' }
    # Work out everything first, so media that cannot finish the job stops setup before
    # SQL Server is half-installed.
    $clientPlan = @()
    if (-not (Test-EtpSqlCmdInstalled)) { $clientPlan = @(Get-EtpSqlClientInstallPlan -PayloadDirectory $PayloadDirectory -InstalledOdbcDrivers (Get-EtpInstalledOdbcDrivers)) }
    if ($needEngine) {
        Write-Host 'Installing SQL Server Express from the media included with this installer.'
        Install-EtpSqlEngineFromPayload -PayloadDirectory $PayloadDirectory -ServiceName $ServiceName
    }
    # SQL Server setup brings only an ODBC 18 Sqlcmd, which does not count, so the bundled
    # one is still installed; the check stays in case a future media brings the ODBC 17 one.
    if ($clientPlan.Count -gt 0 -and -not (Test-EtpSqlCmdInstalled)) {
        foreach ($package in $clientPlan) {
            # /norestart: a driver that asks for a restart must not restart the PC under setup.
            Start-EtpProcess -FilePath "$env:SystemRoot\System32\msiexec.exe" -Description ('install ' + $package.What) `
                -Arguments @('/i', $package.Path, '/qn', '/norestart', 'ADDLOCAL=ALL', $package.Terms)
        }
        # Say why: "not found" and "found in a folder a non-administrator can change" send the
        # operator to different places.
        if (-not (Test-EtpSqlCmdInstalled)) {
            $reason = 'the ODBC Driver 17 Sqlcmd was not found'
            try { $null = Resolve-EtpSqlCmd -Odbc17Only } catch { $reason = $_.Exception.Message }
            throw ('Sqlcmd was installed but cannot be used: ' + $reason)
        }
    }
}

function Invoke-EtpOperationFolderSetup {
    param([Parameter(Mandatory)][string]$ServiceName,[Parameter(Mandatory)][string]$ServerInstance,[Parameter(Mandatory)][string]$Database,
          [string]$AutomationPrincipal,[switch]$CreateAutomationAccount)
    $arguments = @{ SqlServiceIdentity = ('NT SERVICE\' + $ServiceName); ServerInstance = $ServerInstance; Database = $Database; GrantAutomationFolderAccess = $true }
    if (-not [string]::IsNullOrWhiteSpace($AutomationPrincipal)) { $arguments.AutomationPrincipal = $AutomationPrincipal }
    if ($CreateAutomationAccount) { $arguments.CreateAutomationAccount = $true }
    & (Join-Path $PSScriptRoot 'initialize-etp-operation-folders.ps1') @arguments
}

function Initialize-EtpFreshMachine {
    # A PC with no ETP machine configuration yet. The configuration names the SQL Server
    # service account, which Windows only knows once SQL Server exists, so the order is SQL
    # Server, then its client tools, then the configuration. Until 27 September 2026 setup
    # read the configuration first, so on a new PC it could never reach the step that
    # installs SQL Server: every run ended at the missing file with exit code 1603.
    param([Parameter(Mandatory)][string]$ApplicationRoot,[string]$SqlPayloadDirectory,[switch]$SkipSqlInstallation)
    Assert-EtpBootstrapAdministrator
    Assert-EtpBootstrapPayloads $ApplicationRoot
    if ($SkipSqlInstallation -or [string]::IsNullOrWhiteSpace($SqlPayloadDirectory)) {
        throw 'This PC has no ETP machine configuration yet. Run setup again with "Install Microsoft SQL Server 2025 Express" ticked (it skips anything already installed; an installer built without SQL media does not offer it), or prepare SQL Server and the protected folders as described in docs\OPERATIONS.md, then run setup again. Nothing was changed.'
    }
    $serviceName = Resolve-EtpBootstrapServiceName $EtpNewMachineServerInstance $EtpNewMachineDatabase
    # Decided before anything is installed: an instance that is already here was not set up
    # by this run, and is checked (after the client tools, which the check needs) rather than
    # taken as it is.
    $adoptsExistingEngine = Test-EtpSqlEngineInstalled $serviceName
    Install-EtpSqlPrerequisitesFromPayload -PayloadDirectory $SqlPayloadDirectory -ServiceName $serviceName
    if ($adoptsExistingEngine) { Assert-EtpAdoptedSqlInstance -ServiceName $serviceName -ServerInstance $EtpNewMachineServerInstance }
    Invoke-EtpOperationFolderSetup -ServiceName $serviceName -ServerInstance $EtpNewMachineServerInstance -Database $EtpNewMachineDatabase -CreateAutomationAccount
}

function Get-EtpBootstrapDatabaseAction {
    # EXISTS is upgraded, whatever the option says. MISSING is created, unless setup was asked
    # to leave it for a restore. Anything else is not an answer this script understands.
    param([string]$DatabaseState,[switch]$DeferDatabaseCreation)
    switch -CaseSensitive ($DatabaseState) {
        'EXISTS' { return 'Upgrade' }
        'MISSING' { if ($DeferDatabaseCreation) { return 'AwaitRestore' }; return 'Create' }
    }
    throw 'SQL Server returned an unexpected database-existence result.'
}

function New-EtpSetupOwnerLoginSql {
    # Setup creates a new database as the account running it, which migration 0011 makes the
    # first Owner and which owns the database (dbo). But on an instance this installer set up,
    # that account is a SQL administrator only through BUILTIN\Administrators, which Windows
    # removes from an unelevated token: opened normally from the Start menu, ETP could not sign
    # in to SQL Server at all, so the Owner never reached Settings > Users. When the account has
    # no login of its own it is given one the documented way - dbo.configure_application_role
    # as OWNER, the same call Settings > Users and the restore helper's Owner recovery make.
    # SUSER_SNAME() is taken inside SQL Server, so no text from outside reaches this batch. An
    # account that already has its own login (an administrator's own sysadmin login, for one)
    # is left as it is. The procedure gives every other Owner ALTER ANY LOGIN WITH GRANT
    # OPTION (migration 0043), but not this one: SQL Server never lets a login grant a
    # permission to itself. Setup's one-off SYSTEM task gives it afterwards
    # (Invoke-EtpOwnerGrantOptionAsSystem), and New-EtpOwnerLoginAdministrationSql checks.
    return @'
SET NOCOUNT ON; SET XACT_ABORT ON;
DECLARE @identity nvarchar(200)=SUSER_SNAME();
IF SUSER_ID(@identity) IS NULL
BEGIN
  IF NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=@identity AND role_code='OWNER' AND is_active=1)
    THROW 51920,N'The account running setup is not the new database''s Owner.',1;
  BEGIN TRANSACTION;
  EXEC dbo.configure_application_role @identity=@identity,@role='OWNER',@active=1;
  COMMIT TRANSACTION;
END;
'@
}

function New-EtpOwnerLoginAdministrationSql {
    # Read-only. 1.9.3, migration 0043: an Owner changes users in Settings > Users without
    # "Run as administrator" only while its login holds ALTER ANY LOGIN WITH GRANT OPTION.
    # The account running setup, usually the Owner, cannot give that to itself (SQL Server's
    # error 4627), so setup has SYSTEM give it first (Invoke-EtpOwnerGrantOptionAsSystem);
    # this check, made afterwards, says whether that worked, instead of leaving the Owner to
    # discover it at the first save. One word: NOT_AN_OWNER (no login of its own, or not an
    # active Owner), GRANT_OPTION or MISSING.
    return @'
SET NOCOUNT ON;
DECLARE @identity nvarchar(200)=SUSER_SNAME();
SELECT CASE
  WHEN SUSER_ID(@identity) IS NULL
    OR NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=@identity AND role_code='OWNER' AND is_active=1) THEN 'NOT_AN_OWNER'
  WHEN EXISTS(SELECT 1 FROM sys.server_permissions WHERE class=100 AND grantee_principal_id=SUSER_ID(@identity)
    AND permission_name=N'ALTER ANY LOGIN' AND state='W') THEN 'GRANT_OPTION'
  ELSE 'MISSING' END;
'@
}

function Start-EtpProcess {
    param([Parameter(Mandatory)][string]$FilePath,[string[]]$Arguments=@(),[Parameter(Mandatory)][string]$Description)
    # Start-Process joins ArgumentList with spaces and never quotes, so any path
    # containing a space splits into two arguments and the installer sees nonsense.
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '\s' -and $_ -notmatch '^".*"$') { '"' + $_ + '"' } else { $_ } })
    $process = Start-Process -FilePath $FilePath -ArgumentList $quoted -Wait -PassThru -NoNewWindow
    # 3010 is "restart required", which is a success for an unattended prerequisite.
    if ($process.ExitCode -notin @(0, 3010)) { throw "Could not $Description. The installer reported exit code $($process.ExitCode)." }
}

function Assert-EtpBootstrapPayloads {
    param([Parameter(Mandatory)][string]$ApplicationDirectory)
    # Check before any elevated executable, migration or companion script runs.
    # A protected directory can still contain an explicitly writable child file.
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue([IO.Path]::GetFullPath($ApplicationDirectory))
    while ($pending.Count -gt 0) {
        $item = $pending.Dequeue()
        Assert-EtpProtectedInstall $item
        if (Test-Path -LiteralPath $item -PathType Container) {
            foreach ($child in Get-ChildItem -LiteralPath $item -Force) { $pending.Enqueue($child.FullName) }
        }
    }
}

function Complete-EtpAutomationGrants {
    # 1.9.3. Setup used to install the operations broker alone and leave the automation
    # account's rights to a separate install-etp-sql-operations.ps1 run (docs\OPERATIONS.md,
    # step 7), which nothing prompted. On Workpc, 2 October 2026, the recovery drill's first
    # run failed with only "The database operation failed" because of it. Now every setup run
    # ends here: an automation account that is already an active Store Manager but lacks the
    # module's rights gets them, by the same script and checks as the manual step; one that is
    # not a Store Manager yet gets a NEXT STEP line saying exactly what to do. Returns the log
    # lines; a failure here is reported, never fatal, because the database is complete.
    param([Parameter(Mandatory)][scriptblock]$GetState,[Parameter(Mandatory)][scriptblock]$InstallModule,
          [string]$ServerInstance,[string]$Database,[string]$AutomationPrincipal,[string]$ScriptsDirectory)
    $command = Get-EtpAutomationGrantCommand -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $AutomationPrincipal -ScriptsDirectory $ScriptsDirectory
    try { $state = & $GetState }
    catch { return @("WARNING: setup could not check whether $AutomationPrincipal has the operations module's rights ($(Format-EtpFailureForLog $_.Exception)). If the daily backup or the recovery drill fails, run this in an administrator PowerShell window: $command") }
    if ($state.State -ceq 'READY') { return @("$AutomationPrincipal is an active Store Manager with the operations module's rights; the daily backup and the recovery drill can run.") }
    if ($state.State -ceq 'GRANTS_MISSING') {
        $lines = [Collections.Generic.List[string]]::new()
        $lines.Add("$AutomationPrincipal is an active Store Manager but lacks $($state.Missing -join ', '). Setup is installing the restricted SQL operations module for it now (docs\OPERATIONS.md, step 7).")
        try { foreach ($line in @(& $InstallModule)) { $lines.Add("$line") } }
        catch {
            $lines.Add("WARNING: the operations module could not be installed for ${AutomationPrincipal}: $(Format-EtpFailureForLog $_.Exception) The daily backup and the recovery drill cannot run under that account until it is. Then run this in an administrator PowerShell window: $command")
            return $lines.ToArray()
        }
        try { $after = & $GetState } catch { $after = $null }
        if ($null -ne $after -and $after.State -ceq 'READY') { $lines.Add("$AutomationPrincipal now has the operations module's rights.") }
        else {
            $still = if ($null -ne $after -and @($after.Missing).Count -gt 0) { " (missing: $($after.Missing -join ', '))" } else { '' }
            $lines.Add("WARNING: after the operations module was installed, $AutomationPrincipal still does not have all of its rights$still. Run this in an administrator PowerShell window: $command")
        }
        return $lines.ToArray()
    }
    $guidance = Get-EtpAutomationGrantGuidance -GrantState $state -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $AutomationPrincipal -ScriptsDirectory $ScriptsDirectory
    if ($guidance) { return @("NEXT STEP: $guidance") }
    return @("WARNING: setup could not tell whether $AutomationPrincipal has the operations module's rights. If the daily backup or the recovery drill fails, run this in an administrator PowerShell window: $command")
}

function Invoke-EtpPreMigrationBrokerRefresh {
    # Before the pre-migration backup: make sure the master operations broker is there and, if
    # it is unsigned and from an earlier build, current (install-etp-sql-operations.ps1
    # -BrokerOnly; a signed one is never touched here). Returns the log lines and never throws.
    # The broker is only the means; the backup is what protects the data. A broker that could
    # not be brought up to date is not a reason to migrate without a backup, and not a reason
    # to stop either while the one already installed can still take it: the backup goes ahead
    # through that one, records no row counts (rowCountsNotRecorded OPERATIONS_MODULE_OUTDATED)
    # and is verified as always. Without any broker the backup itself fails, and setup stops
    # there with nothing migrated. On the VM rehearsal of 3 October 2026 this step failed
    # (a defect in the 1.9.3 broker's SQL) and stopped setup although the 1.9.2 broker was
    # still in place and could have taken the backup.
    param([Parameter(Mandatory)][scriptblock]$Refresh)
    $lines = [Collections.Generic.List[string]]::new()
    try { foreach ($line in @(& $Refresh)) { $lines.Add("$line") } }
    catch {
        $lines.Add("WARNING: the operations broker could not be checked or brought up to date before the pre-migration backup: $(Format-EtpFailureForLog $_.Exception) The backup goes ahead through the broker already installed; if it is from an earlier build, its receipt records no row counts.")
    }
    return $lines.ToArray()
}

function Import-EtpSecurityModule {
    # Workpc, 9 October 2026 (1.9.6): every setup run failed within a second of copying its
    # files, with "The 'Get-Acl' command was found in the module 'Microsoft.PowerShell.Security',
    # but the module could not be loaded", and the same command run by hand a minute later
    # succeeded every time (6 of 6). Get-Acl is the first thing the preflight needs
    # (Get-EtpOperationsConfiguration -> Assert-EtpProtectedInstall), and Windows PowerShell
    # loads the module on first use with no retry. So the module is loaded here on purpose,
    # before anything is checked or changed, and given up to TimeoutSeconds (DelaySeconds
    # apart) to become loadable. Every failed attempt is logged; the final failure names the
    # last error. Import, Log and Wait are replaceable so the retry can be tested without
    # the real module or real waiting.
    param([scriptblock]$Import = { Import-Module Microsoft.PowerShell.Security -ErrorAction Stop; $null = Get-Command Get-Acl -ErrorAction Stop },
          [scriptblock]$Log = { param([string]$Message) },
          [scriptblock]$Wait = { param([int]$Seconds) Start-Sleep -Seconds $Seconds },
          [ValidateRange(0, 3600)][int]$TimeoutSeconds = 60,
          [ValidateRange(1, 3600)][int]$DelaySeconds = 5)
    $attempts = [int][math]::Floor($TimeoutSeconds / $DelaySeconds) + 1
    $reason = ''
    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        $loaded = $false
        try { & $Import; $loaded = $true }
        catch {
            $reason = [string]$_.Exception.Message
            & $Log "Attempt $attempt of $attempts to load the PowerShell security module (Microsoft.PowerShell.Security, needed for Get-Acl) failed: $reason"
        }
        if ($loaded) {
            if ($attempt -gt 1) { & $Log "The PowerShell security module loaded on attempt $attempt of $attempts." }
            return
        }
        if ($attempt -lt $attempts) { & $Wait $DelaySeconds }
    }
    throw "The PowerShell security module (Microsoft.PowerShell.Security, needed for Get-Acl) could not be loaded in $attempts attempts over $(($attempts - 1) * $DelaySeconds) seconds. Last error: $reason Nothing was checked or changed. Wait a minute and run setup again; if it keeps failing, check whether antivirus is scanning or blocking $(Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security')."
}

function Test-EtpSetupLogDirectoryTrusted {
    # Whether the bootstrap log may be written into an existing SetupLogs folder before this
    # run has protected the folders itself. Until 1.9.7 nothing was written there before
    # initialize-etp-operation-folders.ps1 had run, so a failure anywhere in the preflight -
    # the Get-Acl failure of 9 October 2026 among them - left no bootstrap log at all, only
    # text on a hidden console. The folder is trusted when it exists and passes the same
    # ownership and permission check as everything else setup runs elevated, read through
    # .NET rather than Get-Acl so that the check itself does not depend on the module whose
    # loading is what Import-EtpSecurityModule is there to retry. Anything unreadable or
    # unprotected means no log, as before; setup's own log in %TEMP% still has the exit code.
    param([Parameter(Mandatory)][string]$LogDirectory)
    try {
        if (-not (Test-Path -LiteralPath $LogDirectory -PathType Container)) { return $false }
        Assert-EtpLocalVolume $LogDirectory
        Assert-EtpNoLinks $LogDirectory
        $findings = @(Get-EtpProtectedInstallFindings -Path $LogDirectory -ReadSecurity { param($Item) (Get-Item -LiteralPath $Item -Force).GetAccessControl() })
        return ($findings.Count -eq 0)
    }
    catch { return $false }
}

function New-EtpAutoCloseOffSql {
    # 1.9.9, IE-RT-11. SQL Server Express creates every database with AUTO_CLOSE ON, and no ETP
    # script cleared it: on Workpc EtpReporting was closed and started again 1,112 times in 30
    # days, each start-up a line in the Application log and a slower first query after every
    # idle spell. Turns it off only when it is on, says which, and changes nothing else.
    # NO_WAIT: setup never queues behind someone's open connection for this; it reports instead.
    param([Parameter(Mandatory)][string]$Database)
    $literal = $Database.Replace("'", "''")
    $identifier = '[' + $Database.Replace(']', ']]') + ']'
    return @"
SET NOCOUNT ON;
IF DB_ID(N'$literal') IS NULL SELECT 'MISSING';
ELSE IF EXISTS(SELECT 1 FROM sys.databases WHERE name=N'$literal' AND is_auto_close_on=1)
BEGIN
    ALTER DATABASE $identifier SET AUTO_CLOSE OFF WITH NO_WAIT;
    SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.databases WHERE name=N'$literal' AND is_auto_close_on=0) THEN 'TURNED_OFF' ELSE 'STILL_ON' END;
END
ELSE SELECT 'ALREADY_OFF';
"@
}

function Set-EtpDatabaseAutoCloseOff {
    # Runs New-EtpAutoCloseOffSql through Invoke (a scriptblock taking the query and returning
    # the one result word) and returns the setup log line. Never throws: the database is
    # complete and works with AUTO_CLOSE on, only slower after an idle spell, so a failure here
    # is a WARNING with the command to run by hand, not a reason to fail setup.
    param([Parameter(Mandatory)][string]$Database,[Parameter(Mandatory)][scriptblock]$Invoke,[string]$ServerInstance='.\SQLEXPRESS')
    $manual = "sqlcmd -S `"$ServerInstance`" -E -Q `"ALTER DATABASE [$($Database.Replace(']', ']]'))] SET AUTO_CLOSE OFF`""
    try { $result = "$(& $Invoke (New-EtpAutoCloseOffSql -Database $Database))".Trim() }
    catch { return "WARNING: setup could not check or turn off AUTO_CLOSE for $Database ($(Format-EtpFailureForLog $_.Exception)). ETP works with it on; the first query after an idle spell is slower and SQL Server logs a start-up each time. To turn it off by hand, run this in an administrator PowerShell window: $manual" }
    switch -CaseSensitive ($result) {
        'TURNED_OFF' { return "AUTO_CLOSE was ON for $Database (SQL Server Express sets it on a new database); setup turned it OFF, so the database stays open between uses and SQL Server no longer logs a start-up after every idle spell." }
        'ALREADY_OFF' { return "AUTO_CLOSE is already OFF for $Database; nothing was changed." }
        default { return "WARNING: setup could not turn off AUTO_CLOSE for $Database (SQL Server answered '$result'). ETP works with it on; the first query after an idle spell is slower. To turn it off by hand, run this in an administrator PowerShell window: $manual" }
    }
}

function Get-EtpAutomationTaskPlan {
    # 1.9.9, IE-RT-08. The five-minute automation task runs ETP as the automation account, which
    # can open the database only once an Owner has added it as an active Store Manager in
    # Settings > Users. The 1.9.2 setup on Workpc (2 October 2026) registered it enabled before
    # that, and SQL Server logged "Login failed for user ...\EtpAutomation" every five minutes
    # until the account was added. The task is now registered disabled while the account
    # cannot open the database (NOT_STORE_MANAGER), and enabled as before when it can (READY,
    # or GRANTS_MISSING: it can sign in; the missing rights only affect backup and drill).
    # A state that could not be read (UNKNOWN, or none) keeps the earlier behaviour - enabled -
    # so a failed check can never switch automation off unnoticed. Returns Enabled and the log line.
    param($GrantState,[string]$AutomationPrincipal,[string]$Database,[string]$TaskName='ETP Reporting Automated Operations')
    $state = if ($null -ne $GrantState -and -not [string]::IsNullOrWhiteSpace([string]$GrantState.State)) { [string]$GrantState.State } else { 'UNKNOWN' }
    switch -CaseSensitive ($state) {
        'NOT_STORE_MANAGER' {
            return [pscustomobject]@{ Enabled = $false; Line = "The five-minute ETP automation task is installed but DISABLED, because $AutomationPrincipal cannot open $Database yet (it is not an active Store Manager), and every run would only fail to sign in. Next: in ETP, signed in as an Owner, add $AutomationPrincipal as an active Store Manager in Settings > Users, then run ETP setup again, which enables the task; or enable it yourself in an administrator PowerShell window: Enable-ScheduledTask -TaskName '$TaskName'" }
        }
        { $_ -ceq 'READY' -or $_ -ceq 'GRANTS_MISSING' } {
            return [pscustomobject]@{ Enabled = $true; Line = "The five-minute ETP automation task is installed and enabled; $AutomationPrincipal can open $Database." }
        }
        default {
            return [pscustomobject]@{ Enabled = $true; Line = "The five-minute ETP automation task is installed and enabled. Setup could not tell whether $AutomationPrincipal can open $Database; if SQL Server logs 'Login failed' for it every five minutes, add it as an active Store Manager in Settings > Users." }
        }
    }
}

function Invoke-EtpHeadlessApplication {
    # Runs the application in one of its headless modes, hidden, and returns its exit code and
    # what it wrote to stderr. Until 1.9.9 setup started it with Start-Process -WindowStyle
    # Hidden, which keeps no stream, so a refused upgrade left only "failed with exit code 1"
    # in the setup log and the reason in a diagnostics file of the installing account
    # (IE-CODE-05). stdout is not redirected (it goes where the bootstrap's own output goes);
    # stderr is read to the end while the process runs, so a long report cannot block it.
    param([Parameter(Mandatory)][string]$FilePath,[Parameter(Mandatory)][string]$Argument)
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $FilePath
    $start.Arguments = $Argument
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $errorText = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; ErrorText = [string]$errorText.Result }
    }
    finally { $process.Dispose() }
}

function ConvertTo-EtpApplicationReportLines {
    # Pure: the application's stderr as setup log lines, verbatim apart from blank lines being
    # dropped and an unbounded report being cut (MaxLines lines of at most MaxLength characters).
    # The headless modes write only their privacy-safe reason and SQL error numbers there.
    param([AllowEmptyString()][AllowNull()][string]$Text,[ValidateRange(1, 1000)][int]$MaxLines = 40,[ValidateRange(20, 100000)][int]$MaxLength = 2000)
    if ([string]::IsNullOrWhiteSpace($Text)) { return @() }
    $lines = @($Text -split "\r?\n" | ForEach-Object { $_.TrimEnd() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $result = [Collections.Generic.List[string]]::new()
    foreach ($line in @($lines | Select-Object -First $MaxLines)) {
        if ($line.Length -gt $MaxLength) { $line = $line.Substring(0, $MaxLength) + ' (cut)' }
        $result.Add("The application reported: $line")
    }
    if ($lines.Count -gt $MaxLines) { $result.Add("The application reported $($lines.Count - $MaxLines) more line(s), not logged.") }
    return $result.ToArray()
}

function Assert-EtpHeadlessStepSucceeded {
    # Writes whatever the application reported to the setup log (on success too: a warning there
    # is worth keeping) and throws, naming the reason, when it exited non-zero. The trap then
    # logs the FAILED line with that reason and setup reports 1603 as before.
    param([Parameter(Mandatory)]$Result,[Parameter(Mandatory)][string]$Step,[Parameter(Mandatory)][scriptblock]$Log)
    $lines = @(ConvertTo-EtpApplicationReportLines -Text $Result.ErrorText)
    foreach ($line in $lines) { & $Log $line }
    if ([int]$Result.ExitCode -eq 0) { return }
    if ($lines.Count -gt 0) {
        throw "$Step failed with exit code $($Result.ExitCode). $($lines[0]) (Everything it reported is in the lines above.)"
    }
    throw "$Step failed with exit code $($Result.ExitCode), and the application reported no reason. Its diagnostics entry is in %LOCALAPPDATA%\EtpReporting\Logs of the account that ran setup."
}

# Dot-sourcing exposes only the pure preflight functions for behavioral tests.
if ($MyInvocation.InvocationName -eq '.') { return }

$applicationRoot = [System.IO.Path]::GetFullPath($ApplicationDirectory)
$application = Join-Path $applicationRoot 'Etp.Reporting.Desktop.exe'
$scripts = Join-Path $applicationRoot 'scripts'
$migrationDirectory = Join-Path $applicationRoot 'database\migrations'
$backupScript = Join-Path $scripts 'backup-etp-database.ps1'
$backupDirectory = Join-Path $env:ProgramData 'EtpReporting\Backups'
$logDirectory = Join-Path $env:ProgramData 'EtpReporting\SetupLogs'
$databaseExistedBeforeMigration = $false
$migrationPhaseStarted = $false
$migrationPhaseCompleted = $false
$preMigrationBackupPath = $null

$logPath = Join-Path $logDirectory "bootstrap-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff').log"
# The log is written only into a SetupLogs folder that is known to be protected: one that
# passes the check below already, or the one initialize-etp-operation-folders.ps1 protects
# later in this run. Nothing is created here; a new PC has no such folder until then.
$setupLogDirectoryTrusted = Test-EtpSetupLogDirectoryTrusted -LogDirectory $logDirectory

function Write-SetupLog([string]$message) {
    $line = "$(Get-Date -Format o) $message"
    if ($setupLogDirectoryTrusted -and (Test-Path -LiteralPath $logDirectory -PathType Container)) { Add-Content -LiteralPath $logPath -Value $line -Encoding utf8 }
    Write-Host $message
}

trap {
    Write-SetupLog "FAILED: $($_.Exception.GetType().Name): $($_.Exception.Message)"
    # The masked "The database operation failed" alone said nothing on the VM rehearsal of
    # 3 October 2026. What SQL Server (or Sqlcmd) reported goes into this administrator-only
    # log on a line of its own, so the FAILED line keeps the form tools look for.
    $sqlDetail = Get-EtpExceptionSqlDetail $_.Exception
    if ($sqlDetail) { Write-SetupLog "SQL Server reported: $sqlDetail" }
    if ($migrationPhaseStarted -and -not $migrationPhaseCompleted) {
        if ($databaseExistedBeforeMigration -and -not [string]::IsNullOrWhiteSpace($preMigrationBackupPath)) {
            Write-SetupLog "No automatic restore or reverse migration was attempted. The verified pre-migration backup remains at $preMigrationBackupPath. Diagnose the failure before using the documented manual restore procedure."
        }
        elseif ($databaseExistedBeforeMigration) {
            Write-SetupLog 'No automatic restore, reverse migration, database deletion, or user-data deletion was attempted. The existing database is retained for diagnosis.'
        }
        else {
            Write-SetupLog 'No automatic reverse migration, database deletion, or user-data deletion was attempted. The failed clean-install database state is retained for diagnosis.'
        }
    }
    exit 1
}

# Before the preflight: Get-Acl is its first need, and on Workpc (9 October 2026) its module
# was not loadable in the second after setup copied its files. Each failed attempt goes into
# the log above; if it never loads, the trap logs the FAILED line and setup reports 1603.
Import-EtpSecurityModule -Log { param([string]$Message) Write-SetupLog $Message }

function Resolve-SqlCmdPath {
    try { return Resolve-EtpSqlCmd } catch { return $null }
}

function Invoke-SqlScalar {
    param(
        [Parameter(Mandatory)][string]$Query,
        [string]$TargetDatabase
    )

    $arguments = @('-S', $ServerInstance, '-E', '-b', '-h', '-1', '-W')
    if (-not [string]::IsNullOrWhiteSpace($TargetDatabase)) { $arguments += @('-d', $TargetDatabase) }
    $arguments += @('-Q', $Query)
    $output = @(& $sqlcmdPath -x @arguments)
    if ($LASTEXITCODE -ne 0) { throw "SQL Server preflight query failed with exit code $LASTEXITCODE." }
    $lines = @($output | ForEach-Object { "$_".Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -ne 1) { throw "SQL Server preflight query returned an unexpected result." }
    return $lines[0]
}

function Invoke-SqlHealthCommand {
    param([Parameter(Mandatory)][string]$Query)

    & $sqlcmdPath -x -S $ServerInstance -E -b -d $Database -Q $Query | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Post-migration database health verification failed with exit code $LASTEXITCODE." }
}

function Assert-VerifiedBackupReceipt {
    param([Parameter(Mandatory)][string]$ReceiptPath)

    $receipt=Read-EtpVerifiedReceipt -ReceiptPath $ReceiptPath -BackupDirectory $backupDirectory -Database $Database -SkipCertificateCheck
    if ($receipt.serverInstance -cne $ServerInstance) { throw 'The verified backup does not match the target server.' }
    $script:preMigrationBackupPath=$receipt.backupPath
}

# No protected Operations folder means a PC ETP has never been set up on. Prepare SQL Server
# and the configuration first (see Initialize-EtpFreshMachine); everything after this
# point is the same for a new PC and an existing one. Strict folders without a
# configuration are refused by Test-EtpNewMachine, unchanged.
$freshMachine = $false
$configurationPath = Join-Path $env:ProgramData 'EtpReporting\Operations\operations.json'
Assert-EtpNoLinks $configurationPath
if (Test-EtpNewMachine -OperationsDirectory (Split-Path -Parent $configurationPath)) {
    $freshMachine = $true
    Initialize-EtpFreshMachine -ApplicationRoot $applicationRoot -SqlPayloadDirectory $SqlPayloadDirectory -SkipSqlInstallation:$SkipSqlInstallation
}

$operationConfiguration = Get-EtpOperationsConfiguration
Assert-EtpBootstrapPayloads $applicationRoot
if ($operationConfiguration.allowAutomationFolderAccess -ne $true) { throw 'Resolve automation folder access in the protected machine configuration before bootstrap.' }
$ServerInstance = [string]$operationConfiguration.serverInstance
$Database = [string]$operationConfiguration.database
$serviceName = Resolve-EtpBootstrapServiceName $ServerInstance $Database

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'ETP prerequisite configuration requires administrator rights.'
}
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) { throw "Application executable is missing." }
if (-not (Test-Path -LiteralPath $backupScript -PathType Leaf)) { throw "The database backup script is missing." }
if (-not (Test-Path -LiteralPath $migrationDirectory -PathType Container)) { throw "The bundled migration directory is missing." }
$migrationFiles = @(Get-ChildItem -LiteralPath $migrationDirectory -Filter '*.sql' -File)
if ($migrationFiles.Count -eq 0) { throw "No bundled database migrations were found." }

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
# With the media ticked, also when SQL Server is there but Sqlcmd is not: that used to stop
# below with "Sqlcmd is not installed" although the installer carried it.
if (-not $SkipSqlInstallation -and $SqlPayloadDirectory -and (-not $service -or -not (Test-EtpSqlCmdInstalled))) {
    Install-EtpSqlPrerequisitesFromPayload -PayloadDirectory $SqlPayloadDirectory -ServiceName $serviceName
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
}
if (-not $service) { throw 'The configured database-engine service is not installed. Install SQL Server manually, or run setup with the bundled database media, then retry.' }
if ($service.Status -ne 'Running') { Start-Service -Name $serviceName -ErrorAction SilentlyContinue; $service.Refresh() }
if ($service.Status -ne 'Running') { throw 'Start the configured SQL Server service manually before bootstrap so its edition can be verified before changes are made.' }

$sqlcmdPath = Resolve-SqlCmdPath
if (-not $sqlcmdPath) { throw 'Microsoft Sqlcmd is not installed. Install it from approved offline media or an approved package source, then retry.' }

$serverProperties = Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT CONVERT(varchar(10),SERVERPROPERTY('ProductMajorVersion')) + '|' + CONVERT(varchar(10),SERVERPROPERTY('EngineEdition')) + '|' + CONVERT(varchar(128),SERVERPROPERTY('Edition'));"
$serverParts = $serverProperties.Split('|')
$serverMajorVersion = 0
$serverEngineEdition = 0
if ($serverParts.Count -ne 3 -or -not [int]::TryParse($serverParts[0], [ref]$serverMajorVersion) -or -not [int]::TryParse($serverParts[1], [ref]$serverEngineEdition)) { throw "SQL Server returned an unreadable version result." }
Assert-EtpBootstrapSqlEdition $serverMajorVersion $serverEngineEdition $serverParts[2]
Write-SetupLog "SQL Server compatibility preflight passed (major version $serverMajorVersion, engine edition $($serverParts[1]))."
# The same test backup-etp-database.ps1 makes. Express and Web cannot encrypt a backup and
# need no recovery keys; every other edition encrypts, and cannot back up without them.
$editionEncryptsBackups = -not ($serverParts[2] -like '*Express*' -or $serverParts[2] -like '*Web*')

Set-Service -Name $serviceName -StartupType Automatic
& (Join-Path $PSScriptRoot 'initialize-etp-operation-folders.ps1') -SqlServiceIdentity ('NT SERVICE\'+$serviceName) -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $operationConfiguration.automationPrincipal -GrantAutomationFolderAccess
$setupLogDirectoryTrusted = $true
if ($freshMachine) {
    Write-SetupLog 'This PC had no ETP machine configuration. Setup installed whatever was missing of SQL Server Express, ODBC Driver 17 and Sqlcmd from the included media (an SQLEXPRESS instance that was already here was checked, not reconfigured), and created the protected folders, the EtpAutomation account and the configuration for .\SQLEXPRESS / EtpReporting.'
}

$databaseState = Invoke-SqlScalar -Query "SET NOCOUNT ON; IF DB_ID(N'$Database') IS NULL SELECT 'MISSING' ELSE SELECT 'EXISTS';"
$databaseAction = Get-EtpBootstrapDatabaseAction -DatabaseState $databaseState -DeferDatabaseCreation:$DeferDatabaseCreation
$databaseExistedBeforeMigration = $databaseAction -ceq 'Upgrade'

if ($databaseAction -ceq 'AwaitRestore') {
    # The restore that follows and the next setup run both need the master operations
    # broker: the restore helper ensures it, and setup's pre-migration backup of the
    # restored data goes through it. It can be created before the database exists, and
    # -BrokerOnly never touches one that is already there.
    foreach ($line in @(& (Join-Path $scripts 'install-etp-sql-operations.ps1') -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $operationConfiguration.automationPrincipal -SqlCmdPath $sqlcmdPath -BrokerOnly)) { Write-SetupLog "$line" }
    Write-SetupLog 'Setup stopped before creating a database, as asked, so that existing ETP data can be restored first. SQL Server, the protected folders, the EtpAutomation account, the configuration and the operations broker are ready; no database was created and no scheduled task was installed. Next: run scripts\restore-etp-database.ps1 -BackupPath <your .bak> in an administrator PowerShell window, then run setup again.'
    exit $DatabaseRestorePendingExitCode
}

if ($databaseExistedBeforeMigration) {
    $databaseProperties = Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT state_desc + '|' + user_access_desc + '|' + CONVERT(varchar(5),is_read_only) + '|' + CONVERT(varchar(5),compatibility_level) FROM sys.databases WHERE name=N'$Database';"
    $databaseParts = $databaseProperties.Split('|')
    $compatibilityLevel = 0
    if ($databaseParts.Count -ne 4 -or -not [int]::TryParse($databaseParts[3], [ref]$compatibilityLevel)) { throw "SQL Server returned unreadable database compatibility information." }
    if ($databaseParts[0] -cne 'ONLINE' -or $databaseParts[1] -cne 'MULTI_USER' -or $databaseParts[2] -cne '0') { throw "The existing database must be ONLINE, MULTI_USER, and read-write before migration." }
    if ($compatibilityLevel -lt 150) { throw "The existing database compatibility level is $compatibilityLevel; level 150 or newer is required before migration." }

    $appliedMigrationCount = [long](Invoke-SqlScalar -TargetDatabase $Database -Query "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.schema_migrations',N'U') IS NULL SELECT CONVERT(bigint,0) ELSE SELECT COUNT_BIG(1) FROM dbo.schema_migrations;")
    if ($appliedMigrationCount -lt $migrationFiles.Count) {
        # On an edition that encrypts, the pre-migration backup cannot be taken at all until
        # the recovery keys have been exported. Refuse here, naming the step, instead of
        # letting the backup script fail part-way into an upgrade. A clean install takes no
        # pre-migration backup, so this belongs in this branch and not in the preflight.
        if ($editionEncryptsBackups -and -not (Test-Path -LiteralPath (Join-Path $backupDirectory 'certificate-custody.json') -PathType Leaf)) {
            throw 'This SQL Server edition encrypts backups, so the pre-migration backup needs exported recovery keys. Open the application as Owner, go to Settings > Database > Encrypted backup recovery keys, select "Create and export recovery keys", and run setup again. No migration has run and the database has not been changed; setup has already set the SQL service to start automatically and applied the operations folder permissions, and running it again is safe.'
        }
        $databaseSizeMb = [double](Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT CONVERT(decimal(18,2),SUM(size)*8.0/1024.0) FROM sys.master_files WHERE database_id=DB_ID(N'$Database');")
        $requiredFreeSpaceGb = [math]::Ceiling(([math]::Max($MinimumBackupFreeSpaceGb, ($databaseSizeMb / 1024.0) * 1.25)) * 100) / 100
        $receiptPath = Join-Path $logDirectory "pre-migration-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')-$([Guid]::NewGuid().ToString('N')).json"
        Write-SetupLog 'Existing database has pending bundled migrations. Creating and verifying a pre-migration backup before any migration runs.'
        # The backup goes through the master operations broker. A database that reached this
        # instance by a restore rather than through setup - by hand, or after the restore
        # helper's own broker step failed - can be here without one, and the backup then
        # stopped with only "The database operation failed". -BrokerOnly creates it where it is
        # missing and replaces an unsigned one from an earlier build; a signed one is left as it
        # was (Get-EtpBrokerOnlyAction). A failure here is logged and the backup still runs
        # through whatever broker is installed (Invoke-EtpPreMigrationBrokerRefresh).
        Write-SetupLog 'Checking the master operations broker that takes the pre-migration backup.'
        foreach ($line in @(Invoke-EtpPreMigrationBrokerRefresh -Refresh { & (Join-Path $scripts 'install-etp-sql-operations.ps1') -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $operationConfiguration.automationPrincipal -SqlCmdPath $sqlcmdPath -BrokerOnly })) { Write-SetupLog "$line" }
        # -Purpose PreMigration marks the receipt so the next daily backup's rotation keeps
        # this file. Without it the upgrade's own safety copy was deleted the same day.
        Write-SetupLog 'Taking the pre-migration backup.'
        & $backupScript -ServerInstance $ServerInstance -Database $Database -BackupDirectory $backupDirectory -MinimumFreeSpaceGb $requiredFreeSpaceGb -ResultPath $receiptPath -SqlCmdPath $sqlcmdPath -Purpose PreMigration
        Assert-VerifiedBackupReceipt -ReceiptPath $receiptPath
        Write-SetupLog "Verified pre-migration backup is retained at $preMigrationBackupPath."
    }
    else {
        Write-SetupLog 'The existing database has no pending bundled migrations by journal count; bootstrap will still validate migration IDs and checksums.'
    }
}
else {
    Write-SetupLog 'The configured database does not exist. A clean database will be created; no pre-migration backup is applicable.'
}

$migrationPhaseStarted = $true
# The application's stderr (its privacy-safe reason and SQL error numbers) goes into this log.
$migrationRun = Invoke-EtpHeadlessApplication -FilePath $application -Argument '--initialize-configured-database'
Assert-EtpHeadlessStepSucceeded -Result $migrationRun -Step 'Configured database migration' -Log { param([string]$Message) Write-SetupLog $Message }

$postState = Invoke-SqlScalar -Query "SET NOCOUNT ON; SELECT state_desc + '|' + CONVERT(varchar(5),is_read_only) FROM sys.databases WHERE name=N'$Database';"
if ($postState -cne 'ONLINE|0') { throw "Post-migration health verification requires the database to be ONLINE and read-write." }
$postMigrationCount = [long](Invoke-SqlScalar -TargetDatabase $Database -Query "SET NOCOUNT ON; IF OBJECT_ID(N'dbo.schema_migrations',N'U') IS NULL THROW 51000,'Migration journal is missing.',1; SELECT COUNT_BIG(1) FROM dbo.schema_migrations;")
if ($postMigrationCount -ne $migrationFiles.Count) { throw "Post-migration health verification found $postMigrationCount applied migrations; $($migrationFiles.Count) bundled migrations are required." }
Invoke-SqlHealthCommand -Query "SET NOCOUNT ON; DBCC CHECKDB ([$Database]) WITH NO_INFOMSGS;"
$migrationPhaseCompleted = $true
Write-SetupLog 'EtpReporting migration completed and post-migration state, journal count, and DBCC integrity checks passed.'
# IE-RT-11: every run, so a database restored or created with AUTO_CLOSE ON is corrected too.
Write-SetupLog (Set-EtpDatabaseAutoCloseOff -Database $Database -ServerInstance $ServerInstance -Invoke { param([string]$Query) Invoke-SqlScalar -Query $Query })

if ($databaseAction -ceq 'Create') {
    # See New-EtpSetupOwnerLoginSql: without a login of its own the new Owner can open ETP
    # only as administrator. A restored database gets the same from the restore helper.
    & $sqlcmdPath -x -S $ServerInstance -E -b -d $Database -Q (New-EtpSetupOwnerLoginSql) | Out-Null
    $ownerLogin = if ($LASTEXITCODE -eq 0) {
        Invoke-SqlScalar -TargetDatabase $Database -Query "SET NOCOUNT ON; SELECT CASE WHEN SUSER_ID(SUSER_SNAME()) IS NOT NULL AND EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=SUSER_SNAME() AND role_code='OWNER' AND is_active=1) THEN 'READY' ELSE 'MISSING' END;"
    } else { 'FAILED' }
    if ($ownerLogin -cne 'READY') {
        throw "The new database was created and checked, but setup could not give $($identity.Name), its Owner, a SQL Server login of its own, so ETP would open only when run as administrator. The database is complete and was left as it is: follow docs\OPERATIONS.md, Owner recovery and maintenance, for that account (a SQL administrator calls dbo.configure_application_role for it as OWNER), then run setup again."
    }
    Write-SetupLog "$($identity.Name) is the new database's Owner and has its own SQL Server login, so ETP opens for it without administrator rights."
}

# 1.9.3 (Sagar's decision, 2 October 2026): Settings > Users works unelevated right after
# install. Active Owners need ALTER ANY LOGIN WITH GRANT OPTION, which SQL Server does not let
# the account running setup give itself, so a different SQL administrator - SYSTEM, through a
# one-off scheduled task - gives it (Invoke-EtpOwnerGrantOptionAsSystem, in
# etp-operations-common.ps1). Whatever happens here, the database and the Owner's access are
# complete: nothing in this step stops setup, and the read-only check after it says what is left.
$ownersWithoutGrantOption = try { Invoke-SqlScalar -TargetDatabase $Database -Query (New-EtpOwnersWithoutGrantOptionSql) } catch { 'UNKNOWN' }
if ($ownersWithoutGrantOption -cne '0') {
    $ownerGrant = try { Invoke-EtpOwnerGrantOptionAsSystem -SqlCmdPath $sqlcmdPath -ServerInstance $ServerInstance -Database $Database } catch { $null }
    if ($ownerGrant) { Write-SetupLog $ownerGrant.Message }
    else { Write-SetupLog 'WARNING: setup could not run its one-off task that gives Owners ALTER ANY LOGIN WITH GRANT OPTION. Nothing else depends on it.' }
}
$ownerLoginAdministration = try { Invoke-SqlScalar -TargetDatabase $Database -Query (New-EtpOwnerLoginAdministrationSql) } catch { 'UNKNOWN' }
if ($ownerLoginAdministration -ceq 'MISSING') {
    $manualGrant = Get-EtpOwnerGrantManualCommand -Identity $identity.Name -ServerInstance $ServerInstance -SqlCmdPath $sqlcmdPath
    $manualText = if ($manualGrant) { " To do it by hand, run this once in an administrator PowerShell window (it runs as SYSTEM, which must be a SQL administrator; 0 means granted): $manualGrant" } else { '' }
    Write-SetupLog "NOTE: $($identity.Name) is an Owner but still does not hold ALTER ANY LOGIN WITH GRANT OPTION, and SQL Server does not let setup grant a permission to the account running it. Until a different SQL administrator grants it (docs\OPERATIONS.md, Owners and SQL Server logins), adding or changing users in Settings > Users needs ETP started with 'Run as administrator'. Everything else works unelevated.$manualText"
}
elseif ($ownerLoginAdministration -ceq 'UNKNOWN') {
    Write-SetupLog 'NOTE: setup could not check whether the Owner running it can change users without "Run as administrator" (docs\OPERATIONS.md, Owners and SQL Server logins).'
}

& (Join-Path $scripts 'install-daily-backup-task.ps1')
& (Join-Path $scripts 'install-monthly-recovery-drill-task.ps1')
Write-SetupLog 'Daily backup and monthly recovery-drill tasks are installed.'
# The tasks are useless until the automation account has the operations module's rights.
$getAutomationGrantState = { Get-EtpAutomationGrantState -SqlCmd $sqlcmdPath -Server (Resolve-EtpSqlConnection -SqlCmd $sqlcmdPath -ServerInstance $ServerInstance) -Database $Database -AutomationPrincipal $operationConfiguration.automationPrincipal }
foreach ($line in @(Complete-EtpAutomationGrants -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $operationConfiguration.automationPrincipal -ScriptsDirectory $scripts `
        -GetState $getAutomationGrantState `
        -InstallModule { & (Join-Path $scripts 'install-etp-sql-operations.ps1') -ServerInstance $ServerInstance -Database $Database -AutomationPrincipal $operationConfiguration.automationPrincipal -SqlCmdPath $sqlcmdPath })) { Write-SetupLog $line }
# IE-RT-08: the five-minute task is registered disabled while the automation account cannot
# open the database, so a new PC does not fill the SQL Server log with failed sign-ins; the
# next setup run after Settings > Users enables it (Get-EtpAutomationTaskPlan).
$automationGrantState = try { & $getAutomationGrantState } catch { $null }
$automationTaskPlan = Get-EtpAutomationTaskPlan -GrantState $automationGrantState -AutomationPrincipal $operationConfiguration.automationPrincipal -Database $Database
& (Join-Path $scripts 'install-etp-automation-task.ps1') -Disabled:(-not $automationTaskPlan.Enabled)
Write-SetupLog $automationTaskPlan.Line
# A clean install on an encrypting edition gets this far with no recovery keys, and then
# every nightly backup refuses. Say it now, while somebody is still at the machine.
if ($editionEncryptsBackups -and -not (Test-Path -LiteralPath (Join-Path $backupDirectory 'certificate-custody.json') -PathType Leaf)) {
    Write-SetupLog 'WARNING: this SQL Server edition encrypts backups and no recovery keys have been exported, so the daily backup will refuse to run. Open the application as Owner, go to Settings > Database > Encrypted backup recovery keys, and select "Create and export recovery keys".'
}
Write-SetupLog 'ETP prerequisite bootstrap completed successfully.'
