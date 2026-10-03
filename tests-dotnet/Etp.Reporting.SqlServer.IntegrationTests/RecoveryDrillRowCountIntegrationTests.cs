using System.Security.AccessControl;
using System.Text.Json;
using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.9.3, Phase 4 A4.4 and A4.4a against a real SQL Server: the shipped broker counts the four
/// tables around a backup and in the drill's restored copy before dropping it, and the drill's
/// result is recorded append-only and read back for System status.
/// </summary>
public sealed class RecoveryDrillRowCountIntegrationTests
{
    private static readonly string[] Tables = ["sales_invoices", "sales_lines", "import_files", "daily_reporting_days"];

    [Fact]
    public async Task The_broker_counts_around_a_backup_and_counts_the_restored_copy_before_dropping_it()
    {
        var database = new SqlDatabaseFixture();
        var root = Path.Combine(Path.GetTempPath(), "EtpRecoveryTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            await database.InitializeAsync();
            Directory.CreateDirectory(root);
            var server = new SqlConnectionStringBuilder(database.ConnectionString).DataSource;
            if (!server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                var service = server.Contains('\\') ? "NT SERVICE\\MSSQL$" + server.Split('\\')[1] : "NT SERVICE\\MSSQLSERVER";
                var acl = new DirectoryInfo(root).GetAccessControl();
                acl.AddAccessRule(new FileSystemAccessRule(service, FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(root).SetAccessControl(acl);
            }
            await database.ExecuteAsync("""
                DECLARE @batch uniqueidentifier=NEWID();
                INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count)
                VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),10);
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes)
                VALUES(@batch,'rowcount-a.xlsx',REPLICATE('a',64),1),(@batch,'rowcount-b.xlsx',REPLICATE('b',64),1);
                """);
            var template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "scripts", "sql", "etp-operations-broker.sql"));
            await database.ExecuteAsync(template.Replace("__PROCEDURE__", "test_rowcount_broker").Replace("__DATABASE_LITERAL__", database.Name)
                .Replace("__DATABASE_IDENTIFIER__", database.Name).Replace("__BACKUP_DIRECTORY__", Escape(root)).Replace("__RESTORE_DIRECTORY__", Escape(root)));

            var live = await LiveCountsAsync(database);
            Assert.Equal(2, live["import_files"]);
            var file = database.Name + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
            var encrypts = (string)(await database.ExecuteAsync("SELECT CASE WHEN CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Express%' OR CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) LIKE '%Web%' THEN 'NO' ELSE 'YES' END"))! == "YES";
            if (!encrypts)
            {
                // The broker's own BACKUP: counted just before and just after, both equal to the live counts.
                var lines = await ReadAllFirstColumnsAsync(database, $"EXEC dbo.test_rowcount_broker 'BACKUP',N'{file}'", 300);
                Assert.StartsWith("ETP_METADATA:", lines[0]);
                using var counts = JsonDocument.Parse(Assert.Single(lines, x => x.StartsWith("ETP_ROWCOUNTS:", StringComparison.Ordinal))["ETP_ROWCOUNTS:".Length..]);
                foreach (var table in Tables)
                {
                    Assert.Equal(live[table], counts.RootElement.GetProperty("before").GetProperty(table).GetInt64());
                    Assert.Equal(live[table], counts.RootElement.GetProperty("after").GetProperty(table).GetInt64());
                }
            }
            else
            {
                // An encrypting edition needs the backup certificate; take the backup directly.
                await database.ExecuteAsync($"BACKUP DATABASE [{database.Name}] TO DISK=N'{Escape(Path.Combine(root, file))}' WITH COPY_ONLY,CHECKSUM;", 300);
            }

            // A change after the backup must not appear in the restored copy's counts.
            await database.ExecuteAsync("""
                DECLARE @batch uniqueidentifier=(SELECT TOP(1) import_batch_id FROM dbo.import_batches);
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes) VALUES(@batch,'rowcount-c.xlsx',REPLICATE('c',64),1);
                """);
            var drill = await ReadAllFirstColumnsAsync(database, $"EXEC dbo.test_rowcount_broker 'DRILL',N'{file}'", 300);
            Assert.StartsWith("ETP_METADATA:", drill[0]);
            using var restored = JsonDocument.Parse(Assert.Single(drill, x => x.StartsWith("ETP_ROWCOUNTS:", StringComparison.Ordinal))["ETP_ROWCOUNTS:".Length..]);
            foreach (var table in Tables)
                Assert.Equal(live[table], restored.RootElement.GetProperty("restored").GetProperty(table).GetInt64());
            Assert.Equal(3L, (await LiveCountsAsync(database))["import_files"]);
            // The restored copy was dropped after it was counted.
            Assert.Equal(0, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM sys.databases d JOIN sys.master_files f ON f.database_id=d.database_id WHERE d.name LIKE 'EtpRecovery[_]%' AND f.physical_name LIKE N'" + Escape(root) + "%'")));
            // METADATA sends no counts.
            Assert.DoesNotContain(await ReadAllFirstColumnsAsync(database, $"EXEC dbo.test_rowcount_broker 'METADATA',N'{file}'", 120), x => x.StartsWith("ETP_ROWCOUNTS:", StringComparison.Ordinal));
        }
        finally
        {
            try { await database.ExecuteAsync("IF OBJECT_ID(N'dbo.test_rowcount_broker',N'P') IS NOT NULL DROP PROCEDURE dbo.test_rowcount_broker;"); } catch (SqlException) { }
            await database.DisposeAsync();
            if (Directory.Exists(root) && Path.GetFileName(root).StartsWith("EtpRecoveryTest_", StringComparison.Ordinal)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Drill_results_are_recorded_append_only_and_shown_by_System_status()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var hash = new string('B', 64);
            var none = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.Null(none.LatestRecoveryDrillResult);

            await database.ExecuteAsync($"EXEC dbo.record_recovery_drill_result @outcome='Succeeded',@backup_sha256='{hash}',@row_counts_status='Matched',@sales_invoices_receipt=12,@sales_invoices_restored=12,@sales_lines_receipt=54,@sales_lines_restored=54,@import_files_receipt=3,@import_files_restored=3,@daily_reporting_days_receipt=6,@daily_reporting_days_restored=6;");
            var passed = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.NotNull(passed.LatestRecoveryDrillResult);
            Assert.True(passed.LatestRecoveryDrillResult!.Succeeded);
            Assert.Equal(4, passed.LatestRecoveryDrillResult.RowCounts.Count);
            Assert.DoesNotContain(passed.Warnings, x => x.Code == "RECOVERY_DRILL_FAILED");

            // A4.4a: the drill of a receipt whose sales_lines count was changed by one.
            await database.ExecuteAsync($"EXEC dbo.record_recovery_drill_result @outcome='Failed',@backup_sha256='{hash}',@row_counts_status='Mismatch',@sales_invoices_receipt=12,@sales_invoices_restored=12,@sales_lines_receipt=55,@sales_lines_restored=54,@import_files_receipt=3,@import_files_restored=3,@daily_reporting_days_receipt=6,@daily_reporting_days_restored=6;");
            await database.ExecuteAsync("EXEC dbo.record_operational_audit 'RestoreDrill','Failed',N'Recovery drill failed; row counts in the restored copy differ from the backup receipt',N'operations';");
            var failed = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.False(failed.LatestRecoveryDrillResult!.Succeeded);
            Assert.Contains(failed.Warnings, x => x.Code == "RECOVERY_DRILL_FAILED" && x.Message.Contains("sales_lines receipt 55, restored copy 54", StringComparison.Ordinal));
            var lines = DatabaseRecoveryPresentation.Lines(failed, DatabaseOperationalHealthThresholds.Default, DateTime.UtcNow);
            Assert.Equal("Failed", lines.Single(x => x.Item == "Latest recovery drill result").Status);
            Assert.Equal("Differs", lines.Single(x => x.Item == "Drill row count, sales_lines").Status);
            Assert.Equal(1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='RestoreDrill' AND outcome='Failed'")));

            // Inconsistent results are refused with one message, not a constraint name.
            foreach (var bad in new[]
            {
                // Matched with a pair that differs.
                $"EXEC dbo.record_recovery_drill_result @outcome='Succeeded',@backup_sha256='{hash}',@row_counts_status='Matched',@sales_invoices_receipt=1,@sales_invoices_restored=2,@sales_lines_receipt=1,@sales_lines_restored=1,@import_files_receipt=1,@import_files_restored=1,@daily_reporting_days_receipt=1,@daily_reporting_days_restored=1;",
                // A mismatch recorded as a pass.
                $"EXEC dbo.record_recovery_drill_result @outcome='Succeeded',@backup_sha256='{hash}',@row_counts_status='Mismatch',@sales_invoices_receipt=1,@sales_invoices_restored=2,@sales_lines_receipt=1,@sales_lines_restored=1,@import_files_receipt=1,@import_files_restored=1,@daily_reporting_days_receipt=1,@daily_reporting_days_restored=1;",
                // Not recorded without a reason, or with a reason that only fails passing.
                $"EXEC dbo.record_recovery_drill_result @outcome='Succeeded',@backup_sha256='{hash}',@row_counts_status='NotRecorded';",
                $"EXEC dbo.record_recovery_drill_result @outcome='Succeeded',@backup_sha256='{hash}',@row_counts_status='NotRecorded',@not_recorded_reason='RESTORED_COPY_NOT_COUNTED';",
                $"EXEC dbo.record_recovery_drill_result @outcome='Failed',@backup_sha256='{hash}',@row_counts_status='NotRecorded',@not_recorded_reason='SOMETHING_ELSE';",
            })
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(bad));
                Assert.Equal(51344, error.Number);
            }
            var badHash = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("EXEC dbo.record_recovery_drill_result @outcome='Succeeded',@backup_sha256='abc',@row_counts_status='NotRecorded',@not_recorded_reason='COUNT_FAILED';"));
            Assert.Equal(51342, badHash.Number);

            // Append-only.
            var update = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("UPDATE dbo.recovery_drill_results SET outcome='Succeeded';"));
            Assert.Equal(51343, update.Number);
            var delete = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("DELETE dbo.recovery_drill_results;"));
            Assert.Equal(51343, delete.Number);

