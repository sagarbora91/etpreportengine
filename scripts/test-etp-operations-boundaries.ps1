param(
    [Parameter(Mandatory)]
    [ValidateSet('TargetAliases','BackupReceipts','CertificateCustody','CertificateBinding','Retention','Paths','ProtectedInstall','AtomicReceipts')]
    [string]$Scenario
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'etp-operations-common.ps1')

# This behavioral test harness never calls SQL Server, installs tasks, or touches
# application data. Every file and ACL used below belongs to this unique tree.
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $temporaryParent ('EtpOperationsBoundaries-' + [Guid]::NewGuid().ToString('N'))))
$null = New-Item -ItemType Directory -Path $temporaryRoot
$script:checks = 0
$junctions = @()

function Assert-True {
    param([bool]$Condition,[string]$Message)
    if (-not $Condition) { throw "Test failed: $Message" }
    $script:checks++
}

function Assert-Rejected {
    param([scriptblock]$Action,[string]$MessagePattern)
    $caught = $null
    try { & $Action | Out-Null }
    catch { $caught = $_ }
    Assert-True ($null -ne $caught) 'An unsafe operation was accepted.'
    if ($MessagePattern) { Assert-True ($caught.Exception.Message -match $MessagePattern) "Unexpected rejection: $($caught.Exception.Message)" }
}

