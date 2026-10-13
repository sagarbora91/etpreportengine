extern alias EtpApplication;

using System.Data;
using System.Windows.Threading;
using Etp.Reporting.Desktop.Modules.Settings;
using Etp.Reporting.Desktop.Updates;
using Etp.Reporting.Infrastructure.SqlServer.Connections;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;
using EtpApplication::Etp.Reporting.Application.Updates;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Composition;

// Updates (1.12 wave 2, lane B2): the Owner-only "Update available" notice, the confirm sheet, the result screen after an
// update attempt, and Settings > Software update. Everything here is built from the window and the connection factory;
// the engine itself (Desktop\Updates) never sees a connection string or SqlClient.
public sealed partial class DesktopCompositionRoot
{
    public const string UpdateSettingsTaskId = "update-settings";

    private MainWindow? updateWindow;

    partial void RegisterUpdates(ScreenRegistry registry)
    {
        var connections = currentConnections;
        var coordinator = new UpdateShellCoordinator(() => CreateUpdateServices(connections));
        registry.Register(UpdateSettingsTaskId, request =>
            new UpdateSettingsView(() => new SqlServerUpdateSettingsStore(connections), coordinator, () => request.Access.CanAdminister));
        UpdateStartupHook.WhenLoaded(registry, window => AttachUpdates(window, coordinator));
    }

    /// <summary>
    /// Starts the update flow once the shell is ready (the welcome overlay gone, the role known). Checks every second for up
    /// to three minutes, then gives up quietly: the Owner can still use Settings &gt; Software update.
    /// </summary>
    private void AttachUpdates(MainWindow window, UpdateShellCoordinator coordinator)
    {
        updateWindow = window;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var ticks = 0;
        timer.Tick += async (_, _) =>
        {
            ticks++;
            var ready = window.IsReadyForUpdates;
            if (!ready && ticks < 180) return;
            timer.Stop();
            if (!ready) return;
            try { await coordinator.StartUpAsync(); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                DesktopDiagnostics.Record(exception, "Updates.Startup", "UPDATE_START_FAILED", DesktopDiagnosticSeverity.Warning);
            }
        };
        timer.Start();
    }

