using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Application.DatabaseLifecycle;
using Etp.Reporting.Application.OperationsAdministration;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop.Tests;

public sealed class SettingsWorkspaceViewTests
{
    private const string ConnectionString =
        @"Server=.\SQLEXPRESS;Database=EtpReporting;Integrated Security=True;TrustServerCertificate=True";

    [Fact]
    public void View_runs_connection_and_product_configuration_through_injected_boundaries()
    {
        RunSta(async () =>
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "EtpSettingsWorkspaceTests", Guid.NewGuid().ToString("N"));
            try
            {
                var session = new DesktopSettingsPresentationSession(
                    new DesktopSettingsStore(Path.Combine(testRoot, "settings")),
                    new DesktopConnectionState(ConnectionString));
                var lifecycle = new FakeLifecycleService();
                var administration = new FakeAdministrationService();
                var view = new SettingsWorkspaceView(session, _ => lifecycle, _ => administration,
                    Path.Combine(testRoot, "migrations"));
                view.UpdateAccess(new(true, true));
                DesktopConnectionPresentationState? published = null;
                var operations = new List<SettingsWorkspaceOperation>();
                view.ConnectionPresentationChanged += (_, e) => published = e.State;
                view.OperationCompletedAsync = (operation, _) =>
                {
                    operations.Add(operation);
                    return Task.CompletedTask;
                };

                view.Initialize();
                await view.CheckConnectionAsync(true);
                Assert.Equal("Healthy", view.StatusText);
                await view.PrepareForDisplayAsync(true);
                await view.SaveProductConfigurationAsync();

                Assert.Equal(1, lifecycle.HealthChecks);
                Assert.True(published?.IsConnected);
                Assert.Equal("Product integration settings saved and audited.", view.StatusText);
                Assert.Contains(SettingsWorkspaceOperation.ConnectionTest, operations);
                Assert.Contains(SettingsWorkspaceOperation.ProductConfigurationSaved, operations);
                Assert.True(view.ProductConfigurationEnabled);
                Assert.Equal("docs", session.ProductSettings?.DocumentRepositoryPath);
                Assert.NotNull(administration.Saved);
                Assert.Equal(20, administration.Saved!.MaximumAttachmentMb);
                Assert.Equal("Test Windows-integrated database connection",
                    AutomationProperties.GetName(FindButton(view, "Test Windows-integrated database connection")));
                Assert.Equal("Save product integrations",
                    AutomationProperties.GetName(FindButton(view, "Save product integrations")));
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
            }
        });
    }

    [Fact]
    public void Bootstrap_and_product_changes_preserve_owner_access_rules()
    {
        RunSta(async () =>
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "EtpSettingsWorkspaceAccessTests", Guid.NewGuid().ToString("N"));
            try
            {
                var lifecycle = new FakeLifecycleService();
                var view = new SettingsWorkspaceView(
                    new DesktopSettingsPresentationSession(
                        new DesktopSettingsStore(Path.Combine(testRoot, "settings")),
                        new DesktopConnectionState(ConnectionString)),
                    _ => lifecycle,
                    _ => new FakeAdministrationService(),
                    Path.Combine(testRoot, "migrations"));
                view.UpdateAccess(new(true, false));

                view.Initialize();
                await view.BootstrapDatabaseAsync();
                await view.SaveProductConfigurationAsync();

                Assert.Equal(0, lifecycle.Bootstraps);
                Assert.False(view.ProductConfigurationEnabled);
                Assert.Equal("Your Windows account does not have permission for this action.", view.StatusText);
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Older_health_check_cannot_replace_new_connection_or_its_status(bool failOlderCheck)
    {
        RunSta(async () =>
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "EtpSettingsOrderingTests", Guid.NewGuid().ToString("N"));
            try
            {
                var older = new TaskCompletionSource<DatabaseConnectionHealth>();
                var lifecycle = new FakeLifecycleService { HealthCompletion = older.Task };
                var session = new DesktopSettingsPresentationSession(
                    new DesktopSettingsStore(Path.Combine(testRoot, "settings")), new DesktopConnectionState(ConnectionString));
                var view = new SettingsWorkspaceView(session, _ => lifecycle,
                    _ => new FakeAdministrationService(), Path.Combine(testRoot, "migrations"));
                view.Initialize();
                var pending = view.CheckConnectionAsync(true);
                var newConnection = ConnectionString.Replace("EtpReporting", "EtpSyntheticNew");
                ((TextBox)view.FindName("ConnectionStringInput")).Text = newConnection;
                lifecycle.HealthCompletion = Task.FromResult(new DatabaseConnectionHealth(DatabaseConnectionStatus.Healthy, "New connection healthy", "16.0"));
                await view.CheckConnectionAsync(true);
                var acceptedConnection = session.ConnectionString;
                Assert.Contains("EtpSyntheticNew", acceptedConnection);
                if (failOlderCheck) older.SetException(new InvalidOperationException("Old connection failed"));
                else older.SetResult(new(DatabaseConnectionStatus.Healthy, "Old connection healthy", "16.0"));
                await pending;
                Assert.Equal(acceptedConnection, session.ConnectionString);
                Assert.Equal(acceptedConnection, view.ConnectionStringText);
                Assert.Equal("New connection healthy", view.StatusText);
            }
            finally
            {
                if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
            }
        });
    }

    [Fact]
    public void Integration_drafts_survive_refresh_and_failed_save_and_reject_reentry()
    {
        RunSta(async () =>
        {
            var service = new FakeAdministrationService();
            var root = Path.Combine(Path.GetTempPath(), "EtpSettingsDraftTests", Guid.NewGuid().ToString("N"));
            var view = new SettingsWorkspaceView(new DesktopSettingsPresentationSession(new DesktopSettingsStore(root), new DesktopConnectionState(ConnectionString)),
                _ => new FakeLifecycleService(), _ => service, root);
            view.UpdateAccess(new(true, true));
            await view.PrepareForDisplayAsync(true);
            var share = (TextBox)view.FindName("ShareFolderInput");
            share.Text = "new-share";
            await view.PrepareForDisplayAsync(true);
            Assert.Equal("new-share", share.Text); Assert.True(view.HasProductDraft);
            var pending = new TaskCompletionSource(); service.SaveCompletion = pending.Task;
            var save = view.SaveProductConfigurationAsync();
            Assert.True(view.IsBusy); Assert.False(view.ProductConfigurationEnabled);
            Assert.False(await view.SaveProductConfigurationAsync()); Assert.Equal(1, service.Saves);
            pending.SetException(new InvalidOperationException("Synthetic failure"));
            Assert.False(await save); Assert.True(view.HasProductDraft); Assert.Equal("new-share", share.Text);
            view.DiscardProductDraft(); Assert.False(view.HasProductDraft); Assert.Equal("share", share.Text);
        });
    }

    [Fact]
    public void Failed_integration_load_cannot_overwrite_unloaded_categories()
    {
        RunSta(async () =>
        {
            var service = new FakeAdministrationService { FailLoad = true };
            var root = Path.Combine(Path.GetTempPath(), "EtpSettingsLoadTests", Guid.NewGuid().ToString("N"));
            var view = new SettingsWorkspaceView(new DesktopSettingsPresentationSession(new DesktopSettingsStore(root), new DesktopConnectionState(ConnectionString)),
                _ => new FakeLifecycleService(), _ => service, root);
            view.UpdateAccess(new(true, true));
            await view.PrepareForDisplayAsync(true);
            Assert.False(view.ProductConfigurationEnabled);
            Assert.False(await view.SaveProductConfigurationAsync()); Assert.Equal(0, service.Saves);
            service.FailLoad = false; await view.PrepareForDisplayAsync(true);
            Assert.True(view.ProductConfigurationEnabled);
        });
    }

    [Fact]
    public void Committed_settings_stay_successful_when_followup_refresh_fails()
    {
        RunSta(async () =>
        {
            var service = new FakeAdministrationService(); var root = Path.Combine(Path.GetTempPath(), "EtpSavedSettings", Guid.NewGuid().ToString("N"));
            var view = new SettingsWorkspaceView(new(new DesktopSettingsStore(root), new DesktopConnectionState(ConnectionString)), _ => new FakeLifecycleService(), _ => service, root);
            view.UpdateAccess(new(true,true)); await view.PrepareForDisplayAsync(true);
            ((TextBox)view.FindName("ProductSettingsReasonInput")).Text = "Synthetic change";
            view.OperationCompletedAsync = (_,_) => Task.FromException(new InvalidOperationException("Synthetic refresh failure"));
            Assert.True(await view.SaveProductConfigurationAsync()); Assert.False(view.HasProductDraft); Assert.Equal(1,service.Saves);
            Assert.Contains("saved and audited",view.StatusText); Assert.Contains("follow-up display refresh failed",view.StatusText);
        });
    }

    [Fact]
    public void Connection_context_guard_blocks_health_and_bootstrap_before_touching_another_database()
    {
        RunSta(async () =>
        {
            var lifecycle = new FakeLifecycleService(); var root = Path.Combine(Path.GetTempPath(), "EtpConnectionGuard", Guid.NewGuid().ToString("N"));
            var session = new DesktopSettingsPresentationSession(new DesktopSettingsStore(root),new DesktopConnectionState(ConnectionString));
            var view = new SettingsWorkspaceView(session,_=>lifecycle,_=>new FakeAdministrationService(),root) { CanChangeDatabase = () => false };
            view.UpdateAccess(new(true,true)); view.Initialize(); var original = session.ConnectionString;
            ((TextBox)view.FindName("ConnectionStringInput")).Text = ConnectionString.Replace("EtpReporting","EtpOtherSynthetic");
            await view.CheckConnectionAsync(true); await view.BootstrapDatabaseAsync();
            Assert.Equal(original,session.ConnectionString); Assert.Equal(0,lifecycle.HealthChecks); Assert.Equal(0,lifecycle.Bootstraps);
            Assert.Contains("active connection is unchanged",view.StatusText);
        });
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static Button FindButton(DependencyObject root, string automationName)
    {
        if (root is Button button && AutomationProperties.GetName(button) == automationName) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            try { return FindButton(child, automationName); }
            catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException($"Button '{automationName}' was not found.");
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA test failed.", failure);
    }

    [Fact]
    public void Loaded_integration_configuration_cannot_be_retargeted_by_connection_or_bootstrap()
    {
        RunSta(async () =>
        {
            var testRoot = Path.Combine(Path.GetTempPath(), "EtpSettingsRetargetTests", Guid.NewGuid().ToString("N"));
            try
            {
                var session = new DesktopSettingsPresentationSession(new DesktopSettingsStore(Path.Combine(testRoot, "settings")), new DesktopConnectionState(ConnectionString));
                var lifecycle = new FakeLifecycleService();
                var view = new SettingsWorkspaceView(session, _ => lifecycle, _ => new FakeAdministrationService(), Path.Combine(testRoot, "migrations")) { CanChangeDatabase = () => true };
                view.UpdateAccess(new(true, true)); view.Initialize();
                await view.CheckConnectionAsync(false);
                var original = session.ConnectionString;
                await view.PrepareForDisplayAsync(true);
                Assert.False(view.HasProductDraft);
                ((TextBox)view.FindName("ConnectionStringInput")).Text = ConnectionString.Replace("Database=EtpReporting", "Database=OtherSyntheticDatabase");
                await view.CheckConnectionAsync(true);
                await view.BootstrapDatabaseAsync();
                Assert.Equal(1, lifecycle.HealthChecks); Assert.Equal(0, lifecycle.Bootstraps);
                Assert.Equal(original, session.ConnectionString);
                Assert.Contains("active connection is unchanged", view.StatusText);
                ((TextBox)view.FindName("ConnectionStringInput")).Text = original;
                await view.CheckConnectionAsync(false);
                Assert.Equal(2, lifecycle.HealthChecks);
            }
            finally { if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true); }
        });
    }

    private sealed class FakeLifecycleService : IDatabaseLifecycleService
    {
        public Task<DatabaseConnectionHealth>? HealthCompletion { get; set; }
        public int HealthChecks { get; private set; }
        public int Bootstraps { get; private set; }

        public Task<DatabaseConnectionHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            HealthChecks++;
            return HealthCompletion ?? Task.FromResult(new DatabaseConnectionHealth(DatabaseConnectionStatus.Healthy, "Healthy", "16.0"));
        }

        public Task<DatabaseBootstrapOutcome> BootstrapAsync(BootstrapDatabase command, CancellationToken cancellationToken = default)
        {
            Bootstraps++;
            return Task.FromResult(new DatabaseBootstrapOutcome(false, ["001"]));
        }

        public Task RecordAuditAsync(RecordOperationalAudit command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAdministrationService : IAdministrationService
    {
        public SaveProductConfiguration? Saved { get; private set; }
        public bool FailLoad { get; set; }
        public Task? SaveCompletion { get; set; }
        public int Saves { get; private set; }

        public Task<AdministrationDashboard> LoadAsync(string masterType, CancellationToken cancellationToken = default) =>
            FailLoad ? Task.FromException<AdministrationDashboard>(new InvalidOperationException("Synthetic load failure")) : Task.FromResult(new AdministrationDashboard([], [], [], [],
                new ProductConfiguration("docs", "share", "smtp", 587, true,
                    "from@example.com", 20, DateTime.UtcNow, "owner")));

        public Task SaveMasterAsync(SaveControlledMaster command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveUserAsync(SaveApplicationUser command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveProductConfigurationAsync(SaveProductConfiguration command, CancellationToken cancellationToken = default)
        {
            Saved = command;
            Saves++;
            return SaveCompletion ?? Task.CompletedTask;
        }
    }
}
