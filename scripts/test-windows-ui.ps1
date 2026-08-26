param(
    [string]$Executable = "artifacts/windows-release/Etp.Reporting.Desktop.exe",
    [int]$StartupTimeoutSeconds = 30,
    [switch]$AccessibilityAudit
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repoRoot = Split-Path -Parent $PSScriptRoot
$exe = [IO.Path]::GetFullPath((Join-Path $repoRoot $Executable))
if (-not (Test-Path -LiteralPath $exe)) { throw "Executable not found: $exe" }
$process = Start-Process -FilePath $exe -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $window = $null
    while ([DateTime]::UtcNow -lt $deadline -and $null -eq $window) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) { throw "Application exited during startup with code $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne 0) { $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle) }
    }
    if ($null -eq $window) { throw "Main window did not appear within $StartupTimeoutSeconds seconds." }
    if ($window.Current.Name -notlike '*ETP Reporting Engine*') { throw "Unexpected window title: $($window.Current.Name)" }

    $all = $window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
    $buttons = @($all | Where-Object { $_.Current.ControlType -eq [Windows.Automation.ControlType]::Button })
    $navigation = @($buttons | Where-Object { $_.Current.Name -like 'Open *' })
    if ($navigation.Count -lt 6) { throw "Expected at least 6 navigation actions; found $($navigation.Count)." }
    if ($buttons.Count -lt 1) { throw "No actionable buttons were exposed through UI Automation." }
    foreach ($item in $navigation) {
        $pattern = $null
        if ($item.Current.IsEnabled -and $item.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            ([Windows.Automation.InvokePattern]$pattern).Invoke()
        }
    }
    if ($AccessibilityAudit) {
        $unnamed = @($all | Where-Object {
            $_.Current.IsKeyboardFocusable -and [string]::IsNullOrWhiteSpace($_.Current.Name) -and
            $_.Current.ControlType -ne [Windows.Automation.ControlType]::DataItem -and
            $_.Current.ControlType -ne [Windows.Automation.ControlType]::Pane
        })
        if ($unnamed.Count -gt 0) {
            $types = ($unnamed | ForEach-Object { $_.Current.ControlType.ProgrammaticName }) -join ', '
            throw "$($unnamed.Count) keyboard-focusable controls have no accessible name. Types: $types"
        }
    }
    Write-Host "Windows UI smoke passed: $($navigation.Count) navigation actions, $($buttons.Count) buttons."
} finally {
    if (-not $process.HasExited) { $process.CloseMainWindow() | Out-Null; if (-not $process.WaitForExit(5000)) { $process.Kill() } }
    $process.Dispose()
}
