param($Session, $EvidenceRoot)
$ErrorActionPreference = 'Stop'
$result = Invoke-Command -Session $Session -ScriptBlock {
    $ErrorActionPreference = 'Stop'
    $pc = Get-CimInstance Win32_ComputerSystem
    if ($pc.Name -ne 'DESKTOP-IPT0J6H' -or $pc.Model -ne 'Virtual Machine') { throw 'Acceptance guest identity mismatch' }
    $root = 'C:\ETPAcceptance\UI-20260913-baseline'
    if (Test-Path -LiteralPath $root) { throw 'Baseline already exists; inspect rather than overwrite' }
    New-Item -ItemType Directory -Path $root | Out-Null
    $appRoot = 'C:\Program Files\Saagar Traders\ETP Reporting Engine'
    $sql = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
    $query = 'SET NOCOUNT ON; SELECT COUNT_BIG(*) SalesRows,SUM(source_net_amount) NetSales,SUM(source_quantity) Units FROM dbo.sales_lines; SELECT COUNT_BIG(*) ImportedFiles FROM dbo.import_files; SELECT COUNT_BIG(*) LineageRows FROM dbo.source_lineage; SELECT @@VERSION SqlVersion; SELECT DB_NAME() DatabaseName;'
    & $sql -S '.\SQLEXPRESS' -E -b -d EtpReporting -Q $query | Set-Content "$root\original-counts.txt"
    if ($LASTEXITCODE -ne 0) { throw 'Read-only baseline failed' }
    $links = foreach ($base in @('C:\ProgramData\Microsoft\Windows\Start Menu\Programs',"$env:APPDATA\Microsoft\Windows\Start Menu\Programs",'C:\Users\Public\Desktop',"$env:USERPROFILE\Desktop")) {
        Get-ChildItem -LiteralPath $base -Filter '*ETP*.lnk' -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
            $link = (New-Object -ComObject WScript.Shell).CreateShortcut($_.FullName)
            [pscustomobject]@{path=$_.FullName;target=$link.TargetPath;targetExists=(Test-Path -LiteralPath $link.TargetPath)}
        }
    }
    $links | ConvertTo-Json | Set-Content "$root\shortcuts.json"
    Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object DisplayName -Match 'ETP Reporting' | Select-Object DisplayName,DisplayVersion,InstallLocation | ConvertTo-Json | Set-Content "$root\installation.json"
    Copy-Item -LiteralPath $appRoot -Destination "$root\original-application" -Recurse
    $settingsSources = @("$env:LOCALAPPDATA\EtpReporting", "$env:APPDATA\EtpReporting")
    $settingsIndex=0
    foreach($settingsSource in $settingsSources) {
        if(Test-Path -LiteralPath $settingsSource){ Copy-Item -LiteralPath $settingsSource -Destination "$root\protected-settings-$settingsIndex" -Recurse }
        $settingsIndex++
    }
    Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber | ConvertTo-Json | Set-Content "$root\os.json"
    Get-CimInstance Win32_Process -Filter "Name='Etp.Reporting.Desktop.exe'" | Select-Object ProcessId,ExecutablePath,SessionId | ConvertTo-Json | Set-Content "$root\processes.json"
    $backup = 'C:\ProgramData\EtpReporting\Backups\EtpReporting-UI-20260913-original.bak'
    if(Test-Path -LiteralPath $backup){throw 'Protected backup already exists'}
    New-Item -ItemType Directory -Path (Split-Path $backup) -Force | Out-Null
    & $sql -S '.\SQLEXPRESS' -E -b -Q "BACKUP DATABASE [EtpReporting] TO DISK=N'$backup' WITH COPY_ONLY,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$backup' WITH CHECKSUM;" | Set-Content "$root\backup-verification.txt"
    if($LASTEXITCODE -ne 0){throw 'Backup or checksum verification failed'}
    $receipt = [ordered]@{timestampUtc=[DateTime]::UtcNow.ToString('o');guest=$pc.Name;root=$root;backupPath=$backup;backupSha256=(Get-FileHash -LiteralPath $backup).Hash;backupVerified=$true;appSha256=(Get-FileHash -LiteralPath "$appRoot\Etp.Reporting.Desktop.exe").Hash;version=(Get-Item -LiteralPath "$appRoot\Etp.Reporting.Desktop.exe").VersionInfo.ProductVersion;identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name}
    $receipt | ConvertTo-Json | Set-Content "$root\receipt.json"
    [pscustomobject]$receipt
}
$localRoot = Join-Path $EvidenceRoot '..\20260913-acceptance-resume'
$result | ConvertTo-Json | Set-Content (Join-Path $localRoot 'vm-protected-baseline.json')
foreach($file in @('original-counts.txt','shortcuts.json','installation.json','os.json','processes.json','backup-verification.txt')) {
    Copy-Item -FromSession $Session -LiteralPath "C:\ETPAcceptance\UI-20260913-baseline\$file" -Destination (Join-Path $localRoot $file)
}
