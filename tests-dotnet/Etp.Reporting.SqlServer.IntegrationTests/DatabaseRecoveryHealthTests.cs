using Etp.Reporting.Desktop.Modules.OperationsAdministration;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// R4. The Database and recovery block against a real database: a failed import and no
/// receipt must read as a failure and as Missing, never as healthy; a recorded receipt
/// must surface with its time and fingerprint; and evidence must not be forgeable by a
/// Store Manager.
/// </summary>
public sealed class DatabaseRecoveryHealthTests
{
    private static DatabaseOperationalHealthThresholds Thresholds => DatabaseOperationalHealthThresholds.Default;

    private static string StatusOf(DatabaseOperationalHealth health, string item) =>
        DatabaseRecoveryPresentation.Lines(health, Thresholds, DateTime.UtcNow).Single(x => x.Item == item).Status;

    [Fact]
    public async Task The_failed_import_count_is_what_the_Problems_list_will_actually_show()
    {
        // The block says "Open Import, then Problems, to see them". That promise was broken:
        // the count came from import_batches while Problems is built from import_attempts, so
        // a retried failure showed a row in Problems while health reported zero.
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();

            await database.ExecuteAsync("""
                INSERT dbo.import_attempts
                 (recorded_utc,file_name,outcome,rows_processed,new_rows,already_present_rows,conflict_rows,diagnostics_json)
                VALUES
                 (DATEADD(hour,-4,SYSUTCDATETIME()),'R022_revenue.xlsx','Failed',0,0,0,0,N'[]'),
                 (DATEADD(hour,-3,SYSUTCDATETIME()),'R014_stock.xlsx','Conflict',10,0,4,6,N'[]'),
                 (DATEADD(hour,-2,SYSUTCDATETIME()),'R008_cash.xlsx','Failed',0,0,0,0,N'[]'),
                 (DATEADD(hour,-1,SYSUTCDATETIME()),'R008_cash.xlsx','Imported',57,57,0,0,N'[]');
                """);

            var health = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();

            // Two unresolved files. R008 failed and then imported cleanly, so it is no longer a
            // problem and must not be counted - counting it is exactly the lie this fixes.
            Assert.Equal(2, health.FailedImportsLast24Hours);
            Assert.Equal("2", StatusOf(health, "Failed imports, last 24 hours"));

            // An older failure outside the window is not today's problem.
            await database.ExecuteAsync("""
                INSERT dbo.import_attempts
                 (recorded_utc,file_name,outcome,rows_processed,new_rows,already_present_rows,conflict_rows,diagnostics_json)
                VALUES (DATEADD(hour,-30,SYSUTCDATETIME()),'R099_old.xlsx','Failed',0,0,0,0,N'[]');
                """);
            Assert.Equal(2, (await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync())
                .FailedImportsLast24Hours);

            // A batch that failed before recording any file cannot appear in Problems, and is
            // still counted: a failure the owner cannot see listed must not vanish entirely.
            await database.ExecuteAsync("""
                INSERT dbo.import_batches(import_batch_id,status,started_utc,failure_reason)
                VALUES(NEWID(),'Failed',SYSUTCDATETIME(),N'Folder could not be read');
                """);
            Assert.Equal(3, (await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync())
                .FailedImportsLast24Hours);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task A_failed_import_with_no_receipts_reads_as_failed_and_missing_never_healthy()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();

            // Nothing recorded yet.
            var empty = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.Null(empty.LastSuccessfulBackupUtc);
            Assert.Null(empty.LastSuccessfulRecoveryDrillUtc);
            Assert.Null(empty.LastSuccessfulImportUtc);
            Assert.Equal("Missing", StatusOf(empty, "Last verified backup"));
            Assert.Equal("Missing", StatusOf(empty, "Last verified recovery drill"));
            Assert.Equal("Missing", StatusOf(empty, "Last successful import"));
            Assert.DoesNotContain(DatabaseRecoveryPresentation.Lines(empty, Thresholds, DateTime.UtcNow),
                line => line.Status.Contains("Healthy", StringComparison.OrdinalIgnoreCase));

            // One failed import inside the last 24 hours.
            await database.ExecuteAsync("""
                INSERT dbo.import_batches(import_batch_id,status,started_utc,failure_reason)
                VALUES(NEWID(),'Failed',SYSUTCDATETIME(),N'Fixture failure');
                """);
            var withFailure = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.True(withFailure.FailedImportsLast24Hours >= 1);
            Assert.NotEqual("0", StatusOf(withFailure, "Failed imports, last 24 hours"));

            // A completed import is what "last successful import" counts.
            await database.ExecuteAsync("""
                DECLARE @batch uniqueidentifier=NEWID();
                INSERT dbo.import_batches(import_batch_id,status,started_utc,completed_utc,source_row_count)
                VALUES(@batch,'Completed',SYSUTCDATETIME(),SYSUTCDATETIME(),10);
                INSERT dbo.import_files(import_batch_id,original_file_name,source_sha256,size_bytes)
                VALUES(@batch,'recovery-health.xlsx',REPLICATE('f',64),1);
                """);
            var withImport = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.NotNull(withImport.LastSuccessfulImportUtc);
            Assert.Equal(1, withImport.LastSuccessfulImportFiles);
            Assert.NotEqual("Missing", StatusOf(withImport, "Last successful import"));
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task Recorded_receipts_survive_and_are_read_back_by_a_fresh_repository()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            var backupHash = new string('a', 64);
            var drillHash = new string('b', 64);
            await database.ExecuteAsync($"EXEC dbo.record_verified_operation 'Backup','{backupHash}';");
            await database.ExecuteAsync($"EXEC dbo.record_verified_operation 'RestoreDrill','{drillHash}';");

            // Durability: a repository constructed after the writes, on its own
            // connection, reads them back. The evidence lives in the database, not in
            // the object that recorded it.
            var reloaded = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.NotNull(reloaded.LastSuccessfulBackupUtc);
            Assert.NotNull(reloaded.LastSuccessfulRecoveryDrillUtc);
            Assert.Equal(backupHash, reloaded.LastSuccessfulBackupSha256, ignoreCase: true);
            Assert.Equal(drillHash, reloaded.LastSuccessfulRecoveryDrillSha256, ignoreCase: true);

            var lines = DatabaseRecoveryPresentation.Lines(reloaded, Thresholds, DateTime.UtcNow);
            Assert.NotEqual("Missing", lines.Single(x => x.Item == "Last verified backup").Status);
            // record_verified_operation stores the hash uppercased; the block shows what
            // was actually recorded rather than reformatting it.
            Assert.Contains("Fingerprint aaaaaaaaaaaaaaaa",
                lines.Single(x => x.Item == "Last verified backup").Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Fingerprint bbbbbbbbbbbbbbbb",
                lines.Single(x => x.Item == "Last verified recovery drill").Detail, StringComparison.OrdinalIgnoreCase);
        }
        finally { await database.DisposeAsync(); }
    }

    [Fact]
    public async Task A_store_manager_cannot_record_evidence_that_would_make_the_block_look_healthy()
    {
        var database = new SqlDatabaseFixture();
        try
        {
            await database.InitializeAsync();
            await database.ExecuteAsync("""
                CREATE USER recovery_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER recovery_manager;
                CREATE USER recovery_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER recovery_viewer;
                """);
            foreach (var principal in new[] { "recovery_manager", "recovery_viewer" })
            {
                var denied = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                    $"EXECUTE AS USER='{principal}'; EXEC dbo.record_verified_operation 'Backup','{new string('c', 64)}'; REVERT;"));
                Assert.Equal(229, denied.Number);
            }

            // Nothing was recorded, so the block still reports Missing.
            var health = await new DatabaseOperationalHealthRepository(database.ConnectionString).LoadAsync();
            Assert.Null(health.LastSuccessfulBackupUtc);
            Assert.Equal("Missing", StatusOf(health, "Last verified backup"));
        }
        finally { await database.DisposeAsync(); }
    }
}
