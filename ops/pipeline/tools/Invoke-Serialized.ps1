<#
.SYNOPSIS
  Runs one heavy command (dotnet build / dotnet test / sqlcmd against SQL Express) at a time across
  every agent and worktree on this PC. The PC powers off under sustained heavy load, so parallel agents
  may write code concurrently but must run builds and tests through this script.

.EXAMPLE
  pwsh -NoProfile -File E:\ETP\Code\Worktrees\_tools\Invoke-Serialized.ps1 -WorkingDirectory E:\ETP\Code\Worktrees\ie-p1-a -Command "dotnet build Etp.Reporting.slnx -m:1 -nodeReuse:false"

  Exit code 75 means the lock was still busy after -WaitMinutes: wait a little and run the same call again.
  Any other exit code is the command's own.
#>
param(
    [Parameter(Mandatory)][string]$Command,
    [string]$WorkingDirectory = (Get-Location).Path,
    [int]$WaitMinutes = 6,
    [string]$Label = ''
)

$ErrorActionPreference = 'Stop'
# Priority pause (3 Oct 2026): while _tools\PAUSE-LANE-BUILDS exists, callers whose -Label is not listed in it wait here
# (without taking the lock) so a priority job such as the VM rehearsal gets the lock next. Delete the file to resume.
$pauseFile = Join-Path $PSScriptRoot 'PAUSE-LANE-BUILDS'
$pauseDeadline = (Get-Date).AddMinutes($WaitMinutes)
while ((Test-Path $pauseFile) -and -not (@(Get-Content $pauseFile -ErrorAction SilentlyContinue) -contains $Label)) {
    if ((Get-Date) -gt $pauseDeadline) { Write-Output "LOCK BUSY: lane builds are paused for a priority job. Retry the same call."; exit 75 }
    Start-Sleep -Seconds 15
}
$mutex = New-Object System.Threading.Mutex($false, 'Global\EtpHeavyJobLock')
$acquired = $false
try {
    try { $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes($WaitMinutes)) }
    catch [System.Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) {
        Write-Output "LOCK BUSY: another heavy job held the lock for $WaitMinutes minutes. Retry the same call."
        exit 75
    }
    $log = 'E:\ETP\Code\Worktrees\_tools\heavy-jobs.log'
    $start = Get-Date
    Add-Content -Path $log -Value ("{0:u} START [{1}] {2} :: {3}" -f $start, $Label, $WorkingDirectory, $Command) -ErrorAction SilentlyContinue
    # Workpc (2 Oct 2026): the SDK lives in E:\dotnet and pwsh 7 in E:\Tools\PowerShell\7; the machine PATH still
    # lists the runtime-only C:\Program Files\dotnet first, so put the E: tools ahead for this job and its children.
    $env:PATH = 'E:\dotnet;E:\Tools\PowerShell\7;E:\Tools\Inno Setup 6;' + $env:PATH
    $env:DOTNET_ROOT = 'E:\dotnet'
    if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = 'E:\Tools\NuGetCache' }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    # Heat control (the PC powers off when it overheats): the job and every process it starts
    # (MSBuild, compiler, test hosts) inherit 4 logical CPUs and below-normal priority.
    $self = [System.Diagnostics.Process]::GetCurrentProcess()
    $self.ProcessorAffinity = [IntPtr]0x0F
    $self.PriorityClass = 'BelowNormal'
    $env:DOTNET_PROCESSOR_COUNT = '4'
    Push-Location $WorkingDirectory
    try {
        & pwsh -NoProfile -Command $Command
        $code = $LASTEXITCODE
    }
    finally { Pop-Location }
    Add-Content -Path $log -Value ("{0:u} END   [{1}] exit {2} after {3:n0}s" -f (Get-Date), $Label, $code, ((Get-Date) - $start).TotalSeconds) -ErrorAction SilentlyContinue
    # Cool-down while still holding the lock, so heavy jobs never run back to back.
    Start-Sleep -Seconds 45
    exit $code
}
finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