function Save-Json {
    param([string]$Path,[object]$Value)
    $Value | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function New-ReceiptFixture {
    $backupDirectory = Join-Path $temporaryRoot 'Backups'
    $null = New-Item -ItemType Directory -Path $backupDirectory -Force
    $backupPath = Join-Path $backupDirectory 'DisposableDatabase-verified.bak'
    [IO.File]::WriteAllText($backupPath, 'Disposable encrypted-backup stand-in; no SQL data.')
    $copies = @()
    foreach ($index in 1..2) {
        $directory = Join-Path $temporaryRoot "RecoveryCopy$index"
        $null = New-Item -ItemType Directory -Path $directory -Force
        $certificatePath = Join-Path $directory 'EtpBackupCert.cer'
        $privateKeyPath = Join-Path $directory 'EtpBackupCert.pvk'
        [IO.File]::WriteAllText($certificatePath, 'Disposable certificate stand-in')
        [IO.File]::WriteAllText($privateKeyPath, 'Disposable protected-private-key stand-in')
        $copies += [ordered]@{
            certificatePath=$certificatePath; privateKeyPath=$privateKeyPath
            certificateSha256=(Get-FileHash -LiteralPath $certificatePath -Algorithm SHA256).Hash
            privateKeySha256=(Get-FileHash -LiteralPath $privateKeyPath -Algorithm SHA256).Hash
        }
    }
    $exportId = [Guid]::NewGuid().ToString('N')
    $thumbprint = 'AB' * 20
    $custodyPath = Join-Path $backupDirectory "certificate-custody-$thumbprint-$exportId.json"
    $custody = [ordered]@{ schemaVersion=2; exportId=$exportId; certificateName='EtpBackupCert'; certificateThumbprint=$thumbprint; copies=$copies }
    Save-Json $custodyPath $custody
    $receipt = [ordered]@{
        schemaVersion=2; verified=$true; serverInstance='.\DisposableInstance'; database='DisposableDatabase'
        encryption='AES_256'; verifiedAtUtc='2026-09-15T12:00:00.0000000Z'
        backupPath=$backupPath; lengthBytes=(Get-Item -LiteralPath $backupPath).Length
        sha256=(Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash
        certificateReceipt=$custodyPath; certificateThumbprint=$custody.certificateThumbprint
    }
    $receiptPath = "$backupPath.receipt.json"
    Save-Json $receiptPath $receipt
    return [pscustomobject]@{ BackupDirectory=$backupDirectory; BackupPath=$backupPath; ReceiptPath=$receiptPath; Receipt=$receipt; CustodyPath=$custodyPath; Custody=$custody }
}

try {
    switch ($Scenario) {
        TargetAliases {
            foreach ($target in @('.', '.\SQLEXPRESS', '(local)\SQLEXPRESS', 'localhost', 'LOCALHOST\INSTANCE', [Environment]::MachineName, ([Environment]::MachineName+'\INSTANCE'), '(localdb)\MSSQLLocalDB', 'lpc:.\SQLEXPRESS', 'np:localhost\INSTANCE', 'np:\\localhost\pipe\sql\query', 'np:\\.\pipe\MSSQL$SQLEXPRESS\sql\query')) {
                Assert-EtpLocalSqlTarget $target 'DisposableDatabase'
                $script:checks++
            }
            foreach ($target in @('', 'remote.example', 'remote\INSTANCE', 'tcp:localhost', 'localhost,1433', 'localhost\INSTANCE;Server=remote', 'np:\\remote\pipe\sql\query', '\\remote\pipe\sql\query', '(localdb)', 'np:\\(localdb)\pipe\sql\query', 'lpc:\\localhost\pipe\sql\query', '\\localhost\pipe\sql\query')) {
                Assert-Rejected { Assert-EtpLocalSqlTarget $target 'DisposableDatabase' } 'on this computer'
            }
            foreach ($database in @('', 'database;DROP DATABASE anything', '../database', 'database-name', ('a' * 129))) {
                Assert-Rejected { Assert-EtpLocalSqlTarget '.\SQLEXPRESS' $database } 'valid database name'
            }
        }
        BackupReceipts {
            $fixture = New-ReceiptFixture
            $read = Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase'
            Assert-True ($read.backupPath -ceq $fixture.BackupPath) 'A valid receipt did not preserve its exact backup path.'
            Assert-Rejected { Read-EtpVerifiedReceipt (Join-Path $fixture.BackupDirectory 'missing.json') $fixture.BackupDirectory 'DisposableDatabase' } ''
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'AnotherDatabase' } 'verified backup receipt'
            [IO.File]::AppendAllText($fixture.BackupPath, 'tampered')
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'receipt and file differ'
            $fixture.Receipt.lengthBytes = (Get-Item -LiteralPath $fixture.BackupPath).Length
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'receipt and file differ'
            $fixture.Receipt.sha256 = (Get-FileHash -LiteralPath $fixture.BackupPath -Algorithm SHA256).Hash
            $fixture.Receipt.verified = $false
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'verified backup receipt'
            $fixture.Receipt.verified = $true
            # D9 revised: an unencrypted receipt is valid, because SQL Express cannot
            # encrypt a backup at all. But flipping the field on a receipt that still
            # carries custody details is a downgrade, and must be refused -- otherwise
            # editing one word skips the whole certificate chain.
            $fixture.Receipt.encryption = 'NONE'
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'must not carry certificate custody'
            # A genuine unencrypted receipt carries no certificate at all, and is accepted.
            $encryptedReceipt = $fixture.Receipt.certificateReceipt
            $encryptedThumb = $fixture.Receipt.certificateThumbprint
            $fixture.Receipt.certificateReceipt = $null
            $fixture.Receipt.certificateThumbprint = $null
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            $unencrypted = Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase'
            Assert-True ($unencrypted.encryption -ceq 'NONE') 'A genuine unencrypted receipt should be readable once D9 was revised.'
            $fixture.Receipt.certificateReceipt = $encryptedReceipt
            $fixture.Receipt.certificateThumbprint = $encryptedThumb
            # Anything this build did not write is still refused.
            $fixture.Receipt.encryption = 'AES_128'
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'verified backup receipt'
            $fixture.Receipt.encryption = 'AES_256'
            Save-Json $fixture.ReceiptPath $fixture.Receipt
        }
        CertificateCustody {
            $fixture = New-ReceiptFixture
            Assert-EtpCertificateCustody $fixture.CustodyPath -RequireAvailable
            $script:checks++
            Assert-Rejected { Assert-EtpCertificateCustody (Join-Path $temporaryRoot 'missing-custody.json') -RequireAvailable } 'Export the backup certificate'
            $keyPath = $fixture.Custody.copies[1].privateKeyPath
            $keyContents = [IO.File]::ReadAllText($keyPath)
            [IO.File]::WriteAllText($keyPath, 'changed key')
            Assert-Rejected { Assert-EtpCertificateCustody $fixture.CustodyPath -RequireAvailable } 'missing or has changed'
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'missing or has changed'
            # Retention can examine an authentic backup while recovery media is offline.
            $null = Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' -SkipCertificateCheck
            $script:checks++
            [IO.File]::WriteAllText($keyPath, $keyContents)
            Remove-Item -LiteralPath $fixture.Custody.copies[0].certificatePath
            Assert-Rejected { Assert-EtpCertificateCustody $fixture.CustodyPath -RequireAvailable } 'missing or has changed'
            [IO.File]::WriteAllText($fixture.Custody.copies[0].certificatePath, 'Disposable certificate stand-in')
            $fixture.Custody.copies[1].privateKeyPath = $fixture.Custody.copies[0].privateKeyPath
            Save-Json $fixture.CustodyPath $fixture.Custody
            Assert-Rejected { Assert-EtpCertificateCustody $fixture.CustodyPath -RequireAvailable } 'distinct certificate recovery locations'
            $fixture.Custody.copies[1].privateKeyPath = $keyPath
            $fixture.Custody.copies[1].certificatePath = Join-Path (Split-Path $fixture.Custody.copies[0].certificatePath) '.\EtpBackupCert.cer'
            Save-Json $fixture.CustodyPath $fixture.Custody
            Assert-Rejected { Assert-EtpCertificateCustody $fixture.CustodyPath -RequireAvailable } 'distinct certificate recovery locations'
        }
        CertificateBinding {
            # Dot-sourcing exposes only the resolver; invalid connection settings
            # ensure an accidental fall-through would fail before any SQL call.
            . (Join-Path $PSScriptRoot 'backup-etp-database.ps1') -ServerInstance 'NoDatabaseConnectionForThisTest.invalid' -Database 'DisposableDatabase' -BackupDirectory $temporaryRoot
            $fixture = New-ReceiptFixture
            $null = Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase'
            $script:checks++
            $latestPointer = Join-Path $fixture.BackupDirectory 'certificate-custody.json'
            # No pointer at all is the state of a machine whose recovery keys have never been
            # exported. Until 25 September 2026 it surfaced as "Cannot find path ...", which
            # stopped setup on Developer Edition with nothing an owner could act on.
            Assert-True (-not (Test-Path -LiteralPath $latestPointer)) 'The fixture already had a latest custody pointer.'
            Assert-Rejected { Resolve-EtpLatestCertificateCustody $fixture.BackupDirectory } 'Encrypted backup recovery keys'
            Save-Json $latestPointer @{ schemaVersion=2; certificateThumbprint=$fixture.Custody.certificateThumbprint; immutableReceiptPath=$fixture.CustodyPath }
            Assert-True ((Resolve-EtpLatestCertificateCustody $fixture.BackupDirectory) -ceq $fixture.CustodyPath) 'The latest pointer did not select immutable certificate A.'
            $otherThumbprint = 'CD' * 20
            $otherExportId = [Guid]::NewGuid().ToString('N')
            $otherPath = Join-Path $fixture.BackupDirectory "certificate-custody-$otherThumbprint-$otherExportId.json"
            $otherCopies = @()
            foreach ($index in 1..2) {
                $directory = Join-Path $temporaryRoot "OtherCertificateCopy$index"
                $null = New-Item -ItemType Directory -Path $directory
                $certificatePath = Join-Path $directory 'OtherCertificate.cer'
                $privateKeyPath = Join-Path $directory 'OtherCertificate.pvk'
                [IO.File]::WriteAllText($certificatePath, 'Different certificate stand-in')
                [IO.File]::WriteAllText($privateKeyPath, 'Different private-key stand-in')
                $otherCopies += [ordered]@{
                    certificatePath=$certificatePath; privateKeyPath=$privateKeyPath
                    certificateSha256=(Get-FileHash -LiteralPath $certificatePath -Algorithm SHA256).Hash
                    privateKeySha256=(Get-FileHash -LiteralPath $privateKeyPath -Algorithm SHA256).Hash
                }
            }
            Save-Json $otherPath @{ schemaVersion=2; exportId=$otherExportId; certificateName='EtpBackupCert'; certificateThumbprint=$otherThumbprint; copies=$otherCopies }
            Assert-EtpCertificateCustody $otherPath -RequireAvailable -ExpectedThumbprint $otherThumbprint
            $script:checks++
            Save-Json $latestPointer @{ schemaVersion=2; certificateThumbprint=$otherThumbprint; immutableReceiptPath=$otherPath }
            Assert-True ((Resolve-EtpLatestCertificateCustody $fixture.BackupDirectory) -ceq $otherPath) 'The rotated latest pointer did not select immutable certificate B.'
            Save-Json $latestPointer @{ schemaVersion=2; certificateThumbprint=$fixture.Custody.certificateThumbprint; immutableReceiptPath=$otherPath }
            Assert-Rejected { Resolve-EtpLatestCertificateCustody $fixture.BackupDirectory } 'does not match its custody receipt'
            $outsideCustody = Join-Path $temporaryRoot ([IO.Path]::GetFileName($otherPath))
            Copy-Item -LiteralPath $otherPath -Destination $outsideCustody
            Save-Json $latestPointer @{ schemaVersion=2; certificateThumbprint=$otherThumbprint; immutableReceiptPath=$outsideCustody }
            Assert-Rejected { Resolve-EtpLatestCertificateCustody $fixture.BackupDirectory } 'in this backup folder'
            Save-Json $latestPointer @{ schemaVersion=2; certificateThumbprint=$otherThumbprint; immutableReceiptPath=$latestPointer }
            Assert-Rejected { Resolve-EtpLatestCertificateCustody $fixture.BackupDirectory } 'in this backup folder'
            $mutableCustody = Join-Path $fixture.BackupDirectory 'mutable-custody.json'
            Copy-Item -LiteralPath $otherPath -Destination $mutableCustody
            Save-Json $latestPointer @{ schemaVersion=2; certificateThumbprint=$otherThumbprint; immutableReceiptPath=$mutableCustody }
            Assert-Rejected { Resolve-EtpLatestCertificateCustody $fixture.BackupDirectory } 'immutable certificate-specific custody receipt'
            # Both B copies are present and authentic to B, but cannot authorize A.
            $fixture.Receipt.certificateReceipt = $otherPath
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'does not match its custody receipt'
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' -SkipCertificateCheck } 'does not match its custody receipt'
            # Changing the latest pointer does not change which key A requires.
            Save-Json $latestPointer @{ schemaVersion=2; certificateThumbprint=$otherThumbprint; immutableReceiptPath=$otherPath }
            $fixture.Receipt.certificateReceipt = $fixture.CustodyPath
            $fixture.Receipt.certificateThumbprint = $fixture.Custody.certificateThumbprint.ToLowerInvariant()
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            $null = Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase'
            $script:checks++
            Remove-Item -LiteralPath $fixture.Custody.copies[0].privateKeyPath
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'missing or has changed'
            $null = Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' -SkipCertificateCheck
            $script:checks++
            $fixture.Receipt.certificateReceipt = $latestPointer
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' -SkipCertificateCheck } 'immutable certificate-specific custody receipt'
            $fixture.Receipt.certificateReceipt = $fixture.CustodyPath
            $fixture.Receipt.certificateThumbprint = ('A' * 41)
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' -SkipCertificateCheck } 'thumbprint is invalid'
            $fixture.Receipt.certificateThumbprint = $fixture.Custody.certificateThumbprint
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            $fixture.Custody.schemaVersion = 1
            Save-Json $fixture.CustodyPath $fixture.Custody
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' -SkipCertificateCheck } 'immutable certificate-specific custody receipt'
        }
        Retention {
            $receipts = @()
            $latest = [datetime]::SpecifyKind([datetime]'2026-09-15T18:00:00', [DateTimeKind]::Utc)
            foreach ($day in 0..39) {
                foreach ($hour in 0..1) {
                    $date = $latest.AddDays(-$day).AddHours(-$hour)
                    $receipts += [pscustomobject]@{ backupPath="daily-$day-$hour.bak"; verifiedAtUtc=$date.ToString('o') }
                }
            }
            foreach ($month in 1..14) {
                $receipts += [pscustomobject]@{ backupPath="monthly-$month.bak"; verifiedAtUtc=$latest.AddMonths(-$month).ToString('o') }
            }
            $kept = @(Get-EtpRetainedBackupReceipts $receipts)
            $names = @($kept | ForEach-Object backupPath)
            foreach ($day in 0..13) { Assert-True ($names -contains "daily-$day-0.bak") "Daily recovery point $day was not retained." }
            foreach ($day in 0..39) { Assert-True ($names -notcontains "daily-$day-1.bak") 'An older backup from the same day was retained.' }
            foreach ($month in 2..11) { Assert-True ($names -contains "monthly-$month.bak") "Monthly recovery point $month was not retained." }
            Assert-True ($names -contains 'daily-15-0.bak') 'The newest August backup was not retained as its monthly recovery point.'
            Assert-True ($names -notcontains 'monthly-1.bak') 'An older August backup was retained instead of its newest recovery point.'
            Assert-True ($names -notcontains 'monthly-12.bak') 'More than twelve calendar months were retained.'
            Assert-True ($kept.Count -eq 25) 'Retention did not produce the union of fourteen daily and twelve monthly recovery points.'
            Assert-True (@(Get-EtpRetainedBackupReceipts @()).Count -eq 0) 'Empty retention input produced a backup.'
            $offsetReceipts = @(
                [pscustomobject]@{ backupPath='utc-evening.bak'; verifiedAtUtc='2026-09-15T23:00:00Z' },
                [pscustomobject]@{ backupPath='same-utc-day.bak'; verifiedAtUtc='2026-09-16T00:30:00+02:00' }
            )
            $offsetKept = @(Get-EtpRetainedBackupReceipts $offsetReceipts)
            Assert-True ($offsetKept.Count -eq 1 -and $offsetKept[0].backupPath -eq 'utc-evening.bak') 'Daily retention did not group timestamps by UTC day.'
            $utcDayReceipts = @(
                [pscustomobject]@{ backupPath='before-local-midnight.bak'; verifiedAtUtc='2026-09-15T18:00:00Z' },
                [pscustomobject]@{ backupPath='after-local-midnight.bak'; verifiedAtUtc='2026-09-15T19:00:00Z' }
            )
            $utcDayKept = @(Get-EtpRetainedBackupReceipts $utcDayReceipts)
            Assert-True ($utcDayKept.Count -eq 1 -and $utcDayKept[0].backupPath -eq 'after-local-midnight.bak') 'A local clock boundary split one UTC recovery day.'

            # The live case, 24 September 2026: setup took its pre-migration backup at 08:50,
            # the owner took a daily backup at 10:22, and the pre-migration file was gone.
            $upgradeDayReceipts = @(
                [pscustomobject]@{ backupPath='pre-migration.bak'; verifiedAtUtc='2026-09-24T08:50:14Z'; purpose='PRE_MIGRATION' },
                [pscustomobject]@{ backupPath='same-day-daily.bak'; verifiedAtUtc='2026-09-24T10:22:39Z'; purpose='SCHEDULED' },
                [pscustomobject]@{ backupPath='earlier-same-day-daily.bak'; verifiedAtUtc='2026-09-24T07:10:00Z'; purpose='SCHEDULED' }
            )
            $upgradeDayKept = @(Get-EtpRetainedBackupReceipts $upgradeDayReceipts | ForEach-Object backupPath)
            Assert-True ($upgradeDayKept -contains 'pre-migration.bak') 'The upgrade backup was deleted by the same day rotation.'
            # It must not take the day's slot either, or taking one would cost a daily point.
            Assert-True ($upgradeDayKept -contains 'same-day-daily.bak') 'The pre-migration backup consumed the daily recovery point.'
            Assert-True ($upgradeDayKept -notcontains 'earlier-same-day-daily.bak') 'An older scheduled backup from the same day was retained.'

            # Age never reaches it: this one is outside both the 14-day and 12-month windows.
            $agedReceipts = @([pscustomobject]@{ backupPath='old-pre-migration.bak'; verifiedAtUtc='2024-01-02T03:04:05Z'; purpose='PRE_MIGRATION' })
            foreach ($day in 0..29) {
                $agedReceipts += [pscustomobject]@{ backupPath="aged-daily-$day.bak"; verifiedAtUtc=$latest.AddDays(-$day).ToString('o'); purpose='SCHEDULED' }
            }
            $agedKept = @(Get-EtpRetainedBackupReceipts $agedReceipts | ForEach-Object backupPath)
            Assert-True ($agedKept -contains 'old-pre-migration.bak') 'An aged pre-migration backup was rotated out.'
            Assert-True ($agedKept -notcontains 'aged-daily-20.bak') 'A scheduled backup outside the retention windows was kept.'

            # A purpose this build cannot interpret is kept rather than deleted: the cost of
            # keeping a file is disk, the cost of deleting the wrong one is the database.
            $unknownKept = @(Get-EtpRetainedBackupReceipts @(
                [pscustomobject]@{ backupPath='written-by-a-later-build.bak'; verifiedAtUtc='2026-09-01T01:00:00Z'; purpose='SOMETHING_ELSE' },
                [pscustomobject]@{ backupPath='newer-scheduled.bak'; verifiedAtUtc='2026-09-01T02:00:00Z'; purpose='SCHEDULED' }
            ) | ForEach-Object backupPath)
            Assert-True ($unknownKept -contains 'written-by-a-later-build.bak') 'A backup with an unreadable purpose was deleted.'
            Assert-True ($unknownKept -contains 'newer-scheduled.bak') 'The unreadable purpose consumed the daily recovery point.'

            # The comparison is exact and case-sensitive, like every other receipt field this
            # module trusts. 'Scheduled' is therefore NOT something this build wrote, and is
            # kept. Pinned because the consequence of relaxing it is a deleted database and
            # the consequence of keeping it is one extra file: if the writer's spelling ever
            # changes, this fails here rather than turning every backup permanent in silence.
            $casingKept = @(Get-EtpRetainedBackupReceipts @(
                [pscustomobject]@{ backupPath='wrong-casing.bak'; verifiedAtUtc='2026-09-02T01:00:00Z'; purpose='Scheduled' },
                [pscustomobject]@{ backupPath='padded.bak'; verifiedAtUtc='2026-09-02T02:00:00Z'; purpose=' SCHEDULED ' },
                [pscustomobject]@{ backupPath='exact.bak'; verifiedAtUtc='2026-09-02T03:00:00Z'; purpose='SCHEDULED' }
            ) | ForEach-Object backupPath)
            Assert-True ($casingKept -contains 'wrong-casing.bak' -and $casingKept -contains 'padded.bak') 'A purpose this build does not write was treated as an ordinary backup.'
            Assert-True ($casingKept -contains 'exact.bak') 'The exact spelling lost its daily recovery point.'
        }
        Paths {
            $fixture = New-ReceiptFixture
            Assert-EtpNoLinks (Join-Path $fixture.BackupDirectory 'does-not-exist-yet.json')
            $script:checks++
            $outsideDirectory = Join-Path $temporaryRoot 'BackupsSibling'
            $null = New-Item -ItemType Directory -Path $outsideDirectory
            $outsideBackup = Join-Path $outsideDirectory 'outside.bak'
            Copy-Item -LiteralPath $fixture.BackupPath -Destination $outsideBackup
            foreach ($escaped in @($outsideBackup, (Join-Path $fixture.BackupDirectory '..\BackupsSibling\outside.bak'))) {
                $fixture.Receipt.backupPath = $escaped
                Save-Json $fixture.ReceiptPath $fixture.Receipt
                Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $fixture.BackupDirectory 'DisposableDatabase' } 'outside the backup folder'
            }
            $junction = Join-Path $temporaryRoot 'BackupLink'
            $null = New-Item -ItemType Junction -Path $junction -Target $fixture.BackupDirectory
            $junctions += $junction
            Assert-Rejected { Assert-EtpNoLinks (Join-Path $junction 'missing-file.json') } 'Linked operation paths'
            $fixture.Receipt.backupPath = Join-Path $junction (Split-Path $fixture.BackupPath -Leaf)
            Save-Json $fixture.ReceiptPath $fixture.Receipt
            Assert-Rejected { Read-EtpVerifiedReceipt $fixture.ReceiptPath $junction 'DisposableDatabase' } 'Linked operation paths'
            Assert-Rejected { Read-EtpVerifiedReceipt (Join-Path $junction (Split-Path $fixture.ReceiptPath -Leaf)) $fixture.BackupDirectory 'DisposableDatabase' } 'Linked operation paths'
        }
        ProtectedInstall {
            $scriptPath = Join-Path $temporaryRoot 'user-owned-operation.ps1'
            [IO.File]::WriteAllText($scriptPath, '# disposable test file')
            $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().User
            $acl = Get-Acl -LiteralPath $scriptPath
            $acl.SetOwner($currentUser)
            Set-Acl -LiteralPath $scriptPath -AclObject $acl
            Assert-True ((Get-Acl -LiteralPath $scriptPath).GetOwner([Security.Principal.SecurityIdentifier]).Value -eq $currentUser.Value) 'The disposable script is not user owned.'
            Assert-Rejected { Assert-EtpProtectedInstall $scriptPath } 'owned by Administrators or SYSTEM'
            Assert-Rejected { Resolve-EtpSqlCmd $scriptPath } 'owned by Administrators or SYSTEM'
            Assert-True ([IO.File]::ReadAllText($scriptPath) -eq '# disposable test file') 'Install validation changed the rejected script.'
        }
        AtomicReceipts {
            $path = Join-Path $temporaryRoot 'atomic-receipt.json'
            Write-EtpJsonAtomically $path @{ generation='original' }
            Assert-Rejected { Write-EtpJsonAtomically $path @{ generation='unexpected' } } ''
            Assert-True ((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).generation -eq 'original') 'Existing receipt was replaced without authorization.'
            Write-EtpJsonAtomically $path @{ generation='replacement' } -Replace
            Assert-True ((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).generation -eq 'replacement') 'Atomic receipt replacement failed.'
            Assert-True (@(Get-ChildItem -LiteralPath $temporaryRoot -Filter '*.tmp').Count -eq 0) 'A temporary receipt was left behind.'
        }
    }
    Write-Output "Operations boundary scenario succeeded: $Scenario ($script:checks checks)."
}
finally {
    # Resolve and check every deletion target before removing this disposable tree.
    $resolvedRoot = [IO.Path]::GetFullPath($temporaryRoot)
    if (-not $resolvedRoot.StartsWith($temporaryParent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetDirectoryName($resolvedRoot)+'\' -ine $temporaryParent -or [IO.Path]::GetFileName($resolvedRoot) -notmatch '^EtpOperationsBoundaries-[a-f0-9]{32}$') {
        throw 'Refusing to clean an unexpected test directory.'
    }
    foreach ($junction in $junctions) {
        $resolvedJunction = [IO.Path]::GetFullPath($junction)
        if (-not $resolvedJunction.StartsWith($resolvedRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean an unexpected test junction.' }
        # Non-recursive Directory.Delete removes this junction, never its target.
        [IO.Directory]::Delete($resolvedJunction)
    }
    Assert-EtpNoLinks $resolvedRoot
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