            // A passed drill without counts says why.
            await database.ExecuteAsync($"EXEC dbo.record_recovery_drill_result @outcome='Succeeded',@backup_sha256='{hash}',@row_counts_status='NotRecorded',@not_recorded_reason='OPERATIONS_MODULE_OUTDATED';");
            var notRecorded = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.Equal("NotRecorded", notRecorded.LatestRecoveryDrillResult!.RowCountStatus);
            Assert.Empty(notRecorded.LatestRecoveryDrillResult.RowCounts);
            Assert.Contains("not recorded by this backup", RecoveryDrillResultText.Summary(notRecorded.LatestRecoveryDrillResult), StringComparison.Ordinal);
        }
        finally { await database.DisposeAsync(); }
    }

    private static async Task<Dictionary<string, long>> LiveCountsAsync(SqlDatabaseFixture database)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in Tables) counts[table] = Convert.ToInt64(await database.ExecuteAsync($"SELECT COUNT_BIG(*) FROM dbo.{table}"));
        return counts;
    }

    private static async Task<List<string>> ReadAllFirstColumnsAsync(SqlDatabaseFixture database, string sql, int timeoutSeconds)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = timeoutSeconds };
        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        do { while (await reader.ReadAsync()) if (!reader.IsDBNull(0)) lines.Add(reader.GetValue(0).ToString()!); }
        while (await reader.NextResultAsync());
        return lines;
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
