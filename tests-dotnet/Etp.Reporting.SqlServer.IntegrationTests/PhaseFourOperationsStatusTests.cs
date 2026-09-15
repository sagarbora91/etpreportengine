using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFourOperationsStatusTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Only_dedicated_automation_and_owner_can_publish_verified_health_while_staff_can_read_safe_status()
    {
        await ExecuteAsync("""
            CREATE USER status_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER status_manager;
            CREATE USER status_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER status_viewer;
            CREATE USER status_automation WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER status_automation;
            ALTER ROLE etp_automation ADD MEMBER status_automation;
            CREATE USER status_owner WITHOUT LOGIN; ALTER ROLE etp_owner ADD MEMBER status_owner;
            """);

        var repository = new DatabaseOperationalHealthRepository(database.ConnectionString);
        foreach (var principal in new[] { "status_manager", "status_viewer" })
        {
            await ExecuteAsync($"EXECUTE AS USER='{principal}'; EXEC dbo.record_operational_audit 'Backup','Succeeded',N'Forged backup claim',N'test';");
            await ExecuteAsync($"EXECUTE AS USER='{principal}'; EXEC dbo.record_operational_audit 'RestoreDrill','Succeeded',N'Forged drill claim',N'test';");
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='{principal}'; EXEC dbo.record_verified_operation 'Backup','{new string('A', 64)}';"));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='{principal}'; INSERT dbo.verified_operation_receipts(operation_type,backup_sha256,recorded_by) VALUES('Backup','{new string('A', 64)}',N'forged');"));
            await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='{principal}'; SELECT recorded_by FROM dbo.verified_operation_receipts;"));
            var status = await ReadStatusAsync(principal);
            Assert.Null(status.BackupUtc);
            Assert.Null(status.BackupHash);
            Assert.Null(status.DrillUtc);
            Assert.Null(status.DrillHash);
        }
        var missing = await repository.LoadAsync();
        Assert.Null(missing.LastSuccessfulBackupUtc);
        Assert.Null(missing.LastSuccessfulRecoveryDrillUtc);
        Assert.Contains(missing.Warnings, warning => warning.Code == "BACKUP_MISSING");
        Assert.Contains(missing.Warnings, warning => warning.Code == "RECOVERY_DRILL_MISSING");

        // Even an explicitly granted EXECUTE permission cannot bypass the role check.
        await ExecuteAsync("GRANT EXECUTE ON dbo.record_verified_operation TO status_manager;");
        var forbidden = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync($"EXECUTE AS USER='status_manager'; EXEC dbo.record_verified_operation 'Backup','{new string('A', 64)}';"));
        Assert.Equal(51341, forbidden.Number);

        foreach (var operation in new[] { "Restore", "Backup      ", "backup", "Backup';--" })
        {
            var invalid = await Assert.ThrowsAsync<SqlException>(() => RecordAsync("status_automation", operation, new string('A', 64)));
            Assert.Equal(51342, invalid.Number);
        }
        foreach (var hash in new[] { "", new string('A', 63), new string('A', 65), new string('Z', 64), new string('A', 63) + " " })
        {
            var invalid = await Assert.ThrowsAsync<SqlException>(() => RecordAsync("status_automation", "Backup", hash));
            Assert.Equal(51342, invalid.Number);
        }

        var before = DateTime.UtcNow.AddSeconds(-1);
        await RecordAsync("status_automation", "Backup", new string('a', 64));
        await RecordAsync("status_automation", "RestoreDrill", new string('a', 64));
        // A newer backup does not overwrite which exact backup the successful drill used.
        await RecordAsync("status_owner", "Backup", new string('b', 64));
        var after = DateTime.UtcNow.AddSeconds(1);
        var health = await repository.LoadAsync();
        Assert.InRange(health.LastSuccessfulBackupUtc!.Value, before, after);
        Assert.InRange(health.LastSuccessfulRecoveryDrillUtc!.Value, before, after);
        Assert.Equal(DateTimeKind.Utc, health.LastSuccessfulBackupUtc.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, health.LastSuccessfulRecoveryDrillUtc.Value.Kind);
        Assert.Equal(new string('B', 64), health.LastSuccessfulBackupSha256);
        Assert.Equal(new string('A', 64), health.LastSuccessfulRecoveryDrillSha256);
        Assert.DoesNotContain(health.Warnings, warning => warning.Code.StartsWith("RECOVERY_DRILL", StringComparison.Ordinal));
        foreach (var principal in new[] { "status_manager", "status_viewer" })
        {
            var status = await ReadStatusAsync(principal);
            Assert.Equal(new string('B', 64), status.BackupHash);
            Assert.Equal(new string('A', 64), status.DrillHash);
        }
        Assert.Equal(await ExecuteAsync("EXECUTE AS USER='status_automation'; SELECT SUSER_SNAME();"),
            await ExecuteAsync("SELECT recorded_by FROM dbo.verified_operation_receipts WHERE operation_type='RestoreDrill';"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("EXECUTE AS USER='status_automation'; UPDATE dbo.verified_operation_receipts SET completed_utc='20990101';"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("DELETE dbo.verified_operation_receipts;"));

        await ExecuteAsync("ALTER ROLE etp_automation DROP MEMBER status_automation;");
        await Assert.ThrowsAsync<SqlException>(() => RecordAsync("status_automation", "Backup", new string('C', 64)));
        Assert.Equal(3, Convert.ToInt32(await ExecuteAsync("SELECT COUNT(*) FROM dbo.verified_operation_receipts;")));
    }

    private async Task RecordAsync(string principal, string operation, string hash)
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand($"EXECUTE AS USER='{principal}'; EXEC dbo.record_verified_operation @operation,@hash;", connection);
        command.Parameters.AddWithValue("@operation", operation);
        command.Parameters.AddWithValue("@hash", hash);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(DateTime? BackupUtc, string? BackupHash, DateTime? DrillUtc, string? DrillHash)> ReadStatusAsync(string principal)
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand($"EXECUTE AS USER='{principal}'; EXEC dbo.load_database_operational_health;", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(7, reader.FieldCount);
        Assert.True(reader.GetDecimal(0) > 0);
        return (reader.IsDBNull(2) ? null : reader.GetDateTime(2), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5), reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private async Task<object?> ExecuteAsync(string sql)
    {
        await using var connection = NewConnection();
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private SqlConnection NewConnection() => new(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
}
