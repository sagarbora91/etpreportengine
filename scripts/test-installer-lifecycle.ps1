param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [string]$ExpectedVersion,
    [string]$PreviousInstallerPath
)

$ErrorActionPreference = "Stop"
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$initialInstaller = if ([string]::IsNullOrWhiteSpace($PreviousInstallerPath)) { $installer } else { (Resolve-Path -LiteralPath $PreviousInstallerPath).Path }
if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    [xml]$props = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) "Directory.Build.props")
    $ExpectedVersion = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
}
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("EtpInstallerLifecycle-" + [guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $installDir = Join-Path $testRoot "Application"
    $installLog = Join-Path $testRoot "install.log"
    $process = Start-Process -FilePath $initialInstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$installDir","/LOG=$installLog") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer returned $($process.ExitCode). See $installLog" }
    $exe = Join-Path $installDir "Etp.Reporting.Desktop.exe"
    if (-not (Test-Path -LiteralPath $exe)) { throw "Installed executable is missing." }
    # Installing the current package over the stable AppId exercises upgrade (or same-version repair).
    $upgradeLog = Join-Path $testRoot "upgrade.log"
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=$installDir","/LOG=$upgradeLog") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Upgrade returned $($process.ExitCode)." }
    $actual = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
    if ($actual -and -not $actual.StartsWith($ExpectedVersion, [StringComparison]::Ordinal)) { throw "Expected $ExpectedVersion after upgrade; installed $actual." }
    $uninstaller = Join-Path $installDir "unins000.exe"
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw "Uninstaller is missing." }
    $uninstallLog = Join-Path $testRoot "uninstall.log"
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$uninstallLog") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Uninstaller returned $($process.ExitCode)." }
    if (Test-Path -LiteralPath $exe) { throw "Executable remains after uninstall." }
    $mode = if ([string]::IsNullOrWhiteSpace($PreviousInstallerPath)) { "same-version repair" } else { "version upgrade" }
    Write-Host "Installer install, $mode, and uninstall passed for $ExpectedVersion."
} finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
