using Etp.Reporting.Application.Updates;
using Etp.Reporting.Desktop.Composition;
using Etp.Reporting.Desktop.Updates;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

/// <summary>
/// 1.12 lane B1, migration 0065. Cross-layer: the update settings as SQL stores them are what the C# engine reads.
/// - A fresh database has no feed path (C# then resolves OneDrive\ETP-Updates) and update checks OFF, which the C# gate
///   turns into "turned off" (decision 44 #1: off until a real Workpc upgrade has worked).
/// - A blank path is refused by SQL; a stored full path is used as it is.
/// - Only the Owner can change the settings: a Store Manager or Viewer is refused by SQL whatever the app shows.
/// Impersonation runs on non-pooled connections and REVERTs in the same batch.
/// 1.12 wave 2 (B2) adds the Settings screen's own store (SqlServerUpdateSettingsStore): an Owner writes the folder and the
/// switch with modified_by, modified_utc, the reason and an audit row, and reads them back; a Store Manager and a Viewer
/// running the very same UPDATE text get 229; blank, relative and over-long input never reaches SQL; the read is guarded
/// for a database that has not run 0065.
/// </summary>
public sealed class UpdateSettingsSqlTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    private const string Read = "SELECT update_feed_path,update_check_enabled FROM dbo.product_settings WHERE product_setting_id=1;";

    [SqlFact]
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

    [SqlFact]
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

    [SqlFact]
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

    private SqlServerUpdateSettingsStore Store() => new(() => new EtpConnectionFactory(database.ConnectionString));

    [SqlFact]
    public async Task The_settings_screens_store_writes_as_the_owner_with_reason_and_audit_and_reads_it_back()
    {
        var before = Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='ConfigurationChange';"));
        try
        {
            var store = Store();
            Assert.Equal(new UpdateSettings(null, false), await store.LoadAsync());

            await store.SaveAsync(new UpdateSettings(@"  D:\Shared\ETP-Updates  ", true), "  Synthetic B2 write  ");
            Assert.Equal(new UpdateSettings(@"D:\Shared\ETP-Updates", true), await store.LoadAsync());

            // What the C# store wrote is what the engine reads (the same cross-layer check as the first tests).
            var (path, enabled) = await ReadAsync();
            Assert.Equal(@"D:\Shared\ETP-Updates", UpdatePaths.ResolveFeed(path, () => @"C:\Users\Owner\OneDrive"));
            Assert.True(enabled);
            Assert.Equal("Synthetic B2 write", Convert.ToString(await database.ExecuteAsync("SELECT change_reason FROM dbo.product_settings WHERE product_setting_id=1;")));
            Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(await database.ExecuteAsync("SELECT modified_by FROM dbo.product_settings WHERE product_setting_id=1;"))));
            Assert.Equal(before + 1, Convert.ToInt32(await database.ExecuteAsync("SELECT COUNT(*) FROM dbo.operational_audit WHERE event_type='ConfigurationChange';")));

            // Blank means "use OneDrive": stored as NULL, and the CHECK is never asked to refuse a blank.
            await store.SaveAsync(new UpdateSettings("   ", false), "Synthetic B2 reset");
            Assert.Equal(new UpdateSettings(null, false), await store.LoadAsync());
        }
        finally
        {
            await database.ExecuteAsync(
                "UPDATE dbo.product_settings SET update_feed_path=NULL,update_check_enabled=0,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=N'Synthetic reset' WHERE product_setting_id=1;");
        }
    }

    [SqlFact]
    public async Task Input_the_screen_would_refuse_never_reaches_SQL()
    {
        var store = Store();
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new UpdateSettings(null, true), "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new UpdateSettings("ETP-Updates", true), "Synthetic"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new UpdateSettings(@"D:\" + new string('a', 400), true), "Synthetic"));
        var (path, enabled) = await ReadAsync();
        Assert.Null(path);
        Assert.False(enabled);
    }

    [SqlFact]
    public async Task A_store_manager_and_a_viewer_running_the_screens_UPDATE_get_229_and_nothing_changes()
    {
        await database.ExecuteAsync("""
            IF USER_ID(N'b2_viewer') IS NULL BEGIN CREATE USER b2_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER b2_viewer; END;
            IF USER_ID(N'b2_manager') IS NULL BEGIN CREATE USER b2_manager WITHOUT LOGIN; ALTER ROLE etp_store_manager ADD MEMBER b2_manager; END;
            """);
        var statement = @"DECLARE @path nvarchar(400)=N'D:\Shared',@enabled bit=1,@reason nvarchar(500)=N'Synthetic B2 refused';" + SqlServerUpdateSettingsStore.UpdateSql;
        foreach (var user in new[] { "b2_viewer", "b2_manager" })
        {
            var refused = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsAsync(user, statement));
            Assert.Equal(229, refused.Number);
        }
        var (path, enabled) = await ReadAsync();
        Assert.Null(path);
        Assert.False(enabled);
    }

    [SqlFact]
    public async Task The_read_is_guarded_for_an_older_database_and_a_fresh_one_reads_as_the_defaults()
    {
        // The read statement checks COL_LENGTH first (a nested ELSE branch, never AND), so a database that has not run 0065
        // does not fail the screen; the real column read happens in dynamic SQL that only compiles when the column exists.
        Assert.Contains("COL_LENGTH(N'dbo.product_settings',N'update_check_enabled') IS NULL", SqlServerUpdateSettingsStore.LoadSql, StringComparison.Ordinal);
        Assert.Contains("sp_executesql", SqlServerUpdateSettingsStore.LoadSql, StringComparison.Ordinal);
        Assert.Equal(new UpdateSettings(null, false), await Store().LoadAsync());
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
