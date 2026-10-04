using System.Diagnostics;
using System.Security.Cryptography;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// restore-etp-database.ps1 makes a backup from another PC this PC's live database. None of
/// this needs SQL Server: its refusals, its parsing of what SQL Server reports, and the exact
/// SQL it would run are checked here; the SQL itself is proven only on a real restore.
/// </summary>
public sealed class RestoreDatabaseScriptTests
{
    [Fact]
    public async Task Restore_without_elevation_or_configuration_changes_nothing()
    {
        var root = NewRoot();
        try
        {
            var programData = Directory.CreateDirectory(Path.Combine(root, "ProgramData")).FullName;
            var backup = Path.Combine(root, "old-pc.bak");
            await File.WriteAllTextAsync(backup, "Disposable backup stand-in");
            var before = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(backup)));
            var result = await RunPowerShellAsync(["-File", FindScript("restore-etp-database.ps1"), "-BackupPath", backup], programData);
            Assert.NotEqual(0, result.ExitCode);
            // Unelevated it stops at the elevation check; elevated, at the missing configuration.
            Assert.Matches("elevated|administrator|operations\\.json", result.Output);
            Assert.Contains("Restore stopped:", result.Output);
            Assert.Empty(Directory.EnumerateFileSystemEntries(programData, "*", SearchOption.AllDirectories));
            Assert.Equal(before, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(backup))));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Backup_path_must_be_a_local_bak_file_outside_the_etp_folders()
    {
        var root = NewRoot();
        var link = Path.Combine(root, "link");
        try
        {
            var programData = Directory.CreateDirectory(Path.Combine(root, "ProgramData")).FullName;
            var insideEtp = Directory.CreateDirectory(Path.Combine(programData, "EtpReporting", "Backups")).FullName;
            await File.WriteAllTextAsync(Path.Combine(insideEtp, "x.bak"), "Disposable backup stand-in");
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            await File.WriteAllTextAsync(Path.Combine(target, "x.bak"), "Disposable backup stand-in");
            await File.WriteAllTextAsync(Path.Combine(root, "real.bak"), "Disposable backup stand-in");
            await File.WriteAllTextAsync(Path.Combine(root, "x.txt"), "Disposable backup stand-in");
            var r = root.Replace("'", "''");
            var command = $$"""
                {{Preamble()}}
                $root = '{{r}}'
                $null = New-Item -ItemType Junction -Path (Join-Path $root 'link') -Target (Join-Path $root 'target')
                try {
                    Assert-Refused { Assert-EtpRestoreBackupPath '' } 'full path'
                    Assert-Refused { Assert-EtpRestoreBackupPath 'relative\x.bak' } 'full path'
                    Assert-Refused { Assert-EtpRestoreBackupPath 'C:x.bak' } 'full path'
                    Assert-Refused { Assert-EtpRestoreBackupPath '\\server\share\x.bak' } 'onto this PC'
                    Assert-Refused { Assert-EtpRestoreBackupPath '\\?\C:\x.bak' } 'onto this PC'
                    Assert-Refused { Assert-EtpRestoreBackupPath (Join-Path $root 'x.txt') } 'ending in \.bak'
                    Assert-Refused { Assert-EtpRestoreBackupPath (Join-Path $root 'missing.bak') } 'not found'
                    Assert-Refused { Assert-EtpRestoreBackupPath (Join-Path $env:ProgramData 'EtpReporting\Backups\x.bak') } 'outside this PC''s ETP folders'
                    Assert-Refused { Assert-EtpRestoreBackupPath (Join-Path $root 'link\x.bak') } 'Linked operation paths'
                    $accepted = Assert-EtpRestoreBackupPath (Join-Path $root 'real.bak')
                    if ($accepted -cne (Join-Path $root 'real.bak')) { throw "A local backup was not accepted as itself: $accepted" }
                }
                finally { [IO.Directory]::Delete((Join-Path $root 'link')) }
                Write-Output 'Backup path checks passed.'
                """;
            var result = await RunPowerShellAsync(["-Command", command], programData);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Backup path checks passed.", result.Output);
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Receipt_must_match_the_backup()
    {
        var root = NewRoot();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "old-pc.bak"), "Disposable backup stand-in");
            var command = $$"""
                {{Preamble()}}
                $root = '{{root.Replace("'", "''")}}'
                $bak = Join-Path $root 'old-pc.bak'
                $hash = (Get-FileHash -LiteralPath $bak -Algorithm SHA256).Hash
                $length = (Get-Item -LiteralPath $bak).Length
                # A receipt as the old PC wrote it, naming that PC's folder, with fields this helper ignores.
                $good = @{ schemaVersion = 2; database = 'EtpReporting'; sha256 = $hash.ToLowerInvariant(); lengthBytes = $length; verified = $true; encryption = 'NONE'; purpose = 'SCHEDULED'
                    backupPath = 'C:\ProgramData\EtpReporting\Backups\EtpReporting-20260925-071854-3848d234ef50450f912ee1f5440db719.bak'; serverInstance = '.\SQLEXPRESS' }
                function New-TestReceipt([hashtable]$Change = @{}, [string[]]$Remove = @()) {
                    $copy = @{}
                    foreach ($key in $good.Keys) { $copy[$key] = $good[$key] }
                    foreach ($key in $Change.Keys) { $copy[$key] = $Change[$key] }
                    foreach ($key in $Remove) { $copy.Remove($key) }
                    $path = Join-Path $root ('receipt-' + [Guid]::NewGuid().ToString('N') + '.json')
                    [pscustomobject]$copy | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding utf8
                    return $path
                }
                function Test-Receipt([string]$Path) { Assert-EtpRestoreReceipt -ReceiptPath $Path -Database 'EtpReporting' -Sha256 $hash -LengthBytes $length }
                Test-Receipt (New-TestReceipt)
                Assert-Refused { Test-Receipt (New-TestReceipt @{ sha256 = ('0' * 64) }) } 'SHA-256 differs'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ sha256 = 'not-a-hash' }) } 'no valid SHA-256'
                Assert-Refused { Test-Receipt (New-TestReceipt -Remove @('sha256')) } 'no valid SHA-256'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ lengthBytes = $length + 1 }) } 'size differs'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ database = 'Other' }) } 'another database'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ database = 'etpreporting' }) } 'another database'
                # Decision 24: a staging copy's receipt names the staging database, and is returned
                # so the main flow can hold it against the backup header.
                if ((Test-Receipt (New-TestReceipt)) -cne 'EtpReporting') { throw 'The receipt database was not returned.' }
                if ((Test-Receipt (New-TestReceipt @{ database = 'EtpStaging_20261004' })) -cne 'EtpStaging_20261004') { throw 'A staging receipt was not accepted.' }
                if ((Test-Receipt (New-TestReceipt @{ database = 'EtpAccept_Staging_20261004' })) -cne 'EtpAccept_Staging_20261004') { throw 'An EtpAccept_ receipt was not accepted.' }
                if ($null -ne (Test-Receipt (New-TestReceipt -Remove @('database')))) { throw 'A receipt without a database named one.' }
                Assert-Refused { Test-Receipt (New-TestReceipt @{ database = 'OtherDb' }) } 'another database'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ database = 'etpstaging_20261004' }) } 'another database'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ database = 'EtpStaging_' }) } 'another database'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ database = 'EtpReportingHelios' }) } 'another database'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ verified = $false }) } 'verified backup'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ verified = 'true' }) } 'verified backup'
                Assert-Refused { Test-Receipt (New-TestReceipt @{ encryption = 'AES_256' }) } 'encrypted'
                Assert-Refused { Test-Receipt (Join-Path $root 'missing.json') } 'receipt file was not found'
                Set-Content -LiteralPath (Join-Path $root 'broken.json') -Value '{ not json'
                Assert-Refused { Test-Receipt (Join-Path $root 'broken.json') } 'not a backup receipt'
                Write-Output 'Receipt checks passed.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Receipt checks passed.", result.Output);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Backup_must_be_a_single_full_backup_of_the_configured_database()
    {
        var command = $$"""
            {{Preamble()}}
            # RESTORE HEADERONLY as sqlcmd prints it: BackupName, BackupDescription, BackupType,
            # ExpirationDate, Compressed, Position, DeviceType, UserName, ServerName, DatabaseName, ...
            function Header([string]$Type = '1', [string]$Position = '1', [string]$Name = 'EtpReporting') {
                @('NULL', 'NULL', $Type, 'NULL', '0', $Position, '2', 'OLDPC\Owner', 'OLDPC\SQLEXPRESS', $Name, '957', '2026-09-26 18:47:26.000') -join '|'
            }
            $header = ConvertFrom-EtpRestoreHeader -Lines @('', (Header), '') -Database 'EtpReporting'
            if ($header.DatabaseName -cne 'EtpReporting' -or $header.IsStagingSource) { throw 'The configured database was not recognised.' }
            # Decision 24: a staging copy prepared on another PC is accepted, and marked as one.
            foreach ($staging in @('EtpStaging_20261004', 'EtpAccept_Staging_20261004', 'EtpAccept_P3')) {
                $header = ConvertFrom-EtpRestoreHeader -Lines @((Header -Name $staging)) -Database 'EtpReporting'
                if ($header.DatabaseName -cne $staging -or -not $header.IsStagingSource) { throw "The staging backup $staging was not accepted as one." }
            }
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Name 'OtherDb')) -Database 'EtpReporting' } "database 'OtherDb', not EtpReporting"
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Name 'EtpStaging_')) -Database 'EtpReporting' } "database 'EtpStaging_', not EtpReporting"
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Name 'etpstaging_20261004')) -Database 'EtpReporting' } 'not EtpReporting'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Name 'EtpStaging_2026-10-04')) -Database 'EtpReporting' } 'not EtpReporting'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Name ('EtpStaging_' + ('x' * 120)))) -Database 'EtpReporting' } 'not EtpReporting'
            # The staging allowance never loosens the single-full-backup checks.
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Type '2' -Name 'EtpStaging_20261004')) -Database 'EtpReporting' } 'not a full database backup'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Position '2' -Name 'EtpStaging_20261004')) -Database 'EtpReporting' } 'more than one backup'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header), (Header -Position '2')) -Database 'EtpReporting' } 'more than one backup'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Position '2')) -Database 'EtpReporting' } 'more than one backup'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Type '2')) -Database 'EtpReporting' } 'not a full database backup'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Type '5')) -Database 'EtpReporting' } 'not a full database backup'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @((Header -Name 'EtpReportingHelios')) -Database 'EtpReporting' } "database 'EtpReportingHelios', not EtpReporting"
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @('NULL|NULL|1|NULL|0|1|2|OLDPC\Owner|OLDPC\SQLEXPRESS') -Database 'EtpReporting' } 'does not recognise'
            Assert-Refused { ConvertFrom-EtpRestoreHeader -Lines @() -Database 'EtpReporting' } 'did not describe'
            Write-Output 'Backup header checks passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Backup header checks passed.", result.Output);
    }

    [Fact]
    public async Task A_staging_backup_is_restored_as_the_configured_database()
    {
        // Decision 24 (4 October 2026): the shop PC restores a backup of EtpStaging_yyyyMMdd (or
        // EtpAccept_*) taken on Workpc as EtpReporting. The restore SQL always names the
        // configured database and its own data and log files; the logical names come from the
        // backup, whichever database they were first created as.
        var script = FindScript("restore-etp-database.ps1").Replace("'", "''");
        var command = $$"""
            {{Preamble()}}
            foreach ($name in @('EtpStaging_20261004', 'EtpAccept_Staging_20261004')) { if (-not (Test-EtpRestoreStagingSource $name)) { throw "$name is not a staging source." } }
            foreach ($name in @('EtpReporting', 'OtherDb', 'EtpReportingHelios', 'EtpPhase1Test_X', 'EtpStaging_', 'etpstaging_x', 'EtpStaging_a;b', '', $null)) { if (Test-EtpRestoreStagingSource $name) { throw "'$name' was taken for a staging source." } }

            Assert-EtpRestoreReceiptMatchesHeader -ReceiptDatabase 'EtpStaging_20261004' -HeaderDatabase 'EtpStaging_20261004'
            Assert-EtpRestoreReceiptMatchesHeader -ReceiptDatabase 'EtpReporting' -HeaderDatabase 'EtpReporting'
            Assert-EtpRestoreReceiptMatchesHeader -ReceiptDatabase $null -HeaderDatabase 'EtpStaging_20261004'
            Assert-Refused { Assert-EtpRestoreReceiptMatchesHeader -ReceiptDatabase 'EtpStaging_20261003' -HeaderDatabase 'EtpStaging_20261004' } "receipt is for database 'EtpStaging_20261003', but this file is a backup of database 'EtpStaging_20261004'"
            Assert-Refused { Assert-EtpRestoreReceiptMatchesHeader -ReceiptDatabase 'EtpReporting' -HeaderDatabase 'EtpStaging_20261004' } 'Nothing was restored'
            Assert-Refused { Assert-EtpRestoreReceiptMatchesHeader -ReceiptDatabase 'EtpStaging_20261004' -HeaderDatabase 'EtpReporting' } 'Nothing was restored'

            # Logical names kept from EtpReporting (the staging copy was itself restored from an
            # EtpReporting backup), and logical names of a database created under the staging name.
            foreach ($pair in @(@('EtpReporting', 'EtpReporting_log'), @('EtpStaging_20261004', 'EtpStaging_20261004_log'))) {
                Write-Output '---SQL---'
                New-EtpRestoreDatabaseSql -Database 'EtpReporting' -BackupFile 'C:\PD\restore-source.bak' -DataLogicalName $pair[0] -LogLogicalName $pair[1]
                Write-Output '---END---'
            }

            # The main flow: the header (not the configuration) decides staging, the receipt is held
            # against it, the staging line is logged, and the restore names the configured database.
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'restore-etp-database.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            # The leading comma keeps a one-element result an array (StrictMode has no .Count on a scalar).
            function Find-Top([string]$Pattern) { ,@($top | Where-Object { $_ -isnot [System.Management.Automation.Language.FunctionDefinitionAst] -and $_.Extent.Text -match $Pattern }) }
            $receiptBlock = Find-Top '^if \(-not \[string\]::IsNullOrWhiteSpace\(\$ReceiptPath\)\)'
            $header = Find-Top '^\$header = ConvertFrom-EtpRestoreHeader -Database \$Database '
            $match = Find-Top '^Assert-EtpRestoreReceiptMatchesHeader -ReceiptDatabase \$receiptDatabase -HeaderDatabase \$header\.DatabaseName$'
            $log = Find-Top '^if \(\$header\.IsStagingSource\) \{ Write-RestoreLog "Backup of staging database \$\(\$header\.DatabaseName\); restoring as \$Database\." \}$'
            $verify = Find-Top '^try \{ Invoke-EtpSql .*RESTORE VERIFYONLY'
            $restore = Find-Top 'New-EtpRestoreDatabaseSql -Database \$Database -BackupFile \$staged '
            if ($receiptBlock.Count -ne 1 -or $header.Count -ne 1 -or $match.Count -ne 1 -or $log.Count -ne 1 -or $verify.Count -ne 1 -or $restore.Count -ne 1) {
                throw "The staging steps could not be found ($($receiptBlock.Count), $($header.Count), $($match.Count), $($log.Count), $($verify.Count), $($restore.Count))."
            }
            if ($receiptBlock[0].Extent.Text -notmatch '\$receiptDatabase = Assert-EtpRestoreReceipt ') { throw 'The receipt database is not kept.' }
            if ($match[0].Extent.StartOffset -lt $header[0].Extent.EndOffset -or $log[0].Extent.StartOffset -lt $match[0].Extent.EndOffset -or $verify[0].Extent.StartOffset -lt $log[0].Extent.EndOffset -or $restore[0].Extent.StartOffset -lt $verify[0].Extent.EndOffset) {
                throw 'The receipt is not held against the header, and the staging line logged, before the backup is verified and restored.'
            }
            if ($restore[0].Extent.Text -match 'header\.DatabaseName') { throw 'The restore names the backup''s database rather than the configured one.' }
            Write-Output 'Staging restore checks passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Staging restore checks passed.", result.Output);
        var blocks = result.Output.Split("---SQL---")[1..].Select(b => b[..b.IndexOf("---END---", StringComparison.Ordinal)]).ToArray();
        Assert.Equal(2, blocks.Length);
        foreach (var (sql, dataName, logName) in new[] { (blocks[0], "EtpReporting", "EtpReporting_log"), (blocks[1], "EtpStaging_20261004", "EtpStaging_20261004_log") })
        {
            Assert.Contains("IF DB_ID(N'EtpReporting') IS NOT NULL THROW 51901", sql, StringComparison.Ordinal);
            Assert.Contains("RESTORE DATABASE [EtpReporting] FROM DISK=@disk WITH MOVE @dataName TO @dataFile, MOVE @logName TO @logFile, CHECKSUM, RECOVERY", sql, StringComparison.Ordinal);
            Assert.Contains("DECLARE @dataFile nvarchar(520)=@dataFolder+N'EtpReporting.mdf';", sql, StringComparison.Ordinal);
            Assert.Contains("DECLARE @logFile nvarchar(520)=@logFolder+N'EtpReporting_log.ldf';", sql, StringComparison.Ordinal);
            Assert.Contains($"DECLARE @dataName nvarchar(128)=N'{dataName}';", sql, StringComparison.Ordinal);
            Assert.Contains($"DECLARE @logName nvarchar(128)=N'{logName}';", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("[EtpStaging", sql, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"(?i)\bREPLACE\b(?!\s*\()", sql);
        }
    }

    [Fact]
    public async Task File_list_accepts_exactly_one_data_and_one_log_file()
    {
        var command = $$"""
            {{Preamble()}}
            # RESTORE FILELISTONLY as sqlcmd prints it: LogicalName, PhysicalName, Type, FileGroupName,
            # Size, MaxSize, FileId, ... IsReadOnly (18), IsPresent (19), TDEThumbprint, SnapshotURL.
            function FileRow([string]$Name, [string]$Type, [string]$Size = '8388608', [string]$Present = '1') {
                @($Name, ('C:\Old\' + $Name), $Type, 'PRIMARY', $Size, '35184372080640', '1', '0', '0', '6F9619FF-8B86-D011-B42D-00C04FC964FF',
                  '0', '0', $Size, '512', '1', 'NULL', '0', '00000000-0000-0000-0000-000000000000', '0', $Present, 'NULL', 'NULL') -join '|'
            }
            $data = FileRow 'EtpReporting' 'D' '72351744'
            $log = FileRow 'EtpReporting_log' 'L' '8388608'
            $files = ConvertFrom-EtpRestoreFileList -Lines @($data, '', $log) -Database 'EtpReporting'
            if ($files.DataLogicalName -cne 'EtpReporting' -or $files.LogLogicalName -cne 'EtpReporting_log' -or $files.DataBytes -ne 72351744 -or $files.LogBytes -ne 8388608) {
                throw "The data and log files were misread: $($files | ConvertTo-Json -Compress)"
            }
            Assert-Refused { ConvertFrom-EtpRestoreFileList -Lines @($data, $log, (FileRow 'EtpCatalog' 'F')) -Database 'EtpReporting' } 'file layout'
            Assert-Refused { ConvertFrom-EtpRestoreFileList -Lines @($data, $log, (FileRow 'EtpStream' 'S')) -Database 'EtpReporting' } 'file layout'
            Assert-Refused { ConvertFrom-EtpRestoreFileList -Lines @($data, (FileRow 'EtpReporting2' 'D'), $log) -Database 'EtpReporting' } 'file layout'
            Assert-Refused { ConvertFrom-EtpRestoreFileList -Lines @($data) -Database 'EtpReporting' } 'file layout'
            Assert-Refused { ConvertFrom-EtpRestoreFileList -Lines @($data, (FileRow 'EtpReporting_log' 'L' '8388608' '0')) -Database 'EtpReporting' } 'file layout'
            Assert-Refused { ConvertFrom-EtpRestoreFileList -Lines @('EtpReporting|C:\Old\EtpReporting.mdf|D|PRIMARY|8388608', $log) -Database 'EtpReporting' } 'file layout'
            Assert-Refused { ConvertFrom-EtpRestoreFileList -Lines @((FileRow '' 'D'), $log) -Database 'EtpReporting' } 'file layout'
            Assert-Refused { ConvertFrom-EtpRestoreFileList -Lines @() -Database 'EtpReporting' } 'file layout'
            Write-Output 'File list checks passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("File list checks passed.", result.Output);
    }

    [Fact]
    public async Task Restore_sql_guards_the_database_and_never_replaces()
    {
        var command = $$"""
            {{Preamble()}}
            Assert-Refused { New-EtpRestoreDatabaseSql -Database 'Etp]Reporting' -BackupFile 'C:\x.bak' -DataLogicalName 'a' -LogLogicalName 'b' } 'valid database name'
            Write-Output '---SQL---'
            New-EtpRestoreDatabaseSql -Database 'EtpReporting' -BackupFile 'C:\PD\O''Brien\restore-source.bak' -DataLogicalName 'Etp''Data' -LogLogicalName 'EtpReporting_log'
            Write-Output '---END---'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        var start = result.Output.IndexOf("---SQL---", StringComparison.Ordinal);
        var end = result.Output.IndexOf("---END---", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, result.Output);
        var sql = result.Output[start..end];
        var guard = sql.IndexOf("IF DB_ID(N'EtpReporting') IS NOT NULL THROW 51901", StringComparison.Ordinal);
        var restore = sql.IndexOf("RESTORE DATABASE [EtpReporting] FROM DISK=@disk", StringComparison.Ordinal);
        Assert.True(guard >= 0 && restore > guard, "The existence guard must come first, in the batch that restores.");
        Assert.Contains("MOVE @dataName TO @dataFile, MOVE @logName TO @logFile, CHECKSUM, RECOVERY", sql, StringComparison.Ordinal);
        Assert.Contains("SERVERPROPERTY('InstanceDefaultDataPath')", sql, StringComparison.Ordinal);
        Assert.Contains("SERVERPROPERTY('InstanceDefaultLogPath')", sql, StringComparison.Ordinal);
        Assert.Contains("sys.dm_os_file_exists(@dataFile)", sql, StringComparison.Ordinal);
        Assert.Contains("sys.dm_os_file_exists(@logFile)", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\bREPLACE\b(?!\s*\()", sql);
        Assert.Contains("N'C:\\PD\\O''Brien\\restore-source.bak'", sql, StringComparison.Ordinal);
        Assert.Contains("N'Etp''Data'", sql, StringComparison.Ordinal);
        Assert.Contains("N'EtpReporting_log'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("EXEC", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Owner_recovery_uses_the_documented_procedure_for_the_signed_in_account_only()
    {
        var command = $$"""
            {{Preamble()}}
            $own = @((Get-Command New-EtpOwnerRecoverySql).Parameters.Keys | Where-Object { $_ -notin [System.Management.Automation.PSCmdlet]::CommonParameters -and $_ -notin [System.Management.Automation.PSCmdlet]::OptionalCommonParameters })
            if ($own.Count -ne 0) { throw "Owner recovery takes input: $($own -join ', ')" }
            Write-Output '---SQL---'
            New-EtpOwnerRecoverySql
            Write-Output '---END---'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        var start = result.Output.IndexOf("---SQL---", StringComparison.Ordinal);
        var end = result.Output.IndexOf("---END---", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, result.Output);
        var sql = result.Output[start..end];
        string[] ordered =
        [
            "SET XACT_ABORT ON",
            "DECLARE @identity nvarchar(200)=SUSER_SNAME();",
            "BEGIN TRANSACTION;",
            "EXEC dbo.configure_application_role @identity=@identity,@role='OWNER',@active=1;",
            "MERGE dbo.application_users WITH(HOLDLOCK)",
            "change_reason=@reason",
            "COMMIT TRANSACTION;",
            "ETP_OWNER:",
            "sys.database_role_members",
            "WHERE r.name=N'etp_owner' AND m.name=@principal",
            "ETP_LOGIN_ADMIN:",
            "permission_name=N'ALTER ANY LOGIN' AND state='W'",
        ];
        var position = -1;
        foreach (var fragment in ordered)
        {
            var next = sql.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(next > position, $"'{fragment}' is missing or out of order.");
            position = next;
        }
        // The reason is never empty, and nothing changes the database owner or the old rows.
        Assert.Contains("DECLARE @reason nvarchar(500)=N'Owner recovery after restoring this database onto '", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ALTER AUTHORIZATION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DISABLE TRIGGER", sql, StringComparison.OrdinalIgnoreCase);
        // Workpc, 2 Oct 2026: ETP_OWNER said 0 with the row, user and role all in place.
        // Membership is read from the catalog, not with IS_ROLEMEMBER.
        Assert.DoesNotContain("IS_ROLEMEMBER", sql, StringComparison.OrdinalIgnoreCase);
        // Migration 0043. The batch provisions the account running it, and SQL Server never
        // lets a login grant itself a permission, so it does not try; it only reports.
        Assert.DoesNotContain("GRANT ALTER ANY LOGIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_owner_left_without_the_grant_option_is_given_it_by_system_or_told_how_to_get_it()
    {
        // Migration 0043: the restore helper cannot give the account running it ALTER ANY
        // LOGIN WITH GRANT OPTION itself. Since Sagar's decision of 2 October 2026 it has SYSTEM
        // give it through the one-off task setup uses (the helper runs elevated), checks again,
        // and only then says what that means and where the fix is. A missing or unexpected
        // marker counts as missing; neither step ever stops the restore.
        var script = FindScript("restore-etp-database.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'restore-etp-database.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            $elevated = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -match 'BuiltInRole\]::Administrator' })
            $marker = @($top | Where-Object { $_.Extent.Text -eq '$loginAdministration = @(Get-EtpRestoreMarkers $owner ''ETP_LOGIN_ADMIN'')' })
            $owner = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -match 'ownerResult' })
            $missing = @($top | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$loginAdministration.Count -ne 1 -or $loginAdministration[0] -cne ''GRANT_OPTION''' })
            if ($elevated.Count -ne 1 -or $marker.Count -ne 1 -or $owner.Count -ne 1 -or $missing.Count -ne 2) { throw "The grant-option steps could not be found ($($elevated.Count), $($marker.Count), $($owner.Count), $($missing.Count))." }
            $attempt = $missing[0]; $note = $missing[1]
            if ($elevated[0].Extent.EndOffset -gt $owner[0].Extent.StartOffset) { throw 'The helper does not refuse an unelevated run before the Owner step.' }
            if ($marker[0].Extent.StartOffset -lt $owner[0].Extent.EndOffset -or $attempt.Extent.StartOffset -lt $marker[0].Extent.EndOffset -or $note.Extent.StartOffset -lt $attempt.Extent.EndOffset) { throw 'The grant and the report are not made after the Owner recovery succeeded.' }
            foreach ($step in @($attempt, $note)) {
                if (@($step.FindAll({ param($node) $node -is [System.Management.Automation.Language.ThrowStatementAst] }, $true)).Count -ne 0) { throw 'A missing grant option stops the restore.' }
            }
            $grant = $attempt.Clauses[0].Item2.Extent.Text
            if ($grant -notmatch 'try \{ Invoke-EtpOwnerGrantOptionAsSystem -SqlCmdPath \$sqlcmd -ServerInstance \$ServerInstance -Database \$Database \} catch \{ \$null \}') { throw 'The SYSTEM grant is not attempted, or not guarded.' }
            if ($grant -notmatch 'Write-RestoreLog \$ownerGrant\.Message') { throw 'The outcome of the SYSTEM grant is not logged.' }
            if ($grant -notmatch '(?s)Invoke-EtpOwnerGrantOptionAsSystem.*New-EtpLoginAdministrationCheckSql') { throw 'The grant option is not checked again after the SYSTEM grant.' }
            $body = $note.Clauses[0].Item2
            if ($body.Extent.Text -notmatch 'Write-RestoreLog "NOTE: ' -or $body.Extent.Text -notmatch 'Owners and SQL Server logins' -or $body.Extent.Text -notmatch 'Run as administrator') { throw 'The NOTE does not say what to do.' }
            if ($body.Extent.Text -notmatch 'Get-EtpOwnerGrantManualCommand -Identity \$sqlIdentity -ServerInstance \$ServerInstance -SqlCmdPath \$sqlcmd') { throw 'The NOTE does not give the manual command.' }
            Write-Output 'Grant-option report passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Grant-option report passed.", result.Output);
    }

    [Fact]
    public async Task The_check_after_the_system_grant_is_read_only_and_reads_the_restoring_account()
    {
        var command = $$"""
            {{Preamble()}}
            $own = @((Get-Command New-EtpLoginAdministrationCheckSql).Parameters.Keys | Where-Object { $_ -notin [System.Management.Automation.PSCmdlet]::CommonParameters -and $_ -notin [System.Management.Automation.PSCmdlet]::OptionalCommonParameters })
            if ($own.Count -ne 0) { throw "The check takes input: $($own -join ', ')" }
            Write-Output '---SQL---'
            New-EtpLoginAdministrationCheckSql
            Write-Output '---END---'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        var start = result.Output.IndexOf("---SQL---", StringComparison.Ordinal);
        var end = result.Output.IndexOf("---END---", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, result.Output);
        var sql = result.Output[start..end];
        Assert.Contains("SELECT N'ETP_LOGIN_ADMIN:'", sql, StringComparison.Ordinal);
        Assert.Contains("grantee_principal_id=SUSER_ID(SUSER_SNAME())", sql, StringComparison.Ordinal);
        Assert.Contains("permission_name=N'ALTER ANY LOGIN' AND state='W'", sql, StringComparison.Ordinal);
        foreach (var verb in new[] { "GRANT ", "REVOKE ", "DENY ", "CREATE ", "EXEC", "INSERT", "UPDATE", "DELETE", "MERGE" })
            Assert.DoesNotContain(verb, sql.Replace("GRANT_OPTION", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Triggers_etp_never_creates_are_refused_before_the_restored_code_runs_as_administrator()
    {
        // The Owner recovery and setup's migrations run the restored database's own code as a
        // SQL administrator. A database-level DDL trigger fires on the CREATE USER inside
        // configure_application_role; an extra trigger on application_users fires on the MERGE.
        // ETP creates neither, so either is refused before step 14.
        var script = FindScript("restore-etp-database.ps1").Replace("'", "''");
        var command = $$"""
            {{Preamble()}}
            $etp = @('', '------', 'ETP_SCHEMA:1', 'ETP_MIGRATION:0032_active_users_can_connect', 'ETP_USERS_TRIGGER:trg_application_users_history')
            Assert-EtpRestoredDatabaseCode -Lines $etp
            Assert-EtpRestoredDatabaseCode -Lines @('ETP_SCHEMA:1')
            Assert-Refused { Assert-EtpRestoredDatabaseCode -Lines ($etp + 'ETP_DATABASE_DDL_TRIGGER:audit_everything') } 'triggers ETP never creates \(audit_everything\)'
            Assert-Refused { Assert-EtpRestoredDatabaseCode -Lines ($etp + 'ETP_USERS_TRIGGER:trg_grant_me_sysadmin') } 'trg_grant_me_sysadmin'
            Assert-Refused { Assert-EtpRestoredDatabaseCode -Lines ($etp + ('ETP_DATABASE_DDL_TRIGGER:bad' + [char]7 + 'name')) } 'bad\?name'
            Assert-Refused { Assert-EtpRestoredDatabaseCode -Lines ($etp + 'ETP_DATABASE_DDL_TRIGGER:x') } 'Do not run setup against it'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'restore-etp-database.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            $check = @($top | Where-Object { $_ -is [System.Management.Automation.Language.PipelineAst] -and $_.Extent.Text -eq 'Assert-EtpRestoredDatabaseCode -Lines $schema' })
            $schema = @($top | Where-Object { $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -eq '$schema' })
            $owner = @($top | Where-Object { $_ -isnot [System.Management.Automation.Language.FunctionDefinitionAst] -and $_.Extent.Text -match 'New-EtpOwnerRecoverySql' })
            if ($check.Count -ne 1 -or $schema.Count -ne 1 -or $owner.Count -ne 1) { throw 'The code check, the schema query or the Owner recovery could not be found.' }
            if ($check[0].Extent.StartOffset -lt $schema[0].Extent.EndOffset -or $check[0].Extent.EndOffset -gt $owner[0].Extent.StartOffset) { throw 'The code check does not run between the schema query and the Owner recovery.' }
            if ($schema[0].Extent.Text -notmatch "ETP_DATABASE_DDL_TRIGGER:'\+name FROM sys\.triggers WHERE parent_class=0" -or
                $schema[0].Extent.Text -notmatch "ETP_USERS_TRIGGER:'\+name FROM sys\.triggers WHERE parent_class=1 AND parent_id=OBJECT_ID\(N'dbo\.application_users'\)") { throw 'The schema query does not list the triggers.' }
            Write-Output 'Restored code checks passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Restored code checks passed.", result.Output);
    }

    [Fact]
    public async Task Broker_only_install_never_alters_an_existing_broker()
    {
        // CREATE OR ALTER on a signed broker would silently drop the signature the automation
        // account's backups depend on. -BrokerOnly must create it only where it is missing -
        // or, since 1.9.3, replace an unsigned one from an earlier build, which has no
        // signature to lose (Get-EtpBrokerOnlyAction never answers Replace for a signed one;
        // RecoveryDrillRowCountScriptTests) - and never go on to the grants.
        var script = FindScript("install-etp-sql-operations.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'install-etp-sql-operations.ps1 does not parse.' }
            $brokerOnly = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.IfStatementAst] -and $node.Clauses[0].Item1.Extent.Text -eq '$BrokerOnly' }, $true))
            if ($brokerOnly.Count -ne 1) { throw 'There is no single -BrokerOnly branch.' }
            $clause = $brokerOnly[0].Clauses[0].Item2
            $creates = @($clause.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-EtpSql' -and $node.Extent.Text -match '-Query \$query\b' }, $true))
            if ($creates.Count -ne 1) { throw 'The -BrokerOnly branch does not create the broker exactly once.' }
            $guarded = $false
            $parent = $creates[0].Parent
            while ($null -ne $parent -and -not [object]::ReferenceEquals($parent, $clause)) {
                if ($parent -is [System.Management.Automation.Language.IfStatementAst] -and $parent.Clauses[0].Item1.Extent.Text -ceq '$brokerAction -ceq ''Install'' -or $brokerAction -ceq ''Replace''') { $guarded = $true }
                $parent = $parent.Parent
            }
            if (-not $guarded) { throw 'The broker is created without first finding it missing or unsigned and outdated.' }
            if (@($clause.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Get-EtpBrokerOnlyAction' }, $true)).Count -ne 1) { throw 'The -BrokerOnly branch does not decide with Get-EtpBrokerOnlyAction.' }
            $grantsInBranch = @($clause.FindAll({ param($node) $node -is [System.Management.Automation.Language.VariableExpressionAst] -and $node.VariablePath.UserPath -eq 'grants' }, $true))
            if ($grantsInBranch.Count -ne 0) { throw 'The -BrokerOnly branch reaches the grants.' }
            $last = $clause.Statements[$clause.Statements.Count - 1]
            if ($last -isnot [System.Management.Automation.Language.ReturnStatementAst]) { throw 'The -BrokerOnly branch does not stop before the grants.' }
            $grants = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-EtpSql' -and $node.Extent.Text -match '-Query \$grants\b' }, $true))
            if ($grants.Count -ne 1 -or $grants[0].Extent.StartOffset -lt $brokerOnly[0].Extent.EndOffset) { throw 'The grants run before the -BrokerOnly branch has returned.' }
            Write-Output 'Broker-only structure passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Broker-only structure passed.", result.Output);
    }

    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpRestoreScript", Guid.NewGuid().ToString("N"))).FullName;

    // Dot-sources the script (its main flow stops at the dot-source guard) and adds a helper
    // that requires a refusal with a given message.
    private static string Preamble()
    {
        var script = FindScript("restore-etp-database.ps1").Replace("'", "''");
        return $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -BackupPath 'C:\Unused.bak'
            function Assert-Refused([scriptblock]$Action, [string]$Pattern) {
                $message = $null
                try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
                if ($null -eq $message) { throw "Accepted, but should have been refused with: $Pattern" }
                if ($message -notmatch $Pattern) { throw "Refused with '$message', expected: $Pattern" }
            }
            """;
    }

    private static string FindScript(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "scripts", name);
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string[] arguments, string? programData = null)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(arguments)) start.ArgumentList.Add(argument);
        if (programData is not null) start.Environment["ProgramData"] = programData;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
