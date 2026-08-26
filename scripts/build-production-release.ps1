[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedCommit,
  [Parameter(Mandatory = $true)][string]$OutputDirectory,
  [string]$GradleInitScript = '',
  [string]$ExpectedGradleInitSha256 = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$credentialVariables = @('SAAGAR_KEYSTORE_FILE', 'SAAGAR_KEYSTORE_PASSWORD', 'SAAGAR_KEY_ALIAS', 'SAAGAR_KEY_PASSWORD')
$buildStartedUtc = (Get-Date).ToUniversalTime()

function Invoke-Checked {
  param([string]$FilePath, [string[]]$ArgumentList, [string]$Label)
  & $FilePath @ArgumentList
  if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE" }
}

function Assert-CleanExpectedCommit {
  param([string]$GitPath)
  $head = (& $GitPath -C $repositoryRoot rev-parse HEAD).Trim().ToLowerInvariant()
  if ($LASTEXITCODE -ne 0 -or $head -ne $ExpectedCommit.ToLowerInvariant()) {
    throw 'Production release blocked: HEAD does not equal the approved expected commit.'
  }
  $status = @(& $GitPath -C $repositoryRoot status --porcelain --untracked-files=all)
  if ($LASTEXITCODE -ne 0 -or $status.Count -ne 0) {
    throw 'Production release blocked: use a clean frozen worktree with no tracked, staged or untracked changes.'
  }
  foreach ($state in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REBASE_HEAD', 'rebase-merge', 'rebase-apply')) {
    $statePath = (& $GitPath -C $repositoryRoot rev-parse --git-path $state).Trim()
    if ($LASTEXITCODE -eq 0 -and $statePath) {
      if (-not [IO.Path]::IsPathRooted($statePath)) { $statePath = Join-Path $repositoryRoot $statePath }
      if (Test-Path -LiteralPath $statePath) { throw "Production release blocked: unfinished Git operation ($state)." }
    }
  }
  return $head
}

try {
  $git = (Get-Command git -ErrorAction Stop).Source
  $npm = (Get-Command npm.cmd -ErrorAction Stop).Source
  $node = (Get-Command node.exe -ErrorAction Stop).Source
  $sourceCommit = Assert-CleanExpectedCommit -GitPath $git

  if (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    throw 'Production release blocked: OutputDirectory must be an absolute path outside the repository.'
  }
  $outputPath = [IO.Path]::GetFullPath($OutputDirectory)
  $repositoryPrefix = $repositoryRoot.TrimEnd('\') + '\'
  if ($outputPath -eq $repositoryRoot -or $outputPath.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Production release blocked: OutputDirectory must be outside the repository.'
  }
  if (Test-Path -LiteralPath $outputPath) {
    throw 'Production release blocked: OutputDirectory must be a new path; release evidence is append-only.'
  }

  $gradleInitSha256 = ''
  if ($GradleInitScript) {
    if (-not [IO.Path]::IsPathRooted($GradleInitScript) -or -not (Test-Path -LiteralPath $GradleInitScript -PathType Leaf)) {
      throw 'Production release blocked: GradleInitScript must name an existing absolute file.'
    }
    if ($ExpectedGradleInitSha256 -notmatch '^[0-9a-fA-F]{64}$') {
      throw 'Production release blocked: a 64-character ExpectedGradleInitSha256 is required with GradleInitScript.'
    }
    $gradleInitSha256 = (Get-FileHash -LiteralPath $GradleInitScript -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($gradleInitSha256 -ne $ExpectedGradleInitSha256.ToUpperInvariant()) {
      throw 'Production release blocked: Gradle init script hash does not match the approved identity.'
    }
  } elseif ($ExpectedGradleInitSha256) {
    throw 'Production release blocked: ExpectedGradleInitSha256 was supplied without GradleInitScript.'
  }

  foreach ($name in $credentialVariables) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, 'Process'))) {
      throw 'Production release blocked: required process-scoped signing credentials are incomplete.'
    }
  }
  $keystoreFile = [Environment]::GetEnvironmentVariable('SAAGAR_KEYSTORE_FILE', 'Process')
  if (-not [IO.Path]::IsPathRooted($keystoreFile) -or -not (Test-Path -LiteralPath $keystoreFile -PathType Leaf)) {
    throw 'Production release blocked: the process-scoped keystore path is invalid.'
  }

  $recipePath = $MyInvocation.MyCommand.Path
  $recipeSha256 = (Get-FileHash -LiteralPath $recipePath -Algorithm SHA256).Hash.ToUpperInvariant()
  $wwwTree = (& $git -C $repositoryRoot rev-parse 'HEAD:www').Trim().ToUpperInvariant()
  if ($LASTEXITCODE -ne 0 -or -not $wwwTree) { throw 'Production release blocked: unable to resolve the frozen WWW tree.' }
  $buildIdentitySha256 = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'www\build-identity.js') -Algorithm SHA256).Hash.ToUpperInvariant()

  $gradleWrapper = Join-Path $repositoryRoot 'android\gradlew.bat'
  if (-not (Test-Path -LiteralPath $gradleWrapper -PathType Leaf)) {
    Invoke-Checked -FilePath $npm -ArgumentList @('run', 'add:android') -Label 'Capacitor Android platform generation'
  }
  Invoke-Checked -FilePath $npm -ArgumentList @('run', 'sync') -Label 'Capacitor sync'
  Invoke-Checked -FilePath $node -ArgumentList @((Join-Path $repositoryRoot 'build-overrides\apply-overrides.js')) -Label 'Android override application'
  Invoke-Checked -FilePath $node -ArgumentList @((Join-Path $repositoryRoot 'scripts\verify-generated-android-release.mjs')) -Label 'Generated Android release verification'
  Invoke-Checked -FilePath $npm -ArgumentList @('run', 'prepare:api23') -Label 'API-23 asset preparation'

  $gradleArguments = @('clean', 'assembleRelease', '--no-daemon', '--stacktrace')
  if ($GradleInitScript) { $gradleArguments += @('--init-script', $GradleInitScript) }
  Push-Location -LiteralPath (Join-Path $repositoryRoot 'android')
  try { Invoke-Checked -FilePath $gradleWrapper -ArgumentList $gradleArguments -Label 'Clean production release build' }
  finally { Pop-Location }

  Assert-CleanExpectedCommit -GitPath $git | Out-Null
  $generatedApk = Join-Path $repositoryRoot 'android\app\build\outputs\apk\release\app-release.apk'
  if (-not (Test-Path -LiteralPath $generatedApk -PathType Leaf)) { throw 'Production release blocked: expected release APK is missing.' }
  $generatedItem = Get-Item -LiteralPath $generatedApk
  if ($generatedItem.LastWriteTimeUtc -lt $buildStartedUtc) { throw 'Production release blocked: release APK predates this clean build.' }

  New-Item -ItemType Directory -Path $outputPath | Out-Null
  $artifactName = 'SaagarCC-production-' + $sourceCommit.Substring(0, 12) + '.apk'
  $artifactPath = Join-Path $outputPath $artifactName
  Copy-Item -LiteralPath $generatedApk -Destination $artifactPath
  $registerPath = Join-Path $outputPath ($artifactName -replace '\.apk$', '.release.json')
  Invoke-Checked -FilePath $node -ArgumentList @((Join-Path $repositoryRoot 'scripts\release-register.mjs'), $artifactPath, $registerPath) -Label 'Release registration'

  $artifactItem = Get-Item -LiteralPath $artifactPath
  $artifactSha256 = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToUpperInvariant()
  $receipt = [ordered]@{
    format = 'saagar-production-build-receipt'
    version = 1
    createdAt = (Get-Date).ToUniversalTime().ToString('o')
    sourceCommit = $sourceCommit
    sourceTreeClean = $true
    wwwTreeGitObject = $wwwTree
    buildIdentitySha256 = $buildIdentitySha256
    recipeSha256 = $recipeSha256
    gradleInitSha256 = $gradleInitSha256
    artifactFile = $artifactName
    artifactBytes = $artifactItem.Length
    artifactSha256 = $artifactSha256
    releaseRegisterFile = (Split-Path -Leaf $registerPath)
    credentialsSource = 'process-environment'
  }
  $receiptPath = Join-Path $outputPath 'production-build-receipt.json'
  $utf8NoBom = New-Object Text.UTF8Encoding($false)
  [IO.File]::WriteAllText($receiptPath, (($receipt | ConvertTo-Json -Depth 4) + [Environment]::NewLine), $utf8NoBom)
  Write-Output "Production build receipt: $receiptPath"
  Write-Output "APK SHA-256: $artifactSha256"
  Write-Output "Recipe SHA-256: $recipeSha256"
} finally {
  foreach ($name in $credentialVariables) {
    [Environment]::SetEnvironmentVariable($name, $null, [EnvironmentVariableTarget]::Process)
    Remove-Item -LiteralPath ("Env:" + $name) -ErrorAction SilentlyContinue
  }
}
