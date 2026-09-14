param($Session, $EvidenceRoot)
$ErrorActionPreference='Stop'
$result=Invoke-Command -Session $Session -ScriptBlock {
    $root='C:\ETPAcceptance\UI-20260913-baseline'
    $receipt=Get-Content -LiteralPath "$root\receipt.json" -Raw | ConvertFrom-Json
    if(!$receipt.backupVerified -or (Get-FileHash -LiteralPath $receipt.backupPath).Hash -ne $receipt.backupSha256){throw 'Protected backup checksum mismatch'}
    $processes=@(Get-CimInstance Win32_Process -Filter "Name='Etp.Reporting.Desktop.exe'" | Select-Object ProcessId,ExecutablePath,SessionId)
    ConvertTo-Json -InputObject $processes | Set-Content "$root\processes.json"
    [pscustomobject]@{backupVerified=$true;noEtpProcess=($processes.Count -eq 0);timestampUtc=[DateTime]::UtcNow.ToString('o')}
}
$localRoot=Join-Path $EvidenceRoot '..\20260913-acceptance-resume'
foreach($file in @('processes.json','backup-verification.txt')){Copy-Item -FromSession $Session -LiteralPath "C:\ETPAcceptance\UI-20260913-baseline\$file" -Destination (Join-Path $localRoot $file)}
$result | ConvertTo-Json | Set-Content (Join-Path $localRoot 'baseline-completion.json')
