using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.9.3, Phase 4 A4.4 and A4.4a (decided by Sagar, 2 October 2026). The backup receipt records
/// COUNT_BIG(*) of sales_invoices, sales_lines, import_files and daily_reporting_days, counted
/// by the operations broker just before and just after BACKUP; the recovery drill counts the
/// restored copy inside the broker, before it is dropped, and fails naming the table and both
/// numbers when they differ. These run the shipped PowerShell functions with no SQL Server.
/// </summary>
public sealed class RecoveryDrillRowCountScriptTests
{
    private const string Counts = """{"sales_invoices":1200,"sales_lines":5400,"import_files":30,"daily_reporting_days":61}""";

    [Fact]
    public async Task The_backup_receipt_records_counts_only_when_they_did_not_move_during_the_backup()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $c = '{{Counts}}'
            function Record($lines) { Get-EtpBackupRowCountRecord -BrokerLines $lines }
            # Equal before and after: the counts go into the receipt.
            $r = Record @('', '------', ('ETP_METADATA:[]'), ('ETP_ROWCOUNTS:{"before":' + $c + ',"after":' + $c + '}'))
            if (-not $r.Contains('rowCounts') -or $r.Contains('rowCountsNotRecorded')) { throw "Equal counts: $($r.Keys -join ',')" }
            if ($r.rowCounts.sales_lines -ne 5400 -or $r.rowCounts.Keys.Count -ne 4) { throw 'The counts were not recorded as counted.' }
            if (@($r.rowCounts.Keys) -join ',' -cne 'sales_invoices,sales_lines,import_files,daily_reporting_days') { throw "Order: $(@($r.rowCounts.Keys) -join ',')" }
            # An import wrote between the two counts: no counts, and the reason.
            $moved = $c.Replace('5400','5401')
            $r = Record @(('ETP_ROWCOUNTS:{"before":' + $c + ',"after":' + $moved + '}'))
            if ($r.rowCountsNotRecorded -cne 'CHANGED_DURING_BACKUP' -or $r.Contains('rowCounts')) { throw "Moved: $($r.Keys -join ',')" }
            # A broker from before 1.9.3 sends no counts at all.
            $r = Record @('ETP_METADATA:[]')
            if ($r.rowCountsNotRecorded -cne 'OPERATIONS_MODULE_OUTDATED') { throw "Old broker: $($r.rowCountsNotRecorded)" }
            # Counting failed (null), or the line is not readable: the backup still succeeds.
            foreach ($bad in @(('ETP_ROWCOUNTS:{"before":null,"after":' + $c + '}'), 'ETP_ROWCOUNTS:not json', 'ETP_ROWCOUNTS:[1,2]',
                               ('ETP_ROWCOUNTS:{"before":{"sales_invoices":1},"after":{"sales_invoices":1} }'))) {
                $r = Record @($bad)
                if ($r.rowCountsNotRecorded -cne 'COUNT_FAILED') { throw "Bad line '$bad' gave $($r.rowCountsNotRecorded)" }
            }
            $r = Record @(('ETP_ROWCOUNTS:{"before":' + $c + ',"after":' + $c + '}'), ('ETP_ROWCOUNTS:{"before":' + $c + ',"after":' + $c + '}'))
            if ($r.rowCountsNotRecorded -cne 'COUNT_FAILED') { throw 'Two count lines were accepted.' }
            Write-Output 'Backup row counts passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Backup row counts passed.", result.Output);
    }

    [Fact]
    public async Task The_drill_passes_when_the_restored_copy_matches_and_fails_naming_the_table_and_both_numbers_when_not()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $c = '{{Counts}}'
            $receipt = ('{"schemaVersion":2,"rowCounts":' + $c + '}') | ConvertFrom-Json
            $v = Get-EtpDrillRowCountVerdict -Receipt $receipt -BrokerLines @('ETP_METADATA:[]', ('ETP_ROWCOUNTS:{"restored":' + $c + '}'))
            if (-not $v.Succeeded -or $v.Status -cne 'Matched' -or @($v.Pairs).Count -ne 4) { throw "Match: $($v.Status) $($v.Message)" }
            if ($v.Pairs[1].table -cne 'sales_lines' -or $v.Pairs[1].receipt -ne 5400 -or $v.Pairs[1].restored -ne 5400) { throw 'The sales_lines pair is wrong.' }

            # A4.4a: a receipt whose sales_lines count was changed by one.
            $altered = ('{"schemaVersion":2,"rowCounts":' + $c.Replace('5400','5401') + '}') | ConvertFrom-Json
            $v = Get-EtpDrillRowCountVerdict -Receipt $altered -BrokerLines @(('ETP_ROWCOUNTS:{"restored":' + $c + '}'))
            if ($v.Succeeded -or $v.Status -cne 'Mismatch') { throw "Altered receipt passed: $($v.Status)" }
            foreach ($needle in @('sales_lines', '5401', '5400')) { if (-not $v.Message.Contains($needle)) { throw "The failure does not name '$needle': $($v.Message)" } }
            if ($v.Message.Contains('sales_invoices')) { throw "A matching table was named: $($v.Message)" }
            if ($v.Message -notmatch 'sales_lines: receipt 5401, restored copy 5400') { throw "Wording: $($v.Message)" }
            Write-Output 'Drill comparison passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Drill comparison passed.", result.Output);
    }

    [Fact]
    public async Task A_receipt_without_counts_passes_with_its_reason_and_a_receipt_with_counts_fails_without_a_count_of_the_copy()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $c = '{{Counts}}'
            $restored = @(('ETP_ROWCOUNTS:{"restored":' + $c + '}'))
            function Verdict($json, $lines) { Get-EtpDrillRowCountVerdict -Receipt ($json | ConvertFrom-Json) -BrokerLines $lines }
            function Expect($v, $succeeded, $status, $reason, $needle) {
                if ($v.Succeeded -ne $succeeded -or $v.Status -cne $status -or $v.Reason -cne $reason) { throw "Expected $succeeded/$status/$reason, got $($v.Succeeded)/$($v.Status)/$($v.Reason): $($v.Message)" }
                if ($needle -and -not $v.Message.Contains($needle)) { throw "Message lacks '$needle': $($v.Message)" }
            }
            # Receipts that recorded no counts pass, saying so - never a silent pass.
            Expect (Verdict '{"schemaVersion":2}' $restored) $true 'NotRecorded' 'RECEIPT_WITHOUT_COUNTS' 'not recorded by this backup'
            Expect (Verdict '{"schemaVersion":2,"rowCountsNotRecorded":"CHANGED_DURING_BACKUP"}' $restored) $true 'NotRecorded' 'CHANGED_DURING_BACKUP' 'changed while the backup'
            Expect (Verdict '{"schemaVersion":2,"rowCountsNotRecorded":"OPERATIONS_MODULE_OUTDATED"}' @()) $true 'NotRecorded' 'OPERATIONS_MODULE_OUTDATED' 'older than 1.9.3'
            Expect (Verdict '{"schemaVersion":2,"rowCountsNotRecorded":"COUNT_FAILED"}' $restored) $true 'NotRecorded' 'COUNT_FAILED' 'could not be counted'
            # The receipt has counts but the broker sent none: an operations module from before 1.9.3.
            Expect (Verdict ('{"rowCounts":' + $c + '}') @('ETP_METADATA:[]')) $false 'NotRecorded' 'OPERATIONS_MODULE_OUTDATED' 'Reinstall the operations module'
            # It sent a line, but the restored copy could not be counted.
            Expect (Verdict ('{"rowCounts":' + $c + '}') @('ETP_ROWCOUNTS:{"restored":null}')) $false 'NotRecorded' 'RESTORED_COPY_NOT_COUNTED' 'could not be counted'
            Expect (Verdict ('{"rowCounts":' + $c + '}') @('ETP_ROWCOUNTS:garbage')) $false 'NotRecorded' 'RESTORED_COPY_NOT_COUNTED' ''
            # Receipts this build did not write are not trusted.
            foreach ($bad in @(
                '{"rowCountsNotRecorded":"SOMETHING_ELSE"}',
                '{"rowCountsNotRecorded":"changed_during_backup"}',
                ('{"rowCounts":' + $c + ',"rowCountsNotRecorded":"COUNT_FAILED"}'),
                '{"rowCounts":{"sales_invoices":1,"sales_lines":2,"import_files":3} }',
                '{"rowCounts":{"sales_invoices":1,"sales_lines":2,"import_files":3,"daily_reporting_days":4,"extra":5} }',
                '{"rowCounts":{"sales_invoices":1,"Sales_Lines":2,"import_files":3,"daily_reporting_days":4} }',
                '{"rowCounts":{"sales_invoices":1,"sales_lines":-2,"import_files":3,"daily_reporting_days":4} }',
                '{"rowCounts":{"sales_invoices":1,"sales_lines":"2","import_files":3,"daily_reporting_days":4} }',
                '{"rowCounts":{"sales_invoices":1,"sales_lines":2.5,"import_files":3,"daily_reporting_days":4} }',
                '{"rowCounts":[1,2,3,4]}')) {
                Expect (Verdict $bad $restored) $false 'NotRecorded' 'RECEIPT_COUNTS_UNREADABLE' 'unreadable'
            }
            Write-Output 'Drill reasons passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Drill reasons passed.", result.Output);
    }

    [Fact]
    public async Task A_failed_comparison_records_a_failed_result_and_audit_row_and_no_verified_drill()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $c = '{{Counts}}'
            $hash = 'ab' * 32
            $altered = ('{"rowCounts":' + $c.Replace('5400','5401') + '}') | ConvertFrom-Json
            $failed = Get-EtpDrillRowCountVerdict -Receipt $altered -BrokerLines @(('ETP_ROWCOUNTS:{"restored":' + $c + '}'))
            $global:sent = @()
            Publish-EtpRecoveryDrillResult -Verdict $failed -BackupSha256 $hash -Encryption 'NONE' -Invoke { param($q) $global:sent += $q }
            if ($global:sent.Count -ne 2) { throw "Failed drill sent $($global:sent.Count) statements: $($global:sent -join ' | ')" }
            if (-not $global:sent[0].StartsWith("EXEC dbo.record_recovery_drill_result @outcome='Failed',@backup_sha256='$hash',@row_counts_status='Mismatch',@not_recorded_reason=NULL,")) { throw "Result: $($global:sent[0])" }
            if (-not $global:sent[0].Contains('@sales_lines_receipt=5401,@sales_lines_restored=5400')) { throw "Counts: $($global:sent[0])" }
            if (-not $global:sent[1].StartsWith("EXEC dbo.record_operational_audit 'RestoreDrill','Failed',N'")) { throw "Audit: $($global:sent[1])" }
            if (($global:sent -join ' ').Contains('record_verified_operation')) { throw 'A failed drill was recorded as verified.' }

            $matched = Get-EtpDrillRowCountVerdict -Receipt (('{"rowCounts":' + $c + '}') | ConvertFrom-Json) -BrokerLines @(('ETP_ROWCOUNTS:{"restored":' + $c + '}'))
            $global:sent = @()
            Publish-EtpRecoveryDrillResult -Verdict $matched -BackupSha256 $hash -Encryption 'AES_256' -Invoke { param($q) $global:sent += $q }
            if ($global:sent.Count -ne 3 -or -not $global:sent[0].Contains("@outcome='Succeeded'") -or -not $global:sent[0].Contains("@row_counts_status='Matched'")) { throw "Passed: $($global:sent -join ' | ')" }
            if ($global:sent[1] -cne "EXEC dbo.record_verified_operation 'RestoreDrill','$hash';") { throw "Verified: $($global:sent[1])" }
            if (-not $global:sent[2].Contains("'RestoreDrill','Succeeded',N'Isolated encrypted restore")) { throw "Audit: $($global:sent[2])" }

            $old = Get-EtpDrillRowCountVerdict -Receipt ('{"schemaVersion":2}' | ConvertFrom-Json) -BrokerLines @()
            $global:sent = @()
            Publish-EtpRecoveryDrillResult -Verdict $old -BackupSha256 $hash -Encryption 'NONE' -Invoke { param($q) $global:sent += $q }
            if (-not $global:sent[0].Contains("@row_counts_status='NotRecorded',@not_recorded_reason='RECEIPT_WITHOUT_COUNTS',@sales_invoices_receipt=NULL")) { throw "Old receipt: $($global:sent[0])" }
            if (-not $global:sent[2].Contains('row counts were not recorded by this backup')) { throw "The audit row hides the missing counts: $($global:sent[2])" }

            $refused = $false
            try { Get-EtpRecoveryDrillRecordQueries -Verdict $failed -BackupSha256 "x';DROP TABLE t;--" -Encryption 'NONE' | Out-Null }
            catch { $refused = $_.Exception.Message -ceq 'A complete backup verification hash is required.' }
            if (-not $refused) { throw 'An unsafe hash was accepted.' }
            Write-Output 'Drill recording passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Drill recording passed.", result.Output);
    }

    [Fact]
    public async Task Every_audit_detail_the_drill_can_write_is_one_record_operational_audit_accepts()
    {
        // dbo.record_operational_audit (0026) refuses details with ':', '/', '\' or digits
        // outside a fixed count grammar. A refused detail would fail the drill's recording.
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $hash = 'CD' * 32
            $pairs = @(foreach ($t in $EtpRowCountTables) { [pscustomobject]@{ table = $t; receipt = [long]7; restored = [long]7 } })
            $bad = @($pairs | ForEach-Object { [pscustomobject]@{ table = $_.table; receipt = $_.receipt; restored = [long]8 } })
            $verdicts = @(
                [pscustomobject]@{ Succeeded = $true; Status = 'Matched'; Reason = $null; Pairs = $pairs },
                [pscustomobject]@{ Succeeded = $false; Status = 'Mismatch'; Reason = $null; Pairs = $bad })
            foreach ($r in @('CHANGED_DURING_BACKUP','OPERATIONS_MODULE_OUTDATED','COUNT_FAILED','RECEIPT_WITHOUT_COUNTS')) { $verdicts += [pscustomobject]@{ Succeeded = $true; Status = 'NotRecorded'; Reason = $r; Pairs = @() } }
            foreach ($r in @('OPERATIONS_MODULE_OUTDATED','RESTORED_COPY_NOT_COUNTED','RECEIPT_COUNTS_UNREADABLE')) { $verdicts += [pscustomobject]@{ Succeeded = $false; Status = 'NotRecorded'; Reason = $r; Pairs = @() } }
            $seen = 0
            foreach ($v in $verdicts) {
                foreach ($e in @('AES_256','NONE')) {
                    foreach ($q in @(Get-EtpRecoveryDrillRecordQueries -Verdict $v -BackupSha256 $hash -Encryption $e | Where-Object { $_.Contains('record_operational_audit') })) {
                        if ($q -notmatch "^EXEC dbo\.record_operational_audit 'RestoreDrill','(Succeeded|Failed)',N'([^']*)',N'operations';$") { throw "Unexpected audit statement: $q" }
                        $detail = $Matches[2]
                        if ($detail -match '[:/\\0-9]' -or $detail.Length -gt 200) { throw "record_operational_audit would refuse: $detail" }
                        $seen++
                    }
                }
            }
            if ($seen -ne 18) { throw "Expected 18 audit statements, saw $seen." }
            Write-Output 'Audit details passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Audit details passed.", result.Output);
    }

    [Fact]
    public async Task The_drill_result_file_lists_the_four_pairs_or_the_reason()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $c = '{{Counts}}'
            $v = Get-EtpDrillRowCountVerdict -Receipt (('{"rowCounts":' + $c + '}') | ConvertFrom-Json) -BrokerLines @(('ETP_ROWCOUNTS:{"restored":' + $c + '}'))
            $doc = (New-EtpRecoveryDrillResultDocument -Verdict $v -BackupSha256 ('EF' * 32) | ConvertTo-Json -Depth 10) | ConvertFrom-Json
            if ($doc.schemaVersion -ne 1 -or $doc.succeeded -ne $true -or @($doc.rowCounts).Count -ne 4 -or $null -ne $doc.rowCountsNotRecorded) { throw "Matched document: $($doc | ConvertTo-Json -Compress)" }
            $p = @($doc.rowCounts | Where-Object { $_.table -ceq 'daily_reporting_days' })
            if ($p.Count -ne 1 -or $p[0].receipt -ne 61 -or $p[0].restored -ne 61) { throw 'The daily_reporting_days pair is missing.' }
            $v = Get-EtpDrillRowCountVerdict -Receipt ('{"rowCountsNotRecorded":"CHANGED_DURING_BACKUP"}' | ConvertFrom-Json) -BrokerLines @()
            $doc = (New-EtpRecoveryDrillResultDocument -Verdict $v -BackupSha256 ('EF' * 32) | ConvertTo-Json -Depth 10) | ConvertFrom-Json
            if ($null -ne $doc.rowCounts -or $doc.rowCountsNotRecorded -cne 'CHANGED_DURING_BACKUP' -or -not $doc.rowCountsNote.Contains('not recorded')) { throw "Not-recorded document: $($doc | ConvertTo-Json -Compress)" }
            Write-Output 'Drill document passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Drill document passed.", result.Output);
    }

    [Fact]
    public async Task Broker_only_never_replaces_a_signed_broker_and_replaces_an_unsigned_outdated_one()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $expect = @{ 'ETP_BROKER:MISSING' = 'Install'; 'ETP_BROKER:CURRENT' = 'Keep'; 'ETP_BROKER:OUTDATED_UNSIGNED' = 'Replace'; 'ETP_BROKER:OUTDATED_SIGNED' = 'KeepSigned' }
            foreach ($k in $expect.Keys) {
                $a = Get-EtpBrokerOnlyAction -Lines @('', '-----', $k)
                if ($a -cne $expect[$k]) { throw "$k gave $a" }
            }
            foreach ($bad in @(@(), @('ETP_BROKER:PRESENT'), @('etp_broker:missing'), @('ETP_BROKER:MISSING','ETP_BROKER:CURRENT'))) {
                $refused = $false
                try { Get-EtpBrokerOnlyAction -Lines $bad | Out-Null } catch { $refused = $true }
                if (-not $refused) { throw "Unreadable state accepted: $($bad -join ',')" }
            }
            $q = Get-EtpBrokerStateQuery -Procedure (Get-EtpOperationsProcedureName 'EtpReporting')
            if (-not $q.Contains($EtpOperationsBrokerRevision) -or -not $q.Contains('sys.crypt_properties')) { throw "State query: $q" }
            if ($q -match '(?i)\b(GRANT|ALTER|CREATE|DROP|DENY|REVOKE|UPDATE|DELETE|INSERT)\b') { throw "The state query changes something: $($Matches[0])" }
            $refused = $false
            try { Get-EtpBrokerStateQuery -Procedure "x]; DROP PROCEDURE y;--" | Out-Null } catch { $refused = $true }
            if (-not $refused) { throw 'An unsafe procedure name was accepted.' }
            Write-Output 'Broker-only actions passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Broker-only actions passed.", result.Output);
    }

    [Fact]
    public void The_broker_carries_the_revision_marker_inside_its_body_and_counts_where_the_design_says()
    {
        var broker = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "sql", "etp-operations-broker.sql"));
        var common = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "etp-operations-common.ps1"));
        const string marker = "[ETP_BROKER_REVISION:2]";
        Assert.Contains($"$EtpOperationsBrokerRevision = '{marker}'", common, StringComparison.Ordinal);
        Assert.Equal(1, broker.Split(marker).Length - 1);
        // Inside the body, where OBJECT_DEFINITION sees it and CREATE OR ALTER replaces it.
        Assert.True(broker.IndexOf(marker, StringComparison.Ordinal) > broker.IndexOf("BEGIN", broker.IndexOf("CREATE OR ALTER PROCEDURE", StringComparison.Ordinal), StringComparison.Ordinal));
        // The grant check used by setup asks for the same revision.
        Assert.Contains("CHARINDEX(N'$EtpOperationsBrokerRevision'", common, StringComparison.Ordinal);

        // BACKUP: counted immediately before and immediately after the BACKUP statement runs.
        var backupBranch = broker[broker.IndexOf("IF @operation='BACKUP'", StringComparison.Ordinal)..broker.IndexOf("RESTORE VERIFYONLY", StringComparison.Ordinal)];
        var before = backupBranch.IndexOf("@counts=@countsBefore OUTPUT", StringComparison.Ordinal);
        var backup = backupBranch.IndexOf("EXEC sys.sp_executesql @sql;", StringComparison.Ordinal);
        var after = backupBranch.IndexOf("@counts=@countsAfter OUTPUT", StringComparison.Ordinal);
        Assert.True(before > 0 && backup > before && after > backup, "The backup is not counted just before and just after BACKUP.");
        // DRILL: the restored copy is counted after DBCC CHECKDB and before it is dropped.
        var drill = broker[broker.IndexOf("IF @operation='DRILL'", StringComparison.Ordinal)..];
        var check = drill.IndexOf("DBCC CHECKDB", StringComparison.Ordinal);
        var restored = drill.IndexOf("@counts=@countsRestored OUTPUT", StringComparison.Ordinal);
        var drop = drill.IndexOf("DROP DATABASE", StringComparison.Ordinal);
        Assert.True(check > 0 && restored > check && drop > restored, "The restored copy is not counted between the integrity check and the drop.");
        foreach (var table in new[] { "sales_invoices", "sales_lines", "import_files", "daily_reporting_days" })
            Assert.Contains($"COUNT_BIG(*) FROM #db#.dbo.{table}) AS {table}", broker, StringComparison.Ordinal);
        // After the metadata, so callers that read only the first result still get the metadata.
        Assert.True(broker.LastIndexOf("ETP_ROWCOUNTS:", StringComparison.Ordinal) > broker.IndexOf("SELECT N'ETP_METADATA:'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_drill_compares_before_recording_and_throws_the_comparison_failure()
    {
        var path = Path.Combine(RepositoryRoot(), "scripts", "invoke-etp-recovery-drill.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{path}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'invoke-etp-recovery-drill.ps1 does not parse.' }
            $top = @($ast.EndBlock.Statements)
            function At($pattern) { $found = @($top | Where-Object { $_.Extent.Text -match $pattern }); if ($found.Count -lt 1) { throw "Not found: $pattern" }; return $found[0].Extent.StartOffset }
            $drill = At 'Invoke-EtpOperationsBrokerCall .*-Operation DRILL'
            $reread = At '\$null=Read-EtpVerifiedReceipt -ReceiptPath \$receiptPath'
            $verdict = At 'Get-EtpDrillRowCountVerdict -Receipt \$receipt -BrokerLines \$drill\.Lines'
            $json = At 'New-EtpRecoveryDrillResultDocument'
            $publish = At 'Publish-EtpRecoveryDrillResult'
            $fail = At '^if \(-not \$verdict\.Succeeded\) \{ throw \$verdict\.Message \}$'
            $done = At "Write-Output 'Receipt-verified recovery drill completed\.'"
            if (-not ($drill -lt $reread -and $reread -lt $verdict -and $verdict -lt $json -and $json -lt $publish -and $publish -lt $fail -and $fail -lt $done)) { throw 'The drill does not compare, write, record and then fail in that order.' }
            if (@($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Resolve-EtpDrillReceiptPath' }, $true)).Count -ne 1) { throw 'The receipt path is not resolved once.' }
            if (@($ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'Read-EtpVerifiedReceipt' -and $n.Extent.Text -match '-ReceiptPath \$receiptPath ' }, $true)).Count -ne 2) { throw 'Both receipt reads do not use the resolved path.' }
            Write-Output 'Drill structure passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Drill structure passed.", result.Output);
    }

    // ------------------------------------------------------------ plumbing

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    private static string Common() => Path.Combine(RepositoryRoot(), "scripts", "etp-operations-common.ps1").Replace("'", "''");

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string command)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