    private UpdateShellServices CreateUpdateServices(Func<IEtpConnectionFactory> connections)
    {
        var window = updateWindow ?? throw new InvalidOperationException("The update screens are not ready until the main window has loaded.");
        var dispatcher = window.Dispatcher;
        T OnUi<T>(Func<T> read) => dispatcher.CheckAccess() ? read() : dispatcher.Invoke(read);
        var store = new SqlServerUpdateSettingsStore(connections);

        var sources = new UpdateGateSources(
            IsOwner: () => window.CurrentShellAccess.CanAdminister,
            ImportRunningInThisWindow: () => window.importWorkspaceView.IsBusy,
            ImportOpenInDatabase: token => NightlyHealthLease.ImportIsOpenAsync(connections(), token),
            AutomationLeaseHeld: async token =>
            {
                var lease = await NightlyHealthLease.TryAcquireAsync(connections(), token).ConfigureAwait(false);
                if (lease is null) return true;
                await lease.DisposeAsync().ConfigureAwait(false);
                return false;
            },
            OperationsRunnerBusy: () => window.operationsWorkspaceView.IsBusy,
            ScheduledBackupRunning: () => UpdateScheduledTasks.IsRunning(UpdateScheduledTasks.DailyBackup),
            ScheduledDrillRunning: () => UpdateScheduledTasks.IsRunning(UpdateScheduledTasks.MonthlyRecoveryDrill),
            EntryDraftOpen: window.HasUnsavedWorkForUpdate,
            OtherEtpSessionOpen: UpdateEngine.OtherEtpProcessRunning,
            KeysConfigured: () => ReleaseSigningKeys.AreConfigured,
            UpdateCheckEnabled: async token => (await store.LoadAsync(token).ConfigureAwait(false)).CheckEnabled);

        return new UpdateShellServices(
            Settings: store,
            Running: UpdatePaths.RunningVersion,
            IsOwner: () => OnUi(() => window.CurrentShellAccess.CanAdminister),
            Check: (running, feed) => UpdateEngine.Check(running, feed),
            Gate: new UpdateGateProbe(sources, read => OnUi(read)),
            Install: (running, feed, offer, gateNow, token) =>
                new UpdateEngine().InstallAsync(running, feed, offer, gateNow, MaintenanceTarget.For(connections()), token),
            ResolvePending: running => new UpdateAttemptStore(UpdatePaths.UpdatesRoot)
                .ResolvePending(running, UpdateAttemptStore.SetupIncompleteMarkerPresent(), DateTimeOffset.UtcNow),
            ShowBanner: model =>
            {
                if (model is null) window.ShellBanners.Remove(UpdateAvailableBanner.BannerKey);
                else window.ShellBanners.Show(new UpdateAvailableBanner(model));
            },
            Confirm: request => Task.FromResult(UpdateConfirmSheet.Show(window, request)),
            ShowResult: text => UpdateResultDialog.Show(window, text),
            SetStatus: text => window.ApplicationStatus.Text = text,
            CloseApplication: () => window.Close(),
            NowUtc: () => DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// The two update settings of migration 0065 over SQL. Reads are guarded so an older database (no 0065) reads as the
/// defaults. The write is one transaction (the update, then the audit row), and SQL itself refuses anyone but an Owner
/// (product_settings has no INSERT/UPDATE grant for any other role), whatever the screen shows.
/// </summary>
internal sealed class SqlServerUpdateSettingsStore(Func<IEtpConnectionFactory> connections) : IUpdateSettingsStore
{
    internal const string LoadSql = """
        IF COL_LENGTH(N'dbo.product_settings',N'update_check_enabled') IS NULL
            SELECT CAST(NULL AS nvarchar(400)) AS update_feed_path,CAST(0 AS bit) AS update_check_enabled;
        ELSE
            EXEC sys.sp_executesql N'SELECT update_feed_path,update_check_enabled FROM dbo.product_settings WHERE product_setting_id=1;';
        """;

    internal const string UpdateSql = """
        UPDATE dbo.product_settings
        SET update_feed_path=@path,update_check_enabled=@enabled,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason
        WHERE product_setting_id=1;
        """;

    internal const string AuditSql = "EXEC dbo.record_operational_audit 'ConfigurationChange','Succeeded',N'Software update settings changed',N'database';";

    public async Task<UpdateSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections().OpenAsync(EtpConnectionPurpose.Default, cancellationToken).ConfigureAwait(false);
        await using var command = new SqlCommand(LoadSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new UpdateSettings(null, false);
        return new UpdateSettings(reader.IsDBNull(0) ? null : reader.GetString(0), !reader.IsDBNull(1) && reader.GetBoolean(1));
    }

    public async Task SaveAsync(UpdateSettings settings, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (UpdateSettingsRules.Validate(settings.FeedPath, reason) is { } problem) throw new ArgumentException(problem, nameof(reason));
        await using var connection = await connections().OpenAsync(EtpConnectionPurpose.Default, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var update = new SqlCommand(UpdateSql, connection, transaction))
        {
            update.Parameters.Add(new SqlParameter("@path", SqlDbType.NVarChar, UpdateSettingsRules.MaximumPathLength) { Value = (object?)UpdateSettingsRules.NormalizePath(settings.FeedPath) ?? DBNull.Value });
            update.Parameters.Add(new SqlParameter("@enabled", SqlDbType.Bit) { Value = settings.CheckEnabled });
            update.Parameters.Add(new SqlParameter("@reason", SqlDbType.NVarChar, UpdateSettingsRules.MaximumReasonLength) { Value = reason.Trim() });
            if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The product settings are not set up on this database.");
        }
        await using (var audit = new SqlCommand(AuditSql, connection, transaction))
            await audit.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
