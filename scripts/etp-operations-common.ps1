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
    $candidates = @($ExplicitPath, (Join-Path $env:ProgramFiles 'Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'), (Join-Path $env:ProgramFiles 'sqlcmd\sqlcmd.exe'))
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
    if ($receipt.schemaVersion -ne 2 -or $receipt.verified -ne $true -or $receipt.database -cne $Database -or $receipt.encryption -cne 'AES_256') { throw 'A verified encrypted backup receipt is required.' }
    $root = [IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\') + '\'
    $backup = [IO.Path]::GetFullPath($receipt.backupPath)
    if (-not $backup.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetDirectoryName($backup)+'\' -ine $root) { throw 'The receipt backup is outside the backup folder.' }
    Assert-EtpNoLinks $backup
    $file = Get-Item -LiteralPath $backup
    if ($receipt.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $file.Length -ne $receipt.lengthBytes -or (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash -ine $receipt.sha256) { throw 'Backup verification failed: the receipt and file differ.' }
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

function Get-EtpRetainedBackupReceipts {
    param([object[]]$Receipts)
    # Latest successful backup per UTC day, plus latest per calendar month.
    $ordered = @($Receipts | Sort-Object { ([datetimeoffset]$_.verifiedAtUtc).UtcDateTime } -Descending)
    $days = @{}; $months = @{}; $keep = @{}
    foreach ($receipt in $ordered) {
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

function Register-EtpScheduledOperation {
    param([string]$TaskName,[object]$Action,[object]$Trigger,[string]$Description)
    $configuration = Get-EtpOperationsConfiguration
    $principal = New-ScheduledTaskPrincipal -UserId $configuration.automationPrincipal -LogonType S4U -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)
    Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Settings $settings -Principal $principal -Description $Description -Force | Out-Null
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    if ($task.State -eq 'Disabled' -or $task.Principal.UserId -ine $configuration.automationPrincipal -or $task.Principal.RunLevel -ne 'Limited') { throw 'The scheduled task did not retain the required principal and settings.' }
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
