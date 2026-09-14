param($Session, $EvidenceRoot)
$ErrorActionPreference='Stop'
$localRoot=Join-Path $EvidenceRoot '..\20260913-acceptance-resume'
$receipt=Get-Content -LiteralPath (Join-Path $localRoot 'candidate-r10-receipt.json') -Raw | ConvertFrom-Json
$installer=Join-Path $localRoot 'installer-1.8.8-r10\EtpReportingEngine-Setup-1.8.8-x64.exe'
if((Get-FileHash -LiteralPath $installer).Hash -ne $receipt.artifacts.installer.sha256){throw 'Host installer hash mismatch'}
Invoke-Command -Session $Session -ScriptBlock {
    $pc=Get-CimInstance Win32_ComputerSystem
    if($pc.Name -ne 'DESKTOP-IPT0J6H' -or $pc.Model -ne 'Virtual Machine'){throw 'Acceptance VM identity mismatch'}
    $root='C:\ETPAcceptance\UI-20260913-r10'
    if(Test-Path -LiteralPath $root){throw 'Candidate install attempt already exists; inspect it before retrying'}
    New-Item -ItemType Directory -Path $root | Out-Null
}
Copy-Item -ToSession $Session -LiteralPath $installer -Destination 'C:\ETPAcceptance\UI-20260913-r10\EtpReportingEngine-Setup-1.8.8-x64.exe'
try {
    $result=Invoke-Command -Session $Session -ArgumentList $receipt.artifacts.installer.sha256,$receipt.artifacts.executable.sha256 -ScriptBlock {
        param($installerHash,$exeHash)
        $ErrorActionPreference='Stop'
        $root='C:\ETPAcceptance\UI-20260913-r10'
        $original=Get-Content -LiteralPath 'C:\ETPAcceptance\UI-20260913-baseline\receipt.json' -Raw | ConvertFrom-Json
        if(!$original.backupVerified -or (Get-FileHash -LiteralPath $original.backupPath).Hash -ne $original.backupSha256){throw 'Original verified backup is missing or changed'}
        $installer=Join-Path $root 'EtpReportingEngine-Setup-1.8.8-x64.exe'
        if((Get-FileHash -LiteralPath $installer).Hash -ne $installerHash){throw 'Transferred installer hash mismatch'}
        if(@(Get-Process 'Etp.Reporting.Desktop' -ErrorAction SilentlyContinue).Count){throw 'ETP is running; clean interactive closure is needed before installation'}
        $appRoot='C:\Program Files\Saagar Traders\ETP Reporting Engine'
        $sql='C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'
        $query='SET NOCOUNT ON; SELECT COUNT_BIG(*) SalesRows,SUM(source_net_amount) NetSales,SUM(source_quantity) Units FROM dbo.sales_lines; SELECT COUNT_BIG(*) ImportedFiles FROM dbo.import_files; SELECT COUNT_BIG(*) LineageRows FROM dbo.source_lineage;'
        $before=@(& $sql -S '.\SQLEXPRESS' -E -b -d EtpReporting -Q $query)
        if($LASTEXITCODE -ne 0){throw 'Pre-install aggregate baseline failed'}
        $before | Set-Content "$root\before-counts.txt"
        $preferences=@{}
        foreach($name in @('settings.json','ui-preferences.json')) {
            $path=Join-Path "$env:LOCALAPPDATA\EtpReporting" $name
            $preferences[$name]=if(Test-Path -LiteralPath $path){(Get-FileHash -LiteralPath $path).Hash}else{$null}
        }
        $preferences | ConvertTo-Json | Set-Content "$root\before-settings-hashes.json"
        $setup=Start-Process -FilePath $installer -WindowStyle Hidden -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=desktopicon',('/LOG="'+$root+'\installer.log"')) -PassThru
        if(!$setup.WaitForExit(900000)){throw 'Installer still running after 15 minutes; do not retry automatically'}
        if($setup.ExitCode -ne 0){throw "Installer exit code $($setup.ExitCode); inspect retained log"}
        $app=Join-Path $appRoot 'Etp.Reporting.Desktop.exe'
        if((Get-FileHash -LiteralPath $app).Hash -ne $exeHash){throw 'Installed executable hash mismatch'}
        $after=@(& $sql -S '.\SQLEXPRESS' -E -b -d EtpReporting -Q $query)
        if($LASTEXITCODE -ne 0){throw 'Post-install aggregate query failed'}
        $after | Set-Content "$root\after-counts.txt"
        if(($before -join "`n") -cne ($after -join "`n")){throw 'Protected sales/import/lineage counts changed'}
        foreach($name in $preferences.Keys) {
            $path=Join-Path "$env:LOCALAPPDATA\EtpReporting" $name
            $hash=if(Test-Path -LiteralPath $path){(Get-FileHash -LiteralPath $path).Hash}else{$null}
            if($hash -cne $preferences[$name]){throw "Preference/config file changed: $name"}
        }
        & $sql -S '.\SQLEXPRESS' -E -b -d EtpReporting -Q 'DBCC CHECKDB ([EtpReporting]) WITH NO_INFOMSGS;' | Set-Content "$root\integrity.txt"
        if($LASTEXITCODE -ne 0){throw 'Post-install integrity check failed'}
        $shortcut='C:\ProgramData\Microsoft\Windows\Start Menu\Programs\ETP Reporting Engine\ETP Reporting Engine.lnk'
        if(!(Test-Path -LiteralPath $shortcut)){throw 'Start menu shortcut missing after install'}
        $link=(New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
        if($link.TargetPath -ne $app){throw 'Start menu shortcut points to wrong executable'}
        $result=[ordered]@{timestampUtc=[DateTime]::UtcNow.ToString('o');candidate='1.8.8-r10';version=(Get-Item -LiteralPath $app).VersionInfo.ProductVersion;installedSha256=$exeHash;installerSha256=$installerHash;installerExitCode=$setup.ExitCode;countsUnchanged=$true;settingsUnchanged=$true;integrityPassed=$true;startMenuShortcut=$shortcut;startMenuTarget=$link.TargetPath;uiInteraction='UNVERIFIED - native capture blocked';root=$root}
        $result | ConvertTo-Json | Set-Content "$root\result.json"
        [pscustomobject]$result
    }
    $result | ConvertTo-Json | Set-Content (Join-Path $localRoot 'vm-r10-install-result.json')
} finally {
    Copy-Item -FromSession $Session -LiteralPath 'C:\ETPAcceptance\UI-20260913-r10' -Destination (Join-Path $localRoot 'vm-r10-install-evidence') -Recurse
}
