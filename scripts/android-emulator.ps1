[CmdletBinding()]
param(
  [ValidateSet('configure', 'preflight', 'start', 'stop', 'status')]
  [string]$Action = 'preflight',
  [string]$AvdName = 'saagar_api23_evidence',
  [switch]$ColdBoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $repoRoot
$toolRoot = Join-Path $workspaceRoot '.android-build'
$sdkRoot = Join-Path $toolRoot 'sdk'
$jdkHome = Join-Path $toolRoot 'jdk17\jdk-17.0.19+10'
$adb = Join-Path $sdkRoot 'platform-tools\adb.exe'
$emulator = Join-Path $sdkRoot 'emulator\emulator.exe'
$emulatorCheck = Join-Path $sdkRoot 'emulator\emulator-check.exe'
$avdManager = Join-Path $sdkRoot 'cmdline-tools\latest\bin\avdmanager.bat'
$imageDir = Join-Path $sdkRoot 'system-images\android-23\default\x86_64'
$androidDir = Join-Path $repoRoot 'android'
$localProperties = Join-Path $androidDir 'local.properties'

function Assert-File([string]$Path, [string]$Label) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "$Label is missing: $Path"
  }
}

function Set-AndroidEnvironment {
  $env:ANDROID_HOME = $sdkRoot
  $env:ANDROID_SDK_ROOT = $sdkRoot
  $env:JAVA_HOME = $jdkHome
  $env:Path = "$(Join-Path $jdkHome 'bin');$(Join-Path $sdkRoot 'platform-tools');$(Join-Path $sdkRoot 'emulator');$env:Path"
}

function Assert-Toolchain {
  Assert-File $adb 'ADB'
  Assert-File $emulator 'Android emulator'
  Assert-File $emulatorCheck 'Android emulator acceleration checker'
  Assert-File (Join-Path $jdkHome 'bin\java.exe') 'JDK 17'
  Assert-File (Join-Path $imageDir 'system.img') 'API 23 x86_64 system image'
}

function Ensure-AndroidProject {
  if (-not (Test-Path -LiteralPath (Join-Path $androidDir 'gradlew.bat') -PathType Leaf)) {
    Push-Location $repoRoot
    try { & npm.cmd run add:android } finally { Pop-Location }
  }
  $escapedSdk = $sdkRoot.Replace('\', '\\').Replace(':', '\:')
  [IO.File]::WriteAllText($localProperties, "sdk.dir=$escapedSdk`r`n", [Text.UTF8Encoding]::new($false))
}

function Ensure-Avd {
  $known = @(& $emulator -list-avds 2>$null)
  if ($known -contains $AvdName) { return }
  Assert-File $avdManager 'AVD manager'
  'no' | & $avdManager create avd --force --name $AvdName --package 'system-images;android-23;default;x86_64' --device 'pixel'
  if ($LASTEXITCODE -ne 0) { throw "Could not create AVD $AvdName" }
}

function Get-AvdSerials {
  $deviceLines = @(& $adb devices)
  foreach ($line in $deviceLines) {
    if ($line -notmatch '^(emulator-\d+)\s+device\s*$') { continue }
    $serial = $Matches[1]
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
      $avdReply = @(& $adb -s $serial emu avd name 2>$null)
      $avdExitCode = $LASTEXITCODE
    } finally {
      $ErrorActionPreference = $previousErrorAction
    }
    if ($avdExitCode -ne 0) { continue }
    $reportedName = $avdReply |
      Where-Object { $_ -and $_.Trim() -ne 'OK' } |
      Select-Object -First 1
    if ($reportedName -and $reportedName.Trim() -eq $AvdName) {
      Write-Output $serial
    }
  }
}

Set-AndroidEnvironment
Assert-Toolchain

switch ($Action) {
  'configure' {
    Ensure-AndroidProject
    Ensure-Avd
    Write-Output "Configured Android SDK: $sdkRoot"
    Write-Output "Configured API 23 AVD: $AvdName"
  }
  'preflight' {
    Ensure-AndroidProject
    Ensure-Avd
    & $emulatorCheck accel
    & $adb version
    Write-Output "ANDROID_SDK_ROOT=$sdkRoot"
    Write-Output "JAVA_HOME=$jdkHome"
    Write-Output "AVD=$AvdName"
  }
  'start' {
    Ensure-AndroidProject
    Ensure-Avd
    $serial = @(Get-AvdSerials) | Select-Object -First 1
    if (-not $serial) {
      $arguments = @('-avd', $AvdName, '-no-window', '-no-audio', '-no-boot-anim', '-gpu', 'swiftshader_indirect')
      if ($ColdBoot) { $arguments += @('-no-snapshot', '-wipe-data') }
      Start-Process -FilePath $emulator -ArgumentList $arguments -WindowStyle Hidden | Out-Null
    }
    $deadline = (Get-Date).AddMinutes(3)
    $booted = ''
    do {
      if (-not $serial) { $serial = @(Get-AvdSerials) | Select-Object -First 1 }
      if ($serial) { $booted = (& $adb -s $serial shell getprop sys.boot_completed 2>$null).Trim() }
      if ($booted -eq '1') { break }
      Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    if ($booted -ne '1') { throw 'API 23 emulator did not finish booting within three minutes.' }
    Write-Output "API 23 emulator ready: $serial ($AvdName)"
  }
  'stop' {
    $serials = @(Get-AvdSerials)
    foreach ($serial in $serials) { & $adb -s $serial emu kill | Out-Null }
    if ($serials.Count -eq 0) {
      Write-Output "API 23 emulator already stopped: $AvdName"
    } else {
      Write-Output "API 23 emulator stopped: $($serials -join ', ') ($AvdName)"
    }
  }
  'status' {
    & $adb devices -l
    & $emulator -list-avds
  }
}
