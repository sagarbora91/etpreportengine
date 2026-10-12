using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Updates;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 lane B1, migration 0065. Cross-layer: the update settings as SQL stores them are what the C# engine reads.
/// - A fresh database has no feed path (C# then resolves OneDrive\ETP-Updates) and update checks OFF, which the C# gate
///   turns into "turned off" (decision 44 #1: off until a real Workpc upgrade has worked).
/// - A blank path is refused by SQL; a stored full path is used as it is.
/// - Only the Owner can change the settings: a Store Manager or Viewer is refused by SQL whatever the app shows.
/// Impersonation runs on non-pooled connections and REVERTs in the same batch.
/// </summary>
public sealed class UpdateSettingsSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Read = "SELECT update_feed_path,update_check_enabled FROM dbo.product_settings WHERE product_setting_id=1;";

    [Fact]
    public async Task The_settings_start_off_with_no_path_and_the_engine_reads_them_that_way()
    {
        var (path, enabled) = await ReadAsync();
        Assert.Null(path);
        Assert.False(enabled);

        Assert.Equal(@"C:\Users\Owner\OneDrive\ETP-Updates", UpdatePaths.ResolveFeed(path, () => @"C:\Users\Owner\OneDrive"));
        var gate = UpdateGate.Evaluate(new UpdateGateState(IsOwner: true, KeysConfigured: true, UpdateCheckEnabled: enabled,
            ImportRunning: false, HealthRunRunning: false, BackupRunning: false, RecoveryDrillRunning: false, EntryDraftOpen: false, OtherEtpSessionOpen: false));
        Assert.Equal(new[] { UpdateGateBlock.TurnedOff }, gate.Blocks);
    }

    [Fact]
    public async Task A_blank_path_is_refused_and_a_full_path_is_used_as_stored()
    {
        try
        {
            var blank = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(
                "UPDATE dbo.product_settings SET update_feed_path=N'   ',modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=N'Synthetic blank path' WHERE product_setting_id=1;"));
            Assert.Equal(547, blank.Number);

            await database.ExecuteAsync(
                "UPDATE dbo.product_settings SET update_feed_path=N'D:\\Shared\\ETP-Updates',update_check_enabled=1,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=N'Synthetic feed path' WHERE product_setting_id=1;");
            var (path, enabled) = await ReadAsync();
            Assert.True(enabled);
            Assert.Equal(@"D:\Shared\ETP-Updates", UpdatePaths.ResolveFeed(path, () => @"C:\Users\Owner\OneDrive"));
        }
        finally
        {
            await database.ExecuteAsync(
                "UPDATE dbo.product_settings SET update_feed_path=NULL,update_check_enabled=0,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=N'Synthetic reset' WHERE product_setting_id=1;");
        }
    }

    [Fact]
    public async Task Only_the_owner_can_turn_update_checks_on()
    {
        await database.ExecuteAsync("""
            IF USER_ID(N'b1_viewer') IS NULL BEGIN CREATE USER b1_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER b1_viewer; END;
            IF USER_ID(N'b1_manager') IS NULL BEGIN CREATE USER b1_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER b1_manager; END;
            """);
        const string TurnOn = "UPDATE dbo.product_settings SET update_check_enabled=1,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=N'Synthetic' WHERE product_setting_id=1;";
        foreach (var user in new[] { "b1_viewer", "b1_manager" })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsAsync(user, TurnOn));
            Assert.Equal(229, refused.Number); // permission denied
        }
        Assert.False((await ReadAsync()).Enabled);
    }

    private async Task<(string? Path, bool Enabled)> ReadAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(Read, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetBoolean(1));
    }

    private async Task ExecuteAsAsync(string user, string sql)
    {
        await using var connection = new SqlConnection(TestSqlConnections.ForDatabase(database.Name, pooling: false));
        await connection.OpenAsync();
        await using var command = new SqlCommand($"EXECUTE AS USER='{user}'; BEGIN TRY {sql} END TRY BEGIN CATCH REVERT; THROW; END CATCH; REVERT;", connection);
        await command.ExecuteNonQueryAsync();
    }
}
