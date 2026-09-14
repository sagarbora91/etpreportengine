#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$vmName = 'ETP-Acceptance-186'
try {
    $vm = Get-VM -Name $vmName
    if ($vm.State -eq 'Off' -or $vm.State -eq 'Saved') {
        Start-VM -VM $vm
    } elseif ($vm.State -eq 'Paused') {
        Resume-VM -VM $vm
    } elseif ($vm.State -ne 'Running') {
        throw "VM is in state $($vm.State); no reset or forced action was attempted."
    }
    Start-Process -FilePath "$env:SystemRoot\System32\vmconnect.exe" -ArgumentList @('localhost', $vmName) -WindowStyle Normal
    Write-Host 'Acceptance VM opened. Sign in to its Windows desktop.'
    Write-Host 'Enter the acceptance VM credentials in the connection-worker prompt as well. They remain in memory.'
    & (Join-Path $PSScriptRoot '..\20260912-sprint\Connect-TestVm.ps1')
} catch {
    $_.Exception.Message | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'vm-start-error.txt')
    Write-Host $_.Exception.Message
    Read-Host 'Press Enter to close'
}
