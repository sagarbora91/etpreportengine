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
    public void MainWindow_hosts_settings_without_owning_settings_controls_or_handlers()
    {
        var root = FindRepositoryRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml"));
        var main = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.xaml.cs"));
        var productisationPath = Path.Combine(root, "src", "Etp.Reporting.Desktop", "MainWindow.Productisation.cs");
        Assert.False(File.Exists(productisationPath));
        const string productisation = "";
        var composition = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Composition", "DesktopCompositionRoot.cs"));
        var viewXaml = File.ReadAllText(Path.Combine(root, "src", "Etp.Reporting.Desktop", "Modules", "Settings", "SettingsWorkspaceView.xaml"));

        Assert.Contains("<ContentControl x:Name=\"SettingsPanel\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"ConnectionStringInput\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TestConnection_Click", main, StringComparison.Ordinal);
        Assert.DoesNotContain("BootstrapDatabase_Click", main, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveProductSettings_Click", productisation, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentRepositoryInput", main, StringComparison.Ordinal);
        Assert.DoesNotContain("new SettingsWorkspaceView", main, StringComparison.Ordinal);
        Assert.Contains("new SettingsWorkspaceView", composition, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConnectionStringInput\"", viewXaml, StringComparison.Ordinal);
        Assert.Contains("automation:AutomationProperties.Name=\"Settings workspace\"", viewXaml, StringComparison.Ordinal);
        Assert.True(Count(viewXaml, "automation:AutomationProperties.Name=") >= 13,
            "Every interactive Settings control and status needs an accessible name.");
    }

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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }

    private sealed class FakeLifecycleService : IDatabaseLifecycleService
    {
        public int HealthChecks { get; private set; }
        public int Bootstraps { get; private set; }

        public Task<DatabaseConnectionHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            HealthChecks++;
            return Task.FromResult(new DatabaseConnectionHealth(DatabaseConnectionStatus.Healthy, "Healthy", "16.0"));
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

        public Task<AdministrationDashboard> LoadAsync(string masterType, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdministrationDashboard([], [], [], [],
                new ProductConfiguration("docs", "share", "ocr", "models", "smtp", 587, true,
                    "from@example.com", 20, DateTime.UtcNow, "owner")));

        public Task SaveMasterAsync(SaveControlledMaster command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveUserAsync(SaveApplicationUser command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveProductConfigurationAsync(SaveProductConfiguration command, CancellationToken cancellationToken = default)
        {
            Saved = command;
            return Task.CompletedTask;
        }
    }
}
