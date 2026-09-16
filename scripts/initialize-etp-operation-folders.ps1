param(
    [Parameter(Mandatory)][string]$SqlServiceIdentity,
    [string]$ServerInstance='.\SQLEXPRESS',
    [string]$Database='EtpReporting',
    [string]$AutomationPrincipal=([Environment]::MachineName+'\EtpAutomation'),
    [switch]$GrantAutomationFolderAccess=$true,
    [switch]$CreateAutomationAccount
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')
Assert-EtpLocalSqlTarget $ServerInstance $Database
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run folder setup as a Windows administrator.' }
$root=[IO.Path]::GetFullPath((Join-Path $env:ProgramData 'EtpReporting'))
Assert-EtpNoLinks $root
$sqlSid=([Security.Principal.NTAccount]::new($SqlServiceIdentity)).Translate([Security.Principal.SecurityIdentifier])
$automationSid=$null
if ($GrantAutomationFolderAccess) {
    if ($AutomationPrincipal -notmatch ('^'+[regex]::Escape([Environment]::MachineName)+'\\[^\\]+$')) { throw 'Choose the dedicated local automation account.' }
    if ($CreateAutomationAccount) {
        $accountName=$AutomationPrincipal.Split('\')[1]
        if (-not (Get-LocalUser -Name $accountName -ErrorAction SilentlyContinue)) {
            # S4U tasks do not store a password. Generate an unrecorded random local
            # password rather than enabling a blank or shared interactive credential.
            $randomBytes=New-Object byte[] 48
            $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($randomBytes) } finally { $rng.Dispose() }
            $password=ConvertTo-SecureString ('Etp!'+[Convert]::ToBase64String($randomBytes)) -AsPlainText -Force
            try {
                New-LocalUser -Name $accountName -Password $password -AccountNeverExpires -PasswordNeverExpires `
                    -Description 'ETP scheduled operations; dedicated non-administrator S4U account' | Out-Null
            }
            finally { $password.Dispose(); [Array]::Clear($randomBytes,0,$randomBytes.Length) }
        }
    }
    $automationSid=([Security.Principal.NTAccount]::new($AutomationPrincipal)).Translate([Security.Principal.SecurityIdentifier])
    if ($automationSid.Value -in @('S-1-5-18','S-1-5-19','S-1-5-20') -or $automationSid.Value -match '-500$') { throw 'Choose a non-administrator automation account.' }
    if ($automationSid.Value -in @(Get-LocalGroupMember -SID 'S-1-5-32-544' | ForEach-Object { $_.SID.Value })) { throw 'The automation account must not be an administrator.' }
    if (-not (Get-LocalUser -SID $automationSid -ErrorAction Stop).Enabled) { throw 'Enable the dedicated automation account first.' }
}
function Set-PrivateDirectory([string]$Path,[switch]$ReadOnlyAutomation,[switch]$ParentOnly) {
    $full=[IO.Path]::GetFullPath($Path)
    if ($full -ine $root -and -not $full.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Folder setup escaped its intended root.' }
    Assert-EtpNoLinks $full
    New-Item -ItemType Directory -Path $full -Force | Out-Null
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true,$false)
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid),'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    }
    $sqlRights=if ($ReadOnlyAutomation) { 'ReadAndExecute' } else { 'Modify' }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sqlSid,$sqlRights,'ContainerInherit,ObjectInherit','None','Allow'))
    if ($automationSid) {
        $rights=if ($ReadOnlyAutomation) { 'ReadAndExecute' } else { 'Modify' }
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($automationSid,$rights,'ContainerInherit,ObjectInherit','None','Allow'))
    }
    Set-Acl -LiteralPath $full -AclObject $acl
    if ($ParentOnly) { return }
    # Existing explicit Users ACEs are removed as well as inherited ones.
    $pending=[Collections.Generic.Queue[string]]::new(); $pending.Enqueue($full)
    while ($pending.Count -gt 0) {
        foreach ($child in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
            Assert-EtpNoLinks $child.FullName
            $childAcl=Get-Acl -LiteralPath $child.FullName
            $childAcl.SetAccessRuleProtection($false,$false)
            foreach ($rule in @($childAcl.Access)) { if (-not $rule.IsInherited) { $childAcl.RemoveAccessRuleSpecific($rule) } }
            $childAcl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
            Set-Acl -LiteralPath $child.FullName -AclObject $childAcl
            if ($child.PSIsContainer) { $pending.Enqueue($child.FullName) }
        }
    }
}
Set-PrivateDirectory $root -ReadOnlyAutomation -ParentOnly
foreach ($folder in @('Backups','Documents','Share','SetupLogs')) { Set-PrivateDirectory (Join-Path $root $folder) }
New-Item -ItemType Directory -Path (Join-Path $root 'Backups\RecoveryDrill') -Force | Out-Null
Set-PrivateDirectory (Join-Path $root 'Operations') -ReadOnlyAutomation
if ($GrantAutomationFolderAccess) {
    Write-EtpJsonAtomically -Path (Join-Path $root 'Operations\operations.json') -Value @{
        serverInstance=$ServerInstance; database=$Database; automationPrincipal=$AutomationPrincipal; allowAutomationFolderAccess=$true
    } -Replace
    $configPath=Join-Path $root 'Operations\operations.json'
    $configAcl=Get-Acl -LiteralPath $configPath
    $configAcl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    Set-Acl -LiteralPath $configPath -AclObject $configAcl
    Write-Output 'Protected folders and dedicated automation configuration prepared.'
}
else { Write-Output 'Strict folder protection applied. Automation remains unconfigured until its folder-access decision is approved.' }
