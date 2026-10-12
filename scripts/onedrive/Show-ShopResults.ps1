<#
.SYNOPSIS
  Runs on each shop PC (at logon, and at the start of Send-ShopDailyFiles.ps1). Reads ETP's newest result for this shop from
  OneDrive\ETP-Inbound\<ROLE>\Results\, checks its signature with THIS PC's own signing key, and shows a green or red window:
  "Sheet for 2026-10-11: accepted" or "REFUSED ... Fix: ...". Also writes last-result.txt next to last-send.txt.

.DESCRIPTION
  - A result whose signature does not verify (wrong key, changed text, a manifest signature, a file from another shop) is
    never shown as a verdict: the window says "could not be checked" and tells staff to ask the office.
  - A refused result is shown again at every run until a newer result arrives. A result already shown and not refused is
    shown once (state in <logFolder>\last-result-shown.txt).
  - Only verdicts and the catalogue's plain reasons are in a result: no amounts, no names.
  - Config: shop-daily-files.config.json (the same file as Send-ShopDailyFiles.ps1). Windows PowerShell 5.1 compatible.

.PARAMETER ConfigPath  Override the config location.
.PARAMETER NoWindow    Print to the console only (the hidden daily task uses this; so do the self-tests).
.PARAMETER Force       Show the window even if this result was shown before.

Exit codes: 0 accepted, or no result yet; 2 refused, accepted with a warning, or a result that could not be checked; 1 error.
#>
[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot 'shop-daily-files.config.json'),
    [switch]$NoWindow,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OneDriveCommon.ps1')
. (Join-Path $PSScriptRoot 'ShopResultCommon.ps1')

function Show-EtpResultWindow {
    # A small green, amber or red window. Falls back silently to the console text when no window can be made.
    param($Summary)
    try {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        $colour = switch ($Summary.State) {
            'Accepted' { [Drawing.Color]::FromArgb(0xD9, 0xF2, 0xDD) }
            'Warning' { [Drawing.Color]::FromArgb(0xFF, 0xF1, 0xC9) }
            default { [Drawing.Color]::FromArgb(0xF8, 0xD7, 0xD7) }
        }
        $form = New-Object Windows.Forms.Form
        $form.Text = 'ETP - result of your daily files'
        $form.StartPosition = 'CenterScreen'
        $form.Size = New-Object Drawing.Size(640, 360)
        $form.BackColor = $colour
        $form.TopMost = $true
        $box = New-Object Windows.Forms.TextBox
        $box.Multiline = $true; $box.ReadOnly = $true; $box.ScrollBars = 'Vertical'; $box.BorderStyle = 'None'
        $box.BackColor = $colour; $box.Font = New-Object Drawing.Font('Segoe UI', 11)
        $box.Dock = 'Fill'
        $box.Text = (@($Summary.Headline, '') + @($Summary.Lines)) -join "`r`n"
        $button = New-Object Windows.Forms.Button
        $button.Text = 'OK'; $button.Dock = 'Bottom'; $button.Height = 36
        $button.DialogResult = [Windows.Forms.DialogResult]::OK
        $form.AcceptButton = $button
        $form.Controls.Add($box); $form.Controls.Add($button)
        [void]$form.ShowDialog()
        $form.Dispose()
    }
    catch { Write-Verbose "No window: $($_.Exception.Message)" }
}

function Show-EtpShopResult {
    # Returns 0 or 2 (see the header).
    param([Parameter(Mandatory)][string]$ConfigPath, [bool]$NoWindow, [bool]$Force)
    $context = Get-EtpShopResultsContext -ConfigPath $ConfigPath
    $summary = Update-EtpShopResult -Context $context
    if ($summary.State -ne 'None' -and -not $NoWindow) {
        $shownFile = Join-Path $context.LogFolder 'last-result-shown.txt'
        $marker = "$($summary.File)|$($summary.State)"
        $already = $false
        try { $already = (Test-Path -LiteralPath $shownFile) -and ((Get-Content -Raw -LiteralPath $shownFile).Trim() -eq $marker) } catch { }
        if ($Force -or -not $already -or $summary.State -eq 'Refused') {
            Show-EtpResultWindow -Summary $summary
            try { Write-EtpTextAtomically -Path $shownFile -Text $marker } catch { }
        }
    }
    if (@('None', 'Accepted') -contains $summary.State) { return 0 }
    return 2
}

if ($MyInvocation.InvocationName -ne '.') {
    try { exit (Show-EtpShopResult -ConfigPath $ConfigPath -NoWindow ([bool]$NoWindow) -Force ([bool]$Force)) }
    catch { Write-Error $_.Exception.Message; exit 1 }
}
