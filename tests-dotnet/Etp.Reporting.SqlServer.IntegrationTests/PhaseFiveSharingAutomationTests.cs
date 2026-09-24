using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.SqlServer.IntegrationTests;

public sealed class PhaseFiveSharingAutomationTests(SqlDatabaseFixture database) : IClassFixture<SqlDatabaseFixture>
{
    [Fact]
    public async Task Share_history_is_per_generation_shows_final_outcome_and_preserves_append_only_transitions()
    {
        var generation = Convert.ToInt64(await database.ExecuteAsync("""
            INSERT dbo.daily_report_generations(store_code,business_date,generation_number,content_sha256,control_json,generated_by)
            VALUES('SHARING_TEST','20260924',1,REPLICATE('a',64),N'{}',SUSER_SNAME()); SELECT SCOPE_IDENTITY();
            """));
        var repository = new ProductisationRepository(database.ConnectionString);
        var key = Guid.NewGuid();
        await repository.RecordShareAttemptAsync(generation, null, "EMAIL", "Configured recipient", "test.pdf", "INITIATED", "Submission started", attemptKey: key);
        await repository.RecordShareAttemptAsync(generation, null, "EMAIL", "Configured recipient", "test.pdf", "SMTP_ACCEPTED", "Accepted by SMTP; delivery unconfirmed", attemptKey: key);
        var history = await repository.LoadShareHistoryAsync(generation);
        var entry = Assert.Single(history); Assert.Equal("SMTP_ACCEPTED", entry.Outcome); Assert.Equal(key, entry.AttemptKey);
        Assert.Equal(2, Convert.ToInt32(await database.ExecuteAsync($"SELECT COUNT(*) FROM dbo.share_attempts WHERE daily_report_generation_id={generation};")));
        Assert.Empty(await repository.LoadShareHistoryAsync(generation + 999));
        await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"UPDATE dbo.share_attempts SET outcome='SUCCEEDED' WHERE daily_report_generation_id={generation};"));
        await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync($"DELETE dbo.share_attempts WHERE daily_report_generation_id={generation};"));
        await Assert.ThrowsAsync<SqlException>(() => repository.RecordShareAttemptAsync(generation, null, "EMAIL", null, "test.pdf", "DELIVERED", "Unsupported delivery claim"));
        await database.ExecuteAsync($"""
            CREATE USER sharing_viewer WITHOUT LOGIN; ALTER ROLE etp_viewer ADD MEMBER sharing_viewer;
            EXECUTE AS USER='sharing_viewer';
            INSERT dbo.share_attempts(daily_report_generation_id,channel,attachment_file_name,outcome,safe_message,initiated_by)
            VALUES({generation},'WHATSAPP','test.pdf','HANDOFF_READY',N'Manual handoff; not delivered',SUSER_SNAME()); REVERT;
            """);
        Assert.Equal(2, (await repository.LoadShareHistoryAsync(generation)).Count);
    }

    [Fact]
    public async Task Automatic_import_lease_releases_physical_session_before_connection_is_returned_to_pool()
    {
        await using var first = await AutomationSessionLease.TryAcquireAsync(database.ConnectionString, CancellationToken.None);
        Assert.NotNull(first);
        // A different physical session cannot acquire while the first run is active.
        var competingString = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = database.ConnectionString, ["Application Name"] = "lease competitor" }.ConnectionString;
        Assert.Null(await AutomationSessionLease.TryAcquireAsync(competingString, CancellationToken.None));
        await first.DisposeAsync();
        // Check another pool first; an unreleased lock in the original pool would block it.
        var second = await AutomationSessionLease.TryAcquireAsync(competingString, CancellationToken.None);
        Assert.NotNull(second); await second.DisposeAsync();
        var third = await AutomationSessionLease.TryAcquireAsync(database.ConnectionString, CancellationToken.None);
        Assert.NotNull(third); await third.DisposeAsync(); await third.DisposeAsync();
    }

    [Fact]
    public async Task Watch_settings_have_no_fake_poll_cadence_and_can_still_be_saved()
    {
        Assert.Equal(DBNull.Value, await database.ExecuteAsync("SELECT COL_LENGTH('dbo.watch_folder_settings','poll_minutes');"));
        var repository = new Phase2OperationsRepository(database.ConnectionString);
        var before = await repository.LoadWatchFolderSettingsAsync();
        var root = Path.Combine(Path.GetTempPath(), "EtpWatchSynthetic-" + Guid.NewGuid().ToString("N"));
        var settings = new WatchFolderSettings(Path.Combine(root,"In"),Path.Combine(root,"Done"),Path.Combine(root,"Failed"),Path.Combine(root,"Reports"),false,DateTime.MinValue,"test");
        await repository.SaveWatchFolderSettingsAsync(settings, "Synthetic automatic import settings test");
        var after = await repository.LoadWatchFolderSettingsAsync();
        Assert.Equal(settings.InboundPath, after.InboundPath); Assert.False(after.IsEnabled);
        await repository.SaveWatchFolderSettingsAsync(before, "Restore disposable fixture configuration");
    }
}
