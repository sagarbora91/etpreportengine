param(
    [Parameter(Mandatory)][string]$InstallerPath,
    [string]$ExpectedVersion,
    [string]$PreviousInstallerPath,
    [string]$PreservedFilePath,
    [switch]$AllowPrerequisiteInstallation
)

$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Installer lifecycle validation must run from an elevated PowerShell session on a disposable test machine.'
}
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$initialInstaller = if ([string]::IsNullOrWhiteSpace($PreviousInstallerPath)) { $installer } else { (Resolve-Path -LiteralPath $PreviousInstallerPath).Path }
if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    [xml]$props = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) "Directory.Build.props")
    $ExpectedVersion = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText
}

function Get-NormalizedReleaseProductVersion([string]$value, [string]$label) {
    if ([string]::IsNullOrWhiteSpace($value)) { throw "$label is missing." }
    $match = [regex]::Match(
        $value.Trim(),
        '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:\+[0-9A-Za-z.-]+)?$',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) { throw "$label '$value' must be major.minor.patch with optional +build metadata." }
    return "$($match.Groups['major'].Value).$($match.Groups['minor'].Value).$($match.Groups['patch'].Value)"
}

$normalizedExpectedVersion = Get-NormalizedReleaseProductVersion $ExpectedVersion 'ExpectedVersion'
if (-not [string]::Equals($ExpectedVersion, $normalizedExpectedVersion, [StringComparison]::Ordinal)) {
    throw "ExpectedVersion must use the canonical major.minor.patch form."
}
$preservedPath = $null
$preservedHash = $null
if (-not [string]::IsNullOrWhiteSpace($PreservedFilePath)) {
    $preservedPath = (Resolve-Path -LiteralPath $PreservedFilePath).Path
    if (-not (Test-Path -LiteralPath $preservedPath -PathType Leaf)) { throw "PreservedFilePath must identify an existing file." }
    $preservedHash = (Get-FileHash -LiteralPath $preservedPath -Algorithm SHA256).Hash
}

function Assert-PreservedFileUnchanged([string]$stage) {
    if ([string]::IsNullOrWhiteSpace($preservedPath)) { return }
    if (-not (Test-Path -LiteralPath $preservedPath -PathType Leaf)) { throw "The preserved data file was removed during $stage." }
    $actualHash = (Get-FileHash -LiteralPath $preservedPath -Algorithm SHA256).Hash
    if (-not $actualHash.Equals($preservedHash, [StringComparison]::OrdinalIgnoreCase)) { throw "The preserved data file changed during $stage." }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("EtpInstallerLifecycle-" + [guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$prerequisiteTaskArgument = if ($AllowPrerequisiteInstallation) { '/MERGETASKS=sqlprerequisites' } else { '/MERGETASKS=!sqlprerequisites' }
$completed = $false
try {
    $installDir = Join-Path $testRoot "Application"
    $installLog = Join-Path $testRoot "install.log"
    $process = Start-Process -FilePath $initialInstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/ALLUSERS',$prerequisiteTaskArgument,"/DIR=$installDir","/LOG=$installLog") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer returned $($process.ExitCode). See $installLog" }
    Assert-PreservedFileUnchanged 'install'
    $exe = Join-Path $installDir "Etp.Reporting.Desktop.exe"
    if (-not (Test-Path -LiteralPath $exe)) { throw "Installed executable is missing." }
    # Installing the current package over the stable AppId exercises upgrade (or same-version repair).
    $upgradeLog = Join-Path $testRoot "upgrade.log"
    $process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/ALLUSERS',$prerequisiteTaskArgument,"/DIR=$installDir","/LOG=$upgradeLog") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Upgrade returned $($process.ExitCode)." }
    Assert-PreservedFileUnchanged 'upgrade'
    $installedVersionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $actualProductVersion = Get-NormalizedReleaseProductVersion $installedVersionInfo.ProductVersion 'Installed executable ProductVersion'
    if (-not [string]::Equals($actualProductVersion, $normalizedExpectedVersion, [StringComparison]::Ordinal)) {
        throw "Expected embedded ProductVersion $normalizedExpectedVersion after upgrade; installed '$($installedVersionInfo.ProductVersion)' (normalized '$actualProductVersion')."
    }
    $expectedFileVersion = "$normalizedExpectedVersion.0"
    if ([string]::IsNullOrWhiteSpace($installedVersionInfo.FileVersion) -or
        -not [string]::Equals($installedVersionInfo.FileVersion.Trim(), $expectedFileVersion, [StringComparison]::Ordinal)) {
        throw "Expected embedded FileVersion $expectedFileVersion after upgrade; installed '$($installedVersionInfo.FileVersion)'."
    }
    $uninstaller = Join-Path $installDir "unins000.exe"
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw "Uninstaller is missing." }
    $uninstallLog = Join-Path $testRoot "uninstall.log"
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$uninstallLog") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Uninstaller returned $($process.ExitCode)." }
    if (Test-Path -LiteralPath $exe) { throw "Executable remains after uninstall." }
    Assert-PreservedFileUnchanged 'uninstall'
    $mode = if ([string]::IsNullOrWhiteSpace($PreviousInstallerPath)) { "same-version repair" } else { "version upgrade" }
    Write-Host "Installer install, $mode, and uninstall passed for $ExpectedVersion."
    $completed = $true
} finally {
    if ($completed -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
    elseif (Test-Path -LiteralPath $testRoot) {
        Write-Warning "Installer lifecycle diagnostics retained at $testRoot"
    }
}
