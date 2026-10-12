param([Parameter(Mandatory)][string]$ApplicationDirectory)

$ErrorActionPreference = "Stop"
$applicationRoot = [IO.Path]::GetFullPath($ApplicationDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)
$approvedTasks = @(
    "ETP Reporting Daily Backup",
    "ETP Reporting Monthly Recovery Drill",
    "ETP Reporting Automated Operations",
    # 1.11.2: the OneDrive tasks, when registered from this installation's scripts\onedrive folder.
    "ETP Send Shop Daily Files",
    "ETP Receive Shop Daily Files",
    "ETP Copy Backup To OneDrive"
)
foreach ($taskName in $approvedTasks) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $ownedByThisInstallation = $task -and @($task.Actions | Where-Object {
        $actionText = "$(($_.Execute)) $(($_.Arguments))"
        $actionText.IndexOf($applicationRoot,[StringComparison]::OrdinalIgnoreCase) -ge 0
    }).Count -gt 0
    if ($ownedByThisInstallation) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
}
Write-Host "Scheduled tasks owned by this ETP Reporting installation were removed. Databases, backups, reports and source files were retained."
