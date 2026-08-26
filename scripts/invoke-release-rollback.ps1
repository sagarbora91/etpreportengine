param([Parameter(Mandatory)][string]$PreviousInstallerPath, [switch]$SkipBackup)
$ErrorActionPreference = "Stop"
$installer=(Resolve-Path -LiteralPath $PreviousInstallerPath).Path
if(-not $SkipBackup){ & (Join-Path $PSScriptRoot 'backup-etp-database.ps1'); if($LASTEXITCODE -ne 0){throw 'Pre-rollback backup failed.'} }
$process=Start-Process -FilePath $installer -ArgumentList @('/SILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
if($process.ExitCode -ne 0){throw "Rollback installer returned $($process.ExitCode)."}
Write-Host "Application rollback completed. The SQL database was retained; run health and report controls before acceptance."
