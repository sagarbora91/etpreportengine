param([Parameter(Mandatory)][string]$PreviousInstallerPath, [switch]$SkipBackup)
$ErrorActionPreference = "Stop"
$installer=(Resolve-Path -LiteralPath $PreviousInstallerPath).Path
# -Purpose PreRollback keeps this copy out of the daily rotation: it is the state the
# database was in before the rollback, and the next backup would otherwise delete it.
if(-not $SkipBackup){ & (Join-Path $PSScriptRoot 'backup-etp-database.ps1') -Purpose PreRollback; if($LASTEXITCODE -ne 0){throw 'Pre-rollback backup failed.'} }
$process=Start-Process -FilePath $installer -ArgumentList @('/SILENT','/SUPPRESSMSGBOXES','/NORESTART') -Wait -PassThru
if($process.ExitCode -ne 0){throw "Rollback installer returned $($process.ExitCode)."}
Write-Host "Application rollback completed. The SQL database was retained; run health and report controls before acceptance."
